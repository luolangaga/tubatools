using System.Runtime.CompilerServices;
using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 自定义标题栏的可用宽度预算（Issue #262）。
///
/// TitleBar 控件会把 Content 里每个交互控件的布局矩形原样登记成「直通区域」，不做裁剪；
/// 标题栏内容一旦宽于 Content 列宽，就会盖住最小拖拽区和系统最小化/最大化/关闭按钮。
/// 标题栏左侧现在是资源管理器式标签条、右侧固定「搜索 / 教程 / 下载」，
/// 这组用例锁定「任何列宽下内容都不溢出」以及标签条优先这条不变量。
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

    /// <summary>一段布局实际占用的水平空间。什么都没显示时右侧面板整体收起，内边距也不占位。</summary>
    private static double UsedWidth(TitleBarLayout.Presentation layout)
    {
        if (!layout.ShowTabStrip && !layout.ShowSearchBox && !layout.ShowActionButtons) return 0;

        var used = TitleBarLayout.RightInset;
        if (layout.ShowTabStrip)
            used += layout.TabStripWidth + TitleBarLayout.TabStripMargin
                  + (layout.ShowSearchBox || layout.ShowActionButtons ? TitleBarLayout.TabGap : 0);
        if (layout.ShowSearchBox) used += layout.SearchBoxWidth + (layout.ShowActionButtons ? TitleBarLayout.SearchGap : 0);
        if (layout.ShowActionButtons) used += TitleBarLayout.ActionButtonsWidth;
        return used;
    }

    // ---------- 布局决策 ----------

    [Theory]
    [InlineData(900)]
    [InlineData(1280)]
    [InlineData(3840)]
    public void WideContent_ShowsTabStripFullSearchAndButtons(double width)
    {
        var layout = TitleBarLayout.ForContentWidth(width);

        Assert.True(layout.ShowTabStrip);
        Assert.True(layout.ShowSearchBox);
        Assert.True(layout.ShowActionButtons);
        Assert.Equal(TitleBarLayout.SearchMaxWidth, layout.SearchBoxWidth);
    }

    [Fact]
    public void TabStripUsesSpareTitleBarSpaceBeforeScrolling()
    {
        // 宽窗口：标签条超过设计宽度 520 后不再硬顶，先把中间的空闲（原本只是拖拽区）用掉，
        // 开更多标签也不用立刻进左右滚动；但拖拽区不会小于 MinTitleBarDragWidth。
        var wide = TitleBarLayout.ForContentWidth(1600);

        Assert.True(wide.ShowTabStrip);
        Assert.True(wide.TabStripWidth > TitleBarLayout.TabStripMaxWidth);
        Assert.True(1600 - UsedWidth(wide) >= TitleBarLayout.MinTitleBarDragWidth - 1e-6);

        // 更宽也不会无限膨胀，同样给标题栏留出拖拽宽度
        var huge = TitleBarLayout.ForContentWidth(3840);
        Assert.True(3840 - UsedWidth(huge) >= TitleBarLayout.MinTitleBarDragWidth - 1e-6);
    }

    [Fact]
    public void MediumWindow_KeepsTabStripAtDesignWidth()
    {
        // 900 以内还没有可以借用的空白：标签条停在设计宽度，右侧面板与拖拽区都不受影响
        var layout = TitleBarLayout.ForContentWidth(900);

        Assert.Equal(TitleBarLayout.TabStripMaxWidth, layout.TabStripWidth);
        Assert.Equal(TitleBarLayout.SearchMaxWidth, layout.SearchBoxWidth);
        Assert.True(900 - UsedWidth(layout) > 0);
    }

    [Fact]
    public void NarrowContent_ShrinksSearchBoxBeforeHidingIt()
    {
        // 标签条 + 两个按钮 + 搜索框刚好还有余量的区间：搜索框在最小与设计宽度之间伸缩
        var layout = TitleBarLayout.ForContentWidth(700);

        Assert.True(layout.ShowSearchBox);
        Assert.True(layout.ShowActionButtons);
        Assert.InRange(layout.SearchBoxWidth, TitleBarLayout.MinSearchWidth, TitleBarLayout.SearchMaxWidth);
    }

    [Fact]
    public void TabStripTakesPrecedenceOverSearchBox()
    {
        // 空间不足以同时容纳标签条和搜索框：宁可收起搜索框，也保留标签条
        var width = TitleBarLayout.RightInset + TitleBarLayout.TabStripMinWidth + TitleBarLayout.TabStripMargin
                  + TitleBarLayout.TabGap + TitleBarLayout.ActionButtonsWidth
                  + TitleBarLayout.MinSearchWidth + TitleBarLayout.SearchGap - 1;

        var layout = TitleBarLayout.ForContentWidth(width);

        Assert.True(layout.ShowTabStrip);
        Assert.False(layout.ShowSearchBox);
    }

    [Fact]
    public void VeryNarrow_DropsTabStripAndEverythingElse()
    {
        var layout = TitleBarLayout.ForContentWidth(TitleBarLayout.TabStripMinWidth);

        Assert.False(layout.ShowTabStrip);
        Assert.False(layout.ShowSearchBox);
        Assert.False(layout.ShowActionButtons);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(double.NaN)]
    public void InvalidWidth_HidesAllContent(double width)
    {
        // 什么都不显示时没有直通区域：整条标题栏都能拖动，系统标题按钮也不会被盖住
        var layout = TitleBarLayout.ForContentWidth(width);

        Assert.False(layout.ShowTabStrip);
        Assert.False(layout.ShowSearchBox);
        Assert.False(layout.ShowActionButtons);
    }

    [Fact]
    public void ContentNeverExceedsAvailableWidth()
    {
        for (var width = 0.0; width <= 3000; width += 1)
        {
            var layout = TitleBarLayout.ForContentWidth(width);
            Assert.True(UsedWidth(layout) <= width + 1e-6, $"列宽 {width} 时内容 {UsedWidth(layout)} 溢出");
        }
    }

    [Fact]
    public void VisibleTabStripAlwaysKeepsUsableWidth()
    {
        for (var width = 0.0; width <= 3000; width += 1)
        {
            var layout = TitleBarLayout.ForContentWidth(width);
            if (!layout.ShowTabStrip) continue;
            Assert.True(layout.TabStripWidth >= TitleBarLayout.TabStripMinWidth,
                $"列宽 {width} 时标签条只有 {layout.TabStripWidth}");
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
        Assert.Contains("x:Name=\"SearchBox\"", xaml);
        Assert.Contains("TitleBar.IsDragRegion=\"False\"", xaml);
        Assert.Contains("TitleBarContentHorizontalAlignment", xaml);

        // 搜索框不能再包在固定 320 的列里 —— 那正是窗口变窄时内容溢出的根因
        Assert.DoesNotContain("<ColumnDefinition Width=\"320\" />", xaml);
        Assert.Contains("SizeChanged=\"TitleBarContent_SizeChanged\"", xaml);
    }

    [Fact]
    public void MainWindowTitleBar_RefreshesDragRegionsExplicitly()
    {
        // AutoRefreshDragRegions 会在每个布局帧重算直通区域并写入非客户区（官方文档明确
        // 提示影响性能），窗口拖拽/拖边缩放时随布局反复触发。标题栏内容的变化点是我们
        // 自己改的（标签增删/重排、搜索框收放、动作按钮显隐、标题文字），改为显式刷新。
        var xaml = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.xaml"));
        Assert.DoesNotContain("AutoRefreshDragRegions=\"True\"", xaml);

        var code = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.xaml.cs"));
        Assert.Contains("RefreshTitleBarDragRegions", code);
        Assert.Contains("RecomputeDragRegions()", code);

        // 标签条在代码里增删/重排后也要补刷新，否则可点击区域停在旧坐标
        var tabs = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.Tabs.cs"));
        Assert.Contains("RefreshTitleBarDragRegions", tabs);
        // 标签条横向滚动会让标签头的直通矩形整体平移，必须跟着刷新
        Assert.Contains("HookTabStripScrolling", tabs);
        Assert.Contains("ViewChanged", tabs);
    }

    [Fact]
    public void MainWindow_EnforcesMinimumSizeThroughPresenter()
    {
        // 不能在 AppWindow.Changed 里发现小于最小值再 Resize：拖边缩放/贴边分屏时
        // 会变成「系统缩小 -> 应用拉回」的反馈回环，窗口跟手性明显变差。
        var code = File.ReadAllText(Path.Combine(AppRoot, "Services", "WindowSizeService.cs"));
        Assert.Contains("PreferredMinimumWidth", code);
        Assert.Contains("PreferredMinimumHeight", code);

        var main = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.xaml.cs"));
        Assert.Contains("ApplyMinimumWindowSize", main);
        Assert.Contains("PreferredMinimumWidth: > 0", main);
    }

    [Fact]
    public void MainWindowTitleBar_HasLeftTabStripAndRightActionPanel()
    {
        var xaml = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.xaml"));

        // 资源管理器式多标签：标签条在标题栏左列，搜索/教程/下载收在右列面板里
        Assert.Contains("x:Name=\"ShellTabView\"", xaml);
        Assert.Contains("x:Name=\"TitleRightPanel\"", xaml);
        Assert.Contains("AddTabButtonClick=\"ShellTabView_AddTabButtonClick\"", xaml);
        Assert.Contains("TabCloseRequested=\"ShellTabView_TabCloseRequested\"", xaml);

        // 标签条必须在右侧面板之前（左列 < 右列），否则视觉顺序会反过来
        Assert.True(xaml.IndexOf("ShellTabView", StringComparison.Ordinal)
                  < xaml.IndexOf("TitleRightPanel", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("TitleRightPanel", StringComparison.Ordinal)
                  < xaml.IndexOf("TitleSearchArea", StringComparison.Ordinal));
    }

    [Fact]
    public void MainWindowTitleBar_RightPanelStaysAtTheFarRight()
    {
        // 搜索 / 教程 / 下载固定在标题栏最右边：开更多标签时标签条只借用中间的空白，
        // 右侧内容不会被推着往系统按钮方向走（标签条宽度由 TitleBarLayout 单独限流）。
        var xaml = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.xaml"));
        var panelStart = xaml.IndexOf("x:Name=\"TitleRightPanel\"", StringComparison.Ordinal);
        Assert.True(panelStart > 0);

        var panelTag = xaml.Substring(panelStart, Math.Min(400, xaml.Length - panelStart));
        Assert.Contains("HorizontalAlignment=\"Right\"", panelTag);

        var tabs = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.Tabs.cs"));
        Assert.DoesNotContain("Grid.SetColumn(TitleRightPanel", tabs);
    }

    [Fact]
    public void MainWindowCode_AppliesLayoutBudgetOnContentSizeChange()
    {
        var code = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.xaml.cs"));
        Assert.Contains("TitleBarLayout.ForContentWidth(", code);
        Assert.Contains("ShellTabView.MaxWidth", code);
    }
}
