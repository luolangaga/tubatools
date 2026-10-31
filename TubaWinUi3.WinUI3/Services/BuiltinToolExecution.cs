namespace TubaWinUi3.Services;

/// <summary>所有 UI / AI / 快捷方式共用的内置工具执行入口，记录打开动作一次。</summary>
internal static class BuiltinToolExecution
{
    internal static Task ExecuteTrackedAsync(this IBuiltinTool tool, BuiltinToolContext context)
    {
        // 同步抛错/立即失败的任务不算成功打开；正常打开后不等待用户关闭工具才计数。
        var execution = tool.ExecuteAsync(context);
        if (!execution.IsFaulted && !execution.IsCanceled)
            Telemetry.TelemetryService.TrackToolLaunch(tool.Id, builtin: true);
        return execution;
    }
}
