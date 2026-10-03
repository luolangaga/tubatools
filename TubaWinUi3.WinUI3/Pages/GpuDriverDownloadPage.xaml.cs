using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class GpuDriverDownloadPage : Page
{
    private bool _webViewInitialized;

    public GpuDriverDownloadPage()
    {
        InitializeComponent();
        VendorCombo.ItemsSource = GpuDriverCatalogService.GetVendors();
        VendorCombo.DisplayMemberPath = nameof(GpuDriverVendor.Name);
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_webViewInitialized)
            return;

        _webViewInitialized = true;
        try
        {
            await DriverWebView.EnsureCoreWebView2Async(await WebView2EnvironmentService.GetAsync());
            DriverWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            DriverWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            DriverWebView.CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting;
            NavigateToSelectedVendor();
        }
        catch (Exception ex)
        {
            _webViewInitialized = false;
            StatusText.Text = $"浏览器初始化失败：{ex.Message}";
        }
    }

    private void VendorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        NavigateToSelectedVendor();
    }

    private void NavigateToSelectedVendor()
    {
        if (DriverWebView.CoreWebView2 is null || VendorCombo.SelectedItem is not GpuDriverVendor vendor)
            return;

        DriverWebView.CoreWebView2.Navigate(vendor.CatalogUri.AbsoluteUri);
        StatusText.Text = $"正在打开 {vendor.Name} 官方驱动目录…";
    }

    private void CoreWebView2_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        LoadingRing.IsActive = true;
    }

    private void CoreWebView2_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        LoadingRing.IsActive = false;
        if (!args.IsSuccess)
            StatusText.Text = "官方页面加载失败，请检查网络连接，或在浏览器中打开。";
    }

    private void CoreWebView2_DownloadStarting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args)
    {
        var download = args.DownloadOperation;
        var directory = GpuDriverCatalogService.GetDownloadDirectory();
        if (!GpuDriverCatalogService.TryGetDownloadDestination(
                download.Uri, args.ResultFilePath, directory, out var destination))
        {
            args.Cancel = true;
            StatusText.Text = "已阻止非官方来源或非 .exe / .zip 文件的下载。";
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            args.ResultFilePath = destination;
            args.Handled = true;
            StatusText.Text = $"正在下载：{Path.GetFileName(destination)}";
            download.StateChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                if (download.State == CoreWebView2DownloadState.Completed)
                    StatusText.Text = $"下载完成：{destination}";
                else if (download.State == CoreWebView2DownloadState.Interrupted)
                    StatusText.Text = $"下载未完成：{Path.GetFileName(destination)}";
            });
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            StatusText.Text = $"无法创建下载目录：{ex.Message}";
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (DriverWebView.CoreWebView2?.CanGoBack == true)
            DriverWebView.CoreWebView2.GoBack();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        DriverWebView.CoreWebView2?.Reload();
    }

    private void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        var url = DriverWebView.CoreWebView2?.Source?.ToString() ??
                  (VendorCombo.SelectedItem as GpuDriverVendor)?.CatalogUri.AbsoluteUri;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"无法打开浏览器：{ex.Message}";
        }
    }

    private void OpenDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        var directory = GpuDriverCatalogService.GetDownloadDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"无法打开下载目录：{ex.Message}";
        }
    }
}
