using System.Collections;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using Tubatools.AiJunkCleanup.Models;
using Tubatools.AiJunkCleanup.Runtime;

namespace Tubatools.AiJunkCleanup.Services;

/// <summary>
/// 反射调用宿主公开的 SpaceScanService（NTFS MFT / 目录元数据，只读）。
/// 只在插件进程内运行，不把宿主编译进插件，也不修改宿主代码。
/// </summary>
internal sealed class HostSpaceScan : IDisposable
{
    /// <summary>
    /// 把宿主节点的公开属性编译成委托。整棵索引可能有几百万个节点，
    /// 逐节点 PropertyInfo.GetValue 会把 99% 阶段拖到几十秒。
    /// </summary>
    private sealed class NodeView
    {
        private readonly PropertyInfo? _pathInfo;
        private readonly PropertyInfo? _bytesInfo;
        private readonly PropertyInfo? _filesInfo;
        private readonly PropertyInfo? _directoryInfo;
        private readonly PropertyInfo? _modifiedInfo;
        private readonly PropertyInfo? _childrenInfo;
        private readonly Func<object, string>? _path;
        private readonly Func<object, long>? _bytes;
        private readonly Func<object, long>? _files;
        private readonly Func<object, bool>? _directory;
        private readonly Func<object, DateTime>? _modified;
        private readonly Func<object, IEnumerable?>? _children;

        internal NodeView(Type type)
        {
            _pathInfo = type.GetProperty("FullPath", BindingFlags.Public | BindingFlags.Instance);
            _bytesInfo = type.GetProperty("Bytes", BindingFlags.Public | BindingFlags.Instance);
            _filesInfo = type.GetProperty("FileCount", BindingFlags.Public | BindingFlags.Instance);
            _directoryInfo = type.GetProperty("IsDirectory", BindingFlags.Public | BindingFlags.Instance);
            _modifiedInfo = type.GetProperty("Modified", BindingFlags.Public | BindingFlags.Instance);
            _childrenInfo = type.GetProperty("Children", BindingFlags.Public | BindingFlags.Instance);
            _path = CreateGetter<string>(_pathInfo);
            _bytes = CreateGetter<long>(_bytesInfo);
            _files = CreateGetter<long>(_filesInfo);
            _directory = CreateGetter<bool>(_directoryInfo);
            _modified = CreateGetter<DateTime>(_modifiedInfo);
            _children = CreateGetter<IEnumerable>(_childrenInfo);
        }

        private static Func<object, T>? CreateGetter<T>(PropertyInfo? property)
        {
            if (property?.GetGetMethod() is null) return null;
            try
            {
                var instance = Expression.Parameter(typeof(object), "instance");
                Expression body = Expression.Property(Expression.Convert(instance, property.DeclaringType!), property);
                if (typeof(T) != property.PropertyType) body = Expression.Convert(body, typeof(T));
                return Expression.Lambda<Func<object, T>>(body, instance).Compile();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or MethodAccessException)
            {
                return null;
            }
        }

        internal string Path(object node) => _path?.Invoke(node) ?? (_pathInfo?.GetValue(node) as string ?? "");

        internal long Bytes(object node) => _bytes?.Invoke(node) ?? (_bytesInfo?.GetValue(node) is long value ? value : 0);

        internal long FileCount(object node) => _files?.Invoke(node) ?? (_filesInfo?.GetValue(node) is long value ? value : 0);

        internal bool IsDirectory(object node) => _directory?.Invoke(node) ?? (_directoryInfo?.GetValue(node) is bool value && value);

        internal long Modified(object node)
        {
            var modified = _modified?.Invoke(node) ??
                           (_modifiedInfo?.GetValue(node) is DateTime value ? value : DateTime.MinValue);
            return modified > DateTime.UnixEpoch
                ? new DateTimeOffset(modified.Kind == DateTimeKind.Utc ? modified : modified.ToUniversalTime()).ToUnixTimeSeconds()
                : 0;
        }

        internal IEnumerable? Children(object node) =>
            _children?.Invoke(node) ?? _childrenInfo?.GetValue(node) as IEnumerable;
    }

    private readonly object _session;
    private readonly object _sync;
    private readonly object _root;
    private readonly NodeView _node;
    private readonly PropertyInfo? _percent;
    private readonly PropertyInfo? _phase;
    private readonly PropertyInfo? _files;
    private readonly PropertyInfo? _folders;
    private readonly PropertyInfo? _elapsed;
    private readonly PropertyInfo? _complete;
    private readonly PropertyInfo? _cancelled;
    private readonly PropertyInfo? _method;
    private bool _disposed;

    internal Task ScanTask { get; }
    internal string RootPath { get; }

    private HostSpaceScan(object session, object root, object sync, Type sessionType, Task scanTask, string rootPath)
    {
        _session = session;
        _root = root;
        _sync = sync;
        _node = new NodeView(root.GetType());
        _percent = sessionType.GetProperty("ProgressPercent", BindingFlags.Public | BindingFlags.Instance);
        _phase = sessionType.GetProperty("Phase", BindingFlags.Public | BindingFlags.Instance);
        _files = sessionType.GetProperty("Files", BindingFlags.Public | BindingFlags.Instance);
        _folders = sessionType.GetProperty("Folders", BindingFlags.Public | BindingFlags.Instance);
        _elapsed = sessionType.GetProperty("Elapsed", BindingFlags.Public | BindingFlags.Instance);
        _complete = sessionType.GetProperty("Complete", BindingFlags.Public | BindingFlags.Instance);
        _cancelled = sessionType.GetProperty("Cancelled", BindingFlags.Public | BindingFlags.Instance);
        _method = sessionType.GetProperty("Method", BindingFlags.Public | BindingFlags.Instance);
        ScanTask = scanTask;
        RootPath = rootPath;
    }

    internal static HostSpaceScan? TryCreate(string path, CancellationToken ct, out string error)
    {
        error = "";
        var sessionType = HostBridge.FindHostType("TubaWinUi3.Services.SpaceCleanup.SpaceScanSession");
        var serviceType = HostBridge.FindHostType("TubaWinUi3.Services.SpaceCleanup.SpaceScanService");
        if (sessionType is null || serviceType is null)
        {
            error = "宿主空间扫描服务不可用，请确认工具箱版本。";
            return null;
        }

        var ctor = sessionType.GetConstructors(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(constructor =>
        {
            var parameters = constructor.GetParameters();
            return parameters.Length == 4 &&
                   parameters[0].ParameterType == typeof(string) &&
                   parameters[1].ParameterType == typeof(long) &&
                   parameters[2].ParameterType == typeof(bool);
        });
        if (ctor is null)
        {
            error = "宿主空间扫描会话接口已变化。";
            return null;
        }

        object session;
        try { session = ctor.Invoke(new object?[] { Path.GetFullPath(path), long.MaxValue, true, null }); }
        catch (Exception ex)
        {
            error = "无法创建扫描会话：" + (ex.InnerException?.Message ?? ex.Message);
            return null;
        }

        try
        {
            var root = Prop(session, "Root");
            var sync = Prop(session, "SyncRoot");
            var scan = serviceType.GetMethod("ScanAsync", BindingFlags.Public | BindingFlags.Static);
            if (root is null || sync is null || scan is null)
            {
                error = "宿主空间扫描接口不完整。";
                return null;
            }
            var task = scan.Invoke(null, new object?[] { session, false, ct }) as Task;
            if (task is null)
            {
                error = "宿主空间扫描没有返回任务。";
                return null;
            }
            return new HostSpaceScan(session, root, sync, sessionType, task, Path.GetFullPath(path));
        }
        catch (Exception ex)
        {
            error = "启动扫描失败：" + (ex.InnerException?.Message ?? ex.Message);
            return null;
        }
        finally
        {
            if (error.Length > 0) (session as IDisposable)?.Dispose();
        }
    }

    internal ShallowScanProgress Snapshot()
    {
        lock (_sync)
        {
            var percent = _percent?.GetValue(_session) is double value ? value : (double?)null;
            var elapsed = _elapsed?.GetValue(_session) is TimeSpan time ? time : TimeSpan.Zero;
            return new ShallowScanProgress(
                true,
                _complete?.GetValue(_session) is bool complete && complete,
                _cancelled?.GetValue(_session) is bool cancelled && cancelled,
                percent,
                _phase?.GetValue(_session) as string ?? "读取 MFT 目录索引",
                _files?.GetValue(_session) is long files ? files : 0,
                elapsed,
                null);
        }
    }

    internal ShallowScanData BuildShallow(int depth, int perLevel)
    {
        var all = new List<ShallowDirItem>();
        lock (_sync)
        {
            Walk(_root, 1);
        }
        var top = all
            .GroupBy(item => item.Level)
            .SelectMany(group => group.OrderByDescending(item => item.Bytes).Take(perLevel))
            .OrderBy(item => item.Level)
            .ThenByDescending(item => item.Bytes)
            .ToList();
        return new ShallowScanData(
            _node.Bytes(_root),
            _files?.GetValue(_session) is long files ? files : 0,
            _folders?.GetValue(_session) is long folders ? folders : 0,
            _method?.GetValue(_session) as string ?? "目录元数据",
            (_elapsed?.GetValue(_session) is TimeSpan elapsed ? elapsed : TimeSpan.Zero).TotalSeconds,
            top);

        void Walk(object node, int level)
        {
            if (level > depth) return;
            if (_node.Children(node) is not IEnumerable children) return;
            foreach (var child in children)
            {
                if (child is null) continue;
                if (!_node.IsDirectory(child)) continue;
                all.Add(new ShallowDirItem(level, _node.Path(child), _node.Bytes(child), _node.FileCount(child), _node.Modified(child)));
                Walk(child, level + 1);
            }
        }
    }

    /// <summary>
    /// 从同一棵已扫描完成的树导出深层索引：深层目录 + 大文件。不会再次访问磁盘。
    /// </summary>
    internal ScanIndexData BuildIndex(int maxDepth, long minDirectoryBytes, long minFileBytes,
        int maxDirectories, int maxFiles)
    {
        var directories = new List<ShallowDirItem>();
        var files = new List<ShallowFileItem>();
        lock (_sync)
        {
            Walk(_root, 1);
        }

        var topDirectories = directories
            .OrderByDescending(item => IsCommonJunkDirectory(item.Path))
            .ThenByDescending(item => item.Bytes)
            .Take(maxDirectories)
            .OrderBy(item => item.Level)
            .ThenByDescending(item => item.Bytes)
            .ToList();
        var topFiles = files
            .OrderByDescending(item => IsCommonJunkFile(item.Path))
            .ThenByDescending(item => item.Bytes)
            .Take(maxFiles)
            .ToList();
        return new ScanIndexData(topDirectories, topFiles);

        void Walk(object node, int level)
        {
            if (level > maxDepth) return;
            if (_node.Children(node) is not IEnumerable children) return;
            foreach (var child in children)
            {
                if (child is null) continue;
                if (!_node.IsDirectory(child))
                {
                    var fileBytes = _node.Bytes(child);
                    if (fileBytes >= minFileBytes || IsCommonJunkFile(_node.Path(child)))
                        files.Add(new ShallowFileItem(_node.Path(child), fileBytes, _node.Modified(child)));
                    continue;
                }

                var bytes = _node.Bytes(child);
                if (level <= 3 || bytes >= minDirectoryBytes || IsCommonJunkDirectory(_node.Path(child)))
                    directories.Add(new ShallowDirItem(level, _node.Path(child), bytes, _node.FileCount(child), _node.Modified(child)));
                Walk(child, level + 1);
            }
        }
    }

    /// <summary>常见垃圾目录即使小于索引门槛也收录，避免 AI 只盯大文件。</summary>
    internal static bool IsCommonJunkDirectory(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed).ToLowerInvariant();
        switch (name)
        {
            case "temp":
            case "tmp":
            case "cache":
            case "code cache":
            case "gpucache":
            case "cachestorage":
            case "crashdumps":
            case "wer":
            case "logs":
            case "log":
            case "thumbnails":
            case "iconcache":
            case "inetcache":
            case "temporary internet files":
                return true;
        }
        return path.Contains(@"\SoftwareDistribution\Download", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>常见垃圾文件也优先收录：缩略图/图标缓存，以及临时目录/日志目录里的日志、转储。</summary>
    internal static bool IsCommonJunkFile(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (name.StartsWith("thumbcache_", StringComparison.Ordinal) ||
            name.StartsWith("iconcache_", StringComparison.Ordinal)) return true;
        if (!name.EndsWith(".log", StringComparison.Ordinal) &&
            !name.EndsWith(".dmp", StringComparison.Ordinal) &&
            !name.EndsWith(".tmp", StringComparison.Ordinal)) return false;
        var parent = Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(parent) && IsCommonJunkDirectory(parent);
    }

    private static object? Prop(object? target, string name) =>
        target?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { (_session as IDisposable)?.Dispose(); } catch { }
    }
}
