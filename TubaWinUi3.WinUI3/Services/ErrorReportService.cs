using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TubaWinUi3.Services;

/// <summary>
/// 错误日志打包：解析 Windows 事件日志（Application）中与本程序相关的崩溃/挂起/报错报告，
/// 连同本程序产生的日志文件与系统信息一起打包为 zip，供用户提交到 GitHub Issue 附件。
/// 所有环节失败都不中止打包，改为在结果中返回提示并写入「说明.txt」。
/// </summary>
public static class ErrorReportService
{
    /// <summary>最多扫描的事件条数（防止 Application 日志过大时长时间占用）。</summary>
    private const int MaxScannedEvents = 5000;

    /// <summary>最多收录的相关事件条数。</summary>
    private const int MaxMatchedEvents = 300;

    /// <summary>单个日志文件的收录上限，超出只保留末尾部分。</summary>
    private const long MaxBytesPerLogFile = 2 * 1024 * 1024;

    /// <summary>
    /// 判定事件日志是否与本程序相关的关键字（大小写不敏感）：
    /// 覆盖主程序 TubaWinUi3.exe、启动器 图吧工具箱WinUI3_*.exe、兼容版、
    /// 后端 TubaWinUI3.BackEnd.exe 与 MSIX 包标识。
    /// </summary>
    private static readonly string[] AppMarkers = ["TubaWinUi3", "图吧工具箱", "DA3D64F4.winui3"];

    /// <summary>事件日志中与崩溃/报错相关的提供程序。</summary>
    private static readonly string[] CrashProviders =
    [
        "Application Error",
        "Application Hang",
        ".NET Runtime",
        "Windows Error Reporting",
    ];

    public sealed record ErrorReportResult(string ZipPath, int EventCount, int LogFileCount, long SizeBytes, string? Warning);

    internal sealed record EventLogItem(DateTime? Time, string Provider, int EventId, string Level, string Message, string Xml);

    internal sealed record LogFileItem(string SourcePath, string EntryName);

    /// <summary>执行完整打包流程：收集事件日志 + 应用日志 + 系统信息 → 写入 zip。</summary>
    public static Task<ErrorReportResult> CreateReportAsync(string? currentErrorText = null, string? systemInfo = null, CancellationToken ct = default)
        => Task.Run(() => CreateReport(currentErrorText, systemInfo, ct), ct);

    private static ErrorReportResult CreateReport(string? currentErrorText, string? systemInfo, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var directory = GetReportsDirectory();
        var now = DateTime.Now;
        var zipPath = BuildUniqueZipPath(directory, now);

        var events = CollectEventLogReports(out var warning);
        ct.ThrowIfCancellationRequested();
        var logs = CollectLogFiles();

        var info = string.IsNullOrWhiteSpace(systemInfo) ? CollectSystemInfo() : systemInfo!;
        WritePackage(zipPath, events, logs, currentErrorText, info, warning, now);

        var size = new FileInfo(zipPath).Length;
        return new ErrorReportResult(zipPath, events.Count, logs.Count, size, warning);
    }

    /// <summary>报告输出目录：%USERPROFILE%\Downloads\TubaWinUi3ErrorReports，失败时回退到数据目录。</summary>
    public static string GetReportsDirectory()
    {
        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile))
            {
                var dir = Path.Combine(profile, "Downloads", "TubaWinUi3ErrorReports");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
        catch { }

        var fallback = Path.Combine(ConfigManager.GetDataDir(), "ErrorReports");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    /// <summary>生成不重名的 zip 路径（同一秒内重复打包自动加序号）。</summary>
    internal static string BuildUniqueZipPath(string directory, DateTime now)
    {
        var stamp = now.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(directory, $"TubaWinUi3-ErrorReport-{stamp}.zip");
        for (var i = 2; File.Exists(path) && i < 1000; i++)
            path = Path.Combine(directory, $"TubaWinUi3-ErrorReport-{stamp}-{i}.zip");
        return path;
    }

    /// <summary>在资源管理器中选中指定文件。</summary>
    public static void RevealInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>判断事件日志文本（原始 XML 或消息）是否与本程序相关（纯函数，可单测）。</summary>
    internal static bool IsAppRelatedEvent(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var marker in AppMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>构造 Application 日志的 XPath 查询（纯函数，可单测）。</summary>
    internal static string BuildEventLogXPath()
    {
        var providers = string.Join(" or ", CrashProviders.Select(p => $"@Name='{p}'"));
        return $"*[System[Provider[{providers}]]]";
    }

    private static List<EventLogItem> CollectEventLogReports(out string? warning)
    {
        var items = new List<EventLogItem>();
        warning = null;

        try
        {
            var query = new EventLogQuery("Application", PathType.LogName, BuildEventLogXPath())
            {
                TolerateQueryErrors = true,
                ReverseDirection = true,
            };

            using var reader = new EventLogReader(query);
            var scanned = 0;

            while (scanned < MaxScannedEvents && items.Count < MaxMatchedEvents)
            {
                using var record = reader.ReadEvent();
                if (record is null) break;
                scanned++;

                var xml = TryToXml(record);
                if (xml is null || !IsAppRelatedEvent(xml)) continue;

                items.Add(new EventLogItem(
                    record.TimeCreated,
                    record.ProviderName ?? "未知来源",
                    record.Id,
                    record.LevelDisplayName ?? "未知",
                    TryFormatDescription(record) ?? "（无法读取事件消息，详见原始 XML）",
                    xml));
            }
        }
        catch (Exception ex)
        {
            warning = $"读取 Windows 事件日志失败：{ex.Message}";
        }

        return items;
    }

    private static string? TryToXml(EventRecord record)
    {
        try { return record.ToXml(); }
        catch { return null; }
    }

    private static string? TryFormatDescription(EventRecord record)
    {
        try { return record.FormatDescription(); }
        catch { return null; }
    }

    /// <summary>收集本程序产生的日志文件（只收录实际存在的）。</summary>
    internal static List<LogFileItem> CollectLogFiles()
    {
        var result = new List<LogFileItem>();
        var tempDir = Path.GetTempPath();

        // 统一日志目录：一个类别一个文件，全部收录（太长时由 AddFileWithCap 只留末尾）
        foreach (var file in LogService.ListFiles())
            AddLog(file.Category + ".log", file.Path);

        // 目录被自定义后，两份历史位置可能还留着内容
        AddLog("legacy-app_crash.log", Path.Combine(tempDir, "app_crash.log"));
        AddLog("legacy-startup_trace.log", Path.Combine(tempDir, "startup_trace.log"));

        // 独立组件进程自己的日志（由 Shell 扩展写，不在统一目录里）
        AddLog("shell-menu-handler.log", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TubaWinUi3", "shell-menu", "handler.log"));

        return result;

        void AddLog(string entryName, string sourcePath)
        {
            try
            {
                if (!File.Exists(sourcePath)) return;
                result.Add(new LogFileItem(sourcePath, MakeUniqueEntryName(result, entryName)));
            }
            catch { }
        }
    }

    private static string MakeUniqueEntryName(List<LogFileItem> existing, string name)
    {
        if (!existing.Any(x => string.Equals(x.EntryName, name, StringComparison.OrdinalIgnoreCase)))
            return name;

        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem}-{i}{ext}";
            if (!existing.Any(x => string.Equals(x.EntryName, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    /// <summary>把收集到的内容写入 zip（内部方法，便于单测）。</summary>
    internal static void WritePackage(
        string zipPath,
        IReadOnlyList<EventLogItem> events,
        IReadOnlyList<LogFileItem> logs,
        string? currentErrorText,
        string systemInfo,
        string? warning,
        DateTime now)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        AddTextEntry(zip, "说明.txt", BuildReadme(events.Count, logs.Count, currentErrorText is not null, warning, now));
        AddTextEntry(zip, "系统信息.txt", systemInfo);

        if (!string.IsNullOrWhiteSpace(currentErrorText))
            AddTextEntry(zip, "异常信息.txt", currentErrorText);

        AddTextEntry(zip, "事件日志/事件日志报告.txt", BuildEventLogReportText(events, now));

        for (var i = 0; i < events.Count; i++)
        {
            var item = events[i];
            var name = $"事件日志/原始XML/Event-{i + 1:000}-{SanitizeNameSegment(item.Provider)}-{item.EventId}.xml";
            AddTextEntry(zip, name, item.Xml);
        }

        foreach (var log in logs)
            AddFileWithCap(zip, $"应用日志/{log.EntryName}", log.SourcePath, MaxBytesPerLogFile);
    }

    internal static void AddTextEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content ?? string.Empty);
    }

    /// <summary>
    /// 收录日志文件；超过 <paramref name="maxBytes"/> 时只保留末尾部分并加截断说明。
    /// 文件被占用（正在写入）或读取失败时不抛出，改为在条目内写失败原因，保证包内容完整可读。
    /// </summary>
    internal static void AddFileWithCap(ZipArchive zip, string entryName, string sourcePath, long maxBytes)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var target = entry.Open();

        FileStream source;
        try
        {
            source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception ex)
        {
            WriteBytes(target, Encoding.UTF8.GetBytes($"（无法读取该日志文件：{ex.Message}）"));
            return;
        }

        using (source)
        {
            if (source.Length <= maxBytes)
            {
                source.CopyTo(target);
                return;
            }

            source.Seek(-maxBytes, SeekOrigin.End);
            WriteBytes(target, Encoding.UTF8.GetBytes($"（内容过长，已截断，仅保留最后 {maxBytes / (1024 * 1024)} MB）\r\n\r\n"));
            source.CopyTo(target);
        }
    }

    private static void WriteBytes(Stream target, byte[] bytes)
        => target.Write(bytes, 0, bytes.Length);

    private static string BuildReadme(int eventCount, int logCount, bool hasError, string? warning, DateTime now)
    {
        var sb = new StringBuilder();
        sb.AppendLine("图吧工具箱CE 错误日志报告");
        sb.AppendLine($"生成时间：{now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"应用版本：{GetAppVersion()}");
        sb.AppendLine();
        sb.AppendLine("包含内容：");
        sb.AppendLine("- 系统信息.txt：运行环境与硬件概况");
        if (hasError)
            sb.AppendLine("- 异常信息.txt：本次未处理异常的详细信息");
        sb.AppendLine($"- 事件日志/：Windows 事件日志（Application 日志）中与本程序相关的崩溃 / 挂起 / 报错报告，共 {eventCount} 条");
        sb.AppendLine($"- 应用日志/：本程序自身产生的日志文件，共 {logCount} 个（过长时仅保留末尾部分）");
        if (!string.IsNullOrEmpty(warning))
            sb.AppendLine($"- 注意：{warning}");
        sb.AppendLine();
        sb.AppendLine("如何提交：");
        sb.AppendLine("1. 打开 https://github.com/luolangaga/tubatool/issues/new");
        sb.AppendLine("2. 描述问题现象与复现步骤");
        sb.AppendLine("3. 把本压缩包直接拖入 Issue 编辑框下方的附件区上传");
        sb.AppendLine();
        sb.AppendLine("隐私提示：压缩包内可能包含用户名、文件路径等本机信息，上传前请自行检查。");
        return sb.ToString();
    }

    private static string BuildEventLogReportText(IReadOnlyList<EventLogItem> items, DateTime now)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Windows 事件日志 —— 与本程序相关的报告");
        sb.AppendLine($"生成时间：{now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("数据来源：事件日志 Application（Application Error / Application Hang / .NET Runtime / Windows Error Reporting）");
        sb.AppendLine();

        if (items.Count == 0)
        {
            sb.AppendLine("未找到与本程序相关的事件日志记录。");
            return sb.ToString();
        }

        sb.AppendLine($"共 {items.Count} 条记录（按时间从新到旧）：");
        sb.AppendLine(new string('=', 72));

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            sb.AppendLine();
            sb.AppendLine($"[{i + 1}] {FormatTime(item.Time)}  来源：{item.Provider}  事件 ID：{item.EventId}  级别：{item.Level}");
            sb.AppendLine(item.Message.Trim());
            sb.AppendLine(new string('-', 72));
        }

        return sb.ToString();
    }

    private static string FormatTime(DateTime? value)
    {
        if (value is null) return "时间未知";
        var time = value.Value;
        if (time.Kind == DateTimeKind.Utc) time = time.ToLocalTime();
        return time.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string SanitizeNameSegment(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        return sb.Length == 0 ? "未知来源" : sb.ToString();
    }

    /// <summary>收集运行环境与硬件信息（错误窗口与打包共用）。</summary>
    public static string CollectSystemInfo()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"应用版本：{GetAppVersion()}");
        sb.AppendLine($"运行模式：{(RuntimeHelper.IsMsixPackaged ? "MSIX 打包版" : "便携版")}");
        sb.AppendLine($"程序路径：{Environment.ProcessPath}");
        sb.AppendLine($"数据目录：{ConfigManager.GetDataDir()}");
        sb.AppendLine($"操作系统：{GetWindowsVersion()}");
        sb.AppendLine($"系统架构：{Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "Unknown"}");
        sb.AppendLine($".NET 版本：{Environment.Version}");
        sb.AppendLine($"管理员权限：{(IsRunningAsAdmin() ? "是" : "否")}");

        try
        {
            sb.AppendLine($"处理器：{WmiQuery("Win32_Processor", "Name")}");
            sb.AppendLine($"内存：{GetTotalMemory()}");
            sb.AppendLine($"显卡：{WmiQuery("Win32_VideoController", "Name")}");
            sb.AppendLine($"主板：{WmiQuery("Win32_BaseBoard", "Product")}");
        }
        catch { }

        if (HardwareInfoService.HasCache)
            sb.AppendLine("硬件缓存：已加载");

        return sb.ToString().TrimEnd();
    }

    private static string GetWindowsVersion()
    {
        try
        {
            var version = Environment.OSVersion.Version;
            var build = version.Build;
            var releaseId = "";

            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (key?.GetValue("DisplayVersion") is string dv)
                    releaseId = dv;
                else if (key?.GetValue("ReleaseId") is string ri)
                    releaseId = ri;
            }
            catch { }

            var name = build >= 26100 ? "Windows 11 24H2"
                     : build >= 22631 ? "Windows 11 23H2"
                     : build >= 22621 ? "Windows 11 22H2"
                     : build >= 22000 ? "Windows 11 21H2"
                     : build >= 19045 ? "Windows 10 22H2"
                     : build >= 19044 ? "Windows 10 21H2"
                     : build >= 19043 ? "Windows 10 21H1"
                     : "Windows";

            if (!string.IsNullOrEmpty(releaseId))
                return $"{name} (Build {build}, {releaseId})";
            return $"{name} (Build {build})";
        }
        catch
        {
            return "Windows (版本未知)";
        }
    }

    private static string GetTotalMemory()
    {
        try
        {
            var gcMem = GC.GetGCMemoryInfo();
            var totalMem = gcMem.TotalAvailableMemoryBytes;
            return $"{totalMem / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }
        catch
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (var obj in searcher.Get())
                {
                    var val = Convert.ToUInt64(obj["TotalPhysicalMemory"]);
                    return $"{val / (1024.0 * 1024.0 * 1024.0):F1} GB";
                }
            }
            catch { }
        }
        return "未知";
    }

    private static string WmiQuery(string className, string propertyName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {className}");
            foreach (var obj in searcher.Get())
            {
                var val = obj[propertyName]?.ToString();
                if (!string.IsNullOrEmpty(val)) return val;
            }
        }
        catch { }
        return "未知";
    }

    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static string GetAppVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is not null ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
    }
}
