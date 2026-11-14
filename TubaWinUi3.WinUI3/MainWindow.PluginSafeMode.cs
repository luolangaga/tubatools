using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Plugins;

namespace TubaWinUi3;

public sealed partial class MainWindow
{
    /// <summary>
    /// 安全模式常驻横幅：插件被整体跳过时，用户必须能一眼看出「工具少了是因为安全模式」，
    /// 并且能在这里一键恢复，而不必去插件页翻设置。
    /// </summary>
    private void ApplySafeModeBanner()
    {
        if (!PluginSafeMode.IsActive)
        {
            SafeModeBar.IsOpen = false;
            return;
        }

        // 自动进入时说明原因，否则用户只会觉得「我的插件全丢了」。
        SafeModeBar.Title = LocalizationService.L("SafeMode_Title", "插件安全模式");
        SafeModeBar.Message = PluginSafeMode.EnteredAutomatically
            ? LocalizationService.L("SafeMode_ReasonAuto", "最近几次启动都没能正常完成，已自动跳过全部已安装插件。若工具箱现在一切正常，说明问题来自插件。")
            : LocalizationService.L("SafeMode_ReasonManual", "本次启动已跳过全部已安装插件。");
        SafeModeBar.ActionButton = BuildSafeModeAction();
        SafeModeBar.IsOpen = true;
        AutomationProperties.SetName(SafeModeBar, LocalizationService.L("SafeMode_Title", "插件安全模式"));
    }

    private Button BuildSafeModeAction()
    {
        // 「恢复插件」= 关闭安全模式并重启：当前进程里被跳过的插件无法中途补加载
        // （配置类插件在 Register 期就要接管界面），重启是最干净的恢复方式。
        var button = new Button { Content = LocalizationService.L("SafeMode_EnablePlugins", "恢复插件并重启") };
        AutomationProperties.SetAutomationId(button, "SafeModeEnablePlugins");
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            PluginSafeMode.DisableForNextLaunch();
            if (App.TryRestart()) return;

            button.IsEnabled = true;
            var dialog = new TubaWinUi3.Controls.FluentContentDialog
            {
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme,
                Title = LocalizationService.L("SafeMode_Title", "插件安全模式"),
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = LocalizationService.L("SafeMode_RestartFailed", "安全模式已关闭，但自动重启失败。请手动关闭并重新打开工具箱，插件会在下次启动时恢复。")
                },
                CloseButtonText = LocalizationService.L("Common_Close", "关闭")
            };
            await dialog.ShowAsync();
        };
        return button;
    }
}
