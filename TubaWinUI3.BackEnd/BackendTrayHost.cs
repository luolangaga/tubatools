using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TubaWinUI3.BackEnd;

/// <summary>
/// 后端系统托盘（对应 ContextMenuMgr 的 TrayHost 角色，但在后端进程内实现）：
/// 隐匿窗口 + Shell_NotifyIcon 常驻系统托盘，让用户在未打开主程序时也能
/// 一键打开「主动拦截审核页」、打开数据目录或优雅退出后端。
/// 图标：优先提取主程序（TubaWinUi3.exe）图标，失败时回退系统默认应用图标。
/// </summary>
internal sealed class BackendTrayHost : IDisposable
{
#pragma warning disable SYSLIB1054

    private const int WM_USER = 0x0400;
    private const int WM_TRAYCALLBACK = WM_USER + 20;
    private const uint WM_COMMAND = 0x0111;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint TPM_RIGHTBUTTON = 0x00000002;
    private const uint TPM_RETURNCMD = 0x00000100;
    private const uint WM_NULL = 0x0000;

    private const int ID_OPEN = 1001;
    private const int ID_DATA = 1002;
    private const int ID_EXIT = 1003;

    private static readonly IntPtr IdIApplication = new(32512);
    private static readonly ConcurrentDictionary<IntPtr, BackendTrayHost> Windows = new();

    private readonly string _tip;
    private readonly string _dataDir;
    private readonly Action _shutdown;
    /// <summary>主动拦截功能是否启用（决定托盘菜单文案与主程序跳转参数）。</summary>
    private readonly bool _interceptEnabled;
    private readonly object _lifeLock = new();
    private Thread? _thread;
    private IntPtr _hwnd;
    private IntPtr _hicon;
    private bool _iconOwned;
    private bool _disposed;

    /// <summary>窗口过程委托必须持有强引用，防止被 GC 回收导致崩溃。</summary>
    private static readonly WndProcDelegate WndProcHolder = WndProc;

    /// <summary>Explorer 重启广播（TaskbarCreated）：收到后须重新添加托盘图标，否则图标永久消失。</summary>
    private static uint _taskbarCreatedMsg;

    public BackendTrayHost(string tooltip, string dataDir, bool interceptEnabled, Action shutdown)
    {
        _tip = tooltip;
        _dataDir = dataDir;
        _interceptEnabled = interceptEnabled;
        _shutdown = shutdown;
    }

    public void Start()
    {
        lock (_lifeLock)
        {
            if (_disposed || _thread is not null) return;
            _thread = new Thread(TrayThreadProc)
            {
                IsBackground = true,
                Name = "TubaWinUi3BackendTray",
            };
            _thread.Start();
        }
    }

    public void Dispose()
    {
        Thread? thread;
        IntPtr hwnd;
        lock (_lifeLock)
        {
            if (_disposed) return;
            _disposed = true;
            thread = _thread;
            _thread = null;
            hwnd = _hwnd;
        }

        if (thread is not null)
        {
            try
            {
                if (hwnd != IntPtr.Zero)
                {
                    PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
                if (!thread.Join(3000))
                {
                    try { } catch { }
                }
            }
            catch
            {
                // 已退出
            }
        }
        else if (hwnd != IntPtr.Zero)
        {
            RemoveIconAndWindow();
        }
    }

    // ---------- 托盘线程 ----------

    private void TrayThreadProc()
    {
        try
        {
            var hInstance = GetModuleHandleW(null);
            const string className = "TubaWinUi3BackendTrayHost";

            // TaskbarCreated 广播消息 ID（每进程一次）：Explorer 重启后任务栏重建，
            // 不监听这个消息的话托盘图标会永久消失（表现为「托盘图标没了/点不了」）。
            if (_taskbarCreatedMsg == 0)
                _taskbarCreatedMsg = RegisterWindowMessageW("TaskbarCreated");

            var wndClass = new WNDCLASS
            {
                style = 0,
                lpfnWndProc = WndProcHolder,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = hInstance,
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = className,
            };
            if (RegisterClassW(ref wndClass) == 0 && Marshal.GetLastWin32Error() != 1410 /* ERROR_CLASS_ALREADY_EXISTS */)
            {
                BackEndLog.Warn($"托盘：注册窗口类失败 GLE={Marshal.GetLastWin32Error()}");
                return;
            }

            _hwnd = CreateWindowExW(0, className, className, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                BackEndLog.Warn($"托盘：创建窗口失败 GLE={Marshal.GetLastWin32Error()}");
                return;
            }
            Windows[_hwnd] = this;

            _hicon = ExtractMainAppIcon();
            if (_hicon == IntPtr.Zero)
            {
                // 系统共享图标（勿 DestroyIcon）
                _hicon = LoadIconW(IntPtr.Zero, IdIApplication);
                _iconOwned = false;
            }

            // 等待 Explorer 通知区域就绪（开机自启时 Explorer 可能还没初始化完）。
            // 最多重试 30 次，每次间隔 1 秒，共 30 秒。
            var nid = BuildNotifyIconData();
            const int maxRetries = 30;
            bool trayAdded = false;
            for (int i = 0; i < maxRetries; i++)
            {
                if (Shell_NotifyIconW(NIM_ADD, ref nid))
                {
                    trayAdded = true;
                    break;
                }
                // 检查是否被要求退出（Dispose 发送了 WM_CLOSE）
                if (_disposed) return;
                // 检查 Explorer 托盘窗口是否存在，不存在则等 Explorer 启动
                if (FindWindowW("Shell_TrayWnd", null) == IntPtr.Zero)
                {
                    BackEndLog.Info($"托盘：等待 Explorer 就绪（{i + 1}/{maxRetries}）...");
                }
                Thread.Sleep(1000);
            }
            if (!trayAdded)
            {
                BackEndLog.Warn($"托盘：Shell_NotifyIcon 添加失败（重试 {maxRetries} 次），GLE={Marshal.GetLastWin32Error()}");
                RemoveIconAndWindow();
                return;
            }

            BackEndLog.Info("系统托盘已启动：左键/双击打开主程序审核页，右键菜单管理");

            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }

            RemoveIconAndWindow();
        }
        catch (Exception ex)
        {
            BackEndLog.Warn($"托盘线程异常：{ex.Message}");
            RemoveIconAndWindow();
        }
    }

    /// <summary>Explorer 重启后重新添加托盘图标（TaskbarCreated 广播到达时调用，托盘线程）。</summary>
    private void ReAddTrayIcon()
    {
        try
        {
            if (_hwnd == IntPtr.Zero || _disposed) return;
            var nid = BuildNotifyIconData();
            if (Shell_NotifyIconW(NIM_ADD, ref nid))
            {
                BackEndLog.Info("托盘：Explorer 已重启，图标重新注册");
            }
            else
            {
                BackEndLog.Warn($"托盘：图标重注册失败 GLE={Marshal.GetLastWin32Error()}");
            }
        }
        catch (Exception ex)
        {
            BackEndLog.Warn($"托盘：图标重注册异常 {ex.Message}");
        }
    }

    // ---------- 图标 ----------

    private IntPtr ExtractMainAppIcon()
    {
        try
        {
            var exe = NotificationHelper.FindMainAppExe();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return IntPtr.Zero;

            var info = new SHFILEINFO { szDisplayName = "", szTypeName = "" };
            if (SHGetFileInfoW(exe, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON))
            {
                _iconOwned = true;
                return info.hIcon;
            }
        }
        catch
        {
            // 提取失败回退默认图标
        }
        return IntPtr.Zero;
    }

    private NOTIFYICONDATA BuildNotifyIconData()
    {
        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = (uint)WM_TRAYCALLBACK,
            hIcon = _hicon,
            szTip = string.IsNullOrWhiteSpace(_tip) ? "图吧工具箱CE · 主动拦截" : (_tip.Length > 63 ? _tip[..63] : _tip),
        };
        return nid;
    }

    private void RemoveIconAndWindow()
    {
        try
        {
            if (_hwnd != IntPtr.Zero)
            {
                Windows.TryRemove(_hwnd, out _);
                var nid = new NOTIFYICONDATA
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = 1,
                };
                Shell_NotifyIconW(NIM_DELETE, ref nid);
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            if (_hicon != IntPtr.Zero && _iconOwned)
            {
                DestroyIcon(_hicon);
                _hicon = IntPtr.Zero;
            }
        }
        catch
        {
            // 退出清理尽力而为
        }
    }

    // ---------- 窗口过程 ----------

    private static IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
            if (Windows.TryGetValue(hWnd, out var host))
            {
                // Explorer 重启 → 任务栏重建 → 重新添加图标（NIM_ADD 幂等，失败也不影响后续）
                if (_taskbarCreatedMsg != 0 && message == _taskbarCreatedMsg)
                {
                    host.ReAddTrayIcon();
                    return IntPtr.Zero;
                }

                switch (message)
                {
                case (uint)WM_TRAYCALLBACK:
                    switch ((uint)lParam)
                    {
                        case WM_LBUTTONUP:
                        case WM_LBUTTONDBLCLK:
                            host.OpenFrontend();
                            return IntPtr.Zero;
                        case WM_RBUTTONUP:
                            host.ShowContextMenu();
                            return IntPtr.Zero;
                    }
                    break;

                case WM_COMMAND:
                    switch (wParam.ToInt32() & 0xFFFF)
                    {
                        case ID_OPEN:
                            host.OpenFrontend();
                            return IntPtr.Zero;
                        case ID_DATA:
                            host.OpenDataDirectory();
                            return IntPtr.Zero;
                        case ID_EXIT:
                            host.RequestExit();
                            return IntPtr.Zero;
                    }
                    break;

                case WM_CLOSE:
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
        }

        return DefWindowProcW(hWnd, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        try
        {
            var menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) return;

            AppendMenuW(menu, MF_STRING, (IntPtr)ID_OPEN,
                _interceptEnabled ? "打开主动拦截审核页" : "打开图吧工具箱");
            AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null);
            AppendMenuW(menu, MF_STRING, (IntPtr)ID_DATA, "打开数据目录");
            AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null);
            AppendMenuW(menu, MF_STRING, (IntPtr)ID_EXIT, "退出后端");

            // 关键：TrackPopupMenu 弹出前必须把托盘窗口设为前台（微软官方要求，
            // 否则前台是全屏游戏/其他窗口时菜单弹不出来或被立即取消 —— 「右键没反应」的根源）。
            SetForegroundWindow(_hwnd);

            if (!GetCursorPos(out var pt))
            {
                pt = new POINT { X = 0, Y = 0 };
            }

            var cmd = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, _hwnd, IntPtr.Zero);

            // 经典配套处理：菜单关闭后补一个 WM_NULL，否则下一次弹出可能挂住
            PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch ((int)cmd)
            {
                case ID_OPEN: OpenFrontend(); break;
                case ID_DATA: OpenDataDirectory(); break;
                case ID_EXIT: RequestExit(); break;
            }

            DestroyMenu(menu);
        }
        catch (Exception ex)
        {
            BackEndLog.Warn($"托盘菜单异常：{ex.Message}");
        }
    }

    // ---------- 动作 ----------

    private void OpenFrontend()
    {
        try
        {
            var exe = NotificationHelper.FindMainAppExe();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                BackEndLog.Warn("托盘：找不到主程序，改为打开数据目录");
                OpenDataDirectory();
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = _interceptEnabled ? "--show-active-intercept" : "",
                UseShellExecute = true,
            });
            BackEndLog.Info("托盘：已启动主程序");
        }
        catch (Exception ex)
        {
            BackEndLog.Warn($"托盘：启动主程序失败 {ex.Message}");
        }
    }

    private void OpenDataDirectory()
    {
        try
        {
            // 拦截功能的数据在 active_intercept 子目录；纯游戏监控模式直接开数据根目录
            var dir = _interceptEnabled
                ? Path.Combine(_dataDir, "active_intercept")
                : _dataDir;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            BackEndLog.Warn($"托盘：打开数据目录失败 {ex.Message}");
        }
    }

    private void RequestExit()
    {
        BackEndLog.Info("托盘：用户请求退出后端");
        try { _shutdown?.Invoke(); } catch { }
        PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    // ---------- P/Invoke ----------

    private const uint SHGFI_ICON = 0x00000100;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public WndProcDelegate? lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    // 注意：Shell_NotifyIcon 的 W/A 变体导出自 shell32.dll（user32.dll 中不存在该入口点，
    // NativeAOT 下会抛 EntryPointNotFoundException 导致托盘线程崩溃、图标不显示）。
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern bool GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

#pragma warning restore SYSLIB1054
}