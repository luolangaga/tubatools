using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TubaWinUi3.Services;

/// <summary>LAN endpoints use TLS without machine-wide HTTP.sys certificate bindings.</summary>
internal sealed class LanHttpsServer : IDisposable
{
    private readonly WebApplication _app;
    private static readonly Lazy<X509Certificate2> Identity = new(LoadIdentity);
    internal static string Fingerprint => Identity.Value.GetCertHashString(HashAlgorithmName.SHA256);
    internal static bool MatchesCertificate(string pem)
    {
        try
        {
            using var certificate = X509Certificate2.CreateFromPem(pem);
            return certificate.GetCertHashString(HashAlgorithmName.SHA256) == Fingerprint;
        }
        catch (CryptographicException) { return false; }
    }

    private LanHttpsServer(WebApplication app) => _app = app;

    internal static async Task<LanHttpsServer> StartAsync(int port, long maxBodyBytes,
        Func<HttpContext, Task> handle, CancellationToken cancellationToken = default,
        X509Certificate2? certificate = null, IPAddress? bindAddress = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders(); // Do not log connection links or credentials.
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = maxBodyBytes;
            options.Limits.MaxConcurrentConnections = 32;
            options.Limits.MaxConcurrentUpgradedConnections = 0;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            options.Listen(bindAddress ?? IPAddress.Any, port, listen => listen.UseHttps(https =>
            {
                https.ServerCertificate = certificate ?? Identity.Value;
                https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            }));
        });
        var app = builder.Build();
        var slots = new SemaphoreSlim(16, 16);
        app.Run(async context =>
        {
            if (!await slots.WaitAsync(0, context.RequestAborted))
            { context.Response.StatusCode = 429; return; }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            // Includes actual body consumption, not just Content-Length. Enough for large LAN transfers.
            deadline.CancelAfter(TimeSpan.FromMinutes(15));
            context.RequestAborted = deadline.Token;
            try
            {
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["Cache-Control"] = "no-store";
                await handle(context);
            }
            catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
            { context.Response.StatusCode = ex.StatusCode; }
            catch (OperationCanceledException) { context.Abort(); }
            finally { slots.Release(); }
        });
        try { await app.StartAsync(cancellationToken).ConfigureAwait(false); }
        catch { await app.DisposeAsync(); throw; }
        return new LanHttpsServer(app);
    }

    public void Dispose()
    {
        // Never block the UI synchronization context on asynchronous server shutdown.
        Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await _app.StopAsync(timeout.Token); }
            catch (OperationCanceledException) { }
            finally { await _app.DisposeAsync(); }
        }).GetAwaiter().GetResult();
    }

    private static X509Certificate2 LoadIdentity()
    {
        var suffix = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            Path.GetFullPath(ConfigManager.GetDataDir()).ToUpperInvariant())))[..16];
        var subject = "CN=TubaWinUi3 LAN " + suffix;
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        var saved = store.Certificates.Cast<X509Certificate2>().FirstOrDefault(c =>
            c.Subject == subject && c.HasPrivateKey && c.NotAfter > DateTime.Now.AddDays(7));
        if (saved is not null) return saved;

        using var key = RSA.Create(3072);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new System.Security.Cryptography.OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
        foreach (var ip in PhoneLinkService.GetLocalIps())
            if (IPAddress.TryParse(ip, out var address)) names.AddIpAddress(address);
        request.CertificateExtensions.Add(names.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        // Persist only in the current user's personal store, never install a trusted root.
        var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);
        store.Add(certificate);
        return certificate;
    }
}
