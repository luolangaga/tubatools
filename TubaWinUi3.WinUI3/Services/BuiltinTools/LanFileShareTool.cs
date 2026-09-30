using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Controls;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class LanFileShareTool : IBuiltinTool
{
    public string Id => "lan-file-share";
    public string Name => LocalizationService.L("Builtin_lan-file-share_Name", "局域网文件分享");
    public string Description => LocalizationService.L("Builtin_lan-file-share_Desc", "在局域网内创建HTTP文件分享服务，其他设备可通过浏览器访问和下载文件，支持拖拽上传。");
    public string Glyph => "\uE8F1";
    public string Category => "网络工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(LanFileSharePage));
        return Task.CompletedTask;
    }
}

public sealed partial class LanFileSharePage : Page
{
    private WebView2? _webView;
    private StackPanel _controlBar = null!;
    private TextBlock _statusText = null!;
    private TextBlock _urlText = null!;
    private Button _startBtn = null!;
    private Button _stopBtn = null!;
    private Button _openBrowserBtn = null!;
    private Button _copyUrlBtn = null!;
    private Button _openDirBtn = null!;
    private NumberBox _portBox = null!;
    private TextBlock _fileCountText = null!;
    private Grid _webViewHost = null!;
    private StackPanel _loadingPanel = null!;
    private ProgressRing _loadingRing = null!;
    private TextBlock _loadingText = null!;
    private StackPanel _errorPanel = null!;
    private TextBlock _errorTitle = null!;
    private TextBlock _errorMessage = null!;
    private Button _retryBtn = null!;
    private bool _closed;

    public LanFileSharePage()
    {
        InitializeComponent();
        Content = BuildContent();

        Unloaded += OnPageUnloaded;

        LanFileShareService.StateChanged += OnStateChanged;
        UpdateUI();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e) => OnClosed(sender, e);

    private Grid BuildContent()
    {
        var header = new ToolPageHeader
        {
            Title = "局域网文件分享",
            Subtitle = "在局域网内创建HTTP文件分享服务，其他设备可通过浏览器访问和下载文件",
            Glyph = "\uE8F1"
        };

        _statusText = new TextBlock { FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };

        _urlText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.AccentBlue),
            VerticalAlignment = VerticalAlignment.Center,
            IsTextSelectionEnabled = true
        };

        _portBox = new NumberBox
        {
            Minimum = 1024, Maximum = 65535, Value = LanFileShareService.Port,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            MinWidth = 100
        };
        _portBox.ValueChanged += (_, _) => LanFileShareService.SetPort((int)_portBox.Value);

        _startBtn = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "\uE768", FontSize = 12 }, new TextBlock { Text = "启动" } } },
            Style = App.Current.Resources["AccentButtonStyle"] as Style
        };
        _startBtn.Click += async (_, _) => await StartServerAsync();

        _stopBtn = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "\uE71A", FontSize = 12 }, new TextBlock { Text = "停止" } } },
            Visibility = Visibility.Collapsed
        };
        _stopBtn.Click += (_, _) => StopServer();

        _openBrowserBtn = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "\uE774", FontSize = 12 }, new TextBlock { Text = "在浏览器打开" } } },
            Visibility = Visibility.Collapsed
        };
        _openBrowserBtn.Click += (_, _) => OpenInBrowser();

        _copyUrlBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE8C8", FontSize = 12 },
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(6, 4, 6, 4)
        };
        ToolTipService.SetToolTip(_copyUrlBtn, "复制地址");
        _copyUrlBtn.Click += (_, _) => CopyUrl();

        _openDirBtn = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "\uED25", FontSize = 12 }, new TextBlock { Text = "打开目录" } } },
            Visibility = Visibility.Collapsed
        };
        _openDirBtn.Click += (_, _) => OpenShareDir();

        _fileCountText = new TextBlock { FontSize = 12, Opacity = 0.68, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };

        var leftStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _statusText, _portBox, _urlText, _copyUrlBtn, _fileCountText } };
        var rightStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _openDirBtn, _openBrowserBtn, _startBtn, _stopBtn } };

        var controlRow = new Grid { Padding = new Thickness(24, 0, 24, 8) };
        controlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controlRow.Children.Add(leftStack); Grid.SetColumn(leftStack, 0);
        controlRow.Children.Add(rightStack); Grid.SetColumn(rightStack, 1);

        _controlBar = new StackPanel { Spacing = 4, Children = { header, controlRow } };

        _loadingRing = new ProgressRing { Width = 40, Height = 40, IsActive = true };
        _loadingText = new TextBlock { Text = "正在启动服务...", FontSize = 13, Opacity = 0.68 };
        _loadingPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 8, Visibility = Visibility.Collapsed, Children = { _loadingRing, _loadingText } };

        _errorTitle = new TextBlock { Text = "启动失败", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
        _errorMessage = new TextBlock { FontSize = 13, Opacity = 0.78, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 };
        _retryBtn = new Button { Content = "重试", Style = App.Current.Resources["AccentButtonStyle"] as Style };
        _retryBtn.Click += async (_, _) => await StartServerAsync();
        _errorPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 12, Visibility = Visibility.Collapsed, Children = { new FontIcon { Glyph = "\uE783", FontSize = 48, Foreground = new SolidColorBrush(ThemeColors.AccentRed) }, _errorTitle, _errorMessage, _retryBtn } };

        _webViewHost = new Grid();

        var root = new Grid { RowSpacing = 0 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        root.Children.Add(_controlBar); Grid.SetRow(_controlBar, 0);
        root.Children.Add(_loadingPanel); Grid.SetRow(_loadingPanel, 1);
        root.Children.Add(_errorPanel); Grid.SetRow(_errorPanel, 1);
        root.Children.Add(_webViewHost); Grid.SetRow(_webViewHost, 1);

        return root;
    }

    private async Task StartServerAsync()
    {
        _loadingPanel.Visibility = Visibility.Visible;
        _errorPanel.Visibility = Visibility.Collapsed;
        _webViewHost.Visibility = Visibility.Collapsed;

        try
        {
            await LanFileShareService.StartAsync();
            _loadingPanel.Visibility = Visibility.Collapsed;
            _webViewHost.Visibility = Visibility.Visible;
            await InitWebViewAsync();
        }
        catch (Exception ex)
        {
            _loadingPanel.Visibility = Visibility.Collapsed;
            _errorPanel.Visibility = Visibility.Visible;
            _errorMessage.Text = ex.Message;
        }

        UpdateUI();
    }

    private void StopServer()
    {
        LanFileShareService.Stop();
        _webViewHost.Visibility = Visibility.Collapsed;
        if (_webView is not null)
        {
            try { _webView.CoreWebView2?.Stop(); _webView.Close(); } catch { }
            _webViewHost.Children.Remove(_webView);
            _webView = null;
        }
        UpdateUI();
    }

    private async Task InitWebViewAsync()
    {
        if (_webView is not null) return;

        _webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _webViewHost.Children.Clear();
        _webViewHost.Children.Add(_webView);

        try
        {
            await _webView.EnsureCoreWebView2Async(await WebView2EnvironmentService.GetAsync());
            var url = $"http://127.0.0.1:{LanFileShareService.Port}/";
            _webView.CoreWebView2.Navigate(url);
        }
        catch (Exception ex)
        {
            _errorTitle.Text = "WebView2 初始化失败";
            _errorMessage.Text = $"请确保已安装 WebView2 Runtime。\n\n{ex.Message}";
            _webViewHost.Visibility = Visibility.Collapsed;
            _errorPanel.Visibility = Visibility.Visible;
        }
    }

    private void OpenInBrowser()
    {
        if (!LanFileShareService.IsRunning) return;
        var url = $"http://{LanFileShareService.GetLocalIp()}:{LanFileShareService.Port}/";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    private void CopyUrl()
    {
        if (!LanFileShareService.IsRunning) return;
        var url = $"http://{LanFileShareService.GetLocalIp()}:{LanFileShareService.Port}/";
        // 统一走 ClipboardService（占用重试 + 失败留痕），不再直连 WinRT 剪贴板
        ClipboardService.TrySetText(url);
    }

    private void OpenShareDir()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = LanFileShareService.ShareDir, UseShellExecute = true });
        }
        catch { }
    }

    private void OnStateChanged()
    {
        if (_closed) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed) return;
            UpdateUI();
        });
    }

    private void UpdateUI()
    {
        var running = LanFileShareService.IsRunning;

        _statusText.Text = running ? "● 运行中" : "○ 已停止";
        _statusText.Foreground = new SolidColorBrush(running ? ThemeColors.AccentGreen : ThemeColors.DimText);

        if (running)
        {
            var ip = LanFileShareService.GetLocalIp();
            var port = LanFileShareService.Port;
            _urlText.Text = $"http://{ip}:{port}/";
            _fileCountText.Text = $"{LanFileShareService.SharedFiles.Count} 个文件";
        }
        else
        {
            _urlText.Text = "";
            _fileCountText.Text = "";
        }

        _startBtn.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        _stopBtn.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        _openBrowserBtn.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        _copyUrlBtn.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        _openDirBtn.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        _fileCountText.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        _portBox.IsEnabled = !running;
    }

    private void OnClosed(object sender, RoutedEventArgs e)
    {
        _closed = true;
        LanFileShareService.StateChanged -= OnStateChanged;
        LanFileShareService.Stop();
    }
}
