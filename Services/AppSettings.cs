using System.Text.Json;

namespace TubaWinUi3.Services;

public static class AppSettings
{
    public static event Action<string>? SettingChanged;

    private static string SettingsPath => ConfigManager.GetSettingsPath();

    private static Dictionary<string, string>? _cache;
    private static bool _dirty;

    public static Dictionary<string, string> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                _cache = JsonSerializer.Deserialize(json, TubaDefaultContext.Default.DictionaryStringString) ?? [];
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

    public static void Save()
    {
        if (!_dirty || _cache is null) return;
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_cache, TubaDefaultContext.Default.DictionaryStringString);
            File.WriteAllText(SettingsPath, json);
            _dirty = false;
        }
        catch { }
    }

    public static void Set(string key, string value)
    {
        var s = Load();
        s[key] = value;
        _dirty = true;
        Save();
        SettingChanged?.Invoke(key);
    }

    public static void Set(string key, bool value) => Set(key, value.ToString().ToLowerInvariant());
    public static void Set(string key, int value) => Set(key, value.ToString());
    public static void Set(string key, double value) => Set(key, value.ToString("F2"));

    public static void Remove(string key)
    {
        var s = Load();
        s.Remove(key);
        _dirty = true;
        Save();
        SettingChanged?.Invoke(key);
    }

    public static string? Get(string key)
    {
        var s = Load();
        return s.TryGetValue(key, out var v) ? v : null;
    }

    public static bool GetBool(string key, bool defaultValue = false)
    {
        var v = Get(key);
        return v is not null && bool.TryParse(v, out var b) ? b : defaultValue;
    }

    public static int GetInt(string key, int defaultValue = 0)
    {
        var v = Get(key);
        return v is not null && int.TryParse(v, out var i) ? i : defaultValue;
    }

    public static double GetDouble(string key, double defaultValue = 0)
    {
        var v = Get(key);
        return v is not null && double.TryParse(v, out var d) ? d : defaultValue;
    }

    public static void InvalidateCache()
    {
        _cache = null;
        _dirty = false;
    }
}
