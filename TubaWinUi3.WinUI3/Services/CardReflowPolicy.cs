namespace TubaWinUi3.Services;

/// <summary>
/// 工具卡片网格随窗口宽度重排的策略（纯逻辑，可单测）：
/// 去重阈值、连续拖拽的节流、以及什么时候值得播放一次重排动画。
/// 真正的布局与合成动画在 <see cref="CardGridReflow"/> 里，这里只做判定。
/// </summary>
internal static class CardReflowPolicy
{
    /// <summary>小于半个像素的宽度变化不值得重新排布整张网格。</summary>
    public const double WidthEpsilon = 0.5;

    /// <summary>卡片宽度相对变化超过 8% 视为「结构性重排」（多半是列数变了）。</summary>
    public const double StructuralRatio = 0.08;

    /// <summary>宽度变化超过 12% 时不做横向缩放动画（文字会被压扁）。</summary>
    public const double MaxAnimatableScaleDelta = 0.12;

    /// <summary>两次重排之间的最小间隔：拖边时合并成一帧一次，避免每像素重排整张网格。</summary>
    public const int MinApplyIntervalMs = 33;

    /// <summary>距上次重排超过这个时间才认为是「离散变化」（拖动暂停、最大化/还原），值得补一段动画。</summary>
    public const int IdleMotionGapMs = 200;

    /// <summary>上一段重排动画还没播完就不重启动画，避免卡片在动画中途被拽回重来。</summary>
    public const int MotionRestartGuardMs = 320;

    public const int MotionMsStructural = 260;
    public const int MotionMsQuick = 200;

    /// <summary>小于这个距离的位移肉眼看不出，不启动动画。</summary>
    public const double MinMotionDistance = 2.0;

    /// <summary>目标宽度是否值得写进 ItemsWrapGrid（NaN 表示还没设置过）。</summary>
    public static bool NeedsUpdate(double appliedWidth, double targetWidth)
    {
        if (double.IsNaN(targetWidth) || double.IsInfinity(targetWidth) || targetWidth <= 0) return false;
        if (double.IsNaN(appliedWidth) || double.IsInfinity(appliedWidth)) return true;
        return Math.Abs(targetWidth - appliedWidth) >= WidthEpsilon;
    }

    /// <summary>距上次重排不足最小间隔时推迟到尾部定时器，合并连续拖拽产生的多次请求。</summary>
    public static bool ShouldDefer(long nowMs, long lastApplyMs)
        => lastApplyMs != 0 && nowMs - lastApplyMs < MinApplyIntervalMs;

    /// <summary>卡片宽度相对变化够大（通常意味着列数变化）。</summary>
    public static bool IsStructuralChange(double oldWidth, double newWidth)
    {
        if (double.IsNaN(oldWidth) || double.IsNaN(newWidth) || oldWidth <= 0 || newWidth <= 0) return false;
        return Math.Abs(newWidth / oldWidth - 1) >= StructuralRatio;
    }

    /// <summary>
    /// 这次重排是否值得配一段滑入动画：首次布局直接落位；连续拖拽中的小步进直接跳；
    /// 结构性重排（换列）或隔了一段时间的离散变化才播动画。
    /// </summary>
    public static bool ShouldAnimate(long nowMs, long lastApplyMs, long lastMotionMs, double oldWidth, double newWidth)
    {
        if (lastApplyMs == 0) return false;
        if (double.IsNaN(oldWidth) || oldWidth <= 0 || double.IsNaN(newWidth) || newWidth <= 0) return false;
        if (lastMotionMs != 0 && nowMs - lastMotionMs < MotionRestartGuardMs) return false;
        return IsStructuralChange(oldWidth, newWidth) || nowMs - lastApplyMs >= IdleMotionGapMs;
    }

    /// <summary>
    /// 卡片宽度只小幅变化时，可以用「从旧宽度缩放到新宽度」的合成动画补足尺寸过渡；
    /// 变化太大时缩放会把文字压扁，只做位移滑入。
    /// </summary>
    public static bool TryGetStartScale(double oldWidth, double newWidth, out float scale)
    {
        scale = 1f;
        if (double.IsNaN(oldWidth) || double.IsNaN(newWidth) || oldWidth <= 0 || newWidth <= 0) return false;
        var ratio = oldWidth / newWidth;
        if (Math.Abs(ratio - 1) < 0.004) return false;
        if (Math.Abs(ratio - 1) > MaxAnimatableScaleDelta) return false;
        scale = (float)ratio;
        return true;
    }

    public static bool IsVisibleMove(double dx, double dy)
        => dx * dx + dy * dy >= MinMotionDistance * MinMotionDistance;

    public static int ResolveDurationMs(double oldWidth, double newWidth)
        => IsStructuralChange(oldWidth, newWidth) ? MotionMsStructural : MotionMsQuick;
}
