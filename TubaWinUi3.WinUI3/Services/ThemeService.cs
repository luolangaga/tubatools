using Microsoft.UI.Xaml;

namespace TubaWinUi3.Services;

public static class ThemeService
{
    private const string KeyTheme = "Theme";

    private static AppTheme _currentTheme = AppTheme.Default;

    /// <summary>系统主题缓存（仅在 UI 线程刷新，供任意线程读取）。</summary>
    private static bool _systemIsDark;
    private static bool _systemThemeHooked;

    /// <summary>
    /// 主题应用后触发（参数为解析后的 ElementTheme）。
    /// 子窗口/对话框宿主订阅此事件以实现主题实时跟随。
    /// </summary>
    public static event Action<ElementTheme>? ThemeChanged;

    public static AppTheme CurrentTheme => _currentTheme;

    public static ElementTheme CurrentElementTheme => _currentTheme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    /// <summary>
    /// 当前生效的暗色判定：显式选择优先，跟随系统时取缓存的系统主题。
    /// 仅读静态字段，任意线程可安全访问——后台线程直接读 Application.RequestedTheme
    /// 会触发 WinRT 访问冲突（0xc0000005），必须走这里。
    /// </summary>
    public static bool IsDarkEffective =>
        _currentTheme == AppTheme.Dark ||
        (_currentTheme == AppTheme.Default && _systemIsDark);

    public static void ApplySavedTheme()
    {
        RefreshSystemThemeCache();
        _currentTheme = Parse(AppSettings.Get(KeyTheme));
        ApplyTheme(_currentTheme);
    }

    /// <summary>设置并持久化主题（设置页「外观 → 主题」；立即生效，无需重启）。</summary>
    public static void SetTheme(AppTheme theme)
    {
        if (_currentTheme == theme) return;
        RefreshSystemThemeCache();
        _currentTheme = theme;
        AppSettings.Set(KeyTheme, Format(theme));
        ApplyTheme(theme);
    }

    /// <summary>UI 线程调用：把系统主题写入缓存。</summary>
    private static void RefreshSystemThemeCache()
    {
        try
        {
            _systemIsDark = Application.Current?.RequestedTheme == ApplicationTheme.Dark;
        }
        catch { }
    }

    private static string Format(AppTheme theme) => theme switch
    {
        AppTheme.Light => "light",
        AppTheme.Dark => "dark",
        _ => "auto"
    };

    private static AppTheme Parse(string? value) => value switch
    {
        "light" => AppTheme.Light,
        "dark" => AppTheme.Dark,
        _ => AppTheme.Default
    };

    private static void ApplyTheme(AppTheme theme)
    {
        var window = App.MainWindow;
        if (window?.Content is not FrameworkElement root)
            return;

        HookSystemThemeFollow(root, window);

        var elementTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        root.RequestedTheme = elementTheme;

        if (window is MainWindow mw)
            mw.ApplyTitleBarTheme(elementTheme);

        ThemeChanged?.Invoke(elementTheme);
    }

    /// <summary>
    /// 跟随系统模式下监听实际主题变化：刷新系统主题缓存并广播，
    /// 让标题栏、子窗口与代码构建的画刷联动。仅注册一次。
    /// </summary>
    private static void HookSystemThemeFollow(FrameworkElement root, Window window)
    {
        if (_systemThemeHooked) return;
        _systemThemeHooked = true;
        root.ActualThemeChanged += (sender, _) =>
        {
            // 手动主题下 ActualTheme 反映的是手动值，不更新系统缓存
            if (_currentTheme != AppTheme.Default) return;
            var dark = ((FrameworkElement)sender).ActualTheme == ElementTheme.Dark;
            if (dark == _systemIsDark) return;
            _systemIsDark = dark;
            if (window is MainWindow mw)
                mw.ApplyTitleBarTheme(ElementTheme.Default);
            ThemeChanged?.Invoke(ElementTheme.Default);
        };
    }
}

public enum AppTheme
{
    Default,
    Light,
    Dark
}
