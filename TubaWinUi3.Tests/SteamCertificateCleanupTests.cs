using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TubaWinUi3.Services;
namespace TubaWinUi3.Tests;

public sealed class SteamCertificateCleanupTests
{
    [Fact]
    public void StaleCleanupKeepsCurrentRootAndRemovesOnlyOurOldOnes()
    {
        WithPrivateStore((store, name, old, keep, other) =>
        {
            SteamAccelerationCertificateStore.RemoveStaleRoots(keep.Thumbprint, name);
            SteamAccelerationCertificateStore.RemoveStaleRoots(keep.Thumbprint, name);
            var remaining = store.Certificates;
            try
            {
                Assert.DoesNotContain(remaining.Cast<X509Certificate2>(), c => c.Thumbprint == old.Thumbprint);
                Assert.Contains(remaining.Cast<X509Certificate2>(), c => c.Thumbprint == keep.Thumbprint);
                Assert.Contains(remaining.Cast<X509Certificate2>(), c => c.Thumbprint == other.Thumbprint);
            }
            finally { foreach (var c in remaining) c.Dispose(); }
        });
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-certificate")]
    public void StaleCleanupRejectsInvalidKeepThumbprintBeforeOpeningAnyStore(string? thumbprint)
        => Assert.Throws<InvalidDataException>(() => SteamAccelerationCertificateStore.RemoveStaleRoots(thumbprint));
    [Fact]
    public void PersistedRootKeepsPrivateKeyAcrossReload()
    {
        WithTempDirectory(directory =>
        {
            using var key = RSA.Create(2048);
            var rootRequest = new CertificateRequest(SteamAccelerationCertificateStore.Subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            string path = Path.Combine(directory, "root.pfx");
            SteamAccelerationCertificateStore.SaveRoot(path, root);
            Assert.NotEqual(root.Export(X509ContentType.Pfx), File.ReadAllBytes(path)); // DPAPI 保护，不是裸 PFX
            using var loaded = SteamAccelerationCertificateStore.LoadRoot(path);
            Assert.NotNull(loaded);
            Assert.Equal(root.Thumbprint, loaded!.Thumbprint);
            Assert.True(loaded.HasPrivateKey);
            // 复用同一张根证书时必须还能签发加速代理用的叶子证书。
            using var leafKey = RSA.Create(2048);
            using var leaf = new CertificateRequest("CN=github.com", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                .Create(loaded, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(12), RandomNumberGenerator.GetBytes(16));
            Assert.Equal(loaded.Subject, leaf.Issuer);
        });
    }
    [Fact]
    public void MissingOrCorruptPersistedRootIsIgnored()
    {
        WithTempDirectory(directory =>
        {
            string path = Path.Combine(directory, "root.pfx");
            Assert.Null(SteamAccelerationCertificateStore.LoadRoot(path));
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            Assert.Null(SteamAccelerationCertificateStore.LoadRoot(path));
        });
    }
    private static void WithTempDirectory(Action<string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "tuba-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { check(directory); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static void WithPrivateStore(Action<X509Store, string, X509Certificate2, X509Certificate2, X509Certificate2> check)
    {
        // Never install a trusted root or change the real Root store in this test.
        string name = "TubaSteamCleanupTest_" + Guid.NewGuid().ToString("N");
        using var key = RSA.Create(2048);
        using var old = Create(SteamAccelerationCertificateStore.Subject, key);
        using var keep = Create(SteamAccelerationCertificateStore.Subject, key);
        using var other = Create("CN=Unrelated Test Certificate", key);
        try
        {
            using var store = new X509Store(name, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(old); store.Add(keep); store.Add(other);
            check(store, name, old, keep, other);
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\SystemCertificates\" + name, false); }
    }
    private static X509Certificate2 Create(string subject, RSA key) =>
        new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
}
