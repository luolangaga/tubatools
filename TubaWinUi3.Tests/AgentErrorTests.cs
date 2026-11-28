using System.Text.Json;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

/// <summary>
/// AI 错误提示：保留服务端的原始说明、额度错误带重置时间，
/// 并且只在 OpenCode Zen 上提示可以登录提升额度。全部为纯逻辑，不连接模型服务。
/// </summary>
public class AgentErrorTests
{
    private static AgentErrorNotice Parse(string json, string providerId = "", DateTimeOffset? failedAt = null)
    {
        using var document = JsonDocument.Parse(json);
        return AgentErrorParser.Parse(document.RootElement, providerId, failedAt);
    }

    [Fact]
    public void FreeUsageLimit_KeepsProviderTextAndUsesZenDailyReset()
    {
        var notice = Parse("""
            {"name":"APIError","data":{"statusCode":429,"isRetryable":true,
            "responseBody":"{\"type\":\"error\",\"error\":{\"type\":\"FreeUsageLimitError\",\"message\":\"Error from provider (Console): Rate limit exceeded. Please try again later.\"}}"}}
            """, "opencode");

        Assert.Equal(AgentErrorKind.Limit, notice.Kind);
        Assert.Contains("Rate limit exceeded", notice.Detail);
        Assert.True(notice.DailyReset);
        var reset = Assert.IsType<DateTimeOffset>(notice.ResetsAt);
        Assert.Equal(TimeSpan.Zero, reset.Offset);
        Assert.Equal(0, reset.Hour);
        Assert.True(reset > DateTimeOffset.UtcNow);
        Assert.True(AgentErrorParser.CanLoginWithZen(notice, "opencode"));
    }

    [Fact]
    public void LimitWithRetryAfter_GetsExplicitResetTime()
    {
        var notice = Parse("""
            {"name":"APIError","data":{"statusCode":429,"message":"Rate limit exceeded. Please try again in 3600 seconds."}}
            """, "tuba-custom-abc");

        Assert.Equal(AgentErrorKind.Limit, notice.Kind);
        Assert.False(notice.DailyReset);
        var reset = Assert.IsType<DateTimeOffset>(notice.ResetsAt);
        var remaining = reset - DateTimeOffset.Now;
        Assert.InRange(remaining.TotalSeconds, 3500, 3700);
        // Custom providers cannot use the Zen sign-in flow.
        Assert.False(AgentErrorParser.CanLoginWithZen(notice, "tuba-custom-abc"));
    }

    [Fact]
    public void LimitWithIsoTimestamp_UsesThatInstant()
    {
        var notice = Parse("""
            {"data":{"message":"Quota exceeded. Your quota resets at 2030-01-02T03:04:05Z."}}
            """);

        Assert.Equal(AgentErrorKind.Limit, notice.Kind);
        var reset = Assert.IsType<DateTimeOffset>(notice.ResetsAt);
        Assert.Equal(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero), reset);
    }

    [Fact]
    public void JsonRetryAfterField_IsUsed()
    {
        var notice = Parse("""{"data":{"retryAfter":120,"message":"Too many requests, slow down."}}""");

        var reset = Assert.IsType<DateTimeOffset>(notice.ResetsAt);
        Assert.InRange((reset - DateTimeOffset.Now).TotalSeconds, 100, 140);
    }

    [Fact]
    public void AuthError_OffersZenSignIn()
    {
        var notice = Parse("""{"name":"ProviderAuthError","data":{"message":"Unauthorized: Invalid API key."}}""", "opencode");

        Assert.Equal(AgentErrorKind.Auth, notice.Kind);
        Assert.Contains("Invalid API key", notice.Detail);
        Assert.True(AgentErrorParser.CanLoginWithZen(notice, "opencode"));
    }

    [Fact]
    public void NetworkFailure_IsClassifiedAndKeepsReason()
    {
        var notice = Parse("""{"name":"APICallError","data":{"message":"fetch failed: getaddrinfo ENOTFOUND opencode.ai"}}""");

        Assert.Equal(AgentErrorKind.Network, notice.Kind);
        Assert.Contains("ENOTFOUND", notice.Detail);
    }

    [Fact]
    public void AbortedTurn_IsCancelledNotAnError()
    {
        var notice = Parse("""{"name":"AbortedError","data":{"message":"Request was aborted."}}""");

        Assert.Equal(AgentErrorKind.Cancelled, notice.Kind);
        Assert.Contains("任务已停止", AgentErrorParser.Compose(notice));
        // Cancelling is not a failure: the service detail is not repeated.
        Assert.DoesNotContain("Request was aborted", AgentErrorParser.Compose(notice));
    }

    [Fact]
    public void Compose_ShowsResetTimeAndProviderText()
    {
        var notice = Parse("""
            {"data":{"statusCode":429,"message":"Free usage limit reached. Please try again in 3 hours."}}
            """, "opencode");

        var text = AgentErrorParser.Compose(notice, "opencode");
        Assert.Contains("额度", text);
        Assert.Contains("重置", text);
        Assert.Contains("小时", text);
        Assert.Contains("登录", text);
        Assert.Contains("Free usage limit reached", text);
    }

    [Fact]
    public void ZenFallbackReset_IsRelativeToWhenTheFailureHappened()
    {
        var failedAt = DateTimeOffset.Parse("2026-11-24T01:00:00+08:00"); // 2026-11-23 17:00 UTC
        var notice = Parse("""{"data":{"message":"Free usage limit reached."}}""", "opencode", failedAt);

        var reset = Assert.IsType<DateTimeOffset>(notice.ResetsAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero), reset);
        Assert.Contains("现在可以重试", AgentErrorParser.Compose(notice, "opencode"));
    }

    [Fact]
    public void ChineseResetWording_IsParsed()
    {
        var notice = Parse("""{"data":{"message":"额度已用尽，请 25 分钟后重试。"}}""");

        Assert.Equal(AgentErrorKind.Limit, notice.Kind);
        var reset = Assert.IsType<DateTimeOffset>(notice.ResetsAt);
        Assert.InRange((reset - DateTimeOffset.Now).TotalMinutes, 23, 27);
    }

    [Fact]
    public void ApiKeyInProviderPayload_IsRedacted()
    {
        var notice = Parse("""{"data":{"message":"Invalid API key: sk-live-abcdef1234567890"}}""");

        Assert.DoesNotContain("abcdef1234567890", notice.Detail);
        Assert.Contains("sk-***", notice.Detail);
    }

    [Fact]
    public void ComposedMessage_StaysShortEnough()
    {
        var notice = Parse("{\"data\":{\"message\":\"" + new string('x', 900) + "\"}}");
        Assert.Equal(400, notice.Detail.Length); // 400 chars plus the ellipsis the cleaner appends
        Assert.EndsWith("…", notice.Detail);
    }

    [Fact]
    public async Task ZenCredentialStore_KeepsOtherProvidersAndReplacesZen()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tuba-zen-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "auth.json");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, """{"anthropic":{"type":"api","key":"sk-ant-other"}}""");

            await ZenCredentialStore.SaveAsync("sk-zen-new", path);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal("sk-ant-other", document.RootElement.GetProperty("anthropic").GetProperty("key").GetString());
            Assert.Equal("api", document.RootElement.GetProperty("opencode").GetProperty("type").GetString());
            Assert.Equal("sk-zen-new", document.RootElement.GetProperty("opencode").GetProperty("key").GetString());
        }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } }
    }
}
