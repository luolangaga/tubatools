using TubaWinUi3.Storage;

namespace TubaWinUi3.Services;

public static class FavoritesService
{
    private static string FavoritesPath => ConfigManager.GetFavoritesPath();
    public static string? LastSaveError { get; private set; }
    public static event Action<string>? SaveFailed;

    public static IReadOnlyList<string> GetFavorites()
    {
        try { return FavoriteFile.Read(FavoritesPath).Select(PathResolver.MakeAbsolute).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { System.Diagnostics.Debug.WriteLine($"[Favorites] {ex.Message}"); return []; }
    }
    public static bool IsFavorite(string path) => GetFavorites().Contains(path, StringComparer.OrdinalIgnoreCase);
    private static bool Matches(string stored, string path) => string.Equals(PathResolver.MakeAbsolute(stored), path, StringComparison.OrdinalIgnoreCase);

    private static void Update(Func<List<string>, List<string>> change)
    {
        try { FavoriteFile.Update(FavoritesPath, change); LastSaveError = null; }
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
    public static void InvalidateCache() { } // Reads always use the current shared file.
}
