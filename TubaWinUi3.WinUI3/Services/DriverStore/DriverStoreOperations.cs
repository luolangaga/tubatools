using System.Text;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services.DriverStore;

public static partial class DriverStoreService
{
    private static readonly SemaphoreSlim NativeGate = new(1, 1);
    public static bool IsPublishedInf(string? name) => name is not null
        && Regex.IsMatch(name, @"\Aoem[0-9]+\.inf\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static List<DriverGroup> ScanNative(Action<string>? progress, CancellationToken ct, string? offlineRoot)
    {
        progress?.Invoke("正在通过 Windows API 枚举驱动包…");
        var packages = DriverStoreNative.Enumerate(offlineRoot);
        ct.ThrowIfCancellationRequested();
        if (offlineRoot is null)
        {
            progress?.Invoke("正在读取设备关联（包括未连接设备）…");
            var devices = DriverStoreNative.GetDevices(ct);
            foreach (var pkg in packages)
            {
                pkg.UsageKnown = true;
                if (devices.TryGetValue(pkg.InfName, out var list))
                {
                    pkg.Devices = list;
                    pkg.InUse = list.Count > 0;
                }
            }
        }
        progress?.Invoke("正在计算驱动包大小…");
        foreach (var pkg in packages)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(pkg.StoreInfPath)) continue;
            try
            {
                pkg.SizeBytes = PackageFiles(Path.GetDirectoryName(pkg.StoreInfPath)!).Sum(f => new FileInfo(f).Length);
                pkg.SizeKnown = true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return BuildGroups(packages);
    }

    public static Task<DriverOpResult> DeleteAsync(string infName, CancellationToken ct = default)
    {
        if (!IsPublishedInf(infName)) throw new ArgumentException("不是有效的发布名称", nameof(infName));
        return RunNativeAsync(() =>
        {
            // 确认后重新枚举，不能信任过期的 UI 状态。
            var pkg = ScanNative(null, ct, null).SelectMany(g => g.Packages)
                .SingleOrDefault(p => p.InfName.Equals(infName, StringComparison.OrdinalIgnoreCase));
            if (pkg is null) return DriverOpResult.Fail("驱动包已不存在，请重新扫描");
            if (!pkg.CanDelete) return DriverOpResult.Fail("只能删除非启动关键、无设备关联的旧版本，请重新扫描");
            return DriverStoreNative.Delete(infName);
        }, ct);
    }

    public static Task<DriverOpResult> ExportAsync(string infName, string targetDir, CancellationToken ct = default, string? offlineRoot = null)
    {
        if (infName != "*" && !IsPublishedInf(infName)) throw new ArgumentException("不是有效的发布名称", nameof(infName));
        var target = Path.GetFullPath(targetDir);
        return RunNativeAsync(() =>
        {
            var packages = DriverStoreNative.Enumerate(offlineRoot)
                .Where(p => infName == "*" || p.InfName.Equals(infName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (packages.Count == 0) return DriverOpResult.Fail("没有找到要导出的驱动包");
            var errors = new List<string>();
            foreach (var pkg in packages)
            {
                ct.ThrowIfCancellationRequested();
                try { ExportPackage(pkg, target, ct); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                { errors.Add($"{pkg.InfName}：{ex.Message}"); }
            }
            return errors.Count == 0 ? DriverOpResult.Ok($"已导出 {packages.Count} 个驱动包")
                : DriverOpResult.Fail($"成功 {packages.Count - errors.Count} / {packages.Count}\n" + string.Join("\n", errors));
        }, ct);
    }

    internal static List<string> PackageFiles(string root) => Directory.EnumerateFiles(root, "*", new EnumerationOptions
    {
        RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint
    }).ToList();

    internal static void ExportPackage(DriverPackage pkg, string target, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(pkg.StoreInfPath) || !File.Exists(pkg.StoreInfPath)) throw new IOException("无法定位驱动库中的原始 INF");
        var source = Path.GetFullPath(Path.GetDirectoryName(pkg.StoreInfPath)!);
        target = Path.GetFullPath(target);
        if (IsWithin(source, target) || IsWithin(target, source)) throw new IOException("备份目录不能包含驱动源目录，也不能位于驱动源目录内");
        var destination = Path.Combine(target, Path.GetFileNameWithoutExtension(pkg.InfName) + "_" + Path.GetFileName(source));
        var files = PackageFiles(source);
        if (!files.Contains(pkg.StoreInfPath, StringComparer.OrdinalIgnoreCase)) throw new IOException("驱动 INF 无法读取");
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var output = Path.Combine(destination, Path.GetRelativePath(source, file));
            EnsureNoReparseAncestors(output);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Copy(file, output, overwrite: true);
        }
    }

    private static bool IsWithin(string root, string path) => path.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static void EnsureNoReparseAncestors(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("备份路径不能经过符号链接或目录联接");
    }

    public static Task<DriverOpResult> ImportAsync(string source, CancellationToken ct = default)
    {
        source = Path.GetFullPath(source);
        return RunNativeAsync(() =>
        {
            var files = Directory.Exists(source) ? PackageFiles(source).Where(f => Path.GetExtension(f).Equals(".inf", StringComparison.OrdinalIgnoreCase)).ToList()
                : File.Exists(source) && Path.GetExtension(source).Equals(".inf", StringComparison.OrdinalIgnoreCase) ? [source] : new List<string>();
            if (files.Count == 0) return DriverOpResult.Fail("没有找到 .inf 驱动文件");
            var errors = new List<string>();
            var reboot = false;
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var result = DriverStoreNative.Install(file);
                reboot |= result.RebootRequired;
                if (!result.Success) errors.Add($"{Path.GetFileName(file)}：{result.Message}");
            }
            return new DriverOpResult(errors.Count == 0, reboot, $"成功 {files.Count - errors.Count} / {files.Count} 个驱动包"
                + (errors.Count > 0 ? "\n" + string.Join("\n", errors) : ""));
        }, ct);
    }

    private static async Task<DriverOpResult> RunNativeAsync(Func<DriverOpResult> action, CancellationToken ct)
    {
        await NativeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                try { return action(); }
                catch (Exception ex) when (ex is not OperationCanceledException) { return DriverOpResult.Fail(ex.Message); }
            }, ct).ConfigureAwait(false);
        }
        finally { NativeGate.Release(); }
    }

    public static string ToCsv(IEnumerable<DriverPackage> packages)
    {
        static string Cell(string value)
        {
            if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+')
                || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder("发布名称,原始名称,提供商,类别,版本,日期,字节数,签名状态,设备关联,启动关键,驱动库路径\r\n");
        foreach (var p in packages)
            csv.AppendLine(string.Join(",", new[] { p.InfName, p.OriginalInfName, p.Provider, p.ClassName, p.Version,
                p.Date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "", p.SizeKnown ? p.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
                p.Signer, p.UsageKnown ? string.Join("; ", p.Devices) : "未知", p.BootCritical ? "是" : "否", p.StoreInfPath }.Select(Cell)));
        return csv.ToString();
    }
}
