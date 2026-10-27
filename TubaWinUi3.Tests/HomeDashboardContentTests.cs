using System.Text.Json;
using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

public class HomeDashboardContentTests
{
    [Theory]
    [InlineData(null, "favorites", "favorites")]
    [InlineData(null, "all", "all")]
    [InlineData(null, "recent", "all")]
    [InlineData("all", "favorites", "all")]
    [InlineData("favorites", "all", "favorites")]
    public void HomepageTab_RestoresSavedChoice_AndHonorsExplicitNavigation(string? requested, string? saved, string expected) =>
        Assert.Equal(expected, HomeDashboardContent.ResolveTab(requested, saved));

    [Fact]
    public void LegacyGridSettings_PreserveContentAndDiscardGeometry()
    {
        var cards = HomeDashboardContent.Parse("[{\"Target\":\"hardware\",\"Title\":\"我的配置\",\"Description\":\"自定义介绍\",\"Width\":1,\"Height\":2,\"Row\":99,\"Column\":99}]");
        var card = Assert.Single(cards);
        Assert.Equal("我的配置", card.Title);
        Assert.Equal("自定义介绍", card.Description);
        var saved = JsonSerializer.Serialize(cards);
        Assert.DoesNotContain("Width", saved);
        Assert.DoesNotContain("Height", saved);
        Assert.DoesNotContain("Row", saved);
        Assert.DoesNotContain("Column", saved);
    }

    [Fact]
    public void UserEdits_SurviveRoundTrip()
    {
        List<HomeDashboardCard> cards = [new() { Target = "tool:处理器工具/CPUZ/cpuz.exe", Title = "CPU 参数", Description = "我的工具", Featured = true }, new() { Target = "qq", Featured = false }];
        Assert.Equal(cards, HomeDashboardContent.Parse(JsonSerializer.Serialize(cards)));
        Assert.Empty(HomeDashboardContent.Parse("[]"));
    }

    [Fact]
    public void ExplicitlyClearedDescription_StaysEmptyAfterReload()
    {
        var card = Assert.Single(HomeDashboardContent.Parse("[{\"Target\":\"hardware\",\"Description\":\"\"}]"));
        Assert.Equal("", card.Description);
        Assert.Null(card.Title);
    }

    [Fact]
    public void LegacyContent_PreservesOriginalThreeFeaturedCards()
    {
        var cards = HomeDashboardContent.Parse("[{\"Target\":\"hardware\"},{\"Target\":\"qq\"},{\"Target\":\"builtin:game-monitor\"},{\"Target\":\"builtin:keyboard-test\"}]");
        Assert.All(cards.Take(3), c => Assert.True(c.Featured));
        Assert.False(cards[3].Featured);
    }

    [Fact]
    public void FeaturedCards_HaveNoThreeCardLimit_AndCompactChoiceSurvivesReordering()
    {
        var cards = Enumerable.Range(0, 20).Select(i => new HomeDashboardCard { Target = "builtin:tool-" + i, Featured = true }).ToList();
        cards.Insert(0, new() { Target = "hardware", Featured = false });
        var restored = HomeDashboardContent.Parse(JsonSerializer.Serialize(cards));
        Assert.False(restored[0].Featured);
        Assert.Equal(20, restored.Count(c => c.Featured == true));
        Assert.Equal(cards, restored);
    }

    [Theory]
    [InlineData(null)] [InlineData("broken")] [InlineData("{}")] [InlineData("null")]
    public void MissingOrBrokenSettings_UseDefaults(string? value) =>
        Assert.Equal(HomeDashboardContent.Defaults(), HomeDashboardContent.Parse(value));

    [Fact]
    public void PackagedHomepage_NeverShowsSoftwareStore()
    {
        var cards = HomeDashboardContent.Defaults();
        cards.Add(new() { Target = "builtin:winget-store", Featured = true });
        Assert.Equal(9, cards.Count(c => HomeDashboardContent.IsVisible(c, false)));
        Assert.Equal(8, cards.Count(c => HomeDashboardContent.IsVisible(c, true)));
        Assert.DoesNotContain(cards.Where(c => HomeDashboardContent.IsVisible(c, true)), c => c.Target == "builtin:winget-store");
    }

    [Fact]
    public void FeaturedDefaults_AddContextMenuAndStartupManagement()
    {
        var cards = HomeDashboardContent.LoadFeatured(null, null);
        Assert.Equal(8, cards.Count);
        Assert.Equal("qq", cards[0].Target);
        Assert.All(cards, c => Assert.True(c.Featured));
        Assert.Contains(cards, c => c.Target == "builtin:context-menu-mgr");
        Assert.Contains(cards, c => c.Target == "builtin:startup-manager");
        Assert.Contains(cards, c => c.Target == "builtin:battery-analyzer");
        Assert.Contains(cards, c => c.Target == "builtin:stress-test");
    }

    [Fact]
    public void FeaturedMigration_KeepsOldEdits_DropsSmallCards_AndUpgradesDefaultStripOnce()
    {
        var old = "[{\"Target\":\"hardware\",\"Title\":\"我的电脑\",\"Featured\":true},{\"Target\":\"builtin:quick-device-check\",\"Featured\":true},{\"Target\":\"builtin:space-cleanup\",\"Featured\":true},{\"Target\":\"qq\",\"Featured\":false}]";
        var cards = HomeDashboardContent.LoadFeatured(null, old);
        Assert.Equal(8, cards.Count);
        Assert.Equal("我的电脑", cards[1].Title);
        Assert.Equal("qq", cards[0].Target);
        Assert.Equal(cards, HomeDashboardContent.LoadFeatured(JsonSerializer.Serialize(cards), old));
        Assert.Empty(HomeDashboardContent.LoadFeatured("[]", old));
    }

    [Fact]
    public void FeaturedUpgrade_MovesExistingCommunityFirst_WithoutDuplicatesOrLosingEdits()
    {
        var previous = "[{\"Target\":\"builtin:stress-test\",\"Title\":\"我的烤机\",\"Featured\":true},{\"Target\":\"qq\",\"Title\":\"来聊天\",\"Featured\":true},{\"Target\":\"hardware\",\"Featured\":true}]";
        var cards = HomeDashboardContent.LoadFeatured(null, previous);
        Assert.Equal("qq", cards[0].Target);
        Assert.Equal("来聊天", cards[0].Title);
        Assert.Equal("我的烤机", Assert.Single(cards, c => c.Target == "builtin:stress-test").Title);
        Assert.Contains(cards, c => c.Target == "builtin:battery-analyzer");
        Assert.Equal(cards, HomeDashboardContent.LoadFeatured(JsonSerializer.Serialize(cards), previous));
    }

    [Fact]
    public void InvalidTargetsAreRejected_AndContentIsBounded()
    {
        Assert.Empty(HomeDashboardContent.Parse("[null,{}, {\"Target\":\"unknown\"}, {\"Target\":\"builtin:\"}]"));
        var cards = HomeDashboardContent.Parse(JsonSerializer.Serialize(Enumerable.Repeat(new HomeDashboardCard
            { Target = "hardware", Title = new string('字', 100), Description = new string('文', 400) }, HomeDashboardContent.MaxCards + 10)));
        Assert.Equal(HomeDashboardContent.MaxCards, cards.Count);
        Assert.All(cards, c => { Assert.Equal(60, c.Title!.Length); Assert.Equal(240, c.Description!.Length); });
    }
}
