using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services;

public enum WindowsImageSource
{
    CommunityMirror,
    MicrosoftOfficial
}

public sealed class WindowsImageEntry
{
    public string DisplayName { get; init; } = "";
    public string FileName { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public long SizeBytes { get; init; }
    public string SizeDisplay { get; init; } = "";
    public string Category { get; init; } = "";
    public string? Updated { get; init; }
    public string? Md5 { get; init; }
    public string? Sha1 { get; init; }
    public string? Sha256 { get; init; }
    public string Language { get; init; } = "";
    public string Arch { get; init; } = "";
    public WindowsImageSource Source { get; init; } = WindowsImageSource.CommunityMirror;
    public bool IsEsd => FileName.EndsWith(".esd", StringComparison.OrdinalIgnoreCase);
}

public static class WindowsImageService
{
    private const string ReadmeUrl = "https://raw.githubusercontent.com/ILLKX/Windows/master/README.md";
    private static readonly HttpClient _http = HttpClientFactory.CreateIpv4Preferred(TimeSpan.FromSeconds(30));
    private static List<WindowsImageEntry>? _cache;
    private static DateTime _cacheTime;
    private static readonly TimeSpan _cacheExpiry = TimeSpan.FromHours(1);

    public static List<WindowsImageEntry> CachedEntries => _cache ?? [];

    public static string? FindUltraIso()
    {
        var paths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "UltraISO", "UltraISO.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "UltraISO", "UltraISO.exe"),
            @"D:\Program Files\UltraISO\UltraISO.exe",
            @"C:\Program Files\UltraISO\UltraISO.exe",
            @"C:\Program Files (x86)\UltraISO\UltraISO.exe",
        };

        foreach (var p in paths)
            if (File.Exists(p)) return p;

        using var regKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        if (regKey is not null)
        {
            foreach (var sub in regKey.GetSubKeyNames())
            {
                using var sk = regKey.OpenSubKey(sub);
                var dn = sk?.GetValue("DisplayName") as string;
                if (dn is not null && dn.Contains("UltraISO", StringComparison.OrdinalIgnoreCase))
                {
                    var loc = sk?.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(loc))
                    {
                        var exe = Path.Combine(loc, "UltraISO.exe");
                        if (File.Exists(exe)) return exe;
                    }
                }
            }
        }

        return null;
    }

    public static bool IsUltraIsoAvailable => FindUltraIso() is not null;

    public static async Task<List<WindowsImageEntry>> LoadAsync(CancellationToken ct = default)
    {
        if (_cache is not null && DateTime.Now - _cacheTime < _cacheExpiry)
            return _cache;

        var md = await _http.GetStringAsync(ReadmeUrl, ct);
        _cache = ParseReadme(md);
        _cacheTime = DateTime.Now;
        return _cache;
    }

    internal static List<WindowsImageEntry> ParseReadme(string md)
    {
        var entries = new List<WindowsImageEntry>();
        var currentCategory = "";

        var lines = md.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (line.StartsWith("## ") && !line.StartsWith("### "))
            {
                currentCategory = line[3..].Trim();
                continue;
            }

            if (!line.StartsWith("<details>", StringComparison.OrdinalIgnoreCase))
                continue;

            var summary = "";
            var summaryStart = line.IndexOf("<summary>", StringComparison.OrdinalIgnoreCase);
            if (summaryStart < 0 && i + 1 < lines.Length)
            {
                summaryStart = lines[i + 1].IndexOf("<summary>", StringComparison.OrdinalIgnoreCase);
                if (summaryStart >= 0) summary = ExtractTagContent(lines[i + 1][summaryStart..], "summary");
            }
            if (summaryStart >= 0 && string.IsNullOrEmpty(summary))
                summary = ExtractTagContent(line[summaryStart..], "summary");

            if (string.IsNullOrEmpty(summary)) continue;

            var block = new StringBuilder();
            var j = i + 1;
            while (j < lines.Length)
            {
                if (lines[j].Trim().Equals("</details>", StringComparison.OrdinalIgnoreCase))
                    break;
                block.AppendLine(lines[j]);
                j++;
            }

            var entry = ParseEntry(block.ToString(), summary, currentCategory);
            if (entry is not null)
                entries.Add(entry);
        }

        return entries;
    }

    private static string ExtractTagContent(string s, string tag)
    {
        var start = s.IndexOf(">", StringComparison.OrdinalIgnoreCase);
        var end = s.IndexOf($"</{tag}>", StringComparison.OrdinalIgnoreCase);
        if (start < 0 || end < 0 || end <= start) return s;
        var content = s[(start + 1)..end];
        content = Regex.Replace(content, "<b>|</b>", "").Trim();
        return content.TrimStart(' ', '-');
    }

    private static WindowsImageEntry? ParseEntry(string block, string summary, string category)
    {
        var fileName = ExtractField(block, "Filename");
        var downloadUrl = ExtractLink(block, "Download");
        var sizeStr = ExtractField(block, "Size");
        var updated = ExtractField(block, "Updated");
        var md5 = ExtractField(block, "MD5");
        var sha1 = ExtractField(block, "SHA1");
        var sha256 = ExtractField(block, "SHA256");

        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(downloadUrl))
            return null;

        var sizeBytes = ParseSizeBytes(sizeStr);
        var sizeDisplay = ParseSizeDisplay(sizeStr);
        var (language, arch) = ParseLangArch(fileName, summary);

        return new WindowsImageEntry
        {
            DisplayName = CleanSummary(summary),
            FileName = fileName,
            DownloadUrl = downloadUrl,
            SizeBytes = sizeBytes,
            SizeDisplay = sizeDisplay,
            Category = category,
            Updated = string.IsNullOrEmpty(updated) ? null : updated,
            Md5 = md5,
            Sha1 = sha1,
            Sha256 = sha256,
            Language = language,
            Arch = arch
        };
    }

    private static string? ExtractField(string block, string fieldName)
    {
        var pattern = $@"\*\*{fieldName}\*\*\s*\|\s*`?([^`|\n]+)`?\s*\|";
        var m = Regex.Match(block, pattern, RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static string? ExtractLink(string block, string fieldName)
    {
        var pattern = $@"\*\*{fieldName}\*\*\s*\|\s*\[([^\]]+)\]\(([^)]+)\)";
        var m = Regex.Match(block, pattern, RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[2].Value.Trim() : null;
    }

    private static long ParseSizeBytes(string? sizeStr)
    {
        if (string.IsNullOrEmpty(sizeStr)) return 0;
        var m = Regex.Match(sizeStr, @"\((\d+)\s*bytes\)", RegexOptions.IgnoreCase);
        if (m.Success && long.TryParse(m.Groups[1].Value, out var bytes))
            return bytes;
        return 0;
    }

    private static string ParseSizeDisplay(string? sizeStr)
    {
        if (string.IsNullOrEmpty(sizeStr)) return "";
        var m = Regex.Match(sizeStr, @"^([\d.]+\s*(?:KB|MB|GB|TB))", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : sizeStr.Split('(')[0].Trim();
    }

    private static (string lang, string arch) ParseLangArch(string fileName, string summary)
    {
        var lang = "其他";
        var lower = fileName.ToLowerInvariant();
        if (lower.Contains("zh-hans") || lower.Contains("zh-cn") || lower.Contains("cn_windows"))
            lang = "简体中文";
        else if (lower.Contains("zh-hant") || lower.Contains("zh-tw"))
            lang = "繁体中文";
        else if (lower.Contains("en-us") || lower.Contains("en_windows"))
            lang = "English";
        else if (lower.Contains("ja-jp"))
            lang = "日本語";

        var arch = "x64";
        if (lower.Contains("_x86_") || lower.Contains("_x86."))
            arch = "x86";
        else if (lower.Contains("_arm64_") || lower.Contains("_arm64."))
            arch = "ARM64";

        return (lang, arch);
    }

    private static string CleanSummary(string summary)
    {
        var s = summary.Trim();
        s = Regex.Replace(s, @"<[^>]+>", "");
        return s.Trim();
    }

    public static async Task ConvertEsdToIsoAsync(
        string esdPath,
        string isoPath,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var ultraIso = FindUltraIso()
            ?? throw new InvalidOperationException(
                "ESD 转 ISO 需要 UltraISO。\n" +
                "请安装 UltraISO 后重试。\n" +
                "下载地址: https://www.ezbsystems.com/ultraiso/");

        var tempDir = Path.Combine(Path.GetDirectoryName(esdPath)!, "esd_convert_temp_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);

            var mountDir = Path.Combine(tempDir, "mount");
            Directory.CreateDirectory(mountDir);

            progress?.Report("正在使用 DISM 导出 WIM 映像...");
            var wimPath = Path.Combine(tempDir, "install.wim");

            await RunDismAsync($@"/Export-Image /SourceImageFile:""{esdPath}"" /SourceIndex:1 /DestinationImageFile:""{wimPath}"" /Compress:Max /CheckIntegrity", ct);

            var esdIndexCount = await GetDismImageCountAsync(esdPath, ct);
            for (var idx = 2; idx <= esdIndexCount; idx++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"正在导出映像索引 {idx}/{esdIndexCount}...");
                await RunDismAsync($@"/Export-Image /SourceImageFile:""{esdPath}"" /SourceIndex:{idx} /DestinationImageFile:""{wimPath}"" /Compress:Max /CheckIntegrity", ct);
            }

            progress?.Report("正在提取启动文件...");
            await RunDismAsync($@"/Apply-Image /ImageFile:""{esdPath}"" /Index:1 /ApplyDir:""{mountDir}""", ct);

            var isoRoot = Path.Combine(tempDir, "iso");
            Directory.CreateDirectory(isoRoot);
            var sourcesDir = Path.Combine(isoRoot, "sources");
            Directory.CreateDirectory(sourcesDir);

            File.Move(wimPath, Path.Combine(sourcesDir, "install.wim"));

            CopyBootFiles(mountDir, isoRoot);

            try { Directory.Delete(mountDir, true); } catch { }

            progress?.Report("正在使用 UltraISO 生成 ISO...");
            await CreateIsoWithUltraIsoAsync(ultraIso, isoRoot, isoPath, ct);

            progress?.Report("转换完成！");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { }
        }
    }

    private static void CopyBootFiles(string mountDir, string isoRoot)
    {
        var bootDir = Path.Combine(isoRoot, "boot");
        var efiDir = Path.Combine(isoRoot, "efi", "microsoft", "boot");
        Directory.CreateDirectory(bootDir);
        Directory.CreateDirectory(efiDir);

        var winBoot = Path.Combine(mountDir, "Windows", "Boot");
        if (!Directory.Exists(winBoot)) return;

        void SafeCopy(string src, string destDir)
        {
            if (!File.Exists(src)) return;
            try { File.Copy(src, Path.Combine(destDir, Path.GetFileName(src)), true); } catch { }
        }

        foreach (var f in Directory.GetFiles(Path.Combine(winBoot, "DVD", "PCAT"), "bootmgr*"))
            SafeCopy(f, isoRoot);
        foreach (var f in Directory.GetFiles(Path.Combine(winBoot, "DVD", "PCAT", "BCD")))
            SafeCopy(f, bootDir);
        foreach (var f in Directory.GetFiles(Path.Combine(winBoot, "DVD", "PCAT"), "boot.sdi"))
            SafeCopy(f, bootDir);
        foreach (var f in Directory.GetFiles(Path.Combine(winBoot, "DVD", "PCAT"), "etfsboot.com"))
            SafeCopy(f, bootDir);

        var efiBootDir = Path.Combine(winBoot, "DVD", "EFI");
        if (Directory.Exists(efiBootDir))
        {
            foreach (var f in Directory.GetFiles(efiBootDir, "BCD"))
                SafeCopy(f, efiDir);
            foreach (var f in Directory.GetFiles(efiBootDir, "*.efi"))
                SafeCopy(f, efiDir);
            foreach (var f in Directory.GetFiles(efiBootDir, "efisys.bin"))
                SafeCopy(f, efiDir);
        }

        var srcSources = Path.Combine(mountDir, "sources");
        if (Directory.Exists(srcSources))
        {
            var bootWim = Path.Combine(srcSources, "boot.wim");
            if (File.Exists(bootWim))
                SafeCopy(bootWim, Path.Combine(isoRoot, "sources"));
            foreach (var f in Directory.GetFiles(srcSources, "boot.wim*"))
                SafeCopy(f, Path.Combine(isoRoot, "sources"));
        }
    }

    private static async Task CreateIsoWithUltraIsoAsync(string ultraIsoExe, string isoRoot, string isoPath, CancellationToken ct)
    {
        var start = new System.Diagnostics.ProcessStartInfo(ultraIsoExe);
        foreach (var arg in new[] { "-udfdvd", "-directory", isoRoot, "-output", isoPath, "-silent" }) start.ArgumentList.Add(arg);
        var result = await CapturedProcess.RunAsync(start, ct);
        if (result.ExitCode != 0) throw new InvalidOperationException($"UltraISO 错误 ({result.ExitCode}): {result.Error}");
    }

    private static async Task<int> GetDismImageCountAsync(string imagePath, CancellationToken ct)
    {
        var output = await RunDismCaptureAsync($@"/English /Get-WimInfo /WimFile:""{imagePath}""", ct);
        var count = 0;
        foreach (Match m in Regex.Matches(output, @"Index\s*:\s*(\d+)")) count = Math.Max(count, int.Parse(m.Groups[1].Value));
        if (count == 0) throw new InvalidDataException("DISM 未返回有效的映像索引。");
        return count;
    }

    private static async Task RunDismAsync(string args, CancellationToken ct) => _ = await RunDismCaptureAsync(args, ct);

    private static async Task<string> RunDismCaptureAsync(string args, CancellationToken ct)
    {
        var result = await CapturedProcess.RunAsync(new System.Diagnostics.ProcessStartInfo("dism.exe", args), ct);
        if (result.ExitCode != 0) throw new InvalidOperationException($"DISM 错误 ({result.ExitCode}): {result.Error} {result.Output}");
        return result.Output;
    }

    /// <summary>
    /// 镜像直接下载目录。用户未自定义时为 下载\WindowsImages；
    /// 「WindowsImageDownloadDir」设置对本页所有下载（微软官方/社区镜像/UUP）生效。
    /// </summary>
    public static string GetDownloadDir()
    {
        var custom = AppSettings.Get("WindowsImageDownloadDir");
        var dir = string.IsNullOrWhiteSpace(custom)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "WindowsImages")
            : custom.Trim();
        Directory.CreateDirectory(dir);
        return dir;
    }
}
