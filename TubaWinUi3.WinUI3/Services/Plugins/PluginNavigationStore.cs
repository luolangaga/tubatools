using System.Text.Json;
using TubaWinUi3.PluginSdk;

namespace TubaWinUi3.Services.Plugins;

internal static class PluginNavigationStore
{
    private static string FilePath => Path.Combine(ConfigManager.GetDataDir(), "PluginCustomization", "navigation.json");
    internal static IReadOnlyList<PluginNavigationChange> Read()
    {
        var changes = File.Exists(FilePath) ? JsonSerializer.Deserialize<PluginNavigationChange[]>(File.ReadAllText(FilePath)) ?? [] : [];
        foreach (var change in changes) { ArgumentNullException.ThrowIfNull(change); Validate(change); }
        if (changes.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != changes.Length) throw new InvalidDataException("导航 ID 重复。");
        return changes;
    }
    internal static void Save(IReadOnlyList<PluginNavigationChange> changes)
    {
        foreach (var change in changes) Validate(change);
        if (changes.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != changes.Count) throw new ArgumentException("导航 ID 重复。");
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(changes)); File.Move(temp, FilePath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static void Validate(PluginNavigationChange change)
    {
        if (string.IsNullOrWhiteSpace(change.Id) || change.Id.Length > 512 || change.Id.Any(char.IsControl)) throw new ArgumentException("导航 ID 无效。");
        if (change.Title is { Length: > 120 } || change.Title?.Any(char.IsControl) == true) throw new ArgumentException("导航标题无效。");
        if (change.Glyph is { Length: > 8 } || change.Glyph?.Any(char.IsControl) == true) throw new ArgumentException("导航图标字形无效。");
        if (change.Order < 0) throw new ArgumentOutOfRangeException(nameof(change.Order));
    }
    internal static PluginNavigationChange Merge(PluginNavigationChange? before, PluginNavigationChange next) => new(next.Id,
        next.Title ?? before?.Title, next.Glyph ?? before?.Glyph, next.IsVisible ?? before?.IsVisible, next.Order ?? before?.Order, next.IconPath ?? before?.IconPath);
    internal static IReadOnlyList<PluginNavigationItem> Apply(IReadOnlyList<PluginNavigationItem> defaults, IReadOnlyList<PluginNavigationChange> changes)
    {
        var map = changes.ToDictionary(c => c.Id, StringComparer.Ordinal);
        return defaults.Select(item =>
        {
            if (!map.TryGetValue(item.Id, out var c)) return (Item: item, Rank: item.Order, Explicit: false);
            return (Item: item with { Title = string.IsNullOrEmpty(c.Title) ? item.Title : c.Title,
                Glyph = string.IsNullOrEmpty(c.Glyph) ? item.Glyph : c.Glyph, IsVisible = c.IsVisible ?? item.IsVisible,
                IconPath = c.IconPath ?? item.IconPath }, Rank: item.IsFooter ? item.Order : c.Order ?? item.Order, Explicit: c.Order.HasValue);
        }).OrderBy(e => e.Item.IsFooter).ThenBy(e => e.Rank).ThenBy(e => e.Explicit ? 0 : 1).ThenBy(e => e.Item.Order)
            .Select((e, index) => e.Item with { Order = index }).ToArray();
    }
}
