using System.Runtime.CompilerServices;
using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 多标签页的标题映射与 XAML 接线。
///
/// 标签条在标题栏里，标题必须一眼看出这个标签在干什么：固定页面按资源键本地化、
/// 工具分类走分类显示名、代码构建的工具页取参数里的标题；未收录的页面（几十个工具页）
/// 由 MainWindow 从页面自己的 ToolPageHeader 读取，这里只锁定前两类映射。
/// </summary>
public class ShellTabTitleTests
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

    [Fact]
    public void KnownPages_MapToLocalizedResourceKeys()
    {
        var home = ShellTabTitle.Describe(typeof(Pages.DashboardPage), null);
        Assert.NotNull(home);
        Assert.Equal("MainWindow_NavHome.Content", home!.Value.ResourceKey);
        Assert.Equal("首页", home.Value.Fallback);

        var settings = ShellTabTitle.Describe(typeof(Pages.SettingsPage), null);
        Assert.Equal("MainWindow_NavSettings", settings!.Value.ResourceKey);

        var hardware = ShellTabTitle.Describe(typeof(Pages.HardwarePage), null);
        Assert.Equal("MainWindow_NavHardware.Content", hardware!.Value.ResourceKey);
    }

    [Fact]
    public void ToolCategoryPage_CarriesCategoryAndGlyphFallback()
    {
        var info = ShellTabTitle.Describe(typeof(Pages.HomePage), "显卡工具");

        Assert.NotNull(info);
        Assert.Equal("显卡工具", info!.Value.Category);
        Assert.Null(info.Value.ResourceKey);
    }

    [Fact]
    public void ToolContentPage_UsesParameterTitleAndIcon()
    {
        var info = ShellTabTitle.Describe(typeof(Pages.ToolContentPage), new Pages.ToolContentPageParam
        {
            ToolId = "wifi-password",
            Title = "WiFi 密码",
            Glyph = "\uE8A5",
            Content = null!
        });

        Assert.NotNull(info);
        Assert.Equal("WiFi 密码", info!.Value.Fallback);
        Assert.Equal("wifi-password", info.Value.BuiltinToolId);
    }

    [Fact]
    public void UnknownPage_ReturnsNullSoCallerReadsPageHeader()
    {
        Assert.Null(ShellTabTitle.Describe(typeof(Pages.LogViewerPage), null));
        Assert.Null(ShellTabTitle.Describe(typeof(Pages.SpaceCleanupPage), null));
    }

    [Fact]
    public void HomePageWithoutCategory_FallsBackToHomeTab()
    {
        // 教程页等外部入口会无参数导航到 HomePage，它和首页是同一个位置
        var info = ShellTabTitle.Describe(typeof(Pages.HomePage), null);

        Assert.NotNull(info);
        Assert.Equal("首页", info!.Value.Fallback);
        Assert.Null(info.Value.Category);
    }

    [Fact]
    public void DashboardFavorites_IsTitledFavorites()
    {
        var favorites = ShellTabTitle.Describe(typeof(Pages.DashboardPage), "favorites");

        Assert.NotNull(favorites);
        Assert.Equal("MainWindow_NavFavorites.Content", favorites!.Value.ResourceKey);

        var home = ShellTabTitle.Describe(typeof(Pages.DashboardPage), null);
        Assert.Equal("MainWindow_NavHome.Content", home!.Value.ResourceKey);
    }

    // ---------- XAML 接线 ----------

    [Fact]
    public void MainWindow_ContentHostIsTabbedInsteadOfSingleFrame()
    {
        var xaml = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.xaml"));
        var code = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.Tabs.cs"));

        // 单帧 NavFrame 不能再出现在 XAML 里：每个标签各持一个 Frame
        Assert.DoesNotContain("<Frame x:Name=\"NavFrame\"", xaml);
        Assert.Contains("InitializeTabs();", File.ReadAllText(Path.Combine(AppRoot, "MainWindow.xaml.cs")));

        // NavFrame 保持为字段（PcTutorialPage 按字段名反射取它）
        Assert.Contains("private Frame NavFrame", code);
        Assert.Contains("NavView.Content = _tabContentHost;", code);
    }

    [Fact]
    public void TabStrip_SupportsAddCloseAndReorder()
    {
        var xaml = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.xaml"));

        Assert.Contains("IsAddTabButtonVisible=\"True\"", xaml);
        Assert.Contains("CanReorderTabs=\"True\"", xaml);
        Assert.Contains("TabWidthMode=\"SizeToContent\"", xaml);
    }

    [Fact]
    public void TabsAreOffByDefaultAndToggleableFromSettings()
    {
        var tabs = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.Tabs.cs"));
        var settingsXaml = File.ReadAllText(Path.Combine(AppRoot, "Pages", "SettingsPage.xaml"));
        var settingsCode = File.ReadAllText(Path.Combine(AppRoot, "Pages", "SettingsPage.xaml.cs"));

        // 默认关闭：用户明确要求「这个模式默认改成关闭」
        Assert.Contains("internal const bool TabsEnabledDefault = false;", tabs);
        Assert.Contains("AppSettings.GetBool(TabsEnabledSettingKey, TabsEnabledDefault)", tabs);

        // 设置页有开关，并且走 AppSettings 立刻作用到主窗口
        Assert.Contains("x:Name=\"TabsToggle\"", settingsXaml);
        Assert.Contains("Toggled=\"TabsToggle_Toggled\"", settingsXaml);
        Assert.Contains("AppSettings.Set(MainWindow.TabsEnabledSettingKey, TabsToggle.IsOn)", settingsCode);
        Assert.Contains("InitTabsToggle();", settingsCode);
    }

    [Fact]
    public void TabsEnabled_HidesWindowTitleAndRestoresItWhenOff()
    {
        var tabs = File.ReadAllText(Path.Combine(AppRoot, "MainWindow.Tabs.cs"));

        // 开启多标签时标题文字让位给标签条；关闭后标题回来
        Assert.Contains("AppTitleBar.Title = enabled ? string.Empty", tabs);
    }
}
