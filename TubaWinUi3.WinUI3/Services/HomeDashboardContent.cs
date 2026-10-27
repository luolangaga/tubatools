using System.Text.Json;

namespace TubaWinUi3.Services;

public sealed record HomeDashboardCard
{
    public string Target { get; init; } = "";
    public string? Title { get; init; }
    public string? Description { get; init; }
    public bool? Featured { get; init; }
}

/// <summary>Homepage content only; no grid coordinates or resize state.</summary>
public static class HomeDashboardContent
{
    public const string SettingsKey = "HomeDashboardFeaturedV3";
    public const string PreviousFeaturedSettingsKey = "HomeDashboardFeaturedV2";
    public const string LegacyContentSettingsKey = "HomeDashboardContentV1";
    public const string LegacySettingsKey = "HomeDashboardLayoutV1";
    public const int MaxCards = 500;
    public const string SelectedTabSettingsKey = "HomeDashboardSelectedTab";
    public static string ResolveTab(string? requested, string? saved) =>
        (requested ?? saved) == "favorites" ? "favorites" : "all";
    public static List<HomeDashboardCard> Defaults() =>
    [
        new() { Target = "qq", Featured = true },
        new() { Target = "hardware", Featured = true }, new() { Target = "builtin:quick-device-check", Featured = true },
        new() { Target = "builtin:space-cleanup", Featured = true },
        new() { Target = "builtin:context-menu-mgr", Featured = true }, new() { Target = "builtin:startup-manager", Featured = true },
        new() { Target = "builtin:battery-analyzer", Featured = true }, new() { Target = "builtin:stress-test", Featured = true }
    ];

    public static List<HomeDashboardCard> LoadFeatured(string? currentJson, string? legacyJson)
    {
        if (currentJson is not null) return Parse(currentJson).Select(c => c with { Featured = true }).ToList();
        if (legacyJson is null) return Defaults();
        var legacy = Parse(legacyJson);
        var featured = legacy.Where(c => c.Featured == true).ToList();
        if (featured.Count == 0) return featured;
        // Upgrade the old default strip once; do not resurrect cards the user removed.
        string[] previousDefaults = ["hardware", "builtin:quick-device-check", "builtin:space-cleanup"];
        if (featured.Select(c => c.Target).SequenceEqual(previousDefaults))
            featured.AddRange(Defaults().Where(c => c.Target is "builtin:context-menu-mgr" or "builtin:startup-manager"));
        // Apply the newly requested featured entries once, retaining existing edits.
        var community = legacy.FirstOrDefault(c => c.Target == "qq") ?? new() { Target = "qq" };
        featured.RemoveAll(c => c.Target == "qq");
        featured.Insert(0, community with { Featured = true });
        foreach (string target in new[] { "builtin:battery-analyzer", "builtin:stress-test" })
            if (featured.All(c => c.Target != target)) featured.Add(new() { Target = target, Featured = true });
        return featured;
    }

    public static List<HomeDashboardCard> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Defaults();
        try
        {
            var cards = JsonSerializer.Deserialize<List<HomeDashboardCard?>>(json);
            if (cards is null) return Defaults();
            return cards.Where(c => c is not null && ValidTarget(c.Target)).Take(MaxCards)
                .Select((c, index) => c! with { Title = Limit(c!.Title, 60),
                    Description = c.Description is null ? null : c.Description[..Math.Min(240, c.Description.Length)],
                    Featured = c.Featured ?? index < 3 }).ToList();
        }
        catch (JsonException) { return Defaults(); }
    }

    public static bool IsVisible(HomeDashboardCard card, bool packaged) => !(packaged && card.Target == "builtin:winget-store");
    private static bool ValidTarget(string? target) => target is "hardware" or "qq" ||
        (target?.StartsWith("builtin:", StringComparison.Ordinal) == true && target.Length > 8) ||
        (target?.StartsWith("tool:", StringComparison.Ordinal) == true && target.Length > 5);
    private static string? Limit(string? text, int max) => string.IsNullOrWhiteSpace(text) ? null : text[..Math.Min(max, text.Length)];
}
