using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class UpdateDialog : ContentDialog
{
    private UpdateInfo? _updateInfo;
    private UpdateAsset? _selectedAsset;
    private string? _selectedProxyUrl;
    private CancellationTokenSource? _cts;
    private bool _isDownloading;

    public bool SkipThisVersion { get; private set; }

    public UpdateDialog()
    {
        InitializeComponent();
        XamlRoot = App.MainWindow?.Content?.XamlRoot;
    }

    public async Task ShowUpdateAsync(UpdateInfo updateInfo)
    {
        _updateInfo = updateInfo;

        NewVersionText.Text = updateInfo.Version;
        PublishDateText.Text = updateInfo.PublishedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

        var body = updateInfo.Body ?? "暂无更新说明";
        MarkdownTextService.RenderToRichTextBlock(ChangelogText, body);

        _selectedAsset = UpdateService.FindBestAsset(updateInfo.Assets);

        if (_selectedAsset is null)
        {
            ErrorInfoBar.Message = $"未找到适用于 {UpdateService.CurrentArchitecture} 架构的更新文件";
            ErrorInfoBar.IsOpen = true;
            IsPrimaryButtonEnabled = false;
        }

        await ShowAsync();
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_isDownloading) return;

        var deferral = args.GetDeferral();
        args.Cancel = true;

        try
        {
            await StartUpdateProcess();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        _cts?.Cancel();
    }

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        _cts?.Cancel();
        SkipThisVersion = true;
    }

    private async Task StartUpdateProcess()
    {
        if (_updateInfo is null || _selectedAsset is null) return;

        _cts = new CancellationTokenSource();
        _isDownloading = true;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;

        try
        {
            ProxyTestRing.Visibility = Visibility.Visible;
            ProxyStatusText.Text = "正在测试代理连接速度...";

            var proxyProgress = new Progress<ProxySpeedResult>(result =>
            {
                DispatcherQueue.TryEnqueue(() => UpdateProxyList(result));
            });

            var proxyResults = await UpdateService.TestProxySpeedsAsync(
                _selectedAsset.BrowserDownloadUrl, proxyProgress, _cts.Token);

            ProxyTestRing.Visibility = Visibility.Collapsed;

            if (proxyResults.Count > 0)
            {
                _selectedProxyUrl = proxyResults[0].BaseUrl;
                ProxyStatusText.Text = proxyResults[0].BaseUrl.StartsWith("https://hub.tubawinui3", StringComparison.OrdinalIgnoreCase)
                    ? $"已选择 Hub 镜像 ({proxyResults[0].LatencyMs:F0}ms)"
                    : $"已选择最快代理: {proxyResults[0].Name} ({proxyResults[0].LatencyMs:F0}ms)";
            }
            else
            {
                _selectedProxyUrl = null;
                ProxyStatusText.Text = "Hub 镜像和代理均不可用，将尝试直连下载";
            }

            DownloadSection.Visibility = Visibility.Visible;

            var downloadProgress = new Progress<DownloadProgress>(p =>
            {
                DispatcherQueue.TryEnqueue(() => UpdateDownloadProgress(p));
            });

            var filePath = await UpdateService.DownloadUpdateAsync(
                _selectedAsset, _selectedProxyUrl, downloadProgress, _cts.Token);

            Hide();
            await ShowDownloadCompleteDialog(filePath);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorInfoBar.Message = ex.Message;
            ErrorInfoBar.IsOpen = true;
            IsPrimaryButtonEnabled = true;
            IsSecondaryButtonEnabled = true;
        }
        finally
        {
            _isDownloading = false;
        }
    }

    private void UpdateProxyList(ProxySpeedResult result)
    {
        var children = ProxyListPanel.Children;
        var existing = children.OfType<Border>().FirstOrDefault(b =>
            b.Tag is string tag && tag == result.Name);

        var statusIcon = result.IsAvailable ? "\uE73E" : "\uE711";
        var statusColor = result.IsAvailable ? "Green" : "Red";
        var latencyText = result.IsAvailable ? $"{result.LatencyMs:F0}ms" : "超时";

        var content = new Grid
        {
            ColumnSpacing = 12,
            Tag = result.Name
        };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        var icon = new FontIcon
        {
            Glyph = statusIcon,
            FontSize = 14
        };
        if (result.IsAvailable)
            icon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Green);
        else
            icon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Red);

        Grid.SetColumn(icon, 0);
        content.Children.Add(icon);

        var nameBlock = new TextBlock
        {
            Text = result.Name,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameBlock, 1);
        content.Children.Add(nameBlock);

        var latencyBlock = new TextBlock
        {
            Text = latencyText,
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(latencyBlock, 2);
        content.Children.Add(latencyBlock);

        if (result.IsAvailable)
        {
            var speedBlock = new TextBlock
            {
                Text = UpdateService.FormatSpeed(result.SpeedMbps),
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(speedBlock, 3);
            content.Children.Add(speedBlock);
        }

        var border = new Border
        {
            Padding = new Thickness(8, 6, 8, 6),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(6),
            Child = content,
            Tag = result.Name
        };

        if (existing is not null)
        {
            var idx = children.IndexOf(existing);
            children.RemoveAt(idx);
            children.Insert(idx, border);
        }
        else
        {
            children.Add(border);
        }
    }

    private void UpdateDownloadProgress(DownloadProgress p)
    {
        DownloadProgressBar.Value = p.Percentage;
        DownloadPercentText.Text = $"{p.Percentage:F1}%";
        DownloadSpeedText.Text = UpdateService.FormatSpeed(p.SpeedMbps);
        DownloadSizeText.Text = $"{UpdateService.FormatSize(p.BytesReceived)} / {UpdateService.FormatSize(p.TotalBytes)}";
        DownloadTimeText.Text = UpdateService.FormatTime(p.EstimatedRemaining);
    }

    private async Task ShowDownloadCompleteDialog(string filePath)
    {
        var isExe = filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

        var dialog = new ContentDialog
        {
            Title = "下载完成",
            XamlRoot = XamlRoot,
            PrimaryButtonText = isExe ? "立即安装" : "打开文件夹",
            SecondaryButtonText = "稍后手动安装"
        };

        var stack = new StackPanel { Spacing = 12 };

        var successBorder = new Border
        {
            Padding = new Thickness(20, 16, 20, 16),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10)
        };

        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconBorder = new Border
        {
            Width = 48,
            Height = 48,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Green),
            CornerRadius = new CornerRadius(12)
        };
        var checkIcon = new FontIcon
        {
            Glyph = "\uE73E",
            FontSize = 24,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.White)
        };
        iconBorder.Child = checkIcon;
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        var infoStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4
        };
        infoStack.Children.Add(new TextBlock
        {
            Text = "更新已下载完成",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = $"文件: {Path.GetFileName(filePath)}",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = $"架构: {UpdateService.CurrentArchitecture}",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = isExe ? "点击「立即安装」将关闭本程序并启动安装程序" : "请关闭本程序后解压/安装更新",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
        });
        Grid.SetColumn(infoStack, 1);
        grid.Children.Add(infoStack);

        successBorder.Child = grid;
        stack.Children.Add(successBorder);
        dialog.Content = stack;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                if (isExe)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                    Application.Current.Exit();
                }
                else
                {
                    var folder = Path.GetDirectoryName(filePath)!;
                    System.Diagnostics.Process.Start("explorer.exe", folder);
                }
            }
            catch
            {
            }
        }
    }
}
