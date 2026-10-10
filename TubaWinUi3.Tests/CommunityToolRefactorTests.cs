using System.Text.Json;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

// ---------- 安装记录（CommunityToolRegistry） ----------

[Collection("GlobalConfigTests")]
public class CommunityToolRegistryTests : IDisposable
{
    private readonly string _root;
    private readonly string _tools;
    private readonly string _registryRoot;

    public CommunityToolRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tubatest_" + Guid.NewGuid().ToString("N"));
        _tools = Path.Combine(_root, "Tools");
        _registryRoot = Path.Combine(_root, "CommunityTools");

        var metadata = Path.Combine(_root, "Metadata");
        Directory.CreateDirectory(metadata);
        File.WriteAllText(Path.Combine(metadata, "tools.json"), "{\"tools\":[]}");
        ToolMetadataService.SetMetadataRootForTests(metadata);
        ToolCatalog.SetToolsRootForBuild(_tools);
        CommunityToolRegistry.SetRootForTests(_registryRoot);
    }

    public void Dispose()
    {
        CommunityToolRegistry.SetRootForTests(null);
        ToolMetadataService.SetMetadataRootForTests(null);
        ToolCatalog.SetToolsRootForBuild(null);
        ToolCatalog.OnToolsChanged();
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Upsert_Then_TryGet_RoundTrips_IgnoringCase()
    {
        var record = new CommunityInstallRecord(
            "litemonitor", "综合检测", "sha1", "1.3.6", "luolangaga",
            "plugins/综合检测/litemonitor", "LiteMonitor.zip", "LiteMonitor.exe", DateTimeOffset.Now);

        CommunityToolRegistry.Upsert(record);

        var loaded = CommunityToolRegistry.TryGet("LITEMONITOR");
        Assert.NotNull(loaded);
        Assert.Equal("sha1", loaded!.Sha);
        Assert.Equal("1.3.6", loaded.Version);
        Assert.Equal("LiteMonitor.exe", loaded.LaunchTarget);
    }

    [Fact]
    public void Upsert_ReplacesSameTool_WithoutDuplication()
    {
        var first = new CommunityInstallRecord("a", "其他工具", "old", null, null, null, null, null, DateTimeOffset.Now);
        var second = new CommunityInstallRecord("a", "其他工具", "new", null, null, null, null, null, DateTimeOffset.Now);

        CommunityToolRegistry.Upsert(first);
        CommunityToolRegistry.Upsert(second);

        Assert.Equal("new", CommunityToolRegistry.TryGet("a")!.Sha);
    }

    [Fact]
    public void Remove_DeletesRecord()
    {
        CommunityToolRegistry.Upsert(new CommunityInstallRecord("a", "其他工具", null, null, null, null, null, null, DateTimeOffset.Now));
        CommunityToolRegistry.Remove("A");
        Assert.Null(CommunityToolRegistry.TryGet("a"));
    }

    [Fact]
    public void CorruptFile_TreatedAsEmpty_AndRecoversOnWrite()
    {
        Directory.CreateDirectory(_registryRoot);
        File.WriteAllText(Path.Combine(_registryRoot, "installed.json"), "{ not valid json !!");

        Assert.Null(CommunityToolRegistry.TryGet("anything"));

        CommunityToolRegistry.Upsert(new CommunityInstallRecord("a", "其他工具", null, null, null, null, null, null, DateTimeOffset.Now));
        Assert.NotNull(CommunityToolRegistry.TryGet("a"));
    }

    [Fact]
    public void PruneStale_RemovesRecordsWhoseDirectoryIsGone()
    {
        var toolDir = Path.Combine(_tools, "综合检测", "litemonitor");
        Directory.CreateDirectory(toolDir);
        File.WriteAllText(Path.Combine(toolDir, "LiteMonitor.exe"), "x");

        CommunityToolRegistry.Upsert(new CommunityInstallRecord("litemonitor", "综合检测", null, null, null, null, null, null, DateTimeOffset.Now));
        CommunityToolRegistry.Upsert(new CommunityInstallRecord("ghost", "综合检测", null, null, null, null, null, null, DateTimeOffset.Now));

        var pruned = CommunityToolRegistry.PruneStale();

        Assert.Equal(1, pruned);
        Assert.NotNull(CommunityToolRegistry.TryGet("litemonitor"));
        Assert.Null(CommunityToolRegistry.TryGet("ghost"));
    }
}

// ---------- 状态判定（更新检测） ----------

public class CommunityToolStatusTests
{
    [Theory]
    [InlineData(false, null, null, CommunityToolInstallStatus.NotInstalled)]
    [InlineData(false, "a", "b", CommunityToolInstallStatus.NotInstalled)]
    [InlineData(true, null, null, CommunityToolInstallStatus.Installed)]
    [InlineData(true, "sha", "sha", CommunityToolInstallStatus.Installed)]
    [InlineData(true, "SHA1", "sha1", CommunityToolInstallStatus.Installed)]
    [InlineData(true, "sha1", "sha2", CommunityToolInstallStatus.UpdateAvailable)]
    [InlineData(true, "sha1", null, CommunityToolInstallStatus.Installed)]
    [InlineData(true, null, "sha2", CommunityToolInstallStatus.Installed)]
    public void ResolveStatus_Matrix(bool installed, string? localSha, string? remoteSha, CommunityToolInstallStatus expected)
    {
        Assert.Equal(expected, CommunityToolInstallService.ResolveStatus(installed, localSha, remoteSha));
    }
}

// ---------- tools.json 写入 + 名称覆盖 ----------

[Collection("GlobalConfigTests")]
public class ToolMetadataCommunityTests : IDisposable
{
    private readonly string _root;
    private readonly string _tools;
    private readonly string _metadata;

    public ToolMetadataCommunityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tubatest_" + Guid.NewGuid().ToString("N"));
        _tools = Path.Combine(_root, "Tools");
        _metadata = Path.Combine(_root, "Metadata");

        CreateExe(Path.Combine(_tools, "综合检测", "litemonitor", "LiteMonitor.exe"));

        Directory.CreateDirectory(_metadata);
        File.WriteAllText(Path.Combine(_metadata, "tools.json"), """
        {
          "tools": [
            { "match": "existing", "description": "旧的", "customField": "keep-me" }
          ]
        }
        """);

        ToolCatalog.SetToolsRootForBuild(_tools);
        ToolMetadataService.SetMetadataRootForTests(_metadata);
    }

    public void Dispose()
    {
        ToolMetadataService.SetMetadataRootForTests(null);
        ToolCatalog.SetToolsRootForBuild(null);
        ToolCatalog.OnToolsChanged();
        try { Directory.Delete(_root, true); } catch { }
    }

    private static void CreateExe(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x4D, 0x5A, 0x00, 0x00]); // MZ 头占位
    }

    [Fact]
    public void Upsert_Entry_AppearsInCatalog_WithNameOverride()
    {
        ToolMetadataService.UpsertToolMetadataEntry(
            "litemonitor",
            name: "LiteMonitor 监控",
            description: "硬件监控",
            publisher: "Diorser",
            tags: ["硬件监控", "FPS"],
            launchTarget: "LiteMonitor.exe");

        ToolMetadataService.InvalidateCache();
        ToolCatalog.InvalidateTagsCache();

        var tool = Assert.Single(ToolCatalog.GetTools("综合检测"));

        Assert.Equal("LiteMonitor 监控", tool.Name);           // name 覆盖生效
        Assert.Equal("硬件监控", tool.Description);
        Assert.Equal("Diorser", tool.Publisher);
        Assert.Contains("硬件监控", tool.Tags);
        Assert.EndsWith("LiteMonitor.exe", tool.Path);
    }

    [Fact]
    public void Upsert_PreservesUnknownFields_AndExistingEntries()
    {
        var officialPath = Path.Combine(_metadata, "tools.json");
        var official = File.ReadAllText(officialPath);
        ToolMetadataService.UpsertToolMetadataEntry("litemonitor", name: "LiteMonitor");
        var userRoot = UserToolLibrary.ReadObject(UserToolLibrary.CatalogPath);
        userRoot["tools"]![0]!["customField"] = "keep-me";
        UserToolLibrary.WriteObject(UserToolLibrary.CatalogPath, userRoot);
        ToolMetadataService.UpsertToolMetadataEntry("litemonitor", name: "LiteMonitor 2");
        Assert.Equal(official, File.ReadAllText(officialPath));
        var tool = Assert.Single(UserToolLibrary.GetEntries());
        Assert.Equal("LiteMonitor 2", tool["name"]!.GetValue<string>());
        Assert.Equal("keep-me", tool["customField"]!.GetValue<string>());
    }

    [Fact]
    public void Upsert_ThenToolCatalog_WithoutEntry_NotListed()
    {
        // 未写条目时：目录里的 exe 未被收录 → 不出现（列表以 tools.json 为准）
        ToolCatalog.InvalidateTagsCache();
        ToolMetadataService.InvalidateCache();
        Assert.Empty(ToolCatalog.GetTools("综合检测"));
    }
}

// ---------- plugin.json 单源构建 ----------

public class CommunityPluginJsonTests
{
    [Fact]
    public void BuildPluginJson_UsesDraftFields()
    {
        var draft = new CommunityPluginDraft(
            Name: "CPU-Z",
            Description: "超频工具",
            Category: "处理器工具",
            Tags: ["CPU", "超频"],
            ZipFilePath: @"C:\tmp\cpuz.zip",
            LaunchTarget: "cpuz.exe",
            Publisher: null,
            Homepage: "https://example.com",
            Version: "2.09",
            IconFilePath: @"C:\tmp\icon.ico",
            DownloadUrl: null,
            DownloadFilter: null,
            ArchVariants: [new ImportArchVariant("bin/cpuz_x64.exe", "x64")]);

        var json = CommunityToolService.BuildPluginJson(draft, "tester");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("cpu-z", root.GetProperty("id").GetString());
        Assert.Equal("CPU-Z", root.GetProperty("name").GetString());
        Assert.Equal("2.09", root.GetProperty("version").GetString());
        Assert.Equal("tester", root.GetProperty("author").GetString());
        Assert.Equal("cpuz.zip", root.GetProperty("file").GetString());
        Assert.Equal("cpuz.exe", root.GetProperty("launchTarget").GetString());
        Assert.Equal("icon.ico", root.GetProperty("icon").GetString());
        Assert.Equal("https://example.com", root.GetProperty("homepage").GetString());

        var variants = root.GetProperty("archVariants");
        Assert.Equal("bin/cpuz_x64.exe", variants[0].GetProperty("file").GetString());
        Assert.Equal("x64", variants[0].GetProperty("arch").GetString());

        // publish 未提供：序列化要么省略、要么为 null（两种都视为未设置）
        if (root.TryGetProperty("publisher", out var publisher))
            Assert.Equal(JsonValueKind.Null, publisher.ValueKind);
    }

    [Fact]
    public void ParseTagList_SplitsChineseAndEnglishSeparators()
    {
        var tags = CommunityToolService.ParseTagList("CPU, 跑分；稳定性测试，  ");
        Assert.Equal(["CPU", "跑分", "稳定性测试"], tags);
    }
}

// ---------- 卡片网格尺寸公式 ----------

public class ToolCardLayoutTests
{
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(-100, 1, 0)]
    [InlineData(200, 1, 280)]      // 窄于最小宽度：单列、保持最小宽度（与既有一致）
    [InlineData(280, 1, 280)]
    [InlineData(572, 2, 280)]
    [InlineData(584, 2, 286)]
    [InlineData(2000, 6, 323.3)]   // 6 列：(2000-5×12)/6
    public void Compute_NormalMode(double available, int expectedColumns, double expectedWidth)
    {
        var (columns, itemWidth) = ToolCardLayout.Compute(available, ToolCardLayout.NormalMinItemWidth, ToolCardLayout.NormalSpacing);

        Assert.Equal(expectedColumns, columns);
        Assert.Equal(expectedWidth, itemWidth, precision: 1);
    }

    [Fact]
    public void Compute_CompactMode()
    {
        var (columns, itemWidth) = ToolCardLayout.Compute(500, ToolCardLayout.CompactMinItemWidth, ToolCardLayout.CompactSpacing);

        Assert.Equal(4, columns);
        Assert.Equal(117.5, itemWidth, precision: 1);
    }
}
