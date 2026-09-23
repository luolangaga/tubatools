using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// 侧边栏导航图标（<c>Assets/NavIcons/&lt;slug&gt;.svg</c>）的回归：
/// 固定导航项与全部已知工具分类都要有图标，且写法符合 Fluent 规范。
/// </summary>
public class NavIconTests
{
    private static string IconDir => Path.Combine(AppContext.BaseDirectory, "Assets", "NavIcons");

    [Fact]
    public void EveryFixedNavKey_HasColoredIcon()
    {
        var missing = NavIconCatalog.FixedNavKeys
            .Where(key => !NavIconCatalog.Has(key))
            .ToList();

        Assert.True(missing.Count == 0,
            $"以下固定导航项缺少 Assets/NavIcons/{{slug}}.svg：{string.Join("、", missing)}");
    }

    [Fact]
    public void EveryKnownToolCategory_HasColoredIcon()
    {
        var missing = NavIconCatalog.KnownCategoryKeys
            .Where(category => !NavIconCatalog.Has(category))
            .ToList();

        Assert.True(missing.Count == 0,
            $"以下工具分类缺少 Assets/NavIcons/{{slug}}.svg：{string.Join("、", missing)}");
    }

    [Fact]
    public void IconFolder_HasNoOrphanFiles()
    {
        var known = NavIconCatalog.AllSlugs.ToHashSet(StringComparer.Ordinal);
        var orphans = IconAssetValidation.SvgIds(IconDir).Where(slug => !known.Contains(slug)).ToList();

        Assert.True(orphans.Count == 0,
            $"这些 svg 不在 NavIconCatalog 的映射表里（改名后忘了同步？）：{string.Join("、", orphans)}");
    }

    [Fact]
    public void EveryIcon_FollowsFluentConventions()
    {
        var problems = IconAssetValidation.ValidateAll(IconDir);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void EveryIcon_RasterizesToNonEmptyBitmap()
    {
        var problems = IconAssetValidation.ValidateRasterizes(IconDir);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void ResolveSlug_ReturnsNullForUnknownOrCustomCategories()
    {
        Assert.Null(NavIconCatalog.Get(null));
        Assert.Null(NavIconCatalog.Get(""));
        Assert.Null(NavIconCatalog.Get("   "));
        Assert.Null(NavIconCatalog.Get("用户自建分类"));
        Assert.False(NavIconCatalog.Has("用户自建分类"));
    }

    [Fact]
    public void EveryCategoryKey_IsMappedToADistinctSlug()
    {
        // 两个分类指到同一个 slug 会让侧边栏出现两个一模一样的图标，通常是映射表复制粘贴漏改。
        var slugs = NavIconCatalog.KnownCategoryKeys
            .Concat(NavIconCatalog.FixedNavKeys)
            .Select(NavIconCatalog.ResolveSlug)
            .ToList();

        Assert.DoesNotContain(null, slugs);
        Assert.Equal(slugs.Count, slugs.Distinct(StringComparer.Ordinal).Count());
    }
}
