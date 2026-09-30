using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using static TubaWinUi3.Pages.GameTunnelUi;

namespace TubaWinUi3.Pages;

/// <summary>
/// 网络检测：把「为什么连不上 / 会不会卡」讲清楚。
/// 状态、链路质量（netcheck）、告警、设备、防火墙都在这里，出问题时只需要看这一页。
/// </summary>
public sealed class GameTunnelDiagnosticsDialog
{
    private readonly ContentDialog _dialog;
    private readonly StackPanel _body = new() { Spacing = 12, MinWidth = 520 };
    private readonly ProgressRing _ring = new() { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StringBuilder _report = new();

    private readonly TextBox _pingTargetBox = new() { PlaceholderText = "对方设备的联机地址，如 100.101.102.103", FontFamily = new FontFamily("Consolas") };
    private readonly TextBlock _pingResult = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };

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

        _body.Children.Add(_ring);

        _dialog.Opened += async (_, _) => await LoadAsync();
    }

    public Task<ContentDialogResult> ShowAsync() => _dialog.ShowAsync().AsTask();

    /// <summary>用虚拟网络里第一台有地址的设备预填测试目标——省得用户去别处抄地址。</summary>
    private void FillPingTargetFromPeers(IReadOnlyList<TailscalePeer> peers)
    {
        if (_pingTargetBox.Text.Length > 0) return;

        var peer = peers.FirstOrDefault(p => p.Online && p.Ipv4 is { Length: > 0 })
                   ?? peers.FirstOrDefault(p => p.Ipv4 is { Length: > 0 });
        if (peer?.Ipv4 is { Length: > 0 } ip) _pingTargetBox.Text = ip;
    }

    // ══════════════════ 数据加载 ══════════════════

    private async Task LoadAsync()
    {
        _body.Children.Clear();
        _ring.Visibility = Visibility.Visible;
        _body.Children.Add(_ring);
        _report.Clear();

        try
        {
            TailscaleCli.Invalidate();
            if (!TailscaleService.IsInstalled)
            {
                _body.Children.Clear();
                _body.Children.Add(Info("还没有安装 Tailscale，先到主页点「准备联机环境」。", InfoBarSeverity.Warning));
                return;
            }

            var status = await TailscaleService.GetStatusAsync();
            var peers = status?.Peers ?? [];
            FillPingTargetFromPeers(peers);

            _body.Children.Clear();
            _body.Children.Add(BuildStatusSection(status));
            _body.Children.Add(BuildPingSection());

            var health = TailscaleService.DescribeHealthLines(status?.Health);
            if (health.Count > 0) _body.Children.Add(BuildHealthSection(health));

            if (peers.Count > 0) _body.Children.Add(BuildPeersSection(peers));

            var firewall = await TailscaleService.ListFirewallRulesAsync();
            _body.Children.Add(BuildFirewallSection(firewall));
            _body.Children.Add(BuildFooter());

            // netcheck 比较慢，让它单独填充
            var netSection = new StackPanel { Spacing = 6 };
            _body.Children.Insert(Math.Min(2, _body.Children.Count), netSection);
            _ = FillNetCheckAsync(netSection);
        }
        catch (Exception ex)
        {
            _body.Children.Clear();
            _body.Children.Add(Info($"检测失败：{ex.Message}", InfoBarSeverity.Error));
        }
    }

    private async Task FillNetCheckAsync(StackPanel host)
    {
        var ring = new ProgressRing { IsActive = true, Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Left };
        host.Children.Add(SectionHeader("链路质量（能不能直连）"));
        host.Children.Add(ring);
        var text = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Subtle };
        host.Children.Add(text);

        var output = await TailscaleService.NetCheckAsync();
        var report = TailscaleService.ParseNetCheck(output);
        if (report is null)
        {
            text.Text = "没有拿到检测报告（网络受限时可能失败，不影响联机本身）。";
            ring.IsActive = false;
            ring.Visibility = Visibility.Collapsed;
            return;
        }

        var lines = new List<string>
        {
            report.Verdict,
            report.Detail()
        };
        var near = report.DerpLatency
            .OrderBy(d => d.Ms)
            .Take(3)
            .Select(d => $"{d.Code} {d.Ms:F0}ms");
        lines.Add($"最近中继：{string.Join(" · ", near)}");

        text.Text = string.Join('\n', lines);
        _report.AppendLine("[链路质量] " + string.Join(" / ", lines));

        ring.IsActive = false;
        ring.Visibility = Visibility.Collapsed;
    }

    // ══════════════════ 各区段 ══════════════════

    private FrameworkElement BuildStatusSection(TailscaleStatus? status)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(SectionHeader("本机状态"));

        if (status is null)
        {
            panel.Children.Add(Line("读不到 Tailscale 状态，可能服务没在运行。"));
            return Card(panel);
        }

        panel.Children.Add(Line($"状态：{status.DescribeState()}（{status.BackendState}）"));
        if (status.LoginName is { Length: > 0 } login) panel.Children.Add(Line($"账号：{login}"));
        if (status.TailnetName is { Length: > 0 } tailnet) panel.Children.Add(Line($"网络：{tailnet}"));
        panel.Children.Add(Line(status.Ipv4 is { Length: > 0 } ip ? $"联机地址：{ip}" : "联机地址：尚未分配"));
        if (status.HostName is { Length: > 0 } host) panel.Children.Add(Line($"设备名：{host}"));

        _report.AppendLine($"[状态] {status.BackendState} ip={status.Ipv4 ?? "-"} 账号={status.LoginName ?? "-"}");

        return Card(panel);
    }

    private FrameworkElement BuildPingSection()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(SectionHeader("测试与对方的连接"));
        panel.Children.Add(Line("填入对方的联机地址（100 开头），直接测两台电脑之间的链路。先确认你们在同一个虚拟网络里。"));

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _pingTargetBox.Width = 240;
        var button = new Button { Content = "开始测试", Padding = new Thickness(12, 6, 12, 6), CornerRadius = new CornerRadius(7) };
        button.Click += async (_, _) => await RunPingAsync(button);
        row.Children.Add(_pingTargetBox);
        row.Children.Add(button);
        panel.Children.Add(row);
        panel.Children.Add(_pingResult);

        _pingResult.Text = "";
        return Card(panel);
    }

    private async Task RunPingAsync(Button button)
    {
        var target = _pingTargetBox.Text.Trim();
        if (!GameTunnelInvite.IsValidAddress(target))
        {
            _pingResult.Text = "请填一个有效地址，例如 100.101.102.103";
            return;
        }

        button.IsEnabled = false;
        _pingResult.Text = "正在测试…";
        try
        {
            var result = TailscaleService.ParsePing(await TailscaleService.PingAsync(target));
            _pingResult.Text = result.Ok
                ? $"✓ {result.Summary}——{(result.Direct ? "点对点直连，延迟最理想" : "走加密中继，能玩但延迟会高一些")}"
                : $"✗ {result.Summary}";
            _report.AppendLine($"[ping] {target} => {_pingResult.Text}");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private FrameworkElement BuildHealthSection(IReadOnlyList<TailscaleHealthNote> notes)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(SectionHeader("Tailscale 提示"));
        foreach (var note in notes)
        {
            panel.Children.Add(Line($"· {note.Describe()}"));
            _report.AppendLine($"[health/{note.Code}] {note.Text}");
        }
        return Card(panel);
    }

    private FrameworkElement BuildPeersSection(IReadOnlyList<TailscalePeer> peers)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(SectionHeader($"同一网络里的设备（{peers.Count}）"));
        foreach (var peer in peers.Take(8))
        {
            var label = peer.Ipv4 is { Length: > 0 } ip
                ? $"{peer.HostName} · {ip} · {peer.Describe()}"
                : $"{peer.HostName} · {peer.Describe()}";
            panel.Children.Add(Line("· " + label));
        }
        if (peers.Count > 8) panel.Children.Add(Line($"…还有 {peers.Count - 8} 台"));

        var openButton = new HyperlinkButton { Padding = new Thickness(0, 4, 0, 0) };
        openButton.Content = "在控制台打开设备列表";
        openButton.Click += (_, _) => OpenUrl(TailscaleService.MachinesUrl);
        panel.Children.Add(openButton);

        return Card(panel);
    }

    private FrameworkElement BuildFirewallSection(string rules)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(SectionHeader("防火墙放行（仅 Tailscale 网卡）"));

        var hasRules = !string.IsNullOrWhiteSpace(rules);
        panel.Children.Add(Line(hasRules
            ? "本工具已为这些端口放行："
            : "本工具还没有添加过放行规则。"));

        if (hasRules)
        {
            foreach (var rule in rules.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                panel.Children.Add(Line("· " + rule.Trim()));
        }

        var button = new Button { Content = "清理本工具添加的规则", Padding = new Thickness(12, 6, 12, 6), CornerRadius = new CornerRadius(7) };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            await TailscaleService.RemoveAllFirewallRulesAsync();
            button.IsEnabled = true;
            await LoadAsync();
        };
        panel.Children.Add(button);

        return Card(panel);
    }

    private FrameworkElement BuildFooter()
    {
        var panel = new StackPanel { Spacing = 8 };

        var copyButton = new Button { Content = "复制诊断信息", Padding = new Thickness(12, 6, 12, 6), CornerRadius = new CornerRadius(7) };
        copyButton.Click += (_, _) =>
        {
            // 失败重试与诊断由 ClipboardService 统一负责（剪贴板被占用时不得崩溃）
            var copied = ClipboardService.TrySetText(_report.ToString());
            copyButton.Content = copied.Success ? "已复制" : "复制失败";
        };

        panel.Children.Add(copyButton);
        return panel;
    }

    // ══════════════════ 小工具 ══════════════════

    private InfoBar Info(string message, InfoBarSeverity severity) => new()
    {
        Message = message,
        Severity = severity,
        IsOpen = true
    };

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
