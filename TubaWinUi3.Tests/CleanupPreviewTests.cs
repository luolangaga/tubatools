using FluentCleaner.Models;
using TubaWinUi3.Services.SpaceCleanup;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 垃圾清理「清理明细」的数据整理：必须展示具体目标目录、目录分组和文件/注册表项。
/// </summary>
public sealed class CleanupPreviewTests
{
    private const string Root = @"C:\Users\tester\AppData\Local\Foo";

    private static ScanResult CreateResult()
    {
        var entry = new CleanerEntry
        {
            Name = "测试规则",
            FileKeys = [FileKeyEntry.Parse(Root + @"|*.log|RECURSE")],
        };
        var result = new ScanResult { Entry = entry };
        result.FilesToDelete.Add(Root + @"\a.log");
        result.FilesToDelete.Add(Root + @"\Sub\b.log");
        result.FilesToDelete.Add(@"C:\Other\c.log");
        result.RegistryToDelete.Add(new RegistryItemToDelete { KeyPath = @"HKCU\Software\Foo" });
        return result;
    }

    [Fact]
    public void Targets_ExpandFileKeysAndCountMatchingFiles()
    {
        var targets = CleanupPreview.GetTargets(CreateResult());

        var target = Assert.Single(targets);
        Assert.Equal(Root, target.Path);
        Assert.Equal(2, target.FileCount);
        Assert.Contains("子目录", target.Scope);
        Assert.Equal("*.log", target.Pattern);
    }

    [Fact]
    public void DirectoryGroups_GroupFilesByDirectory()
    {
        var groups = CleanupPreview.GetDirectoryGroups(CreateResult());

        Assert.Equal(3, groups.Count);
        var foo = Assert.Single(groups, g => g.Directory == Root);
        Assert.Equal(1, foo.TotalFiles);
        Assert.Equal(["a.log"], foo.Files);
        var sub = Assert.Single(groups, g => g.Directory == Root + @"\Sub");
        Assert.Equal(["b.log"], sub.Files);
    }

    [Fact]
    public void BuildText_ShowsTargetsFilesAndRegistry()
    {
        var row = new CleanupRow
        {
            Name = "测试条目",
            Category = "测试分类",
            Detail = "3 个文件 · 1 个注册表项目",
            Bytes = 1024,
            Junk = CreateResult(),
        };

        var text = CleanupPreview.BuildText([row]);

        Assert.Contains("条目：测试条目", text);
        Assert.Contains("将删除以下位置", text);
        Assert.Contains(Root, text);
        Assert.Contains("文件（共 3 个）", text);
        Assert.Contains("a.log", text);
        Assert.Contains("b.log", text);
        Assert.Contains(@"HKCU\Software\Foo", text);
    }

    [Fact]
    public void BuildText_WithoutScanResultExplainsMissingDetails()
    {
        var row = new CleanupRow
        {
            Name = "测试条目",
            Category = "测试分类",
            Detail = "0 个文件 · 0 个注册表项目",
        };

        var text = CleanupPreview.BuildText([row]);

        Assert.Contains("没有可预览的文件明细", text);
    }
}
