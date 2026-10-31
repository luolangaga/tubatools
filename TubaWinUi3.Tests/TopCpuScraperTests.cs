using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

public class TopCpuScraperTests
{
    private const string Html = """
        <h1>2026年10月 性能排行榜</h1>
        <div id="rr1"><span class="min-w-8">1.</span><a>Intel Core Ultra 9 285K</a>
        <span class="text-gray-400">24C 24T</span><span class="font-bold">2,318</span></div>
        """;

    [Fact]
    public void SingleCoreAndMultiCoreScoresStayInTheirOwnFields()
    {
        var single = Assert.Single(TopCpuScraperService.ParseCpuRankingsHtml(Html, "cinebench-r23-single-core", "desktop").Entries);
        var multi = Assert.Single(TopCpuScraperService.ParseCpuRankingsHtml(Html, "cinebench-r23-multi-core", "desktop").Entries);
        Assert.Equal(2318, single.SingleCore);
        Assert.Equal(0, single.MultiCore);
        Assert.Equal(2318, multi.MultiCore);
        Assert.Equal("desktop", multi.Category);
    }

    [Fact]
    public void TimeSpyScoreAndVramAreSeparate()
    {
        var html = Html.Replace("Intel Core Ultra 9 285K", "NVIDIA GeForce RTX 5090")
            .Replace("24C 24T", "32GB GDDR7").Replace("2,318", "46,923");
        var gpu = Assert.Single(TopCpuScraperService.ParseGpuRankingsHtml(html, "time-spy", "desktop").Entries);
        Assert.Equal(46923, gpu.Rating);
        Assert.Equal("46923", gpu.TimeSpy);
        Assert.Equal("32GB GDDR7", gpu.Vram);
        Assert.Empty(gpu.Tflops);
    }
}
