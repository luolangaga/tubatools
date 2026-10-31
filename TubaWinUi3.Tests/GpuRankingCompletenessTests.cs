using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

public class GpuRankingCompletenessTests
{
    [Fact]
    public void RejectsFullRankingMissingAnEntryFromSubRankings()
    {
        var desktop = new GpuRankingEntry { Name = "Desktop GPU", Rank = 1, Rating = 100 };
        var laptop = new GpuRankingEntry { Name = "Mobile GPU", Rank = 2, Rating = 80 };
        Assert.False(GpuRankingService.IsCompleteRanking([desktop], [desktop], [laptop]));
        Assert.True(GpuRankingService.IsCompleteRanking([desktop, laptop], [desktop], [laptop]));
    }

    [Fact]
    public void KeepsSeparateSourceRowsWithSameDisplayNameButRejectsRankGaps()
    {
        var first = new GpuRankingEntry { Name = "AMD Radeon 890M", Rank = 1, Rating = 100 };
        var second = new GpuRankingEntry { Name = "AMD Radeon 890M", Rank = 2, Rating = 90 };
        Assert.True(GpuRankingService.IsCompleteRanking([first, second], [first], [second]));
        second.Rank = 3;
        Assert.False(GpuRankingService.IsCompleteRanking([first, second], [first], [second]));
    }
}
