using System.Text.Json;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.UI.Dispatching;

namespace TubaWinUi3.Services.ActiveIntercept;

/// <summary>
/// 主动拦截的门面：进程内运行时的启停、通知文件监视、与工作区（管道客户端）的衔接。
///
/// 2026-11 起不再有独立的 TubaWinUI3.BackEnd.exe：拦截轮询、注册表隔离、管道服务
/// 全部由 <see cref="ActiveInterceptRuntime"/> 在主程序进程内装配。因此：
///   - 主程序在跑 → 功能可用；主程序退出 → 拦截/游戏监控随之停止；
///   - 开关变化不再创建/删除计划任务，只更新设置并热启停运行时。
/// </summary>
public static class ActiveInterceptService
{
    private static readonly object _lock = new();

    /// <summary>通知文件监控器（整个进程只装一次）。</summary>
    private static FileSystemWatcher? _notifWatcher;

    /// <summary>拦截子系统（轮询 + 管道服务）是否在运行。</summary>
    public static bool IsRunning => ActiveInterceptRuntime.IsInterceptRunning;

    /// <summary>开机自启设置项（仅控制主程序本身是否随登录启动）。</summary>
    public const string EnabledSettingKey = "ActiveInterceptEnabled";

    /// <summary>启用/关闭主动拦截：写设置 → 热启停运行时；启动失败时回滚设置，避免 UI 与真实状态不一致。</summary>
    public static bool SetEnabled(bool enabled)
    {
        if (enabled && !LaunchElevation.IsRunningAsAdmin()) return false;

        AppSettings.Set(EnabledSettingKey, enabled);
        AppSettings.Flush();

        var ok = SyncBackend();
        if (enabled && !ok)
        {
            AppSettings.Set(EnabledSettingKey, false);
            AppSettings.Flush();
            SyncBackend();
            return false;
        }
        if (!enabled)
        {
            try { InterceptWorkspace.Shutdown(); } catch { }
        }
        return true;
    }

    /// <summary>按当前设置同步运行时（App 启动、开关切换、游戏监控开关都会走这里）。</summary>
    public static bool SyncBackend()
    {
        var ok = ActiveInterceptRuntime.Sync();

        if (ActiveInterceptRuntime.IsInterceptRunning)
        {
            StartNotificationWatcher();
            TryInitializeWorkspace();
        }
        else
        {
            StopNotificationWatcher();
        }
        return ok;
    }

    /// <summary>拦截参数（通知模式等）变更后重建轮询器，让新配置立即生效。</summary>
    public static void RestartBackend()
    {
        ActiveInterceptRuntime.RestartIntercept();
        if (ActiveInterceptRuntime.IsInterceptRunning) TryInitializeWorkspace();
    }

    /// <summary>启动拦截运行时（幂等；仅当设置已开启时才真正装配）。</summary>
    public static bool Start() => SyncBackend();

    /// <summary>主程序退出：停止运行时、通知监视与管道客户端。</summary>
    public static void Stop()
    {
        ActiveInterceptRuntime.Shutdown();
        StopNotificationWatcher();
        try { InterceptWorkspace.Shutdown(); } catch { }
    }

    /// <summary>兼容旧调用点的别名。</summary>
    public static void Shutdown() => Stop();

    private static void TryInitializeWorkspace()
    {
        try
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            if (queue is not null) InterceptWorkspace.Initialize(queue);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ActiveIntercept] 工作区初始化失败：{ex.Message}");
        }
    }

    // ========== 通知监控 ==========
    // 运行时写入 active_intercept/notifications/*.json，本服务监听并弹出 Windows 原生 Toast。
    // 点击 Toast 启动主程序 --show-active-intercept 跳转审核页。

    private static void StartNotificationWatcher()
    {
        lock (_lock)
        {
            if (_notifWatcher is not null) return;
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
                    Task.Delay(200).ContinueWith(_ => ShowNotificationFromFile(e.FullPath));
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ActiveIntercept] 通知监控启动失败：{ex.Message}");
            }
        }
    }

    private static void StopNotificationWatcher()
    {
        lock (_lock)
        {
            try
            {
                _notifWatcher?.Dispose();
                _notifWatcher = null;
            }
            catch { }
        }
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
