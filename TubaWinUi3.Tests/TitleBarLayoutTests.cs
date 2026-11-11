using System.Runtime.CompilerServices;
using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 自定义标题栏的可用宽度预算（Issue #262）。
///
/// TitleBar 控件会把 Content 里每个交互控件的布局矩形原样登记成「直通区域」，不做裁剪；
/// 标题栏内容一旦宽于 Content 列宽，就会盖住最小拖拽区和系统最小化/最大化/关闭按钮。
/// 这组用例锁定「任何列宽下内容都不溢出」这条不变量，以及 XAML 里对应的接线
/// （固定 320 的搜索框列、缺失的拖拽标记都会让问题复发）。
/// </summary>
public class TitleBarLayoutTests
{
    private static readonly string AppRoot = Path.Combine(FindRepoRoot(), "TubaWinUi3.WinUI3");

    private static string FindRepoRoot([CallerFilePath] string callerFilePath = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(callerFilePath), AppContext.BaseDirectory })
        {
            if (string.IsNullOrEmpty(start)) continue;

            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "TubaWinUi3.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        throw new InvalidOperationException("未找到仓库根目录（TubaWinUi3.sln）");
    }

    private static double UsedWidth(TitleBarLayout.Presentation layout)
        => layout.ShowSearchBox
            ? layout.SearchBoxWidth + TitleBarLayout.SearchGap
              + (layout.ShowActionButtons ? TitleBarLayout.ActionButtonsWidth : 0)
            : 0;

    // ---------- 布局决策 ----------

    [Theory]
    [InlineData(500)]
    [InlineData(800)]
    [InlineData(3840)]
    public void WideContent_KeepsDesignSearchWidthAndShowsButtons(double width)
    {
        var layout = TitleBarLayout.ForContentWidth(width);

        Assert.True(layout.ShowSearchBox);
        Assert.True(layout.ShowActionButtons);
        Assert.Equal(TitleBarLayout.SearchMaxWidth, layout.SearchBoxWidth);
    }

    [Fact]
    public void NarrowContent_ShrinksSearchBoxBeforeHidingAnything()
    {
        // 两个按钮 + 间距 + 最小搜索框刚好放得下：搜索框收到最小宽度，按钮仍在
        var atThreshold = TitleBarLayout.ForContentWidth(
            TitleBarLayout.MinSearchWidth + TitleBarLayout.SearchGap + TitleBarLayout.ActionButtonsWidth);

        Assert.True(atThreshold.ShowSearchBox);
        Assert.True(atThreshold.ShowActionButtons);
        Assert.Equal(TitleBarLayout.MinSearchWidth, atThreshold.SearchBoxWidth);

        var shrunk = TitleBarLayout.ForContentWidth(280);

        Assert.True(shrunk.ShowSearchBox);
        Assert.True(shrunk.ShowActionButtons);
        Assert.Equal(280 - TitleBarLayout.SearchGap - TitleBarLayout.ActionButtonsWidth, shrunk.SearchBoxWidth);
        Assert.True(shrunk.SearchBoxWidth < TitleBarLayout.SearchMaxWidth);
    }

    [Fact]
    public void VeryNarrow_HidesActionButtonsBeforeSearchBox()
    {
        var layout = TitleBarLayout.ForContentWidth(160);

        Assert.True(layout.ShowSearchBox);
        Assert.False(layout.ShowActionButtons);
        Assert.Equal(160 - TitleBarLayout.SearchGap, layout.SearchBoxWidth);
    }

    [Theory]
    [InlineData(TitleBarLayout.MinSearchWidth + TitleBarLayout.SearchGap - 1)]
    [InlineData(60)]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(double.NaN)]
    public void TooNarrowOrInvalid_HidesAllContent(double width)
    {
        // 什么都不显示时没有直通区域：整条标题栏都能拖动，系统标题按钮也不会被盖住
        var layout = TitleBarLayout.ForContentWidth(width);

        Assert.False(layout.ShowSearchBox);
        Assert.False(layout.ShowActionButtons);
    }

    [Fact]
    public void ContentNeverExceedsAvailableWidth()
    {
        for (var width = 0.0; width <= 2000; width += 1)
        {
            var layout = TitleBarLayout.ForContentWidth(width);
            Assert.True(UsedWidth(layout) <= width + 1e-9, $"列宽 {width} 时内容 {UsedWidth(layout)} 溢出");
        }
    }

    // ---------- XAML 接线 ----------

    [Fact]
    public void MainWindowTitleBar_ContentIsShrinkableAndExplicitlyDraggable()
    {
        var xaml = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.xaml"));

        // 内容根节点必须是拖拽区；交互控件要显式排除（否则会被 IsDragRegion="True" 吞掉点击）
        Assert.Contains("x:Name=\"TitleBarContent\"", xaml);
        Assert.Contains("TitleBar.IsDragRegion=\"True\"", xaml);
        Assert.Contains("AutoRefreshDragRegions=\"True\"", xaml);
        Assert.Contains("x:Name=\"SearchBox\"", xaml);
        Assert.Contains("TitleBar.IsDragRegion=\"False\"", xaml);
        Assert.Contains("TitleBarContentHorizontalAlignment", xaml);

        // 搜索框不能再包在固定 320 的列里 —— 那正是窗口变窄时内容溢出的根因
        Assert.DoesNotContain("<ColumnDefinition Width=\"320\" />", xaml);
        Assert.Contains("SizeChanged=\"TitleBarContent_SizeChanged\"", xaml);
    }

    [Fact]
    public void MainWindowCode_AppliesLayoutBudgetOnContentSizeChange()
    {
        var code = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.xaml.cs"));
        Assert.Contains("TitleBarLayout.ForContentWidth(", code);
    }
}
