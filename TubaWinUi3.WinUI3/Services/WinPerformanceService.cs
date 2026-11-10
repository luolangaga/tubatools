using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>
/// Win性能测试服务：负责最终得分计算、历史读写，以及收集用于图片列表
/// 渲染测试的图标缓存路径。分数独立于游戏/办公性能，不参与两者的总分。
/// </summary>
public static class WinPerformanceService
{
    /// <summary>基准平均耗时（毫秒）：达到该耗时得 100 分。快于基准按平方根曲线增长（无上限）。</summary>
    private const double FullScoreMs = 8000.0;

    /// <summary>评分下限：平均耗时达到该值附近时得分趋近 0。</summary>
    private const double FloorMs = 45000.0;

    /// <summary>图片列表渲染测试的目标图片数量。</summary>
    public const int ImageListCount = 10000;

    /// <summary>列表加载测试的目标条目数量。</summary>
    public const int ListLoadCount = 20000;

    /// <summary>树形列表展开测试的节点数量。</summary>
    public const int TreeExpandCount = 3000;

    /// <summary>排序过滤测试的数据量（条）。</summary>
    public const int SortFilterCount = 60000;

    /// <summary>长文本渲染测试的字符数量。</summary>
    public const int LongTextChars = 60000;

    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = false
    };

    /// <summary>
    /// 根据「去掉最高耗时轮后」的平均耗时计算得分。
    /// 基准 8000ms 得 100 分；快于基准按平方根曲线增长（无上限：5000ms≈113、2000ms≈200、
    /// 1000ms≈283、500ms≈400），避免高分爆炸；慢于基准线性下降，45000ms 得 0 分。
    /// 纯函数，便于单元测试。
    /// </summary>
    public static int ComputeWinScore(double avgMs)
    {
        if (avgMs <= 0.0) return 0;
        if (avgMs >= FloorMs) return 0;
        if (avgMs <= FullScoreMs)
        {
            // 快于基准：平方根曲线，100 分 = 8000ms
            return (int)(100.0 * Math.Sqrt(FullScoreMs / avgMs));
        }
        // 慢于基准：8000ms -> 100，45000ms -> 0，线性下降
        return Math.Max(0, (int)(100.0 * (FloorMs - avgMs) / (FloorMs - FullScoreMs)));
    }

    /// <summary>复用现有等级体系（S/A+/A/B+/B/C/D/E）。</summary>
    public static string ComputeGrade(int score) => PerformanceBenchmarkService.ComputeGrade(score);

    /// <summary>
    /// 汇总一轮完整测试：去掉 RunCount 轮中耗时最高（得分最低）的一轮，
    /// 用剩余轮次的平均耗时计算最终得分与等级。
    /// </summary>
    public static WinPerformanceResult FinalizeResult(WinPerformanceResult result)
    {
        if (result.Runs.Count == 0)
        {
            result.BestAvgMs = 0.0;
            result.FinalScore = 0;
            result.Grade = ComputeGrade(0);
            return result;
        }

        int dropCount = Math.Clamp(result.DroppedRunCount, 0, result.Runs.Count - 1);
        var kept = result.Runs
            .OrderByDescending(r => r.TotalMs)
            .Skip(dropCount)
            .ToList();
        double avgMs = kept.Count > 0 ? kept.Average(r => r.TotalMs) : 0.0;
        result.BestAvgMs = avgMs;
        result.FinalScore = ComputeWinScore(avgMs);
        result.Grade = ComputeGrade(result.FinalScore);

        // 各子测试平均耗时（仅统计保留轮次），用于明细展示
        result.AvgListLoadMs = AvgOf(kept, r => r.ListLoadMs);
        result.AvgImageListMs = AvgOf(kept, r => r.ImageListMs);
        result.AvgTabSwitchMs = AvgOf(kept, r => r.TabSwitchMs);
        result.AvgScrollMs = AvgOf(kept, r => r.ScrollMs);
        result.AvgTreeExpandMs = AvgOf(kept, r => r.TreeExpandMs);
        result.AvgSortFilterMs = AvgOf(kept, r => r.SortFilterMs);
        result.AvgTextRenderMs = AvgOf(kept, r => r.TextRenderMs);
        return result;

        static double AvgOf(List<WinPerformanceRunResult> runs, Func<WinPerformanceRunResult, double> selector)
            => runs.Count > 0 ? runs.Average(selector) : 0.0;
    }

    /// <summary>
    /// 收集用于「渲染大量图片列表」测试的图片路径。
    /// 优先使用已缓存的工具图标（IconCache），其次扫描图标缓存目录，再兜底
    /// 使用应用自带的 Assets 图片；数量不足时循环取用，始终凑满
    /// <see cref="ImageListCount"/> 个（若系统上完全没有任何图片则返回空列表，
    /// 页面会跳过图片列表子测试）。
    /// </summary>
    public static List<string> CollectIconImagePaths(int count = ImageListCount)
    {
        var paths = new List<string>();
        try
        {
            var tools = ToolCatalog.GetAllToolsCached();
            foreach (var tool in tools)
            {
                if (string.IsNullOrWhiteSpace(tool.Path)) continue;
                try
                {
                    var cached = ToolIconService.GetCachedIconPath(tool.Path);
                    if (!string.IsNullOrWhiteSpace(cached) && File.Exists(cached))
                        paths.Add(cached);
                }
                catch { }
                if (paths.Count >= count) break;
            }
        }
        catch { }

        // 兜底：扫描图标缓存目录中已生成的 PNG
        if (paths.Count < count)
        {
            try
            {
                var cacheDir = ConfigManager.GetIconCacheDir();
                if (Directory.Exists(cacheDir))
                {
                    foreach (var file in Directory.EnumerateFiles(cacheDir, "*.png"))
                    {
                        if (!paths.Contains(file)) paths.Add(file);
                        if (paths.Count >= count) break;
                    }
                }
            }
            catch { }
        }

        // 兜底：应用自带的 Assets 图片，保证即使没有图标缓存也能渲染真实图片
        if (paths.Count < count)
        {
            try
            {
                var assetsDir = Path.Combine(ToolCatalog.AppDirectory, "Assets");
                if (Directory.Exists(assetsDir))
                {
                    foreach (var file in Directory.EnumerateFiles(assetsDir, "*.png", SearchOption.AllDirectories))
                    {
                        if (!paths.Contains(file)) paths.Add(file);
                        if (paths.Count >= count) break;
                    }
                    if (paths.Count < count)
                    {
                        foreach (var file in Directory.EnumerateFiles(assetsDir, "*.jpg", SearchOption.AllDirectories))
                        {
                            if (!paths.Contains(file)) paths.Add(file);
                            if (paths.Count >= count) break;
                        }
                    }
                }
            }
            catch { }
        }

        // 循环取用：数量不足时重复填充，模拟「大量重复图片」的列表加载
        if (paths.Count > 0)
        {
            while (paths.Count < count)
                paths.Add(paths[paths.Count % paths.Count]);
        }
        else if (count > 0)
        {
            // 完全没有可用图片时，填充空路径让调用方跳过图片测试
            for (int i = 0; i < count; i++) paths.Add("");
        }
        return paths;
    }

    /// <summary>生成用于排序/过滤子测试的随机数据集（固定种子保证可复现）。</summary>
    public static List<KeyValuePair<string, int>> GenerateSortData(int count = SortFilterCount)
    {
        var rand = new Random(42);
        var list = new List<KeyValuePair<string, int>>(count);
        for (int i = 0; i < count; i++)
        {
            int v = rand.Next(0, 100000);
            list.Add(new KeyValuePair<string, int>($"Item {v}", v));
        }
        return list;
    }

    public static List<WinPerformanceResult> LoadHistory()
    {
        string path = Path.Combine(ConfigManager.GetDataDir(), "WinBenchmarkHistory.json");
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<WinPerformanceResult>>(File.ReadAllText(path), s_jsonOpts) ?? [];
        }
        catch { return []; }
    }

    public static void SaveHistory(WinPerformanceResult result)
    {
        try
        {
            var list = LoadHistory();
            list.Add(result);
            if (list.Count > 20)
            {
                list = list.TakeLast(20).ToList();
            }
            string path = Path.Combine(ConfigManager.GetDataDir(), "WinBenchmarkHistory.json");
            File.WriteAllText(path, JsonSerializer.Serialize(list, s_jsonOpts));
        }
        catch { }
    }

    public static void ClearHistory()
    {
        try
        {
            string path = Path.Combine(ConfigManager.GetDataDir(), "WinBenchmarkHistory.json");
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}
