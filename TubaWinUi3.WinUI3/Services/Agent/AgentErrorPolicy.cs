using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace TubaWinUi3.Services.Agent;

/// <summary>
/// 错误恢复策略：瞬时传输错误指数退避重试、工具失败结构化反馈
/// （让模型自我修正后重试）。
/// </summary>
public static class AgentErrorPolicy
{
    /// <summary>
    /// 对瞬时错误（网络/超时）做指数退避重试：2s、4s、8s，最多 maxAttempts 次。
    /// 非瞬时错误或重试耗尽后原样抛出。
    /// </summary>
    public static async Task WithRetryAsync(Func<CancellationToken, Task> action, int maxAttempts = 3, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action(ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransient(ex))
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1) * 2), ct);
            }
            catch
            {
                throw;
            }
        }
    }

    public static bool IsTransient(Exception ex)
        => ex is HttpRequestException or TimeoutException or TaskCanceledException
           || ex.InnerException is HttpRequestException or TimeoutException;

    /// <summary>
    /// 工具执行失败 → 结构化错误文本（作为 tool 结果回填给模型，
    /// 模型据此修正参数或换一种方式重试，实现错误恢复闭环）。
    /// 区分可重试（参数/调用方式问题）与系统性失败（勿重试）——旧的统一
    /// "请重试"式鼓励会让模型对同一失败操作反复调用空转烧轮次（死循环放大环节之一）。
    /// </summary>
    public static string FormatToolError(Exception ex, string toolName)
    {
        // 剥开反射包装（AIFunction 调用可能抛出 TargetInvocationException）
        var current = ex;
        while (current is TargetInvocationException && current.InnerException is not null)
            current = current.InnerException;

        var message = string.IsNullOrWhiteSpace(current.Message) ? current.GetType().Name : current.Message;
        return current is ArgumentException or JsonException or FormatException
            ? $"[工具错误] {toolName} 参数无效：{message}。请检查调用参数后重试，或换一种方式完成用户的目标。"
            : $"[工具错误] {toolName} 执行失败：{message}。此问题属于系统/环境层面，重复调用不会改变结果，请勿重试；请改用其他工具或直接基于已有信息总结回答。";
    }

    /// <summary>
    /// 模型 API 请求失败 → 面向用户的详细错误文本：
    /// HTTP 状态码 / 常见原因提示 / 完整异常链（便于定位 API Key、端点、模型问题）。
    /// 若异常来自本地推理（ORT GenAI / 内存分配失败），给出本地相关排查建议（而不是端点/Key）。
    /// 判定**只看异常本身**，不读全局提供商状态，保证错误格式化与调用顺序无关。
    /// </summary>
    public static string FormatApiError(Exception ex)
    {
        var current = ex;
        while (current is TargetInvocationException && current.InnerException is not null)
            current = current.InnerException;

        var isLocal = IsLocalInferenceError(current);

        var hints = new List<string>();
        if (!isLocal && current is HttpRequestException { StatusCode: { } code })
            hints.Add($"HTTP {(int)code}（{code}）");
        if (!isLocal && current is UnauthorizedAccessException)
            hints.Add("API Key 无效或没有权限");
        if (!isLocal && current is HttpRequestException { StatusCode: null })
            hints.Add("网络连接失败（无法访问 AI 服务端点）");
        if (isLocal && IsMemoryError(current))
            hints.Add("本地模型显存/内存不足（上下文过长）");

        var chain = new List<string>();
        for (var e = current; e is not null; e = e.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(e.Message) && !chain.Contains(e.Message))
                chain.Add(e.Message);
        }
        var detail = string.Join(" → ", chain.Take(3));

        var head = hints.Count > 0
            ? $"AI 服务请求失败：{string.Join("；", hints)}。"
            : "AI 服务请求失败：";
        if (detail.Length > 0)
            head += $"\n{detail}";

        head += isLocal
            ? "\n\n本地模型推理失败，请检查：模型是否完整（本地 AI 试炼场）、内存是否充足、或换用更小的对话模型。"
            : "\n\n请检查 设置 → AI 服务 中的端点、模型名与 API Key，或稍后重试。";
        return head;
    }

    /// <summary>异常是否来自本地推理（ORT GenAI / 本地模型），用于给出对应的排查建议。</summary>
    internal static bool IsLocalInferenceError(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var m = e.Message;
            if (string.IsNullOrEmpty(m)) continue;
            if (IsMemoryErrorMessage(m) ||
                m.Contains("onnxruntime", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("onnxruntime-genai", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("GroupQueryAttention", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("genai_config", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("模型尚未加载", StringComparison.Ordinal) ||
                m.Contains("本地模型", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>识别内存分配失败（BFCArena / bad_alloc / 分配失败）。</summary>
    private static bool IsMemoryError(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (!string.IsNullOrEmpty(e.Message) && IsMemoryErrorMessage(e.Message))
                return true;
        }
        return false;
    }

    private static bool IsMemoryErrorMessage(string m)
        => m.Contains("Failed to allocate", StringComparison.OrdinalIgnoreCase)
           || m.Contains("BFCArena", StringComparison.OrdinalIgnoreCase)
           || m.Contains("bad_alloc", StringComparison.OrdinalIgnoreCase)
           || m.Contains("out of memory", StringComparison.OrdinalIgnoreCase)
           || m.Contains("内存不足", StringComparison.Ordinal);
}
