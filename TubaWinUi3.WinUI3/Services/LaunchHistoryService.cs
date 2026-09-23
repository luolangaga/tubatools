using System.Text.Json;
using System.Text.Json.Serialization;

namespace TubaWinUi3.Services;

public class LaunchRecord
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("lastLaunched")]
    public DateTime LastLaunched { get; set; }

    [JsonPropertyName("firstLaunched")]
    public DateTime FirstLaunched { get; set; }
}

public static class LaunchHistoryService
{
    private const int MaxEntries = 100;

    /// <summary>「常用」页面顶部常用推荐区块的显示开关（设置页 - 常规 可重新开启）。</summary>
    public const string ShowFrequentRecommendationsSettingKey = "ShowFrequentRecommendations";

    private static string HistoryPath => ConfigManager.GetLaunchHistoryPath();
    private static List<LaunchRecord>? _cache;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static IReadOnlyList<LaunchRecord> GetRecords()
    {
        if (_cache is not null)
            return _cache;

        try
        {
            if (File.Exists(HistoryPath))
            {
                var json = File.ReadAllText(HistoryPath);
                var records = JsonSerializer.Deserialize<List<LaunchRecord>>(json, _jsonOptions);
                if (records is not null)
                {
                    foreach (var r in records)
                    {
                        r.Path = PathResolver.MakeAbsolute(r.Path);
                    }
                    _cache = records;
                    return _cache;
                }
            }

            _cache = MigrateFromOldFormat();
            if (_cache.Count > 0)
                Save(_cache);
        }
        catch
        {
            _cache = [];
        }

        return _cache;
    }

    public static IReadOnlyList<string> GetHistory()
    {
        return GetRecords().Select(r => r.Path).ToList();
    }

    public static IReadOnlyList<LaunchRecord> GetFrequentTools(int maxCount = 12)
    {
        var records = GetRecords();
        if (records.Count == 0)
            return [];

        var now = DateTime.UtcNow;
        var scored = records
            .Select(r =>
            {
                var daysSinceLast = (now - r.LastLaunched).TotalDays;
                var recencyBonus = Math.Max(0, 7 - daysSinceLast) * 0.5;
                var frequencyScore = r.Count * 1.0;
                return (Record: r, Score: frequencyScore + recencyBonus);
            })
            .OrderByDescending(x => x.Score)
            .Take(maxCount)
            .Select(x => x.Record)
            .ToList();

        return scored;
    }

    public static void RecordLaunch(string toolPath)
    {
        var records = GetRecords().ToList();

        var existing = records.FirstOrDefault(r =>
            r.Path.Equals(toolPath, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.Count++;
            existing.LastLaunched = DateTime.UtcNow;
            records.Remove(existing);
            records.Insert(0, existing);
        }
        else
        {
            records.Insert(0, new LaunchRecord
            {
                Path = toolPath,
                Count = 1,
                LastLaunched = DateTime.UtcNow,
                FirstLaunched = DateTime.UtcNow
            });
        }

        if (records.Count > MaxEntries)
            records = records.Take(MaxEntries).ToList();

        _cache = records;
        Save(records);
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

    private static List<LaunchRecord> MigrateFromOldFormat()
    {
        try
        {
            if (!File.Exists(HistoryPath))
                return [];

            var json = File.ReadAllText(HistoryPath);
            var oldList = JsonSerializer.Deserialize<List<string>>(json);
            if (oldList is null || oldList.Count == 0)
                return [];

            var now = DateTime.UtcNow;
            return oldList.Select((path, index) => new LaunchRecord
            {
                Path = path,
                Count = 1,
                LastLaunched = now.AddMinutes(-index),
                FirstLaunched = now.AddHours(-index)
            }).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static void Save(List<LaunchRecord> records)
    {
        try
        {
            var dir = Path.GetDirectoryName(HistoryPath)!;
            Directory.CreateDirectory(dir);
            var toSave = records.Select(r => new LaunchRecord
            {
                Path = PathResolver.MakeRelative(r.Path),
                Count = r.Count,
                LastLaunched = r.LastLaunched,
                FirstLaunched = r.FirstLaunched
            }).ToList();
            var json = JsonSerializer.Serialize(toSave, _jsonOptions);
            File.WriteAllText(HistoryPath, json);
        }
        catch { }
    }
}
