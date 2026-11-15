using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class ToolIconService
{
    private static string CacheRoot => ConfigManager.GetIconCacheDir();

    private static string BundledCacheRoot => Path.Combine(ToolCatalog.AppDirectory, "IconCache");

    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(90);
    private const long MaxCacheSizeBytes = 50 * 1024 * 1024;
    private const int MaxCacheFiles = 2000;

    private static readonly Dictionary<string, string> ExtensionGlyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        [".bat"] = "\uE756",
        [".cmd"] = "\uE756",
        [".ps1"] = "\uE943",
        [".vbs"] = "\uE943",
        [".msc"] = "\uEC7A",
    };

    private static readonly LruCache<string, string> _memoryCache = new(512);
    // Shared by all pages, rather than allowing each navigation to start 4–8 extractors.
    private static readonly SemaphoreSlim ExtractionGate = new(1);
    private static int _cacheRevision;
    internal static int CacheRevision => Volatile.Read(ref _cacheRevision);

    // Resolve file-system caches off-thread, without touching shared/bound ToolItems.
    // The page applies these paths on its UI thread before realizing the cards.
    internal static Task<IReadOnlyList<(ToolItem Tool, string Path)>> GetCachedIconsAsync(
        IReadOnlyList<ToolItem> tools, CancellationToken cancellationToken = default)
    {
        var pending = tools.Where(t => t.IconPath is null && !string.IsNullOrWhiteSpace(t.Path)).ToList();
        return Task.Run<IReadOnlyList<(ToolItem Tool, string Path)>>(() =>
        {
            var result = new List<(ToolItem, string)>();
            foreach (var tool in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = GetCachedIconPath(tool.Path);
                if (path is not null) result.Add((tool, path));
            }
            return result;
        }, cancellationToken);
    }

    public static string GetCategoryGlyph(string category) => category switch
    {
        "处理器工具" => "\uE950", "显卡工具" => "\uE7F4", "内存工具" => "\uE964",
        "硬盘工具" => "\uEDA2", "显示器工具" => "\uE7F4", "外设工具" => "\uE765",
        "烤鸡工具" => "\uECAD", "综合检测" => "\uE9D9", _ => "\uE8B7"
    };

    public static string? GetIconGlyph(string toolPath)
    {
        var extension = Path.GetExtension(toolPath);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return "\uE8B7";
        }
        return ExtensionGlyphs.TryGetValue(extension, out var glyph) ? glyph : "\uE8B7";
    }

    public static string? GetCachedIconPath(string toolPath)
    {
        if (!File.Exists(toolPath))
            return null;

        var extension = Path.GetExtension(toolPath);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return null;

        var cacheKey = GetCacheKey(toolPath);

        if (_memoryCache.TryGetValue(cacheKey, out var cachedPath))
            return cachedPath;

        var bundledIconPath = Path.Combine(BundledCacheRoot, $"{cacheKey}.png");
        if (File.Exists(bundledIconPath) && !IsSourceStale(toolPath, bundledIconPath))
        {
            _memoryCache.Set(cacheKey, bundledIconPath);
            return bundledIconPath;
        }

        Directory.CreateDirectory(CacheRoot);
        var iconPath = Path.Combine(CacheRoot, $"{cacheKey}.png");

        if (!File.Exists(iconPath))
            return null;

        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(iconPath);
        if (age >= CacheMaxAge)
        {
            try { File.Delete(iconPath); } catch { }
            _memoryCache.Remove(cacheKey);
            return null;
        }

        if (IsSourceStale(toolPath, iconPath))
        {
            try { File.Delete(iconPath); } catch { }
            _memoryCache.Remove(cacheKey);
            return null;
        }

        _memoryCache.Set(cacheKey, iconPath);
        return iconPath;
    }

    public static async Task<string?> ExtractIconToCacheAsync(string toolPath, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(toolPath);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return null;

        await ExtractionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Recheck after acquiring the gate: another page may already have extracted it.
                var cached = GetCachedIconPath(toolPath);
                if (cached is not null) return cached;
                if (!File.Exists(toolPath)) return null;
                Directory.CreateDirectory(CacheRoot);
                var cacheKey = GetCacheKey(toolPath);
                var iconPath = Path.Combine(CacheRoot, $"{cacheKey}.png");
                var temporaryPath = iconPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

                try
                {
                    using var icon = Icon.ExtractAssociatedIcon(toolPath);
                    if (icon is null)
                        return null;

                    using var bitmap = icon.ToBitmap();
                    // Readers on a newly navigated page must never see a half-written PNG.
                    bitmap.Save(temporaryPath, System.Drawing.Imaging.ImageFormat.Png);
                    File.Move(temporaryPath, iconPath, overwrite: true);
                    _memoryCache.Set(cacheKey, iconPath);
                    return iconPath;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Unable to extract icon for {toolPath}: {ex.Message}");
                    return null;
                }
                finally
                {
                    try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { ExtractionGate.Release(); }
    }

    public static async Task LoadIconsAsync(
        IReadOnlyList<ToolItem> tools,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcher,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var itemsToLoad = tools
                .Where(t => t.IconPath is null && !string.IsNullOrWhiteSpace(t.Path))
                .Where(t =>
                {
                    var ext = System.IO.Path.GetExtension(t.Path);
                    return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                           ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            if (itemsToLoad.Count == 0)
                return;

            // 缓存命中检查移入后台线程：每个工具 8~9 次同步文件系统调用
            // （File.Exists / GetLastWriteTimeUtc / CreateDirectory），在 UI 线程
            // 串行执行 142 个 exe 会造成 ~1200 次同步 syscall 卡顿。
            // 仅图标路径的发布回 UI 线程，用低优先级让输入/滚动先得到处理。
            var needExtract = await Task.Run(() =>
            {
                var extract = new List<ToolItem>();
                foreach (var tool in itemsToLoad)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var cached = GetCachedIconPath(tool.Path);
                    if (cached is not null)
                        Publish(tool, cached);
                    else
                        extract.Add(tool);
                }
                return extract;
            }, cancellationToken).ConfigureAwait(false);

            if (needExtract.Count == 0)
                return;

            // A sequential producer avoids creating a task for every off-screen tool.
            foreach (var tool in needExtract)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var iconPath = await ExtractIconToCacheAsync(tool.Path, cancellationToken).ConfigureAwait(false);
                if (iconPath is not null) Publish(tool, iconPath);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { Debug.WriteLine($"[ToolIcon] Loading failed: {ex.Message}"); }

        void Publish(ToolItem tool, string path)
        {
            if (cancellationToken.IsCancellationRequested) return;
            if (dispatcher is not null)
                dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (!cancellationToken.IsCancellationRequested) tool.IconPath = path;
                });
            else tool.IconPath = path;
        }
    }

    public static void CleanExpiredCache() => CleanExpiredCacheAt(CacheRoot, fullCleanup: true);

    /// <summary>
    /// 清理过期图标缓存。缓存目录可能被外部清理工具/磁盘错误损坏，
    /// Directory.EnumerateFiles 会抛 IOException，而这段逻辑在启动路径上，
    /// 不能让缓存问题把整个工具箱带崩（#120）。损坏时直接重建缓存目录。
    /// </summary>
    internal static void CleanExpiredCacheAt(string cacheRoot, bool fullCleanup = false)
    {
        Interlocked.Increment(ref _cacheRevision);
        if (!Directory.Exists(cacheRoot))
            return;

        _memoryCache.Clear();

        var cutoff = DateTime.UtcNow - CacheMaxAge;

        try
        {
            foreach (var file in Directory.EnumerateFiles(cacheRoot, "*.png"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoff)
                        File.Delete(file);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ToolIcon] Cache cleanup failed, rebuilding: {ex.Message}");
            ResetCacheDirectory(cacheRoot);
        }

        if (fullCleanup)
        {
            CleanOrphanedCache();
            EnforceCacheSizeLimit();
        }
    }

    private static void ResetCacheDirectory(string cacheRoot)
    {
        try
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, true);
        }
        catch { }

        try
        {
            Directory.CreateDirectory(cacheRoot);
        }
        catch { }
    }

    public static void CleanAllCache()
    {
        Interlocked.Increment(ref _cacheRevision);
        _memoryCache.Clear();

        if (!Directory.Exists(CacheRoot))
            return;

        try
        {
            Directory.Delete(CacheRoot, true);
        }
        catch { }
    }

    public static void InvalidateCache(string toolPath)
    {
        Interlocked.Increment(ref _cacheRevision);
        var cacheKey = GetCacheKey(toolPath);
        _memoryCache.Remove(cacheKey);

        var iconPath = Path.Combine(CacheRoot, $"{cacheKey}.png");
        try { if (File.Exists(iconPath)) File.Delete(iconPath); } catch { }
    }

    private static bool IsSourceStale(string toolPath, string cachedIconPath)
    {
        try
        {
            var sourceWrite = File.GetLastWriteTimeUtc(toolPath);
            var cacheWrite = File.GetLastWriteTimeUtc(cachedIconPath);
            return sourceWrite > cacheWrite;
        }
        catch
        {
            return false;
        }
    }

    private static void CleanOrphanedCache()
    {
        if (!Directory.Exists(CacheRoot))
            return;

        try
        {
            var validKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tools = ToolCatalog.GetAllToolsCached();
            foreach (var tool in tools)
            {
                var ext = Path.GetExtension(tool.Path);
                if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    validKeys.Add(GetCacheKey(tool.Path));
                }
            }

            foreach (var file in Directory.EnumerateFiles(CacheRoot, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!validKeys.Contains(name))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    private static void EnforceCacheSizeLimit()
    {
        if (!Directory.Exists(CacheRoot))
            return;

        try
        {
            var files = new List<FileInfo>();
            foreach (var file in Directory.EnumerateFiles(CacheRoot, "*.png"))
            {
                try { files.Add(new FileInfo(file)); } catch { }
            }

            var totalSize = files.Sum(f => f.Length);
            if (totalSize <= MaxCacheSizeBytes && files.Count <= MaxCacheFiles)
                return;

            var sorted = files
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();

            foreach (var file in sorted)
            {
                if (totalSize <= MaxCacheSizeBytes && files.Count <= MaxCacheFiles)
                    break;

                var key = Path.GetFileNameWithoutExtension(file.FullName);
                _memoryCache.Remove(key);

                try
                {
                    totalSize -= file.Length;
                    files.Remove(file);
                    file.Delete();
                }
                catch { }
            }
        }
        catch { }
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string GetCacheKey(string toolPath)
    {
        var relative = PathResolver.MakeRelative(toolPath);
        return Hash(relative);
    }

        private sealed class LruCache<TKey, TValue> where TKey : notnull
        {
            private readonly int _capacity;
            private readonly Dictionary<TKey, LinkedListNode<LruEntry>> _map;
            private readonly LinkedList<LruEntry> _list;

            public LruCache(int capacity)
            {
                if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
                _capacity = capacity;
                _map = new Dictionary<TKey, LinkedListNode<LruEntry>>(capacity);
                _list = new LinkedList<LruEntry>();
            }

            public bool TryGetValue(TKey key, out TValue? value)
            {
                lock (_map)
                {
                    if (_map.TryGetValue(key, out var node))
                    {
                        _list.Remove(node);
                        _list.AddFirst(node);
                        value = node.Value.Value;
                        return true;
                    }

                    value = default;
                    return false;
                }
            }

            public void Set(TKey key, TValue value)
            {
                lock (_map)
                {
                    if (_map.TryGetValue(key, out var existing))
                    {
                        _list.Remove(existing);
                        existing.Value = new LruEntry(key, value);
                        _list.AddFirst(existing);
                    }
                    else
                    {
                        if (_map.Count >= _capacity)
                        {
                            var lru = _list.Last!;
                            _list.RemoveLast();
                            _map.Remove(lru.Value.Key);
                        }

                        var node = _list.AddFirst(new LruEntry(key, value));
                        _map[key] = node;
                    }
                }
            }

            public void Remove(TKey key)
            {
                lock (_map)
                {
                    if (_map.Remove(key, out var node))
                        _list.Remove(node);
                }
            }

            public void Clear()
            {
                lock (_map)
                {
                    _map.Clear();
                    _list.Clear();
                }
            }

            private readonly struct LruEntry
            {
                public TKey Key { get; }
                public TValue Value { get; }

                public LruEntry(TKey key, TValue value)
                {
                    Key = key;
                    Value = value;
                }
            }
        }
}
