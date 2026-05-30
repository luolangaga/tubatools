using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.UI;
using TubaWinUi3.Services;

namespace TubaWinUi3.Services;

public sealed class SpeedTestTool : IBuiltinTool
{
    public string Id => "speed-test";
    public string Name => "网速测试";
    public string Description => "测试网络下载和上传速度，使用 Cloudflare 测速节点。";
    public string Glyph => "\uEB3E";
    public string Category => "网络工具";
    public BuiltinToolKind Kind => BuiltinToolKind.ProgressTask;

    private static readonly Color AccentBlue = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);

    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var dialog = new ContentDialog
        {
            Title = "网速测试",
            CloseButtonText = "关闭",
            XamlRoot = context.XamlRoot
        };
        dialog.Resources["ContentDialogMaxWidth"] = 860;
        dialog.Closing += (_, args) =>
        {
            if (_isRunning) args.Cancel = true;
            else _cts?.Cancel();
        };

        var content = BuildDialogContent();
        dialog.Content = content;

        await dialog.ShowAsync();
    }

    private ScrollViewer BuildDialogContent()
    {
        var downloadSpeedText = new TextBlock { FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(AccentBlue) };
        var uploadSpeedText = new TextBlock { FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(AccentGreen) };
        var downloadUnit = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText), Text = "Mbps" };
        var uploadUnit = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText), Text = "Mbps" };

        var downloadCard = MakeSpeedCard("下载速度", downloadSpeedText, downloadUnit, "\uE896", AccentBlue);
        var uploadCard = MakeSpeedCard("上传速度", uploadSpeedText, uploadUnit, "\uE898", AccentGreen);

        var cardsGrid = new Grid { ColumnSpacing = 10 };
        cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cardsGrid.Children.Add(downloadCard); Grid.SetColumn(downloadCard, 0);
        cardsGrid.Children.Add(uploadCard); Grid.SetColumn(uploadCard, 1);

        var downloadProgress = new ProgressBar { Minimum = 0, Maximum = 1000, HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = new SolidColorBrush(AccentBlue) };
        var uploadProgress = new ProgressBar { Minimum = 0, Maximum = 1000, HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = new SolidColorBrush(AccentGreen) };
        downloadProgress.Visibility = Visibility.Collapsed;
        uploadProgress.Visibility = Visibility.Collapsed;

        var downloadStatusLabel = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText), Text = "" };
        var uploadStatusLabel = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText), Text = "" };

        var downloadProgressPanel = new StackPanel { Spacing = 4 };
        downloadProgressPanel.Children.Add(downloadProgress);
        downloadProgressPanel.Children.Add(downloadStatusLabel);
        downloadProgressPanel.Visibility = Visibility.Collapsed;

        var uploadProgressPanel = new StackPanel { Spacing = 4 };
        uploadProgressPanel.Children.Add(uploadProgress);
        uploadProgressPanel.Children.Add(uploadStatusLabel);
        uploadProgressPanel.Visibility = Visibility.Collapsed;

        var startBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 12 },
                    new TextBlock { Text = "开始测试" }
                }
            },
            Style = Application.Current.Resources["AccentButtonStyle"] as Style
        };

        var cancelBtn = new Button
        {
            Content = "取消",
            Visibility = Visibility.Collapsed
        };

        var actionBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actionBar.Children.Add(startBtn);
        actionBar.Children.Add(cancelBtn);

        var serverInfo = new Border
        {
            Padding = new Thickness(12),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new StackPanel { Spacing = 4 }
        };
        var serverStack = (StackPanel)serverInfo.Child;
        serverStack.Children.Add(new TextBlock
        {
            Text = "测速节点：Cloudflare Speed Test (speed.cloudflare.com)",
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        });
        serverStack.Children.Add(new TextBlock
        {
            Text = "测试时长约 60 秒（下载 30 秒 + 上传 30 秒）",
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });

        var resultText = new TextBlock
        {
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(AccentGreen),
            Visibility = Visibility.Collapsed
        };

        var rootStack = new StackPanel { Spacing = 14, MaxWidth = 800 };
        rootStack.Children.Add(new TextBlock
        {
            Text = "测试网络下载和上传带宽，使用 Cloudflare 全球 CDN 节点",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });
        rootStack.Children.Add(serverInfo);
        rootStack.Children.Add(cardsGrid);
        rootStack.Children.Add(downloadProgressPanel);
        rootStack.Children.Add(uploadProgressPanel);
        rootStack.Children.Add(actionBar);
        rootStack.Children.Add(resultText);

        var scrollViewer = new ScrollViewer { Content = rootStack, MaxWidth = 860 };
        scrollViewer.Tag = new SpeedTestState
        {
            DownloadSpeedText = downloadSpeedText,
            UploadSpeedText = uploadSpeedText,
            DownloadProgress = downloadProgress,
            UploadProgress = uploadProgress,
            DownloadStatusLabel = downloadStatusLabel,
            UploadStatusLabel = uploadStatusLabel,
            DownloadProgressPanel = downloadProgressPanel,
            UploadProgressPanel = uploadProgressPanel,
            StartBtn = startBtn,
            CancelBtn = cancelBtn,
            ResultText = resultText
        };

        startBtn.Click += async (_, _) => await RunTestAsync(scrollViewer);
        cancelBtn.Click += (_, _) => _cts?.Cancel();

        return scrollViewer;
    }

    private async Task RunTestAsync(ScrollViewer root)
    {
        var state = GetState(root);
        if (state is null) return;

        _isRunning = true;
        state.StartBtn.Visibility = Visibility.Collapsed;
        state.CancelBtn.Visibility = Visibility.Visible;
        state.ResultText.Visibility = Visibility.Collapsed;

        state.DownloadSpeedText.Text = "0.00";
        state.UploadSpeedText.Text = "0.00";

        _cts = new CancellationTokenSource();

        try
        {
            state.DownloadProgressPanel.Visibility = Visibility.Visible;
            state.DownloadProgress.Visibility = Visibility.Visible;
            state.DownloadProgress.IsIndeterminate = true;
            state.DownloadStatusLabel.Text = "正在下载测试（约 30 秒）...";

            var downloadProgress = new Progress<SpeedTestProgress>(p =>
            {
                state.DownloadSpeedText.Text = SpeedTestService.FormatSpeed(p.CurrentSpeedMbps);
                state.DownloadProgress.IsIndeterminate = false;
                state.DownloadProgress.Value = Math.Min(p.CurrentSpeedMbps, 1000);
                state.DownloadStatusLabel.Text = $"已下载 {FormatBytes(p.BytesTransferred)} | {SpeedTestService.FormatSpeed(p.CurrentSpeedMbps)}";
            });

            var downloadResult = await SpeedTestService.RunDownloadTestAsync(downloadProgress, _cts.Token);

            state.DownloadProgress.IsIndeterminate = false;
            state.DownloadProgress.Value = 100;

            if (downloadResult.Success)
            {
                state.DownloadSpeedText.Text = SpeedTestService.FormatSpeed(downloadResult.DownloadMbps);
                state.DownloadStatusLabel.Text = $"平均下载速度 {SpeedTestService.FormatSpeed(downloadResult.DownloadMbps)}";
            }
            else
            {
                state.DownloadStatusLabel.Text = downloadResult.Error;
            }

            if (_cts.Token.IsCancellationRequested)
            {
                state.ResultText.Text = "测试已取消";
                state.ResultText.Foreground = new SolidColorBrush(Colors.Orange);
                state.ResultText.Visibility = Visibility.Visible;
                return;
            }

            state.UploadProgressPanel.Visibility = Visibility.Visible;
            state.UploadProgress.Visibility = Visibility.Visible;
            state.UploadProgress.IsIndeterminate = true;
            state.UploadStatusLabel.Text = "正在上传测试（约 30 秒）...";

            var uploadProgress = new Progress<SpeedTestProgress>(p =>
            {
                state.UploadSpeedText.Text = SpeedTestService.FormatSpeed(p.CurrentSpeedMbps);
                state.UploadProgress.IsIndeterminate = false;
                state.UploadProgress.Value = Math.Min(p.CurrentSpeedMbps, 1000);
                state.UploadStatusLabel.Text = $"已上传 {FormatBytes(p.BytesTransferred)} | {SpeedTestService.FormatSpeed(p.CurrentSpeedMbps)}";
            });

            var uploadResult = await SpeedTestService.RunUploadTestAsync(uploadProgress, _cts.Token);

            state.UploadProgress.IsIndeterminate = false;
            state.UploadProgress.Value = 100;

            if (uploadResult.Success)
            {
                state.UploadSpeedText.Text = SpeedTestService.FormatSpeed(uploadResult.UploadMbps);
                state.UploadStatusLabel.Text = $"平均上传速度 {SpeedTestService.FormatSpeed(uploadResult.UploadMbps)}";
            }
            else
            {
                state.UploadStatusLabel.Text = uploadResult.Error;
            }

            state.ResultText.Text = $"测试完成！下载 {SpeedTestService.FormatSpeed(downloadResult.DownloadMbps)} / 上传 {SpeedTestService.FormatSpeed(uploadResult.UploadMbps)}";
            state.ResultText.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            state.ResultText.Text = "测试已取消";
            state.ResultText.Foreground = new SolidColorBrush(Colors.Orange);
            state.ResultText.Visibility = Visibility.Visible;
        }
        finally
        {
            FinishTest(state);
        }
    }

    private void FinishTest(SpeedTestState state)
    {
        _isRunning = false;
        state.StartBtn.Visibility = Visibility.Visible;
        state.CancelBtn.Visibility = Visibility.Collapsed;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_000_000_000) return $"{bytes / 1_000_000_000.0:F2} GB";
        if (bytes >= 1_000_000) return $"{bytes / 1_000_000.0:F1} MB";
        if (bytes >= 1_000) return $"{bytes / 1_000.0:F0} KB";
        return $"{bytes} B";
    }

    private static Border MakeSpeedCard(string label, TextBlock value, TextBlock unit, string glyph, Color accent)
    {
        var iconBorder = new Border
        {
            Width = 40,
            Height = 40,
            Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(8),
            Child = new FontIcon { FontSize = 20, Foreground = new SolidColorBrush(accent), Glyph = glyph }
        };

        var labelBlock = new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var valuePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        valuePanel.Children.Add(value);
        valuePanel.Children.Add(unit);

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(labelBlock);
        stack.Children.Add(valuePanel);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(iconBorder);
        grid.Children.Add(stack); Grid.SetColumn(stack, 1);

        return new Border
        {
            Padding = new Thickness(16),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = grid
        };
    }

    private static SpeedTestState? GetState(ScrollViewer root) => root?.Tag as SpeedTestState;

    private sealed class SpeedTestState
    {
        public TextBlock DownloadSpeedText = null!;
        public TextBlock UploadSpeedText = null!;
        public ProgressBar DownloadProgress = null!;
        public ProgressBar UploadProgress = null!;
        public TextBlock DownloadStatusLabel = null!;
        public TextBlock UploadStatusLabel = null!;
        public StackPanel DownloadProgressPanel = null!;
        public StackPanel UploadProgressPanel = null!;
        public Button StartBtn = null!;
        public Button CancelBtn = null!;
        public TextBlock ResultText = null!;
    }
}