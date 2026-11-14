using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

public class RankingTierTests
{
    [Fact]
    public void ReturnsNoTiersForShortLists()
    {
        Assert.Empty(RankingTiers.Build(0));
        Assert.Empty(RankingTiers.Build(RankingTiers.MinimumCount - 1));
    }

    [Fact]
    public void SplitsHundredEntriesByPercentile()
    {
        var tiers = RankingTiers.Build(100);

        Assert.Equal(5, tiers.Count);
        Assert.Equal([10, 20, 30, 25, 15], tiers.Select(t => t.Count).ToArray());
        Assert.Equal(0, tiers[0].Start);
        Assert.Equal(99, tiers[^1].End);

        for (var i = 1; i < tiers.Count; i++)
            Assert.Equal(tiers[i - 1].End + 1, tiers[i].Start);
    }

    [Fact]
    public void CoversEveryPositionExactlyOnce()
    {
        foreach (var count in new[] { 10, 37, 299, 402, 627 })
        {
            var tiers = RankingTiers.Build(count);
            var covered = tiers.Sum(t => t.Count);
            Assert.Equal(count, covered);
            Assert.Equal(count - 1, tiers[^1].End);
        }
    }

    [Fact]
    public void KeepsTierLabelsStable()
    {
        var tiers = RankingTiers.Build(402);
        Assert.Equal(["顶尖性能", "高端性能", "主流性能", "入门性能", "基础性能"],
            tiers.Select(t => t.Title).ToArray());
        Assert.Equal([1, 2, 3, 4, 5], tiers.Select(t => t.Level).ToArray());
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(100, 100, 1)]
    [InlineData(50, 100, 0.5)]
    [InlineData(1, 4000, RankingScale.MinimumFraction)]
    [InlineData(5, 4000, RankingScale.MinimumFraction)]
    [InlineData(-3, 100, 0)]
    [InlineData(500, 100, 1)]
    public void FractionClampsToVisibleRange(double value, double max, double expected)
    {
        Assert.Equal(expected, RankingScale.Fraction(value, max), 6);
    }

    [Fact]
    public void FractionTreatsInvalidMaxAsZero()
    {
        Assert.Equal(0, RankingScale.Fraction(120, 0));
    }

    [Theory]
    [InlineData(100, 100, 100)]
    [InlineData(50, 100, 50)]
    [InlineData(3, 999, 0)]
    [InlineData(0, 100, 0)]
    [InlineData(200, 0, 0)]
    public void PercentRoundsRelativeToLeader(long value, long max, int expected)
    {
        Assert.Equal(expected, RankingScale.Percent(value, max));
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(0, 100, 100)]
    [InlineData(99, 100, 0)]
    [InlineData(49, 100, 51)]
    [InlineData(300, 0, 0)]
    public void BeatPercentUsesZeroBasedPosition(int position, int count, int expected)
    {
        Assert.Equal(expected, RankingScale.BeatPercent(position, count));
    }

    [Fact]
    public void BuildTicksAnchorsOnCategoryLeader()
    {
        // 100 → 1 的完整榜单，两列铺排；100% 落在榜首，50% 落在第 51 个型号所在行
        var metrics = Enumerable.Range(1, 100).Select(i => (long)(101 - i)).ToList();
        var ticks = RankingLadderLayout.BuildTicks(metrics, 100, 2);

        Assert.Equal([100, 75, 50, 25, 10, 5, 1], ticks.Select(t => t.Percent).ToArray());
        Assert.Equal(0, ticks[0].Y);
        Assert.Equal(25 * RankingLadderLayout.RowHeight, ticks[2].Y);
    }

    [Fact]
    public void BuildTicksSkipsThresholdsAboveTheList()
    {
        // 筛选后只剩极高的型号：除 100% 外的刻度都落在列表之外，不应出现
        var metrics = new List<long> { 1000, 990, 980 };
        var ticks = RankingLadderLayout.BuildTicks(metrics, 1000, 3);

        Assert.Single(ticks);
        Assert.Equal(100, ticks[0].Percent);
    }

    [Fact]
    public void BuildTicksKeepsLabelsApart()
    {
        // 分数在榜尾骤降时，5% 与 1% 会落在同一行，只保留更靠上的一个
        var metrics = Enumerable.Repeat(1000L, 15).Append(100L).Append(50L).Append(1L).ToList();
        var ticks = RankingLadderLayout.BuildTicks(metrics, 1000, 2);

        Assert.Equal([100, 75, 5], ticks.Select(t => t.Percent).ToArray());
        for (var i = 1; i < ticks.Count; i++)
            Assert.True(ticks[i].Y - ticks[i - 1].Y >= RankingLadderLayout.MinimumTickGap);
    }

    [Theory]
    [InlineData(600, 1)]
    [InlineData(900, 2)]
    [InlineData(1300, 3)]
    [InlineData(1800, 4)]
    public void ColumnsGrowWithViewportWidth(double width, int expected)
    {
        Assert.Equal(expected, RankingLadderLayout.ColumnsFor(width));
    }

    [Fact]
    public void WallHeightFollowsRowCount()
    {
        Assert.Equal(0, RankingLadderLayout.HeightFor(0, 3));
        Assert.Equal(26 * 4, RankingLadderLayout.HeightFor(10, 3));
        Assert.Equal(26 * 100, RankingLadderLayout.HeightFor(299, 3));
    }
}
