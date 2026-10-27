using System.Globalization;
using System.Text;

namespace TubaWinUi3.Services;

/// <summary>
/// 一个 EasyTier 虚拟网络的配置（写进 TOML 文件）。
/// 用配置文件而不是命令行参数，是因为只有配置文件实例支持运行中追加端口转发——
/// 朋友加入后正是靠这个把游戏端口接出来。
/// </summary>
public sealed class EasyTierConfig
{
    public string NetworkName { get; set; } = "";

    public string NetworkSecret { get; set; } = "";

    public string Hostname { get; set; } = "";

    /// <summary>固定虚拟地址；null 表示自动分配（dhcp）。</summary>
    public string? IPv4 { get; set; }

    /// <summary>用于发现的公共/自建节点。</summary>
    public List<string> Peers { get; } = [];

    /// <summary>虚拟网络内其它成员可以访问的本机 TCP 端口。"0" 表示全部拒绝。</summary>
    public List<string> TcpWhitelist { get; } = [];

    /// <summary>虚拟网络内其它成员可以访问的本机 UDP 端口。"0" 表示全部拒绝。</summary>
    public List<string> UdpWhitelist { get; } = [];

    /// <summary>只走直连，绝不经中转节点。</summary>
    public bool DirectOnly { get; set; }

    /// <summary>不直连任何成员、不监听入站，别人无从得知本机地址；流量全部经中转。</summary>
    public bool HideAddress { get; set; }

    public string ToToml()
    {
        var toml = new StringBuilder();
        toml.AppendLine("hostname = " + Quote(Hostname));
        toml.AppendLine(IPv4 is null ? "dhcp = true" : "ipv4 = " + Quote(IPv4));

        // 每块网卡上随机端口（IPv6 自动追加）；隐藏地址时不监听任何入站。
        toml.AppendLine(HideAddress
            ? "listeners = []"
            : "listeners = [\"tcp://0.0.0.0:0\", \"udp://0.0.0.0:0\"]");
        toml.AppendLine("tcp_whitelist = " + Array(TcpWhitelist));
        toml.AppendLine("udp_whitelist = " + Array(UdpWhitelist));
        toml.AppendLine();

        toml.AppendLine("[network_identity]");
        toml.AppendLine("network_name = " + Quote(NetworkName));
        toml.AppendLine("network_secret = " + Quote(NetworkSecret));
        toml.AppendLine();

        foreach (var peer in Peers)
        {
            toml.AppendLine("[[peer]]");
            toml.AppendLine("uri = " + Quote(peer));
            toml.AppendLine();
        }

        toml.AppendLine("[flags]");
        toml.AppendLine("no_tun = true");
        toml.AppendLine("latency_first = true");
        toml.AppendLine("enable_kcp_proxy = true");
        toml.AppendLine("enable_quic_proxy = true");
        // Zstandard
        toml.AppendLine("data_compress_algo = 2");
        // 绝不为陌生网络做中转。
        toml.AppendLine("private_mode = true");
        toml.AppendLine("p2p_only = " + (DirectOnly && !HideAddress ? "true" : "false"));
        if (HideAddress)
        {
            toml.AppendLine("disable_p2p = true");
            toml.AppendLine("disable_udp_hole_punching = true");
            toml.AppendLine("disable_tcp_hole_punching = true");
            toml.AppendLine("disable_sym_hole_punching = true");
            toml.AppendLine("disable_upnp = true");
        }
        return toml.ToString();
    }

    private static string Array(IEnumerable<string> values)
        => "[" + string.Join(", ", values.Select(Quote)) + "]";

    private static string Quote(string value)
    {
        var quoted = new StringBuilder("\"");
        foreach (var c in value ?? "")
        {
            if (c is '"' or '\\') quoted.Append('\\').Append(c);
            else if (c < 0x20) quoted.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            else quoted.Append(c);
        }
        return quoted.Append('"').ToString();
    }
}
