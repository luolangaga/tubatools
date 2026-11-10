using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.Controls;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Plugins;

namespace TubaWinUi3.Pages;

/// <summary>Recreates plugin content on every navigation, including Back; never reuses a disposed view.</summary>
public sealed partial class PluginToolPage : Page
{
    private readonly ToolPageHeader _header = new();
    private readonly Grid _host = new();
    private PluginXamlView? _view;
    public PluginToolPage()
    {
        InitializeComponent();
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(_header); Grid.SetRow(_host, 1); root.Children.Add(_host); Content = root;
    }
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e); Detach();
        if (e.Parameter is not string id || BuiltinToolRegistry.GetById(id) is not PluginXamlTool tool) return;
        _header.ToolId = tool.Id; _header.Title = tool.Name; _header.Subtitle = tool.Description; _header.Glyph = tool.Glyph;
        try { _view = new PluginXamlView(tool); _host.Children.Add(_view); }
        catch (Exception ex) { _host.Children.Add(new InfoBar { IsOpen = true, Severity = InfoBarSeverity.Error, Message = ex.Message, Margin = new Thickness(24) }); }
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { Detach(); base.OnNavigatedFrom(e); }
    public void Detach() { _view?.Dispose(); _view = null; _host.Children.Clear(); }
}
