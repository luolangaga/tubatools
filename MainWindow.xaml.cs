using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;
using Windows.UI;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3;

public sealed partial class MainWindow : Window
{
    public Frame NavigationFrame => NavFrame;

    private bool _syncingNavSelection;
    private bool _navFromSidebar;
    private bool _suppressSearch;
    private bool _searchDismissed;
    private readonly ObservableCollection<SearchResult> _searchResults = [];
    private readonly DispatcherQueueTimer _searchDebounceTimer;
    private Flyout? _downloadFlyout;
    private int _lastBadgeCount;

    public MainWindow()
    {
        InitializeComponent();

        SearchListView.ItemsSource = _searchResults;

        _searchDebounceTimer = DispatcherQueue.CreateTimer();
        _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(100);
        _searchDebounceTimer.Tick += OnSearchDebounceTick;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        AppWindow.SetIcon(iconPath);

        ApplyTitleBarTheme(ElementTheme.Default);

        BackdropService.ApplyBackdrop(this);
        BackdropService.BackdropChanged += OnBackdropChanged;

        WindowSizeService.ApplySavedWindowSize(this);

        Closed += MainWindow_Closed;
        AppWindow.Changed += AppWindow_Changed;
        NavFrame.Navigated += NavFrame_Navigated;

        PopulateCategories();
        NavigateToDefaultPage();

        DownloadQueueService.Initialize(DispatcherQueue);
        DownloadQueueService.QueueChanged += OnDownloadQueueChanged;
        UpdateDownloadBadge();
    }

    private void NavFrame_Navigated(object sender, NavigationEventArgs e)
    {
        if (_navFromSidebar)
        {
            _navFromSidebar = false;
            return;
        }

        _syncingNavSelection = true;

        if (e.SourcePageType == typeof(SettingsPage))
        {
            NavView.SelectedItem = NavView.SettingsItem;
        }
        else
        {
            var targetTag = ResolvePageTag(e.SourcePageType, e.Parameter);
            if (targetTag is not null)
            {
                foreach (var item in NavView.MenuItems)
                {
                    if (item is NavigationViewItem navItem && navItem.Tag is string t && t == targetTag)
                    {
                        NavView.SelectedItem = navItem;
                        break;
                    }
                }
            }
        }

        _syncingNavSelection = false;
    }

    private static string? ResolvePageTag(Type pageType, object? parameter)
    {
        if (pageType == typeof(SettingsPage)) return "settings";
        if (pageType == typeof(FavoritesPage)) return "favorites";
        if (pageType == typeof(HardwarePage)) return "hardware";
        if (pageType == typeof(BuiltinToolsPage)) return "builtin";

        if (pageType == typeof(HomePage))
        {
            if (parameter is string category) return category;
            return "all";
        }
        return null;
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerPointProperties props = e.GetCurrentPoint(null).Properties;

        if (props.IsXButton1Pressed)
        {
            if (NavFrame.CanGoBack)
            {
                NavFrame.GoBack();
                e.Handled = true;
            }
        }
        else if (props.IsXButton2Pressed)
        {
            if (NavFrame.CanGoForward)
            {
                NavFrame.GoForward();
                e.Handled = true;
            }
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        BackdropService.BackdropChanged -= OnBackdropChanged;
        AppWindow.Changed -= AppWindow_Changed;
        WindowSizeService.SaveWindowSize(this);
        DownloadQueueService.QueueChanged -= OnDownloadQueueChanged;
    }

    private void OnDownloadQueueChanged()
    {
        DispatcherQueue.TryEnqueue(UpdateDownloadBadge);
    }

    private void UpdateDownloadBadge()
    {
        var count = DownloadQueueService.PendingCount;
        if (count > 0)
        {
            DownloadQueueBadge.Value = count > 99 ? 99 : count;
            DownloadQueueBadge.Visibility = Visibility.Visible;
        }
        else
        {
            DownloadQueueBadge.Visibility = Visibility.Collapsed;
        }

        if (count > _lastBadgeCount)
        {
            PlayDownloadPulseAnimation();
        }
        _lastBadgeCount = count;
    }

    private void PlayDownloadPulseAnimation()
    {
        var btn = DownloadQueueButton;
        btn.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        btn.RenderTransform = new ScaleTransform();

        var scaleX = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTargetProperty(scaleX, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
        Storyboard.SetTarget(scaleX, btn);
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(150), Value = 1.3, EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut } });
        scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(400), Value = 1.0, EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseInOut } });

        var scaleY = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTargetProperty(scaleY, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
        Storyboard.SetTarget(scaleY, btn);
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(150), Value = 1.3, EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut } });
        scaleY.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(400), Value = 1.0, EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseInOut } });

        var sb = new Storyboard();
        sb.Children.Add(scaleX);
        sb.Children.Add(scaleY);
        sb.Begin();
    }

    private void DownloadQueueButton_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadFlyout is null)
        {
            _downloadFlyout = new Flyout
            {
                Content = new DownloadQueueFlyout(),
                Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
            };
        }
        _downloadFlyout.ShowAt(DownloadQueueButton);
    }

    private void OnBackdropChanged()
    {
        DispatcherQueue.TryEnqueue(() => BackdropService.ApplyBackdrop(this));
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange) return;
        var size = sender.Size;
        var minWidth = 800;
        var minHeight = 600;
        var needsResize = false;
        var newW = size.Width;
        var newH = size.Height;

        if (size.Width < minWidth)
        {
            newW = minWidth;
            needsResize = true;
        }
        if (size.Height < minHeight)
        {
            newH = minHeight;
            needsResize = true;
        }

        if (needsResize)
        {
            sender.Resize(new Windows.Graphics.SizeInt32(newW, newH));
        }
    }

    public void ApplyTitleBarTheme(ElementTheme theme)
    {
        var tb = AppWindow.TitleBar;
        var isDark = theme == ElementTheme.Dark ||
                     (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        if (isDark)
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 50, 50, 50);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 180, 180, 180);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 30, 30, 30);

            tb.BackgroundColor = Color.FromArgb(255, 32, 32, 32);
            tb.InactiveBackgroundColor = Color.FromArgb(255, 32, 32, 32);
        }
        else
        {
            tb.ButtonForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonBackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.ButtonHoverForegroundColor = Color.FromArgb(255, 30, 30, 30);
            tb.ButtonHoverBackgroundColor = Color.FromArgb(255, 230, 230, 230);
            tb.ButtonPressedForegroundColor = Color.FromArgb(255, 100, 100, 100);
            tb.ButtonPressedBackgroundColor = Color.FromArgb(255, 210, 210, 210);

            tb.BackgroundColor = Color.FromArgb(0, 255, 255, 255);
            tb.InactiveBackgroundColor = Color.FromArgb(0, 255, 255, 255);
        }

        tb.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        NavFrame.GoBack();
    }

    private async void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingNavSelection) return;

        _navFromSidebar = true;

        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "all":
                    NavFrame.Navigate(typeof(HomePage), null);
                    break;
                case "favorites":
                    NavFrame.Navigate(typeof(FavoritesPage));
                    break;
                case "hardware":
                    NavFrame.Navigate(typeof(HardwarePage));
                    break;
                case "builtin":
                    NavFrame.Navigate(typeof(BuiltinToolsPage));
                    break;

                case "benchmark":
                    _navFromSidebar = false;
                    ExecuteBenchmarkToolAsync();
                    break;
                case string category:
                    NavFrame.Navigate(typeof(HomePage), category);
                    break;
            }
        }
    }

    private void NavigateToDefaultPage()
    {
        var defaultPage = AppSettings.Get("DefaultPage") ?? "all";
        NavigationViewItem? targetItem = null;

        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag is string tag && tag == defaultPage)
            {
                targetItem = navItem;
                break;
            }
        }

        if (targetItem is not null)
        {
            NavView.SelectedItem = targetItem;
        }
        else
        {
            NavFrame.Navigate(typeof(HomePage), null);
        }
    }

    private void PopulateCategories()
    {
        while (NavView.MenuItems.Count > 6)
        {
            NavView.MenuItems.RemoveAt(5);
        }

        var categories = ToolCatalog.GetCategories();
        var otherCategory = categories.FirstOrDefault(c => c.Contains("其他"));
        var restCategories = categories.Where(c => !c.Contains("其他"));

        foreach (var category in restCategories)
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = category.Replace("工具", ""),
                Tag = category,
                Icon = new FontIcon { Glyph = GetCategoryGlyphStatic(category) }
            });
        }

        if (otherCategory != null)
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = otherCategory.Replace("工具", ""),
                Tag = otherCategory,
                Icon = new FontIcon { Glyph = GetCategoryGlyphStatic(otherCategory) }
            });
        }
    }

    public static string GetCategoryGlyphStatic(string category)
    {
        var customGlyph = AppSettings.Get($"CategoryGlyph_{category}");
        if (!string.IsNullOrWhiteSpace(customGlyph))
            return customGlyph;

        if (category.Contains("处理器", StringComparison.CurrentCultureIgnoreCase))
            return "\uEEA1";
        if (category.Contains("显卡", StringComparison.CurrentCultureIgnoreCase))
            return "\uF211";
        if (category.Contains("显示器", StringComparison.CurrentCultureIgnoreCase))
            return "\uE7F4";
        if (category.Contains("硬盘", StringComparison.CurrentCultureIgnoreCase))
            return "\uEDA2";
        if (category.Contains("内存", StringComparison.CurrentCultureIgnoreCase))
            return "\uEEA0";
        if (category.Contains("外设", StringComparison.CurrentCultureIgnoreCase))
            return "\uE962";
        if (category.Contains("游戏", StringComparison.CurrentCultureIgnoreCase))
            return "\uE7FC";
        if (category.Contains("声卡", StringComparison.CurrentCultureIgnoreCase))
            return "\uE7F5";
        if (category.Contains("网卡", StringComparison.CurrentCultureIgnoreCase))
            return "\uEDA3";
        if (category.Contains("综合", StringComparison.CurrentCultureIgnoreCase))
            return "\uEC4E";
        if (category.Contains("其他", StringComparison.CurrentCultureIgnoreCase))
            return "\uE712";

        return "\uE8B7";
    }

    public void RefreshToolCategories()
    {
        PopulateCategories();
    }

    private async Task ExecuteBenchmarkToolAsync()
    {
        var tool = BuiltinToolRegistry.GetById("performance-benchmark");
        if (tool is null) return;
        var context = new BuiltinToolContext
        {
            XamlRoot = Content.XamlRoot,
            CancellationToken = CancellationToken.None
        };
        try { await tool.ExecuteAsync(context); } catch { }
    }

    private void PopulateSearchSuggestions()
    {
        var items = UnifiedSearchService.GetQuickPanelItems();
        _searchResults.Clear();
        foreach (var item in items)
            _searchResults.Add(item);
    }

    private void ShowSearchPopup()
    {
        if (_searchDismissed) return;
        SearchPopup.IsOpen = _searchResults.Count > 0;
    }

    private void HideSearchPopup()
    {
        SearchPopup.IsOpen = false;
    }

    private void SearchPopup_GettingFocus(object sender, GettingFocusEventArgs e)
    {
        e.TryCancel();
    }

    private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressSearch || _searchDismissed) return;
        var query = SearchTextBox.Text.Trim();
        if (query.Length == 0)
            PopulateSearchSuggestions();
        ShowSearchPopup();
    }

    private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!SearchTextBox.FocusState.HasFlag(FocusState.Programmatic))
                HideSearchPopup();
        });
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSearch) return;

        _searchDismissed = false;
        var query = SearchTextBox.Text.Trim();

        if (query.Length == 0)
        {
            _searchDebounceTimer.Stop();
            PopulateSearchSuggestions();
            ShowSearchPopup();
            return;
        }

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void OnSearchDebounceTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        var query = SearchTextBox.Text.Trim();
        if (query.Length == 0) return;

        _ = SearchInBackgroundAsync(query);
    }

    private async Task SearchInBackgroundAsync(string query)
    {
        try
        {
            var results = await Task.Run(() => UnifiedSearchService.Search(query));
            _searchResults.Clear();
            foreach (var r in results)
                _searchResults.Add(r);
            SearchPopup.IsOpen = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Search] {ex}");
        }
    }

    private void SearchListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchResult result)
        {
            HideSearchPopup();
            HandleSearchResult(result);
        }
    }

    private void SearchSubmitButton_Click(object sender, RoutedEventArgs e)
    {
        var first = _searchResults.FirstOrDefault();
        if (first is not null)
        {
            HideSearchPopup();
            HandleSearchResult(first);
        }
    }

    private void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            SearchListView.SelectedIndex = -1;
            HideSearchPopup();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Enter)
        {
            var idx = SearchListView.SelectedIndex;
            SearchResult? result = idx >= 0 && idx < _searchResults.Count
                ? _searchResults[idx]
                : _searchResults.Count > 0 ? _searchResults[0] : null;

            if (result is not null)
            {
                HideSearchPopup();
                HandleSearchResult(result);
            }
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Down)
        {
            if (SearchListView.Items.Count > 0)
            {
                var next = SearchListView.SelectedIndex < 0
                    ? 0
                    : Math.Min(SearchListView.SelectedIndex + 1, SearchListView.Items.Count - 1);
                SearchListView.SelectedIndex = next;
                SearchListView.ScrollIntoView(SearchListView.SelectedItem);
            }
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Up)
        {
            if (SearchListView.Items.Count > 0)
            {
                var prev = SearchListView.SelectedIndex <= 0
                    ? 0
                    : SearchListView.SelectedIndex - 1;
                SearchListView.SelectedIndex = prev;
                SearchListView.ScrollIntoView(SearchListView.SelectedItem);
            }
            e.Handled = true;
        }
    }

    private void SearchListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            if (SearchListView.SelectedItem is SearchResult result)
            {
                _suppressSearch = true;
                SearchTextBox.Text = string.Empty;
                _suppressSearch = false;
                HideSearchPopup();
                HandleSearchResult(result);
            }
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            HideSearchPopup();
            e.Handled = true;
        }
    }

    private void HandleSearchResult(SearchResult result)
    {
        switch (result.Kind)
        {
            case SearchItemKind.ExternalTool:
            case SearchItemKind.CustomTool:
                NavigateToTool(result.MatchKey);
                break;
            case SearchItemKind.BuiltinTool:
                NavFrame.Navigate(typeof(BuiltinToolsPage),
                    new SearchNavigationTarget { HighlightBuiltinId = result.MatchKey });
                SyncNavSelection("builtin");
                break;

            case SearchItemKind.Setting:
                NavFrame.Navigate(typeof(SettingsPage),
                    new SearchNavigationTarget { HighlightSettingKey = result.MatchKey });
                SyncNavSelection("settings");
                break;
            case SearchItemKind.QuickAction:
                HandleQuickAction(result.MatchKey);
                break;
        }
    }

    private void SyncNavSelection(string tag)
    {
        _syncingNavSelection = true;
        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag is string t && t == tag)
            {
                NavView.SelectedItem = navItem;
                break;
            }
        }
        _syncingNavSelection = false;
    }

    private void NavigateToTool(string toolPath)
    {
        try
        {
            var tools = ToolCatalog.GetAllToolsCached();
            var tool = tools.FirstOrDefault(t => t.Path.Equals(toolPath, StringComparison.OrdinalIgnoreCase));
            if (tool is not null)
            {
                NavFrame.Navigate(typeof(HomePage),
                    new SearchNavigationTarget { HighlightToolPath = toolPath });

                if (!string.IsNullOrEmpty(tool.Category))
                    SyncNavSelection(tool.Category);
            }
        }
        catch { }
    }

    private void HandleQuickAction(string action)
    {
        if (!action.StartsWith("navigate:")) return;
        var target = action["navigate:".Length..];

        switch (target)
        {
            case "hardware":
                NavFrame.Navigate(typeof(HardwarePage));
                SyncNavSelection("hardware");
                break;
            case "favorites":
                NavFrame.Navigate(typeof(FavoritesPage));
                SyncNavSelection("favorites");
                break;
            case "builtin":
                NavFrame.Navigate(typeof(BuiltinToolsPage));
                SyncNavSelection("builtin");
                break;
            case "benchmark":
                _navFromSidebar = false;
                ExecuteBenchmarkToolAsync();
                break;

            case "settings":
                NavFrame.Navigate(typeof(SettingsPage));
                break;
        }
    }
}
