using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class FurMarkBenchmarkLogTests
{
    private const string Complete = """
        (21:48:12:307) [ Demo Quick Stats ]
        (21:48:12:307) - SCORE                : 3900
        (21:48:12:307) - duration             : 60013 ms
        (21:48:12:307) - FPS (min/avg/max)    : 59 / 65 / 67
        (21:48:12:748) FurMark (GeeXLab engine) shutdown ok.
        """;

    [Fact]
    public void NormalAutomaticExit_HasValidScore()
    {
        Assert.True(FurMarkBenchmarkLog.TryParse(Complete, 60000, out var result));
        Assert.Equal(3900, result.FurMarkScore);
        Assert.Equal(65, result.AvgFps);
    }

    [Fact]
    public void ScoreWrittenBeforeFps_IsNotYetComplete()
        => Assert.False(FurMarkBenchmarkLog.TryParse(Complete[..Complete.IndexOf("- FPS")], 60000, out _));

    [Fact]
    public void EarlyClose_WithScore_IsRejected()
        => Assert.False(FurMarkBenchmarkLog.TryParse(Complete.Replace("60013 ms", "10013 ms"), 60000, out _));

    [Fact]
    public void LaterPartialSummary_DoesNotReuseEarlierScore()
        => Assert.False(FurMarkBenchmarkLog.TryParse(Complete + "\n[ Demo Quick Stats ]\n- SCORE : 20", 60000, out _));

    [Theory]
    [InlineData("0 / 0 / 0")]
    [InlineData("67 / 65 / 59")]
    [InlineData("not available")]
    public void InvalidFps_IsRejected(string fps)
        => Assert.False(FurMarkBenchmarkLog.TryParse(Complete.Replace("59 / 65 / 67", fps), 60000, out _));

    [Fact]
    public void DecimalComma_IsAccepted()
    {
        Assert.True(FurMarkBenchmarkLog.TryParse(Complete.Replace("59 / 65 / 67", "59,1 / 65,2 / 67,3"), 60000, out var result));
        Assert.Equal(65.2, result.AvgFps);
    }

    [Fact]
    public void ZeroMinimum_WithPositiveAverage_IsAccepted()
        => Assert.True(FurMarkBenchmarkLog.TryParse(Complete.Replace("59 / 65 / 67", "0 / 65 / 67"), 60000, out _));

    [Theory]
    [InlineData("")]
    [InlineData("frame 3600 ; FPS: 60 ; time: 60.0 sec")]
    [InlineData("SCORE : 3900\nduration : 60000 ms\nFPS (min/avg/max) : 59 / 65 / 67")]
    public void MissingSummary_IsRejected(string log)
        => Assert.False(FurMarkBenchmarkLog.TryParse(log, 60000, out _));
}
