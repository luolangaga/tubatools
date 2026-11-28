using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3;

/// <summary>
/// “图标菜单”导航布局（类似 Microsoft Store 左侧栏）的宿主逻辑。
///
/// 图标栏只是 NavView 的另一种呈现：条目、顺序、可见性、图标与选中项全部从 NavView 同步，
/// 点击仍回到 <see cref="NavigateToTag"/>，因此分类、插件导航定制、语言切换、图标工坊、
/// 安全模式等既有链路不需要第二套实现。默认布局仍是 <see cref="NavLayoutModeService.SidebarMode"/>。
/// </summary>
public sealed partial class MainWindow
{
    private void NavRail_ItemInvoked(object? sender, string tag)
    {
        if (tag == "settings")
        {
            _navFromSidebar = true;
            NavView.SelectedItem = NavView.SettingsItem;
            NavRail.SetSelected("settings");
            NavFrame.Navigate(typeof(Pages.SettingsPage));
            return;
        }

        var target = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag is string itemTag && itemTag == tag &&
                                    item.Visibility == Visibility.Visible);
        if (target is null) return;

        if (ReferenceEquals(NavView.SelectedItem, target))
        {
            // 再点一次当前项时 NavigationView 不会再发 SelectionChanged；图标栏按同一映射直接导航，
            // 与点侧栏当前项（无动作）保持最小差异：这里用于「回到该入口」。
            NavRail.SetSelected(tag);
            NavigateToTag(tag);
            return;
        }

        _navFromSidebar = true;
        NavView.SelectedItem = target;
        NavRail.SetSelected(tag);
    }

    /// <summary>把 NavigationView 当前的菜单项（含插件定制后的顺序/标题/可见性）复制到图标栏。</summary>
    private void RefreshNavRail()
    {
        if (NavRail.Visibility != Visibility.Visible) return;

        var items = new List<NavRail.NavRailItem>();
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is not string tag || item.Visibility != Visibility.Visible) continue;
            items.Add(new NavRail.NavRailItem(
                tag,
                item.Content?.ToString() ?? tag,
                NavigationIconService.ForRail(item.Icon, NavigationFallbackGlyph(tag))));
        }

        NavRail.NavRailItem? footer = null;
        if (NavView.IsSettingsVisible && NavView.SettingsItem is NavigationViewItem settings)
        {
            footer = new NavRail.NavRailItem(
                "settings",
                settings.Content?.ToString() ?? LocalizationService.L("MainWindow_NavSettings", "设置"),
                NavigationIconService.ForRail(settings.Icon, "\uE713"));
        }

        NavRail.SetItems(items, footer, CurrentNavTag());

        // 启动阶段活动标签可能晚于图标栏建立；排队再同步一次，避免第一屏“没有任何项选中”。
        DispatcherQueue.TryEnqueue(UpdateNavRailSelection);
    }

    private void UpdateNavRailSelection()
    {
        if (NavRail.Visibility != Visibility.Visible) return;
        NavRail.SetSelected(CurrentNavTag());
    }

    private string? CurrentNavTag()
    {
        if (NavView.SettingsItem is { } settings && ReferenceEquals(NavView.SelectedItem, settings))
            return "settings";
        if (NavView.SelectedItem is NavigationViewItem { Tag: string tag })
            return tag;

        // 启动/切布局时 NavigationView 的选中项可能还没回写（面板隐藏后尤其容易为空），
        // 但当前标签页已经确定；用活动标签的页面兜底，图标菜单才不会出现“哪项都没选中”。
        var pageType = _activeTab?.PageType;
        if (pageType is null) return null;
        if (pageType == typeof(Pages.SettingsPage)) return "settings";
        return ResolvePageTag(pageType, _activeTab.Parameter);
    }
}
