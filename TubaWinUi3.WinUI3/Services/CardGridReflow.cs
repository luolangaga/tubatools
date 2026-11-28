using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace TubaWinUi3.Services;

/// <summary>
/// 工具卡片网格（GridView + ItemsWrapGrid）随窗口宽度重排的调度器：
/// <list type="bullet">
/// <item>连续拖边时把多次请求合并成一次重排（节流 + 尾部对齐），避免每像素重排整张网格；</item>
/// <item>跳过没有实际变化的 ItemWidth 赋值，减少 ItemsWrapGrid 的整表重测；</item>
/// <item>换列等结构性重排时，用合成层的 Translation/Scale 把卡片从旧位置滑到新位置。</item>
/// </list>
/// 动画只改可视层（GetElementVisual），不回写布局，所以不会像 ChildrenTransitions 那样
/// 形成「布局 → 动画 → 再布局」的循环（见 CardBoard 的既有教训，Issue #266）。
/// </summary>
internal static class CardGridReflow
{
    /// <summary>拖拽停止后再补一次精确宽度的时间。</summary>
    private const int TrailingDelayMs = 90;

    private static readonly ConditionalWeakTable<GridView, GridState> States = new();

    private static readonly Lazy<UISettings?> SystemSettings = new(() =>
    {
        try { return new UISettings(); } catch { return null; }
    });

    private sealed class GridState
    {
        public DispatcherQueueTimer? Trailing;
        public bool HasPending;
        public double PendingWidth;
        public long LastApplyMs;
        public long LastMotionMs;
        public bool MotionArmed;
        public int MotionLayoutTicks;
        public double MotionOldWidth;
        public double MotionNewWidth;
        public bool HooksInstalled;
        public readonly List<ItemSnapshot> Snapshots = [];
    }

    private readonly struct ItemSnapshot(
        UIElement container,
        object? content,
        Vector3 offset,
        float width)
    {
        public UIElement Container { get; } = container;
        public object? Content { get; } = content;
        public Vector3 Offset { get; } = offset;
        public float Width { get; } = width;
    }

    /// <summary>
    /// 请求把网格的卡片宽度调整到 <paramref name="targetItemWidth"/>。
    /// 页面只在 SizeChanged 里调用即可，去重 / 节流 / 动画都在这里处理。
    /// </summary>
    public static void Request(GridView grid, double targetItemWidth)
    {
        if (grid?.ItemsPanelRoot is not ItemsWrapGrid panel) return;

        var state = States.GetOrCreateValue(grid);
        InstallHooks(grid, state);

        // 与「已经应用的值」或「已经排队等待的值」相同就不再动作。
        var baseline = state.HasPending ? state.PendingWidth : panel.ItemWidth;
        if (!CardReflowPolicy.NeedsUpdate(baseline, targetItemWidth)) return;

        var now = Environment.TickCount64;
        if (CardReflowPolicy.ShouldDefer(now, state.LastApplyMs) && grid.DispatcherQueue is { } queue)
        {
            state.PendingWidth = targetItemWidth;
            state.HasPending = true;
            state.Trailing ??= CreateTrailingTimer(grid, state, queue);
            state.Trailing.Stop();
            state.Trailing.Start();
            return;
        }

        // 立即应用时要丢弃过期的排队值，否则尾部定时器会把旧宽度写回来。
        state.HasPending = false;
        state.Trailing?.Stop();
        Apply(panel, state, targetItemWidth, now);
    }

    private static DispatcherQueueTimer CreateTrailingTimer(GridView grid, GridState state, DispatcherQueue queue)
    {
        var timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(TrailingDelayMs);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!state.HasPending) return;
            state.HasPending = false;

            if (grid.ItemsPanelRoot is not ItemsWrapGrid panel) return;
            if (!CardReflowPolicy.NeedsUpdate(panel.ItemWidth, state.PendingWidth)) return;
            Apply(panel, state, state.PendingWidth, Environment.TickCount64);
        };
        return timer;
    }

    private static void Apply(ItemsWrapGrid panel, GridState state, double targetWidth, long now)
    {
        var oldWidth = panel.ItemWidth;
        state.MotionArmed = false;
        state.MotionLayoutTicks = 0;
        state.Snapshots.Clear();

        if (MotionEnabled && CardReflowPolicy.ShouldAnimate(now, state.LastApplyMs, state.LastMotionMs, oldWidth, targetWidth))
        {
            CaptureSnapshots(panel, state);
            if (state.Snapshots.Count > 0)
            {
                state.MotionArmed = true;
                state.MotionOldWidth = oldWidth;
                state.MotionNewWidth = targetWidth;
                state.LastMotionMs = now;
            }
        }

        panel.ItemWidth = targetWidth;
        state.LastApplyMs = now;
    }

    private static void CaptureSnapshots(ItemsWrapGrid panel, GridState state)
    {
        var children = panel.Children;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is not UIElement container) continue;

            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(container);
                state.Snapshots.Add(new ItemSnapshot(
                    container,
                    (container as ContentControl)?.Content,
                    visual.Offset,
                    visual.Size.X));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CardReflow] 记录卡片位置失败: {ex.Message}");
            }
        }
    }

    private static void InstallHooks(GridView grid, GridState state)
    {
        if (state.HooksInstalled) return;
        state.HooksInstalled = true;

        // ItemWidth 写入后的真实位置要等这一轮布局结束才拿得到。
        grid.LayoutUpdated += (_, _) => PlayArmedMotion(state);
        grid.Unloaded += (_, _) =>
        {
            state.Trailing?.Stop();
            state.HasPending = false;
            state.MotionArmed = false;
            state.Snapshots.Clear();
        };
    }

    private static bool HasAnyContainerMoved(List<ItemSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            if (snapshot.Container is not FrameworkElement container || container.Parent is null) continue;
            var offset = ElementCompositionPreview.GetElementVisual(container).Offset;
            if (CardReflowPolicy.IsVisibleMove(snapshot.Offset.X - offset.X, snapshot.Offset.Y - offset.Y))
                return true;
        }
        return false;
    }

    private static void PlayArmedMotion(GridState state)
    {
        if (!state.MotionArmed) return;

        // ItemWidth 写入后可能还要再走一轮布局才落到卡片上：位置没动就再等一轮，
        // 最多等两轮，避免某次布局没触发时状态一直挂着。
        state.MotionLayoutTicks++;
        var snapshots = state.Snapshots;
        if (snapshots.Count == 0)
        {
            state.MotionArmed = false;
            state.MotionLayoutTicks = 0;
            return;
        }
        if (state.MotionLayoutTicks < 2 && !HasAnyContainerMoved(snapshots))
            return;

        state.MotionArmed = false;
        state.MotionLayoutTicks = 0;

        try
        {
            var compositor = ElementCompositionPreview.GetElementVisual(snapshots[0].Container).Compositor;
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(.1f, .9f), new Vector2(.2f, 1f));
            var duration = TimeSpan.FromMilliseconds(CardReflowPolicy.ResolveDurationMs(state.MotionOldWidth, state.MotionNewWidth));

            foreach (var snapshot in snapshots)
            {
                if (snapshot.Container is not FrameworkElement container || container.Parent is null) continue;
                // 重排时容器可能被回收给了别的项，位置差就不再属于同一张卡片。
                if (container is ContentControl content && !ReferenceEquals(content.Content, snapshot.Content)) continue;

                var visual = ElementCompositionPreview.GetElementVisual(container);
                var dx = snapshot.Offset.X - visual.Offset.X;
                var dy = snapshot.Offset.Y - visual.Offset.Y;
                var move = CardReflowPolicy.IsVisibleMove(dx, dy);
                var scale = CardReflowPolicy.TryGetStartScale(snapshot.Width, visual.Size.X, out var startScale);
                if (!move && !scale) continue;

                ElementCompositionPreview.SetIsTranslationEnabled(container, true);

                if (move)
                {
                    var slide = compositor.CreateVector3KeyFrameAnimation();
                    slide.Duration = duration;
                    slide.InsertKeyFrame(0f, new Vector3(dx, dy, 0));
                    slide.InsertKeyFrame(1f, Vector3.Zero, easing);
                    visual.StartAnimation("Translation", slide);
                }

                if (scale)
                {
                    visual.CenterPoint = new Vector3(0f, visual.Size.Y / 2f, 0f);
                    var grow = compositor.CreateVector3KeyFrameAnimation();
                    grow.Duration = duration;
                    grow.InsertKeyFrame(0f, new Vector3(startScale, 1f, 1f));
                    grow.InsertKeyFrame(1f, Vector3.One, easing);
                    visual.StartAnimation("Scale", grow);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CardReflow] 重排动画失败: {ex.Message}");
        }
        finally
        {
            snapshots.Clear();
        }
    }

    private static bool MotionEnabled =>
        !FastModeService.IsFastModeEnabled() && (SystemSettings.Value?.AnimationsEnabled ?? true);
}
