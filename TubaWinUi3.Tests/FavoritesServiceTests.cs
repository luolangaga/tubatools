using System.Text.Json;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// FavoritesService 测试:用 Custom 数据目录(临时目录)隔离 favorites.json,
/// 结束后恢复原配置,避免污染真实用户数据。
/// </summary>
// 与 ToolCatalogTests 等共享全局静态配置(ConfigManager/ToolCatalog)的测试串行执行,避免互相覆盖
[Collection("GlobalConfigTests")]
public class FavoritesServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigLocation _originalLocation;
    private readonly string? _originalCustomPath;

    public FavoritesServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TubaFavTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _originalLocation = ConfigManager.GetConfigLocation();
        _originalCustomPath = ConfigManager.GetCustomPath();
        ConfigManager.SetConfigLocation(ConfigLocation.Custom, _tempDir);
        FavoritesService.InvalidateCache();
    }

    public void Dispose()
    {
        ConfigManager.SetConfigLocation(_originalLocation, _originalCustomPath);
        FavoritesService.InvalidateCache();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void AddFavorite_AppendsToEnd()
    {
        FavoritesService.AddFavorite(@"C:\tools\a.exe");
        FavoritesService.AddFavorite(@"C:\tools\b.exe");
        FavoritesService.AddFavorite(@"C:\tools\c.exe");

        Assert.Equal(
            [@"C:\tools\a.exe", @"C:\tools\b.exe", @"C:\tools\c.exe"],
            FavoritesService.GetFavorites());
    }

    [Fact]
    public void AddFavorite_DuplicatesIgnored()
    {
        FavoritesService.AddFavorite(@"C:\tools\a.exe");
        FavoritesService.AddFavorite(@"C:\tools\a.exe");

        Assert.Single(FavoritesService.GetFavorites());
    }

    [Fact]
    public void RemoveFavorite_KeepsRemainingOrder()
    {
        FavoritesService.AddFavorite(@"C:\tools\a.exe");
        FavoritesService.AddFavorite(@"C:\tools\b.exe");
        FavoritesService.AddFavorite(@"C:\tools\c.exe");

        FavoritesService.RemoveFavorite(@"C:\tools\b.exe");

        Assert.Equal(
            [@"C:\tools\a.exe", @"C:\tools\c.exe"],
            FavoritesService.GetFavorites());
    }

    [Fact]
    public void SaveOrder_PersistsNewOrder()
    {
        FavoritesService.AddFavorite(@"C:\tools\a.exe");
        FavoritesService.AddFavorite(@"C:\tools\b.exe");
        FavoritesService.AddFavorite(@"C:\tools\c.exe");

        FavoritesService.SaveOrder([@"C:\tools\c.exe", @"C:\tools\a.exe", @"C:\tools\b.exe"]);

        Assert.Equal(
            [@"C:\tools\c.exe", @"C:\tools\a.exe", @"C:\tools\b.exe"],
            FavoritesService.GetFavorites());
    }

    [Fact]
    public void SaveOrder_FiltersBlankPaths()
    {
        FavoritesService.AddFavorite(@"C:\tools\a.exe");
        FavoritesService.AddFavorite(@"C:\tools\b.exe");

        FavoritesService.SaveOrder([@"C:\tools\b.exe", "", @"   ", @"C:\tools\a.exe"]);

        Assert.Equal(
            [@"C:\tools\b.exe", @"C:\tools\a.exe"],
            FavoritesService.GetFavorites());
    }

    [Fact]
    public void SaveOrder_FileContentMatchesOrder()
    {
        FavoritesService.AddFavorite(@"C:\tools\a.exe");
        FavoritesService.AddFavorite(@"C:\tools\b.exe");
        FavoritesService.SaveOrder([@"C:\tools\b.exe", @"C:\tools\a.exe"]);

        var json = File.ReadAllText(ConfigManager.GetFavoritesPath());
        var stored = JsonSerializer.Deserialize<List<string>>(json);
        // Portable favorites deliberately store root tokens rather than absolute paths.
        Assert.Equal(new[] { @"C:\tools\b.exe", @"C:\tools\a.exe" }.Select(PathResolver.MakeRelative), stored);
    }

    [Fact]
    public void SaveOrder_AfterCacheInvalidation_KeepsOrder()
    {
        FavoritesService.AddFavorite(@"C:\tools\a.exe");
        FavoritesService.AddFavorite(@"C:\tools\b.exe");
        FavoritesService.SaveOrder([@"C:\tools\b.exe", @"C:\tools\a.exe"]);

        FavoritesService.InvalidateCache();

        Assert.Equal(
            [@"C:\tools\b.exe", @"C:\tools\a.exe"],
            FavoritesService.GetFavorites(), StringComparer.OrdinalIgnoreCase);
    }
}
