using System.Text.Json;

namespace TubaWinUi3.Services;

public static class FavoritesService
{
    private const string Key = "FavoriteTools";
    private static string FavoritesPath => ConfigManager.GetFavoritesPath();
    private static List<string>? _cache;

    public static IReadOnlyList<string> GetFavorites()
    {
        if (_cache is not null)
            return _cache;

        try
        {
            if (File.Exists(FavoritesPath))
            {
                var json = File.ReadAllText(FavoritesPath);
                _cache = JsonSerializer.Deserialize(json, TubaDefaultContext.Default.ListString) ?? [];
            }
            else
            {
                _cache = [];
            }
        }
        catch
        {
            _cache = [];
        }

        return _cache;
    }

    public static bool IsFavorite(string toolPath)
    {
        return GetFavorites().Contains(toolPath, StringComparer.OrdinalIgnoreCase);
    }

    public static void AddFavorite(string toolPath)
    {
        var list = GetFavorites()
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        if (list.Contains(toolPath, StringComparer.OrdinalIgnoreCase))
            return;

        list.Add(toolPath);
        _cache = list;
        Save(list);
    }

    public static void RemoveFavorite(string toolPath)
    {
        var list = GetFavorites()
            .Where(p => !p.Equals(toolPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _cache = list;
        Save(list);
    }

    public static void RemoveAll()
    {
        _cache = [];
        Save([]);
    }

    public static void ToggleFavorite(string toolPath)
    {
        if (IsFavorite(toolPath))
            RemoveFavorite(toolPath);
        else
            AddFavorite(toolPath);
    }

    public static void InvalidateCache()
    {
        _cache = null;
    }

    private static void Save(List<string> favorites)
    {
        try
        {
            var dir = Path.GetDirectoryName(FavoritesPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(favorites, TubaDefaultContext.Default.ListString);
            File.WriteAllText(FavoritesPath, json);
        }
        catch { }
    }
}
