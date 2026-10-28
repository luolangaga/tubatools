using System.Diagnostics;
using System.Runtime.InteropServices;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

/// <summary>
/// 系统托盘图标（全进程唯一实例）。托盘常驻有三种理由，彼此独立、共用一个图标：
/// 1. 设置里开启了「关闭时最小化到系统托盘」—— 由 <see cref="SetTrayEnabled"/> 标记（窗口可见时就常驻）；
/// 2. 关闭主窗口后留在托盘 —— 由 <see cref="ShowForCloseToTray"/> 标记；
/// 3. 内置工具在后台运行（流量监控器 / 网络调度器 / 游戏防晕3D …）—— 由各页面调用 <see cref="Show"/>。
///
/// 任一理由成立图标就可见；理由全部消失时自动收起，不留一个点了没反应的托盘图标。
/// 托盘菜单：打开主窗口 / [停止「工具名」并退出] / 退出图吧工具箱。
///
/// <b>实现直接调 Win32 Shell_NotifyIcon（隐藏消息窗口 + TrackPopupMenu）</b>，不用
/// System.Windows.Forms.NotifyIcon：后者需要 FrameworkReference
/// Microsoft.WindowsDesktop.App.WindowsForms，会把整个 WindowsDesktop 共享框架
/// （System.Windows.Forms / Forms.Design / Accessibility 等，约 26 MB）打进自包含产物，
/// 而这些代码一个类型都用不上。
///
/// <b>退出必须走 <see cref="App.RequestExit"/></b>，不要再像早期实现那样直接 Process.Kill：
/// 杀进程会跳过 MainWindow_Closed 的完整清理 —— FPS 的 ETW 内核会话不会随进程终止自动回收，
/// 残留会让下次启动的帧率采集失效，遥测队列里的数据也会丢。
/// </summary>
public static class TrayIconService
{
    /// <summary>「关闭后留在托盘」生效中（主窗口已隐藏）。</summary>
    private static bool _residentInTray;

    /// <summary>设置里开启了「关闭时最小化到系统托盘」：窗口还在前台也常驻托盘（开启即注册）。</summary>
    private static bool _trayEnabled;

    /// <summary>正在后台运行的内置工具名；null = 没有后台工具。</summary>
    private static string? _backgroundToolName;

    private static Action? _stopAction;
    private static Action? _restoreAction;

    /// <summary>托盘菜单是否正在显示（TrackPopupMenu 是嵌套模态循环，禁止重入）。</summary>
    private static bool _menuOpen;

    /// <summary>气泡/悬停文本截断长度。NOTIFYICONDATAW.szTip 为 128 个字符（含结尾），
    /// 这里保守取 62：旧 NotifyIcon 时代就按这个长度截断，保持不变。</summary>
    private const int TooltipMaxLength = 62;

    /// <summary>气泡正文 / 标题上限（szInfo 256、szInfoTitle 64，均含结尾）。</summary>
    private const int BalloonTextMaxLength = 200;
    private const int BalloonTitleMaxLength = 60;

    /// <summary>是否需要常驻托盘（任一理由成立）。</summary>
    private static bool ShouldStayInTray => _trayEnabled || _residentInTray || _backgroundToolName is not null;

    public static bool IsVisible => _iconAdded && ShouldStayInTray;

    /// <summary>
    /// 设置「关闭时最小化到系统托盘」开关状态变化（开启即注册，不等第一次关闭窗口）。
    /// 关闭开关时图标收起，除非还有后台工具在跑。
    /// </summary>
    public static void SetTrayEnabled(bool enabled)
    {
        _trayEnabled = enabled;
        UpdateIcon();
    }

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

        UpdateIcon();
    }

    /// <summary>主窗口已关闭并隐藏到托盘（「关闭时最小化到系统托盘」）。</summary>
    public static void ShowForCloseToTray()
    {
        _residentInTray = true;
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
        _trayEnabled = false;
        _residentInTray = false;
        _backgroundToolName = null;
        _stopAction = null;
        _restoreAction = null;

        UpdateIcon();
    }

    public static void Dispose()
    {
        RemoveIcon();
        Log("托盘图标已移除，开始销毁隐藏消息窗口");

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_iconHandle != IntPtr.Zero)
        {
            // 共享图标（LoadIcon 返回的系统图标）不归我们所有，DestroyIcon 会破坏系统图标缓存
            if (_iconOwned) DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
            _iconOwned = false;
        }

        if (_classAtom != 0)
        {
            UnregisterClassW(ClassName, GetModuleHandleW(null));
            _classAtom = 0;
        }

        _trayEnabled = false;
        _residentInTray = false;
        _backgroundToolName = null;
        _stopAction = null;
        _restoreAction = null;
    }

    /// <summary>气泡通知（首次最小化到托盘时的说明）。</summary>
    public static void ShowBalloon(string title, string text, int timeoutMs = 5000)
    {
        if (!_iconAdded) return;

        var data = NewData();
        data.uFlags = NIF_INFO;
        data.szInfoTitle = Truncate(title, BalloonTitleMaxLength);
        data.szInfo = Truncate(text, BalloonTextMaxLength);
        data.uVersionOrTimeout = (uint)timeoutMs;
        data.dwInfoFlags = NIIF_INFO;

        try
        {
            if (!Shell_NotifyIconW(NIM_MODIFY, ref data))
                Debug.WriteLine("[TrayIcon] 气泡提示发送失败（已忽略）");
        }
        catch (Exception ex)
        {
            // 通知中心被策略禁用等情况：提示看不到不影响功能本身
            Debug.WriteLine($"[TrayIcon] 气泡提示失败（已忽略）: {ex.Message}");
        }
    }

    // ---------- 托盘图标（Shell_NotifyIcon） ----------

    private const string ClassName = "TubaWinUi3.TrayMessageWindow";

    private const uint WM_APP = 0x8000;
    private const uint WM_TRAYICON = WM_APP + 1;
    private const uint WM_NULL = 0x0000;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;

    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NIIF_INFO = 0x00000001;

    /// <summary>托盘图标 id（同一 hWnd 下唯一，用来定位/更新/删除图标）。</summary>
    private const uint TrayIconId = 1;

    private const int MenuOpen = 1001;
    private const int MenuStop = 1002;
    private const int MenuExit = 1003;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;

    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_RETURNCMD = 0x0100;

    private const uint WS_POPUP = 0x80000000;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTSIZE = 0x00000040;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static IntPtr _hwnd;
    private static ushort _classAtom;
    private static IntPtr _iconHandle;

    /// <summary>_iconHandle 是否由我们创建（LoadImage/ExtractIcon）——只有这种才能 DestroyIcon。</summary>
    private static bool _iconOwned;
    private static bool _iconAdded;
    private static uint _taskbarCreatedMessage;

    /// <summary>持有委托，防止 GC 回收后原生窗口过程回调到野指针。</summary>
    private static WndProcDelegate? _wndProc;

    private static void EnsureWindow()
    {
        if (_hwnd != IntPtr.Zero) return;

        var hInstance = GetModuleHandleW(null);
        _wndProc = WindowProc;

        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = ClassName
        };

        // 已经注册过（进程内不会发生，但重入时不应失败）→ 忽略返回值
        _classAtom = RegisterClassExW(ref wc);
        if (_classAtom == 0)
            Debug.WriteLine($"[TrayIcon] 窗口类注册失败: {Marshal.GetLastWin32Error()}");

        // 不可见的顶层窗口（不是 message-only：TrackPopupMenu 需要能把菜单窗口置前）
        _hwnd = CreateWindowExW(0, ClassName, ClassName, WS_POPUP, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            Log($"隐藏消息窗口创建失败，GLE={Marshal.GetLastWin32Error()}");
            return;
        }

        // explorer.exe 重启后托盘会被清空，需要收到 TaskbarCreated 重新添加
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            // 未调用 NIM_SETVERSION → 回调里 lParam 低字就是鼠标消息
            var mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);

            if (mouseMessage == WM_LBUTTONDBLCLK)
                RestoreMainWindow();
            else if (mouseMessage is WM_RBUTTONUP or WM_CONTEXTMENU)
                ShowContextMenu();

            return IntPtr.Zero;
        }

        if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            _iconAdded = false;
            UpdateIcon();
            return IntPtr.Zero;
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static void UpdateIcon()
    {
        if (!ShouldStayInTray)
        {
            RemoveIcon();
            return;
        }

        EnsureWindow();
        if (_hwnd == IntPtr.Zero) return;

        if (_iconHandle == IntPtr.Zero)
            _iconHandle = LoadTrayIcon();

        var data = NewData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = WM_TRAYICON;
        data.hIcon = _iconHandle;
        data.szTip = Truncate(BuildTooltip(), TooltipMaxLength);

        var operation = _iconAdded ? NIM_MODIFY : NIM_ADD;
        try
        {
            if (Shell_NotifyIconW(operation, ref data))
            {
                _iconAdded = true;
            }
            else
            {
                // 旧 NotifyIcon 在资源管理器未就绪时会静默失败；这里留日志，下次状态变化会重试
                Log($"托盘图标{(operation == NIM_ADD ? "添加" : "更新")}失败，GLE={Marshal.GetLastWin32Error()}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TrayIcon] 托盘图标更新失败（已忽略）: {ex.Message}");
        }
    }

    private static void RemoveIcon()
    {
        if (!_iconAdded || _hwnd == IntPtr.Zero)
        {
            _iconAdded = false;
            return;
        }

        var data = NewData();
        try { Shell_NotifyIconW(NIM_DELETE, ref data); }
        catch (Exception ex) { Debug.WriteLine($"[TrayIcon] 托盘图标移除失败（已忽略）: {ex.Message}"); }

        _iconAdded = false;
    }

    private static NOTIFYICONDATAW NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = TrayIconId,
        szTip = "",
        szInfo = "",
        szInfoTitle = ""
    };

    private static void ShowContextMenu()
    {
        if (_hwnd == IntPtr.Zero) return;

        // TrackPopupMenu 是嵌套模态消息循环：重入会出现两个菜单叠着，直接忽略
        if (_menuOpen) return;

        var hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        _menuOpen = true;
        try
        {
            AppendMenuW(hMenu, MF_STRING, MenuOpen,
                LocalizationService.L("Tray_OpenMainWindow", "打开主窗口"));

            if (_backgroundToolName is not null)
            {
                var stopText = string.Format(
                    LocalizationService.L("Tray_StopToolAndExit", "停止「{0}」并退出"),
                    _backgroundToolName);
                AppendMenuW(hMenu, MF_STRING, MenuStop, stopText);
            }

            AppendMenuW(hMenu, MF_SEPARATOR, 0, null);
            AppendMenuW(hMenu, MF_STRING, MenuExit,
                LocalizationService.L("Tray_ExitApp", "退出图吧工具箱"));

            GetCursorPos(out var cursor);

            // 菜单不会自动消失的老问题：弹出前抢前台、弹完补一条空消息
            SetForegroundWindow(_hwnd);
            var command = TrackPopupMenu(hMenu,
                TPM_LEFTALIGN | TPM_BOTTOMALIGN | TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
                cursor.X, cursor.Y, 0, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            // 动作一律排到 UI 队列里执行，绝不在菜单的模态循环内跑：
            // 退出/停止会走到 MainWindow_Closed 的整套清理（ETW、LiteMonitor、托盘销毁、
            // Application.Exit），在 TrackPopupMenu 未返回时拆 UI 会留下未展开的原生栈，
            // 曾经表现为进程退出时的 COM（combase 0x80070057）与堆损坏（0xc0000374）崩溃。
            switch (command)
            {
                case MenuOpen:
                    RestoreMainWindow();
                    break;
                case MenuStop:
                    RunOnUiThread(StopToolAndExit);
                    break;
                case MenuExit:
                    RunOnUiThread(App.RequestExit);
                    break;
            }
        }
        finally
        {
            _menuOpen = false;
            DestroyMenu(hMenu);
        }
    }

    /// <summary>把动作排到主窗口的 UI 队列（不在原生回调/模态循环里执行）。</summary>
    private static void RunOnUiThread(Action action)
    {
        var queue = App.MainWindow?.DispatcherQueue;
        if (queue is null)
        {
            // 主窗口已不存在（正在退出）：没有队列可排，就地执行
            action();
            return;
        }

        if (!queue.TryEnqueue(() => action()))
        {
            Debug.WriteLine("[TrayIcon] UI 队列已停止，动作改为就地执行");
            action();
        }
    }

    private static IntPtr LoadTrayIcon()
    {
        _iconOwned = false;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                var handle = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
                if (handle != IntPtr.Zero)
                {
                    _iconOwned = true;
                    return handle;
                }
                Debug.WriteLine($"[TrayIcon] 加载应用图标失败: {Marshal.GetLastWin32Error()}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrayIcon] 加载应用图标失败，改用系统图标: {ex.Message}");
            }
        }

        // 回退：进程自身 exe 的图标 → 系统默认应用图标
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                var handle = ExtractIconW(IntPtr.Zero, exePath, 0);
                if (handle != IntPtr.Zero && handle != new IntPtr(1))
                {
                    _iconOwned = true;
                    return handle;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TrayIcon] 提取 exe 图标失败（已忽略）: {ex.Message}");
        }

        // LoadIcon 返回共享图标：不归我们所有，Dispose 时绝不能 DestroyIcon
        return LoadIconW(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
    }

    private static string BuildTooltip()
    {
        var appTitle = LocalizationService.L("App_Title", "图吧工具箱CE");

        if (_backgroundToolName is not null)
            return string.Format(LocalizationService.L("Tray_TooltipTool", "{0}：{1} 正在后台运行"), appTitle, _backgroundToolName);

        return _residentInTray
            ? string.Format(LocalizationService.L("Tray_TooltipResident", "{0}：已最小化到系统托盘"), appTitle)
            : string.Format(LocalizationService.L("Tray_TooltipEnabled", "{0}：正在运行，关闭窗口后留在托盘"), appTitle);
    }

    private static string Truncate(string text, int maxLength)
        => text.Length > maxLength ? text[..maxLength] : text;

    /// <summary>托盘侧异常/生命周期写进崩溃日志：原生崩溃没有托管栈，只能靠这些线索定位。</summary>
    private static void Log(string message)
    {
        try
        {
            LogService.Info(LogService.Crash, "[托盘] " + message);
        }
        catch
        {
            // 日志写不进去不影响托盘本身
        }
    }

    private static void RestoreMainWindow()
    {
        if (App.MainWindow is not { } mainWindow)
        {
            Debug.WriteLine("[TrayIcon] 主窗口不存在，无法恢复（可能正在退出）");
            return;
        }

        // 托盘消息由隐藏窗口的消息循环派发，与 UI 线程不一定一致，一律回 UI 线程
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

    // ---------- Win32 ----------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, int uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved,
        IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIconW(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);
}
