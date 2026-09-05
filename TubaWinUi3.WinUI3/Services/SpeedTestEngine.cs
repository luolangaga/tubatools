using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace TubaWinUi3.Services;

/// <summary>测速服务端协议类型。</summary>
public enum SpeedServerKind
{
    /// <summary>LibreSpeed 协议（浙江大学 speedtest.zju.edu.cn）。</summary>
    LibreSpeed,
    /// <summary>Cloudflare 官方测速端点（speed.cloudflare.com，境外）。</summary>
    Cloudflare,
    /// <summary>Ookla（speedtest.net）服务器遗留 HTTP 端点（同 sivel/speedtest-cli 口径）。</summary>
    Ookla
}

/// <summary>一个可选的测速节点。</summary>
public sealed record SpeedTestNode(string Id, string Name, string BaseUrl, SpeedServerKind Kind, string? HostHeader = null)
{
    public override string ToString() => Name;
}

public static class SpeedTestNodes
{
    /// <summary>浙大测速节点（LibreSpeed，境内，默认）。</summary>
    public static readonly SpeedTestNode Zju =
        new("zju", "浙江大学", "https://speedtest.zju.edu.cn", SpeedServerKind.LibreSpeed);

    /// <summary>Cloudflare 官方测速端点（境外，测国际出口带宽）。</summary>
    public static readonly SpeedTestNode Cloudflare =
        new("cloudflare", "Cloudflare（境外）", "https://speed.cloudflare.com", SpeedServerKind.Cloudflare);

    // ── Ookla 节点（来源 heiok.com/speedtest.html 收录列表，2026-09 逐个 curl 实测筛选）──
    // 同页其余节点（南京电信5G 5396 / 杭州电信 59386 / 上海联通5G 24447 / 澳门CTM 33794 / 香港CSL 13538）
    // 实测不可达或时通时不通，未收录。

    /// <summary>苏州移动 Ookla 节点：用 canonical 域名而非裸 IP，服务器换 IP 时域名跟随。</summary>
    public static readonly SpeedTestNode SuzhouMobile =
        new("ookla-16204", "苏州移动（Ookla 16204）",
            "http://server-16204.prod.hosts.ooklaserver.net:8080", SpeedServerKind.Ookla);

    /// <summary>上海电信 Ookla 节点：直连 IP 会被 307 到不可解析的 *.online.sh.cn，请求须改写 Host 头。</summary>
    public static readonly SpeedTestNode ShanghaiTelecom =
        new("ookla-3633", "上海电信（Ookla 3633）",
            "http://222.68.195.2:8080", SpeedServerKind.Ookla, "sh-ct.online.sh.cn");

    /// <summary>新加坡 Singtel Ookla 节点（境外直连基准，测国际出口吞吐）。</summary>
    public static readonly SpeedTestNode SingaporeSingtel =
        new("ookla-13623", "新加坡 Singtel（Ookla 13623）",
            "https://server-13623.prod.hosts.ooklaserver.net:8080", SpeedServerKind.Ookla);

    public static readonly SpeedTestNode Default = Zju;
    public static readonly SpeedTestNode[] All = { Zju, SuzhouMobile, ShanghaiTelecom, SingaporeSingtel, Cloudflare };

    public static SpeedTestNode? ById(string? id) =>
        string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(n => n.Id == id);
}

/// <summary>
/// 多节点网络测速引擎，节点协议两套：
/// ── LibreSpeed（浙大 speedtest.zju.edu.cn，与网页端 speedtest_worker.min.js 保持一致）──
///   IP   : GET  /getIP.php                  -> 纯文本公网 IP
///   延迟 : GET  /empty.php?cors=true&amp;r=xxx    逐个测量 RTT
///   下载 : GET  /garbage.php?ckSize=N       N MiB 随机数据，多路并行
///   上传 : POST /empty.php                  随机数据体（服务器丢弃），多路并行
/// ── Cloudflare（speed.cloudflare.com 官方测速端点）──
///   IP   : GET  /meta                       -> JSON，取 clientIp（被 WAF 拒时回退 /cdn-cgi/trace 的 ip= 行）
///   延迟 : GET  /__down?bytes=0             逐个测量 RTT
///   下载 : GET  /__down?bytes=N             N 字节数据，多路并行（块取 25MB：1e8 会被 WAF 以 403 精确拒绝）
///   上传 : POST /__up                      随机数据体（服务器丢弃），多路并行
/// ── Ookla（speedtest.net 服务器遗留 HTTP 端点，同 sivel/speedtest-cli）──
///   IP   : 节点无回显端点，直接用 Cloudflare /cdn-cgi/trace（公网 IP 与所选节点无关）
///   延迟 : GET  /speedtest/latency.txt          逐个测量 RTT
///   下载 : GET  /speedtest/random{N}x{N}.jpg    多路并行；镜像缺大图时 N 按 4000→1000 逐级降尺寸
///   上传 : POST /speedtest/upload.php           随机数据体（服务器丢弃），多路并行
///   特例 : 上海电信 3633 直连 IP 会 307 到不可解析的 *.online.sh.cn，请求统一改写 Host 头
/// 速率口径统一为兆比特每秒（Mbps），含 1.06 开销补偿系数（与 LibreSpeed 网页端一致）。
/// </summary>
public sealed class SpeedTestEngine : IDisposable
{
    private const string ZjuGetIpPath = "/getIP.php";
    private const string ZjuEmptyPath = "/empty.php";
    private const string ZjuGarbagePath = "/garbage.php";
    private const string CfDownPath = "/__down";
    private const string CfUpPath = "/__up";
    private const string CfMetaPath = "/meta";
    private const string CfTracePath = "/cdn-cgi/trace";
    private const string CfTraceFallbackBase = "https://speed.cloudflare.com";
    private const string OoklaLatencyPath = "/speedtest/latency.txt";
    private const string OoklaUploadPath = "/speedtest/upload.php";
    private static readonly int[] OoklaDownloadSizes = { 4000, 3000, 2000, 1000 }; // 镜像缺大图时逐级降尺寸
    private const long CloudflareDownloadBytes = 25_000_000;           // 1e8 会被 CF WAF 403，25MB 为官方中档块实测稳定
    private const int CloudflareUploadChunkBytes = 32 * 1024 * 1024;   // 单流顺序 POST，加大块提高占空比；按发送计数后慢速链路尾部也不丢
    private const double OverheadCompensation = 1.06;                  // TCP/IP 头开销补偿，与网页端一致
    private const int ReadBufferSize = 256 * 1024;
    private const int MaxConsecutiveFailures = 8;                      // 单流连续失败上限，超过即退出避免空转

    private readonly HttpClient _client;
    private readonly SpeedTestNode _node;
    private readonly string _baseUrl;
    private readonly byte[] _uploadChunk;

    public int PingCount { get; init; } = 24;
    public int DownloadSeconds { get; init; } = 10;
    public int UploadSeconds { get; init; } = 8;
    public int DownloadStreams { get; init; } = 4;
    public int UploadStreams { get; init; } = 3;
    public int DownloadChunkMiB { get; init; } = 100;
    public int UploadChunkBytes { get; init; } = 1 * 1024 * 1024;

    /// <summary>当前使用的测速节点。</summary>
    public SpeedTestNode Node => _node;

    /// <summary>实时速率回调：当前速率(Mbps)、阶段进度 0..1、已用秒数。</summary>
    public delegate void SpeedCallback(double mbps, double progress, double seconds);

    /// <summary>延迟阶段回调：当前中位延迟(ms)、当前抖动(ms)、已测次数、总数。</summary>
    public delegate void LatencyCallback(double pingMs, double jitterMs, int done, int total);

    public SpeedTestEngine(SpeedTestNode? node = null)
    {
        _node = node ?? SpeedTestNodes.Default;
        _baseUrl = _node.BaseUrl.TrimEnd('/');
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(6),
            PooledConnectionLifetime = TimeSpan.FromMinutes(3),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(45),
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = false,
            MaxConnectionsPerServer = 32
        };
        _client = new HttpClient(handler)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) TubaWinUi3-SpeedTest/1.0");

        var uploadChunkBytes = _node.Kind == SpeedServerKind.Cloudflare ? CloudflareUploadChunkBytes : UploadChunkBytes;
        _uploadChunk = new byte[uploadChunkBytes];
        RandomNumberGenerator.Fill(_uploadChunk);
    }

    public void Dispose() => _client.Dispose();

    // ─────────────────────────── 公网 IP ───────────────────────────

    public async Task<string> GetPublicIpAsync(CancellationToken ct = default)
    {
        if (_node.Kind == SpeedServerKind.Ookla)
        {
            // Ookla 节点没有 IP 回显端点；公网 IP 与所选节点无关，直接走 Cloudflare trace
            return await GetIpFromTraceAsync(CfTraceFallbackBase, ct).ConfigureAwait(false);
        }

        if (_node.Kind != SpeedServerKind.Cloudflare)
        {
            using var resp = await _client.GetAsync(_baseUrl + ZjuGetIpPath,
                HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
        }

        // Cloudflare：优先 /meta（JSON clientIp），被 WAF 拒绝（实测 403）时回退 /cdn-cgi/trace
        try
        {
            using var metaResp = await _client.GetAsync(_baseUrl + CfMetaPath,
                HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (metaResp.IsSuccessStatusCode)
            {
                var meta = await metaResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(meta);
                if (doc.RootElement.TryGetProperty("clientIp", out var ip) &&
                    ip.ValueKind == JsonValueKind.String &&
                    ip.GetString() is { } clientIp && clientIp.Length > 0)
                    return clientIp.Trim();
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* 回退 trace */ }

        return await GetIpFromTraceAsync(_baseUrl, ct).ConfigureAwait(false);
    }

    private async Task<string> GetIpFromTraceAsync(string baseUrl, CancellationToken ct)
    {
        using var traceResp = await _client.GetAsync(baseUrl + CfTracePath,
            HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        traceResp.EnsureSuccessStatusCode();
        var trace = await traceResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        foreach (var line in trace.Split('\n'))
            if (line.StartsWith("ip=", StringComparison.Ordinal))
                return line[3..].Trim();
        throw new HttpRequestException("无法从节点解析公网 IP");
    }

    /// <summary>构造请求；节点配置了 HostHeader 时改写 Host 头（上海电信 3633 直连 IP 会 307 到不可解析的通配域名）。</summary>
    private HttpRequestMessage NewRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        if (_node.HostHeader is { } host)
            req.Headers.Host = host;
        return req;
    }

    // ─────────────────────────── 延迟 / 抖动 ───────────────────────────

    private string LatencyUrl()
        => _node.Kind switch
        {
            SpeedServerKind.Cloudflare => $"{_baseUrl}{CfDownPath}?bytes=0&r={Guid.NewGuid():N}",
            SpeedServerKind.Ookla => $"{_baseUrl}{OoklaLatencyPath}?x={Guid.NewGuid():N}",
            _ => $"{_baseUrl}{ZjuEmptyPath}?cors=true&r={Guid.NewGuid():N}"
        };

    public async Task<(double PingMs, double JitterMs)> MeasureLatencyAsync(
        LatencyCallback? live = null, CancellationToken ct = default)
    {
        var samples = new List<double>(PingCount);
        double diffSum = 0;
        int diffCount = 0;

        for (int i = 0; i < PingCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            try
            {
                var url = LatencyUrl();
                using var req = NewRequest(HttpMethod.Get, url);
                using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                _ = resp.StatusCode; // 任何响应均视为链路可达
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // 单次丢包/失败跳过，不中断整体测试
                await Task.Delay(15, ct).ConfigureAwait(false);
                continue;
            }
            sw.Stop();

            double ms = Math.Max(0.01, sw.Elapsed.TotalMilliseconds);
            samples.Add(ms);
            if (samples.Count >= 2)
            {
                diffSum += Math.Abs(ms - samples[^2]);
                diffCount++;
            }

            var sorted = samples.OrderBy(x => x).ToArray();
            double median = sorted.Length % 2 == 1
                ? sorted[sorted.Length / 2]
                : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0;
            double curJitter = diffCount > 0 ? diffSum / diffCount : 0.0;

            live?.Invoke(median, curJitter, samples.Count, PingCount);
            await Task.Delay(15, ct).ConfigureAwait(false);
        }

        if (samples.Count == 0)
            return (double.NaN, double.NaN);

        var final = samples.OrderBy(x => x).ToArray();
        double ping = final.Length % 2 == 1
            ? final[final.Length / 2]
            : (final[final.Length / 2 - 1] + final[final.Length / 2]) / 2.0;
        double jitter = diffCount > 0 ? diffSum / diffCount : 0.0;
        return (ping, jitter);
    }

    // ─────────────────────────── 下载 ───────────────────────────

    private string DownloadTarget(int ooklaSize)
        => _node.Kind switch
        {
            SpeedServerKind.Cloudflare => $"{_baseUrl}{CfDownPath}?bytes={CloudflareDownloadBytes}",
            SpeedServerKind.Ookla => $"{_baseUrl}/speedtest/random{ooklaSize}x{ooklaSize}.jpg?x={Guid.NewGuid():N}",
            _ => $"{_baseUrl}{ZjuGarbagePath}?ckSize={DownloadChunkMiB}"
        };

    public async Task<double> MeasureDownloadAsync(SpeedCallback? live, CancellationToken ct)
    {
        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        long bytes = 0;
        var sw = Stopwatch.StartNew();

        var streams = new Task[DownloadStreams];
        for (int s = 0; s < DownloadStreams; s++)
        {
            streams[s] = Task.Run(
                () => DownloadStreamWorkerAsync(phaseCts.Token, n => Interlocked.Add(ref bytes, n)),
                CancellationToken.None);
        }

        await RunSamplerAsync(sw, phaseCts,
            () => Volatile.Read(ref bytes),
            () => Math.Min(1.0, sw.Elapsed.TotalSeconds / DownloadSeconds),
            DownloadSeconds,
            live).ConfigureAwait(false);

        await WaitStreamsGracefullyAsync(streams, ct, TimeSpan.FromSeconds(2.5)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested(); // 用户主动停止时向调用方传播取消，而不是返回残缺结果
        long total = Volatile.Read(ref bytes);
        if (total <= 0)
            throw new HttpRequestException($"{_node.Name}节点无数据返回，节点可能不可达或链路受限");
        return BitsToMbps(total, sw.Elapsed.TotalSeconds);
    }

    private async Task DownloadStreamWorkerAsync(CancellationToken ct, Action<long> add)
    {
        var buffer = new byte[ReadBufferSize];
        int sizeIdx = 0; // Ookla 镜像缺大图时逐级降尺寸（4000→1000）
        int consecutiveFailures = 0;
        while (!ct.IsCancellationRequested && consecutiveFailures < MaxConsecutiveFailures)
        {
            try
            {
                var target = DownloadTarget(OoklaDownloadSizes[Math.Min(sizeIdx, OoklaDownloadSizes.Length - 1)]);
                using var req = NewRequest(HttpMethod.Get, target);
                using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    consecutiveFailures++; // 例如节点对请求尺寸返回 403
                    if (_node.Kind == SpeedServerKind.Ookla && sizeIdx < OoklaDownloadSizes.Length - 1)
                        sizeIdx++;
                    await Task.Delay(60, ct).ConfigureAwait(false);
                    continue;
                }
                consecutiveFailures = 0;

                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                while (!ct.IsCancellationRequested)
                {
                    int n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (n <= 0) break;
                    add(n);
                }
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // 中途断流等异常：退避后重试，连续失败达上限才退出，避免整条流报废
                consecutiveFailures++;
                try { await Task.Delay(80, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    // ─────────────────────────── 上传 ───────────────────────────

    private string UploadTarget()
        => _node.Kind switch
        {
            SpeedServerKind.Cloudflare => _baseUrl + CfUpPath,
            SpeedServerKind.Ookla => _baseUrl + OoklaUploadPath,
            _ => _baseUrl + ZjuEmptyPath
        };

    public async Task<double> MeasureUploadAsync(SpeedCallback? live, CancellationToken ct)
    {
        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        long sent = 0;
        var sw = Stopwatch.StartNew();

        var streams = new Task[UploadStreams];
        for (int s = 0; s < UploadStreams; s++)
        {
            streams[s] = Task.Run(
                () => UploadStreamWorkerAsync(ct, phaseCts.Token, n => Interlocked.Add(ref sent, n)),
                CancellationToken.None);
        }

        await RunSamplerAsync(sw, phaseCts,
            () => Volatile.Read(ref sent),
            () => Math.Min(1.0, sw.Elapsed.TotalSeconds / UploadSeconds),
            UploadSeconds,
            live).ConfigureAwait(false);

        // 自然结束后不再发起新请求，但放行在途上传块继续发送并计数，避免尾部吞吐被低估
        await WaitStreamsGracefullyAsync(streams, ct, TimeSpan.FromSeconds(6)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        long total = Volatile.Read(ref sent);
        if (total <= 0)
            throw new HttpRequestException($"{_node.Name}节点无数据返回，节点可能不可达或链路受限");
        return BitsToMbps(total, sw.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// 单路上传流：<paramref name="stopNewCt"/> 控制“是否继续发起新请求”（自然结束时取消），
    /// <paramref name="ct"/> 控制“立即中止在途请求”（用户主动停止）。
    /// </summary>
    private async Task UploadStreamWorkerAsync(CancellationToken ct, CancellationToken stopNewCt, Action<long> add)
    {
        var target = UploadTarget();
        int consecutiveFailures = 0;
        try
        {
            while (!stopNewCt.IsCancellationRequested && consecutiveFailures < MaxConsecutiveFailures)
            {
                try
                {
                    using var content = new CountingContent(_uploadChunk, add);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
                    using var req = NewRequest(HttpMethod.Post, target);
                    req.Content = content;
                    using var resp = await _client.SendAsync(req, ct).ConfigureAwait(false);
                    _ = resp.StatusCode;
                    consecutiveFailures = 0;
                }
                catch (OperationCanceledException) when (!stopNewCt.IsCancellationRequested)
                {
                    // 仅用户主动取消时退出；自然结束后的“收尾在途请求”不受 stopNewCt 影响
                    break;
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    consecutiveFailures++; // 单流持续失败达上限后退出，避免空转
                    try { await Task.Delay(80, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        catch { /* 静默 */ }
    }

    /// <summary>
    /// 按实际写入网络的字节数计数的上传内容：慢速链路下一个大块在采样窗内发不完，
    /// 已发送的部分也要计入，否则吞吐会被整体低估甚至记为 0。
    /// </summary>
    private sealed class CountingContent : HttpContent
    {
        private readonly byte[] _data;
        private readonly Action<long> _add;

        public CountingContent(byte[] data, Action<long> add)
        {
            _data = data;
            _add = add;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            const int chunk = 64 * 1024;
            for (int offset = 0; offset < _data.Length; offset += chunk)
            {
                int n = Math.Min(chunk, _data.Length - offset);
                await stream.WriteAsync(_data.AsMemory(offset, n)).ConfigureAwait(false);
                _add(n);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _data.Length;
            return true;
        }
    }

    // ─────────────────────────── 采样器 / 工具 ───────────────────────────

    private async Task RunSamplerAsync(
        Stopwatch sw,
        CancellationTokenSource phaseCts,
        Func<long> totalBytes,
        Func<double> progress,
        double totalSeconds,
        SpeedCallback? live)
    {
        // 1 秒滑动窗口：上传按块离散到达、下载有响应间隙，瞬时差分会大幅抖动，
        // 窗口平均既平滑又贴近真实持续吞吐。
        const double windowSeconds = 1.0;
        var window = new Queue<(double T, long Bytes)>();

        try
        {
            while (true)
            {
                await Task.Delay(120, phaseCts.Token).ConfigureAwait(false);
                double now = sw.Elapsed.TotalSeconds;
                long b = totalBytes();
                window.Enqueue((now, b));
                while (window.Count > 1 && now - window.Peek().T > windowSeconds)
                    window.Dequeue();

                double liveMbps = 0;
                var oldest = window.Peek();
                double dt = now - oldest.T;
                if (dt >= 0.2 && b >= oldest.Bytes)
                {
                    liveMbps = BitsToMbps(b - oldest.Bytes, dt);
                    if (liveMbps < 0) liveMbps = 0;
                }
                live?.Invoke(liveMbps, Math.Clamp(progress(), 0, 1), now);

                if (now >= totalSeconds)
                    break;
            }
        }
        catch (OperationCanceledException) { }

        // 停止发起新请求，允许在途请求收尾后再统计
        phaseCts.Cancel();
    }

    private static async Task WaitStreamsGracefullyAsync(Task[] streams, CancellationToken ct, TimeSpan timeout)
    {
        try
        {
            await Task.WhenAll(streams).WaitAsync(timeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { }
        catch { }
    }

    private static double BitsToMbps(long bytes, double seconds)
    {
        if (seconds <= 0.02 || bytes <= 0) return 0;
        return bytes * 8.0 / 1e6 / seconds * OverheadCompensation;
    }
}
