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
    // 不要用 C:\tools：某些机器上真实存在 C:\Tools，会被 PathResolver 识别成
    // ToolsRoot 并改写成 {ToolsRoot}，导致收藏路径大小写被规范化。
    private static readonly string PathRoot = Path.Combine(
        Path.GetTempPath(), "TubaFavoritePaths-" + Guid.NewGuid().ToString("N"));
    private static string P(string name) => Path.Combine(PathRoot, name);

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
        FavoritesService.AddFavorite(P("a.exe"));
        FavoritesService.AddFavorite(P("b.exe"));
        FavoritesService.AddFavorite(P("c.exe"));

        Assert.Equal(
            [P("a.exe"), P("b.exe"), P("c.exe")],
            FavoritesService.GetFavorites());
    }

    [Fact]
    public void AddFavorite_DuplicatesIgnored()
    {
        FavoritesService.AddFavorite(P("a.exe"));
        FavoritesService.AddFavorite(P("a.exe"));

        Assert.Single(FavoritesService.GetFavorites());
    }

    [Fact]
    public void RemoveFavorite_KeepsRemainingOrder()
    {
        FavoritesService.AddFavorite(P("a.exe"));
        FavoritesService.AddFavorite(P("b.exe"));
        FavoritesService.AddFavorite(P("c.exe"));

        FavoritesService.RemoveFavorite(P("b.exe"));

        Assert.Equal(
            [P("a.exe"), P("c.exe")],
            FavoritesService.GetFavorites());
    }

    [Fact]
    public void SaveOrder_PersistsNewOrder()
    {
        FavoritesService.AddFavorite(P("a.exe"));
        FavoritesService.AddFavorite(P("b.exe"));
        FavoritesService.AddFavorite(P("c.exe"));

        FavoritesService.SaveOrder([P("c.exe"), P("a.exe"), P("b.exe")]);

        Assert.Equal(
            [P("c.exe"), P("a.exe"), P("b.exe")],
            FavoritesService.GetFavorites());
    }

    [Fact]
    public void SaveOrder_FiltersBlankPaths()
    {
        FavoritesService.AddFavorite(P("a.exe"));
        FavoritesService.AddFavorite(P("b.exe"));

        FavoritesService.SaveOrder([P("b.exe"), "", @"   ", P("a.exe")]);

        Assert.Equal(
            [P("b.exe"), P("a.exe")],
            FavoritesService.GetFavorites());
    }

    [Fact]
    public void SaveOrder_FileContentMatchesOrder()
    {
        FavoritesService.AddFavorite(P("a.exe"));
        FavoritesService.AddFavorite(P("b.exe"));
        FavoritesService.SaveOrder([P("b.exe"), P("a.exe")]);

        var json = File.ReadAllText(ConfigManager.GetFavoritesPath());
        var stored = JsonSerializer.Deserialize<List<string>>(json);
        // Portable favorites deliberately store root tokens rather than absolute paths.
        Assert.Equal(new[] { P("b.exe"), P("a.exe") }.Select(PathResolver.MakeRelative), stored);
    }

    [Fact]
    public void SaveOrder_AfterCacheInvalidation_KeepsOrder()
    {
        FavoritesService.AddFavorite(P("a.exe"));
        FavoritesService.AddFavorite(P("b.exe"));
        FavoritesService.SaveOrder([P("b.exe"), P("a.exe")]);

        FavoritesService.InvalidateCache();

        Assert.Equal(
            [P("b.exe"), P("a.exe")],
            FavoritesService.GetFavorites(), StringComparer.OrdinalIgnoreCase);
    }
}
