namespace TubaWinUi3.Services.Agent;

/// <summary>
/// Agent 运行时调试日志（统一写入 日志目录/agent.log），
/// 用于定位确认流/多轮循环在真实环境中的卡点。
/// </summary>
internal static class AgentDebugLog
{
    public static void Info(string message) => LogService.Info(LogService.Agent, message);

    public static void Error(string message, Exception? ex = null)
        => LogService.Error(LogService.Agent, message, ex);
}
