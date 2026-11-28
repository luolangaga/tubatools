using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// 卡片网格重排策略：宽度去重、拖拽节流、以及「什么时候值得播一段滑入动画」。
/// 这些判定决定拖边时会不会逐像素重排整张网格，也决定动画会不会在拖拽途中被反复重启。
/// </summary>
public class CardReflowPolicyTests
{
    [Theory]
    [InlineData(double.NaN, 300, true)]      // 首次布局（ItemWidth 还是 NaN）必须写入
    [InlineData(300, 300.2, false)]          // 亚像素抖动不重排
    [InlineData(300, 300.6, true)]
    [InlineData(300, 299.4, true)]
    [InlineData(300, double.NaN, false)]     // 非法目标值忽略
    [InlineData(300, 0, false)]
    [InlineData(300, -10, false)]
    public void NeedsUpdate_OnlyForRealChanges(double applied, double target, bool expected)
    {
        Assert.Equal(expected, CardReflowPolicy.NeedsUpdate(applied, target));
    }

    [Theory]
    [InlineData(1000, 0, false)]             // 还没有应用过：立刻落位，不排队
    [InlineData(1032, 1000, true)]           // 间隔不足最小重排间隔：合并到尾部定时器
    [InlineData(1033, 1000, false)]          // 到达最小间隔：立即重排
    [InlineData(1200, 1000, false)]
    public void ShouldDefer_ThrottlesContinuousResize(long now, long lastApply, bool expected)
    {
        Assert.Equal(expected, CardReflowPolicy.ShouldDefer(now, lastApply));
    }

    [Theory]
    [InlineData(300, 390, true)]             // 列数变化带来的宽度跳变
    [InlineData(300, 320, false)]            // 同一列数下的细微变宽
    [InlineData(390, 300, true)]
    [InlineData(0, 300, false)]
    [InlineData(300, 0, false)]
    [InlineData(double.NaN, 300, false)]
    public void IsStructuralChange_DetectsColumnChanges(double oldWidth, double newWidth, bool expected)
    {
        Assert.Equal(expected, CardReflowPolicy.IsStructuralChange(oldWidth, newWidth));
    }

    [Fact]
    public void ShouldAnimate_FirstLayoutNeverAnimates()
    {
        Assert.False(CardReflowPolicy.ShouldAnimate(nowMs: 10_000, lastApplyMs: 0, lastMotionMs: 0, oldWidth: 300, newWidth: 390));
    }

    [Fact]
    public void ShouldAnimate_StructuralChangeAnimatesOnceIdleEnough()
    {
        Assert.True(CardReflowPolicy.ShouldAnimate(nowMs: 10_000, lastApplyMs: 9_950, lastMotionMs: 0, oldWidth: 300, newWidth: 390));
    }

    [Fact]
    public void ShouldAnimate_SmallChangeDuringFastDragSnaps()
    {
        // 40ms 前刚重排过、宽度只差 5px：直接跳，避免每个拖拽步进都跑动画
        Assert.False(CardReflowPolicy.ShouldAnimate(nowMs: 10_000, lastApplyMs: 9_960, lastMotionMs: 0, oldWidth: 300, newWidth: 305));
    }

    [Fact]
    public void ShouldAnimate_IdleSmallChangeAnimates()
    {
        // 拖动暂停后再变化：值得补一段动画
        Assert.True(CardReflowPolicy.ShouldAnimate(nowMs: 10_000, lastApplyMs: 9_000, lastMotionMs: 0, oldWidth: 300, newWidth: 305));
    }

    [Fact]
    public void ShouldAnimate_DoesNotRestartWhilePreviousMotionRuns()
    {
        // 上一段动画刚开始 100ms，即便发生结构性变化也不重启（避免卡片被拽回起点）
        Assert.False(CardReflowPolicy.ShouldAnimate(nowMs: 10_000, lastApplyMs: 8_000, lastMotionMs: 9_900, oldWidth: 300, newWidth: 390));
        // 动画结束之后（超过保护窗口）才允许再次动画
        Assert.True(CardReflowPolicy.ShouldAnimate(nowMs: 10_000, lastApplyMs: 8_000, lastMotionMs: 9_600, oldWidth: 300, newWidth: 390));
    }

    [Theory]
    [InlineData(300, 310, true)]             // 小幅变宽：用缩放补足尺寸过渡
    [InlineData(310, 300, true)]
    [InlineData(300, 300.5, false)]          // 差异太小，看不出来
    [InlineData(300, 400, false)]            // 换列级别的大变化：缩放会压扁文字，只做位移
    [InlineData(0, 300, false)]
    public void TryGetStartScale_OnlyForSmallWidthDeltas(double oldWidth, double newWidth, bool expected)
    {
        Assert.Equal(expected, CardReflowPolicy.TryGetStartScale(oldWidth, newWidth, out var scale));
        Assert.True(scale > 0);
    }

    [Fact]
    public void ResolveDurationMs_UsesLongerTimingForStructuralReflow()
    {
        Assert.Equal(CardReflowPolicy.MotionMsStructural, CardReflowPolicy.ResolveDurationMs(300, 390));
        Assert.Equal(CardReflowPolicy.MotionMsQuick, CardReflowPolicy.ResolveDurationMs(300, 310));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1.5, 0, false)]
    [InlineData(2, 0, true)]
    [InlineData(-3, 1, true)]
    public void IsVisibleMove_IgnoresSubPixelDrift(double dx, double dy, bool expected)
    {
        Assert.Equal(expected, CardReflowPolicy.IsVisibleMove(dx, dy));
    }
}
