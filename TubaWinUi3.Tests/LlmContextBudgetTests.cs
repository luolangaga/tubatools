using TubaWinUi3.Services.AiPlayground;

namespace TubaWinUi3.Tests;

/// <summary>
/// 本地模型上下文预算（LlmContextBudget）测试。
/// 覆盖之前的 KV cache 超量分配事故：max_length 必须按可用内存夹紧，
/// 且绝不能用「提示词字符数」当 token 数。
/// </summary>
public class LlmContextBudgetTests
{
    [Fact]
    public void EstimateTokens_CjkCountsOne_AsciiCountsThird()
    {
        Assert.Equal(0, LlmContextBudget.EstimateTokens(""));
        Assert.Equal(0, LlmContextBudget.EstimateTokens(null));
        Assert.Equal(3, LlmContextBudget.EstimateTokens("你好吗"));      // 3 CJK
        Assert.Equal(1, LlmContextBudget.EstimateTokens("abc"));         // 3 ASCII → 1
        Assert.Equal(2, LlmContextBudget.EstimateTokens("a\nb"));        // 2 ASCII→1 + 1 newline
    }

    [Fact]
    public void KvBytesPerToken_Phi35Mini_MatchesKnownSize()
    {
        // phi-3.5-mini：32 层 × 2 × 32 KV头 × 96 head_size × 4B = 768 KB/token
        var bytes = LlmContextBudget.KvBytesPerToken(layers: 32, kvHeads: 32, headSize: 96);
        Assert.Equal(786432, bytes);
    }

    [Fact]
    public void KvBytesPerToken_Phi4Mini_UsesKvHeads_NotAttentionHeads()
    {
        // phi-4-mini：32 层 × 2 × 8 KV头 × 128 × 4B = 256 KB/token（GQA 省内存）
        var bytes = LlmContextBudget.KvBytesPerToken(layers: 32, kvHeads: 8, headSize: 128);
        Assert.Equal(262144, bytes);
    }

    [Fact]
    public void KvBytesPerToken_InvalidDims_ReturnsZero()
    {
        Assert.Equal(0, LlmContextBudget.KvBytesPerToken(0, 8, 128));
        Assert.Equal(0, LlmContextBudget.KvBytesPerToken(32, 0, 128));
        Assert.Equal(0, LlmContextBudget.KvBytesPerToken(32, 8, 0));
    }

    [Theory]
    [InlineData("""{"model":{"decoder":{"num_hidden_layers":32,"num_key_value_heads":32,"head_size":96}}}""", 32, 32, 96)]
    [InlineData("""{"model":{"decoder":{"num_hidden_layers":32,"num_attention_heads":24,"head_size":128}}}""", 32, 24, 128)]
    public void TryParseDecoderDims_ReadsDims(string json, int layers, int kvHeads, int headSize)
    {
        Assert.True(LlmContextBudget.TryParseDecoderDims(json, out var l, out var k, out var h));
        Assert.Equal(layers, l);
        Assert.Equal(kvHeads, k);
        Assert.Equal(headSize, h);
    }

    [Fact]
    public void TryParseDecoderDims_MissingDims_ReturnsFalse()
    {
        Assert.False(LlmContextBudget.TryParseDecoderDims("""{"model":{}}""", out _, out _, out _));
        Assert.False(LlmContextBudget.TryParseDecoderDims("not json", out _, out _, out _));
    }

    [Fact]
    public void ComputeBudgetTokens_RegressionHugePrompt_StaysSmall()
    {
        // 事故：提示词约 4 万字符被当 token → max_length≈41621，
        // phi-3.5 KV 768KB/token → 32GB 分配失败。有预算后必须夹在 MaxBudgetTokens 内。
        var kv = LlmContextBudget.KvBytesPerToken(32, 32, 96);
        var budget = LlmContextBudget.ComputeBudgetTokens(kv, availableBytes: 8L << 30);

        Assert.True(budget <= LlmContextBudget.MaxBudgetTokens);
        Assert.True(budget >= LlmContextBudget.MinBudgetTokens);
        // 8GiB 可用 − 4GiB 预留 = 4GiB；4GiB / 768KB ≈ 5461 token
        Assert.InRange(budget, 5400, 5500);
    }

    [Fact]
    public void ComputeBudgetTokens_PlentyOfMemory_StillCappedAtMax()
    {
        var kv = LlmContextBudget.KvBytesPerToken(32, 8, 128); // 256KB/token
        var budget = LlmContextBudget.ComputeBudgetTokens(kv, availableBytes: 64L << 30);
        Assert.Equal(LlmContextBudget.MaxBudgetTokens, budget);
    }

    [Fact]
    public void ComputeBudgetTokens_TinyMemory_ClampedToMin()
    {
        var kv = LlmContextBudget.KvBytesPerToken(32, 32, 96);
        var budget = LlmContextBudget.ComputeBudgetTokens(kv, availableBytes: 64L << 20); // 64MB
        Assert.Equal(LlmContextBudget.MinBudgetTokens, budget);
    }

    [Fact]
    public void ComputeBudgetTokens_AvailableBelowReserve_ClampedToMin()
    {
        // 可用内存 < 4GiB 预留 → 没有 KV 空间，给下限而不是 0/负数
        var kv = LlmContextBudget.KvBytesPerToken(32, 32, 96);
        Assert.Equal(LlmContextBudget.MinBudgetTokens,
            LlmContextBudget.ComputeBudgetTokens(kv, availableBytes: LlmContextBudget.ReserveBytes - 1));
    }

    [Fact]
    public void ComputeBudgetTokens_RealMachine_Phi35_StaysReasonable()
    {
        // 本机 10.9GB 可用 − 4GiB = 6.9GiB；6.9GiB / 768KB ≈ 9420 token（远小于旧行为的 40000）
        var kv = LlmContextBudget.KvBytesPerToken(32, 32, 96);
        var budget = LlmContextBudget.ComputeBudgetTokens(kv, availableBytes: 10900L << 20);
        Assert.InRange(budget, 9000, 9600);
        // 旧行为 max_length≈40000 → KV ≈ 29GB（事故）；现在 KV ≈ 6.9GB
        Assert.True((long)budget * kv < 8L << 30);
    }

    [Fact]
    public void ComputeBudgetTokens_UnknownModel_UsesDefault()
    {
        Assert.Equal(LlmContextBudget.DefaultBudgetTokens, LlmContextBudget.ComputeBudgetTokens(0, 8L << 30));
        Assert.Equal(LlmContextBudget.DefaultBudgetTokens, LlmContextBudget.ComputeBudgetTokens(768 * 1024, 0));
    }

    [Fact]
    public void TruncateToTokens_RealTokenizerCounter_ExactUnderBudget()
    {
        // 真实 tokenizer 计数（这里用「每 2 字符 1 token」模拟）比估算更严；
        // 截断后必须严格落在预算内（旧代码用估算 → 真实 prompt 超 max_length）。
        static int Counter(string s) => (s.Length + 1) / 2;

        var text = new string('x', 20000);
        var result = LlmContextBudget.TruncateToTokens(text, maxTokens: 1000, Counter, out var truncated);

        Assert.True(truncated);
        Assert.True(Counter(result) <= 1000, $"真实计数 {Counter(result)} > 1000");
        Assert.Contains("已省略中间内容", result);
    }

    // ---------- 用户自定义上下文（ResolveEffectiveBudget） ----------

    [Fact]
    public void ResolveEffectiveBudget_ZeroOverride_UsesMemory()
    {
        Assert.Equal(8000, LlmContextBudget.ResolveEffectiveBudget(8000, 0));
        Assert.Equal(8000, LlmContextBudget.ResolveEffectiveBudget(8000, -1));
    }

    [Fact]
    public void ResolveEffectiveBudget_UserLower_UsesUser()
    {
        Assert.Equal(2000, LlmContextBudget.ResolveEffectiveBudget(8000, 2000));
    }

    [Fact]
    public void ResolveEffectiveBudget_UserHigher_NeverExceedsMemory()
    {
        // 用户想设 60000，但内存只允许 8000 → 取 8000（防 OOM）
        Assert.Equal(8000, LlmContextBudget.ResolveEffectiveBudget(8000, 60000));
    }

    [Fact]
    public void ResolveEffectiveBudget_ClampedToHardBounds()
    {
        Assert.Equal(LlmContextBudget.MaxBudgetTokens,
            LlmContextBudget.ResolveEffectiveBudget(LlmContextBudget.MaxBudgetTokens + 999, 0));
        Assert.Equal(LlmContextBudget.MinBudgetTokens,
            LlmContextBudget.ResolveEffectiveBudget(10, 0));
    }

    [Fact]
    public void TruncateToTokens_UnderBudget_Unchanged()
    {
        var text = "short prompt";
        var result = LlmContextBudget.TruncateToTokens(text, 1000, out var truncated);
        Assert.Equal(text, result);
        Assert.False(truncated);
    }

    [Fact]
    public void TruncateToTokens_OverBudget_KeepsHeadAndTail()
    {
        var head = new string('A', 3000);
        var tail = "<|assistant|>\n";
        var text = head + new string('B', 30000) + tail;

        var result = LlmContextBudget.TruncateToTokens(text, maxTokens: 500, out var truncated);

        Assert.True(truncated);
        Assert.True(result.Length < text.Length);
        Assert.Contains("已省略中间内容", result);
        // 结尾的助手标记必须存活（生成起点）
        Assert.EndsWith(tail, result);
    }

    [Fact]
    public void TruncateToTokens_CjkHeavy_GuaranteedUnderBudget()
    {
        // CJK 1 字符 = 1 token，最容易估错；截断后必须真的落在预算内
        var text = "<|system|>\n" + new string('中', 40000) + "<|assistant|>\n";
        var result = LlmContextBudget.TruncateToTokens(text, maxTokens: 2000, out var truncated);

        Assert.True(truncated);
        Assert.True(LlmContextBudget.EstimateTokens(result) <= 2000,
            $"截断后仍超预算：{LlmContextBudget.EstimateTokens(result)} > 2000");
        Assert.EndsWith("<|assistant|>\n", result);
    }
}
