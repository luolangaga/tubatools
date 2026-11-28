using System.Text.RegularExpressions;

namespace TubaWinUi3.Tests;

/// <summary>卡片重排的接线不变量：所有卡片宽度都必须走统一的调度入口。</summary>
public class CardGridReflowWiringTests
{
    private static readonly string AppRoot = TestPaths.AppRoot;

    [Fact]
    public void Pages_NeverAssignItemWidthDirectly()
    {
        // 直接写 ItemsWrapGrid.ItemWidth 会逐像素重排整张网格，并绕过节流/滑入动画。
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(AppRoot, "Pages"), "*.cs", SearchOption.AllDirectories))
        {
            if (Regex.IsMatch(File.ReadAllText(file), @"\.ItemWidth\s*="))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.True(offenders.Count == 0,
            "卡片宽度请走 CardGridReflow.Request（节流 + 换列动画），不要直接写 ItemsWrapGrid.ItemWidth：" +
            string.Join(", ", offenders));
    }

    [Fact]
    public void ToolCardLayout_AppliesThroughReflowScheduler()
    {
        var code = File.ReadAllText(Path.Combine(AppRoot, "Services", "ToolCardLayout.cs"));
        Assert.Contains("CardGridReflow.Request(grid, itemWidth)", code);
        Assert.DoesNotContain(".ItemWidth = ", code);
    }

    [Fact]
    public void ReflowAnimatesRenderLayerOnly()
    {
        // 动画必须只改合成层（Translation/Scale），不能改布局属性或挂 ChildrenTransitions，
        // 否则会和 ItemsWrapGrid 的重排形成布局/动画循环（CardBoard 的既有教训，Issue #266）。
        var code = File.ReadAllText(Path.Combine(AppRoot, "Services", "CardGridReflow.cs"));
        Assert.Contains("StartAnimation(\"Translation\"", code);
        Assert.Contains("StartAnimation(\"Scale\"", code);
        Assert.DoesNotContain("RepositionThemeTransition", code);
        Assert.DoesNotContain("ChildrenTransitions", code);
        Assert.DoesNotContain("InvalidateMeasure", code);
    }
}
