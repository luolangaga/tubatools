using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>
/// 「连接手机」内置工具页：服务开关与状态、配对信息（二维码 / 配对码 / IP）、
/// 快捷入口（聊天弹窗 / 手机任务弹窗）与文件接收位置。
/// </summary>
public sealed partial class PhoneLinkPage : Page
{
    private bool _subscribed;
    private bool _tasksQueued;
    private string? _qrPayload;

    public PhoneLinkPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _subscribed = true;
            PhoneLinkService.StateChanged += OnServiceStateChanged;
            PhoneLinkService.JobsChanged += OnJobsChanged;
        }
        RefreshService();
        RefreshReceive();
        RefreshJobs();
        if (!_tasksQueued)
        {
            _tasksQueued = true;
            _ = RunLoadedTasksAsync();
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed) return;
        _subscribed = false;
        PhoneLinkService.StateChanged -= OnServiceStateChanged;
        PhoneLinkService.JobsChanged -= OnJobsChanged;
    }

    private void OnServiceStateChanged() => DispatcherQueue.TryEnqueue(RefreshService);

    private void OnJobsChanged() => DispatcherQueue.TryEnqueue(RefreshJobs);

    // ───────────────────────── 服务 / 配对 ─────────────────────────

    private void RefreshService()
    {
        var running = PhoneLinkService.IsRunning;
        if (ServiceToggle.IsOn != running) ServiceToggle.IsOn = running;
        ServiceStatusText.Text = running ? $"运行中 · 0.0.0.0:{PhoneLinkService.Port}（所有网卡）" : "已关闭";
        PairRunningPanel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        PairOffHint.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        if (!running)
        {
            DevicesText.Text = "";
            DeviceListPanel.Children.Clear();
            return;
        }
        var ips = PhoneLinkService.GetLocalIps();
        var ip = ips[0];
        PairCodeText.Text = PhoneLinkService.PairCode;
        AddressText.Text = "可连接地址（无需选择网卡）：\n" +
            string.Join("\n", ips.Select(address => $"{address}:{PhoneLinkService.Port}")) +
            (ip == "127.0.0.1" ? "\n未检测到局域网 IPv4 地址，请检查网络连接。" : "");
        RefreshDeviceList();
        _ = UpdateQrAsync(PhoneLinkService.BuildQrPayload(ip));
    }

    /// <summary>已配对设备列表：每台单独「断开」，不必把其他手机一起踢掉。</summary>
    private void RefreshDeviceList()
    {
        var devices = PhoneLinkService.PairedDevices;
        DevicesText.Text = devices.Count == 0 ? "暂无已连接的手机" : $"已连接 {devices.Count} 台手机";
        DeviceListPanel.Children.Clear();
        foreach (var device in devices)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                Text = device.Name,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            info.Children.Add(new TextBlock
            {
                Text = $"连接于 {device.Since:yyyy-MM-dd HH:mm}",
                FontSize = 12,
                Opacity = 0.7
            });
            row.Children.Add(info);

            var revoke = new Button
            {
                Content = "断开",
                Tag = device.Id,
                VerticalAlignment = VerticalAlignment.Center
            };
            revoke.Click += DeviceRevokeBtn_Click;
            Grid.SetColumn(revoke, 1);
            row.Children.Add(revoke);

            DeviceListPanel.Children.Add(row);
        }
    }

    private void DeviceRevokeBtn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string id) PhoneLinkService.RevokeDevice(id);
    }

    private void ServiceToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (ServiceToggle.IsOn == PhoneLinkService.IsRunning) return;
        string? startupError = null;
        try
        {
            if (ServiceToggle.IsOn) PhoneLinkService.Start();
            else PhoneLinkService.Stop();
        }
        catch (Exception ex)
        {
            startupError = "启动失败：" + ex.Message;
        }
        RefreshService();
        if (startupError is not null) ServiceStatusText.Text = startupError;
        RefreshJobs();
    }

    private void RefreshCodeBtn_Click(object sender, RoutedEventArgs e) => PhoneLinkService.RefreshPairCode();

    private void RevokeBtn_Click(object sender, RoutedEventArgs e) => PhoneLinkService.RevokeAll();

    private async Task UpdateQrAsync(string payload)
    {
        if (_qrPayload == payload) return;
        _qrPayload = payload;
        try
        {
            var png = QrCodeGenerator.GeneratePng(payload, 400);
            var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await stream.WriteAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(png));
            stream.Seek(0);
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(stream);
            if (_qrPayload == payload) QrImage.Source = bmp;
        }
        catch
        {
            _qrPayload = null;
        }
    }

    // ───────────────────────── 快捷入口 ─────────────────────────

    private void RefreshJobs()
    {
        var running = PhoneLinkService.GetJobs().Count(j => j.IsRunning);
        JobsBadgeText.Text = $"{running} 个进行中";
        JobsBadgeText.Visibility = running > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChatRowBtn_Click(object sender, RoutedEventArgs e) => ShowChatDialog();

    private void JobsRowBtn_Click(object sender, RoutedEventArgs e) => ShowJobsDialog();

    private void ShowChatDialog()
    {
        if (XamlRoot is null) return;
        _ = ContentDialogGuard.ShowWhenIdleAsync(new PhoneLinkChatDialog(XamlRoot), TimeSpan.FromSeconds(10));
    }

    private void ShowJobsDialog(string? jobId = null)
    {
        if (XamlRoot is null) return;
        _ = ContentDialogGuard.ShowWhenIdleAsync(new PhoneLinkJobsDialog(XamlRoot, jobId), TimeSpan.FromSeconds(10));
    }

    // ───────────────────────── 文件接收 ─────────────────────────

    private void RefreshReceive()
    {
        var path = PhoneLinkService.ReceiveDir;
        ReceivePathText.Text = path;
        ToolTipService.SetToolTip(ReceivePathText, path);
        ResetReceiveBtn.Visibility = PhoneLinkService.CustomReceiveDir is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void ChangeReceiveBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = await Win32Dialogs.PickFolderAsync();
            if (string.IsNullOrWhiteSpace(folder)) return;
            PhoneLinkService.SetReceiveDir(folder);
            RefreshReceive();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PhoneLink] 选择接收位置失败：{ex.Message}");
        }
    }

    private void ResetReceiveBtn_Click(object sender, RoutedEventArgs e)
    {
        PhoneLinkService.SetReceiveDir(null);
        RefreshReceive();
    }

    private void OpenReceiveBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = PhoneLinkService.ReceiveDir;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    // ───────────────────────── 首屏任务（托盘引导 / 通知点击落地）─────────────────────────

    private async Task RunLoadedTasksAsync()
    {
        try
        {
            await MaybePromptCloseToTrayAsync();
            var pending = PhoneLinkActivation.TryConsume();
            if (pending is null) return;
            if (pending.Target == "chat") ShowChatDialog();
            else if (pending.Target == "jobs") ShowJobsDialog(pending.JobId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PhoneLink] 首屏任务失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 首次打开本工具时询问是否允许「关闭窗口后留在系统托盘」：
    /// 手机连接是后台服务，关窗即退出会让手机再也连不上；文案照搬设置项。
    /// </summary>
    private async Task MaybePromptCloseToTrayAsync()
    {
        if (AppSettings.Get("PhoneLinkCloseToTrayPrompted") is not null) return;
        if (CloseToTrayService.IsEnabled)
        {
            // 已经是开启状态：无需再问，直接记标记
            AppSettings.Set("PhoneLinkCloseToTrayPrompted", "1");
            return;
        }
        var xamlRoot = XamlRoot ?? App.MainWindow?.Content?.XamlRoot;
        if (xamlRoot is null) return; // 窗口还没就绪：不消耗标记，下次打开再问

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
            Title = LocalizationService.L("Settings_CloseToTray_Title", "关闭主窗口时最小化到系统托盘"),
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = LocalizationService.L("Settings_CloseToTray_Desc",
                           "点关闭按钮不再退出程序，而是留在系统托盘继续运行，避免误关闭后重启时重新检测硬件信息；双击托盘图标恢复窗口，右键可退出")
                       + "\n\n开启后关闭窗口时，手机连接会继续在系统托盘后台运行，手机随时可以再连上。"
            },
            PrimaryButtonText = "开启",
            CloseButtonText = "暂不开启",
            DefaultButton = ContentDialogButton.Primary
        };
        var confirmed = false;
        dialog.PrimaryButtonClick += (_, _) => confirmed = true;
        // 弹窗真的出现（无论选哪个按钮）才算「问过了」；被其他弹窗挡住没弹出来则下次再问
        var shown = await ContentDialogGuard.ShowWhenIdleAsync(dialog, TimeSpan.FromSeconds(10));
        if (!shown) return;
        AppSettings.Set("PhoneLinkCloseToTrayPrompted", "1");
        if (confirmed) CloseToTrayService.SetEnabled(true);
    }
}
