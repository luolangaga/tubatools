using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class FpsServiceTests
{
    [Fact]
    public void IsPresentEventId_CoversAllPresentModes()
    {
        // Legacy/fullscreen Present (0xB8)
        Assert.True(FpsService.IsPresentEventId(0x00B8));
        // Modern PresentHistory family (windowed & fullscreen on WDDM 2.x)
        Assert.True(FpsService.IsPresentEventId(0x00AB));
        Assert.True(FpsService.IsPresentEventId(0x00D7));
        // MPO blt/flip family (borderless windowed without present history)
        Assert.True(FpsService.IsPresentEventId(0x00A6));
        Assert.True(FpsService.IsPresentEventId(0x0074));
        Assert.True(FpsService.IsPresentEventId(0x0103));
        Assert.True(FpsService.IsPresentEventId(0x0182));
        // Hardware flip family
        Assert.True(FpsService.IsPresentEventId(0x00A8));
        Assert.True(FpsService.IsPresentEventId(0x00FC));
        Assert.True(FpsService.IsPresentEventId(0x010A));
        // Win32k composed-present (windowed / no-MPO path)
        Assert.True(FpsService.IsPresentEventId(0x00C9));

        // Unrelated DxgKrnl events / junk must not be counted
        Assert.False(FpsService.IsPresentEventId(0x00B1)); // DmaPacket
        Assert.False(FpsService.IsPresentEventId(0x00C1)); // VSyncDPC
        Assert.False(FpsService.IsPresentEventId(0x0011)); // VSyncDPC_Info
        Assert.False(FpsService.IsPresentEventId(0x0000));
    }

    [Fact]
    public void Tracker_Regular60Hz_ReadsNear60Fps()
    {
        var tracker = new FpsService.FpsTracker();
        long frameTicks = TimeSpan.TicksPerSecond / 60; // 60Hz
        for (int i = 1; i <= 300; i++)
            tracker.OnPresent(i * frameTicks);

        Assert.InRange(tracker.Fps, 55, 65);
        // 300 presents → 299 frame intervals (the first present is the baseline)
        Assert.Equal(299, tracker.TotalFrames);
    }

    [Fact]
    public void Tracker_SparseOneHzProcess_ReadsOneFps()
    {
        // A desktop process presenting ~1x/sec used to be picked up by the
        // "first tracker with FPS > 0" fallback → the old stuck-at-1 symptom.
        var tracker = new FpsService.FpsTracker();
        long secondTicks = TimeSpan.TicksPerSecond;
        for (int i = 1; i <= 10; i++)
            tracker.OnPresent(i * secondTicks);

        Assert.Equal(1.0, tracker.Fps, 3);
        Assert.Equal(9, tracker.TotalFrames); // gaps between events
    }

    [Fact]
    public void Tracker_StalePresents_DecaysToZero()
    {
        // When presents stop (menu, loading, dead ETW session) the readout must
        // zero out quickly instead of lingering on the last value for minutes.
        var tracker = new FpsService.FpsTracker();
        long frameTicks = TimeSpan.TicksPerSecond / 60;
        for (int i = 1; i <= 120; i++)
            tracker.OnPresent(i * frameTicks);
        Assert.True(tracker.Fps > 30);

        var now = DateTime.UtcNow;
        tracker.Decay(now);                    // fresh — stays alive
        Assert.True(tracker.Fps > 30);

        tracker.Decay(now.AddSeconds(5));      // stale — zeroed
        Assert.Equal(0, tracker.Fps, 3);
    }

    [Fact]
    public void Tracker_PercentileLow_UsesAverageFrameTimeOfWorstFrames()
    {
        // 1000 帧里混入 10% 的 33.3ms 帧（30 FPS），其余 60 FPS。
        // 1% low 应 ≈ 最差 1% 帧的平均帧时间 → 30 FPS（旧实现取瞬时 FPS 平均会偏高）。
        var tracker = new FpsService.FpsTracker();
        long fast = TimeSpan.TicksPerSecond / 60;   // 16.67ms @60FPS
        long slow = TimeSpan.TicksPerSecond / 30;   // 33.33ms @30FPS
        long t = 0;
        for (int i = 0; i < 1000; i++)
        {
            t += (i % 10 == 0) ? slow : fast;       // 每 10 帧一卡
            tracker.OnPresent(t);
        }
        Assert.True(tracker.TotalFrames >= 900);
        Assert.InRange(tracker.OnePercentLow, 28, 32);
    }

    [Fact]
    public void Tracker_PercentileLow_InsufficientSamples_ReturnsMinusOne()
    {
        // 样本不足时返回 -1（上层显示 "--"），而不是拿 1-2 帧噪声填数字
        var tracker = new FpsService.FpsTracker();
        long frameTicks = TimeSpan.TicksPerSecond / 60;
        for (int i = 1; i <= 50; i++)
            tracker.OnPresent(i * frameTicks);

        Assert.Equal(-1, tracker.OnePercentLow);
        Assert.Equal(-1, tracker.PointOnePercentLow);
    }

    [Fact]
    public void Tracker_SubMillisecondFakeFrames_AreExcludedFromStats()
    {
        // 双源重复事件会产生 0.1ms 的假帧 —— 必须被帧时间下限过滤，否则
        // Avg/Max/1%low 全被污染。
        var tracker = new FpsService.FpsTracker();
        long frameTicks = TimeSpan.TicksPerSecond / 60;
        long t = 0;
        for (int i = 0; i < 120; i++)
        {
            t += frameTicks;                 // 真实帧
            tracker.OnPresent(t);
            tracker.OnPresent(t + 1_000);    // 0.1ms 假帧（同帧重复）
        }

        Assert.Equal(119, tracker.TotalFrames);   // 只有真实帧进入统计
        Assert.InRange(tracker.AvgFps, 55, 65);
        Assert.InRange(tracker.MaxFps, 55, 65);
    }

    [Fact]
    public void Tracker_AvgFps_IsTotalFramesOverTotalTime()
    {
        var tracker = new FpsService.FpsTracker();
        long fast = TimeSpan.TicksPerSecond / 60;
        long slow = TimeSpan.TicksPerSecond / 30;
        long t = 0;
        for (int i = 0; i < 310; i++)
        {
            t += (i % 31 == 0) ? slow : fast;   // ~10 个卡顿帧
            tracker.OnPresent(t);
        }
        Assert.InRange(tracker.AvgFps, 55, 62);
    }

    [Fact]
    public void TryRecordPresent_PresentHistoryDominatesAllTiers()
    {
        // PresentHistory (0xAB/0xD7) is the authoritative per-frame source on
        // modern Windows — including fullscreen, where Present (0xB8) fires for
        // the same frame. Counting both would double the FPS, so while history
        // events flow (within a 500ms window), legacy + win32k events are shadowed.
        var tracker = new FpsService.FpsTracker();
        int counted = 0;

        // history event at t=10s
        Assert.True(FpsService.TryRecordPresent(tracker, 0x00AB, 100_000_000));
        counted++;
        Assert.Equal(100_000_000, tracker.LastHistoryTicks);

        // 5ms later a legacy Present + a win32k event for the same frame — both shadowed
        Assert.False(FpsService.TryRecordPresent(tracker, 0x00B8, 100_050_000));
        Assert.False(FpsService.TryRecordPresent(tracker, 0x00C9, 100_050_500));
        Assert.Equal(100_000_000, tracker.LastPresentTicks);

        // next real frame via history counts again
        Assert.True(FpsService.TryRecordPresent(tracker, 0x00D7, 200_000_000));
        counted++;
        Assert.Equal(2, counted);
    }

    [Fact]
    public void TryRecordPresent_PresentHistoryExpires_ThenLowerTierCounts()
    {
        var tracker = new FpsService.FpsTracker();

        // history at t=0
        Assert.True(FpsService.TryRecordPresent(tracker, 0x00AB, 10_000_000));
        // 300ms later win32k still shadowed (within 500ms mode window)
        Assert.False(FpsService.TryRecordPresent(tracker, 0x00C9, 13_000_000));
        // after the mode window expires, win32k becomes the per-frame source
        Assert.True(FpsService.TryRecordPresent(tracker, 0x00C9, 20_000_000));
        Assert.True(FpsService.TryRecordPresent(tracker, 0x00C9, 21_000_000));
        Assert.Equal(3, tracker.TotalFrames + 1); // baseline + intervals
    }

    [Fact]
    public void TryRecordPresent_Win32kTracksCompositedFrames_AndShadowsLegacy()
    {
        // Borderless-windowed games (MPO off, no present history) present via
        // DWM composition — the per-frame signal is the Win32k composition
        // surface event (0xC9), NOT the DxgKrnl legacy events. While win32k
        // events flow, stray legacy presents must not add duplicate frames.
        var tracker = new FpsService.FpsTracker();
        int counted = 0;

        Assert.True(FpsService.TryRecordPresent(tracker, 0x00C9, 100_000_000)); counted++;
        Assert.False(FpsService.TryRecordPresent(tracker, 0x00B8, 100_050_000)); // stray legacy, same frame
        Assert.True(FpsService.TryRecordPresent(tracker, 0x00C9, 116_700_000)); counted++;   // 60Hz
        Assert.False(FpsService.TryRecordPresent(tracker, 0x00A6, 116_750_000)); // MPO blt, same frame
        Assert.True(FpsService.TryRecordPresent(tracker, 0x00C9, 133_300_000)); counted++;
        Assert.Equal(3, counted);
        Assert.Equal(2, tracker.TotalFrames); // 3 presents → 2 intervals
    }

    [Fact]
    public void TryRecordPresent_FullscreenNoHistory_UsesLegacyEvents()
    {
        // Fullscreen exclusive on a system without present history: only the
        // legacy kernel events fire; win32k never appears.
        var tracker = new FpsService.FpsTracker();
        int counted = 0;

        Assert.True(FpsService.TryRecordPresent(tracker, 0x00B8, 100_000_000)); counted++;
        Assert.True(FpsService.TryRecordPresent(tracker, 0x00B8, 116_700_000)); counted++;
        Assert.True(FpsService.TryRecordPresent(tracker, 0x0074, 133_300_000));  counted++;
        Assert.Equal(3, counted);
    }

    [Fact]
    public void TryRecordPresent_MpoEventsWithoutHistory_CountOncePerFrame()
    {
        // MPO blt + flip events for the same frame arrive microseconds apart;
        // only the first one within the dedup window may be counted.
        var tracker = new FpsService.FpsTracker();
        int counted = 0;

        Assert.True(FpsService.TryRecordPresent(tracker, 0x00B8, 100_000_000));
        counted++;
        Assert.False(FpsService.TryRecordPresent(tracker, 0x00A6, 100_000_500));  // same frame
        Assert.False(FpsService.TryRecordPresent(tracker, 0x0074, 100_001_000));  // same frame
        Assert.True(FpsService.TryRecordPresent(tracker, 0x0074, 102_000_000));   // next frame
        counted++;
        Assert.Equal(2, counted);
        Assert.Equal(1, tracker.TotalFrames); // 2 presents → 1 interval
    }

    [Fact]
    public void Tracker_FrameTime_ReflectsLatestValidInterval()
    {
        // 帧生成时间 = 最近一次有效帧间隔（60 FPS → ≈16.7ms）
        var tracker = new FpsService.FpsTracker();
        long frameTicks = TimeSpan.TicksPerSecond / 60;
        for (int i = 1; i <= 120; i++)
            tracker.OnPresent(i * frameTicks);

        Assert.InRange(tracker.LastFrameTimeMs, 16, 17);
    }

    [Fact]
    public void Tracker_FrameTime_IgnoresSubMillisecondFakeFrames()
    {
        // 0.1ms 假帧不更新帧时间读数 —— 否则卡顿监测会被双源重复事件污染
        var tracker = new FpsService.FpsTracker();
        long frameTicks = TimeSpan.TicksPerSecond / 60;
        long t = 0;
        for (int i = 0; i < 60; i++)
        {
            t += frameTicks;
            tracker.OnPresent(t);
            tracker.OnPresent(t + 1_000); // 0.1ms 假帧
        }

        Assert.InRange(tracker.LastFrameTimeMs, 16, 17);
    }

    [Fact]
    public void ReadFrameMetrics_FrameTime_ExpiresAfterTwoSeconds()
    {
        // 无新帧 2s 后帧时间读数过期（与 FPS 过期口径一致）→ -1，覆盖层显示 "--"
        var tracker = new FpsService.FpsTracker();
        long frameTicks = TimeSpan.TicksPerSecond / 60;
        for (int i = 1; i <= 60; i++)
            tracker.OnPresent(i * frameTicks);

        var now = DateTime.UtcNow;
        FpsService.ReadFrameMetrics(tracker, now, out var freshMs, out _);
        Assert.InRange(freshMs, 16, 17);
        FpsService.ReadFrameMetrics(tracker, now.AddSeconds(5), out var staleMs, out _);
        Assert.Equal(-1, staleMs);
    }

    [Fact]
    public void RenderLatency_PairsComposeWithPendingSubmit()
    {
        // 提交 T → DWM 合成(0xC9) T+12ms → 渲染延迟 ≈ 12ms
        var tracker = new FpsService.FpsTracker();
        long submit = 100_000_000;
        tracker.EnqueueSubmit(submit);
        tracker.TryRecordComposed(submit + TimeSpan.TicksPerMillisecond * 12);
        Assert.InRange(tracker.LastRenderLatencyMs, 11.9, 12.1);

        // 无待配对提交的合成事件（桌面闪烁等）→ 忽略，保留上一读数
        double before = tracker.LastRenderLatencyMs;
        tracker.TryRecordComposed(submit + 100_000);
        Assert.Equal(before, tracker.LastRenderLatencyMs);
    }

    [Fact]
    public void RenderLatency_ComposeWithoutSubmit_NeverSamples()
    {
        var tracker = new FpsService.FpsTracker();
        tracker.TryRecordComposed(100_000_000);
        Assert.Equal(-1, tracker.LastRenderLatencyMs);
    }

    [Fact]
    public void RenderLatency_OutOfWindowPair_Discarded()
    {
        // 切出/停顿后的陈旧配对（Δ 超 1000ms）不记为有效样本
        var tracker = new FpsService.FpsTracker();
        long submit = 100_000_000;
        tracker.EnqueueSubmit(submit);
        tracker.TryRecordComposed(submit + TimeSpan.TicksPerSecond * 5);
        Assert.Equal(-1, tracker.LastRenderLatencyMs);
    }

    [Fact]
    public void RenderLatency_MultipleFrames_StrictFifo()
    {
        // 多帧排队：合成事件必须按提交顺序配对
        var tracker = new FpsService.FpsTracker();
        long f1 = 100_000_000;
        long f2 = f1 + TimeSpan.TicksPerSecond / 60; // 帧2在帧1后 16.7ms 提交
        tracker.EnqueueSubmit(f1);
        tracker.EnqueueSubmit(f2);
        tracker.TryRecordComposed(f1 + TimeSpan.TicksPerMillisecond * 8);   // 帧1: Δ8ms
        Assert.InRange(tracker.LastRenderLatencyMs, 7.9, 8.1);
        tracker.TryRecordComposed(f2 + TimeSpan.TicksPerMillisecond * 12);  // 帧2: Δ12ms
        Assert.InRange(tracker.LastRenderLatencyMs, 11.9, 12.1);
    }

    [Fact]
    public void ReadFrameMetrics_RenderLatency_ExpiresAfterThreeSeconds()
    {
        var tracker = new FpsService.FpsTracker();
        long submit = 100_000_000;
        tracker.EnqueueSubmit(submit);
        tracker.TryRecordComposed(submit + TimeSpan.TicksPerMillisecond * 12);

        var now = DateTime.UtcNow;
        FpsService.ReadFrameMetrics(tracker, now, out _, out var freshMs);
        Assert.InRange(freshMs, 11.9, 12.1);
        FpsService.ReadFrameMetrics(tracker, now.AddSeconds(5), out _, out var staleMs);
        Assert.Equal(-1, staleMs);
    }

    [Fact]
    public void Tracker_PercentileLow_RollsOldFramesOutOfWindow()
    {
        // 滚动窗口语义：启动/加载期的慢帧必须随窗口滚动自然退出，
        // 1% low 反映「当前画面」而不是整个会话的累计。
        var tracker = new FpsService.FpsTracker();
        long fast = TimeSpan.TicksPerSecond / 60;   // 16.67ms @60FPS
        long slow = TimeSpan.TicksPerSecond / 30;   // 33.33ms @30FPS
        long t = 0;

        for (int i = 0; i < 5000; i++) { t += fast; tracker.OnPresent(t); }
        Assert.InRange(tracker.OnePercentLow, 55, 65);      // 纯 60fps

        for (int i = 0; i < 100; i++) { t += slow; tracker.OnPresent(t); }
        Assert.InRange(tracker.OnePercentLow, 28, 32);      // 慢帧进了窗口 → 1% low 掉到 ~30

        for (int i = 0; i < 3000; i++) { t += fast; tracker.OnPresent(t); }
        Assert.InRange(tracker.OnePercentLow, 55, 65);      // 慢帧滚出窗口 → 恢复 ~60
    }

    [Fact]
    public void Tracker_PointOnePercentLow_RequiresThousandFrames()
    {
        // 0.1% low 必须有 1000 帧以上才有统计意义 —— 样本不足必须返回 -1，
        // 而不是拿 3 帧最差帧的噪声填数字。
        var tracker = new FpsService.FpsTracker();
        long frameTicks = TimeSpan.TicksPerSecond / 60;
        long t = 0;

        for (int i = 0; i < 500; i++) { t += frameTicks; tracker.OnPresent(t); }
        Assert.Equal(-1, tracker.PointOnePercentLow);

        for (int i = 0; i < 1000; i++) { t += frameTicks; tracker.OnPresent(t); }
        Assert.InRange(tracker.PointOnePercentLow, 55, 65);
    }
}