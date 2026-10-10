using System.IO.Compression;
using System.Text.Json.Nodes;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

[Collection("GlobalConfigTests")]
public sealed class UserToolUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Tuba215_" + Guid.NewGuid().ToString("N"));
    private readonly string _metadata;
    private readonly string _tools;

    public UserToolUpdateTests()
    {
        _metadata = Path.Combine(_root, "Metadata");
        _tools = Path.Combine(_root, "Tools");
        Directory.CreateDirectory(_metadata);
        Directory.CreateDirectory(_tools);
        File.WriteAllText(Path.Combine(_metadata, "tools.json"), "{\"tools\":[]}");
        ToolCatalog.SetToolsRootForBuild(_tools);
        ToolMetadataService.SetMetadataRootForTests(_metadata);
        UserToolLibrary.RootOverride = Path.Combine(_root, "Data");
        ConfigManager.DataDirectoryOverride = UserToolLibrary.RootOverride;
        AppSettings.InvalidateCache();
        FavoritesService.InvalidateCache();
        CommunityToolRegistry.SetRootForTests(Path.Combine(_root, "Community"));
    }

    public void Dispose()
    {
        UserToolLibrary.RootOverride = null;
        ConfigManager.DataDirectoryOverride = null;
        AppSettings.InvalidateCache();
        FavoritesService.InvalidateCache();
        ToolMetadataService.SetMetadataRootForTests(null);
        ToolCatalog.SetToolsRootForBuild(null);
        ToolCatalog.InvalidateTagsCache();
        CommunityToolRegistry.SetRootForTests(null);
        Directory.Delete(_root, true);
    }

    private static string Exe(string directory, string file = "main.exe")
    {
        var path = Path.GetFullPath(Path.Combine(directory, file));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x4d, 0x5a, 0, 0]);
        return path;
    }

    private string Register(string category, string directoryName, string name = "我的工具")
    {
        var directory = Path.Combine(ToolCatalog.UserToolsRoot, "Custom", category, directoryName);
        Exe(directory);
        ToolMetadataService.UpsertToolMetadataEntry(directoryName, name: name,
            launchTarget: "main.exe", toolDirectory: directory, category: category);
        ToolCatalog.InvalidateTagsCache();
        return directory;
    }

    [Fact]
    public async Task Import_ThenReplaceOfficialCatalog_PreservesRegistrationAndSelectedExecutable()
    {
        var archive = Path.Combine(_root, "import.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            foreach (var file in new[] { "helper.exe", "bin/selected.exe" })
            {
                using var output = zip.CreateEntry(file).Open();
                output.Write([0x4d, 0x5a, 0, 0]);
            }
        }
        var result = await CustomToolPackageService.ImportAsync(new CustomToolImportRequest(
            archive, "我的工具", "我的分类", "bin/selected.exe", "自定义描述", "用户", ["测试"], []));
        Assert.StartsWith(ToolCatalog.UserToolsRoot, result.ToolDirectory);
        Assert.Equal("{\"tools\":[]}", File.ReadAllText(Path.Combine(_metadata, "tools.json")));
        File.WriteAllText(Path.Combine(_metadata, "tools.json"), """
            {"tools":[{"match":"new-official","description":"新版官方工具"}]}
            """);
        ToolMetadataService.InvalidateCache();
        ToolCatalog.InvalidateTagsCache();
        var item = Assert.Single(ToolCatalog.GetTools("我的分类"));
        Assert.Equal(result.PrimaryExecutablePath, item.Path);
        Assert.Equal("自定义描述", item.Description);
        Assert.True(File.Exists(item.Path));
        Assert.Contains("我的分类", ToolCatalog.GetCategories());
    }

    [Fact]
    public void SameDirectoryNames_InDifferentCategories_DoNotOverwriteOrFuzzyMatch()
    {
        var first = Register("甲", "same", "工具甲");
        var second = Register("乙", "same", "工具乙");
        Assert.Equal("工具甲", Assert.Single(ToolCatalog.GetTools("甲")).Name);
        Assert.Equal("工具乙", Assert.Single(ToolCatalog.GetTools("乙")).Name);
        Exe(Path.Combine(ToolCatalog.UserToolsRoot, "Custom", "甲", "same-extra"));
        ToolCatalog.InvalidateTagsCache();
        Assert.Single(ToolCatalog.GetTools("甲"));
        Assert.NotEqual(ToolMetadataService.FindJsonMetadataByDir(first)!.Id,
            ToolMetadataService.FindJsonMetadataByDir(second)!.Id);
    }

    [Fact]
    public void SameDisplayNames_InSameCategory_AreSeparateTools()
    {
        Register("甲", "one", "相同名称");
        Register("甲", "two", "相同名称");
        Assert.Equal(2, ToolCatalog.GetAllToolsCached().Count);
    }

    [Fact]
    public async Task RemovingOneRegistration_DoesNotRemoveSameNameInAnotherCategory()
    {
        var first = Register("甲", "same");
        Register("乙", "same");
        await ToolMetadataService.RemoveMetadataAsync(Path.Combine(first, "main.exe"));
        ToolCatalog.InvalidateTagsCache();
        Assert.Empty(ToolCatalog.GetTools("甲"));
        Assert.Single(ToolCatalog.GetTools("乙"));
        // 移除登记不偷偷删除文件；文件删除由用户确认的卸载入口负责。
        Assert.True(File.Exists(Path.Combine(first, "main.exe")));
    }

    [Fact]
    public void MetadataMissing_DoesNotDeleteNonemptyCategory()
    {
        var exe = Exe(Path.Combine(_tools, "我的分类", "未登记工具"));
        Assert.Empty(ToolCatalog.GetTools("我的分类"));
        Assert.False(ToolCatalog.PruneCategoryIfEmpty("我的分类"));
        Assert.True(File.Exists(exe));
    }

    [Fact]
    public void Recovery_RequiresSelectedExecutable_AndKeepsOriginalPath()
    {
        var directory = Path.Combine(_tools, "我的分类", "未登记工具");
        Exe(directory, "helper.exe");
        var primary = Exe(directory, "bin/main.exe");
        var candidate = Assert.Single(ToolRecoveryService.FindUnregistered());
        Assert.Throws<ArgumentException>(() => ToolRecoveryService.Register(candidate, "工具", Path.Combine(_root, "elsewhere.exe")));
        ToolRecoveryService.Register(candidate, "恢复后的名称", primary.Replace('\\', '/'));
        Assert.Equal(primary, Assert.Single(ToolCatalog.GetTools("我的分类")).Path);
        Assert.Empty(ToolRecoveryService.FindUnregistered());
    }

    [Fact]
    public async Task LegacyMixedCatalog_MigratesOnce_AndDoesNotResurrectRemovedRegistration()
    {
        var directory = Path.Combine(_tools, "我的分类", "old-tool");
        var primary = Exe(directory);
        File.WriteAllText(Path.Combine(_metadata, "tools.default.json"), "{\"tools\":[]}");
        File.WriteAllText(Path.Combine(_metadata, "tools.json"), """
            {"tools":[{"match":"old-tool","name":"旧工具","launchTarget":"main.exe","customField":"keep"}]}
            """);
        ToolMetadataService.InvalidateCache();
        Assert.Equal("旧工具", Assert.Single(ToolCatalog.GetTools("我的分类")).Name);
        Assert.Equal("keep", Assert.Single(UserToolLibrary.GetEntries())["customField"]!.GetValue<string>());
        await ToolMetadataService.RemoveMetadataAsync(primary);
        ToolMetadataService.InvalidateCache();
        ToolCatalog.InvalidateTagsCache();
        Assert.Empty(ToolCatalog.GetTools("我的分类"));
        Assert.True(File.Exists(primary));
    }

    [Fact]
    public void LegacyMigrationFailure_IsRetried_AfterFileIsRepaired()
    {
        Exe(Path.Combine(_tools, "甲", "old"));
        File.WriteAllText(Path.Combine(_metadata, "tools.default.json"), "{\"tools\":[]}");
        var legacy = Path.Combine(_metadata, "tools.json");
        File.WriteAllText(legacy, "broken");
        Assert.Empty(ToolCatalog.GetTools("甲"));
        Assert.Equal("broken", File.ReadAllText(legacy));
        File.WriteAllText(legacy, "{\"tools\":[{\"match\":\"old\",\"name\":\"恢复\"}]}");
        ToolMetadataService.InvalidateCache();
        ToolCatalog.InvalidateTagsCache();
        Assert.Equal("恢复", Assert.Single(ToolCatalog.GetTools("甲")).Name);
    }

    [Fact]
    public void CommunityRecord_RecoversRegistration_WhenOldMetadataWasAlreadyOverwritten()
    {
        var directory = Path.Combine(_tools, "甲", "community-tool");
        var primary = Exe(directory, "selected.exe");
        CommunityToolRegistry.Upsert(new CommunityInstallRecord("community-tool", "甲", "sha", "1", "作者",
            null, null, "selected.exe", DateTimeOffset.UtcNow));
        ToolMetadataService.InvalidateCache();
        var item = Assert.Single(ToolCatalog.GetTools("甲"));
        Assert.Equal(primary, item.Path);
        Assert.Equal("community:community-tool", item.LibraryId);
    }

    [Fact]
    public void UserState_PreservesSorting_WithoutFreezingOfficialDefinition()
    {
        var directory = Path.Combine(_tools, "甲", "official");
        Exe(directory);
        var official = Path.Combine(_metadata, "tools.json");
        File.WriteAllText(official, """
            {"tools":[{"match":"official","order":9,"version":1,"downloadUrl":"old-url"}]}
            """);
        ToolMetadataService.InvalidateCache();
        ToolMetadataService.SaveToolOrder([directory]);
        ToolMetadataService.UpdateToolVersion("official", 4);
        Assert.Contains("old-url", File.ReadAllText(official));
        File.WriteAllText(official, """
            {"tools":[{"match":"official","order":99,"version":2,"downloadUrl":"new-url","description":"新版"}]}
            """);
        ToolMetadataService.InvalidateCache();
        var meta = ToolMetadataService.FindJsonMetadataByDir(directory)!;
        Assert.Equal(0, meta.Order);
        Assert.Equal(4, meta.ToolVersion);
        Assert.Equal("new-url", meta.DownloadUrl);
        Assert.Equal("新版", meta.Description);
    }

    [Fact]
    public async Task ParallelImports_DoNotLoseRegistrations()
    {
        var directories = Enumerable.Range(0, 20).Select(i => Path.Combine(ToolCatalog.UserToolsRoot, "Custom", "甲", "tool" + i)).ToList();
        foreach (var directory in directories) Exe(directory);
        await Task.WhenAll(directories.Select(directory => Task.Run(() =>
            ToolMetadataService.UpsertToolMetadataEntry(Path.GetFileName(directory), name: Path.GetFileName(directory),
                toolDirectory: directory, category: "甲"))));
        Assert.Equal(20, UserToolLibrary.GetEntries().Count);
    }

    [Fact]
    public void CorruptUserCatalog_IsNotOverwrittenByImport()
    {
        Directory.CreateDirectory(UserToolLibrary.DataDirectory);
        File.WriteAllText(UserToolLibrary.CatalogPath, "corrupt-user-data");
        Assert.ThrowsAny<Exception>(() => Register("甲", "new"));
        Assert.Equal("corrupt-user-data", File.ReadAllText(UserToolLibrary.CatalogPath));
    }

    [Fact]
    public void UserLibrary_IsPortableToAnotherDataDirectory()
    {
        var directory = Register("甲", "portable");
        var original = UserToolLibrary.DataDirectory;
        var destination = Path.Combine(_root, "MovedData");
        Directory.Move(original, destination);
        UserToolLibrary.RootOverride = destination;
        ToolMetadataService.InvalidateCache();
        ToolCatalog.InvalidateTagsCache();
        var item = Assert.Single(ToolCatalog.GetTools("甲"));
        Assert.StartsWith(destination, item.Path);
        Assert.True(File.Exists(item.Path));
        Assert.DoesNotContain(directory, File.ReadAllText(UserToolLibrary.CatalogPath));
    }

    [Fact]
    public async Task ConfigurationBackup_RestoresBothNewAndLegacyToolFiles_InAnotherDataDirectory()
    {
        Register("甲", "new");
        var legacyDirectory = Path.Combine(_tools, "乙", "legacy");
        var legacyExecutable = Exe(legacyDirectory);
        var candidate = Assert.Single(ToolRecoveryService.FindUnregistered());
        ToolRecoveryService.Register(candidate, "旧工具", legacyExecutable);
        FavoritesService.AddFavorite(legacyExecutable);
        var originalFavorites = File.ReadAllText(ConfigManager.GetFavoritesPath());
        var originalCatalog = File.ReadAllText(UserToolLibrary.CatalogPath);
        var output = Path.Combine(UserToolLibrary.DataDirectory, "backup.zip");
        Assert.True(await ConfigManager.ExportConfigAsync(output));
        Assert.Equal(originalCatalog, File.ReadAllText(UserToolLibrary.CatalogPath));
        Assert.Equal(originalFavorites, File.ReadAllText(ConfigManager.GetFavoritesPath()));
        using (var archive = ZipFile.OpenRead(output))
        {
            Assert.Single(archive.Entries, e => e.FullName == "user-tools.json");
            Assert.DoesNotContain(archive.Entries, e => e.FullName == "backup.zip");
        }
        var restored = Path.Combine(_root, "RestoredData");
        UserToolLibrary.RootOverride = restored;
        ConfigManager.DataDirectoryOverride = restored;
        Assert.True(await ConfigManager.ImportConfigAsync(output));
        var items = ToolCatalog.GetAllToolsCached();
        Assert.Equal(2, items.Count);
        Assert.All(items, item => { Assert.StartsWith(restored, item.Path); Assert.True(File.Exists(item.Path)); });
        Assert.True(FavoritesService.IsFavorite(Assert.Single(items, item => item.Name == "旧工具").Path));
    }

    [Fact]
    public void CommunityInstallDirectory_DoesNotCollideWithCustomToolOfSameName()
    {
        var custom = Register("甲", "same");
        var community = new CommunityTool { Id = "same", Name = "社区工具", Category = "甲" };
        var destination = CommunityToolInstallService.GetToolDirectory(community);
        Assert.NotEqual(custom, destination);
        Assert.Contains(Path.Combine("UserTools", "Community"), destination);
    }

    [Fact]
    public void UpdatingMigratedCommunityTool_PreservesItsDirectoryAndStableId()
    {
        var directory = Path.Combine(_tools, "甲", "community-tool");
        Exe(directory);
        File.WriteAllText(Path.Combine(_metadata, "tools.default.json"), "{\"tools\":[]}");
        File.WriteAllText(Path.Combine(_metadata, "tools.json"), "{\"tools\":[{\"match\":\"community-tool\",\"name\":\"旧社区工具\"}]}");
        ToolMetadataService.InvalidateCache();
        var id = Assert.Single(ToolMetadataService.GetUserTools()).Id;
        ToolMetadataService.UpsertToolMetadataEntry("community-tool", name: "更新后",
            toolDirectory: directory, category: "甲", communityId: "community-tool");
        var community = new CommunityTool { Id = "community-tool", Name = "社区工具", Category = "甲" };
        Assert.Equal(directory, CommunityToolInstallService.GetToolDirectory(community));
        Assert.Equal(id, Assert.Single(ToolMetadataService.GetUserTools()).Id);
    }

    [Fact]
    public void KernelUpdate_PreservesUnregisteredToolsAndConfiguration_WhileUpdatingOfficialFiles()
    {
        var official = Exe(Path.Combine(_tools, "甲", "official"));
        var extra = Exe(Path.Combine(_tools, "乙", "extra"));
        var settings = Path.Combine(_tools, "甲", "official", "user.ini");
        File.WriteAllText(settings, "user settings");
        var zipPath = Path.Combine(_root, "kernel.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("甲/official/main.exe").Open());
            writer.Write("updated official");
        }
        ZipExtractHelper.ExtractTolerantAndReplace(zipPath, _tools, ExtractReplaceProfile.ToolsBundle,
            preserveExistingFiles: true);
        Assert.Equal("updated official", File.ReadAllText(official));
        Assert.True(File.Exists(extra));
        Assert.Equal("user settings", File.ReadAllText(settings));
    }

    [Fact]
    public void KernelUpdate_PreservationFailure_LeavesOldDirectoryIntact()
    {
        var extra = Exe(Path.Combine(_tools, "甲", "extra"));
        var zipPath = Path.Combine(_root, "conflict.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("甲/extra").Open());
            writer.Write("file conflicting with existing directory");
        }
        Assert.Throws<IOException>(() => ZipExtractHelper.ExtractTolerantAndReplace(zipPath, _tools,
            ExtractReplaceProfile.ToolsBundle, preserveExistingFiles: true));
        Assert.True(File.Exists(extra));
    }
}
