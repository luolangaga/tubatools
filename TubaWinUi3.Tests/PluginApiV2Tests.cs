using System.Text.Json;
using System.Xml;
using TubaWinUi3.PluginSdk;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Plugins;

namespace TubaWinUi3.Tests;

[Collection("BuiltinToolRegistry")]
public sealed class PluginApiV2Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tuba-api2-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previous = ConfigManager.DataDirectoryOverride;
    public PluginApiV2Tests() { Directory.CreateDirectory(_root); ConfigManager.DataDirectoryOverride = _root; }
    public void Dispose() { BuiltinToolRegistry.ReplacePluginTools("api2-test", []); ConfigManager.DataDirectoryOverride = _previous; Directory.Delete(_root, true); }
    private static PluginToolDefinition Definition(string id = "suite") => new()
    { Id = id, Name = "维护工具", Xaml = "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />" };
    [Fact]
    public void XamlRegistration_HostLoadsFileAndSvg_OneToolContainsMultipleControls()
    {
        File.WriteAllText(Path.Combine(_root, "page.xaml"), """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Button Content="功能一"/><Button Content="功能二"/></StackPanel>
            """);
        File.WriteAllText(Path.Combine(_root, "icon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 128 128\" />");
        var host = new PluginManager.RegistrationHost("api2-test", _root, new(2, "entry.dll", "Entry", Icon: "icon.svg"));
        host.RegisterTool(new PluginToolDefinition { Id = "suite", Name = "维护工具", XamlPath = "page.xaml", Tags = ["优化"], ShowInSidebar = true, ShowInSettings = true });
        Assert.Null(BuiltinToolRegistry.GetById("plugin:api2-test:suite"));
        host.Commit();
        var tool = Assert.IsType<PluginXamlTool>(BuiltinToolRegistry.GetById("plugin:api2-test:suite"));
        Assert.Contains("功能二", tool.Xaml);
        Assert.Equal(Path.Combine(_root, "icon.svg"), BuiltinIconService.ResolveSvgPath(tool.Id));
        Assert.Contains("优化", tool.Definition.Tags);
        Assert.True(tool.Definition.ShowInSidebar && tool.Definition.ShowInSettings);
        Assert.Throws<InvalidOperationException>(() => host.RegisterTool(Definition("late")));
    }
    [Fact]
    public void InvalidRegistration_DoesNotReplaceExistingBatch()
    {
        var host = new PluginManager.RegistrationHost("api2-test", _root, new(2, "entry.dll", "Entry"));
        host.RegisterTool(Definition()); host.Commit();
        var before = BuiltinToolRegistry.GetById("plugin:api2-test:suite");
        var next = new PluginManager.RegistrationHost("api2-test", _root, new(2, "entry.dll", "Entry"));
        next.RegisterTool(Definition()); next.RegisterTool(Definition());
        Assert.Throws<InvalidOperationException>(next.Commit);
        Assert.Same(before, BuiltinToolRegistry.GetById("plugin:api2-test:suite"));
    }
    [Fact]
    public void ManifestV2_MetadataIsRead_AndV1Rejected()
    {
        File.WriteAllText(Path.Combine(_root, "entry.dll"), "fixture");
        var file = Path.Combine(_root, "tubatools.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new PluginManifest(2, "entry.dll", "Entry", "维护套件", "说明", "作者", "https://example.com")));
        Assert.Equal("维护套件", PluginPackageRules.ReadManifest(_root).Name);
        File.WriteAllText(file, """{"apiVersion":1,"assembly":"entry.dll","entryType":"Entry"}""");
        Assert.Throws<InvalidDataException>(() => PluginPackageRules.ReadManifest(_root));
    }

    [Fact]
    public void Manifest_ReadsFullTrustFlag()
    {
        File.WriteAllText(Path.Combine(_root, "entry.dll"), "fixture");
        var file = Path.Combine(_root, "tubatools.json");
        File.WriteAllText(file, """{"apiVersion":2,"assembly":"entry.dll","entryType":"Entry","fullTrust":true}""");
        Assert.True(PluginPackageRules.ReadManifest(_root).FullTrust);
        File.WriteAllText(file, JsonSerializer.Serialize(new PluginManifest(2, "entry.dll", "Entry")));
        Assert.False(PluginPackageRules.ReadManifest(_root).FullTrust);
    }

    [Fact]
    public void FullTrust_IsPropagatedToRegisteredTool()
    {
        var host = new PluginManager.RegistrationHost("full-test", _root, new(2, "entry.dll", "Entry", FullTrust: true));
        host.RegisterTool(Definition());
        host.Commit();
        try
        {
            var tool = Assert.IsType<PluginXamlTool>(BuiltinToolRegistry.GetById("plugin:full-test:suite"));
            Assert.True(tool.FullTrust);
        }
        finally { BuiltinToolRegistry.ReplacePluginTools("full-test", []); }
    }
    [Theory]
    [InlineData("<Grid />")]
    [InlineData("<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"Entry\" />")]
    public void InvalidRuntimeXaml_IsRejectedBeforeCommit(string xaml) =>
        Assert.Throws<InvalidDataException>(() => PluginToolRules.Validate(new() { Id = "a", Name = "a", Xaml = xaml }));
    [Fact]
    public void EntryMustChooseExactlyOneContentMode()
    {
        Assert.Throws<ArgumentException>(() => PluginToolRules.Validate(new() { Id = "a", Name = "a" }));
        Assert.Throws<ArgumentException>(() => PluginToolRules.Validate(new() { Id = "a", Name = "a", Xaml = Definition().Xaml, ExecuteAsync = _ => Task.CompletedTask }));
    }
    [Fact]
    public void IconsRejectEscapingMissingAndDoctype()
    {
        Assert.Throws<InvalidDataException>(() => PluginPackageRules.ResolveIcon(_root, "../icon.svg"));
        Assert.Throws<InvalidDataException>(() => PluginPackageRules.ResolveIcon(_root, "https://example.com/icon.svg"));
        Assert.Throws<InvalidDataException>(() => PluginPackageRules.ResolveIcon(_root, "missing.svg"));
        File.WriteAllText(Path.Combine(_root, "bad.svg"), "<!DOCTYPE svg [<!ENTITY e SYSTEM 'file:///secret'>]><svg>&e;</svg>");
        Assert.Throws<XmlException>(() => PluginPackageRules.ResolveIcon(_root, "bad.svg"));
    }
    [Fact]
    public async Task SettingsArePluginScopedAtomicAndSurviveNewInstances()
    {
        var a = new PluginSettingsStore("Package.A"); var b = new PluginSettingsStore("Package.B");
        await Task.WhenAll(a.SetAsync("one", "1", default), a.SetAsync("two", "2", default));
        await b.SetAsync("one", "other", default);
        Assert.Equal("1", await new PluginSettingsStore("package.a").GetAsync("one", default));
        Assert.Equal("2", await a.GetAsync("two", default));
        Assert.Equal("other", await b.GetAsync("one", default));
        await a.SetAsync("one", null, default); Assert.Null(await a.GetAsync("one", default));
        File.WriteAllText(Path.Combine(a.DirectoryPath, "settings.json"), "broken");
        await Assert.ThrowsAsync<JsonException>(() => a.SetAsync("one", "new", default));
        Assert.Equal("broken", File.ReadAllText(Path.Combine(a.DirectoryPath, "settings.json")));
    }
}
