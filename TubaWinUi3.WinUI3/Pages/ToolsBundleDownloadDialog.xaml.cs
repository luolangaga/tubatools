using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class ToolsBundleDownloadDialog : ContentDialog
{
    private ToolsBundleUpdateInfo? _updateInfo;
    private bool _isBusy;
    private bool _selectedLite;

    public bool DownloadSucceeded { get; private set; }

    public ToolsBundleDownloadDialog()
    {
        InitializeComponent();
        XamlRoot = App.MainWindow?.Content?.XamlRoot;
    }

    public void SetDescription(string text)
    {
        DescText.Text = text;
    }

    /// <summary>
    /// 弹出下载对话框。info 可为 null（内部自行检查更新），也可直接传入已解析的
    /// 更新信息（含 HasUpdate=false 的场景：精简版用户升级到完整版）。
    /// </summary>
    public async Task ShowDownloadAsync(ToolsBundleUpdateInfo? info = null)
    {
        if (info is not null)
        {
            ApplyInfo(info);
        }
        else
        {
            ResolvingSection.Visibility = Visibility.Visible;
            _ = ResolveAndShowAsync();
        }

        await ShowAsync();
    }

    private void ApplyInfo(ToolsBundleUpdateInfo info)
    {
        _updateInfo = info;
        var kind = ToolsBundleService.GetInstalledKind();

        // 完整版已是最新：无事可做（完整版不可降级到精简版，也不重复下载）
        if (kind == ToolsBundleService.KindFull && !info.HasUpdate)
        {
            DescText.Text = "当前完整版内核已是最新版本，无需下载。";
            IsPrimaryButtonEnabled = false;
            return;
        }

        UpdateDescriptionFromInfo(info);
        ShowVariantSelection(info, kind);
    }

    private void UpdateDescriptionFromInfo(ToolsBundleUpdateInfo info)
    {
        if (info.HasUpdate)
        {
            var sizeStr = info.Size > 0 ? $"（完整版约 {ToolsBundleService.FormatSize(info.Size)}）" : "";
            DescText.Text = $"发现内核新版本 v{info.Version}{sizeStr}，请选择要下载的版本。";
        }
        else
        {
            DescText.Text = $"当前内核版本 v{info.Version}，可选择切换到完整版内核。";
        }
    }

    /// <summary>
    /// 展示 精简版/完整版 选择：
    /// - 已安装完整版：精简版选项禁用（不支持降级）；
    /// - 精简版已是最新：精简版禁用，仅可升级完整版；
    /// - 首次安装：两者均可选，默认完整版。
    /// </summary>
    private void ShowVariantSelection(ToolsBundleUpdateInfo info, string? kind)
    {
        VariantSection.Visibility = Visibility.Visible;
        IsPrimaryButtonEnabled = true;

        var liteSelectable = info.HasLiteAsset &&
                             kind != ToolsBundleService.KindFull &&
                             info.HasUpdate;

        LiteRadio.IsEnabled = liteSelectable;
        LiteRadioSub.Text = !info.HasLiteAsset
            ? "该版本未提供精简包"
            : kind == ToolsBundleService.KindFull
                ? "已安装完整版，不可降级"
                : !info.HasUpdate
                    ? (kind == ToolsBundleService.KindLite ? "当前已是最新" : "已内置精简工具，无需下载")
                    : info.LiteSize > 0
                        ? $"约 {ToolsBundleService.FormatSize(info.LiteSize)}"
                        : "";

        FullRadio.IsEnabled = true;
        FullRadioSub.Text = info.Size > 0 ? $"约 {ToolsBundleService.FormatSize(info.Size)}" : "";
        FullRadio.IsChecked = true;

        string? hint = (kind, info.HasUpdate) switch
        {
            (ToolsBundleService.KindFull, true) => "已安装完整版内核，不支持降级到精简版。",
            (ToolsBundleService.KindLite, false) => "当前精简版内核已是最新，可升级到完整版获得全部工具。",
            (null, false) => "已内置精简工具集，可下载完整版内核获得全部工具。",
            _ => null
        };
        if (hint is null)
        {
            VariantHintText.Visibility = Visibility.Collapsed;
        }
        else
        {
            VariantHintText.Text = hint;
            VariantHintText.Visibility = Visibility.Visible;
        }

        // 两个下载源同时竞赛，无需用户手动选择
        SourceSection.Visibility = Visibility.Visible;
    }

    private async Task ResolveAndShowAsync()
    {
        try
        {
            var info = await ToolsBundleService.CheckForToolsUpdateAsync();
            ResolvingSection.Visibility = Visibility.Collapsed;

            if (info is null)
            {
                DescText.Text = $"无法获取内核信息，请检查网络连接后重试。";
                return;
            }

            ApplyInfo(info);
        }
        catch (Exception ex)
        {
            ResolvingSection.Visibility = Visibility.Collapsed;
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
    }

    private void OnVariantChecked(object sender, RoutedEventArgs e)
    {
        if (sender == LiteRadio) _selectedLite = true;
        else if (sender == FullRadio) _selectedLite = false;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_isBusy)
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
        if (_updateInfo is null)
        {
            _updateInfo = await ToolsBundleService.CheckForToolsUpdateAsync();
            if (_updateInfo is null)
            {
                ErrorBar.Message = $"未找到可用的内核更新。";
                ErrorBar.IsOpen = true;
                return;
            }
            ApplyInfo(_updateInfo);
        }

        var lite = _selectedLite;
        var version = _updateInfo.Version;
        var kind = lite ? ToolsBundleService.KindLite : ToolsBundleService.KindFull;
        var variantLabel = lite ? "精简版" : "完整版";

        _isBusy = true;
        IsPrimaryButtonEnabled = false;
        CloseButtonText = null;
        PrimaryButtonText = "已加入队列";
        SourceSection.Visibility = Visibility.Collapsed;
        VariantSection.Visibility = Visibility.Collapsed;

        // MSIX 解压到 LocalAppData 内核目录；精简版便携已内置 Tools 时就地升级
        var toolsDir = ToolsBundleService.GetInstallTargetDir();

        var resolver = ToolsBundleService.CreateUrlResolver(_updateInfo, preferGitCode: true, lite: lite);

        var item = DownloadQueueService.EnqueueWithResolver(
            displayName: $"{variantLabel}内核 " + (version ?? ""),
            urlResolver: resolver,
            destinationPath: toolsDir,
            postProcessor: new ToolsBundleExtractProcessor(version, kind),
            description: lite ? "图吧工具箱精简版内核" : "图吧工具箱完整内核",
            glyph: "\uE896",
            fallbackUrl: _updateInfo.FallbackUrl(lite));

        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(DownloadItem.State))
            {
                DispatcherQueue.TryEnqueue(() => OnDownloadItemStateChanged(item));
            }
            else if (e.PropertyName == nameof(DownloadItem.Progress))
            {
                DispatcherQueue.TryEnqueue(() => OnDownloadItemProgressChanged(item));
            }
        };

        ProgressSection.Visibility = Visibility.Visible;
        DownloadProgressBar.IsIndeterminate = true;
        ProgressLabel.Text = "已加入下载队列...";
    }

    private void OnDownloadItemStateChanged(DownloadItem item)
    {
        switch (item.State)
        {
            case DownloadItemState.Downloading:
                ProgressLabel.Text = $"正在下载内核...";
                DownloadProgressBar.IsIndeterminate = false;
                break;
            case DownloadItemState.Processing:
                ProgressLabel.Text = $"正在解压内核...";
                DownloadProgressBar.IsIndeterminate = true;
                PercentText.Text = "解压中";
                SpeedText.Text = "--";
                SizeText.Text = "--";
                TimeText.Text = "--";
                break;
            case DownloadItemState.Completed:
                DownloadSucceeded = true;
                Hide();
                _ = ShowSuccessDialogAsync();
                break;
            case DownloadItemState.Failed:
                ErrorBar.Message = LocalizeFailureMessage(item.ErrorMessage);
                ErrorBar.IsOpen = true;
                IsPrimaryButtonEnabled = true;
                PrimaryButtonText = "重试";
                ProgressSection.Visibility = Visibility.Collapsed;
                SourceSection.Visibility = Visibility.Collapsed;
                if (_updateInfo is not null && ToolsBundleService.GetInstalledKind() != ToolsBundleService.KindFull)
                {
                    // 失败重试时恢复版本选择（完整版用户本就无选择界面）
                    VariantSection.Visibility = Visibility.Visible;
                }
                _isBusy = false;
                CloseButtonText = "跳过";
                break;
        }
    }

    private static string LocalizeFailureMessage(string? message)
    {
        if (string.IsNullOrEmpty(message)) return "内核下载失败，请重试。";

        // 内部异常为 UnauthorizedAccessException 时给出中文提示
        if (message.Contains("Access to the path", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("UnauthorizedAccessException", StringComparison.OrdinalIgnoreCase))
        {
            var path = ExtractQuotedPath(message);
            return string.IsNullOrEmpty(path)
                ? "内核安装失败：目标文件被占用或只读，请关闭正在运行的工具（如 DirectX Repair）后重试。"
                : $"内核安装失败：无法写入 {path}（文件被占用或只读）。请关闭正在运行的工具（如 DirectX Repair）后重试。";
        }

        return message;
    }

    private static string? ExtractQuotedPath(string message)
    {
        var start = message.IndexOf('\'');
        if (start < 0) return null;
        var end = message.IndexOf('\'', start + 1);
        if (end <= start) return null;
        return message[(start + 1)..end];
    }

    private void OnDownloadItemProgressChanged(DownloadItem item)
    {
        if (item.Progress is null) return;
        var p = item.Progress;

        DownloadProgressBar.IsIndeterminate = false;
        DownloadProgressBar.Value = p.Percentage;
        PercentText.Text = $"{p.Percentage:F1}%";
        SpeedText.Text = DownloadQueueService.FormatSpeed(p.SpeedMbps);
        SizeText.Text = $"{DownloadQueueService.FormatSize(p.BytesReceived)} / {DownloadQueueService.FormatSize(p.TotalBytes)}";
        TimeText.Text = DownloadQueueService.FormatTime(p.EstimatedRemaining);
    }

    private async Task ShowSuccessDialogAsync()
    {
        var variantLabel = _selectedLite ? "精简版" : "完整版";
        var dialog = new ContentDialog
        {
            Title = "下载完成",
            XamlRoot = XamlRoot,
            PrimaryButtonText = "完成",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var stack = new StackPanel { Spacing = 12 };

        var border = new Border
        {
            Padding = new Thickness(20, 16, 20, 16),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
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
            Background = new SolidColorBrush(Microsoft.UI.Colors.Green),
            CornerRadius = new CornerRadius(12)
        };
        iconBorder.Child = new FontIcon
        {
            Glyph = "\uE73E",
            FontSize = 24,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
        };
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        infoStack.Children.Add(new TextBlock
        {
            Text = $"{variantLabel}内核下载完成！",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        });
        infoStack.Children.Add(new TextBlock
        {
            Text = "已解压到工具目录，刷新后即可使用对应工具。",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        if (_updateInfo is not null)
        {
            infoStack.Children.Add(new TextBlock
            {
                Text = $"版本：v{_updateInfo.Version}",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
        }
        Grid.SetColumn(infoStack, 1);
        grid.Children.Add(infoStack);

        border.Child = grid;
        stack.Children.Add(border);
        dialog.Content = stack;

        await dialog.ShowAsync();
    }
}
