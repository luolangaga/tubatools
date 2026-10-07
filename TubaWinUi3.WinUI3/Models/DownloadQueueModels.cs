using System.ComponentModel;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace TubaWinUi3.Models;

public sealed record DownloadQueueProgress(
    long BytesReceived,
    long TotalBytes,
    double Percentage,
    double SpeedMbps,
    TimeSpan? EstimatedRemaining);

public enum DownloadItemState
{
    Queued,
    Resolving,
    Downloading,
    Paused,
    Processing,
    Completed,
    Failed,
    Cancelled
}

public sealed record ResolvedDownloadUrl(string Url, string FileName, long Size = 0);

public sealed class DownloadQueueEntry
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string? Glyph { get; set; }
    public string DestinationPath { get; set; } = "";
    public string? DirectUrl { get; set; }
    public DownloadItemState State { get; set; }
    public string? ResolvedUrl { get; set; }
    public string? ResolvedFileName { get; set; }
    public long ResolvedSize { get; set; }
    public long BytesReceived { get; set; }
    public long TotalBytes { get; set; }
    public string? PostProcessorKey { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public static class PostProcessorRegistry
{
    private static readonly Dictionary<string, IDownloadPostProcessor> _processors = [];

    public static void Register(IDownloadPostProcessor processor)
    {
        _processors[processor.DisplayName] = processor;
    }

    public static IDownloadPostProcessor? Find(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        return _processors.TryGetValue(key, out var p) ? p : null;
    }

    public static string? GetKey(IDownloadPostProcessor? processor)
    {
        if (processor is null) return null;
        return processor.DisplayName;
    }

    public static void RegisterDefaults()
    {
        Register(new ArchiveExtractProcessor());
        Register(new InstallerLaunchProcessor());
        Register(new MoveToDestinationProcessor());
        Register(new ToolsBundleExtractProcessor());
    }
}

public interface IDownloadPostProcessor
{
    string DisplayName { get; }
    Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct);
}

public sealed class ArchiveExtractProcessor : IDownloadPostProcessor
{
    public string DisplayName => "解压文件";
    public async Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        statusProgress?.Report("正在解压...");
        await Task.Run(() =>
        {
            if (File.Exists(downloadedFilePath))
            {
                var skipped = ZipExtractHelper.ExtractTolerant(
                    downloadedFilePath, destinationPath, statusProgress);
                if (skipped.Count > 0)
                {
                    statusProgress?.Report($"已跳过 {skipped.Count} 个无法解压的文件（可能被占用或只读）");
                }
                try { File.Delete(downloadedFilePath); } catch { }
            }
        }, ct);
    }
}

public sealed class InstallerLaunchProcessor : IDownloadPostProcessor
{
    public string DisplayName => "运行安装程序";
    public Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        statusProgress?.Report("正在启动安装程序...");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(downloadedFilePath)
            {
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            // 下载的文件可能被杀软移除/隔离或损坏，抛给队列以 Failed 状态呈现，避免崩溃
            throw new IOException($"无法启动安装程序，文件已不可用（可能被安全软件移除或磁盘错误）：{ex.Message}", ex);
        }
        return Task.CompletedTask;
    }
}

public sealed class MoveToDestinationProcessor : IDownloadPostProcessor
{
    public string DisplayName => "移动文件";
    public async Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        statusProgress?.Report("正在移动文件...");
        await Task.Run(() =>
        {
            Directory.CreateDirectory(destinationPath);
            var destFile = Path.Combine(destinationPath, Path.GetFileName(downloadedFilePath));
            if (File.Exists(destFile)) File.Delete(destFile);
            File.Move(downloadedFilePath, destFile);
        }, ct);
    }
}

public sealed class DelegatePostProcessor : IDownloadPostProcessor
{
    private readonly Func<string, string, IProgress<string>?, CancellationToken, Task> _action;
    public string DisplayName { get; }

    public DelegatePostProcessor(string displayName,
        Func<string, string, IProgress<string>?, CancellationToken, Task> action)
    {
        DisplayName = displayName;
        _action = action;
    }

    public Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
        => _action(downloadedFilePath, destinationPath, statusProgress, ct);
}

public sealed class ChainedPostProcessor : IDownloadPostProcessor
{
    private readonly IDownloadPostProcessor[] _processors;
    public string DisplayName { get; }

    public ChainedPostProcessor(string displayName, params IDownloadPostProcessor[] processors)
    {
        DisplayName = displayName;
        _processors = processors;
    }

    public async Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        var currentFile = downloadedFilePath;
        for (var i = 0; i < _processors.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            statusProgress?.Report($"{_processors[i].DisplayName} ({i + 1}/{_processors.Length})...");
            await _processors[i].ExecuteAsync(currentFile, destinationPath, statusProgress, ct);
            if (!File.Exists(currentFile) && i < _processors.Length - 1)
                currentFile = Directory.GetFiles(destinationPath).FirstOrDefault() ?? currentFile;
        }
    }
}

public sealed class UpdateInstallProcessor : IDownloadPostProcessor
{
    private readonly bool _isPortableMode;

    public string DisplayName => "准备安装更新";

    public UpdateInstallProcessor(bool isPortableMode)
    {
        _isPortableMode = isPortableMode;
    }

    public Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        var isExe = downloadedFilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        var isZip = downloadedFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        if (isExe)
        {
            statusProgress?.Report("正在启动安装程序...");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = downloadedFilePath,
                    UseShellExecute = true
                });
            }
            catch (Win32Exception ex)
            {
                // 下载的文件可能被杀软移除/隔离或损坏，抛给队列以 Failed 状态呈现，避免崩溃
                throw new IOException($"无法启动更新安装程序，文件已不可用（可能被安全软件移除或磁盘错误）：{ex.Message}", ex);
            }
            // 安装包已经起来了，本进程必须真的退出（否则安装器替换文件时程序还在运行）。
            // 走 App.RequestExit 而不是 Application.Exit：「关闭时最小化到系统托盘」会把
            // 直接关窗口解读成隐藏，而且这里需要完整清理（ETW 会话/遥测/托盘图标）。
            App.RequestExit();
        }
        else if (isZip && _isPortableMode)
        {
            statusProgress?.Report("正在打开文件夹...");
            var folder = Path.GetDirectoryName(downloadedFilePath)!;
            System.Diagnostics.Process.Start("explorer.exe", folder);
        }
        else
        {
            statusProgress?.Report("正在打开文件夹...");
            var folder = Path.GetDirectoryName(downloadedFilePath)!;
            System.Diagnostics.Process.Start("explorer.exe", folder);
        }

        return Task.CompletedTask;
    }
}

public sealed class DownloadItem : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string DisplayName { get; }
    public string? Description { get; }
    public string? Glyph { get; }
    public string DestinationPath { get; }
    public object? Tag { get; }

    private DownloadItemState _state = DownloadItemState.Queued;
    public DownloadItemState State
    {
        get => _state;
        internal set { if (_state != value) { _state = value; OnPropertyChanged(); } }
    }

    private DownloadQueueProgress? _progress;
    public DownloadQueueProgress? Progress
    {
        get => _progress;
        internal set { _progress = value; OnPropertyChanged(); }
    }

    private string? _processingStatus;
    public string? ProcessingStatus
    {
        get => _processingStatus;
        internal set { if (_processingStatus != value) { _processingStatus = value; OnPropertyChanged(); } }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        internal set { if (_errorMessage != value) { _errorMessage = value; OnPropertyChanged(); } }
    }

    private DateTimeOffset? _completedAt;
    public DateTimeOffset? CompletedAt
    {
        get => _completedAt;
        internal set { _completedAt = value; OnPropertyChanged(); }
    }

    internal string? DirectUrl { get; }
    internal Func<CancellationToken, Task<ResolvedDownloadUrl>>? UrlResolver { get; }
    internal Func<CancellationToken, Task<List<ResolvedDownloadUrl>>>? MultiFileResolver { get; }
    internal string? AlternateUrl { get; }
    internal IDownloadPostProcessor? PostProcessor { get; }
    internal CancellationTokenSource? Cts { get; set; }

    internal string? ResolvedUrl { get; set; }
    internal string? ResolvedFileName { get; set; }
    internal long ResolvedSize { get; set; }
    internal long ResumePosition { get; set; }

    /// <summary>UI 进度节流：上次派发进度的 Environment.TickCount64。</summary>
    internal long LastProgressTick;

    private DownloadItem(
        string displayName, string? directUrl,
        Func<CancellationToken, Task<ResolvedDownloadUrl>>? urlResolver,
        Func<CancellationToken, Task<List<ResolvedDownloadUrl>>>? multiFileResolver,
        string destinationPath, IDownloadPostProcessor? postProcessor,
        string? description, string? glyph, object? tag,
        string? alternateUrl = null)
    {
        DisplayName = displayName;
        DirectUrl = directUrl;
        UrlResolver = urlResolver;
        MultiFileResolver = multiFileResolver;
        AlternateUrl = alternateUrl;
        DestinationPath = destinationPath;
        PostProcessor = postProcessor;
        Description = description;
        Glyph = glyph;
        Tag = tag;
    }

    public static DownloadItem CreateDirect(
        string displayName, string downloadUrl, string destinationPath,
        IDownloadPostProcessor? postProcessor = null,
        string? description = null, string? glyph = null, object? tag = null)
        => new(displayName, downloadUrl, null, null, destinationPath, postProcessor, description, glyph, tag);

    public static DownloadItem CreateWithResolver(
        string displayName,
        Func<CancellationToken, Task<ResolvedDownloadUrl>> urlResolver,
        string destinationPath,
        IDownloadPostProcessor? postProcessor = null,
        string? description = null, string? glyph = null, object? tag = null,
        string? alternateUrl = null)
        => new(displayName, null, urlResolver, null, destinationPath, postProcessor, description, glyph, tag, alternateUrl);

    public static DownloadItem CreateMultiFile(
        string displayName,
        Func<CancellationToken, Task<List<ResolvedDownloadUrl>>> multiFileResolver,
        string destinationPath,
        IDownloadPostProcessor? postProcessor = null,
        string? description = null, string? glyph = null, object? tag = null)
        => new(displayName, null, null, multiFileResolver, destinationPath, postProcessor, description, glyph, tag);

    internal void SetState(DownloadItemState state) => State = state;
    internal void SetProgress(DownloadQueueProgress? progress) => Progress = progress;
    internal void SetProcessingStatus(string? status) => ProcessingStatus = status;
    internal void SetErrorMessage(string? message) => ErrorMessage = message;
    internal void SetCompleted()
    {
        CompletedAt = DateTimeOffset.Now;
        State = DownloadItemState.Completed;
    }

    internal void Reset()
    {
        State = DownloadItemState.Queued;
        Progress = null;
        ProcessingStatus = null;
        ErrorMessage = null;
        CompletedAt = null;
        Cts = new CancellationTokenSource();
        ResolvedUrl = null;
        ResolvedFileName = null;
        ResolvedSize = 0;
        ResumePosition = 0;
        LastProgressTick = 0;
    }

    internal void PrepareResume()
    {
        Cts = new CancellationTokenSource();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal static class ZipExtractHelper
{
    private const int EntryRetryCount = 3;
    private static readonly TimeSpan EntryRetryDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// 容错解压：逐条目处理，重复条目去重、文件/目录同名冲突时先移除冲突文件、
    /// 写入前清除只读属性、单条目瞬时失败自动重试，个别条目失败仅记录并跳过，
    /// 不影响其余文件。
    /// </summary>
    public static List<string> ExtractTolerant(string zipPath, string destinationDir,
        IProgress<string>? statusProgress = null)
    {
        var skipped = new List<string>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Directory.CreateDirectory(destinationDir);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var isDir = entry.FullName.EndsWith('/') || entry.Name.Length == 0;
            var fullName = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var targetPath = Path.Combine(destinationDir, fullName);

            if (!IsSafeTarget(destinationDir, targetPath))
            {
                skipped.Add(entry.FullName);
                continue;
            }

            var dedupeKey = fullName.TrimEnd(Path.DirectorySeparatorChar);
            if (!seenPaths.Add(dedupeKey))
                continue;

            try
            {
                if (isDir)
                {
                    TryCreateDirectory(targetPath);
                    continue;
                }

                var parent = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(parent))
                    TryCreateDirectory(parent);

                if (!TryExtractEntry(entry, targetPath))
                    skipped.Add(entry.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(entry.FullName);
            }
        }

        TryClearReadOnlyAttributes(destinationDir);
        return skipped;
    }

    private static bool IsSafeTarget(string root, string target)
    {
        try
        {
            var rootFull = Path.GetFullPath(root);
            var targetFull = Path.GetFullPath(target);
            return targetFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void TryCreateDirectory(string dir)
    {
        if (Directory.Exists(dir)) return;

        // 目标路径被同名文件占用：先移除该文件再建目录
        if (File.Exists(dir))
        {
            TryDeleteFile(dir);
        }

        Directory.CreateDirectory(dir);
    }

    private static bool TryExtractEntry(ZipArchiveEntry entry, string targetPath)
    {
        for (var attempt = 1; attempt <= EntryRetryCount; attempt++)
        {
            try
            {
                if (File.Exists(targetPath))
                    TryClearReadOnlyAttribute(targetPath);

                entry.ExtractToFile(targetPath, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= EntryRetryCount) return false;
                Thread.Sleep(EntryRetryDelay);
            }
        }
        return false;
    }

    public static void TryClearReadOnlyAttributes(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                TryClearReadOnlyAttribute(path);
                return;
            }
            if (!Directory.Exists(path)) return;

            TryClearReadOnlyAttribute(path);
            foreach (var file in Directory.EnumerateFiles(path))
                TryClearReadOnlyAttribute(file);
            foreach (var dir in Directory.EnumerateDirectories(path))
                TryClearReadOnlyAttributes(dir);
        }
        catch { }
    }

    public static void TryClearReadOnlyAttribute(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
        catch { }
    }

    private static void TryDeleteFile(string path)
    {
        for (var attempt = 1; attempt <= EntryRetryCount; attempt++)
        {
            try
            {
                TryClearReadOnlyAttribute(path);
                File.Delete(path);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= EntryRetryCount) throw;
                Thread.Sleep(EntryRetryDelay);
            }
        }
    }

    // ---------- 解压 + 原子目录替换（工具包 / 社区工具共用；文案按 profile 区分） ----------

    private const int ReplaceMaxAttempts = 3;      // 首次 + 自动重试 2 次
    private const int ReplaceRetryDelayMs = 500;
    private const int CleanupAttempts = 3;

    /// <summary>
    /// 解压到临时目录后原子替换目标目录（临时解压 → 备份旧目录 → 移动新目录 → 删备份），
    /// 失败自动重试（覆盖文件占用），最终以逐文件拷贝兜底；个别文件写入失败仅跳过并提示。
    /// 成功后删除下载的压缩包并调用 <paramref name="onCompleted"/>。
    /// </summary>
    public static void ExtractTolerantAndReplace(
        string archivePath,
        string destinationDir,
        ExtractReplaceProfile profile,
        IProgress<string>? statusProgress = null,
        Action? onCompleted = null)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= ReplaceMaxAttempts; attempt++)
        {
            if (attempt > 1)
            {
                statusProgress?.Report($"解压遇到文件占用，正在自动重试（第 {attempt}/{ReplaceMaxAttempts} 次）...");
                Thread.Sleep(ReplaceRetryDelayMs * attempt);
            }

            var extractDir = Path.Combine(Path.GetTempPath(), $"TubaWinUi3_Extract_{Guid.NewGuid():N}");
            try
            {
                ExtractOnce(archivePath, destinationDir, extractDir, profile, statusProgress,
                    allowCopyFallback: attempt == ReplaceMaxAttempts, onCompleted);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                TryDeleteDirectory(extractDir);
                if (attempt >= ReplaceMaxAttempts)
                    throw new IOException(DescribeReplaceFailure(ex, profile), ex);
            }
        }

        if (lastError is not null) throw lastError;
    }

    /// <summary>
    /// 失败文案：目标文件被占用/只读是最常见的失败原因（工具正在运行会锁住自身文件），
    /// 单独给出可操作提示；其余情况保留原始错误内容。
    /// </summary>
    private static string DescribeReplaceFailure(Exception ex, ExtractReplaceProfile profile)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is not UnauthorizedAccessException) continue;

            var path = ExtractQuotedPath(e.Message);
            return string.IsNullOrEmpty(path)
                ? profile.BusyHint
                : string.Format(profile.BusyHintFormat, path);
        }

        var message = ex.InnerException?.Message ?? ex.Message;
        return string.Format(profile.GenericFailureFormat, ReplaceMaxAttempts - 1, message);
    }

    private static string? ExtractQuotedPath(string message)
    {
        var start = message.IndexOf('\'');
        if (start < 0) return null;
        var end = message.IndexOf('\'', start + 1);
        return end > start ? message[(start + 1)..end] : null;
    }

    private static void ExtractOnce(string archivePath, string destinationDir, string extractDir,
        ExtractReplaceProfile profile, IProgress<string>? statusProgress, bool allowCopyFallback,
        Action? onCompleted)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("下载的文件不存在", archivePath);

        try
        {
            statusProgress?.Report("正在解压文件...");
            var skipped = ExtractTolerant(archivePath, extractDir, statusProgress);
            if (skipped.Count > 0)
            {
                statusProgress?.Report($"已跳过 {skipped.Count} 个无法解压的文件（可能被占用或只读）");
            }
        }
        catch
        {
            TryDeleteDirectory(extractDir);
            throw;
        }

        var backupDir = destinationDir + "_bak";

        try
        {
            if (Directory.Exists(destinationDir))
            {
                TryDeleteDirectory(backupDir);
                Directory.Move(destinationDir, backupDir);
            }

            var destParent = Path.GetDirectoryName(destinationDir);
            if (!string.IsNullOrEmpty(destParent))
                Directory.CreateDirectory(destParent);

            try
            {
                Directory.Move(extractDir, destinationDir);
            }
            catch
            {
                TryRestoreDirectory(backupDir, destinationDir);
                throw;
            }

            TryDeleteDirectory(backupDir);
            try { File.Delete(archivePath); } catch { }
            onCompleted?.Invoke();
        }
        catch
        {
            if (!allowCopyFallback) throw;

            // 兜底：目录原子替换行不通（文件被占用）时，逐文件复制覆盖
            statusProgress?.Report("正在使用文件拷贝模式完成安装...");
            TryRestoreDirectory(backupDir, destinationDir);
            try
            {
                var skippedCount = CopyDirectoryContents(extractDir, destinationDir, statusProgress);
                if (skippedCount >= Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories).Length)
                    throw new IOException("目标目录不可写，文件拷贝全部失败");

                TryDeleteDirectory(extractDir);
                try { File.Delete(archivePath); } catch { }
                onCompleted?.Invoke();
            }
            catch (Exception fallbackEx)
            {
                throw new IOException(string.Format(profile.CopyFailureFormat, fallbackEx.Message), fallbackEx);
            }
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        for (var i = 0; i < CleanupAttempts && Directory.Exists(dir); i++)
        {
            try
            {
                // 只读文件会导致 Directory.Delete 抛异常，先清除属性
                TryClearReadOnlyAttributes(dir);
                Directory.Delete(dir, true);
                return;
            }
            catch
            {
                if (i < CleanupAttempts - 1) Thread.Sleep(300);
            }
        }
    }

    private static void TryRestoreDirectory(string backupDir, string destinationDir)
    {
        if (Directory.Exists(destinationDir) && Directory.Exists(backupDir))
        {
            try
            {
                TryClearReadOnlyAttributes(destinationDir);
                Directory.Delete(destinationDir, true);
            }
            catch { }
        }
        if (Directory.Exists(backupDir) && !Directory.Exists(destinationDir))
        {
            try { Directory.Move(backupDir, destinationDir); } catch { }
        }
    }

    private static int CopyDirectoryContents(string sourceDir, string destinationDir,
        IProgress<string>? statusProgress = null)
    {
        Directory.CreateDirectory(destinationDir);
        var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
        var skipped = 0;
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var target = Path.Combine(destinationDir, relative);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            if (!TryCopyWithRetry(file, target)) skipped++;
        }

        if (skipped > 0)
        {
            statusProgress?.Report($"已跳过 {skipped} 个无法写入的文件（可能被占用或只读）");
        }

        return skipped;
    }

    private static bool TryCopyWithRetry(string source, string target)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (File.Exists(target))
                    TryClearReadOnlyAttribute(target);

                File.Copy(source, target, true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 3) return false;
                Thread.Sleep(300);
            }
        }
        return false;
    }
}

/// <summary>解压 + 原子替换的文案集合（工具包与社区工具各按上下文区分，行为逻辑共享）。</summary>
public sealed record ExtractReplaceProfile(
    string BusyHint,
    string BusyHintFormat,        // {0} = 被占用/只读的路径
    string GenericFailureFormat,  // {0} = 重试次数，{1} = 原始错误
    string CopyFailureFormat)     // {0} = 原始错误
{
    public static readonly ExtractReplaceProfile ToolsBundle = new(
        "内核安装失败：目标文件被占用或只读，请关闭正在运行的工具（如 DirectX Repair）后重试。",
        "内核安装失败：无法写入 {0}（文件被占用或只读）。请关闭正在运行的工具（如 DirectX Repair）后重试。",
        "解压工具包失败（已自动重试 {0} 次）：{1}",
        "解压工具包失败：{0}");

    public static readonly ExtractReplaceProfile CommunityTool = new(
        "安装失败：目标文件被占用或只读，请关闭正在运行的工具后重试。",
        "安装失败：无法写入 {0}（文件被占用或只读）。请关闭正在运行的工具后重试。",
        "解压社区工具失败（已自动重试 {0} 次）：{1}",
        "解压社区工具失败：{0}");
}

public sealed class ToolsBundleExtractProcessor : IDownloadPostProcessor
{
    private readonly string? _version;
    private readonly string? _kind;

    public string DisplayName => "解压工具包";

    public ToolsBundleExtractProcessor(string? version = null, string? kind = null)
    {
        _version = version;
        _kind = kind;
    }

    public async Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        statusProgress?.Report("正在解压工具包...");
        await Task.Run(() =>
            ZipExtractHelper.ExtractTolerantAndReplace(
                downloadedFilePath, destinationPath, ExtractReplaceProfile.ToolsBundle,
                statusProgress, onCompleted: ApplyCompletedState), ct);
    }

    private void ApplyCompletedState()
    {
        if (!string.IsNullOrEmpty(_version))
        {
            Services.AppSettings.Set("ToolsBundleVersion", _version);
        }

        if (!string.IsNullOrEmpty(_kind))
        {
            Services.ToolsBundleService.SetInstalledKind(_kind);
        }

        Services.ToolCatalog.RefreshToolsRoot();

        // 强制刷新侧边栏 / 标签页的工具分类（MSIX 内核安装完成后立即生效）
        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.DispatcherQueue.TryEnqueue(mainWindow.RefreshToolCategories);
        }
    }
}

/// <summary>社区工具安装所需的全部信息（队列后处理器使用；来自 plugin.json + 索引里的文件 sha）。</summary>
public sealed record CommunityToolInstallRequest(
    string ToolId,
    string DisplayName,
    string Category,
    string? Description,
    string? Publisher,
    IReadOnlyList<string> Tags,
    string? LaunchTarget,
    string? Version,
    string? Author,
    string? RepoPath,
    string? FileName,
    string? Sha);

/// <summary>
/// 社区工具安装：解压/就位 → 写 tools.json（让 ToolCatalog 收录）→ 写安装记录（更新检测）→ 刷新界面。
/// 目录原子替换 + 容错解压与工具包共用（ZipExtractHelper.ExtractTolerantAndReplace），
/// 已有安装在失败时保持完好。参数化处理器不注册进 PostProcessorRegistry：
/// 解析器型队列项重启后本来就不恢复，注册只会误导恢复语义。
/// </summary>
public sealed class CommunityToolInstallProcessor : IDownloadPostProcessor
{
    private readonly CommunityToolInstallRequest _request;

    public string DisplayName => "安装社区工具";

    public CommunityToolInstallProcessor(CommunityToolInstallRequest request)
    {
        _request = request;
    }

    public async Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        statusProgress?.Report("正在安装社区工具...");
        await Task.Run(() => Install(downloadedFilePath, destinationPath, statusProgress), ct);
    }

    private void Install(string downloadedFilePath, string destinationPath, IProgress<string>? statusProgress)
    {
        var isArchive = downloadedFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        if (isArchive)
        {
            ZipExtractHelper.ExtractTolerantAndReplace(
                downloadedFilePath, destinationPath, ExtractReplaceProfile.CommunityTool, statusProgress);
        }
        else
        {
            Directory.CreateDirectory(destinationPath);
            var targetPath = Path.Combine(destinationPath, Path.GetFileName(downloadedFilePath));
            if (!string.Equals(Path.GetFullPath(downloadedFilePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                File.Move(downloadedFilePath, targetPath, true);
        }

        // tools.json 条目：卡片名称/描述/标签/启动目标（ToolCatalog 收录的唯一依据）。
        // 写入失败会抛 IOException → 队列项 Failed 并给出原因，不静默。
        Services.ToolMetadataService.UpsertToolMetadataEntry(
            _request.ToolId,
            name: _request.DisplayName,
            description: _request.Description,
            publisher: _request.Publisher,
            tags: _request.Tags,
            launchTarget: _request.LaunchTarget);
        Services.ToolMetadataService.InvalidateCache();

        Services.CommunityToolRegistry.Upsert(new Services.CommunityInstallRecord(
            _request.ToolId,
            _request.Category,
            _request.Sha,
            _request.Version,
            _request.Author,
            _request.RepoPath,
            _request.FileName,
            _request.LaunchTarget,
            DateTimeOffset.Now));

        Services.ToolCatalog.RefreshToolsRoot();

        if (App.MainWindow is MainWindow mainWindow)
        {
            mainWindow.DispatcherQueue.TryEnqueue(mainWindow.RefreshToolCategories);
        }

        statusProgress?.Report("安装完成");
    }
}
