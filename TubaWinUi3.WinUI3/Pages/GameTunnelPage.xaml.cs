using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TubaWinUi3.Pages;

/// <summary>
/// 游戏联机助手主页：联机环境（我的虚拟网络地址 + 网里有谁）+ 和朋友连起来 + 我要玩的游戏 + 最近联机。
/// 所有细节都收进弹窗里，主页只回答「现在能不能玩、点哪里」。
/// </summary>
public sealed partial class GameTunnelPage : Page
{
    private DispatcherQueueTimer? _timer;
    private TailscaleStatus? _status;
    private bool _refreshing;

    /// <summary>「我要玩的游戏」条只建一次（含 Logo 拉取），后续刷新不再重建。</summary>
    private bool _gamesLoaded;

    /// <summary>自动拉起托盘客户端的冷却：避免用户有意退出它时被反复重启。</summary>
    private DateTimeOffset _lastTrayAutoStart = DateTimeOffset.MinValue;
    private static readonly TimeSpan TrayAutoStartCooldown = TimeSpan.FromSeconds(30);

    /// <summary>本次进入页面是否已经尝试过自动连接（只试一次，尊重用户主动断开的意图）。</summary>
    private bool _autoConnectTried;

    public GameTunnelPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        if (IsLoaded) StartTimer();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        _timer = null;
    }

    private void StartTimer()
    {
        _timer ??= DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(5);
        _timer.Tick -= Timer_Tick;
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private async void Timer_Tick(DispatcherQueueTimer sender, object args) => await RefreshAsync();

    // ══════════════════ 状态刷新 ══════════════════

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            TailscaleCli.Invalidate();
            await EnsureTrayAsync();
            _status = TailscaleService.IsInstalled ? await TailscaleService.GetStatusAsync() : null;
            await AutoConnectIfNeededAsync();
            RenderStatus();
            RenderPeers();
            RenderHealth();
            UpdateCurrentInviteButton();
            await EnsureGamesAsync();
        }
        catch
        {
            // 状态刷新失败不应影响页面可用性
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>
    /// 托盘客户端是这个功能的必要条件（不是可选的界面）：没有它，tailscaled 只在命令行
    /// 临时连上的几十毫秒里连接，命令一退出就断开。所以只要能确定它没在跑就自动拉起来，
    /// 绝不要求用户手动去点一下。
    /// </summary>
    private async Task EnsureTrayAsync()
    {
        if (!TailscaleService.IsInstalled || TailscaleService.IsTrayRunning) return;
        if (DateTimeOffset.UtcNow - _lastTrayAutoStart < TrayAutoStartCooldown) return;

        _lastTrayAutoStart = DateTimeOffset.UtcNow;
        await TailscaleService.EnsureTrayRunningAsync(TimeSpan.FromSeconds(6));
    }

    /// <summary>
    /// 已登录但处于「未连接」时自动连上：用户打开本页就是要联机，不该再点一次才动。
    /// 每次进入页面只自动尝试一次，避免覆盖用户主动 Disconnect 的意图。
    /// </summary>
    private async Task AutoConnectIfNeededAsync()
    {
        if (_autoConnectTried || _status is null) return;
        _autoConnectTried = true;

        if (!_status.IsLoggedIn || _status.IsReady || _status.IsStarting) return;

        var result = await TailscaleService.BringUpAsync();
        if (!result.Ok) return;

        await TailscaleService.WaitForReadyAsync(TimeSpan.FromSeconds(15));
        _status = await TailscaleService.GetStatusAsync();
    }

    private void RenderStatus()
    {
        var installed = TailscaleService.IsInstalled;

        // 只有联机环境可用时才提示「怎么关掉它」
        TrayHint.Visibility = Visibility.Collapsed;

        if (!installed)
        {
            StatusIcon.Glyph = "\uE896";
            StatusTitle.Text = "第一次使用：先准备联机环境";
            StatusDetail.Text = "需要安装 Tailscale 客户端并登录一次（免费）。点右边的按钮，全程大约一分钟。";
            PrepareButtonText.Text = "准备联机环境";
            AddressRow.Visibility = Visibility.Collapsed;
            LiveBadge.Visibility = Visibility.Collapsed;
            return;
        }

        if (_status is null)
        {
            StatusIcon.Glyph = "\uE9F5";
            StatusTitle.Text = "已安装 Tailscale，正在读取状态…";
            StatusDetail.Text = "如果一直停在这里，点「网络检测」看看。";
            PrepareButtonText.Text = "检查环境";
            AddressRow.Visibility = Visibility.Collapsed;
            LiveBadge.Visibility = Visibility.Collapsed;
            return;
        }

        if (_status.IsReady)
        {
            StatusIcon.Glyph = "\uE73E";
            StatusTitle.Text = "联机环境已就绪";
            var who = _status.LoginName is { Length: > 0 } login ? $"已登录 {login}" : "已登录";
            StatusDetail.Text = $"{who} · 这是你在虚拟网络里的固定地址，朋友在游戏里填它来连你。";
            PrepareButtonText.Text = "环境详情";
            AddressRow.Visibility = Visibility.Visible;
            MyAddressText.Text = _status.Ipv4;
            TrayHint.Visibility = Visibility.Visible;

            // 「游戏正在监听端口」用上次选的游戏来判断（设置里就存着，不需要额外记录）
            var settings = GameTunnelCatalog.LoadSettings();
            var lastGame = GameTunnelCatalog.FindPreset(settings.LastPresetId);
            var port = lastGame?.DefaultPort ?? settings.LastPort;
            var protocol = lastGame?.Protocol ?? GameTunnelProtocol.Tcp;
            var listening = port > 0 && GameTunnelProbe.Check(port, protocol).Listening;
            LiveBadge.Visibility = listening ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        if (_status.IsLoggedIn)
        {
            StatusIcon.Glyph = "\uE7BA";
            StatusTitle.Text = "Tailscale 已登录，但当前没有连接";
            StatusDetail.Text = "点右边的按钮连接一下，连上就有联机地址了。";
            PrepareButtonText.Text = "连接";
            AddressRow.Visibility = Visibility.Collapsed;
            LiveBadge.Visibility = Visibility.Collapsed;
            return;
        }

        if (_status.IsStarting)
        {
            // NoState / Starting：后端还在启动或卡住了，这**不是**「未登录」，
            // 不能把用户推去重复登录（浏览器里授权过也不会有效果）
            StatusIcon.Glyph = "\uE9F5";
            if (!TailscaleService.IsTrayRunning)
            {
                // 托盘没在跑时本页会自动拉起它（见 EnsureTrayAsync），所以这里说「正在启动」而不是报错
                StatusTitle.Text = "正在自动启动 Tailscale 客户端…";
                StatusDetail.Text = "它必须常驻后台，否则刚建立的连接会被立刻断开。本页会自动把它拉起来；如果一直停在这里，点右边手动启动一次。";
                PrepareButtonText.Text = "手动启动";
            }
            else
            {
                StatusTitle.Text = _status.HaveNodeKey ? "本机已授权过，正在等 Tailscale 后端就绪" : "Tailscale 正在启动";
                StatusDetail.Text = "如果一直停在这里，通常是本机连不上 controlplane.tailscale.com（代理、加速器或 DNS 污染）。点右边可以查看原因并重启服务。";
                PrepareButtonText.Text = "查看原因";
            }
            AddressRow.Visibility = Visibility.Collapsed;
            LiveBadge.Visibility = Visibility.Collapsed;
            return;
        }

        StatusIcon.Glyph = "\uE7BA";
        StatusTitle.Text = "还没登录 Tailscale";
        StatusDetail.Text = "登录一次即可（浏览器里点一下授权）。登录后本机就有一个固定的联机地址。";
        PrepareButtonText.Text = "去登录";
        AddressRow.Visibility = Visibility.Collapsed;
        LiveBadge.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 状态卡里的「同一虚拟网络里还有谁」——数据来自同一次 status --json 的解析，不额外起进程。
    /// </summary>
    private void RenderPeers()
    {
        if (_status?.IsReady != true)
        {
            PeerLine.Visibility = Visibility.Collapsed;
            return;
        }

        var peers = _status.Peers;
        if (peers.Count == 0)
        {
            PeerLine.Text = "还没有别的设备加入这个网络——把下面的邀请发给朋友";
        }
        else
        {
            var names = string.Join(" · ", peers.Take(4).Select(p => $"{p.DisplayName ?? p.HostName}（{p.Describe()}）"));
            PeerLine.Text = peers.Count > 4
                ? $"同一虚拟网络：{names} 等 {peers.Count} 台设备"
                : $"同一虚拟网络：{names}";
        }

        PeerLine.Visibility = Visibility.Visible;
    }

    private void RenderHealth()
    {
        var notes = TailscaleService.DescribeHealthLines(_status?.Health);
        var blocking = notes.Where(n => n.ImpactsConnectivity).ToList();
        var reminder = notes.Where(n => !n.ImpactsConnectivity && !n.Transient).ToList();

        // 只有真正影响连通性的问题才报警，其余放进详情里
        var show = blocking.Count > 0 ? blocking : reminder;
        if (show.Count == 0)
        {
            HealthBar.IsOpen = false;
            return;
        }

        HealthBar.Severity = blocking.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        HealthBar.Title = blocking.Count > 0 ? "联机可能受影响" : "Tailscale 提示";
        HealthBar.Message = string.Join("；", show.Select(n => n.Describe()));
        HealthBar.IsOpen = true;
    }

    // ══════════════════ 主按钮 ══════════════════

    private async void Prepare_Click(object sender, RoutedEventArgs e)
    {
        // 已就绪时这个按钮变成「环境详情」，打开同样的向导看状态
        await ShowSetupAsync();
    }

    private async Task<bool> ShowSetupAsync()
    {
        try
        {
            var dialog = new GameTunnelSetupDialog(XamlRoot);
            var ready = await dialog.RunAsync();
            await RefreshAsync();
            return ready;
        }
        catch (Exception ex)
        {
            ShowError("准备环境失败", ex);
            return false;
        }
    }

    private async void StartHost_Click(object sender, RoutedEventArgs e) => await StartHostAsync();

    /// <summary>主机流程：先确保环境可用，再打开向导（可带上主页选好的游戏）。</summary>
    private async Task StartHostAsync(string? presetId = null)
    {
        try
        {
            if (!await EnsureReadyAsync()) return;

            var dialog = new GameTunnelHostDialog(XamlRoot, presetId);
            await dialog.RunAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError("打开主机向导失败", ex);
        }
    }

    /// <summary>游戏卡：直接进主机向导，并预选这个游戏。</summary>
    private async void GameChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TunnelGameCard chip }) return;
        await StartHostAsync(chip.IsAddCard ? null : chip.Id);
    }

    /// <summary>「我要玩的游戏」条只建一次；Logo 与主机向导共用同一份缓存。</summary>
    private async Task EnsureGamesAsync()
    {
        if (_gamesLoaded) return;
        _gamesLoaded = true;

        var cards = GameTunnelCatalog.Presets.Select(TunnelGameCard.FromPreset).ToList();
        cards.Add(TunnelGameCard.AddCard());
        GamesList.ItemsSource = cards;

        await Task.WhenAll(cards.Select(card => card.LoadLogoAsync()));
    }

    private async void StartJoin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 加入流程会自己处理「没装客户端」的情况，所以这里不强制先准备环境
            var dialog = new GameTunnelJoinDialog(XamlRoot);
            await dialog.RunAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError("加入失败", ex);
        }
    }

    /// <summary>需要联机地址的操作（当主机）必须先具备可用环境。</summary>
    private async Task<bool> EnsureReadyAsync()
    {
        _status = TailscaleService.IsInstalled ? await TailscaleService.GetStatusAsync() : null;
        if (_status?.IsReady == true) return true;
        return await ShowSetupAsync();
    }

    /// <summary>本次运行已经建过邀请时，露出「查看当前邀请信息」入口。</summary>
    private void UpdateCurrentInviteButton()
        => CurrentInviteButton.Visibility = GameTunnelCatalog.CurrentInvite is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>回到「我来当主机」的第 3 步：地址、邀请码、一键加入脚本都在那一页。</summary>
    private async void ShowCurrentInvite_Click(object sender, RoutedEventArgs e)
    {
        if (GameTunnelCatalog.CurrentInvite is null) return;

        SetCurrentInviteBusy(true);
        try
        {
            if (!await EnsureReadyAsync()) return;

            var dialog = new GameTunnelHostDialog(XamlRoot, presetId: null, resumeInvite: true);
            await dialog.RunAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError("打不开邀请信息", ex);
        }
        finally
        {
            SetCurrentInviteBusy(false);
        }
    }

    /// <summary>准备邀请信息要读状态、放行防火墙、生成密钥，会卡一小会儿：按钮转圈并禁掉，避免连点。</summary>
    private void SetCurrentInviteBusy(bool busy)
    {
        CurrentInviteButton.IsEnabled = !busy;
        CurrentInviteRing.IsActive = busy;
        CurrentInviteRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CurrentInviteIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        CurrentInviteText.Text = busy ? "正在准备邀请信息…" : "查看当前邀请信息";
    }

    private async void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new GameTunnelDiagnosticsDialog(XamlRoot);
            await dialog.ShowAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowError("网络检测失败", ex);
        }
    }

    private async void Help_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new GameTunnelGuideDialog(XamlRoot);
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            ShowError("打不开教程", ex);
        }
    }

    private void CopyAddress_Click(object sender, RoutedEventArgs e)
    {
        if (_status?.Ipv4 is { Length: > 0 } ip) CopyText(ip, "联机地址已复制");
    }

    private void CopyText(string text, string message)
    {
        // 失败重试与诊断由 ClipboardService 统一负责（剪贴板被占用时不得崩溃）
        if (ClipboardService.TrySetText(text).Success)
            ShowInfo(message);
        else
            ShowError("复制失败，请手动选中复制", null);
    }

    private void ShowInfo(string message)
    {
        HealthBar.Severity = InfoBarSeverity.Success;
        HealthBar.Title = "";
        HealthBar.Message = message;
        HealthBar.IsOpen = false;
        HealthBar.IsOpen = true;
    }

    private void ShowError(string title, Exception? ex)
    {
        HealthBar.Severity = InfoBarSeverity.Error;
        HealthBar.Title = title;
        HealthBar.Message = ex?.Message ?? "操作没有完成，请重试";
        HealthBar.IsOpen = false;
        HealthBar.IsOpen = true;
    }
}
