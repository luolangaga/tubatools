using System.Diagnostics;
using System.Text.Json;
using Windows.System;

namespace TubaWinUi3.Services;

/// <summary>
/// 游戏后台自动覆盖层（2026-09-11 重构）：
/// 后端（TubaWinUI3.BackEnd.exe）检测到全屏游戏后，把信号写入数据目录下的
/// game_overlay_signal.json；本服务在主程序启动后常驻轮询该文件（1 秒），
/// 命中时用现成的 Pages.GameOverlayWindow 显示悬浮窗并绑定游戏窗口，
/// 游戏退出前台（或信号过期 —— 后端意外退出）后自动关闭。
///
/// 与手动覆盖层的共存规则：
/// - 本服务只管理自己启动的覆盖层（_autoManaged 标记）；
/// - 用户在游戏监控页手动启动的覆盖层绝不会被自动关闭或改绑窗口。
/// </summary>
public sealed class GameOverlayAutoService
{
    public static GameOverlayAutoService Instance { get; } = new();

    private const string SignalFileName = "game_overlay_signal.json";
    private const string SettingsPrefix = "GameOverlay_";
    /// <summary>信号超过该时长未刷新视为后端已退出/失效（后端每 ≤6s 刷新一次时间戳）。</summary>
    private static readonly TimeSpan SignalStaleAfter = TimeSpan.FromSeconds(60);

    private DispatcherQueueTimer? _pollTimer;
    private DispatcherQueueTimer? _sampleTimer;
    private bool _autoManaged;          // 当前覆盖层实例是否由本服务启动
    private bool _samplingInFlight;     // 采样防重入
    private long _lastHwnd;
    private bool _started;
    private bool _lastLoggedActive;     // 日志防抖：只在状态变化时记录
    private GameMonitorRecordSession? _session;  // 自动数据记录（默认开启，游戏退出自动落盘+打开查看窗口）

    /// <summary>自动记录开关（默认开；写 "false" 关闭）。</summary>
    private const string AutoRecordSetting = "GameOverlay_AutoRecord";
    private const string RecordMetricsSetting = "GameOverlay_RecordMetrics";
    private const string RecordOutputsSetting = "GameOverlay_RecordOutputs";

    private GameOverlayAutoService() { }

    /// <summary>文件日志：Debug.WriteLine 在运行中的程序里完全不可见，
    /// 「打开了但没显示悬浮窗」这类问题必须靠文件日志定位。</summary>
    internal static void Log(string message)
    {
        try
        {
            var path = Path.Combine(ConfigManager.GetDataDir(), "game_overlay_auto.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>主程序启动时调用（幂等）。必须在 UI 线程。</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _pollTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(1);
        _pollTimer.Tick += (_, _) => PollSignal();
        _pollTimer.Start();
        Log("轮询已启动");
    }

    public void Stop()
    {
        if (_pollTimer is not null)
        {
            _pollTimer.Stop();
            _pollTimer = null;
        }
        CloseAutoOverlay();
        _started = false;
    }

    private void PollSignal()
    {
        var path = Path.Combine(ConfigManager.GetDataDir(), SignalFileName);
        if (!File.Exists(path)) return;

        SignalData? signal;
        try
        {
            signal = JsonSerializer.Deserialize<SignalData>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Log($"读取/解析信号失败（原子写间隙或坏 JSON，下个 tick 重试）: {ex.Message}");
            return;
        }
        if (signal is null) return;

        bool active = signal.Active;
        bool stale = false;
        if (active && DateTime.TryParse(signal.Utc, out var utc) &&
            DateTimeOffset.UtcNow - utc > SignalStaleAfter)
        {
            active = false; // 后端意外退出 → 信号过期，当作 inactive 处理
            stale = true;
        }

        if (active != _lastLoggedActive)
        {
            _lastLoggedActive = active;
            Log($"信号状态 → active={active}{(stale ? "（信号过期，后端疑似已退出）" : "")} process={signal.Process} pid={signal.Pid} hwnd=0x{signal.Hwnd:X}");
        }

        if (active)
        {
            var hwnd = new IntPtr(signal.Hwnd);
            if (hwnd == IntPtr.Zero || !Pages.GameOverlayWindow.IsWindowSafe(hwnd))
            {
                // 窗口句柄无效（游戏可能刚切换加载画面）：暂不处理，等下一个有效信号
                if (!_autoManaged)
                {
                    Log($"信号 active 但 hwnd 无效（0x{signal.Hwnd:X}，IsWindow=false），等待下一个有效信号");
                    return;
                }
                hwnd = IntPtr.Zero; // 继续用旧窗口
            }

            if (Pages.GameOverlayWindow.Instance is not null)
            {
                if (_autoManaged)
                {
                    // 游戏窗口切换（如全屏 → 窗口化再回全屏）：跟随新窗口
                    if (hwnd != IntPtr.Zero && hwnd.ToInt64() != _lastHwnd)
                    {
                        _lastHwnd = hwnd.ToInt64();
                        Pages.GameOverlayWindow.Instance.SetTargetWindow(hwnd);
                    }
                }
                // 用户手动开的覆盖层：完全不动
                return;
            }

            ShowAutoOverlay(hwnd, signal.Process);
            _lastHwnd = hwnd.ToInt64();
        }
        else
        {
            if (_autoManaged) CloseAutoOverlay();
        }
    }

    private void ShowAutoOverlay(IntPtr targetHwnd, string process)
    {
        Log($"开始自动显示悬浮窗（hwnd=0x{targetHwnd:X}）");
        try
        {
            var widgets = ParseLayout(AppSettings.Get(SettingsPrefix + "Layout") ?? "");
            if (widgets.Count == 0)
            {
                // 用户还没配置过布局：给一个最小可用默认（FPS 数字 + 曲线）
                widgets =
                [
                    new Pages.GameOverlayWindow.WidgetInstance
                    { Type = Pages.OverlayWidgetType.FpsText, X = 12, Y = 12, Width = 216, Height = 36, FontSize = 20 },
                    new Pages.GameOverlayWindow.WidgetInstance
                    { Type = Pages.OverlayWidgetType.FpsChart, X = 12, Y = 56, Width = 216, Height = 56 },
                ];
            }

            // 配置兜底逻辑与 GameOverlayPage.LoadConfig 一致（NaN/越界回默认）
            double cw = AppSettings.GetDouble(SettingsPrefix + "CanvasW", 600);
            double chh = AppSettings.GetDouble(SettingsPrefix + "CanvasH", 300);
            if (double.IsNaN(cw) || cw < 200) cw = 600;
            if (double.IsNaN(chh) || chh < 100) chh = 300;

            double bgOp = AppSettings.GetDouble(SettingsPrefix + "BgOpacity", 70);
            if (double.IsNaN(bgOp) || bgOp < 0 || bgOp > 100) bgOp = 70;

            double refresh = AppSettings.GetDouble(SettingsPrefix + "Refresh", 1000);
            if (double.IsNaN(refresh) || refresh < 200) refresh = 1000;

            var font = AppSettings.Get(SettingsPrefix + "FontFamily");
            if (!string.IsNullOrEmpty(font)) Pages.GameOverlayWindow.SetFontFamily(font);

            Pages.GameOverlayWindow.ShowOverlay(
                targetHwnd,
                widgets,
                (float)(bgOp / 100),
                MapPosition(AppSettings.GetInt(SettingsPrefix + "Position", 0)),
                (int)cw, (int)chh,
                desktopMode: false,
                oledProtection: AppSettings.Get(SettingsPrefix + "OledProtection") == "true");

            _autoManaged = true;
            StartSampling(TimeSpan.FromMilliseconds(refresh));
            StartAutoRecord(process);
            Log($"悬浮窗已自动显示（hwnd=0x{targetHwnd:X}, {widgets.Count} 组件, canvas={cw:F0}x{chh:F0}, 布局键长度={(AppSettings.Get(SettingsPrefix + "Layout") ?? "").Length}）");
        }
        catch (Exception ex)
        {
            Log($"自动显示失败: {ex}");
        }
    }

    private void CloseAutoOverlay()
    {
        if (!_autoManaged) return;
        StopSampling();
        Pages.GameOverlayWindow.CloseOverlay();
        _autoManaged = false;
        _lastHwnd = 0;
        FinishAutoRecord(openViewer: true); // 游戏退出 → 落盘并自动打开记录查看窗口
        Log("悬浮窗已自动关闭");
    }

    // ═══════════ 自动数据记录（默认开启；与页面记录共用 GameMonitorRecordSession） ═══════════

    /// <summary>检测到游戏、悬浮窗显示时启动记录会话。指标/格式沿用页面保存的设置（未配置 = 全指标 + 默认格式）。</summary>
    private void StartAutoRecord(string process)
    {
        if (AppSettings.Get(AutoRecordSetting) == "false")
        {
            Log("自动记录已关闭（GameOverlay_AutoRecord=false），跳过");
            return;
        }
        try
        {
            var metrics = GameMonitorRecorder.ParseSelection(AppSettings.Get(RecordMetricsSetting));
            if (metrics.Count == 0)
            {
                Log("自动记录跳过：记录指标勾选为空");
                return;
            }
            var outputs = GameMonitorRecorder.ParseOutputs(AppSettings.Get(RecordOutputsSetting));
            var interval = AppSettings.GetDouble(SettingsPrefix + "Refresh", 1000);
            if (double.IsNaN(interval) || interval < 200) interval = 1000;

            _session = new GameMonitorRecordSession();
            _session.Start(metrics, outputs, (int)interval, process);
            Log($"自动记录已启动（{metrics.Count} 项指标, 间隔 {interval:F0}ms, 格式={outputs}）");
        }
        catch (Exception ex)
        {
            Log($"自动记录启动失败: {ex.Message}");
        }
    }

    /// <summary>结束记录会话并落盘。openViewer=true 时自动打开记录查看窗口（游戏正常退出）。</summary>
    private void FinishAutoRecord(bool openViewer)
    {
        var session = _session;
        _session = null;
        if (session is null || !session.Active) return;
        try
        {
            var result = session.Stop();
            if (result is null)
            {
                Log("自动记录结束：无有效采样，未生成文件");
                return;
            }
            Log($"自动记录已保存：{string.Join("、", result.Paths.Select(Path.GetFileName))}（{result.Samples.Count} 条采样）");
            if (!openViewer) return;
            try
            {
                // 优先用 JSON（记录查看页的原生格式），没有就用第一个文件
                var file = result.Paths.FirstOrDefault(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                           ?? result.Paths[0];
                BuiltinToolWindow.Show(typeof(Pages.GameMonitorRecordsPage), file, "游戏监控 · 记录查看");
            }
            catch (Exception ex)
            {
                Log($"自动打开记录查看窗口失败: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Log($"自动记录落盘失败: {ex.Message}");
        }
    }

    // ═══════════ 采样（与 GameOverlayPage.OnPollTick 同款） ═══════════

    private void StartSampling(TimeSpan interval)
    {
        StopSampling();
        _sampleTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _sampleTimer.Interval = interval;
        _sampleTimer.Tick += OnSampleTick;
        _sampleTimer.Start();
        // 立即采一次，避免悬浮窗空 1 秒
        _ = DoSampleAsync();
    }

    private void StopSampling()
    {
        if (_sampleTimer is null) return;
        _sampleTimer.Tick -= OnSampleTick;
        _sampleTimer.Stop();
        _sampleTimer = null;
    }

    private async void OnSampleTick(object? sender, object e) => await DoSampleAsync();

    private async Task DoSampleAsync()
    {
        if (!_autoManaged || _samplingInFlight) return;
        _samplingInFlight = true;
        try
        {
            var sample = await Task.Run(() => LiteMonitorService.Instance.Read(fpsEnabled: true));
            if (!_autoManaged) return; // 期间已被关闭，丢弃迟到采样
            Pages.GameOverlayWindow.Instance?.UpdateData(sample);

            // 自动记录：复用同一条采样，不额外增加硬件读取开销
            if (_session is { Active: true } s)
            {
                s.Append(sample);
                if (s.ExceededMaxDuration)
                {
                    // 达到单次记录硬上限：立即落盘（不弹窗口打扰游戏），悬浮窗继续
                    s.MarkTruncated();
                    Log($"自动记录已达 {GameMonitorRecorder.MaxDurationMinutes} 分钟上限，落盘保存");
                    FinishAutoRecord(openViewer: false);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"采样失败: {ex.Message}");
        }
        finally
        {
            _samplingInFlight = false;
        }
    }

    // ═══════════ 配置解析 ═══════════

    /// <summary>解析 GameOverlay_Layout JSON → 覆盖层组件（字段与 GameOverlayPage.SerializeLayout 一致）。</summary>
    private static List<Pages.GameOverlayWindow.WidgetInstance> ParseLayout(string json)
    {
        var list = new List<Pages.GameOverlayWindow.WidgetInstance>();
        if (string.IsNullOrEmpty(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                try
                {
                    var type = (Pages.OverlayWidgetType)item.GetProperty("type").GetInt32();
                    // 坐标/尺寸按 double 读：拖拽保存的布局可能带小数，GetInt32 会抛异常
                    double GetD(string name) => item.GetProperty(name).GetDouble();
                    bool showPrefix = item.TryGetProperty("showPrefix", out var sp)
                        ? sp.GetBoolean()
                        : !string.IsNullOrEmpty(item.TryGetProperty("prefix", out var pp) ? pp.GetString() ?? "" : "")
                            && type != Pages.OverlayWidgetType.CustomText;
                    list.Add(new Pages.GameOverlayWindow.WidgetInstance
                    {
                        Type = type,
                        X = (int)GetD("x"),
                        Y = (int)GetD("y"),
                        Width = (int)GetD("w"),
                        Height = (int)GetD("h"),
                        FontSize = (int)GetD("fs"),
                        Prefix = item.TryGetProperty("prefix", out var p) ? p.GetString() ?? "" : "",
                        ShowPrefix = showPrefix,
                        Layer = item.TryGetProperty("layer", out var ly) ? ly.GetInt32() : 0,
                        CustomText = item.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
                        ImagePath = item.TryGetProperty("img", out var im) ? im.GetString() ?? "" : "",
                        ColorArgb = item.TryGetProperty("color", out var cl) && cl.TryGetUInt32(out var cc) ? cc : 0xFF00A0FF,
                        TextColorArgb = item.TryGetProperty("tcolor", out var tc) && tc.TryGetUInt32(out var tcv) ? tcv : 0xFFFFFFFFu,
                        IsChart = type is Pages.OverlayWidgetType.FpsChart
                            or Pages.OverlayWidgetType.CpuTempChart,
                    });
                }
                catch
                {
                    // 单组件解析失败不拖垮整个布局
                }
            }
        }
        catch
        {
        }
        return list;
    }

    private static Pages.GameOverlayWindow.OverlayPosition MapPosition(int index) => index switch
    {
        0 => Pages.GameOverlayWindow.OverlayPosition.TopLeft,
        1 => Pages.GameOverlayWindow.OverlayPosition.TopCenter,
        2 => Pages.GameOverlayWindow.OverlayPosition.TopRight,
        3 => Pages.GameOverlayWindow.OverlayPosition.MiddleLeft,
        4 => Pages.GameOverlayWindow.OverlayPosition.Center,
        5 => Pages.GameOverlayWindow.OverlayPosition.MiddleRight,
        6 => Pages.GameOverlayWindow.OverlayPosition.BottomLeft,
        7 => Pages.GameOverlayWindow.OverlayPosition.BottomCenter,
        8 => Pages.GameOverlayWindow.OverlayPosition.BottomRight,
        _ => Pages.GameOverlayWindow.OverlayPosition.TopLeft,
    };

    private sealed class SignalData
    {
        // 键名必须与后端 FrontendOverlayBridge.OverlaySignal 的 JsonPropertyName 一致；
        // System.Text.Json 默认区分大小写，漏了这里的 attribute 会导致 Active 恒为 false
        //（信号永远"读不到"，悬浮窗永远不显示 —— 2026-09-11 实测踩坑）。
        [System.Text.Json.Serialization.JsonPropertyName("active")]
        public bool Active { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("process")]
        public string Process { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("pid")]
        public uint Pid { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("hwnd")]
        public long Hwnd { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("utc")]
        public string Utc { get; set; } = "";
    }
}
