using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace TubaWinUi3.Services;

/// <summary>
/// 让房主的世界出现在朋友本机的「扫描局域网世界」列表里：
/// 用 Minecraft「对局域网开放」的原生广播格式，指向本机的端口转发。
/// </summary>
/// <remarks>
/// 从局域网列表进入不只是方便——只有从那个列表选中的世界，离线账号才不会被正版会话校验拦下。
/// </remarks>
public sealed class LanWorldAnnouncer : IDisposable
{
    private static readonly IPAddress Group = IPAddress.Parse("224.0.2.60");
    private const int AnnouncePort = 4445;
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(1500);

    private readonly CancellationTokenSource _stop = new();
    private readonly Func<string> _motd;
    private readonly int _port;
    private readonly bool _localOnly;

    /// <param name="motd">列表里显示的条目文字，每次广播都重新读取（跟随界面语言）。</param>
    /// <param name="port">本机转发到房主游戏的端口。</param>
    /// <param name="localOnly">只广播给本机（127.0.0.1），不向局域网发任何包。</param>
    public LanWorldAnnouncer(Func<string> motd, int port, bool localOnly)
    {
        _motd = motd;
        _port = port;
        _localOnly = localOnly;
        _ = RunAsync();
    }

    public void Dispose() => _stop.Cancel();

    /// <summary>Minecraft 读取的格式："[MOTD]文字[/MOTD][AD]端口[/AD]"。</summary>
    public static string Format(string motd, int port) => $"[MOTD]{motd}[/MOTD][AD]{port}[/AD]";

    private async Task RunAsync()
    {
        var target = _localOnly ? new IPEndPoint(IPAddress.Loopback, AnnouncePort) : new IPEndPoint(Group, AnnouncePort);
        var sockets = _localOnly
            ? new List<Socket> { new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) }
            : CreateMulticastSenders();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var message = Encoding.UTF8.GetBytes(Format(_motd(), _port));
                foreach (var socket in sockets)
                {
                    try
                    {
                        socket.SendTo(message, target);
                    }
                    catch (SocketException)
                    {
                        // 某块网卡掉了，或者游戏还没开始监听；继续试。
                    }
                }

                try
                {
                    await Task.Delay(Interval, _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
        finally
        {
            foreach (var socket in sockets) socket.Close();
        }
    }

    /// <summary>
    /// 每块网卡一个发送 socket，游戏监听哪个网卡都能收到。
    /// 这些 socket 只发送、从不 bind，因此 Windows 防火墙不会询问。
    /// </summary>
    private static List<Socket> CreateMulticastSenders()
    {
        var sockets = new List<Socket>();
        foreach (var address in GetIPv4Addresses())
        {
            try
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
                sockets.Add(socket);
            }
            catch (SocketException)
            {
            }
        }
        return sockets;
    }

    internal static IReadOnlyList<IPAddress> GetIPv4Addresses()
    {
        var addresses = new List<IPAddress>();
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var info in network.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily == AddressFamily.InterNetwork)
                        addresses.Add(info.Address);
                }
            }
        }
        catch (NetworkInformationException)
        {
        }

        if (!addresses.Contains(IPAddress.Loopback)) addresses.Add(IPAddress.Loopback);
        return addresses;
    }
}
