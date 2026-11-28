using System.Text;
using TubaWinUi3.PluginSdk;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public sealed class IconStudioTests
{
    [Fact]
    public void ClassicBuiltinAliasesMapToOldAssets()
    {
        Assert.Equal("junk-cleaner", IconStudioCatalog.ClassicBuiltinAssetId("space-cleanup"));
        Assert.Equal("community-tools", IconStudioCatalog.ClassicBuiltinAssetId("plugins"));
        Assert.Equal("cpu-ranking", IconStudioCatalog.ClassicBuiltinAssetId("CPU-RANKING"));
        Assert.Null(IconStudioCatalog.ClassicBuiltinAssetId("plugin:some-tool"));
        Assert.Null(IconStudioCatalog.ClassicBuiltinAssetId(""));
    }

    [Fact]
    public void DialogTitleCategoriesMatchExistingKeywordOrder()
    {
        Assert.Equal("download", IconStudioCatalog.DialogCategoryForTitle("下载最新版本"));
        Assert.Equal("network", IconStudioCatalog.DialogCategoryForTitle("加入联机房间"));
        Assert.Equal("phone", IconStudioCatalog.DialogCategoryForTitle("连接手机"));
        Assert.Equal("ai", IconStudioCatalog.DialogCategoryForTitle("AI 模型提供商"));
        Assert.Equal("storage", IconStudioCatalog.DialogCategoryForTitle("清理磁盘空间"));
        Assert.Equal("settings", IconStudioCatalog.DialogCategoryForTitle("配置管理"));
        Assert.Equal("file", IconStudioCatalog.DialogCategoryForTitle("选择文件目录"));
        Assert.Equal("export", IconStudioCatalog.DialogCategoryForTitle("导出并保存"));
        Assert.Equal("import", IconStudioCatalog.DialogCategoryForTitle("导入数据"));
        Assert.Equal("security", IconStudioCatalog.DialogCategoryForTitle("权限拦截确认"));
        Assert.Equal("info", IconStudioCatalog.DialogCategoryForTitle("提示"));
        Assert.Equal("download", IconStudioCatalog.DialogCategoryForTitle("Download update"));
    }

    [Fact]
    public void ClassicNavigationCoversPrimaryAndCategoryTags()
    {
        Assert.Equal("Assets/IconStyles/Classic/Navigation/home.svg", IconStudioCatalog.ClassicNavigationRelativePath("home"));
        Assert.Equal("Assets/IconStyles/Classic/Builtin/cpu-ranking.svg", IconStudioCatalog.ClassicNavigationRelativePath("处理器"));
        Assert.Equal("Assets/IconStyles/Classic/Navigation/display.svg", IconStudioCatalog.ClassicNavigationRelativePath("显示器"));
        Assert.Equal("Assets/IconStyles/Classic/Builtin/community-tools.svg", IconStudioCatalog.ClassicNavigationRelativePath("综合"));
        Assert.Equal("Assets/IconStyles/Classic/Navigation/other.svg", IconStudioCatalog.ClassicNavigationRelativePath("未知分类"));
        Assert.Equal("Assets/IconStyles/Classic/Builtin/community-tools.svg", IconStudioCatalog.ClassicNavigationRelativePath("plugin-page:plugins"));
    }

    [Fact]
    public void DialogSlotsAreUniqueAndComplete()
    {
        var ids = IconStudioCatalog.DialogSlots.Select(s => s.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("download", ids);
        Assert.Contains("phone", ids);
        Assert.Contains("info", ids);
        Assert.All(IconStudioCatalog.DialogSlots, slot =>
        {
            Assert.False(string.IsNullOrWhiteSpace(slot.Glyph));
            Assert.StartsWith("Assets/IconStyles/Classic/", slot.ClassicRelativePath);
        });
    }

    [Fact]
    public void ImporterStoresRasterAndSvgByNameHash()
    {
        var root = TempRoot();
        var previous = ConfigManager.DataDirectoryOverride;
        try
        {
            ConfigManager.DataDirectoryOverride = root;
            var sourceRoot = Path.Combine(root, "sources");
            Directory.CreateDirectory(sourceRoot);

            var pngPath = Path.Combine(sourceRoot, "tiny.png");
            File.WriteAllBytes(pngPath, TinyPng);
            var importedPng = IconStudioImageImporter.Import(pngPath);
            Assert.True(File.Exists(importedPng));
            Assert.StartsWith("icon-", Path.GetFileName(importedPng));
            Assert.EndsWith(".png", importedPng);

            var svgPath = Path.Combine(sourceRoot, "tiny.svg");
            File.WriteAllText(svgPath, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\"/>", Encoding.UTF8);
            var importedSvg = IconStudioImageImporter.Import(svgPath);
            Assert.True(File.Exists(importedSvg));
            Assert.EndsWith(".svg", importedSvg);
        }
        finally
        {
            ConfigManager.DataDirectoryOverride = previous;
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ImporterRejectsUnsupportedAndPrunesOrphans()
    {
        var root = TempRoot();
        var previous = ConfigManager.DataDirectoryOverride;
        try
        {
            ConfigManager.DataDirectoryOverride = root;
            var sourceRoot = Path.Combine(root, "sources");
            Directory.CreateDirectory(sourceRoot);
            var textPath = Path.Combine(sourceRoot, "not-an-image.txt");
            File.WriteAllText(textPath, "hello");
            Assert.Throws<InvalidDataException>(() => IconStudioImageImporter.Import(textPath));

            var pngA = Path.Combine(sourceRoot, "a.png");
            var pngB = Path.Combine(sourceRoot, "b.png");
            File.WriteAllBytes(pngA, TinyPng);
            File.WriteAllBytes(pngB, TinyPng);
            var a = IconStudioImageImporter.Import(pngA);
            var b = IconStudioImageImporter.Import(pngB);
            Assert.Equal(a, b); // 内容相同按 SHA-256 去重。

            IconStudioImageImporter.Prune([]);
            Assert.False(File.Exists(a));
        }
        finally
        {
            ConfigManager.DataDirectoryOverride = previous;
            Directory.Delete(root, true);
        }
    }

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "tuba-iconstudio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    // 1×1 透明 PNG；只用于验证 GDI+ 解码与哈希去重，不依赖图形库生成。
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Zl1sAAAAASUVORK5CYII=");
}
