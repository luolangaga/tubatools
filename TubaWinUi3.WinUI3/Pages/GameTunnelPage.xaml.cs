using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TubaWinUi3.Pages;

/// <summary>
/// 游戏联机助手主页：联机组件状态 + 当前房间 + 和朋友连起来 + 我要玩的游戏。
/// 传输层是 EasyTier（无网卡、免登录），房间码里带着网络密钥。
/// </summary>
public sealed partial class GameTunnelPage : Page
{
    private readonly TunnelSession _session = TunnelSession.Instance;
    private DispatcherQueueTimer? _timer;
    private bool _refreshing;

    /// <summary>「我要玩的游戏」条只建一次（含 Logo 拉取），后续刷新不再重建。</summary>
    private bool _gamesLoaded;

    private bool _subscribed;

    public GameTunnelPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _session.Changed += OnSessionChanged;
            _subscribed = true;
        }

        await RefreshAsync();
        if (IsLoaded) StartTimer();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        _timer = null;
        if (_subscribed)
        {
            _session.Changed -= OnSessionChanged;
            _subscribed = false;
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Render();

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
            Render();
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

    private void Render()
    {
        RenderStatus();
        RenderPeers();
        UpdateCurrentInviteButton();
    }

    private void RenderStatus()
    {
        var ready = EasyTierPackage.IsReady;
        StopButton.Visibility = _session.IsActive ? Visibility.Visible : Visibility.Collapsed;
        TrayHint.Visibility = _session.IsActive ? Visibility.Visible : Visibility.Collapsed;

        if (_session.IsActive)
        {
            var isHost = _session.IsHost;
            StatusIcon.Glyph = _session.Stage == TunnelStage.Working ? "\uE9F5" : "\uE73E";
            StatusTitle.Text = _session.Stage == TunnelStage.Working
                ? (isHost ? "正在创建房间…" : "正在加入房间…")
                : (isHost ? "房间已开启" : "已加入房间");

            if (isHost)
            {
                StatusDetail.Text = _session.RoomCode is { Length: > 0 } code
                    ? $"房间码 {code} · 端口 {_session.GamePort}。把房间码发给朋友，粘贴后直接进同一个虚拟网络。"
                    : $"端口 {_session.GamePort}。";
                MyAddressText.Text = _session.RoomCode ?? "";
            }
            else
            {
                StatusDetail.Text = _session.LocalPort > 0
                    ? $"本机地址 127.0.0.1:{_session.LocalPort} → 房主 {_session.HostAddress}:{_session.GamePort}"
                    : "正在建立端口转发…";
                MyAddressText.Text = _session.LocalPort > 0
                    ? $"127.0.0.1:{_session.LocalPort}"
                    : "正在准备…";
            }

            AddressRow.Visibility = Visibility.Visible;
            CopyAddressButton.Visibility = Visibility.Visible;
            LiveBadge.Visibility = _session.Stage == TunnelStage.Ready ? Visibility.Visible : Visibility.Collapsed;
            PrepareButtonText.Text = "环境详情";
            RenderHint();
            return;
        }

        AddressRow.Visibility = Visibility.Collapsed;
        LiveBadge.Visibility = Visibility.Collapsed;

        if (_session.Stage == TunnelStage.Failed)
        {
            StatusIcon.Glyph = "\uE783";
            StatusTitle.Text = "联机没有成功";
            StatusDetail.Text = _session.Error ?? "请重试，或点「网络检测」查看原因。";
            PrepareButtonText.Text = ready ? "查看联机组件" : "准备联机组件";
            RenderHint();
            return;
        }

        if (!ready)
        {
            StatusIcon.Glyph = "\uE896";
            StatusTitle.Text = "第一次使用：先准备联机组件";
            StatusDetail.Text = "需要下载 EasyTier 联机组件（约 30MB，仅一次）。不需要注册账号，也不需要登录任何服务。";
            PrepareButtonText.Text = "准备联机组件";
            RenderHint();
            return;
        }

        StatusIcon.Glyph = "\uE73E";
        StatusTitle.Text = "联机环境已就绪";
        StatusDetail.Text = $"联机组件 {EasyTierPackage.Version} 已就绪。建房或加入时才会启动虚拟网络。";
        PrepareButtonText.Text = "环境详情";
        RenderHint();
    }

    private void RenderHint()
    {
        if (_session.Warning is { Length: > 0 } warning)
        {
            HealthBar.Severity = InfoBarSeverity.Warning;
            HealthBar.Title = "需要注意";
            HealthBar.Message = warning;
            HealthBar.IsOpen = true;
            return;
        }

        if (_session.Stage == TunnelStage.Failed && _session.Error is { Length: > 0 } error)
        {
            HealthBar.Severity = InfoBarSeverity.Error;
            HealthBar.Title = "联机失败";
            HealthBar.Message = error;
            HealthBar.IsOpen = true;
            return;
        }

        HealthBar.IsOpen = false;
    }

    /// <summary>房间里的其他设备，以及连接质量。</summary>
    private void RenderPeers()
    {
        if (!_session.IsActive)
        {
            PeerLine.Visibility = Visibility.Collapsed;
            return;
        }

        var peers = _session.Peers.Where(p => p.IsMember && !p.IsLocal).ToList();
        if (peers.Count == 0)
        {
            PeerLine.Text = _session.IsHost
                ? "还没有别的设备加入——把房间码发给朋友"
                : "还没有连上房主——首次打洞通常需要几秒";
            PeerLine.Visibility = Visibility.Visible;
            return;
        }

        var names = string.Join(" · ", peers.Take(4).Select(p => $"{p.Hostname}（{p.Describe()}）"));
        var total = peers.Count > 4 ? $"{names} 等 {peers.Count} 台设备" : names;
        var extra = _session.IsHost
            ? $" · 直连 {_session.DirectPeers} / 中转 {_session.RelayedPeers}"
            : _session.HostPeer?.LatencyMs is { } ms ? $" · 房主延迟 {ms:F0}ms" : "";
        PeerLine.Text = $"同一虚拟网络：{total}{extra}";
        PeerLine.Visibility = Visibility.Visible;
    }

    // ══════════════════ 主按钮 ══════════════════

    private async void Prepare_Click(object sender, RoutedEventArgs e) => await ShowSetupAsync();

    private async Task<bool> ShowSetupAsync()
    {
        try
        {
            var dialog = new GameTunnelSetupDialog(XamlRoot);
            var ready = await dialog.RunAsync();
            Render();
            return ready;
        }
        catch (Exception ex)
        {
            ShowError("准备环境失败", ex);
            return false;
        }
    }

    private async void StartHost_Click(object sender, RoutedEventArgs e) => await StartHostAsync();

    /// <summary>主机流程：先确保组件可用，再打开向导（可带上主页选好的游戏）。</summary>
    private async Task StartHostAsync(string? presetId = null)
    {
        try
        {
            if (!await EnsureReadyAsync()) return;

            var dialog = new GameTunnelHostDialog(XamlRoot, presetId);
            await dialog.RunAsync();
            Render();
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
            // 加入流程会自己处理「组件没装」的情况，所以这里不强制先准备环境
            var dialog = new GameTunnelJoinDialog(XamlRoot);
            await dialog.RunAsync();
            Render();
        }
        catch (Exception ex)
        {
            ShowError("加入失败", ex);
        }
    }

    /// <summary>建房前必须组件可用。</summary>
    private async Task<bool> EnsureReadyAsync()
    {
        if (EasyTierPackage.IsReady) return true;
        return await ShowSetupAsync();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _session.Stop();
        Render();
        ShowInfo("联机已停止");
    }

    /// <summary>本次运行已经建过房间时，露出「查看当前房间与邀请」入口。</summary>
    private void UpdateCurrentInviteButton()
    {
        var show = _session.IsActive && _session.IsHost && _session.Invite is not null;
        CurrentInviteButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>回到「我来当主机」的第 3 步：房间码、邀请文案、一键加入脚本都在那一页。</summary>
    private async void ShowCurrentInvite_Click(object sender, RoutedEventArgs e)
    {
        if (!_session.IsActive || _session.Invite is null) return;

        SetCurrentInviteBusy(true);
        try
        {
            var dialog = new GameTunnelHostDialog(XamlRoot, presetId: null, resumeInvite: true);
            await dialog.RunAsync();
            Render();
        }
        catch (Exception ex)
        {
            ShowError("打不开房间信息", ex);
        }
        finally
        {
            SetCurrentInviteBusy(false);
        }
    }

    private void SetCurrentInviteBusy(bool busy)
    {
        CurrentInviteButton.IsEnabled = !busy;
        CurrentInviteRing.IsActive = busy;
        CurrentInviteRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CurrentInviteIcon.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        CurrentInviteText.Text = busy ? "正在准备房间信息…" : "查看当前房间与邀请";
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new GameTunnelSettingsDialog(XamlRoot);
            await dialog.ShowAsync();
            Render();
        }
        catch (Exception ex)
        {
            ShowError("打不开联机设置", ex);
        }
    }

    private async void Diagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new GameTunnelDiagnosticsDialog(XamlRoot);
            await dialog.ShowAsync();
            Render();
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
        var text = _session.IsHost
            ? _session.RoomCode
            : _session.LocalPort > 0 ? $"127.0.0.1:{_session.LocalPort}" : null;

        if (string.IsNullOrWhiteSpace(text))
        {
            ShowError("现在没有可复制的内容", null);
            return;
        }
        CopyText(text, _session.IsHost ? "房间码已复制" : "本机地址已复制");
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
