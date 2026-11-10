using System.Reflection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.PluginSdk;
using Windows.Foundation.Collections;
using Windows.UI;

namespace Tubatools.ClassicTheme;

/// <summary>经典外壳：整高侧栏 + 内容标题栏 + 无卡片工具图标行。</summary>
internal sealed class ClassicThemeApplier(IPluginHostAccess host)
{
    private const double SidebarRatio = 0.19;
    private const double SidebarPaddingRatio = 0.34;
    private readonly IPluginHostAccess _host = host;
    private readonly List<SavedResource> _savedResources = [];
    private readonly List<Action> _restore = [];
    private readonly List<NavItemSnapshot> _navSnapshots = [];
    private readonly HashSet<NavigationViewItem> _styledNavItems = [];
    private readonly HashSet<FrameworkElement> _hiddenElements = [];
    private readonly HashSet<Page> _styledPages = [];
    private FrameworkElement? _root;
    private Grid? _rootGrid;
    private Panel? _rootPanel;
    private Window? _window;
    private NavigationView? _nav;
    private Frame? _frame;
    private TitleBar? _titleBar;
    private Image? _backgroundImage;
    private ResourceDictionary? _appResources;
    private AppWindowTitleBar? _nativeTitleBar;
    private Panel? _navOriginalParent;
    private int _navOriginalIndex = -1;
    private int _rootOriginalColumnCount;
    private bool? _savedCompact;
    private IObservableVector<object>? _menuVector;
    private VectorChangedEventHandler<object>? _menuHandler;
    private long _backButtonToken;
    private long _paneToggleToken;
    private DispatcherQueueTimer? _navStyleTimer;
    private DispatcherQueueTimer? _reapplyTimer;
    private NavigationViewItem? _classicSettingsItem;
    private GridView? _compactGrid;
    private double _sidebarWidth = 243;
    private double _navPadding = 87;
    private bool _applied;

    public void Apply()
    {
        if (_applied) return;
        _window = host.MainWindow as Window;
        _root = host.RootVisual as FrameworkElement;
        if (_root is null) throw new InvalidOperationException("主窗口还不可用，无法应用经典样式。");
        _nav = host.NavigationView as NavigationView;
        _frame = host.NavigationFrame as Frame;
        _rootGrid = _root as Grid;
        _rootPanel = _root as Panel;
        _titleBar = FindByName<TitleBar>(_root, "AppTitleBar");
        _backgroundImage = FindByName<Image>(_root, "BackgroundImg");
        _nativeTitleBar = _window?.AppWindow?.TitleBar;

        // 先置位，保证任何一步抛异常时 Restore 都能把已经改过的资源/属性还原。
        _applied = true;
        _root.SizeChanged += OnRootSizeChanged;
        _appResources = ClassicThemePalette.CreateResources();
        Application.Current.Resources.MergedDictionaries.Add(_appResources);

        if (_rootPanel is not null)
        {
            var old = _rootPanel.Background;
            _rootPanel.Background = ClassicThemePalette.RootGradient();
            _restore.Add(() => _rootPanel.Background = old);
        }

        ConfigureTitleBar();
        if (_backgroundImage is not null)
        {
            var oldVisibility = _backgroundImage.Visibility;
            _backgroundImage.Visibility = Visibility.Collapsed;
            _restore.Add(() => _backgroundImage.Visibility = oldVisibility);
        }
        ConfigureShellLayout();
        ConfigureNavResources();

        if (_frame is not null)
        {
            var old = _frame.Background;
            _frame.Background = new SolidColorBrush(ClassicThemePalette.Transparent);
            _restore.Add(() => _frame.Background = old);
            _frame.Navigated += OnFrameNavigated;
        }

        _savedCompact = GetCompactMode();
        if (_savedCompact == false) SetCompactMode(true);
        _restore.Add(() => { if (_savedCompact is bool value) SetCompactMode(value); });

        if (_frame?.Content is Page page) ApplyPageStyle(page);
        UpdateTitleBarTitle();
        _host.ThemeChanged += OnThemeChanged;
        RefreshThemeResources();
        ApplyClassicTitleBarColors();
        ScheduleReapply();
    }

    public void Restore()
    {
        if (!_applied) return;
        _applied = false;
        _reapplyTimer?.Stop();
        _reapplyTimer = null;

        if (_frame is not null) _frame.Navigated -= OnFrameNavigated;
        _host.ThemeChanged -= OnThemeChanged;
        if (_root is not null) _root.SizeChanged -= OnRootSizeChanged;
        if (_menuVector is not null && _menuHandler is not null) _menuVector.VectorChanged -= _menuHandler;
        if (_titleBar is not null)
        {
            if (_backButtonToken != 0) _titleBar.UnregisterPropertyChangedCallback(TitleBar.IsBackButtonVisibleProperty, _backButtonToken);
            if (_paneToggleToken != 0) _titleBar.UnregisterPropertyChangedCallback(TitleBar.IsPaneToggleButtonVisibleProperty, _paneToggleToken);
        }

        if (_appResources is not null && Application.Current is { } app)
            app.Resources.MergedDictionaries.Remove(_appResources);
        _appResources = null;

        for (var i = _savedResources.Count - 1; i >= 0; i--)
        {
            var item = _savedResources[i];
            try
            {
                if (item.HadValue) item.Dictionary[item.Key] = item.Value;
                else item.Dictionary.Remove(item.Key);
            }
            catch { }
        }
        _savedResources.Clear();

        for (var i = _restore.Count - 1; i >= 0; i--)
            try { _restore[i](); } catch { }
        _restore.Clear();

        RefreshThemeResources();
        RestoreHostTitleBarPalette();
        if (_nav?.SettingsItem is NavigationViewItem settings)
            settings.Visibility = Visibility.Visible;

        _navSnapshots.Clear();
        _styledNavItems.Clear();
        _styledPages.Clear();
        _hiddenElements.Clear();
        _menuVector = null;
        _menuHandler = null;
        _navStyleTimer = null;
        _backButtonToken = 0;
        _paneToggleToken = 0;
        _nativeTitleBar = null;
        _titleBar = null;
        _backgroundImage = null;
        _frame = null;
        _nav = null;
        _rootGrid = null;
        _rootPanel = null;
        _root = null;
        _window = null;
        _navOriginalParent = null;
        _navOriginalIndex = -1;
    }

    private void ConfigureTitleBar()
    {
        if (_titleBar is null) return;
        var oldBackground = _titleBar.Background;
        var oldForeground = _titleBar.Foreground;
        var oldBorderBrush = _titleBar.BorderBrush;
        var oldBorderThickness = _titleBar.BorderThickness;
        var oldIcon = _titleBar.IconSource;
        var oldBack = _titleBar.IsBackButtonVisible;
        var oldPane = _titleBar.IsPaneToggleButtonVisible;
        var oldFontSize = _titleBar.FontSize;
        var oldFontWeight = _titleBar.FontWeight;
        var oldPadding = _titleBar.Padding;

        _titleBar.Background = ClassicThemePalette.TitleGradient();
        _titleBar.Foreground = new SolidColorBrush(ClassicThemePalette.White);
        _titleBar.BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
        _titleBar.BorderThickness = new Thickness(0, 0, 0, 1);
        _titleBar.IconSource = null;
        _titleBar.IsBackButtonVisible = false;
        _titleBar.IsPaneToggleButtonVisible = false;
        _titleBar.FontSize = 16;
        _titleBar.FontWeight = FontWeights.SemiBold;
        _titleBar.Padding = new Thickness(40, 0, 0, 0);
        _restore.Add(() =>
        {
            _titleBar.Background = oldBackground;
            _titleBar.Foreground = oldForeground;
            _titleBar.BorderBrush = oldBorderBrush;
            _titleBar.BorderThickness = oldBorderThickness;
            _titleBar.IconSource = oldIcon;
            _titleBar.IsBackButtonVisible = oldBack;
            _titleBar.IsPaneToggleButtonVisible = oldPane;
            _titleBar.FontSize = oldFontSize;
            _titleBar.FontWeight = oldFontWeight;
            _titleBar.Padding = oldPadding;
        });

        if (_root is not null)
        {
            Hide(FindByName<FrameworkElement>(_root, "SearchBox"));
            Hide(FindByName<FrameworkElement>(_root, "NewbieTutorialButton"));
            Hide(FindByName<FrameworkElement>(_root, "DownloadQueueButton"));
        }

        _backButtonToken = _titleBar.RegisterPropertyChangedCallback(TitleBar.IsBackButtonVisibleProperty, (sender, _) =>
        {
            var bar = (TitleBar)sender;
            if (bar.IsBackButtonVisible) bar.IsBackButtonVisible = false;
        });
        _paneToggleToken = _titleBar.RegisterPropertyChangedCallback(TitleBar.IsPaneToggleButtonVisibleProperty, (sender, _) =>
        {
            var bar = (TitleBar)sender;
            if (bar.IsPaneToggleButtonVisible) bar.IsPaneToggleButtonVisible = false;
        });
    }

    private void ConfigureShellLayout()
    {
        if (_nav is null || _rootGrid is null || _titleBar is null) return;

        var windowWidth = _root?.ActualWidth ?? 0;
        if (windowWidth <= 0) windowWidth = 1280;
        _sidebarWidth = Math.Clamp(windowWidth * SidebarRatio, 220, 360);
        _navPadding = Math.Round(_sidebarWidth * SidebarPaddingRatio);

        _rootOriginalColumnCount = _rootGrid.ColumnDefinitions.Count;
        _rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_sidebarWidth) });
        _rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _restore.Add(() =>
        {
            while (_rootGrid.ColumnDefinitions.Count > _rootOriginalColumnCount)
                _rootGrid.ColumnDefinitions.RemoveAt(_rootGrid.ColumnDefinitions.Count - 1);
        });

        var oldTitleColumn = Grid.GetColumn(_titleBar);
        var oldTitleSpan = Grid.GetColumnSpan(_titleBar);
        Grid.SetColumn(_titleBar, 1);
        Grid.SetColumnSpan(_titleBar, 1);
        _restore.Add(() =>
        {
            Grid.SetColumn(_titleBar, oldTitleColumn);
            Grid.SetColumnSpan(_titleBar, oldTitleSpan);
        });

        _navOriginalParent = VisualTreeHelper.GetParent(_nav) as Panel;
        _navOriginalIndex = -1;
        if (_navOriginalParent is not null)
            for (var i = 0; i < _navOriginalParent.Children.Count; i++)
                if (ReferenceEquals(_navOriginalParent.Children[i], _nav)) { _navOriginalIndex = i; break; }

        if (_root is not null && FindByName<FrameworkElement>(_root, "SplashOverlay") is { } splash)
        {
            var oldSplashSpan = Grid.GetColumnSpan(splash);
            Grid.SetColumnSpan(splash, 2);
            _restore.Add(() => Grid.SetColumnSpan(splash, oldSplashSpan));
        }

        var oldRow = Grid.GetRow(_nav);
        var oldRowSpan = Grid.GetRowSpan(_nav);
        var oldColumn = Grid.GetColumn(_nav);
        var oldColumnSpan = Grid.GetColumnSpan(_nav);
        var oldNavAlignment = _nav.HorizontalAlignment;
        var oldNavVertical = _nav.VerticalAlignment;

        _navOriginalParent?.Children.Remove(_nav);
        _rootGrid.Children.Insert(Math.Min(1, _rootGrid.Children.Count), _nav);
        Grid.SetRow(_nav, 0);
        Grid.SetRowSpan(_nav, 3);
        Grid.SetColumn(_nav, 0);
        Grid.SetColumnSpan(_nav, 2);
        _nav.HorizontalAlignment = HorizontalAlignment.Stretch;
        _nav.VerticalAlignment = VerticalAlignment.Stretch;
        _restore.Add(() =>
        {
            _rootGrid.Children.Remove(_nav);
            if (_navOriginalParent is not null)
            {
                var index = Math.Clamp(_navOriginalIndex, 0, _navOriginalParent.Children.Count);
                _navOriginalParent.Children.Insert(index, _nav);
            }
            Grid.SetRow(_nav, oldRow);
            Grid.SetRowSpan(_nav, oldRowSpan);
            Grid.SetColumn(_nav, oldColumn);
            Grid.SetColumnSpan(_nav, oldColumnSpan);
            _nav.HorizontalAlignment = oldNavAlignment;
            _nav.VerticalAlignment = oldNavVertical;
        });

        var oldPaneMode = _nav.PaneDisplayMode;
        var oldPaneOpen = _nav.IsPaneOpen;
        var oldPaneLength = _nav.OpenPaneLength;
        var oldCompactThreshold = _nav.CompactModeThresholdWidth;
        var oldExpandedThreshold = _nav.ExpandedModeThresholdWidth;
        var oldPaneToggle = _nav.IsPaneToggleButtonVisible;
        var oldBack = _nav.IsBackButtonVisible;
        var oldBackground = _nav.Background;
        _nav.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        _nav.IsPaneOpen = true;
        _nav.OpenPaneLength = _sidebarWidth;
        _nav.CompactModeThresholdWidth = 0;
        _nav.ExpandedModeThresholdWidth = 0;
        _nav.IsPaneToggleButtonVisible = false;
        _nav.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
        _nav.Background = new SolidColorBrush(ClassicThemePalette.Transparent);
        _restore.Add(() =>
        {
            _nav.PaneDisplayMode = oldPaneMode;
            _nav.IsPaneOpen = oldPaneOpen;
            _nav.OpenPaneLength = oldPaneLength;
            _nav.CompactModeThresholdWidth = oldCompactThreshold;
            _nav.ExpandedModeThresholdWidth = oldExpandedThreshold;
            _nav.IsPaneToggleButtonVisible = oldPaneToggle;
            _nav.IsBackButtonVisible = oldBack;
            _nav.Background = oldBackground;
        });

        var oldHeader = _nav.PaneHeader;
        _nav.PaneHeader = CreateSidebarHeader(_navPadding);
        var oldFooter = _nav.PaneFooter;
        _nav.PaneFooter = CreateVersionFooter(_navPadding);
        _restore.Add(() =>
        {
            _nav.PaneHeader = oldHeader;
            _nav.PaneFooter = oldFooter;
        });

        _classicSettingsItem = new NavigationViewItem { Content = "设置", Tag = "classic-settings" };
        _nav.MenuItems.Add(_classicSettingsItem);
        StyleNavItem(_classicSettingsItem);
        _nav.ItemInvoked += OnNavItemInvoked;
        _restore.Add(() =>
        {
            _nav.ItemInvoked -= OnNavItemInvoked;
            _nav.MenuItems.Remove(_classicSettingsItem);
            _classicSettingsItem = null;
        });
        if (_nav.SettingsItem is NavigationViewItem hostSettings)
        {
            var oldVisibility = hostSettings.Visibility;
            hostSettings.Visibility = Visibility.Collapsed;
            _restore.Add(() => hostSettings.Visibility = oldVisibility);
        }

        if (_frame is not null)
        {
            var oldMargin = _frame.Margin;
            _frame.Margin = new Thickness(0, 48, 0, 0);
            _restore.Add(() => _frame.Margin = oldMargin);
        }

        if (_root is not null)
        {
            Hide(FindByName<FrameworkElement>(_root, "UpdateBanner"));
            Hide(FindByName<FrameworkElement>(_root, "ToolUpdateToast"));
        }

        StyleNavItems();
        if (_nav.MenuItems is IObservableVector<object> vector)
        {
            _menuVector = vector;
            _menuHandler = (_, _) => StyleNavItems();
            vector.VectorChanged += _menuHandler;
            _restore.Add(() => vector.VectorChanged -= _menuHandler);
        }

        // 分类是异步扫描出来的，启动后再多扫几次，避免新菜单项错过样式。
        if (_root?.DispatcherQueue is { } queue)
        {
            _navStyleTimer = queue.CreateTimer();
            _navStyleTimer.Interval = TimeSpan.FromSeconds(1);
            var ticks = 0;
            _navStyleTimer.Tick += (_, _) =>
            {
                StyleNavItems();
                if (++ticks >= 8) _navStyleTimer?.Stop();
            };
            _navStyleTimer.Start();
            _restore.Add(() => { _navStyleTimer?.Stop(); _navStyleTimer = null; });
        }

        _restore.Add(() =>
        {
            foreach (var snapshot in _navSnapshots)
            {
                try
                {
                    snapshot.Item.MinHeight = snapshot.MinHeight;
                    snapshot.Item.Margin = snapshot.Margin;
                    snapshot.Item.FontSize = snapshot.FontSize;
                    snapshot.Item.FontWeight = snapshot.FontWeight;
                    snapshot.Item.Padding = snapshot.Padding;
                    snapshot.Item.HorizontalContentAlignment = snapshot.HorizontalContentAlignment;
                    snapshot.Item.Foreground = snapshot.Foreground;
                    snapshot.Item.Icon = snapshot.Icon;
                }
                catch { }
            }
        });
    }

    private void ConfigureNavResources()
    {
        if (_nav is null) return;
        SetResource(_nav.Resources, "NavigationViewDefaultPaneBackground", ClassicThemePalette.NavGradient());
        SetResource(_nav.Resources, "NavigationViewExpandedPaneBackground", ClassicThemePalette.NavGradient());
        SetResource(_nav.Resources, "NavigationViewTopPaneBackground", ClassicThemePalette.NavGradient());
        SetResource(_nav.Resources, "NavigationViewContentBackground", new SolidColorBrush(ClassicThemePalette.Transparent));
        SetResource(_nav.Resources, "NavigationViewItemForeground", new SolidColorBrush(ClassicThemePalette.White));
        SetResource(_nav.Resources, "NavigationViewItemForegroundPointerOver", new SolidColorBrush(ClassicThemePalette.White));
        SetResource(_nav.Resources, "NavigationViewItemForegroundPressed", new SolidColorBrush(ClassicThemePalette.White));
        SetResource(_nav.Resources, "NavigationViewItemForegroundSelected", new SolidColorBrush(ClassicThemePalette.White));
        SetResource(_nav.Resources, "NavigationViewItemForegroundSelectedPointerOver", new SolidColorBrush(ClassicThemePalette.White));
        SetResource(_nav.Resources, "NavigationViewItemForegroundSelectedPressed", new SolidColorBrush(ClassicThemePalette.White));
        SetResource(_nav.Resources, "NavigationViewItemForegroundDisabled", new SolidColorBrush(ClassicThemePalette.TextDisabled));
        SetResource(_nav.Resources, "NavigationViewItemBackground", new SolidColorBrush(ClassicThemePalette.Transparent));
        SetResource(_nav.Resources, "NavigationViewItemBackgroundPointerOver", new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)));
        SetResource(_nav.Resources, "NavigationViewItemBackgroundPressed", new SolidColorBrush(Color.FromArgb(51, 255, 255, 255)));
        SetResource(_nav.Resources, "NavigationViewItemBackgroundSelected", new SolidColorBrush(ClassicThemePalette.Transparent));
        SetResource(_nav.Resources, "NavigationViewItemBackgroundSelectedPointerOver", new SolidColorBrush(ClassicThemePalette.Transparent));
        SetResource(_nav.Resources, "NavigationViewItemBackgroundSelectedPressed", new SolidColorBrush(ClassicThemePalette.Transparent));
        SetResource(_nav.Resources, "NavigationViewItemBackgroundDisabled", new SolidColorBrush(ClassicThemePalette.Transparent));
        SetResource(_nav.Resources, "NavigationViewItemSeparatorForeground", new SolidColorBrush(Color.FromArgb(51, 255, 255, 255)));
        SetResource(_nav.Resources, "NavigationViewSelectionIndicatorForeground", new SolidColorBrush(ClassicThemePalette.Transparent));
    }

    private void StyleNavItems()
    {
        if (_nav is null) return;
        if (_classicSettingsItem is not null && _nav.MenuItems.Contains(_classicSettingsItem))
        {
            var index = _nav.MenuItems.IndexOf(_classicSettingsItem);
            if (index >= 0 && index != _nav.MenuItems.Count - 1)
            {
                _nav.MenuItems.RemoveAt(index);
                _nav.MenuItems.Add(_classicSettingsItem);
            }
        }
        foreach (var item in _nav.MenuItems.OfType<NavigationViewItem>()) StyleNavItem(item);
        if (_nav.SettingsItem is NavigationViewItem settings)
        {
            StyleNavItem(settings);
            settings.Visibility = Visibility.Collapsed;
        }
    }

    private void StyleNavItem(NavigationViewItem item)
    {
        if (!_styledNavItems.Add(item))
        {
            ApplyNavItemStyle(item);
            return;
        }
        _navSnapshots.Add(new NavItemSnapshot(item, item.MinHeight, item.Margin, item.FontSize,
            item.FontWeight, item.Padding, item.HorizontalContentAlignment, item.Foreground, item.Icon));
        ApplyNavItemStyle(item);
    }

    private void ApplyNavItemStyle(NavigationViewItem item)
    {
        item.MinHeight = 54;
        item.Margin = new Thickness(_navPadding, 2, 0, 2);
        item.FontSize = 16;
        item.FontWeight = FontWeights.Normal;
        item.Padding = new Thickness(0);
        item.HorizontalContentAlignment = HorizontalAlignment.Left;
        item.Foreground = new SolidColorBrush(ClassicThemePalette.White);
        item.Icon = CreateNavIcon(item);
        item.Resources["NavigationViewItemBackgroundSelected"] = new SolidColorBrush(ClassicThemePalette.Transparent);
        item.Resources["NavigationViewItemBackgroundSelectedPointerOver"] = new SolidColorBrush(ClassicThemePalette.Transparent);
        item.Resources["NavigationViewItemBackgroundSelectedPressed"] = new SolidColorBrush(ClassicThemePalette.Transparent);
    }

    private FontIcon CreateNavIcon(NavigationViewItem item) => new()
    {
        Glyph = GlyphFor(item),
        FontSize = 18,
        Foreground = new SolidColorBrush(ClassicThemePalette.White),
        VerticalAlignment = VerticalAlignment.Center
    };

    private string GlyphFor(NavigationViewItem item)
    {
        if (ReferenceEquals(item, _nav?.SettingsItem)) return "\uE713";
        if (item.Tag is not string tag) return "\uE90F";
        return tag switch
        {
            "home" or "all" => "\uE80F",
            "hardware" => "\uE977",
            "builtin" => "\uE90F",
            "benchmark" => "\uE9D9",
            "community" => "\uE779",
            "ai-assistant" => "\uE99A",
            "favorites" => "\uE734",
            "classic-settings" => "\uE713",
            _ when tag.StartsWith("plugin-page:", StringComparison.Ordinal) => "\uE7F4",
            _ => CategoryGlyph(tag)
        };
    }

    private static string CategoryGlyph(string category)
    {
        try
        {
            var type = Type.GetType("TubaWinUi3.MainWindow, TubaWinUi3");
            var method = type?.GetMethod("GetCategoryGlyphStatic", BindingFlags.Public | BindingFlags.Static);
            if (method?.Invoke(null, [category]) is string glyph && glyph.Length > 0) return glyph;
        }
        catch { }
        return "\uE90F";
    }

    private void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem { Tag: "classic-settings" })
        {
            var type = Type.GetType("TubaWinUi3.Pages.SettingsPage, TubaWinUi3");
            if (type is not null) _frame?.Navigate(type);
        }
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        if (_titleBar is not null)
        {
            _titleBar.IsBackButtonVisible = false;
            _titleBar.IsPaneToggleButtonVisible = false;
        }
        StyleNavItems();
        if (e.Content is Page page) ApplyPageStyle(page);
        UpdateTitleBarTitle();
        RefreshThemeResources();
        ApplyClassicTitleBarColors();
    }

    private void ApplyPageStyle(Page page)
    {
        if (_titleBar is not null)
        {
            _titleBar.IsBackButtonVisible = false;
            _titleBar.IsPaneToggleButtonVisible = false;
        }
        if (!_styledPages.Add(page)) return;
        switch (page.GetType().Name)
        {
            case "HomePage": ApplyHomePage(page); break;
            case "HardwarePage": ApplyHardwarePage(page); break;
        }
    }

    private void ApplyHomePage(Page page)
    {
        Hide(FindByName<FrameworkElement>(page, "CategoryTitle"));
        Hide(FindByName<FrameworkElement>(page, "CategorySubtitle"));
        Hide(FindByName<FrameworkElement>(page, "ToolCountText"));
        Hide(FindByName<FrameworkElement>(page, "ViewModeBox"));
        Hide(FindByName<FrameworkElement>(page, "RecoverToolsButton"));
        Hide(FindByName<FrameworkElement>(page, "EditOrderButton"));
        Hide(FindByName<FrameworkElement>(page, "TagBarArea"));
        HideHomeHeader(page);

        var resources = new ResourceDictionary
        {
            ["CardBackgroundFillColorDefaultBrush"] = new SolidColorBrush(ClassicThemePalette.Transparent),
            ["CardStrokeColorDefaultBrush"] = new SolidColorBrush(ClassicThemePalette.Transparent),
            ["SubtleFillColorSecondaryBrush"] = new SolidColorBrush(ClassicThemePalette.Transparent)
        };
        page.Resources.MergedDictionaries.Add(resources);
        _restore.Add(() => page.Resources.MergedDictionaries.Remove(resources));

        if (FindByName<ComboBox>(page, "ViewModeBox") is { } mode)
        {
            var oldIndex = mode.SelectedIndex;
            if (oldIndex != 1) mode.SelectedIndex = 1;
            _restore.Add(() => { try { mode.SelectedIndex = oldIndex; } catch { } });
        }

        if (FindByName<GridView>(page, "ToolsGrid") is { } tools)
        {
            var oldVisibility = tools.Visibility;
            var oldFooter = tools.FooterTemplate;
            tools.Visibility = Visibility.Collapsed;
            tools.FooterTemplate = null;
            _restore.Add(() =>
            {
                tools.Visibility = oldVisibility;
                tools.FooterTemplate = oldFooter;
            });
        }
        if (FindByName<GridView>(page, "CompactGrid") is { } compact)
        {
            var oldVisibility = compact.Visibility;
            var oldMargin = compact.Margin;
            var oldWidth = compact.Width;
            var oldAlign = compact.HorizontalAlignment;
            var oldFooter = compact.FooterTemplate;
            compact.Visibility = Visibility.Visible;
            compact.Margin = new Thickness(12, 20, 0, 0);
            compact.HorizontalAlignment = HorizontalAlignment.Left;
            compact.FooterTemplate = null;
            _compactGrid = compact;
            ApplyCompactGridMetrics(compact);
            compact.SizeChanged += OnCompactGridSizeChanged;
            _restore.Add(() =>
            {
                compact.Visibility = oldVisibility;
                compact.Margin = oldMargin;
                compact.Width = oldWidth;
                compact.HorizontalAlignment = oldAlign;
                compact.FooterTemplate = oldFooter;
                compact.SizeChanged -= OnCompactGridSizeChanged;
                _compactGrid = null;
            });
        }

        void OnLoaded(object sender, RoutedEventArgs args)
        {
            page.Loaded -= OnLoaded;
            ApplyHomeGrids(page);
            HideFooterHints(page);
        }
        if (page.IsLoaded) { ApplyHomeGrids(page); HideFooterHints(page); }
        else page.Loaded += OnLoaded;
        _restore.Add(() => page.Loaded -= OnLoaded);
    }

    private void ApplyHardwarePage(Page page) => Hide(FindByName<FrameworkElement>(page, "HeaderPanel"));

    private void ApplyHomeGrids(Page page)
    {
        if (FindByName<FrameworkElement>(page, "ToolsGrid") is { } tools) tools.Visibility = Visibility.Collapsed;
        if (FindByName<FrameworkElement>(page, "CompactGrid") is { } compact)
        {
            compact.Visibility = Visibility.Visible;
            if (compact is GridView grid) ApplyCompactGridMetrics(grid);
        }
    }

    private void OnCompactGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is GridView grid) ApplyCompactGridMetrics(grid);
    }

    private void ApplyCompactGridMetrics(GridView grid)
    {
        var scale = _root?.XamlRoot?.RasterizationScale ?? 1;
        if (scale <= 0) scale = 1;
        // WinForms 原版：10 列、约 110px 的固定节距，左边对齐、右边留空。
        var pitch = 110d / scale;
        var itemWidth = Math.Max(84, pitch - 10);
        if (grid.ItemsPanelRoot is ItemsWrapGrid panel) panel.ItemWidth = itemWidth;
        grid.Width = (itemWidth + 10) * 10;
    }

    private void HideHomeHeader(Page page)
    {
        if (FindByName<FrameworkElement>(page, "CategoryTitle") is not { } title) return;
        var current = VisualTreeHelper.GetParent(title);
        while (current is not null)
        {
            if (current is Grid grid && grid.Children.Count >= 2)
            {
                Hide(grid);
                return;
            }
            current = VisualTreeHelper.GetParent(current);
        }
    }

    private void HideFooterHints(Page page)
    {
        foreach (var text in Descendants<TextBlock>(page))
        {
            var value = text.Text ?? "";
            if (value.Contains("双击", StringComparison.Ordinal) ||
                value.Contains("拖入", StringComparison.Ordinal) ||
                value.Contains("zip/exe", StringComparison.OrdinalIgnoreCase))
                Hide(text);
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private void UpdateTitleBarTitle()
    {
        if (_titleBar is null) return;
        var title = _nav?.SelectedItem is NavigationViewItem { Content: string selected } && !string.IsNullOrWhiteSpace(selected)
            ? selected
            : PageTitle(_frame?.Content);
        if (!string.IsNullOrWhiteSpace(title)) _titleBar.Title = title;
    }

    private static string? PageTitle(object? content) => content?.GetType().Name switch
    {
        "DashboardPage" => "首页",
        "HardwarePage" => "硬件信息",
        "BuiltinToolsPage" => "内置工具",
        "PerformanceBenchmarkPage" => "性能测试",
        "PluginsPage" => "插件",
        "AiAssistantPage" => "AI 助手",
        "SettingsPage" => "设置",
        _ => null
    };

    public void Reapply()
    {
        if (!_applied || _root is null || _nav is null || _rootGrid is null) return;
        var width = _root.ActualWidth > 0 ? _root.ActualWidth : 1280;
        var newWidth = Math.Clamp(width * SidebarRatio, 220, 360);
        var newPadding = Math.Round(newWidth * SidebarPaddingRatio);
        if (Math.Abs(newWidth - _sidebarWidth) > 1 || Math.Abs(newPadding - _navPadding) > 1)
        {
            _sidebarWidth = newWidth;
            _navPadding = newPadding;
            if (_rootGrid.ColumnDefinitions.Count >= 2)
                _rootGrid.ColumnDefinitions[0].Width = new GridLength(_sidebarWidth);
            _nav.OpenPaneLength = _sidebarWidth;
            _nav.PaneHeader = CreateSidebarHeader(_navPadding);
            _nav.PaneFooter = CreateVersionFooter(_navPadding);
        }
        StyleNavItems();
        UpdateTitleBarTitle();
        ReapplyCurrentPage();
        ApplyClassicTitleBarColors();
    }

    private void ReapplyCurrentPage()
    {
        if (_frame?.Content is not Page page) return;
        switch (page.GetType().Name)
        {
            case "HomePage":
                HideHomeHeader(page);
                ApplyHomeGrids(page);
                HideFooterHints(page);
                if (_compactGrid is not null) ApplyCompactGridMetrics(_compactGrid);
                break;
            case "HardwarePage":
                Hide(FindByName<FrameworkElement>(page, "HeaderPanel"));
                break;
        }
    }

    private void ScheduleReapply()
    {
        if (_root?.DispatcherQueue is not { } queue) return;
        _reapplyTimer = queue.CreateTimer();
        _reapplyTimer.Interval = TimeSpan.FromSeconds(1);
        var ticks = 0;
        _reapplyTimer.Tick += (_, _) =>
        {
            Reapply();
            if (++ticks >= 3) _reapplyTimer?.Stop();
        };
        _reapplyTimer.Start();
    }

    private void Hide(FrameworkElement? element)
    {
        if (element is null || !_hiddenElements.Add(element)) return;
        var oldVisibility = element.Visibility;
        element.Visibility = Visibility.Collapsed;
        _restore.Add(() => element.Visibility = oldVisibility);
    }

    private static StackPanel CreateSidebarHeader(double navPadding)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(navPadding, 24, 0, 24),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(new FontIcon
        {
            Glyph = "\uE7F4",
            FontSize = 24,
            Foreground = new SolidColorBrush(ClassicThemePalette.White),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = "图吧工具箱",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ClassicThemePalette.White),
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private static TextBlock CreateVersionFooter(double navPadding)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        var text = version is null ? "Version" : $"Version : {version.Major}.{version.Minor}.{version.Build}";
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            Margin = new Thickness(navPadding, 0, 0, 16),
            Padding = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromArgb(179, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Left,
            TextAlignment = TextAlignment.Left,
            IsHitTestVisible = false
        };
    }

    private void OnThemeChanged()
    {
        if (_applied)
        {
            StyleNavItems();
            ApplyClassicTitleBarColors();
        }
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_applied) Reapply();
    }

    private void RefreshThemeResources()
    {
        if (_root is null) return;
        var original = _root.RequestedTheme;
        var opposite = original == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        _root.RequestedTheme = opposite;
        _root.RequestedTheme = original;
    }

    private void ApplyClassicTitleBarColors()
    {
        if (_nativeTitleBar is null) return;
        _nativeTitleBar.BackgroundColor = ClassicThemePalette.Transparent;
        _nativeTitleBar.InactiveBackgroundColor = ClassicThemePalette.Transparent;
        _nativeTitleBar.ButtonBackgroundColor = ClassicThemePalette.Transparent;
        _nativeTitleBar.ButtonForegroundColor = ClassicThemePalette.White;
        _nativeTitleBar.ButtonHoverBackgroundColor = Color.FromArgb(51, 255, 255, 255);
        _nativeTitleBar.ButtonHoverForegroundColor = ClassicThemePalette.White;
        _nativeTitleBar.ButtonPressedBackgroundColor = Color.FromArgb(77, 255, 255, 255);
        _nativeTitleBar.ButtonPressedForegroundColor = ClassicThemePalette.White;
        _nativeTitleBar.ButtonInactiveBackgroundColor = ClassicThemePalette.Transparent;
        _nativeTitleBar.ButtonInactiveForegroundColor = Color.FromArgb(179, 255, 255, 255);
    }

    private void RestoreHostTitleBarPalette()
    {
        if (_nativeTitleBar is null) return;
        var dark = _root?.ActualTheme == ElementTheme.Dark;
        if (dark)
        {
            _nativeTitleBar.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
            _nativeTitleBar.ButtonBackgroundColor = ClassicThemePalette.Transparent;
            _nativeTitleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            _nativeTitleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 50, 50, 50);
            _nativeTitleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 180, 180, 180);
            _nativeTitleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 30, 30, 30);
            _nativeTitleBar.BackgroundColor = Color.FromArgb(255, 32, 32, 32);
            _nativeTitleBar.InactiveBackgroundColor = Color.FromArgb(255, 32, 32, 32);
        }
        else
        {
            _nativeTitleBar.ButtonForegroundColor = Color.FromArgb(255, 30, 30, 30);
            _nativeTitleBar.ButtonBackgroundColor = ClassicThemePalette.Transparent;
            _nativeTitleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 30, 30, 30);
            _nativeTitleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 230, 230, 230);
            _nativeTitleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 100, 100, 100);
            _nativeTitleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 210, 210, 210);
            _nativeTitleBar.BackgroundColor = ClassicThemePalette.Transparent;
            _nativeTitleBar.InactiveBackgroundColor = ClassicThemePalette.Transparent;
        }
        _nativeTitleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 160, 160, 160);
    }

    private void SetResource(ResourceDictionary dictionary, object key, object value)
    {
        var hadValue = dictionary.ContainsKey(key);
        var oldValue = hadValue ? dictionary[key] : null;
        _savedResources.Add(new SavedResource(dictionary, key, hadValue, oldValue));
        dictionary[key] = value;
    }

    private static bool? GetCompactMode()
    {
        try
        {
            var type = Type.GetType("TubaWinUi3.Services.CompactModeService, TubaWinUi3");
            var method = type?.GetMethod("IsCompactModeEnabled", BindingFlags.Public | BindingFlags.Static);
            return method?.Invoke(null, null) is bool value ? value : null;
        }
        catch { return null; }
    }

    private static void SetCompactMode(bool value)
    {
        try
        {
            var type = Type.GetType("TubaWinUi3.Services.CompactModeService, TubaWinUi3");
            type?.GetMethod("SetCompactModeEnabled", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, [value]);
        }
        catch { }
    }

    private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T typed && typed.Name == name) return typed;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindByName<T>(VisualTreeHelper.GetChild(root, i), name) is { } found) return found;
        }
        return null;
    }

    private readonly record struct SavedResource(ResourceDictionary Dictionary, object Key, bool HadValue, object? Value);

    private readonly record struct NavItemSnapshot(NavigationViewItem Item, double MinHeight, Thickness Margin,
        double FontSize, Windows.UI.Text.FontWeight FontWeight, Thickness Padding, HorizontalAlignment HorizontalContentAlignment,
        Brush? Foreground, IconElement? Icon);
}
