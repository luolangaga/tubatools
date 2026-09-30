using System.Runtime.CompilerServices;
using System.Xml.Linq;
using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 「关闭主窗口 → 最小化到系统托盘」：判据真值表、默认值、设置页/搜索/本地化接线。
///
/// 托盘图标本身依赖 WinForms 消息泵与 Explorer，单测里跑不起来；这里覆盖的是
/// 「什么时候该隐藏、什么时候必须放行真正退出」这条最容易回归（且最贵）的逻辑 ——
/// 判错一边会误退出（用户被迫重启、重新读一遍硬件信息），另一边会让关机被拖住。
/// </summary>
public class CloseToTrayTests
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

    // ---------- 判据真值表 ----------

    [Fact]
    public void ShouldHideOnClose_EnabledAndIdle_Hides()
    {
        Assert.True(CloseToTrayService.ShouldHideOnClose(
            closeToTrayEnabled: true, isLiteMode: false, isExiting: false, sessionEnding: false));
    }

    [Fact]
    public void ShouldHideOnClose_DisabledAndIdle_ReallyCloses()
    {
        Assert.False(CloseToTrayService.ShouldHideOnClose(
            closeToTrayEnabled: false, isLiteMode: false, isExiting: false, sessionEnding: false));
    }

    /// <summary>后台工具（流量监控器/网络调度器/防晕3D）在跑：即使关掉开关也必须留在托盘，否则工具会被自己关掉。</summary>
    [Fact]
    public void ShouldHideOnClose_BackgroundToolRunning_HidesEvenWhenDisabled()
    {
        Assert.True(CloseToTrayService.ShouldHideOnClose(
            closeToTrayEnabled: false, isLiteMode: true, isExiting: false, sessionEnding: false));
    }

    /// <summary>托盘菜单「退出」：明确退出必须放行，否则程序永远退不掉。</summary>
    [Fact]
    public void ShouldHideOnClose_ExplicitExit_ReallyCloses()
    {
        Assert.False(CloseToTrayService.ShouldHideOnClose(
            closeToTrayEnabled: true, isLiteMode: true, isExiting: true, sessionEnding: false));
    }

    /// <summary>注销/关机：放行关闭，否则系统会卡在「正在关闭应用」。</summary>
    [Fact]
    public void ShouldHideOnClose_SessionEnding_ReallyCloses()
    {
        Assert.False(CloseToTrayService.ShouldHideOnClose(
            closeToTrayEnabled: true, isLiteMode: false, isExiting: false, sessionEnding: true));
    }

    [Fact]
    public void ShouldHideOnClose_SessionEndingBeatsExplicitExit()
    {
        Assert.False(CloseToTrayService.ShouldHideOnClose(
            closeToTrayEnabled: true, isLiteMode: false, isExiting: true, sessionEnding: true));
    }

    // ---------- 设置 ----------

    [Fact]
    public void DefaultEnabled_IsTrue()
    {
        // 默认开启：用户要的就是「别退出」，关掉开关的人自然会去设置里改
        Assert.True(CloseToTrayService.DefaultEnabled);
    }

    [Fact]
    public void SettingKeys_AreStable()
    {
        // 键名是落盘契约，改名等于把老用户的开关重置回默认值
        Assert.Equal("CloseToTray", CloseToTrayService.EnabledSettingKey);
        Assert.Equal("CloseToTrayHintShown", CloseToTrayService.HintShownSettingKey);
    }

    // ---------- 设置页 / 搜索 / 本地化接线 ----------

    [Fact]
    public void SettingsPage_ExposesCloseToTrayCard()
    {
        var xaml = File.ReadAllText(Path.Combine(AppRoot, "Pages", "SettingsPage.xaml"));
        Assert.Contains("x:Name=\"SettingsCloseToTrayCard\"", xaml);
        Assert.Contains("l:Uids.Uid=\"Settings_CloseToTray_Title\"", xaml);
        Assert.Contains("l:Uids.Uid=\"Settings_CloseToTray_Desc\"", xaml);
        Assert.Contains("Toggled=\"CloseToTrayToggle_Toggled\"", xaml);

        // 搜索/设置页定位靠这两张表，缺一个就会出现「搜到了点不进去」
        var code = File.ReadAllText(Path.Combine(AppRoot, "Pages", "SettingsPage.xaml.cs"));
        Assert.Contains("[\"CloseToTray\"] = \"GeneralExpander\"", code);
        Assert.Contains("[\"CloseToTray\"] = \"SettingsCloseToTrayCard\"", code);
        Assert.Contains("CloseToTrayService.SetEnabled(", code);
    }

    [Fact]
    public void UnifiedSearch_ExposesCloseToTraySetting()
    {
        var code = File.ReadAllText(Path.Combine(AppRoot, "Services", "UnifiedSearchService.cs"));
        Assert.Contains("Search_SettingCloseToTray", code);
        Assert.Contains("\"CloseToTray\"", code);
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void Resources_ContainTrayStrings(string language)
    {
        var path = Path.Combine(AppRoot, "Strings", language, "Resources.resw");
        Assert.True(File.Exists(path), $"缺少资源文件: {path}");

        var values = XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => e.Element("value")?.Value ?? string.Empty);

        string[] keys =
        [
            "Settings_CloseToTray_Title.Text",
            "Settings_CloseToTray_Desc.Text",
            "Search_SettingCloseToTray",
            "Search_SettingCloseToTrayDesc",
            "Tray_OpenMainWindow",
            "Tray_StopToolAndExit",
            "Tray_ExitApp",
            "Tray_TooltipResident",
            "Tray_TooltipTool",
            "Tray_BalloonTitle",
            "Tray_BalloonText",
        ];

        foreach (var key in keys)
        {
            Assert.True(values.TryGetValue(key, out var value), $"{language} 缺少资源键: {key}");
            Assert.False(string.IsNullOrWhiteSpace(value), $"{language} 资源键为空: {key}");
        }

        // 托盘提示带占位符，格式化时才不会抛 FormatException
        Assert.Contains("{0}", values["Tray_StopToolAndExit"]);
        Assert.Contains("{0}", values["Tray_TooltipResident"]);
        Assert.Contains("{0}", values["Tray_TooltipTool"]);
        Assert.Contains("{1}", values["Tray_TooltipTool"]);
    }
}
