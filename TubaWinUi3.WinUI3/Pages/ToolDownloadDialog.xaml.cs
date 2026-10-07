using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class ToolDownloadDialog : ContentDialog
{
    private readonly string _toolName;
    private readonly string _downloadUrl;
    private readonly string? _filter;
    private readonly string _destinationDir;
    private CancellationTokenSource? _cts;
    private bool _isDownloading;

    public bool DownloadSucceeded { get; private set; }
    public string? DownloadedFilePath { get; private set; }

    public ToolDownloadDialog(string toolName, string toolDesc, string downloadUrl, string? filter, string destinationDir)
    {
        InitializeComponent();
        XamlRoot = App.MainWindow?.Content?.XamlRoot;

        _toolName = toolName;
        _downloadUrl = downloadUrl;
        _filter = filter;
        _destinationDir = destinationDir;

        Title = $"下载 {toolName}";
        ToolNameText.Text = toolName;
        ToolDescText.Text = toolDesc;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_isDownloading)
        {
            args.Cancel = true;
            return;
        }

        var deferral = args.GetDeferral();
        args.Cancel = true;

        try
        {
            await StartDownloadAsync();
        }
        finally
        {
            try { deferral.Complete(); } catch { }
        }
    }

    private async Task StartDownloadAsync()
    {
        _cts = new CancellationTokenSource();
        _isDownloading = true;
        IsPrimaryButtonEnabled = false;

        try
        {
            ResolvingSection.Visibility = Visibility.Visible;
            ProgressSection.Visibility = Visibility.Collapsed;

            var info = await ToolDownloaderService.ResolveDownloadUrlAsync(
                _downloadUrl, _filter, _cts.Token);

            ResolvingSection.Visibility = Visibility.Collapsed;

            if (info is null)
            {
                ErrorBar.Message = "无法获取下载地址，请检查网络连接。";
                ErrorBar.IsOpen = true;
                IsPrimaryButtonEnabled = true;
                _isDownloading = false;
                return;
            }

            ProgressSection.Visibility = Visibility.Visible;
            PrimaryButtonText = "下载中...";

            var progress = new Progress<ToolDownloadProgress>(p =>
            {
                DispatcherQueue.TryEnqueue(() => UpdateProgress(p));
            });

            var filePath = await ToolDownloaderService.DownloadToFileAsync(
                info.DownloadUrl, _destinationDir, info.FileName, progress, _cts.Token);

            DownloadedFilePath = filePath;

            if (info.IsArchive)
            {
                PercentText.Text = "解压中...";
                DownloadProgressBar.IsIndeterminate = true;
                await ToolDownloaderService.ExtractArchiveAsync(filePath, _destinationDir, _cts.Token);
            }

            DownloadSucceeded = true;
            Hide();

            if (info.IsInstaller)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
                catch { }
            }

            await ShowSuccessDialog(info);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
            IsPrimaryButtonEnabled = true;
            PrimaryButtonText = "重试";
        }
        finally
        {
            _isDownloading = false;
        }
    }

    private void UpdateProgress(ToolDownloadProgress p)
    {
        DownloadProgressBar.Value = p.Percentage;
        PercentText.Text = $"{p.Percentage:F1}%";
        SpeedText.Text = ToolDownloaderService.FormatSpeed(p.SpeedMbps);
        SizeText.Text = $"{ToolDownloaderService.FormatSize(p.BytesReceived)} / {ToolDownloaderService.FormatSize(p.TotalBytes)}";
        TimeText.Text = ToolDownloaderService.FormatTime(p.EstimatedRemaining);
    }

    private async Task ShowSuccessDialog(ToolDownloadInfo info)
    {
        var dialog = new ContentDialog
        {
            Title = "下载完成",
            XamlRoot = XamlRoot,
            PrimaryButtonText = info.IsInstaller ? "已启动安装" : "完成",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var stack = new StackPanel { Spacing = 12 };

        var border = new Border
        {
            Padding = new Thickness(16),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconBorder = new Border
        {
            Width = 48,
            Height = 48,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(ThemeColors.AccentGreen),
            CornerRadius = new CornerRadius(8)
        };
        iconBorder.Child = new FontIcon
        {
            Glyph = "\uE73E",
            FontSize = 24,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]
        };
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        infoStack.Children.Add(new TextBlock
        {
            Text = $"{_toolName} 下载完成！",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = info.IsInstaller ? "安装程序已启动，请按提示完成安装。" :
                   info.IsArchive ? "已解压到工具目录，刷新后可直接打开。" :
                   "文件已保存到工具目录。",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = $"文件：{info.FileName}",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        Grid.SetColumn(infoStack, 1);
        grid.Children.Add(infoStack);

        border.Child = grid;
        stack.Children.Add(border);
        dialog.Content = stack;

        await dialog.ShowAsync();
    }
}
