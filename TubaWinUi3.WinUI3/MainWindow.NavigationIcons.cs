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
            if (item.Tag is string pluginTag && pluginTag.StartsWith("plugin-page:", StringComparison.Ordinal) &&
                BuiltinToolRegistry.GetById(pluginTag[12..]) is { } plugin)
            {
                var custom = _navigationHighContrast ? null : IconThemeService.GetNavigationCustomImage(pluginTag);
                if (custom is not null && BuiltinIconService.LoadAsset(custom) is { } customSource)
                    item.Icon = NavigationIconService.CreateImageIcon(customSource);
                else
                    item.Icon = !_navigationHighContrast && BuiltinIconService.Get(plugin.Id) is { } icon
                        ? NavigationIconService.CreateImageIcon(icon) : new FontIcon { Glyph = plugin.Glyph };
            }
            else if (item.Tag is string tag)
                item.Icon = NavigationIconService.Create(tag, NavigationFallbackGlyph(tag), _navigationHighContrast);
        }
        if (NavView.SettingsItem is NavigationViewItem settings)
            settings.Icon = NavigationIconService.Create("settings", "\uE713", _navigationHighContrast);
        TryApplyPluginNavigationCustomization();
        RefreshNavRail();
    }

    private static string NavigationFallbackGlyph(string tag) => tag switch
    {
        "home" => "\uE80F",
        "hardware" => "\uE977",
        "builtin" => "\uE90F",
        "benchmark" => "\uE9D9",
        "community" => "\uE779",
        "ai-assistant" => "\uE99A",
        _ => GetCategoryGlyphStatic(tag)
    };

    /// <summary>
    /// 图标工坊的菜单槽位：使用真实 NavigationViewItem 的 Tag，保证覆盖键和导航刷新走同一套值。
    /// 标题取插件导航/本地化后的显示名，默认图片由 <see cref="IconStudioCatalog"/> 解析。
    /// </summary>
    internal IReadOnlyList<IconStudioNavigationItem> GetIconStudioNavigationItems()
    {
        var result = new List<IconStudioNavigationItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chinese = LocalizationService.CurrentLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is not string tag || !seen.Add(tag)) continue;
            var defaults = NavigationDefault(item, tag);
            var subtitle = tag.StartsWith("plugin-page:", StringComparison.Ordinal)
                ? (chinese ? "插件工具" : "Plugin tool")
                : tag is "home" or "hardware" or "builtin" or "benchmark" or "community" or "ai-assistant" or "settings"
                    ? (chinese ? "主导航" : "Main navigation")
                    : (chinese ? "工具分类" : "Tool category");
            result.Add(new(tag, defaults.Title, subtitle, defaults.Glyph,
                IconStudioCatalog.DefaultNavigationImage(tag)));
        }

        if (NavView.SettingsItem is NavigationViewItem settings && seen.Add("settings"))
        {
            var defaults = NavigationDefault(settings, "settings");
            result.Add(new("settings", defaults.Title, chinese ? "页脚" : "Footer", defaults.Glyph,
                IconStudioCatalog.DefaultNavigationImage("settings")));
        }
        return result;
    }

    // AccessibilitySettings.HighContrastChanged 在部分非打包环境订阅时会抛 0x80070490。
    // 使用已有的容错查询，在 XAML 主题变化及窗口重新激活时刷新，不依赖 WinRT 系统事件。
    private void NavigationThemeChanged(FrameworkElement sender, object args)
        => ApplyNavigationIcons();

    private void NavigationWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        // 失焦时整层隐藏，停止合成渲染；恢复焦点再打开。
        FestivalLayer.SetActive(args.WindowActivationState != WindowActivationState.Deactivated);
        if (args.WindowActivationState != WindowActivationState.Deactivated &&
            _navigationHighContrast != ThemeColors.IsSystemHighContrast())
            ApplyNavigationIcons();
    }
}
