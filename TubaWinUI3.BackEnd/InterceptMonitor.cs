using TubaWinUI3.BackEnd.Models;

namespace TubaWinUI3.BackEnd;

/// <summary>
/// 主动拦截轮询引擎（移植自 ContextMenuMgr 的 ContextMenuRegistryMonitor +
/// ContextMenuRegistryCatalog 分类/隔离逻辑）：
/// 1. 首次运行以当前机器状态建立基线白名单（不误杀存量项目）。
/// 2. 每轮先做"期望纠偏"：明确保持拦截的项被第三方改回启用 → 无审核自动重拦
///    （防通知风暴，杜绝"放行后被反复拦截"）。
/// 3. 运行期新增/重现项 → 拦截（隔离）并挂起审核 → 推送通知（管道 + 冷却文件双通道）。
/// 4. 启动期（监视器未运行期间）出现的新项 → 仅进入待审核，不拦截不通知。
/// 5. 信任策略（总是放行/总是拦截）为权威：对新增项直接生效，不再询问。
/// 6. 「停止追踪」列表内的条目完全跳过（不再拦截、不再提醒）。
/// </summary>
public sealed class InterceptMonitor
{
    private const int AllowedMissingPruneCycles = 12;

    private readonly BackendConfig _config;
    private readonly string _activeInterceptDir;
    private readonly StateStore _state;
    private readonly EventLog _events;
    private readonly BlockEngine _blockEngine;
    private readonly TrustPolicyStore _policies;
    private readonly IgnoreStore _ignore;
    private readonly NotifyStateStore _notifications;
    private readonly CancellationTokenSource _cts = new();
    private long _cycleCount;

    /// <summary>新条目被隔离（拦截 + 待审核）时触发：参数 = (条目, 变更类型, 提示消息)。</summary>
    public event Action<InterceptItemDto, string, string>? ItemQuarantined;

    /// <summary>广播推送（Program 接线到管道服务器）。</summary>
    public Action<InterceptBackendNotification>? Notify { get; set; }

    public InterceptMonitor(BackendConfig config)
    {
        _config = config;
        _activeInterceptDir = Path.Combine(config.DataDir, "active_intercept");
        _state = new StateStore(_activeInterceptDir);
        _events = new EventLog(_activeInterceptDir);
        _blockEngine = new BlockEngine(_activeInterceptDir);
        _policies = new TrustPolicyStore(_activeInterceptDir);
        _ignore = new IgnoreStore(_activeInterceptDir);
        _notifications = new NotifyStateStore(_activeInterceptDir);
    }

    /// <summary>供 Program 复用同一组存储（与管道处理器共享实例）。</summary>
    public StateStore State => _state;
    public EventLog Events => _events;
    public BlockEngine BlockEngine => _blockEngine;
    public TrustPolicyStore Policies => _policies;
    public IgnoreStore Ignore => _ignore;
    public NotifyStateStore Notifications => _notifications;

    /// <summary>启动轮询循环（阻塞直到取消）。</summary>
    public void Run()
    {
        BackEndLog.Info($"主动拦截后端启动：数据目录 {_config.DataDir}，轮询间隔 {_config.PollIntervalSeconds}s");

        EnsureBaselineIfFresh();

        bool isBaselineEstablishment = true;
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                RunCycleCore(isBaselineEstablishment);
            }
            catch (Exception ex)
            {
                BackEndLog.Error($"轮询循环异常：{ex}");
            }

            isBaselineEstablishment = false;
            _cycleCount++;
            if (_cycleCount % 60 == 0)
            {
                try
                {
                    _events.Compact(Math.Max(100, _config.MaxEventRows));
                }
                catch (Exception ex)
                {
                    BackEndLog.Warn($"事件日志压缩失败：{ex.Message}");
                }
            }

            try
            {
                _cts.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Max(1, _config.PollIntervalSeconds)));
            }
            catch
            {
                break;
            }
        }
    }

    /// <summary>单轮执行（--once 诊断模式；按启动期语义：只高亮不拦截）。</summary>
    public void RunOnce()
    {
        EnsureBaselineIfFresh();
        RunCycleCore(isBaselineEstablishment: true);
    }

    public void Stop() => _cts.Cancel();

    // ---------- 基线 ----------

    private void EnsureBaselineIfFresh()
    {
        if (_state.BaselineEstablished) return;

        BackEndLog.Info("首次运行：以当前机器状态建立基线白名单（不拦截存量右键项）");
        var current = ContextMenuScanner.Scan();
        foreach (var item in current)
        {
            if (!item.Writable) continue;
            _state.Upsert(new InterceptStateEntry
            {
                Id = item.Id,
                Hive = item.Hive,
                View = item.View,
                SubKey = item.SubKey,
                Kind = item.Kind,
                Clsid = item.Clsid,
                Name = item.Name,
                Command = item.Command,
                ExePath = item.ExePath,
                IsModernMenu = item.IsModernMenu,
                DesiredState = DesiredState.Allowed,
                FirstSeenUtc = DateTime.UtcNow.ToString("o"),
                UpdatedAtUtc = DateTime.UtcNow.ToString("o"),
                Source = "baseline",
            });
        }
        _state.SetBaselineEstablished(true);
        _state.Save();
        BackEndLog.Info($"基线已建立：{current.Count} 个存量右键项标记为放行");
    }

    // ---------- 每轮 ----------

    private void RunCycleCore(bool isBaselineEstablishment)
    {
        // 1) 纠偏 pass：明确的"保持拦截"策略被第三方改回 → 无审核自动重拦
        var snapshot = ContextMenuScanner.Scan();
        // 纯心跳，每轮 10 秒一次；只在控制台可见，不落盘（否则 backend.log 一天几千行）
        BackEndLog.Debug($"扫描完成：{snapshot.Count} 项");

        var reconciledAny = ReconcileDisabledDrift(snapshot);
        if (reconciledAny)
        {
            // 纠偏可能改了注册表，重扫一轮避免把自身写入当外部变更
            snapshot = ContextMenuScanner.Scan();
        }

        // 2) 分类 pass：新增/重现 → 拦截（运行期）或高亮（启动期）
        var byId = _state.ById();
        var touched = false;
        var presentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in snapshot)
        {
            if (_ignore.Contains(item.Id)) continue;
            presentIds.Add(item.Id);

            var state = byId.GetValueOrDefault(item.Id);
            var isBlocked = item.Writable && _blockEngine.IsBlocked(item);
            var action = ChangeClassifier.ClassifyItemMonitorAction(
                item, isBlocked, state, hasBaseline: true, isBaselineEstablishment);

            switch (action)
            {
                case ItemMonitorAction.None:
                case ItemMonitorAction.ReconcileDisabledState:
                    // 吸收进基线（纠偏已在 pass 1 完成）
                    if (state is not null) touched |= RefreshObserved(state, item);
                    break;

                case ItemMonitorAction.OfflineAddedHighlight:
                    touched |= MarkPending(state, item, "added", offline: true);
                    break;

                case ItemMonitorAction.OfflineReappearedHighlight:
                    touched |= MarkPending(state, item, "reappeared", offline: true);
                    break;

                case ItemMonitorAction.QuarantineAdded:
                    touched |= Quarantine(state, item, "added");
                    break;

                case ItemMonitorAction.QuarantineReappeared:
                    touched |= Quarantine(state, item, "reappeared");
                    break;

                case ItemMonitorAction.MetadataModifiedHighlight:
                    if (state is not null) touched |= RefreshObserved(state, item);
                    break;
            }
        }

        // 3) 缺失项处理：已放行项消失一段时间后清理状态；待审核/已删除/保持拦截保留
        var scanById = snapshot.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in byId.Values.ToList())
        {
            if (_ignore.Contains(entry.Id)) continue;
            if (presentIds.Contains(entry.Id)) continue;
            if (entry.IsDeleted || entry.IsPendingApproval) continue; // 保留：可撤销 / 待审核
            if (entry.DesiredState == DesiredState.Blocked) continue;  // 保留：明确的拦截策略

            entry.ConsecutiveMissingSnapshots++;
            if (entry.ConsecutiveMissingSnapshots >= AllowedMissingPruneCycles)
            {
                BackEndLog.Info($"已放行项消失，清理状态：{entry.Id}（{entry.Name}）");
                _state.Remove(entry.Id);
                touched = true;
            }
            else
            {
                _state.Upsert(entry);
                touched = true;
            }
        }

        if (touched)
        {
            _state.Save();
        }
    }

    // ---------- 纠偏 ----------

    private bool ReconcileDisabledDrift(List<ContextMenuItem> snapshot)
    {
        var byId = _state.ById();
        var touched = false;
        foreach (var item in snapshot)
        {
            if (_ignore.Contains(item.Id)) continue;
            var state = byId.GetValueOrDefault(item.Id);
            var isBlocked = item.Writable && _blockEngine.IsBlocked(item);
            if (!ChangeClassifier.ShouldReconcileBlockedState(item, isBlocked, state)) continue;

            BackEndLog.Info($"纠偏：{item.Name} 被第三方改回启用，自动重新拦截");
            _blockEngine.Block(item, out var note);
            state!.ObservedBlocked = true;
            state.SuppressNextDetection = true;
            state.Note = "自动纠偏：" + (string.IsNullOrWhiteSpace(note) ? "重新拦截" : note);
            state.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
            _events.Append(ToEvent(state, "Reblocked", state.Note));
            _state.Upsert(state);
            touched = true;
        }

        if (touched)
        {
            _state.Save();
        }
        return touched;
    }

    // ---------- 隔离 / 高亮 ----------

    /// <summary>运行期新增/重现：拦截 + 待审核 + 通知。信任策略为权威时直接放行/拦截。</summary>
    private bool Quarantine(InterceptStateEntry? state, ContextMenuItem item, string changeKind)
    {
        BackEndLog.Info($"检测到{changeKind}：{item.Name}（{item.SubKey}）");

        var entry = state ?? StateFromScan(item);
        if (state is null)
        {
            entry.FirstSeenUtc = DateTime.UtcNow.ToString("o");
            entry.Source = "runtime";
        }
        entry.IsDeleted = false;          // 重现时清除删除标记
        entry.DeletedAtUtc = "";
        entry.SuppressNextDetection = true;

        // 信任策略为权威（用户显式配置优先于系统默认放行）
        var policy = string.IsNullOrWhiteSpace(entry.ExePath)
            ? TrustPolicyKind.Ask
            : _policies.GetPolicy(entry.ExePath);

        if (policy == TrustPolicyKind.Allow)
        {
            entry.DesiredState = DesiredState.Allowed;
            entry.IsPendingApproval = false;
            entry.PendingChangeKind = "";
            entry.Note = "信任策略（总是放行）自动放行";
            entry.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
            _state.Upsert(entry);
            _events.Append(ToEvent(entry, "Allowed", entry.Note));
            return true;
        }

        if (policy == TrustPolicyKind.Block)
        {
            if (item.Writable && !_blockEngine.IsBlocked(item))
            {
                _blockEngine.Block(item, out _);
                entry.ObservedBlocked = true;
            }
            entry.DesiredState = DesiredState.Blocked;
            entry.IsPendingApproval = false;
            entry.PendingChangeKind = "";
            entry.Note = "信任策略（总是拦截）自动拦截";
            entry.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
            _state.Upsert(entry);
            _events.Append(ToEvent(entry, "Reblocked", entry.Note));
            return true;
        }

        // 系统/内置程序（位于 Windows 目录 / WindowsApps / MS 目录）：默认放行
        if (IsLikelySystemItem(entry))
        {
            entry.DesiredState = DesiredState.Allowed;
            entry.IsPendingApproval = false;
            entry.PendingChangeKind = "";
            entry.Note = "系统程序，默认放行";
            entry.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
            _state.Upsert(entry);
            _events.Append(ToEvent(entry, "Allowed", entry.Note));
            BackEndLog.Info($"系统程序默认放行：{entry.Name}");
            return true;
        }

        // 默认：先拦截后审核
        var blockNote = "";
        if (item.Writable && !_blockEngine.IsBlocked(item))
        {
            _blockEngine.Block(item, out blockNote);
            entry.ObservedBlocked = true;
        }
        entry.DesiredState = DesiredState.Blocked;
        entry.IsPendingApproval = true;
        entry.PendingChangeKind = changeKind;
        entry.Note = string.IsNullOrWhiteSpace(blockNote) ? "已拦截，等待审核" : "已拦截（" + blockNote + "），等待审核";
        entry.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
        _state.Upsert(entry);
        _events.Append(ToEvent(entry, changeKind == "reappeared" ? "Reappeared" : "Blocked", entry.Note));

        // 通知：管道广播 + 文件/Toast（冷却去重）
        Notify?.Invoke(new InterceptBackendNotification
        {
            Kind = InterceptPipeNotificationKind.ItemDetected,
            Item = BuildDto(entry, item),
            Message = $"检测到新增右键项：{entry.Name}，已拦截待审核",
            Timestamp = DateTimeOffset.UtcNow,
        });
        ItemQuarantined?.Invoke(BuildDto(entry, item), changeKind, entry.Note);
        NotifyFileWithCooldown(entry);

        return true;
    }

    /// <summary>启动期新增/重现：仅进入待审核（不拦截、不通知）。系统程序直接默认放行。</summary>
    private bool MarkPending(InterceptStateEntry? state, ContextMenuItem item, string changeKind, bool offline)
    {
        var entry = state ?? StateFromScan(item);
        if (state is null)
        {
            entry.FirstSeenUtc = DateTime.UtcNow.ToString("o");
            entry.Source = "runtime";
        }
        entry.IsDeleted = false;
        entry.DeletedAtUtc = "";
        entry.DesiredState = DesiredState.Blocked;
        entry.IsPendingApproval = true;
        entry.PendingChangeKind = changeKind;
        entry.Note = offline
            ? "监视器未运行期间出现，待审核（未拦截）"
            : "待审核";
        entry.UpdatedAtUtc = DateTime.UtcNow.ToString("o");

        // 系统程序：默认放行（不拦截、不进入待审核）
        if (IsLikelySystemItem(entry))
        {
            entry.DesiredState = DesiredState.Allowed;
            entry.IsPendingApproval = false;
            entry.PendingChangeKind = "";
            entry.Note = "系统程序，默认放行";
            _state.Upsert(entry);
            _events.Append(ToEvent(entry, "Allowed", entry.Note));
            BackEndLog.Info($"启动期系统程序默认放行：{entry.Name}");
            return true;
        }

        _state.Upsert(entry);
        _events.Append(ToEvent(entry, "Pending", entry.Note));
        BackEndLog.Info($"启动期{changeKind}（仅挂起审核）：{entry.Name}");
        return true;
    }

    /// <summary>刷新观测元数据（保持 DTO 新鲜；仅在确实变化时标脏）。</summary>
    private bool RefreshObserved(InterceptStateEntry state, ContextMenuItem item)
    {
        var changed = false;
        if (!string.Equals(state.Name, item.Name, StringComparison.Ordinal)) { state.Name = item.Name; changed = true; }
        if (!string.Equals(state.Command, item.Command, StringComparison.Ordinal)) { state.Command = item.Command; changed = true; }
        if (!string.Equals(state.Clsid, item.Clsid, StringComparison.OrdinalIgnoreCase)) { state.Clsid = item.Clsid; changed = true; }
        if (!string.Equals(state.ExePath, item.ExePath, StringComparison.OrdinalIgnoreCase)) { state.ExePath = item.ExePath; changed = true; }
        if (state.Kind != item.Kind) { state.Kind = item.Kind; changed = true; }
        if (state.IsModernMenu != item.IsModernMenu) { state.IsModernMenu = item.IsModernMenu; changed = true; }
        var observed = item.Writable && _blockEngine.IsBlocked(item);
        if (state.ObservedBlocked != observed)
        {
            state.ObservedBlocked = observed;
            changed = true;
        }
        if (changed)
        {
            state.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
        }
        return changed;
    }

    private void NotifyFileWithCooldown(InterceptStateEntry entry)
    {
        try
        {
            var cooldown = TimeSpan.FromMinutes(Math.Max(1, _config.NotifyCooldownMinutes));
            if (_notifications.WasNotifiedRecently(entry.Id, cooldown, DateTime.UtcNow))
            {
                return;
            }
            _notifications.MarkNotified(entry.Id, DateTime.UtcNow);
            _notifications.Save();
            NotificationHelper.NotifyNewBlock(_config.DataDir, entry.Name, entry.SubKey, entry.ExePath);
        }
        catch (Exception ex)
        {
            BackEndLog.Warn($"通知失败：{ex.Message}");
        }
    }

    // ---------- 工具 ----------

    private static InterceptStateEntry StateFromScan(ContextMenuItem item)
    {
        return new InterceptStateEntry
        {
            Id = item.Id,
            Hive = item.Hive,
            View = item.View,
            SubKey = item.SubKey,
            Kind = item.Kind,
            Clsid = item.Clsid,
            Name = item.Name,
            Command = item.Command,
            ExePath = item.ExePath,
            IsModernMenu = item.IsModernMenu,
            DesiredState = DesiredState.Allowed,
            FirstSeenUtc = DateTime.UtcNow.ToString("o"),
            UpdatedAtUtc = DateTime.UtcNow.ToString("o"),
            Source = "runtime",
        };
    }

    /// <summary>系统/内置程序：位于 Windows 目录 / WindowsApps / Microsoft 目录下。</summary>
    private static bool IsLikelySystemItem(InterceptStateEntry entry)
        => IsLikelySystemPath(entry.ExePath);

    private static bool IsLikelySystemPath(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;
        try
        {
            var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(exePath));
            if (full.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase)) return true;

            var win = Environment.ExpandEnvironmentVariables("%WINDIR%");
            if (!string.IsNullOrWhiteSpace(win)
                && full.StartsWith(win.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var pf = Environment.ExpandEnvironmentVariables("%ProgramFiles%");
            if (!string.IsNullOrWhiteSpace(pf)
                && full.StartsWith(pf.TrimEnd('\\') + "\\Microsoft\\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var pf86 = Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%");
            if (!string.IsNullOrWhiteSpace(pf86)
                && full.StartsWith(pf86.TrimEnd('\\') + "\\Microsoft\\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch
        {
            // 路径解析失败按非系统处理
        }
        return false;
    }

    private static InterceptEvent ToEvent(InterceptStateEntry state, string action, string note)
    {
        return new InterceptEvent
        {
            Action = action,
            Id = state.Id,
            Hive = state.Hive,
            View = state.View,
            SubKey = state.SubKey,
            Kind = state.Kind,
            Clsid = state.Clsid,
            Name = state.Name,
            Command = state.Command,
            ExePath = state.ExePath,
            IsModernMenu = state.IsModernMenu,
            Source = state.Source,
            Note = note,
        };
    }

    private static InterceptItemDto BuildDto(InterceptStateEntry state, ContextMenuItem item)
    {
        return new InterceptItemDto
        {
            Id = state.Id,
            Hive = state.Hive,
            View = state.View,
            SubKey = state.SubKey,
            Kind = state.Kind,
            Clsid = state.Clsid,
            Name = state.Name,
            Command = state.Command,
            ExePath = state.ExePath,
            IsModernMenu = state.IsModernMenu,
            DesiredState = state.DesiredState switch
            {
                DesiredState.Blocked => "blocked",
                DesiredState.Allowed => "allowed",
                _ => "none",
            },
            IsBlocked = state.ObservedBlocked,
            IsPendingApproval = state.IsPendingApproval,
            PendingChangeKind = state.PendingChangeKind,
            IsDeleted = state.IsDeleted,
            HasBackup = !string.IsNullOrWhiteSpace(state.BackupFilePath),
            IsIgnored = false,
            ConsistencyIssue = state.IsDeleted ? "此项之前已删除但当前又出现" : "",
            Source = state.Source,
            FirstSeenUtc = state.FirstSeenUtc,
            UpdatedAtUtc = state.UpdatedAtUtc,
            Note = state.Note,
        };
    }
}