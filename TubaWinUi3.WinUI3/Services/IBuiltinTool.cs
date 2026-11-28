using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TubaWinUi3.Services;

public interface IBuiltinTool
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    string Glyph { get; }
    string Category { get; }
    BuiltinToolKind Kind { get; }

    /// <summary>
    /// 是否作为独立入口显示在侧边栏（插件工具使用；内置工具默认 false）。
    /// 放在接口上是为了让主窗口启动路径不必引用 PluginSdk 的 PluginXamlTool 类型：
    /// 更新不完整导致 TubaWinUi3.PluginSdk.dll 缺失时，导航仍能降级启动而不是闪退（#265）。
    /// </summary>
    bool ShowInSidebar => false;

    Task ExecuteAsync(BuiltinToolContext context);
}

public enum BuiltinToolKind
{
    Dialog,
    BackgroundTask,
    ProgressTask,
    InstantAction
}

public sealed class BuiltinToolContext
{
    public XamlRoot XamlRoot { get; init; } = null!;
    public Action<string>? OnProgress { get; init; }
    public Func<string, string, ContentDialogResult>? ShowDialog { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public Func<string, string, string, Task<bool>>? ConfirmDownload { get; init; }

    /// <summary>合并类工具可指定初始标签（如性能排行的 benchmark / cpu / gpu / latency）。</summary>
    public string? PreferredTab { get; init; }

    public ContentDialog CreateDialog(string title, string closeButtonText = "关闭")
    {
        return new TubaWinUi3.Controls.FluentContentDialog
        {
            Title = title,
            CloseButtonText = closeButtonText,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
    }

    /// <summary>
    /// 统一错误对话框：标题 + 可滚动异常详情（等宽字体），按钮统一为「确定」。
    /// </summary>
    public async Task ShowError(string title, Exception ex)
    {
        var detail = new TextBlock
        {
            Text = ex.ToString(),
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        var dialog = new TubaWinUi3.Controls.FluentContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                Content = detail,
                MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            CloseButtonText = "确定",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        await dialog.ShowAsync();
    }
}
