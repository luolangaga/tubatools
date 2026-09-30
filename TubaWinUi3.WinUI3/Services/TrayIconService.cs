using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

/// <summary>
/// 系统托盘图标（全进程唯一实例）。托盘常驻有两种理由，彼此独立：
/// 1. 内置工具在后台运行（流量监控器 / 网络调度器 / 游戏防晕3D …）—— 由各页面调用 <see cref="Show"/>；
/// 2. 关闭主窗口后留在托盘（「关闭时最小化到系统托盘」，见 <see cref="CloseToTrayService"/>）——
///    由 <see cref="ShowForCloseToTray"/> 标记。
///
/// 任一理由成立图标就可见；两个理由都消失时自动收起，不留一个点了没反应的托盘图标。
/// 托盘菜单：打开主窗口 / [停止「工具名」并退出] / 退出图吧工具箱。
///
/// <b>退出必须走 <see cref="App.RequestExit"/></b>，不要再像早期实现那样直接 Process.Kill：
/// 杀进程会跳过 MainWindow_Closed 的完整清理 —— FPS 的 ETW 内核会话不会随进程终止自动回收，
/// 残留会让下次启动的帧率采集失效，遥测队列里的数据也会丢。
/// </summary>
public static class TrayIconService
{
    private static NotifyIcon? _notifyIcon;
    private static ContextMenuStrip? _menu;

    /// <summary>「关闭后留在托盘」生效中（主窗口已隐藏）。</summary>
    private static bool _residentInTray;

    /// <summary>正在后台运行的内置工具名；null = 没有后台工具。</summary>
    private static string? _backgroundToolName;

    private static Action? _stopAction;
    private static Action? _restoreAction;

    /// <summary>气泡/悬停文本截断长度。NotifyIcon.Text 上限 .NET 6+ 为 127 字符（更早版本 63），
    /// 这里保守取 62，配合 UpdateIcon 里的 try/catch 双保险。</summary>
    private const int TooltipMaxLength = 62;

    /// <summary>是否需要常驻托盘（任一理由成立）。</summary>
    private static bool ShouldStayInTray => _residentInTray || _backgroundToolName is not null;

    public static bool IsVisible => _notifyIcon?.Visible == true;

    /// <summary>
    /// 内置工具进入后台运行：常驻托盘，并提供「停止该工具并退出」的入口。
    /// 调用方随后关闭主窗口，由 <see cref="CloseToTrayService.ShouldHideOnClose"/> 放行隐藏。
    /// </summary>
    public static void Show(
        string toolName = "游戏防晕3D",
        Action? stopAction = null,
        Action? restoreAction = null)
    {
        _backgroundToolName = toolName;
        _stopAction = stopAction ?? AntiMotionSicknessOverlay.CloseOverlay;
        _restoreAction = restoreAction;

        EnsureIcon();
        UpdateIcon();
    }

    /// <summary>主窗口已关闭并隐藏到托盘（「关闭时最小化到系统托盘」）。</summary>
    public static void ShowForCloseToTray()
    {
        _residentInTray = true;
        EnsureIcon();
        UpdateIcon();
    }

    /// <summary>
    /// 主窗口已重新显示：不再需要「留在托盘」这个理由。
    /// 后台工具若仍在运行，图标继续保留（它是停止该工具的唯一入口）。
    /// </summary>
    public static void OnMainWindowRestored()
    {
        _residentInTray = false;
        UpdateIcon();
    }

    /// <summary>
    /// 清空全部常驻理由并收起托盘图标（退出流程 / 需要整块收起托盘时使用）。
    /// 注意同时会丢掉「后台工具在跑」的停止入口，后台工具仍在运行时不要调用它。
    /// </summary>
    public static void Hide()
    {
        _residentInTray = false;
        _backgroundToolName = null;
        _stopAction = null;
        _restoreAction = null;

        if (_notifyIcon is not null)
            _notifyIcon.Visible = false;
    }

    public static void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _menu?.Dispose();
        _menu = null;
        _residentInTray = false;
        _backgroundToolName = null;
        _stopAction = null;
        _restoreAction = null;
    }

    /// <summary>气泡通知（首次最小化到托盘时的说明）。</summary>
    public static void ShowBalloon(string title, string text, int timeoutMs = 5000)
    {
        if (_notifyIcon is null || !_notifyIcon.Visible) return;

        try
        {
            _notifyIcon.ShowBalloonTip(timeoutMs, title, text, ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            // 通知中心被策略禁用等情况：提示看不到不影响功能本身
            Debug.WriteLine($"[TrayIcon] 气泡提示失败（已忽略）: {ex.Message}");
        }
    }

    // ---------- 内部 ----------

    private static void EnsureIcon()
    {
        if (_notifyIcon is not null) return;

        _menu = new ContextMenuStrip();
        _notifyIcon = new NotifyIcon
        {
            Icon = GetIcon(),
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.DoubleClick += (_, _) => RestoreMainWindow();
        _notifyIcon.BalloonTipClicked += (_, _) => RestoreMainWindow();
    }

    private static void UpdateIcon()
    {
        if (_notifyIcon is null) return;

        try
        {
            _notifyIcon.Visible = ShouldStayInTray;
            _notifyIcon.Text = BuildTooltip();
        }
        catch (Exception ex)
        {
            // Text 超长（不同 .NET 版本上限不同）等情况：图标还在，只是悬停文字退回默认
            Debug.WriteLine($"[TrayIcon] 托盘图标文本设置失败（已忽略）: {ex.Message}");
        }

        RebuildMenu();
    }

    private static string BuildTooltip()
    {
        var appTitle = LocalizationService.L("App_Title", "图吧工具箱CE");

        var text = _backgroundToolName is null
            ? string.Format(LocalizationService.L("Tray_TooltipResident", "{0}：已最小化到系统托盘"), appTitle)
            : string.Format(LocalizationService.L("Tray_TooltipTool", "{0}：{1} 正在后台运行"), appTitle, _backgroundToolName);

        return text.Length > TooltipMaxLength ? text[..TooltipMaxLength] : text;
    }

    private static void RebuildMenu()
    {
        if (_menu is null) return;

        _menu.Items.Clear();
        _menu.Items.Add(LocalizationService.L("Tray_OpenMainWindow", "打开主窗口"), null, (_, _) => RestoreMainWindow());

        if (_backgroundToolName is not null)
        {
            var stopText = string.Format(
                LocalizationService.L("Tray_StopToolAndExit", "停止「{0}」并退出"),
                _backgroundToolName);
            _menu.Items.Add(stopText, null, (_, _) => StopToolAndExit());
        }

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(LocalizationService.L("Tray_ExitApp", "退出图吧工具箱"), null, (_, _) => App.RequestExit());
    }

    private static void RestoreMainWindow()
    {
        if (App.MainWindow is not { } mainWindow)
        {
            Debug.WriteLine("[TrayIcon] 主窗口不存在，无法恢复（可能正在退出）");
            return;
        }

        // 托盘消息不一定落在 UI 线程上（NotifyIcon 的事件由 WinForms 消息循环派发），一律回 UI 线程
        if (!mainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            var restored = false;
            try
            {
                App.IsLiteMode = false;
                _restoreAction?.Invoke();
                restored = mainWindow.RestoreFromTray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrayIcon] 恢复主窗口失败: {ex.Message}");
            }
            finally
            {
                // 只有窗口真的显示出来才收起「常驻托盘」标记：恢复失败还收起图标，
                // 用户会同时失去窗口和托盘入口，只能去任务管理器结束进程
                if (restored) OnMainWindowRestored();
            }
        }))
        {
            Debug.WriteLine("[TrayIcon] 恢复主窗口失败：UI 队列已停止");
        }
    }

    private static void StopToolAndExit()
    {
        var stop = _stopAction;
        _backgroundToolName = null;
        _stopAction = null;
        // 旧实现同样会清掉 lite 标记：遥测最后一个事件在退出收尾时上报，别把它记成「后台模式」
        App.IsLiteMode = false;

        var queue = App.MainWindow?.DispatcherQueue;
        void RunStop()
        {
            try { stop?.Invoke(); }
            catch (Exception ex) { Debug.WriteLine($"[TrayIcon] 停止后台工具失败: {ex.Message}"); }
        }

        if (queue is null || queue.HasThreadAccess) RunStop();
        else queue.TryEnqueue(RunStop);

        App.RequestExit();
    }

    private static Icon GetIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            try { return new Icon(iconPath); }
            catch (Exception ex) { Debug.WriteLine($"[TrayIcon] 加载应用图标失败，改用系统图标: {ex.Message}"); }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
