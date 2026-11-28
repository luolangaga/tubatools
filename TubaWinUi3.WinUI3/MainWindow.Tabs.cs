using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.Controls;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;

namespace TubaWinUi3;

/// <summary>
/// 主窗口的「资源管理器式」多标签页。
///
/// 设计要点：
/// <list type="bullet">
/// <item>每个标签一个独立 <see cref="Frame"/>（独立导航栈、独立返回按钮状态）。</item>
/// <item>侧边栏 <c>NavView</c> 全标签共享：点菜单只作用于当前标签，与资源管理器一致。</item>
/// <item>标签条在标题栏左侧，搜索 / 教程 / 下载固定在右侧。</item>
/// <item>关掉最后一个标签时立刻补一个首页标签，程序不会停在「没有任何内容」的空窗。</item>
/// <item><c>NavFrame</c> 始终指向当前标签的 Frame —— 仓库里几十处直接调用
/// <c>NavFrame.Navigate/Content/CanGoBack</c> 的代码因此语义不变。它保持为**字段**
/// （不是属性）：<c>PcTutorialPage</c> 按字段名反射取它。</item>
/// </list>
/// </summary>
public sealed partial class MainWindow
{
    private const int TabFrameCacheSize = 10;

    /// <summary>设置键：是否启用资源管理器式多标签页（默认关闭，可在设置页开启）。</summary>
    internal const string TabsEnabledSettingKey = "ShellTabsEnabled";

    /// <summary>多标签开启时默认值；关闭后标题栏恢复显示窗口标题。</summary>
    internal const bool TabsEnabledDefault = false;

    private readonly List<ShellTab> _tabs = [];

    /// <summary>
    /// 标签头的绑定源。TabView 的拖拽重排只在数据绑定模式（TabItemsSource）下生效，
    /// 直接往 TabItems 里塞 TabViewItem 是拖不动的，所以这里以集合作为数据源。
    /// </summary>
    private readonly ObservableCollection<TabViewItem> _tabItems = [];

    private readonly Border _tabContentHost = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch
    };

    /// <summary>当前标签的导航帧。切换标签时重新指向，全仓库共用同一个名字。</summary>
    private Frame NavFrame = null!;

    private bool _syncingTabs;
    private ShellTab? _activeTab;

    /// <summary>多标签是否启用（设置项 ShellTabsEnabled，默认开启）。关闭时标题栏不留标签条。</summary>
    private bool _tabsEnabled = true;

    private void InitializeTabs()
    {
        // NavView 的内容区换成标签宿主，切换标签时只替换它唯一的子元素
        NavView.Content = _tabContentHost;
        // 数据绑定模式：拖拽重排（CanReorderTabs）只有在这种模式下才生效
        ShellTabView.TabItemsSource = _tabItems;
        _tabItems.CollectionChanged += (_, _) =>
        {
            // 标签增删或拖拽重排都会改变标题栏里可点击区域的位置；关闭
            // AutoRefreshDragRegions 后必须在集合变化后补一次显式刷新。
            // 这里只排队（批量关闭会合并），等布局跑完再算，否则拿到的是旧坐标。
            HookTabStripScrolling();
            QueueTitleBarDragRegionRefresh();
        };
        ShellTabView.Loaded += (_, _) => HookTabStripScrolling();
        AddTab(select: true);
        ApplyTabsEnabled(AppSettings.GetBool(TabsEnabledSettingKey, TabsEnabledDefault));
    }

    /// <summary>
    /// 多标签开关（设置页「启动和行为 → 多标签页」，默认关闭）。
    /// 开启：标题栏左侧换成标签条，窗口标题文字隐藏（标签本身已经说明当前页面）。
    /// 关闭：标签条隐藏、恢复窗口标题，并只保留当前标签，其余标签的页面走清理后丢弃。
    /// </summary>
    internal void ApplyTabsEnabled(bool enabled)
    {
        _tabsEnabled = enabled;

        // 标签条显隐同时受开关和标题栏宽度预算约束：窗口极窄时布局会把整条标签让给
        // 系统标题按钮，这里跟着走，避免开关后凭空冒出一条挤在按钮上的标签条。
        var showStrip = enabled && (_lastTitleBarLayout?.ShowTabStrip ?? true);
        ShellTabView.Visibility = showStrip ? Visibility.Visible : Visibility.Collapsed;
        // 标签条需要占用标题栏空间，标题文字让位；关掉多标签时标题回来
        AppTitleBar.Title = enabled ? string.Empty : LocalizationService.L("App_Title", "图吧工具箱CE");
        Title = LocalizationService.L("App_Title", "图吧工具箱CE");

        // 右侧面板固定贴最右（与是否开启多标签无关）：开更多标签时标签条只借用
        // 中间的空白拖拽区，搜索 / 教程 / 下载不会被推着往系统按钮那边走。
        // 关闭期间标题栏布局仍跟着窗口宽度更新，但标签条宽度被跳过没写；
        // 重新开启时先补上当前宽度的上限，避免沿用旧值把搜索框挤掉。
        if (showStrip && _lastTitleBarLayout is { } layout)
            ShellTabView.MaxWidth = layout.TabStripWidth;

        // 关掉多标签时只留当前标签；批量关闭期间抑制选中跳转，避免构造期多走一次导航
        if (!enabled && _tabs.Count > 1)
        {
            _syncingTabs = true;
            try
            {
                foreach (var tab in OrderedTabs().Where(t => !ReferenceEquals(t, _activeTab)).ToList())
                    CloseTab(tab, selectNeighbor: false);
            }
            finally { _syncingTabs = false; }
        }

        RefreshTitleBarDragRegions();
    }

    private ShellTab AddTab(bool select)
    {
        var frame = new Frame { CacheSize = TabFrameCacheSize };
        frame.Navigated += TabFrame_Navigated;

        var item = new TabViewItem
        {
            IsClosable = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        var tab = new ShellTab(frame, item);
        item.Tag = tab;
        item.ContextFlyout = CreateTabFlyout(tab);
        ApplyTabTitle(tab, ShellTabTitle.Home.Fallback, ShellTabTitle.Home.Glyph);

        _tabs.Add(tab);
        _tabItems.Add(item);
        UpdateTabClosable();
        if (select) SelectTab(tab);
        return tab;
    }

    /// <summary>标签条内部的横向滚动器（TabView 模板里的那一个），用于跟踪直通区域位移。</summary>
    private ScrollViewer? _tabStripScroller;

    /// <summary>
    /// 标签条横向滚动（溢出后的左右按钮、滚轮、选中项自动滚入视野）时，标签头的
    /// 非客户区直通矩形会整体平移，但 TitleBar 不会因此重算区域。关闭
    /// AutoRefreshDragRegions 后必须跟着补刷新，否则滚动后点标签会被当成拖动窗口。
    /// </summary>
    private void HookTabStripScrolling()
    {
        if (_tabStripScroller is not null) return;
        _tabStripScroller = Descendants<ScrollViewer>(ShellTabView).FirstOrDefault();
        if (_tabStripScroller is null) return;
        _tabStripScroller.ViewChanged += (_, _) => QueueTitleBarDragRegionRefresh();
    }

    /// <summary>标签右键菜单：新建 / 关闭 / 关闭其他 / 关闭右侧（资源管理器的常见操作）。</summary>
    private MenuFlyout CreateTabFlyout(ShellTab owner)
    {
        var newTab = new MenuFlyoutItem { Text = LocalizationService.L("ShellTab_New", "新建标签页") };
        newTab.Click += (_, _) => NewTabAndOpenHome();

        var close = new MenuFlyoutItem { Text = LocalizationService.L("ShellTab_Close", "关闭标签页") };
        close.Click += (_, _) => CloseTab(owner);

        var closeOthers = new MenuFlyoutItem { Text = LocalizationService.L("ShellTab_CloseOthers", "关闭其他标签页") };
        closeOthers.Click += (_, _) => CloseOtherTabs(owner);

        var closeRight = new MenuFlyoutItem { Text = LocalizationService.L("ShellTab_CloseRight", "关闭右侧标签页") };
        closeRight.Click += (_, _) => CloseTabsToRight(owner);

        var flyout = new MenuFlyout();
        flyout.Items.Add(newTab);
        flyout.Items.Add(close);
        flyout.Items.Add(closeOthers);
        flyout.Items.Add(closeRight);

        // 打开时按当前标签数量决定可用性（标签数会变，不能在创建时算死）
        flyout.Opening += (_, _) =>
        {
            var ordered = OrderedTabs();
            var index = ordered.IndexOf(owner);
            close.IsEnabled = ordered.Count > 1;
            closeOthers.IsEnabled = ordered.Count > 1;
            closeRight.IsEnabled = index >= 0 && index < ordered.Count - 1;
        };
        return flyout;
    }

    private void CloseOtherTabs(ShellTab keep)
    {
        // 批量移除时 TabView 会自己改写选中项，内容区跟着来回切换没有意义：
        // 整段抑制，末尾显式选中保留的那个标签。
        _syncingTabs = true;
        try
        {
            foreach (var tab in OrderedTabs())
                if (!ReferenceEquals(tab, keep)) CloseTab(tab, selectNeighbor: false);
        }
        finally { _syncingTabs = false; }

        SelectTab(keep);
    }

    private void CloseTabsToRight(ShellTab from)
    {
        var ordered = OrderedTabs();
        var index = ordered.IndexOf(from);
        if (index < 0) return;

        _syncingTabs = true;
        try
        {
            for (var i = ordered.Count - 1; i > index; i--)
                CloseTab(ordered[i], selectNeighbor: false);
        }
        finally { _syncingTabs = false; }

        SelectTab(from);
    }

    /// <summary>标签条上的真实顺序（用户拖拽重排后会与 <see cref="_tabs"/> 的插入顺序不同）。</summary>
    private List<ShellTab> OrderedTabs() => _tabItems
        .Select(item => item.Tag)
        .OfType<ShellTab>()
        .ToList();

    /// <summary>只剩一个标签时不显示关闭按钮（与资源管理器一致，避免把界面点成空白）。</summary>
    private void UpdateTabClosable()
    {
        var single = _tabs.Count <= 1;
        foreach (var tab in _tabs) tab.Item.IsClosable = !single;
    }

    /// <summary>把某个标签设为可见：NavFrame 指向它、内容容器换成它的 Frame、侧栏与返回按钮同步。</summary>
    private void SelectTab(ShellTab tab)
    {
        if (ReferenceEquals(_activeTab, tab) && ReferenceEquals(_tabContentHost.Child, tab.Frame)) return;

        _activeTab = tab;
        NavFrame = tab.Frame;
        if (!ReferenceEquals(_tabContentHost.Child, tab.Frame))
            _tabContentHost.Child = tab.Frame;

        if (!ReferenceEquals(ShellTabView.SelectedItem, tab.Item))
        {
            _syncingTabs = true;
            try { ShellTabView.SelectedItem = tab.Item; }
            finally { _syncingTabs = false; }
        }

        SyncNavSelectionForActiveTab();
        AppTitleBar.IsBackButtonVisible = tab.Frame.CanGoBack;
        RefreshTitleBarDragRegions();
    }

    private static void ApplyTabTitle(ShellTab tab, string title, string glyph, ImageSource? icon = null)
    {
        tab.Title = title;
        tab.Item.Header = title;
        ToolTipService.SetToolTip(tab.Item, title);
        tab.Item.IconSource = icon is not null
            ? new ImageIconSource { ImageSource = icon }
            : new FontIconSource { Glyph = glyph, FontSize = 14 };
    }

    /// <summary>按页面类型 + 参数计算标签标题；已知页面直接映射，未知页面从页头读取。</summary>
    private void RefreshTabTitle(ShellTab tab)
    {
        if (tab.PageType is null) return;

        if (ShellTabTitle.Describe(tab.PageType, tab.Parameter) is { } info)
        {
            var title = info.Category is not null
                ? LocalizationService.GetCategoryDisplayName(info.Category)
                : info.ResourceKey is not null
                    ? LocalizationService.L(info.ResourceKey, info.Fallback)
                    : info.Fallback;
            var icon = info.Category is not null ? CategoryTabIcon(info.Category) : BuiltinIcon(info.BuiltinToolId);
            ApplyTabTitle(tab, title, info.Glyph, icon);
            tab.TitleFromPage = false;
            return;
        }

        // 未收录的页面（几十个工具页）：用页面自己的页头标题，没有就退回页面类型名
        if (tab.Frame.Content is FrameworkElement page && FindPageHeader(page) is { } header)
        {
            ApplyTabTitle(tab, header.Title, "\uE8A5", BuiltinIcon(header.ToolId));
            tab.TitleFromPage = true;
            return;
        }

        var typeName = tab.PageType.Name;
        ApplyTabTitle(tab, typeName, "\uE8A5");
        tab.TitleFromPage = true;
    }

    private static ToolPageHeader? FindPageHeader(DependencyObject page)
    {
        foreach (var header in Descendants<ToolPageHeader>(page))
            if (!string.IsNullOrWhiteSpace(header.Title)) return header;
        return null;
    }

    private static ImageSource? BuiltinIcon(string? toolId)
        => string.IsNullOrWhiteSpace(toolId) ? null : BuiltinIconService.Get(toolId);

    private static ImageSource? CategoryTabIcon(string category)
    {
        try { return new SvgImageSource(new Uri("ms-appx:///" + NavigationIconService.AssetFor(category))); }
        catch { return null; }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        var guard = 0;
        while (queue.Count > 0 && guard++ < 4000)
        {
            var current = queue.Dequeue();
            var count = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(current, i);
                if (child is T match) yield return match;
                queue.Enqueue(child);
            }
        }
    }

    /// <summary>标签导航完成：更新标题、返回按钮与侧栏选中项。</summary>
    private void TabFrame_Navigated(object sender, NavigationEventArgs e)
    {
        var tab = _tabs.FirstOrDefault(t => ReferenceEquals(t.Frame, sender));
        if (tab is null) return;

        tab.PageType = e.SourcePageType;
        tab.Parameter = e.Parameter;
        RefreshTabTitle(tab);

        // 代码构建的页面常常在导航完成后才填好页头，下一帧再补一次标题
        if (tab.TitleFromPage)
            DispatcherQueue.TryEnqueue(() => { if (tab.TitleFromPage) RefreshTabTitle(tab); });

        if (!ReferenceEquals(_activeTab, tab)) return;

        AppTitleBar.IsBackButtonVisible = tab.Frame.CanGoBack;
        RefreshTitleBarDragRegions();

        // 只要真的打开过 AI 助手（侧栏、内置工具、搜索结果、外部激活都走同一条 Frame），
        // 就算用户已经发现这个入口，收起侧栏角标并结束引导。
        if (e.SourcePageType == typeof(AiAssistantPage))
            OnAiAssistantVisited();

        // 侧栏点击触发的那次导航：选中项已由点击本身决定，不必再回写
        if (_navFromSidebar)
        {
            _navFromSidebar = false;
            return;
        }

        SyncNavSelectionForActiveTab();
    }

    /// <summary>侧栏选中项跟随当前标签的页面（切标签、返回、代码导航都要同步）。</summary>
    private void SyncNavSelectionForActiveTab()
    {
        var tab = _activeTab;
        if (tab?.PageType is null) return;

        _syncingNavSelection = true;
        try
        {
            if (tab.PageType == typeof(SettingsPage))
            {
                NavView.SelectedItem = NavView.SettingsItem;
                return;
            }
            var tag = ResolvePageTag(tab.PageType, tab.Parameter);
            if (tag is null) return;
            foreach (var item in NavView.MenuItems)
            {
                if (item is NavigationViewItem navItem && navItem.Tag is string t && t == tag)
                {
                    NavView.SelectedItem = navItem;
                    return;
                }
            }
        }
        finally { _syncingNavSelection = false; }

        // NavigationView 的选中项可能因为面板隐藏而没触发 SelectionChanged，
        // 这里显式同步一次图标菜单的选中态。
        UpdateNavRailSelection();
    }

    // ---------- 标签条交互 ----------

    private void ShellTabView_AddTabButtonClick(TabView sender, object args) => NewTabAndOpenHome();

    private void ShellTabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab?.Tag is ShellTab tab) CloseTab(tab);
    }

    private void ShellTabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingTabs) return;
        if (ShellTabView.SelectedItem is TabViewItem { Tag: ShellTab tab }) SelectTab(tab);
    }

    // ---------- 快捷键（Ctrl+T 新建 / Ctrl+W 关闭 / Ctrl+Tab 下一个） ----------

    private void NewTabAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        NewTabAndOpenHome();
    }

    private void CloseTabAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (_activeTab is { } tab) CloseTab(tab);
    }

    private void NextTabAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        var ordered = OrderedTabs();
        if (ordered.Count < 2) return;
        var index = _activeTab is null ? -1 : ordered.IndexOf(_activeTab);
        SelectTab(ordered[(index + 1) % ordered.Count]);
    }

    /// <summary>新建标签并打开首页（标题栏「+」按钮 / Ctrl+T）。多标签关闭时退化为在当前帧打开首页。</summary>
    private void NewTabAndOpenHome()
    {
        if (!_tabsEnabled)
        {
            NavFrame.Navigate(typeof(DashboardPage));
            return;
        }
        var tab = AddTab(select: true);
        tab.Frame.Navigate(typeof(DashboardPage));
    }

    /// <summary>
    /// 关闭标签。批量关闭（右键菜单「关闭其他 / 关闭右侧」）用 <paramref name="selectNeighbor"/>=false
    /// 避免中途抢走选中、内容区来回切换。关掉最后一个标签时补一个首页标签，界面不会变空白。
    /// </summary>
    private void CloseTab(ShellTab tab, bool selectNeighbor = true)
    {
        var ordered = OrderedTabs();
        var index = ordered.IndexOf(tab);
        if (index < 0) return;

        _tabs.Remove(tab);
        tab.Frame.Navigated -= TabFrame_Navigated;
        if (ReferenceEquals(_activeTab, tab)) _activeTab = null;

        DetachClosedPage(tab);
        _tabItems.Remove(tab.Item);
        UpdateTabClosable();

        if (_tabs.Count == 0)
        {
            NewTabAndOpenHome();
            return;
        }

        if (!selectNeighbor || _activeTab is not null) return;

        // TabView 会自行选中相邻项并触发 SelectionChanged；这里只兜住「什么都没选中」的情况
        var remaining = OrderedTabs();
        if (remaining.Count > 0) SelectTab(remaining[Math.Clamp(index, 0, remaining.Count - 1)]);
    }

    /// <summary>
    /// 关闭标签时让页面走一遍清理：Frame 被丢弃后不会再触发 OnNavigatedFrom，
    /// 而取消扫描、停定时器、释放句柄这些逻辑都挂在那里。
    /// </summary>
    private static void DetachClosedPage(ShellTab tab)
    {
        try
        {
            switch (tab.Frame.Content)
            {
                case ToolContentPage content: content.Detach(); break;
                case PluginToolPage plugin: plugin.Detach(); break;
                case AiPlaygroundPage playground: playground.CleanupOnWindowClose(); break;
            }
            tab.Frame.Content = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Tabs] 关闭标签清理失败（已忽略）: {ex.Message}");
        }
    }

    /// <summary>语言切换：所有标签（含后台标签页）按新语言重新计算标题。</summary>
    private void RefreshAllTabTitles()
    {
        foreach (var tab in _tabs.ToList()) RefreshTabTitle(tab);
    }
}
