using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Plugins;

/// <summary>
/// fullTrust 插件的强类型宿主入口：插件引用 TubaWinUi3.dll（不要打包它）后可直接使用。
/// 未声明 fullTrust 的插件也能引用，但没有必要；所有界面修改必须在宿主 UI 线程执行。
/// </summary>
public static class PluginHostBridge
{
    public static Window? MainWindow => App.MainWindow;
    public static NavigationView? Navigation => App.MainWindow?.PluginNavigationView;
    public static Frame? NavigationFrame => App.MainWindow?.PluginNavigationFrame;
    public static FrameworkElement? Root => App.MainWindow?.PluginRoot;
    public static object? CurrentPage => NavigationFrame?.Content;
    public static DispatcherQueue? Dispatcher => App.MainWindow?.DispatcherQueue;
    /// <summary>宿主程序集；可用反射访问未公开的宿主服务/成员（不保证跨版本兼容）。</summary>
    public static System.Reflection.Assembly HostAssembly => typeof(App).Assembly;
    public static Type? FindType(string fullName) => HostAssembly.GetType(fullName, throwOnError: false);

    public static string? GetSetting(string key) => AppSettings.Get(key);
    public static void SetSetting(string key, string? value)
    {
        if (value is null) AppSettings.Remove(key); else AppSettings.Set(key, value);
    }

    public static bool Navigate(Type pageType, object? parameter = null) => NavigationFrame?.Navigate(pageType, parameter) == true;
    public static void NavigateBack()
    {
        var frame = NavigationFrame;
        if (frame?.CanGoBack == true) frame.GoBack();
    }
}
