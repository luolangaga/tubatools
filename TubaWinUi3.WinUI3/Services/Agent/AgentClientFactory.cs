using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using TubaWinUi3.Services.Ai;
using TubaWinUi3.Services.Ai.Onnx;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 创建 AI 助手的 IChatClient：选中「本地模型」提供商时返回进程内 ONNX 客户端，
/// 否则对接现有 OpenAI 兼容端点（AppSettings：AiApiEndpoint / AiModelName / AiApiKey）。
/// 使用官方 OpenAI SDK + M.E.AI 适配层（AsIChatClient），不手写 SSE 流式解析与 JSON Schema。
/// </summary>
public static class AgentClientFactory
{
    public static IChatClient CreateClient()
    {
        // 本地模型提供商：走进程内 ONNX 推理（无端点/Key）
        if (AiProviderStore.SelectedProvider.Kind == ProviderKind.Local)
            return LocalOnnxChatClient.Create();

        var (endpoint, model, apiKey) = AiService.GetConfig();

        if (!endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = "https://" + endpoint;
        }

        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint.TrimEnd('/'))
        });

        // 外层包一层思考链回传装饰器：DeepSeek 系思考模型要求 reasoning_content 原样回传
        return new ReasoningEchoChatClient(openAiClient.GetChatClient(model).AsIChatClient());
    }
}
