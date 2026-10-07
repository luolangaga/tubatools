using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using TubaWinUi3.Services.AiPlayground;

namespace TubaWinUi3.Services.Ai.Onnx;

/// <summary>本地模型解析出的一个工具调用。</summary>
public sealed class LocalToolCall
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>参数字典（未做 schema 校验，直接透传给工具）。</summary>
    public Dictionary<string, object?> Arguments { get; init; } = [];
}

/// <summary>
/// 把 M.E.AI 的消息列表改编成本地模型可用的提示词（纯逻辑，可单测，无 ORT 依赖）。
/// 本地模型不支持原生 function calling，因此：
/// - 工具定义（<see cref="AITool"/>）渲染成系统提示词里的 JSON 说明块；
/// - 历史里的工具调用 / 工具结果渲染成普通文本（模型只认文本轮次）。
/// </summary>
public static class LocalChatPrompt
{
    public const string ToolCallOpen = "<tool_call>";
    public const string ToolCallClose = "</tool_call>";

    /// <summary>把工具定义渲染成注入系统提示词末尾的说明块（无工具返回空串）。</summary>
    public static string BuildToolSchemaBlock(IEnumerable<AITool>? tools)
    {
        if (tools is null) return "";

        var sb = new StringBuilder();
        foreach (var tool in tools)
        {
            if (tool is not AIFunction fn) continue;
            var schema = fn.JsonSchema.ValueKind == JsonValueKind.Undefined
                ? "{}"
                : fn.JsonSchema.GetRawText();
            sb.Append("- ").Append(fn.Name).Append(": ")
              .Append(fn.Description ?? "").Append('\n')
              .Append("  参数(JSON Schema): ").Append(schema).Append('\n');
        }

        if (sb.Length == 0) return "";

        return "\n\n## 可用工具\n" +
               "需要调用工具时，只输出一行如下格式的 JSON（可连续多行，不要在代码块里）：\n" +
               ToolCallOpen + "{\"name\":\"工具名\",\"arguments\":{...}}" + ToolCallClose + "\n" +
               "不需要工具时直接用自然语言回答。工具列表：\n" + sb;
    }

    /// <summary>
    /// 构造本地提示词：抽取系统消息 → 与 <paramref name="systemPrompt"/> 合并 →
    /// 其余消息按角色渲染（assistant 的工具调用回放为 tool_call 文本，tool 结果回放为结果文本）。
    /// </summary>
    public static string Build(
        AiPromptFormat format,
        IEnumerable<ChatMessage> messages,
        IEnumerable<AITool>? tools = null)
    {
        var systemParts = new List<string>();
        var history = new List<(string Role, string Text)>();

        foreach (var m in messages)
        {
            if (m.Role == ChatRole.System)
            {
                var text = TextOf(m);
                if (!string.IsNullOrWhiteSpace(text)) systemParts.Add(text);
                continue;
            }

            if (m.Role == ChatRole.Tool)
            {
                history.Add(("user", RenderToolResults(m)));
                continue;
            }

            history.Add((m.Role == ChatRole.Assistant ? "assistant" : "user", RenderAssistantOrUser(m)));
        }

        var system = string.Join("\n\n", systemParts) + BuildToolSchemaBlock(tools);
        return LlmPromptBuilder.BuildPrompt(format, system.Trim(), history);
    }

    /// <summary>消息的纯文本（含 TextContent），用于系统提示词与普通轮次。</summary>
    public static string TextOf(ChatMessage m)
    {
        var sb = new StringBuilder();
        foreach (var c in m.Contents)
        {
            if (c is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                sb.Append(tc.Text);
        }
        if (sb.Length == 0 && !string.IsNullOrEmpty(m.Text))
            sb.Append(m.Text);
        return sb.ToString();
    }

    /// <summary>
    /// assistant 轮：文本 + 该轮发起的工具调用（回放为 tool_call 文本，让模型看到自己上次的调用）。
    /// </summary>
    private static string RenderAssistantOrUser(ChatMessage m)
    {
        var sb = new StringBuilder(TextOf(m));
        foreach (var fcc in m.Contents.OfType<FunctionCallContent>())
        {
            var args = fcc.Arguments is null
                ? "{}"
                : JsonSerializer.Serialize(fcc.Arguments);
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(ToolCallOpen)
              .Append("{\"name\":\"").Append(fcc.Name).Append("\",\"arguments\":").Append(args).Append('}')
              .Append(ToolCallClose);
        }
        return sb.ToString();
    }

    /// <summary>tool 轮：把函数结果渲染成模型可读文本（多个结果拼接）。</summary>
    private static string RenderToolResults(ChatMessage m)
    {
        var sb = new StringBuilder();
        foreach (var frc in m.Contents.OfType<FunctionResultContent>())
        {
            var result = frc.Result?.ToString() ?? "";
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("[工具结果] ").Append(result);
        }
        if (sb.Length == 0) return TextOf(m);
        return sb.ToString();
    }
}
