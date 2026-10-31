using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

public class LocalRankingMatcherTests
{
    [Fact]
    public void MatchesModelWithVendorAndClockSuffix()
    {
        var entries = new[] { "Intel Core i7-13700K", "Intel Core i7-13700KF" };
        Assert.Equal(entries[0], LocalRankingMatcher.Find("Intel(R) Core(TM) i7-13700K CPU @ 3.40GHz", entries, x => x));
    }

    [Fact]
    public void RejectsAmbiguousAndDifferentModels()
    {
        var entries = new[] { "NVIDIA GeForce RTX 4090", "NVIDIA GeForce RTX 4090" };
        Assert.Null(LocalRankingMatcher.Find("NVIDIA GeForce RTX 4090", entries, x => x));
        Assert.Null(LocalRankingMatcher.Find("NVIDIA GeForce RTX 4090 Ti", new[] { "NVIDIA GeForce RTX 4090" }, x => x));
    }
}
