using System.Text.Json;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// AI 缓存优化单元测试：
/// 1) usage 缓存命中 token 解析（DeepSeek/GLM / Moonshot-Kimi / OpenAI 标准三种风格）；
/// 旧助手的系统提示词测试已随旧引擎移除；此处保留试炼场共用 AiService 的用量解析测试。
/// </summary>
public class AiCacheOptimizationTests
{
    private static JsonElement Usage(string json)
        => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ParseCacheTokens_DeepSeekStyle_ParsesHitAndMiss()
    {
        // DeepSeek / GLM 返回 prompt_cache_hit_tokens + prompt_cache_miss_tokens
        var usage = Usage("""{"prompt_tokens":1200,"completion_tokens":80,"total_tokens":1280,"prompt_cache_hit_tokens":1000,"prompt_cache_miss_tokens":200}""");

        var (hit, miss) = AiService.ParseCacheTokens(usage);

        Assert.Equal(1000, hit);
        Assert.Equal(200, miss);
    }

    [Fact]
    public void ParseCacheTokens_CachedTokensStyle_ParsesHit()
    {
        // Moonshot/Kimi 返回 usage.cached_tokens
        var usage = Usage("""{"prompt_tokens":900,"completion_tokens":60,"total_tokens":960,"cached_tokens":700}""");

        var (hit, miss) = AiService.ParseCacheTokens(usage);

        Assert.Equal(700, hit);
        Assert.Null(miss);
    }

    [Fact]
    public void ParseCacheTokens_OpenAiPromptTokensDetails_ParsesHit()
    {
        // OpenAI 标准：usage.prompt_tokens_details.cached_tokens
        var usage = Usage("""{"prompt_tokens":500,"completion_tokens":40,"total_tokens":540,"prompt_tokens_details":{"cached_tokens":300}}""");

        var (hit, miss) = AiService.ParseCacheTokens(usage);

        Assert.Equal(300, hit);
        Assert.Null(miss);
    }

    [Fact]
    public void ParseCacheTokens_NoCacheFields_ReturnsNulls()
    {
        var usage = Usage("""{"prompt_tokens":100,"completion_tokens":10,"total_tokens":110}""");

        var (hit, miss) = AiService.ParseCacheTokens(usage);

        Assert.Null(hit);
        Assert.Null(miss);
    }

    [Fact]
    public void ParseCacheTokens_PreferDeepSeekOverCachedTokens()
    {
        // 同时出现两种字段时以 DeepSeek 风格为准（prompt_cache_hit_tokens 优先）
        var usage = Usage("""{"prompt_tokens":1200,"completion_tokens":80,"prompt_cache_hit_tokens":1000,"prompt_cache_miss_tokens":200,"cached_tokens":999}""");

        var (hit, miss) = AiService.ParseCacheTokens(usage);

        Assert.Equal(1000, hit);
        Assert.Equal(200, miss);
    }

}
