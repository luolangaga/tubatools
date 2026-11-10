using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TubaWinUi3;

public sealed partial class MainWindow
{
    /// <summary>fullTrust 插件与 <see cref="Plugins.PluginHostBridge"/> 的宿主入口。</summary>
    public NavigationView PluginNavigationView => NavView;
    public Frame PluginNavigationFrame => NavFrame;
    public FrameworkElement PluginRoot => (FrameworkElement)Content;
}
