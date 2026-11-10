using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;

namespace TubaWinUi3.Services;

// 加速根证书只创建并信任一次：私钥经 DPAPI 保护后落盘复用，停止加速只还原 Hosts，
// 不再卸载证书，避免每次暂停后重新确认信任、浏览器在暂停期间失去本机代理证书。
internal static class SteamAccelerationCertificateStore
{
    internal const string Subject = "CN=Tuba Steam Local Accelerator";
    private const string RootFile = "root.pfx";
    // The logical CurrentUser Root provider invokes the interactive Protected Roots
    // UI on deletion. Address only its physical current-user registry store instead.
    // https://learn.microsoft.com/windows/win32/api/wincrypt/nf-wincrypt-certopenstore
    private const int SystemRegistryProvider = 13;
    private const uint CurrentUser = 0x00010000, OpenExisting = 0x00004000, CryptProtectUiForbidden = 0x00000001;

    internal static X509Certificate2 EnsureRoot(string dataDir, bool trust)
    {
        string path = Path.Combine(dataDir, RootFile);
        var root = LoadRoot(path);
        if (root == null || !IsReusable(root))
        {
            root?.Dispose();
            root = CreateRoot();
            try { SaveRoot(path, root); }
            catch { root.Dispose(); throw; }
        }
        if (trust)
        {
            try { EnsureTrusted(root); }
            catch (CryptographicException ex)
            {
                root.Dispose();
                throw new InvalidOperationException("需要信任本机加速证书才能启动；证书安装被取消或失败，请重试并允许安装", ex);
            }
            // 旧版本每次启动都会新建并信任一张根证书，顺手清掉只保留当前这一张。
            try { RemoveStaleRoots(root.Thumbprint); } catch (Exception) { }
        }
        return root;
    }

    // 只有同一张自签名 CA 证书才可复用；临近过期时重新签发并再信任一次。
    private static bool IsReusable(X509Certificate2 root) =>
        root.HasPrivateKey && root.Subject == Subject &&
        root.SubjectName.RawData.AsSpan().SequenceEqual(root.IssuerName.RawData) &&
        root.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(7);

    private static X509Certificate2 CreateRoot()
    {
        using var rsa = RSA.Create(3072);
        var req = new CertificateRequest(Subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        // 信任一次长期有效，避免用户定期重新确认根证书。
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
    }

    internal static X509Certificate2? LoadRoot(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var pfx = Transform(File.ReadAllBytes(path), protect: false);
            try { return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.EphemeralKeySet); }
            finally { CryptographicOperations.ZeroMemory(pfx); }
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or Win32Exception or UnauthorizedAccessException or NotSupportedException)
        { return null; }
    }

    internal static void SaveRoot(string path, X509Certificate2 certificate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pfx = certificate.Export(X509ContentType.Pfx);
        try { File.WriteAllBytes(path, Transform(pfx, protect: true)); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }

    internal static void EnsureTrusted(X509Certificate2 certificate)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false);
        bool present = matches.Count > 0;
        foreach (var match in matches) match.Dispose();
        if (present) return;
        // 信任库只需要公钥副本，私钥留在 DPAPI 保护的文件里。
        using var trusted = X509CertificateLoader.LoadCertificate(certificate.RawData);
        store.Add(trusted);
    }

    // 清掉同主题、不同指纹的遗留根证书，只保留当前复用的这一张。
    internal static void RemoveStaleRoots(string? keepThumbprint, string storeName = "Root")
    {
        if (string.IsNullOrWhiteSpace(keepThumbprint) || keepThumbprint.Length != 40 || !keepThumbprint.All(Uri.IsHexDigit))
            throw new InvalidDataException("加速器证书保留指纹无效，已跳过清理。");
        using var handle = CertOpenStore(new IntPtr(SystemRegistryProvider), 0, IntPtr.Zero, CurrentUser | OpenExisting, storeName);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            if (error is 2 or 3) return; // No physical user store means no owned certificate to remove.
            throw new Win32Exception(error, "无法打开加速器证书存储。");
        }
        using var store = new X509Store(handle.DangerousGetHandle());
        foreach (var certificate in store.Certificates)
        {
            try
            {
                // 只删除本产品自签名的那一张证书，别的产品/用户证书一概不动。
                if (certificate.Thumbprint == keepThumbprint || certificate.Subject != Subject ||
                    !certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData)) continue;
                store.Remove(certificate);
            }
            finally { certificate.Dispose(); }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    // Per-Windows-user DPAPI: the root key is useless to another account or after copying to another PC.
    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        DataBlob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法访问加速器证书密钥。");
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
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
