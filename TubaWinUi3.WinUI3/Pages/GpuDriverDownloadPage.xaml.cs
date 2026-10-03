using System.Diagnostics;
using System.Management;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class GpuDriverDownloadPage : Page
{
    private string _hardwareId = "";
    private CancellationTokenSource? _searchCancellation;

    public GpuDriverDownloadPage()
    {
        InitializeComponent();
        VendorCombo.ItemsSource = GpuDriverCatalogService.GetVendors();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        DetectGraphicsAdapter();
    }

    private void DetectGraphicsAdapter()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPDeviceID FROM Win32_VideoController");
            foreach (ManagementObject adapter in searcher.Get())
            {
                var name = adapter["Name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var vendor = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ? "NVIDIA" :
                             name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "AMD" :
                             name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel" : null;
                if (vendor is null)
                    continue;

                ModelText.Text = name;
                _hardwareId = adapter["PNPDeviceID"]?.ToString() ?? "";
                VendorCombo.SelectedItem = GpuDriverCatalogService.GetVendors()
                    .FirstOrDefault(item => item.Name == vendor);
                return;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"无法自动识别显卡：{ex.Message}。可手动填写型号。";
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (VendorCombo.SelectedItem is not GpuDriverVendor vendor ||
            string.IsNullOrWhiteSpace(ModelText.Text))
        {
            StatusText.Text = "请选择厂商并填写显卡型号。";
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        ResultsList.ItemsSource = null;
        EmptyPanel.Visibility = Visibility.Visible;
        StatusText.Text = $"正在查询 {vendor.Name} 官方驱动目录…";

        try
        {
            var releases = await GpuDriverCatalogService.FindDriversAsync(
                vendor.Name, ModelText.Text.Trim(), _hardwareId, _searchCancellation.Token);
            ResultsList.ItemsSource = releases;
            EmptyPanel.Visibility = releases.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = releases.Count == 0
                ? "未解析到可下载的官方驱动版本。可检查型号/硬件 ID，或打开官方页面备用。"
                : $"找到 {releases.Count} 个可下载版本。";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            EmptyPanel.Visibility = Visibility.Visible;
            StatusText.Text = $"查询失败：{ex.Message}。可打开厂商官方页面备用。";
        }
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GpuDriverRelease release })
            return;

        var directory = GpuDriverCatalogService.GetDownloadDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            if (!GpuDriverCatalogService.TryGetDownloadDestination(
                    release.DownloadUri.AbsoluteUri, release.FileName, directory, out var path))
            {
                StatusText.Text = "下载链接不是受支持的厂商官方 HTTPS 直链，已拒绝。";
                return;
            }

            DownloadQueueService.Enqueue(
                $"{release.Vendor} {release.Version}",
                release.DownloadUri.AbsoluteUri,
                path,
                description: $"{release.Model} · 官方显卡驱动",
                glyph: "\uE950");
            StatusText.Text = $"已加入下载队列：{release.FileName}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"加入下载队列失败：{ex.Message}";
        }
    }

    private void OpenOfficialPage_Click(object sender, RoutedEventArgs e)
    {
        if (VendorCombo.SelectedItem is not GpuDriverVendor vendor)
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = vendor.CatalogUri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"无法打开厂商官网：{ex.Message}";
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
