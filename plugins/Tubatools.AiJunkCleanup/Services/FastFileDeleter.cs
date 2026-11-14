using System.Diagnostics;

namespace Tubatools.AiJunkCleanup.Services;

internal sealed record DeleteTarget(string Path, bool IsDirectory, long EstimatedBytes = 0);

internal sealed record DeleteProgress(
    long BytesTotal,
    long BytesDeleted,
    long ItemsDeleted,
    int TargetsDone,
    int TargetsTotal,
    string CurrentPath,
    bool Indeterminate = false);

internal sealed record DeleteTargetResult(
    string Path,
    bool Success,
    long BytesDeleted,
    long ItemsDeleted,
    int FailedFiles,
    string? Error);

internal sealed record DeleteOutcome(
    bool Canceled,
    long BytesDeleted,
    long ItemsDeleted,
    IReadOnlyList<DeleteTargetResult> Targets);

/// <summary>
/// 插件自己的直接删除引擎：不进入回收站，不调用 Explorer / IFileOperation。
/// 目录优先调用原生 Directory.Delete(recursive)，优先速度；进度按已处理项数估算，
/// 大目录删除期间显示不确定进度。只有原生删除因只读/占用失败时，才回退到逐文件兜底。
/// </summary>
internal static class FastFileDeleter
{
    private const int Parallelism = 4;

    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false
    };

    private sealed record FileEntry(string Path, long Bytes, FileAttributes Attributes);

    private sealed class Counters
    {
        internal long Bytes;
        internal long Items;
        internal int TargetsDone;
    }

    private sealed class TargetState
    {
        private readonly object _gate = new();
        internal int Failed;
        internal string? Error;

        internal void Fail(Exception ex)
        {
            Interlocked.Increment(ref Failed);
            lock (_gate) Error ??= ex.Message;
        }
    }

    internal static Task<DeleteOutcome> DeleteAsync(IReadOnlyList<DeleteTarget> targets, long totalBytesHint,
        IProgress<DeleteProgress>? progress, CancellationToken ct, LockedFileHandler? lockedFileHandler = null) =>
        Task.Run(() => Delete(targets, totalBytesHint, progress, ct, lockedFileHandler));

    private static DeleteOutcome Delete(IReadOnlyList<DeleteTarget> targets, long totalBytesHint,
        IProgress<DeleteProgress>? progress, CancellationToken ct, LockedFileHandler? lockedFileHandler)
    {
        var results = new List<DeleteTargetResult>(targets.Count);
        var counters = new Counters();
        var watch = Stopwatch.StartNew();
        long lastReport = 0;

        void Report(string current, bool indeterminate, bool force = false)
        {
            var now = watch.ElapsedMilliseconds;
            var last = Interlocked.Read(ref lastReport);
            if (!force && !indeterminate && now - last < 100) return;
            if (Interlocked.CompareExchange(ref lastReport, now, last) != last) return;
            var bytes = Interlocked.Read(ref counters.Bytes);
            progress?.Report(new DeleteProgress(
                Math.Max(totalBytesHint, bytes),
                bytes,
                Interlocked.Read(ref counters.Items),
                Volatile.Read(ref counters.TargetsDone),
                targets.Count,
                DisplayPath(current),
                indeterminate));
        }

        void ReportTarget(string current, bool indeterminate) => Report(current, indeterminate, false);

        try
        {
            foreach (var target in targets)
            {
                ct.ThrowIfCancellationRequested();
                var result = DeleteOne(target, ct, counters, ReportTarget, lockedFileHandler);
                results.Add(result);
                Interlocked.Increment(ref counters.TargetsDone);
                Report(result.Path, false, true);
            }
        }
        catch (OperationCanceledException)
        {
            return new DeleteOutcome(true, Interlocked.Read(ref counters.Bytes), Interlocked.Read(ref counters.Items), results);
        }

        Report("完成", false, true);
        return new DeleteOutcome(false, Interlocked.Read(ref counters.Bytes), Interlocked.Read(ref counters.Items), results);
    }

    private static DeleteTargetResult DeleteOne(DeleteTarget target, CancellationToken ct, Counters counters,
        Action<string, bool> report, LockedFileHandler? lockedFileHandler)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.Path));
        var longPath = ToLongPath(path);
        var beforeBytes = Interlocked.Read(ref counters.Bytes);
        var beforeItems = Interlocked.Read(ref counters.Items);
        var state = new TargetState();

        try
        {
            var attributes = File.GetAttributes(longPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return new DeleteTargetResult(path, false, 0, 0, 0, "拒绝删除链接或挂载点。");

            if ((attributes & FileAttributes.Directory) == 0)
            {
                var bytes = new FileInfo(longPath).Length;
                DeleteFile(new FileEntry(path, bytes, attributes), state, counters, report, lockedFileHandler, ct);
            }
            else
            {
                // 快速路径：原生递归删除，不逐文件回调；进度按已处理项数估算。
                report(path, true);
                try
                {
                    Directory.Delete(longPath, true);
                    Interlocked.Add(ref counters.Bytes, Math.Max(0, target.EstimatedBytes));
                    Interlocked.Increment(ref counters.Items);
                }
                catch (DirectoryNotFoundException)
                {
                    Interlocked.Add(ref counters.Bytes, Math.Max(0, target.EstimatedBytes));
                    Interlocked.Increment(ref counters.Items);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 只读文件、占用或权限问题：回退到可清只读位、可汇报错误的逐文件兜底。
                    DeleteDirectory(path, ct, state, counters, report, lockedFileHandler);
                }
                report(path, false);
            }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            state.Fail(ex);
        }

        var success = !File.Exists(longPath) && !Directory.Exists(longPath);
        if (success)
        {
            // 快速路径/兜底路径混合时，用清单里的测量值补齐“约释放”大小。
            var addedBytes = Interlocked.Read(ref counters.Bytes) - beforeBytes;
            if (addedBytes < target.EstimatedBytes)
                Interlocked.Add(ref counters.Bytes, target.EstimatedBytes - addedBytes);
            if (Interlocked.Read(ref counters.Items) - beforeItems < 1)
                Interlocked.Increment(ref counters.Items);
        }
        return new DeleteTargetResult(
            path,
            success,
            Interlocked.Read(ref counters.Bytes) - beforeBytes,
            Interlocked.Read(ref counters.Items) - beforeItems,
            state.Failed,
            success ? null : state.Error ?? "项目仍然存在，可能被占用或被系统拒绝。");
    }

    private static void DeleteDirectory(string root, CancellationToken ct, TargetState state, Counters counters,
        Action<string, bool> report, LockedFileHandler? lockedFileHandler)
    {
        var directories = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            directories.Add(directory);

            List<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(ToLongPath(directory)).EnumerateFileSystemInfos("*", Options).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                state.Fail(ex);
                continue;
            }

            var files = new List<FileEntry>();
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try
                {
                    attributes = entry.Attributes;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
                {
                    state.Fail(ex);
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        // Junction / symlink 目录：只删除链接本身，绝不递归进目标。
                        directories.Add(entry.FullName);
                    }
                    else
                    {
                        DeleteLink(entry.FullName, state, counters);
                    }
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(entry.FullName);
                    pending.Push(entry.FullName);
                    continue;
                }

                long bytes = 0;
                if (entry is FileInfo file)
                {
                    try { bytes = file.Length; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException) { }
                }
                files.Add(new FileEntry(entry.FullName, bytes, attributes));
            }

            if (files.Count == 1)
            {
                DeleteFile(files[0], state, counters, report, lockedFileHandler, ct);
            }
            else if (files.Count > 1)
            {
                Parallel.ForEach(files,
                    new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct },
                    file => DeleteFile(file, state, counters, report, lockedFileHandler, ct));
            }

            report(directory, false);
        }

        // 子目录必须先于父目录删除；路径更长的子项一定比父项深。
        directories.Sort((left, right) => right.Length.CompareTo(left.Length));
        foreach (var directory in directories)
        {
            ct.ThrowIfCancellationRequested();
            DeleteDirectoryEntry(directory, state, counters, report);
        }
    }

    private static void DeleteLink(string path, TargetState state, Counters counters)
    {
        try
        {
            File.Delete(ToLongPath(path));
            Interlocked.Increment(ref counters.Items);
        }
        catch (FileNotFoundException) { Interlocked.Increment(ref counters.Items); }
        catch (DirectoryNotFoundException) { Interlocked.Increment(ref counters.Items); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            state.Fail(ex);
        }
    }

    private static void DeleteFile(FileEntry file, TargetState state, Counters counters, Action<string, bool> report,
        LockedFileHandler? lockedFileHandler, CancellationToken ct)
    {
        try
        {
            var path = ToLongPath(file.Path);
            if ((file.Attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, file.Attributes & ~FileAttributes.ReadOnly);
            File.Delete(path);
            Interlocked.Add(ref counters.Bytes, file.Bytes);
            Interlocked.Increment(ref counters.Items);
        }
        catch (FileNotFoundException) { Interlocked.Increment(ref counters.Items); }
        catch (DirectoryNotFoundException) { Interlocked.Increment(ref counters.Items); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!TryHandleLockedFile(file, ex, state, counters, lockedFileHandler, ct))
                state.Fail(ex);
        }
        report(file.Path, false);
    }

    private static bool TryHandleLockedFile(FileEntry file, Exception error, TargetState state, Counters counters,
        LockedFileHandler? handler, CancellationToken ct)
    {
        if (handler is null) return false;
        var display = DisplayPath(file.Path);
        var processes = LockedFileResolver.Find(display);
        if (processes.Count == 0) return false;

        var decision = handler(display, processes, ct).GetAwaiter().GetResult();
        if (decision == LockResolution.Cancel) throw new OperationCanceledException(ct);
        if (decision == LockResolution.Skip)
        {
            state.Fail(error);
            return true;
        }

        CloseProcesses(processes);
        Thread.Sleep(300);
        try
        {
            var path = ToLongPath(file.Path);
            if ((file.Attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, file.Attributes & ~FileAttributes.ReadOnly);
            File.Delete(path);
            Interlocked.Add(ref counters.Bytes, file.Bytes);
            Interlocked.Increment(ref counters.Items);
        }
        catch (FileNotFoundException) { Interlocked.Increment(ref counters.Items); }
        catch (DirectoryNotFoundException) { Interlocked.Increment(ref counters.Items); }
        catch (Exception retry) when (retry is IOException or UnauthorizedAccessException)
        {
            state.Fail(retry);
        }
        return true;
    }

    private static void CloseProcesses(IReadOnlyList<LockedProcess> processes) =>
        ProcessCloser.Terminate(processes);

    private static void DeleteDirectoryEntry(string directory, TargetState state, Counters counters,
        Action<string, bool> report)
    {
        try
        {
            var path = ToLongPath(directory);
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(path, false);
            }
            else
            {
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                Directory.Delete(path, false);
            }
            Interlocked.Increment(ref counters.Items);
        }
        catch (DirectoryNotFoundException) { Interlocked.Increment(ref counters.Items); }
        catch (FileNotFoundException) { Interlocked.Increment(ref counters.Items); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            state.Fail(ex);
        }
        report(directory, false);
    }

    private static string ToLongPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        if (path.Length < 240) return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + path[2..]
            : @"\\?\" + path;
    }

    private static string DisplayPath(string path) =>
        path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)
            ? @"\\" + path[8..]
            : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
}
