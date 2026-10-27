using System.Globalization;
using System.Text.Json;

namespace TubaWinUi3.Services;

/// <summary>虚拟网络里的一台设备，来自 <c>easytier-cli peer -o json</c>。</summary>
public sealed class EasyTierPeer
{
    public string Hostname { get; init; } = "";

    /// <summary>虚拟 IPv4；公共节点没有地址（不是网络成员）。</summary>
    public string IPv4 { get; init; } = "";

    public bool IsLocal { get; init; }

    /// <summary>流量是否直连（cost == p2p），否则经中转。</summary>
    public bool IsDirect { get; init; }

    /// <summary>往返延迟（毫秒），还没测出来时为 null。</summary>
    public double? LatencyMs { get; init; }

    /// <summary>实际使用的传输层，如 udp / tcp / quic。</summary>
    public string Tunnel { get; init; } = "";

    public NatKind Nat { get; init; }

    public bool IsMember => !string.IsNullOrWhiteSpace(IPv4);

    public static IReadOnlyList<EasyTierPeer> ParseList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            var rows = new List<EasyTierPeer>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                rows.Add(Parse(item));
            }
            return rows;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static EasyTierPeer Parse(JsonElement item)
    {
        string cost = GetString(item, "cost");
        return new EasyTierPeer
        {
            Hostname = GetString(item, "hostname"),
            IPv4 = GetString(item, "ipv4"),
            IsLocal = string.Equals(cost, "Local", StringComparison.OrdinalIgnoreCase),
            IsDirect = string.Equals(cost, "p2p", StringComparison.OrdinalIgnoreCase),
            LatencyMs = double.TryParse(GetString(item, "lat_ms"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                ? lat
                : null,
            Tunnel = GetString(item, "tunnel_proto"),
            Nat = ParseNat(GetString(item, "nat_type"))
        };
    }

    public static NatKind ParseNat(string value)
    {
        return value switch
        {
            "OpenInternet" or "NoPat" => NatKind.Open,
            "FullCone" => NatKind.FullCone,
            "Restricted" => NatKind.Restricted,
            "PortRestricted" => NatKind.PortRestricted,
            "Symmetric" or "SymUdpFirewall" or "SymmetricEasyInc" or "SymmetricEasyDec" => NatKind.Symmetric,
            _ => NatKind.Unknown
        };
    }

    private static string GetString(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            _ => ""
        };
    }

    public string Describe()
    {
        if (IsLocal) return "本机";
        if (!IsMember) return "节点";
        var path = IsDirect ? "直连" : "中转";
        return LatencyMs is { } ms ? $"{path} · {ms:F0}ms" : path;
    }
}

/// <summary>网络对入站连接的开放程度：越开放越容易打洞直连。</summary>
public enum NatKind
{
    Unknown,
    Open,
    FullCone,
    Restricted,
    PortRestricted,
    Symmetric
}

public static class NatKindExtensions
{
    public static string Describe(this NatKind nat) => nat switch
    {
        NatKind.Open => "开放网络",
        NatKind.FullCone => "完全锥形 NAT",
        NatKind.Restricted => "受限 NAT",
        NatKind.PortRestricted => "端口受限 NAT",
        NatKind.Symmetric => "对称 NAT（较难直连）",
        _ => "未知"
    };
}
