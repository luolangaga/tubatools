using System.Net.Http;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class TopCpuScraperService
{
    private static readonly HttpClient Http = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    static TopCpuScraperService()
    {
        Http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
    }

    private static readonly string BaseUrl = "https://www.topcpu.net";

    public static async Task<TopCpuCpuResult> ScrapeCpuRankingsAsync(
        string bench = "cinebench-r23-multi-core",
        string? category = null)
    {
        var url = BuildCpuUrl(bench, category);
        var html = await Http.GetStringAsync(url);
        return ParseCpuRankingsHtml(html, bench, category, url);
    }

    internal static TopCpuCpuResult ParseCpuRankingsHtml(string html, string bench, string? category, string url = "")
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim() ?? "";
        var lastUpdated = ExtractLastUpdated(title);

        var entries = new List<CpuRankingEntry>();
        var nodes = doc.DocumentNode.SelectNodes("//div[starts-with(@id,'rr')]");
        if (nodes is null || nodes.Count == 0)
        {
            nodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'flex-col') and .//input[@data-cmp]]");
        }

        if (nodes is not null)
        {
            foreach (var node in nodes)
            {
                var entry = ParseCpuEntry(node);
                if (entry is not null && bench.Contains("single-core", StringComparison.Ordinal))
                {
                    entry.SingleCore = entry.Rating;
                    entry.MultiCore = 0;
                }
                if (entry is not null)
                    entries.Add(entry);
            }
        }

        var cat = category switch
        {
            "desktop" => "desktop",
            "laptop" => "laptop",
            "server" => "server",
            "embedded" => "embedded",
            _ => "all"
        };

        foreach (var e in entries)
            e.Category = cat == "all" ? DetectCpuCategory(e.Name) : cat;

        return new TopCpuCpuResult
        {
            Entries = entries,
            LastUpdated = lastUpdated,
            BenchName = ExtractBenchName(title),
            SourceUrl = url
        };
    }

    public static async Task<TopCpuGpuResult> ScrapeGpuRankingsAsync(
        string bench = "time-spy",
        string? category = null)
    {
        var url = BuildGpuUrl(bench, category);
        var html = await Http.GetStringAsync(url);
        return ParseGpuRankingsHtml(html, bench, category, url);
    }

    internal static TopCpuGpuResult ParseGpuRankingsHtml(string html, string bench, string? category, string url = "")
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim() ?? "";
        var lastUpdated = ExtractLastUpdated(title);

        var entries = new List<GpuRankingEntry>();
        var nodes = doc.DocumentNode.SelectNodes("//div[starts-with(@id,'rr')]");
        if (nodes is null || nodes.Count == 0)
        {
            nodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'flex-col') and .//input[@data-cmp]]");
        }

        if (nodes is not null)
        {
            foreach (var node in nodes)
            {
                var entry = ParseGpuEntry(node);
                if (entry is not null)
                    entries.Add(entry);
            }
        }

        var cat = category switch
        {
            "desktop" => "desktop",
            "laptop" => "laptop",
            "integrated" => "integrated",
            "professional" => "professional",
            "ai" => "ai",
            _ => "all"
        };

        foreach (var e in entries)
            e.Category = cat == "all" ? DetectGpuCategory(e.Name) : cat;

        return new TopCpuGpuResult
        {
            Entries = entries,
            LastUpdated = lastUpdated,
            BenchName = ExtractBenchName(title),
            SourceUrl = url
        };
    }

    private static string BuildCpuUrl(string bench, string? category)
    {
        var path = $"/cpu-r/{bench}";
        if (!string.IsNullOrEmpty(category) && category != "all")
            path += $"-{category}";
        return BaseUrl + path;
    }

    private static string BuildGpuUrl(string bench, string? category)
    {
        var benchPath = bench switch
        {
            "fp32" => "fp32-float",
            "time-spy" => "3dmark-time-spy",
            "time-spy-extreme" => "3dmark-time-spy-extreme",
            "speed-way" => "3dmark-speed-way",
            "ai-tops" => "ai-tops",
            _ => "fp32-float"
        };

        var path = $"/gpu-r/{benchPath}";

        if (!string.IsNullOrEmpty(category) && category != "all")
        {
            var catSuffix = category switch
            {
                "desktop" => "-desktop",
                "laptop" => "-mobile",
                "integrated" => "-integrated",
                "professional" => "-professional",
                "ai" => "-ai",
                _ => ""
            };
            path += catSuffix;
        }

        return BaseUrl + path;
    }

    private static CpuRankingEntry? ParseCpuEntry(HtmlNode node)
    {
        try
        {
            var rankSpan = node.SelectSingleNode(".//span[contains(@class,'min-w')]");
            if (rankSpan is null) return null;

            var rankText = rankSpan.InnerText.Trim().TrimEnd('.', ' ');
            if (!int.TryParse(rankText, out var rank)) return null;

            var link = node.SelectSingleNode(".//a");
            if (link is null) return null;

            var name = HtmlEntity.DeEntitize(link.InnerText.Trim());

            var specSpan = node.SelectSingleNode(".//span[contains(@class,'text-gray-400')]");
            var spec = specSpan is not null ? HtmlEntity.DeEntitize(specSpan.InnerText.Trim()) : "";

            var scoreSpan = node.SelectSingleNode(".//span[contains(@class,'font-bold')]");
            var scoreText = scoreSpan?.InnerText.Trim().Replace(",", "") ?? "0";
            if (!int.TryParse(scoreText, out var score)) score = 0;

            var brand = DetectBrand(name);
            var (cores, process) = ParseCpuSpec(spec);

            return new CpuRankingEntry
            {
                Rank = rank,
                Name = name,
                Brand = brand,
                Process = process,
                Rating = score,
                Grade = ComputeGrade(rank),
                SingleCore = 0,
                MultiCore = score,
                Cores = cores,
                Tdp = "",
                Cache = "",
                Category = ""
            };
        }
        catch
        {
            return null;
        }
    }

    private static GpuRankingEntry? ParseGpuEntry(HtmlNode node)
    {
        try
        {
            var rankSpan = node.SelectSingleNode(".//span[contains(@class,'min-w')]");
            if (rankSpan is null) return null;

            var rankText = rankSpan.InnerText.Trim().TrimEnd('.', ' ');
            if (!int.TryParse(rankText, out var rank)) return null;

            var link = node.SelectSingleNode(".//a");
            if (link is null) return null;

            var name = HtmlEntity.DeEntitize(link.InnerText.Trim());

            var specSpan = node.SelectSingleNode(".//span[contains(@class,'text-gray-400')]");
            var spec = specSpan is not null ? HtmlEntity.DeEntitize(specSpan.InnerText.Trim()) : "";

            var scoreSpan = node.SelectSingleNode(".//span[contains(@class,'font-bold')]");
            var scoreText = scoreSpan?.InnerText.Trim() ?? "";

            var tflops = "";
            var rating = 0;

            if (node.InnerText.Contains("TFLOPS", StringComparison.OrdinalIgnoreCase))
            {
                tflops = scoreText.Replace("TFLOPS", "").Trim();
                double.TryParse(tflops, out var tfVal);
                rating = (int)Math.Round(tfVal);
            }
            else
            {
                var cleanScore = scoreText.Replace(",", "");
                int.TryParse(cleanScore, out rating);
            }

            var brand = DetectGpuBrand(name);
            var vram = ExtractVram(spec);

            return new GpuRankingEntry
            {
                Rank = rank,
                Name = name,
                Brand = brand,
                Rating = rating,
                Grade = ComputeGrade(rank),
                Gaming = 0,
                Render = 0,
                Tflops = tflops,
                GeekbenchOpencl = "",
                TimeSpy = tflops.Length == 0 ? rating.ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
                Vram = vram,
                Category = ""
            };
        }
        catch
        {
            return null;
        }
    }

    private static string DetectBrand(string name)
    {
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return "Intel";
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("EPYC", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Threadripper", StringComparison.OrdinalIgnoreCase)) return "AMD";
        if (name.Contains("Apple", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("M1", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("M2", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("M3", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("M4", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("M5", StringComparison.OrdinalIgnoreCase)) return "Apple";
        if (name.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Snapdragon", StringComparison.OrdinalIgnoreCase)) return "Qualcomm";
        return "Other";
    }

    private static string DetectGpuBrand(string name)
    {
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("TITAN", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("L", StringComparison.OrdinalIgnoreCase) && name.Contains("40") && name.Contains("GB") ||
            name.Contains("H100", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("H200", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("B200", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("A100", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("A40", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("A10", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("A30", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("A16", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("A2", StringComparison.OrdinalIgnoreCase)) return "Nvidia";
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Instinct", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RX ", StringComparison.OrdinalIgnoreCase)) return "AMD";
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Arc", StringComparison.OrdinalIgnoreCase)) return "Intel";
        if (name.Contains("Apple", StringComparison.OrdinalIgnoreCase)) return "Apple";
        if (name.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Adreno", StringComparison.OrdinalIgnoreCase)) return "Qualcomm";
        return "Other";
    }

    private static string DetectCpuCategory(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("hx") || lower.Contains("h ") || lower.EndsWith(" h") ||
            lower.Contains("u ") || lower.EndsWith(" u") ||
            lower.Contains("p ") || lower.EndsWith(" p") ||
            lower.Contains("ultra 7 2") && lower.Contains("h") && !lower.Contains("k") ||
            lower.Contains("ryzen ai") ||
            lower.Contains("ryzen 3 5") && lower.Contains("u") ||
            lower.Contains("ryzen 5 7") && lower.Contains("u") ||
            lower.Contains("ryzen 7 7") && lower.Contains("u") ||
            lower.Contains("core ultra") && (lower.Contains("h") || lower.Contains("u")) && !lower.Contains("k"))
            return "laptop";
        if (lower.Contains("epyc") || lower.Contains("xeon") || lower.Contains("server"))
            return "server";
        return "desktop";
    }

    private static string DetectGpuCategory(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("mobile") || lower.Contains("laptop") ||
            lower.Contains("max-q") || lower.Contains("m ") ||
            lower.Contains("rx 7900m") || lower.Contains("rx 6800m") ||
            lower.Contains("rx 7800m"))
            return "laptop";
        if (lower.Contains("rtx a") || lower.Contains("rtx pro") ||
            lower.Contains("instinct") || lower.Contains("data center") ||
            lower.Contains("h100") || lower.Contains("h200") ||
            lower.Contains("b200") || lower.Contains("a100") ||
            lower.Contains("l40") || lower.Contains("l20") ||
            lower.Contains("a40") || lower.Contains("h800"))
            return "professional";
        if (lower.Contains("integrated") || lower.Contains("iris") ||
            lower.Contains("uhd") || lower.Contains("adreno"))
            return "integrated";
        return "desktop";
    }

    private static (string cores, string process) ParseCpuSpec(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return ("", "");

        var cores = "";
        var coreMatch = Regex.Match(spec, @"(\d+C\s+\d+T)");
        if (coreMatch.Success)
            cores = coreMatch.Groups[1].Value;

        var process = "";
        var procMatch = Regex.Match(spec, @"(\d+\s*nm)", RegexOptions.IgnoreCase);
        if (procMatch.Success)
            process = procMatch.Groups[1].Value;

        return (cores, process);
    }

    private static string ExtractVram(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return "";
        var match = Regex.Match(spec, @"(\d+\s*GB\s*\w*)");
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    private static string ExtractLastUpdated(string title)
    {
        var match = Regex.Match(title, @"(\d{4})年(\d{1,2})月");
        if (match.Success)
            return $"{match.Groups[1].Value}年{match.Groups[2].Value}月";
        return DateTime.Now.ToString("yyyy年M月");
    }

    private static string ExtractBenchName(string title)
    {
        var benches = new[]
        {
            "Cinebench R23 单核", "Cinebench R23 多核",
            "Geekbench 6 单核", "Geekbench 6 多核",
            "Cinebench 2024 单核", "Cinebench 2024 多核",
            "Cinebench 2024 GPU",
            "Cinebench 2026 单核", "Cinebench 2026 多核",
            "Blender",
            "Geekbench 5 单核", "Geekbench 5 多核",
            "Passmark CPU 单核", "Passmark CPU 多核",
            "FP32浮点性能",
            "3DMark Time Spy", "3DMark Time Spy Extreme",
            "3DMark Speed Way",
            "AI算力"
        };

        foreach (var b in benches)
        {
            if (title.Contains(b, StringComparison.OrdinalIgnoreCase))
                return b;
        }

        return "";
    }

    private static string ComputeGrade(int rank)
    {
        if (rank <= 10) return "A+";
        if (rank <= 50) return "A";
        if (rank <= 150) return "B+";
        if (rank <= 300) return "B";
        if (rank <= 500) return "C+";
        if (rank <= 800) return "C";
        return "D";
    }
}

public sealed class TopCpuCpuResult
{
    public List<CpuRankingEntry> Entries { get; set; } = [];
    public string LastUpdated { get; set; } = "";
    public string BenchName { get; set; } = "";
    public string SourceUrl { get; set; } = "";
}

public sealed class TopCpuGpuResult
{
    public List<GpuRankingEntry> Entries { get; set; } = [];
    public string LastUpdated { get; set; } = "";
    public string BenchName { get; set; } = "";
    public string SourceUrl { get; set; } = "";
}
