using TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

/// <summary>
/// 监视器对某一条目应采取的动作（移植自 ContextMenuMgr 的 ItemMonitorAction）。
/// </summary>
public enum ItemMonitorAction
{
    /// <summary>无动作：静默吸收进已知基线。</summary>
    None,

    /// <summary>
    /// 条目偏离了明确的"保持拦截"策略。无需用户审核或通知，自动重新拦截。
    /// 第三方反复把已屏蔽项改回启用时，本动作保证策略权威且不打扰用户
    /// （这正是旧实现"放行后又被打回"缺陷的根治点）。
    /// </summary>
    ReconcileDisabledState,

    /// <summary>运行期：全新未知条目出现。拦截（隔离）并挂起审核。</summary>
    QuarantineAdded,

    /// <summary>运行期：之前被删除的条目重现。拦截（隔离）并挂起审核。</summary>
    QuarantineReappeared,

    /// <summary>启动期：监视器未运行期间出现的新条目。仅高亮展示，不拦截不通知。</summary>
    OfflineAddedHighlight,

    /// <summary>启动期：之前被删除的条目重现。仅高亮展示一致性告警，不拦截不通知。</summary>
    OfflineReappearedHighlight,

    /// <summary>已知条目仅元数据变化（命令/路径/名称等）。仅 Modified 高亮，不做任何操作。</summary>
    MetadataModifiedHighlight,
}

/// <summary>
/// 纯函数式的外部变更分类状态机（移植自 ContextMenuMgr 的 ContextMenuChangeClassifier）。
/// 不触碰注册表/状态库；"是否被屏蔽"由调用方以 <paramref name="isBlocked"/> 传入，
/// 便于单测与审计。
/// </summary>
public static class ChangeClassifier
{
    /// <summary>计算当前注册表现存条目的变更类型。</summary>
    public static InterceptChangeKind GetDetectedChangeKind(
        ContextMenuItem item,
        bool isBlocked,
        InterceptStateEntry? state,
        bool hasBaseline)
    {
        if (state is null)
        {
            return hasBaseline ? InterceptChangeKind.Added : InterceptChangeKind.None;
        }

        if (state.IsDeleted)
        {
            return InterceptChangeKind.Reappeared;
        }

        return HasObservedChange(item, isBlocked, state)
            ? InterceptChangeKind.Modified
            : InterceptChangeKind.None;
    }

    /// <summary>人类可读的变更说明。</summary>
    public static string? GetDetectedChangeDetails(
        ContextMenuItem item,
        InterceptChangeKind changeKind)
    {
        return changeKind switch
        {
            InterceptChangeKind.Added => "此项为新增项（上次保存的快照中不存在）。",
            InterceptChangeKind.Reappeared => "此项之前已通过本程序删除，现已重新出现在注册表中。",
            InterceptChangeKind.Modified => "此项在程序外被修改。",
            _ => null,
        };
    }

    /// <summary>一致性提示：期望与现状不符时返回说明，一致返回 null。</summary>
    public static string? GetConsistencyIssue(ContextMenuItem item, bool isBlocked, InterceptStateEntry? state)
    {
        if (state is null || !item.Writable) return null;

        if (state.IsDeleted)
        {
            return "此项已通过本程序删除，但当前又出现在注册表中。";
        }

        if (state.DesiredState == DesiredState.Blocked && !isBlocked)
        {
            return "期望为“保持拦截”，但注册表现状为未拦截。";
        }

        if (state.DesiredState == DesiredState.Allowed && isBlocked)
        {
            return "期望为“放行”，但注册表现状为已拦截。";
        }

        return null;
    }

    /// <summary>明确的"保持拦截"策略是否必须在注册表项暂时缺失时保留（不得被清理清除）。</summary>
    public static bool ShouldPreserveExplicitBlockedState(InterceptStateEntry state)
    {
        return !state.IsDeleted && state.DesiredState == DesiredState.Blocked;
    }

    /// <summary>实际条目是否偏离了明确的"保持拦截"策略，需要无审核自动重新拦截。</summary>
    public static bool ShouldReconcileBlockedState(ContextMenuItem item, bool isBlocked, InterceptStateEntry? state)
    {
        return state is not null
               && item.Writable
               && !state.IsDeleted
               && state.DesiredState == DesiredState.Blocked
               && !isBlocked
               && !state.IsPendingApproval;
    }

    /// <summary>条目的任何观测元数据/屏蔽状态相对持久化状态的差异。</summary>
    public static bool HasObservedChange(ContextMenuItem item, bool isBlocked, InterceptStateEntry state)
    {
        return HasExternalEnabledStateChange(item, isBlocked, state)
               || !string.Equals(state.Name, item.Name, StringComparison.Ordinal)
               || !string.Equals(state.Command, item.Command, StringComparison.Ordinal)
               || !string.Equals(state.Clsid, item.Clsid, StringComparison.OrdinalIgnoreCase)
               || !string.Equals(state.ExePath, item.ExePath, StringComparison.OrdinalIgnoreCase)
               || state.Kind != item.Kind;
    }

    public static bool HasExternalEnabledStateChange(ContextMenuItem item, bool isBlocked, InterceptStateEntry state)
    {
        if (!item.Writable) return false;

        if (state.DesiredState == DesiredState.Blocked)
        {
            return !isBlocked;
        }
        if (state.DesiredState == DesiredState.Allowed)
        {
            return isBlocked;
        }
        return false;
    }

    /// <summary>
    /// 分类监视器对现存条目应执行的动作（外部变更状态机的唯一入口）。
    ///
    /// 判定矩阵（与 ContextMenuMgr 一致）：
    /// - ShouldReconcileBlockedState      -> ReconcileDisabledState（自动重拦，无需审核）
    /// - 已挂起审核的条目                  -> None（不重复拦截/通知）
    /// - state.IsDeleted                   -> QuarantineReappeared（运行期）/ OfflineReappearedHighlight（启动期）
    /// - state 为空 + hasBaseline          -> QuarantineAdded（运行期）/ OfflineAddedHighlight（启动期）
    /// - state 为空 + !hasBaseline         -> None（首次运行，整体吸收为基线）
    /// - HasObservedChange                 -> MetadataModifiedHighlight
    /// - 其余                              -> None
    /// </summary>
    public static ItemMonitorAction ClassifyItemMonitorAction(
        ContextMenuItem item,
        bool isBlocked,
        InterceptStateEntry? state,
        bool hasBaseline,
        bool isBaselineEstablishment)
    {
        if (ShouldReconcileBlockedState(item, isBlocked, state))
        {
            return ItemMonitorAction.ReconcileDisabledState;
        }

        if (state is not null && state.IsPendingApproval)
        {
            return ItemMonitorAction.None;
        }

        if (state is not null && state.IsDeleted)
        {
            return isBaselineEstablishment
                ? ItemMonitorAction.OfflineReappearedHighlight
                : ItemMonitorAction.QuarantineReappeared;
        }

        if (state is null)
        {
            if (!hasBaseline)
            {
                return ItemMonitorAction.None;
            }

            return isBaselineEstablishment
                ? ItemMonitorAction.OfflineAddedHighlight
                : ItemMonitorAction.QuarantineAdded;
        }

        if (HasObservedChange(item, isBlocked, state))
        {
            return ItemMonitorAction.MetadataModifiedHighlight;
        }

        return ItemMonitorAction.None;
    }
}
