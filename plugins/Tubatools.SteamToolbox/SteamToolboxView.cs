using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.PluginSdk;
using Windows.Storage.Streams;

namespace Tubatools.SteamToolbox;

/// <summary>
/// 一次页面实例的全部状态。宿主每次打开工具都会创建新的实例。
/// 界面用 C# 还原原 SteamToolboxPage.xaml 的布局：运行时 XAML 在新式 WinUI 控件上
/// 依赖宿主编译期类型表，插件包无法保证这些类型始终可用。
/// </summary>
internal sealed class SteamToolboxView
{
    private readonly PluginExecutionContext _host;
    private readonly Grid _root = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Palette _palette = new();
    private readonly Dictionary<string, ImageSource?> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly ImageSource? _fallbackAvatar;
    private static Style? _accentButtonStyle, _subtleButtonStyle;

    private readonly SelectorBar _tabs = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly SelectorBarItem _collectionTab = new() { Text = "我的收藏", IsSelected = true };
    private readonly SelectorBarItem _idleTab = new() { Text = "挂卡与时长" };
    private readonly ScrollViewer _collectionScroll = new();
    private readonly ScrollViewer _idleScroll = new();
    private readonly StackPanel _collectionSurface = new() { MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 20, Padding = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _idleContent = new() { Spacing = 20, Padding = new Thickness(0, 16, 0, 8) };

    private readonly TextBlock _gameCount = new();
    private readonly TextBlock _totalSize = new();
    private readonly TextBlock _accountCount = new();
    private readonly TextBlock _installPath = new() { Text = "正在检测…", TextTrimming = TextTrimming.CharacterEllipsis, IsTextSelectionEnabled = true };
    private readonly TextBlock _accountEmpty;
    private readonly TextBlock _emptyText;
    private readonly InfoBar _notice = new() { IsOpen = false, IsClosable = true };
    private readonly ProgressBar _loading = new() { IsIndeterminate = true, Visibility = Visibility.Collapsed };
    private readonly VariableSizedWrapGrid _accounts = new() { Orientation = Orientation.Horizontal, ItemWidth = 340, ItemHeight = 192 };
    private readonly StackPanel _games = new();
    private readonly TextBox _search;
    private readonly ComboBox _sort;
    private readonly Button _refresh;
    private readonly Button _locate;

    private readonly TextBlock _idleStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _currentTaskTitle = new() { Text = "当前没有挂时长任务", FontSize = 18, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _currentTaskName = new() { Text = "选择游戏开始挂时长", FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _currentTaskAppId = new() { FontSize = 12 };
    private readonly TextBlock _currentTaskAccount = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _currentTaskStarted = new() { Text = "—", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _currentTaskElapsed = new() { Text = "00:00:00", FontFamily = new FontFamily("Consolas"), FontSize = 22 };
    private readonly Image _currentTaskArt = new() { Stretch = Stretch.UniformToFill };
    private readonly Border _currentTaskCard;
    private readonly Border _loginForm;
    private readonly Border _gameSelectionCard;
    private readonly StackPanel _guardPanel = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly TextBlock _guardPrompt = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _guardCode;
    private readonly Button _pauseIdle;
    private readonly Button _stopIdle;
    private readonly Button _logoutIdle;
    private readonly Button _passwordLogin;
    private readonly Button _login;
    private readonly Button _guardSubmit;
    private readonly Button _refreshOnline;
    private readonly Button _startOwned;
    private readonly Button _startManual;
    private readonly TextBox _username;
    private readonly PasswordBox _password;
    private readonly CheckBox _preferCode;
    private readonly ComboBox _ownedGames;
    private readonly ListView _friends = new() { SelectionMode = ListViewSelectionMode.None, MaxHeight = 280 };
    private readonly NumberBox _idleAppId;
    private readonly Image _loginQr;
    private readonly TextBlock _onlineProfile = new() { Text = "登录后可查看账号资料、拥有的游戏与好友。", TextWrapping = TextWrapping.Wrap };
    private readonly Grid _loginColumns = new() { ColumnSpacing = 32, RowSpacing = 20 };
    private readonly StackPanel _credentialFields = new() { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _qrPanel = new() { Spacing = 12, Width = 240, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly InfoBar _idleStateBar = new() { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational };
    private readonly Expander _friendsExpander = new() { Header = "好友", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };

    private SteamSnapshot? _snapshot;
    private IReadOnlyList<SteamOwnedGame> _owned = [];
    private bool _busy, _loadingProfile, _loaded;
    private string? _profileAccount, _renderedTaskArt, _renderedQrUrl;

    private SteamToolboxView(PluginExecutionContext host)
    {
        _host = host;
        _dispatcher = DispatcherQueue.GetForCurrentThread() ?? throw new InvalidOperationException("插件页面必须在界面线程创建。");
        _accountEmpty = Text("还没有本机账号记录，登录 Steam 后刷新即可。", 14, secondary: true, wrap: true);
        _emptyText = Text("", 14, secondary: true, wrap: true);
        _emptyText.Visibility = Visibility.Collapsed;
        try { _fallbackAvatar = new SvgImageSource(new Uri(_host.Services.GetAssetPath("assets/icon.svg"))); }
        catch { }
        _search = new TextBox { Header = "搜索游戏", PlaceholderText = "名称或 AppID" };
        _sort = new ComboBox { Header = "排序", SelectedIndex = 0 };
        _sort.Items.Add(new ComboBoxItem { Content = "名称" });
        _sort.Items.Add(new ComboBoxItem { Content = "占用从大到小" });
        _refresh = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new FontIcon { Glyph = "\uE72C", FontSize = 14 }, new TextBlock { Text = "刷新" } }
            }
        };
        ToolTipService.SetToolTip(_refresh, "重新扫描账号与游戏");
        _locate = Btn("更改目录");
        _currentTaskCard = Card(20);
        _loginForm = Card(24);
        _gameSelectionCard = Card(24);
        _guardCode = new TextBox { Header = "Steam Guard 验证码", MaxLength = 10 };
        _pauseIdle = Btn("暂停");
        _stopIdle = Btn("停止任务");
        _logoutIdle = Btn("退出账号");
        _passwordLogin = AccentBtn("账号密码登录");
        _login = Btn("生成登录二维码");
        _guardSubmit = Btn("提交验证码");
        _refreshOnline = Btn("刷新账号资料");
        _startOwned = Btn("挂所选游戏");
        _startManual = AccentBtn("挂此 AppID");
        _username = new TextBox { Header = "账号名称", PlaceholderText = "Steam 登录账号，非昵称" };
        _password = new PasswordBox { Header = "密码" };
        _preferCode = new CheckBox { Content = "使用手机令牌码验证" };
        _ownedGames = new ComboBox { Header = "账号游戏库（包含未安装的游戏）", HorizontalAlignment = HorizontalAlignment.Stretch };
        _idleAppId = new NumberBox { Header = "手动指定 AppID", Minimum = 1, Maximum = uint.MaxValue, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        _loginQr = new Image { Width = 180, Height = 180, Visibility = Visibility.Collapsed };
        _idleStateBar.Content = _idleStatus;

        _palette.Apply(dark: false);
        BuildFrame();
        WireEvents();
    }

    public static FrameworkElement Create(PluginExecutionContext host) => new SteamToolboxView(host)._root;

    private void BuildFrame()
    {
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _tabs.Items.Add(_collectionTab);
        _tabs.Items.Add(_idleTab);
        _root.Children.Add(_tabs);

        _collectionScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _collectionScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _collectionScroll.Content = BuildCollectionPanel();
        _root.Children.Add(_collectionScroll);
        Grid.SetRow(_collectionScroll, 1);

        _idleScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _idleScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _idleScroll.Visibility = Visibility.Collapsed;
        _idleScroll.HorizontalContentAlignment = HorizontalAlignment.Center;
        _idleScroll.Content = _idleContent;
        _root.Children.Add(_idleScroll);
        Grid.SetRow(_idleScroll, 1);
        BuildIdlePanel();
    }

    private void WireEvents()
    {
        _tabs.SelectionChanged += (sender, _) => ShowTab(sender.SelectedItem == _idleTab);
        _refresh.Click += async (_, _) => await GuardAsync(RefreshAsync);
        _locate.Click += async (_, _) => await GuardAsync(LocateAsync);
        _search.TextChanged += (_, _) => ApplyFilter();
        _sort.SelectionChanged += (_, _) => ApplyFilter();
        _login.Click += (_, _) => { HideNotice(); SteamIdleService.Login(); };
        _passwordLogin.Click += (_, _) => PasswordLogin();
        _guardSubmit.Click += (_, _) => SubmitGuard();
        _logoutIdle.Click += (_, _) => SteamIdleService.Disconnect();
        _stopIdle.Click += (_, _) => StopIdle();
        _pauseIdle.Click += (_, _) => PauseOrResume();
        _refreshOnline.Click += async (_, _) => await GuardAsync(RefreshOnlineAsync);
        _startOwned.Click += (_, _) => StartOwnedIdle();
        _startManual.Click += (_, _) => StartManualIdle();
        _idleTimer.Tick += (_, _) => RenderCurrentTask();
        _root.Loaded += OnLoaded;
        _root.Unloaded += OnUnloaded;
        _root.SizeChanged += (_, _) => ResizeBodies();
        _root.ActualThemeChanged += (_, _) => _palette.Apply(_root.ActualTheme == ElementTheme.Dark);
        _root.Loaded += (_, _) => _palette.Apply(_root.ActualTheme == ElementTheme.Dark);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        SteamIdleService.Changed += OnIdleChanged;
        _idleTimer.Start();
        ResizeBodies();
        OnIdleChanged();
        _ = GuardAsync(RefreshAsync);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SteamIdleService.Changed -= OnIdleChanged;
        _idleTimer.Stop();
    }

    private async Task GuardAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) when (_host.CancellationToken.IsCancellationRequested) { }
        catch (Exception e) { ShowError(e); }
    }

    private void ShowTab(bool idle)
    {
        _collectionScroll.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
        _idleScroll.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
        ResizeBodies();
    }

    private void ResizeBodies()
    {
        // 宿主插件页已统一内缩 24px；这里按页面内容宽度还原原内置页 1040 的居中表面。
        double width = Math.Max(0, _root.ActualWidth);
        _collectionSurface.Width = Math.Min(1040, width);
        _idleContent.Width = Math.Min(1040, width);
        double inner = Math.Max(0, _idleContent.Width - 96);
        bool compact = inner < 680;
        _loginColumns.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 248);
        Grid.SetColumn(_qrPanel, compact ? 0 : 1);
        Grid.SetRow(_qrPanel, compact ? 1 : 0);
        _credentialFields.Width = Math.Min(420, Math.Max(0, compact ? inner : inner - 280));
    }

    private void ShowError(Exception e)
    {
        _notice.Message = string.IsNullOrWhiteSpace(e.Message) ? "操作失败。" : e.Message;
        _notice.Severity = InfoBarSeverity.Warning;
        _notice.IsOpen = true;
    }

    private void HideNotice() => _notice.IsOpen = false;

    private StackPanel BuildCollectionPanel()
    {
        _collectionSurface.Children.Add(BuildSummary());
        _collectionSurface.Children.Add(BuildInstallRow());
        _collectionSurface.Children.Add(_notice);
        _collectionSurface.Children.Add(_loading);
        _collectionSurface.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Text("你的账号", 20, weight: FontWeights.SemiBold),
                Text("这台电脑上登录过的 Steam 账号", 12, secondary: true, wrap: true)
            }
        });
        _collectionSurface.Children.Add(_accounts);
        _collectionSurface.Children.Add(_accountEmpty);
        _collectionSurface.Children.Add(Text("封面优先使用本机缓存，缺失时从 Steam CDN 加载。时长来自最近账号的本机记录。", 12, secondary: true, wrap: true));
        _collectionSurface.Children.Add(Text("已安装游戏", 20, weight: FontWeights.SemiBold));
        _collectionSurface.Children.Add(BuildSearchRow());
        _collectionSurface.Children.Add(_emptyText);
        _collectionSurface.Children.Add(_games);
        return _collectionSurface;
    }

    private Border BuildSummary()
    {
        var card = Card(24);
        var grid = new Grid { ColumnSpacing = 24 };
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(Stat("游戏收藏", "\uE7FC", _gameCount));
        var size = Stat("磁盘占用", "\uEDA2", _totalSize); Grid.SetColumn(size, 1); grid.Children.Add(size);
        var accounts = Stat("Steam 账号", "\uE77B", _accountCount); Grid.SetColumn(accounts, 2); grid.Children.Add(accounts);
        card.Child = grid;
        return card;
    }

    private FrameworkElement Stat(string label, string glyph, TextBlock value)
    {
        value.FontSize = 28;
        value.FontWeight = FontWeights.SemiBold;
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Text(label, 14, secondary: true),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children = { new FontIcon { Glyph = glyph, Foreground = _palette.AccentText }, value }
                }
            }
        };
    }

    private Grid BuildInstallRow()
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new FontIcon { Glyph = "\uE8B7", Foreground = _palette.SecondaryText, VerticalAlignment = VerticalAlignment.Top });
        var path = new StackPanel
        {
            Spacing = 4,
            Children = { Text("STEAM 安装目录", 12, secondary: true), _installPath }
        };
        Grid.SetColumn(path, 1);
        grid.Children.Add(path);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _locate, _refresh } };
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        return grid;
    }

    private Grid BuildSearchRow()
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(_search);
        Grid.SetColumn(_sort, 1);
        grid.Children.Add(_sort);
        return grid;
    }

    private void BuildIdlePanel()
    {
        _idleContent.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Text("挂卡与游戏时长", 20, weight: FontWeights.SemiBold),
                Text("登录账号 → 选择游戏 → 查看当前任务。", 14, secondary: true, wrap: true)
            }
        });

        _currentTaskCard.BorderBrush = _palette.Accent;
        _currentTaskCard.Child = new StackPanel
        {
            Spacing = 16,
            Children =
            {
                _currentTaskTitle,
                new Grid
                {
                    ColumnSpacing = 16,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Auto },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                    },
                    Children =
                    {
                        new Border
                        {
                            Width = 136, Height = 64, CornerRadius = new CornerRadius(4), Background = _palette.Subtle,
                            Child = new Grid
                            {
                                Children =
                                {
                                    new FontIcon { Glyph = "\uE7FC", Foreground = _palette.AccentText },
                                    _currentTaskArt
                                }
                            }
                        },
                        TaskInfo()
                    }
                },
                new Grid
                {
                    ColumnSpacing = 20,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                        new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                    },
                    Children = { FieldBlock("开始时间", _currentTaskStarted), SecondColumn(FieldBlock("本次累计运行", _currentTaskElapsed)) }
                },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _pauseIdle, _stopIdle, _logoutIdle } },
                Text("此处计时表示工具箱会话运行时间，暂停和断线时不累计；Steam 实际入账时长及掉卡以服务器为准。", 12, secondary: true, wrap: true)
            }
        };
        _idleContent.Children.Add(_currentTaskCard);

        _credentialFields.Children.Add(_username);
        _credentialFields.Children.Add(_password);
        _credentialFields.Children.Add(_preferCode);
        _credentialFields.Children.Add(_passwordLogin);
        _qrPanel.Children.Add(new FontIcon { Glyph = "\uED14", FontSize = 32, Foreground = _palette.AccentText });
        _qrPanel.Children.Add(Text("手机扫码，更轻松", 14, weight: FontWeights.SemiBold));
        _qrPanel.Children.Add(_login);
        _qrPanel.Children.Add(_loginQr);
        _qrPanel.Children.Add(Text("用 Steam 手机客户端扫码并确认", 12, secondary: true));
        Grid.SetColumn(_qrPanel, 1);
        _loginColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _loginColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(248) });
        _loginColumns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _loginColumns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _loginColumns.Children.Add(_credentialFields);
        _loginColumns.Children.Add(_qrPanel);
        _guardPanel.Children.Add(_guardPrompt);
        _guardPanel.Children.Add(_guardCode);
        _guardPanel.Children.Add(_guardSubmit);
        _loginForm.Child = new StackPanel
        {
            Spacing = 16,
            Children =
            {
                Text("登录 Steam", 18, weight: FontWeights.SemiBold),
                _loginColumns,
                _guardPanel,
                _idleStateBar,
                Text("密码和会话凭据仅保留在内存中，不保存到设置。", 12, secondary: true, wrap: true)
            }
        };
        _idleContent.Children.Add(_loginForm);

        var selectionTitle = new Grid();
        selectionTitle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        selectionTitle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selectionTitle.Children.Add(Text("选择要挂时长的游戏", 18, weight: FontWeights.SemiBold));
        Grid.SetColumn(_refreshOnline, 1);
        selectionTitle.Children.Add(_refreshOnline);
        var manual = new Grid { ColumnSpacing = 12 };
        manual.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        manual.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        manual.Children.Add(_idleAppId);
        _startManual.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(_startManual, 1);
        manual.Children.Add(_startManual);
        _friendsExpander.Content = _friends;
        _gameSelectionCard.Child = new StackPanel
        {
            Spacing = 16,
            Children =
            {
                selectionTitle,
                _onlineProfile,
                new StackPanel { Spacing = 8, Children = { _ownedGames, _startOwned } },
                manual,
                Text("在其他客户端开始游戏时会让出会话。关闭本页后继续运行，退出工具箱时停止。时长和掉卡资格以 Steam 为准。", 14, secondary: true, wrap: true),
                _friendsExpander
            }
        };
        _idleContent.Children.Add(_gameSelectionCard);
    }

    private FrameworkElement TaskInfo()
    {
        var panel = new StackPanel
        {
            Spacing = 4,
            Children = { _currentTaskName, _currentTaskAppId, _currentTaskAccount },
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Grid.SetColumn(panel, 1);
        return panel;
    }

    private static FrameworkElement SecondColumn(FrameworkElement element)
    {
        Grid.SetColumn(element, 1);
        return element;
    }

    private FrameworkElement FieldBlock(string label, TextBlock value) => new StackPanel
    {
        Spacing = 4,
        Children = { Text(label, 12, secondary: true), value }
    };

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        _refresh.IsEnabled = false;
        _loading.Visibility = Visibility.Visible;
        HideNotice();
        try
        {
            var configured = await _host.Services.GetSettingAsync("steamRoot", _host.CancellationToken);
            var root = SteamCatalog.FindRoot(configured);
            _installPath.Text = root ?? "未找到 Steam，请指定包含 steam.exe 的安装目录。";
            _snapshot = root == null ? null : await Task.Run(() => SteamCatalog.Scan(root), _host.CancellationToken);
            var games = _snapshot?.Libraries.SelectMany(l => l.Games).ToList() ?? [];
            _gameCount.Text = games.Count.ToString();
            _totalSize.Text = SteamCatalog.Size(games.Sum(g => g.Bytes));
            _accountCount.Text = (_snapshot?.Accounts.Count ?? 0).ToString();
            _accounts.Children.Clear();
            foreach (var account in _snapshot?.Accounts ?? []) _accounts.Children.Add(BuildAccountCard(account));
            _accountEmpty.Visibility = _snapshot?.Accounts.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            ApplyFilter();
            if (_snapshot?.Warnings.Count > 0)
            {
                _notice.Message = string.Join("\n", _snapshot.Warnings.Take(6));
                _notice.Severity = InfoBarSeverity.Warning;
                _notice.IsOpen = true;
            }
        }
        finally
        {
            _busy = false;
            _refresh.IsEnabled = true;
            _loading.Visibility = Visibility.Collapsed;
        }
    }

    private async Task LocateAsync()
    {
        var folder = await _host.Services.PickFolderAsync("选择 Steam 安装目录", _host.CancellationToken);
        if (folder is null) return;
        if (!File.Exists(Path.Combine(folder, "steam.exe")))
        {
            ShowError(new ArgumentException("此目录没有 steam.exe，请选择 Steam 安装目录。"));
            return;
        }
        await _host.Services.SetSettingAsync("steamRoot", folder, _host.CancellationToken);
        await RefreshAsync();
    }

    private Border BuildAccountCard(SteamAccount account)
    {
        var card = Card(20);
        card.Width = 328;
        card.Height = 180;
        card.Margin = new Thickness(0, 0, 12, 12);
        var grid = new Grid { RowSpacing = 12 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(Avatar(account));
        var info = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Trimmed(Text(account.Name, 18, weight: FontWeights.SemiBold)),
                Trimmed(Text(account.Login, 14, secondary: true))
            }
        };
        Grid.SetColumn(info, 1);
        header.Children.Add(info);
        grid.Children.Add(header);

        var id = Text(account.Id, 12, secondary: true);
        id.FontFamily = new FontFamily("Consolas");
        id.IsTextSelectionEnabled = true;
        Grid.SetRow(id, 1);
        grid.Children.Add(id);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(Text(account.Status, 12, accent: true));
        var profile = IconButton("\uE8A7", "打开 Steam 个人主页");
        profile.Click += (_, _) => Open($"https://steamcommunity.com/profiles/{account.Id}");
        profile.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(profile, 1);
        footer.Children.Add(profile);
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        card.Child = grid;
        return card;
    }

    private FrameworkElement Avatar(SteamAccount account)
    {
        var picture = new PersonPicture { Width = 48, Height = 48, DisplayName = account.Name };
        picture.ProfilePicture = LoadImage(account.Avatar, 96) ?? _fallbackAvatar;
        return picture;
    }

    private static TextBlock Trimmed(TextBlock block)
    {
        block.TextTrimming = TextTrimming.CharacterEllipsis;
        return block;
    }

    private void ApplyFilter()
    {
        string query = _search.Text.Trim();
        var libraries = _snapshot?.Libraries.Select(library => new
        {
            library.Path,
            Games = (SortIndex == 1 ? library.Games.OrderByDescending(g => g.Bytes).ThenBy(g => g.Name) : library.Games.OrderBy(g => g.Name))
                .Where(g => g.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || g.AppId.ToString().Contains(query))
                .ToList()
        }).Where(library => library.Games.Count > 0).ToList() ?? [];

        _games.Children.Clear();
        foreach (var library in libraries)
        {
            var header = Text($"{library.Path}  ·  {library.Games.Count} 个游戏  ·  {SteamCatalog.Size(library.Games.Sum(g => g.Bytes))}", 14, weight: FontWeights.SemiBold, wrap: true);
            header.Margin = new Thickness(4, 20, 0, 12);
            _games.Children.Add(header);
            var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 278, ItemHeight = 302 };
            foreach (var game in library.Games) grid.Children.Add(BuildGameCard(game));
            _games.Children.Add(grid);
        }
        _emptyText.Visibility = libraries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _emptyText.Text = _snapshot == null
            ? "安装并登录 Steam 后，你的游戏收藏会显示在这里。"
            : query.Length > 0 ? "没有匹配的游戏，试试其他名称或 AppID。"
            : "没有找到游戏清单，请确认游戏库磁盘已连接并在 Steam 中正确添加。";
    }

    private int SortIndex => _sort.SelectedIndex;

    private Border BuildGameCard(SteamGame game)
    {
        var card = Card(0);
        card.Width = 270;
        card.Margin = new Thickness(0, 0, 8, 12);
        var content = new StackPanel();

        var coverPanel = new Grid { Height = 126, Background = _palette.Subtle };
        coverPanel.Children.Add(new FontIcon { Glyph = "\uE7FC", FontSize = 44, Foreground = _palette.AccentText });
        var cover = LoadImage(game.Art, 270);
        if (cover is not null) coverPanel.Children.Add(new Image { Source = cover, Stretch = Stretch.UniformToFill });
        content.Children.Add(coverPanel);

        var body = new StackPanel { Padding = new Thickness(14), Spacing = 8 };
        var name = Text(game.Name, 17, weight: FontWeights.SemiBold);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTipService.SetToolTip(name, game.Name);
        body.Children.Add(name);
        body.Children.Add(Text(game.Details, 14, secondary: true));
        body.Children.Add(Text(game.Playtime, 12, secondary: true, wrap: true));
        var updated = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        updated.Children.Add(Text("更新于", 12));
        updated.Children.Add(Text(game.Updated, 12));
        updated.Children.Add(Text(game.State, 12, accent: true));
        body.Children.Add(updated);

        var actions = new Grid { ColumnSpacing = 6 };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var launch = AccentBtn("▷ 启动");
        launch.HorizontalAlignment = HorizontalAlignment.Stretch;
        launch.IsEnabled = game.Installed;
        launch.Click += (_, _) => Open($"steam://rungameid/{game.AppId}");
        actions.Children.Add(launch);
        var idle = Btn("挂时长");
        idle.Click += (_, _) => StartIdle(game.AppId);
        Grid.SetColumn(idle, 1);
        actions.Children.Add(idle);
        var more = new Button { Content = new FontIcon { Glyph = "\uE712" }, Padding = new Thickness(8) };
        ToolTipService.SetToolTip(more, "游戏操作");
        var flyout = new MenuFlyout();
        var folder = new MenuFlyoutItem { Text = "打开游戏文件夹" };
        folder.Click += (_, _) => OpenFolder(game.Folder);
        var store = new MenuFlyoutItem { Text = "Steam 商店页面" };
        store.Click += (_, _) => Open($"https://store.steampowered.com/app/{game.AppId}/");
        flyout.Items.Add(folder);
        flyout.Items.Add(store);
        more.Flyout = flyout;
        Grid.SetColumn(more, 2);
        actions.Children.Add(more);
        body.Children.Add(actions);

        content.Children.Add(body);
        card.Child = content;
        return card;
    }

    private void Open(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) { ShowError(e); }
    }

    private void OpenFolder(string folder)
    {
        if (Directory.Exists(folder)) Open(folder);
        else ShowError(new IOException("游戏目录不可访问，请确认游戏库磁盘已连接。"));
    }

    private void PasswordLogin()
    {
        if (string.IsNullOrWhiteSpace(_username.Text) || string.IsNullOrEmpty(_password.Password))
        {
            ShowError(new ArgumentException("请输入账号名称和密码。"));
            return;
        }
        HideNotice();
        SteamIdleService.Login(_username.Text.Trim(), _password.Password, _preferCode.IsChecked == true);
        _password.Password = "";
    }

    private void SubmitGuard()
    {
        try
        {
            SteamIdleService.SubmitGuardCode(_guardCode.Text);
            _guardCode.Text = "";
        }
        catch (Exception e) { ShowError(e); }
    }

    private void StartOwnedIdle()
    {
        if (_ownedGames.SelectedItem is OwnedGameChoice choice) StartIdle(choice.Game.AppId);
        else ShowError(new ArgumentException("请先在账号游戏库中选择游戏。"));
    }

    private void StartManualIdle()
    {
        var value = _idleAppId.Value;
        if (!double.IsNaN(value) && value >= 1 && value <= uint.MaxValue && value == Math.Truncate(value))
            StartIdle((uint)value);
        else ShowError(new ArgumentException("请输入有效的游戏 AppID。"));
    }

    private void StartIdle(uint appId)
    {
        try
        {
            var local = _snapshot?.Libraries.SelectMany(l => l.Games).FirstOrDefault(g => g.AppId == appId);
            var owned = _owned.FirstOrDefault(g => g.AppId == appId);
            SteamIdleService.Play(appId, owned?.Name ?? local?.Name, local?.Art);
            _tabs.SelectedItem = _idleTab;
            _idleScroll.ChangeView(null, 0, null);
            HideNotice();
        }
        catch (Exception e) { _tabs.SelectedItem = _idleTab; ShowError(e); }
    }

    private void StopIdle()
    {
        try { SteamIdleService.Play(0); }
        catch (Exception e) { ShowError(e); }
    }

    private void PauseOrResume()
    {
        try
        {
            if (SteamIdleService.CurrentTask.State == SteamIdleTaskState.Paused) SteamIdleService.Resume();
            else SteamIdleService.Pause();
        }
        catch (Exception e) { ShowError(e); }
    }

    private void OnIdleChanged() => _dispatcher.TryEnqueue(async () =>
    {
        try
        {
            _idleStatus.Text = SteamIdleService.Status + (SteamIdleService.ConnectionStatus.Length == 0 ? "" : "\n" + SteamIdleService.ConnectionStatus);
            RenderCurrentTask();
            if (SteamIdleService.LoggedIn && _profileAccount != SteamIdleService.AccountLabel && !_loadingProfile) await RefreshOnlineAsync();
            if (!SteamIdleService.LoggedIn)
            {
                _profileAccount = null;
                _ownedGames.Items.Clear();
                _friends.Items.Clear();
                _owned = [];
                _onlineProfile.Text = "登录后自动加载账号游戏库与好友，也可点击刷新。";
            }
            _guardPanel.Visibility = SteamIdleService.GuardPrompt == null ? Visibility.Collapsed : Visibility.Visible;
            _guardPrompt.Text = SteamIdleService.GuardPrompt ?? "";
            _loginQr.Visibility = SteamIdleService.QrUrl == null ? Visibility.Collapsed : Visibility.Visible;
            await RenderQrAsync();
        }
        catch (Exception e) { ShowError(e); }
    });

    private async Task RenderQrAsync()
    {
        var url = SteamIdleService.QrUrl;
        if (url == _renderedQrUrl) return;
        _renderedQrUrl = url;
        if (url is null) { _loginQr.Source = null; return; }
        var bytes = QrCodeService.GeneratePng(url);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
        }
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        _loginQr.Source = bitmap;
    }

    private async Task RefreshOnlineAsync()
    {
        if (_loadingProfile) return;
        _loadingProfile = true;
        var requestedAccount = SteamIdleService.AccountLabel;
        try
        {
            var data = await SteamIdleService.GetOnlineInfoAsync();
            _onlineProfile.Text = $"{data.Name} · SteamID {data.Id}\n{data.Games.Count} 个游戏 · {data.Friends.Count} 位好友";
            _owned = data.Games;
            _ownedGames.Items.Clear();
            foreach (var game in data.Games) _ownedGames.Items.Add(new OwnedGameChoice(game));
            _friends.Items.Clear();
            foreach (var friend in data.Friends) _friends.Items.Add(friend.Display);
            _profileAccount = SteamIdleService.AccountLabel;
        }
        catch (Exception e)
        {
            if (SteamIdleService.AccountLabel == requestedAccount)
            {
                // Explicit refresh retries; timer/status changes do not spam the endpoint.
                _profileAccount = requestedAccount;
                _onlineProfile.Text = "账号游戏库读取失败，可点击刷新重试：" + e.Message;
            }
        }
        finally { _loadingProfile = false; }
    }

    private void RenderCurrentTask()
    {
        var connectionStatus = SteamIdleService.ConnectionStatus;
        _idleStatus.Text = SteamIdleService.Status + (connectionStatus.Length == 0 ? "" : "\n" + connectionStatus);
        var task = SteamIdleService.CurrentTask;
        bool running = task.State == SteamIdleTaskState.Running;
        bool paused = task.State == SteamIdleTaskState.Paused;
        _currentTaskTitle.Text = task.State switch
        {
            SteamIdleTaskState.Running => "当前正在挂时长",
            SteamIdleTaskState.Paused => "挂时长已暂停",
            SteamIdleTaskState.Disconnected => "挂时长已中断 · 连接断开",
            SteamIdleTaskState.Stopped => "上次任务 · 已停止",
            _ => "当前没有挂时长任务"
        };
        _currentTaskName.Text = task.AppId == 0 ? "选择一个游戏，开始挂时长" : task.Name;
        _currentTaskAppId.Text = task.AppId == 0 ? "" : $"AppID {task.AppId}";
        _currentTaskAccount.Text = task.AppId == 0 ? SteamIdleService.AccountLabel : task.Account;
        _currentTaskStarted.Text = task.StartedAt?.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss") ?? "—";
        _currentTaskElapsed.Text = $"{(long)task.Elapsed.TotalHours:00}:{task.Elapsed.Minutes:00}:{task.Elapsed.Seconds:00}";
        if (_renderedTaskArt != task.Art)
        {
            _renderedTaskArt = task.Art;
            _currentTaskArt.Source = LoadImage(task.Art, 320);
        }
        _pauseIdle.Content = paused ? "恢复" : "暂停";
        _pauseIdle.IsEnabled = running || (paused && SteamIdleService.LoggedIn && !SteamIdleService.PlayingBlocked);
        _stopIdle.IsEnabled = running || paused;
        _logoutIdle.IsEnabled = SteamIdleService.LoggedIn || SteamIdleService.QrUrl != null || SteamIdleService.GuardPrompt != null;
        _loginForm.Visibility = SteamIdleService.LoggedIn ? Visibility.Collapsed : Visibility.Visible;
        _currentTaskCard.Visibility = SteamIdleService.LoggedIn || task.AppId != 0 ? Visibility.Visible : Visibility.Collapsed;
        _gameSelectionCard.Visibility = SteamIdleService.LoggedIn ? Visibility.Visible : Visibility.Collapsed;
        _startOwned.IsEnabled = _startManual.IsEnabled = SteamIdleService.LoggedIn && !SteamIdleService.PlayingBlocked;
    }

    private ImageSource? LoadImage(string? uri, int decodeWidth)
    {
        if (string.IsNullOrEmpty(uri)) return null;
        if (_images.TryGetValue(uri, out var cached)) return cached;
        ImageSource? image = null;
        try { image = new BitmapImage { UriSource = new Uri(uri), DecodePixelWidth = decodeWidth }; }
        catch (UriFormatException) { }
        _images[uri] = image;
        return image;
    }

    private TextBlock Text(string text, double size, bool secondary = false, bool wrap = false,
        Windows.UI.Text.FontWeight? weight = null, bool accent = false)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Foreground = accent ? _palette.AccentText : secondary ? _palette.SecondaryText : _palette.Text
        };
        if (weight is { } value) block.FontWeight = value;
        return block;
    }

    private Border Card(double padding) => new()
    {
        Padding = new Thickness(padding),
        CornerRadius = new CornerRadius(8),
        Background = _palette.Card,
        BorderBrush = _palette.CardStroke,
        BorderThickness = new Thickness(1)
    };

    private Button Btn(string text) => new() { Content = text };

    private Button AccentBtn(string text)
    {
        var button = new Button { Content = text };
        if ((_accentButtonStyle ??= LoadButtonStyle("AccentButtonStyle")) is { } style) button.Style = style;
        else { button.Background = _palette.Accent; button.Foreground = _palette.OnAccent; }
        return button;
    }

    private Button IconButton(string glyph, string tooltip)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph }, Padding = new Thickness(8) };
        if ((_subtleButtonStyle ??= LoadButtonStyle("SubtleButtonStyle")) is { } style) button.Style = style;
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private static Style? LoadButtonStyle(string key)
    {
        try
        {
            var probe = (Button)XamlReader.Load(
                $"<Button xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Style=\"{{StaticResource {key}}}\"/>");
            return probe.Style;
        }
        catch { return null; }
    }

    /// <summary>主题画刷实例在整页共享；切换主题时只改 Color，全部引用者自动跟随。</summary>
    private sealed class Palette
    {
        public SolidColorBrush Text { get; } = new();
        public SolidColorBrush SecondaryText { get; } = new();
        public SolidColorBrush AccentText { get; } = new();
        public SolidColorBrush Card { get; } = new();
        public SolidColorBrush CardStroke { get; } = new();
        public SolidColorBrush Subtle { get; } = new();
        public SolidColorBrush Control { get; } = new();
        public SolidColorBrush Accent { get; } = new();
        public SolidColorBrush OnAccent { get; } = new();

        public void Apply(bool dark)
        {
            Text.Color = dark ? Color(0xFF, 0xFF, 0xFF, 0xFF) : Color(0xFF, 0x1B, 0x1B, 0x1B);
            SecondaryText.Color = dark ? Color(0xFF, 0xC5, 0xC5, 0xC5) : Color(0xFF, 0x5D, 0x5D, 0x5D);
            AccentText.Color = dark ? Color(0xFF, 0x60, 0xCD, 0xFF) : Color(0xFF, 0x00, 0x5F, 0xB8);
            Card.Color = dark ? Color(0x1A, 0x2B, 0x2B, 0x2B) : Color(0xD9, 0xFF, 0xFF, 0xFF);
            CardStroke.Color = dark ? Color(0x1F, 0xFF, 0xFF, 0xFF) : Color(0x14, 0x00, 0x00, 0x00);
            Subtle.Color = dark ? Color(0x1A, 0xFF, 0xFF, 0xFF) : Color(0x0A, 0x00, 0x00, 0x00);
            Control.Color = dark ? Color(0x14, 0xFF, 0xFF, 0xFF) : Color(0x0F, 0x00, 0x00, 0x00);
            Accent.Color = dark ? Color(0xFF, 0x60, 0xCD, 0xFF) : Color(0xFF, 0x00, 0x5F, 0xB8);
            OnAccent.Color = dark ? Color(0xFF, 0x00, 0x1A, 0x26) : Color(0xFF, 0xFF, 0xFF, 0xFF);
        }

        private static Windows.UI.Color Color(byte a, byte r, byte g, byte b) => ColorHelper.FromArgb(a, r, g, b);
    }
}

internal sealed class OwnedGameChoice(SteamOwnedGame game)
{
    public SteamOwnedGame Game { get; } = game;
    public override string ToString() => Game.Display;
}
