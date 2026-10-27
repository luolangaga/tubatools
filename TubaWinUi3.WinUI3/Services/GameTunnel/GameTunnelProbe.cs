using System.Net.NetworkInformation;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>本机端口占用探测：判断游戏是不是真的把房间开起来了。</summary>
public static class GameTunnelProbe
{
    public sealed record PortStatus(bool Listening, string Message);

    /// <summary>本机 TCP 端口能否建立连接（用于确认端口转发链路真的通了）。</summary>
    public static async Task<bool> CanConnectAsync(int port, TimeSpan timeout, CancellationToken cancellation = default)
    {
        if (!GameTunnelCatalog.IsValidPort(port)) return false;
        try
        {
            using var client = new System.Net.Sockets.TcpClient(System.Net.Sockets.AddressFamily.InterNetwork);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeoutCts.CancelAfter(timeout);
            await client.ConnectAsync(System.Net.IPAddress.Loopback, port, timeoutCts.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }
    }

    public static PortStatus Check(int port, GameTunnelProtocol protocol)
    {
        if (!GameTunnelCatalog.IsValidPort(port))
            return new PortStatus(false, "端口不合法（应在 1-65535 之间）");

        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var tcp = false;
            var udp = false;

            if (protocol.UsesTcp())
            {
                tcp = properties.GetActiveTcpListeners().Any(endpoint => endpoint.Port == port);
            }

            if (protocol.UsesUdp())
            {
                try
                {
                    udp = properties.GetActiveUdpListeners().Any(endpoint => endpoint.Port == port);
                }
                catch
                {
                    // 某些系统上 UDP 列表可能读不到，按未知处理
                }
            }

            var listening = protocol switch
            {
                GameTunnelProtocol.Tcp => tcp,
                GameTunnelProtocol.Udp => udp,
                _ => tcp || udp
            };

            if (listening)
            {
                var what = protocol switch
                {
                    GameTunnelProtocol.Tcp => "TCP",
                    GameTunnelProtocol.Udp => "UDP",
                    _ => tcp && udp ? "TCP/UDP" : tcp ? "TCP" : "UDP"
                };
                return new PortStatus(true, $"检测到有程序在监听 {what} {port} 端口，游戏应该已经开好了");
            }

            return new PortStatus(false, $"目前没有程序监听 {port} 端口。请先在游戏里开好（建房 / 启动服务器）；开好后点「重新检测」");
        }
        catch (Exception ex)
        {
            return new PortStatus(false, $"端口检测失败：{ex.Message}");
        }
    }
}
