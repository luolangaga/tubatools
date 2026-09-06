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
using TubaWinUi3.Models;
namespace TubaWinUi3;

public partial class App : Application
{
    private MainWindow? _window;
    public static MainWindow? MainWindow => ((App)Current)?._window;
    public static bool IsLiteMode { get; set; } = false;

    public App()
    {
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
        InitializeComponent();

        // LiveCharts/SkiaSharp 不再于启动时初始化：首个图表页面首次访问时才配置（ChartInitializer）。
        // LiveCharts.Configure 在 App() 中已移除，启动不再加载 SkiaSharp 原生库。

        AppSettings.Load();
        
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
        // 流氓软件的克星「安全增强菜单 - 复制完整路径」配方通过 --copy-path <路径>
        // 唤醒本程序复制路径到剪贴板（后台模式，不显示主窗口）。
        var cmdLine = Environment.GetCommandLineArgs();
        var copyPathIndex = Array.FindIndex(cmdLine, a => string.Equals(a, "--copy-path", StringComparison.OrdinalIgnoreCase));
        if (copyPathIndex >= 0)
        {
            if (copyPathIndex + 1 < cmdLine.Length && !string.IsNullOrWhiteSpace(cmdLine[copyPathIndex + 1]))
            {
                try
                {
                    var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    data.SetText(cmdLine[copyPathIndex + 1]);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                    Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
                }
                catch
                {
                }
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

        if (!RuntimeHelper.IsMsixPackaged && !IsRunningAsAdmin())
        {
            ElevateAndRestart();
            Exit();
            return;
        }

        _window = new MainWindow();
        _window.Activate();
        ToolItem.SetUIDispatcher(_window.DispatcherQueue);
        BrowserAutomationService.Initialize(_window.DispatcherQueue);

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

        // 图标缓存清理与硬件盘点都不再抢启动窗口：图标清理延迟到空闲期执行，
        // 硬件 WMI 盘点（20+ 条查询）延迟 10s 后台预热，打开硬件信息页时直接命中缓存。
        _ = DelayThenRunAsync(TimeSpan.FromSeconds(15), () => { ToolIconService.CleanExpiredCache(); return Task.CompletedTask; });
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

        // 主动拦截：若用户开启了主动拦截，自动拉起 NativeAOT 后端（独立常驻进程）。
        // MSIX 沙箱下不支持启动独立后端进程。
        if (!RuntimeHelper.IsMsixPackaged && AppSettings.GetBool("ActiveInterceptEnabled", false))
        {
            ActiveInterceptService.Start();
        }

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
                    var dialog = new ToolsBundleDownloadDialog
                    {
                        XamlRoot = root.XamlRoot,
                        RequestedTheme = ThemeService.CurrentElementTheme
                    };
                    await dialog.ShowDownloadAsync();
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

            if (MainWindow?.DispatcherQueue is null) return;

            MainWindow.DispatcherQueue.TryEnqueue(async () =>
            {
                if (MainWindow?.Content is not FrameworkElement root) return;
                var dialog = new ToolsBundleDownloadDialog
                {
                    XamlRoot = root.XamlRoot,
                    RequestedTheme = ThemeService.CurrentElementTheme
                };
                dialog.SetDescription("发现工具包新版本，建议更新以获取最新工具。");
                await dialog.ShowDownloadAsync(info);
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

    private void OnUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        _pendingException = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "未知错误");
        NavigateToErrorPage();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // 未观察异常（多为第三方库内部后台任务，如 OpenAI SDK 的 SSE 分页在网络
        // 失败重试耗尽后遗留）不应打断用户：记日志并标记已观察即可。
        // 业务路径（provider 流/页面回调）的异常均已各自处理并展示错误气泡。
        TubaWinUi3.Services.Agent.AgentDebugLog.Error(
            "[App] 未观察任务异常（已标记观察，不影响使用）", e.Exception);
        e.SetObserved();
    }

    private void OnWinUIUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "app_crash.log"),
            $"WinUI Unhandled Exception:\n{e.Exception}\n\nMessage: {e.Message}");
        _pendingException = e.Exception ?? new Exception(e.Message);
        NavigateToErrorPage();
        e.Handled = true;
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
}
