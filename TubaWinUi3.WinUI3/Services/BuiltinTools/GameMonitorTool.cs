using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class GameMonitorTool : IBuiltinTool
{
    public string Id => "game-monitor";
    public string Name => "游戏监控";
    public string Description => "拖拽组件设计监控覆盖层，内置多种布局预设，实时显示 FPS、温度、负载等硬件参数，可勾选指标记录数据并导出 JSON / Markdown 报告";
    public string Glyph => "\uE9F5";
    public string Category => "游戏工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        try
        {
            App.MainWindow?.NavigateToToolPage(typeof(GameOverlayPage));
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GameMonitor] ExecuteAsync FAILED: {ex.Message}\n{ex.StackTrace}");
            ShowErrorDialog(context, ex);
            return Task.CompletedTask;
        }
    }

    private static async void ShowErrorDialog(BuiltinToolContext context, Exception ex)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = "游戏监控 - 启动失败",
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
