using System.Net.Http;
using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class CpuRankingService
{
    private static List<CpuRankingEntry>? _desktop;
    private static List<CpuRankingEntry>? _laptop;
    private static DateTime _lastRefreshTime = DateTime.MinValue;
    private static readonly TimeSpan Cooldown = TimeSpan.FromHours(1);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static readonly string[] FallbackUrls =
    [
        "https://raw.tubawinui3.cn/luolangaga/tubatools/raw/branch/master/Metadata/cpu-ranking.json",
        "https://raw.githubusercontent.com/luolangaga/tubatools/master/Metadata/cpu-ranking.json"
    ];

    public static List<CpuRankingEntry> Desktop => _desktop ?? [];
    public static List<CpuRankingEntry> Laptop => _laptop ?? [];
    public static string LastUpdated { get; private set; } = "";
    public static bool CanRefresh => DateTime.Now - _lastRefreshTime >= Cooldown;
    public static DateTime LastRefreshTime => _lastRefreshTime;
    public static TimeSpan CooldownTime => Cooldown;
    public static string BenchName { get; private set; } = "";

    public static void Load()
    {
        if (_desktop is not null) return;

        var path = FindDataFile();
        if (path is null)
        {
            _desktop = [];
            _laptop = [];
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            ParseAndSet(json);
        }
        catch
        {
            _desktop = [];
            _laptop = [];
        }
    }

    public static async Task<CpuRefreshResult> RefreshFromNetworkAsync()
    {
        if (!CanRefresh)
        {
            var remaining = Cooldown - (DateTime.Now - _lastRefreshTime);
            return new CpuRefreshResult
            {
                Success = false,
                Message = $"数据已是最新，{remaining.Minutes} 分钟后可再次刷新"
            };
        }

        try
        {
            var desktopResult = await TopCpuScraperService.ScrapeCpuRankingsAsync(
                "cinebench-r23-multi-core", "desktop");
            var laptopResult = await TopCpuScraperService.ScrapeCpuRankingsAsync(
                "cinebench-r23-multi-core", "laptop");

            var desktopEntries = desktopResult.Entries;
            var laptopEntries = laptopResult.Entries;

            if (desktopEntries.Count == 0 && laptopEntries.Count == 0)
            {
                return await RefreshFromFallbackAsync();
            }

            _desktop = desktopEntries;
            _laptop = laptopEntries;
            _lastRefreshTime = DateTime.Now;
            LastUpdated = desktopResult.LastUpdated;
            BenchName = desktopResult.BenchName;

            SaveCache();

            return new CpuRefreshResult
            {
                Success = true,
                Message = $"已从 TopCPU.net 刷新！桌面 {desktopEntries.Count} 款 / 笔记本 {laptopEntries.Count} 款",
                DesktopCount = desktopEntries.Count,
                LaptopCount = laptopEntries.Count
            };
        }
        catch (HttpRequestException ex)
        {
            return await RefreshFromFallbackAsync($"TopCPU.net 请求失败：{ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return await RefreshFromFallbackAsync("TopCPU.net 请求超时");
        }
        catch (Exception ex)
        {
            return await RefreshFromFallbackAsync($"TopCPU.net 刷新失败：{ex.Message}");
        }
    }

    private static async Task<CpuRefreshResult> RefreshFromFallbackAsync(string? topCpuError = null)
    {
        try
        {
            string? json = null;
            string? lastError = null;

            foreach (var url in FallbackUrls)
            {
                try
                {
                    json = await Http.GetStringAsync(url);
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
            }

            if (json is null)
            {
                return new CpuRefreshResult
                {
                    Success = false,
                    Message = topCpuError is not null
                        ? $"{topCpuError}，备用源也失败：{lastError}"
                        : $"网络请求失败：{lastError}"
                };
            }

            var data = JsonSerializer.Deserialize(json, TubaCamelCaseContext.Default.CpuRankingData);

            if (data is null || (data.Desktop.Count == 0 && data.Laptop.Count == 0))
            {
                return new CpuRefreshResult
                {
                    Success = false,
                    Message = topCpuError is not null
                        ? $"{topCpuError}，备用数据解析失败"
                        : "数据解析失败，JSON 格式可能已变更"
                };
            }

            foreach (var e in data.Desktop) e.Category = "desktop";
            foreach (var e in data.Laptop) e.Category = "laptop";

            _desktop = data.Desktop;
            _laptop = data.Laptop;
            _lastRefreshTime = DateTime.Now;
            LastUpdated = data.LastUpdated;
            BenchName = "";

            SaveCache();

            var prefix = topCpuError is not null ? $"{topCpuError}，已从备用源刷新。 " : "";
            return new CpuRefreshResult
            {
                Success = true,
                Message = $"{prefix}桌面 {data.Desktop.Count} 款 / 笔记本 {data.Laptop.Count} 款",
                DesktopCount = data.Desktop.Count,
                LaptopCount = data.Laptop.Count
            };
        }
        catch (Exception ex)
        {
            return new CpuRefreshResult
            {
                Success = false,
                Message = topCpuError is not null
                    ? $"{topCpuError}，备用源也失败：{ex.Message}"
                    : $"刷新失败：{ex.Message}"
            };
        }
    }

    public static void ForceAllowRefresh() => _lastRefreshTime = DateTime.MinValue;

    public static List<CpuRankingEntry> GetByCategory(string category) =>
        category == "laptop" ? Laptop : Desktop;

    public static List<CpuRankingEntry> Filter(List<CpuRankingEntry> entries, string? brand, string? keyword)
    {
        var filtered = entries.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(brand) && brand != "全部")
            filtered = filtered.Where(e => e.Brand == brand);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            filtered = filtered.Where(e =>
                e.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                e.Process.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                e.Cores.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }
        return filtered.ToList();
    }

    private static void ParseAndSet(string json)
    {
        var data = JsonSerializer.Deserialize(json, TubaCamelCaseContext.Default.CpuRankingData);
        if (data is null)
        {
            _desktop = [];
            _laptop = [];
            return;
        }

        foreach (var e in data.Desktop) e.Category = "desktop";
        foreach (var e in data.Laptop) e.Category = "laptop";

        _desktop = data.Desktop;
        _laptop = data.Laptop;
        LastUpdated = data.LastUpdated;
    }

    private static void SaveCache()
    {
        try
        {
            var dir = System.IO.Path.Combine(GetCacheDir(), "Metadata");
            Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "cpu-ranking.json");

            var data = new CpuRankingData
            {
                LastUpdated = LastUpdated,
                Source = "topcpu.net",
                Desktop = _desktop ?? [],
                Laptop = _laptop ?? []
            };

            var json = JsonSerializer.Serialize(data, TubaCamelCaseIndentedContext.Default.CpuRankingData);

            File.WriteAllText(path, json);
        }
        catch { }
    }

    private static string GetCacheDir() => ConfigManager.GetDataDir();

    private static string? FindDataFile()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            var p = System.IO.Path.Combine(dir, "Metadata", "cpu-ranking.json");
            if (File.Exists(p)) return p;
            var parent = System.IO.Path.GetDirectoryName(dir);
            if (parent is null) break;
            dir = parent;
        }

        var fallback = System.IO.Path.Combine(AppContext.BaseDirectory, "Metadata", "cpu-ranking.json");
        if (File.Exists(fallback)) return fallback;

        var cachePath = System.IO.Path.Combine(GetCacheDir(), "Metadata", "cpu-ranking.json");
        if (File.Exists(cachePath)) return cachePath;

        return null;
    }
}

public sealed class CpuRefreshResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int DesktopCount { get; set; }
    public int LaptopCount { get; set; }
}
