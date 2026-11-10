using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public sealed class FpsService : IDisposable
{
    private const string SessionName = "TubaWinUi3_FPS";
    private static readonly Guid DxgKrnlProviderId = FpsPresentEvents.DxgKrnlProviderId;
    private static readonly Guid Win32kProviderId = FpsPresentEvents.Win32kProviderId;

    // present 事件家族常量、同帧去重窗口与 TryRecordPresent 判据已下沉到
    // FpsPresentEvents（FpsTracker.cs）—— 主程序与 NativeAOT 后端共享同一份。

    private readonly ConcurrentDictionary<int, FpsTracker> _trackers = new();
    private readonly PresentMonCapture _presentMon = new();
    private readonly ConcurrentDictionary<int, (string Name, DateTime Expires)> _nameCache = new();
    private readonly object _startLock = new();
    private int _manualFocusPid;
    private volatile bool _running;
    private volatile bool _paused;
    private TraceEventSession? _session;
    private Task? _processTask;
    private Timer? _decayTimer;
    private DateTime _sessionStart;

    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "LiteMonitor", "LiteMonitorFPS", "PresentMon", "Unknown", "TubaWinUi3", "dwm",
        "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost", "RuntimeBroker",
        "ApplicationFrameHost", "sihost", "taskhostw", "ctfmon", "explorer",
        "msedgewebview2", "MicrosoftEdge", "SearchApp", "svchost", "csrss",
        "smss", "lsass", "wininit", "services", "winlogon", "fontdrvhost",
        "dllhost", "conhost", "Taskmgr", "Registry", "MemCompression",
        "ServiceHub", "PerfWatson2", "devenv", "MSBuild",
        "System", "ntoskrnl", "Interrupt", "DPCs", "Idle", "Memory Compression"
    };

    private static readonly HashSet<int> ExcludedPids = new() { 0, 4 };

    /// <summary>
    /// True when the DxgKrnl/Win32k event ID belongs to the per-frame present family.
    /// </summary>
    internal static bool IsPresentEventId(int id) => FpsPresentEvents.IsPresentEventId(id);

    /// <summary>一帧只计一次的 present 判据（实现下沉到共享的 FpsPresentEvents）。</summary>
    internal static bool TryRecordPresent(FpsTracker tracker, int id, long ticks) =>
        FpsPresentEvents.TryRecordPresent(tracker, id, ticks);

    /// <summary>
    /// 读出帧时间与渲染完成延迟（ETW 回退不提供延迟）；-1 = 无有效读数（覆盖层显示 "--"）。
    /// 帧时间与 FPS 使用相同的 1 秒失效口径。
    /// </summary>
    internal static void ReadFrameMetrics(FpsTracker tracker, DateTime nowUtc,
        out float frameTimeMs, out float renderLatencyMs)
    {
        frameTimeMs = renderLatencyMs = -1;
        if (tracker.LastFrameTimeMs > 0 &&
            tracker.LastPresentUtc != DateTime.MinValue &&
            nowUtc - tracker.LastPresentUtc <= TimeSpan.FromMilliseconds(FrameScopeFrameStatistics.WindowMs))
            frameTimeMs = (float)Math.Round(tracker.LastFrameTimeMs, 1);

    }


    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public bool IsPaused => _paused;
    public bool IsRunning => _running;
    public DateTime SessionStart => _sessionStart;

    public static bool IsAdmin()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public void Pause() { _paused = true; }
    public void Resume() { _paused = false; }

    public (float fps, string process) GetFps()
    {
        var stats = GetFpsStats();
        return (stats.fps, stats.process);
    }

    /// <summary>
    /// Returns FPS plus 1% low and 0.1% low for the target (focused) process.
    /// </summary>
    public (float fps, string process, float low1, float low01, float frameTimeMs, float renderLatencyMs) GetFpsStats()
    {
        if (_paused) return (0, "", 0, 0, -1, -1);

        int manualPid = _manualFocusPid;
        int targetPid = manualPid != 0 ? manualPid : GetForegroundWindowPid();
        var preferred = _presentMon.GetMetrics(targetPid);
        if (preferred is { } metrics)
            return (metrics.Fps, GetProcessName(targetPid), metrics.Low1, metrics.Low01,
                metrics.FrameTimeMs, metrics.LatencyMs);

        EnsureRunning();
        // Preserve the manual/foreground selection and session reports on the legacy path.
        if (manualPid != 0 && !_trackers.ContainsKey(manualPid))
        {
            targetPid = GetForegroundWindowPid();
            if (targetPid != manualPid && _presentMon.GetMetrics(targetPid) is { } foreground)
                return (foreground.Fps, GetProcessName(targetPid), foreground.Low1, foreground.Low01,
                    foreground.FrameTimeMs, foreground.LatencyMs);
        }

        if (targetPid != 0 && _trackers.TryGetValue(targetPid, out var tracker) &&
            (targetPid == manualPid || tracker.Fps > 0))
        {
            float low1 = -1, low01 = -1;
            // Same one-second window and floor tail counts as FrameScope.
            var v1 = tracker.OnePercentLow;
            if (v1 > 0) { low1 = (float)Math.Round(v1); if (low1 < 1) low1 = -1; }
            var v01 = tracker.PointOnePercentLow;
            if (v01 > 0) { low01 = (float)Math.Round(v01); if (low01 < 1) low01 = -1; }
            ReadFrameMetrics(tracker, DateTime.UtcNow, out var frameMs, out var latencyMs);
            return (
                (float)Math.Round(tracker.Fps),
                GetProcessName(targetPid),
                low1,
                low01,
                frameMs,
                latencyMs);
        }
        return (0, "", -1, -1, -1, -1);
    }

    private int GetForegroundWindowPid()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return 0;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return 0;
            var name = GetProcessName((int)pid);
            if (Excluded.Contains(name)) return 0;
            return (int)pid;
        }
        catch { return 0; }
    }

    public List<(int pid, string name, float fps)> GetProcessList()
    {
        var list = new List<(int pid, string name, float fps)>();
        var presentMonPids = new HashSet<int>();
        foreach (var (pid, fps) in _presentMon.GetProcesses())
        {
            presentMonPids.Add(pid);
            var name = GetProcessName(pid);
            if (!Excluded.Contains(name)) list.Add((pid, name, fps));
        }
        foreach (var kv in _trackers)
        {
            if (presentMonPids.Contains(kv.Key)) continue;
            if (kv.Value.Fps <= 0) continue;
            if (ExcludedPids.Contains(kv.Key)) continue;
            try
            {
                var name = GetProcessName(kv.Key);
                if (!Excluded.Contains(name))
                    list.Add((kv.Key, name, (float)kv.Value.Fps));
            }
            catch { }
        }
        return list.OrderByDescending(x => x.fps).ToList();
    }

    public List<FpsSnapshot> GetAllSnapshots()
    {
        var list = new List<FpsSnapshot>();
        var presentMonPids = new HashSet<int>();
        foreach (var (pid, snapshot) in _presentMon.GetSnapshots())
        {
            presentMonPids.Add(pid);
            var name = GetProcessName(pid);
            if (Excluded.Contains(name)) continue;
            snapshot.ProcessName = name;
            list.Add(snapshot);
        }
        foreach (var kv in _trackers)
        {
            if (presentMonPids.Contains(kv.Key) || kv.Value.TotalFrames < 2) continue;
            if (ExcludedPids.Contains(kv.Key)) continue;
            try
            {
                var name = GetProcessName(kv.Key);
                if (!Excluded.Contains(name))
                    list.Add(kv.Value.TakeSnapshot(name));
            }
            catch { }
        }
        return list.OrderByDescending(x => x.TotalFrames).ToList();
    }

    public string ExportReport(MonitorSample? hwSample)
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════");
        sb.AppendLine("  TubaWinUi3 帧率分析报告");
        sb.AppendLine($"  生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  统计时段: {_sessionStart:HH:mm:ss} → {DateTime.Now:HH:mm:ss}");
        sb.AppendLine("═══════════════════════════════════════════");
        sb.AppendLine();

        if (hwSample != null)
        {
            sb.AppendLine("【硬件信息】");
            if (!string.IsNullOrEmpty(hwSample.CpuName)) sb.AppendLine($"  CPU: {hwSample.CpuName}");
            if (!string.IsNullOrEmpty(hwSample.GpuName)) sb.AppendLine($"  GPU: {hwSample.GpuName}");
            if (hwSample.MemTotalGB > 0) sb.AppendLine($"  内存: {hwSample.MemTotalGB:F1} GB");
            sb.AppendLine();
        }

        var snapshots = GetAllSnapshots();
        if (snapshots.Count == 0)
        {
            sb.AppendLine("  暂无帧率数据。");
            return sb.ToString();
        }

        sb.AppendLine("【帧率统计（按应用分类）】");
        sb.AppendLine("─────────────────────────────────────────────");
        foreach (var snap in snapshots)
        {
            sb.AppendLine($"  ▸ {snap.ProcessName}");
            sb.AppendLine($"    当前 FPS:   {snap.CurrentFps:0}");
            sb.AppendLine($"    平均 FPS:   {FormatReportFps(snap.AvgFps)}");
            sb.AppendLine($"    最低 FPS:   {FormatReportFps(snap.MinFps)}");
            sb.AppendLine($"    最高 FPS:   {FormatReportFps(snap.MaxFps)}");
            sb.AppendLine($"    1% Low:     {FormatReportFps(snap.OnePercentLow)}");
            sb.AppendLine($"    0.1% Low:   {FormatReportFps(snap.PointOnePercentLow)}");
            sb.AppendLine($"    总帧数:     {snap.TotalFrames}");
            sb.AppendLine($"    统计时长:   {snap.TotalSeconds:F1}s");
            sb.AppendLine();
        }

        if (hwSample != null)
        {
            sb.AppendLine("【硬件状态快照】");
            sb.AppendLine("─────────────────────────────────────────────");
            if (hwSample.CpuLoad >= 0) sb.AppendLine($"  CPU 负载: {hwSample.CpuLoad:0}%");
            if (hwSample.CpuTemp >= 0) sb.AppendLine($"  CPU 温度: {hwSample.CpuTemp:0}°C");
            if (hwSample.CpuClock > 0) sb.AppendLine($"  CPU 频率: {hwSample.CpuClock / 1000f:0.0} GHz");
            if (hwSample.CpuPower > 0) sb.AppendLine($"  CPU 功耗: {hwSample.CpuPower:0.0} W");
            if (hwSample.GpuLoad >= 0) sb.AppendLine($"  GPU 负载: {hwSample.GpuLoad:0}%");
            if (hwSample.GpuTemp >= 0) sb.AppendLine($"  GPU 温度: {hwSample.GpuTemp:0}°C");
            if (hwSample.GpuClock > 0) sb.AppendLine($"  GPU 频率: {hwSample.GpuClock:0} MHz");
            if (hwSample.GpuPower > 0) sb.AppendLine($"  GPU 功耗: {hwSample.GpuPower:0.0} W");
            if (hwSample.GpuVramLoad >= 0) sb.AppendLine($"  显存负载: {hwSample.GpuVramLoad:0}%");
            if (hwSample.GpuVramUsedGB >= 0) sb.AppendLine($"  显存使用: {hwSample.GpuVramUsedGB:F1} GB");
            if (hwSample.MemLoad >= 0) sb.AppendLine($"  内存负载: {hwSample.MemLoad:0}%");
            if (hwSample.MemUsedGB >= 0) sb.AppendLine($"  内存使用: {hwSample.MemUsedGB:F1} / {hwSample.MemTotalGB:F1} GB");
        }

        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════");
        sb.AppendLine("  报告由 TubaWinUi3 硬件监控生成");
        sb.AppendLine("═══════════════════════════════════════════");
        return sb.ToString();
    }

    private static string FormatReportFps(double fps) => fps > 0 ? fps.ToString("0") : "--";

    public void SetFocus(int pid) { _manualFocusPid = pid; }
    public void ClearFocus() { _manualFocusPid = 0; }

    private void EnsureRunning()
    {
        if (_running) return;
        Start();
    }

    private void Start()
    {
        lock (_startLock)
        {
            if (_running) return;
            if (!IsAdmin()) return;

            try
            {
                StopExistingSession();

                var session = new TraceEventSession(SessionName);
                try { session.EnableProvider(DxgKrnlProviderId); } catch { }
                try { session.EnableProvider(Win32kProviderId); } catch { }
                _session = session;
                _running = true;
                _paused = false;
                _sessionStart = DateTime.Now;

                _processTask = Task.Factory.StartNew(() =>
                {
                    try
                    {
                        session.Source.Dynamic.All += OnTraceEvent;
                        session.Source.Process();
                    }
                    catch { }
                    finally
                    {
                        // Only clear state if this is still the live session — a
                        // concurrent Start() may have already replaced it.
                        lock (_startLock)
                        {
                            if (_session == session)
                            {
                                _running = false;
                                _session = null;
                            }
                        }
                        try { session.Dispose(); } catch { }
                    }
                }, TaskCreationOptions.LongRunning);

                // Decay timer: only remove PIDs idle for a long time, NEVER stop the entire session
                _decayTimer = new Timer(_ =>
                {
                    if (_paused) return;
                    var nowUtc = DateTime.UtcNow;
                    var stalePids = new List<int>();
                    foreach (var kv in _trackers)
                    {
                        kv.Value.Decay(nowUtc);
                        // 只移除长期(5 分钟)无帧的进程。短暂暂停/切出不删 tracker ——
                        // 否则累计统计(1%low/0.1%low/平均帧率)反复归零重爬，读数乱跳。
                        if (kv.Value.LastPresentUtc != DateTime.MinValue &&
                            nowUtc - kv.Value.LastPresentUtc > TimeSpan.FromMinutes(5))
                            stalePids.Add(kv.Key);
                    }
                    foreach (var pid in stalePids)
                        _trackers.TryRemove(pid, out var _);
                }, null, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(FrameScopeFrameStatistics.WindowMs));
            }
            catch { _running = false; }
        }
    }

    private void OnTraceEvent(TraceEvent data)
    {
        try
        {
            if (_paused) return;
            int id = (int)data.ID;
            if (!IsPresentEventId(id)) return;
            if (data.ProcessID <= 0) return;
            if (data.ProcessID == Environment.ProcessId) return;
            if (ExcludedPids.Contains(data.ProcessID)) return;

            var name = GetProcessName(data.ProcessID);
            if (Excluded.Contains(name)) return;

            var tracker = _trackers.GetOrAdd(data.ProcessID, _ => new FpsTracker());
            FpsPresentEvents.TryRecordPresent(tracker, id, data.TimeStamp.Ticks);
        }
        catch { }
    }

    private string GetProcessName(int pid)
    {
        // Cache with a short TTL: PIDs get recycled, and a stale name (e.g. "dwm")
        // could wrongly exclude or mislabel the process now owning that PID.
        if (_nameCache.TryGetValue(pid, out var cached) && DateTime.UtcNow < cached.Expires)
            return cached.Name;
        try
        {
            var name = Process.GetProcessById(pid).ProcessName;
            _nameCache[pid] = (name, DateTime.UtcNow.AddSeconds(10));
            return name;
        }
        catch { return "Unknown"; }
    }

    private static void StopExistingSession()
    {
        try
        {
            using var existing = TraceEventSession.GetActiveSession(SessionName);
            if (existing != null) existing.Stop();
        }
        catch { }
    }

    public void Dispose()
    {
        _presentMon.Dispose();
        _running = false;
        _paused = false;
        _decayTimer?.Dispose();
        _decayTimer = null;
        try { _session?.Source?.StopProcessing(); } catch { }
        try { _session?.Dispose(); } catch { }
        _session = null;
        StopExistingSession();
        _trackers.Clear();
    }
}
