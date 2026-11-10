using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
using Downloader;
using Microsoft.UI.Dispatching;
using Microsoft.Toolkit.Uwp.Notifications;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>
/// 下载队列：底层引擎为 Downloader 库（github.com/bezzad/Downloader，MIT）。
/// 每个文件按 ChunkCount 分块并行下载；暂停/失败/退出应用后半成品
/// （目标文件名 + .download 侧车文件，内嵌分块元数据）保留在磁盘上，
/// 再次启动（Resume/Retry 或跨会话）时由 Downloader 自动断点续传。
/// </summary>
public static class DownloadQueueService
{
    private const int MaxConcurrentDownloads = 2;
    private const int ChunkCount = 8;                 // 单文件分块数（服务器不支持 Range 时自动退化为单连接）
    private const int ParallelChunkCount = 4;         // 单文件同时活动的分块连接数
    private const int ProgressThrottleMs = 300;
    private const string PartialSuffix = ".tubadl";             // 旧版手写引擎的半成品后缀，仅做兼容清理
    private const string DownloaderPartialSuffix = ".download"; // Downloader 半成品侧车文件
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

    private static readonly SemaphoreSlim _semaphore = new(MaxConcurrentDownloads);
    private static readonly ObservableCollection<DownloadItem> _queue = [];
    private static readonly Dictionary<string, Task> _activeTasks = [];
    private static readonly Dictionary<string, int> _multiFileAutoRetries = [];
    private const int MaxMultiFileAutoRetries = 6;
    private static int _pendingCount;
    private static DispatcherQueue? _dispatcherQueue;
#pragma warning disable CS0414
    private static bool _dirty;
#pragma warning restore CS0414
    private static readonly object _saveLock = new();

    public static void Initialize(DispatcherQueue dq)
    {
        _dispatcherQueue = dq;
        PostProcessorRegistry.RegisterDefaults();
        _ = Task.Run(() => LoadQueue());
    }

    public static ObservableCollection<DownloadItem> Queue => _queue;
    public static event Action? QueueChanged;

    public static int PendingCount => _pendingCount;

    public static DownloadItem Enqueue(
        string displayName,
        string downloadUrl,
        string destinationPath,
        IDownloadPostProcessor? postProcessor = null,
        string? description = null,
        string? glyph = null,
        object? tag = null)
    {
        var item = DownloadItem.CreateDirect(displayName, downloadUrl, destinationPath,
            postProcessor, description, glyph, tag);
        AddAndStart(item);
        return item;
    }

    public static DownloadItem EnqueueWithResolver(
        string displayName,
        Func<CancellationToken, Task<ResolvedDownloadUrl>> urlResolver,
        string destinationPath,
        IDownloadPostProcessor? postProcessor = null,
        string? description = null,
        string? glyph = null,
        object? tag = null,
        string? fallbackUrl = null)
    {
        var item = DownloadItem.CreateWithResolver(displayName, urlResolver, destinationPath,
            postProcessor, description, glyph, tag, fallbackUrl);
        AddAndStart(item);
        return item;
    }

    public static DownloadItem EnqueueMultiFile(
        string displayName,
        Func<CancellationToken, Task<List<ResolvedDownloadUrl>>> multiFileResolver,
        string destinationPath,
        IDownloadPostProcessor? postProcessor = null,
        string? description = null,
        string? glyph = null,
        object? tag = null)
    {
        var item = DownloadItem.CreateMultiFile(displayName, multiFileResolver, destinationPath,
            postProcessor, description, glyph, tag);
        AddAndStart(item);
        return item;
    }

    public static void Pause(string itemId)
    {
        var item = FindItem(itemId);
        if (item is null) return;
        if (item.State is not (DownloadItemState.Downloading or DownloadItemState.Queued)) return;

        item.Cts?.Cancel();
    }

    public static void Resume(string itemId)
    {
        var item = FindItem(itemId);
        if (item is null) return;
        if (item.State != DownloadItemState.Paused) return;

        item.PrepareResume();
        item.SetState(DownloadItemState.Queued);
        StartItemAsync(item);
    }

    public static void Cancel(string itemId)
    {
        var item = FindItem(itemId);
        if (item is null) return;

        item.Cts?.Cancel();
        if (item.State is DownloadItemState.Queued or DownloadItemState.Resolving)
        {
            _multiFileAutoRetries.Remove(itemId);
            DispatchState(item, DownloadItemState.Cancelled);
            DecrementPending();
            CleanupPartialFile(item);
            MarkDirty();
        }
        else if (item.State is DownloadItemState.Paused)
        {
            _multiFileAutoRetries.Remove(itemId);
            DispatchState(item, DownloadItemState.Cancelled);
            CleanupPartialFile(item);
            MarkDirty();
        }
    }

    public static void Retry(string itemId)
    {
        var item = FindItem(itemId);
        if (item is null) return;
        if (item.State is not (DownloadItemState.Failed or DownloadItemState.Cancelled)) return;

        _multiFileAutoRetries.Remove(itemId);
        item.Reset();
        IncrementPending();
        StartItemAsync(item);
    }

    public static void Remove(string itemId)
    {
        var item = FindItem(itemId);
        if (item is null) return;
        if (item.State is DownloadItemState.Downloading or DownloadItemState.Processing or DownloadItemState.Resolving)
        {
            item.Cts?.Cancel();
            return;
        }

        var wasPending = item.State is DownloadItemState.Queued or DownloadItemState.Resolving
            or DownloadItemState.Downloading or DownloadItemState.Processing or DownloadItemState.Paused;
        _queue.Remove(item);
        _multiFileAutoRetries.Remove(itemId);
        if (wasPending) DecrementPending();
        CleanupPartialFile(item);
        MarkDirty();
        QueueChanged?.Invoke();
    }

    public static void DeleteFile(string itemId)
    {
        var item = FindItem(itemId);
        if (item is null) return;
        if (item.State is not DownloadItemState.Completed) return;

        try
        {
            var fileName = item.ResolvedFileName ?? SanitizeFileName(item.DisplayName);
            if (!string.IsNullOrEmpty(fileName))
            {
                var filePath = Path.Combine(item.DestinationPath, fileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
        }
        catch { }

        _queue.Remove(item);
        MarkDirty();
        QueueChanged?.Invoke();
    }

    public static void ClearCompleted()
    {
        var toRemove = _queue.Where(i =>
            i.State is DownloadItemState.Completed or DownloadItemState.Failed or DownloadItemState.Cancelled)
            .ToList();
        foreach (var item in toRemove)
            _queue.Remove(item);
        MarkDirty();
        QueueChanged?.Invoke();
    }

    public static void SaveQueue()
    {
        lock (_saveLock)
        {
            try
            {
                var entries = _queue.Select(ToEntry).ToList();
                var json = JsonSerializer.Serialize(entries, _jsonOpts);
                var path = ConfigManager.GetDownloadQueuePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, json);
                _dirty = false;
            }
            catch { }
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{(double)bytes / (1L << 30):F2} GB";
        if (bytes >= 1L << 20) return $"{(double)bytes / (1L << 20):F1} MB";
        if (bytes >= 1L << 10) return $"{(double)bytes / (1L << 10):F1} KB";
        return $"{bytes} B";
    }

    public static string FormatSpeed(double mbps)
    {
        if (mbps >= 1000) return $"{mbps / 1000:F2} Gbps";
        if (mbps >= 1) return $"{mbps:F2} Mbps";
        return $"{mbps * 1000:F0} Kbps";
    }

    public static string FormatTime(TimeSpan? time)
    {
        if (time is null || time.Value.TotalSeconds <= 0) return "--";
        var t = time.Value;
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }

    private static void LoadQueue()
    {
        try
        {
            var path = ConfigManager.GetDownloadQueuePath();
            if (!File.Exists(path)) return;

            var json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<DownloadQueueEntry>>(json);
            if (entries is null) return;

            var items = new List<DownloadItem>();

            foreach (var entry in entries)
            {
                DownloadItem? item = null;
                var postProcessor = PostProcessorRegistry.Find(entry.PostProcessorKey);

                if (!string.IsNullOrEmpty(entry.DirectUrl))
                {
                    item = DownloadItem.CreateDirect(
                        entry.DisplayName, entry.DirectUrl, entry.DestinationPath,
                        postProcessor, entry.Description, entry.Glyph);
                }

                if (item is null) continue;

                item.Id = entry.Id;
                item.ResolvedUrl = entry.ResolvedUrl;
                item.ResolvedFileName = entry.ResolvedFileName;
                item.ResolvedSize = entry.ResolvedSize;

                if (entry.State == DownloadItemState.Paused)
                {
                    item.SetState(DownloadItemState.Paused);
                    item.ResumePosition = entry.BytesReceived;
                    if (entry.TotalBytes > 0)
                        item.SetProgress(new DownloadQueueProgress(entry.BytesReceived, entry.TotalBytes,
                            entry.TotalBytes > 0 ? (double)entry.BytesReceived / entry.TotalBytes * 100 : 0, 0, null));
                    IncrementPending();
                    CleanupLegacyPartialFile(item);
                }
                else if (entry.State == DownloadItemState.Completed)
                {
                    if (entry.CompletedAt.HasValue)
                        item.CompletedAt = entry.CompletedAt;
                    item.SetState(DownloadItemState.Completed);
                }
                else if (entry.State == DownloadItemState.Downloading
                    || entry.State == DownloadItemState.Queued
                    || entry.State == DownloadItemState.Resolving)
                {
                    item.SetState(DownloadItemState.Paused);
                    item.ResumePosition = entry.BytesReceived;
                    if (entry.TotalBytes > 0)
                        item.SetProgress(new DownloadQueueProgress(entry.BytesReceived, entry.TotalBytes,
                            entry.TotalBytes > 0 ? (double)entry.BytesReceived / entry.TotalBytes * 100 : 0, 0, null));
                    IncrementPending();
                    CleanupLegacyPartialFile(item);
                }
                else
                {
                    item.SetState(entry.State);
                    if (!string.IsNullOrEmpty(entry.ErrorMessage))
                        item.SetErrorMessage(entry.ErrorMessage);
                }

                items.Add(item);
            }

            if (_dispatcherQueue is not null)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    foreach (var item in items)
                        _queue.Add(item);
                    QueueChanged?.Invoke();
                });
            }
            else
            {
                foreach (var item in items)
                    _queue.Add(item);
                QueueChanged?.Invoke();
            }
        }
        catch { }
    }

    /// <summary>旧版手写引擎的 .tubadl 半成品无法被 Downloader 续传，启动恢复时直接清理。</summary>
    private static void CleanupLegacyPartialFile(DownloadItem item)
    {
        try
        {
            var fileName = item.ResolvedFileName ?? SanitizeFileName(item.DisplayName);
            if (string.IsNullOrEmpty(fileName)) return;
            var legacyPartial = Path.Combine(item.DestinationPath, fileName + PartialSuffix);
            if (File.Exists(legacyPartial))
                File.Delete(legacyPartial);
        }
        catch { }
    }

    private static void MarkDirty()
    {
        _dirty = true;
        _dispatcherQueue?.TryEnqueue(SaveQueue);
    }

    private static DownloadQueueEntry ToEntry(DownloadItem item)
    {
        return new DownloadQueueEntry
        {
            Id = item.Id,
            DisplayName = item.DisplayName,
            Description = item.Description,
            Glyph = item.Glyph,
            DestinationPath = item.DestinationPath,
            DirectUrl = item.DirectUrl,
            State = item.State,
            ResolvedUrl = item.ResolvedUrl,
            ResolvedFileName = item.ResolvedFileName,
            ResolvedSize = item.ResolvedSize,
            BytesReceived = item.Progress?.BytesReceived ?? item.ResumePosition,
            TotalBytes = item.Progress?.TotalBytes ?? 0,
            PostProcessorKey = PostProcessorRegistry.GetKey(item.PostProcessor),
            ErrorMessage = item.ErrorMessage,
            CompletedAt = item.CompletedAt
        };
    }

    private static void AddAndStart(DownloadItem item)
    {
        item.Cts = new CancellationTokenSource();
        _queue.Insert(0, item);
        IncrementPending();
        MarkDirty();
        StartItemAsync(item);

        ShowToast("已加入下载队列", $"\"{item.DisplayName}\" 已开始下载");
    }

    private static async void StartItemAsync(DownloadItem item)
    {
        try
        {
            await _semaphore.WaitAsync(item.Cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            OnDownloadPaused(item);
            return;
        }

        if (item.State is DownloadItemState.Cancelled)
        {
            _semaphore.Release();
            return;
        }

        var task = ProcessItemAsync(item);
        lock (_activeTasks)
            _activeTasks[item.Id] = task;
    }

    private static void OnDownloadPaused(DownloadItem item)
    {
        if (item.State is not (DownloadItemState.Downloading or DownloadItemState.Queued or DownloadItemState.Resolving))
            return;

        var pos = item.ResumePosition;
        if (item.Progress is not null && item.Progress.BytesReceived > pos)
            pos = item.Progress.BytesReceived;
        item.ResumePosition = pos;
        DispatchState(item, DownloadItemState.Paused);
        DecrementPending();
        MarkDirty();
    }

    internal static void OnIntegrityInstallationCancelled(DownloadItem item)
    {
        // Processing-stage cancellation is not a paused download. Preserve the completed file,
        // finish the queue item, and require a fresh confirmation on any retry.
        DispatchState(item, DownloadItemState.Cancelled);
        DecrementPending();
        MarkDirty();
    }

    private static async Task ProcessItemAsync(DownloadItem item)
    {
        var ct = item.Cts?.Token ?? CancellationToken.None;
        var newFilesThisPass = 0;
        try
        {
            if (item.MultiFileResolver is not null)
            {
                newFilesThisPass = await ProcessMultiFileAsync(item, ct);
                _multiFileAutoRetries.Remove(item.Id);
            }
            else
            {
                if (item.ResolvedUrl is null)
                {
                    DispatchState(item, DownloadItemState.Resolving);
                    var resolved = await ResolveUrlAsync(item, ct);
                    item.ResolvedUrl = resolved.Url;
                    item.ResolvedFileName = resolved.FileName;
                    item.ResolvedSize = resolved.Size;
                    MarkDirty();
                }

                ct.ThrowIfCancellationRequested();

                if (item.State != DownloadItemState.Downloading)
                    DispatchState(item, DownloadItemState.Downloading);
                var downloadedFile = await DownloadFileWithFallbackAsync(item, ct);

                ct.ThrowIfCancellationRequested();

                await RunPostProcessorAsync(item, downloadedFile, ct);
            }

            DispatchCompleted(item);
        }
        catch (DownloadIntegrityCancelledException)
        {
            OnIntegrityInstallationCancelled(item);
        }
        catch (OperationCanceledException)
        {
            OnDownloadPaused(item);
        }
        catch (Exception ex)
        {
            // 多文件任务（UUP 文件集等）：微软 CDN 直链有效期只有约 15 分钟，长任务中
            // 后续文件的链接可能已过期。与官方 aria2 脚本的做法一致：重新解析文件列表
            // 换新链接再跑一遍，已按大小校验完成的文件会自动跳过。本轮有进展时重新计数，
            // 避免同一处反复失败造成死循环。
            if (item.MultiFileResolver is not null)
            {
                if (newFilesThisPass > 0)
                    _multiFileAutoRetries.Remove(item.Id);

                if (CanAutoRetryMultiFile(item))
                {
                    var reason = DescribeError(ex);
                    if (reason.Length > 120) reason = reason[..120] + "...";
                    DispatchProcessingStatus(item, $"下载中断（{reason}），正在刷新下载列表并自动重试...");
                    await Task.Delay(3000).ConfigureAwait(false);
                    StartItemAsync(item);
                    return;
                }

                _multiFileAutoRetries.Remove(item.Id);
            }

            var errorMsg = DescribeError(ex);
            DispatchError(item, errorMsg);
            DispatchState(item, DownloadItemState.Failed);
            DecrementPending();
            MarkDirty();

            ShowToast("下载失败", $"\"{item.DisplayName}\" 下载失败：{errorMsg}");
        }
        finally
        {
            _semaphore.Release();
            lock (_activeTasks)
                _activeTasks.Remove(item.Id);
            QueueChanged?.Invoke();
        }
    }

    private const uint HResultEncryptionFailed = 0x80071770; // HRESULT_FROM_WIN32(ERROR_ENCRYPTION_FAILED)

    /// <summary>
    /// 队列失败文案。异常外层已经是处理器整理过的可读文案（「内核安装失败：文件被占用…」
    /// 「主源下载失败：…」等），不能再取 InnerException 把提示换回系统原文。
    /// 0x1770（ERROR_ENCRYPTION_FAILED）另给排查方向：原始文案「无法加密指定的文件」
    /// 只说明目录带「加密」属性但所在卷不支持 EFS（内存盘 / exFAT / Dev Drive 常见），
    /// 对用户没有指导意义。
    /// </summary>
    internal static string DescribeError(Exception ex)
    {
        // 只看最外层：解压链路已经把 0x1770 包装成带上下文的提示（外层 HResult 不再是 0x1770），
        // 只有文件 API 直接抛上来的原始错误才需要替换文案。
        if (unchecked((uint)ex.HResult) == HResultEncryptionFailed)
            return "无法写入文件：文件夹带「加密」属性，但系统无法完成 EFS 加密。" +
                   "若 TEMP 或应用数据目录在内存盘、exFAT/FAT32、Dev Drive 上，请改回 C 盘默认目录；" +
                   "NTFS 磁盘上多为 EFS 证书/密钥失效（重装系统、改密码或文件夹加密工具造成），" +
                   "可在文件夹属性→高级中取消「加密内容以便保护数据」后重试。";

        return ex.Message;
    }

    private static bool CanAutoRetryMultiFile(DownloadItem item)
    {
        lock (_activeTasks)
        {
            _multiFileAutoRetries.TryGetValue(item.Id, out var count);
            if (count >= MaxMultiFileAutoRetries) return false;
            _multiFileAutoRetries[item.Id] = count + 1;
            return true;
        }
    }

    /// <summary>逐个下载文件清单；返回本轮实际新下载的文件数（已存在且大小一致的文件直接跳过）。</summary>
    private static async Task<int> ProcessMultiFileAsync(DownloadItem item, CancellationToken ct)
    {
        DispatchState(item, DownloadItemState.Resolving);
        var files = await item.MultiFileResolver!(ct);

        if (files.Count == 0)
        {
            if (item.PostProcessor is not null)
            {
                DispatchState(item, DownloadItemState.Processing);
                DispatchProcessingStatus(item, item.PostProcessor.DisplayName);
                var progress = new Progress<string>(status => DispatchProcessingStatus(item, status));
                await item.PostProcessor.ExecuteAsync(item.DestinationPath, item.DestinationPath, progress, ct);
            }
            return 0;
        }

        DispatchState(item, DownloadItemState.Downloading);

        var newFiles = 0;
        long completedBytes = 0;   // 已完成文件的累计字节
        long knownTotal = 0;       // 已知文件的累计总字节

        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];

            Directory.CreateDirectory(item.DestinationPath);

            var localPath = Path.Combine(item.DestinationPath, file.FileName);
            var localDir = Path.GetDirectoryName(localPath);
            if (localDir is not null) Directory.CreateDirectory(localDir);

            // 大小一致且已存在的文件直接跳过：重试 / 恢复会话时不重复下载
            if (file.Size > 0 && File.Exists(localPath) && new FileInfo(localPath).Length == file.Size)
            {
                completedBytes += file.Size;
                knownTotal += file.Size;
                ReportAggregatedProgress(item, completedBytes, knownTotal, 0);
                continue;
            }

            DeleteLegacyPartial(localPath);

            long fileTotal = file.Size;
            await DownloadWithDownloaderAsync(item, file.Url, localPath, ct,
                onStarted: total => fileTotal = total > 0 ? total : file.Size,
                onProgress: e =>
                {
                    fileTotal = e.TotalBytesToReceive > 0 ? e.TotalBytesToReceive : fileTotal;
                    ReportAggregatedProgress(item, completedBytes + e.ReceivedBytesSize,
                        knownTotal + fileTotal, e.BytesPerSecondSpeed);
                });

            var actualSize = File.Exists(localPath) ? new FileInfo(localPath).Length : fileTotal;
            completedBytes += actualSize;
            knownTotal += actualSize;
            newFiles++;
        }

        ReportAggregatedProgress(item, completedBytes, completedBytes, 0);

        ct.ThrowIfCancellationRequested();

        if (item.PostProcessor is not null)
        {
            DispatchState(item, DownloadItemState.Processing);
            DispatchProcessingStatus(item, item.PostProcessor.DisplayName);
            var progress = new Progress<string>(status => DispatchProcessingStatus(item, status));
            await item.PostProcessor.ExecuteAsync(item.DestinationPath, item.DestinationPath, progress, ct);
        }

        return newFiles;
    }

    private static void DeleteLegacyPartial(string finalPath)
    {
        try
        {
            var legacyPartial = finalPath + PartialSuffix;
            if (File.Exists(legacyPartial)) File.Delete(legacyPartial);
        }
        catch { }
    }

    private static async Task RunPostProcessorAsync(DownloadItem item, string downloadedFile, CancellationToken ct)
    {
        if (item.PostProcessor is null) return;
        DispatchState(item, DownloadItemState.Processing);
        DispatchProcessingStatus(item, item.PostProcessor.DisplayName);
        var progress = new Progress<string>(status => DispatchProcessingStatus(item, status));
        await item.PostProcessor.ExecuteAsync(downloadedFile, item.DestinationPath, progress, ct);
    }

    /// <summary>
    /// 优先使用主下载源（GitCode），失败时先自动重下一次（续传半成品），
    /// 仍失败则切换备用源（GitHub）重试，并校验 zip 完整性后再交给解压。
    /// </summary>
    private static async Task<string> DownloadFileWithFallbackAsync(DownloadItem item, CancellationToken ct)
    {
        var alternate = !string.IsNullOrEmpty(item.AlternateUrl) &&
                        !string.Equals(item.AlternateUrl, item.ResolvedUrl, StringComparison.OrdinalIgnoreCase)
            ? item.AlternateUrl
            : null;

        // 第 1 次：主源（默认 GitCode）
        try
        {
            return await DownloadAndValidateAsync(item, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception primaryEx)
        {
            // 第 2 次：主源重试一次（网络波动常见；半成品保留，由 Downloader 断点续传）
            DispatchProcessingStatus(item, "下载中断，正在自动重试...");
            try
            {
                return await DownloadAndValidateAsync(item, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception retryEx) when (alternate is not null)
            {
                // 第 3 次：切换备用源（GitHub）
                DispatchProcessingStatus(item, "GitCode 下载失败，正在切换 GitHub 重试...");
                item.ResolvedUrl = alternate;
                item.ResumePosition = 0;
                MarkDirty();
                try
                {
                    return await DownloadAndValidateAsync(item, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception altEx)
                {
                    throw new InvalidOperationException(
                        $"主源下载失败：{primaryEx.InnerException?.Message ?? primaryEx.Message}；重试失败：{retryEx.InnerException?.Message ?? retryEx.Message}；备用源失败：{altEx.InnerException?.Message ?? altEx.Message}",
                        altEx);
                }
            }
        }
    }

    private static async Task<string> DownloadAndValidateAsync(DownloadItem item, CancellationToken ct)
    {
        var fileName = item.ResolvedFileName ?? SanitizeFileName(item.DisplayName);
        var finalPath = Path.Combine(item.DestinationPath, fileName);

        // 已存在的半成品大小（含侧车元数据）用于恢复时初始进度展示
        var partialPath = finalPath + DownloaderPartialSuffix;
        var partialBytes = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;

        await DownloadWithDownloaderAsync(item, item.ResolvedUrl!, finalPath, ct,
            onStarted: total =>
            {
                if (total > 0)
                {
                    item.ResolvedSize = total;
                    if (partialBytes > 0)
                        ReportAggregatedProgress(item, partialBytes, total, 0);
                }
            },
            onProgress: e => HandleSingleFileProgress(item, e));

        ValidateDownloadedFile(finalPath);
        return finalPath;
    }

    /// <summary>
    /// 用 Downloader 引擎把 <paramref name="url"/> 下载到 <paramref name="finalPath"/>。
    /// 成功正常返回；取消/暂停抛 <see cref="OperationCanceledException"/>；
    /// 失败抛完成事件携带的异常。半成品（finalPath.download）由 Downloader 自动管理，
    /// 存在且服务器文件大小未变时自动续传。
    /// </summary>
    private static async Task DownloadWithDownloaderAsync(DownloadItem item, string url, string finalPath,
        CancellationToken ct,
        Action<long>? onStarted = null,
        Action<DownloadProgressChangedEventArgs>? onProgress = null)
    {
        Directory.CreateDirectory(item.DestinationPath);
        var service = new DownloadService(CreateDownloadConfiguration());

        AsyncCompletedEventArgs? completion = null;
        service.DownloadFileCompleted += (_, e) => completion = e;
        if (onStarted is not null)
            service.DownloadStarted += (_, e) => onStarted(e.TotalBytesToReceive);
        if (onProgress is not null)
            service.DownloadProgressChanged += (_, e) => onProgress(e);

        try
        {
            await service.DownloadFileTaskAsync(url, finalPath, ct).ConfigureAwait(false);
        }
        finally
        {
            await service.DisposeAsync().ConfigureAwait(false);
        }

        if (completion is null)
            throw new InvalidOperationException("下载未返回完成状态");
        if (completion.Cancelled)
            throw new OperationCanceledException();
        if (completion.Error is not null)
            throw completion.Error;
    }

    /// <summary>
    /// 若是 zip，校验压缩包完整性（遍历并打开每个条目，验证本地文件头）。
    /// 损坏则删除并抛异常，触发自动重下（重下时 Downloader 会清掉损坏的完整文件）。
    /// </summary>
    private static void ValidateDownloadedFile(string filePath)
    {
        if (!filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return;
        if (!File.Exists(filePath)) return;

        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(filePath);
            foreach (var entry in archive.Entries)
            {
                using var s = entry.Open();
            }
        }
        catch (Exception ex)
        {
            try { File.Delete(filePath); } catch { }
            throw new InvalidDataException($"下载的压缩包已损坏（{ex.Message}）", ex);
        }
    }

    private static DownloadConfiguration CreateDownloadConfiguration() => new()
    {
        BufferBlockSize = 64 * 1024,
        ChunkCount = ChunkCount,
        ParallelCount = ParallelChunkCount,
        ParallelDownload = true,
        MaxTryAgainOnFailure = 3,                       // 分块级自动重试（指数退避）
        MinimumSizeOfChunking = 1024 * 1024,            // 小于 1MB 不分块
        EnableAutoResumeDownload = true,                // 半成品内嵌分块元数据，跨会话续传
        ClearPackageOnCompletionWithFailure = false,    // 失败保留半成品，重试可续传
        FileExistPolicy = FileExistPolicy.Delete,       // 目标完整文件已存在则删除重下
        DownloadFileExtension = DownloaderPartialSuffix,
        HttpClientTimeout = 2 * 60 * 60 * 1000,         // 默认 100s 会误杀慢速大文件流
        MaximumMemoryBufferBytes = 64 * 1024 * 1024,
        // 下载链路同样走 IPv4 优先连接：CDN 域名若解析出不可达的 IPv6 地址，
        // 默认 handler 会顺序尝试并挂起整个超时周期（见 HttpClientFactory 说明）
        CustomHttpMessageHandlerFactory = CreateDownloadHandler,
        RequestConfiguration = new RequestConfiguration
        {
            UserAgent = "TubaWinUi3-DownloadQueue",
            Proxy = ProxyService.GetWebProxy(),
        }
    };

    private static SocketsHttpHandler CreateDownloadHandler()
    {
        var handler = HttpClientFactory.CreateIpv4PreferredHandler();
        // 注入自定义 handler 后 RequestConfiguration.Proxy 不再由库自动施加，手动保留用户配置的代理
        var proxy = ProxyService.GetWebProxy();
        if (proxy is not null)
        {
            handler.Proxy = proxy;
            handler.UseProxy = true;
        }
        return handler;
    }

    private static void HandleSingleFileProgress(DownloadItem item, DownloadProgressChangedEventArgs e)
        => ReportAggregatedProgress(item, e.ReceivedBytesSize, e.TotalBytesToReceive, e.BytesPerSecondSpeed);

    private static void ReportAggregatedProgress(DownloadItem item, long received, long total, double bytesPerSecond)
    {
        var percentage = total > 0 ? Math.Min(received * 100.0 / total, 100) : 0;
        var speedMbps = bytesPerSecond * 8 / 1_000_000;
        var remaining = total > 0 && bytesPerSecond > 1
            ? TimeSpan.FromSeconds(Math.Max(0, total - received) / bytesPerSecond)
            : (TimeSpan?)null;

        item.ResumePosition = received;

        // Downloader 每个分块的每个数据块都会触发进度事件，直接刷 UI 会卡顿；
        // 按固定间隔节流，收尾阶段（>=99.9%）立即推送避免停在 99%。
        var now = Environment.TickCount64;
        if (percentage > 0 && percentage < 99.9 && now - item.LastProgressTick < ProgressThrottleMs)
            return;
        item.LastProgressTick = now;

        DispatchProgress(item, new DownloadQueueProgress(received, total, percentage, speedMbps, remaining));
    }

    private static void CleanupPartialFile(DownloadItem item)
    {
        if (item.State is not (DownloadItemState.Cancelled or DownloadItemState.Failed))
            return;

        var fileName = item.ResolvedFileName ?? SanitizeFileName(item.DisplayName);
        if (string.IsNullOrEmpty(fileName)) return;

        var finalPath = Path.Combine(item.DestinationPath, fileName);
        foreach (var partial in new[] { finalPath + PartialSuffix, finalPath + DownloaderPartialSuffix })
        {
            try { if (File.Exists(partial)) File.Delete(partial); } catch { }
        }
    }

    private static DownloadItem? FindItem(string itemId)
        => _queue.FirstOrDefault(i => i.Id == itemId);

    private static void ShowToast(string title, string message)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();
        }
        catch
        {
            // Toast notifications may throw ArgumentException ("Value does not
            // fall within the expected range") in unpackaged mode when no AUMID
            // is registered. Swallow so the download flow is not broken.
        }
    }

    private static void DispatchState(DownloadItem item, DownloadItemState state)
    {
        if (_dispatcherQueue is not null)
            _dispatcherQueue.TryEnqueue(() => item.SetState(state));
        else
            item.SetState(state);
    }

    private static void DispatchProgress(DownloadItem item, DownloadQueueProgress progress)
    {
        if (_dispatcherQueue is not null)
            _dispatcherQueue.TryEnqueue(() => item.SetProgress(progress));
        else
            item.SetProgress(progress);
    }

    private static void DispatchProcessingStatus(DownloadItem item, string status)
    {
        if (_dispatcherQueue is not null)
            _dispatcherQueue.TryEnqueue(() => item.SetProcessingStatus(status));
        else
            item.SetProcessingStatus(status);
    }

    private static void DispatchCompleted(DownloadItem item)
    {
        if (_dispatcherQueue is not null)
            _dispatcherQueue.TryEnqueue(() =>
            {
                item.SetCompleted();
                DecrementPending();
                MarkDirty();

                ShowToast("下载完成", $"\"{item.DisplayName}\" 已下载完成");
            });
        else
        {
            item.SetCompleted();
            DecrementPending();
            MarkDirty();

            ShowToast("下载完成", $"\"{item.DisplayName}\" 已下载完成");
        }
    }

    private static void DispatchError(DownloadItem item, string message)
    {
        if (_dispatcherQueue is not null)
            _dispatcherQueue.TryEnqueue(() => item.SetErrorMessage(message));
        else
            item.SetErrorMessage(message);
    }

    private static async Task<ResolvedDownloadUrl> ResolveUrlAsync(DownloadItem item, CancellationToken ct)
    {
        if (item.DirectUrl is not null)
        {
            var fileName = Path.GetFileName(new Uri(item.DirectUrl).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('?'))
                fileName = SanitizeFileName(item.DisplayName);
            return new ResolvedDownloadUrl(item.DirectUrl, fileName);
        }

        if (item.UrlResolver is not null)
            return await item.UrlResolver(ct);

        throw new InvalidOperationException("No download URL or resolver provided");
    }

    private static void IncrementPending()
    {
        Interlocked.Increment(ref _pendingCount);
        QueueChanged?.Invoke();
    }

    private static void DecrementPending()
    {
        Interlocked.Decrement(ref _pendingCount);
        QueueChanged?.Invoke();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
            if (!invalid.Contains(c)) result.Append(c);
        return result.Length == 0 ? "download" : result.ToString();
    }
}
