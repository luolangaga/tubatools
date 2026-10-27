using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using static TubaWinUi3.Pages.GameTunnelUi;

namespace TubaWinUi3.Pages;

/// <summary>
/// 网络检测：把「联机组件、当前房间、链路、防火墙、日志」放在一页里，出问题只需要看这里。
/// </summary>
public sealed class GameTunnelDiagnosticsDialog
{
    private readonly ContentDialog _dialog;
    private readonly StackPanel _body = new() { Spacing = 12, MinWidth = 540 };
    private readonly ProgressRing _ring = new() { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StringBuilder _report = new();

    private readonly TextBox _portBox = new() { PlaceholderText = "要测试的本机端口，如 25565", FontFamily = new FontFamily("Consolas") };
    private readonly TextBlock _portResult = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };

    public GameTunnelDiagnosticsDialog(XamlRoot xamlRoot)
    {
        _dialog = new ContentDialog
        {
            Title = "网络检测",
            Content = new ScrollViewer
            {
                Content = _body,
                MaxHeight = 470,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            PrimaryButtonText = "重新检测",
            SecondaryButtonText = "复制报告",
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        _dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            await LoadAsync();
        };
        _dialog.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            if (_report.Length > 0) ClipboardService.TrySetText(_report.ToString());
        };

        _body.Children.Add(_ring);
        _dialog.Opened += async (_, _) => await LoadAsync();
    }

    public Task<ContentDialogResult> ShowAsync() => _dialog.ShowAsync().AsTask();

    // ══════════════════ 数据加载 ══════════════════

    private async Task LoadAsync()
    {
        _body.Children.Clear();
        _body.Children.Add(_ring);
        _report.Clear();

        try
        {
            EasyTierPackage.Invalidate();
            var session = TunnelSession.Instance;

            _body.Children.Clear();
            _body.Children.Add(BuildComponentSection());
            _body.Children.Add(BuildSessionSection(session));
            if (session.Peers.Count > 0) _body.Children.Add(BuildPeersSection(session));

            var port = session.LocalPort > 0 ? session.LocalPort : session.GamePort;
            if (port > 0 && _portBox.Text.Length == 0) _portBox.Text = port.ToString();
            _body.Children.Add(BuildProbeSection());

            var firewall = await GameTunnelFirewall.ListRulesAsync();
            _body.Children.Add(BuildFirewallSection(firewall));
            _body.Children.Add(BuildLogSection());
            _body.Children.Add(BuildFooter());
        }
        catch (Exception ex)
        {
            _body.Children.Clear();
            _body.Children.Add(Info($"检测失败：{ex.Message}", InfoBarSeverity.Error));
        }
    }

    // ══════════════════ 各区段 ══════════════════

    private FrameworkElement BuildComponentSection()
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(SectionHeader("联机组件"));

        if (EasyTierPackage.IsReady)
        {
            panel.Children.Add(Line($"EasyTier {EasyTierPackage.Version} · {EasyTierPackage.Architecture} · 校验通过"));
            panel.Children.Add(Line(EasyTierPackage.InstallDirectory, 11));
            _report.AppendLine($"[组件] EasyTier {EasyTierPackage.Version} {EasyTierPackage.Architecture} ok");
        }
        else
        {
            panel.Children.Add(Line("还没有下载，或文件校验失败。回到主页点「准备联机组件」即可。"));
            _report.AppendLine("[组件] 未就绪");
        }

        return Card(panel);
    }

    private FrameworkElement BuildSessionSection(TunnelSession session)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(SectionHeader("当前房间"));

        if (!session.IsActive && session.Stage != TunnelStage.Failed)
        {
            panel.Children.Add(Line("当前没有房间。建房或加入后这里会显示角色、房间码与连接质量。"));
            _report.AppendLine("[房间] 无");
            return Card(panel);
        }

        var role = session.IsHost ? "房主" : session.IsGuest ? "加入者" : "-";
        panel.Children.Add(Line($"角色：{role} · 状态：{DescribeStage(session.Stage)}"));
        if (session.RoomCode is { Length: > 0 } code)
            panel.Children.Add(Line($"房间码：{code[..Math.Min(6, code.Length)]}-****-****-****（日志与报告中不写完整码）"));
        if (session.GamePort > 0) panel.Children.Add(Line($"房主游戏端口：{session.GamePort}"));
        if (session.LocalPort > 0) panel.Children.Add(Line($"本机转发端口：{session.LocalPort}"));
        if (session.HostPeer is { } host)
            panel.Children.Add(Line($"房主：{host.Hostname} · {host.Describe()} · {host.Tunnel}"));
        panel.Children.Add(Line($"网络内设备：{session.Peers.Count(p => p.IsMember && !p.IsLocal)} 台（直连 {session.DirectPeers} / 中转 {session.RelayedPeers}）"));
        panel.Children.Add(Line($"发现节点：{(session.ConnectedNodes < 0 ? "未知" : session.ConnectedNodes + " 个已连接")}"));
        panel.Children.Add(Line($"本机 NAT：{session.LocalNat.Describe()}"));
        if (session.Warning is { Length: > 0 } warning) panel.Children.Add(Line("提醒：" + warning, 12));
        if (session.Error is { Length: > 0 } error) panel.Children.Add(Line("错误：" + error, 12));

        _report.AppendLine($"[房间] {role} {DescribeStage(session.Stage)} room={session.RoomCode ?? "-"} game={session.GamePort} local={session.LocalPort} nodes={session.ConnectedNodes} nat={session.LocalNat}");
        return Card(panel);
    }

    private FrameworkElement BuildPeersSection(TunnelSession session)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(SectionHeader("房间设备"));

        foreach (var peer in session.Peers.OrderByDescending(p => p.IsLocal).ThenBy(p => p.Hostname))
        {
            var name = peer.IsLocal ? "本机" : peer.Hostname;
            panel.Children.Add(Line($"{name} · {peer.IPv4} · {peer.Describe()} · {peer.Tunnel}"));
        }
        return Card(panel);
    }

    private FrameworkElement BuildProbeSection()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(SectionHeader("TCP 连通性测试"));
        panel.Children.Add(Line("填本机转发端口或房主游戏端口，点右边按钮测一次。直连/中转都能测。"));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _portBox.Width = 180;
        var button = new Button { Content = "测试", Padding = new Thickness(14, 6, 14, 6), CornerRadius = new CornerRadius(7) };
        button.Click += async (_, _) => await ProbeAsync();
        row.Children.Add(_portBox);
        row.Children.Add(button);
        panel.Children.Add(row);
        panel.Children.Add(_portResult);

        return Card(panel);
    }

    private async Task ProbeAsync()
    {
        if (!int.TryParse(_portBox.Text.Trim(), out var port) || !GameTunnelCatalog.IsValidPort(port))
        {
            _portResult.Text = "端口不合法（1-65535）";
            return;
        }

        _portResult.Text = $"正在连接 127.0.0.1:{port}…";
        var ok = await GameTunnelProbe.CanConnectAsync(port, TimeSpan.FromSeconds(4));
        _portResult.Text = ok
            ? $"127.0.0.1:{port} 可以连接 ✓"
            : $"127.0.0.1:{port} 没有响应——确认游戏已经开好，或房主还开着房间。";
        _report.AppendLine($"[测试] 127.0.0.1:{port} => {(ok ? "ok" : "fail")}");
    }

    private FrameworkElement BuildFirewallSection(string rules)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(SectionHeader("防火墙"));
        panel.Children.Add(Line(string.IsNullOrWhiteSpace(rules)
            ? "没有找到本工具添加的规则。首次建房/加入时会自动为 easytier-core.exe 放行入站。"
            : rules.Trim()));

        var clean = new Button { Content = "清理本工具的防火墙规则", Padding = new Thickness(12, 6, 12, 6), CornerRadius = new CornerRadius(7) };
        clean.Click += async (_, _) =>
        {
            clean.IsEnabled = false;
            var result = await GameTunnelFirewall.RemoveAllAsync();
            clean.Content = result.Ok ? "已清理" : result.Message;
            clean.IsEnabled = true;
        };
        panel.Children.Add(clean);
        return Card(panel);
    }

    private FrameworkElement BuildLogSection()
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(SectionHeader("最近日志"));
        var log = TunnelLog.ReadRecent(40);
        panel.Children.Add(Line(string.IsNullOrWhiteSpace(log) ? "还没有日志。" : log, 11));
        return Card(panel);
    }

    private FrameworkElement BuildFooter()
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(Line($"日志文件：{TunnelLog.LogPath}", 11));
        panel.Children.Add(Line("报告不包含完整的房间码与网络密钥，可以放心发给别人排查。", 11));
        return Card(panel);
    }

    // ══════════════════ 小工具 ══════════════════

    private static string DescribeStage(TunnelStage stage) => stage switch
    {
        TunnelStage.Working => "准备中",
        TunnelStage.Ready => "已就绪",
        TunnelStage.Failed => "失败",
        _ => "空闲"
    };

    private static InfoBar Info(string message, InfoBarSeverity severity) => new()
    {
        Message = message,
        Severity = severity,
        IsOpen = true,
        IsClosable = false
    };
}
