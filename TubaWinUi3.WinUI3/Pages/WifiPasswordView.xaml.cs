using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using TubaWinUi3.Services;
using Windows.Storage.Streams;

namespace TubaWinUi3.Pages;

public sealed partial class WifiPasswordView : UserControl
{
    private List<WifiNetwork> _all = [];
    private WifiNetwork? _selected;
    private bool _busy;
    private bool _closed;
    private ContentDialog? _shareDialog;
    private ContentDialog? _detailsDialog;
    private TaskCompletionSource? _detailsClosed;

    private bool _lanBusy;
    private bool _lanScanned;
    private LanNetwork? _lanNetwork;
    private CancellationTokenSource? _lanCts;
    private ContentDialog? _lanDialog;
    private IReadOnlyList<LanDevice> _lanDeviceList = [];

    public WifiPasswordView() => InitializeComponent();
    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) => VisualStateManager.GoToState(this, e.NewSize.Width >= 800 ? "Wide" : "Narrow", false);
    private async void View_Loaded(object sender, RoutedEventArgs e)
    {
        _closed = false;
        await RefreshAsync();
        if (Sections.SelectedIndex == 1 && !_lanScanned) await ScanLanAsync();
    }
    private void View_Unloaded(object sender, RoutedEventArgs e) => ClearSensitiveData();
    public void ClearSensitiveData()
    {
        _closed = true;
        _shareDialog?.Hide();
        _detailsDialog?.Hide();
        _lanDialog?.Hide();
        _shareDialog = null;
        _detailsDialog = null;
        _lanDialog = null;
        Password.Text = "";
        foreach (var network in _all) network.Password = "";
        _all.Clear();
        _selected = null;
        Networks.ItemsSource = null;
        CancelLanScan();
        _lanScanned = false;
        _lanNetwork = null;
        _lanDeviceList = [];
        LanDevices.ItemsSource = null;
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async Task RefreshAsync()
    {
        if (_busy || _closed) return;
        _busy = true;
        RefreshButton.IsEnabled = false;
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;
        Empty.Visibility = Visibility.Collapsed;
        Notice.IsOpen = false;
        try
        {
            var networks = await WifiPasswordService.GetNetworksAsync();
            if (_closed) { foreach (var n in networks) n.Password = ""; return; }
            foreach (var n in _all) n.Password = "";
            _all = networks;
            var current = _all.FirstOrDefault(n => n.IsConnected);
            ConnectionTitle.Text = current is null ? "当前未连接已保存的 WiFi" : current.Ssid;
            Summary.Text = $"{_all.Count} 个已保存网络  ·  {_all.Count(n => n.IsConnected)} 个已连接  ·  {_all.Count(n => n.IsOpen)} 个开放网络";
            ApplyFilter();
        }
        catch (Exception ex)
        {
            if (!_closed)
            {
                ConnectionTitle.Text = "无法读取 WiFi 网络";
                Summary.Text = "请检查无线网卡和 WLAN AutoConfig 服务后重试";
                _all.Clear();
                ApplyFilter();
                ShowNotice("读取失败：" + ex.Message, InfoBarSeverity.Error);
            }
        }
        finally
        {
            _busy = false;
            RefreshButton.IsEnabled = true;
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
        }
    }
    private void Search_Changed(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => ApplyFilter();
    private void Options_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void ApplyFilter()
    {
        if (Networks is null || Search is null || Filter is null || Sort is null) return;
        var query = Search.Text.Trim();
        var items = _all.Where(n => (n.Ssid.Contains(query, StringComparison.OrdinalIgnoreCase) || n.Authentication.Contains(query, StringComparison.OrdinalIgnoreCase))
            && (Filter.SelectedIndex switch { 1 => n.IsConnected, 2 => n.HasPassword, 3 => n.IsOpen, _ => true }));
        items = Sort.SelectedIndex switch
        {
            1 => items.OrderBy(n => n.Ssid, StringComparer.CurrentCultureIgnoreCase),
            2 => items.OrderByDescending(n => n.Ssid, StringComparer.CurrentCultureIgnoreCase),
            _ => items.OrderByDescending(n => n.IsConnected).ThenBy(n => n.Ssid, StringComparer.CurrentCultureIgnoreCase)
        };
        var visible = items.ToList();
        var previous = _selected;
        Networks.ItemsSource = visible;
        Networks.SelectedItem = visible.Contains(previous!) ? previous : visible.FirstOrDefault();
        ResultCount.Text = $"已保存的网络  ·  {visible.Count} / {_all.Count}";
        Empty.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = _all.Count == 0 ? "未找到已保存的 WiFi 网络" : "没有匹配的网络，试试其他关键词或筛选条件";
        if (visible.Count == 0) SetDetail(null);
    }
    private void Network_Selected(object sender, SelectionChangedEventArgs e) => SetDetail(Networks.SelectedItem as WifiNetwork);
    private void SetDetail(WifiNetwork? network)
    {
        _selected = network;
        Reveal.IsChecked = false;
        DetailsButton.IsEnabled = network is not null;
        DetailContent.Visibility = network is null ? Visibility.Collapsed : Visibility.Visible;
        DetailName.Text = network?.Ssid ?? "选择一个网络";
        Password.Text = network?.HasPassword == true ? "••••••••••••" : "";
        if (network is null) return;
        DetailStatus.Text = network.Status;
        Auth.Text = network.Authentication;
        Cipher.Text = network.Cipher;
        Adapter.Text = network.Adapter;
        AutoConnect.Text = network.AutoConnect ? "开启" : "关闭";
        HiddenNetwork.Text = network.Hidden ? "是" : "否";
        PasswordHint.Text = network.IsOpen ? "开放网络无需密码，请留意连接安全。" : network.HasPassword ? "点击眼睛显示密码，切换网络后自动隐藏。" : "此网络未保存可读的共享密码，可能使用企业认证或受权限限制。";
        Reveal.IsEnabled = CopyPassword.IsEnabled = network.HasPassword;
        Share.IsEnabled = network.CanShare;
        ToolTipService.SetToolTip(Share, network.CanShare ? "使用手机扫码连接" : "通用二维码支持开放网络和 WPA/WPA2 共享密码网络");
    }
    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        Password.Text = _selected?.HasPassword == true ? (Reveal.IsChecked == true ? _selected.Password : "••••••••••••") : "";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Reveal, Reveal.IsChecked == true ? "隐藏密码" : "显示密码");
    }
    private void CopyPassword_Click(object sender, RoutedEventArgs e) { if (_selected?.HasPassword == true) Copy(_selected.Password, "密码已复制"); }
    private void CopyName_Click(object sender, RoutedEventArgs e) { if (_selected is not null) Copy(_selected.Ssid, "网络名称已复制"); }
    private void Copy(string text, string message)
    {
        if (CopyText(text)) ShowNotice(message, InfoBarSeverity.Success);
        else ShowNotice("复制失败：剪贴板当前不可用，请重试。", InfoBarSeverity.Error);
    }
    private static bool CopyText(string text) => ClipboardService.TrySetText(text).Success;
    private async void Details_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _detailsDialog is not null) return;
        _detailsClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var content = DetailPanel.Child;
        DetailPanel.Child = null;
        try
        {
            _detailsDialog = new ContentDialog { XamlRoot = XamlRoot, Title = "网络详情", Content = content, CloseButtonText = "关闭" };
            await _detailsDialog.ShowAsync();
        }
        catch (Exception ex) { if (!_closed) ShowNotice(ex.Message, InfoBarSeverity.Error); }
        finally
        {
            if (_detailsDialog is not null) _detailsDialog.Content = null;
            DetailPanel.Child = content;
            _detailsDialog = null;
            Reveal.IsChecked = false;
            if (!_closed) SetDetail(_selected);
            _detailsClosed?.TrySetResult();
            _detailsClosed = null;
        }
    }
    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        var network = _selected;
        if (network?.CanShare != true || _shareDialog is not null) return;
        var detailsClosed = _detailsClosed?.Task;
        _detailsDialog?.Hide();
        if (detailsClosed is not null) await detailsClosed;
        if (_closed) return;
        try
        {
            var bytes = QrCodeGenerator.GeneratePng(network.QrPayload(), 360);
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer()); stream.Seek(0);
            var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream);
            if (_closed) return;
            var image = new Image { Source = bitmap, Width = 260, Height = 260 };
            var panel = new StackPanel { Spacing = 16, Children =
            {
                new TextBlock { Text = network.Ssid, FontSize = 20, TextWrapping = TextWrapping.Wrap },
                image,
                new TextBlock { Text = "用手机相机扫码连接。二维码包含此网络凭据，请仅向信任的人展示。", TextWrapping = TextWrapping.Wrap }
            }};
            _shareDialog = new ContentDialog { XamlRoot = XamlRoot, Title = "扫码连接 WiFi", Content = panel, CloseButtonText = "关闭", PrimaryButtonText = "保存二维码" };
            var result = await _shareDialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !_closed)
            {
                var path = Win32Dialogs.PickSave("PNG 图片\0*.png\0", "png", "WiFi-二维码.png");
                if (path is not null) { await System.IO.File.WriteAllBytesAsync(path, bytes); ShowNotice("二维码已保存", InfoBarSeverity.Success); }
            }
            image.Source = null;
        }
        catch (Exception ex) { if (!_closed) ShowNotice("二维码生成失败：" + ex.Message, InfoBarSeverity.Error); }
        finally { _shareDialog = null; }
    }
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        try { if (!await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:network-wifi"))) ShowNotice("无法打开 Windows WiFi 设置", InfoBarSeverity.Warning); }
        catch (Exception ex) { ShowNotice(ex.Message, InfoBarSeverity.Error); }
    }
    private void ShowNotice(string message, InfoBarSeverity severity) { Notice.Message = message; Notice.Severity = severity; Notice.IsOpen = true; }

    #region 局域网设备

    private async void Sections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Sections.SelectedIndex == 1 && !_lanScanned && !_closed) await ScanLanAsync();
    }
    private async void ScanLan_Click(object sender, RoutedEventArgs e) => await ScanLanAsync();
    private async void Router_Click(object sender, RoutedEventArgs e) => await OpenRouterAsync();

    private async Task ScanLanAsync()
    {
        if (_lanBusy || _closed) return;
        _lanBusy = true;
        ScanLanButton.IsEnabled = false;
        LanLoading.IsActive = true;
        LanLoading.Visibility = Visibility.Visible;
        LanEmpty.Visibility = Visibility.Collapsed;
        LanNotice.IsOpen = false;
        LanDevices.ItemsSource = null;
        _lanDeviceList = [];
        LanCount.Text = "正在扫描局域网…";

        var cts = new CancellationTokenSource();
        _lanCts = cts;
        try
        {
            var network = LanDeviceService.GetActiveNetwork();
            if (network is null)
            {
                LanTitle.Text = "未找到局域网连接";
                LanSummary.Text = "请先连接 WiFi 或网线后重试";
                RouterButton.IsEnabled = false;
                LanCount.Text = "局域网设备";
                LanEmptyText.Text = "没有可扫描的网络";
                LanEmpty.Visibility = Visibility.Visible;
                return;
            }

            _lanNetwork = network;
            RouterButton.IsEnabled = true;
            LanTitle.Text = $"{network.InterfaceName}  ·  {network.SubnetLabel}";
            RouterButton.IsEnabled = network.Gateway is not null;

            var progress = new Progress<string>(text => { if (!_closed) LanSummary.Text = text; });
            var result = await LanDeviceService.ScanAsync(progress, cts.Token);
            if (_closed) return;

            _lanNetwork = result.Network;
            _lanScanned = true;
            _lanDeviceList = result.Devices;
            LanDevices.ItemsSource = _lanDeviceList;

            var gateway = result.Network.Gateway is { } address ? $"网关 {address}" : "未找到网关";
            LanSummary.Text = $"本机 {result.Network.LocalAddress} · {gateway} · 探测 {result.ProbedHosts} 个地址"
                + (result.Truncated ? "（网段较大，已限制扫描数量）" : "");
            LanCount.Text = $"局域网设备  ·  {result.Devices.Count} 台";
            LanEmptyText.Text = "没有发现局域网设备，对方可能处于休眠，或已开启网络隔离";
            LanEmpty.Visibility = result.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // 反查主机名不绑定扫描的 CTS：扫描结束时 CTS 会被释放，而 DNS 查询还在后台跑。
            // 每次查询 800ms 超时 + 并发 16，独立完成即可（只回填 HostName，无 UI 副作用）。
            _ = LanDeviceService.ResolveHostNamesAsync(result.Devices);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_closed)
            {
                LanCount.Text = "局域网设备";
                LanTitle.Text = "扫描失败";
                ShowLanNotice("扫描失败：" + ex.Message, InfoBarSeverity.Error);
                LanEmptyText.Text = "扫描未完成，请重试";
                LanEmpty.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (ReferenceEquals(_lanCts, cts)) _lanCts = null;
            cts.Dispose();
            _lanBusy = false;
            ScanLanButton.IsEnabled = true;
            LanLoading.IsActive = false;
            LanLoading.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelLanScan()
    {
        var cts = _lanCts;
        _lanCts = null;
        try { cts?.Cancel(); } catch { }
    }

    private async Task OpenRouterAsync()
    {
        var gateway = _lanNetwork?.Gateway;
        if (gateway is null)
        {
            ShowLanNotice("未找到默认网关，请手动在浏览器打开路由器地址", InfoBarSeverity.Warning);
            return;
        }

        var url = $"http://{gateway}/";
        try
        {
            if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(url)))
                ShowLanNotice($"无法打开浏览器，请手动访问 {url}", InfoBarSeverity.Warning);
        }
        catch (Exception ex) { ShowLanNotice("打开路由器管理页失败：" + ex.Message, InfoBarSeverity.Error); }
    }

    private async void LanDevice_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LanDevice device) await ShowLanDeviceDialogAsync(device);
    }

    private async Task ShowLanDeviceDialogAsync(LanDevice device)
    {
        if (_closed || _lanDialog is not null) return;

        var facts = new Grid { ColumnSpacing = 18, RowSpacing = 8 };
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        void AddFact(string label, string value)
        {
            var row = facts.RowDefinitions.Count;
            facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var labelBlock = new TextBlock { Text = label, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top };
            var valueBlock = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            Grid.SetRow(labelBlock, row);
            Grid.SetRow(valueBlock, row);
            Grid.SetColumn(valueBlock, 1);
            facts.Children.Add(labelBlock);
            facts.Children.Add(valueBlock);
        }

        AddFact("IP 地址", device.Ip);
        AddFact("MAC 地址", device.Mac);
        if (device.Vendor.Length > 0) AddFact("厂商", device.Vendor);
        if (device.HostName.Length > 0) AddFact("主机名", device.HostName);
        AddFact("类型", device.IsSelf ? "本机" : device.IsGateway ? "网关 / 路由器" : "局域网设备");

        var routerHelp = device.CanKick;
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(facts);
        if (routerHelp)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "如果要限制这台设备联网，需要在路由器上操作：把上面的 MAC 地址加入「设备管理 / 访问控制 / MAC 过滤」黑名单，路由器就会拒绝它连入，重启路由器后依然有效。\n\n不同品牌的菜单名称略有差异；路由器管理页通常就是同一网段的网关地址。",
                TextWrapping = TextWrapping.Wrap
            });
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
            Title = device.DisplayName,
            Content = panel,
            CloseButtonText = "关闭"
        };
        if (routerHelp)
        {
            dialog.PrimaryButtonText = "复制 MAC";
            dialog.SecondaryButtonText = "打开路由器";
        }

        _lanDialog = dialog;
        try
        {
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (CopyText(device.Mac)) ShowLanNotice("MAC 地址已复制，在路由器黑名单中粘贴即可", InfoBarSeverity.Success);
                else ShowLanNotice("复制失败：剪贴板当前不可用", InfoBarSeverity.Error);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                await OpenRouterAsync();
            }
        }
        catch (Exception ex) { if (!_closed) ShowLanNotice(ex.Message, InfoBarSeverity.Error); }
        finally
        {
            dialog.Content = null;
            _lanDialog = null;
        }
    }

    private void ShowLanNotice(string message, InfoBarSeverity severity)
    {
        LanNotice.Message = message;
        LanNotice.Severity = severity;
        LanNotice.IsOpen = true;
    }

    #endregion
}
