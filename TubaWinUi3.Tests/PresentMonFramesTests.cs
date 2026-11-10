using System.Diagnostics;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public sealed class PresentMonFramesTests
{
    private const string Header = "Application,ProcessID,SwapChainAddress,QPCTime,MsBetweenPresents,MsUntilRenderComplete,MsUntilDisplayed,PresentMode";

    [Fact]
    public void MissingLatencyColumnDoesNotDisableFps()
    {
        var frames = new PresentMonFrames();
        Assert.True(frames.ReadHeader("Application,ProcessID,SwapChainAddress,QPCTime,MsBetweenPresents"));
        long now = Stopwatch.Frequency * 10;
        frames.Add($"Game.exe,10000001,a,{now},10", now);
        Assert.Equal(new PresentMonFrames.Metrics(100, 100, 100, 10, -1), frames.GetMetrics(10000001, now));
    }

    [Fact]
    public void HardwareTrackSelectionKeepsTimingAndLatencyOnSameStream()
    {
        var frames = new PresentMonFrames();
        Assert.True(frames.ReadHeader(Header));
        long now = Stopwatch.Frequency * 10;
        const int pid = 10000001;
        frames.Add($"Game.exe,{pid},a,{now - 20},10,3,40,Hardware: Independent Flip", now);
        frames.Add($"Game.exe,{pid},a,{now - 10},100,70,120,Composed: Flip", now);
        frames.Add($"Game.exe,{pid},b,{now},200,10,240,Composed: Flip", now);
        Assert.Equal(new PresentMonFrames.Metrics(100, 100, 100, 10, 3), frames.GetMetrics(pid, now));
        var report = Assert.Single(frames.GetSnapshots()).Snapshot;
        // Upstream's default report filter can exclude a single short sample.
        var expected = FrameScopeFrameStatistics.Replay(new List<FrameScopeFrameStatistics.Frame> { new(0, 10) });
        Assert.Equal(expected.Filtered.Average ?? 0, report.AvgFps);
        Assert.Equal(expected.Filtered.Low1 ?? -1, report.OnePercentLow);
    }

    [Fact]
    public void OneSecondBoundaryIsExclusiveAndUnavailableLatencyStaysMissing()
    {
        var frames = new PresentMonFrames();
        Assert.True(frames.ReadHeader(Header));
        long now = Stopwatch.Frequency * 10;
        const int pid = 10000001;
        frames.Add($"Game.exe,{pid},a,{now - Stopwatch.Frequency},500,5,40,Hardware: Independent Flip", now - Stopwatch.Frequency);
        frames.Add($"Game.exe,{pid},a,{now},10,NA,40,Hardware: Independent Flip", now);
        Assert.Equal(new PresentMonFrames.Metrics(100, 100, 100, 10, -1), frames.GetMetrics(pid, now));
        Assert.Null(frames.GetMetrics(pid, now + Stopwatch.Frequency));
    }

    [Fact]
    public void PresentMon26CsvReportsSelectedProcessAndExpiresWhenFramesStop()
    {
        var frames = new PresentMonFrames();
        // PresentMon 2.6.0's v1 console format, including the legacy QPC suffix.
        Assert.True(frames.ReadHeader("Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped," +
            "TimeInSeconds,msInPresentAPI,msBetweenPresents,AllowsTearing,PresentMode,msUntilRenderComplete," +
            "msUntilDisplayed,msBetweenDisplayChange,msFlipDelay,QPCTime"));

        const int game = 10000001;
        long now = Stopwatch.Frequency * 10;
        frames.Add($"Game,Test.exe,{game},0x123,DXGI,1,0,0,9.96,0,16,0,Hardware: Independent Flip,2,24,16,0,{now - Stopwatch.Frequency / 25}", now);
        frames.Add($"Game,Test.exe,{game},0x123,DXGI,1,0,1,10,0,32,0,Hardware: Independent Flip,2,0,32,0,{now}", now);
        frames.Add($"Other.exe,{game + 1},0x123,DXGI,1,0,0,10,0,4,0,Hardware: Independent Flip,2,5,4,0,{now}", now);

        Assert.Equal(new PresentMonFrames.Metrics(42, 31, 31, 32, 2), frames.GetMetrics(game, now));
        Assert.Equal(250, frames.GetMetrics(game + 1, now)!.Value.Fps);
        Assert.Null(frames.GetMetrics(game, now + Stopwatch.Frequency));
    }
}
