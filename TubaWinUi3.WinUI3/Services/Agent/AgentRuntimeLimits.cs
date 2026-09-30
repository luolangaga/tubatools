namespace TubaWinUi3.Services.Agent;

internal static class AgentRuntimeLimits
{
    internal const int DefaultMaxRounds = 30;
    internal const int ContinueMaxRounds = 10;
    internal const float DefaultTemperature = 0.4f;
    internal const int MaxReasoningChars = 6000;
    internal const int HistoryBudgetChars = 40000;
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// 思维链截断标记：新旧引擎共用（<c>TubaChatProvider.TruncateThinking</c> /
    /// 本类的 <see cref="TruncateReasoning"/> / 两边的流式截断分支）。
    /// 只此一份 —— 曾经新引擎写「思维链过长」、旧引擎写「思维过程过长」，
    /// 导致 <c>ReasoningEchoChatClientTests</c> 断言对不上而长期失败。
    /// </summary>
    internal const string ReasoningTruncatedMarker = "[思维链过长，已截断]";

    internal static string? TruncateReasoning(string? reasoning)
    {
        if (string.IsNullOrEmpty(reasoning) || reasoning.Length <= MaxReasoningChars)
            return reasoning;

        var cut = reasoning[..MaxReasoningChars];
        if (cut.Length > 0 && char.IsHighSurrogate(cut[^1]))
            cut = cut[..^1];
        return cut + "\n\n" + ReasoningTruncatedMarker;
    }
}
