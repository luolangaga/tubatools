using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using TubaWinUi3.Services.AiPlayground;

namespace TubaWinUi3.Services.Ai.Onnx;

/// <summary>
/// 进程内本地 ONNX 模型的 <see cref="IChatClient"/> 实现：把 M.E.AI 消息改编成
/// 本地提示词（<see cref="LocalChatPrompt"/>）交给 <see cref="LocalModelRuntime"/> 流式生成，
/// 再把输出经 <see cref="ToolCallStreamParser"/> 还原成文本增量 + <see cref="FunctionCallContent"/>。
/// 本地模型无原生 function calling，工具调用为「尽力而为」（约定文本格式）。
/// </summary>
public sealed class LocalOnnxChatClient : IChatClient
{
    private readonly string _modelId;

    private LocalOnnxChatClient(string modelId) => _modelId = modelId;

    /// <summary>按当前选中的本地模型创建客户端（模型未选中时抛友好异常）。</summary>
    public static LocalOnnxChatClient Create()
    {
        var modelId = AiProviderStore.SelectedModelId;
        if (string.IsNullOrWhiteSpace(modelId))
            throw new InvalidOperationException(
                "「本地模型」提供商下还没有可用模型。请先在「本地 AI 试炼场」下载或导入一个对话模型。");
        return new LocalOnnxChatClient(modelId);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prompt = LocalChatPrompt.Build(
            LocalModelRuntime.GetPromptFormat(_modelId),
            messages,
            options?.Tools);

        // 模型加载可能很慢：放到后台线程，且只在模型变化时真正重载。
        await Task.Run(() => LocalModelRuntime.EnsureLoaded(_modelId), cancellationToken);

        var temperature = options?.Temperature ?? 0.4;
        var maxTokens = options?.MaxOutputTokens ?? 2048;

        var parser = new ToolCallStreamParser();

        await foreach (var piece in LocalModelRuntime.GenerateAsync(
                           prompt, temperature, 0.9, maxTokens, cancellationToken))
        {
            var parsed = parser.Append(piece);
            if (parsed.IsEmpty) continue;
            yield return Update(parsed);
        }

        var tail = parser.Flush();
        if (!tail.IsEmpty) yield return Update(tail);

        yield return new ChatResponseUpdate(ChatRole.Assistant, [])
        {
            FinishReason = ChatFinishReason.Stop,
        };
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var text = new System.Text.StringBuilder();
        var contents = new List<AIContent>();
        ChatFinishReason? finish = null;

        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (var c in update.Contents)
            {
                if (c is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                    text.Append(tc.Text);
                contents.Add(c);
            }
            if (update.FinishReason is { } fr) finish = fr;
        }

        var message = new ChatMessage(ChatRole.Assistant, contents);
        return new ChatResponse(message) { FinishReason = finish };
    }

    private static ChatResponseUpdate Update(ParseResult parsed)
    {
        var contents = new List<AIContent>();
        if (parsed.Text.Length > 0) contents.Add(new TextContent(parsed.Text));
        foreach (var call in parsed.Tools)
        {
            contents.Add(new FunctionCallContent(
                callId: call.Id,
                name: call.Name,
                arguments: call.Arguments));
        }
        return new ChatResponseUpdate(ChatRole.Assistant, contents);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is not null ? null
         : serviceType == typeof(LocalOnnxChatClient) ? this
         : null;

    public void Dispose() { }
}
