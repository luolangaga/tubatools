using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Pages;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class ScreenTestTool : IBuiltinTool
{
    public string Id => "screen-test";
    public string Name => LocalizationService.L("Builtin_screen-test_Name", "屏幕坏点检测");
    public string Description => LocalizationService.L("Builtin_screen-test_Desc", "全屏播放纯色与检测图案，快速发现屏幕坏点、漏光、色阶问题。");
    public string Glyph => "\uE7F4";
    public string Category => "硬件工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        try
        {
            var window = new Window();
            var page = new ScreenTestPage(window);
            page.RequestedTheme = ElementTheme.Dark;

            window.Content = page;

            window.AppWindow.Title = "屏幕坏点检测";
            window.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);

            // 部分 Windows 10 版本 AppWindow.TitleBar 为 null，不可直接解引用
            if (SafeTitleBar.Get(window) is { } tb)
            {
                tb.ExtendsContentIntoTitleBar = true;
                tb.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
                tb.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
                tb.ButtonForegroundColor = Color.FromArgb(100, 255, 255, 255);
                tb.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
                tb.ButtonHoverBackgroundColor = Color.FromArgb(40, 255, 255, 255);
            }

            window.Activate();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScreenTest] ExecuteAsync FAILED: {ex.Message}\n{ex.StackTrace}");
            ShowErrorDialog(context, ex);
            return Task.CompletedTask;
        }
    }

    private static async void ShowErrorDialog(BuiltinToolContext context, Exception ex)
    {
        try
        {
            var dialog = new TubaWinUi3.Controls.FluentContentDialog
            {
                Title = "屏幕坏点检测 - 启动失败",
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = $"{ex.Message}\n\n{ex.StackTrace}",
                        IsTextSelectionEnabled = true,
                        TextWrapping = TextWrapping.Wrap
                    }
                },
                CloseButtonText = "确定",
                XamlRoot = context.XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            await dialog.ShowAsync();
        }
        catch { }
    }
}
