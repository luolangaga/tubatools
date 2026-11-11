namespace TubaWinUi3.Services.AiAgent;

internal static class BrowserToolNames
{
    internal const string Status = "status";
    internal const string Navigate = "navigate";
    internal const string Snapshot = "snapshot";
    internal const string Click = "click";
    internal const string Type = "type";
    internal const string Press = "press";
    internal const string Scroll = "scroll";
    internal const string Select = "select";
    internal const string Screenshot = "screenshot";
    internal const string Eval = "eval";
    internal const string Wait = "wait";
    internal const string History = "history";

    internal static readonly string[] All =
    [
        Status, Navigate, Snapshot, Click, Type, Press, Scroll, Select, Screenshot, Eval, Wait, History
    ];

    internal static bool IsBrowserTool(string name) => Normalize(name).Length > 0;

    /// <summary>Accepts the unprefixed name, browser_use_* and client-prefixed variants.</summary>
    internal static string Normalize(string name)
    {
        var value = (name ?? "").Trim();
        if (value.Length == 0) return "";
        if (All.Contains(value, StringComparer.Ordinal)) return value;
        foreach (var prefix in new[] { "browser_use_", "tuba_browser_", "browser_" })
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = value[prefix.Length..];
                if (All.Contains(suffix, StringComparer.Ordinal)) return suffix;
            }
        var best = "";
        foreach (var known in All)
        {
            if (!value.EndsWith(known, StringComparison.OrdinalIgnoreCase) || value.Length == known.Length) continue;
            var separator = value[value.Length - known.Length - 1];
            if (separator is '_' or '.' && known.Length > best.Length) best = known;
        }
        return best;
    }
}
