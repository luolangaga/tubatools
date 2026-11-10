using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// 「网站加速」仍在主程序内（不属于已迁移到插件的 Steam 工具箱页面）。
/// 这里只覆盖加速代理的 Hosts 回滚、路由与启动恢复。
/// </summary>
public sealed class SteamToolboxTests
{
    [Fact]
    public void Accelerator_HostsRollbackPreservesOtherEntries()
    {
        var before = System.Text.Encoding.UTF8.GetBytes("127.0.0.1 localhost\r\n# 用户条目\r\n");
        var owned = System.Text.Encoding.ASCII.GetBytes("# TubaSteamAcceleration BEGIN\r\n127.0.0.1 github.com\r\n# TubaSteamAcceleration END\r\n");
        var after = System.Text.Encoding.ASCII.GetBytes("192.0.2.1 example.test\r\n");
        Assert.Equal(before.Concat(after), SteamAccelerationService.RemoveOwnedBlock(before.Concat(owned).Concat(after).ToArray()));
        Assert.Equal(before, SteamAccelerationService.RemoveOwnedBlock(before));
    }

    [Fact]
    public void Accelerator_RefusesToEraseAnIncompleteHostsBlock()
    {
        Assert.Throws<IOException>(() => SteamAccelerationService.RemoveOwnedBlock(System.Text.Encoding.ASCII.GetBytes("# TubaSteamAcceleration BEGIN\r\nuser entry")));
    }

    [Fact]
    public async Task Accelerator_StartsTlsProxyForwardsAndRestoresHosts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "steam-proxy-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var hosts = Path.Combine(directory, "hosts");
        File.WriteAllText(hosts, "127.0.0.1 localhost\r\n");
        int FreePort()
        {
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start(); return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        SteamAccelerationService.TestDirectory = directory;
        SteamAccelerationService.TestHostsPath = hosts;
        SteamAccelerationService.TestHttpPort = FreePort();
        SteamAccelerationService.TestHttpsPort = FreePort();
        SteamAccelerationService.TestHttpFactory = () => new System.Net.Http.HttpMessageInvoker(new FixtureHandler());
        try
        {
            await SteamAccelerationService.StartAsync([
                new SteamAccelerationRule { Domains = ["github.com"] },
                new SteamAccelerationRule { Id = "scoped", Domains = ["github.com"], MatchPatterns = ["https://github.com/scoped/*"], UserAgent = "Chrome/150; ${origin}" }
            ]);
            Assert.True(SteamAccelerationService.Running);
            Assert.Contains("127.0.0.1 github.com", File.ReadAllText(hosts));
            using var handler = new System.Net.Http.SocketsHttpHandler
            {
                UseProxy = false,
                ConnectCallback = async (_, token) =>
                {
                    var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                    await socket.ConnectAsync(System.Net.IPAddress.Loopback, SteamAccelerationService.TestHttpsPort, token);
                    return new System.Net.Sockets.NetworkStream(socket, true);
                },
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    // Trust only our test certificate; never install it in a Windows trust store.
                    RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is System.Security.Cryptography.X509Certificates.X509Certificate2 cert && cert.Subject == "CN=github.com" && cert.MatchesHostname("github.com")
                }
            };
            using var client = new System.Net.Http.HttpClient(handler);
            using var response = await client.GetAsync("https://github.com/test?q=1");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("https://github.com/test?q=1", await response.Content.ReadAsStringAsync());
            using var scopedRequest = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://github.com/scoped/test");
            scopedRequest.Headers.UserAgent.ParseAdd("ValveSteamClient/1.0");
            scopedRequest.Headers.Add("X-Forwarded-For", "127.0.0.1");
            using var scopedResponse = await client.SendAsync(scopedRequest);
            Assert.Equal("github.com", Assert.Single(scopedResponse.Headers.GetValues("X-Observed-Host")));
            Assert.Equal("Chrome/150; ValveSteamClient/1.0", Assert.Single(scopedResponse.Headers.GetValues("X-Observed-UA")));
            Assert.Equal("False", Assert.Single(scopedResponse.Headers.GetValues("X-Observed-Forwarded")));
            Assert.Equal(2, SteamAccelerationService.Traffic.Requests);
            Assert.Equal(0, SteamAccelerationService.Traffic.Failures);
            using var failed = await client.GetAsync("https://github.com/error");
            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.Equal(1, SteamAccelerationService.Traffic.Failures);
            Assert.Contains("github.com · HTTP 503", SteamAccelerationService.LastFailure);
            await SteamAccelerationService.StopAsync();
            Assert.False(SteamAccelerationService.Running);
            Assert.DoesNotContain("TubaSteamAcceleration", File.ReadAllText(hosts));
            Assert.Contains("127.0.0.1 localhost", File.ReadAllText(hosts));
            Assert.False(File.Exists(Path.Combine(directory, "active.json")));
        }
        finally
        {
            await SteamAccelerationService.StopAsync();
            SteamAccelerationService.TestDirectory = null; SteamAccelerationService.TestHostsPath = null;
            SteamAccelerationService.TestHttpPort = 80; SteamAccelerationService.TestHttpsPort = 443;
            SteamAccelerationService.TestHttpFactory = null;
            Directory.Delete(directory, true);
        }
    }
    private sealed class FixtureHandler : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new System.Net.Http.HttpResponseMessage(request.RequestUri!.AbsolutePath == "/error" ? System.Net.HttpStatusCode.ServiceUnavailable : System.Net.HttpStatusCode.OK)
            { Content = new System.Net.Http.StringContent(request.RequestUri.ToString()) };
            response.Headers.TryAddWithoutValidation("X-Observed-Host", request.Headers.Host);
            response.Headers.TryAddWithoutValidation("X-Observed-UA", request.Headers.UserAgent.ToString());
            response.Headers.TryAddWithoutValidation("X-Observed-Forwarded", request.Headers.Any(h => h.Key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)).ToString());
            return Task.FromResult(response);
        }
    }

    [Fact]
    public void CatalogueIncludesWattGroupsAndKeepsRestrictedRulesUnavailable()
    {
        var groups = SteamAccelerationCatalog.Load();
        Assert.Contains(groups, g => g.Name == "Steam 服务");
        Assert.Contains(groups, g => g.Name == "Twitch 直播");
        Assert.Contains(groups, g => g.Name == "网盘服务");
        Assert.Contains(groups, g => g.Name == "Github");
        Assert.True(groups.Sum(g => g.Rules.Count) > 40);
        Assert.All(groups.SelectMany(g => g.Rules).Where(r => r.ProxyType == 4), r => Assert.False(r.Available));
        Assert.All(groups.SelectMany(g => g.Rules).SelectMany(r => r.Domains), d => Assert.DoesNotContain("*", d));
    }

    [Fact]
    public void StartupRecoveryRestoresHostsWithoutStartingAcceleration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "steam-recovery-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        string hosts = Path.Combine(directory, "hosts");
        File.WriteAllText(hosts, "127.0.0.1 localhost\r\n# TubaSteamAcceleration BEGIN\r\n127.0.0.1 github.com\r\n# TubaSteamAcceleration END\r\n");
        File.WriteAllText(Path.Combine(directory, "active.json"), "{\"Version\":2}");
        SteamAccelerationService.TestDirectory = directory; SteamAccelerationService.TestHostsPath = hosts;
        try
        {
            Assert.False(SteamAccelerationService.Running);
            SteamAccelerationService.RecoverOnStartup();
            SteamAccelerationService.RecoverOnStartup();
            Assert.False(SteamAccelerationService.Running);
            Assert.Equal("127.0.0.1 localhost\r\n", File.ReadAllText(hosts));
            Assert.False(File.Exists(Path.Combine(directory, "active.json")));
        }
        finally
        {
            SteamAccelerationService.TestDirectory = null; SteamAccelerationService.TestHostsPath = null;
            Directory.Delete(directory, true);
        }
    }
}
