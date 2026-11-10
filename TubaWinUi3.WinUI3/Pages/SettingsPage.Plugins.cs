using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Plugins;

namespace TubaWinUi3.Pages;

public sealed partial class SettingsPage
{
    private StackPanel? _pluginSettings;
    private void InitializePluginSettings()
    {
        Loaded += (_, _) => { RefreshPluginSettings(); ToolCatalog.ToolsChanged += OnPluginToolsChanged; };
        Unloaded += (_, _) => ToolCatalog.ToolsChanged -= OnPluginToolsChanged;
    }
    private void OnPluginToolsChanged() => DispatcherQueue.TryEnqueue(RefreshPluginSettings);
    private void RefreshPluginSettings()
    {
        if (_pluginSettings is not null) IntegrationSection.Children.Remove(_pluginSettings);
        _pluginSettings = null;
        var tools = BuiltinToolRegistry.Tools.OfType<PluginXamlTool>().Where(t => t.Definition.ShowInSettings).ToArray();
        if (tools.Length == 0) return;
        _pluginSettings = new StackPanel { Spacing = 8, Margin = new Thickness(0, 16, 0, 0) };
        _pluginSettings.Children.Add(new TextBlock { Text = LocalizationService.L("PluginUi_Settings", "插件设置"), Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        foreach (var tool in tools)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            if (BuiltinIconService.Get(tool.Id) is { } icon) content.Children.Add(new Image { Source = icon, Width = 24, Height = 24 });
            content.Children.Add(new TextBlock { Text = tool.Name, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(16, 12, 16, 12) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tool.Name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "PluginSettings:" + tool.Id);
            button.Click += async (_, _) =>
            {
                try { await tool.ExecuteTrackedAsync(new BuiltinToolContext { XamlRoot = XamlRoot }); }
                catch (Exception ex) { await new BuiltinToolContext { XamlRoot = XamlRoot }.ShowError(tool.Name, ex); }
            };
            _pluginSettings.Children.Add(button);
        }
        IntegrationSection.Children.Add(_pluginSettings);
    }
}
