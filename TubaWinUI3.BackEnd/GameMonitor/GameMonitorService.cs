using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TubaWinUI3.BackEnd.GameMonitor;

/// <summary>
/// 游戏后台自动监控编排（2026-09-11 简化）：
/// 1. 检测线程每 2 秒轮询前台窗口，判定「这是全屏游戏」；
/// 2. 判定成立（连续 2 次命中）→ 经 FrontendOverlayBridge 写信号文件并确保主程序运行，
///    由主程序用现成的 GameOverlayWindow 显示悬浮窗并绑定游戏窗口；
///    判定失效（连续 3 次脱靶或窗口销毁）→ 写 inactive 信号，主程序关闭悬浮窗。
///
/// 简化历程：最初检测还要求该进程 ETW present ≥10 FPS（三重判据），但后端 ETW 会话
/// 在部分机器/驱动组合下收不到 present 事件（FPS 恒为 0 → 永不命中），且引入管理员
/// 权限门槛。实际上 FPS 由主程序悬浮窗自己统计，检测层只需要回答「前台是不是全屏」，
/// 故砍掉 ETW 判据 —— 后端不再有任何 ETW / admin 依赖。
///
/// 「是游戏」判据（两重，全部满足才算命中）：
///   a. 前台窗口属于非排除进程（外壳/浏览器/播放器等一律不算）；
///   b. 窗口接近全屏（≥93% 显示器面积 或 ≥97% 工作区面积 —— 覆盖独占全屏与无边框窗口；
///      带 WS_CAPTION 的普通窗口直接排除，浏览器全屏视频靠进程排除名单拦截）。
/// 迟滞设计：加载画面/切桌面/暂停菜单短暂脱靶不闪断。
/// </summary>
internal sealed class GameMonitorService : IDisposable
{
    private const int PollIntervalMs = 2000;
    private const int ConfirmHits = 2;      // 连续命中次数 → 确认为游戏
    private const int MissTolerance = 3;    // 连续脱靶次数 → 判定退出游戏

    private readonly string _dataDir;
    private FrontendOverlayBridge? _bridge;
    private Thread? _pollThread;
    private volatile bool _stop;

    // 状态机（仅检测线程访问）
    private int _hits;
    private int _misses;
    private int _untickCount;
    private IntPtr _activeGameHwnd;
    private uint _activeGamePid;

    public GameMonitorService(Models.BackendConfig config)
    {
        _dataDir = config.DataDir;
    }

    public void Start()
    {
        _bridge = new FrontendOverlayBridge(_dataDir);

        _pollThread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name = "BackEnd-GameDetector",
        };
        _pollThread.Start();
        BackEndLog.Info("游戏监控：检测线程已启动");
    }

    private void PollLoop()
    {
        while (!_stop)
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                BackEndLog.Warn($"游戏监控：检测轮询异常 {ex.Message}");
            }

            Thread.Sleep(PollIntervalMs);
        }
    }

    private void Tick()
    {
        var candidate = DetectForegroundGame();

        if (candidate is not null)
        {
            _untickCount = 0;
            var (hwnd, pid, proc) = candidate.Value;
            _hits++;
            _misses = 0;

            if (_hits >= ConfirmHits)
            {
                if (_activeGameHwnd != hwnd)
                {
                    // 切换到新游戏窗口
                    _activeGameHwnd = hwnd;
                    _activeGamePid = pid;
                    _bridge?.NotifyGame(hwnd, pid, proc);
                    BackEndLog.Info($"游戏监控：检测到全屏游戏 {proc}（PID {pid}），已通知主程序显示覆盖层");
                }
                else
                {
                    // 游戏持续在前台：心跳刷新信号时间戳（内部 6s 节流），
                    // 否则前端 60s 后判定后端已死、误关悬浮窗。
                    _bridge?.KeepAlive();
                }
            }
        }
        else
        {
            _hits = 0;
            _untickCount++;
            // 未命中诊断：每 150 个 tick（约 5 分钟）记录一次前台窗口的判定明细，
            // 「开了游戏但不弹」时看这条就能定位卡在哪一关（进程排除/全屏/FPS）。
            // 别调回 30 秒级别：不开游戏时那等于一整天全在写心跳。
            if (_untickCount >= 150)
            {
                _untickCount = 0;
                BackEndLog.Info($"游戏监控诊断：{DescribeForeground()}");
            }

            if (_activeGameHwnd != IntPtr.Zero)
            {
                // 关键：前台丢失 ≠ 游戏退出。搜索弹窗/用户切桌面/弹窗抢焦点都会让
                // 前台暂时不是游戏 —— 只要游戏窗口还活着且可见未最小化，就继续心跳，
                // 悬浮窗照常显示（它本来就是置顶的）。只有窗口销毁/最小化/被 cloak
                // 才计退出，宽限期 3 个 tick（6s）防抖。
                bool windowGone = !WinApi.IsWindow(_activeGameHwnd)
                                  || WinApi.IsIconic(_activeGameHwnd)
                                  || IsWindowCloaked(_activeGameHwnd);
                if (windowGone)
                {
                    _misses++;
                    if (_misses >= MissTolerance)
                    {
                        BackEndLog.Info("游戏监控：游戏窗口已关闭/最小化，通知主程序关闭覆盖层");
                        _bridge?.NotifyExit();
                        _activeGameHwnd = IntPtr.Zero;
                        _activeGamePid = 0;
                        _misses = 0;
                    }
                }
                else
                {
                    _misses = 0;
                    _bridge?.KeepAlive();
                }
            }
        }
    }

    /// <summary>前台窗口判定明细（诊断日志用，低频调用）。</summary>
    private string DescribeForeground()
    {
        try
        {
            var hwnd = WinApi.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return "无前台窗口";

            if (!WinApi.GetWindowThreadProcessId(hwnd, out uint pid) || pid == 0) return $"hwnd={hwnd} 无PID";
            if (pid == (uint)Environment.ProcessId) return "前台=后端自身";

            string proc = "?";
            try { proc = Process.GetProcessById((int)pid).ProcessName; } catch { }

            bool visible = WinApi.IsWindowVisible(hwnd);
            bool iconic = WinApi.IsIconic(hwnd);
            bool cloaked = IsWindowCloaked(hwnd);
            long style = WinApi.GetWindowStyle(hwnd);
            bool caption = (style & WinApi.WS_CAPTION) != 0;
            bool full = IsForegroundFullScreen(hwnd, out double mc, out double wc);
            bool excluded = GameProcessFilter.IsExcluded(proc);

            return $"前台={proc}({pid}) 可见={visible} 最小化={iconic} cloak={cloaked} " +
                   $"标题栏={caption} 全屏={full}(显示器{mc:P0}/工作区{wc:P0}) 排除={excluded}";
        }
        catch (Exception ex)
        {
            return $"诊断异常 {ex.Message}";
        }
    }

    /// <summary>前台窗口游戏判定；命中返回 (hwnd, pid, 进程名)。</summary>
    private (IntPtr Hwnd, uint Pid, string Proc)? DetectForegroundGame()
    {
        var hwnd = WinApi.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        if (!WinApi.GetWindowThreadProcessId(hwnd, out uint pid) || pid == 0) return null;
        if (pid == (uint)Environment.ProcessId) return null;

        // 窗口不可见 / 最小化 / 被遮蔽（cloak）→ 不是游戏画面
        if (!WinApi.IsWindowVisible(hwnd) || WinApi.IsIconic(hwnd) || IsWindowCloaked(hwnd)) return null;

        string? proc = GetProcessName((int)pid);
        if (string.IsNullOrEmpty(proc) || GameProcessFilter.IsExcluded(proc)) return null;

        // 全屏判定：窗口矩形 vs 显示器矩形 / 工作区
        if (!IsForegroundFullScreen(hwnd, out double monitorCover, out double workCover)) return null;

        _ = monitorCover;
        _ = workCover;
        return (hwnd, pid, proc);
    }

    /// <summary>
    /// 前台窗口是否接近全屏：≥93% 显示器面积（独占全屏/无边框），或 ≥97% 工作区面积
    /// （任务栏自动隐藏/置顶场景下的无边框窗口）。
    /// 带 WS_CAPTION 的标准应用窗口（浏览器/编辑器等最大化窗口）直接排除 ——
    /// 否则最大化的 WorkBuddy 之类应用会因工作区覆盖率 ≥97% 被误判成游戏。
    /// </summary>
    internal static bool IsForegroundFullScreen(IntPtr hwnd, out double monitorCover, out double workCover)
    {
        monitorCover = workCover = 0;
        if (!WinApi.GetWindowRect(hwnd, out var wr)) return false;

        // 标准标题栏窗口不是全屏游戏（窗口化游戏也可能带标题栏，但它们本来
        // 就不是「自动覆盖」的目标场景——无边框/独占全屏才是）。
        long style = WinApi.GetWindowStyle(hwnd);
        if ((style & WinApi.WS_CAPTION) != 0) return false;

        var monitor = WinApi.MonitorFromWindow(hwnd, WinApi.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return false;

        var mi = new WinApi.MONITORINFO { cbSize = (uint)Marshal.SizeOf<WinApi.MONITORINFO>() };
        if (!WinApi.GetMonitorInfoW(monitor, ref mi)) return false;

        return IsForegroundFullScreenCore(wr, mi.rcMonitor, mi.rcWork, out monitorCover, out workCover);
    }

    /// <summary>纯几何版全屏判定（无窗口依赖，可单测）。</summary>
    internal static bool IsForegroundFullScreenCore(WinApi.RECT wr, WinApi.RECT monitorRect, WinApi.RECT workRect,
        out double monitorCover, out double workCover)
    {
        monitorCover = (double)AreaIntersect(wr, monitorRect) / Math.Max(1, Area(monitorRect));
        workCover = (double)AreaIntersect(wr, workRect) / Math.Max(1, Area(workRect));
        return monitorCover >= 0.93 || workCover >= 0.97;

        static long Area(WinApi.RECT r) => (long)Math.Max(0, r.Right - r.Left) * Math.Max(0, r.Bottom - r.Top);
        static long AreaIntersect(WinApi.RECT a, WinApi.RECT b) =>
            Area(new WinApi.RECT
            {
                Left = Math.Max(a.Left, b.Left),
                Top = Math.Max(a.Top, b.Top),
                Right = Math.Min(a.Right, b.Right),
                Bottom = Math.Min(a.Bottom, b.Bottom),
            });
    }

    private static bool IsWindowCloaked(IntPtr hwnd)
    {
        try
        {
            int cloaked = 0;
            int hr = WinApi.DwmGetWindowAttribute(hwnd, WinApi.DWMWA_CLOAKED, out cloaked, sizeof(int));
            return hr == 0 && cloaked != 0;
        }
        catch { return false; }
    }

    private static string? GetProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stop = true;
        try { _bridge?.NotifyExit(); } catch { }
    }
}

/// <summary>
/// 进程排除名单（游戏判定专用，与 ETW 监控的统计排除名单分开 —— 统计层可以
/// 记录浏览器帧数，但判定层绝不把浏览器/播放器/外壳当成游戏）。
/// </summary>
internal static class GameProcessFilter
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        // 桌面外壳与系统
        "explorer", "dwm", "winlogon", "csrss", "smss", "services", "lsass", "wininit",
        "svchost", "dllhost", "conhost", "taskhostw", "sihost", "ctfmon", "fontdrvhost",
        "RuntimeBroker", "ApplicationFrameHost", "SearchHost", "SearchApp",
        "ShellExperienceHost", "StartMenuExperienceHost", "TextInputHost",
        "Widgets", "WidgetService", "msedgewebview2",
        // 锁屏 / 登录界面（合盖或 Win+L 后 LockApp 会变成无边框全屏前台窗口，
        // 命中全屏判据 → 被误判成游戏并自动开始录制，2026-09-18 实测踩坑）
        "LockApp", "LogonUI", "Windows.UI.Logon", "CredentialUIBroker",
        // 系统 AI / 截屏辅助覆盖层（Click to Do 的 ClickToDo.exe 是无边框满屏浮层，
        // 命中全屏判据 → 被误判成游戏并自动开始录制，2026-09-19 实测踩坑；
        // 截图工具家族的取景浮层同理）
        "ClickToDo", "SnippingTool", "ScreenSketch", "ScreenClippingHost",
        // 浏览器（全屏视频/网页游戏会误报，宁可不算）
        "msedge", "chrome", "firefox", "iexplore", "opera", "brave", "vivaldi", "360se",
        "360chrome", "QQBrowser", "SogouExplorer", "baidunetdisk",
        // 视频/媒体播放器
        "vlc", "potplayer", "wmplayer", "mpc-hc", "mpc-hc64", "cinema", "dandanplay",
        "bilibili", "Netease_Music", "CloudMusic", "QQMusic", "KuGou", "KwMusic",
        // 工具箱自身
        "TubaWinUi3", "TubaWinUI3.BackEnd", "LiteMonitor", "PresentMon",
        // 教学白板（全屏授课界面会被误判成游戏 —— 2026-09-11 实测 EasyWhiteBoard 中招）
        "EasyWhiteBoard", "EasiNote", "EasiCamera", "Seewo",
        // 通讯与办公（全屏共享/视频会议不算游戏）
        "Teams", "WeChat", "QQ", "DingTalk", "Feishu", "Lark", "Zoom", "wemeetapp",
        "WINWORD", "EXCEL", "POWERPNT", "OUTLOOK", "mspaint", "notepad",
        // 系统与调试
        "System", "Idle", "MemCompression", "Registry", "Taskmgr", "devenv", "MSBuild",
        "PerfWatson2", "ServiceHub", "Code", "WindowsTerminal", "OpenConsole",
    };

    public static bool IsExcluded(string processName) => Excluded.Contains(processName);
}

/// <summary>检测循环用 Win32 声明（按需最小集合）。</summary>
internal static class WinApi
{
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int DWMWA_CLOAKED = 14;

    public const int GWL_STYLE = -16;
    public const long WS_CAPTION = 0x00C00000;   // WS_CAPTION = WS_BORDER | WS_DLGFRAME
    public const long WS_SYSMENU = 0x00080000;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    /// <summary>读窗口样式（兼容 32/64 位）。</summary>
    public static long GetWindowStyle(IntPtr hWnd) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, GWL_STYLE).ToInt64() : GetWindowLong32(hWnd, GWL_STYLE);
}
