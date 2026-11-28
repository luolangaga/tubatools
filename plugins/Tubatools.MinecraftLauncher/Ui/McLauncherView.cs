using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.PluginSdk;
using Tubatools.MinecraftLauncher.Services;

namespace Tubatools.MinecraftLauncher.Ui;

internal interface IMcPage
{
    void OnShown();
}

/// <summary>
/// PCL CE 风格的主壳：顶部品牌标签栏（启动 / 版本 / 下载 / 设置）+ 页面容器。
/// 左侧 300px 启动面板与右侧卡片流由 <see cref="McLaunchPage"/> 负责。
/// </summary>
internal sealed class McLauncherView
{
    private readonly PluginExecutionContext _host;
    private readonly DispatcherQueue _dispatcher;
    private readonly Grid _root = new();
    private readonly McPalette _palette = new();
    private readonly LauncherSettings _settings;
    private readonly McAccountStore _accounts;
    private readonly McLaunchLog _log;
    private readonly Grid _pageHost = new();
    private readonly Dictionary<string, FrameworkElement> _pages = new(StringComparer.Ordinal);
    private readonly List<(string Id, Button Button, Border Fill, Border Underline, TextBlock Label, FontIcon Icon)> _nav = [];
    private readonly InfoBar _notice = new() { IsOpen = false, IsClosable = true, Margin = new Thickness(12, 0, 12, 10) };
    private readonly DispatcherQueueTimer _noticeTimer;
    private readonly TextBlock _gameFolderLabel = new();
    private Button? _folderButton;

    private McVersionScanResult _scan = new();
    private IReadOnlyList<JavaRuntime> _javas = [];
    private bool _initialized;
    private bool _launching;
    private string _page = "launch";
    private Page? _hostPage;
    private ElementTheme _savedPageTheme;
    private Brush? _savedPageBackground;

    public McLauncherView(PluginExecutionContext host)
    {
        _host = host;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
                      ?? throw new InvalidOperationException("插件页面必须在界面线程创建。");
        McArt.Initialize(Path.GetDirectoryName(host.Services.GetAssetPath("assets/mc/hero.png")) ?? "",
            host.Services.GetAssetPath("assets/icon.svg"));
        _settings = LauncherSettings.Load(host.Services.DataDirectory);
        _accounts = new McAccountStore(host.Services.DataDirectory);
        _log = new McLaunchLog(host.Services.DataDirectory);
        _scan = new McVersionScanResult { GameDirectory = _settings.GameDirectory };
        _noticeTimer = _dispatcher.CreateTimer();
        _noticeTimer.Interval = TimeSpan.FromSeconds(5);
        _noticeTimer.IsRepeating = false;
        _noticeTimer.Tick += (_, _) => _notice.IsOpen = false;
        _notice.Closed += (_, _) => _notice.Visibility = Visibility.Collapsed;

        BuildShell();
        _root.Tag = this;
        _root.RequestedTheme = ElementTheme.Dark;
        _palette.Apply(true);
        _root.Loaded += OnLoaded;
        _root.Unloaded += OnUnloaded;
    }

    private void ApplyDarkPageChrome()
    {
        DependencyObject? current = _root;
        while (current is not null && current is not Page)
            current = VisualTreeHelper.GetParent(current);
        if (current is not Page page) return;

        _hostPage = page;
        _savedPageTheme = page.RequestedTheme;
        _savedPageBackground = page.Background;
        page.RequestedTheme = ElementTheme.Dark;
        page.Background = new SolidColorBrush(McPalette.Color(0xFF, 0x0E, 0x11, 0x16));
    }

    private void RestorePageChrome()
    {
        if (_hostPage is null) return;
        var page = _hostPage;
        _hostPage = null;
        page.RequestedTheme = _savedPageTheme;
        page.Background = _savedPageBackground;
    }

    public static FrameworkElement Create(PluginExecutionContext host) => new McLauncherView(host)._root;

    public PluginExecutionContext Host => _host;
    public McPalette Palette => _palette;
    public LauncherSettings Settings => _settings;
    public McAccountStore Accounts => _accounts;
    public McLaunchLog Log => _log;
    public IReadOnlyList<McVersion> Versions => _scan.Versions;
    public IReadOnlyList<string> VersionProblems => _scan.Problems;
    public IReadOnlyList<JavaRuntime> Javas => _javas;
    public McVersion? SelectedVersion { get; private set; }
    public DispatcherQueue Dispatcher => _dispatcher;
    public string DataDirectory => _host.Services.DataDirectory;
    public McLaunchPage LaunchPage =>
        _pages.TryGetValue("launch", out var page) && page.Tag is McLaunchPage launchPage
            ? launchPage
            : throw new InvalidOperationException("启动页尚未创建。");
    public bool IsLaunching => _launching;
    public event Action? StateChanged;
    public event Action? LaunchingChanged;

    public string AssetPath(string relative) => _host.Services.GetAssetPath(relative);

    private void BuildShell()
    {
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var background = new Grid { IsHitTestVisible = false };
        background.Children.Add(new Image
        {
            Source = McArt.Load("panorama_blur.png"),
            Stretch = Stretch.UniformToFill,
            Opacity = 0.86
        });
        var shade = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0.5, 0),
            EndPoint = new Windows.Foundation.Point(0.5, 1)
        };
        shade.GradientStops.Add(new GradientStop { Color = McPalette.Color(0x66, 0x05, 0x07, 0x0B), Offset = 0 });
        shade.GradientStops.Add(new GradientStop { Color = McPalette.Color(0x84, 0x07, 0x09, 0x0D), Offset = 0.5 });
        shade.GradientStops.Add(new GradientStop { Color = McPalette.Color(0xB8, 0x05, 0x06, 0x09), Offset = 1 });
        background.Children.Add(new Border { Background = shade });
        Grid.SetRowSpan(background, 3);
        _root.Children.Add(background);

        _root.Children.Add(BuildTitleBar());
        Grid.SetRow(_pageHost, 1);
        _root.Children.Add(_pageHost);
        Grid.SetRow(_notice, 2);
        _notice.Visibility = Visibility.Collapsed;
        _root.Children.Add(_notice);
    }

    private FrameworkElement BuildTitleBar()
    {
        var root = new Grid { Height = 56 };
        var shade = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1)
        };
        shade.GradientStops.Add(new GradientStop { Color = McPalette.Color(0xB8, 0x11, 0x13, 0x18), Offset = 0 });
        shade.GradientStops.Add(new GradientStop { Color = McPalette.Color(0x66, 0x12, 0x14, 0x19), Offset = 1 });
        root.Children.Add(new Border { Background = shade });
        root.Children.Add(new Border
        {
            BorderBrush = _palette.CardStroke,
            BorderThickness = new Thickness(0, 0, 0, 1),
            VerticalAlignment = VerticalAlignment.Bottom
        });

        var grid = new Grid { Padding = new Thickness(14, 0, 10, 0), ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        brand.Children.Add(McArt.BrandIcon(32));
        var brandText = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(new TextBlock
        {
            Text = "图吧我的世界启动器",
            FontSize = 15.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White)
        });
        brandText.Children.Add(new TextBlock
        {
            Text = "MINECRAFT LAUNCHER",
            FontSize = 8.5,
            CharacterSpacing = 120,
            Foreground = _palette.Secondary
        });
        brand.Children.Add(brandText);
        grid.Children.Add(brand);

        var nav = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        AddNav(nav, "launch", "\uE768", "启动");
        AddNav(nav, "versions", "\uE8F1", "版本");
        AddNav(nav, "download", "\uE896", "下载");
        AddNav(nav, "settings", "\uE713", "设置");
        Grid.SetColumn(nav, 1);
        grid.Children.Add(nav);

        _folderButton = McUi.Button("游戏目录", _palette, height: 38, glyph: "\uE838", fontSize: 12);
        _folderButton.VerticalAlignment = VerticalAlignment.Center;
        _folderButton.Click += (_, _) => OpenFolder(_settings.GameDirectory);
        ToolTipService.SetToolTip(_folderButton, _settings.GameDirectory);
        Grid.SetColumn(_folderButton, 3);
        grid.Children.Add(_folderButton);

        root.Children.Add(grid);
        return root;
    }

    private void AddNav(StackPanel host, string id, string glyph, string label)
    {
        var fill = new Border
        {
            Background = new SolidColorBrush(Colors.Transparent),
            CornerRadius = new CornerRadius(4)
        };
        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 15,
            Foreground = new SolidColorBrush(McPalette.Color(0xD0, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center
        };
        var text = new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(McPalette.Color(0xD0, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center
        };
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(13, 0, 13, 0)
        };
        content.Children.Add(icon);
        content.Children.Add(text);

        var layout = new Grid();
        layout.Children.Add(fill);
        layout.Children.Add(content);

        var button = new Button
        {
            Content = layout,
            Height = 40,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            UseSystemFocusVisuals = false
        };
        button.PointerEntered += (_, _) =>
        {
            if (_page != id) fill.Background = new SolidColorBrush(McPalette.Color(0x1F, 0xFF, 0xFF, 0xFF));
        };
        button.PointerExited += (_, _) =>
        {
            if (_page != id) fill.Background = new SolidColorBrush(Colors.Transparent);
        };
        button.Click += (_, _) => ShowPage(id);
        host.Children.Add(button);
        _nav.Add((id, button, fill, new Border { Visibility = Visibility.Collapsed }, text, icon));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyDarkPageChrome();
        _palette.Apply(true);
        if (_initialized) return;
        _initialized = true;
        ShowPage("launch");
        Log.Write("启动器初始化");
        try
        {
            await RefreshVersionsAsync(true);
            await EnsureJavaAsync(true);
            var last = _settings.LastVersion;
            var selected = _scan.Versions.FirstOrDefault(version =>
                               string.Equals(version.Id, last, StringComparison.OrdinalIgnoreCase))
                           ?? _scan.Versions.FirstOrDefault();
            if (selected is not null) SelectVersion(selected);
            LaunchPage.Refresh();
        }
        catch (Exception ex)
        {
            Log.Write("初始化失败：" + ex);
            Notify("启动器初始化失败：" + ex.Message, InfoBarSeverity.Error, "初始化失败");
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _noticeTimer.Stop();
        if (_launching && _pages.TryGetValue("launch", out var page) && page.Tag is McLaunchPage launchPage)
            launchPage.CancelLaunch();
        RestorePageChrome();
    }

    public void ShowPage(string id)
    {
        if (!_pages.ContainsKey(id))
        {
            FrameworkElement page;
            object pageObject;
            switch (id)
            {
                case "versions":
                {
                    var versions = new McVersionsPage(this);
                    pageObject = versions;
                    page = versions.Root;
                    break;
                }
                case "download":
                {
                    var download = new McDownloadPage(this);
                    pageObject = download;
                    page = download.Root;
                    break;
                }
                case "settings":
                {
                    var settings = new McSettingsPage(this);
                    pageObject = settings;
                    page = settings.Root;
                    break;
                }
                default:
                {
                    id = "launch";
                    var launch = new McLaunchPage(this);
                    pageObject = launch;
                    page = launch.Root;
                    break;
                }
            }

            var wrapper = new Grid { Tag = pageObject };
            wrapper.Children.Add(page);
            _pages[id] = wrapper;
            _pageHost.Children.Add(wrapper);
        }

        _page = id;
        foreach (var (pageId, wrapper) in _pages)
            wrapper.Visibility = pageId == id ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in _nav)
        {
            var selected = item.Id == id;
            item.Fill.Background = selected ? _palette.Accent : new SolidColorBrush(Colors.Transparent);
            item.Label.Foreground = new SolidColorBrush(selected
                ? Colors.White
                : McPalette.Color(0xD0, 0xFF, 0xFF, 0xFF));
            item.Icon.Foreground = item.Label.Foreground;
        }

        if (_pages.TryGetValue(id, out var hostPage) && hostPage.Tag is IMcPage pageImpl)
            pageImpl.OnShown();
    }

    public void SelectVersion(McVersion? version)
    {
        SelectedVersion = version;
        _settings.LastVersion = version?.Id;
        _settings.Save();
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        try { StateChanged?.Invoke(); }
        catch (Exception ex) { Log.Write("刷新界面失败：" + ex.Message); }
    }
    public async Task RefreshVersionsAsync(bool rescan)
    {
        var gameDirectory = _settings.GameDirectory;
        if (rescan || _scan.Versions.Count == 0)
        {
            var scanned = await Task.Run(() => McVersionStore.Scan(gameDirectory));
            _scan = scanned;
            Log.Write($"扫描版本：{scanned.Versions.Count} 个，问题 {scanned.Problems.Count} 个");
        }

        if (SelectedVersion is not null)
        {
            var refreshed = _scan.Versions.FirstOrDefault(version =>
                string.Equals(version.Id, SelectedVersion.Id, StringComparison.OrdinalIgnoreCase));
            SelectedVersion = refreshed;
        }

        RaiseStateChanged();
        if (_pages.TryGetValue("versions", out var versionsPage) && versionsPage.Tag is IMcPage page)
            page.OnShown();
    }

    public async Task EnsureJavaAsync(bool force)
    {
        if (!force && _javas.Count > 0) return;
        var java = await Task.Run(() => McJava.Detect(_settings.JavaPath, _settings.GameDirectory));
        _javas = java;
        Log.Write($"检测到 Java：{string.Join("；", java.Select(runtime => runtime.Display))}");
        RaiseStateChanged();
    }

    public void SaveSettings()
    {
        _settings.Save();
        _gameFolderLabel.Text = "游戏目录";
        if (_folderButton is not null)
            ToolTipService.SetToolTip(_folderButton, "打开游戏目录：" + _settings.GameDirectory);
        RaiseStateChanged();
    }

    public async Task ApplyGameDirectoryAsync(string directory)
    {
        var previous = _settings.GameDirectory;
        if (string.Equals(previous, directory, StringComparison.OrdinalIgnoreCase)) return;
        _settings.GameDirectory = directory;
        _settings.LastVersion = null;
        SelectedVersion = null;
        SaveSettings();
        await RefreshVersionsAsync(true);
        await EnsureJavaAsync(true);
        Notify("已切换游戏目录：" + directory, InfoBarSeverity.Informational, "设置已保存");
    }

    public void Notify(string message, InfoBarSeverity severity = InfoBarSeverity.Informational, string? title = null)
    {
        if (!_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(() => Notify(message, severity, title));
            return;
        }

        _notice.Title = title ?? "";
        _notice.Message = message;
        _notice.Severity = severity;
        _notice.Visibility = Visibility.Visible;
        _notice.IsOpen = true;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    public void AppendLog(string line)
    {
        _log.Write(line);
        if (!_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (_pages.TryGetValue("launch", out var page) && page.Tag is McLaunchPage launchPage)
                    launchPage.AppendLogLine(line);
            });
            return;
        }

        if (_pages.TryGetValue("launch", out var launchHost) && launchHost.Tag is McLaunchPage current)
            current.AppendLogLine(line);
    }

    public async Task<bool> ShowAccountDialogAsync()
    {
        if (_root.XamlRoot is null) return false;
        var dialog = new McAccountDialog(_host, _palette, _accounts, _root.XamlRoot);
        var changed = await dialog.ShowAsync();
        if (dialog.MicrosoftLoginRequested)
        {
            if (await ShowMicrosoftLoginAsync())
            {
                RaiseStateChanged();
                LaunchPage.Refresh();
                if (_pages.TryGetValue("settings", out var settingsHost) && settingsHost.Tag is McSettingsPage settingsPage)
                    settingsPage.RefreshAccounts();
                return true;
            }
        }

        if (changed)
        {
            RaiseStateChanged();
            LaunchPage.Refresh();
            if (_pages.TryGetValue("settings", out var page) && page.Tag is McSettingsPage settingsPage)
                settingsPage.RefreshAccounts();
        }

        return _accounts.Selected is not null;
    }

    public async Task<bool> ShowMicrosoftLoginAsync()
    {
        if (_root.XamlRoot is null) return false;
        var dialog = new McMicrosoftLoginDialog(_palette, _root.XamlRoot, _settings.MicrosoftClientId);
        var session = await dialog.ShowAsync();
        if (session is null) return false;
        _accounts.UpsertMicrosoft(session);
        AppendLog($"已登录微软账户：{session.Name}");
        RaiseStateChanged();
        LaunchPage.Refresh();
        if (_pages.TryGetValue("settings", out var page) && page.Tag is McSettingsPage settingsPage)
            settingsPage.RefreshAccounts();
        return true;
    }

    /// <summary>启动前刷新微软令牌；失败时提示重新登录。</summary>
    public async Task<McAccount?> EnsureAccountTokenAsync(McAccount account)
    {
        if (!account.IsMicrosoft) return account;
        if (!string.IsNullOrWhiteSpace(account.AccessToken) &&
            account.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
            return account;
        if (string.IsNullOrWhiteSpace(account.RefreshToken))
        {
            Notify("微软登录已过期，请重新登录微软账户。", InfoBarSeverity.Warning, "需要重新登录");
            return null;
        }

        try
        {
            var progress = new Progress<string>(message => AppendLog("[微软登录] " + message));
            var session = await McMicrosoftAuth.RefreshAsync(McHttp.Client, account.RefreshToken,
                account.OAuthClientId, progress, CancellationToken.None);
            _accounts.UpdateTokens(account, session);
            AppendLog("微软登录已自动续期。");
            RaiseStateChanged();
            LaunchPage.Refresh();
            return _accounts.Selected;
        }
        catch (Exception ex)
        {
            AppendLog("微软登录续期失败：" + ex.Message);
            Notify("微软登录已过期：" + ex.Message, InfoBarSeverity.Warning, "需要重新登录");
            return null;
        }
    }

    public void SetLaunching(bool launching)
    {
        if (_launching == launching) return;
        _launching = launching;
        LaunchingChanged?.Invoke();
    }

    public static void OpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + path + "\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // 打开资源管理器失败时静默忽略。
        }
    }
}
