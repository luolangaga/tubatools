using TubaWinUi3.Storage;

namespace TubaWinUi3.Services;

public static class FavoritesService
{
    private static string FavoritesPath => ConfigManager.GetFavoritesPath();
    public static string? LastSaveError { get; private set; }
    public static event Action<string>? SaveFailed;

    // 收藏文件会被 TUI/其它前端改写，所以每次取用仍按「路径 + 写入时间 + 长度」校验一次；
    // 命中快照直接复用，避免扫描期为每个工具都开一次命名互斥体并把 JSON 解析两遍。
    private static readonly object _snapshotGate = new();
    private static string? _snapshotPath;
    private static DateTime _snapshotStamp;
    private static long _snapshotLength = long.MinValue;
    private static string[] _snapshot = [];
    private static HashSet<string> _snapshotSet = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> GetFavorites()
    {
        var path = FavoritesPath;
        try
        {
            var info = new FileInfo(path);
            var exists = info.Exists;
            var stamp = exists ? info.LastWriteTimeUtc : DateTime.MinValue;
            var length = exists ? info.Length : -1;

            lock (_snapshotGate)
            {
                if (_snapshotPath == path && _snapshotStamp == stamp && _snapshotLength == length)
                    return _snapshot;
            }

            var entries = exists
                ? FavoriteFile.Read(path).Select(PathResolver.MakeAbsolute).ToArray()
                : [];

            lock (_snapshotGate)
            {
                _snapshotPath = path;
                _snapshotStamp = stamp;
                _snapshotLength = length;
                _snapshot = entries;
                _snapshotSet = new HashSet<string>(entries, StringComparer.OrdinalIgnoreCase);
            }
            return entries;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { System.Diagnostics.Debug.WriteLine($"[Favorites] {ex.Message}"); return []; }
    }

    /// <summary>路径是否在收藏里。只查内存快照，文件状态由 GetFavorites 一次 stat 校验。</summary>
    public static bool IsFavorite(string path)
    {
        GetFavorites();
        lock (_snapshotGate) return _snapshotSet.Contains(path);
    }
    private static bool Matches(string stored, string path) => string.Equals(PathResolver.MakeAbsolute(stored), path, StringComparison.OrdinalIgnoreCase);

    private static void Update(Func<List<string>, List<string>> change)
    {
        try
        {
            FavoriteFile.Update(FavoritesPath, change);
            LastSaveError = null;
            InvalidateCache();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            LastSaveError = LocalizationService.L("Persistence_FavoritesError", "收藏保存失败，请检查数据目录权限和剩余空间后重试。");
            System.Diagnostics.Debug.WriteLine($"[Favorites] {ex}");
            SaveFailed?.Invoke(LastSaveError);
        }
    }
    public static void AddFavorite(string path) => Update(list =>
    { if (!list.Any(p => Matches(p, path))) list.Add(PathResolver.MakeRelative(path)); return list; });
    public static void RemoveFavorite(string path) => Update(list => list.Where(p => !Matches(p, path)).ToList());
    public static void RemoveAll() => Update(_ => []);
    public static void ToggleFavorite(string path) => Update(list =>
    {
        if (list.Any(p => Matches(p, path))) return list.Where(p => !Matches(p, path)).ToList();
        list.Add(PathResolver.MakeRelative(path)); return list;
    });
    public static void SaveOrder(IEnumerable<string> paths)
    {
        var ordered = paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Reordering must retain entries another frontend added since this UI snapshot.
        Update(list => ordered.Select(PathResolver.MakeRelative).Concat(list.Where(p =>
            !ordered.Any(path => Matches(p, path)))).ToList());
    }
    /// <summary>丢弃内存快照（数据目录切换、收藏写入后调用）。下次读取会重新校验文件状态。</summary>
    public static void InvalidateCache()
    {
        lock (_snapshotGate)
        {
            _snapshotPath = null;
            _snapshotStamp = default;
            _snapshotLength = long.MinValue;
            _snapshot = [];
            _snapshotSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
