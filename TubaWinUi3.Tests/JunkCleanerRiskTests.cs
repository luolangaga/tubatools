using FluentCleaner.Models;
using FluentCleaner.Services;
using TubaWinUi3.Services.SpaceCleanup;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 垃圾清理的风险分级与默认勾选策略（Issue #257：下载文件夹曾被默认勾选后直接清空）。
/// </summary>
public class JunkCleanerRiskTests
{
    private static readonly SensitiveFolder[] Personal =
    [
        new("下载", @"C:\Users\tester\Downloads"),
        new("文档", @"C:\Users\tester\Documents"),
    ];

    private static readonly SensitiveFolder[] Protected =
    [
        new("用户目录", @"C:\Users\tester"),
        new("磁盘根目录", @"C:\"),
    ];

    private static CleanerEntry Entry(string name, params FileKeyEntry[] keys) => new()
    {
        Name = name,
        FileKeys = [.. keys],
    };

    private static JunkRisk Evaluate(CleanerEntry entry, ScanResult? result = null)
        => JunkRiskClassifier.EvaluateCore(entry, result, Personal, Protected);

    [Fact]
    public void AllFilesInDownloadsRoot_IsDangerous()
    {
        var entry = Entry("下载文件夹", FileKeyEntry.Parse(@"C:\Users\tester\Downloads|*|RECURSE"));

        var risk = Evaluate(entry);

        Assert.Equal(JunkRiskLevel.Dangerous, risk.Level);
        Assert.Contains("下载", risk.Reason);
        Assert.Contains("全部文件", risk.Reason);
    }

    [Fact]
    public void WildcardChildOfDownloads_StillCountsAsDownloads()
    {
        var entry = Entry("下载子目录", FileKeyEntry.Parse(@"C:\Users\tester\Downloads\*|*"));

        Assert.Equal(JunkRiskLevel.Dangerous, Evaluate(entry).Level);
    }

    [Fact]
    public void NarrowFilterInPersonalFolder_IsCautionOnly()
    {
        var entry = Entry("文档临时文件", FileKeyEntry.Parse(@"C:\Users\tester\Documents|*.tmp"));

        var risk = Evaluate(entry);

        Assert.Equal(JunkRiskLevel.Caution, risk.Level);
        Assert.Contains("文档", risk.Reason);
    }

    [Fact]
    public void AppCacheFolder_IsNormal()
    {
        var entry = Entry("缓存", FileKeyEntry.Parse(@"C:\Users\tester\AppData\Local\Foo\Cache|*|RECURSE"));

        Assert.Equal(JunkRiskLevel.Normal, Evaluate(entry).Level);
    }

    [Fact]
    public void DriveAndUserProfileRoots_AreDangerous()
    {
        Assert.Equal(JunkRiskLevel.Dangerous,
            Evaluate(Entry("整盘", FileKeyEntry.Parse(@"C:\|*.bak|RECURSE"))).Level);
        Assert.Equal(JunkRiskLevel.Dangerous,
            Evaluate(Entry("用户目录", FileKeyEntry.Parse(@"C:\Users\tester|*.tmp|RECURSE"))).Level);
    }

    [Fact]
    public void WarningAndRegistry_RaiseCaution()
    {
        var entry = Entry("带警告", FileKeyEntry.Parse(@"C:\Users\tester\AppData\Local\Foo|*.log"));
        entry.Warning = "Removes saved passwords";
        var result = new ScanResult { Entry = entry };
        result.RegistryToDelete.Add(new RegistryItemToDelete { KeyPath = @"HKCU\Software\Foo" });

        var risk = Evaluate(entry, result);

        Assert.Equal(JunkRiskLevel.Caution, risk.Level);
        Assert.Contains("Removes saved passwords", risk.Reason);
        Assert.Contains("注册表", risk.Reason);
    }

    [Fact]
    public void ExplicitDefault_OnlyPreselectedForNormalRisk()
    {
        Assert.True(JunkRiskClassifier.IsDefaultSelected(true, JunkRisk.None));
        Assert.False(JunkRiskClassifier.IsDefaultSelected(false, JunkRisk.None));
        Assert.False(JunkRiskClassifier.IsDefaultSelected(true, new JunkRisk(JunkRiskLevel.Caution, "w")));
        Assert.False(JunkRiskClassifier.IsDefaultSelected(true, new JunkRisk(JunkRiskLevel.Dangerous, "w")));
    }

    [Fact]
    public void CleanerEntry_DefaultIsOptIn()
    {
        Assert.False(new CleanerEntry().Default);

        var entry = Assert.Single(new Winapp2Parser().Parse("""
            [No Default Key]
            DetectFile=C:\Foo
            FileKey1=C:\Foo\Cache|*.*
            """));

        Assert.False(entry.Default);
    }
}
