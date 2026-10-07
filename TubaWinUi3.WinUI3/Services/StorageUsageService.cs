using System.IO;

namespace TubaWinUi3.Services;

/// <summary>
/// 存储占用分组：决定弹窗中的分区、默认勾选状态与风险提示。
/// </summary>
public enum StorageGroupKind
{
    /// <summary>程序本体与第三方工具，属于安装内容，不可在此删除。</summary>
    Program,
    /// <summary>可自动重建的缓存，删除后无副作用。</summary>
    Cache,
    /// <summary>内置工具按需下载的运行时/组件，删除后下次用到时会重新下载。</summary>
    Component,
    /// <summary>本软件下载的文件（网页下载、UUP Dump 包等），属于用户主动获取的内容。</summary>
    Download,
    /// <summary>日志与诊断产物。</summary>
    Log,
    /// <summary>系统临时目录中的残留。</summary>
    Temp,
    /// <summary>用户数据（聊天记录、接收的文件等），删除不可恢复。</summary>
    UserData
}

/// <summary>一个可度量的存储条目。</summary>
public sealed class StorageItem
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public StorageGroupKind Kind { get; init; }

    /// <summary>参与统计与删除的绝对路径（文件或目录）。</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    /// <summary>统计时排除的子路径，避免与其它条目重复计数。</summary>
    public IReadOnlyList<string> ExcludedPaths { get; init; } = [];

    /// <summary>弹窗打开时的默认勾选状态。</summary>
    public bool DefaultChecked { get; init; }

    public long SizeBytes { get; set; }
    public int FileCount { get; set; }

    /// <summary>只展示不删除（如程序本体）。</summary>
    public bool CanDelete => Paths.Count > 0;

    /// <summary>供界面显示的主路径。</summary>
    public string DisplayPath => Paths.Count switch
    {
        0 => string.Empty,
        1 => Paths[0],
        _ => $"{Paths[0]} 等 {Paths.Count} 处"
    };
}

/// <summary>
/// 清点图吧工具箱的磁盘占用：程序目录、缓存、内置工具产物、日志、临时文件与个人数据。
/// 每个条目独立计算大小并可单独删除；被占用或无权访问的文件自动跳过，不影响正在运行的任务。
/// </summary>
public static class StorageUsageService
{
    private const int MeasureConcurrency = 4;

    public sealed record ScanProgress(int Completed, int Total, StorageItem Item);

    public sealed record DeleteOutcome(long FreedBytes, int DeletedCount, int SkippedCount, IReadOnlyList<string> FailedPaths);

    /// <summary>按当前安装/数据位置生成条目清单（大小尚未测量）。</summary>
    public static IReadOnlyList<StorageItem> CreateCatalog()
    {
        var appDir = ToolCatalog.AppDirectory;
        var dataDir = ConfigManager.GetDataDir();
        var toolsRoot = Capture(() => ToolCatalog.ToolsRoot);
        var webView2Dir = WebView2EnvironmentService.UserDataFolder;

        var items = new List<StorageItem>();

        // ── 程序本体与工具：只展示，不可删除 ──────────────────
        var programExclusions = new List<string>();
        if (!string.IsNullOrEmpty(toolsRoot)) programExclusions.Add(toolsRoot);
        if (IsUnder(dataDir, appDir)) programExclusions.Add(dataDir);

        items.Add(new StorageItem
        {
            Id = "program",
            Name = "程序本体",
            Description = "图吧工具箱自身的可执行文件、资源与内置图标缓存。",
            Kind = StorageGroupKind.Program,
            Paths = Directory.Exists(appDir) ? [appDir] : [],
            ExcludedPaths = programExclusions
        });

        if (!string.IsNullOrEmpty(toolsRoot))
        {
            items.Add(new StorageItem
            {
                Id = "tools",
                Name = "第三方工具",
                Description = "Tools 目录下由你放置的硬件检测与测试工具。",
                Kind = StorageGroupKind.Program,
                Paths = Existing(toolsRoot)
            });
        }

        // ── 可自动重建的缓存 ─────────────────────────────────
        items.Add(FileItem(StorageGroupKind.Cache, "icon-cache", "工具图标缓存",
            "从各工具可执行文件提取的图标，删除后浏览工具时会自动重建。", ConfigManager.GetIconCacheDir()));
        items.Add(FileItem(StorageGroupKind.Cache, "desktop-icons", "桌面快捷方式图标",
            "发送到桌面时生成的图标文件，下次发送时自动重建。", Path.Combine(dataDir, "DesktopIcons")));
        items.Add(FileItem(StorageGroupKind.Cache, "game-logos", "游戏图标缓存",
            "游戏联机助手中下载的游戏 Logo，再次打开页面时重新下载。", Path.Combine(dataDir, "GameLogos")));
        items.Add(FileItem(StorageGroupKind.Cache, "latency-images", "网络延迟图缓存",
            "网络测试中下载的延迟分布图，重新测试时下载。", Path.Combine(dataDir, "Cache")));
        items.Add(FileItem(StorageGroupKind.Cache, "benchmark-cache", "性能测试缓存",
            "跑分工具的中间结果缓存，重新测试时生成。", Path.Combine(dataDir, "BenchmarkCache")));
        items.Add(FileItem(StorageGroupKind.Cache, "webview2", "网页渲染缓存",
            "内置浏览器（AI 助手、格式转换等）的 WebView2 数据目录。正在使用中的文件会被跳过，关闭软件后再清理可全部释放。",
            webView2Dir));

        // ── 内置工具按需下载的组件 ───────────────────────────
        items.Add(FileItem(StorageGroupKind.Component, "ffmpeg", "FFmpeg",
            "格式转换与视频处理的音视频引擎，再次转换时重新下载。", Path.Combine(dataDir, "ffmpeg")));
        items.Add(FileItem(StorageGroupKind.Component, "imagemagick", "ImageMagick",
            "图片格式转换引擎，再次转换时重新下载。", Path.Combine(dataDir, "imagemagick")));
        items.Add(FileItem(StorageGroupKind.Component, "officecli", "OfficeCLI",
            "文档（Word/Excel/PPT）高保真转换引擎，约 33 MB，再次转换时重新下载。", Path.Combine(dataDir, "officecli")));
        items.Add(FileItem(StorageGroupKind.Component, "sysinternals", "Sysinternals 工具",
            "启动项管理器使用的微软官方工具集，再次打开时重新下载。", Path.Combine(dataDir, "Sysinternals")));
        items.Add(FileItem(StorageGroupKind.Component, "dotnet-downloads", ".NET 运行时包",
            "运行库修复功能下载的安装包，再次修复时重新下载。", Path.Combine(dataDir, "DotnetDownloads")));
        items.Add(FileItem(StorageGroupKind.Component, "runtime-repair", "运行库修复缓存",
            "运行库检测与修复过程中产生的下载缓存。", Path.Combine(dataDir, "RuntimeRepair")));
        items.Add(FileItem(StorageGroupKind.Component, "installers", "安装包暂存",
            "PawnIO 等驱动的安装包暂存，需要时重新下载。", Path.Combine(dataDir, "downloads")));
        items.Add(FileItem(StorageGroupKind.Component, "ai-models", "本地 AI 模型",
            "「本地AI试炼场」下载的模型与 NPU 编译缓存，删除后需要重新下载。", Path.Combine(dataDir, "ai-models")));

        // ── 下载的文件：默认不勾选 ───────────────────────────
        items.Add(new StorageItem
        {
            Id = "downloads",
            Name = "网页下载与 UUP Dump 包",
            Description = "「网页下载」与「Windows 镜像下载」保存的文件（含 UUP 文件集与转换中间产物），删除后需要重新下载。",
            Kind = StorageGroupKind.Download,
            Paths = Existing(GetHttpDownloadPath())
        });

        // ── 日志与诊断 ─────────────────────────────────────
        items.Add(FileItem(StorageGroupKind.Log, "stress-log", "压力测试日志",
            "烤机与网络压力测试的过程记录。", Path.Combine(dataDir, "stress_test.log")));
        items.Add(FileItem(StorageGroupKind.Log, "agent-log", "AI 调试日志",
            "AI 助手运行时的调试输出。", Path.Combine(dataDir, "agent-debug.log")));
        items.Add(FileItem(StorageGroupKind.Log, "overlay-log", "游戏监控日志",
            "游戏覆盖层自动启动的记录。", Path.Combine(dataDir, "game_overlay_auto.log")));
        items.Add(FileItem(StorageGroupKind.Log, "sensor-dump", "传感器转储",
            "硬件传感器信息导出的文本快照。", Path.Combine(dataDir, "sensor_dump.txt")));
        items.Add(FileItem(StorageGroupKind.Log, "error-reports", "错误报告存档",
            "崩溃与异常上报的存档，用于反馈问题。", Path.Combine(dataDir, "ErrorReports")));
        items.Add(FileItem(StorageGroupKind.Log, "crash-log", "崩溃日志",
            "系统临时目录中的 app_crash.log。", Path.Combine(Path.GetTempPath(), "app_crash.log")));

        // ── 系统临时目录残留 ────────────────────────────────
        items.Add(MultiItem(StorageGroupKind.Temp, "temp", "系统临时文件",
            "更新包、工具暂存、文档转换中间产物、崩溃报告等在 %TEMP% 中的残留，正在使用中的会被跳过。",
            TempCleanupService.EnumerateAppTempEntries()));

        // ── 个人数据：默认不勾选 ─────────────────────────────
        items.Add(FileItem(StorageGroupKind.UserData, "ai-data", "AI 聊天记录与记忆",
            "AI 助手的会话记录、长期记忆与导入的技能。", Path.Combine(dataDir, "AiAssistant")));
        items.Add(FileItem(StorageGroupKind.UserData, "active-intercept", "主动拦截记录",
            "拦截事件、信任策略与已忽略项。", Path.Combine(dataDir, "active_intercept")));
        items.Add(FileItem(StorageGroupKind.UserData, "game-tunnel", "联机助手数据",
            "自定义游戏档案、Tailscale API 令牌与生成的加入脚本。", Path.Combine(dataDir, "GameTunnel")));
        items.Add(FileItem(StorageGroupKind.UserData, "junk-rules", "垃圾清理规则库",
            "从网络更新的 Winapp2 规则库副本与自定义规则，删除后回退到内置规则。", Path.Combine(dataDir, "JunkCleaner")));
        items.Add(FileItem(StorageGroupKind.UserData, "time-sync", "时间同步设置记录",
            "已应用的 NTP 服务器列表与同步设置。", Path.Combine(dataDir, "TimeSync")));
        items.Add(FileItem(StorageGroupKind.UserData, "lan-share", "局域网传输文件",
            "「文件传输」共享目录中的文件，删除后无法再通过链接取回。", Path.Combine(dataDir, "LanShare")));
        items.Add(FileItem(StorageGroupKind.UserData, "backgrounds", "自定义背景图",
            "设置中导入的界面背景图片。", Path.Combine(dataDir, "Backgrounds")));
        items.Add(FileItem(StorageGroupKind.UserData, "monitor-records", "游戏监控数据",
            "游戏内实时监控采集的帧率与硬件数据记录。", Path.Combine(dataDir, "GameMonitorRecords")));
        items.Add(FileItem(StorageGroupKind.UserData, "startup-manager", "启动项管理数据",
            "被禁用的启动项备份与导出的启动项清单。",
            Path.Combine(dataDir, "启动项管理"), Path.Combine(dataDir, "启动项导出")));
        items.Add(FileItem(StorageGroupKind.UserData, "benchmark-history", "性能测试历史",
            "硬件跑分与整机性能测试的历史成绩。",
            Path.Combine(dataDir, "BenchmarkHistory.json"), Path.Combine(dataDir, "WinBenchmarkHistory.json")));

        return items;
    }

    /// <summary>测量所有条目的占用大小，并发受限以便磁盘顺序读取。</summary>
    public static async Task ScanAsync(
        IReadOnlyList<StorageItem> items,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var gate = new SemaphoreSlim(MeasureConcurrency);
        var completed = 0;

        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (size, files) = MeasureItem(item);
                item.SizeBytes = size;
                item.FileCount = files;
            }
            finally
            {
                gate.Release();
                var done = Interlocked.Increment(ref completed);
                progress?.Report(new ScanProgress(done, items.Count, item));
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>统计单个条目的字节数与文件数（跳过排除项、符号链接与无权访问的项）。</summary>
    public static (long Size, int Files) MeasureItem(StorageItem item)
    {
        long size = 0;
        var files = 0;
        foreach (var path in item.Paths)
        {
            var (pathSize, pathFiles) = MeasurePath(path, item.ExcludedPaths);
            size += pathSize;
            files += pathFiles;
        }
        return (size, files);
    }

    /// <summary>统计文件或目录的大小；目录递归时跳过排除项与重解析点。</summary>
    public static (long Size, int Files) MeasurePath(string path, IReadOnlyList<string>? exclusions = null)
    {
        try
        {
            if (File.Exists(path))
                return (new FileInfo(path).Length, 1);
            if (!Directory.Exists(path)) return (0, 0);
        }
        catch
        {
            return (0, 0);
        }

        long size = 0;
        var files = 0;
        var pending = new Stack<string>();
        pending.Push(path);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(current); }
            catch { continue; }

            foreach (var entry in entries)
            {
                if (IsExcluded(entry, exclusions)) continue;
                try
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                    else
                    {
                        size += new FileInfo(entry).Length;
                        files++;
                    }
                }
                catch { }
            }
        }

        return (size, files);
    }

    /// <summary>删除选中条目的全部内容，返回释放的空间与失败项。</summary>
    public static DeleteOutcome Delete(
        IEnumerable<StorageItem> items,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var targets = items.Where(i => i.CanDelete).ToArray();
        long freed = 0;
        var deleted = 0;
        var skipped = 0;
        var failed = new List<string>();
        var completed = 0;

        foreach (var item in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var path in item.Paths)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        freed += TempCleanupService.MeasureDirectory(path);
                        if (TempCleanupService.TryDeleteDirectory(path)) deleted++;
                        else { skipped++; failed.Add(path); }
                    }
                    else if (File.Exists(path))
                    {
                        freed += new FileInfo(path).Length;
                        if (TempCleanupService.TryDeleteFile(path)) deleted++;
                        else { skipped++; failed.Add(path); }
                    }
                }
                catch
                {
                    skipped++;
                    failed.Add(path);
                }
            }

            var done = Interlocked.Increment(ref completed);
            progress?.Report(new ScanProgress(done, targets.Length, item));
        }

        return new DeleteOutcome(freed, deleted, skipped, failed);
    }

    /// <summary>把分组类型转成中文标签。</summary>
    public static string GroupLabel(StorageGroupKind kind) => kind switch
    {
        StorageGroupKind.Program => "程序与工具",
        StorageGroupKind.Cache => "缓存",
        StorageGroupKind.Component => "内置工具组件",
        StorageGroupKind.Download => "下载的文件",
        StorageGroupKind.Log => "日志与诊断",
        StorageGroupKind.Temp => "系统临时文件",
        StorageGroupKind.UserData => "个人数据",
        _ => "其它"
    };

    /// <summary>分组适用的说明文案。</summary>
    public static string GroupHint(StorageGroupKind kind) => kind switch
    {
        StorageGroupKind.Program => "安装内容，不能在此删除",
        StorageGroupKind.Cache => "删除后会自动重建，不影响使用",
        StorageGroupKind.Component => "删除后下次用到时会重新下载",
        StorageGroupKind.Download => "删除后需要重新下载",
        StorageGroupKind.Log => "仅保留最近记录即可，可放心删除",
        StorageGroupKind.Temp => "残留文件，可放心删除",
        StorageGroupKind.UserData => "删除后无法恢复，请谨慎选择",
        _ => string.Empty
    };

    /// <summary>分组在占用条上的配色。</summary>
    public static string GroupColor(StorageGroupKind kind) => kind switch
    {
        StorageGroupKind.Program => "#4C8BF5",
        StorageGroupKind.Cache => "#3FB6A8",
        StorageGroupKind.Component => "#F0A03C",
        StorageGroupKind.Download => "#E2685E",
        StorageGroupKind.Log => "#9B7BD4",
        StorageGroupKind.Temp => "#7A8A99",
        StorageGroupKind.UserData => "#C97FA8",
        _ => "#8A8A8A"
    };

    private static StorageItem FileItem(StorageGroupKind kind, string id, string name, string description, params string[] paths)
        => MultiItem(kind, id, name, description, paths);

    private static StorageItem MultiItem(StorageGroupKind kind, string id, string name, string description, IEnumerable<string> paths)
        => new()
        {
            Id = id,
            Name = name,
            Description = description,
            Kind = kind,
            Paths = ExistingPaths(paths),
            DefaultChecked = kind is not StorageGroupKind.UserData and not StorageGroupKind.Download
        };

    /// <summary>读取「网页下载」的保存位置，与设置页保持同一解析逻辑。</summary>
    private static string GetHttpDownloadPath()
        => PathResolver.MakeAbsolute(AppSettings.Get("HttpDownloadPath"))
           ?? Path.Combine(ConfigManager.GetDataDir(), "download");

    private static IReadOnlyList<string> Existing(params string[] paths) => ExistingPaths(paths);

    private static IReadOnlyList<string> ExistingPaths(IEnumerable<string> paths)
        => paths.Where(p => !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p))).ToArray();

    private static bool IsExcluded(string path, IReadOnlyList<string>? exclusions)
    {
        if (exclusions is null || exclusions.Count == 0) return false;
        foreach (var exclusion in exclusions)
        {
            if (string.Equals(Trim(path), Trim(exclusion), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsUnder(string path, string parent)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(parent)) return false;
        var normalizedParent = Trim(parent) + Path.DirectorySeparatorChar;
        return Trim(path).StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static string Trim(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string Capture(Func<string> factory)
    {
        try { return factory(); }
        catch { return string.Empty; }
    }
}
