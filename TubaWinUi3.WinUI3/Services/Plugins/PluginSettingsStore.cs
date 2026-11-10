using System.Collections.Concurrent;
using System.Text.Json;

namespace TubaWinUi3.Services.Plugins;

internal sealed class PluginSettingsStore(string pluginId)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    internal string DirectoryPath => Path.Combine(ConfigManager.GetDataDir(), "PluginData", PluginPackageRules.Segment(pluginId));
    private string FilePath => Path.Combine(DirectoryPath, "settings.json");
    private Dictionary<string, string> Read() => File.Exists(FilePath)
        ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("插件配置无效。") : [];
    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw new ArgumentException("插件配置键长度必须为 1–128。", nameof(key));
    }
    internal async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        ValidateKey(key);
        var gate = Gates.GetOrAdd(FilePath, _ => new(1, 1)); await gate.WaitAsync(ct);
        try { return Read().GetValueOrDefault(key); } finally { gate.Release(); }
    }
    internal async Task SetAsync(string key, string? value, CancellationToken ct)
    {
        ValidateKey(key);
        var gate = Gates.GetOrAdd(FilePath, _ => new(1, 1)); await gate.WaitAsync(ct);
        try
        {
            var data = Read();
            if (value is null) data.Remove(key); else data[key] = value;
            ct.ThrowIfCancellationRequested(); Directory.CreateDirectory(DirectoryPath);
            await File.WriteAllTextAsync(FilePath + ".tmp", JsonSerializer.Serialize(data), ct);
            File.Move(FilePath + ".tmp", FilePath, true);
        }
        finally { gate.Release(); }
    }
}
