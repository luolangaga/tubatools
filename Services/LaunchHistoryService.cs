using System.Text.Json;

namespace TubaWinUi3.Services;

public static class LaunchHistoryService
{
    private const int MaxEntries = 5;
    private static string HistoryPath => ConfigManager.GetLaunchHistoryPath();
    private static List<string>? _cache;

    public static IReadOnlyList<string> GetHistory()
    {
        if (_cache is not null)
            return _cache;

        try
        {
            if (File.Exists(HistoryPath))
            {
                var json = File.ReadAllText(HistoryPath);
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

    public static void RecordLaunch(string toolPath)
    {
        var list = GetHistory()
            .Where(p => !p.Equals(toolPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        list.Insert(0, toolPath);

        if (list.Count > MaxEntries)
            list = list.Take(MaxEntries).ToList();

        _cache = list;
        Save(list);
    }

    public static void Clear()
    {
        _cache = [];
        Save([]);
    }

    public static void InvalidateCache()
    {
        _cache = null;
    }

    private static void Save(List<string> history)
    {
        try
        {
            var dir = Path.GetDirectoryName(HistoryPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(history, TubaDefaultContext.Default.ListString);
            File.WriteAllText(HistoryPath, json);
        }
        catch { }
    }
}
