using System.Diagnostics;
using Microsoft.Win32;

namespace TubaWinUi3.Services;

/// <summary>
/// 「关闭主窗口 → 最小化到系统托盘」。
///
/// 动机：主程序每次冷启动都要重新做一遍硬件盘点（WMI 20+ 条查询、LiteMonitor 初始化、
/// 图标缓存扫描），误点一次关闭按钮的代价就是被迫重启 + 重新读一遍硬件信息。
/// 开启本功能后，点关闭按钮不再退出进程，而是隐藏主窗口留在系统托盘继续运行；
/// 托盘菜单可以唤回窗口，也可以真正退出（<see cref="App.RequestExit"/> 走完整清理，
/// 不会漏掉 FPS 的 ETW 会话释放）。
///
/// 判定集中在 <see cref="ShouldHideOnClose"/>（纯函数，<c>CloseToTrayTests</c> 覆盖），
/// 窗口侧（MainWindow）只负责执行「取消关闭 + 隐藏 + 拉起托盘图标」。
/// </summary>
public static class CloseToTrayService
{
    /// <summary>设置键：关闭主窗口时是否最小化到系统托盘。缺省视为开启（用户要的就是「别退出」）。</summary>
    public const string EnabledSettingKey = "CloseToTray";

    /// <summary>设置键：首次隐藏到托盘的气泡提示只弹一次。</summary>
    public const string HintShownSettingKey = "CloseToTrayHintShown";

    /// <summary>未写过设置时的默认值。</summary>
    public const bool DefaultEnabled = true;

    /// <summary>气泡正文截断长度：气泡提示正文上限 255 字符，留出余量（标题另有 63 字符限制）。</summary>
    private const int BalloonTextMaxLength = 200;

    // SystemEvents 的回调在它自己的监听线程上触发，主线程读取，必须 volatile
    private static volatile bool _sessionEnding;
    private static bool _sessionWatchAttached;

    /// <summary>注销 / 关机 / 重启进行中：此时必须放行关闭，否则会拖住系统注销。</summary>
    public static bool IsSessionEnding => _sessionEnding;

    /// <summary>
    /// 会话已恢复正常（窗口被重新显示）：清掉会话结束标记。
    /// 用户取消注销 / 系统取消关机时不会有「结束被取消」事件，只能靠用户还能操作界面这一点反推，
    /// 否则「关闭时最小化到托盘」会在整个进程生命周期内静默失效。
    /// </summary>
    public static void ResetSessionEnding() => _sessionEnding = false;

    /// <summary>当前设置值（默认开启）。</summary>
    public static bool IsEnabled => AppSettings.GetBool(EnabledSettingKey, DefaultEnabled);

    public static void SetEnabled(bool enabled) => AppSettings.Set(EnabledSettingKey, enabled);

    /// <summary>
    /// 订阅系统会话结束事件。<see cref="SystemEvents.SessionEnding"/> 的回调依赖消息泵，
    /// 主线程已有 WinUI 消息循环；只注册一次，失败静默（拿不到就当没有这个保护，不影响主流程）。
    /// </summary>
    public static void AttachSessionEndingWatch()
    {
        if (_sessionWatchAttached) return;
        _sessionWatchAttached = true;

        try
        {
            SystemEvents.SessionEnding += (_, _) => _sessionEnding = true;
        }
        catch (Exception ex)
        {
            _sessionWatchAttached = false;
            Debug.WriteLine($"[CloseToTray] 会话结束事件监听注册失败（已忽略）: {ex.Message}");
        }
    }

    /// <summary>
    /// 纯判据（可单测）：这次「关闭窗口」是否应改写成「隐藏到托盘」。
    /// <list type="bullet">
    /// <item><c>isExiting</c>：用户从托盘菜单明确要退出 → 必须放行真正的关闭；</item>
    /// <item><c>sessionEnding</c>：注销 / 关机 → 必须放行，否则系统关不掉；</item>
    /// <item><c>isLiteMode</c>：后台工具（流量监控器 / 网络调度器 / 防晕3D）正在跑 —— 沿用既有行为，
    /// 无论开关如何都要留在托盘，否则这些监控类工具会被自己关掉；</item>
    /// <item>其余情况由「关闭时最小化到托盘」开关决定。</item>
    /// </list>
    /// </summary>
    public static bool ShouldHideOnClose(bool closeToTrayEnabled, bool isLiteMode, bool isExiting, bool sessionEnding)
        => !isExiting && !sessionEnding && (isLiteMode || closeToTrayEnabled);

    /// <summary>主窗口已隐藏到托盘：常驻托盘图标，并在首次隐藏时给一次气泡说明。</summary>
    public static void OnHiddenToTray()
    {
        TrayIconService.ShowForCloseToTray();

        // 只有「关闭时最小化到托盘」这条路径才提示：后台工具页面自己点的「最小化到托盘」不需要教学，
        // 开关关掉时也不该消耗掉这次性提示（否则用户以后开启功能就再也看不到说明了）
        if (!IsEnabled) return;
        if (AppSettings.GetBool(HintShownSettingKey, false)) return;
        AppSettings.Set(HintShownSettingKey, true);

        var title = LocalizationService.L("Tray_BalloonTitle", "图吧工具箱仍在后台运行");
        var text = LocalizationService.L(
            "Tray_BalloonText",
            "程序已最小化到系统托盘，硬件信息等数据依然保留，再次打开无需重新检测。双击托盘图标可恢复窗口；右键图标可退出程序。");
        if (text.Length > BalloonTextMaxLength) text = text[..BalloonTextMaxLength];

        TrayIconService.ShowBalloon(title, text);
    }
}
