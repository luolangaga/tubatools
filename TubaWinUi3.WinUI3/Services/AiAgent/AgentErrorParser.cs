using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services.AiAgent;

internal enum AgentErrorKind { Unknown, Cancelled, Limit, Auth, Network, Component }

/// <summary>A model/API failure parsed from the agent's error payload.</summary>
internal sealed record AgentErrorNotice(AgentErrorKind Kind, string Detail = "", DateTimeOffset? ResetsAt = null, bool DailyReset = false);

/// <summary>
/// Turns the raw error the AI component reports into a concrete, readable message:
/// the provider's own text is kept, quota errors carry a reset time (explicit in the payload,
/// or the documented Zen daily reset), and we remember whether OpenCode Zen sign-in can help.
/// </summary>
internal static class AgentErrorParser
{
    internal const string ZenProviderId = "opencode";
    private const int MaxDetailLength = 400;

    internal static AgentErrorNotice Parse(JsonElement error, string providerId, DateTimeOffset? failedAt = null) =>
        Build(error.GetRawText(), DetailFromJson(error), RetryFromJson(error, failedAt), providerId, null, failedAt);

    internal static AgentErrorNotice Parse(string raw, string providerId = "", DateTimeOffset? failedAt = null) =>
        Build(raw, DetailFromRaw(raw), null, providerId, null, failedAt);

    internal static AgentErrorNotice FromException(Exception ex, string providerId = "") => ex switch
    {
        AgentModelFailureException failure => failure.Notice,
        OperationCanceledException => new(AgentErrorKind.Cancelled),
        TimeoutException timeout => Build(timeout.Message, timeout.Message, null, providerId, AgentErrorKind.Network),
        HttpRequestException http => Build(http.Message, http.Message, null, providerId),
        IOException io => Build(io.Message, io.Message, null, providerId, AgentErrorKind.Component),
        _ => Build(ex.Message, ex.Message, null, providerId)
    };

    /// <summary>Signing in to Zen raises the free-model allowance, so only offer it for that provider.</summary>
    internal static bool CanLoginWithZen(AgentErrorNotice notice, string providerId) =>
        providerId == ZenProviderId && notice.Kind is AgentErrorKind.Limit or AgentErrorKind.Auth;

    internal static string Compose(AgentErrorNotice notice, string providerId = "")
    {
        var zen = providerId == ZenProviderId;
        var lines = new List<string>();
        switch (notice.Kind)
        {
            case AgentErrorKind.Cancelled:
                lines.Add(AgentText.L("Cancelled", "任务已停止。"));
                break;
            case AgentErrorKind.Limit:
                lines.Add(zen
                    ? AgentText.L("ErrLimitZen", "OpenCode Zen 免费模型的调用额度已用尽。")
                    : AgentText.L("ErrLimit", "模型调用额度已用尽。"));
                AddReset(lines, notice);
                lines.Add(zen
                    ? AgentText.L("ErrLimitActionZen", "可点「登录」提升免费额度，或改用其他模型。")
                    : notice.ResetsAt is null
                        ? AgentText.L("ErrLimitActionPlain", "可改用其他模型，或稍后重试。")
                        : AgentText.L("ErrLimitAction", "可改用其他模型，或等额度重置后重试。"));
                break;
            case AgentErrorKind.Auth:
                lines.Add(AgentText.L("ErrAuth", "模型服务未授权：API Key 无效或没有访问权限。"));
                lines.Add(zen
                    ? AgentText.L("ErrAuthActionZen", "可点「登录」重新获取 OpenCode Zen 的 API Key。")
                    : AgentText.L("ErrAuthAction", "请在「AI 提供商」中检查该服务的 API Key。"));
                break;
            case AgentErrorKind.Network:
                lines.Add(AgentText.L("ErrNetwork", "连接模型服务失败，请检查网络或代理设置后重试。"));
                break;
            case AgentErrorKind.Component:
                lines.Add(AgentText.L("ErrComponent", "AI 组件不可用。"));
                break;
            default:
                lines.Add(notice.Detail.Length > 0
                    ? AgentText.L("ErrUnknown", "AI 任务未完成。")
                    : AgentText.L("Failed", "AI 任务未完成，请检查网络、模型可用性和 AI 组件状态后重试。"));
                break;
        }
        if (notice.Detail.Length > 0 && notice.Kind != AgentErrorKind.Cancelled)
        {
            var pattern = notice.Kind is AgentErrorKind.Limit or AgentErrorKind.Auth or AgentErrorKind.Network
                ? AgentText.L("ErrDetail", "服务返回：{0}") : AgentText.L("ErrReason", "错误信息：{0}");
            lines.Add(string.Format(pattern, notice.Detail));
        }
        return string.Join("\n", lines);
    }

    private static void AddReset(List<string> lines, AgentErrorNotice notice)
    {
        if (notice.ResetsAt is not { } reset) return;
        var span = reset - DateTimeOffset.Now;
        if (span <= TimeSpan.Zero)
        {
            lines.Add(string.Format(AgentText.L("ErrResetReady", "额度重置时间：{0}，现在可以重试。"), Clock(reset)));
            return;
        }
        var pattern = notice.DailyReset ? AgentText.L("ErrResetDaily", "免费额度按天重置：{0}（{1}后）。") : AgentText.L("ErrResetAt", "额度重置时间：{0}（{1}后）。");
        lines.Add(string.Format(pattern, Clock(reset), Duration(span)));
    }

    private static string Clock(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        var day = local.Date == today ? AgentText.L("ErrToday", "今天")
            : local.Date == today.AddDays(1) ? AgentText.L("ErrTomorrow", "明天")
            : local.ToString(AgentText.L("ErrDateFormat", "M月d日"), System.Globalization.CultureInfo.InvariantCulture);
        return string.Format(AgentText.L("ErrClock", "{0} {1}"), day, local.ToString("HH:mm"));
    }

    private static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1)) return AgentText.L("ErrDurSoon", "不到 1 分钟");
        if (span < TimeSpan.FromHours(1)) return string.Format(AgentText.L("ErrDurMinutes", "{0} 分钟"), Math.Max(1, (int)span.TotalMinutes));
        var hours = (int)span.TotalHours;
        return span.Minutes == 0
            ? string.Format(AgentText.L("ErrDurHours", "{0} 小时"), hours)
            : string.Format(AgentText.L("ErrDurHoursMinutes", "{0} 小时 {1} 分"), hours, span.Minutes);
    }

    private static AgentErrorNotice Build(string raw, string detail, DateTimeOffset? retryAt, string providerId,
        AgentErrorKind? forcedKind = null, DateTimeOffset? failedAt = null)
    {
        var kind = forcedKind ?? Classify(raw + " " + detail);
        var clean = Clean(detail);
        var reset = retryAt ?? ResetFrom(clean, failedAt) ?? ResetFrom(raw, failedAt);
        var daily = false;
        // The free Zen allowance is documented as a daily quota resetting at 00:00 UTC.
        if (reset is null && kind == AgentErrorKind.Limit && providerId == ZenProviderId)
        {
            var now = failedAt?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
            reset = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero).AddDays(1);
            daily = true;
        }
        return new AgentErrorNotice(kind, clean, reset, daily);
    }

    private static AgentErrorKind Classify(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("aborted") || lower.Contains("interrupted")) return AgentErrorKind.Cancelled;
        var contextOverflow = lower.Contains("context length") || lower.Contains("context window") || lower.Contains("maximum context") ||
            lower.Contains("too many tokens") || lower.Contains("上下文长度");
        if (!contextOverflow && (lower.Contains("freeusagelimit") || lower.Contains("free usage limit") || lower.Contains("rate limit") ||
            lower.Contains("rate_limit") || lower.Contains("ratelimit") || lower.Contains("too many requests") || lower.Contains("quota") ||
            lower.Contains("usage limit") || lower.Contains("insufficient balance") || lower.Contains("exceeded") || lower.Contains("429") ||
            lower.Contains("额度") || lower.Contains("限额")))
            return AgentErrorKind.Limit;
        if (lower.Contains("invalid api key") || lower.Contains("invalid_api_key") || lower.Contains("unauthorized") || lower.Contains("401") ||
            lower.Contains("authentication") || lower.Contains("no auth") || lower.Contains("api key") || lower.Contains("apikey"))
            return AgentErrorKind.Auth;
        if (lower.Contains("fetch failed") || lower.Contains("econnrefused") || lower.Contains("enotfound") || lower.Contains("getaddrinfo") ||
            lower.Contains("socket") || lower.Contains("connect") || lower.Contains("network") || lower.Contains("timeout") ||
            lower.Contains("timed out") || lower.Contains("certificate") || lower.Contains("tls") || lower.Contains("proxy"))
            return AgentErrorKind.Network;
        return AgentErrorKind.Unknown;
    }

    private static readonly Regex IsoTime = new(
        @"\b(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2})?(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2}))\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RelativeTime = new(
        @"(?:try again|retry|resets?|reset|available|recharge)[^\n]{0,40}?(?:in|after)\s*(\d+(?:\.\d+)?)\s*(seconds?|secs?|s|minutes?|mins?|m|hours?|hrs?|h|天|小时|分钟|秒)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RetryAfterHeader = new(
        @"retry-after\s*[:=]\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ChineseRelative = new(
        @"(\d+(?:\.\d+)?)\s*(秒|分钟|小时|天)\s*(?:后|之后|以后)", RegexOptions.Compiled);

    private static readonly Regex Secret = new(
        @"\bsk-[A-Za-z0-9_\-]{6,}|(\bBearer\s+)[A-Za-z0-9._\-]{12,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UtcMidnight = new(
        @"(?:utc\s*0{1,2}[:：]00|0{1,2}[:：]00\s*utc|midnight\s*utc|utc\s*midnight)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static DateTimeOffset? ResetFrom(string text, DateTimeOffset? failedAt)
    {
        if (text.Length == 0) return null;
        var now = failedAt ?? DateTimeOffset.Now;
        var iso = IsoTime.Match(text);
        if (iso.Success && DateTimeOffset.TryParse(iso.Groups[1].Value, out var at)) return at;
        if (UtcMidnight.IsMatch(text)) return NextUtcMidnight(now);
        var retry = RetryAfterHeader.Match(text);
        if (retry.Success && int.TryParse(retry.Groups[1].Value, out var seconds) && seconds > 0)
            return now.AddSeconds(seconds);
        var relative = RelativeTime.Match(text);
        if (!relative.Success || !double.TryParse(relative.Groups[1].Value, out var value) || value <= 0)
        {
            var chinese = ChineseRelative.Match(text);
            if (!chinese.Success || !double.TryParse(chinese.Groups[1].Value, out value) || value <= 0) return null;
            return now.Add(UnitSpan(ChineseUnit(chinese.Groups[2].Value), value));
        }
        return now.Add(UnitSpan(relative.Groups[2].Value.ToLowerInvariant(), value));
    }

    private static string ChineseUnit(string unit) => unit switch
    {
        "秒" => "seconds", "分钟" => "minutes", "小时" => "hours", "天" => "days", _ => "seconds"
    };

    private static TimeSpan UnitSpan(string unit, double value)
    {
        if (unit.StartsWith("天") || unit.StartsWith("day")) return TimeSpan.FromDays(value);
        if (unit == "h" || unit.StartsWith("hour") || unit.StartsWith("hr") || unit.StartsWith("小时")) return TimeSpan.FromHours(value);
        if (unit == "m" || unit.StartsWith("min") || unit.StartsWith("分")) return TimeSpan.FromMinutes(value);
        return TimeSpan.FromSeconds(value);
    }

    private static DateTimeOffset NextUtcMidnight(DateTimeOffset reference)
    {
        var now = reference.ToUniversalTime();
        return new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero).AddDays(1);
    }

    private static DateTimeOffset? RetryFromJson(JsonElement json, DateTimeOffset? failedAt) =>
        ScanRetry(json, new DateTimeOffset?[] { null }, 0, failedAt ?? DateTimeOffset.Now);

    private static DateTimeOffset? ScanRetry(JsonElement json, DateTimeOffset?[] found, int depth, DateTimeOffset now)
    {
        if (depth > 4 || json.ValueKind != JsonValueKind.Object) return found[0];
        foreach (var property in json.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var seconds) &&
                (property.Name.Equals("retryAfter", StringComparison.OrdinalIgnoreCase) ||
                 property.Name.Equals("retry_after", StringComparison.OrdinalIgnoreCase) ||
                 property.Name.Equals("retry-after", StringComparison.OrdinalIgnoreCase) ||
                 property.Name.Equals("retryIn", StringComparison.OrdinalIgnoreCase)) && seconds > 0 && seconds < 86400 * 365)
            {
                found[0] ??= now.AddSeconds(seconds);
                continue;
            }
            if (property.Value.ValueKind == JsonValueKind.String && property.Name.Equals("retry-after", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(property.Value.GetString(), out var headerSeconds) && headerSeconds > 0)
            {
                found[0] ??= now.AddSeconds(headerSeconds);
                continue;
            }
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) ScanRetry(property.Value, found, depth + 1, now);
        }
        return found[0];
    }

    private static string DetailFromJson(JsonElement error)
    {
        if (error.ValueKind == JsonValueKind.Object)
        {
            if (FindString(error, "message") is { Length: > 0 } message) return message;
            // Provider payloads can arrive as a nested JSON string (responseBody / body).
            foreach (var name in new[] { "responseBody", "body", "response" })
                if (FindString(error, name) is { Length: > 0 } nested && DetailFromRaw(nested) is { Length: > 0 } inner)
                    return inner;
        }
        return DetailFromRaw(error.ValueKind == JsonValueKind.String ? error.GetString() ?? "" : error.ToString());
    }

    private static string? FindString(JsonElement element, string name, int depth = 0)
    {
        if (depth > 4) return null;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind == JsonValueKind.String) return Clean(property.Value.GetString() ?? "");
                    if (property.Value.ValueKind == JsonValueKind.Object &&
                        FindString(property.Value, "message", depth + 1) is { Length: > 0 } nested) return nested;
                }
            foreach (var property in element.EnumerateObject())
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array &&
                    FindString(property.Value, name, depth + 1) is { Length: > 0 } nested) return nested;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (FindString(item, name, depth + 1) is { Length: > 0 } nested) return nested;
        }
        return null;
    }

    private static string DetailFromRaw(string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0) return "";
        if (text[0] is '{' or '[')
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                var extracted = DetailFromJson(document.RootElement);
                if (extracted.Length > 0 && !extracted.Equals(text, StringComparison.Ordinal)) return extracted;
            }
            catch (JsonException) { }
        }
        return Clean(text);
    }

    private static string Clean(string text)
    {
        var builder = new StringBuilder(Math.Min(text.Length, MaxDetailLength));
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) { space = builder.Length > 0; continue; }
            if (char.IsControl(c)) continue;
            if (space && builder.Length > 0) builder.Append(' ');
            space = false;
            builder.Append(c);
            if (builder.Length >= MaxDetailLength) { builder.Length = MaxDetailLength - 1; builder.Append('…'); break; }
        }
        var cleaned = builder.ToString().Trim();
        // Never echo credentials that a provider may repeat inside its error payload.
        return Secret.Replace(cleaned, m => m.Groups[1].Success ? m.Groups[1].Value + "***" : "sk-***");
    }
}
