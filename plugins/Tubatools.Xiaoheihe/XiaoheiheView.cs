using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.PluginSdk;
using Tubatools.Xiaoheihe.Services;

namespace Tubatools.Xiaoheihe;

/// <summary>
/// 小黑盒社区客户端主视图。布局接近桌面论坛客户端：
/// 左侧固定导航 + 顶部搜索/账号工具栏 + 右侧内容卡片流。
/// </summary>
internal sealed partial class XiaoheiheView
{
    private readonly PluginExecutionContext _host;
    private readonly DispatcherQueue _dispatcher;
    private readonly Grid _root = new();
    private readonly Palette _palette = new();
    private readonly Dictionary<string, ImageSource?> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly XhhAuthStore _store;
    private readonly Stack<Func<Task>> _backStack = new();
    private readonly List<NavEntry> _nav = [];

    private XhhClient _client;
    private XhhSession? _session;
    private WebView2? _webView;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _qrTimer;
    private bool _loaded;
    private bool _compact;
    private bool _qrPolling;
    private bool _qrFinished;
    private bool _captchaDialogOpen;
    private string? _qrValue;
    private long _qrExpiresAt;
    private string _currentPage = "home";
    private Func<Task>? _refreshAction;

    private readonly Border _sidebar = new();
    private readonly StackPanel _navPanel = new() { Spacing = 3 };
    private readonly TextBlock _brandTitle = new();
    private readonly TextBlock _brandSubtitle = new();
    private readonly Border _accountCard = new();
    private readonly StackPanel _accountCardContent = new() { Spacing = 8 };
    private readonly TextBlock _legal = new();
    private readonly TextBlock _pageTitle = new();
    private readonly TextBlock _pageSubtitle = new();
    private readonly AutoSuggestBox _searchBox = new();
    private readonly Button _refreshButton;
    private readonly Button _backButton;
    private readonly Button _accountButton;
    private readonly InfoBar _notice = new() { IsOpen = false, IsClosable = true, Margin = new Thickness(0, 0, 0, 8) };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 3, Visibility = Visibility.Collapsed };
    private readonly Grid _content = new();
    private ConnectedAnimation? _pendingConnectedAnimation;

    private readonly List<XhhPost> _homePosts = [];
    private int _homeOffset;
    private bool _homeHasMore = true;
    private StackPanel? _homeSurface;

    private readonly List<XhhTopicGroup> _topicGroups = [];
    private XhhTopic? _selectedTopic;
    private readonly List<XhhPost> _topicPosts = [];
    private int _topicOffset;
    private bool _topicHasMore = true;
    private StackPanel? _topicFeedSurface;
    private StackPanel? _topicListSurface;
    private TextBlock? _topicTitleText;
    private ComboBox? _topicSortBox;

    private readonly List<XhhSearchItem> _searchItems = [];
    private string _searchQuery = "";
    private string _searchType = "general";
    private int _searchOffset;
    private bool _searchHasMore = true;
    private StackPanel? _searchSurface;
    private StackPanel? _hotSearchSurface;
    private TextBox? _searchPageBox;

    private string _mineSection = "posts";
    private readonly List<XhhPost> _minePosts = [];
    private readonly List<XhhPost> _favPosts = [];
    private readonly List<XhhPost> _historyPosts = [];
    private readonly List<XhhMessageItem> _messages = [];
    private readonly List<XhhGame> _ownedGames = [];

    private readonly List<XhhGame> _hotGames = [];
    private readonly List<XhhGame> _gameLibrary = [];
    private string _gameSection = "hot";

    private XiaoheiheView(PluginExecutionContext host)
    {
        _host = host;
        _dispatcher = DispatcherQueue.GetForCurrentThread() ??
                      throw new InvalidOperationException("插件页面必须在界面线程创建。");
        _store = new XhhAuthStore(host.Services.DataDirectory);
        _session = _store.Load();
        _client = new XhhClient(_session?.Cookie, _session?.HeyboxId);

        _refreshButton = IconButton("\uE72C", "刷新当前页面");
        _backButton = IconButton("\uE72B", "返回");
        _backButton.Visibility = Visibility.Collapsed;
        _accountButton = new Button
        {
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(8)
        };
        _accountButton.Click += async (_, _) => await NavigateAsync("account");

        _searchBox.PlaceholderText = "搜索帖子、游戏、用户…";
        _searchBox.QueryIcon = new SymbolIcon(Symbol.Find);
        _searchBox.Width = 280;
        _searchBox.QuerySubmitted += async (_, args) =>
        {
            string query = args.QueryText?.Trim() ?? "";
            if (query.Length == 0) return;
            _searchQuery = query;
            _searchType = "general";
            await NavigateAsync("search");
        };

        BuildShell();
        WireEvents();
        _palette.Apply(dark: false);
        RefreshNav();
        UpdateAccountButton();
    }

    public static FrameworkElement Create(PluginExecutionContext host) => new XiaoheiheView(host)._root;

    private void BuildShell()
    {
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(224) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        BuildSidebar();
        _root.Children.Add(_sidebar);

        var contentShell = new Grid();
        contentShell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentShell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentShell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentShell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        contentShell.Children.Add(BuildTopBar());
        Grid.SetRow(_progress, 1);
        contentShell.Children.Add(_progress);
        Grid.SetRow(_notice, 2);
        contentShell.Children.Add(_notice);
        Grid.SetRow(_content, 3);
        contentShell.Children.Add(_content);

        Grid.SetColumn(contentShell, 1);
        _root.Children.Add(contentShell);
    }

    private void BuildSidebar()
    {
        _sidebar.Background = _palette.Sidebar;
        _sidebar.BorderBrush = _palette.Separator;
        _sidebar.BorderThickness = new Thickness(0, 0, 1, 0);
        _sidebar.Padding = new Thickness(12, 14, 12, 12);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Padding = new Thickness(4, 0, 4, 18)
        };
        var icon = new Image { Width = 38, Height = 38, Stretch = Stretch.Uniform };
        try { icon.Source = new SvgImageSource(new Uri(_host.Services.GetAssetPath("assets/icon.svg"))); }
        catch (Exception) { }
        brand.Children.Add(icon);

        var brandText = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        _brandTitle.Text = "小黑盒";
        _brandTitle.FontSize = 18;
        _brandTitle.FontWeight = FontWeights.SemiBold;
        _brandSubtitle.Text = "社区客户端";
        _brandSubtitle.FontSize = 11;
        _brandSubtitle.Foreground = _palette.SecondaryText;
        brandText.Children.Add(_brandTitle);
        brandText.Children.Add(_brandSubtitle);
        brand.Children.Add(brandText);
        layout.Children.Add(brand);

        var navScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _navPanel
        };
        Grid.SetRow(navScroll, 1);
        layout.Children.Add(navScroll);

        AddNav("home", "\uE80F", "推荐", "发现社区热门内容");
        AddNav("community", "\uE716", "社区", "按话题浏览帖子");
        AddNav("search", "\uE721", "搜索", "搜索帖子、游戏和用户");
        AddNav("mine", "\uE77B", "我的", "个人主页、签到与收藏");
        AddNav("games", "\uE7FC", "游戏", "热门游戏与我的游戏库");
        AddNav("compose", "\uE70F", "发帖", "发布图文帖或文章");
        AddNav("account", "\uE713", "账号", "登录、会话与设置");

        var bottom = new StackPanel { Spacing = 8, Padding = new Thickness(0, 12, 0, 0) };
        bottom.Children.Add(new Border { Height = 1, Background = _palette.Separator, Margin = new Thickness(2, 0, 2, 4) });
        _accountCard.Background = _palette.Subtle;
        _accountCard.BorderBrush = _palette.CardStroke;
        _accountCard.BorderThickness = new Thickness(1);
        _accountCard.CornerRadius = new CornerRadius(10);
        _accountCard.Padding = new Thickness(12);
        _accountCard.Child = _accountCardContent;
        _accountCard.Tapped += async (_, _) => await NavigateAsync("account");
        bottom.Children.Add(_accountCard);

        _legal.Text = "第三方客户端 · 仅供学习交流";
        _legal.FontSize = 10;
        _legal.Foreground = _palette.TertiaryText;
        _legal.TextWrapping = TextWrapping.Wrap;
        _legal.HorizontalAlignment = HorizontalAlignment.Center;
        bottom.Children.Add(_legal);

        Grid.SetRow(bottom, 2);
        layout.Children.Add(bottom);
        _sidebar.Child = layout;

        UpdateAccountCard();
    }

    private FrameworkElement BuildTopBar()
    {
        var bar = new Grid
        {
            Height = 68,
            ColumnSpacing = 12,
            Padding = new Thickness(8, 0, 0, 0)
        };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        bar.Children.Add(_backButton);

        var title = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        _pageTitle.Text = "推荐";
        _pageTitle.FontSize = 22;
        _pageTitle.FontWeight = FontWeights.SemiBold;
        _pageSubtitle.Text = "为你推荐的最新内容";
        _pageSubtitle.FontSize = 12;
        _pageSubtitle.Foreground = _palette.SecondaryText;
        title.Children.Add(_pageTitle);
        title.Children.Add(_pageSubtitle);
        Grid.SetColumn(title, 1);
        bar.Children.Add(title);

        var searchHost = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        searchHost.Children.Add(_searchBox);
        Grid.SetColumn(searchHost, 2);
        bar.Children.Add(searchHost);

        _refreshButton.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_refreshButton, 3);
        bar.Children.Add(_refreshButton);

        _accountButton.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_accountButton, 4);
        bar.Children.Add(_accountButton);

        var border = new Border
        {
            Child = bar,
            BorderBrush = _palette.Separator,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        return border;
    }

    private void WireEvents()
    {
        _root.Loaded += async (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            _palette.Apply(_root.ActualTheme == ElementTheme.Dark);
            ApplyCompact(_root.ActualWidth < 900);
            RefreshNav();
            UpdateAccountButton();
            await RunAsync(async () =>
            {
                if (_client.IsLoggedIn) await TryRestoreSessionAsync();
                await ShowHomeAsync();
            });
        };
        _root.Unloaded += (_, _) =>
        {
            _lifetime.Cancel();
            StopQrTimer();
            DisposeWebView();
        };
        _root.ActualThemeChanged += (_, _) =>
        {
            _palette.Apply(_root.ActualTheme == ElementTheme.Dark);
            RefreshAccountChrome();
        };
        _root.SizeChanged += (_, _) => ApplyCompact(_root.ActualWidth < 900);

        _refreshButton.Click += async (_, _) =>
        {
            if (_refreshAction is { } refresh) await RunAsync(refresh);
        };
        _backButton.Click += async (_, _) => await GoBackAsync();
    }

    private void AddNav(string key, string glyph, string label, string tooltip)
    {
        var indicator = new Border
        {
            Width = 3,
            Height = 20,
            CornerRadius = new CornerRadius(2),
            Background = _palette.Accent,
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center
        };
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            Width = 22,
            Foreground = _palette.SecondaryText,
            VerticalAlignment = VerticalAlignment.Center
        };
        var text = new TextBlock
        {
            Text = label,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(indicator);
        Grid.SetColumn(icon, 1);
        row.Children.Add(icon);
        Grid.SetColumn(text, 2);
        row.Children.Add(text);

        var button = new Button
        {
            Content = row,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 9, 8, 9),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += async (_, _) => await NavigateAsync(key);
        _navPanel.Children.Add(button);
        _nav.Add(new NavEntry(key, button, indicator, text, icon, tooltip));
    }

    private async Task NavigateAsync(string page)
    {
        if (page == _currentPage && _backStack.Count == 0)
        {
            return;
        }

        _backStack.Clear();
        _currentPage = page;
        RefreshNav();
        UpdateBackButton();
        HideNotice();
        if (page != "account")
        {
            StopQrTimer();
            DisposeWebView();
        }

        await RunAsync(async () =>
        {
            switch (page)
            {
                case "home": await ShowHomeAsync(); break;
                case "community": await ShowCommunityAsync(); break;
                case "search": await ShowSearchPageAsync(); break;
                case "mine": await ShowMineAsync(); break;
                case "games": await ShowGamesAsync(); break;
                case "compose": await ShowComposePageAsync(); break;
                case "account": ShowAccountPage(); break;
            }
        });
    }

    private async Task ShowHomeAsync()
    {
        _pageTitle.Text = "推荐";
        _pageSubtitle.Text = "小黑盒社区 · 为你推荐";
        _refreshAction = () => ReloadHomeAsync();
        _searchPageBox = null;
        BuildHomePage();
        if (_homePosts.Count == 0) await LoadHomeAsync(reset: true);
    }

    private void PrepareConnectedAnimation(string key, FrameworkElement source)
    {
        try
        {
            _pendingConnectedAnimation = ConnectedAnimationService.GetForCurrentView().PrepareToAnimate(key, source);
        }
        catch (Exception)
        {
            _pendingConnectedAnimation = null;
        }
    }

    private void TryStartConnectedAnimation(FrameworkElement target)
    {
        if (_pendingConnectedAnimation is null) return;
        ConnectedAnimation animation = _pendingConnectedAnimation;
        _pendingConnectedAnimation = null;

        void Start()
        {
            _root.DispatcherQueue.TryEnqueue(() =>
            {
                try { animation.TryStart(target); } catch (Exception) { }
            });
        }

        if (target.IsLoaded) Start();
        else target.Loaded += (_, _) => Start();
    }

    private void ShowPage(FrameworkElement view)
    {
        _content.Children.Clear();
        var transform = new TranslateTransform();
        view.RenderTransform = transform;
        view.Opacity = 1;
        _content.Children.Add(view);

        var storyboard = new Storyboard();
        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, view);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var slide = new DoubleAnimation
        {
            From = 18,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, transform);
        Storyboard.SetTargetProperty(slide, "X");
        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
        storyboard.Begin();
    }

    private void PushBack(Func<Task> action)
    {
        _backStack.Push(action);
        UpdateBackButton();
    }

    private async Task GoBackAsync()
    {
        if (_backStack.Count == 0) return;
        Func<Task> back = _backStack.Pop();
        UpdateBackButton();
        await RunAsync(back);
    }

    private void UpdateBackButton() => _backButton.Visibility = _backStack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    private void RefreshNav()
    {
        foreach (NavEntry entry in _nav)
        {
            bool selected = entry.Key == _currentPage;
            entry.Indicator.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            entry.Label.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            entry.Label.Foreground = selected ? _palette.Text : _palette.SecondaryText;
            entry.Icon.Foreground = selected ? _palette.AccentText : _palette.SecondaryText;
            entry.Button.Background = selected ? _palette.Selected : new SolidColorBrush(Colors.Transparent);
        }
    }

    private void ApplyCompact(bool compact)
    {
        if (_compact == compact) return;
        _compact = compact;
        _root.ColumnDefinitions[0].Width = new GridLength(compact ? 68 : 224);
        _sidebar.Padding = compact ? new Thickness(8, 14, 8, 12) : new Thickness(12, 14, 12, 12);
        _brandTitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _brandSubtitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _legal.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        foreach (NavEntry entry in _nav)
        {
            entry.Label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            entry.Button.Padding = compact ? new Thickness(10, 9, 10, 9) : new Thickness(8, 9, 8, 9);
        }
        UpdateAccountCard();
    }

    private async Task RunAsync(Func<Task> action)
    {
        _progress.Visibility = Visibility.Visible;
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (XhhCaptchaException ex)
        {
            ShowNotice(ex.Message, InfoBarSeverity.Warning, "去网页登录", async () => await NavigateAsync("account"));
            await ShowCaptchaDialogAsync(ex.Message);
        }
        catch (XhhAuthException ex)
        {
            ClearSession();
            ShowNotice(ex.Message, InfoBarSeverity.Warning, "重新登录", async () => await NavigateAsync("account"));
        }
        catch (XhhTwoFactorException ex)
        {
            ShowNotice(ex.Message, InfoBarSeverity.Warning, "打开网页登录", async () => await NavigateAsync("account"));
        }
        catch (XhhException ex)
        {
            ShowNotice(ex.Message, InfoBarSeverity.Error);
        }
        catch (Exception ex)
        {
            ShowNotice(string.IsNullOrWhiteSpace(ex.Message) ? "操作失败，请稍后重试。" : ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _progress.Visibility = Visibility.Collapsed;
            _refreshButton.IsEnabled = _refreshAction is not null;
        }
    }

    private async Task ShowCaptchaDialogAsync(string message)
    {
        if (_captchaDialogOpen) return;
        _captchaDialogOpen = true;
        try
        {
            bool go = await _host.ConfirmAsync("需要小黑盒风控校验",
                $"{message}\n\n是否现在打开「账号 → 网页登录」读取风控令牌？");
            if (go) await NavigateAsync("account");
        }
        catch (Exception)
        {
            // 弹窗不可用时保留 InfoBar 中的“去网页登录”入口。
        }
        finally
        {
            _captchaDialogOpen = false;
        }
    }

    private void ShowNotice(string message, InfoBarSeverity severity, string? actionText = null, Func<Task>? action = null)
    {
        _notice.Message = message;
        _notice.Severity = severity;
        _notice.IsOpen = true;
        if (actionText is not null && action is not null)
        {
            var button = new Button { Content = actionText };
            button.Click += async (_, _) => await action();
            _notice.ActionButton = button;
        }
        else
        {
            _notice.ActionButton = null;
        }
    }

    private void HideNotice() => _notice.IsOpen = false;

    private void UpdateAccountButton()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (_client.IsLoggedIn)
        {
            panel.Children.Add(Avatar(_session?.Avatar, 24));
            string name = string.IsNullOrWhiteSpace(_session?.Nickname) ? "已登录" : _session!.Nickname;
            panel.Children.Add(T(XhhFormat.Clip(name, 10), 13));
            if (_session?.Level > 0) panel.Children.Add(Pill($"Lv.{_session.Level}", accent: true));
        }
        else
        {
            panel.Children.Add(new FontIcon { Glyph = "\uE77B", FontSize = 15, Foreground = _palette.SecondaryText });
            panel.Children.Add(T("未登录", 13, secondary: true));
        }
        _accountButton.Content = panel;
        ToolTipService.SetToolTip(_accountButton, _client.IsLoggedIn ? "查看账号与设置" : "登录小黑盒");
    }

    private void UpdateAccountCard()
    {
        _accountCardContent.Children.Clear();
        if (_compact)
        {
            _accountCardContent.Children.Add(Avatar(_session?.Avatar, 30));
            _accountCardContent.HorizontalAlignment = HorizontalAlignment.Center;
            return;
        }
        _accountCardContent.HorizontalAlignment = HorizontalAlignment.Stretch;

        if (_client.IsLoggedIn)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(Avatar(_session?.Avatar, 34));
            var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(T(XhhFormat.Clip(_session?.Nickname ?? "已登录", 12), 13, weight: FontWeights.SemiBold));
            text.Children.Add(T(_session?.Level > 0 ? $"Lv.{_session.Level} · 查看账号" : "查看账号与设置", 11, secondary: true));
            row.Children.Add(text);
            _accountCardContent.Children.Add(row);
        }
        else
        {
            _accountCardContent.Children.Add(T("未登录", 13, weight: FontWeights.SemiBold));
            _accountCardContent.Children.Add(T("登录后同步收藏、历史与社区偏好", 11, secondary: true, wrap: true));
            var login = AccentBtn("登录小黑盒");
            login.HorizontalAlignment = HorizontalAlignment.Stretch;
            login.Click += async (_, _) => await NavigateAsync("account");
            _accountCardContent.Children.Add(login);
        }
    }

    private void RefreshAccountChrome()
    {
        UpdateAccountButton();
        UpdateAccountCard();
        RefreshNav();
    }

    private void ClearSession()
    {
        _client.ClearLogin();
        _session = null;
        _store.Clear();
        ResetMineCaches();
        UpdateAccountButton();
        UpdateAccountCard();
    }

    // ------------------------------------------------------------------
    // 通用 UI 小组件
    // ------------------------------------------------------------------

    private TextBlock T(string text, double size = 14, bool secondary = false, bool wrap = false,
        Windows.UI.Text.FontWeight? weight = null, bool accent = false, bool tertiary = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Foreground = accent ? _palette.AccentText : tertiary ? _palette.TertiaryText : secondary ? _palette.SecondaryText : _palette.Text
        };
        if (weight is { } value) block.FontWeight = value;
        return block;
    }

    private Border Card(double padding = 16, double radius = 12) => new()
    {
        Padding = new Thickness(padding),
        CornerRadius = new CornerRadius(radius),
        Background = _palette.Card,
        BorderBrush = _palette.CardStroke,
        BorderThickness = new Thickness(1)
    };

    private Button Btn(string text, string? glyph = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        if (!string.IsNullOrWhiteSpace(glyph))
            content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
        content.Children.Add(new TextBlock { Text = text, FontSize = 13 });
        return new Button { Content = content, Padding = new Thickness(12, 6, 12, 6) };
    }

    private Button AccentBtn(string text, string? glyph = null)
    {
        var button = Btn(text, glyph);
        try { button.Style = (Style)Application.Current.Resources["AccentButtonStyle"]; }
        catch (Exception)
        {
            button.Background = _palette.Accent;
            button.Foreground = _palette.OnAccent;
        }
        return button;
    }

    private Button IconButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 15 },
            Padding = new Thickness(9),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private Border Pill(string text, bool accent = false)
    {
        return new Border
        {
            Background = accent ? _palette.AccentSoft : _palette.Subtle,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = accent ? _palette.AccentText : _palette.SecondaryText
            }
        };
    }

    private FrameworkElement Stat(string glyph, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new FontIcon { Glyph = glyph, FontSize = 11, Foreground = _palette.TertiaryText });
        panel.Children.Add(T(text, 11, tertiary: true));
        return panel;
    }

    private FrameworkElement Avatar(string? url, double size)
    {
        var grid = new Grid { Width = size, Height = size };
        var fallback = new FontIcon
        {
            Glyph = "\uE77B",
            FontSize = size * 0.48,
            Foreground = _palette.SecondaryText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(fallback);
        if (!string.IsNullOrWhiteSpace(url) && LoadImage(url, (int)(size * 2)) is { } image)
        {
            var picture = new Image { Source = image, Stretch = Stretch.UniformToFill };
            picture.ImageFailed += (_, _) => picture.Visibility = Visibility.Collapsed;
            grid.Children.Add(picture);
        }
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = _palette.Subtle,
            Child = grid
        };
    }

    private FrameworkElement RemoteImage(string? url, double width, double height, double radius = 10)
    {
        var grid = new Grid { Width = width, Height = height };
        grid.Children.Add(new FontIcon
        {
            Glyph = "\uEB9F",
            FontSize = 22,
            Foreground = _palette.TertiaryText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (!string.IsNullOrWhiteSpace(url) && LoadImage(url, (int)Math.Min(1200, width * 2)) is { } image)
        {
            var picture = new Image { Source = image, Stretch = Stretch.UniformToFill };
            picture.ImageFailed += (_, _) => picture.Visibility = Visibility.Collapsed;
            grid.Children.Add(picture);
        }
        return new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(radius),
            Background = _palette.Subtle,
            Child = grid
        };
    }

    private FrameworkElement EmptyState(string glyph, string title, string subtitle)
    {
        var panel = new StackPanel
        {
            Spacing = 10,
            Padding = new Thickness(0, 64, 0, 64),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 40,
            Foreground = _palette.TertiaryText,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(T(title, 17, weight: FontWeights.SemiBold));
        panel.Children.Add(T(subtitle, 13, secondary: true, wrap: true));
        return panel;
    }

    private FrameworkElement Section(string title, string? subtitle = null)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(T(title, 19, weight: FontWeights.SemiBold));
        if (!string.IsNullOrWhiteSpace(subtitle)) panel.Children.Add(T(subtitle!, 12, secondary: true, wrap: true));
        return panel;
    }

    private Border Divider() => new()
    {
        Height = 1,
        Background = _palette.Separator,
        Margin = new Thickness(0, 10, 0, 10)
    };

    private void MakeClickable(Border card, Func<Task> action)
    {
        card.Tapped += async (_, _) => await RunAsync(action);
        card.PointerEntered += (_, _) => card.BorderBrush = _palette.Accent;
        card.PointerExited += (_, _) => card.BorderBrush = _palette.CardStroke;
    }

    private ImageSource? LoadImage(string? url, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        string normalized = NormalizeImageUrl(url);
        if (_images.TryGetValue(normalized, out ImageSource? cached)) return cached;
        ImageSource? image = null;
        try { image = new BitmapImage { UriSource = new Uri(normalized), DecodePixelWidth = decodeWidth }; }
        catch (UriFormatException) { }
        _images[normalized] = image;
        return image;
    }

    internal static string NormalizeImageUrl(string url)
    {
        url = url.Trim();
        if (url.StartsWith("//", StringComparison.Ordinal)) url = "https:" + url;
        else if (url.StartsWith('/')) url = "https://www.xiaoheihe.cn" + url;
        else if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return url;
        else if (!url.Contains("://", StringComparison.Ordinal)) url = "https://" + url;
        if (url.Contains("format/webp", StringComparison.OrdinalIgnoreCase))
            url = url.Replace("format/webp", "format/jpg", StringComparison.OrdinalIgnoreCase);
        return url;
    }

    private ScrollViewer Scroll(FrameworkElement content, bool horizontalCenter = false) => new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Content = content,
        HorizontalContentAlignment = HorizontalAlignment.Stretch
    };

    private StackPanel Surface(double maxWidth = 1000) => new()
    {
        Spacing = 14,
        Padding = new Thickness(0, 8, 0, 28),
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private sealed record NavEntry(string Key, Button Button, Border Indicator, TextBlock Label, FontIcon Icon, string Tooltip);

    /// <summary>主题画刷由整页共享，切换主题时只改 Color，所有引用者自动跟随。</summary>
    private sealed class Palette
    {
        public SolidColorBrush Text { get; } = new();
        public SolidColorBrush SecondaryText { get; } = new();
        public SolidColorBrush TertiaryText { get; } = new();
        public SolidColorBrush AccentText { get; } = new();
        public SolidColorBrush Card { get; } = new();
        public SolidColorBrush CardStroke { get; } = new();
        public SolidColorBrush Subtle { get; } = new();
        public SolidColorBrush Selected { get; } = new();
        public SolidColorBrush Sidebar { get; } = new();
        public SolidColorBrush Separator { get; } = new();
        public SolidColorBrush Accent { get; } = new();
        public SolidColorBrush AccentSoft { get; } = new();
        public SolidColorBrush OnAccent { get; } = new();

        public void Apply(bool dark)
        {
            Text.Color = dark ? Color(0xFF, 0xF2, 0xF2, 0xF5) : Color(0xFF, 0x1B, 0x1B, 0x1F);
            SecondaryText.Color = dark ? Color(0xFF, 0xBC, 0xBC, 0xC6) : Color(0xFF, 0x5D, 0x5D, 0x66);
            TertiaryText.Color = dark ? Color(0xFF, 0x8A, 0x8A, 0x96) : Color(0xFF, 0x83, 0x83, 0x8C);
            AccentText.Color = dark ? Color(0xFF, 0x7C, 0xD6, 0xFF) : Color(0xFF, 0x00, 0x67, 0xC0);
            Card.Color = dark ? Color(0x18, 0x2A, 0x2A, 0x2E) : Color(0xCC, 0xFF, 0xFF, 0xFF);
            CardStroke.Color = dark ? Color(0x22, 0xFF, 0xFF, 0xFF) : Color(0x14, 0x00, 0x00, 0x00);
            Subtle.Color = dark ? Color(0x18, 0xFF, 0xFF, 0xFF) : Color(0x0A, 0x00, 0x00, 0x00);
            Selected.Color = dark ? Color(0x24, 0x7C, 0xD6, 0xFF) : Color(0x14, 0x00, 0x67, 0xC0);
            Sidebar.Color = dark ? Color(0x12, 0x14, 0x14, 0x17) : Color(0x08, 0xF3, 0xF3, 0xF3);
            Separator.Color = dark ? Color(0x20, 0xFF, 0xFF, 0xFF) : Color(0x12, 0x00, 0x00, 0x00);
            Accent.Color = dark ? Color(0xFF, 0x5B, 0xC8, 0xFF) : Color(0xFF, 0x00, 0x67, 0xC0);
            AccentSoft.Color = dark ? Color(0x22, 0x7C, 0xD6, 0xFF) : Color(0x16, 0x00, 0x67, 0xC0);
            OnAccent.Color = dark ? Color(0xFF, 0x00, 0x1A, 0x26) : Color(0xFF, 0xFF, 0xFF, 0xFF);
        }

        private static Windows.UI.Color Color(byte a, byte r, byte g, byte b) => ColorHelper.FromArgb(a, r, g, b);
    }
}
