using TubaWinUi3.PluginSdk;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Plugins;

namespace TubaWinUi3.Tests;

[Collection("BuiltinToolRegistry")]
public sealed class PluginCustomizationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tuba-customization-" + Guid.NewGuid().ToString("N"));
    private readonly string? _before = ConfigManager.DataDirectoryOverride;
    public PluginCustomizationTests() { Directory.CreateDirectory(_root); ConfigManager.DataDirectoryOverride = _root; }
    public void Dispose() { ConfigManager.DataDirectoryOverride = _before; Directory.Delete(_root, true); }
    private static PluginNavigationItem[] Defaults => [new("home", "Home", "h", true, 0), new("hardware", "Hardware", "w", true, 1), new("settings", "Settings", "s", true, 99, true)];
    [Fact]
    public void Navigation_ChangesArePersistentPartialAndResettable()
    {
        var change = PluginNavigationStore.Merge(new("hardware", Title: "My hardware", IsVisible: false), new("hardware", Order: 0, Glyph: "x"));
        PluginNavigationStore.Save([change]);
        var items = PluginNavigationStore.Apply(Defaults, PluginNavigationStore.Read());
        Assert.Equal("hardware", items[0].Id); Assert.Equal("My hardware", items[0].Title); Assert.False(items[0].IsVisible); Assert.Equal("x", items[0].Glyph);
        Assert.Equal("settings", items[^1].Id);
        PluginNavigationStore.Save([]); Assert.Equal(Defaults.Select(i => i.Id), PluginNavigationStore.Apply(Defaults, PluginNavigationStore.Read()).Select(i => i.Id));
    }
    [Fact]
    public void Navigation_EmptyOverridesRestorePropertiesAndFooterStaysLast()
    {
        var items = PluginNavigationStore.Apply(Defaults, [new("home", Title: "", Glyph: ""), new("settings", Order: 0)]);
        Assert.Equal("Home", items[0].Title); Assert.Equal("h", items[0].Glyph); Assert.True(items[^1].IsFooter);
        Assert.Throws<ArgumentException>(() => PluginNavigationStore.Save([new("home"), new("home")]));
        Assert.Throws<ArgumentOutOfRangeException>(() => PluginNavigationStore.Save([new("home", Order: -1)]));
    }
    [Fact]
    public async Task Background_ImportsVerifiedImageAndSurvivesSourceRemoval()
    {
        var source = Path.Combine(_root, "tiny.png");
        await File.WriteAllBytesAsync(source, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aX1kAAAAASUVORK5CYII="));
        var imported = await PluginCustomizationFiles.ImportBackgroundAsync(_root, "tiny.png", default);
        Assert.StartsWith(ConfigManager.GetBackgroundsDir(), imported); Assert.NotEqual(source, imported);
        Assert.Equal(imported, await PluginCustomizationFiles.ImportBackgroundAsync(_root, source, default));
        File.Delete(source); Assert.True(File.Exists(imported));
    }
    [Fact]
    public async Task CustomAssets_KeepPackagePathRulesAndValidateSvg()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => PluginCustomizationFiles.ImportBackgroundAsync(_root, "../outside.png", default));
        var svg = Path.Combine(_root, "icon.svg"); await File.WriteAllTextAsync(svg, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"/>");
        var icon = await PluginCustomizationFiles.ImportNavigationIconAsync(_root, "icon.svg", default);
        File.Delete(svg); Assert.True(File.Exists(icon));
        await File.WriteAllTextAsync(Path.Combine(_root, "file.txt"), "not an image");
        await Assert.ThrowsAsync<ArgumentException>(() => PluginCustomizationFiles.ImportBackgroundAsync(_root, "file.txt", default));
    }
}
