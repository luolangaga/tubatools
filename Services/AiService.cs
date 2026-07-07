using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TubaWinUi3.Services;

public sealed class AiToolCallItem
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Arguments { get; init; } = "";
}

public sealed class AiChatMessage
{
    public string Role { get; init; } = "";
    public string Content { get; init; } = "";
    public List<AiToolCallItem>? ToolCalls { get; init; }
    public string? ToolCallId { get; init; }
    public string? Name { get; init; }

    public static AiChatMessage System(string content) => new() { Role = "system", Content = content };
    public static AiChatMessage User(string content) => new() { Role = "user", Content = content };
    public static AiChatMessage Assistant(string content, List<AiToolCallItem>? toolCalls = null)
        => new() { Role = "assistant", Content = content, ToolCalls = toolCalls };
    public static AiChatMessage Tool(string toolCallId, string content, string? name = null)
        => new() { Role = "tool", Content = content, ToolCallId = toolCallId, Name = name };
}

public sealed class AiChatResponse
{
    public string Content { get; init; } = "";
    public List<AiToolCallItem>? ToolCalls { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
    public string? FinishReason { get; init; }
}

public sealed class AiToolDefinition
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string ParametersJson { get; init; } = "{}";
}

public static class AiService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly HttpClient _streamHttp = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static bool IsConfigured
    {
        get
        {
            var endpoint = AppSettings.Get("AiApiEndpoint");
            var model = AppSettings.Get("AiModelName");
            var key = AppSettings.Get("AiApiKey");
            return !string.IsNullOrWhiteSpace(endpoint)
                && !string.IsNullOrWhiteSpace(model)
                && !string.IsNullOrWhiteSpace(key);
        }
    }

    public static (string? Endpoint, string? Model, string? ApiKey) GetConfig()
    {
        return (
            AppSettings.Get("AiApiEndpoint"),
            AppSettings.Get("AiModelName"),
            AppSettings.Get("AiApiKey")
        );
    }

    public static void SetConfig(string? endpoint, string? model, string? apiKey)
    {
        if (endpoint is not null) AppSettings.Set("AiApiEndpoint", endpoint);
        if (model is not null) AppSettings.Set("AiModelName", model);
        if (apiKey is not null) AppSettings.Set("AiApiKey", apiKey);
    }

    public static async Task ChatStreamAsync(
        List<AiChatMessage> messages,
        Action<string> onChunk,
        Action<string>? onError = null,
        CancellationToken ct = default,
        double temperature = 0.3,
        int? maxTokens = null,
        List<AiToolDefinition>? tools = null,
        Action<int, string?, string?, string?>? onToolCallDelta = null)
    {
        var (endpoint, model, apiKey) = GetConfig();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(apiKey))
        {
            onError?.Invoke("AI 服务未配置，请在设置中配置 API 地址、模型名和 API Key");
            return;
        }

        var url = endpoint.TrimEnd('/') + "/chat/completions";

        var body = BuildRequestBody(messages, temperature, true, maxTokens, tools);

        var json = body.ToJsonString();

        HttpRequestMessage? request = null;
        HttpResponseMessage? response = null;

        try
        {
            request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            response = await _streamHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;

                if (!line.StartsWith("data: ", StringComparison.OrdinalIgnoreCase)) continue;
                var data = line.Substring(6).Trim();

                if (data == "[DONE]") break;

                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("choices", out var choices) &&
                        choices.GetArrayLength() > 0)
                    {
                        var choice = choices[0];
                        if (choice.TryGetProperty("delta", out var delta))
                        {
                            if (delta.TryGetProperty("content", out var contentProp))
                            {
                                var chunk = contentProp.GetString();
                                if (chunk is not null) onChunk(chunk);
                            }

                            if (onToolCallDelta is not null &&
                                delta.TryGetProperty("tool_calls", out var tcDelta) &&
                                tcDelta.GetArrayLength() > 0)
                            {
                                foreach (var tc in tcDelta.EnumerateArray())
                                {
                                    var index = tc.TryGetProperty("index", out var idxProp) ? idxProp.GetInt32() : 0;
                                    string? tcId = null;
                                    string? nameDelta = null;
                                    string? argsDelta = null;

                                    if (tc.TryGetProperty("id", out var idProp))
                                        tcId = idProp.GetString();

                                    if (tc.TryGetProperty("function", out var fnProp))
                                    {
                                        if (fnProp.TryGetProperty("name", out var nProp))
                                            nameDelta = nProp.GetString();
                                        if (fnProp.TryGetProperty("arguments", out var aProp))
                                            argsDelta = aProp.GetString();
                                    }

                                    onToolCallDelta(index, tcId, nameDelta, argsDelta);
                                }
                            }
                        }
                    }
                }
                catch (JsonException) { }
            }
        }
        catch (OperationCanceledException)
        {
            onError?.Invoke("已取消");
        }
        catch (HttpRequestException ex)
        {
            onError?.Invoke($"请求失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex.Message);
        }
        finally
        {
            response?.Dispose();
            request?.Dispose();
        }
    }

    public static async Task<AiChatResponse> ChatWithToolsAsync(
        List<AiChatMessage> messages,
        List<AiToolDefinition>? tools = null,
        CancellationToken ct = default,
        double temperature = 0.3,
        int? maxTokens = null)
    {
        var (endpoint, model, apiKey) = GetConfig();

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(apiKey))
            return new AiChatResponse { Success = false, Error = "AI 服务未配置" };

        var url = endpoint.TrimEnd('/') + "/chat/completions";
        var body = BuildRequestBody(messages, temperature, false, maxTokens, tools);

        var json = body.ToJsonString();

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await _http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var errMsg = TryExtractError(responseBody) ?? $"HTTP {(int)response.StatusCode}";
                return new AiChatResponse { Success = false, Error = errMsg };
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            var messageObj = root.GetProperty("choices")[0].GetProperty("message");
            var content = messageObj.TryGetProperty("content", out var cp) ? (cp.GetString() ?? "") : "";

            List<AiToolCallItem>? toolCalls = null;
            if (messageObj.TryGetProperty("tool_calls", out var tcProp) && tcProp.GetArrayLength() > 0)
            {
                toolCalls = new List<AiToolCallItem>();
                foreach (var tc in tcProp.EnumerateArray())
                {
                    var id = tc.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                    var fn = tc.TryGetProperty("function", out var fnProp) ? fnProp : default;
                    var name = fn.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                    var args = fn.TryGetProperty("arguments", out var argsProp) ? argsProp.GetString() ?? "" : "";
                    toolCalls.Add(new AiToolCallItem { Id = id, Name = name, Arguments = args });
                }
            }

            string? finishReason = null;
            if (root.GetProperty("choices")[0].TryGetProperty("finish_reason", out var frProp))
                finishReason = frProp.GetString();

            int? promptTokens = null, completionTokens = null;
            if (root.TryGetProperty("usage", out var usage))
            {
                promptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : null;
                completionTokens = usage.TryGetProperty("completion_tokens", out var ct2) ? ct2.GetInt32() : null;
            }

            return new AiChatResponse
            {
                Content = content,
                ToolCalls = toolCalls,
                Success = true,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                FinishReason = finishReason
            };
        }
        catch (OperationCanceledException)
        {
            return new AiChatResponse { Success = false, Error = "已取消" };
        }
        catch (Exception ex)
        {
            return new AiChatResponse { Success = false, Error = ex.Message };
        }
    }

    private static JsonObject BuildRequestBody(
        List<AiChatMessage> messages,
        double temperature,
        bool stream,
        int? maxTokens,
        List<AiToolDefinition>? tools)
    {
        var msgArr = new JsonArray();
        foreach (var m in messages)
        {
            var msgObj = new JsonObject { ["role"] = m.Role };

            if (!string.IsNullOrEmpty(m.Content))
                msgObj["content"] = m.Content;
            else if (m.Role == "assistant" && m.ToolCalls is not null)
                msgObj["content"] = "";
            else if (m.Role == "tool")
                msgObj["content"] = m.Content ?? "";

            if (m.ToolCalls is not null)
            {
                var tcArr = new JsonArray();
                foreach (var tc in m.ToolCalls)
                {
                    tcArr.Add(new JsonObject
                    {
                        ["id"] = tc.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = tc.Name,
                            ["arguments"] = tc.Arguments
                        }
                    });
                }
                msgObj["tool_calls"] = tcArr;
            }

            if (!string.IsNullOrEmpty(m.ToolCallId))
                msgObj["tool_call_id"] = m.ToolCallId;

            if (!string.IsNullOrEmpty(m.Name))
                msgObj["name"] = m.Name;

            msgArr.Add(msgObj);
        }

        var body = new JsonObject
        {
            ["model"] = AppSettings.Get("AiModelName") ?? "",
            ["messages"] = msgArr,
            ["temperature"] = temperature,
            ["stream"] = stream
        };

        if (maxTokens.HasValue)
            body["max_tokens"] = maxTokens.Value;

        if (tools is not null && tools.Count > 0)
        {
            var toolsArr = new JsonArray();
            foreach (var t in tools)
            {
                var paramNode = JsonNode.Parse(t.ParametersJson);
                toolsArr.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = paramNode
                    }
                });
            }
            body["tools"] = toolsArr;
        }

        return body;
    }

    public static async Task<AiChatResponse> ChatSingleAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default,
        double temperature = 0.3,
        int? maxTokens = null)
    {
        return await ChatWithToolsAsync(
        [
            AiChatMessage.System(systemPrompt),
            AiChatMessage.User(userMessage)
        ], tools: null, ct: ct, temperature: temperature, maxTokens: maxTokens);
    }

    public static async Task<AiChatResponse> TestConnectionAsync(CancellationToken ct = default)
    {
        return await ChatSingleAsync(
            "You are a helpful assistant. Reply with exactly: OK",
            "Hello, please confirm you are working.",
            ct,
            temperature: 0,
            maxTokens: 10);
    }

    private static string? TryExtractError(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("message", out var msg))
                    return msg.GetString();
                return err.ToString();
            }
        }
        catch { }
        return null;
    }
}
