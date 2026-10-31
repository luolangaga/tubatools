using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace TubaWinUi3.Services.Telemetry;

internal enum TelemetrySendResult
{
    /// <summary>已被服务端接收（含部分接收 206）。</summary>
    Delivered,

    /// <summary>网络/服务端临时故障，稍后重试。</summary>
    RetryLater,

    /// <summary>数据或密钥类错误，重试也不会成功，直接丢弃。</summary>
    Discard,
}

/// <summary>
/// 采集上传的 HTTP 传输层：走自定义 ConnectCallback 完成 DNS 容错
/// （详见 <see cref="TelemetryEndpointResolver"/>），并复用应用已配置的代理。
/// TLS 握手由 SocketsHttpHandler 在拿到连接流后自行完成，SNI/证书仍按原始域名校验。
/// </summary>
internal sealed class TelemetryTransport : IDisposable
{
    /// <summary>单个候选 IP 的连接预算：坏地址（被污染的黑洞 IP）只让它占用这么久。</summary>
    private static readonly TimeSpan PerAddressConnectTimeout = TimeSpan.FromSeconds(3);

    /// <summary>一次上传的端到端预算（含 DNS 探测与全部候选地址尝试）。</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(25);

    private readonly TelemetryEndpointResolver _resolver;
    private readonly Uri _trackUri;
    private readonly HttpClient _http;
    private readonly string _ingestToken;

    public TelemetryTransport(TelemetryEndpointResolver resolver, Uri trackUri, WebProxy? proxy, string ingestToken)
    {
        _ingestToken = ingestToken;
        _resolver = resolver;
        _trackUri = trackUri;

        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(12),
            ConnectCallback = ConnectAsync,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        if (proxy is not null)
        {
            handler.Proxy = proxy;
            handler.UseProxy = true;
        }

        _http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>上传一个已序列化（gzip）的遥测批次；任何情况下都不抛出异常。</summary>
    public async Task<TelemetrySendResult> PostAsync(byte[] gzipPayload, string contentEncoding, string contentType, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _trackUri)
            {
                Content = new ByteArrayContent(AxiomTelemetryPayload.ConvertBatch(gzipPayload, contentEncoding)),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _ingestToken);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Content.Headers.ContentEncoding.Add("gzip");

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(RequestTimeout);

            using var response = await _http.SendAsync(request, budget.Token).ConfigureAwait(false);
            var result = ClassifyResponse((int)response.StatusCode);
            if (result != TelemetrySendResult.Delivered) return result;
            var body = await response.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
            return AxiomTelemetryPayload.IsFullyAccepted(body)
                ? TelemetrySendResult.Delivered : TelemetrySendResult.RetryLater;
        }
        catch (System.Text.Json.JsonException)
        {
            return TelemetrySendResult.Discard; // 损坏的离线条目不能无限重试
        }
        catch (InvalidDataException)
        {
            return TelemetrySendResult.Discard;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return TelemetrySendResult.RetryLater;
        }
        catch
        {
            // 连接被拒/超时/DNS 异常等统归为临时故障
            _resolver.InvalidateCache();
            return TelemetrySendResult.RetryLater;
        }
    }

    /// <summary>
    /// 响应码归类（纯函数，可单测）：
    /// 200/206 视为送达；400/401/403/404/413/439 为数据或密钥错误，重试无意义；
    /// 其余（429/5xx 等）稍后重试。
    /// </summary>
    internal static TelemetrySendResult ClassifyResponse(int statusCode) => statusCode switch
    {
        >= 200 and < 300 => TelemetrySendResult.Delivered,
        400 or 401 or 403 or 404 or 413 or 439 => TelemetrySendResult.Discard,
        _ => TelemetrySendResult.RetryLater,
    };

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        // 走代理时这里的目标是代理服务器，按系统默认方式连接即可
        if (!string.Equals(host, _resolver.Host, StringComparison.OrdinalIgnoreCase))
            return await ConnectDirectAsync(host, port, ct).ConfigureAwait(false);

        var candidates = await _resolver.GetCandidatesAsync(ct).ConfigureAwait(false);
        if (candidates.Count == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        Exception? lastError = null;
        foreach (var address in candidates)
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(PerAddressConnectTimeout);

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), attempt.Token).ConfigureAwait(false);
                _resolver.ReportSuccess(address);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                socket.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                socket.Dispose();
            }
        }

        _resolver.InvalidateCache();
        throw lastError ?? new SocketException((int)SocketError.HostNotFound);
    }

    private static async ValueTask<Stream> ConnectDirectAsync(string host, int port, CancellationToken ct)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(host, port, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public void Dispose() => _http.Dispose();
}
