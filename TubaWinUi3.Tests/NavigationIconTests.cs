using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class NavigationIconTests
{
    private static string IconDir => Path.Combine(AppContext.BaseDirectory, "Assets", "NavIcons");

    [Theory]
    [InlineData("处理器工具", "NavIcons/cpu")]
    [InlineData("显卡工具", "NavIcons/gpu")]
    [InlineData("显示器工具", "NavIcons/display")]
    [InlineData("硬盘工具", "NavIcons/disk")]
    [InlineData("内存工具", "NavIcons/memory")]
    [InlineData("外设工具", "NavIcons/peripherals")]
    [InlineData("声卡工具", "NavIcons/sound")]
    [InlineData("网卡工具", "NavIcons/network")]
    [InlineData("综合检测", "NavIcons/diagnostics")]
    [InlineData("综合工具", "NavIcons/toolbox")]
    [InlineData("其他工具", "NavIcons/other")]
    [InlineData("游戏工具", "BuiltinIcons/game-monitor")]
    [InlineData("烤鸡工具", "BuiltinIcons/stress-test")]
    [InlineData("home", "NavIcons/home")]
    [InlineData("hardware", "HomeTiles/hardware")]
    [InlineData("builtin", "NavIcons/toolbox")]
    [InlineData("benchmark", "BuiltinIcons/performance-benchmark")]
    [InlineData("community", "NavIcons/community")]
    [InlineData("settings", "NavIcons/settings")]
    public void Category_ResolvesToItsOwnBundledAsset(string category, string expected)
    {
        var asset = NavigationIconService.AssetFor(category);
        Assert.Equal($"Assets/{expected}.svg", asset);
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, asset)));
    }

    [Fact]
    public void NewCategory_UsesGenericColoredIcon()
        => Assert.Equal("Assets/NavIcons/other.svg", NavigationIconService.AssetFor("自定义分类"));

    [Fact]
    public void NavigationAssets_FollowBuiltinGradientStyleAndRasterize()
    {
        Assert.Empty(IconAssetValidation.ValidateAll(IconDir, filled: true));
        Assert.Empty(IconAssetValidation.ValidateRasterizes(IconDir));
    }
}
