using System.Text.Json;

namespace TubaWinUi3.Services;

public static class CompactModeService
{
    private const string CompactModeKey = "CompactModeEnabled";

    private static readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TubaWinUi3", "settings.json");

    private static Dictionary<string, string>? _cache;
    private static bool _dirty;

    public static event Action<bool>? CompactModeChanged;

    private static Dictionary<string, string> LoadSettings()
    {
        if (_cache is not null) return _cache;
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
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

    private static void SaveSettings()
    {
        if (!_dirty || _cache is null) return;
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_cache);
            File.WriteAllText(_settingsPath, json);
            _dirty = false;
        }
        catch { }
    }

    public static bool IsCompactModeEnabled()
    {
        var s = LoadSettings();
        if (s.TryGetValue(CompactModeKey, out var v) && bool.TryParse(v, out var enabled))
            return enabled;
        return false;
    }

    public static void SetCompactModeEnabled(bool enabled)
    {
        var s = LoadSettings();
        s[CompactModeKey] = enabled.ToString().ToLowerInvariant();
        _dirty = true;
        SaveSettings();
        CompactModeChanged?.Invoke(enabled);
    }
}
