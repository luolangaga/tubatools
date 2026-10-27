using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3;

public sealed partial class MainWindow
{
    private bool _navigationHighContrast;

    private void ApplyNavigationIcons()
    {
        _navigationHighContrast = ThemeColors.IsSystemHighContrast();
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is string tag)
                item.Icon = NavigationIconService.Create(tag, NavigationFallbackGlyph(tag), _navigationHighContrast);
        }
        if (NavView.SettingsItem is NavigationViewItem settings)
            settings.Icon = NavigationIconService.Create("settings", "\uE713", _navigationHighContrast);
    }

    private static string NavigationFallbackGlyph(string tag) => tag switch
    {
        "home" => "\uE80F",
        "hardware" => "\uE977",
        "builtin" => "\uE90F",
        "benchmark" => "\uE9D9",
        "community" => "\uE779",
        _ => GetCategoryGlyphStatic(tag)
    };

    // AccessibilitySettings.HighContrastChanged 在部分非打包环境订阅时会抛 0x80070490。
    // 使用已有的容错查询，在 XAML 主题变化及窗口重新激活时刷新，不依赖 WinRT 系统事件。
    private void NavigationThemeChanged(FrameworkElement sender, object args)
        => ApplyNavigationIcons();

    private void NavigationWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated &&
            _navigationHighContrast != ThemeColors.IsSystemHighContrast())
            ApplyNavigationIcons();
    }
}
