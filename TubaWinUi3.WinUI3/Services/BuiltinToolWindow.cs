using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

/// <summary>
/// 内置工具独立窗口宿主：把工具页面放到一个新 Window 的 Frame 中。
/// 由 MainWindow.NavigateToToolPage 在设置"独立窗口"模式下调用，
/// 关闭窗口时触发 ToolContentPageParam.OnClose 清理（与嵌入模式一致）。
/// </summary>
public sealed class BuiltinToolWindow
{
    private static readonly List<BuiltinToolWindow> _openWindows = [];

    /// <summary>最近激活的工具窗口；页面内"返回/关闭"按钮通过它路由到正确窗口。</summary>
    public static BuiltinToolWindow? ActiveWindow { get; private set; }

    private static int _forceWindowDepth;

    /// <summary>是否处于"强制独立窗口"作用域（AI 助手启动内置工具时使用，与设置项无关）。</summary>
    public static bool ForceWindowMode => _forceWindowDepth > 0;

    /// <summary>在作用域内让 NavigateToToolPage 一律走独立窗口，嵌套调用安全（计数式），结束后自动恢复。</summary>
    public static IDisposable ForceWindowScope() => new WindowForceScope();

    private sealed class WindowForceScope : IDisposable
    {
        public WindowForceScope() => _forceWindowDepth++;
        public void Dispose() => _forceWindowDepth--;
    }

    private readonly Window _window;
    private readonly Frame _frame;

    private BuiltinToolWindow(Type pageType, object? parameter, string title)
    {
        _window = new Window();
        _frame = new Frame { CacheSize = 10 };
        // 与其它独立窗口一致：跟随应用主题设置（跟随系统时为 Default）
        _frame.RequestedTheme = ThemeService.CurrentElementTheme;
        _frame.Navigate(pageType, parameter, new DrillInNavigationTransitionInfo());
        _window.Content = _frame;
        BuiltinWindowHelper.ApplyStandardStyle(_window, title);
        ThemeService.ThemeChanged += OnThemeChanged;
        _window.Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
                ActiveWindow = this;
        };
        _window.Closed += (_, _) => OnClosed();
        _openWindows.Add(this);
    }

    /// <summary>应用内主题切换时实时同步窗口主题与标题栏。</summary>
    private void OnThemeChanged(ElementTheme theme)
    {
        _frame.RequestedTheme = theme;
        BuiltinWindowHelper.ApplyTitleBarTheme(_window);
    }

    public static void Show(Type pageType, object? parameter, string title)
    {
        var instance = new BuiltinToolWindow(pageType, parameter, title);
        instance._window.Activate();
        ActiveWindow = instance;
    }

    /// <summary>
    /// 查出承载指定页面的内置工具窗口（文件/文件夹选择器需要宿主窗口句柄时使用，
    /// 找不到时调用方自行回退到主窗口）。
    /// </summary>
    public static Window? FindHostingWindow(FrameworkElement? page)
    {
        if (page is null) return null;
        foreach (var instance in _openWindows)
            if (ReferenceEquals(instance._frame.Content, page)) return instance._window;
        return null;
    }

    public void Close() => _window.Close();

    /// <summary>页面内"返回/关闭"按钮调用：能后退则后退，否则关闭窗口。</summary>
    public void GoBackOrClose()
    {
        if (_frame.CanGoBack)
            _frame.GoBack();
        else
            _window.Close();
    }

    private void OnClosed()
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        // 窗口关闭时 Frame 不会触发 OnNavigatedFrom，需手动执行与嵌入模式一致的清理
        if (_frame.Content is ToolContentPage page)
            page.Detach();
        if (ActiveWindow == this)
            ActiveWindow = _openWindows.LastOrDefault(w => w != this);
        _openWindows.Remove(this);
    }
}
