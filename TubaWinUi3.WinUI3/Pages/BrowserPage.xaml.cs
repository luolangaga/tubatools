using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using TubaWinUi3.Services;
using TubaWinUi3.Services.AiAgent;
using Windows.Storage.Streams;
using VirtualKey = Windows.System.VirtualKey;

namespace TubaWinUi3.Pages;

public sealed class BrowserPageParam
{
    public required string Url { get; init; }
    public string? Title { get; init; }
    public bool AiHosted { get; init; }
}

public sealed partial class BrowserPage : Page
{
    private string _url;
    private bool _aiHosted;
    private bool _initializing;
    private bool _detached;
    private volatile bool _coreReady;
    private ulong _currentNavigationId;
    private volatile string _currentUrl = "about:blank";
    private volatile string _currentTitle = "";
    private DispatcherQueue? _dispatcher;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // These are read from the browser host pipe thread; never touch WebView2 here.
    internal bool IsReady => _ready.Task.IsCompletedSuccessfully && _coreReady;
    internal Task ReadyTask => _ready.Task;
    internal string CurrentUrl => _currentUrl;
    internal string CurrentTitle => _currentTitle;

    public BrowserPage()
    {
        InitializeComponent();

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;

        PageHeader.Title = "浏览器";
        _url = "about:blank";
        Loaded += BrowserPage_Loaded;
        ToolTipService.SetToolTip(AiStatusChip,
            L("Browser_AiChipTooltip", "内置 WebView2 · 专属用户数据，不包含日常浏览器登录状态"));
    }

    private void BrowserPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_aiHosted) return;
        try
        {
            if (BuiltinToolWindow.FindHostingWindow(this) is { } window) window.SetTitleBar(AiTitleBar);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[BrowserPage] SetTitleBar failed: " + ex.Message);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is BrowserPageParam param)
        {
            _url = param.Url;
            _aiHosted = param.AiHosted;
            if (_aiHosted)
            {
                PageHeader.Visibility = Visibility.Collapsed;
                AiTitleBar.Visibility = Visibility.Visible;
                AiTitleText.Text = L("Browser_AiBrowserTitle", "AI 浏览器");
                BrowserToolbar.Visibility = Visibility.Visible;
            }
            PageHeader.Title = _aiHosted ? L("Browser_AiBrowserTitle", "AI 浏览器") : param.Title ?? "浏览器";
            _ = InitWebViewAsync();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Detach();
    }

    private async Task InitWebViewAsync()
    {
        if (_initializing || _ready.Task.IsCompleted) return;
        _initializing = true;
        try
        {
            _dispatcher = DispatcherQueue.GetForCurrentThread();
            var environment = _aiHosted
                ? await AiBrowserWebView.GetEnvironmentAsync()
                : await WebView2EnvironmentService.GetAsync();
            await WebView.EnsureCoreWebView2Async(environment);

            var core = WebView.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.DocumentTitleChanged += OnDocumentTitleChanged;
            core.SourceChanged += OnSourceChanged;
            core.NewWindowRequested += OnNewWindowRequested;

            if (_aiHosted)
            {
                AiBrowserHostService.ActivityChanged += OnAiActivityChanged;
                AiBrowserControl.StopRequested += OnBrowserStopRequested;
                AiBrowserHostService.AttachPage(this);
            }

            _currentUrl = string.IsNullOrWhiteSpace(_url) ? "about:blank" : _url;
            _coreReady = true;
            _ready.TrySetResult();
            core.Navigate(_currentUrl);
            UpdateAddress();
            UpdateNavigationButtons();
            UpdateAiBar();
        }
        catch (Exception ex)
        {
            _coreReady = false;
            _ready.TrySetException(ex);
            System.Diagnostics.Debug.WriteLine("[BrowserPage] WebView2 init failed: " + ex);
            if (_aiHosted) AiBrowserHostService.PageHostClosed(this);
            if (XamlRoot is not null)
            {
                var dialog = new TubaWinUi3.Controls.FluentContentDialog
                {
                    Title = L("Browser_WebViewFailedTitle", "WebView2 初始化失败"),
                    Content = L("Browser_WebViewFailedMessage", "请确保已安装 WebView2 Runtime。") + "\n\n" + ex.Message,
                    CloseButtonText = L("Browser_Ok", "确定"),
                    XamlRoot = XamlRoot,
                    RequestedTheme = ThemeService.CurrentElementTheme
                };
                await dialog.ShowAsync();
            }
            if (!_aiHosted) App.MainWindow?.NavigateBack();
        }
        finally { _initializing = false; }
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        _currentNavigationId = args.NavigationId;
        _currentUrl = args.Uri;
        SetLoading(true);
        UpdateAddress();
        UpdateAiBar();
    }

    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.NavigationId != _currentNavigationId) return;
        SetLoading(false);
        _currentUrl = sender.Source?.ToString() ?? _currentUrl;
        _currentTitle = sender.DocumentTitle;
        if (!string.IsNullOrWhiteSpace(_currentTitle)) PageHeader.Title = _currentTitle;
        UpdateAddress();
        UpdateNavigationButtons();
        UpdateAiBar();
    }

    private void OnDocumentTitleChanged(CoreWebView2 sender, object args)
    {
        _currentTitle = sender.DocumentTitle;
        if (!string.IsNullOrWhiteSpace(_currentTitle)) PageHeader.Title = _currentTitle;
    }

    private void OnSourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
    {
        _currentUrl = sender.Source?.ToString() ?? _currentUrl;
        UpdateAddress();
        UpdateNavigationButtons();
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        if (!_aiHosted) return;
        args.Handled = true;
        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out _)) sender.Navigate(args.Uri);
    }

    internal async Task<string> CallCdpAsync(string method, string parameters, CancellationToken ct)
    {
        if (_dispatcher is null) throw new InvalidOperationException("WebView2 dispatcher is not available.");
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    // WebView2 is apartment-affine: never touch CoreWebView2 before this point.
                    if (WebView.CoreWebView2 is not { } core)
                    {
                        completion.TrySetException(new InvalidOperationException("WebView2 is not ready."));
                        return;
                    }
                    completion.TrySetResult(await core.CallDevToolsProtocolMethodAsync(method, parameters));
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            }))
            completion.TrySetException(new InvalidOperationException("WebView2 dispatcher is not available."));
        return await completion.Task.WaitAsync(ct);
    }

    internal async Task<byte[]> CaptureAsync(CancellationToken ct)
    {
        if (_dispatcher is null) throw new InvalidOperationException("WebView2 dispatcher is not available.");
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    if (WebView.CoreWebView2 is not { } core)
                    {
                        completion.TrySetException(new InvalidOperationException("WebView2 is not ready."));
                        return;
                    }
                    using var stream = new InMemoryRandomAccessStream();
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                    var length = checked((int)stream.Size);
                    var bytes = new byte[length];
                    using var reader = new DataReader(stream.GetInputStreamAt(0));
                    await reader.LoadAsync((uint)length);
                    reader.ReadBytes(bytes);
                    completion.TrySetResult(bytes);
                }
                catch (Exception ex) { completion.TrySetException(ex); }
            }))
            completion.TrySetException(new InvalidOperationException("WebView2 dispatcher is not available."));
        return await completion.Task.WaitAsync(ct);
    }

    private void OnAiActivityChanged(bool active)
    {
        if (!_aiHosted) return;
        DispatcherQueue.TryEnqueue(UpdateAiBar);
    }

    private void OnBrowserStopRequested()
    {
        if (!_aiHosted) return;
        DispatcherQueue.TryEnqueue(UpdateAiBar);
    }

    private void UpdateAiBar()
    {
        if (!_aiHosted)
        {
            AiStatusChip.Visibility = Visibility.Collapsed;
            return;
        }
        var stopped = AiBrowserControl.IsStopped;
        var active = AiBrowserHostService.IsActive;
        if (!stopped && !active)
        {
            AiStatusChip.Visibility = Visibility.Collapsed;
            return;
        }
        AiStatusChip.Visibility = Visibility.Visible;
        AiStatusText.Text = stopped
            ? L("Browser_AiStoppedShort", "AI 已停止")
            : L("Browser_AiActiveShort", "AI 控制中");
        AiControlActionButton.Content = stopped
            ? L("Browser_ResumeShort", "继续")
            : L("Browser_StopShort", "停止");
    }

    private void AiControlAction_Click(object sender, RoutedEventArgs e)
    {
        if (AiBrowserControl.IsStopped)
        {
            AiBrowserControl.Resume();
        }
        else
        {
            AiBrowserControl.Stop("用户停止 AI 浏览器控制");
            AiBrowserHostService.MarkInactive();
        }
        UpdateAiBar();
    }

    private void AddressBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        NavigateFromAddress();
    }

    private void AddressBox_GotFocus(object sender, RoutedEventArgs e) => AddressBox.SelectAll();

    private void AddressBox_LostFocus(object sender, RoutedEventArgs e) => UpdateAddress();

    private void NavigateFromAddress()
    {
        var text = AddressBox.Text.Trim();
        if (text.Length == 0 || WebView.CoreWebView2 is null) return;
        string url;
        try { url = BrowserCdp.NormalizeUrl(text); }
        catch (BrowserToolException) { url = "https://www.bing.com/search?q=" + Uri.EscapeDataString(text); }
        WebView.CoreWebView2.Navigate(url);
    }

    private void UpdateAddress()
    {
        if (AddressBox.FocusState == FocusState.Unfocused && !string.IsNullOrWhiteSpace(_currentUrl))
            AddressBox.Text = _currentUrl;
    }

    private void UpdateNavigationButtons()
    {
        var core = WebView.CoreWebView2;
        ToolbarBackButton.IsEnabled = core?.CanGoBack == true;
        ToolbarForwardButton.IsEnabled = core?.CanGoForward == true;
    }

    private void SetLoading(bool active)
    {
        LoadingRing.IsActive = active;
        ToolbarLoadingRing.IsActive = active;
    }

    private void Detach()
    {
        if (!_aiHosted || _detached) return;
        _detached = true;
        _coreReady = false;
        AiBrowserHostService.ActivityChanged -= OnAiActivityChanged;
        AiBrowserControl.StopRequested -= OnBrowserStopRequested;
        AiBrowserHostService.PageHostClosed(this);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e) => Detach();

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2?.CanGoBack == true)
            WebView.CoreWebView2.GoBack();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2?.CanGoForward == true)
            WebView.CoreWebView2.GoForward();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2 is not null)
            WebView.CoreWebView2.Reload();
    }

    private void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        var url = WebView.CoreWebView2?.Source?.ToString() ?? _currentUrl;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private static string L(string key, string fallback) => LocalizationService.L(key, fallback);

    public static void Open(string url, string? title = null)
    {
        App.MainWindow?.NavigateToToolPage(typeof(BrowserPage), new BrowserPageParam
        {
            Url = url,
            Title = title
        });
    }
}
