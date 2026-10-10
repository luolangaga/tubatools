using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

[Collection("GlobalConfigTests")]
public sealed class ToolStartupScanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TubaStartup_" + Guid.NewGuid().ToString("N"));
    private readonly string _tools;
    private readonly string _metadata;

    public ToolStartupScanTests()
    {
        _tools = Path.Combine(_root, "Tools");
        _metadata = Path.Combine(_root, "Metadata");
        Directory.CreateDirectory(_tools);
        Directory.CreateDirectory(_metadata);
        UserToolLibrary.RootOverride = Path.Combine(_root, "Data");
        ConfigManager.DataDirectoryOverride = UserToolLibrary.RootOverride;
        AppSettings.InvalidateCache();
        FavoritesService.InvalidateCache();
        ToolCatalog.SetToolsRootForBuild(_tools);
        WriteMetadata("""{"tools":[{"match":"Sample","category":"测试"}]}""");
        ToolCatalog.InvalidateTagsCache();
    }

    private void WriteMetadata(string json)
    {
        File.WriteAllText(Path.Combine(_metadata, "tools.json"), json);
        ToolMetadataService.SetMetadataRootForTests(_metadata);
    }

    private string CreateFile(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(_tools, "测试", "Sample", relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x4d, 0x5a, 0, 0]);
        return path;
    }

    [Theory]
    [InlineData("Sample.exe", "bin/Sample.exe", "Sample.exe")]
    [InlineData("helper.exe", "bin/Sample.exe", "bin/Sample.exe")]
    [InlineData("Sample64.exe", "bin/Sample.exe", "Sample64.exe")]
    [InlineData("helper.cmd", "bin/another.exe", "helper.cmd")]
    public void SharedSnapshot_PreservesEntrySelection(string direct, string nested, string expected)
    {
        var files = new[] { CreateFile(direct), CreateFile(nested) };
        var directory = Path.Combine(_tools, "测试", "Sample");
        var expectedPath = Path.GetFullPath(Path.Combine(directory, expected));
        Assert.Equal(expectedPath, ToolCatalog.FindPrimaryLaunchable(directory, files));
        Assert.Equal(expectedPath, ToolCatalog.FindPrimaryLaunchable(directory));
    }

    [Theory]
    [InlineData("chosen.exe")]
    [InlineData("chosen*.exe")]
    [InlineData("bin/chosen.exe")]
    public void SharedSnapshot_HonorsDeclaredLaunchTarget(string target)
    {
        var fallback = CreateFile("Sample.exe");
        var chosen = CreateFile("bin/chosen.exe");
        WriteMetadata(System.Text.Json.JsonSerializer.Serialize(new
        {
            tools = new[] { new { match = "Sample", category = "测试", launchTarget = target } }
        }));
        var directory = Path.Combine(_tools, "测试", "Sample");
        Assert.Equal(chosen, Path.GetFullPath(ToolCatalog.FindPrimaryLaunchable(directory, [fallback, chosen])!));
        Assert.Equal(chosen, Path.GetFullPath(ToolCatalog.FindPrimaryLaunchable(directory)!));
    }

    [Fact]
    public void CategoryScan_KeepsNestedArchVariants_AndRefreshesAfterUpdate()
    {
        var primary = CreateFile("Sample.exe");
        var variant = CreateFile("bin/Sample64.exe");
        CreateFile("bin/data.dll");

        var item = Assert.Single(ToolCatalog.GetTools("测试"));
        Assert.Equal(primary, item.Path);
        Assert.Contains(item.AlternateVersions, v => v.Path == variant && v.Arch == "x64");

        File.Delete(variant);
        ToolCatalog.InvalidateTagsCache();
        var refreshed = Assert.Single(ToolCatalog.GetTools("测试"));
        Assert.Empty(refreshed.AlternateVersions);
    }

    [Fact]
    public void CategoryScan_FileNameMatchStillIncludesTool_UnknownDirectoryIsExcluded()
    {
        var primary = CreateFile("bin/chosen.exe");
        WriteMetadata("""{"tools":[{"match":"chosen","category":"测试"}]}""");
        var unknown = Path.Combine(_tools, "测试", "Unknown");
        Directory.CreateDirectory(unknown);
        File.WriteAllBytes(Path.Combine(unknown, "unknown.exe"), []);

        Assert.Equal(primary, Assert.Single(ToolCatalog.GetTools("测试")).Path);
    }

    public void Dispose()
    {
        ToolMetadataService.SetMetadataRootForTests(null);
        ToolCatalog.SetToolsRootForBuild(null);
        ToolCatalog.InvalidateTagsCache();
        UserToolLibrary.RootOverride = null;
        ConfigManager.DataDirectoryOverride = null;
        AppSettings.InvalidateCache();
        FavoritesService.InvalidateCache();
        Directory.Delete(_root, true);
    }
}
