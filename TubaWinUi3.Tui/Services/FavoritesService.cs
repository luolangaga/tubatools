using TubaWinUi3.Storage;

namespace TubaWinUi3.Tui.Services;

internal static class FavoritesService
{
    private static string FavoritesPath => ConfigManager.GetFavoritesPath();
    public static bool IsFavorite(string path)
    {
        try { return FavoriteFile.Read(FavoritesPath).Any(p => Matches(p, path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { return false; }
    }
    public static void InvalidateCache() { }
    private static bool Matches(string stored, string path) => string.Equals(
        ToolMetadataService.ResolveUserDirectory(stored), path, StringComparison.OrdinalIgnoreCase);
    public static bool ToggleFavorite(string path)
    {
        bool added = false;
        FavoriteFile.Update(FavoritesPath, entries =>
        {
            added = !entries.Any(p => Matches(p, path));
            entries.RemoveAll(p => Matches(p, path));
            if (added) entries.Add(path);
            return entries;
        });
        return added;
    }
}
