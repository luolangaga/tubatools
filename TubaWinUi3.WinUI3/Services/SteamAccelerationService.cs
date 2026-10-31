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
    private static HttpMessageInvoker? _http;
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
    public static readonly string[] GithubDomains = ["github.com", "api.github.com", "raw.githubusercontent.com", "avatars.githubusercontent.com", "github.githubassets.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com", "codeload.github.com", "user-images.githubusercontent.com"];
    public static readonly string[] SteamDomains = ["steamcommunity.com", "www.steamcommunity.com", "store.steampowered.com", "help.steampowered.com", "checkout.steampowered.com", "community.akamai.steamstatic.com", "shared.akamai.steamstatic.com", "cdn.akamai.steamstatic.com", "avatars.akamai.steamstatic.com"];
    private static Dictionary<string, SteamAccelerationRule> _routes = new(StringComparer.OrdinalIgnoreCase);
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
            _routes = new(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in selected.Where(r => r.Available))
                foreach (var domain in rule.Domains) _routes[domain] = rule;
            _domains = _routes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Record recovery information before making either system change.
            _root = CreateRoot();
            File.WriteAllText(Journal, JsonSerializer.Serialize(new { Thumbprint = _root.Thumbprint }));
            if (!IsTest) using (var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser))
            { store.Open(OpenFlags.ReadWrite); store.Add(_root); }
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
            _http = TestHttpFactory?.Invoke() ?? new HttpMessageInvoker(new TlsTransport(new SocketsHttpHandler
            {
                UseProxy = false, UseCookies = false, AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                ConnectTimeout = TimeSpan.FromSeconds(10), PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectCallback = ConnectUpstreamAsync
            }));
            var traffic = new SteamTrafficSession(DateTimeOffset.Now);
            _traffic = traffic;
            var forwarder = app.Services.GetRequiredService<IHttpForwarder>();
            app.Run(async context =>
            {
                var host = context.Request.Host.Host;
                if (!_domains.Contains(host)) { context.Response.StatusCode = 421; return; }
                traffic.Request();
                if (!context.Request.IsHttps)
                { context.Response.Redirect("https://" + host + context.Request.PathBase + context.Request.Path + context.Request.QueryString); return; }
                var route = _routes[host];
                if (route.ProxyType == 1 && Uri.TryCreate("https://" + route.Forward.Split(';')[0], UriKind.Absolute, out var redirect))
                {
                    context.Response.Redirect(redirect.GetLeftPart(UriPartial.Authority) + context.Request.PathBase + context.Request.Path + context.Request.QueryString);
                    return;
                }
                var error = await forwarder.SendAsync(context, "https://" + host + "/", _http,
                    new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromMinutes(2), Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionExact }, HttpTransformer.Default);
                if (error != ForwarderError.None) { traffic.Fail(); if (!context.Response.HasStarted) context.Response.StatusCode = 502; }
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
            _http?.Dispose(); _http = null;
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
            _http?.Dispose(); _http = null; DisposeCertificates(); DnsCache.Clear();
            Status = "加速已停止，Hosts 与信任证书已还原";
        }
        finally { Gate.Release(); }
    }
    public static void RecoverOnStartup()
    {
        if (!File.Exists(Journal)) return;
        // Preserve all user / other program entries byte-for-byte outside our marked block.
        var bytes = File.ReadAllBytes(HostsPath);
        var clean = RemoveOwnedBlock(bytes);
        if (!bytes.AsSpan().SequenceEqual(clean)) File.WriteAllBytes(HostsPath, clean);
        using var json = JsonDocument.Parse(File.ReadAllText(Journal));
        string? thumbprint = json.RootElement.GetProperty("Thumbprint").GetString();
        if (!IsTest) SteamAccelerationCertificateStore.RemoveOwnedRoot(thumbprint);
        File.Delete(Journal);
        FlushDns();
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
    private static X509Certificate2 CreateRoot()
    {
        using var rsa = RSA.Create(3072);
        var req = new CertificateRequest(SteamAccelerationCertificateStore.Subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }
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

    private static async ValueTask<Stream> ConnectUpstreamAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var traffic = Volatile.Read(ref _traffic);
        string host = context.DnsEndPoint.Host;
        var route = _routes.GetValueOrDefault(host);
        string destination = string.IsNullOrWhiteSpace(route?.Forward) ? host : route.Forward.Split(';')[0];
        if (Uri.CheckHostName(destination) == UriHostNameType.Unknown) destination = host;
        var ips = IPAddress.TryParse(destination, out var direct) ? new[] { direct } : await ResolveAsync(destination, ct);
        foreach (var ip in ips.Where(ip => !IPAddress.IsLoopback(ip)))
        foreach (var sni in new[] { route?.Sni == "@domain" ? host : route?.Sni, ip.ToString(), destination, host }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct())
        {
            var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(6));
                await socket.ConnectAsync(ip, 443, timeout.Token);
                // An IP TargetHost omits DNS SNI. Validate the actual origin's hostname explicitly.
                var ssl = new SslStream(new NetworkStream(socket, true), false, (_, certificate, _, errors) =>
                    certificate is X509Certificate2 cert && (errors & ~SslPolicyErrors.RemoteCertificateNameMismatch) == SslPolicyErrors.None && (cert.MatchesHostname(host) || cert.MatchesHostname(destination)));
                try
                {
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = sni, ApplicationProtocols = [SslApplicationProtocol.Http11] }, timeout.Token);
                    return traffic.Meter(ssl, host, $"{ip}:443");
                }
                catch { await ssl.DisposeAsync(); throw; }
            }
            catch { socket.Dispose(); ct.ThrowIfCancellationRequested(); }
        }
        throw new HttpRequestException("无法连接目标网站的可用地址。");
    }
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
        foreach (var server in new[] { "https://dns.alidns.com/resolve", "https://doh.pub/dns-query" })
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, server + "?name=" + Uri.EscapeDataString(host) + "&type=A");
                request.Headers.Accept.ParseAdd("application/dns-json");
                using var response = await client.SendAsync(request, ct); response.EnsureSuccessStatusCode();
                using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (!data.RootElement.TryGetProperty("Answer", out var answer)) continue;
                var addresses = answer.EnumerateArray().Where(a => a.GetProperty("type").GetInt32() == 1).Select(a => IPAddress.Parse(a.GetProperty("data").GetString()!))
                    .Where(ip => !IPAddress.IsLoopback(ip) && ip.GetAddressBytes()[0] is not (0 or 10 or 127) && !ip.ToString().StartsWith("192.168.")).ToArray();
                if (addresses.Length == 0) continue;
                DnsCache[host] = (DateTime.UtcNow.AddMinutes(3), addresses); return addresses;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException) { ct.ThrowIfCancellationRequested(); }
        }
        throw new HttpRequestException("安全 DNS 解析失败：" + host);
    }
}
