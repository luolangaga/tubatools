using System.Diagnostics;
using TubaWinUi3.Services.ActiveIntercept.Runtime;
using TubaWinUi3.Services.ActiveIntercept.Runtime.GameMonitor;
using TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

namespace TubaWinUi3.Services.ActiveIntercept;

/// <summary>
/// 主动拦截 / 游戏自动监控的进程内运行时（取代原 TubaWinUI3.BackEnd.exe）。
///
/// 生命周期：
///   - 随主程序启动：主程序启动时按设置装配（App.RunStartupSequenceAsync → <see cref="Sync"/>）；
///   - 随主程序退出：MainWindow_Closed → <see cref="Shutdown"/>；
///   - 开关变化：设置页/工具页调用 <see cref="Sync"/> 做增删，不需要重启整个运行时；
///   - 拦截参数（通知模式/冷却）变化：<see cref="RestartIntercept"/> 重建轮询器。
///
/// 保留原命名管道协议不变：前端 <see cref="InterceptWorkspace"/> 仍按原方式连接，
/// 只是服务端从独立进程搬到了本进程内，管道名、信封、鉴权规则全部沿用。
/// </summary>
public static class ActiveInterceptRuntime
{
    /// <summary>旧版独立后端的计划任务名（升级时清理，避免每次登录拉起已删除的 exe）。</summary>
    public const string LegacyBackendTaskName = "TubaWinUi3ActiveInterceptBackend";

    private static readonly object Gate = new();
    private static Host? _host;

    /// <summary>拦截子系统（轮询 + 管道服务）是否在运行。</summary>
    public static bool IsInterceptRunning
    {
        get { lock (Gate) return _host?.IsInterceptRunning == true; }
    }

    /// <summary>游戏自动监控检测器是否在运行。</summary>
    public static bool IsGameMonitorRunning
    {
        get { lock (Gate) return _host?.IsGameMonitorRunning == true; }
    }

    /// <summary>是否还有任一功能需要运行时（以当前设置为准）。</summary>
    public static bool AnyFeatureEnabled =>
        AppSettings.GetBool("ActiveInterceptEnabled", false) ||
        GameMonitorAutoService.IsEnabled;

    /// <summary>
    /// 按设置同步运行时。幂等；只增量装配开关发生变化的那一部分。
    /// 返回 false 仅当需要拦截但启动失败（例如当前进程不是管理员）。
    /// </summary>
    public static bool Sync()
    {
        var wantIntercept = AppSettings.GetBool("ActiveInterceptEnabled", false);
        var wantGame = GameMonitorAutoService.IsEnabled;

        // 拦截要写 HKLM，必须管理员；非管理员时如实降级为「未运行」。
        if (wantIntercept && !LaunchElevation.IsRunningAsAdmin())
        {
            BackEndLog.Warn("主动拦截：当前进程不是管理员，拦截子系统未启动。");
            wantIntercept = false;
        }

        lock (Gate)
        {
            _host ??= new Host();

            bool ok = true;
            if (wantIntercept && !_host.IsInterceptRunning)
                ok &= _host.StartIntercept();
            else if (!wantIntercept && _host.IsInterceptRunning)
                _host.StopIntercept();

            if (wantGame && !_host.IsGameMonitorRunning)
                _host.StartGameMonitor();
            else if (!wantGame && _host.IsGameMonitorRunning)
                _host.StopGameMonitor();

            if (!wantIntercept && !wantGame)
            {
                _host.Dispose();
                _host = null;
            }

            return ok;
        }
    }

    /// <summary>重建拦截子系统（通知模式等只读配置变更后调用）。</summary>
    public static void RestartIntercept()
    {
        lock (Gate)
        {
            if (_host?.IsInterceptRunning != true) return;
            _host.StopIntercept();
            _host.StartIntercept();
        }
    }

    /// <summary>主程序退出时调用：停止全部后台线程与管道服务。</summary>
    public static void Shutdown()
    {
        lock (Gate)
        {
            _host?.Dispose();
            _host = null;
        }
    }

    /// <summary>
    /// 升级清理（只做一次，后台执行）：
    /// 1. 删除旧版后端计划任务，避免登录时拉起已不存在的 TubaWinUI3.BackEnd.exe；
    /// 2. 结束残留的旧后端进程；
    /// 3. 删除旧版的 active_intercept/config.json（运行时不再读取）。
    /// </summary>
    public static void CleanupLegacyBackend()
    {
        Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Delete /TN \"{LegacyBackendTaskName}\" /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    proc.WaitForExit(3000);
                    if (proc.ExitCode == 0)
                        BackEndLog.Info($"已清理旧版后端计划任务：{LegacyBackendTaskName}");
                }
            }
            catch (Exception ex)
            {
                BackEndLog.Debug($"清理旧版后端计划任务跳过：{ex.Message}");
            }

            try
            {
                foreach (var p in Process.GetProcessesByName("TubaWinUI3.BackEnd"))
                {
                    try { p.Kill(entireProcessTree: true); p.WaitForExit(2000); } catch { }
                    p.Dispose();
                }
            }
            catch { }

            try
            {
                var legacyConfig = Path.Combine(ConfigManager.GetDataDir(), "active_intercept", "config.json");
                if (File.Exists(legacyConfig)) File.Delete(legacyConfig);
            }
            catch { }
        });
    }

    /// <summary>当前设置对应的后端配置（每次装配时读取一次，运行期不变）。</summary>
    internal static BackendConfig BuildConfig() => new()
    {
        PollIntervalSeconds = 10,
        DataDir = ConfigManager.GetDataDir(),
        LogFile = LogService.PathFor(LogService.Backend),
        NotifyMode = AppSettings.Get("ActiveInterceptNotifyMode") ?? "always",
        NotifyCooldownMinutes = Math.Max(1, AppSettings.GetInt("ActiveInterceptNotifyCooldownMinutes", 30)),
        MaxEventRows = 1000,
        EnableIntercept = AppSettings.GetBool("ActiveInterceptEnabled", false),
        EnableGameMonitor = GameMonitorAutoService.IsEnabled,
    };

    /// <summary>运行时容器：拦截与游戏监控各自独立起停，互不牵连。</summary>
    private sealed class Host : IDisposable
    {
        private InterceptMonitor? _monitor;
        private InterceptRequestHandler? _handler;
        private NamedPipeBackendServer? _server;
        private Thread? _monitorThread;
        private GameMonitorService? _gameMonitor;

        public bool IsInterceptRunning => _server is not null && _monitor is not null;
        public bool IsGameMonitorRunning => _gameMonitor is not null;

        public bool StartIntercept()
        {
            if (IsInterceptRunning) return true;

            try
            {
                var config = BuildConfig();
                var dataDir = Path.Combine(config.DataDir, "active_intercept");
                Directory.CreateDirectory(dataDir);

                _monitor = new InterceptMonitor(config);
                _handler = new InterceptRequestHandler(
                    dataDir,
                    _monitor.State,
                    _monitor.Events,
                    _monitor.BlockEngine,
                    _monitor.Policies,
                    _monitor.Ignore,
                    _monitor.Notifications);
                _server = new NamedPipeBackendServer(_handler.DispatchAsync);

                _handler.Notify = n => _server?.BroadcastNotification(n);
                _monitor.Notify = n => _server?.BroadcastNotification(n);

                _server.Start(CancellationToken.None);

                _monitorThread = new Thread(_monitor.Run)
                {
                    IsBackground = true,
                    Name = "ActiveIntercept-Monitor",
                };
                _monitorThread.Start();

                BackEndLog.Info("主动拦截运行时已启动（进程内）");
                return true;
            }
            catch (Exception ex)
            {
                BackEndLog.Error($"主动拦截运行时启动失败：{ex}");
                StopIntercept();
                return false;
            }
        }

        public void StopIntercept()
        {
            var monitor = _monitor;
            var server = _server;

            try { server?.BroadcastServiceStopping(); } catch { }
            try { server?.Stop(); } catch { }
            try { monitor?.Stop(); } catch { }

            try
            {
                if (_monitorThread is { IsAlive: true })
                    _monitorThread.Join(TimeSpan.FromSeconds(3));
            }
            catch { }

            _monitorThread = null;
            _server = null;
            _handler = null;
            _monitor = null;

            if (monitor is not null)
                BackEndLog.Info("主动拦截运行时已停止");
        }

        public void StartGameMonitor()
        {
            if (IsGameMonitorRunning) return;
            try
            {
                _gameMonitor = new GameMonitorService(BuildConfig());
                _gameMonitor.Start();
                BackEndLog.Info("游戏自动监控已启动（进程内，检测到全屏游戏时显示 FPS 覆盖层）");
            }
            catch (Exception ex)
            {
                BackEndLog.Error($"游戏自动监控启动失败：{ex}");
                try { _gameMonitor?.Dispose(); } catch { }
                _gameMonitor = null;
            }
        }

        public void StopGameMonitor()
        {
            var game = _gameMonitor;
            _gameMonitor = null;
            try { game?.Dispose(); } catch { }
            if (game is not null) BackEndLog.Info("游戏自动监控已停止");
        }

        public void Dispose()
        {
            StopIntercept();
            StopGameMonitor();
        }
    }
}
