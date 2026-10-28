using System.Text;

namespace TubaWinUi3.Services;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>日志目录里的一个日志文件（一个类别一个文件）。</summary>
public sealed record LogFile(string Category, string Path, long SizeBytes, DateTime Modified);


/// <summary>
/// 全局唯一的日志写入点：所有日志统一落在 <see cref="LogDirectory"/> 下，一个类别一个文件
/// （<c>&lt;类别&gt;.log</c>），超过上限自动滚动成 <c>.1 / .2 / .3</c>。
///
/// <para>任何写失败都静默忽略——日志本身绝不能成为新的崩溃源，崩溃路径也会走到这里。
/// 目录可在设置页自定义（<c>LogDir</c>），目录不存在或不可写时回退数据目录下的 <c>Logs</c>。</para>
///
/// <para>新增日志来源时不要再自己拼路径/自己轮转，调 <see cref="Info(string,string)"/> 等即可，
/// 这样「设置页改目录」「存储清理」「错误报告打包」「运行日志页」全都自动覆盖到。</para>
/// </summary>
public static class LogService
{
    /// <summary>设置页自定义日志目录的键。</summary>
    public const string LogDirSettingKey = "LogDir";

    /// <summary>单文件滚动阈值。</summary>
    public const long MaxFileBytes = 1L * 1024 * 1024;

    /// <summary>保留的历史滚动文件个数（<c>.1</c> 最新）。</summary>
    public const int MaxRotatedFiles = 3;

    /// <summary>
    /// 整个日志目录的总配额。超过后从「最旧的滚动副本」开始删，其次是最久没写过的非关键主文件，
    /// 直到回落至配额的 <see cref="QuotaTargetRatio"/>。关键类别（crash/startup/app）的主文件永不删除。
    /// </summary>
    public const long MaxTotalBytes = 10L * 1024 * 1024;

    /// <summary>清理目标 = 配额的 80%：一次多删一点，避免每次写入都触发清理。</summary>
    internal const double QuotaTargetRatio = 0.8;

    /// <summary>两次配额检查的最小间隔（写入很密集时不至于每条日志都扫目录）。</summary>
    private static readonly TimeSpan QuotaCheckInterval = TimeSpan.FromSeconds(60);

    // ── 日志类别（文件名即类别）─────────────────────────────────
    public const string App = "app";
    public const string Startup = "startup";
    public const string Crash = "crash";
    public const string Agent = "agent";
    public const string Clipboard = "clipboard";
    public const string GameTunnel = "game-tunnel";
    public const string GameOverlay = "game-overlay";
    public const string StressTest = "stress-test";
    public const string Backend = "backend";
    public const string RogueCleaner = "rogue-cleaner";
    public const string PhoneLink = "phone-link";

    /// <summary>已知类别，决定运行日志页的顺序。</summary>
    public static IReadOnlyList<string> KnownCategories { get; } =
    [
        App, Startup, Crash, Agent, GameTunnel, GameOverlay, StressTest,
        Backend, RogueCleaner, Clipboard, PhoneLink
    ];

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>串行化全部写入与滚动，避免多线程同时滚动/交错写。</summary>
    private static readonly object Gate = new();

    /// <summary>各文件当前长度缓存（只在本进程内维护，供滚动判断，省掉每次写入前的 stat）。</summary>
    private static readonly Dictionary<string, long> Sizes = new(StringComparer.OrdinalIgnoreCase);

    private static int _prepared;

    // ── 目录 ───────────────────────────────────────────────

    /// <summary>默认日志目录：数据目录下的 Logs。</summary>
    public static string DefaultLogDirectory => Path.Combine(ConfigManager.GetDataDir(), "Logs");

    /// <summary>当前生效的日志目录：自定义目录存在即用，否则回退默认目录。</summary>
    public static string LogDirectory => ResolveLogDirectory();

    /// <summary>某个类别的日志文件路径。</summary>
    public static string PathFor(string category)
        => Path.Combine(LogDirectory, Sanitize(category) + ".log");

    /// <summary>解析当前生效的日志目录（纯逻辑，可单测）。</summary>
    internal static string ResolveLogDirectory()
    {
        try
        {
            var custom = AppSettings.Get(LogDirSettingKey);
            if (!string.IsNullOrWhiteSpace(custom) && System.IO.Directory.Exists(custom))
                return custom;
        }
        catch
        {
            // 崩溃路径也会走到这里：设置读取失败绝不能拦住日志落盘
        }
        return DefaultLogDirectory;
    }

    /// <summary>校验候选目录可写（写入并删除探测文件）。</summary>
    public static bool IsDirectoryWritable(string dir)
    {
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".tuba_log_probe_{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static string Sanitize(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) return App;
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(category.Length);
        foreach (var c in category)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    // ── 写入 ───────────────────────────────────────────────

    public static void Debug(string category, string message) => Write(LogLevel.Debug, category, message);

    public static void Info(string category, string message) => Write(LogLevel.Info, category, message);

    public static void Warn(string category, string message) => Write(LogLevel.Warn, category, message);

    public static void Error(string category, string message) => Write(LogLevel.Error, category, message);

    public static void Error(string category, string message, Exception? ex)
        => Write(LogLevel.Error, category, ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    /// <summary>按级别写一条（多行消息的第二行起不加前缀，保持异常堆栈可读）。</summary>
    public static void Write(LogLevel level, string category, string message)
    {
        var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var tag = level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Warn => "WARN ",
            LogLevel.Error => "ERROR",
            _ => "INFO "
        };

        var text = message ?? string.Empty;
        var newline = Environment.NewLine;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var payload = new StringBuilder(text.Length + 32);
        payload.Append(time).Append(" [").Append(tag).Append("] ").Append(lines[0]).Append(newline);
        for (var i = 1; i < lines.Length; i++)
            payload.Append("    ").Append(lines[i]).Append(newline);

        Append(category, payload.ToString());
    }

    /// <summary>
    /// 原样写入一段文本（调用方已自带时间戳/格式，如崩溃报告、启动轨迹）。
    /// 保证结尾有换行，其余不做任何加工。
    /// </summary>
    public static void WriteBlock(string category, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Append(category, text.EndsWith('\n') ? text : text + Environment.NewLine);
    }

    /// <summary>清空某个类别的日志（含滚动文件），如启动轨迹每轮重写。</summary>
    public static void ResetFile(string category, string? header = null)
    {
        lock (Gate)
        {
            try
            {
                EnsureDirectory(Path.GetDirectoryName(PathFor(category)));
                Clear(category);
                if (!string.IsNullOrEmpty(header)) WriteBlock(category, header);
            }
            catch { }
        }
    }

    private static void Append(string category, string text)
    {
        var path = PathFor(category);
        lock (Gate)
        {
            try
            {
                EnsureDirectory(Path.GetDirectoryName(path));
                RotateIfNeeded(path);
                using var stream = new FileStream(
                    path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                var bytes = Utf8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
                Sizes[path] = stream.Length;
            }
            catch
            {
                Sizes.Remove(path);
            }

            MaybeEnforceQuota();
        }
    }

    // ── 配额 ───────────────────────────────────────────────

    /// <summary>需要保护的类别：这三个是排障的核心，永远不因总配额被删（滚动副本仍会清理）。</summary>
    private static bool IsProtectedCategory(string category)
        => string.Equals(category, Crash, StringComparison.OrdinalIgnoreCase)
           || string.Equals(category, Startup, StringComparison.OrdinalIgnoreCase)
           || string.Equals(category, App, StringComparison.OrdinalIgnoreCase);

    private static DateTime _lastQuotaCheckUtc = DateTime.MinValue;

    /// <summary>写入后按间隔节流检查一次总量（崩溃/异常日志量小，不至于每次写入都扫目录）。</summary>
    private static void MaybeEnforceQuota()
    {
        var now = DateTime.UtcNow;
        if (now - _lastQuotaCheckUtc < QuotaCheckInterval) return;
        _lastQuotaCheckUtc = now;
        EnforceQuota(MaxTotalBytes);
    }

    /// <summary>立即执行一次总量配额清理（启动后一次、打开日志页时一次）。返回删除的文件数。</summary>
    public static int EnforceQuota() => EnforceQuota(MaxTotalBytes);

    /// <summary>
    /// 目录内日志总量超过 <paramref name="maxBytes"/> 时，按「最旧的滚动副本 → 最久没写过的
    /// 非关键主文件」顺序删除，直到降到配额的 <see cref="QuotaTargetRatio"/>。删除失败（被占用）跳过。
    /// </summary>
    internal static int EnforceQuota(long maxBytes)
    {
        var dir = LogDirectory;
        List<QuotaEntry> entries;
        try
        {
            if (!System.IO.Directory.Exists(dir)) return 0;
            entries = System.IO.Directory.EnumerateFiles(dir, "*.log*")
                .Select(TryDescribe)
                .Where(entry => entry is not null)
                .Select(entry => entry!)
                .ToList();
        }
        catch
        {
            return 0;
        }

        var total = entries.Sum(entry => entry.Size);
        if (total <= maxBytes) return 0;

        var target = (long)(maxBytes * QuotaTargetRatio);
        var deleted = 0;

        foreach (var entry in entries
                     .Where(entry => entry.IsRotated || !IsProtectedCategory(entry.Category))
                     .OrderBy(entry => entry.IsRotated ? 0 : 1)
                     .ThenBy(entry => entry.LastWriteUtc))
        {
            if (total <= target) break;
            try
            {
                File.Delete(entry.Path);
                total -= entry.Size;
                deleted++;
                lock (Gate) Sizes.Remove(entry.Path);
            }
            catch
            {
                // 被别的进程占着就跳过，下次再试
            }
        }

        return deleted;
    }

    private sealed record QuotaEntry(string Path, string Category, bool IsRotated, long Size, DateTime LastWriteUtc);

    private static QuotaEntry? TryDescribe(string path)
    {
        try
        {
            var name = Path.GetFileName(path);
            var rotated = false;

            // <类别>.log 或 <类别>.log.N
            var dot = name.LastIndexOf('.');
            if (dot > 0 && int.TryParse(name[(dot + 1)..], out _)) rotated = true;

            var logIndex = name.IndexOf(".log", StringComparison.OrdinalIgnoreCase);
            if (logIndex <= 0) return null;
            var category = name[..logIndex];

            var info = new FileInfo(path);
            if (!info.Exists) return null;
            return new QuotaEntry(path, category, rotated, info.Length, info.LastWriteTimeUtc);
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureDirectory(string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        System.IO.Directory.CreateDirectory(dir);
    }

    private static void RotateIfNeeded(string path)
    {
        if (SizeOf(path) <= MaxFileBytes) return;

        try
        {
            if (!File.Exists(path))
            {
                Sizes[path] = 0;
                return;
            }

            var oldest = $"{path}.{MaxRotatedFiles}";
            if (File.Exists(oldest)) File.Delete(oldest);

            for (var i = MaxRotatedFiles - 1; i >= 1; i--)
            {
                var from = $"{path}.{i}";
                if (File.Exists(from)) File.Move(from, $"{path}.{i + 1}", overwrite: true);
            }

            File.Move(path, $"{path}.1", overwrite: true);
            Sizes[path] = 0;
        }
        catch
        {
            // 滚动失败就继续往原文件追加，不影响日志本身
            Sizes.Remove(path);
        }
    }

    private static long SizeOf(string path)
    {
        if (Sizes.TryGetValue(path, out var size)) return size;
        try
        {
            size = File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            size = 0;
        }
        Sizes[path] = size;
        return size;
    }

    // ── 初始化 / 旧位置迁移 ────────────────────────────────

    /// <summary>确保目录存在，并把散落在旧位置的历史日志并入统一目录（每进程只做一次）。</summary>
    public static void Prepare()
    {
        try
        {
            EnsureDirectory(LogDirectory);
        }
        catch { }

        if (Interlocked.Exchange(ref _prepared, 1) != 0) return;
        MigrateLegacyLogs();
    }

    /// <summary>
    /// 迁移历史日志：早期各功能自己拼路径（数据目录根部、功能子目录、%TEMP%），
    /// 这里一次性并入 <c>&lt;类别&gt;.log</c> 后删掉旧文件，避免用户升级后日志「人间蒸发」。
    /// </summary>
    private static void MigrateLegacyLogs()
    {
        var dataDir = SafeDataDir();
        var tempDir = Path.GetTempPath();

        var legacy = new List<(string Path, string Category)>
        {
            (Path.Combine(tempDir, "app_crash.log"), Crash),
            (Path.Combine(tempDir, "startup_trace.log"), Startup),
        };

        if (dataDir is not null)
        {
            legacy.Add((Path.Combine(dataDir, "Logs", "app_crash.log"), Crash));
            legacy.Add((Path.Combine(dataDir, "Logs", "startup_trace.log"), Startup));
            legacy.Add((Path.Combine(dataDir, "agent-debug.log"), Agent));
            legacy.Add((Path.Combine(dataDir, "game_overlay_auto.log"), GameOverlay));
            legacy.Add((Path.Combine(dataDir, "stress_test.log"), StressTest));
            legacy.Add((Path.Combine(dataDir, "active_intercept", "backend.log"), Backend));
            legacy.Add((Path.Combine(dataDir, "Clipboard", "error.log"), Clipboard));

            try
            {
                var rogueLogs = Path.Combine(dataDir, "RogueCleaner", "logs");
                if (System.IO.Directory.Exists(rogueLogs))
                {
                    foreach (var file in System.IO.Directory.EnumerateFiles(rogueLogs, "*.log")
                                 .OrderByDescending(File.GetLastWriteTimeUtc)
                                 .Take(5)
                                 .Reverse())
                    {
                        legacy.Add((file, RogueCleaner));
                    }
                }
            }
            catch { }
        }

        foreach (var (path, category) in legacy)
        {
            try
            {
                if (!File.Exists(path)) continue;
                var target = PathFor(category);
                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                    continue;

                var content = File.ReadAllText(path);
                if (!string.IsNullOrWhiteSpace(content)) WriteBlock(category, content);
                File.Delete(path);
            }
            catch { } // 迁移失败保留原文件
        }
    }

    private static string? SafeDataDir()
    {
        try { return ConfigManager.GetDataDir(); }
        catch { return null; }
    }

    // ── 查询 / 维护 ────────────────────────────────────────

    /// <summary>当前目录下的日志文件（不含滚动副本），按已知类别顺序排列。</summary>
    public static IReadOnlyList<LogFile> ListFiles()
    {
        var dir = LogDirectory;
        var result = new List<LogFile>();

        try
        {
            if (!System.IO.Directory.Exists(dir)) return result;
            foreach (var file in System.IO.Directory.EnumerateFiles(dir, "*.log"))
            {
                try
                {
                    var info = new FileInfo(file);
                    result.Add(new LogFile(Path.GetFileNameWithoutExtension(file), file, info.Length, info.LastWriteTime));
                }
                catch { }
            }
        }
        catch { }

        return result
            .OrderBy(f => CategoryOrder(f.Category))
            .ThenBy(f => f.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int CategoryOrder(string category)
    {
        for (var i = 0; i < KnownCategories.Count; i++)
        {
            if (string.Equals(KnownCategories[i], category, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return KnownCategories.Count;
    }

    /// <summary>类别的中文显示名（未知类别原样返回）。</summary>
    public static string DisplayName(string category) => category switch
    {
        App => LocalizationService.L("Log_File_app", "应用日志"),
        Startup => LocalizationService.L("Log_File_startup", "启动轨迹"),
        Crash => LocalizationService.L("Log_File_crash", "崩溃与异常"),
        Agent => LocalizationService.L("Log_File_agent", "AI 助手"),
        Clipboard => LocalizationService.L("Log_File_clipboard", "剪贴板"),
        GameTunnel => LocalizationService.L("Log_File_game-tunnel", "游戏联机"),
        GameOverlay => LocalizationService.L("Log_File_game-overlay", "游戏监控"),
        StressTest => LocalizationService.L("Log_File_stress-test", "压力测试"),
        Backend => LocalizationService.L("Log_File_backend", "主动拦截后端"),
        RogueCleaner => LocalizationService.L("Log_File_rogue-cleaner", "流氓软件清理"),
        PhoneLink => LocalizationService.L("Log_File_phone-link", "连接手机"),
        _ => category
    };

    /// <summary>
    /// 读取文件末尾内容（默认最后 2000 行 / 512 KB，够看一个崩溃上下文又不至于卡界面）。
    /// </summary>
    public static string ReadTail(string path, int maxLines = 2000, int maxBytes = 512 * 1024)
    {
        try
        {
            if (!File.Exists(path)) return string.Empty;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - maxBytes);
            stream.Seek(start, SeekOrigin.Begin);

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();

            if (start > 0)
            {
                var cut = text.IndexOf('\n');
                if (cut >= 0) text = text[(cut + 1)..];
            }

            text = text.Replace("\r\n", "\n").TrimEnd('\n');
            var lines = text.Length == 0 ? Array.Empty<string>() : text.Split('\n');
            if (lines.Length > maxLines) lines = lines[^maxLines..];

            return string.Join("\n", lines);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>按类别读取末尾内容。</summary>
    public static string ReadTailFor(string category, int maxLines = 2000, int maxBytes = 512 * 1024)
        => ReadTail(PathFor(category), maxLines, maxBytes);

    /// <summary>清空某个类别（含滚动副本）。</summary>
    public static void Clear(string category)
    {
        var path = PathFor(category);
        lock (Gate)
        {
            try
            {
                TryDelete(path);
                for (var i = 1; i <= MaxRotatedFiles; i++) TryDelete($"{path}.{i}");
                Sizes.Remove(path);
            }
            catch { }
        }
    }

    /// <summary>清空目录下全部日志。</summary>
    public static void ClearAll()
    {
        foreach (var file in ListFiles()) Clear(file.Category);
    }

    /// <summary>目录内全部日志（含滚动副本）的字节数。</summary>
    public static long TotalSizeBytes()
    {
        long total = 0;
        try
        {
            if (!System.IO.Directory.Exists(LogDirectory)) return 0;
            foreach (var file in System.IO.Directory.EnumerateFiles(LogDirectory, "*.log*"))
            {
                try { total += new FileInfo(file).Length; } catch { }
            }
        }
        catch { }
        return total;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}
