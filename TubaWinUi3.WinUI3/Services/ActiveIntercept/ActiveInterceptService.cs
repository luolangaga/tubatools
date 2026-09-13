using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Toolkit.Uwp.Notifications;

namespace TubaWinUi3.Services.ActiveIntercept;

/// <summary>
/// 主动拦截后端（TubaWinUI3.BackEnd.exe）进程管理。
/// 后端是独立常驻进程：主程序只负责拉起/停止，不持有其生命周期（主程序退出不影响后端）。
/// 配置以 JSON 写入数据目录下的 active_intercept/config.json，由后端 --config 读取。
/// </summary>
public static class ActiveInterceptService
{
    private static readonly object _lock = new();

    /// <summary>后端进程（若由本进程拉起）。</summary>
    private static Process? _process;

    /// <summary>是否已在本进程内启动过（用于去重与开关状态展示）。</summary>
    private static bool _startedHere;

    /// <summary>通知文件监控器。</summary>
    private static FileSystemWatcher? _notifWatcher;

    /// <summary>后端可执行文件相对主程序目录的名称。</summary>
    public const string BackEndFileName = "TubaWinUI3.BackEnd.exe";

    /// <summary>数据目录下的后端配置文件名。</summary>
    private const string ConfigFileName = "active_intercept\\config.json";

    public static string BackEndExePath
    {
        get
        {
            // 未打包：与主程序同目录；打包/MSIX：从包内目录解析
            foreach (var dir in new[] { ToolCatalog.AppDirectory, AppContext.BaseDirectory })
            {
                var path = Path.Combine(dir, BackEndFileName);
                if (File.Exists(path)) return path;
            }
            return Path.Combine(ToolCatalog.AppDirectory, BackEndFileName);
        }
    }

    public static string ConfigPath => Path.Combine(ConfigManager.GetDataDir(), ConfigFileName);

    /// <summary>后端是否在运行（检查本进程持有的句柄或按进程名查找）。</summary>
    public static bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                if (_process is not null && !_process.HasExited) return true;
            }
            // 主程序可能在重启后重新发现已常驻的后端
            try
            {
                var exe = Path.GetFileNameWithoutExtension(BackEndExePath);
                return Process.GetProcessesByName(exe).Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    private const int ErrorNotSupported = 0x32;

    /// <summary>启动后端（幂等）。写配置 + 拉起进程，失败不抛出。</summary>
    public static bool Start()
    {
        // MSIX 打包模式下不支持主动拦截后端（沙箱限制无法启动独立进程）
        if (RuntimeHelper.IsMsixPackaged)
        {
            System.Diagnostics.Debug.WriteLine("[ActiveIntercept] MSIX 模式下不支持主动拦截后端");
            return false;
        }

        lock (_lock)
        {
            if (_process is not null && !_process.HasExited) return true;
            if (_startedHere) return false;

            try
            {
                var exePath = BackEndExePath;
                if (!File.Exists(exePath)) return false;

                WriteConfig();

                var args = $"\"--config\" \"{ConfigPath}\"";
                try
                {
                    // UseShellExecute=true 通过 Shell 启动，避免 MSIX AppContainer 沙箱阻止子进程。
                    _process = Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = args,
                        UseShellExecute = true,
                        Verb = "open",
                    });
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorNotSupported && RuntimeHelper.IsMsixPackaged)
                {
                    // MSIX 沙箱下 ShellExecuteEx 也受限时，以 cmd.exe 为载体启动（实测可行）。
                    _process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c start \"\" \"{exePath}\" {args}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    });
                }

                if (_process is not null)
                {
                    try
                    {
                        _process.EnableRaisingEvents = true;
                        // 后端是独立常驻进程：进程退出只清句柄，不自动重启（避免崩溃循环）。
                        _process.Exited += (_, _) =>
                        {
                            lock (_lock) _process = null;
                        };
                    }
                    catch
                    {
                        // cmd.exe 载体的 Process 可能不支持 Exited 事件，忽略。
                    }
                }

                _startedHere = true;
                StartNotificationWatcher();
                // 管道工作区：与后端建立常开通知订阅（推送驱动刷新，取代轮询）
                try
                {
                    InterceptWorkspace.Initialize(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ActiveIntercept] 工作区初始化失败：{ex.Message}");
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ActiveIntercept] 启动后端失败：{ex.Message}");
                return false;
            }
        }
    }

    /// <summary>停止后端（用户关闭开关时调用）。</summary>
    public static void Stop()
    {
        lock (_lock)
        {
            _startedHere = false;
            StopNotificationWatcher();
            InterceptWorkspace.Shutdown();
            try
            {
                if (_process is not null && !_process.HasExited)
                {
                    // 优雅退出优先
                    if (!_process.CloseMainWindow())
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                    _process.WaitForExit(2000);
                    _process = null;
                }
                else
                {
                    // _process 为空（后端非本次拉起），按进程名查找并杀掉
                    var exe = Path.GetFileNameWithoutExtension(BackEndExePath);
                    foreach (var p in Process.GetProcessesByName(exe))
                    {
                        try { p.Kill(entireProcessTree: true); p.WaitForExit(2000); } catch { }
                        p.Dispose();
                    }
                }
            }
            catch
            {
                // 忽略：进程可能已退出
            }
        }
    }

    /// <summary>写后端配置文件到数据目录（JSON，JsonSerializer 保证转义正确）。</summary>
    public static void EnsureConfigWritten() => WriteConfig();

    /// <summary>
    /// 是否还有任一功能需要后端进程（以主程序当前设置为准）。
    /// </summary>
    private static bool AnyFeatureEnabled =>
        AppSettings.GetBool("ActiveInterceptEnabled", false) ||
        GameMonitorBackendService.IsEnabled;

    /// <summary>
    /// 按两个功能的开关状态同步后端进程（单一事实来源）：
    ///   有任一功能开启 → 写配置并确保后端在运行；
    ///   全部关闭 → 停掉后端。
    /// 后端已运行但配置与期望不一致（如只开拦截时又打开了游戏监控）→ 重启后端，
    /// 否则新开关永远不会生效（后端只在启动时读一次配置）。
    /// 拦截开关切换时调用本方法而不是裸 Start/Stop —— 只有拦截关闭而游戏监控
    /// 仍然开着时，后端进程必须继续常驻（只是不再装配拦截子系统）。
    /// </summary>
    public static void SyncBackend()
    {
        if (RuntimeHelper.IsMsixPackaged) return;

        if (AnyFeatureEnabled)
        {
            lock (_lock)
            {
                bool running = _process is not null && !_process.HasExited;
                if (running && ConfigDiffersFromDesired())
                {
                    // 配置漂移：停旧（释放互斥锁）→ 启新（加载新配置）
                    Stop();
                    Start();
                    return;
                }
            }
            Start();
        }
        else
        {
            Stop();
        }
    }

    /// <summary>重启后端使配置变更生效（仅在任一功能开启时有意义）。</summary>
    public static void RestartBackend()
    {
        if (RuntimeHelper.IsMsixPackaged) return;
        if (!AnyFeatureEnabled) return;
        Stop();
        Start();
    }

    private static void WriteConfig()
    {
        try
        {
            var json = BuildConfigJson();
            var dir = Path.GetDirectoryName(ConfigPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ActiveIntercept] 写配置失败：{ex.Message}");
        }
    }

    private static string BuildConfigJson()
    {
        var dataDir = ConfigManager.GetDataDir();
        var config = new BackendConfigDto
        {
            PollIntervalSeconds = 10,
            DataDir = dataDir,
            LogFile = Path.Combine(dataDir, "active_intercept", "backend.log"),
            NotifyMode = AppSettings.Get("ActiveInterceptNotifyMode") ?? "always",
            NotifyCooldownMinutes = Math.Max(1, AppSettings.GetInt("ActiveInterceptNotifyCooldownMinutes", 30)),
            MaxEventRows = 1000,
            // 功能隔离：两个子系统各自独立开关，没开的在后端里绝不装配
            EnableIntercept = AppSettings.GetBool("ActiveInterceptEnabled", false),
            EnableGameMonitor = GameMonitorBackendService.IsEnabled,
        };
        return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// 磁盘上的后端配置是否与当前设置期望的不一致（不一致说明某个功能开关
    /// 在后端运行期间发生了变化，需要重启后端才能生效）。
    /// </summary>
    private static bool ConfigDiffersFromDesired()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return true;
            var onDisk = File.ReadAllText(ConfigPath);
            return onDisk.Trim() != BuildConfigJson().Trim();
        }
        catch
        {
            return false; // 读不了就别乱重启
        }
    }

    private sealed class BackendConfigDto
    {
        public int PollIntervalSeconds { get; set; }
        public string DataDir { get; set; } = "";
        public string LogFile { get; set; } = "";
        public string NotifyMode { get; set; } = "always";
        public int NotifyCooldownMinutes { get; set; } = 30;
        public int MaxEventRows { get; set; } = 1000;
        public bool EnableIntercept { get; set; } = true;
        public bool EnableGameMonitor { get; set; }
    }

    // ========== 通知监控 ==========
    // 后端写入 active_intercept/notifications/*.json，本服务监听并弹出 Windows 原生 Toast。
    // 点击 Toast 启动主程序 --show-active-intercept 跳转审核页。

    private static void StartNotificationWatcher()
    {
        try
        {
            var notifDir = Path.Combine(ConfigManager.GetDataDir(), "active_intercept", "notifications");
            Directory.CreateDirectory(notifDir);

            // 先处理已有的积压通知
            ProcessPendingNotifications(notifDir);

            _notifWatcher = new FileSystemWatcher(notifDir, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            _notifWatcher.Created += (_, e) =>
            {
                // FileSystemWatcher 回调在后台线程，延迟处理等文件写入完成
                System.Threading.Tasks.Task.Delay(200).ContinueWith(_ => ShowNotificationFromFile(e.FullPath));
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ActiveIntercept] 通知监控启动失败：{ex.Message}");
        }
    }

    private static void StopNotificationWatcher()
    {
        try
        {
            _notifWatcher?.Dispose();
            _notifWatcher = null;
        }
        catch { }
    }

    private static void ProcessPendingNotifications(string notifDir)
    {
        try
        {
            foreach (var file in Directory.GetFiles(notifDir, "*.json"))
            {
                ShowNotificationFromFile(file);
            }
        }
        catch { }
    }

    private static void ShowNotificationFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return;
            var json = File.ReadAllText(filePath);
            var req = JsonSerializer.Deserialize(json, ActiveInterceptJsonContext.Default.NotificationRequest);
            if (req is null || string.IsNullOrWhiteSpace(req.Title)) return;

            // 用 Microsoft.Toolkit.Uwp.Notifications 弹出 Windows 原生 Toast
            // 点击时以 --show-active-intercept 启动主程序，跳转审核页
            new ToastContentBuilder()
                .AddText(req.Title)
                .AddText(req.Body)
                .AddArgument("action", "show-active-intercept")
                .Show(toast =>
                {
                    toast.ExpirationTime = DateTimeOffset.Now.AddMinutes(10);
                });

            // 通知已弹出，删除请求文件
            try { File.Delete(filePath); } catch { }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ActiveIntercept] 弹通知失败：{ex.Message}");
            // 失败不删除文件，下次重试
        }
    }
}
