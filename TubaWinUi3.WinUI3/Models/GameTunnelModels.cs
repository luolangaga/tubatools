namespace TubaWinUi3.Models;

/// <summary>游戏服务器使用的传输层协议。</summary>
public enum GameTunnelProtocol
{
    Tcp,
    Udp,
    TcpAndUdp
}

public static class GameTunnelProtocolExtensions
{
    public static bool UsesTcp(this GameTunnelProtocol protocol)
        => protocol is GameTunnelProtocol.Tcp or GameTunnelProtocol.TcpAndUdp;

    public static bool UsesUdp(this GameTunnelProtocol protocol)
        => protocol is GameTunnelProtocol.Udp or GameTunnelProtocol.TcpAndUdp;

    public static string Describe(this GameTunnelProtocol protocol) => protocol switch
    {
        GameTunnelProtocol.Tcp => "TCP",
        GameTunnelProtocol.Udp => "UDP",
        _ => "TCP + UDP"
    };
}

/// <summary>内置游戏档案：端口、协议、主机/朋友两侧的分步教程。</summary>
public sealed class GamePreset
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>拿不到真实 Logo 时的兜底图标字形（Segoe Fluent Icons）。</summary>
    public required string Glyph { get; init; }

    /// <summary>官方 Logo 素材的候选地址，按顺序尝试（第一个通就用）。</summary>
    public IReadOnlyList<string> LogoUrls { get; init; } = [];

    public required int DefaultPort { get; init; }
    public required GameTunnelProtocol Protocol { get; init; }
    public required string Tagline { get; init; }

    /// <summary>主机在游戏里需要先完成的事（一句话）。</summary>
    public required string HostAction { get; init; }

    public required IReadOnlyList<string> HostSteps { get; init; }
    public required IReadOnlyList<string> GuestSteps { get; init; }

    /// <summary>朋友在游戏内的连接位置说明，如「多人游戏 → 通过 IP 加入」。</summary>
    public required string GuestEntryPoint { get; init; }

    public string? Note { get; init; }

    /// <summary>是否使用 Minecraft「对局域网开放」的发现方式（只对 Java 版有效）。</summary>
    public bool UsesLanDiscovery { get; init; }
}

/// <summary>用户自己添加的游戏（与内置档案同构，可编辑删除）。</summary>
public sealed class CustomGame
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Port { get; set; }
    public GameTunnelProtocol Protocol { get; set; } = GameTunnelProtocol.Tcp;

    /// <summary>给朋友看的补充说明（可空）。</summary>
    public string? Note { get; set; }
}

/// <summary>邀请码载荷：一段可以直接粘进聊天软件的紧凑字符串。</summary>
public sealed class InviteInfo
{
    /// <summary>房主在虚拟网络里的固定地址（每个房间独立，不会冲突）。</summary>
    public const string HostAddress = "10.144.144.1";

    /// <summary>房间码 <c>U/XXXX-XXXX-XXXX-XXXX</c>；同时编码了网络名与密钥。</summary>
    public string RoomCode { get; set; } = "";

    public string NetworkName { get; set; } = "";

    public string NetworkSecret { get; set; } = "";

    public string Host { get; set; } = HostAddress;

    public int Port { get; set; }

    public string Game { get; set; } = "";

    public GameTunnelProtocol Protocol { get; set; } = GameTunnelProtocol.Tcp;

    /// <summary>加入后可以直接从游戏的局域网列表进入（Minecraft Java 版）。</summary>
    public bool UsesLanDiscovery { get; set; }

    public string Address => $"{Host}:{Port}";

    public string ShortCode => RoomCode.Length >= 6 ? RoomCode[..6] + "-****-****-****" : RoomCode;
}
