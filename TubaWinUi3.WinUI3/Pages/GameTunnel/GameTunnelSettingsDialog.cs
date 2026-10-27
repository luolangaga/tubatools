using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;
using static TubaWinUi3.Pages.GameTunnelUi;

namespace TubaWinUi3.Pages;

/// <summary>
/// 联机设置：中继、隐藏 IP、发现节点、本地端口等。改动在下一个房间生效。
/// </summary>
public sealed class GameTunnelSettingsDialog
{
    private readonly ContentDialog _dialog;

    private readonly ToggleSwitch _relay = new() { Header = "允许中转", OnContent = "直连失败时经中转节点传输", OffContent = "只允许直连（打不通就连不上）" };
    private readonly ToggleSwitch _hide = new() { Header = "隐藏我的 IP 地址", OnContent = "全部流量经中转，其他玩家看不到你的地址", OffContent = "优先直连，延迟更低" };
    private readonly ToggleSwitch _publicNodes = new() { Header = "使用内置公共发现节点", OnContent = "开箱即用", OffContent = "只用下面自己填的节点" };
    private readonly TextBox _customNodes = new() { PlaceholderText = "tcp://你的节点:11010，多个用空格或换行分隔", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64 };
    private readonly ToggleSwitch _shareLan = new() { Header = "把房间分享给本机所在局域网", OnContent = "同一 Wi-Fi 的其他设备也能连进来", OffContent = "只允许本机使用" };
    private readonly NumberBox _localPort = new() { Header = "加入时使用的本地端口（0 = 自动）", Minimum = 0, Maximum = 65535, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    private readonly ComboBox _timeout = new() { Header = "找房主的超时时间" };
    private readonly ToggleSwitch _firewall = new() { Header = "自动放行防火墙", OnContent = "为联机组件添加入站规则", OffContent = "需要自己放行" };
    private readonly InfoBar _status = new() { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Informational };

    public GameTunnelSettingsDialog(XamlRoot xamlRoot)
    {
        var settings = GameTunnelCatalog.LoadSettings();

        _relay.IsOn = settings.AllowRelay;
        _hide.IsOn = settings.HideAddress;
        _publicNodes.IsOn = settings.UsePublicNodes;
        _customNodes.Text = settings.CustomNodes ?? "";
        _shareLan.IsOn = settings.ShareWithLan;
        _localPort.Value = settings.LocalPort;
        _firewall.IsOn = settings.AutoFirewall;

        foreach (var seconds in new[] { 30, 60, 120 })
            _timeout.Items.Add(new ComboBoxItem { Content = $"{seconds} 秒", Tag = seconds });
        _timeout.SelectedIndex = settings.SearchTimeoutSeconds switch { 30 => 0, 120 => 2, _ => 1 };

        var panel = new StackPanel { Spacing = 14, MinWidth = 460 };
        panel.Children.Add(Line("改动在下一个房间生效；正在运行的房间不受影响。", 12));
        panel.Children.Add(_relay);
        panel.Children.Add(_hide);
        panel.Children.Add(_publicNodes);
        panel.Children.Add(_customNodes);
        panel.Children.Add(_shareLan);
        panel.Children.Add(_localPort);
        panel.Children.Add(_timeout);
        panel.Children.Add(_firewall);
        panel.Children.Add(_status);

        _dialog = new ContentDialog
        {
            Title = "联机设置",
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 480,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        _dialog.PrimaryButtonClick += (_, args) =>
        {
            var settingsNow = GameTunnelCatalog.LoadSettings();
            var nodes = GameTunnelCatalog.ParseNodes(_customNodes.Text, out var invalid);
            if (invalid.Count > 0)
            {
                args.Cancel = true;
                _status.Severity = InfoBarSeverity.Error;
                _status.Message = "这些节点地址看不懂：" + string.Join("、", invalid.Take(4));
                _status.IsOpen = true;
                return;
            }

            // 隐藏 IP 必须走中转，两者不能同时「只要直连」。
            if (_hide.IsOn && !_relay.IsOn)
            {
                _hide.IsOn = true;
                _relay.IsOn = true;
            }

            settingsNow.AllowRelay = _relay.IsOn;
            settingsNow.HideAddress = _hide.IsOn;
            settingsNow.UsePublicNodes = _publicNodes.IsOn;
            settingsNow.CustomNodes = string.Join(Environment.NewLine, nodes);
            settingsNow.ShareWithLan = _shareLan.IsOn;
            settingsNow.LocalPort = double.IsNaN(_localPort.Value) ? 0 : (int)_localPort.Value;
            settingsNow.SearchTimeoutSeconds = (_timeout.SelectedItem as ComboBoxItem)?.Tag as int? ?? 60;
            settingsNow.AutoFirewall = _firewall.IsOn;
            GameTunnelCatalog.SaveSettings(settingsNow);
        };
    }

    public Task<ContentDialogResult> ShowAsync() => _dialog.ShowAsync().AsTask();
}
