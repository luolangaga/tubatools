using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class AntiMotionSicknessTool : IBuiltinTool
{
    public string Id => "anti-motion-sickness";
    public string Name => LocalizationService.L("Builtin_anti-motion-sickness_Name", "游戏防晕3D");
    public string Description => LocalizationService.L("Builtin_anti-motion-sickness_Desc", "屏幕中央准星+四边标记辅助，缓解3D眩晕。一键帮你节约13块钱！");
    public string Glyph => "\uE7FC";
    public string Category => "游戏工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        try
        {
            App.MainWindow?.NavigateToToolPage(typeof(AntiMotionSicknessWindow));
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AntiMotionSickness] ExecuteAsync FAILED: {ex.Message}\n{ex.StackTrace}");
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
                Title = "游戏防晕3D - 启动失败",
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
