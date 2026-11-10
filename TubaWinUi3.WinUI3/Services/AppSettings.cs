using System.Text.Json;
using System.Globalization;
using TubaWinUi3.Storage;
using TubaWinUi3.Services.Ai;

namespace TubaWinUi3.Services;

public static class AppSettings
{
    public static event Action<string>? SettingChanged;
    public static event Action<string>? SaveFailed;
    public static string? LastSaveError { get; private set; }

    private static string SettingsPath => ConfigManager.GetSettingsPath();

    private static readonly object _gate = new();
    private static Dictionary<string, string>? _cache;
    private static bool _dirty;

    // 写盘去抖：连续 Set 合并为一次落盘，拖拽排序等高频调用不再每次都
    // 全量序列化 + 同步写文件；Save 在后台线程执行，不阻塞调用方。
    private static readonly System.Threading.Timer _persistTimer =
        new(_ => Save(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

    private const int PersistDebounceMs = 500;

    public static Dictionary<string, string> Load()
    {
        lock (_gate) return new Dictionary<string, string>(LoadLocked());
    }

    private static Dictionary<string, string> LoadLocked()
    {
        lock (_gate)
        {
            if (_cache is not null) return _cache;
            try
            {
                if (File.Exists(SettingsPath) || File.Exists(SettingsPath + ".bak"))
                {
                    using var lease = AtomicFile.Lock(SettingsPath);
                    var json = AtomicFile.ReadText(SettingsPath, text => _ = JsonSerializer.Deserialize<Dictionary<string, string>>(text) ?? throw new JsonException("设置格式无效。"));
                    _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(json!) ?? [];
                    if (_cache.Any(p => IsSecretKey(p.Key) && !string.IsNullOrEmpty(p.Value)
                        && !p.Value.StartsWith("dpapi:v1:", StringComparison.Ordinal)))
                    { _dirty = true; Save(); }
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
    }

    public static void Save()
    {
        lock (_gate)
        {
            if (!_dirty || _cache is null) return;
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath)!;
                Directory.CreateDirectory(dir);
                foreach (var key in _cache.Keys.Where(IsSecretKey).ToArray())
                    if (_cache[key]?.StartsWith("dpapi:v1:", StringComparison.Ordinal) != true)
                        _cache[key] = ProtectedApiKeyConverter.Encode(_cache[key] ?? "");
                var json = JsonSerializer.Serialize(_cache);
                using var lease = AtomicFile.Lock(SettingsPath);
                AtomicFile.WriteText(SettingsPath, json);
                if (File.Exists(SettingsPath + ".bak"))
                {
                    var previous = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath + ".bak"));
                    if (previous is not null)
                    {
                        foreach (var key in previous.Keys.Where(IsSecretKey).ToArray())
                            if (previous[key]?.StartsWith("dpapi:v1:", StringComparison.Ordinal) != true)
                                previous[key] = ProtectedApiKeyConverter.Encode(previous[key] ?? "");
                        AtomicFile.WriteText(SettingsPath + ".bak", JsonSerializer.Serialize(previous), backup: false);
                    }
                }
                LastSaveError = null;
                _dirty = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettings] Save failed: {ex.Message}");
                LastSaveError = LocalizationService.L("Persistence_SettingsError", "设置保存失败，请检查数据目录权限和剩余空间后重试。");
                // Invoke outside the storage lock and isolate subscriber exceptions.
                var message = LastSaveError;
                ThreadPool.QueueUserWorkItem(_ => { try { SaveFailed?.Invoke(message); } catch { } });
            }
        }
    }

    /// <summary>立即同步落盘（窗口关闭/退出前调用，避免去抖窗口内丢失最后一次变更）。</summary>
    public static void Flush() => Save();

    private static void SchedulePersist()
    {
        try { _persistTimer.Change(PersistDebounceMs, System.Threading.Timeout.Infinite); }
        catch { }
    }

    // 事件在锁外广播，逐订阅者隔离异常，单个处理器失败不中断其余订阅者。
    private static void RaiseSettingChanged(string key)
    {
        var handler = SettingChanged;
        if (handler is null) return;
        foreach (var subscriber in handler.GetInvocationList())
        {
            try { ((Action<string>)subscriber)(key); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettings] SettingChanged subscriber error: {ex.Message}");
            }
        }
    }

    public static void Set(string key, string value)
    {
        lock (_gate)
        {
            var s = LoadLocked();
            s[key] = IsSecretKey(key) ? ProtectedApiKeyConverter.Encode(value) : value;
            _dirty = true;
            SchedulePersist();
        }
        RaiseSettingChanged(key);
    }

    public static void Set(string key, bool value) => Set(key, value.ToString().ToLowerInvariant());
    public static void Set(string key, int value) => Set(key, value.ToString(CultureInfo.InvariantCulture));
    public static void Set(string key, double value) => Set(key, value.ToString("R", CultureInfo.InvariantCulture));

    public static void Remove(string key)
    {
        lock (_gate)
        {
            var s = LoadLocked();
            s.Remove(key);
            _dirty = true;
            SchedulePersist();
        }
        RaiseSettingChanged(key);
    }

    public static string? Get(string key)
    {
        lock (_gate)
        {
            var s = LoadLocked();
            if (!s.TryGetValue(key, out var v) || v is null) return null;
            if (!IsSecretKey(key)) return v;
            try
            {
                var decoded = ProtectedApiKeyConverter.Decode(v);
                if (!v.StartsWith("dpapi:v1:", StringComparison.Ordinal) && decoded.Length > 0)
                {
                    s[key] = ProtectedApiKeyConverter.Encode(decoded);
                    _dirty = true;
                    Save();
                }
                return decoded;
            }
            catch (JsonException) { return null; }
        }
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
        if (v is null) return defaultValue;
        // Old versions wrote the decimal separator of the current culture. Never allow
        // thousands separators: "1.50" must not become 150 after a region change.
        var normalized = v.Contains(',') && !v.Contains('.') ? v.Replace(',', '.') : v;
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            && double.IsFinite(d) ? d : defaultValue;
    }

    internal static bool IsSecretKey(string key) => key is "GitHubToken" or "ProxyPassword" or "AiApiKey"
        or "OpenCodeZenToken" or "SearchApiKey";

    public static void InvalidateCache()
    {
        lock (_gate)
        {
            _cache = null;
            _dirty = false;
        }
    }
}