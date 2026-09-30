using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using TubaWinUi3.Services.Ai;

namespace TubaWinUi3.Services.Agent;

/// <summary>Agent 循环的事件回调（在工作线程触发，UI 自行 marshal 到 UI 线程）。</summary>
public sealed class AgentRunCallbacks
{
    /// <summary>流式文本增量。</summary>
    public Action<string>? OnTextChunk { get; init; }

    /// <summary>流式思维过程增量，与最终回答分开呈现。</summary>
    public Action<string>? OnReasoningChunk { get; init; }

    /// <summary>步骤开始（含等待确认）。</summary>
    public Action<AgentStep>? OnStepStarted { get; init; }

    /// <summary>步骤结束（成功/失败/拒绝/取消）。</summary>
    public Action<AgentStep>? OnStepCompleted { get; init; }

    /// <summary>需要用户确认（循环暂停，等待 ResumeLoopAsync）。</summary>
    public Action<IReadOnlyList<AgentConfirmationRequest>>? OnConfirmationsRequested { get; init; }

    /// <summary>致命错误（连接失败/轮次上限）。</summary>
    public Action<string>? OnError { get; init; }

    /// <summary>正常结束。</summary>
    public Action<string>? OnCompleted { get; init; }

    /// <summary>本轮 LLM 调用的 token 消耗（调用方自行累加）。</summary>
    public Action<AgentUsage>? OnUsage { get; init; }

    /// <summary>一轮开始（会话据此重置步骤组统计）。</summary>
    public Action? OnRoundStarted { get; init; }

    /// <summary>一轮工具链执行完毕且无待确认（会话据此结算步骤组，UI 折叠步骤链）。</summary>
    public Action? OnRoundCompleted { get; init; }
}

/// <summary>一次 LLM 调用的 token 消耗。</summary>
public sealed class AgentUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    /// <summary>缓存命中 token。流式经 OpenAI SDK 时仅当上游返回标准
    /// prompt_tokens_details.cached_tokens（网关已做规范化）才有值；直连 DeepSeek 时为 null。</summary>
    public int? CacheHitTokens { get; init; }
    public int? CacheMissTokens { get; init; }
    public int TotalTokens => PromptTokens + CompletionTokens;
}

/// <summary>
/// 多轮 Agent 循环：流式生成 → 解析函数调用 → 执行普通工具 / 暂停等待确认 →
/// 结果回填 → 下一轮。支持规划（create_plan）、上下文记忆与错误恢复。
/// </summary>
public static class AgentRuntime
{
    public const int DefaultMaxRounds = AgentRuntimeLimits.DefaultMaxRounds;
    public const int ContinueMaxRounds = AgentRuntimeLimits.ContinueMaxRounds;
    public const float DefaultTemperature = AgentRuntimeLimits.DefaultTemperature;

    /// <summary>
    /// 运行 Agent 循环。返回 true = 本轮完成；返回 false = 已暂停等待用户确认
    /// （确认结果通过 <see cref="ResumeLoopAsync"/> 应用后继续）。
    /// </summary>
    public static async Task<bool> RunLoopAsync(
        List<ChatMessage> history,
        AgentRunCallbacks cb,
        CancellationToken ct,
        int maxRounds = DefaultMaxRounds,
        IChatClient? clientOverride = null)
    {
        using IChatClient client = clientOverride ?? AgentClientFactory.CreateClient();

        for (var round = 0; round < maxRounds; round++)
        {
            ct.ThrowIfCancellationRequested();

            // 历史预算压缩：协议回传总量超限时从最旧丢弃（保留首条 system），
            // 防 30 轮工具循环回传（思考+结果）撑爆 64K 上下文——死循环"烧穿上下文"的路径。
            TrimHistory(history);

            // 无进展检测：连续多轮纯工具调用（无用户可见文本）视为疑似死循环，
            // 直接终止（而非注入指令——旧引擎循环自有，无需再等模型"自觉"）。
            // 计数由每轮末尾 AgentToolLoopGuard.ReportRound 维护。
            if (AgentToolLoopGuard.ShouldInjectStopDirective)
            {
                AgentDebugLog.Info("连续纯工具轮达阈值，终止循环");
                cb.OnError?.Invoke("检测到连续多轮仅调用工具而未产出回复，疑似陷入循环，已自动停止。请简化指令或补充说明后重试。");
                return true;
            }

            cb.OnRoundStarted?.Invoke();
            AgentDebugLog.Info($"第 {round + 1}/{maxRounds} 轮开始，历史 {history.Count} 条");

            var options = new ChatOptions
            {
                Temperature = DefaultTemperature,
                Tools = AgentToolRegistry.Tools.Select(t => t.Function).Cast<AITool>().ToList()
            };

            var fullText = new StringBuilder();
            var reasoningSb = new StringBuilder();
            // 思考链最大长度护栏（与新引擎 TubaChatProvider.MaxThinkingChars 一致）：
            // 单轮思考体积无上限是死循环放大环节之一（思考越滚越长 → 上下文越满 →
            // 模型越绕越深），逐段累积时即限流，回填历史时 TruncateThinking 双保险。
            var reasoningRoom = AgentRuntimeLimits.MaxReasoningChars;
            var callContents = new List<FunctionCallContent>();
            AgentUsage? usage = null;

            try
            {
                // 单轮请求硬超时（防端点挂起导致界面"卡死"）
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                requestTimeout.CancelAfter(AgentRuntimeLimits.RequestTimeout);

                // 传输层瞬时错误：指数退避重试（2s / 4s / 8s）
                await AgentErrorPolicy.WithRetryAsync(async innerCt =>
                {
                    await foreach (var update in client.GetStreamingResponseAsync(history, options, innerCt))
                    {
                        if (!string.IsNullOrEmpty(update.Text))
                        {
                            fullText.Append(update.Text);
                            cb.OnTextChunk?.Invoke(update.Text);
                        }

                        // 思考链（reasoning_content）增量：M.E.AI 适配器转为 TextReasoningContent，
                        // 逐段累积，随助手消息存入历史（后续请求需原样回传）。
                        // 超限后丢弃后续增量；截断保留开头（推理关键段在前），防切断 surrogate pair。
                        foreach (var trc in update.Contents.OfType<TextReasoningContent>())
                        {
                            if (reasoningRoom <= 0) continue;
                            var t = trc.Text ?? "";
                            if (t.Length <= reasoningRoom)
                            {
                                reasoningSb.Append(t);
                                reasoningRoom -= t.Length;
                                cb.OnReasoningChunk?.Invoke(t);
                            }
                            else
                            {
                                var cut = t[..reasoningRoom];
                                if (cut.Length > 0 && char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
                                reasoningSb.Append(cut);
                                reasoningRoom = 0;
                                if (cut.Length > 0) cb.OnReasoningChunk?.Invoke(cut);
                                cb.OnReasoningChunk?.Invoke("\n\n" + AgentRuntimeLimits.ReasoningTruncatedMarker);
                            }
                        }

                        // M.E.AI 10.x：流式工具调用以 FunctionCallContent 到达，
                        // 同一 CallId 的后续更新携带累积后的完整参数，取最后一条。
                        foreach (var fcc in update.Contents.OfType<FunctionCallContent>())
                        {
                            if (string.IsNullOrEmpty(fcc.CallId))
                            {
                                callContents.Add(fcc);
                                continue;
                            }
                            var idx = callContents.FindIndex(c => c.CallId == fcc.CallId);
                            if (idx >= 0) callContents[idx] = fcc;
                            else callContents.Add(fcc);
                        }

                        // token 用量：流式末尾的 usage chunk（finish_reason 更新中携带）
                        if (update.FinishReason is not null &&
                            update.RawRepresentation is OpenAI.Chat.StreamingChatCompletionUpdate sc &&
                            sc.Usage is { } tokenUsage)
                        {
                            usage = new AgentUsage
                            {
                                PromptTokens = tokenUsage.InputTokenCount,
                                CompletionTokens = tokenUsage.OutputTokenCount,
                                CacheHitTokens = tokenUsage.InputTokenDetails?.CachedTokenCount
                            };
                        }
                    }
                }, maxAttempts: 3, ct: requestTimeout.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                AgentDebugLog.Info("第 " + (round + 1) + " 轮被用户取消");
                throw;
            }
            catch (OperationCanceledException)
            {
                AgentDebugLog.Error("第 " + (round + 1) + " 轮请求超时（120 秒）");
                cb.OnError?.Invoke("AI 服务响应超时（120 秒），请重试。");
                return true;
            }
            catch (Exception ex)
            {
                AgentDebugLog.Error("第 " + (round + 1) + " 轮请求失败", ex);
                cb.OnError?.Invoke(AgentErrorPolicy.FormatApiError(ex));
                return true;
            }

            AgentDebugLog.Info($"第 {round + 1} 轮流式完成：文本 {fullText.Length} 字符，工具调用 {callContents.Count} 个");
            // 优先使用 API 返回的 usage；端点不返回时本地估算兜底，
            // 保证 token 统计（发送按钮旁气泡）始终有值。
            cb.OnUsage?.Invoke(usage ?? EstimateUsage(history, fullText.ToString()));

            // 汇总流式函数调用 → 完整工具调用列表（Name 片段按序拼接）
            var calls = callContents
                .GroupBy(c => string.IsNullOrEmpty(c.CallId) ? Guid.NewGuid().ToString("N") : c.CallId)
                .Select(g => (
                    Id: g.Last().CallId ?? "",
                    Name: string.Concat(g.Select(c => c.Name ?? "")),
                    Args: g.Last().Arguments is null
                        ? ""
                        : JsonSerializer.Serialize(g.Last().Arguments))
                )
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .ToList();

            var assistantMsg = new ChatMessage(ChatRole.Assistant, fullText.ToString());
            if (reasoningSb.Length > 0)
                assistantMsg.Contents.Add(new TextReasoningContent(
                    AgentRuntimeLimits.TruncateReasoning(reasoningSb.ToString()) ?? ""));
            foreach (var c in calls)
                assistantMsg.Contents.Add(new FunctionCallContent(
                    callId: c.Id,
                    name: c.Name,
                    arguments: AgentArgsJson.ParseToDictionary(c.Args)));
            history.Add(assistantMsg);

            if (calls.Count == 0)
            {
                cb.OnCompleted?.Invoke(fullText.ToString());
                return true;
            }

            // 执行本轮工具调用
            var pending = new List<PendingToolCall>();
            var toolResults = new List<ChatMessage>();

            foreach (var call in calls)
            {
                var tool = AgentToolRegistry.Find(call.Name);
                if (tool is null)
                {
                    AgentDebugLog.Info($"未知工具 '{call.Name}'");
                    toolResults.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(callId: call.Id, result: $"错误：未知工具 '{call.Name}'")]));
                    continue;
                }

                // 技能强制触发：禁用 web_search（价格必须来自浏览器，搜索不是可购买的真实价格）。
                // 拦截计数：第二次起直接强硬终止，防弱模型反复尝试被禁工具空转烧轮次。
                if (call.Name == "web_search" &&
                    (AgentToolContext.Current?.IsSkillTriggerActive == true || AgentToolContext.SkillTriggerActive))
                {
                    AgentDebugLog.Info($"技能触发中，禁用 web_search（call {call.Id}）");
                    var blocked = AgentToolLoopGuard.RegisterWebSearchBlocked();
                    toolResults.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(callId: call.Id, result: blocked >= 2
                            ? "[web_search 已禁用] 本任务中 web_search 已连续两次被拦截，请勿再尝试。请改用浏览器工具：browser_navigate / browser_get_page / browser_run_js；或基于已有信息直接总结回答。"
                            : "web_search 已被禁用：当前任务触发了「电脑选购」技能，要求用浏览器查询京东实时价格（搜索返回的不是可购买的真实价格）。请改用浏览器工具：browser_navigate 打开 https://search.jd.com/Search?keyword=商品名（URL 编码），再用 browser_get_page / browser_run_js 提取价格。")]));
                    continue;
                }

                // 重复调用护栏：非空参数且签名完全相同的第二次调用直接拦截（不真正执行），
                // 打破模型"反复调用同一操作"的循环；空参数（查询类）豁免——允许重复取最新值。
                var normalizedArgs = AgentToolLoopGuard.NormalizeArgs(call.Args);
                if (normalizedArgs is not null && AgentToolLoopGuard.IsDuplicate(tool.Name, normalizedArgs))
                {
                    AgentDebugLog.Info($"重复调用拦截：{tool.Name} 相同参数已执行过（call {call.Id}）");
                    toolResults.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(callId: call.Id, result: "[重复调用已拦截] 相同参数的工具调用在本轮对话中已执行过，结果不会变化。请勿重复执行，直接基于已有结果继续完成用户的目标；确有必要时先向用户说明再进行下一步。")]));
                    continue;
                }

                var step = CreateStep(tool, call.Id, call.Args);

                // 需要确认：AlwaysConfirm（必须用户亲手参与，如等待登录）不受完全访问模式豁免；
                // 其余危险/计划工具在非完全访问模式下暂停
                if (tool.AlwaysConfirm || (!AgentToolContext.IsFullAccess && (tool.IsPlanTool || tool.RequiresConfirmation)))
                {
                    AgentDebugLog.Info($"工具 {tool.Name} 需确认，暂停循环");
                    step.Status = AgentStepStatus.AwaitingConfirmation;
                    cb.OnStepStarted?.Invoke(step);
                    pending.Add(new PendingToolCall { CallId = call.Id, Tool = tool, Args = call.Args, Step = step });
                    continue;
                }

                cb.OnStepStarted?.Invoke(step);
                AgentDebugLog.Info($"执行工具 {tool.Name} 开始");
                try
                {
                    var result = await tool.Function.InvokeAsync(new AIFunctionArguments(AgentArgsJson.ParseToDictionary(call.Args)), ct);
                    var text = result?.ToString() ?? "";
                    // 空结果标记：避免模型把"无内容返回"误判为执行失败而无限重试（死循环放大环节之一）
                    step.Result = TruncateResult(string.IsNullOrWhiteSpace(text)
                        ? "（工具已执行，未返回内容）"
                        : text);
                    step.Status = AgentStepStatus.Success;
                    step.Duration = DateTime.Now - step.StartedAt;
                    cb.OnStepCompleted?.Invoke(step);
                    AgentDebugLog.Info($"执行工具 {tool.Name} 完成，结果 {step.Result.Length} 字符");
                    toolResults.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(callId: call.Id, result: step.Result)]));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    step.Status = AgentStepStatus.Cancelled;
                    step.Duration = DateTime.Now - step.StartedAt;
                    cb.OnStepCompleted?.Invoke(step);
                    throw;
                }
                catch (Exception ex)
                {
                    step.Status = AgentStepStatus.Failed;
                    step.Error = ex.Message;
                    step.Duration = DateTime.Now - step.StartedAt;
                    cb.OnStepCompleted?.Invoke(step);
                    AgentDebugLog.Error($"执行工具 {tool.Name} 失败", ex);
                    toolResults.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(callId: call.Id, result: AgentErrorPolicy.FormatToolError(ex, call.Name))]));
                }
            }

            history.AddRange(toolResults);

            if (pending.Count > 0)
            {
                cb.OnConfirmationsRequested?.Invoke(pending.Select(ToRequest).ToList());
                return false;
            }

            // 本轮工具链执行完毕 → 结算步骤组（UI 折叠该轮步骤链，下一轮文本/步骤另起一组）
            cb.OnRoundCompleted?.Invoke();

            // 无进展统计：模型未产出用户可见文本、仅调用工具 → 计数递增，达阈值后循环开头终止
            AgentToolLoopGuard.ReportRound(hadUserText: fullText.Length > 0, hadToolCalls: true);
        }

        cb.OnError?.Invoke("对话轮次已达上限，请简化你的问题或点击「继续」让助手继续。");
        return true;
    }

    /// <summary>
    /// 应用用户对危险操作的确认决策（已确认的会真正执行工具），然后继续循环。
    /// </summary>
    public static async Task<bool> ResumeLoopAsync(
        List<ChatMessage> history,
        IReadOnlyList<AgentConfirmationDecision> decisions,
        AgentRunCallbacks cb,
        CancellationToken ct,
        IChatClient? clientOverride = null)
    {
        AgentDebugLog.Info($"ResumeLoop 开始，决策 {decisions.Count} 条");

        foreach (var d in decisions)
        {
            var p = d.Request.Pending;
            if (p is null)
            {
                AgentDebugLog.Error("ResumeLoop：决策缺少 Pending 快照，跳过");
                continue;
            }

            string resultText;
            if (d.Confirmed)
            {
                if (p.Tool.IsPlanTool)
                {
                    resultText = "计划已确认，请按计划开始执行。";
                    p.Step.Status = AgentStepStatus.Success;
                    p.Step.Result = resultText;
                    cb.OnStepCompleted?.Invoke(p.Step);
                }
                else
                {
                    p.Step.Status = AgentStepStatus.Running;
                    AgentDebugLog.Info($"确认执行工具 {p.Tool.Name} 开始");
                    try
                    {
                        // 用户明确确认的操作：直接执行（不拦截，用户知情同意），
                        // 但登记签名——后续轮次模型再发起相同调用会被 RunLoopAsync 拦截
                        var normalized = AgentToolLoopGuard.NormalizeArgs(p.Args);
                        if (normalized is not null) AgentToolLoopGuard.IsDuplicate(p.Tool.Name, normalized);

                        var result = await p.Tool.Function.InvokeAsync(new AIFunctionArguments(AgentArgsJson.ParseToDictionary(p.Args)), ct);
                        var text = result?.ToString() ?? "";
                        // 空结果标记 + 超长截断（与 RunLoopAsync 直接执行路径一致）
                        resultText = TruncateResult(string.IsNullOrWhiteSpace(text)
                            ? "（工具已执行，未返回内容）"
                            : text);
                        p.Step.Result = resultText;
                        p.Step.Status = AgentStepStatus.Success;
                        p.Step.Duration = DateTime.Now - p.Step.StartedAt;
                        AgentDebugLog.Info($"确认执行工具 {p.Tool.Name} 完成，结果 {resultText.Length} 字符");
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        p.Step.Status = AgentStepStatus.Cancelled;
                        p.Step.Duration = DateTime.Now - p.Step.StartedAt;
                        cb.OnStepCompleted?.Invoke(p.Step);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        resultText = AgentErrorPolicy.FormatToolError(ex, p.Tool.Name);
                        p.Step.Error = ex.Message;
                        p.Step.Status = AgentStepStatus.Failed;
                        p.Step.Duration = DateTime.Now - p.Step.StartedAt;
                        AgentDebugLog.Error($"确认执行工具 {p.Tool.Name} 失败", ex);
                    }
                    cb.OnStepCompleted?.Invoke(p.Step);
                }
            }
            else
            {
                resultText = p.Tool.IsPlanTool
                    ? "用户拒绝了该计划。请根据用户反馈调整计划，或直接回答用户的问题。"
                    : $"用户拒绝了该操作：{p.Step.Summary}";
                p.Step.Status = AgentStepStatus.Rejected;
                p.Step.Result = resultText;
                cb.OnStepCompleted?.Invoke(p.Step);
            }

            history.Add(new ChatMessage(ChatRole.Tool,
                [new FunctionResultContent(callId: p.CallId, result: resultText)]));
        }

        return await RunLoopAsync(history, cb, ct, ContinueMaxRounds, clientOverride);
    }

    // ---------- 内部辅助 ----------

    /// <summary>
    /// 工具结果长度上限：超长结果截断保留开头（控制上下文体积——上下文越满模型
    /// 越容易迷失、越绕越深；上限与 <see cref="AgentToolLoopGuard.MaxToolResultChars"/>
    /// 一致，新/旧引擎行为统一）；防切断 surrogate pair（emoji 等）。
    /// </summary>
    private static string TruncateResult(string text)
    {
        if (text.Length <= AgentToolLoopGuard.MaxToolResultChars)
            return text;

        var cut = text[..AgentToolLoopGuard.MaxToolResultChars];
        if (char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
        return cut + $"\n…（结果过长，已截断 {text.Length - AgentToolLoopGuard.MaxToolResultChars} 字符）";
    }

    /// <summary>
    /// 历史回传总量预算压缩（与新引擎 TubaChatProvider.TrimHistory 同语义）：
    /// 估算总长超过 <see cref="AgentRuntimeLimits.HistoryBudgetChars"/> 时从最旧丢弃，
    /// 保留首条 system（DeepSeek 网关要求 system 在前，且拒绝多条 system）。
    /// 30 轮工具循环中每轮回传思考(≤6000)+结果(≤6000)，无上限会撑爆 64K 上下文窗口——
    /// 这是死循环"烧穿上下文"的路径；预算只在超限时生效，正常会话完全无感知。
    /// </summary>
    internal static void TrimHistory(List<ChatMessage> history)
    {
        var total = history.Sum(RoughLength);
        if (total <= AgentRuntimeLimits.HistoryBudgetChars) return;

        // 从最旧丢弃；最新 user 位于尾部，正常不会先被丢
        for (var i = 0; i < history.Count && total > AgentRuntimeLimits.HistoryBudgetChars; i++)
        {
            if (i == 0 && history[i].Role == ChatRole.System) continue;
            total -= RoughLength(history[i]);
            history.RemoveAt(i);
            i--;
        }
    }

    /// <summary>单条消息的粗略字符估算（文本/思考/工具调用与结果，用于预算压缩阈值判断）。</summary>
    private static long RoughLength(ChatMessage m)
    {
        var len = 0L;
        var hasTextContent = false;
        foreach (var c in m.Contents)
        {
            switch (c)
            {
                case TextContent tc:
                    hasTextContent = true;
                    len += tc.Text?.Length ?? 0;
                    break;
                case TextReasoningContent trc:
                    len += trc.Text?.Length ?? 0;
                    break;
                case FunctionCallContent fcc:
                    len += fcc.Name?.Length ?? 0;
                    if (fcc.Arguments is not null) len += JsonSerializer.Serialize(fcc.Arguments).Length;
                    break;
                case FunctionResultContent frc:
                    len += frc.Result?.ToString()?.Length ?? 0;
                    break;
            }
        }
        if (!hasTextContent) len += m.Text?.Length ?? 0;
        return len;
    }

    /// <summary>
    /// 本地 token 估算（API 未返回 usage 时的兜底）：
    /// 统计本轮提示词（历史消息文本 + 工具调用/结果）与输出文本的估算 token 数。
    /// </summary>
    private static AgentUsage EstimateUsage(IEnumerable<ChatMessage> history, string completionText)
    {
        var promptTokens = 0;
        foreach (var msg in history)
        {
            promptTokens += AgentMemory.EstimateTokens(msg.Text ?? "");
            foreach (var fcc in msg.Contents.OfType<FunctionCallContent>())
                promptTokens += AgentMemory.EstimateTokens($"{fcc.Name} {JsonSerializer.Serialize(fcc.Arguments)}");
            foreach (var frc in msg.Contents.OfType<FunctionResultContent>())
                promptTokens += AgentMemory.EstimateTokens(frc.Result?.ToString() ?? "");
        }
        return new AgentUsage
        {
            PromptTokens = promptTokens,
            CompletionTokens = AgentMemory.EstimateTokens(completionText)
        };
    }

    private static AgentStep CreateStep(AgentTool tool, string callId, string argsJson)
    {
        var args = ReadArgs(argsJson);
        var summary = BuildSummary(tool.Name, args);
        return new AgentStep
        {
            ToolName = tool.Name,
            DisplayName = tool.DisplayName,
            Glyph = tool.Glyph,
            Summary = summary,
            Detail = string.IsNullOrWhiteSpace(argsJson) ? null : argsJson,
            Reason = args.TryGetValue("reason", out var r) && !string.IsNullOrWhiteSpace(r) ? r : tool.DefaultReason,
            CallId = callId,
            IsDangerous = tool.RequiresConfirmation,
        };
    }

    private static AgentConfirmationRequest ToRequest(PendingToolCall p)
    {
        var args = ReadArgs(p.Args);
        var isPlan = p.Tool.IsPlanTool;
        return new AgentConfirmationRequest
        {
            CallId = p.CallId,
            ToolName = p.Tool.Name,
            DisplayName = p.Tool.DisplayName,
            Glyph = p.Tool.Glyph,
            Summary = isPlan ? args.GetValueOrDefault("goal") ?? "" : BuildSummary(p.Tool.Name, args),
            Detail = isPlan ? "" : p.Args,
            Reason = args.TryGetValue("reason", out var r) && !string.IsNullOrWhiteSpace(r) ? r : p.Tool.DefaultReason ?? "",
            Kind = isPlan ? "plan" : p.Tool.ConfirmKind ?? "action",
            PlanSteps = isPlan ? (args.TryGetValue("steps", out var s) ? ParseSteps(s) : null) : null,
            PlanGoal = isPlan ? args.GetValueOrDefault("goal") : null,
            Step = p.Step,
            Pending = p,
        };
    }

    private static IReadOnlyList<string>? ParseSteps(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            return doc.RootElement.EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
        catch { return null; }
    }

    private static Dictionary<string, string> ReadArgs(string jsonArgs)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(jsonArgs)) return result;
        try
        {
            using var doc = JsonDocument.Parse(jsonArgs);
            foreach (var prop in doc.RootElement.EnumerateObject())
                result[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? ""
                    : prop.Value.GetRawText();
        }
        catch { }
        return result;
    }

    private static string BuildSummary(string toolName, Dictionary<string, string> args)
    {
        static string Arg(Dictionary<string, string> d, string key, string prefix)
        {
            var v = d.GetValueOrDefault(key) ?? "";
            return string.IsNullOrWhiteSpace(v) ? "" : $"{prefix}：{v}";
        }

        var summary = toolName switch
        {
            "web_search" => Arg(args, "query", "搜索"),
            "fetch_page" => Arg(args, "url", "访问"),
            "run_command" => Arg(args, "cmd", "命令"),
            "run_powershell" => Arg(args, "script", "脚本"),
            "read_file" => Arg(args, "path", "读取"),
            "write_file" => Arg(args, "path", "写入"),
            "edit_file" => Arg(args, "path", "编辑"),
            "append_file" => Arg(args, "path", "追加"),
            "list_dir" => Arg(args, "path", "目录"),
            "get_info" => Arg(args, "path", "查看"),
            "find_files" => $"{Arg(args, "path", "搜索目录")} {Arg(args, "pattern", "模式")}".Trim(),
            "delete_file" => Arg(args, "path", "删除"),
            "move_file" => $"{Arg(args, "source", "移动")} → {Arg(args, "destination", "到")}",
            "copy_file" => $"{Arg(args, "source", "复制")} → {Arg(args, "destination", "到")}",
            "download_file" => $"{Arg(args, "url", "下载")} → {Arg(args, "destinationPath", "保存到")}",
            "read_reg" => Arg(args, "key", "注册表"),
            "write_reg" => $"{Arg(args, "key", "注册表")}\\{Arg(args, "value", "")}",
            "launch_tool" => Arg(args, "toolName", "启动"),
            "run_cli_tool" => $"{Arg(args, "toolName", "工具")} {Arg(args, "args", "参数")}".Trim(),
            "create_plan" => Arg(args, "goal", "目标"),
            "read_memory" => "读取会话记忆",
            "write_memory" => "更新会话记忆",
            "clear_memory" => "清空会话记忆",
            "browser_wait_for_login" => Arg(args, "site", "等待登录"),
            _ => ""
        };

        if (!string.IsNullOrWhiteSpace(summary)) return summary.Trim();

        // 兜底：拼接参数
        var joined = string.Join(" ", args.Where(kv => kv.Key != "reason").Select(kv => $"{kv.Key}={kv.Value}"));
        return joined.Length > 80 ? joined[..80] + "…" : joined;
    }
}
