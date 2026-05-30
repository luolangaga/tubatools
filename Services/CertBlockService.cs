using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class CertBlockService
{
    private static List<CertBlockVendor>? _vendors;
    private static bool _isLoading;

    public static IReadOnlyList<CertBlockVendor> Vendors => _vendors ?? [];
    public static bool IsLoading => _isLoading;
    public static int TotalCerts => _vendors?.Sum(v => v.TotalCount) ?? 0;
    public static int BlockedCerts => _vendors?.Sum(v => v.BlockedCount) ?? 0;
    public static int VendorCount => _vendors?.Count ?? 0;
    public static bool IsAdmin => System.Security.Principal.WindowsIdentity.GetCurrent().Owner
        ?.IsWellKnown(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid) ?? false;

    public static void LoadAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        _ = Task.Run(() =>
        {
            try { LoadAll(); }
            finally { _isLoading = false; }
        });
    }

    public static void Reload()
    {
        if (_vendors is null) return;
        foreach (var vendor in _vendors)
        {
            foreach (var cert in vendor.Certificates)
            {
                cert.IsBlocked = IsCertDisallowed(cert.Thumbprint);
            }
        }
    }

    public static bool BlockVendor(CertBlockVendor vendor)
    {
        if (!IsAdmin) return false;
        foreach (var cert in vendor.Certificates)
        {
            try
            {
                AddToDisallowedStore(cert.Thumbprint, cert.FileName);
                cert.IsBlocked = true;
            }
            catch { }
        }
        return true;
    }

    public static bool UnblockVendor(CertBlockVendor vendor)
    {
        if (!IsAdmin) return false;
        foreach (var cert in vendor.Certificates)
        {
            try
            {
                RemoveFromDisallowedStore(cert.Thumbprint);
                cert.IsBlocked = false;
            }
            catch { }
        }
        return true;
    }

    public static bool BlockAll()
    {
        if (_vendors is null || !IsAdmin) return false;
        foreach (var vendor in _vendors)
            BlockVendorDirect(vendor);
        return true;
    }

    public static bool UnblockAll()
    {
        if (_vendors is null || !IsAdmin) return false;
        foreach (var vendor in _vendors)
            UnblockVendorDirect(vendor);
        return true;
    }

    private static void BlockVendorDirect(CertBlockVendor vendor)
    {
        foreach (var cert in vendor.Certificates)
        {
            try
            {
                AddToDisallowedStore(cert.Thumbprint, cert.FileName);
                cert.IsBlocked = true;
            }
            catch { }
        }
    }

    private static void UnblockVendorDirect(CertBlockVendor vendor)
    {
        foreach (var cert in vendor.Certificates)
        {
            try
            {
                RemoveFromDisallowedStore(cert.Thumbprint);
                cert.IsBlocked = false;
            }
            catch { }
        }
    }

    private static void LoadAll()
    {
        var baseDir = FindCertBlockRoot();
        var certDir = Path.Combine(baseDir, "Certificates");
        if (!Directory.Exists(certDir)) return;

        var mapJson = ReadAssetFile(baseDir, "certificate-map.json");
        var namesJson = ReadAssetFile(baseDir, "display-names.json");
        if (mapJson is null) return;

        var map = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(mapJson)!;
        var names = namesJson is not null
            ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(namesJson)
            : null;

        var vendors = new List<CertBlockVendor>();
        foreach (var (id, certFiles) in map)
        {
            var vendor = new CertBlockVendor
            {
                Id = id,
                DisplayName = ResolveName(names, id)
            };

            foreach (var cf in certFiles)
            {
                var fp = Path.Combine(certDir, cf);
                if (!File.Exists(fp)) continue;
                try
                {
                    var certData = File.ReadAllBytes(fp);
                    using var x509 = X509CertificateLoader.LoadCertificate(certData);
                    var entry = new CertBlockEntry
                    {
                        FileName = cf,
                        CommonName = x509.GetNameInfo(X509NameType.SimpleName, false) ?? cf,
                        SerialNumber = x509.SerialNumber ?? "",
                        Thumbprint = x509.Thumbprint ?? "",
                        IsBlocked = IsCertDisallowed(x509.Thumbprint)
                    };
                    vendor.Certificates.Add(entry);
                }
                catch { }
            }

            if (vendor.Certificates.Count > 0)
                vendors.Add(vendor);
        }

        _vendors = vendors;
    }

    private static bool IsCertDisallowed(string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint)) return false;
        try
        {
            using var store = new X509Store("Disallowed", StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
            return found.Count > 0;
        }
        catch
        {
            try
            {
                using var store = new X509Store("Disallowed", StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);
                var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
                return found.Count > 0;
            }
            catch { return false; }
        }
    }

    private static void AddToDisallowedStore(string thumbprint, string certFileName)
    {
        var certDir = Path.Combine(FindCertBlockRoot(), "Certificates");
        var fp = Path.Combine(certDir, certFileName);
        if (!File.Exists(fp)) return;

        var certData = File.ReadAllBytes(fp);
        using var cert = X509CertificateLoader.LoadCertificate(certData);

        try
        {
            using var store = new X509Store("Disallowed", StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);
            store.Add(cert);
            return;
        }
        catch { }

        using var cuStore = new X509Store("Disallowed", StoreLocation.CurrentUser);
        cuStore.Open(OpenFlags.ReadWrite);
        cuStore.Add(cert);
    }

    private static void RemoveFromDisallowedStore(string thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint)) return;

        try
        {
            using var store = new X509Store("Disallowed", StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
            foreach (var c in found) store.Remove(c);
        }
        catch { }

        try
        {
            using var store = new X509Store("Disallowed", StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
            foreach (var c in found) store.Remove(c);
        }
        catch { }
    }

    private static string FindCertBlockRoot()
    {
        var appDir = ToolCatalog.AppDirectory;
        foreach (var d in new[] { appDir, Path.Combine(appDir, "CertBlock") })
        {
            if (Directory.Exists(Path.Combine(d, "Certificates"))) return d;
        }
        var directory = new DirectoryInfo(appDir);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "CertBlock");
            if (Directory.Exists(Path.Combine(candidate, "Certificates"))) return candidate;
            candidate = directory.FullName;
            if (Directory.Exists(Path.Combine(candidate, "Certificates"))) return candidate;
            directory = directory.Parent;
        }
        return Path.Combine(appDir, "CertBlock");
    }

    private static string? ReadAssetFile(string baseDir, string name)
    {
        var appDir = ToolCatalog.AppDirectory;
        foreach (var d in new[] { Path.Combine(baseDir, "CertBlockAssets"), baseDir, Path.Combine(appDir, "CertBlockAssets") })
        {
            var p = Path.Combine(d, name);
            if (File.Exists(p)) return File.ReadAllText(p);
        }
        return null;
    }

    private static string ResolveName(Dictionary<string, Dictionary<string, string>>? names, string id)
    {
        if (names is not null && names.TryGetValue("zh-CN", out var cn) && cn.TryGetValue(id, out var n)) return n;
        if (names is not null && names.TryGetValue("en-US", out var en) && en.TryGetValue(id, out n)) return n;
        return id;
    }
}
