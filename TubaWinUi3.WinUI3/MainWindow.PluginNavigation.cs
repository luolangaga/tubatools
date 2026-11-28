using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.PluginSdk;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Plugins;

namespace TubaWinUi3;

public sealed partial class MainWindow
{
    private static string PluginNavigationId(string tag) => tag switch
    {
        "community" => "plugins", "home" or "hardware" or "builtin" or "benchmark" or "ai-assistant" or "settings" => tag,
        _ when tag.StartsWith("plugin-page:", StringComparison.Ordinal) => "tool:" + tag[12..],
        _ => "category:" + tag
    };
    private PluginNavigationItem NavigationDefault(NavigationViewItem item, string tag)
    {
        if (tag.StartsWith("plugin-page:", StringComparison.Ordinal) && BuiltinToolRegistry.GetById(tag[12..]) is { } plugin)
            return new(PluginNavigationId(tag), plugin.Name, plugin.Glyph, true, 10 + Math.Max(0, _dynamicMenuItems.IndexOf(item)));
        var defaults = tag switch
        {
            "home" => ("MainWindow_NavHome.Content", "首页", "\uE80F", 0),
            "hardware" => ("MainWindow_NavHardware.Content", "硬件信息", "\uE977", 1),
            "builtin" => ("MainWindow_NavBuiltin.Content", "内置", "\uE90F", 2),
            "benchmark" => ("MainWindow_NavBenchmark.Content", "性能测试", "\uE9D9", 3),
            "community" => ("MainWindow_NavCommunity.Content", "插件", "\uE779", 4),
            "ai-assistant" => ("MainWindow_NavAiAssistant.Content", "AI 助手", "\uE99A", 5),
            "settings" => ("MainWindow_NavSettings", "设置", "\uE713", int.MaxValue),
            _ => ("", LocalizationService.GetCategoryDisplayName(tag), GetCategoryGlyphStatic(tag), 10 + Math.Max(0, _dynamicMenuItems.IndexOf(item)))
        };
        return new(PluginNavigationId(tag), defaults.Item1.Length > 0 ? LocalizationService.L(defaults.Item1, defaults.Item2) : defaults.Item2,
            defaults.Item3, true, defaults.Item4, tag == "settings");
    }
    private IReadOnlyList<PluginNavigationItem> NavigationDefaults()
    {
        var items = NavView.MenuItems.OfType<NavigationViewItem>().Where(n => n.Tag is string)
            .Select(n => NavigationDefault(n, (string)n.Tag)).ToList();
        items.Add(NavView.SettingsItem is NavigationViewItem settings ? NavigationDefault(settings, "settings")
            : new("settings", LocalizationService.L("MainWindow_NavSettings", "设置"), "\uE713", true, int.MaxValue, true));
        return items;
    }
    internal IReadOnlyList<PluginNavigationItem> GetPluginNavigationItems() => PluginNavigationStore.Apply(NavigationDefaults(), PluginNavigationStore.Read());
    private void PluginNavigationLoaded(object sender, RoutedEventArgs e) => TryApplyPluginNavigationCustomization();

    /// <summary>
    /// 插件导航定制依赖 TubaWinUi3.PluginSdk。安装/更新不完整导致 SDK 缺失时，
    /// 方法在 JIT 阶段就会抛 FileNotFoundException；调用点必须兜住并跳过插件导航，
    /// 让主窗口仍能启动（#265）。
    /// </summary>
    private void TryApplyPluginNavigationCustomization()
    {
        try { ApplyPluginNavigationCustomization(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[PluginNavigation] " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    internal void ApplyPluginNavigationCustomization()
    {
        IReadOnlyList<PluginNavigationChange> changes;
        try { changes = PluginNavigationStore.Read(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[PluginNavigation] " + ex.GetType().Name); return; }
        var data = PluginNavigationStore.Apply(NavigationDefaults(), changes);
        var items = NavView.MenuItems.OfType<NavigationViewItem>().Where(n => n.Tag is string)
            .ToDictionary(n => PluginNavigationId((string)n.Tag), StringComparer.Ordinal);
        if (NavView.SettingsItem is NavigationViewItem settings) items["settings"] = settings;
        foreach (var item in data)
        {
            if (item.IsFooter) NavView.IsSettingsVisible = item.IsVisible;
            if (!items.TryGetValue(item.Id, out var control)) continue;
            control.Content = item.Title; control.Visibility = item.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            var change = changes.FirstOrDefault(c => c.Id == item.Id);
            var tag = control.Tag is string t ? t : (item.Id == "settings" ? "settings" : item.Id);
            var studioImage = _navigationHighContrast ? null : IconThemeService.GetNavigationCustomImage(tag);
            if (studioImage is not null && BuiltinIconService.LoadAsset(studioImage) is { } studioSource)
                control.Icon = NavigationIconService.CreateImageIcon(studioSource);
            else if (change is { IconPath.Length: > 0 } && !_navigationHighContrast &&
                     File.Exists(change.IconPath) && BuiltinIconService.LoadAsset(change.IconPath) is { } changeSource)
                control.Icon = NavigationIconService.CreateImageIcon(changeSource);
            else if (change is { Glyph.Length: > 0 }) control.Icon = new FontIcon { Glyph = item.Glyph };
            else if (control.Tag is string pluginTag && pluginTag.StartsWith("plugin-page:", StringComparison.Ordinal) && BuiltinToolRegistry.GetById(pluginTag[12..]) is { } plugin)
                control.Icon = !_navigationHighContrast && BuiltinIconService.Get(plugin.Id) is { } source
                    ? NavigationIconService.CreateImageIcon(source) : new FontIcon { Glyph = plugin.Glyph };
            else control.Icon = NavigationIconService.Create(item.Id == "settings" ? "settings" : (string)control.Tag, item.Glyph, _navigationHighContrast);
        }
        var ordered = data.Where(i => !i.IsFooter).Select(i => items[i.Id]).ToArray();
        var current = NavView.MenuItems.OfType<NavigationViewItem>().ToArray();
        if (current.SequenceEqual(ordered))
        {
            RefreshNavRail();
            return;
        }
        var selected = NavView.SelectedItem; var syncing = _syncingNavSelection; _syncingNavSelection = true;
        try
        {
            var index = 0;
            var updated = NavView.MenuItems.Select(o => o is NavigationViewItem ? ordered[index++] : o).ToArray();
            NavView.MenuItems.Clear(); foreach (var item in updated) NavView.MenuItems.Add(item); NavView.SelectedItem = selected;
        }
        finally { _syncingNavSelection = syncing; }
        RefreshNavRail();
    }
}
