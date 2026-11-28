using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Tubatools.MacOSInstaller.Core;

internal sealed class PayloadManager
{
    private readonly string _assets;
    private readonly string _root;
    private readonly string _marker;

    public PayloadManager(string assetsDirectory, string dataDirectory)
    {
        _assets = assetsDirectory;
        _root = Path.Combine(dataDirectory, "payload");
        _marker = Path.Combine(_root, ".ready-3");
    }

    public string OpenCoreRoot { get; private set; } = "";
    public string SamplePlist => Path.Combine(OpenCoreRoot, "Docs", "Sample.plist");
    public string AcpiSamples => Path.Combine(OpenCoreRoot, "Docs", "AcpiSamples", "Binaries");
    public string Macserial => Path.Combine(OpenCoreRoot, "Utilities", "macserial", "macserial.exe");
    public string Ocvalidate => Path.Combine(OpenCoreRoot, "Utilities", "ocvalidate", "ocvalidate.exe");
    public string X64Efi => Path.Combine(OpenCoreRoot, "X64", "EFI");
    public string KextRoot => Path.Combine(_root, "kexts");

    private const string OpenCoreZip = "OpenCore-1.0.9-DEBUG.zip";

    private static readonly string[] KextZips =
    [
        "Lilu-1.7.2-RELEASE.zip",
        "VirtualSMC-1.3.8-RELEASE.zip",
        "WhateverGreen-1.7.1-RELEASE.zip",
        "AppleALC-1.9.8-RELEASE.zip",
        "NVMeFix-1.1.3-RELEASE.zip",
        "IntelMausi-1.0.8-RELEASE.zip",
        "RestrictEvents-1.1.6-RELEASE.zip",
        "VoodooPS2Controller-2.3.7-RELEASE.zip",
        "RealtekRTL8111-V3.0.0.zip"
    ];

    public async Task EnsureAsync(Action<string> log, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        string signature = ComputeSignature();
        string openCoreDir = Path.Combine(_root, "opencore");

        if (File.Exists(_marker))
        {
            string cachedSignature = "";
            try { cachedSignature = (await File.ReadAllTextAsync(_marker, ct)).Trim(); } catch { }
            string? cachedRoot = FindOpenCoreRoot(openCoreDir);
            if (cachedSignature == signature && cachedRoot is not null && Directory.Exists(KextRoot))
            {
                OpenCoreRoot = cachedRoot;
                log("使用已解压且与当前组件一致的 OpenCore 与 kext 缓存。");
                return;
            }
            log("组件缓存与当前插件不一致，正在重新展开…");
        }

        log("正在展开内置的 OpenCore 与 kext 组件…");
        if (Directory.Exists(_root))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(_root))
            {
                string name = Path.GetFileName(entry);
                if (name.StartsWith('.') && !name.StartsWith(".ready", StringComparison.OrdinalIgnoreCase))
                    continue;
                try { if (Directory.Exists(entry)) Directory.Delete(entry, true); else File.Delete(entry); }
                catch { }
            }
        }

        string ocZip = Asset(OpenCoreZip);
        await Task.Run(() => ZipFile.ExtractToDirectory(ocZip, openCoreDir, overwriteFiles: true), ct);
        OpenCoreRoot = FindOpenCoreRoot(openCoreDir) ?? throw new InvalidOperationException("OpenCore 组件包结构不完整。");

        Directory.CreateDirectory(KextRoot);
        foreach (string zip in KextZips)
        {
            ct.ThrowIfCancellationRequested();
            string path = Asset(zip);
            string name = Path.GetFileNameWithoutExtension(zip);
            string dest = Path.Combine(KextRoot, name);
            log($"展开 {name}…");
            await Task.Run(() => ZipFile.ExtractToDirectory(path, dest, overwriteFiles: true), ct);
        }

        await File.WriteAllTextAsync(_marker, signature, new UTF8Encoding(false), ct);
    }

    private string Asset(string name) => Path.Combine(_assets, name);

    private string ComputeSignature()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string name in KextZips.Prepend(OpenCoreZip))
        {
            string path = Asset(name);
            hash.AppendData(Encoding.UTF8.GetBytes(name));
            if (!File.Exists(path))
            {
                hash.AppendData([0]);
                continue;
            }
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
            byte[] buffer = new byte[1024 * 1024];
            while (true)
            {
                int read = fs.Read(buffer);
                if (read <= 0) break;
                hash.AppendData(buffer.AsSpan(0, read));
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string? FindOpenCoreRoot(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        foreach (string candidate in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
        {
            if (File.Exists(Path.Combine(candidate, "X64", "EFI", "OC", "OpenCore.efi")))
                return candidate;
        }
        return File.Exists(Path.Combine(dir, "X64", "EFI", "OC", "OpenCore.efi")) ? dir : null;
    }

    public string? FindKext(string name)
    {
        if (!Directory.Exists(KextRoot)) return null;
        foreach (string dir in Directory.EnumerateDirectories(KextRoot, name + ".kext", SearchOption.AllDirectories))
            return dir;
        return null;
    }

    public IReadOnlyList<string> FindKextsStartingWith(string prefix)
    {
        var list = new List<string>();
        if (!Directory.Exists(KextRoot)) return list;
        foreach (string dir in Directory.EnumerateDirectories(KextRoot, prefix + "*.kext", SearchOption.AllDirectories))
            list.Add(dir);
        return list;
    }
}
