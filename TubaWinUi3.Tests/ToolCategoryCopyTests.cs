using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

// 依赖全局静态配置（ToolCatalog/ToolMetadataService）的测试，与同类测试串行执行
[Collection("GlobalConfigTests")]
public class ToolCategoryCopyTests : IDisposable
{
    private readonly string _root;
    private readonly string _tools;
    private readonly string _metadata;

    public ToolCategoryCopyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tubatest_" + Guid.NewGuid().ToString("N"));
        _tools = Path.Combine(_root, "Tools");
        _metadata = Path.Combine(_root, "Metadata");

        // 物理布局：只建主分类目录；「显卡工具」「烤鸡工具」无物理目录（纯副本分类）
        CreateExe(Path.Combine(_tools, "综合检测", "AIDA64", "AIDA64.exe"));
        CreateExe(Path.Combine(_tools, "内存工具", "memtest", "memtest.exe"));
        CreateExe(Path.Combine(_tools, "内存工具", "memtest64", "memtest64.exe"));
        CreateExe(Path.Combine(_tools, "内存工具", "memtestpro", "memtestpro.exe"));

        Directory.CreateDirectory(_metadata);
        var json = """
        {
          "tools": [
            { "match": "AIDA64", "category": "综合检测", "categories": ["处理器工具", "显卡工具"], "order": 1, "description": "测试AIDA64", "downloadUrl": "gc:Tools/综合检测/AIDA64" },
            { "match": "memtest", "category": "内存工具", "categories": ["烤鸡工具"] },
            { "match": "一键双烤", "builtin": "stress-test", "categories": ["烤鸡工具"] }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(_metadata, "tools.json"), json);

        ToolCatalog.SetToolsRootForBuild(_tools);
        ToolMetadataService.SetMetadataRootForTests(_metadata);

        // 应用启动时注册的内置工具在测试环境需手动注册（已注册则跳过）
        if (BuiltinToolRegistry.GetById("stress-test") is null)
            BuiltinToolRegistry.RegisterDefaults();
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
    public void Copy_AppearsInExtraCategory_WithoutPhysicalDir()
    {
        var items = ToolCatalog.GetTools("显卡工具"); // 无物理目录，纯副本分类

        var copy = Assert.Single(items);
        // 显示名经 StripArchSuffix："AIDA64" 目录名被剥掉 "64" 后缀（既有架构命名行为）
        Assert.Equal("AIDA", copy.Name);
        Assert.True(copy.IsLinked);
        Assert.Equal("综合检测", copy.PrimaryCategory);
        Assert.Contains("显卡工具", copy.Categories);
        Assert.Equal(1, copy.SortOrder);
        Assert.Equal("测试AIDA64", copy.Description);
        Assert.Equal(Path.Combine(_tools, "综合检测", "AIDA64", "AIDA64.exe"), copy.Path);
    }

    [Fact]
    public void PrimaryCategory_ShowsPhysicalItem_NotCopy()
    {
        var items = ToolCatalog.GetTools("综合检测");

        var aida = items.Where(t => t.Path!.EndsWith("AIDA64.exe")).ToList();
        Assert.Single(aida);
        Assert.False(aida[0].IsLinked); // 物理项，不是副本
        Assert.Equal("综合检测", aida[0].Category);
    }

    [Fact]
    public void Copy_PrefixOverlapDirs_ResolveExactMatch()
    {
        var items = ToolCatalog.GetTools("烤鸡工具");

        // memtest / memtest64 / memtestpro 都包含 "memtest"，评分制必须选中精确目录
        var copy = items.Single(t => t.IsLinked);
        Assert.Equal("memtest", copy.Name);
        Assert.Equal("内存工具", copy.PrimaryCategory);
        Assert.Equal(Path.Combine(_tools, "内存工具", "memtest", "memtest.exe"), copy.Path);
    }

    [Fact]
    public void ReorderByName_DuplicateNamesInSavedOrder_NoDuplicateItems()
    {
        // 用户真实 settings.json 的 ToolOrder_烤鸡工具 曾含重复的 "memtest"，
        // 旧实现按名字 First 选取会把同一工具返回两次（页面出现重复卡片）
        var memtest = new ToolItem { Name = "memtest", Category = "烤鸡工具", Path = "p1", RelativePath = "r1", Extension = "exe" };
        var builtin = new ToolItem { Name = "一键三烤", Category = "烤鸡工具", Path = "p2", RelativePath = "r2", Extension = "内置" };

        var reordered = ToolCatalog.ReorderByName([memtest, builtin], ["memtest", "memtest", "一键三烤"]);

        Assert.Equal(2, reordered.Count);
        Assert.Same(memtest, reordered[0]);
        Assert.Same(builtin, reordered[1]);
    }

    [Fact]
    public void BuiltinPlacement_AppearsWithVirtualDirKey()
    {
        var items = ToolCatalog.GetTools("烤鸡工具");

        var builtin = items.Single(t => t.IsBuiltinLink);
        Assert.Equal("stress-test", builtin.BuiltinToolId);
        // 虚拟目录键：ToolsRoot/分类/目录名（与旧 link.json 时代收藏/排序键一致）
        Assert.Equal(Path.Combine(_tools, "烤鸡工具", "一键双烤"), builtin.Path);
        Assert.Equal("烤鸡工具", builtin.Category);
    }

    [Fact]
    public void CopyCategory_LeftoverEmptyDir_DoesNotDuplicate()
    {
        // 模拟构建残留：输出目录 Tools 里同名空壳目录（旧 link.json 时代产物，
        // 源码删除后 MSBuild 拷贝只增不删），条目有 downloadUrl 时曾误建无图标占位条目
        Directory.CreateDirectory(Path.Combine(_tools, "处理器工具", "AIDA64"));

        var items = ToolCatalog.GetTools("处理器工具");

        var aida = items.Where(t => t.Name!.Equals("AIDA", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(aida); // 只保留 tools.json 副本合成条目，占位条目被避让
        Assert.True(aida[0].IsLinked);
    }
}
