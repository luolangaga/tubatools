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
    private const int LatencyMaxConsecutiveFails = 5;                  // 延迟探测连续失败上限：节点不可达时快速报错，不空转满 24 次
    private const int PingHeaderTimeoutMs = 8000;                      // 单次探测响应头超时：连接建立但服务端不回包时不把阶段卡死
    private const int DownloadHeaderTimeoutMs = 12000;                 // 下载请求响应头超时（响应体不受限，慢速链路可长时间流式读取）
    private const double DownloadStallSeconds = 5.0;                   // 下载停滞判定：连续 5s 无任何新字节
    private const double UploadStallSeconds = 6.0;                     // 上传停滞判定：连续 6s 无任何新发送字节

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
            using var resp = await SendWithHeaderTimeoutAsync(
                new HttpRequestMessage(HttpMethod.Get, _baseUrl + ZjuGetIpPath), ct, PingHeaderTimeoutMs).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
        }

        // Cloudflare：优先 /meta（JSON clientIp），被 WAF 拒绝（实测 403）时回退 /cdn-cgi/trace
        try
        {
            using var metaResp = await SendWithHeaderTimeoutAsync(
                new HttpRequestMessage(HttpMethod.Get, _baseUrl + CfMetaPath), ct, PingHeaderTimeoutMs).ConfigureAwait(false);
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
        // 注意：这里不走 NewRequest——Ookla 节点的 Host 头改写只适用于其自身节点，发往 Cloudflare 会 400
        using var traceResp = await SendWithHeaderTimeoutAsync(
            new HttpRequestMessage(HttpMethod.Get, baseUrl + CfTracePath), ct, PingHeaderTimeoutMs).ConfigureAwait(false);
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

    /// <summary>
    /// 带响应头超时的发送：连接建立但服务端长时间不回响应头时按失败计，
    /// 避免 HttpClient 总超时为无限时单请求挂起把整个阶段卡死。响应体读取不受此超时限制。
    /// </summary>
    private async Task<HttpResponseMessage> SendWithHeaderTimeoutAsync(
        HttpRequestMessage req, CancellationToken ct, int headerTimeoutMs)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(headerTimeoutMs);
        return await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
    }

    /// <summary>指数退避：60ms 起倍增、1.6s 封顶，容忍节点短暂限流窗口而不是快速把重试次数烧光。</summary>
    private static int BackoffDelayMs(int consecutiveFailures)
        => Math.Min(1600, 60 << Math.Min(consecutiveFailures - 1, 5));

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
        int consecutiveFails = 0;

        for (int i = 0; i < PingCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            try
            {
                var url = LatencyUrl();
                using var req = NewRequest(HttpMethod.Get, url);
                using var resp = await SendWithHeaderTimeoutAsync(req, ct, PingHeaderTimeoutMs)
                    .ConfigureAwait(false);
                _ = resp.StatusCode; // 任何响应均视为链路可达
            }
            // 用户主动取消（OCE 且 ct 已取消）向上传播；其余失败（含单次头超时）跳过并退避
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                consecutiveFails++;
                if (consecutiveFails >= LatencyMaxConsecutiveFails)
                    throw new HttpRequestException(
                        $"无法从{_node.Name}节点测得延迟：连续 {consecutiveFails} 次探测失败，节点可能不可达或拒绝连接，请稍后重试或更换节点");
                await Task.Delay(120, ct).ConfigureAwait(false);
                continue;
            }
            consecutiveFails = 0;
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
            throw new HttpRequestException($"无法从{_node.Name}节点测得延迟：全部探测请求失败，节点可能不可达或拒绝连接");

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

        var end = await RunSamplerAsync(sw, phaseCts,
            () => Volatile.Read(ref bytes),
            () => Math.Min(1.0, sw.Elapsed.TotalSeconds / DownloadSeconds),
            DownloadSeconds, live, streams, DownloadStallSeconds).ConfigureAwait(false);

        await WaitStreamsGracefullyAsync(streams, ct,
            end == PhaseEnd.Normal ? TimeSpan.FromSeconds(2.5) : TimeSpan.FromSeconds(1.2)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested(); // 用户主动停止时向调用方传播取消，而不是返回残缺结果
        long total = Volatile.Read(ref bytes);
        ThrowIfPhaseAborted(end, total);
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
                using var resp = await SendWithHeaderTimeoutAsync(req, ct, DownloadHeaderTimeoutMs)
                    .ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    consecutiveFailures++; // 例如节点对请求尺寸返回 403
                    if (_node.Kind == SpeedServerKind.Ookla && sizeIdx < OoklaDownloadSizes.Length - 1)
                        sizeIdx++;
                    await Task.Delay(BackoffDelayMs(consecutiveFailures), ct).ConfigureAwait(false);
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
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 仅本次请求响应头超时（服务端无响应），按失败计退避重试，而不是整条流退出
                consecutiveFailures++;
                await DelayBackoffQuietlyAsync(consecutiveFailures, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // 中途断流等异常：退避后重试，连续失败达上限才退出，避免整条流报废
                consecutiveFailures++;
                await DelayBackoffQuietlyAsync(consecutiveFailures, ct).ConfigureAwait(false);
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

        var end = await RunSamplerAsync(sw, phaseCts,
            () => Volatile.Read(ref sent),
            () => Math.Min(1.0, sw.Elapsed.TotalSeconds / UploadSeconds),
            UploadSeconds, live, streams, UploadStallSeconds).ConfigureAwait(false);

        // 自然结束后不再发起新请求，但放行在途上传块继续发送并计数，避免尾部吞吐被低估
        await WaitStreamsGracefullyAsync(streams, ct,
            end == PhaseEnd.Normal ? TimeSpan.FromSeconds(6) : TimeSpan.FromSeconds(1.2)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        long total = Volatile.Read(ref sent);
        ThrowIfPhaseAborted(end, total);
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
                    await DelayBackoffQuietlyAsync(consecutiveFailures, ct).ConfigureAwait(false);
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

    /// <summary>采样循环的结束原因。</summary>
    private enum PhaseEnd
    {
        /// <summary>阶段正常走完设定时长。</summary>
        Normal,
        /// <summary>全部并发流已退出（连续失败达上限）——继续等待不会有任何数据。</summary>
        StreamsDead,
        /// <summary>数据流停滞：长时间没有任何新字节（连接挂起、服务端无响应）。</summary>
        Stalled
    }

    private async Task<PhaseEnd> RunSamplerAsync(
        Stopwatch sw,
        CancellationTokenSource phaseCts,
        Func<long> totalBytes,
        Func<double> progress,
        double totalSeconds,
        SpeedCallback? live,
        Task[] streams,
        double stallSeconds)
    {
        // 1 秒滑动窗口：上传按块离散到达、下载有响应间隙，瞬时差分会大幅抖动，
        // 窗口平均既平滑又贴近真实持续吞吐。
        const double windowSeconds = 1.0;
        var window = new Queue<(double T, long Bytes)>();
        long lastBytes = -1;
        double lastProgressAt = 0;
        var end = PhaseEnd.Normal;

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

                // 全部并发流已退出（节点连续拒绝/重置）→ 没有必要干等剩余时长，立即终止阶段
                bool allDead = true;
                foreach (var t in streams)
                    if (!t.IsCompleted) { allDead = false; break; }
                if (allDead)
                {
                    end = PhaseEnd.StreamsDead;
                    break;
                }

                // 数据流停滞：长时间无任何新字节（连接挂起/服务端不响应）→ 提前终止并明确报错
                if (stallSeconds > 0)
                {
                    if (b != lastBytes) { lastBytes = b; lastProgressAt = now; }
                    else if (now - lastProgressAt >= stallSeconds)
                    {
                        end = PhaseEnd.Stalled;
                        break;
                    }
                }

                if (now >= totalSeconds)
                    break;
            }
        }
        catch (OperationCanceledException) { }

        // 停止发起新请求（异常终止时同时中止挂起的在途请求），允许在途收尾后再统计
        phaseCts.Cancel();
        return end;
    }

    /// <summary>阶段异常终止（全部流退出 / 数据流停滞）时给出可行动的错误，而不是静默返回残缺速率。</summary>
    private void ThrowIfPhaseAborted(PhaseEnd end, long totalBytes)
    {
        switch (end)
        {
            case PhaseEnd.StreamsDead:
                throw new HttpRequestException(totalBytes > 0
                    ? $"{_node.Name} 节点中途多次拒绝或重置连接（可能被限流），请稍后重试或更换测速节点"
                    : $"{_node.Name} 节点拒绝了所有测速请求（可能被限流或拦截），请稍后重试或更换测速节点");
            case PhaseEnd.Stalled:
                throw new HttpRequestException($"{_node.Name} 节点数据流停滞（连接长时间无响应），请重试或更换测速节点");
        }
    }

    /// <summary>退避等待：期间用户取消则静默返回，由循环条件退出。</summary>
    private static async Task DelayBackoffQuietlyAsync(int consecutiveFailures, CancellationToken ct)
    {
        try { await Task.Delay(BackoffDelayMs(consecutiveFailures), ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
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
