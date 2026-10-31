using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TubaWinUi3.Services;
namespace TubaWinUi3.Tests;

public sealed class SteamCertificateCleanupTests
{
    [Fact]
    public void CleanupOnlyDeletesMatchingOwnedCertificateAndIsIdempotent()
    {
        WithPrivateStore((store, name, owned, other) =>
        {
            SteamAccelerationCertificateStore.RemoveOwnedRoot(owned.Thumbprint, name);
            SteamAccelerationCertificateStore.RemoveOwnedRoot(owned.Thumbprint, name);
            var remaining = store.Certificates;
            try { Assert.DoesNotContain(remaining.Cast<X509Certificate2>(), c => c.Thumbprint == owned.Thumbprint); Assert.Contains(remaining.Cast<X509Certificate2>(), c => c.Thumbprint == other.Thumbprint); }
            finally { foreach (var c in remaining) c.Dispose(); }
        });
    }
    [Fact]
    public void CleanupRefusesAnotherProductsCertificateEvenWithMatchingJournal()
    {
        WithPrivateStore((store, name, owned, other) =>
        {
            Assert.Throws<CryptographicException>(() => SteamAccelerationCertificateStore.RemoveOwnedRoot(other.Thumbprint, name));
            Assert.Equal(2, store.Certificates.Count);
        });
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-certificate")]
    public void CleanupRejectsInvalidThumbprintsBeforeOpeningAnyStore(string? thumbprint)
        => Assert.Throws<InvalidDataException>(() => SteamAccelerationCertificateStore.RemoveOwnedRoot(thumbprint));
    private static void WithPrivateStore(Action<X509Store, string, X509Certificate2, X509Certificate2> check)
    {
        // Never install a trusted root or change the real Root store in this test.
        string name = "TubaSteamCleanupTest_" + Guid.NewGuid().ToString("N");
        using var key = RSA.Create(2048);
        using var owned = new CertificateRequest(SteamAccelerationCertificateStore.Subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var other = new CertificateRequest("CN=Unrelated Test Certificate", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        try
        {
            using var store = new X509Store(name, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(owned); store.Add(other);
            check(store, name, owned, other);
        }
        finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\SystemCertificates\" + name, false); }
    }
}
