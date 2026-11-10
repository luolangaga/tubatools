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
    public void TryRecordPresent_PresentHistoryDominatesAllTiers()
    {
        // PresentHistory (0xAB/0xD7) is the authoritative per-frame source on
        // modern Windows — including fullscreen, where Present (0xB8) fires for
        // the same frame. Counting both would double the FPS, so while history
        // events flow (within a 500ms window), legacy + win32k events are shadowed.
        var tracker = new FpsTracker();
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
        var tracker = new FpsTracker();

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
        var tracker = new FpsTracker();
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
        var tracker = new FpsTracker();
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
        var tracker = new FpsTracker();
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
    public void Tracker_OneSecondWindowIncludesSubMillisecondAndLongFrames()
    {
        var tracker = new FpsTracker();
        long t = TimeSpan.TicksPerSecond;
        tracker.OnPresent(t);
        tracker.OnPresent(t += 5000); // 0.5ms is a valid upstream frame.
        Assert.Equal(2000, tracker.Fps);
        Assert.Equal(2000, tracker.OnePercentLow);
        tracker.OnPresent(t += TimeSpan.TicksPerSecond * 2);
        Assert.Equal(1, tracker.Fps); // 0.5 rounds away from zero.
        Assert.Equal(1, tracker.PointOnePercentLow);
        for (int i = 0; i < 101; i++) tracker.OnPresent(t += 100000);
        Assert.Equal(100, tracker.Fps);
        Assert.Equal(100, tracker.OnePercentLow);
        Assert.Equal(100, tracker.PointOnePercentLow);
    }

    [Fact]
    public void LowUsesFloorTailAndHalfUpRoundingWithoutWarmup()
    {
        var values = Enumerable.Repeat(10d, 148).Concat(new[] { 20d, 100d }).ToList();
        var stats = FrameScopeFrameStatistics.Snapshot(values);
        Assert.Equal(10, stats.Low1); // floor(150 * .01) = one slowest frame.
        Assert.Equal(10, stats.Low01);
        Assert.Equal(3, FrameScopeFrameStatistics.Rounded(2.5));
        Assert.Null(FrameScopeFrameStatistics.Snapshot(new List<double>()).Average);
    }

    [Fact]
    public void ReplayAndTrackerReportUseSamePerSecondFilteredSummary()
    {
        var tracker = new FpsTracker();
        long ticks = TimeSpan.TicksPerSecond;
        tracker.OnPresent(ticks);
        var frames = new List<FrameScopeFrameStatistics.Frame>();
        for (int i = 0; i < 500; i++) {
            double ms = i < 50 ? 50 : 10;
            ticks += (long)(ms * TimeSpan.TicksPerMillisecond);
            tracker.OnPresent(ticks);
            frames.Add(new FrameScopeFrameStatistics.Frame(ticks / (double)TimeSpan.TicksPerMillisecond, ms));
        }
        var expected = FrameScopeFrameStatistics.Replay(frames);
        var actual = tracker.TakeSnapshot("game");
        Assert.Equal(expected.Filtered.Average, actual.AvgFps);
        Assert.Equal(expected.Filtered.Low1, actual.OnePercentLow);
        Assert.Equal(expected.Filtered.Low01, actual.PointOnePercentLow);
        Assert.Equal(500, actual.TotalFrames);
    }

    [Fact]
    public void EtwFallbackDoesNotInventRenderLatency()
    {
        var tracker = new FpsTracker();
        tracker.OnPresent(TimeSpan.TicksPerSecond);
        tracker.OnPresent(TimeSpan.TicksPerSecond + 100000);
        FpsService.ReadFrameMetrics(tracker, DateTime.UtcNow, out var frame, out var latency);
        Assert.Equal(10, frame);
        Assert.Equal(-1, latency);
        FpsService.ReadFrameMetrics(tracker, DateTime.UtcNow.AddSeconds(1.1), out frame, out latency);
        Assert.Equal(-1, frame);
        Assert.Equal(-1, latency);
    }

    [Fact]
    public async Task Tracker_ConcurrentReadsAndPresents_KeepWindowConsistent()
    {
        var tracker = new FpsTracker();
        var writer = Task.Run(() => {
            for (int i = 1; i <= 40000; i++) tracker.OnPresent(i * 100000L);
        });
        var readers = Enumerable.Range(0, 3).Select(_ => Task.Run(() => {
            for (int i = 0; i < 100; i++) {
                double value = tracker.OnePercentLow;
                Assert.True(value == -1 || value == 100);
                value = tracker.PointOnePercentLow;
                Assert.True(value == -1 || value == 100);
            }
        }));
        await Task.WhenAll(readers.Append(writer));
        Assert.Equal(39999, tracker.TotalFrames);
    }
}
