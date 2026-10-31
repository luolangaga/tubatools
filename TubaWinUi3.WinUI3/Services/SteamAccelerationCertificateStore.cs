using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;

namespace TubaWinUi3.Services;

internal static class SteamAccelerationCertificateStore
{
    internal const string Subject = "CN=Tuba Steam Local Accelerator";
    // The logical CurrentUser Root provider invokes the interactive Protected Roots
    // UI on deletion. Address only its physical current-user registry store instead.
    // https://learn.microsoft.com/windows/win32/api/wincrypt/nf-wincrypt-certopenstore
    private const int SystemRegistryProvider = 13;
    private const uint CurrentUser = 0x00010000, OpenExisting = 0x00004000;

    internal static void RemoveOwnedRoot(string? thumbprint, string storeName = "Root")
    {
        if (string.IsNullOrWhiteSpace(thumbprint) || thumbprint.Length != 40 || !thumbprint.All(Uri.IsHexDigit))
            throw new InvalidDataException("加速器证书清理记录中的指纹无效。");
        using var handle = CertOpenStore(new IntPtr(SystemRegistryProvider), 0, IntPtr.Zero, CurrentUser | OpenExisting, storeName);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            if (error is 2 or 3) return; // No physical user store means no owned certificate to remove.
            throw new Win32Exception(error, "无法打开加速器证书存储。");
        }
        using var store = new X509Store(handle.DangerousGetHandle());
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
        try
        {
            foreach (var certificate in matches)
            {
                // A journal alone must never authorize deleting another product's certificate.
                if (!string.Equals(certificate.Subject, Subject, StringComparison.Ordinal) ||
                    !certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData))
                    throw new CryptographicException("清理记录指向的不是图吧 Steam 加速器证书，已跳过。");
                store.Remove(certificate);
            }
        }
        finally { foreach (var certificate in matches) certificate.Dispose(); }
    }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern CertificateStoreHandle CertOpenStore(IntPtr provider, uint encoding, IntPtr cryptProvider, uint flags, string name);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CertCloseStore(IntPtr handle, uint flags);
    private sealed class CertificateStoreHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public CertificateStoreHandle() : base(true) { }
        protected override bool ReleaseHandle() => CertCloseStore(handle, 0);
    }
}
