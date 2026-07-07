using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "TraceEvent session requires reflection")]
[UnconditionalSuppressMessage("Aot", "IL3050:RequiresDynamicCode", Justification = "ETW trace session requires dynamic code generation")]
public sealed class FpsService : IDisposable
{
    private const string SessionName = "TubaWinUi3_FPS";
    private static readonly Guid DxgKrnlProviderId = new("802EC45A-1E99-4B83-9920-87C98277BA9D");
    private const int PresentEventId = 0x00B8;

    private readonly ConcurrentDictionary<int, FpsTracker> _trackers = new();
    private readonly ConcurrentDictionary<int, string> _nameCache = new();
    private int _manualFocusPid;
    private volatile bool _running;
    private volatile bool _paused;
    private TraceEventSession? _session;
    private Task? _processTask;
    private DateTime _lastAccess = DateTime.MinValue;
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

    private sealed class FpsTracker
    {
        private const int SampleCount = 60;
        private readonly long[] _timestamps = new long[SampleCount];
        private int _index;
        private int _count;
        private double _lastFps;
        private readonly List<double> _frameTimes = new(3600);
        private double _totalFrameTime;
        private int _totalFrames;
        private double _minFps = double.MaxValue;
        private double _maxFps;
        private double _fpsSum;

        public double Fps => _lastFps;
        public double AvgFps => _totalFrames > 0 ? _fpsSum / _totalFrames : 0;
        public double MinFps => _minFps == double.MaxValue ? 0 : _minFps;
        public double MaxFps => _maxFps;
        public double OnePercentLow => CalcPercentileLow(0.01);
        public double PointOnePercentLow => CalcPercentileLow(0.001);
        public int TotalFrames => _totalFrames;
        public double TotalSeconds => _totalFrameTime;

        public void OnPresent(long ticks)
        {
            _timestamps[_index] = ticks;
            _index = (_index + 1) % SampleCount;
            if (_count < SampleCount) _count++;

            if (_count >= 2)
            {
                var prev = _timestamps[(_index - 2 + SampleCount) % SampleCount];
                var frameTime = (double)(ticks - prev) / TimeSpan.TicksPerSecond;
                if (frameTime > 0 && frameTime < 10)
                {
                    _frameTimes.Add(frameTime);
                    if (_frameTimes.Count > 36000) _frameTimes.RemoveRange(0, _frameTimes.Count - 3600);

                    var instantFps = 1.0 / frameTime;
                    _fpsSum += instantFps;
                    _totalFrames++;
                    _totalFrameTime += frameTime;
                    if (instantFps < _minFps) _minFps = instantFps;
                    if (instantFps > _maxFps) _maxFps = instantFps;
                }

                var first = _timestamps[(_index - _count + SampleCount) % SampleCount];
                var last = _timestamps[(_index - 1 + SampleCount) % SampleCount];
                var duration = (double)(last - first) / TimeSpan.TicksPerSecond;
                if (duration > 0)
                    _lastFps = (_count - 1) / duration;
            }
        }

        private double CalcPercentileLow(double percentile)
        {
            if (_frameTimes.Count < 10) return 0;
            var sorted = _frameTimes.ToList();
            sorted.Sort();
            var idx = (int)Math.Ceiling(sorted.Count * percentile) - 1;
            if (idx < 0) idx = 0;
            var ft = sorted[idx];
            return ft > 0 ? 1.0 / ft : 0;
        }

        public FpsSnapshot TakeSnapshot(string processName)
        {
            return new FpsSnapshot
            {
                ProcessName = processName,
                CurrentFps = _lastFps,
                AvgFps = AvgFps,
                MinFps = MinFps,
                MaxFps = MaxFps,
                OnePercentLow = OnePercentLow,
                PointOnePercentLow = PointOnePercentLow,
                TotalFrames = _totalFrames,
                TotalSeconds = _totalFrameTime,
                FrameTimes = _frameTimes.ToList()
            };
        }

        public void Decay()
        {
            if (_count > 0) _count--;
            if (_count < 2) _lastFps = 0;
        }
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
        _lastAccess = DateTime.Now;
        if (_paused) return (0, "");
        EnsureRunning();

        if (_trackers.IsEmpty) return (0, "");

        int targetPid;

        if (_manualFocusPid != 0 && _trackers.ContainsKey(_manualFocusPid))
        {
            targetPid = _manualFocusPid;
        }
        else
        {
            targetPid = GetForegroundWindowPid();
            if (targetPid == 0 || !_trackers.ContainsKey(targetPid) || _trackers[targetPid].Fps <= 0)
            {
                targetPid = 0;
                foreach (var kv in _trackers)
                {
                    if (kv.Value.Fps > 0) { targetPid = kv.Key; break; }
                }
            }
        }

        if (targetPid != 0 && _trackers.TryGetValue(targetPid, out var tracker))
            return ((float)Math.Round(tracker.Fps), GetProcessName(targetPid));
        return (0, "");
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
        foreach (var kv in _trackers)
        {
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
        foreach (var kv in _trackers)
        {
            if (kv.Value.TotalFrames < 2) continue;
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
            sb.AppendLine($"    平均 FPS:   {snap.AvgFps:0}");
            sb.AppendLine($"    最低 FPS:   {snap.MinFps:0}");
            sb.AppendLine($"    最高 FPS:   {snap.MaxFps:0}");
            sb.AppendLine($"    1% Low:     {snap.OnePercentLow:0}");
            sb.AppendLine($"    0.1% Low:   {snap.PointOnePercentLow:0}");
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

    public void SetFocus(int pid) { _manualFocusPid = pid; }
    public void ClearFocus() { _manualFocusPid = 0; }

    private void EnsureRunning()
    {
        if (_running) return;
        Start();
    }

    private void Start()
    {
        if (_running) return;
        if (!IsAdmin()) return;

        try
        {
            StopExistingSession();

            _session = new TraceEventSession(SessionName);
            _session.EnableProvider(DxgKrnlProviderId);

            _running = true;
            _paused = false;
            _sessionStart = DateTime.Now;

            _processTask = Task.Factory.StartNew(() =>
            {
                try
                {
                    _session.Source.Dynamic.All += OnTraceEvent;
                    _session.Source.Process();
                }
                catch { }
            }, TaskCreationOptions.LongRunning);

            _decayTimer = new Timer(_ =>
            {
                if (_paused) return;
                if (_lastAccess > DateTime.MinValue && (DateTime.Now - _lastAccess).TotalSeconds > 5)
                {
                    Stop();
                    return;
                }

                var stalePids = new List<int>();
                foreach (var kv in _trackers)
                {
                    kv.Value.Decay();
                    if (kv.Value.Fps <= 0) stalePids.Add(kv.Key);
                }
                foreach (var pid in stalePids)
                    _trackers.TryRemove(pid, out var _);
            }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
        }
        catch { _running = false; }
    }

    private void OnTraceEvent(TraceEvent data)
    {
        try
        {
            if (_paused) return;
            if ((int)data.ID != PresentEventId) return;
            if (data.ProcessID <= 0) return;
            if (data.ProcessID == Environment.ProcessId) return;
            if (ExcludedPids.Contains(data.ProcessID)) return;

            var name = GetProcessName(data.ProcessID);
            if (Excluded.Contains(name)) return;

            var tracker = _trackers.GetOrAdd(data.ProcessID, _ => new FpsTracker());
            tracker.OnPresent(data.TimeStamp.Ticks);
        }
        catch { }
    }

    private string GetProcessName(int pid)
    {
        if (_nameCache.TryGetValue(pid, out var cached)) return cached;
        try
        {
            var name = Process.GetProcessById(pid).ProcessName;
            _nameCache.TryAdd(pid, name);
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
        Stop();
    }

    private void Stop()
    {
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