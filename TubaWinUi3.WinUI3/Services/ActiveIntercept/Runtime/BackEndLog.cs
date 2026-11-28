namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

/// <summary>
/// 主动拦截运行时的日志门面。运行时已经并入主程序进程，沿用全局
/// <see cref="LogService"/> 的 backend 类别（目录设置、滚动、总量配额、错误报告打包
/// 全部自动生效），不再自己维护第二套日志文件。
/// </summary>
public static class BackEndLog
{
    public static void Info(string message) => LogService.Info(LogService.Backend, message);

    public static void Warn(string message) => LogService.Warn(LogService.Backend, message);

    public static void Error(string message) => LogService.Error(LogService.Backend, message);

    /// <summary>调试级：只进调试器输出，不落盘（心跳/每轮扫描这类噪声走这里）。</summary>
    public static void Debug(string message)
    {
        try { System.Diagnostics.Debug.WriteLine("[ActiveIntercept] " + message); } catch { }
    }
}
