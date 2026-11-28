using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

/// <summary>
/// 管道命令的业务分发处理器（对应 ContextMenuMgr 后端 HandleRequestAsync 的职责）。
/// 所有状态变更操作经 <see cref="_opGate"/> 串行化，与轮询监视器互斥，杜绝并发写竞争。
/// 每次状态变更：注册表原语（BlockEngine）→ 状态持久化 → 操作记录（EventLog）。
/// </summary>
public sealed class InterceptRequestHandler
{
    private readonly string _activeInterceptDir;
    private readonly string _backupDir;
    private readonly StateStore _state;
    private readonly EventLog _events;
    private readonly BlockEngine _blockEngine;
    private readonly TrustPolicyStore _policies;
    private readonly IgnoreStore _ignore;
    private readonly NotifyStateStore _notifications;
    private readonly SemaphoreSlim _opGate = new(1, 1);

    /// <summary>请求拦截服务退出（进程内运行时据此停机）。</summary>
    public event EventHandler? ShutdownRequested;

    /// <summary>主机可注入：向已订阅前端推送通知（Program 接线到管道服务器广播）。</summary>
    public Action<InterceptBackendNotification>? Notify { get; set; }

    public InterceptRequestHandler(
        string activeInterceptDir,
        StateStore state,
        EventLog events,
        BlockEngine blockEngine,
        TrustPolicyStore policies,
        IgnoreStore ignore,
        NotifyStateStore notifications)
    {
        _activeInterceptDir = activeInterceptDir;
        _state = state;
        _events = events;
        _blockEngine = blockEngine;
        _policies = policies;
        _ignore = ignore;
        _notifications = notifications;
        _backupDir = Path.Combine(activeInterceptDir, "backups");
        Directory.CreateDirectory(_backupDir);
    }

    public async Task<InterceptPipeResponse> DispatchAsync(InterceptPipeRequest request, CancellationToken cancellationToken)
    {
        switch (request.Command)
        {
            case InterceptPipeCommand.Ping:
                return Ok("pong");

            case InterceptPipeCommand.SubscribeNotifications:
                return Ok("订阅成功");

            case InterceptPipeCommand.GetSnapshot:
                return await GetSnapshotAsync(cancellationToken);

            case InterceptPipeCommand.ApplyDecision:
                return await ApplyDecisionAsync(request, cancellationToken);

            case InterceptPipeCommand.DeleteItem:
                return await DeleteItemAsync(request.ItemId ?? "", request.ClientOperationId, cancellationToken);

            case InterceptPipeCommand.UndoDelete:
                return await UndoDeleteAsync(request.ItemId ?? "", request.ClientOperationId, cancellationToken);

            case InterceptPipeCommand.PurgeDeletedItem:
                return await PurgeDeletedItemAsync(request.ItemId ?? "", cancellationToken);

            case InterceptPipeCommand.StopTracking:
                return await StopTrackingAsync(request.ItemId ?? "", cancellationToken);

            case InterceptPipeCommand.ResumeTracking:
                return await ResumeTrackingAsync(request.ItemId ?? "", cancellationToken);

            case InterceptPipeCommand.RemoveEventRows:
                return await RemoveEventRowsAsync(request.RowIds, cancellationToken);

            case InterceptPipeCommand.ClearEvents:
                return await ClearEventsAsync(cancellationToken);

            case InterceptPipeCommand.SetTrustPolicy:
                return await SetTrustPolicyAsync(request.ExePath ?? "", request.Policy ?? "", cancellationToken);

            case InterceptPipeCommand.RequestShutdown:
                ShutdownRequested?.Invoke(this, EventArgs.Empty);
                return Ok("拦截服务退出已请求");

            case InterceptPipeCommand.SetLogLevel:
                return Ok("日志级别设置成功（当前实现为固定级别）");

            default:
                return Fail("未知命令: " + request.Command, null);
        }
    }

    // ================= 查询 =================

    private Task<InterceptPipeResponse> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var scan = ContextMenuScanner.Scan();
        var scanById = scan.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        var states = _state.ById();

        var items = new List<InterceptItemDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in states.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!seen.Add(entry.Id)) continue;
            scanById.TryGetValue(entry.Id, out var scanItem);
            items.Add(BuildItemDto(entry, scanItem));
        }

        // 扫描到但无状态（基线后理论上不会出现；防御性补齐，避免列表缺项）
        foreach (var item in scan)
        {
            if (seen.Add(item.Id) && !_ignore.Contains(item.Id))
            {
                items.Add(BuildItemDto(StateFromScan(item), item));
            }
        }

        // 「停止追踪」的条目：state 中已移除，但必须仍出现在快照里（IsIgnored），
        // 否则「已停止追踪」筛选/计数/恢复追踪全部不可见
        foreach (var ignoredEntry in _ignore.GetAll())
        {
            if (!seen.Add(ignoredEntry.Id)) continue;
            scanById.TryGetValue(ignoredEntry.Id, out var scanItem);
            items.Add(new InterceptItemDto
            {
                Id = ignoredEntry.Id,
                Hive = ParseHiveFromId(ignoredEntry.Id),
                View = RegView.Default,
                SubKey = ignoredEntry.SubKey,
                Kind = scanItem?.Kind ?? ContextMenuKind.ShellVerb,
                Clsid = scanItem?.Clsid ?? "",
                Name = ignoredEntry.Name,
                Command = scanItem?.Command ?? "",
                ExePath = ignoredEntry.ExePath,
                IsModernMenu = scanItem?.IsModernMenu ?? false,
                DesiredState = "none",
                IsBlocked = false,
                IsPendingApproval = false,
                IsIgnored = true,
                Source = "ignored",
                FirstSeenUtc = "",
                UpdatedAtUtc = ignoredEntry.CreatedUtc,
                Note = ignoredEntry.Note,
            });
        }

        // 名称排序，保证列表顺序稳定
        items.Sort((a, b) =>
        {
            var cmp = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return cmp != 0 ? cmp : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        });

        var events = EventLog.ReadAll(_activeInterceptDir)
            .Select(e => new InterceptEventDto
            {
                RowId = e.RowId,
                TimestampUtc = e.TimestampUtc,
                Action = e.Action,
                Id = e.Id,
                Hive = e.Hive,
                View = e.View,
                SubKey = e.SubKey,
                Kind = e.Kind,
                Clsid = e.Clsid,
                Name = e.Name,
                Command = e.Command,
                ExePath = e.ExePath,
                Source = e.Source,
                IsModernMenu = e.IsModernMenu,
                Note = e.Note,
            })
            .ToList();

        var ignored = _ignore.GetAll().Select(i => new InterceptIgnoredDto
        {
            Id = i.Id,
            Name = i.Name,
            SubKey = i.SubKey,
            ExePath = i.ExePath,
            CreatedUtc = i.CreatedUtc,
            Note = i.Note,
        }).ToList();

        var policies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in _policies.GetAll())
        {
            policies[p.ExePath] = p.Policy.ToString().ToLowerInvariant();
        }

        int pending = items.Count(i => i.IsPendingApproval && !i.IsDeleted);
        int blocked = items.Count(i => !i.IsPendingApproval && !i.IsDeleted && i.DesiredState == "blocked");
        int allowed = items.Count(i => !i.IsDeleted && i.DesiredState == "allowed");

        return Task.FromResult(new InterceptPipeResponse
        {
            Success = true,
            Message = "快照已生成",
            Snapshot = new InterceptSnapshot
            {
                Items = items,
                Events = events,
                Ignored = ignored,
                Policies = policies,
                PendingCount = pending,
                BlockedCount = blocked,
                AllowedCount = allowed,
                StateFingerprint = _state.State.SavedAtUtc,
                ConfigNotice = "",
            },
        });
    }

    private InterceptItemDto BuildItemDto(InterceptStateEntry entry, ContextMenuItem? scanItem)
    {
        var desired = entry.DesiredState switch
        {
            DesiredState.Blocked => "blocked",
            DesiredState.Allowed => "allowed",
            _ => "none",
        };

        var isBlocked = scanItem is not null && scanItem.Writable
            ? _blockEngine.IsBlocked(scanItem) || entry.ObservedBlocked
            : entry.ObservedBlocked;

        var isIgnored = _ignore.Contains(entry.Id);
        var hasBackup = !string.IsNullOrWhiteSpace(entry.BackupFilePath)
                        && File.Exists(Path.Combine(_backupDir, entry.BackupFilePath));

        var isBlockedLive = scanItem is not null && scanItem.Writable && _blockEngine.IsBlocked(scanItem);
        var consistencyIssue = scanItem is not null && scanItem.Writable
            ? ChangeClassifier.GetConsistencyIssue(scanItem, isBlockedLive, entry)
            : null;

        return new InterceptItemDto
        {
            Id = entry.Id,
            Hive = entry.Hive,
            View = entry.View,
            SubKey = entry.SubKey,
            Kind = entry.Kind,
            Clsid = entry.Clsid,
            Name = entry.Name,
            Command = entry.Command,
            ExePath = entry.ExePath,
            IsModernMenu = entry.IsModernMenu,
            DesiredState = desired,
            IsBlocked = isBlocked,
            IsPendingApproval = entry.IsPendingApproval,
            PendingChangeKind = entry.PendingChangeKind,
            IsDeleted = entry.IsDeleted,
            HasBackup = hasBackup,
            IsIgnored = isIgnored,
            ConsistencyIssue = consistencyIssue ?? "",
            Source = entry.Source,
            FirstSeenUtc = entry.FirstSeenUtc,
            UpdatedAtUtc = entry.UpdatedAtUtc,
            Note = entry.Note,
        };
    }

    /// <summary>变更后条目 DTO（状态变更命令的响应 &amp; 广播载荷）。</summary>
    private InterceptItemDto? BuildChangedItem(string itemId)
    {
        var byId = _state.ById();
        if (!byId.TryGetValue(itemId, out var entry)) return null;
        var scan = ContextMenuScanner.Scan().FirstOrDefault(i =>
            string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
        return BuildItemDto(entry, scan);
    }

    // ================= 审核决策 =================

    private async Task<InterceptPipeResponse> ApplyDecisionAsync(InterceptPipeRequest request, CancellationToken cancellationToken)
    {
        var itemId = request.ItemId ?? "";
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return Fail("缺少条目 Id", InterceptPipeErrorCodes.ItemNotFound);
        }

        await _opGate.WaitAsync(cancellationToken);
        try
        {
            var byId = _state.ById();
            if (!byId.TryGetValue(itemId, out var state))
            {
                return Fail("条目不存在", InterceptPipeErrorCodes.ItemNotFound);
            }

            var scan = ContextMenuScanner.Scan().FirstOrDefault(i =>
                string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));

            var decision = request.Decision ?? InterceptDecision.Allow;
            switch (decision)
            {
                case InterceptDecision.Allow:
                    return ApplyAllow(state, scan, request.Trust ?? false, request.ClientOperationId);
                case InterceptDecision.Deny:
                    return ApplyDeny(state, scan, request.ClientOperationId);
                case InterceptDecision.Remove:
                    return ApplyRemove(state, scan, request.ClientOperationId);
                default:
                    return Fail("未知决策", null);
            }
        }
        finally
        {
            _opGate.Release();
        }
    }

    private InterceptPipeResponse ApplyAllow(InterceptStateEntry state, ContextMenuItem? scan, bool trust, Guid? clientOperationId)
    {
        if (scan is not null && scan.Writable && _blockEngine.IsBlocked(scan))
        {
            _blockEngine.Unblock(scan, out _);
            state.ObservedBlocked = false;
            state.SuppressNextDetection = true;
        }

        state.DesiredState = DesiredState.Allowed;
        state.IsPendingApproval = false;
        state.PendingChangeKind = "";
        state.Note = trust ? "用户放行并信任此程序" : "用户放行";
        state.UpdatedAtUtc = DateTime.UtcNow.ToString("o");

        if (trust && !string.IsNullOrWhiteSpace(state.ExePath))
        {
            _policies.SetPolicy(state.ExePath, TrustPolicyKind.Allow, "用户在审核页选择『放行并信任此程序』");
            _policies.Save();
        }

        _state.Upsert(state);
        _state.Save();
        _events.Append(ToEvent(state, "Allowed", state.Note));
        return OkItem(state, clientOperationId);
    }

    private InterceptPipeResponse ApplyDeny(InterceptStateEntry state, ContextMenuItem? scan, Guid? clientOperationId)
    {
        var note = "";
        if (scan is not null && scan.Writable && !_blockEngine.IsBlocked(scan))
        {
            _blockEngine.Block(scan, out note);
            state.ObservedBlocked = true;
            state.SuppressNextDetection = true;
        }

        state.DesiredState = DesiredState.Blocked;
        state.IsPendingApproval = false;
        state.PendingChangeKind = "";
        state.Note = string.IsNullOrWhiteSpace(note) ? "保持拦截" : note;
        state.UpdatedAtUtc = DateTime.UtcNow.ToString("o");

        _state.Upsert(state);
        _state.Save();
        _events.Append(ToEvent(state, "Reblocked", state.Note));
        return OkItem(state, clientOperationId);
    }

    private InterceptPipeResponse ApplyRemove(InterceptStateEntry state, ContextMenuItem? scan, Guid? clientOperationId)
    {
        if (scan is not null && scan.Writable && string.IsNullOrWhiteSpace(scan.SubKey))
        {
            return Fail("缺少注册表路径，无法移除", InterceptPipeErrorCodes.RegistryWriteFailed);
        }

        if (scan is not null && scan.Writable)
        {
            // 有注册表实体：物理删除（.reg 备份，可撤销）
            if (!DeleteItemCore(state, scan, out var deletedNote))
            {
                return Fail("删除注册表项失败" + (string.IsNullOrWhiteSpace(deletedNote) ? "" : $"：{deletedNote}"),
                    InterceptPipeErrorCodes.RegistryWriteFailed);
            }
        }
        else
        {
            // 仅从队列移除：按放行落定，避免下次扫描再次拦截
            state.DesiredState = DesiredState.Allowed;
            state.IsPendingApproval = false;
            state.PendingChangeKind = "";
            state.Note = "从审核队列移除（注册表项已不存在）";
            state.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
            _state.Upsert(state);
            _state.Save();
        }

        _events.Append(ToEvent(state, "Removed", state.Note));
        return OkItem(state, clientOperationId);
    }

    // ================= 删除 / 撤销 / 清除 =================

    private async Task<InterceptPipeResponse> DeleteItemAsync(string itemId, Guid? clientOperationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return Fail("缺少条目 Id", InterceptPipeErrorCodes.ItemNotFound);
        }

        await _opGate.WaitAsync(cancellationToken);
        try
        {
            var byId = _state.ById();
            var scan = ContextMenuScanner.Scan().FirstOrDefault(i =>
                string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));

            if (scan is null || !scan.Writable)
            {
                // 已不存在：只标记删除状态
                if (!byId.TryGetValue(itemId, out var state))
                {
                    return Fail("条目不存在", InterceptPipeErrorCodes.ItemNotFound);
                }
                state.IsDeleted = true;
                state.DeletedAtUtc = DateTime.UtcNow.ToString("o");
                state.IsPendingApproval = false;
                state.PendingChangeKind = "";
                state.Note = "删除条目（注册表项已不存在）";
                state.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
                _state.Upsert(state);
                _state.Save();
                _events.Append(ToEvent(state, "Removed", state.Note));
                return OkItem(state, clientOperationId);
            }

            var entry = byId.GetValueOrDefault(itemId) ?? StateFromScan(scan);
            if (!DeleteItemCore(entry, scan, out var note))
            {
                return Fail("删除注册表项失败" + (string.IsNullOrWhiteSpace(note) ? "" : $"：{note}"),
                    InterceptPipeErrorCodes.RegistryWriteFailed);
            }
            _events.Append(ToEvent(entry, "Removed", entry.Note));
            return OkItem(entry, clientOperationId);
        }
        finally
        {
            _opGate.Release();
        }
    }

    private async Task<InterceptPipeResponse> UndoDeleteAsync(string itemId, Guid? clientOperationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return Fail("缺少条目 Id", InterceptPipeErrorCodes.ItemNotFound);
        }

        await _opGate.WaitAsync(cancellationToken);
        try
        {
            var byId = _state.ById();
            if (!byId.TryGetValue(itemId, out var state) || !state.IsDeleted)
            {
                return Fail("该条目未处于已删除状态", InterceptPipeErrorCodes.ItemNotFound);
            }

            if (string.IsNullOrWhiteSpace(state.BackupFilePath) || !File.Exists(Path.Combine(_backupDir, state.BackupFilePath)))
            {
                return Fail("备份文件不存在，无法撤销", InterceptPipeErrorCodes.RegistryRestoreFailed);
            }

            var backupPath = Path.Combine(_backupDir, state.BackupFilePath);
            var psi = new ProcessStartInfo("reg.exe", $"import \"{backupPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var proc = Process.Start(psi))
            {
                if (proc is null) return Fail("无法启动 reg.exe", InterceptPipeErrorCodes.RegistryRestoreFailed);
                proc.WaitForExit(15000);
                if (proc.ExitCode != 0)
                {
                    return Fail($"reg.exe import 失败（退出码 {proc.ExitCode}）", InterceptPipeErrorCodes.RegistryRestoreFailed);
                }
            }

            state.IsDeleted = false;
            state.DeletedAtUtc = "";
            state.BackupFilePath = "";
            state.DesiredState = DesiredState.Blocked;
            state.IsPendingApproval = true;
            state.PendingChangeKind = "reappeared";
            state.Note = "撤销删除，已恢复并重新进入待审核";
            state.ObservedBlocked = false;
            state.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
            _state.Upsert(state);
            _state.Save();
            _events.Append(ToEvent(state, "Restored", state.Note));
            return OkItem(state, clientOperationId);
        }
        finally
        {
            _opGate.Release();
        }
    }

    private async Task<InterceptPipeResponse> PurgeDeletedItemAsync(string itemId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return Fail("缺少条目 Id", InterceptPipeErrorCodes.ItemNotFound);
        }

        await _opGate.WaitAsync(cancellationToken);
        try
        {
            var byId = _state.ById();
            if (!byId.TryGetValue(itemId, out var state) || !state.IsDeleted)
            {
                return Fail("该条目未处于已删除状态", InterceptPipeErrorCodes.ItemNotFound);
            }

            if (!string.IsNullOrWhiteSpace(state.BackupFilePath))
            {
                try
                {
                    var backupPath = Path.Combine(_backupDir, state.BackupFilePath);
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                }
                catch { }
            }

            _events.Append(ToEvent(state, "Purged", "永久清除已删除条目"));
            _state.Remove(itemId);
            _state.Save();
            return Ok("已永久清除");
        }
        finally
        {
            _opGate.Release();
        }
    }

    // ================= 停止 / 恢复追踪 =================

    private async Task<InterceptPipeResponse> StopTrackingAsync(string itemId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return Fail("缺少条目 Id", InterceptPipeErrorCodes.ItemNotFound);
        }

        await _opGate.WaitAsync(cancellationToken);
        try
        {
            var byId = _state.ById();
            InterceptStateEntry? state = byId.GetValueOrDefault(itemId);
            if (state is null && !byId.ContainsKey(itemId) && string.IsNullOrWhiteSpace(itemId)) return Fail("条目不存在", null);

            var scan = ContextMenuScanner.Scan().FirstOrDefault(i =>
                string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (state is null && scan is not null)
            {
                state = StateFromScan(scan);
            }
            if (state is null)
            {
                return Fail("条目不存在", InterceptPipeErrorCodes.ItemNotFound);
            }

            // 加入忽略列表（保留注册表现状，不再拦截/提醒）
            _ignore.Add(itemId, state.Name, state.SubKey, state.ExePath, "用户在审核页选择停止追踪");
            _ignore.Save();

            _events.Append(ToEvent(state, "Ignored", "停止追踪：不再拦截与提醒"));
            _state.Remove(itemId);
            _state.Save();
            return Ok("已停止追踪");
        }
        finally
        {
            _opGate.Release();
        }
    }

    private async Task<InterceptPipeResponse> ResumeTrackingAsync(string itemId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return Fail("缺少条目 Id", InterceptPipeErrorCodes.ItemNotFound);
        }

        await _opGate.WaitAsync(cancellationToken);
        try
        {
            if (!_ignore.Contains(itemId))
            {
                return Fail("该条目不在停止追踪列表中", InterceptPipeErrorCodes.ItemNotFound);
            }

            _ignore.Remove(itemId);
            _ignore.Save();

            var scan = ContextMenuScanner.Scan().FirstOrDefault(i =>
                string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (scan is not null)
            {
                _events.Append(ToEvent(StateFromScan(scan), "Tracking", "恢复追踪：重新进入拦截流程"));
            }
            return Ok("已恢复追踪");
        }
        finally
        {
            _opGate.Release();
        }
    }

    // ================= 操作记录 =================

    private Task<InterceptPipeResponse> RemoveEventRowsAsync(IReadOnlyCollection<string>? rowIds, CancellationToken cancellationToken)
    {
        var removed = _events.RemoveRows(rowIds ?? []);
        return Task.FromResult(Ok($"已删除 {removed} 条操作记录"));
    }

    private Task<InterceptPipeResponse> ClearEventsAsync(CancellationToken cancellationToken)
    {
        var cleared = _events.Clear();
        return Task.FromResult(Ok($"已清空 {cleared} 条操作记录"));
    }

    // ================= 信任策略 =================

    private async Task<InterceptPipeResponse> SetTrustPolicyAsync(string exePath, string policy, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return Fail("缺少程序路径", null);
        }

        await _opGate.WaitAsync(cancellationToken);
        try
        {
            var kind = policy.ToLowerInvariant() switch
            {
                "allow" => TrustPolicyKind.Allow,
                "block" => TrustPolicyKind.Block,
                _ => TrustPolicyKind.Ask,
            };
            if (kind == TrustPolicyKind.Ask)
            {
                _policies.RemovePolicy(exePath);
            }
            else
            {
                _policies.SetPolicy(exePath, kind, "用户在审核页/详情设置");
            }
            _policies.Save();

            // 策略为权威：立即对同一程序的所有待审核条目生效
            var byId = _state.ById();
            var touched = new List<InterceptStateEntry>();
            foreach (var entry in byId.Values.Where(e => e.IsPendingApproval &&
                                                         string.Equals(e.ExePath, exePath, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var scan = ContextMenuScanner.Scan().FirstOrDefault(i =>
                    string.Equals(i.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
                if (scan is null) continue;

                if (kind == TrustPolicyKind.Allow && scan.Writable && _blockEngine.IsBlocked(scan))
                {
                    _blockEngine.Unblock(scan, out _);
                    entry.ObservedBlocked = false;
                    entry.DesiredState = DesiredState.Allowed;
                    entry.Note = "信任策略（总是放行）自动放行";
                    entry.IsPendingApproval = false;
                    entry.PendingChangeKind = "";
                    entry.SuppressNextDetection = true;
                    entry.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
                    _events.Append(ToEvent(entry, "Allowed", entry.Note));
                    touched.Add(entry);
                }
                else if (kind == TrustPolicyKind.Block && scan.Writable && !_blockEngine.IsBlocked(scan))
                {
                    _blockEngine.Block(scan, out _);
                    entry.ObservedBlocked = true;
                    entry.DesiredState = DesiredState.Blocked;
                    entry.Note = "信任策略（总是拦截）自动拦截";
                    entry.IsPendingApproval = false;
                    entry.PendingChangeKind = "";
                    entry.SuppressNextDetection = true;
                    entry.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
                    _events.Append(ToEvent(entry, "Reblocked", entry.Note));
                    touched.Add(entry);
                }
            }

            if (touched.Count > 0)
            {
                foreach (var entry in touched)
                {
                    _state.Upsert(entry);
                }
                _state.Save();
            }

            return Ok($"信任策略已设为 {policy}");
        }
        finally
        {
            _opGate.Release();
        }
    }

    // ================= 内部工具 =================

    /// <summary>物理删除注册表项：先解除屏蔽标记，再导出 .reg 备份，最后删除子键树（可撤销）。</summary>
    private bool DeleteItemCore(InterceptStateEntry state, ContextMenuItem scan, out string note)
    {
        note = "";
        try
        {
            if (scan.Writable && _blockEngine.IsBlocked(scan))
            {
                _blockEngine.Unblock(scan, out _);
            }

            var backupName = $"delete-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{HashId(scan.Id)}.reg";
            var backupPath = Path.Combine(_backupDir, backupName);
            var psi = new ProcessStartInfo("reg.exe", $"export \"{BuildRegistryPathForExport(scan)}\" \"{backupPath}\" /y")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var proc = Process.Start(psi))
            {
                if (proc is null)
                {
                    note = "无法启动 reg.exe";
                    return false;
                }
                proc.WaitForExit(15000);
                if (proc.ExitCode != 0 || !File.Exists(backupPath))
                {
                    note = $"reg.exe export 失败(退出码 {proc.ExitCode})";
                    return false;
                }
            }

            using (var root = RegistryAccess.OpenBase(scan.Hive, scan.View, writable: true))
            {
                root.DeleteSubKeyTree(scan.SubKey, throwOnMissingSubKey: false);
            }

            state.IsDeleted = true;
            state.DeletedAtUtc = DateTime.UtcNow.ToString("o");
            state.BackupFilePath = backupName;
            state.IsPendingApproval = false;
            state.PendingChangeKind = "";
            state.DesiredState = DesiredState.Blocked;
            state.ObservedBlocked = false;
            state.Note = $"已删除注册表项（备份：{backupName}）";
            state.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
            _state.Upsert(state);
            _state.Save();
            return true;
        }
        catch (Exception ex)
        {
            note = ex.Message;
            return false;
        }
    }

    private static string HashId(string id)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(id));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    private static string BuildRegistryPathForExport(ContextMenuItem item)
    {
        var hive = item.Hive == RegHive.HKCU ? "HKCU" : "HKLM";
        return $"{hive}\\{item.SubKey}";
    }

    /// <summary>从条目 Id（hive|view|subkey）解析 hive，用于停止追踪条目的展示。</summary>
    private static RegHive ParseHiveFromId(string id)
        => id.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ? RegHive.HKLM : RegHive.HKCU;

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

    private InterceptPipeResponse OkItem(InterceptStateEntry state, Guid? clientOperationId)
    {
        // 广播由管道服务器在收到成功响应后统一执行（携带 Item + ClientOperationId，前端去重）
        return new InterceptPipeResponse
        {
            Success = true,
            Message = "操作成功",
            Item = BuildChangedItem(state.Id),
            ClientOperationId = clientOperationId,
        };
    }

    private static InterceptPipeResponse Ok(string message)
        => new() { Success = true, Message = message };

    private static InterceptPipeResponse Fail(string message, string? errorCode)
        => new() { Success = false, Message = message, ErrorCode = errorCode };
}
