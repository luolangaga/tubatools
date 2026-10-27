using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services.DriverStore;

/// <summary>驱动包（DriverStore 中一个 oemNN.inf 对应一个包）。</summary>
public sealed record DriverPackage
{
    public required string InfName { get; init; }          // oem12.inf（发布名称）
    public required string OriginalInfName { get; init; }  // 原始名称，如 nvlt.inf
    public string Version { get; init; } = "";
    public DateTime? Date { get; init; }
    public string Provider { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string Signer { get; init; } = "";
    public bool InUse { get; internal set; }               // 有设备正在使用
    public bool IsNewest { get; internal set; }            // 同组内最新（组内排序后第一个）
    public bool UsageKnown { get; internal set; }
    public bool IsOffline { get; init; }
    public bool BootCritical { get; init; }
    public string ClassGuid { get; init; } = "";
    public string StoreInfPath { get; init; } = "";
    public bool SizeKnown { get; internal set; }
    public string DisplayTitle => $"{OriginalInfName} · {Version}";
    public string DisplayMetadata => $"{InfName} · {Provider} · {ClassName} · {Date:yyyy-MM-dd} · " + (SizeKnown ? $"{SizeBytes / 1048576d:F1} MB" : "大小未知");
    public string DisplayStatus => (IsNewest ? "最新版本" : "旧版本") + (BootCritical ? " · 启动关键" : "")
        + (InUse ? " · 有设备关联" : UsageKnown ? " · 无设备关联" : " · 设备关联未知") + $" · 签名：{Signer}";
    public string DisplayDevices => Devices.Count > 0 ? string.Join("、", Devices) : "";
    public bool CanDelete => !IsOffline && UsageKnown && !InUse && !BootCritical && !IsNewest;
    public List<string> Devices { get; internal set; } = []; // 正在使用的设备名（去重）
    public long SizeBytes { get; internal set; }           // FileRepository 目录大小（尽力匹配，0=未知）
}

/// <summary>同一驱动的多个版本（按原始 inf 名分组）。</summary>
public sealed class DriverGroup
{
    public required string Key { get; init; }
    public required string OriginalInfName { get; init; }
    public string Provider { get; init; } = "";
    public string ClassName { get; init; } = "";
    public List<DriverPackage> Packages { get; internal set; } = []; // 按日期+版本降序
    public long TotalSizeBytes => Packages.Sum(p => p.SizeBytes);
    public bool InUse => Packages.Any(p => p.InUse);
    public int OldCount => Packages.Count(p => !p.IsNewest);
    public long OldSizeBytes => Packages.Where(p => p.CanDelete).Sum(p => p.SizeBytes);
}

public sealed record DriverOpResult(bool Success, bool RebootRequired, string Message)
{
    public static DriverOpResult Ok(string message = "") => new(true, false, message);
    public static DriverOpResult Reboot(string message = "") => new(true, true, message);
    public static DriverOpResult Fail(string message) => new(false, false, message);
}

/// <summary>Windows 原生驱动管理：DISM + SetupAPI + NewDev。旧文本解析仅保留用于历史回归测试，不用于生产扫描。</summary>
public static partial class DriverStoreService
{
    private static readonly Regex OemInfRegex = new(@"oem\d+\.inf", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyInfRegex = new(@"[A-Za-z0-9_.\-]+\.(inf)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VersionLineRegex = new(@"(\d{1,4}[/.]\d{1,2}[/.]\d{2,4})\D+(\d+(?:\.\d+)+)", RegexOptions.Compiled);

    public static string DriverStorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "DriverStore", "FileRepository");

    // ===== 枚举 =====

    public static async Task<List<DriverGroup>> ScanAsync(Action<string>? progress = null, CancellationToken ct = default, string? offlineRoot = null)
    {
        await NativeGate.WaitAsync(ct).ConfigureAwait(false);
        try { return await Task.Run(() => ScanNative(progress, ct, offlineRoot), ct).ConfigureAwait(false); }
        finally { NativeGate.Release(); }
    }

    /// <summary>解析 pnputil /enum-drivers 输出为包列表（纯函数，可单测）。按值模式解析，与界面语言无关。</summary>
    internal static List<DriverPackage> ParseEnumDrivers(string output)
    {
        var result = new List<DriverPackage>();
        if (string.IsNullOrEmpty(output)) return result;

        DriverPackage? current = null;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;

            var oemMatch = OemInfRegex.Match(line);
            if (oemMatch.Success)
            {
                current = new DriverPackage { InfName = oemMatch.Value.ToLowerInvariant(), OriginalInfName = "" };
                result.Add(current);
                continue;
            }

            if (current is null) continue;

            // 原始名称行：块内出现的非 oem 的 *.inf
            if (string.IsNullOrEmpty(current.OriginalInfName))
            {
                var inf = AnyInfRegex.Match(line);
                if (inf.Success && !OemInfRegex.IsMatch(inf.Value))
                    current = current with { OriginalInfName = inf.Value.ToLowerInvariant() };
                // record 的 init-only 属性不能原地改，重写最后一个元素
                result[^1] = current;
            }

            // 版本/日期行：如「驱动程序日期和版本: 06/21/2006 31.0.15.5222」
            var verMatch = VersionLineRegex.Match(line);
            if (verMatch.Success)
            {
                current = current with { Date = ParseDriverDate(verMatch.Groups[1].Value), Version = verMatch.Groups[2].Value };
                result[^1] = current;
                continue;
            }

            // 提供程序/签名者/类：标签本地化，按关键字尽力识别（缺了不影响分组与排序）
            if (line.Contains("提供程序") || line.Contains("Provider", StringComparison.OrdinalIgnoreCase))
            {
                var value = ExtractColonValue(line);
                if (!string.IsNullOrEmpty(value)) { current = current with { Provider = value }; result[^1] = current; }
            }
            else if (line.Contains("签名者") || line.Contains("Signer", StringComparison.OrdinalIgnoreCase))
            {
                var value = ExtractColonValue(line);
                if (!string.IsNullOrEmpty(value)) { current = current with { Signer = value }; result[^1] = current; }
            }
            else if (line.Contains("类名") || line.Contains("Class Name", StringComparison.OrdinalIgnoreCase) ||
                     Regex.IsMatch(line, "^类[:：]", RegexOptions.None))
            {
                var value = ExtractColonValue(line);
                if (!string.IsNullOrEmpty(value)) { current = current with { ClassName = value }; result[^1] = current; }
            }
        }

        // 原始名称缺失（极端解析失败）时退回 oem 名，保证分组键始终存在
        for (var i = 0; i < result.Count; i++)
        {
            if (string.IsNullOrEmpty(result[i].OriginalInfName))
                result[i] = result[i] with { OriginalInfName = result[i].InfName };
        }
        return result;
    }

    internal static string? ExtractColonValue(string line)
    {
        var idx = line.IndexOfAny([':', '：']);
        if (idx < 0 || idx + 1 >= line.Length) return null;
        var value = line[(idx + 1)..].Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>解析 pnputil 日期字段：MM/DD/YYYY 或 YYYY/MM/DD（其他格式返回 null，排序退回版本号）。</summary>
    internal static DateTime? ParseDriverDate(string text)
    {
        var parts = text.Split(['/', '.']);
        if (parts.Length != 3) return null;
        if (int.TryParse(parts[0], out var first) && first > 31)
            return TryDate(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2])); // YYYY/MM/DD
        if (int.TryParse(parts[0], out var mm) && int.TryParse(parts[1], out var dd) && int.TryParse(parts[2], out var yy))
            return TryDate(yy < 100 ? 2000 + yy : yy, mm, dd); // MM/DD/YYYY
        return null;

        static DateTime? TryDate(int y, int m, int d)
        {
            try { return new DateTime(y, m, d); }
            catch { return null; }
        }
    }

    // ===== DriverStore 占用 =====

    internal sealed record RepoDirInfo(string DirName, string OriginalInfName, string Version, long SizeBytes);

    /// <summary>扫描 FileRepository：目录名前缀 = 原始 inf 名，目录内 inf 的 DriverVer 提供版本（纯函数，可单测）。</summary>
    internal static List<RepoDirInfo> ScanRepositoryDirs(string rootPath)
    {
        var result = new List<RepoDirInfo>();
        if (!Directory.Exists(rootPath)) return result;

        foreach (var dir in Directory.EnumerateDirectories(rootPath))
        {
            try
            {
                var name = Path.GetFileName(dir);
                var infName = name;
                var underscore = name.IndexOf(".inf_", StringComparison.OrdinalIgnoreCase);
                if (underscore > 0)
                    infName = name[..(underscore + 4)];

                string version = "";
                foreach (var file in Directory.EnumerateFiles(dir, "*.inf"))
                {
                    version = ReadInfDriverVer(file);
                    break;
                }

                long size = 0;
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { size += new FileInfo(file).Length; } catch { }
                }

                result.Add(new RepoDirInfo(name, infName.ToLowerInvariant(), version, size));
            }
            catch { /* 单个目录失败跳过 */ }
        }
        return result;
    }

    /// <summary>读 INF 文本中的 DriverVer（自动识别 UTF-16 / ANSI / UTF-8，兼容 BOM 与否）。</summary>
    internal static string ReadInfDriverVerText(string content)
    {
        var match = Regex.Match(content, @"DriverVer\s*=\s*(\d{1,4}[/.]\d{1,2}[/.]\d{2,4})\s*,\s*(\d+(?:\.\d+)*)",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[2].Value : "";
    }

    internal static string ReadInfDriverVer(string filePath)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            string text;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            else
                text = Encoding.UTF8.GetString(bytes);
            return ReadInfDriverVerText(text);
        }
        catch { return ""; }
    }

    /// <summary>把 FileRepository 目录大小匹配回驱动包：原始名+版本都一致优先，退回该原始名唯一目录（纯函数，可单测）。</summary>
    internal static void AssignRepositorySizes(List<DriverPackage> packages, List<RepoDirInfo>? repoDirs = null)
    {
        repoDirs ??= ScanRepositoryDirs(DriverStorePath);
        var byOriginal = repoDirs.GroupBy(d => d.OriginalInfName, StringComparer.OrdinalIgnoreCase)
                                 .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var pkg in packages)
        {
            if (!byOriginal.TryGetValue(pkg.OriginalInfName, out var dirs) || dirs.Count == 0)
                continue;

            var exact = dirs.FirstOrDefault(d =>
                !string.IsNullOrEmpty(d.Version) && !string.IsNullOrEmpty(pkg.Version) &&
                string.Equals(d.Version, pkg.Version, StringComparison.OrdinalIgnoreCase));
            pkg.SizeBytes = (exact ?? (dirs.Count == 1 ? dirs[0] : null))?.SizeBytes ?? 0;
        }
    }

    /// <summary>按原始 inf 分组、组内按日期+版本降序并标记最新（纯函数，可单测）。</summary>
    internal static List<DriverGroup> BuildGroups(List<DriverPackage> packages)
    {
        return packages
            .GroupBy(p => $"{p.OriginalInfName}|{p.Provider}|{p.ClassGuid}|{p.ClassName}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var ordered = g.OrderByDescending(p => p.Date ?? DateTime.MinValue)
                               .ThenByDescending(p => ParseVersionOrNull(p.Version) ?? new Version(0, 0))
                               .ToList();
                for (var i = 0; i < ordered.Count; i++)
                    ordered[i].IsNewest = ordered[i].Date == ordered[0].Date && ParseVersionOrNull(ordered[i].Version) == ParseVersionOrNull(ordered[0].Version);

                var named = ordered.FirstOrDefault(p => !string.IsNullOrEmpty(p.Provider))?.Provider ?? "";
                var cls = ordered.FirstOrDefault(p => !string.IsNullOrEmpty(p.ClassName))?.ClassName ?? "";
                return new DriverGroup
                {
                    Key = g.Key,
                    OriginalInfName = ordered.FirstOrDefault(p => p.OriginalInfName != p.InfName)?.OriginalInfName
                                      ?? ordered[0].OriginalInfName,
                    Provider = named,
                    ClassName = cls,
                    Packages = ordered,
                };
            })
            .OrderByDescending(g => g.Packages.Count > 1)
            .ThenBy(g => g.OriginalInfName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static Version? ParseVersionOrNull(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return Version.TryParse(text.Trim(), out var v) ? v : null;
    }

}
