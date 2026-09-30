using System.Diagnostics;
using System.Security.Principal;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;
using TubaWinUi3.Services.ActiveIntercept;
using TubaWinUi3.Services.Agent;
using TubaWinUi3.Services.Ai;
using TubaWinUi3.Services.Telemetry;
using TubaWinUi3.Models;
namespace TubaWinUi3;

public partial class App : Application
{
    private MainWindow? _window;
    public static MainWindow? MainWindow => ((App)Current)?._window;
    public static bool IsLiteMode { get; set; } = false;

    private static bool _exiting;

    /// <summary>是否已进入退出流程（托盘「退出」等明确退出请求）——窗口关闭拦截据此放行，不再隐藏到托盘。</summary>
    public static bool IsExiting => _exiting;

    public App()
    {
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);

        // 切勿设置 WEBVIEW2_USER_DATA_FOLDER：它优先级高于一切，会覆盖进程内所有 WebView2 环境
        // 的用户数据目录（包括 CreateWithOptionsAsync 显式传入的 userDataFolder 和第三方组件自建的
        // 默认环境），把工具箱自己那套与 FieldCure ChatPanel 那套挤进同一目录；而一个 UDF 同时只能
        // 有一个会话，后创建的环境会静默失败（返回 null 环境，不抛异常），表现为 AI 面板收不到消息、
        // 消息区空白。工具箱自己的 WebView2 统一经 WebView2EnvironmentService.GetAsync() 指定可写
        // 目录（%LocalAppData%\TubaWinUi3\WebView2），各套环境各用各的目录才互不干扰。
        InitializeComponent();

        // LiveCharts/SkiaSharp 不再于启动时初始化：首个图表页面首次访问时才配置（ChartInitializer）。
        // LiveCharts.Configure 在 App() 中已移除，启动不再加载 SkiaSharp 原生库。

        AppSettings.Load();

        // 匿名遥测（在线用户统计 + 错误日志）：默认开启，可在设置中关闭。
        // 初始化只构建对象、不做网络等待，上传全在后台进行，失败静默落盘续传。
        TelemetryService.Initialize();

        // AI 助手：接上 FieldCure 组件库的诊断回调。组件内部的失败（WebView2 环境创建失败、
        // 渲染被就绪守卫拦下、脚本异常等）默认完全静默，只会表现为"字没了、什么都没发生"；
        // 接上后统一落盘 <DataDir>\AiAssistant\diag.log，正式版也能抓到真身。
        AiDiagnosticsLog.Initialize();

        // 界面语言必须在任何打了 Uid 的控件创建前就绪（MainWindow 在 OnLaunched 里创建）。
        LocalizationService.Initialize();

        // 「关闭主窗口 → 最小化到系统托盘」：注销/关机时必须放行关闭，否则会拖住系统注销
        CloseToTrayService.AttachSessionEndingWatch();

        BuiltinToolRegistry.RegisterDefaults();
        AgentToolRegistry.RegisterDefaults();
        AgentSkillRegistry.RegisterDefaults();

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        UnhandledException += OnWinUIUnhandledException;
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void ElevateAndRestart()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;

        try
        {
            // 保留原始命令行参数（--open-builtin 等）以便提权后继续执行
            var originalArgs = string.Join(" ", Environment.GetCommandLineArgs().Skip(1).Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            Process.Start(new ProcessStartInfo(exePath)
            {
                Arguments = originalArgs,
                Verb = "runas",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 兜底：启动路径上的任何异常都不能让进程直接崩溃。从 OnLaunched 逃出去的托管异常
        // 会被 WinUI 转成 STOWED_EXCEPTION（空引用异常跨 ABI 映射为 0x80004003 E_POINTER）
        // 直接结束进程，在应用商店崩溃报告里表现为
        // STOWED_EXCEPTION_80004003_Microsoft.UI.Xaml.dll!DirectUI::FrameworkApplicationGenerated::OnLaunchedProtected
        // —— 用户看到的是闪退，开发者连堆栈都拿不到。这里统一捕获并留下可上报的诊断日志。
        try
        {
            OnLaunchedCore(args);
        }
        catch (Exception ex)
        {
            HandleLaunchFailure(ex);
        }
    }

    private void OnLaunchedCore(LaunchActivatedEventArgs args)
    {
        // 流氓软件的克星「安全增强菜单 - 复制完整路径」配方通过 --copy-path <路径>
        // 唤醒本程序复制路径到剪贴板（后台模式，不显示主窗口）。
        var cmdLine = Environment.GetCommandLineArgs();
        var copyPathIndex = Array.FindIndex(cmdLine, a => string.Equals(a, "--copy-path", StringComparison.OrdinalIgnoreCase));
        if (copyPathIndex >= 0)
        {
            if (copyPathIndex + 1 < cmdLine.Length && !string.IsNullOrWhiteSpace(cmdLine[copyPathIndex + 1]))
            {
                // 统一走 ClipboardService：占用时重试，失败只记日志（本进程马上 Exit，
                // 没有窗口可以承载错误提示）。flush 保证进程退出后剪贴板内容依然可粘贴。
                ClipboardService.TrySetText(cmdLine[copyPathIndex + 1], flush: true);
            }
            Exit();
            return;
        }

        // 右键菜单「技术位置」的动态命令文字探测子进程模式：
        // 主程序以自身 --context-title-probe 做 COM 隔离（挂死的扩展 COM 调用只拖垮这个子进程），
        // 结果写入 --probe-out 指定的 JSON 文件后立即退出，不显示主窗口。
        var probeIndex = Array.FindIndex(cmdLine, a => string.Equals(a, "--context-title-probe", StringComparison.OrdinalIgnoreCase));
        if (probeIndex >= 0 && probeIndex + 3 < cmdLine.Length)
        {
            var probeOut = string.Empty;
            var probeOutIndex = Array.FindIndex(cmdLine, a => string.Equals(a, "--probe-out", StringComparison.OrdinalIgnoreCase));
            if (probeOutIndex >= 0 && probeOutIndex + 1 < cmdLine.Length) probeOut = cmdLine[probeOutIndex + 1];
            try
            {
                var probe = Services.RogueCleaner.ContextCommandTitleProbe.ProbeForChildProcess(
                    cmdLine[probeIndex + 1], cmdLine[probeIndex + 2], cmdLine[probeIndex + 3]);
                if (!string.IsNullOrWhiteSpace(probeOut))
                {
                    File.WriteAllText(probeOut, System.Text.Json.JsonSerializer.Serialize(probe), new System.Text.UTF8Encoding(false));
                }
            }
            catch
            {
                // 探测异常也写出失败结果，父进程据此降级，不弹出错误上报窗口
                try
                {
                    if (!string.IsNullOrWhiteSpace(probeOut))
                    {
                        File.WriteAllText(probeOut, "{\"Title\":null,\"Icon\":null,\"Error\":\"探测过程出错。\",\"Source\":null}", new System.Text.UTF8Encoding(false));
                    }
                }
                catch
                {
                }
            }
            Exit();
            return;
        }

        // 后端 --toast 模式：读取通知文件，弹出 Windows 原生 Toast 后立即退出（不显示主窗口）。
        // 双通道防重复：主程序已运行时 FileSystemWatcher 先消费文件，此处读不到即跳过。
        var toastIndex = Array.FindIndex(cmdLine, a => string.Equals(a, "--toast", StringComparison.OrdinalIgnoreCase));
        if (toastIndex >= 0 && toastIndex + 1 < cmdLine.Length)
        {
            var notifFile = cmdLine[toastIndex + 1];
            try
            {
                // 延迟等待 FileSystemWatcher 先处理（主程序已运行时）
                Thread.Sleep(500);
                if (File.Exists(notifFile))
                {
                    var json = File.ReadAllText(notifFile);
                    var req = System.Text.Json.JsonSerializer.Deserialize(
                        json, Services.ActiveIntercept.ActiveInterceptJsonContext.Default.NotificationRequest);
                    if (req is not null && !string.IsNullOrWhiteSpace(req.Title))
                    {
                        new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                            .AddText(req.Title)
                            .AddText(req.Body)
                            .AddArgument("action", "show-active-intercept")
                            .Show(toast =>
                            {
                                toast.ExpirationTime = DateTimeOffset.Now.AddMinutes(10);
                            });
                    }
                    try { File.Delete(notifFile); } catch { }
                }
                // 文件已被 FileSystemWatcher 消费 → 无需重复弹通知
            }
            catch { }
            Exit();
            return;
        }

        // EnergyStar silent auto-start (scheduled-task launched this instance
        // in the background — silently enable EcoQoS without showing the main UI).
        var silentEnergyStar = cmdLine
            .Any(a => string.Equals(a, EnergyStarStartupService.SilentArg, StringComparison.OrdinalIgnoreCase));

        if (silentEnergyStar)
        {
            try { EnergyStarService.Initialize(); } catch { /* swallow so OS keeps the task happy */ }
            // No main window: keep this process throttling in the background.
            // Active throttling is driven by the static service; the process can
            // stay alive without a WinUI window (the dispatcher here is unused).
            return;
        }

        if (!RuntimeHelper.IsMsixPackaged && !IsRunningAsAdmin()
            && Environment.GetEnvironmentVariable("TUBA_SCREENSHOT_NOELEVATE") != "1")
        {
            ElevateAndRestart();
            Exit();
            return;
        }

        _window = new MainWindow();
        _window.Activate();
        ToolItem.SetUIDispatcher(_window.DispatcherQueue);
        BrowserAutomationService.Initialize(_window.DispatcherQueue);
        // 游戏后台自动覆盖层：常驻轮询后端信号文件（检测到全屏游戏自动显示悬浮窗）
        Services.GameOverlayAutoService.Instance.Start();

        // 后端检测到游戏自动拉起主程序时（--game-overlay-auto）：
        // 用户在玩游戏，主界面不应抢焦点弹到游戏前面 —— 最小化到任务栏即可。
        var gameOverlayAuto = cmdLine
            .Any(a => string.Equals(a, "--game-overlay-auto", StringComparison.OrdinalIgnoreCase));
        if (gameOverlayAuto)
        {
            Services.GameOverlayAutoService.Log($"后端自动拉起启动（exe={Environment.ProcessPath}），主窗口将延迟最小化");
            // 不要在 OnLaunched 里立即最小化：窗口尚未完成首次布局，此刻动窗口状态
            // 与 WinUI 启动竞态，曾触发 ArgumentException 崩溃。延迟到渲染稳定后。
            // 用 SW_SHOWMINNOACTIVE（最小化且不激活）：Activate() 已经把主窗口弹到
            // 游戏前面抢了焦点，普通 SW_MINIMIZE 前这 1.5 秒游戏会丢失前台；
            // NOACTIVE 不改前台归属，游戏不受影响（实测 2026-09-11 23:37 前台被抢 20s）。
            var w = _window;
            w.DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(1500);
                try
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(w);
                    ShowWindow(hwnd, SW_SHOWMINNOACTIVE);
                    Services.GameOverlayAutoService.Log("主窗口已延迟最小化（NOACTIVE，不抢前台）");
                }
                catch (Exception ex)
                {
                    Services.GameOverlayAutoService.Log($"最小化主窗口失败: {ex.Message}");
                }
            });
        }

        // 主动拦截 Toast 通知被点击时，后端以 --show-active-intercept 启动主程序，
        // 直接跳转「流氓软件的克星 → 主动拦截」审核页。
        var showActiveIntercept = cmdLine
            .Any(a => string.Equals(a, "--show-active-intercept", StringComparison.OrdinalIgnoreCase));
        if (showActiveIntercept)
        {
            _window.NavigateToToolPage(typeof(Pages.RogueCleanerPage), "activeintercept");
        }

        // Windows 搜索索引快捷方式启动内置工具：--open-builtin <toolId>
        var openBuiltinIndex = Array.FindIndex(cmdLine, a => string.Equals(a, "--open-builtin", StringComparison.OrdinalIgnoreCase));
        if (openBuiltinIndex >= 0 && openBuiltinIndex + 1 < cmdLine.Length)
        {
            var builtinId = cmdLine[openBuiltinIndex + 1];
            _window.NavigateToToolPage(typeof(Pages.BuiltinToolsPage), builtinId);
        }

        _ = RunStartupSequenceAsync();
    }

    /// <summary>
    /// 启动失败兜底：把异常写进可上报的日志（错误报告打包会收录 %TEMP%\app_crash.log），
    /// 并尽力给用户一个能复制堆栈/打包日志/重新打开的窗口，而不是静默闪退。
    /// </summary>
    private void HandleLaunchFailure(Exception ex)
    {
        var crashLogPath = Path.Combine(Path.GetTempPath(), "app_crash.log");
        var detail = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 启动失败（App.OnLaunched）:\n" +
                     $"版本: {UpdateService.CurrentVersion}  打包: {RuntimeHelper.IsMsixPackaged}  " +
                     $"管理员: {IsRunningAsAdmin()}  系统: {WindowsVersionText()}\n" +
                     $"程序: {Environment.ProcessPath}\n" +
                     $"{ex}\n" + new string('-', 80) + "\n";

        try { File.AppendAllText(crashLogPath, detail); } catch { }
        try { Services.GameOverlayAutoService.Log($"启动失败: {ex.Message}\n{ex.StackTrace}"); } catch { }
        Debug.WriteLine(detail);

        _pendingException = ex;

        // 主窗口没建起来也要留住进程：错误窗口提供复制/打包/重开，是用户唯一能求助的入口
        try
        {
            new Pages.ErrorWindow().Activate();
            return;
        }
        catch (Exception fallbackEx)
        {
            Debug.WriteLine($"[Startup] 错误窗口也未能启动: {fallbackEx.Message}");
        }

        // XAML 整体不可用时退回 Win32 消息框，至少让用户知道发生了什么、日志在哪
        try
        {
            MessageBoxW(IntPtr.Zero,
                $"图吧工具箱启动失败：\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
                $"诊断日志：{crashLogPath}\n" +
                "请在「设置 → 错误报告」中打包日志并反馈。",
                "图吧工具箱", MB_OK | MB_ICONERROR);
        }
        catch { }

        // 连错误窗口都起不来时，提示看完就主动结束：宁可退出，也不要留下无窗口的僵尸进程
        Environment.Exit(1);
    }

    private static string WindowsVersionText()
    {
        try
        {
            var v = Environment.OSVersion.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            return "未知";
        }
    }

    /// <summary>退出兜底短超时：主窗口不存在 / 关闭请求排不进 UI 队列时，清理根本跑不起来，不必久等。</summary>
    private const int ExitWatchdogShortMs = 3000;

    /// <summary>退出兜底：等待 <c>MainWindow_Closed</c> 清理完成的上限。清理是最重要的收尾，宁可多等。</summary>
    private const int ExitCleanupWaitMs = 20000;

    /// <summary>清理完成后留给 <c>Application.Exit()</c> 自己收尾的时间。</summary>
    private const int ExitPostCleanupGraceMs = 2000;

    private static volatile bool _cleanupFinished;

    /// <summary><c>MainWindow_Closed</c> 的收尾清理已完成（退出看门狗据此判断何时可以强制结束进程）。</summary>
    public static void NotifyCleanupFinished() => _cleanupFinished = true;

    /// <summary>
    /// 请求真正退出程序（托盘菜单「退出」等）。任意线程可调用。
    ///
    /// 流程：关掉主窗口 → <c>MainWindow_Closed</c> 走完整清理（FPS 的 ETW 会话、LiteMonitor 句柄、
    /// 遥测收尾、设置落盘、托盘图标移除）→ 结束进程。绝不能再退回 Process.Kill：
    /// 内核 ETW 会话不会随进程终止回收，残留会让下次启动的帧率采集失效。
    /// </summary>
    public static void RequestExit()
    {
        if (_exiting) return;
        _exiting = true;
        _cleanupFinished = false;

        var window = MainWindow;
        var queue = window?.DispatcherQueue;

        bool cleanupWillRun;
        if (queue is null || queue.HasThreadAccess)
        {
            ExitCore(window);
            cleanupWillRun = window is not null;
        }
        else
        {
            cleanupWillRun = queue.TryEnqueue(() => ExitCore(window));
        }

        // 兜底：第三方控件（WebView2 等）偶尔会拖住消息循环，让 Exit() 迟迟不返回，
        // 留下一个没有窗口的僵尸进程。硬退必须等清理跑完 —— 固定 3 秒硬超时会砍在
        // LiteMonitor/ETW 释放的中途，而那正是这里最不能丢的一步。
        _ = Task.Run(async () =>
        {
            if (cleanupWillRun)
            {
                var deadline = Environment.TickCount64 + ExitCleanupWaitMs;
                while (!_cleanupFinished && Environment.TickCount64 < deadline)
                    await Task.Delay(100);

                if (_cleanupFinished)
                    await Task.Delay(ExitPostCleanupGraceMs);
            }
            else
            {
                await Task.Delay(ExitWatchdogShortMs);
            }

            Environment.Exit(0);
        });
    }

    private static void ExitCore(MainWindow? window)
    {
        try
        {
            window?.CloseForExit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] 关闭主窗口失败（继续退出）: {ex.Message}");
        }

        try
        {
            Current.Exit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] Application.Exit 失败（已忽略）: {ex.Message}");
        }
    }

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private static async Task RunStartupSequenceAsync()
    {
        // MSIX 下包身份解析失败会回滚到共享 %LocalAppData%（非打包路径）：
        // 工具根/数据目录将指向旧安装版位置，可能启动非打包路径的程序，输出诊断日志
        if (RuntimeHelper.IsMsixPackaged && RuntimeHelper.LocalAppDataRootUsedFallback)
        {
            System.Diagnostics.Debug.WriteLine("[Startup] 警告：MSIX 包身份路径解析失败，数据根已回滚到共享 %LocalAppData%，工具根可能指向非打包路径");
        }

        if (MainWindow?.DispatcherQueue is not null)
        {
            MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                ThemeService.ApplySavedTheme();
            });
        }

        // 图标缓存清理与硬件盘点都不再抢启动窗口：图标清理延迟到空闲期执行（后台线程，
        // 内部是纯文件 IO + 线程安全缓存，不能留在 UI 线程上扫盘），
        // 硬件 WMI 盘点（20+ 条查询）延迟 10s 后台预热，打开硬件信息页时直接命中缓存。
        _ = DelayThenRunAsync(TimeSpan.FromSeconds(15), () => Task.Run(() => ToolIconService.CleanExpiredCache()));
        _ = DelayThenRunAsync(TimeSpan.FromSeconds(10), () => { HardwareInfoService.PreloadAsync(); return Task.CompletedTask; });
        _ = Task.Run(() => ConfigManager.AutoMigratePathsIfNeeded());

        // 规则：分类下没有工具就删除。启动时清理历史遗留的空白分类目录
        // （扫描放后台线程，删除与设置写入回 UI 线程）。
        _ = Task.Run(async () =>
        {
            List<string> emptyCategories;
            try
            {
                emptyCategories = ToolCatalog.FindEmptyCategories();
            }
            catch
            {
                return;
            }

            if (emptyCategories.Count == 0 || MainWindow?.DispatcherQueue is null)
                return;

            MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                var removed = 0;
                foreach (var name in emptyCategories)
                {
                    if (ToolCatalog.PruneCategoryIfEmpty(name))
                        removed++;
                }

                if (removed > 0)
                {
                    ToolCatalog.InvalidateTagsCache();
                    if (MainWindow is MainWindow mw)
                        mw.RefreshToolCategories();
                }
            });
        });

        // 后端进程统一入口：按「主动拦截 + 游戏后台监控」两个功能的开关状态同步。
        // 有任一功能开启 → 拉起 NativeAOT 后端（独立常驻进程）；MSIX 沙箱下不支持。
        ActiveInterceptService.SyncBackend();

        var wizardShown = false;
        try
        {
            if (AppSettings.Get("SetupCompleted") == null)
            {
                // 等待主窗口内容挂载（XamlRoot 就绪）后再显示向导：
                // Activate() 返回时 XAML 树可能尚未挂载，直接 ShowAsync 会因
                // XamlRoot 为空抛 ArgumentException，导致向导被静默跳过。
                var root = await WaitForContentXamlRootAsync();
                if (root?.XamlRoot is { } xamlRoot)
                {
                    var wizard = new SetupWizardDialog
                    {
                        XamlRoot = xamlRoot,
                        RequestedTheme = ThemeService.CurrentElementTheme
                    };
                    await wizard.ShowAsync();
                    wizardShown = true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Setup] Wizard failed: {ex.Message}");
        }
        finally
        {
            // 仅当向导确实展示过（用户完成/跳过，或 ContentDialog 正常关闭）才标记完成；
            // 若因 XamlRoot 未就绪等导致根本没有展示机会，保留未完成状态，下次启动再试。
            if (wizardShown)
                AppSettings.Set("SetupCompleted", true);
        }

        if (RuntimeHelper.IsMsixPackaged)
        {
            if (!ToolsBundleService.IsToolsBundleReady())
            {
                await ShowToolsBundleDownloadDialogAsync();
            }
            _ = CheckForToolsUpdateSilentAsync();
        }
        else if (RuntimeHelper.IsLiteBuild)
        {
            // 精简版随包内置必要工具，首启无需下载内核包；
            // 仅当用户此前通过内核包安装过（有版本记录）才静默检查更新。
            if (ToolsBundleService.GetCurrentVersion() is not null)
            {
                _ = CheckForToolsUpdateSilentAsync();
            }
        }

        if (!RuntimeHelper.IsMsixPackaged)
        {
            // 更新检查延后 10s 发起，避开启动窗口期的磁盘/网络竞争
            _ = DelayThenRunAsync(TimeSpan.FromSeconds(10), CheckForToolUpdatesSilentAsync);
            _ = DelayThenRunAsync(TimeSpan.FromSeconds(10), () => CheckForUpdateSilentAsync());
        }
        else
        {
            _ = DelayThenRunAsync(TimeSpan.FromSeconds(10), CheckForToolUpdatesSilentAsync);
        }

        // 若用户已启用 Windows 搜索索引注册，启动时刷新快捷方式
        if (AppSettings.GetBool("WindowsSearchIndex", false))
        {
            _ = DelayThenRunAsync(TimeSpan.FromSeconds(15), () => WindowsSearchIndexService.RegisterAllToolsAsync());
        }
        ToolCatalog.ToolsChanged += () =>
        {
            if (AppSettings.GetBool("WindowsSearchIndex", false))
                _ = WindowsSearchIndexService.RefreshAsync();
        };
    }

    private static async Task DelayThenRunAsync(TimeSpan delay, Func<Task> action)
    {
        try
        {
            await Task.Delay(delay);
            await action();
        }
        catch { }
    }

    /// <summary>
    /// 等待主窗口内容挂载完成并返回其根 FrameworkElement。
    /// Activate() 返回时 XAML 树可能尚未挂载（XamlRoot 为空），
    /// 等待 Loaded 事件（带超时兜底）以确保拿到有效的 XamlRoot。
    /// </summary>
    private static async Task<FrameworkElement?> WaitForContentXamlRootAsync()
    {
        var window = MainWindow;
        if (window?.Content is not FrameworkElement content)
            return null;

        if (content.XamlRoot is not null)
            return content;

        var tcs = new TaskCompletionSource<FrameworkElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler handler = null!;
        handler = (_, _) =>
        {
            content.Loaded -= handler;
            tcs.TrySetResult(content);
        };
        content.Loaded += handler;

        var timeout = Task.Delay(TimeSpan.FromSeconds(15));
        var done = await Task.WhenAny(tcs.Task, timeout);
        if (done != tcs.Task)
        {
            content.Loaded -= handler;
            return content.XamlRoot is not null ? content : null;
        }
        return await tcs.Task;
    }

    private static async Task ShowToolsBundleDownloadDialogAsync()
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await Task.Delay(i == 0 ? 300 : 1000);

                if (MainWindow?.Content is FrameworkElement root)
                {
                    // 用户已标记「跳过此版本」时不再自动弹出（仍可手动检查/下载）
                    ToolsBundleUpdateInfo? info = null;
                    try { info = await ToolsBundleService.CheckForToolsUpdateAsync(); }
                    catch { }
                    if (info is not null && info.HasUpdate &&
                        ToolsBundleService.GetSkippedVersion() == info.Version)
                        return;

                    var dialog = new ToolsBundleDownloadDialog
                    {
                        XamlRoot = root.XamlRoot,
                        RequestedTheme = ThemeService.CurrentElementTheme
                    };
                    await dialog.ShowDownloadAsync(info);
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ToolsBundle] Download dialog attempt {i + 1} failed: {ex.Message}");
            }
        }
    }

    private static async Task CheckForToolsUpdateSilentAsync()
    {
        try
        {
            // 精简版（Lite）便携：内置工具不经 LocalAppData 内核目录，以是否下载过内核包为准
            if (RuntimeHelper.IsLiteBuild)
            {
                if (ToolsBundleService.GetCurrentVersion() is null) return;
            }
            else if (!ToolsBundleService.IsToolsBundleReady())
            {
                return;
            }

            var info = await ToolsBundleService.CheckForToolsUpdateAsync();
            if (info is null || !info.HasUpdate) return;
            if (ToolsBundleService.GetSkippedVersion() == info.Version) return;

            if (MainWindow?.DispatcherQueue is null) return;

            MainWindow.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    if (MainWindow?.Content is not FrameworkElement root) return;
                    var dialog = new ToolsBundleDownloadDialog
                    {
                        XamlRoot = root.XamlRoot,
                        RequestedTheme = ThemeService.CurrentElementTheme
                    };
                    dialog.SetDescription("发现工具包新版本，建议更新以获取最新工具。");
                    // ShowDownloadAsync 内部会等已有对话框关闭（如刚弹过的「下载完成」提示）；
                    // 这里再兜一层异常，保证静默检查的 async 回调永远不会把异常抛到全局。
                    await dialog.ShowDownloadAsync(info);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ToolsBundle] Silent update dialog failed: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ToolsBundle] Update check failed: {ex.Message}");
        }
    }

    private static async Task<bool> CheckForUpdateSilentAsync()
    {
        try
        {
            var update = await UpdateService.CheckForUpdateAsync();
            if (update is null) return false;

            var skipped = UpdateService.GetSkippedVersion();
            if (skipped == update.Version) return false;

            if (MainWindow?.DispatcherQueue is null) return false;

            if (UpdateService.IsUpdateAlreadyDownloaded(update))
            {
                MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    if (MainWindow is MainWindow mw)
                        mw.ShowUpdateAlreadyDownloaded(update);
                });
                return true;
            }

            var autoDownload = false;

            MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                if (MainWindow is MainWindow mw)
                    mw.ShowUpdateBanner(update, autoDownload);
            });

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Update] Silent check failed: {ex.Message}");
            return false;
        }
    }

    private static async Task CheckForToolUpdatesSilentAsync()
    {
        try
        {
            var updates = await ToolUpdateService.CheckForToolUpdatesAsync();
            if (updates is null || updates.Count == 0) return;

            ToolUpdateService.EnqueueToolUpdates(updates);
        }
        catch { }
    }

    private static Exception? _pendingException;

    /// <summary>
    /// 已知的 AI 助手面板（FieldCure ChatPanel）销毁竞态：面板已从界面移除、本轮回复作废，
    /// 异常来自第三方组件对已关闭 WebView2 的收尾渲染（async void 事件里抛出，宿主拦不住），
    /// 记日志留痕即可，不该再弹错误窗口打断用户（Issue #194，详见 ChatPanelCrashFilter）。
    ///
    /// <para><b>两个未处理异常入口都必须先过这里</b>：WinUI 的 Application.UnhandledException
    /// 与 AppDomain 的 UnhandledException。此前只判了前者，用户报告里那条 ChatPanel
    /// 异常正是从 AppDomain 入口漏过去、弹了错误窗口。</para>
    /// </summary>
    private static bool IsIgnorableAiPanelTeardownRace(Exception? ex)
    {
        if (!ChatPanelCrashFilter.IsTeardownRace(ex)) return false;

        try
        {
            TubaWinUi3.Services.Agent.AgentDebugLog.Error(
                "[App] AI 面板销毁竞态异常（已忽略，不影响使用）", ex);
        }
        catch { }

        return true;
    }

    private void OnUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "未知错误");

        // 先过已知噪声过滤，再决定要不要把错误窗口弹给用户
        var ignorable = IsIgnorableAiPanelTeardownRace(ex);
        TelemetryService.TrackException(ex, ignorable ? "AppDomain.ChatPanelTeardownRace" : "AppDomain", fatal: true);
        if (ignorable) return;

        _pendingException = ex;
        NavigateToErrorPage();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // 未观察异常（多为第三方库内部后台任务，如 OpenAI SDK 的 SSE 分页在网络
        // 失败重试耗尽后遗留）不应打断用户：记日志并标记已观察即可。
        // 业务路径（provider 流/页面回调）的异常均已各自处理并展示错误气泡。
        TubaWinUi3.Services.Agent.AgentDebugLog.Error(
            "[App] 未观察任务异常（已标记观察，不影响使用）", e.Exception);
        TelemetryService.TrackException(e.Exception, "Task");
        e.SetObserved();
    }

    private void OnWinUIUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var detail = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] WinUI Unhandled Exception (Handled={e.Handled}):\n" +
                     $"Message: {e.Message}\n{e.Exception}\n" +
                     $"StackTrace:\n{e.Exception?.StackTrace}\n" +
                     $"Inner: {e.Exception?.InnerException}\n" +
                     new string('-', 80) + "\n";
        try
        {
            // 追加而非覆盖：连续崩溃时历史记录都在
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "app_crash.log"), detail);
            try
            {
                Services.GameOverlayAutoService.Log($"WinUI 未处理异常: {e.Message}\n{e.Exception?.StackTrace}");
            }
            catch { }
        }
        catch { }

        // 未处理异常上报（文本已脱敏，不含用户名/路径）；已知的 AI 面板销毁竞态带专门
        // 来源标记，便于在后台一眼分辨是第三方组件噪声还是真问题。
        try
        {
            var source = ChatPanelCrashFilter.IsTeardownRace(e.Exception) ? "WinUI.ChatPanelTeardownRace" : "WinUI";
            TelemetryService.TrackException(e.Exception, source);
        }
        catch { }

        e.Handled = true;

        if (IsIgnorableAiPanelTeardownRace(e.Exception)) return;

        _pendingException = e.Exception ?? new Exception(e.Message);
        NavigateToErrorPage();
    }

    public static Exception? ConsumePendingException()
    {
        var ex = _pendingException;
        _pendingException = null;
        return ex;
    }

    private void NavigateToErrorPage()
    {
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            var errorWindow = new Pages.ErrorWindow();
            errorWindow.Activate();
        });
    }

    private const int SW_MINIMIZE = 6;
    private const int SW_SHOWMINNOACTIVE = 7;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
