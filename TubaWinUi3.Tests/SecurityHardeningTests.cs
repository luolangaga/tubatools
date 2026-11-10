extern alias backend;
using System.Net;
using System.Net.Http;
using System.IO.Pipes;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using Microsoft.AspNetCore.Http;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public sealed class SecurityHardeningTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TubaSecurityTests-" + Guid.NewGuid().ToString("N"));
    private readonly object? _previousRoot = Field("_shareDir").GetValue(null);
    private readonly object? _previousToken = Field("_accessToken").GetValue(null);
    private readonly Func<long>? _previousSpace = LanFileShareService.AvailableSpaceForTests;
    private const string Token = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private string Share => Path.Combine(_root, "share");
    private static FieldInfo Field(string name) => typeof(LanFileShareService).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    public SecurityHardeningTests()
    {
        Directory.CreateDirectory(Share);
        Field("_shareDir").SetValue(null, Share);
        Field("_accessToken").SetValue(null, Token);
        LanFileShareService.AvailableSpaceForTests = () => 100L << 30;
    }
    public void Dispose()
    {
        Field("_shareDir").SetValue(null, _previousRoot);
        Field("_accessToken").SetValue(null, _previousToken);
        LanFileShareService.AvailableSpaceForTests = _previousSpace;
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    private static DefaultHttpContext Context(string path, string method = "GET", string? body = null, bool authorized = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https"; context.Request.Host = new HostString("127.0.0.1", 18080);
        context.Request.Path = path; context.Request.Method = method;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body ?? ""));
        context.Response.Body = new MemoryStream();
        if (authorized) context.Request.Headers.Cookie = "__Host-tuba-share=" + Token;
        return context;
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("nested/../../outside.txt")]
    [InlineData("C:\\outside.txt")]
    [InlineData("\\\\server\\share\\file")]
    [InlineData("/outside.txt")]
    [InlineData("nested/file:stream")]
    [InlineData("nested/../file")]
    [InlineData("NUL.txt")]
    [InlineData("nested/COM1")]
    [InlineData("nested/COM¹.txt")]
    [InlineData("nested./file")]
    [InlineData("nested /file")]
    [InlineData("")]
    public void Paths_RejectEscapesAndWindowsAliases(string relative) =>
        Assert.Throws<UnauthorizedAccessException>(() => SafeSharePath.Resolve(Share, relative));

    [Fact]
    public void Paths_AcceptNestedUnicodeName() =>
        Assert.Equal(Path.Combine(Share, "目录", "文件.txt"), SafeSharePath.Resolve(Share, "目录/文件.txt"));

    [Fact]
    public void Paths_RejectJunctionsWithoutFollowingTheirTargets()
    {
        var outside = Path.Combine(_root, "outside"); Directory.CreateDirectory(outside);
        var link = Path.Combine(Share, "linked");
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, outside }) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!; process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        try { Assert.Throws<UnauthorizedAccessException>(() => SafeSharePath.Resolve(Share, "linked/file.txt")); }
        finally { Directory.Delete(link); } // Remove the junction itself, never its target recursively.
    }

    [Theory]
    [InlineData("/api/files", "GET")]
    [InlineData("/api/files", "DELETE")]
    [InlineData("/api/upload", "POST")]
    [InlineData("/api/folder", "POST")]
    [InlineData("/api/clear", "DELETE")]
    [InlineData("/download/test.txt", "GET")]
    [InlineData("/api/info", "GET")]
    [InlineData("/qr", "GET")]
    public async Task Share_RejectsUnauthenticatedOperations(string path, string method)
    {
        var context = Context(path, method, authorized: false);
        await LanFileShareService.HandleRequest(context);
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task Share_OriginalReadWriteDeleteAttacksAreBlocked()
    {
        var outside = Path.Combine(_root, "outside.txt"); File.WriteAllText(outside, "SAFE");
        var read = Context("/download/..%5Coutside.txt");
        await LanFileShareService.HandleRequest(read); Assert.Equal(403, read.Response.StatusCode);
        var write = Context("/api/upload", "POST", "UNSAFE");
        write.Request.Headers["X-File-Name"] = "..%5Coutside.txt";
        await LanFileShareService.HandleRequest(write); Assert.Equal(403, write.Response.StatusCode);
        var delete = Context("/api/files", "DELETE", "{\"path\":\"../outside.txt\"}");
        await LanFileShareService.HandleRequest(delete); Assert.Equal(403, delete.Response.StatusCode);
        Assert.Equal("SAFE", File.ReadAllText(outside));
    }

    [Theory]
    [InlineData("../outside.txt", 403)]
    [InlineData("目录/valid.txt", 200)]
    public async Task Share_MultipartUsesTheSamePathBoundary(string name, int status)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("CONTENT"), "files", name);
        var context = Context("/api/upload", "POST");
        context.Request.ContentType = multipart.Headers.ContentType!.ToString();
        context.Request.Body = await multipart.ReadAsStreamAsync();
        await LanFileShareService.HandleRequest(context);
        Assert.Equal(status, context.Response.StatusCode);
        Assert.False(File.Exists(Path.Combine(_root, "outside.txt")));
        if (status == 200) Assert.Equal("CONTENT", File.ReadAllText(Path.Combine(Share, "目录", "valid.txt")));
    }

    [Fact]
    public async Task Share_LowDiskSpaceRejectsWritesAndRemovesPartialFiles()
    {
        LanFileShareService.AvailableSpaceForTests = () => 1L << 30;
        var context = Context("/api/upload", "POST", "CONTENT");
        context.Request.Headers["X-File-Name"] = "too-large.txt";
        var exception = await Assert.ThrowsAsync<BadHttpRequestException>(() => LanFileShareService.HandleRequest(context));
        Assert.Equal(413, exception.StatusCode);
        Assert.Empty(Directory.GetFiles(Share));
    }

    [Fact]
    public async Task Share_SessionCookieIsSecureAndRevocationWorks()
    {
        var session = Context("/api/session", "POST", "{\"token\":\"" + Token + "\"}", authorized: false);
        await LanFileShareService.HandleRequest(session);
        Assert.Equal(200, session.Response.StatusCode);
        var cookie = session.Response.Headers.SetCookie.ToString().ToLowerInvariant();
        Assert.Contains("httponly", cookie); Assert.Contains("secure", cookie); Assert.Contains("samesite=strict", cookie);
        Field("_accessToken").SetValue(null, "");
        var revoked = Context("/api/files"); await LanFileShareService.HandleRequest(revoked);
        Assert.Equal(401, revoked.Response.StatusCode);
    }

    [Fact]
    public async Task Share_RejectsForeignOriginEvenWithCookie()
    {
        var context = Context("/api/clear", "DELETE"); context.Request.Headers.Origin = "https://evil.invalid";
        await LanFileShareService.HandleRequest(context); Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task Json_UnknownLengthRequestIsStillBounded()
    {
        var context = Context("/api/pair", "POST", new string('x', RequestLimits.MaxJsonBytes + 1));
        context.Request.ContentLength = null;
        var exception = await Assert.ThrowsAsync<BadHttpRequestException>(() => RequestLimits.ReadJsonAsync(context.Request));
        Assert.Equal(413, exception.StatusCode);
    }

    [Fact]
    public async Task Streams_EnforceActualBytesAndCancellation()
    {
        using var source = new MemoryStream(new byte[9]); using var destination = new MemoryStream();
        var exception = await Assert.ThrowsAsync<BadHttpRequestException>(() => RequestLimits.CopyBoundedAsync(source, destination, 8, default));
        Assert.Equal(413, exception.StatusCode); Assert.True(destination.Length <= 8);
        source.Position = 0; using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RequestLimits.CopyBoundedAsync(source, destination, 20, cancel.Token));
    }

    [Fact]
    public async Task Downloads_RejectTamperingAndMissingDigest()
    {
        using var file = new MemoryStream("EXPECTED"u8.ToArray());
        var hash = Convert.ToHexString(SHA256.HashData(file.ToArray()));
        await TrustedDownloads.VerifyAsync(file, hash, default);
        file.WriteByte(0);
        await Assert.ThrowsAsync<InvalidDataException>(() => TrustedDownloads.VerifyAsync(file, hash, default));
        Assert.Throws<InvalidDataException>(() => TrustedDownloads.ParseSha256(null));
        Assert.Throws<InvalidDataException>(() => TrustedDownloads.ParseSha256("md5:abcd"));
    }

    [Fact]
    public async Task Updates_ArbitraryMzFileIsNotAnOfficialPackage()
    {
        var file = Path.Combine(_root, "fake.exe"); await File.WriteAllBytesAsync(file, "MZfake"u8.ToArray());
        Assert.False(UpdateService.IsInstallerFileValid(file));
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePackageTrust.OpenVerifiedAsync(file, default));
    }

    [Fact]
    public void Updates_StagingIsOwnedByAdministratorsAndRejectsUserWritableAcl()
    {
        var security = UpdateStaging.CreateSecurity();
        Assert.True(UpdateStaging.IsProtected(security));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            FileSystemRights.Write, AccessControlType.Allow));
        Assert.False(UpdateStaging.IsProtected(security));
    }

    [Fact]
    public void Pipe_OnlySystemAndElevatedAdministratorsReceiveAccess()
    {
        var security = backend::TubaWinUI3.BackEnd.NamedPipeBackendServer.CreatePipeSecurity();
        Assert.True(security.AreAccessRulesProtected);
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToArray();
        Assert.Equal(2, rules.Length);
        Assert.All(rules, rule => Assert.True(rule.IdentityReference.Value is "S-1-5-18" or "S-1-5-32-544"));
    }

    [Fact]
    public async Task Tls_RequiresTheExpectedCertificateAndRejectsCleartext()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Windows SChannel requires an imported key container for the TLS server credential.
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null,
            X509KeyStorageFlags.UserKeySet);
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        using var server = await LanHttpsServer.StartAsync(port, 1024,
            context => context.Response.WriteAsync("TLS_OK"), certificate: certificate, bindAddress: IPAddress.Loopback);
        using var correctHandler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert?.Thumbprint == certificate.Thumbprint };
        using var correct = new HttpClient(correctHandler);
        Assert.Equal("TLS_OK", await correct.GetStringAsync($"https://127.0.0.1:{port}/"));
        using var normal = new HttpClient();
        await Assert.ThrowsAsync<HttpRequestException>(() => normal.GetStringAsync($"https://127.0.0.1:{port}/"));
        try
        {
            var cleartext = await normal.GetAsync($"http://127.0.0.1:{port}/");
            Assert.False(cleartext.IsSuccessStatusCode);
        }
        catch (HttpRequestException) { /* TLS-only listeners may close a plaintext connection outright. */ }
    }
}
