namespace TubaWinUi3.PluginSdk;

/// <summary>
/// 完全访问宿主（需要在 tubatools.json 中声明 "fullTrust": true）。
/// 插件与宿主同进程、同权限运行，此接口不是安全沙箱；开启后可以操作主窗口、视觉树、
/// 导航和宿主设置，等同宿主自己的代码。插件升级宿主后需要重新编译。
/// </summary>
public interface IPluginHostAccess
{
    /// <summary>MainWindow 实例（WinUI Window）。</summary>
    object? MainWindow { get; }
    /// <summary>主窗口根视觉元素（FrameworkElement），可用 VisualTreeHelper 遍历/修改任意界面。</summary>
    object? RootVisual { get; }
    /// <summary>宿主 NavigationView，可增删菜单项、订阅导航事件。</summary>
    object? NavigationView { get; }
    /// <summary>宿主 Frame，可导航到任意 Page 类型或读取当前页面。</summary>
    object? NavigationFrame { get; }
    /// <summary>当前页面实例。</summary>
    object? CurrentPage { get; }
    /// <summary>在宿主 UI 线程执行；修改界面元素必须走这里。</summary>
    Task RunOnUiThreadAsync(Func<Task> action, CancellationToken ct = default);
    /// <summary>导航到指定页面类型（宿主页面或插件自己的 Page 子类）。</summary>
    Task NavigateAsync(Type pageType, object? parameter = null, CancellationToken ct = default);
    Task NavigateBackAsync(CancellationToken ct = default);
    /// <summary>读取任意宿主设置键；键不存在返回 null。</summary>
    Task<string?> GetSettingAsync(string key, CancellationToken ct = default);
    /// <summary>写入任意宿主设置键；value 为 null 时删除。会持久化并在下次启动生效。</summary>
    Task SetSettingAsync(string key, string? value, CancellationToken ct = default);
    /// <summary>宿主主题变化（浅色/深色/系统）。</summary>
    event Action? ThemeChanged;
    /// <summary>宿主界面语言变化。</summary>
    event Action? LanguageChanged;
}
