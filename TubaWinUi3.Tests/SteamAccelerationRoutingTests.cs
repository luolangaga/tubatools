using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public sealed class SteamAccelerationRoutingTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.1.1")]
    [InlineData("fc00::1")]
    public void UpstreamDnsCannotLoopBackIntoProxyOrPrivateNetwork(string address)
        => Assert.False(SteamAccelerationService.IsPublicAddress(System.Net.IPAddress.Parse(address)));

    [Theory]
    [InlineData("steamcommunity.com", "/", "Steam 社区")]
    [InlineData("store.steampowered.com", "/", "Steam 商店")]
    [InlineData("login.steampowered.com", "/jwt/finalizelogin", "Steam 商店")]
    [InlineData("steamcommunity.com", "/comment/Profile/post", "Steam 讨论/留言 (IPv6)")]
    [InlineData("store.steampowered.com", "/app/570/", "Steam 商店(解锁访问限制)")]
    public void SelectingAllPreservesParentsAndUsesChildOnlyOnMatchingPaths(string host, string path, string expected)
    {
        var all = SteamAccelerationCatalog.Load().SelectMany(g => g.Rules);
        Assert.Equal(expected, SteamAccelerationRouting.Select(all, host, path)?.Name);
        Assert.Equal(expected, SteamAccelerationRouting.Select(all.Reverse(), host, path)?.Name);
    }

    [Fact]
    public void ChildOnlySelectionDoesNotClaimOtherPathsOrHosts()
    {
        var child = new SteamAccelerationRule { Domains = ["store.steampowered.com"], MatchPatterns = ["https://store.steampowered.com/app/*"] };
        Assert.Null(SteamAccelerationRouting.Select([child], "store.steampowered.com", "/login/"));
        Assert.Null(SteamAccelerationRouting.Select([child], "login.steampowered.com", "/app/570/"));
        Assert.Null(SteamAccelerationRouting.Select([child], "store.steampowered.com", "/APP/570/"));
    }

    [Fact]
    public void DomainFrontingValidatesConfiguredSniAndNeverIgnoresChainErrors()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=steamstore-a.akamaihd.net", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        bool Check(SslPolicyErrors errors, string sni) => SteamAccelerationService.IsValidUpstreamCertificate(certificate, errors, "store.steampowered.com", "steamstore.rmbgame.net", sni);
        Assert.True(Check(SslPolicyErrors.RemoteCertificateNameMismatch, "steamstore-a.akamaihd.net"));
        Assert.False(Check(SslPolicyErrors.RemoteCertificateChainErrors, "steamstore-a.akamaihd.net"));
        Assert.False(Check(SslPolicyErrors.RemoteCertificateNameMismatch, "unrelated.example"));
        Assert.False(Check(SslPolicyErrors.RemoteCertificateNotAvailable, "steamstore-a.akamaihd.net"));
    }
}
