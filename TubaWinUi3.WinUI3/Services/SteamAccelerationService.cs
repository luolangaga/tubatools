using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Forwarder;

namespace TubaWinUi3.Services;

// Local reverse proxy design follows Watt Toolkit / FastGithub: independent DNS,
// direct TLS transport, hostname validation and YARP streaming forwarding.
public static class SteamAccelerationService
{
    // Isolated integration tests use temporary files, high ports and no system trust changes.
    internal static string? TestDirectory;
    internal static string? TestHostsPath;
    internal static int TestHttpPort = 80, TestHttpsPort = 443;
    internal static Func<HttpMessageInvoker>? TestHttpFactory;
    private static bool IsTest => TestDirectory != null && TestHostsPath != null;
    private const string Begin = "# TubaSteamAcceleration BEGIN";
    private const string End = "# TubaSteamAcceleration END";
    private static readonly SemaphoreSlim Gate = new(1);
    private static WebApplication? _app;
    private static readonly Dictionary<SteamAccelerationRule, HttpMessageInvoker> Clients = new();
    private static SteamTrafficSession _traffic = new(DateTimeOffset.Now);
    private static System.Threading.Timer? _trafficTimer;
    public static SteamTrafficSnapshot Traffic => Volatile.Read(ref _traffic).Snapshot();
    private static X509Certificate2? _root;
    private static readonly ConcurrentDictionary<string, X509Certificate2> Leaves = new();
    private static readonly ConcurrentDictionary<string, (DateTime Expiry, IPAddress[] Addresses)> DnsCache = new();
    private static string DataDir => TestDirectory ?? Path.Combine(ConfigManager.GetDataDir(), "SteamAcceleration");
    private static string Journal => Path.Combine(DataDir, "active.json");
    private static string HostsPath => TestHostsPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
    public static bool Running => _app != null;
    public static string Status { get; private set; } = "加速未开启";
    public static string LastFailure { get; private set; } = "";
    public static readonly string[] GithubDomains = ["github.com", "api.github.com", "raw.githubusercontent.com", "avatars.githubusercontent.com", "github.githubassets.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com", "codeload.github.com", "user-images.githubusercontent.com"];
    public static readonly string[] SteamDomains = ["steamcommunity.com", "www.steamcommunity.com", "store.steampowered.com", "help.steampowered.com", "checkout.steampowered.com", "community.akamai.steamstatic.com", "shared.akamai.steamstatic.com", "cdn.akamai.steamstatic.com", "avatars.akamai.steamstatic.com"];
    private static IReadOnlyList<SteamAccelerationRule> _routes = [];
    private static Dictionary<string, SteamAccelerationRule> _fallbacks = new(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> _domains = new(StringComparer.OrdinalIgnoreCase);

    public static Task StartAsync(bool github, bool steam) => StartAsync(new[]
    {
        new SteamAccelerationRule { Name = "GitHub", Domains = github ? GithubDomains : [] },
        new SteamAccelerationRule { Name = "Steam", Domains = steam ? SteamDomains : [] }
    }.Where(r => r.Domains.Length > 0).ToList());
    public static async Task StartAsync(IReadOnlyList<SteamAccelerationRule> selected)
    {
        await Gate.WaitAsync();
        try
        {
            if (Running) return;
            if (selected.Count == 0) throw new ArgumentException("请至少选择一项加速服务。");
            RecoverOnStartup();
            Directory.CreateDirectory(DataDir);
            _routes = selected.Where(r => r.Available).ToArray();
            _domains = _routes.SelectMany(r => r.Domains).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (_domains.Count == 0) throw new ArgumentException("请至少选择一项可用的加速服务。");
            _fallbacks = _domains.ToDictionary(d => d, d => new SteamAccelerationRule { Domains = [d] }, StringComparer.OrdinalIgnoreCase);
            LastFailure = "";
            // 根证书只信任一次并由 DPAPI 保护的文件长期复用，暂停/退出不再卸载。
            _root = SteamAccelerationCertificateStore.EnsureRoot(DataDir, !IsTest);
            // Record recovery information before changing the Hosts file.
            File.WriteAllText(Journal, "{\"Version\":2}");
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders(); // Never log URLs, request headers or cookies.
            if (IsTest) builder.Logging.AddConsole();
            builder.WebHost.ConfigureKestrel(server =>
            {
                server.AddServerHeader = false;
                server.Limits.MaxRequestBodySize = 1024L * 1024 * 1024;
                server.Listen(IPAddress.Loopback, TestHttpPort, listen => listen.Protocols = HttpProtocols.Http1);
                server.Listen(IPAddress.Loopback, TestHttpsPort, listen =>
                {
                    listen.Protocols = HttpProtocols.Http1AndHttp2;
                    listen.UseHttps(https => https.ServerCertificateSelector = (_, host) => host != null && _domains.Contains(host) ? GetLeaf(host) : null);
                });
            });
            builder.Services.AddHttpForwarder();
            var app = builder.Build();
            var traffic = new SteamTrafficSession(DateTimeOffset.Now);
            _traffic = traffic;
            foreach (var rule in _routes.Concat(_fallbacks.Values).Distinct())
                Clients[rule] = TestHttpFactory?.Invoke() ?? new HttpMessageInvoker(new TlsTransport(new SocketsHttpHandler
                {
                    UseProxy = false, UseCookies = false, AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.None,
                    ConnectTimeout = TimeSpan.FromSeconds(30), PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                    ConnectCallback = (context, ct) => ConnectUpstreamAsync(context, rule, traffic, ct)
                }));
            var forwarder = app.Services.GetRequiredService<IHttpForwarder>();
            app.Run(async context =>
            {
                var host = context.Request.Host.Host;
                if (!_domains.Contains(host)) { context.Response.StatusCode = 421; return; }
                traffic.Request();
                if (!context.Request.IsHttps)
                { context.Response.Redirect("https://" + host + context.Request.PathBase + context.Request.Path + context.Request.QueryString); return; }
                var route = SteamAccelerationRouting.Select(_routes, host, context.Request.Path.Value ?? "/") ?? _fallbacks[host];
                if (route.ProxyType is 2 or 3)
                { context.Response.StatusCode = route.ProxyType == 2 ? 200 : 403; return; }
                if (route.ProxyType == 1 && Uri.TryCreate("https://" + route.Forward.Split(';')[0], UriKind.Absolute, out var redirect))
                {
                    context.Response.Redirect(redirect.GetLeftPart(UriPartial.Authority) + context.Request.PathBase + context.Request.Path + context.Request.QueryString);
                    return;
                }
                var error = await forwarder.SendAsync(context, "https://" + host + "/", Clients[route],
                    new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromMinutes(2), Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact }, new SteamAccelerationTransformer(route));
                if (!context.RequestAborted.IsCancellationRequested && (error != ForwarderError.None || context.Response.StatusCode >= 400))
                {
                    traffic.Fail();
                    if (error != ForwarderError.None && !context.Response.HasStarted) context.Response.StatusCode = 502;
                    var cause = context.Features.Get<IForwarderErrorFeature>()?.Exception?.GetBaseException();
                    string reason = cause switch
                    {
                        System.Security.Authentication.AuthenticationException => "TLS 证书验证失败",
                        SocketException => "上游连接失败",
                        OperationCanceledException => "上游连接超时",
                        _ => "上游转发失败"
                    };
                    LastFailure = $"{host} · HTTP {context.Response.StatusCode}" + (error == ForwarderError.None ? " · 网站返回错误" : $" · {reason}（{error}）");
                }
            });
            try
            {
                await app.StartAsync();
                // Only redirect names once the listeners are live.
                WriteHosts(_domains);
                _app = app;
                _trafficTimer = new System.Threading.Timer(_ => traffic.Sample(DateTimeOffset.Now), null, 1000, 1000);
                Status = $"加速运行中 · {_domains.Count} 个域名 · 本机反向代理";
            }
            catch { await app.DisposeAsync(); throw; }
        }
        catch
        {
            DisposeClients();
            RecoverOnStartup();
            DisposeCertificates();
            throw;
        }
        finally { Gate.Release(); }
    }
    public static void Shutdown()
    {
        if (!Running) return;
        RecoverOnStartup();
        _trafficTimer?.Dispose(); _trafficTimer = null;
        _ = _app!.StopAsync();
    }
    public static async Task StopAsync()
    {
        await Gate.WaitAsync();
        try
        {
            // Restore name resolution before taking the listeners down.
            RecoverOnStartup();
            var app = _app; _app = null;
            _trafficTimer?.Dispose(); _trafficTimer = null;
            if (app != null) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); try { await app.StopAsync(timeout.Token); } finally { await app.DisposeAsync(); } }
            DisposeClients(); DisposeCertificates(); DnsCache.Clear();
            Status = "加速已暂停，Hosts 已还原 · 信任证书保持有效";
        }
        finally { Gate.Release(); }
    }
    private static readonly object RecoveryGate = new();
    public static void RecoverOnStartup()
    {
        // 启动恢复在后台线程执行，可能和用户立刻点击「开始加速」（StartAsync 内也会调用）
        // 同时到达；串行化，避免两次删 Hosts 块互相踩踏。
        lock (RecoveryGate)
        {
            if (!File.Exists(Journal)) return;
            // 只还原 Hosts：根证书保持信任，避免每次暂停/退出都卸载后要求重新确认。
            // Preserve all user / other program entries byte-for-byte outside our marked block.
            var bytes = File.ReadAllBytes(HostsPath);
            var clean = RemoveOwnedBlock(bytes);
            if (!bytes.AsSpan().SequenceEqual(clean)) File.WriteAllBytes(HostsPath, clean);
            File.Delete(Journal);
            FlushDns();
        }
    }
    internal static byte[] RemoveOwnedBlock(byte[] input)
    {
        var start = input.AsSpan().IndexOf(Encoding.ASCII.GetBytes(Begin));
        if (start < 0) return input;
        var tail = input.AsSpan(start).IndexOf(Encoding.ASCII.GetBytes(End));
        if (tail < 0) throw new IOException("加速器 Hosts 标记不完整，请先修复 Hosts 后重试。");
        int end = start + tail + End.Length;
        if (end < input.Length && input[end] == '\r') end++;
        if (end < input.Length && input[end] == '\n') end++;
        return input.AsSpan(0, start).ToArray().Concat(input.AsSpan(end).ToArray()).ToArray();
    }
    private static void WriteHosts(IEnumerable<string> domains)
    {
        byte[] original = RemoveOwnedBlock(File.ReadAllBytes(HostsPath));
        var append = Encoding.ASCII.GetBytes("\r\n" + Begin + "\r\n" + string.Join("\r\n", domains.Select(d => "127.0.0.1 " + d)) + "\r\n" + End + "\r\n");
        File.WriteAllBytes(HostsPath, original.Concat(append).ToArray());
        FlushDns();
    }
    [System.Runtime.InteropServices.DllImport("dnsapi.dll")]
    private static extern bool DnsFlushResolverCache();
    private static void FlushDns() { if (!IsTest) DnsFlushResolverCache(); }
    private static X509Certificate2 GetLeaf(string host) => Leaves.GetOrAdd(host, domain =>
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=" + domain, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName(domain); req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var eku = new OidCollection { new("1.3.6.1.5.5.7.3.1") }; req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, false));
        using var cert = req.Create(_root!, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(7), RandomNumberGenerator.GetBytes(16));
        using var withKey = cert.CopyWithPrivateKey(rsa);
        // Schannel needs an imported key handle for dynamically generated server certificates.
        return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.DefaultKeySet);
    });
    private static void DisposeCertificates()
    { foreach (var cert in Leaves.Values) cert.Dispose(); Leaves.Clear(); _root?.Dispose(); _root = null; }
    private static void DisposeClients()
    { foreach (var client in Clients.Values) client.Dispose(); Clients.Clear(); }

    private static async ValueTask<Stream> ConnectUpstreamAsync(SocketsHttpConnectionContext context, SteamAccelerationRule route, SteamTrafficSession traffic, CancellationToken ct)
    {
        string host = context.DnsEndPoint.Host;
        Exception? lastError = null;
        // A stale CDN endpoint must not make a healthy origin permanently unreachable.
        foreach (string destination in route.Forward.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(d => Uri.CheckHostName(d) != UriHostNameType.Unknown).Append(host).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IPAddress[] ips;
            try { ips = IPAddress.TryParse(destination, out var direct) ? [direct] : await ResolveAsync(destination, ct); }
            catch (Exception ex) when (ex is HttpRequestException or SocketException) { lastError = ex; continue; }
            foreach (var ip in ips.Where(IsPublicAddress))
            foreach (var sni in new[] { destination == host ? host : route.Sni == "@domain" ? host : route.Sni, ip.ToString(), destination, host }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct())
            {
                var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(6));
                    await socket.ConnectAsync(ip, 443, timeout.Token);
                    // An IP TargetHost omits DNS SNI. Validate the actual origin's hostname explicitly.
                    var ssl = new SslStream(new NetworkStream(socket, true), false, (_, certificate, _, errors) =>
                        certificate is X509Certificate2 cert && IsValidUpstreamCertificate(cert, errors, host, destination, sni!));
                    try
                    {
                        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = sni, ApplicationProtocols = [SslApplicationProtocol.Http11] }, timeout.Token);
                        return traffic.Meter(ssl, host, $"{ip}:443");
                    }
                    catch { await ssl.DisposeAsync(); throw; }
                }
                catch (Exception ex) { socket.Dispose(); ct.ThrowIfCancellationRequested(); lastError = ex; }
            }
        }
        throw new HttpRequestException("无法连接目标网站的可用地址。", lastError);
    }
    internal static bool IsValidUpstreamCertificate(X509Certificate2 certificate, SslPolicyErrors errors, string host, string destination, string sni)
        => (errors & ~SslPolicyErrors.RemoteCertificateNameMismatch) == SslPolicyErrors.None &&
            (certificate.MatchesHostname(host) || certificate.MatchesHostname(destination) || certificate.MatchesHostname(sni));
    // YARP uses HTTPS destinations. Rewrite to HTTP because ConnectCallback already
    // supplies a fully authenticated TLS stream; this avoids a second TLS handshake.
    private sealed class TlsTransport : DelegatingHandler
    {
        public TlsTransport(HttpMessageHandler inner) : base(inner) { }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var original = request.RequestUri!;
            request.Headers.Host = original.Host;
            request.RequestUri = new UriBuilder(original) { Scheme = "http", Port = 443 }.Uri;
            return base.SendAsync(request, cancellationToken);
        }
    }
    private static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        if (DnsCache.TryGetValue(host, out var cached) && cached.Expiry > DateTime.UtcNow) return cached.Addresses;
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(8) };
        foreach (var server in new[] { "https://dns.alidns.com/resolve", "https://doh.pub/dns-query", "https://cloudflare-dns.com/dns-query" })
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, server + "?name=" + Uri.EscapeDataString(host) + "&type=A");
                request.Headers.Accept.ParseAdd("application/dns-json");
                using var response = await client.SendAsync(request, ct); response.EnsureSuccessStatusCode();
                using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (!data.RootElement.TryGetProperty("Answer", out var answer)) continue;
                var addresses = answer.EnumerateArray().Where(a => a.GetProperty("type").GetInt32() == 1).Select(a => IPAddress.Parse(a.GetProperty("data").GetString()!))
                    .Where(IsPublicAddress).ToArray();
                if (addresses.Length == 0) continue;
                DnsCache[host] = (DateTime.UtcNow.AddMinutes(3), addresses); return addresses;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException) { ct.ThrowIfCancellationRequested(); }
        }
        // Only a public address is usable: the OS may return our own hosts entry.
        try
        {
            var addresses = (await Dns.GetHostAddressesAsync(host, ct)).Where(IsPublicAddress).ToArray();
            if (addresses.Length > 0) return addresses;
        }
        catch (SocketException) { }
        throw new HttpRequestException("安全 DNS 解析失败：" + host);
    }
    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return false;
        var bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork
            ? bytes[0] is not (0 or 10 or 127) && bytes[0] < 224 && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) && !(bytes[0] == 192 && bytes[1] == 168) && !(bytes[0] == 169 && bytes[1] == 254)
            : (bytes[0] & 0xfe) != 0xfc && bytes[0] != 0xff;
    }
}
