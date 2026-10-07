using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Tests;

public class AgentErrorPolicyTests
{
    [Fact]
    public void FormatApiError_UnauthorizedAccess_HintsApiKey()
    {
        var text = AgentErrorPolicy.FormatApiError(new UnauthorizedAccessException("The api key sk-xxx was invalid"));

        Assert.Contains("API Key 无效或没有权限", text);
        Assert.Contains("sk-xxx", text);
        Assert.Contains("设置 → AI 服务", text);
    }

    [Fact]
    public void FormatApiError_HttpRequestWithStatus_ShowsCode()
    {
        var ex = new HttpRequestException("Response status code does not indicate success: 404 (Not Found).", null, System.Net.HttpStatusCode.NotFound);
        var text = AgentErrorPolicy.FormatApiError(ex);

        Assert.Contains("HTTP 404", text);
        Assert.Contains("404", text);
    }

    [Fact]
    public void FormatApiError_IncludesInnerExceptionChain()
    {
        var inner = new InvalidOperationException("模型不存在：model-xyz");
        var outer = new Exception("请求失败", inner);
        var text = AgentErrorPolicy.FormatApiError(outer);

        Assert.Contains("模型不存在：model-xyz", text);
    }

    [Fact]
    public void FormatApiError_NullStatusCode_NetworkHint()
    {
        var ex = new HttpRequestException("连接被拒绝");
        var text = AgentErrorPolicy.FormatApiError(ex);

        Assert.Contains("网络连接失败", text);
    }

    // ---------- 本地模型推理错误：给出本地排查建议，而不是端点/Key ----------

    [Fact]
    public void FormatApiError_LocalOrtMemoryError_HintsLocalNotEndpoint()
    {
        // 复现真实事故：ORT GenAI 的 GroupQueryAttention / BFCArena 分配失败
        var ex = new InvalidOperationException(
            "Non-zero status code returned while running GroupQueryAttention node. " +
            "Status Message: onnxruntime::BFCArena::AllocateRawInternal Failed to allocate memory " +
            "for requested buffer of size 32731660544");

        var text = AgentErrorPolicy.FormatApiError(ex);

        Assert.Contains("本地模型", text);
        Assert.Contains("内存不足", text);
        Assert.DoesNotContain("设置 → AI 服务", text); // 不再误导去查端点/Key
    }

    [Fact]
    public void IsLocalInferenceError_DetectsOrtMarkers()
    {
        Assert.True(AgentErrorPolicy.IsLocalInferenceError(
            new InvalidOperationException("模型尚未加载。")));
        Assert.True(AgentErrorPolicy.IsLocalInferenceError(
            new Exception("outer", new Exception("onnxruntime-genai failure"))));
        Assert.False(AgentErrorPolicy.IsLocalInferenceError(
            new UnauthorizedAccessException("api key invalid")));
    }

    // ---------- FormatToolError：区分可重试（参数）与系统性失败（勿重试） ----------

    /// <summary>系统性失败（非参数类异常）→ 明确"请勿重试"终态，防止模型对同一失败操作反复调用。</summary>
    [Fact]
    public void FormatToolError_SystemError_MarksNoRetry()
    {
        var text = AgentErrorPolicy.FormatToolError(new InvalidOperationException("磁盘被占用"), "run_command");

        Assert.Contains("[工具错误]", text);
        Assert.Contains("run_command", text);
        Assert.Contains("磁盘被占用", text);
        Assert.Contains("请勿重试", text);
        Assert.DoesNotContain("请检查调用参数后重试", text);
    }

    /// <summary>参数类错误（ArgumentException）→ 标记"参数无效"，允许调整后重试。</summary>
    [Fact]
    public void FormatToolError_ArgumentError_MarksRetryable()
    {
        var text = AgentErrorPolicy.FormatToolError(new ArgumentException("路径不能为空", "path"), "read_file");

        Assert.Contains("[工具错误]", text);
        Assert.Contains("参数无效", text);
        Assert.Contains("请检查调用参数后重试", text);
        Assert.DoesNotContain("请勿重试", text);
    }

    /// <summary>JsonException 属参数类错误（工具收到非法 JSON 参数），可调整后重试。</summary>
    [Fact]
    public void FormatToolError_JsonError_MarksRetryable()
    {
        var text = AgentErrorPolicy.FormatToolError(new JsonException("'{' is invalid"), "write_file");

        Assert.Contains("参数无效", text);
        Assert.DoesNotContain("请勿重试", text);
    }

    /// <summary>AIFunction 反射包装（TargetInvocationException）解包后按内部异常分类。</summary>
    [Fact]
    public void FormatToolError_UnwrapsTargetInvocation()
    {
        var inner = new InvalidOperationException("网络连接中断");
        var wrapped = new TargetInvocationException(inner);
        var text = AgentErrorPolicy.FormatToolError(wrapped, "fetch_page");

        Assert.Contains("网络连接中断", text);
        Assert.Contains("请勿重试", text); // 内部是系统性失败 → 不重试
    }

    /// <summary>异常 Message 为空时回退到类型名，仍给出正确分类。</summary>
    [Fact]
    public void FormatToolError_EmptyMessage_FallsBackToTypeName()
    {
        var text = AgentErrorPolicy.FormatToolError(new Exception(""), "get_info");

        Assert.Contains(nameof(Exception), text);
        Assert.Contains("请勿重试", text);
    }
}
