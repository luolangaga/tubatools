using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace TubaWinUi3.Services;

/// <summary>
/// 单实例闸门 + 激活转发：任何方式再次启动工具箱时不再开第二个实例，而是吊起已运行实例的窗口，
/// 并按新进程的 <see cref="LaunchIntent"/> 导航（--open-builtin / --file-lock / --show-active-intercept）。
///
/// 机制（全部复用仓库既有、已验证的写法）：
/// - 单实例判据 = 命名互斥体 <see cref="MutexName"/>（原 App 里的名字，保持不变）；
/// - 跨进程激活通道 = 数据目录下的 JSON 文件 + FileSystemWatcher（与 Services\PhoneLink\PhoneLinkActivation 同构）；
/// - 提权重启接管 = Mutex.WaitOne + AbandonedMutexException（与 TubaWinUI3.BackEnd\Program.cs 一致）。
///
/// 注意：闸门只能在 <c>App.OnLaunchedCore</c> 的自动提权块之后启用——真正留下窗口的进程一定是管理员；
/// 且 --copy-path / --toast / --phone-toast-handler / --energystar-silent 等无窗口辅助模式
/// 都在闸门之前 return，本类只服务于「要建主窗口」的启动路径。
/// </summary>
public static class SingleInstanceService
{
    /// <summary>主实例互斥体名。与旧 App 实现同名，保证既有「主实例在跑」判定继续成立。</summary>
    public const string MutexName = "TubaWinUi3.MainInstance";

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    /// <summary>归零：把前台权限交还给系统，任何进程都可夺取（转发进程在此场景下用不到，但语义最保守）。</summary>
    private const int AsfwAny = -1;

    private sealed class ActivationRequest
    {
        public string Kind { get; set; } = nameof(LaunchIntentKind.Normal);
        public string? Arg { get; set; }
    }

    private static readonly object _gate = new();
    private static Mutex? _mutex;
    private static FileSystemWatcher? _watcher;

    /// <summary>激活请求目录：新进程写，主实例读。</summary>
    public static string ActivationDir => Path.Combine(ConfigManager.GetDataDir(), "instance");

    /// <summary>主实例自身 PID 文件，供转发进程 AllowSetForegroundWindow 使用。</summary>
    private static string OwnerPidPath => Path.Combine(ActivationDir, "owner.pid");

    /// <summary>主实例是否在运行（只探测，不获取所有权）。</summary>
    public static bool IsRunning()
    {
        try { return Mutex.TryOpenExisting(MutexName, out _); }
        catch { return false; }
    }

    /// <summary>
    /// 尝试成为主实例。
    /// <list type="bullet">
    /// <item><paramref name="takeover"/>=false：非阻塞地创建互斥体；已存在（有别的实例在跑）返回 false，
    /// 调用方随后用 <see cref="Forward"/> 转发意图并退出。</item>
    /// <item><paramref name="takeover"/>=true：等待旧实例退出（父实例会释放或内核置为 abandoned）后接管，
    /// 上限 30s；接管成功返回 true，超时则退回转发逻辑（返回 false）。</item>
    /// </list>
    /// </summary>
    public static bool TryEnter(bool takeover)
    {
        if (!takeover)
        {
            try
            {
                var mutex = new Mutex(true, MutexName, out var createdNew);
                if (createdNew)
                {
                    lock (_gate) _mutex = mutex;
                    return true;
                }

                // 已存在：未获取所有权，直接释放句柄交给转发（ReleaseMutex 对非持有者会抛异常）。
                mutex.Dispose();
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstance] 创建互斥体失败，按普通启动继续: {ex.Message}");
                return true;
            }
        }

        // takeover：轮询等待旧实例退出。旧实例正常释放 → WaitOne 成功；崩溃/强退 → AbandonedMutexException。
        var deadline = Environment.TickCount64 + 30_000;
        while (Environment.TickCount64 < deadline)
        {
            try
            {
                var mutex = new Mutex(false, MutexName);
                bool acquired;
                try
                {
                    acquired = mutex.WaitOne(1000);
                }
                catch (AbandonedMutexException)
                {
                    // 旧实例崩溃/被强杀：内核把互斥体置为 abandoned，此实例接管所有权。
                    acquired = true;
                }

                if (acquired)
                {
                    lock (_gate) _mutex = mutex;
                    return true;
                }

                mutex.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SingleInstance] 接管等待异常（重试）: {ex.Message}");
            }
        }

        Debug.WriteLine("[SingleInstance] 接管等待超时，退回转发逻辑");
        return false;
    }

    /// <summary>
    /// 把 <paramref name="intent"/> 转发给已运行的主实例并让出前台权限。
    /// <see cref="LaunchIntentKind.GameOverlayAuto"/> 直接忽略——用户正在游戏，
    /// 主实例自身的 <see cref="GameOverlayAutoService"/> 已在轮询信号文件自动显示悬浮窗，
    /// 这里绝不能恢复/置前主窗口去抢焦点。
    /// </summary>
    public static void Forward(LaunchIntent intent)
    {
        if (intent.Kind == LaunchIntentKind.GameOverlayAuto) return;

        // 尽力把前台权限交给主实例：从后台进程 SetForegroundWindow 常被系统拒绝，
        // AllowSetForegroundWindow(ownerPid) 让主实例随后的置前合法。
        TryAllowForegroundToOwner();

        try
        {
            Directory.CreateDirectory(ActivationDir);
            var payload = JsonSerializer.Serialize(new ActivationRequest
            {
                Kind = intent.Kind.ToString(),
                Arg = intent.Arg
            });
            var name = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
            File.WriteAllText(Path.Combine(ActivationDir, name[..30] + ".json"), payload);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SingleInstance] 写激活文件失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 主实例启动后调用：先处理积压的激活文件（覆盖「转发先于监听就绪」的竞态——
    /// 新进程可能在主实例建 watcher 之前就写好意图文件），再监听后续请求。
    /// 回调在后台线程触发，调用方需自行调度到 UI 线程。
    /// </summary>
    public static void StartWatcher(Action<LaunchIntent> onActivate)
    {
        try
        {
            Directory.CreateDirectory(ActivationDir);

            // 先消费积压文件（与 PhoneLinkActivation 同款）：晚启动的主实例不会漏掉早到的转发意图。
            foreach (var pending in Directory.GetFiles(ActivationDir, "*.json"))
                HandleFile(pending, onActivate);

            try { File.WriteAllText(OwnerPidPath, Environment.ProcessId.ToString()); } catch { }

            _watcher = new FileSystemWatcher(ActivationDir, "*.json")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            _watcher.Created += (_, e) =>
                Task.Delay(150).ContinueWith(_ => HandleFile(e.FullPath, onActivate));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SingleInstance] 激活监听启动失败：{ex.Message}");
        }
    }

    /// <summary>退出流程调用：停监听、释放互斥体所有权、删 PID 文件。</summary>
    public static void Shutdown()
    {
        try
        {
            _watcher?.Dispose();
            _watcher = null;
        }
        catch { }

        try { if (File.Exists(OwnerPidPath)) File.Delete(OwnerPidPath); } catch { }

        lock (_gate)
        {
            if (_mutex is null) return;
            try { _mutex.ReleaseMutex(); } catch { }
            try { _mutex.Dispose(); } catch { }
            _mutex = null;
        }
    }

    private static void HandleFile(string filePath, Action<LaunchIntent> onActivate)
    {
        try
        {
            if (!File.Exists(filePath)) return;
            var text = File.ReadAllText(filePath);
            try { File.Delete(filePath); } catch { }

            var req = JsonSerializer.Deserialize<ActivationRequest>(text);
            if (req is null) return;

            var kind = Enum.TryParse<LaunchIntentKind>(req.Kind, ignoreCase: true, out var parsed)
                ? parsed
                : LaunchIntentKind.Normal;
            onActivate(new LaunchIntent(kind, req.Arg));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SingleInstance] 处理激活文件失败：{ex.Message}");
        }
    }

    private static void TryAllowForegroundToOwner()
    {
        try
        {
            if (!File.Exists(OwnerPidPath)) return;
            if (!int.TryParse(File.ReadAllText(OwnerPidPath).Trim(), out var pid) || pid <= 0) return;
            AllowSetForegroundWindow(pid);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SingleInstance] AllowSetForegroundWindow 失败（已忽略）: {ex.Message}");
        }
    }
}
