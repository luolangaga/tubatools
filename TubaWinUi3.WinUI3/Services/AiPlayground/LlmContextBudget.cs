using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>
/// 本地模型上下文预算与 token 估算（纯逻辑，无 ORT 依赖，可单测）。
/// <para>
/// ORT GenAI 按 <c>max_length</c> 为 KV cache **一次性预分配**（<c>past_present_share_buffer</c>），
/// 因此 max_length 必须按内存预算夹紧，绝不能用「提示词字符数」当 token 数——
/// 那会把 max_length 撑到几万，直接触发 BFCArena 分配数十 GB 失败
/// （<c>GroupQueryAttention … Failed to allocate memory for requested buffer</c>）。
/// </para>
/// </summary>
public static class LlmContextBudget
{
    /// <summary>无模型信息时的兜底上下文预算（prompt + 生成）。</summary>
    public const int DefaultBudgetTokens = 4096;

    /// <summary>预算下限（再小就没有可用空间）。</summary>
    public const int MinBudgetTokens = 1024;

    /// <summary>预算上限（再大对小模型没有意义，且 KV 内存吃不消）。</summary>
    public const int MaxBudgetTokens = 16384;

    /// <summary>
    /// KV cache 之外的固定预留：模型权重、运行时缓冲、系统与其他进程的一般占用。
    /// KV cache 是启动时一次性预分配的大块内存，可用内存要先扣掉这部分才安全。
    /// </summary>
    public const long ReserveBytes = 4L << 30; // 4 GiB

    /// <summary>
    /// 估算 token 数：CJK 按 1 字符 ≈ 1 token，其余按 3 字符 ≈ 1 token，换行各计 1。
    /// 与 <c>AgentMemory.EstimateTokens</c> 同口径。
    /// </summary>
    public static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var cjk = 0;
        var other = 0;
        foreach (var ch in text)
        {
            if (ch >= 0x2E80) cjk++;
            else other++;
        }
        return cjk + (int)Math.Ceiling(other / 3.0) + text.Count(c => c == '\n');
    }

    /// <summary>
    /// 每个 token 的 KV cache 字节数：层数 × 2(K/V) × KV 头数 × head_size × 元素字节。
    /// </summary>
    public static long KvBytesPerToken(int layers, int kvHeads, int headSize, int elemBytes = 4)
        => layers <= 0 || kvHeads <= 0 || headSize <= 0
            ? 0
            : (long)layers * 2 * kvHeads * headSize * elemBytes;

    /// <summary>
    /// 从 genai_config.json 解析解码器维度；缺失时返回 false。
    /// </summary>
    public static bool TryParseDecoderDims(
        string genaiConfigJson, out int layers, out int kvHeads, out int headSize)
    {
        layers = kvHeads = headSize = 0;
        try
        {
            using var doc = JsonDocument.Parse(genaiConfigJson);
            if (!doc.RootElement.TryGetProperty("model", out var model)) return false;
            if (!model.TryGetProperty("decoder", out var decoder)) return false;

            layers = GetInt(decoder, "num_hidden_layers");
            kvHeads = GetInt(decoder, "num_key_value_heads");
            if (kvHeads <= 0) kvHeads = GetInt(decoder, "num_attention_heads");
            headSize = GetInt(decoder, "head_size");
            return layers > 0 && kvHeads > 0 && headSize > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 按可用内存与每 token KV 尺寸算出上下文预算（prompt + 生成）：
    /// 可用内存先扣掉 <see cref="ReserveBytes"/>（权重/系统预留），余下全部留给 KV cache。
    /// kvBytesPerToken ≤ 0（未知模型）时返回 <see cref="DefaultBudgetTokens"/>。
    /// </summary>
    public static int ComputeBudgetTokens(long kvBytesPerToken, long availableBytes)
    {
        if (kvBytesPerToken <= 0 || availableBytes <= 0) return DefaultBudgetTokens;

        var allowance = availableBytes - ReserveBytes;
        if (allowance <= 0) return MinBudgetTokens;

        var byMemory = (int)Math.Min(allowance / kvBytesPerToken, MaxBudgetTokens);
        return Math.Clamp(byMemory, MinBudgetTokens, MaxBudgetTokens);
    }

    /// <summary>
    /// 合并「内存自动值」与「用户自定义值」得到最终预算：
    /// 用户值 &gt; 0 时作为上限，但**永不突破内存自动值**（防再次 OOM）；
    /// 用户值 ≤ 0（自动）时直接返回内存自动值。结果夹在 [Min, Max]。
    /// </summary>
    public static int ResolveEffectiveBudget(int memoryBudget, int userTokens)
    {
        var memory = Math.Clamp(memoryBudget, MinBudgetTokens, MaxBudgetTokens);
        if (userTokens <= 0) return memory;
        return Math.Clamp(Math.Min(userTokens, memory), MinBudgetTokens, MaxBudgetTokens);
    }

    /// <summary>
    /// 把提示词按 token 预算截断（保留开头 60% + 结尾 40%，丢掉中段历史），
    /// 保证结尾的助手起始标记（&lt;|assistant|&gt; 等）存活。
    /// 迭代收敛，确保返回文本的估算 token 数 **必定** ≤ maxTokens（CJK 占多数时会多收几轮）。
    /// </summary>
    public static string TruncateToTokens(string prompt, int maxTokens, out bool truncated)
        => TruncateToTokens(prompt, maxTokens, EstimateTokens, out truncated);

    /// <summary>
    /// 用指定的 token 计数器（本地模型传真实 tokenizer 计数）截断提示词。
    /// ORT 按真实分词数校验 prompt 长度，故决定 max_length 时**必须**用真实计数器；
    /// 估算偏差会让真实 prompt 超过 max_length（<c>input_ids size … exceeds max length</c>）。
    /// </summary>
    public static string TruncateToTokens(
        string prompt, int maxTokens, Func<string, int> countTokens, out bool truncated)
    {
        truncated = false;
        if (string.IsNullOrEmpty(prompt) || maxTokens <= 0) return prompt;

        var count = new Func<string, int>(s => Math.Max(1, countTokens(s)));
        if (count(prompt) <= maxTokens) return prompt;

        truncated = true;

        // 初始字符预算按真实 token 比例折算，再迭代收缩到真实计数落进预算
        var keepChars = (int)Math.Min(prompt.Length, (long)prompt.Length * maxTokens / count(prompt));
        for (var i = 0; i < 16; i++)
        {
            var candidate = Compose(prompt, Math.Max(1, keepChars));
            var tokens = count(candidate);
            if (tokens <= maxTokens) return candidate;

            // 按超出比例收缩（留 5% 余量）
            var scale = (double)maxTokens / tokens * 0.95;
            keepChars = Math.Max(1, (int)(keepChars * scale));
        }

        // 兜底：最坏情况 1 字符 = 1 token
        return Compose(prompt, Math.Max(1, Math.Min(maxTokens, prompt.Length)));
    }

    /// <summary>保留开头 60% + 结尾 40%，中段以省略标记代替。</summary>
    private static string Compose(string prompt, int keepChars)
    {
        if (keepChars >= prompt.Length) return prompt;

        const double headFraction = 0.6;
        var headChars = (int)(keepChars * headFraction);
        var tailChars = keepChars - headChars;

        var sb = new StringBuilder(keepChars + 32);
        sb.Append(prompt, 0, headChars);
        sb.Append("\n…（上下文过长，已省略中间内容）…\n");
        if (tailChars > 0) sb.Append(prompt, prompt.Length - tailChars, tailChars);
        return sb.ToString();
    }

    private static int GetInt(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : 0;
}
