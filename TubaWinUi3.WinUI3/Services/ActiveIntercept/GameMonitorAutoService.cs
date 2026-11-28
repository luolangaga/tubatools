namespace TubaWinUi3.Services.ActiveIntercept;

/// <summary>
/// 游戏自动监控（主程序侧设置与控制）：
/// 开启后，由主程序进程内的 <see cref="ActiveInterceptRuntime"/> 装配检测器 ——
/// 检测到全屏/无边框游戏窗口时，写覆盖层信号，由 <see cref="GameOverlayAutoService"/>
/// 在游戏窗口上显示 FPS 覆盖层。
///
/// 不再依赖独立的 TubaWinUI3.BackEnd.exe，也不再需要管理员权限：
/// 只要主程序在运行且开关已开启，功能就可用；主程序退出后自动停止。
/// </summary>
public static class GameMonitorAutoService
{
    public const string EnabledSettingKey = "GameMonitorBackendEnabled";
    public const string PromptedSettingKey = "GameMonitorBackendPrompted";

    /// <summary>功能是否已开启（设置项）。</summary>
    public static bool IsEnabled => AppSettings.GetBool(EnabledSettingKey, false);

    /// <summary>检测器当前是否在运行（主程序内）。</summary>
    public static bool IsRunning => ActiveInterceptRuntime.IsGameMonitorRunning;

    /// <summary>首次询问弹窗是否已经展示过。</summary>
    public static bool HasPrompted => AppSettings.Get(PromptedSettingKey) is not null;

    /// <summary>设置项标记为已询问（无论用户选择开启还是暂不）。</summary>
    public static void MarkPrompted() => AppSettings.Set(PromptedSettingKey, "1");

    /// <summary>开启/关闭功能：写设置 → 启停覆盖层轮询 → 同步进程内检测器。</summary>
    public static void SetEnabled(bool enabled)
    {
        AppSettings.Set(EnabledSettingKey, enabled);
        AppSettings.Flush();

        if (enabled) GameOverlayAutoService.Instance.Start();
        else GameOverlayAutoService.Instance.Stop();

        ActiveInterceptService.SyncBackend();
    }
}
