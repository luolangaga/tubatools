namespace TubaWinUi3.Services;

/// <summary>
/// 启动里程碑日志（面包屑），写入统一日志目录下的 <c>startup.log</c>。
///
/// 用途：WinUI 原生代码里的失败会以 STOWED_EXCEPTION 直接结束进程（0xc000027b），
/// 托管栈、AppDomain.UnhandledException、Application.UnhandledException 全都拿不到，
/// 应用商店崩溃报告里也只留下 <c>Microsoft.UI.Xaml.dll!FailFastWithStowedExceptions</c>
/// 这一层 —— 真凶既不在托管栈里也不在报告里。此时唯一能定位"崩在哪一步"的办法，
/// 就是在启动路径的每一步留一枚面包屑：崩掉后打开 运行日志 → 启动轨迹，
/// 最后一枚里程碑就是崩溃前跑完的最后一段代码。
///
/// 只做纯文件追加（不碰 XAML / WinRT / 注册表），任何一步失败都静默忽略，
/// 因此它本身不会成为新的崩溃源。错误报告打包会一并收录。
/// </summary>
internal static class StartupTrace
{
    private static readonly object Gate = new();

    /// <summary>新一轮启动：写入会话头（覆盖上一轮，只保留最近一次启动的轨迹）。</summary>
    public static void Begin()
    {
        try
        {
            lock (Gate)
            {
                LogService.Prepare();
                LogService.ResetFile(LogService.Startup,
                    $"===== 启动于 {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  " +
                    $"版本 {UpdateService.CurrentVersion}  打包 {RuntimeHelper.IsMsixPackaged}  " +
                    $"系统 {Environment.OSVersion.Version}  程序 {Environment.ProcessPath} =====");
            }
        }
        catch { }
    }

    /// <summary>记录一枚里程碑。</summary>
    public static void Mark(string stage)
    {
        try
        {
            lock (Gate)
            {
                LogService.Info(LogService.Startup, stage);
            }
        }
        catch { }
    }
}
