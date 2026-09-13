using System.Diagnostics;
using System.Runtime.InteropServices;
using TubaWinUI3.BackEnd.GameMonitor;
using TubaWinUI3.BackEnd.Models;

namespace TubaWinUI3.BackEnd;

/// <summary>
/// 主动拦截后端入口（架构移植自 ContextMenuMgr 的 BackendRuntime 职责：
/// 存储 → 监视器 → 命名管道服务器，三者共享同一组存储实例并互相接线）。
/// 用法：TubaWinUI3.BackEnd.exe [--config &lt;路径&gt;] [--once] [--stop]
/// - --config：后端配置文件（JSON，含 DataDir / PollIntervalSeconds / LogFile / NotifyMode）。
/// - --once：只执行一轮扫描后退出（诊断/测试用）。
/// - --stop：通知已运行的后端正例退出。
/// 单实例：通过命名互斥锁保证同时只有一个后端进程运行。
/// </summary>
internal static class Program
{
    private const string MutexName = "Global\\TubaWinUi3_ActiveIntercept_Backend";

    private static async Task<int> Main(string[] args)
    {
        // 常驻托盘服务：隐藏自身控制台窗口（后台服务不弹黑窗口）。
        // 但 --help 时保留控制台方便查看用法。
        if (!args.Any(a => a is "--help" or "-h" or "-?"))
        {
            HideConsoleWindow();
        }

        if (args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a, "-?", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("TubaWinUI3.BackEnd — 主动拦截后端（流氓软件拦截器，NativeAOT）");
            Console.WriteLine();
            Console.WriteLine("用法：TubaWinUI3.BackEnd.exe [--config <路径>] [--once] [--stop]");
            Console.WriteLine("  --config  后端配置文件（JSON：DataDir / PollIntervalSeconds / LogFile / NotifyMode）");
            Console.WriteLine("  --once    只执行一轮扫描后退出（诊断用）");
            Console.WriteLine("  --stop    通知已运行的后端退出");
            return 0;
        }

        // --stop：通过互斥锁通知已运行实例退出
        if (args.Any(a => string.Equals(a, "--stop", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                if (Mutex.TryOpenExisting(MutexName, out var existing))
                {
                    existing.ReleaseMutex();
                    existing.Dispose();
                    Console.WriteLine("已通知后端退出。");
                }
                else
                {
                    Console.WriteLine("后端未在运行。");
                }
            }
            catch { }
            return 0;
        }

        // Toast 通知被点击时，Windows 通过 COM 服务器启动后端并传入 --toast-handler。
        // 此时启动主程序跳转审核页，然后退出。
        if (args.Any(a => string.Equals(a, "--toast-handler", StringComparison.OrdinalIgnoreCase)))
        {
            LaunchMainApp("--show-active-intercept");
            return 0;
        }

        // 单实例互斥锁
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            try
            {
                if (!mutex.WaitOne(2000))
                {
                    Console.Error.WriteLine("已有后端实例在运行，退出。");
                    return 0;
                }
            }
            catch (AbandonedMutexException)
            {
                // 之前的实例崩溃了，接管
            }
        }

        string? configPath = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase))
            {
                configPath = args[i + 1];
                break;
            }
        }
        bool once = args.Any(a => string.Equals(a, "--once", StringComparison.OrdinalIgnoreCase));

        var config = BackendConfigLoader.Load(configPath ?? "");
        if (string.IsNullOrWhiteSpace(config.DataDir))
        {
            Console.Error.WriteLine("错误：配置缺少 DataDir（--config 指向的 JSON 必须包含 DataDir）。");
            return 2;
        }

        BackEndLog.Configure(config.LogFile);
        BackEndLog.Info($"后端启动（PID {Environment.ProcessId}），配置：{configPath ?? "(默认)"}，" +
                        $"拦截={config.EnableIntercept}，游戏监控={config.EnableGameMonitor}");

        // ---- 功能隔离：两个子系统互不激活，没开的绝不装配 ----
        if (!config.EnableIntercept && !config.EnableGameMonitor)
        {
            BackEndLog.Info("两个功能均未启用，后端无事可做，退出。");
            return 0;
        }

        if (config.EnableIntercept) NotificationHelper.RegisterComServer();

        // ---- 装配（共享同一组存储实例）----
        InterceptMonitor? monitor = null;
        InterceptRequestHandler? handler = null;
        NamedPipeBackendServer? server = null;
        if (config.EnableIntercept)
        {
            monitor = new InterceptMonitor(config);
            handler = new InterceptRequestHandler(
                Path.Combine(config.DataDir, "active_intercept"),
                monitor.State,
                monitor.Events,
                monitor.BlockEngine,
                monitor.Policies,
                monitor.Ignore,
                monitor.Notifications);
            server = new NamedPipeBackendServer(handler.DispatchAsync);
        }

        GameMonitorService? gameMonitor = null;
        if (config.EnableGameMonitor)
        {
            gameMonitor = new GameMonitorService(config);
        }

        using var cts = new CancellationTokenSource();

        if (monitor is not null && handler is not null && server is not null)
        {
            // 推送：监视器与管道处理器都直通管道广播
            monitor.Notify = server.BroadcastNotification;
            handler.Notify = server.BroadcastNotification;

            // 优雅停机：任何一方请求退出 → 取消令牌
            handler.ShutdownRequested += (_, _) => SafeCancel(cts);
        }
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            SafeCancel(cts);
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SafeCancel(cts);

        if (server is not null)
        {
            server.Start(cts.Token);
            BackEndLog.Info($"命名管道服务器已启动：{InterceptPipeConstants.PipeName}");
        }

        if (gameMonitor is not null)
        {
            gameMonitor.Start();
            BackEndLog.Info("游戏后台自动监控已启动（检测到游戏时自动显示 FPS 覆盖层）");
        }

        // 系统托盘（常驻模式；--once 诊断模式不建托盘）。菜单文案按启用功能自适应。
        BackendTrayHost? tray = null;
        if (!once)
        {
            var tip = config.EnableIntercept && config.EnableGameMonitor ? "图吧工具箱CE · 主动拦截与游戏监控运行中"
                    : config.EnableGameMonitor ? "图吧工具箱CE · 游戏自动监控运行中"
                    : "图吧工具箱CE · 主动拦截已开启";
            tray = new BackendTrayHost(tip, config.DataDir, config.EnableIntercept, () => SafeCancel(cts));
            tray.Start();
        }

        try
        {
            if (once)
            {
                monitor?.RunOnce();
                BackEndLog.Info("--once 单轮执行完成");
            }
            else
            {
                // 阻塞等待停机信号（拦截循环由 monitor.Run 自行阻塞；纯游戏监控模式
                // 没有常驻循环，靠 Delay 保持进程存活直到 cts 触发）
                if (monitor is not null)
                {
                    monitor.Run();
                    BackEndLog.Info("后端退出");
                }
                else
                {
                    await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
                }
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            BackEndLog.Info("后端退出");
            return 0;
        }
        catch (Exception ex)
        {
            BackEndLog.Error($"后端运行失败：{ex}");
            return 1;
        }
        finally
        {
            try { gameMonitor?.Dispose(); } catch { }
            tray?.Dispose();
            server?.BroadcastServiceStopping();
            server?.Stop();
        }
    }

    private static void SafeCancel(CancellationTokenSource cts)
    {
        try { cts.Cancel(); } catch { }
    }

    // ---------- 控制台窗口隐藏 ----------

    private const int SW_HIDE = 0;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>隐藏后端自身的控制台窗口（常驻托盘服务无黑窗口）。失败静默忽略。</summary>
    private static void HideConsoleWindow()
    {
        try
        {
            var hwnd = GetConsoleWindow();
            if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SW_HIDE);
        }
        catch
        {
            // 非交互环境（如服务/无控制台）下静默忽略
        }
    }

    private static void LaunchMainApp(string arg)
    {
        try
        {
            var exe = NotificationHelper.FindMainAppExe();
            if (string.IsNullOrEmpty(exe))
            {
                BackEndLog.Error("找不到主程序：TubaWinUi3.exe");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arg,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            BackEndLog.Error($"启动主程序失败：{ex.Message}");
        }
    }
}