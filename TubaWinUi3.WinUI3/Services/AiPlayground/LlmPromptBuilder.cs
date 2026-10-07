using System.Text;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>
/// LLM 提示词构造与停止标记（纯逻辑，无 ORT GenAI 依赖，可单测；x86 构建同样可用）。
/// </summary>
public static class LlmPromptBuilder
{
    private static readonly string[] StopMarkers =
    [
        "<|end|>", "<|eot_id|>", "<|im_end|>", "<|endoftext|>",
        "<|user|>", "<|system|>", "<|assistant|>", "<|im_start|>",
    ];

    /// <summary>返回最早出现的停止标记位置；未出现返回 -1。</summary>
    public static int FindStopMarker(StringBuilder text)
    {
        var s = text.ToString();
        int earliest = -1;
        foreach (var marker in StopMarkers)
        {
            var index = s.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0 && (earliest < 0 || index < earliest))
                earliest = index;
        }
        return earliest;
    }

    /// <summary>按模板风格构造提示词。</summary>
    public static string BuildPrompt(
        AiPromptFormat format,
        string? systemPrompt,
        IReadOnlyList<(string Role, string Text)> history)
    {
        var sb = new StringBuilder();
        switch (format)
        {
            case AiPromptFormat.ChatML:
                if (!string.IsNullOrWhiteSpace(systemPrompt))
                    sb.Append("<|im_start|>system\n").Append(systemPrompt).Append("<|im_end|>\n");
                foreach (var (role, text) in history)
                {
                    sb.Append("<|im_start|>")
                      .Append(role == "assistant" ? "assistant" : "user")
                      .Append('\n').Append(text).Append("<|im_end|>\n");
                }
                sb.Append("<|im_start|>assistant\n");
                break;

            case AiPromptFormat.Llama3:
                if (!string.IsNullOrWhiteSpace(systemPrompt))
                    sb.Append("<|start_header_id|>system<|end_header_id|>\n\n")
                      .Append(systemPrompt).Append("<|eot_id|>");
                foreach (var (role, text) in history)
                {
                    sb.Append("<|start_header_id|>")
                      .Append(role == "assistant" ? "assistant" : "user")
                      .Append("<|end_header_id|>\n\n").Append(text).Append("<|eot_id|>");
                }
                sb.Append("<|start_header_id|>assistant<|end_header_id|>\n\n");
                break;

            case AiPromptFormat.Plain:
                if (!string.IsNullOrWhiteSpace(systemPrompt))
                    sb.Append("[系统] ").AppendLine(systemPrompt).AppendLine();
                foreach (var (role, text) in history)
                    sb.AppendLine(role == "assistant" ? $"[助手] {text}" : $"[用户] {text}");
                sb.Append("[助手] ");
                break;

            default: // Phi（Phi-3 / Phi-4 系列的官方格式）
                if (!string.IsNullOrWhiteSpace(systemPrompt))
                    sb.Append("<|system|>\n").Append(systemPrompt).Append("<|end|>\n");
                foreach (var (role, text) in history)
                {
                    sb.Append("<|")
                      .Append(role == "assistant" ? "assistant" : "user")
                      .Append("|>\n").Append(text).Append("<|end|>\n");
                }
                sb.Append("<|assistant|>\n");
                break;
        }
        return sb.ToString();
    }
}
