using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

/// <summary>Shared navigation and accessible controls for the computer and diagnostic workspaces.</summary>
internal static class ToolExperience
{
    public static string L(string key, string fallback) => LocalizationService.L("Experience_" + key, fallback);

    public static TextBlock Text(string text, string style = "BodyTextBlockStyle") => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap,
        Style = (Style)Application.Current.Resources[style], IsTextSelectionEnabled = true
    };

    public static Button Button(string id, string text, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, text);
        if (primary) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            catch (Exception ex)
            {
                await new TubaWinUi3.Controls.FluentContentDialog { XamlRoot = button.XamlRoot, Title = L("ActionFailed", "操作未完成"),
                    Content = Text(ex.Message), CloseButtonText = L("Close", "关闭"),
                    RequestedTheme = ThemeService.CurrentElementTheme }.ShowAsync();
            }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    public static async Task OpenBuiltinAsync(string id, XamlRoot root)
    {
        var tool = BuiltinToolRegistry.Tools.FirstOrDefault(t => t.Id == id);
        if (tool is null) throw new InvalidOperationException(L("Unavailable", "此工具已隐藏或当前版本不支持，请到恢复工具中检查。"));
        await tool.ExecuteTrackedAsync(new BuiltinToolContext { XamlRoot = root });
    }

    public static Task NavigateAsync(Type page, object? parameter = null)
    {
        App.MainWindow?.NavigateToToolPage(page, parameter);
        return Task.CompletedTask;
    }
}
