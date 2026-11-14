using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tubatools.AiJunkCleanup.Models;

/// <summary>
/// 解析 AI 回复中的 <c>tuba-junk-plan</c> 围栏代码块。解析器刻意宽松：
/// 模型可能把风险写成 safe/caution/danger、把大小写成字符串，或在 JSON 里用 files 代替 items。
/// 但风险未知时一律按高风险处理，避免把不确定的东西默认勾选。
/// </summary>
internal static class JunkPlanParser
{
    internal const int MaxItems = 120;

    private static readonly Regex Fence = new(
        @"^[ \t]*(?<fence>`{3,})[ \t]*(?<lang>tuba-junk-plan|junk-plan)[ \t]*\r?\n(?<body>.*?)^[ \t]*\k<fence>[ \t]*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };

    internal static bool TryParse(string? markdown, out JunkPlan plan)
    {
        plan = null!;
        if (string.IsNullOrWhiteSpace(markdown)) return false;
        var json = LastFenceBody(markdown);
        if (json is null)
        {
            if (!TryExtractJsonObject(markdown, out json)) return false;
        }
        return TryParseJson(json, out plan);
    }

    internal static IReadOnlyList<JunkPlan> FindAll(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];
        var plans = new List<JunkPlan>();
        foreach (Match match in Fence.Matches(markdown))
            if (TryParseJson(match.Groups["body"].Value, out var plan)) plans.Add(plan);
        return plans;
    }

    /// <summary>去掉围栏代码块，用于需要展示纯说明文字的场景。</summary>
    internal static string Strip(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var text = Fence.Replace(markdown, "");
        text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
        return text;
    }

    private static string? LastFenceBody(string markdown)
    {
        string? body = null;
        foreach (Match match in Fence.Matches(markdown)) body = match.Groups["body"].Value;
        return body;
    }

    private static bool TryExtractJsonObject(string text, out string json)
    {
        json = "";
        // 兜底：模型忘了围栏时，尝试取第一个 { 到最后一个 }。只在文本里确实出现 items 时启用，
        // 避免把普通 JSON 误当成清理清单。
        if (!text.Contains("\"items\"", StringComparison.OrdinalIgnoreCase) &&
            !text.Contains("\"files\"", StringComparison.OrdinalIgnoreCase)) return false;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return false;
        json = text[start..(end + 1)];
        return true;
    }

    private static bool TryParseJson(string json, out JunkPlan plan)
    {
        plan = null!;
        try
        {
            var dto = JsonSerializer.Deserialize<PlanDto>(json, JsonOptions);
            if (dto is null) return false;
            var source = dto.Items ?? dto.Files;
            if (source is null || source.Count == 0) return false;
            var items = new List<JunkPlanItem>();
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in source)
            {
                if (items.Count >= MaxItems) break;
                var path = NormalizePath(item.Path);
                if (path.Length == 0) continue;
                var risk = ParseRisk(item.Risk);
                var value = new JunkPlanItem(
                    path,
                    IsDirectoryKind(item.Kind, item.Type, path),
                    ParseSize(item.SizeBytes, item.SizeMB, item.Size),
                    risk,
                    Clean(item.Category, "其他"),
                    Clean(item.Reason, "AI 没有提供删除原因"),
                    Clean(item.RiskDetail ?? item.Detail, ""),
                    Clean(item.Suggestion, ""));
                if (seen.TryGetValue(path, out var index))
                {
                    // 同一路径重复出现时保留风险更高的一条，理由拼接起来。
                    var existing = items[index];
                    if (value.Risk > existing.Risk || existing.Reason.Length == 0)
                    {
                        var merged = value with
                        {
                            Reason = Join(existing.Reason, value.Reason),
                            RiskDetail = Join(existing.RiskDetail, value.RiskDetail)
                        };
                        items[index] = merged;
                    }
                    continue;
                }
                seen[path] = items.Count;
                items.Add(value);
            }
            if (items.Count == 0) return false;
            plan = new JunkPlan(Clean(dto.Summary, "AI 生成的清理建议"), items);
            return true;
        }
        catch (JsonException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    private static string NormalizePath(string? value)
    {
        var path = (value ?? "").Trim().Trim('"');
        if (path.Length == 0 || path.Length > 32767) return "";
        return path;
    }

    private static bool IsDirectoryKind(string? kind, string? type, string path)
    {
        var value = (kind ?? type ?? "").Trim().ToLowerInvariant();
        if (value is "directory" or "dir" or "folder" or "文件夹" or "目录") return true;
        if (value is "file" or "文件") return false;
        return path.EndsWith('\\') || path.EndsWith('/');
    }

    private static JunkRisk ParseRisk(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "low" or "safe" or "none" or "normal" or "低" or "低风险" => JunkRisk.Low,
        "medium" or "caution" or "中" or "中风险" => JunkRisk.Medium,
        "high" or "danger" or "dangerous" or "高" or "高风险" => JunkRisk.High,
        // 缺失或未知：宁可默认高风险，让用户主动看。
        _ => JunkRisk.High
    };

    private static long ParseSize(JsonElement bytes, double? sizeMb, string? size)
    {
        if (bytes.ValueKind == JsonValueKind.Number && bytes.TryGetInt64(out var number) && number > 0) return number;
        if (bytes.ValueKind == JsonValueKind.String && long.TryParse(bytes.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0) return parsed;
        if (sizeMb is > 0 and < 1_000_000) return (long)(sizeMb.Value * 1024 * 1024);
        if (!string.IsNullOrWhiteSpace(size))
        {
            var text = size.Trim();
            var suffix = text.EndsWith("TB", StringComparison.OrdinalIgnoreCase) ? "tb"
                : text.EndsWith("GB", StringComparison.OrdinalIgnoreCase) ? "gb"
                : text.EndsWith("MB", StringComparison.OrdinalIgnoreCase) ? "mb"
                : text.EndsWith("KB", StringComparison.OrdinalIgnoreCase) ? "kb"
                : text.EndsWith("B", StringComparison.OrdinalIgnoreCase) ? "b" : "";
            if (suffix.Length > 0) text = text[..^suffix.Length];
            if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0)
            {
                var factor = suffix switch { "tb" => 1024d * 1024 * 1024 * 1024, "gb" => 1024d * 1024 * 1024, "mb" => 1024d * 1024, "kb" => 1024d, _ => 1d };
                var result = value * factor;
                if (result is > 0 and < long.MaxValue) return (long)result;
            }
        }
        return 0;
    }

    private static string Clean(string? value, string fallback)
    {
        var text = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length == 0 ? fallback : text.Length > 800 ? text[..800] + "…" : text;
    }

    private static string Join(string left, string right) =>
        left.Length == 0 ? right : right.Length == 0 || left.Contains(right, StringComparison.Ordinal) ? left : left + " · " + right;

    private sealed class PlanDto
    {
        public string? Summary { get; set; }
        public List<ItemDto>? Items { get; set; }
        public List<ItemDto>? Files { get; set; }
    }

    private sealed class ItemDto
    {
        public string? Path { get; set; }
        public string? Kind { get; set; }
        public string? Type { get; set; }
        public JsonElement SizeBytes { get; set; }
        public double? SizeMB { get; set; }
        public string? Size { get; set; }
        public string? Risk { get; set; }
        public string? Category { get; set; }
        public string? Reason { get; set; }
        public string? RiskDetail { get; set; }
        public string? Detail { get; set; }
        public string? Suggestion { get; set; }
    }
}
