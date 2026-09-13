using System.IO;

namespace TubaWinUi3.Services.ActiveIntercept;

/// <summary>
/// 游戏后台自动监控（主程序侧）：
/// 开启后由后端进程（TubaWinUI3.BackEnd.exe，与「流氓软件右键菜单拦截」共用）
/// 在后台检测游戏 —— 检测到全屏/无边框游戏窗口且该进程正在渲染（ETW 帧事件），
/// 自动在其窗口上显示 FPS 覆盖层，主程序无需保持运行。
///
/// 两个功能互相独立：这里只管设置项与后端进程的启停同步（ActiveInterceptService
/// 是后端进程的唯一管理者，配置里的两个功能开关决定后端装配哪些子系统）。
///
/// MSIX 打包模式下不支持（无法启动独立后端进程），开关在 UI 层直接隐藏。
/// </summary>
public static class GameMonitorBackendService
{
    public const string EnabledSettingKey = "GameMonitorBackendEnabled";
    public const string PromptedSettingKey = "GameMonitorBackendPrompted";

    /// <summary>当前环境是否支持本功能（MSIX 打包模式不支持启动独立后端）。</summary>
    public static bool IsSupported => !RuntimeHelper.IsMsixPackaged;

    /// <summary>功能是否已开启（设置项）。</summary>
    public static bool IsEnabled => AppSettings.GetBool(EnabledSettingKey, false);

    /// <summary>首次询问弹窗是否已经展示过。</summary>
    public static bool HasPrompted => AppSettings.Get(PromptedSettingKey) is not null;

    /// <summary>设置项标记为已询问（无论用户选择开启还是暂不）。</summary>
    public static void MarkPrompted() => AppSettings.Set(PromptedSettingKey, "1");

    /// <summary>
    /// 后端可执行文件是否存在（MSIX 包内没有这个 exe，选项也随之隐藏）。
    /// </summary>
    public static bool IsBackendAvailable => File.Exists(ActiveInterceptService.BackEndExePath);

    /// <summary>开启/关闭功能并同步后端进程（写配置 → 启动/重启/停止）。</summary>
    public static void SetEnabled(bool enabled)
    {
        if (!IsSupported) return;

        AppSettings.Set(EnabledSettingKey, enabled);
        // 后端是单实例进程：任意一边开关变化都走统一同步逻辑，
        // 由 ActiveInterceptService 决定 启动/重启/停止。
        ActiveInterceptService.SyncBackend();
    }
}
