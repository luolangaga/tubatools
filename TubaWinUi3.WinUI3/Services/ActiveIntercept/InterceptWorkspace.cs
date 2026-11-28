using Microsoft.UI.Dispatching;

namespace TubaWinUi3.Services.ActiveIntercept;

/// <summary>
/// 主动拦截工作区服务（对应 ContextMenuMgr 的 ContextMenuWorkspaceService）：
/// 持有管道客户端、最新快照缓存与"已见待审核 Id"去重基线；
/// 管道推送 + 主动刷新双路合流，UI 事件统一编组到 UI 线程。
/// 页面只订阅事件并渲染，操作逻辑全部收敛在本服务（前台操作逻辑对齐 ContextMenuMgr）。
/// </summary>
public static class InterceptWorkspace
{
    private static readonly object Sync = new();
    private static InterceptPipeClient? _client;
    private static CancellationTokenSource? _lifeCts;
    private static DispatcherQueue? _ui;
    private static bool _initialized;

    private static List<InterceptItemDto> _items = [];
    private static List<InterceptEventDto> _events = [];
    private static List<InterceptIgnoredDto> _ignored = [];
    private static Dictionary<string, string> _policies = new(StringComparer.OrdinalIgnoreCase);
    private static int _pendingCount;
    private static int _blockedCount;
    private static int _allowedCount;
    private static string _fingerprint = "";

    /// <summary>已上报过"待审核"的条目 Id（防重复弹窗/提示）。</summary>
    private static readonly HashSet<string> SeenPendingIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>最近发起的客户端操作 Id（用于过滤自己触发的广播）。</summary>
    private static readonly Queue<Guid> RecentClientOps = new();

    // ================= 状态暴露 =================

    public static IReadOnlyList<InterceptItemDto> Items => _items;
    public static IReadOnlyList<InterceptEventDto> Events => _events;
    public static IReadOnlyList<InterceptIgnoredDto> Ignored => _ignored;
    public static IReadOnlyDictionary<string, string> Policies => _policies;
    public static int PendingCount => _pendingCount;
    public static int BlockedCount => _blockedCount;
    public static int AllowedCount => _allowedCount;

    public static bool IsConnected => _client?.IsConnected ?? false;
    public static bool IsAvailable => _client is not null;

    // ================= 事件（已在 UI 线程触发） =================

    /// <summary>拦截列表或计数变化。</summary>
    public static event EventHandler? ItemsChanged;

    /// <summary>操作记录变化。</summary>
    public static event EventHandler? EventsChanged;

    /// <summary>新条目录入待审核（驱动页面 InfoBar 与新项高亮）。</summary>
    public static event EventHandler<InterceptItemDto>? PendingApprovalDetected;

    /// <summary>拦截服务消息（需用户注意）。</summary>
    public static event EventHandler<string>? ServiceAttention;

    /// <summary>拦截服务连接状态变化（参数：是否已连接）。</summary>
    public static event EventHandler<bool>? ConnectionChanged;

    // ================= 生命周期 =================

    /// <summary>在主程序启动主动拦截运行时后调用（UI 线程）。</summary>
    public static void Initialize(DispatcherQueue uiQueue)
    {
        lock (Sync)
        {
            if (_initialized) return;
            _initialized = true;
            _ui = uiQueue;
            _lifeCts = new CancellationTokenSource();
            _client = new InterceptPipeClient();
            _client.NotificationReceived += (_, notification) => OnNotification(notification);
            _ = _client.ConnectNotificationsAsync();
        }
    }

    public static void Shutdown()
    {
        InterceptPipeClient? client;
        lock (Sync)
        {
            if (!_initialized) return;
            _initialized = false;
            client = _client;
            _client = null;
            try { _lifeCts?.Cancel(); } catch { }
        }
        client?.Dispose();
    }

    // ================= 查询/刷新 =================

    /// <summary>拉取全量快照并合流本地缓存（管道未连接时返回 false）。</summary>
    public static async Task<bool> RefreshAsync()
    {
        var client = GetClient();
        if (client is null) return false;

        InterceptSnapshot? snapshot;
        try
        {
            snapshot = await client.GetSnapshotAsync().ConfigureAwait(false);
        }
        catch (BackendRequestException)
        {
            RaiseConnection(false);
            return false;
        }
        catch (Exception)
        {
            RaiseConnection(false);
            return false;
        }

        if (snapshot is null)
        {
            RaiseConnection(false);
            return false;
        }

        MergeSnapshot(snapshot);
        RaiseConnection(true);
        return true;
    }

    /// <summary>待审核数量（管道不可用时返回 -1 表示未知）。</summary>
    public static async Task<int> EnsureFreshPendingCountAsync()
    {
        if (_pendingCount == 0 && IsConnected)
        {
            await RefreshAsync().ConfigureAwait(false);
        }
        return _pendingCount;
    }

    // ================= 操作（全部走管道；失败抛 BackendRequestException） =================

    public static async Task<InterceptItemDto?> ApplyDecisionAsync(string itemId, InterceptDecision decision, bool trust)
    {
        var client = RequireClient();
        var opId = TrackOp();
        var updated = await client.ApplyDecisionAsync(itemId, decision, trust, opId).ConfigureAwait(false);
        if (updated is not null)
        {
            UpsertItem(updated);
            RemovePendingMark(updated.Id);
        }
        return updated;
    }

    public static async Task<InterceptItemDto?> DeleteItemAsync(string itemId)
    {
        var client = RequireClient();
        var opId = TrackOp();
        var updated = await client.DeleteItemAsync(itemId, opId).ConfigureAwait(false);
        if (updated is not null)
        {
            UpsertItem(updated);
            RemovePendingMark(updated.Id);
        }
        return updated;
    }

    public static async Task<InterceptItemDto?> UndoDeleteAsync(string itemId)
    {
        var client = RequireClient();
        var opId = TrackOp();
        var updated = await client.UndoDeleteAsync(itemId, opId).ConfigureAwait(false);
        if (updated is not null)
        {
            UpsertItem(updated);
            RemovePendingMark(updated.Id);
        }
        return updated;
    }

    public static async Task PurgeDeletedItemAsync(string itemId)
    {
        var client = RequireClient();
        await client.PurgeDeletedItemAsync(itemId).ConfigureAwait(false);
        RemoveItemLocal(itemId);
    }

    public static async Task StopTrackingAsync(string itemId)
    {
        var client = RequireClient();
        await client.StopTrackingAsync(itemId).ConfigureAwait(false);
        RemoveItemLocal(itemId);
        RefreshIgnoredOnly();
    }

    public static async Task ResumeTrackingAsync(string itemId)
    {
        var client = RequireClient();
        await client.ResumeTrackingAsync(itemId).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    public static async Task RemoveEventRowsAsync(IEnumerable<string> rowIds)
    {
        var client = RequireClient();
        await client.RemoveEventRowsAsync(rowIds).ConfigureAwait(false);
        RaiseEventsChanged();
    }

    public static async Task ClearEventsAsync()
    {
        var client = RequireClient();
        await client.ClearEventsAsync().ConfigureAwait(false);
        RaiseEventsChanged();
    }

    public static async Task SetTrustPolicyAsync(string exePath, string policy)
    {
        var client = RequireClient();
        await client.SetTrustPolicyAsync(exePath, policy).ConfigureAwait(false);
        lock (Sync)
        {
            if (policy == "ask") _policies.Remove(exePath);
            else _policies[exePath] = policy;
        }
        RaiseItemsChanged();
    }

    // ================= 内部：合流 =================

    private static InterceptPipeClient? GetClient()
    {
        lock (Sync) return _client;
    }

    private static InterceptPipeClient RequireClient()
    {
        var client = GetClient();
        if (client is null)
        {
            throw new InvalidOperationException("主动拦截工作区尚未初始化。");
        }
        return client;
    }

    private static Guid TrackOp()
    {
        var id = Guid.NewGuid();
        lock (Sync)
        {
            RecentClientOps.Enqueue(id);
            while (RecentClientOps.Count > 32) RecentClientOps.Dequeue();
        }
        return id;
    }

    private static void MergeSnapshot(InterceptSnapshot snapshot)
    {
        var newPending = new List<InterceptItemDto>();
        bool itemsChanged;
        bool eventsChanged;
        lock (Sync)
        {
            var staleSeen = new List<string>();
            foreach (var seen in SeenPendingIds)
            {
                if (!snapshot.Items.Any(i => i.IsPendingApproval && !i.IsDeleted
                                              && string.Equals(i.Id, seen, StringComparison.OrdinalIgnoreCase)))
                {
                    staleSeen.Add(seen);
                }
            }
            foreach (var stale in staleSeen) SeenPendingIds.Remove(stale);

            foreach (var item in snapshot.Items)
            {
                if (item.IsPendingApproval && !item.IsDeleted && SeenPendingIds.Add(item.Id))
                {
                    newPending.Add(item);
                }
            }

            // 仅当数据真正变化时才对外发事件，杜绝"刷新 → 事件 → 再刷新"的无限抖动
            var mergedItems = MergeIgnoredItems(snapshot.Items, snapshot.Ignored);
            itemsChanged = ItemsFingerprint(mergedItems) != ItemsFingerprint(_items)
                           || snapshot.PendingCount != _pendingCount
                           || snapshot.BlockedCount != _blockedCount
                           || snapshot.AllowedCount != _allowedCount
                           || snapshot.Ignored.Count != _ignored.Count;
            eventsChanged = EventsFingerprint(snapshot.Events) != EventsFingerprint(_events);

            _items = mergedItems;
            _events = snapshot.Events;
            _ignored = snapshot.Ignored;
            _policies = snapshot.Policies;
            _pendingCount = snapshot.PendingCount;
            _blockedCount = snapshot.BlockedCount;
            _allowedCount = snapshot.AllowedCount;
            _fingerprint = snapshot.StateFingerprint;
        }

        if (itemsChanged) RaiseItemsChanged();
        if (eventsChanged) RaiseEventsChanged();

        foreach (var item in newPending)
        {
            RaisePending(item);
        }
    }

    /// <summary>
    /// 把「已停止追踪」条目合并进拦截列表（IsIgnored=true）。
    /// 运行时新版本已会在快照中返回这些条目；即便如此前台仍做一次合并兜底，
    /// 保证哪怕运行的是旧版运行时，「已停止追踪」筛选/计数/恢复追踪也始终可见可用。
    /// </summary>
    private static List<InterceptItemDto> MergeIgnoredItems(
        IEnumerable<InterceptItemDto> items, IEnumerable<InterceptIgnoredDto> ignored)
    {
        var merged = new List<InterceptItemDto>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
        {
            if (ids.Add(it.Id)) merged.Add(it);
        }
        foreach (var ig in ignored)
        {
            if (!ids.Add(ig.Id)) continue;
            merged.Add(new InterceptItemDto
            {
                Id = ig.Id,
                Name = ig.Name,
                SubKey = ig.SubKey,
                ExePath = ig.ExePath,
                DesiredState = "none",
                IsIgnored = true,
                Source = "ignored",
                UpdatedAtUtc = ig.CreatedUtc,
                Note = ig.Note,
            });
        }
        return merged;
    }

    /// <summary>条目指纹：任一影响展示的字段变化都会改变指纹。</summary>
    private static string ItemsFingerprint(IEnumerable<InterceptItemDto> items)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var i in items.OrderBy(i => i.Id, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(i.Id).Append('|')
              .Append(i.DesiredState).Append('|')
              .Append(i.IsPendingApproval ? '1' : '0').Append('|')
              .Append(i.PendingChangeKind).Append('|')
              .Append(i.IsDeleted ? '1' : '0').Append('|')
              .Append(i.IsBlocked ? '1' : '0').Append('|')
              .Append(i.IsIgnored ? '1' : '0').Append('|')
              .Append(i.IsModernMenu ? '1' : '0').Append('|')
              .Append(i.UpdatedAtUtc).Append('|')
              .Append(i.Name).Append('|')
              .Append(i.ExePath).Append(';');
        }
        return sb.ToString();
    }

    /// <summary>操作记录指纹（倒序列表：数量 + 首末行 Id）。</summary>
    private static string EventsFingerprint(IEnumerable<InterceptEventDto> events)
    {
        var list = events.ToList();
        if (list.Count == 0) return "0";
        return $"{list.Count}:{list[0].RowId}:{list[^1].RowId}";
    }

    private static void RefreshIgnoredOnly()
    {
        _ = Task.Run(async () =>
        {
            var client = GetClient();
            if (client is null) return;
            try
            {
                var snapshot = await client.GetSnapshotAsync().ConfigureAwait(false);
                if (snapshot is not null)
                {
                    MergeSnapshot(snapshot);
                }
            }
            catch
            {
                // 忽略：下次刷新兜底
            }
        });
    }

    private static void OnNotification(InterceptBackendNotification notification)
    {
        // 过滤自己触发的广播（ClientOperationId 去重）
        if (notification.ClientOperationId is Guid opId)
        {
            lock (Sync)
            {
                if (RecentClientOps.Contains(opId)) return;
            }
        }

        switch (notification.Kind)
        {
            case InterceptPipeNotificationKind.ItemDetected:
                if (notification.Item is not null)
                {
                    UpsertItem(notification.Item);
                    if (notification.Item.IsPendingApproval && !notification.Item.IsDeleted)
                    {
                        bool isNew;
                        lock (Sync) isNew = SeenPendingIds.Add(notification.Item.Id);
                        if (isNew) RaisePending(notification.Item);
                    }
                }
                break;

            case InterceptPipeNotificationKind.ItemStateChanged:
                if (notification.Item is not null)
                {
                    UpsertItem(notification.Item);
                    RemovePendingMark(notification.Item.Id);
                }
                break;

            case InterceptPipeNotificationKind.ServiceMessage:
                RaiseAttention(notification.Message);
                break;

            case InterceptPipeNotificationKind.ServiceStopping:
                RaiseConnection(false);
                break;
        }
    }

    private static void UpsertItem(InterceptItemDto item)
    {
        List<InterceptItemDto> replaced;
        lock (Sync)
        {
            var list = new List<InterceptItemDto>(_items);
            var index = list.FindIndex(i => string.Equals(i.Id, item.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                list[index] = item;
            }
            else
            {
                list.Add(item);
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _items = list;
            RecomputeCountsLocked();
            replaced = list;
        }
        RaiseItemsChanged();
    }

    private static void RemoveItemLocal(string itemId)
    {
        List<InterceptItemDto>? changed = null;
        lock (Sync)
        {
            var list = _items.Where(i => !string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (list.Count != _items.Count)
            {
                _items = list;
                RecomputeCountsLocked();
                changed = list;
            }
        }
        if (changed is not null) RaiseItemsChanged();
    }

    private static void RemovePendingMark(string itemId)
    {
        lock (Sync)
        {
            var item = _items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (item is null || item.IsPendingApproval) return;
            SeenPendingIds.Remove(itemId);
        }
    }

    private static void RecomputeCountsLocked()
    {
        _pendingCount = _items.Count(i => i.IsPendingApproval && !i.IsDeleted);
        _blockedCount = _items.Count(i => !i.IsPendingApproval && !i.IsDeleted && i.DesiredState == "blocked");
        _allowedCount = _items.Count(i => !i.IsDeleted && i.DesiredState == "allowed");
    }

    // ================= UI 编组 =================

    private static void RaiseItemsChanged()
    {
        _ui?.TryEnqueue(() => ItemsChanged?.Invoke(null, EventArgs.Empty));
    }

    private static void RaiseEventsChanged()
    {
        _ui?.TryEnqueue(() => EventsChanged?.Invoke(null, EventArgs.Empty));
    }

    private static void RaisePending(InterceptItemDto item)
    {
        _ui?.TryEnqueue(() => PendingApprovalDetected?.Invoke(null, item));
    }

    private static void RaiseAttention(string message)
    {
        _ui?.TryEnqueue(() => ServiceAttention?.Invoke(null, message));
    }

    private static bool _lastReportedConnected;

    private static void RaiseConnection(bool connected)
    {
        bool changed;
        lock (Sync)
        {
            changed = _lastReportedConnected != connected;
            if (changed) _lastReportedConnected = connected;
        }
        if (changed)
        {
            _ui?.TryEnqueue(() => ConnectionChanged?.Invoke(null, connected));
        }
    }
}