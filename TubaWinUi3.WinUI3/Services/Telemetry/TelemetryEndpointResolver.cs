using System.Net;
using System.Net.Sockets;

namespace TubaWinUi3.Services.Telemetry;

/// <summary>
/// 采集端点的地址解析与缓存。
/// 部分网络的解析可能返回黑洞 IP（
/// TCP 永远连不上），因此按「上次成功的 IP → 系统 A 记录 → 可选备用域名 →
/// 内置兜底 IP」逐级回退，避免采集数据在坏地址上空耗超时。
/// 连接层只负责拿到候选 IP，TLS 的 SNI 与证书校验仍使用原始域名
/// （SocketsHttpHandler.ConnectCallback 的约定），因此不会引起证书不匹配。
/// </summary>
internal sealed class TelemetryEndpointResolver
{
    /// <summary>成功地址的缓存时长：过期后重新按完整顺序探测，IP 迁移能自愈。</summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    /// <summary>单次连接尝试的候选地址上限，控制最坏耗时（每地址独立预算）。</summary>
    internal const int MaxCandidates = 4;

    private readonly object _gate = new();
    private IPAddress? _cachedAddress;
    private DateTime _cachedAtUtc;

    public TelemetryEndpointResolver(string host, string fallbackHost, IReadOnlyList<string> fallbackAddresses)
    {
        Host = host;
        FallbackHost = fallbackHost;
        FallbackAddresses = fallbackAddresses;
    }

    /// <summary>采集端点主机名（连接与 SNI 均使用它）。</summary>
    public string Host { get; }

    /// <summary>可选备用域名；留空时只解析采集端点，不访问旧提供商。</summary>
    public string FallbackHost { get; }

    /// <summary>内置兜底 IP（解析全部失败时使用，可能随服务端迁移失效）。</summary>
    public IReadOnlyList<string> FallbackAddresses { get; }

    /// <summary>按优先级返回本次连接可尝试的地址列表（已去重、已限量）。</summary>
    public async Task<IReadOnlyList<IPAddress>> GetCandidatesAsync(CancellationToken ct)
    {
        var system = await ResolveAsync(Host, ct).ConfigureAwait(false);
        var fallback = string.IsNullOrWhiteSpace(FallbackHost)
            ? Array.Empty<IPAddress>()
            : await ResolveAsync(FallbackHost, ct).ConfigureAwait(false);

        lock (_gate)
        {
            if (_cachedAddress is not null && DateTime.UtcNow - _cachedAtUtc > CacheLifetime)
                _cachedAddress = null;

            return MergeCandidates(_cachedAddress, system, fallback, ParseFallbackAddresses(), MaxCandidates);
        }
    }

    /// <summary>记录本次连接成功的地址，后续优先复用。</summary>
    public void ReportSuccess(IPAddress address)
    {
        lock (_gate)
        {
            _cachedAddress = address;
            _cachedAtUtc = DateTime.UtcNow;
        }
    }

    /// <summary>连接全部失败时清空缓存，下次重新完整探测。</summary>
    public void InvalidateCache()
    {
        lock (_gate)
        {
            _cachedAddress = null;
        }
    }

    /// <summary>
    /// 候选地址合并（纯函数，可单测）：缓存地址优先，其后依次为系统解析、
    /// Traffic Manager 解析与内置兜底地址；按 IP 去重并截断到上限。
    /// </summary>
    internal static IReadOnlyList<IPAddress> MergeCandidates(
        IPAddress? cached,
        IEnumerable<IPAddress> systemAddresses,
        IEnumerable<IPAddress> fallbackHostAddresses,
        IEnumerable<IPAddress> pinnedAddresses,
        int max)
    {
        var result = new List<IPAddress>(max);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddRange(IEnumerable<IPAddress>? addresses)
        {
            if (addresses is null) return;
            foreach (var address in addresses)
            {
                if (result.Count >= max) return;
                // 链路本地/未指定地址没有连接价值
                if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) continue;
                if (!seen.Add(address.ToString())) continue;
                result.Add(address);
            }
        }

        if (cached is not null) AddRange([cached]);
        AddRange(systemAddresses);
        AddRange(fallbackHostAddresses);
        AddRange(pinnedAddresses);
        return result;
    }

    /// <summary>解析 IPv4 地址；失败或无结果时返回空列表（继续走下一级回退）。</summary>
    private static async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct)
                .ConfigureAwait(false);
            return addresses.Length > 0 ? addresses : [];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private IReadOnlyList<IPAddress> ParseFallbackAddresses()
    {
        var result = new List<IPAddress>(FallbackAddresses.Count);
        foreach (var text in FallbackAddresses)
        {
            if (IPAddress.TryParse(text, out var address))
                result.Add(address);
        }
        return result;
    }
}
