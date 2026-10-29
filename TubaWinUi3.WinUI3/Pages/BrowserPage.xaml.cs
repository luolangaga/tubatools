using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed class BrowserPageParam
{
    public required string Url { get; init; }
    public string? Title { get; init; }
}

public sealed partial class BrowserPage : Page
{
    private string _url;

    public BrowserPage()
    {
        InitializeComponent();

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;

        PageHeader.Title = "浏览器";
        _url = "about:blank";
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is BrowserPageParam param)
        {
            _url = param.Url;
            PageHeader.Title = param.Title ?? "浏览器";
            _ = InitWebViewAsync();
        }
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            await WebView.EnsureCoreWebView2Async(await WebView2EnvironmentService.GetAsync());

            WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            WebView.CoreWebView2.DocumentTitleChanged += OnDocumentTitleChanged;

            WebView.CoreWebView2.Navigate(_url);
        }
        catch (Exception ex)
        {
            var dialog = new TubaWinUi3.Controls.FluentContentDialog
            {
                Title = "WebView2 初始化失败",
                Content = $"请确保已安装 WebView2 Runtime。\n\n{ex.Message}",
                CloseButtonText = "确定",
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            await dialog.ShowAsync();
            App.MainWindow?.NavigateBack();
        }
    }

    private ulong _currentNavigationId;

    private void OnNavigationStarting(
        Microsoft.Web.WebView2.Core.CoreWebView2 sender,
        Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs args)
    {
        _currentNavigationId = args.NavigationId;
        LoadingRing.IsActive = true;
    }

    private void OnNavigationCompleted(
        Microsoft.Web.WebView2.Core.CoreWebView2 sender,
        Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
    {
        // 忽略已被新导航顶掉的旧导航：重定向/JS 跳转会使上一次导航以
        // IsSuccess=false、WebErrorStatus=Unknown 结束，但页面实际已由新导航加载成功
        if (args.NavigationId != _currentNavigationId)
            return;

        LoadingRing.IsActive = false;

        // 加载失败时由 WebView2 原生错误页展示，不再使用自定义错误面板
    }

    private void OnDocumentTitleChanged(
        Microsoft.Web.WebView2.Core.CoreWebView2 sender,
        object args)
    {
        var docTitle = sender.DocumentTitle;
        if (!string.IsNullOrEmpty(docTitle))
        {
            PageHeader.Title = docTitle;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2?.CanGoBack == true)
            WebView.CoreWebView2.GoBack();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (WebView.CoreWebView2 is not null)
            WebView.CoreWebView2.Reload();
    }

    private void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        var url = WebView.CoreWebView2?.Source?.ToString() ?? _url;
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

    public static void Open(string url, string? title = null)
    {
        App.MainWindow?.NavigateToToolPage(typeof(BrowserPage), new BrowserPageParam
        {
            Url = url,
            Title = title
        });
    }
}
