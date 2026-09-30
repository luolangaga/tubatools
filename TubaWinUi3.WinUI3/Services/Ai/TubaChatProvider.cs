using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;
using OpenAI;
using OpenAI.Chat;
using TubaWinUi3.Services.Agent;

using FcChatMessage = FieldCure.Ai.Providers.Models.ChatMessage;

namespace TubaWinUi3.Services.Ai;

/// <summary>
/// ChatPanel 的 <see cref="IAiProvider"/> 适配器：把现有 OpenAI 兼容端点（DeepSeek 等，
/// AppSettings：AiApiEndpoint / AiModelName / AiApiKey）接入 FieldCure AssistStudio ChatPanel。
///
/// 职责：
/// - 请求：AiRequest（FieldCure 消息 + 系统提示词 + IAssistTool[]）→ OpenAI SDK Chat 请求；
///   带思考链的 assistant 消息通过 JsonPatch 原样回传 reasoning_content（DeepSeek 网关要求，
///   与 ReasoningEchoChatClient 同一技巧）；
/// - 响应：流式更新 → <see cref="StreamEvent"/>（TextDelta / ThinkingDelta / ToolCallStart+Delta
///   / Usage / StreamCompleted），供 ChatPanel 渲染流式文本、思考块与内联工具调用；
/// - 统计：每次请求完成通过 <see cref="UsageReported"/> 静态事件上报 token 用量，页面据此累计
///   会话级消耗。工具本身不在此执行（由 ChatPanel 的 ToolCallExecutor 调用 IAssistTool）。
/// </summary>
public sealed class TubaChatProvider : IAiProvider
{
    /// <summary>单轮请求硬超时（防端点挂起导致界面"卡死"，与 AgentRuntime 一致）。</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(120);

    /// <summary>工具调用循环默认温度（与旧 AgentRuntime.DefaultTemperature 保持一致，保证调用稳定）。</summary>
    private const float DefaultTemperature = 0.4f;

    /// <summary>
    /// 思维链最大长度（字符）：思考模型（DeepSeek 系）单轮 reasoning_content 超过该长度即截断。
    /// 思维链随工具循环逐轮累积，若不设上限会无限撑爆上下文——思考文本越长，模型越容易
    /// "过度确认"、在工具循环里越绕越深直至死循环（用户反馈"思维链死循环"）。
    /// 截断后字段仍非空，满足 DeepSeek 网关 "reasoning_content must be passed back" 要求
    /// （网关校验字段存在，不逐字比对）。
    /// </summary>
    internal const int MaxThinkingChars = 6000;

    /// <summary>
    /// 历史回传总量预算（字符，不含 system）：协议消息重建后若超过该预算，
    /// 从最旧消息开始丢弃（保留首条 system 与最新 user）。30 轮工具循环中每轮回传
    /// 思考(≤6000)+结果(≤6000)，历史无上限会撑爆 DeepSeek 64K 上下文窗口——
    /// 这是死循环"烧穿上下文"的路径；预算只在超限时生效，正常会话完全无感知。
    /// </summary>
    internal const int HistoryBudgetChars = 40000;

    /// <summary>
    /// 截断思维链到 <see cref="MaxThinkingChars"/> 并追加截断标记；null/短文本原样返回。
    /// 保留开头（推理关键段在前），避免切断 surrogate pair（emoji 等）产生孤立代理字符，
    /// 防止 JSON 序列化异常或回传乱码。
    /// </summary>
    internal static string? TruncateThinking(string? thinking)
    {
        if (string.IsNullOrEmpty(thinking) || thinking.Length <= MaxThinkingChars) return thinking;
        var cut = thinking[..MaxThinkingChars];
        if (cut.Length > 0 && char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
        return cut + "\n\n" + AgentRuntimeLimits.ReasoningTruncatedMarker;
    }

    /// <summary>每轮完成（含取消前已拿到的用量）上报，页面据此累计会话 token 统计。</summary>
    public static event Action<TokenUsage?>? UsageReported;

    /// <summary>流请求失败（网络/超时/服务端错误）时触发；用户主动停止不触发。供 UI 联动（如 bot 头像 alert 态）。</summary>
    public static event Action<string>? RequestFailed;

    private TokenUsage? _lastUsage;
    private bool _isTruncated;
    private string? _lastRequestBody;
    private string? _lastRawResponse;

    public string ProviderName => "图吧AI助手";

    public string ModelId
    {
        get
        {
            var (_, model, _) = AiService.GetConfig();
            return model;
        }
    }

    public TokenUsage? LastUsage => _lastUsage;
    public bool IsTruncated => _isTruncated;
    public string? LastRequestBody => _lastRequestBody;
    public string? LastRawResponse => _lastRawResponse;

    /// <summary>附件已禁用（AllowAttachments=false），PDF 能力声明仅作占位。</summary>
    public PdfCapability PdfCapability => PdfCapability.TextExtraction;
    public AudioCapability AudioCapability => AudioCapability.NotSupported;
    public ToolCallingSupport ToolCallingSupport => ToolCallingSupport.Supported;
    public ThinkingSupport GetThinkingSupport(string modelId) => ThinkingSupport.Optional;

    // ---------- 流式（ChatPanel 主路径） ----------

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        AiRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chat = CreateChatClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RequestTimeout);

        // 生产者：拉取 OpenAI 流并灌入 Channel（迭代器内不能 yield+try/catch 并存，
        // 异常统一走 Channel 完成传播，含超时转译）
        var channel = Channel.CreateUnbounded<StreamEvent>();
        var producer = Task.Run(async () =>
        {
            try
            {
                await foreach (var ev in StreamCoreAsync(chat, request, timeoutCts.Token))
                    channel.Writer.TryWrite(ev);
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                RequestFailed?.Invoke("AI 服务响应超时（120 秒）");
                channel.Writer.TryComplete(new TimeoutException("AI 服务响应超时（120 秒），请重试。"));
            }
            catch (Exception ex)
            {
                LogStreamFailure(request, ex);
                // 用户主动停止（ct 已取消）不算失败，不触发 alert
                if (!ct.IsCancellationRequested) RequestFailed?.Invoke(ex.Message);
                channel.Writer.TryComplete(ex);
            }
        }, ct);

        await foreach (var ev in channel.Reader.ReadAllAsync(ct))
            yield return ev;
        await producer; // 异常（含超时）在此抛出，ChatPanel 会以错误文案展示
    }

    /// <summary>请求失败诊断：完整序列化请求体 + 消息形状摘要写入 agent-debug.log。</summary>
    private static void LogStreamFailure(AiRequest request, Exception ex)
    {
        try
        {
            var summary = request.Messages is null
                ? "(null)"
                : string.Join(" | ", request.Messages.Select(m =>
                    $"{m.Role}:{(m.Content?.Length ?? 0)}c/{(string.IsNullOrWhiteSpace(m.ThinkingContent) ? "无思考" : m.ThinkingContent!.Length + "t思考")}/{(m.ToolCalls?.Count ?? 0)}工具"));
            AgentDebugLog.Error(
                $"[ChatProvider] 请求失败 {ex.GetType().Name}: {ex.Message}\n消息形状：{summary}",
                null);

            var body = string.Join("\n", BuildMessages(request)
                .Select(m => System.ClientModel.Primitives.ModelReaderWriter.Write(m).ToString()));
            const int maxBodyChars = 8000;
            if (body.Length > maxBodyChars)
            {
                var cut = maxBodyChars;
                if (char.IsHighSurrogate(body[cut - 1])) cut--; // 不切断代理对
                body = body[..cut] + "…（已截断）";
            }
            AgentDebugLog.Info($"[ChatProvider] 失败请求完整体（{request.Messages?.Count ?? 0} 条树消息 → {BuildMessages(request).Count} 条协议消息）：\n{body}");
        }
        catch (Exception logEx)
        {
            AgentDebugLog.Error("[ChatProvider] 失败诊断自身异常", logEx);
        }
    }

    /// <summary>核心拉流：OpenAI 流式更新 → StreamEvent（无 try/catch，异常向上传播）。</summary>
    private async IAsyncEnumerable<StreamEvent> StreamCoreAsync(
        ChatClient chat,
        AiRequest request,
        [EnumeratorCancellation] CancellationToken streamToken)
    {
        var messages = BuildMessages(request);

        // 无进展检测（死循环最后手段）：连续多轮纯工具调用未产出回答 → 向 system 消息注入
        // 终止指令，催促模型立即总结停止；消息每轮从请求重建，注入不会残留进消息树
        if (AgentToolLoopGuard.ShouldInjectStopDirective)
        {
            const string stopDirective =
                "\n\n【系统检测】已连续多轮仅调用工具而未产出回答，疑似陷入循环。请立即停止调用新工具，直接基于已有信息给出最终回答；如确需更多信息，先向用户说明。";
            if (messages.Count > 0 && messages[0] is SystemChatMessage sys
                && sys.Content.Count > 0 && sys.Content[0].Text is { } sysText)
            {
                messages[0] = OpenAI.Chat.ChatMessage.CreateSystemMessage(sysText + stopDirective);
            }
            else
            {
                messages.Insert(0, OpenAI.Chat.ChatMessage.CreateSystemMessage(stopDirective.TrimStart()));
            }
        }

        var options = BuildOptions(request);
        _lastRequestBody = $"messages={messages.Count}, tools={request.Tools?.Count ?? 0}, model={ModelId}";
        _lastRawResponse = null;

        var toolCalls = new Dictionary<int, StreamedToolCall>();
        TokenUsage? usage = null;
        var isTruncated = false;
        // 思维链长度护栏：当前轮已转发的 reasoning 字符数与是否已发出截断提示
        var thinkingLength = 0;
        var thinkingTruncated = false;
        // 无进展检测：本轮是否产出过用户可见文本 / 工具调用（供流结束后 ReportRound 上报）
        var hasUserText = false;
        var hasToolCalls = false;

        await foreach (var update in chat.CompleteChatStreamingAsync(messages, options, streamToken))
        {
            // 文本增量
            if (update.ContentUpdate is { } content)
            {
                foreach (var part in content)
                {
                    if (part.Kind == ChatMessageContentPartKind.Text && part.Text is { Length: > 0 })
                    {
                        hasUserText = true;
                        yield return new StreamEvent.TextDelta(part.Text);
                    }
                }
            }

            // 思考链增量（DeepSeek 系 reasoning_content，与 M.E.AI 同款 JsonPatch 提取）
            // 思维链最大长度护栏：单轮累计超过 MaxThinkingChars 后丢弃后续增量——
            // 截断发生在转发前，UI 渲染 / 消息树存储 / 网关回传三处一致，防止思考文本
            // 无限膨胀撑爆上下文、模型"过度确认"陷入工具循环死循环。
#pragma warning disable SCME0001 // JsonPatch 为评估 API，但这是 SDK 唯一支持扩展字段（reasoning_content）的途径
            if (update.Patch.TryGetValue("$.choices[0].delta.reasoning_content"u8, out string? reasoning)
                && !string.IsNullOrEmpty(reasoning))
#pragma warning restore SCME0001
            {
                var room = MaxThinkingChars - thinkingLength;
                if (room > 0)
                {
                    var chunk = reasoning.Length <= room ? reasoning : reasoning[..room];
                    // 防切断 surrogate pair（emoji 等）产生孤立代理字符
                    if (chunk.Length > 0 && char.IsHighSurrogate(chunk[^1])) chunk = chunk[..^1];
                    thinkingLength += chunk.Length;
                    yield return new StreamEvent.ThinkingDelta(chunk);
                }
                else if (!thinkingTruncated)
                {
                    thinkingTruncated = true;
                    yield return new StreamEvent.ThinkingDelta("\n\n" + AgentRuntimeLimits.ReasoningTruncatedMarker);
                }
            }

            // 工具调用增量：OpenAI SDK 按 Index 下发 ToolCallId/函数名/参数片段
            if (update.ToolCallUpdates is { } toolUpdates)
            {
                foreach (var tcu in toolUpdates)
                {
                    if (!toolCalls.TryGetValue(tcu.Index, out var call))
                        toolCalls[tcu.Index] = call = new StreamedToolCall { Index = tcu.Index };

                    if (!string.IsNullOrEmpty(tcu.ToolCallId)) call.Id = tcu.ToolCallId;
                    if (tcu.FunctionName is { Length: > 0 }) call.Name.Append(tcu.FunctionName);

                    // Id 首次齐备 → 补发 ToolCallStart 及此前缓存的参数片段
                    if (!call.StartEmitted && call.Id is { Length: > 0 })
                    {
                        call.StartEmitted = true;
                        var id = call.Id;
                        hasToolCalls = true;
                        yield return new StreamEvent.ToolCallStart(id, call.Name.ToString(), null);
                        foreach (var chunk in call.ArgChunks)
                            yield return new StreamEvent.ToolCallDelta(id, chunk);
                    }

                    var argsChunk = tcu.FunctionArgumentsUpdate?.ToString() ?? "";
                    if (argsChunk.Length > 0)
                    {
                        if (call.StartEmitted)
                            yield return new StreamEvent.ToolCallDelta(call.Id!, argsChunk);
                        else
                            call.ArgChunks.Add(argsChunk);
                    }
                }
            }

            // token 用量（流式末尾由网关携带）
            if (update.Usage is { } u)
                usage = ToTokenUsage(u);

            if (update.FinishReason is { } finish)
                isTruncated = finish == ChatFinishReason.Length;
        }

        // 流结束：补发始终未带 Id 的工具调用（Id 在此补生成，参数片段缓存在 ArgChunks 中）
        foreach (var call in toolCalls.Values)
        {
            if (call.StartEmitted) continue;
            call.Id ??= $"tba_{call.Index}";
            call.StartEmitted = true;
            hasToolCalls = true;
            yield return new StreamEvent.ToolCallStart(call.Id, call.Name.ToString(), null);
            foreach (var chunk in call.ArgChunks)
                yield return new StreamEvent.ToolCallDelta(call.Id, chunk);
        }

        // 无进展检测：上报本轮进展（有文本 → 清零；纯工具轮 → 递增，达阈值后注入终止指令）
        AgentToolLoopGuard.ReportRound(hasUserText, hasToolCalls);

        _lastUsage = usage;
        _isTruncated = isTruncated;
        UsageReported?.Invoke(usage);
        yield return new StreamEvent.StreamCompleted(isTruncated);
    }

    // ---------- 非流式（接口要求，ChatPanel 内部只用 StreamAsync） ----------

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        var chat = CreateChatClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RequestTimeout);

        var messages = BuildMessages(request);
        var options = BuildOptions(request);
        ChatCompletion completion;
        try
        {
            completion = await chat.CompleteChatAsync(messages, options, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("AI 服务响应超时（120 秒），请重试。");
        }

        var toolCalls = new List<ToolCall>();
        foreach (var tc in completion.ToolCalls ?? Array.Empty<ChatToolCall>())
        {
            toolCalls.Add(new ToolCall
            {
                Id = tc.Id,
                FunctionName = tc.FunctionName,
                Arguments = tc.FunctionArguments.ToString()
            });
        }

#pragma warning disable SCME0001 // JsonPatch 为评估 API，但这是 SDK 唯一支持扩展字段（reasoning_content）的途径
        completion.Patch.TryGetValue("$.choices[0].message.reasoning_content"u8, out string? thinking);
#pragma warning restore SCME0001

        var text = string.Join("", completion.Content.Select(p => p.Text).Where(t => t is not null));
        var usage = ToTokenUsage(completion.Usage);
        var isTruncated = completion.FinishReason == ChatFinishReason.Length;

        _lastUsage = usage;
        _isTruncated = isTruncated;
        UsageReported?.Invoke(usage);

        return new AiResponse
        {
            Content = text,
            ToolCalls = toolCalls,
            Usage = usage,
            IsTruncated = isTruncated,
            ThinkingContent = TruncateThinking(thinking)
        };
    }

    // ---------- 模型 / 连接（按需最小实现） ----------

    public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        var provider = AiProviderStore.SelectedProvider;
        IReadOnlyList<AiModel> models = provider.Models
            .Select(m => new AiModel(m.Id, m.DisplayText, null))
            .ToList();
        return Task.FromResult(models);
    }

    public Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default)
    {
        var (_, _, apiKey) = AiService.GetConfig();
        var valid = !string.IsNullOrWhiteSpace(apiKey);
        return Task.FromResult(new ConnectionInfo(valid, null, null, valid ? null : "未配置 API Key"));
    }

    // ---------- 内部 ----------

    /// <summary>每次请求新建客户端，保证端点/模型/Key 变更即时生效（与旧引擎每次运行创建一致）。</summary>
    private static ChatClient CreateChatClient()
    {
        var (endpoint, model, apiKey) = AiService.GetConfig();
        if (!endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = "https://" + endpoint;
        }
        var openAi = new OpenAIClient(
            new System.ClientModel.ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint.TrimEnd('/')),
                // 传输层错误（含 DNS 解析失败）最多重试 2 次：默认 3 次在无网络/DNS
                // 故障时拖慢失败反馈且无意义；ChatPanel 侧还有 120 秒兜底超时
                RetryPolicy = new System.ClientModel.Primitives.ClientRetryPolicy(maxRetries: 2),
            });
        return openAi.GetChatClient(model);
    }

    private static TokenUsage? ToTokenUsage(ChatTokenUsage? usage)
    {
        if (usage is null) return null;
        return new TokenUsage(usage.InputTokenCount, usage.OutputTokenCount)
        {
            // DeepSeek 网关把缓存命中放在 prompt_tokens_details.cached_tokens（语义上属于"读缓存"）
            CacheReadInputTokens = usage.InputTokenDetails?.CachedTokenCount
        };
    }

    private static ChatCompletionOptions BuildOptions(AiRequest request)
    {
        // 不设 MaxOutputTokens：端点默认即可（旧引擎亦不截断长回答）
        var options = new ChatCompletionOptions { Temperature = DefaultTemperature };
        if (request.Tools is { Count: > 0 })
        {
            foreach (var t in request.Tools)
            {
                options.Tools.Add(ChatTool.CreateFunctionTool(
                    t.Name,
                    t.Description,
                    BinaryData.FromString(t.ParameterSchema)));
            }
        }
        return options;
    }

    /// <summary>
    /// 把 ChatPanel 的渲染树重建为协议消息历史。
    ///
    /// 背景：ChatPanel 的消息树是渲染结构——根气泡累积整轮全部文本与思考，
    /// 每个工具轮次另有一条「包装消息」（当轮文本 + ToolCalls，不带思考）。
    /// 但 DeepSeek 思考模式要求历史与 API 协议一致：每条 assistant 消息 =
    /// 当轮文本 + 工具调用 + 它自己的 reasoning_content（否则网关 400
    /// "The reasoning_content in the thinking mode must be passed back"）。
    ///
    /// 重建规则（等价于旧 AgentSession 引擎的每轮一条消息形状，已在生产验证）：
    /// - 工具轮次包装消息 → 原样成为协议消息（文本 + 工具 + 思考回传）；
    /// - 根气泡的累积文本 = 各轮文本顺序拼接 → 按包装消息内容逐段剥离，
    ///   剩余文本（纯文本结束轮）在轮次边界（用户/系统消息）前另成一条协议消息；
    /// - 思考链只累积在根气泡上 → 未显式携带思考的 assistant 消息用最近根气泡的思考补齐。
    /// </summary>
    internal static List<OpenAI.Chat.ChatMessage> BuildMessages(AiRequest request)
    {
        var list = new List<OpenAI.Chat.ChatMessage>();
        string? pendingThinking = null;
        var remainingRootText = new StringBuilder();
        var hasPendingRoot = false;

        void FlushPendingRoot()
        {
            if (!hasPendingRoot) return;
            hasPendingRoot = false;
            var text = remainingRootText.ToString();
            remainingRootText.Clear();
            if (!string.IsNullOrWhiteSpace(text))
                list.Add(ToAssistantMessage(new FcChatMessage(ChatRole.Assistant, text), pendingThinking));
        }

        foreach (var m in request.Messages ?? Array.Empty<FcChatMessage>())
        {
            switch (m.Role)
            {
                case ChatRole.User:
                    FlushPendingRoot();
                    list.Add(OpenAI.Chat.ChatMessage.CreateUserMessage(m.Content ?? ""));
                    break;

                case ChatRole.System:
                    FlushPendingRoot();
                    list.Add(OpenAI.Chat.ChatMessage.CreateSystemMessage(m.Content ?? ""));
                    break;

                case ChatRole.Tool:
                    list.Add(OpenAI.Chat.ChatMessage.CreateToolMessage(m.ToolCallId ?? "", m.Content ?? ""));
                    break;

                case ChatRole.Assistant:
                    if (m.ToolCalls is { Count: > 0 })
                    {
                        // 工具轮次包装消息：逐条成为协议消息
                        list.Add(ToAssistantMessage(m, pendingThinking));
                        // 从根气泡累积文本中剥离当轮文本（根 = 各轮文本顺序拼接，包装消息是其前缀段）
                        if (hasPendingRoot && !string.IsNullOrEmpty(m.Content))
                        {
                            var accumulated = remainingRootText.ToString();
                            if (accumulated.StartsWith(m.Content, StringComparison.Ordinal))
                                remainingRootText.Remove(0, m.Content.Length);
                        }
                    }
                    else
                    {
                        // 根气泡：记录待剥离的累积文本与思考（同一对象后续轮次继续累积）
                        hasPendingRoot = true;
                        remainingRootText.Append(m.Content ?? "");
                        if (!string.IsNullOrWhiteSpace(m.ThinkingContent))
                            pendingThinking = m.ThinkingContent;
                    }
                    break;
            }
        }
        FlushPendingRoot();

        var prompt = request.SystemPrompt;
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            // 树内首个消息本身是 system（恢复的会话）→ 合并避免网关拒绝多个 system
            if (list.Count > 0 && list[0] is SystemChatMessage s0 && s0.Content.Count > 0 && s0.Content[0].Text is { } t)
                list[0] = OpenAI.Chat.ChatMessage.CreateSystemMessage(t + "\n\n" + prompt);
            else
                list.Insert(0, OpenAI.Chat.ChatMessage.CreateSystemMessage(prompt));
        }
        return TrimHistory(list);
    }

    /// <summary>
    /// 历史预算压缩：协议消息总量（不含 system）超过 <see cref="HistoryBudgetChars"/> 时
    /// 从最旧开始丢弃，保留首条 system 与最新一条 user（模型需知道系统指令与当前任务）。
    /// 仅在超限时生效；删除最旧的历史不会破坏 DeepSeek 网关的 reasoning 回传校验
    /// （该校验只作用于当前请求中实际存在的 assistant 消息）。
    /// </summary>
    internal static List<OpenAI.Chat.ChatMessage> TrimHistory(List<OpenAI.Chat.ChatMessage> list)
    {
        if (list.Count <= 2) return list;

        // 保留集：首条 system + 最新 user
        var keep = new HashSet<int>();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is SystemChatMessage) { keep.Add(i); break; }
        }
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] is UserChatMessage) { keep.Add(i); break; }
        }

        var total = 0L;
        foreach (var m in list)
        {
            if (m is not SystemChatMessage) total += RoughLength(m);
        }
        if (total <= HistoryBudgetChars) return list;

        // 从最旧的非保留消息开始丢弃，直到达标（删光非保留项为止）
        var drop = new HashSet<int>();
        for (var i = 0; i < list.Count && total > HistoryBudgetChars; i++)
        {
            if (keep.Contains(i)) continue;
            total -= RoughLength(list[i]);
            drop.Add(i);
        }

        var result = new List<OpenAI.Chat.ChatMessage>(list.Count - drop.Count);
        for (var i = 0; i < list.Count; i++)
        {
            if (!drop.Contains(i)) result.Add(list[i]);
        }
        return result;
    }

    /// <summary>协议消息字符量估算：文本 + 工具调用参数 + 思考链（reasoning_content）。</summary>
    internal static int RoughLength(OpenAI.Chat.ChatMessage m)
    {
        switch (m)
        {
            case SystemChatMessage sm:
                return sm.Content.Sum(p => p.Text?.Length ?? 0);
            case UserChatMessage um:
                return um.Content.Sum(p => p.Text?.Length ?? 0);
            case AssistantChatMessage am:
            {
                var len = am.Content.Sum(p => p.Text?.Length ?? 0);
                foreach (var tc in am.ToolCalls)
                    len += tc.FunctionArguments?.ToString()?.Length ?? 0;
#pragma warning disable SCME0001 // JsonPatch 为评估 API，但这是 SDK 唯一支持扩展字段（reasoning_content）的途径
                if (am.Patch.TryGetValue("$.reasoning_content"u8, out string? r))
                    len += r.Length;
#pragma warning restore SCME0001
                return len;
            }
            case ToolChatMessage tm:
                return tm.Content.Sum(p => p.Text?.Length ?? 0);
            default:
                return 0;
        }
    }

    internal static OpenAI.Chat.ChatMessage ToOpenAiMessage(FcChatMessage m, string? fallbackThinking = null) => m.Role switch
    {
        ChatRole.User => OpenAI.Chat.ChatMessage.CreateUserMessage(m.Content ?? ""),
        ChatRole.System => OpenAI.Chat.ChatMessage.CreateSystemMessage(m.Content ?? ""),
        ChatRole.Tool => OpenAI.Chat.ChatMessage.CreateToolMessage(m.ToolCallId ?? "", m.Content ?? ""),
        _ => ToAssistantMessage(m, fallbackThinking),
    };

    /// <summary>assistant 消息：文本 + 工具调用 + reasoning_content 原样回传。</summary>
    /// <param name="fallbackThinking">思考链缺失时（工具轮次包装消息）使用的补齐值，来自最近一个含思考的根气泡。</param>
    internal static OpenAI.Chat.ChatMessage ToAssistantMessage(FcChatMessage m, string? fallbackThinking = null)
    {
        var parts = new List<ChatMessageContentPart>();
        if (!string.IsNullOrEmpty(m.Content))
            parts.Add(ChatMessageContentPart.CreateTextPart(m.Content));

        // 纯工具调用轮次的 assistant 消息 Content 为空（多轮循环中作为历史回传），
        // 但 OpenAI SDK 的 AssistantChatMessage 要求至少一个 content part；
        // 补空文本 part（与 ReasoningEchoChatClient 同款处理，网关接受空 content）。
        if (parts.Count == 0)
            parts.Add(ChatMessageContentPart.CreateTextPart(""));

        var assistant = new AssistantChatMessage(parts);
        if (m.ToolCalls is { Count: > 0 })
        {
            foreach (var tc in m.ToolCalls)
            {
                assistant.ToolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                    tc.Id,
                    tc.FunctionName,
                    BinaryData.FromString(string.IsNullOrWhiteSpace(tc.Arguments) ? "{}" : tc.Arguments)));
            }
        }

        // DeepSeek 思考模型要求 reasoning 原样回传（JsonPatch 是 SDK 扩展字段唯一途径）；
        // 历史恢复的 thinking 未走流式护栏可能超长，回传前防御性截断，保证请求体体积受控
        var thinking = TruncateThinking(
            !string.IsNullOrWhiteSpace(m.ThinkingContent) ? m.ThinkingContent : fallbackThinking);
        if (!string.IsNullOrWhiteSpace(thinking))
        {
#pragma warning disable SCME0001 // JsonPatch 为评估 API，但这是 SDK 唯一支持扩展字段（reasoning_content）的途径
            assistant.Patch.Set("$.reasoning_content"u8, thinking);
#pragma warning restore SCME0001
        }
        return assistant;
    }

    /// <summary>流式工具调用累积槽（按 Index 分组）。</summary>
    private sealed class StreamedToolCall
    {
        public int Index;
        public string? Id;
        public StringBuilder Name { get; } = new();
        public List<string> ArgChunks { get; } = [];
        public bool StartEmitted;
    }
}