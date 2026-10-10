using System.IO.Compression;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class IssueToolPreferencesTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not a ZIP")]
    [InlineData("PK\u0003\u0004truncated")]
    public void InvalidPackage_ReturnsErrorWithoutThrowing(string contents)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, contents);
            Assert.False(CustomToolPackageService.TryGetExecutables(path, out var files, out var error));
            Assert.Empty(files);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingPackage_ReturnsErrorWithoutThrowing()
    {
        Assert.False(CustomToolPackageService.TryGetExecutables(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip"), out var files, out var error));
        Assert.Empty(files);
        Assert.NotNull(error);
    }

    [Fact]
    public void ValidPackage_FindsNestedExecutablesAndReleasesFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.Delete(path);
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                archive.CreateEntry("工具/中文.EXE");
                archive.CreateEntry("readme.txt");
            }
            Assert.True(CustomToolPackageService.TryGetExecutables(path, out var files, out var error));
            Assert.Null(error);
            Assert.Equal("工具/中文.EXE", Assert.Single(files).EntryPath);
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("broken")]
    [InlineData("null")]
    public void InvalidPreferences_DefaultToNoExclusions(string? json)
        => Assert.Empty(ToolVisibilityService.Parse(json));

    [Fact]
    public void Preferences_UseCaseInsensitiveStableIds()
    {
        var entries = ToolVisibilityService.Parse("[\"builtin:energy-star\",\"BUILTIN:ENERGY-STAR\"]");
        Assert.Single(entries);
        Assert.Contains("builtin:ENERGY-STAR", entries);
    }
}

[Collection("GlobalConfigTests")]
public class ToolPreferencePersistenceTests
{
    [Fact]
    public void Save_PersistsIndependentChoices_AndAllowsReenabling()
    {
        var root = Path.Combine(Path.GetTempPath(), "TubaVisibility_" + Guid.NewGuid().ToString("N"));
        var previous = ConfigManager.DataDirectoryOverride;
        try
        {
            ConfigManager.DataDirectoryOverride = root;
            AppSettings.InvalidateCache();
            ToolVisibilityService.Save(["energy-star"], ["builtin:unigetui"]);
            AppSettings.Flush();
            AppSettings.InvalidateCache();
            Assert.False(ToolVisibilityService.IsBuiltinEnabled("energy-star"));
            Assert.True(ToolVisibilityService.IsBuiltinEnabled("unigetui"));
            Assert.False(ToolVisibilityService.IsSearchEnabled("builtin:unigetui"));
            Assert.True(ToolVisibilityService.IsSearchEnabled("builtin:energy-star"));
            ToolVisibilityService.Save([], []);
            AppSettings.Flush();
            Assert.True(ToolVisibilityService.IsBuiltinEnabled("energy-star"));
            Assert.True(ToolVisibilityService.IsSearchEnabled("builtin:unigetui"));
        }
        finally
        {
            AppSettings.Flush();
            ConfigManager.DataDirectoryOverride = previous;
            AppSettings.InvalidateCache();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
