using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public enum TunnelRole
{
    None,
    Host,
    Guest
}

public enum TunnelStage
{
    Idle,
    Working,
    Ready,
    Failed
}

public enum TunnelStep
{
    Component,
    World,
    Network,
    FindHost,
    Enter
}

public enum TunnelStepState
{
    Pending,
    Active,
    Done
}

/// <summary>
/// 一个联机会话（建房或加入），进程内唯一：离开页面后房间继续运行，
/// 直到用户点「停止联机」或退出程序。
/// </summary>
/// <remarks>
/// 传输层是 EasyTier 的无网卡模式（no_tun）：房主把自己的固定虚拟地址与游戏端口写进网络配置，
/// 访客加入同一网络后，通过端口转发把游戏端口接到本机。邀请码本身编码了网络名与密钥，
/// 因此不需要账号、登录或任何在线服务。
/// </remarks>
public sealed class TunnelSession
{
    /// <summary>房主主机名前缀，后面跟游戏端口；访客靠它找到房主。</summary>
    public const string HostNamePrefix = "tuba-host-";

    /// <summary>访客主机名前缀。</summary>
    public const string GuestNamePrefix = "tuba-guest-";

    private static readonly TimeSpan DirectGracePeriod = TimeSpan.FromSeconds(8);

    private static readonly Lazy<TunnelSession> LazyInstance = new(() => new TunnelSession());

    public static TunnelSession Instance => LazyInstance.Value;

    private readonly Dictionary<TunnelStep, TunnelStepState> _steps = [];
    private SynchronizationContext? _ui;
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;
    private CancellationTokenSource? _run;

    private EasyTierInstance? _network;
    private LanWorldAnnouncer? _announcer;
    private InviteInfo? _invite;
    private int _gamePort;
    private int _localPort;
    private int _gameMissingTicks;
    private volatile string? _warning;

    private TunnelSession()
    {
    }

    public event EventHandler? Changed;

    public TunnelRole Role { get; private set; }

    public TunnelStage Stage { get; private set; }

    public bool IsActive => Stage is TunnelStage.Working or TunnelStage.Ready;

    public bool IsHost => Role == TunnelRole.Host;

    public bool IsGuest => Role == TunnelRole.Guest;

    public string? RoomCode => _invite?.RoomCode;

    public InviteInfo? Invite => _invite;

    public string GameName => _invite?.Game ?? "";

    public GameTunnelProtocol Protocol => _invite?.Protocol ?? GameTunnelProtocol.Tcp;

    /// <summary>房主的游戏端口（访客加入后也是房主那个端口）。</summary>
    public int GamePort => _gamePort;

    /// <summary>访客：本机转发端口，游戏里填 127.0.0.1:这个端口。</summary>
    public int LocalPort => _localPort;

    /// <summary>访客：房主的虚拟地址（10.144.144.1）。</summary>
    public string HostAddress => _invite?.Host ?? InviteInfo.HostAddress;

    public IReadOnlyList<EasyTierPeer> Peers { get; private set; } = [];

    public EasyTierPeer? HostPeer { get; private set; }

    public int DirectPeers { get; private set; }

    public int RelayedPeers { get; private set; }

    /// <summary>当前连上的发现节点数；-1 表示还没查过。</summary>
    public int ConnectedNodes { get; private set; } = -1;

    public NatKind LocalNat { get; private set; } = NatKind.Unknown;

    public TimeSpan SearchTime { get; private set; }

    public string? Error { get; private set; }

    /// <summary>会话还能用，但有值得提醒的情况（例如游戏可能已经关了）。</summary>
    public string? Warning => _warning;

    public int DownloadPercent { get; private set; }

    public string? DownloadText { get; private set; }

    public string? FirewallMessage { get; private set; }

    public TunnelStepState GetStepState(TunnelStep step)
        => _steps.TryGetValue(step, out var state) ? state : TunnelStepState.Pending;

    public IReadOnlyList<TunnelStep> Steps => Role == TunnelRole.Host
        ? [TunnelStep.Component, TunnelStep.World, TunnelStep.Network]
        : [TunnelStep.Component, TunnelStep.Network, TunnelStep.FindHost, TunnelStep.Enter];

    // ══════════════════════ 建房 ══════════════════════

    public async Task StartHostAsync(string gameName, int port, GameTunnelProtocol protocol, bool usesLanDiscovery)
    {
        if (IsActive)
            throw new InvalidOperationException("已经有一个联机在运行，请先在主页点「停止联机」。");

        var run = Begin(TunnelRole.Host);
        var token = run.Token;
        try
        {
            if (!GameTunnelCatalog.IsValidPort(port))
                throw new InvalidOperationException("游戏端口不合法（应在 1-65535 之间）");

            SetStep(TunnelStep.Component, TunnelStepState.Active);
            await EnsureComponentAsync(token).ConfigureAwait(true);
            SetStep(TunnelStep.Component, TunnelStepState.Done);

            var settings = GameTunnelCatalog.LoadSettings();
            if (settings.AutoFirewall)
            {
                var firewall = await GameTunnelFirewall.EnsureComponentRuleAsync(EasyTierPackage.CorePath, token)
                    .ConfigureAwait(true);
                FirewallMessage = firewall.Ok
                    ? $"防火墙：{firewall.Message}"
                    : $"防火墙：{firewall.Message}（朋友连不上时可手动放行 easytier-core.exe）";
                await GameTunnelFirewall.CleanLegacyOnceAsync(token).ConfigureAwait(true);
                OnChanged();
            }

            SetStep(TunnelStep.World, TunnelStepState.Active);
            var probe = GameTunnelProbe.Check(port, protocol);
            if (!probe.Listening)
                TunnelLog.Warn($"建房时端口 {port} 还没有程序监听：{probe.Message}");
            _gamePort = port;
            SetStep(TunnelStep.World, TunnelStepState.Done);

            SetStep(TunnelStep.Network, TunnelStepState.Active);
            var code = TunnelRoomCode.Create();
            _invite = new InviteInfo
            {
                RoomCode = code.Code,
                NetworkName = code.NetworkName,
                NetworkSecret = code.NetworkSecret,
                Port = port,
                Game = gameName,
                Protocol = protocol,
                UsesLanDiscovery = usesLanDiscovery
            };
            GameTunnelCatalog.CurrentInvite = _invite;

            var config = new EasyTierConfig
            {
                NetworkName = code.NetworkName,
                NetworkSecret = code.NetworkSecret,
                Hostname = HostNamePrefix + port,
                IPv4 = InviteInfo.HostAddress,
                DirectOnly = !settings.AllowRelay,
                HideAddress = settings.HideAddress
            };
            config.Peers.AddRange(GameTunnelCatalog.GetNodes(settings));
            if (protocol.UsesTcp()) config.TcpWhitelist.Add(port.ToString());
            if (protocol.UsesUdp()) config.UdpWhitelist.Add(port.ToString());

            await StartNetworkAsync(config, run).ConfigureAwait(true);

            // 给发现节点一点时间，房间码发出去就能用。
            for (var i = 0; i < 10 && ConnectedNodes <= 0; i++)
            {
                ConnectedNodes = await _network!.CountConnectedNodesAsync(token).ConfigureAwait(true);
                if (ConnectedNodes <= 0) await Task.Delay(1000, token).ConfigureAwait(true);
            }

            SetStep(TunnelStep.Network, TunnelStepState.Done);
            Stage = TunnelStage.Ready;
            TunnelLog.Info($"房间已开启（端口 {port}，{ConnectedNodes} 个节点已连接）");
            OnChanged();

            _ = MonitorHostAsync(run);
        }
        catch (Exception ex)
        {
            HandleFailure(run, ex);
            throw;
        }
    }

    private async Task MonitorHostAsync(CancellationTokenSource run)
    {
        var token = run.Token;
        var tick = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(2000, token).ConfigureAwait(false);
                tick++;

                var peers = await _network!.GetPeersAsync(token).ConfigureAwait(false);
                ApplyPeers(peers);

                if (tick % 3 == 0)
                {
                    ConnectedNodes = await _network.CountConnectedNodesAsync(token).ConfigureAwait(false);

                    if (GameTunnelProbe.Check(_gamePort, Protocol).Listening)
                    {
                        _gameMissingTicks = 0;
                        if (_warning != null) _warning = null;
                    }
                    else if (++_gameMissingTicks >= 3)
                    {
                        _warning = $"端口 {_gamePort} 上已经没有程序在监听——游戏可能已经关闭或退出了世界。";
                    }
                }

                Post(OnChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Post(() => HandleFailure(run, ex));
        }
    }

    // ══════════════════════ 加入 ══════════════════════

    public async Task StartJoinAsync(InviteInfo invite)
    {
        if (invite is null || string.IsNullOrWhiteSpace(invite.NetworkName))
            throw new InvalidOperationException("邀请码内容不完整，请让对方重新发一次");
        if (IsActive)
            throw new InvalidOperationException("已经有一个联机在运行，请先在主页点「停止联机」。");

        var run = Begin(TunnelRole.Guest);
        var token = run.Token;
        _invite = invite;
        GameTunnelCatalog.CurrentInvite = invite;
        try
        {
            SetStep(TunnelStep.Component, TunnelStepState.Active);
            await EnsureComponentAsync(token).ConfigureAwait(true);
            SetStep(TunnelStep.Component, TunnelStepState.Done);

            var settings = GameTunnelCatalog.LoadSettings();
            if (settings.AutoFirewall)
            {
                var firewall = await GameTunnelFirewall.EnsureComponentRuleAsync(EasyTierPackage.CorePath, token)
                    .ConfigureAwait(true);
                FirewallMessage = firewall.Ok ? $"防火墙：{firewall.Message}" : $"防火墙：{firewall.Message}";
                await GameTunnelFirewall.CleanLegacyOnceAsync(token).ConfigureAwait(true);
            }

            SetStep(TunnelStep.Network, TunnelStepState.Active);
            var config = new EasyTierConfig
            {
                NetworkName = invite.NetworkName,
                NetworkSecret = invite.NetworkSecret,
                Hostname = GuestNamePrefix + RandomHex(4),
                IPv4 = null,
                DirectOnly = !settings.AllowRelay,
                HideAddress = settings.HideAddress
            };
            config.Peers.AddRange(GameTunnelCatalog.GetNodes(settings));
            // 加入者不向任何人开放入站端口。
            config.TcpWhitelist.Add("0");
            config.UdpWhitelist.Add("0");

            await StartNetworkAsync(config, run).ConfigureAwait(true);
            SetStep(TunnelStep.Network, TunnelStepState.Done);

            SetStep(TunnelStep.FindHost, TunnelStepState.Active);
            var (host, hostPort) = await FindHostAsync(settings, token).ConfigureAwait(true);
            HostPeer = host;
            _gamePort = hostPort;
            SetStep(TunnelStep.FindHost, TunnelStepState.Done);

            SetStep(TunnelStep.Enter, TunnelStepState.Active);
            var hostAddress = IPAddress.Parse(string.IsNullOrWhiteSpace(host.IPv4) ? InviteInfo.HostAddress : host.IPv4);
            var target = new IPEndPoint(hostAddress, hostPort);
            var shared = settings.ShareWithLan;
            var bindAddress = shared ? IPAddress.Any : IPAddress.Loopback;
            var localPort = ChooseLocalPort(settings, hostPort, bindAddress);

            if (!await _network!.AddPortForwardAsync("tcp", new IPEndPoint(bindAddress, localPort), target, token)
                .ConfigureAwait(true))
            {
                throw new InvalidOperationException("端口转发建立失败，请检查安全软件是否拦截了联机组件。");
            }
            if (Protocol.UsesUdp())
            {
                await _network.AddPortForwardAsync("udp", new IPEndPoint(bindAddress, localPort), target, token)
                    .ConfigureAwait(true);
            }

            _localPort = localPort;

            var reachable = false;
            for (var i = 0; i < 6 && !reachable; i++)
            {
                reachable = await GameTunnelProbe.CanConnectAsync(localPort, TimeSpan.FromSeconds(4), token)
                    .ConfigureAwait(true);
                if (!reachable) await Task.Delay(1000, token).ConfigureAwait(true);
            }
            if (!reachable)
                TunnelLog.Warn("房主的游戏暂时没有通过转发响应；先让用户进游戏试试。");

            if (invite.UsesLanDiscovery && Protocol.UsesTcp())
            {
                var name = $"图吧工具箱 · {(string.IsNullOrWhiteSpace(invite.Game) ? "联机房间" : invite.Game)}";
                _announcer = new LanWorldAnnouncer(() => name, localPort, localOnly: !shared);
            }

            SetStep(TunnelStep.Enter, TunnelStepState.Done);
            Stage = TunnelStage.Ready;
            TunnelLog.Info($"已加入房间（房主端口 {hostPort}，本地端口 {localPort}）");
            OnChanged();

            _ = MonitorGuestAsync(run);
        }
        catch (Exception ex)
        {
            HandleFailure(run, ex);
            throw;
        }
    }

    /// <summary>找房主：hostname 里带着游戏端口，优先等直连，超时后再走中转。</summary>
    private async Task<(EasyTierPeer Host, int Port)> FindHostAsync(
        GameTunnelSettings settings, CancellationToken token)
    {
        var started = DateTime.UtcNow;
        DateTime? firstSeen = null;

        while (true)
        {
            SearchTime = DateTime.UtcNow - started;
            var peers = await _network!.GetPeersAsync(token).ConfigureAwait(true);
            ApplyPeers(peers);

            var host = peers.FirstOrDefault(IsRoomHost);
            if (host is not null)
            {
                HostPeer = host;
                firstSeen ??= DateTime.UtcNow;
                var parsedPort = PortOfHost(host);
                var port = _invite?.Port > 0 ? _invite!.Port : parsedPort;

                // 直连优先：打洞通常要几秒。隐藏地址时永远没有直连，立刻走中转；
                // 不允许中转时继续等直连，直到超时。
                var acceptRelay = settings.HideAddress
                                  || (settings.AllowRelay && DateTime.UtcNow - firstSeen > DirectGracePeriod);
                if (port > 0 && (host.IsDirect || acceptRelay))
                {
                    TunnelLog.Info($"找到房主 {host.Hostname}（{(host.IsDirect ? "直连" : "中转")}，{host.Tunnel}）");
                    return (host, port);
                }
            }

            if (SearchTime.TotalSeconds > settings.SearchTimeoutSeconds)
            {
                if (host is not null)
                    throw new InvalidOperationException(
                        "找到了房间，但一直没能建立连接。可以让对方在设置里允许中转，或换网络条件更好的一方当房主。");
                if (ConnectedNodes == 0)
                    throw new InvalidOperationException("没能连上任何发现节点，请检查网络或代理设置。");
                throw new InvalidOperationException("房间里没有找到房主——邀请码可能属于另一个房间，或对方已经关闭了房间。");
            }

            if (SearchTime.TotalSeconds > 10 && (int)SearchTime.TotalSeconds % 5 < 2)
                ConnectedNodes = await _network.CountConnectedNodesAsync(token).ConfigureAwait(true);

            OnChanged();
            await Task.Delay(1500, token).ConfigureAwait(true);
        }
    }

    private static bool IsRoomHost(EasyTierPeer peer)
        => !peer.IsLocal && peer.IsMember
           && peer.Hostname.StartsWith(HostNamePrefix, StringComparison.Ordinal)
           && PortOfHost(peer) is > 0 and < 65536;

    internal static int PortOfHost(EasyTierPeer peer)
    {
        if (!peer.Hostname.StartsWith(HostNamePrefix, StringComparison.Ordinal)) return 0;
        return int.TryParse(peer.Hostname[HostNamePrefix.Length..], out var port) ? port : 0;
    }

    private async Task MonitorGuestAsync(CancellationTokenSource run)
    {
        var token = run.Token;
        var tick = 0;
        var failures = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(2000, token).ConfigureAwait(false);
                tick++;

                var peers = await _network!.GetPeersAsync(token).ConfigureAwait(false);
                ApplyPeers(peers);
                var host = peers.FirstOrDefault(IsRoomHost);
                if (host is not null) HostPeer = host;

                if (tick % 5 == 0)
                {
                    ConnectedNodes = await _network.CountConnectedNodesAsync(token).ConfigureAwait(false);
                    if (await GameTunnelProbe.CanConnectAsync(_localPort, TimeSpan.FromSeconds(3), token)
                            .ConfigureAwait(false))
                    {
                        failures = 0;
                        if (_warning != null) _warning = null;
                    }
                    else if (++failures >= 2)
                    {
                        _warning = "与房主的连接暂时中断——游戏可能已经关闭，或网络正在重连。";
                    }
                }

                Post(OnChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Post(() => HandleFailure(run, ex));
        }
    }

    // ══════════════════════ 控制 ══════════════════════

    /// <summary>关闭房间或退出加入，释放组件进程。</summary>
    public void Stop()
    {
        if (Stage == TunnelStage.Idle) return;
        TunnelLog.Info("用户停止了联机");
        Release();
        Role = TunnelRole.None;
        Stage = TunnelStage.Idle;
        OnChanged();
    }

    /// <summary>程序退出时调用。</summary>
    public void Shutdown()
    {
        Release();
        Role = TunnelRole.None;
        Stage = TunnelStage.Idle;
    }

    // ══════════════════════ 内部 ══════════════════════

    private CancellationTokenSource Begin(TunnelRole role)
    {
        Release();
        _ui = SynchronizationContext.Current ?? _ui;
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread() ?? _dispatcher;
        _run = new CancellationTokenSource();
        _steps.Clear();
        Role = role;
        Stage = TunnelStage.Working;
        Error = null;
        _warning = null;
        DownloadPercent = 0;
        DownloadText = null;
        FirewallMessage = null;
        _invite = null;
        _gamePort = 0;
        _localPort = 0;
        _gameMissingTicks = 0;
        Peers = [];
        HostPeer = null;
        DirectPeers = 0;
        RelayedPeers = 0;
        ConnectedNodes = -1;
        LocalNat = NatKind.Unknown;
        SearchTime = TimeSpan.Zero;
        OnChanged();
        return _run;
    }

    private void Release()
    {
        _run?.Cancel();
        _run = null;
        _announcer?.Dispose();
        _announcer = null;
        _network?.Dispose();
        _network = null;
        GameTunnelCatalog.CurrentInvite = null;
        _invite = null;
        Peers = [];
        HostPeer = null;
        _localPort = 0;
        _gamePort = 0;
    }

    private async Task EnsureComponentAsync(CancellationToken token)
    {
        var progress = new Progress<(int Percent, string Message)>(report =>
        {
            DownloadPercent = report.Percent;
            DownloadText = report.Message;
            OnChanged();
        });

        await EasyTierPackage.EnsureInstalledAsync(progress, token).ConfigureAwait(true);
        DownloadPercent = 100;
        DownloadText = null;
        OnChanged();
    }

    private async Task StartNetworkAsync(EasyTierConfig config, CancellationTokenSource run)
    {
        _network = await Task.Run(() => EasyTierInstance.Start(config), run.Token).ConfigureAwait(true);
        var network = _network;
        network.Exited += (_, _) => Post(() =>
        {
            if (ReferenceEquals(_network, network) && run == _run && !run.IsCancellationRequested)
                HandleFailure(run, new InvalidOperationException("联机组件意外退出，请重新开始。"));
        });
    }

    private void ApplyPeers(IReadOnlyList<EasyTierPeer> peers)
    {
        Peers = peers;
        var members = peers.Where(p => p.IsMember && !p.IsLocal).ToList();
        DirectPeers = members.Count(p => p.IsDirect);
        RelayedPeers = members.Count - DirectPeers;
        LocalNat = peers.FirstOrDefault(p => p.IsLocal)?.Nat ?? LocalNat;
    }

    private void HandleFailure(CancellationTokenSource run, Exception ex)
    {
        if (run != _run || run.IsCancellationRequested) return;

        Error = ex.Message;
        TunnelLog.Error("联机失败：" + ex.Message, ex);
        if (_network is not null)
        {
            var output = _network.GetRecentOutput();
            if (output.Length > 0) TunnelLog.Info("联机组件最近输出：" + Environment.NewLine + output);
        }

        Release();
        Stage = TunnelStage.Failed;
        OnChanged();
    }

    private int ChooseLocalPort(GameTunnelSettings settings, int hostPort, IPAddress bindAddress)
    {
        if (settings.LocalPort > 0)
        {
            if (!IsPortFree(settings.LocalPort, bindAddress))
                throw new InvalidOperationException($"本地端口 {settings.LocalPort} 被占用，请在设置里换一个或改为自动。");
            return settings.LocalPort;
        }

        // 优先沿用房主的端口号：依赖「游戏端口 + 1」这类约定的 Mod（语音等）还能工作。
        if (IsPortFree(hostPort, bindAddress)) return hostPort;

        for (var i = 0; i < 20; i++)
        {
            var port = EasyTierInstance.FindFreePort();
            if (IsPortFree(port, bindAddress)) return port;
        }
        return EasyTierInstance.FindFreePort();
    }

    /// <summary>查系统监听表而不是 bind 试探：后者在 0.0.0.0 上会触发防火墙询问。</summary>
    private static bool IsPortFree(int port, IPAddress bindAddress)
    {
        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            bool Clashes(IPEndPoint used) => used.Port == port
                && (IPAddress.Any.Equals(bindAddress) || IPAddress.Any.Equals(used.Address)
                    || IPAddress.IPv6Any.Equals(used.Address) || used.Address.Equals(bindAddress));
            return !properties.GetActiveTcpListeners().Any(Clashes)
                   && !properties.GetActiveUdpListeners().Any(Clashes);
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    private static string RandomHex(int bytes)
    {
        var buffer = new byte[bytes];
        Random.Shared.NextBytes(buffer);
        return Convert.ToHexString(buffer).ToLowerInvariant();
    }

    private void SetStep(TunnelStep step, TunnelStepState state)
    {
        _steps[step] = state;
        OnChanged();
    }

    private void Post(Action action)
    {
        var run = _run;
        if (run is null) return;

        void Guarded()
        {
            if (ReferenceEquals(run, _run) && !run.IsCancellationRequested) action();
        }

        if (_dispatcher is not null)
        {
            if (!_dispatcher.TryEnqueue(Guarded)) Guarded();
            return;
        }

        if (_ui is not null)
        {
            _ui.Post(_ => Guarded(), null);
            return;
        }

        Guarded();
    }

    private void OnChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            TunnelLog.Warn("联机状态刷新失败：" + ex.Message);
        }
    }
}
