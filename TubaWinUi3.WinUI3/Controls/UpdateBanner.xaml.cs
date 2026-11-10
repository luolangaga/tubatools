using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Controls;

public sealed partial class UpdateBanner : UserControl
{
    private UpdateInfo? _updateInfo;
    private bool _isDownloaded;
    private bool _isDownloading;

    public UpdateBanner()
    {
        InitializeComponent();
        ApplyLocalizedNames();
    }

    private void ApplyLocalizedNames()
    {
        AutomationProperties.SetName(ActionButton, LocalizationService.L("UpdateBanner_ActionButton", "开始更新"));
        AutomationProperties.SetName(ChangelogButton, LocalizationService.L("UpdateBanner_ChangelogButton", "查看更新日志"));
        AutomationProperties.SetName(CloseButton, LocalizationService.L("UpdateBanner_CloseButton", "关闭更新提示"));
    }

    public void ShowUpdateAvailable(UpdateInfo update)
    {
        _updateInfo = update;
        _isDownloaded = false;
        _isDownloading = false;

        BannerText.Text = string.Format(LocalizationService.L("UpdateBanner_NewVersion", "发现新版本 v{0}"), update.Version);
        ActionButton.Content = LocalizationService.L("UpdateBanner_ActionButton", "开始更新");
        ActionButton.Visibility = Visibility.Visible;
        DownloadProgressBar.Visibility = Visibility.Collapsed;
        ChangelogButton.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
    }

    public void ShowDownloading()
    {
        _isDownloading = true;
        _isDownloaded = false;

        BannerText.Text = LocalizationService.L("UpdateBanner_Downloading", "正在下载更新...");
        BannerText.Visibility = Visibility.Collapsed;
        DownloadProgressBar.Visibility = Visibility.Visible;
        ActionButton.Content = LocalizationService.L("UpdateBanner_DownloadingButton", "下载中");
        ActionButton.IsEnabled = false;
        ChangelogButton.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
    }

    public void ShowDownloadProgress(double percentage)
    {
        DownloadProgressBar.IsIndeterminate = false;
        DownloadProgressBar.Value = percentage;
    }

    public void ShowDownloadComplete()
    {
        _isDownloaded = true;
        _isDownloading = false;

        // 此处只检查下载文件仍存在；完整校验和不匹配确认在点击安装后异步进行。
        if (!UpdateService.IsPendingUpdateReady())
        {
            ShowDownloadFailed(LocalizationService.L("UpdateBanner_InvalidFile", "更新文件无效或已被清理，请重新下载"));
            return;
        }

        BannerText.Text = LocalizationService.L("UpdateBanner_DownloadedVerifyOnInstall", "更新已下载，安装前会校验文件");
        BannerText.Visibility = Visibility.Visible;
        DownloadProgressBar.Visibility = Visibility.Collapsed;
        ActionButton.Content = LocalizationService.L("UpdateBanner_InstallButton", "立即安装");
        ActionButton.IsEnabled = true;
        ChangelogButton.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
    }

    public void ShowDownloadFailed(string error)
    {
        _isDownloading = false;
        _isDownloaded = false; // 失败后"重试"应重新下载而不是再次启动失效文件

        BannerText.Text = string.Format(LocalizationService.L("UpdateBanner_DownloadFailed", "更新下载失败：{0}"), error);
        BannerText.Visibility = Visibility.Visible;
        DownloadProgressBar.Visibility = Visibility.Collapsed;
        ActionButton.Content = LocalizationService.L("UpdateBanner_RetryButton", "重试");
        ActionButton.IsEnabled = true;
        ChangelogButton.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
    }

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloaded)
        {
            ActionButton.IsEnabled = false;
            try { if (await UpdateService.LaunchUpdateFromItem()) return; }
            finally { ActionButton.IsEnabled = true; }
            if (UpdateService.LastLaunchWasCancelled) return;

            // 启动失败：文件已被移除/损坏，给出恢复选项而不是崩溃
            await ShowLaunchFailedDialogAsync();
            return;
        }

        if (_isDownloading) return;

        if (_updateInfo is not null)
        {
            var item = UpdateService.AutoDownloadUpdate(_updateInfo);
            if (item is not null)
            {
                ShowDownloading();
                item.PropertyChanged += OnDownloadItemPropertyChanged;
            }
        }
    }

    /// <summary>更新安装包无法启动时的恢复对话框：重新下载 / 打开下载文件夹 / 关闭。</summary>
    private async Task ShowLaunchFailedDialogAsync()
    {
        try
        {
            var dialog = new TubaWinUi3.Controls.FluentContentDialog
            {
                Title = LocalizationService.L("UpdateBanner_LaunchFailedTitle", "更新文件不可用"),
                Content = new TextBlock
                {
                    Text = LocalizationService.L("UpdateBanner_LaunchFailedMessage", "更新安装包已被移除或损坏（可能受安全软件、清理工具或磁盘错误影响），无法启动。请重新下载后再安装。"),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 400
                },
                PrimaryButtonText = LocalizationService.L("UpdateBanner_RedownloadButton", "重新下载"),
                SecondaryButtonText = LocalizationService.L("UpdateBanner_OpenFolderButton", "打开下载文件夹"),
                CloseButtonText = LocalizationService.L("Common_Close", "关闭"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                UpdateService.ClearPendingUpdate();
                if (_updateInfo is not null)
                {
                    var item = UpdateService.AutoDownloadUpdate(_updateInfo);
                    if (item is not null)
                    {
                        ShowDownloading();
                        item.PropertyChanged += OnDownloadItemPropertyChanged;
                    }
                }
            }
            else if (result == ContentDialogResult.Secondary)
            {
                UpdateService.OpenUpdateFolder();
            }
        }
        catch
        {
            // 对话框自身异常不影响主流程
        }
    }

    private void OnDownloadItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not DownloadItem item) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(DownloadItem.State):
                    switch (item.State)
                    {
                        case DownloadItemState.Completed:
                            item.PropertyChanged -= OnDownloadItemPropertyChanged;
                            ShowDownloadComplete();
                            break;
                        case DownloadItemState.Failed:
                            item.PropertyChanged -= OnDownloadItemPropertyChanged;
                            ShowDownloadFailed(item.ErrorMessage ?? LocalizationService.L("Common_UnknownError", "未知错误"));
                            break;
                    }
                    break;
                case nameof(DownloadItem.Progress):
                    if (item.Progress is not null && item.Progress.TotalBytes > 0)
                    {
                        ShowDownloadProgress(item.Progress.Percentage);
                    }
                    break;
            }
        });
    }

    private void ChangelogButton_Click(object sender, RoutedEventArgs e)
    {
        WhatsNewWindow.Show();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
        UpdateService.ClearPendingUpdate();
    }
}
