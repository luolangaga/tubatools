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
    private static readonly Guid DxgKrnlProviderId = new("802EC45A-1E99-4B83-9920-87C98277BA9D");
    private static readonly Guid Win32kProviderId = new("8C416C79-D49B-4F01-A467-E56D3AA8234C");

    // DxgKrnl per-frame present event family (event IDs verified against PresentMon's
    // Microsoft_Windows_DxgKrnl ETW header). Older code listened to 0xB8 only, which
    // fires for fullscreen/kernel-attributed presents — 无边框窗口 (borderless windowed)
    // games are mostly tracked via PresentHistory / MPO events instead, so their
    // process never appeared in the tracker and the UI latched onto some idle ~1Hz
    // process. We now accept the whole family and dedupe per frame.
    private const int PresentEventId = 0x00B8;                // Present (kernel present end; fullscreen/legacy)
    private const int PresentHistoryStartId = 0x00AB;         // PresentHistory_Start (modern; all modes)
    private const int PresentHistoryDetailedStartId = 0x00D7; // PresentHistoryDetailed_Start (all modes)
    private const int BltEventId = 0x00A6;                    // Blt_Info (MPO blt path)
    private const int MmioFlipEventId = 0x0074;               // MMIOFlip_Info (MPO flip path)
    private const int MmioFlipMpoEventId = 0x0103;            // MMIOFlip_MPO
    private const int MmioFlipMpo3EventId = 0x0182;           // MMIOFlip_MPO3
    private const int FlipEventId = 0x00A8;                   // Flip_Info (hardware flip)
    private const int FlipMpoEventId = 0x00FC;                // FlipMultiPlaneOverlay_Info
    private const int IndependentFlipEventId = 0x010A;        // IndependentFlip_Info
    private const int Win32kPresentEventId = 0x00C9;          // Win32k TokenCompositionSurfaceObject (composited/windowed frames)

    // The kernel emits several events per presented frame (e.g. Present + QueuePacket,
    // PresentHistory + MMIOFlip). Treat all events within 1ms as the same frame.
    private const long SameFrameWindowTicks = TimeSpan.TicksPerMillisecond;
    // Win32k (0xC9) 一帧可能对应多个 composition surface（多窗口/UI 层），事件可相差
    // 数毫秒 —— 用更宽的同帧窗口合并；4ms 仍能保留 240Hz 以上的真实帧。
    private const long Win32kSameFrameWindowTicks = TimeSpan.TicksPerMillisecond * 4;
    // How long an observed per-frame event source stays "authoritative" for a process
    // before falling back to the next tier (see TryRecordPresent).
    private const long ModeWindowTicks = TimeSpan.TicksPerMillisecond * 500;

    private readonly ConcurrentDictionary<int, FpsTracker> _trackers = new();
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
    internal static bool IsPresentEventId(int id) =>
        id is PresentEventId or PresentHistoryStartId or PresentHistoryDetailedStartId
            or BltEventId or MmioFlipEventId or MmioFlipMpoEventId or MmioFlipMpo3EventId
            or FlipEventId or FlipMpoEventId or IndependentFlipEventId or Win32kPresentEventId;

    /// <summary>
    /// Records a present event for a process. Returns false when the event is a
    /// duplicate (same frame already counted) or shadowed by a higher-priority
    /// event source, so each presented frame is counted exactly once.
    ///
    /// Per-process event source priority (based on what fired within the last
    /// 500ms): PresentHistory (0xAB/0xD7) ＞ Win32k composed presents (0xC9) ＞
    /// legacy kernel present/MPO events (0xB8/0xA6/0x74/…). The lower tiers are
    /// only fallbacks for systems/modes that don't emit the higher-tier events
    /// (e.g. MPO disabled + no present history → Win32k is the per-frame signal;
    /// fullscreen exclusive → 0xB8).
    /// </summary>
    internal static bool TryRecordPresent(FpsTracker tracker, int id, long ticks)
    {
        if (id is PresentHistoryStartId or PresentHistoryDetailedStartId)
            tracker.LastHistoryTicks = ticks;
        else if (id == Win32kPresentEventId)
            tracker.LastWin32kTicks = ticks;

        // Same-frame dedup: several kernel events fire per presented frame
        // (e.g. 0xA6+0x74, 0xAB+0xD7) microseconds apart.
        long dupWindow = id == Win32kPresentEventId ? Win32kSameFrameWindowTicks : SameFrameWindowTicks;
        if (ticks - tracker.LastPresentTicks < dupWindow) return false;

        // Shadow lower tiers while the authoritative source is flowing.
        if (id is not (PresentHistoryStartId or PresentHistoryDetailedStartId) &&
            ticks - tracker.LastHistoryTicks < ModeWindowTicks) return false;
        if (id is (PresentEventId or BltEventId or MmioFlipEventId or MmioFlipMpoEventId
                or MmioFlipMpo3EventId or FlipEventId or FlipMpoEventId or IndependentFlipEventId) &&
            ticks - tracker.LastWin32kTicks < ModeWindowTicks) return false;

        tracker.OnPresent(ticks);
        return true;
    }

    /// <summary>
    /// 读出帧生成时间与「提交→合成」渲染延迟；-1 = 无有效读数（覆盖层显示 "--"）。
    /// 帧时间随 FPS 的过期口径（2s 无新帧）；延迟样本只认 3s 内的刚配对数据。
    /// </summary>
    internal static void ReadFrameMetrics(FpsTracker tracker, DateTime nowUtc,
        out float frameTimeMs, out float renderLatencyMs)
    {
        frameTimeMs = renderLatencyMs = -1;
        if (tracker.LastFrameTimeMs > 0 &&
            tracker.LastPresentUtc != DateTime.MinValue &&
            nowUtc - tracker.LastPresentUtc <= TimeSpan.FromSeconds(2))
            frameTimeMs = (float)Math.Round(tracker.LastFrameTimeMs, 1);
        if (tracker.LastRenderLatencyMs >= 0 &&
            tracker.LastLatencyUtc != DateTime.MinValue &&
            nowUtc - tracker.LastLatencyUtc <= TimeSpan.FromSeconds(3))
            renderLatencyMs = (float)Math.Round(tracker.LastRenderLatencyMs, 1);
    }

    internal sealed class FpsTracker
    {
        private const int SampleCount = 60;
        private readonly long[] _timestamps = new long[SampleCount];
        private int _index;
        private int _count;
        private double _lastFps;
        private double _lastFrameTimeMs;
        private readonly List<double> _frameTimes = new(3600);
        private double _totalFrameTime;
        private int _totalFrames;
        private double _minFps = double.MaxValue;
        private double _maxFps;
        private double _fpsSum;
        // 「提交→合成」延迟配对：帧提交信号入队，等待 Win32k 合成事件(0xC9) FIFO 配对。
        // 全屏独占 / MPO 直通不产生合成事件 → 队列有上限，防止无限增长。
        private const int MaxPendingSubmits = 32;
        private const double MaxPairLatencyMs = 1000;
        private readonly Queue<long> _pendingSubmits = new();

        /// <summary>QPC timestamp (ticks) of the latest present event.</summary>
        public long LastPresentTicks;
        /// <summary>Wall clock of the latest present event (for stale detection).</summary>
        public DateTime LastPresentUtc = DateTime.MinValue;
        /// <summary>Latest PresentHistory event tick (authoritative source, tier 1).</summary>
        public long LastHistoryTicks;
        /// <summary>Latest Win32k composed-present event tick (tier 2).</summary>
        public long LastWin32kTicks;

        public double Fps => _lastFps;
        // 平均 FPS = 总帧数 / 总帧时间（对瞬时 FPS 求均值会被假帧抬高，口径不稳）
        public double AvgFps => _totalFrames > 0 && _totalFrameTime > 0 ? _totalFrames / _totalFrameTime : 0;
        public double MinFps => _minFps == double.MaxValue ? 0 : _minFps;
        public double MaxFps => _maxFps;
        public double OnePercentLow => CalcPercentileLow(0.01);
        public double PointOnePercentLow => CalcPercentileLow(0.001);
        public int TotalFrames => _totalFrames;
        public double TotalSeconds => _totalFrameTime;
        /// <summary>最近一次有效帧间隔（毫秒，帧生成时间）；0 = 尚无有效样本。</summary>
        public double LastFrameTimeMs => _lastFrameTimeMs;
        /// <summary>最近一次「提交→合成」延迟样本（毫秒）；-1 = 无样本（无合成事件或配对被丢弃）。</summary>
        public double LastRenderLatencyMs = -1;
        /// <summary>最近一次延迟样本的墙钟时间（readout 新鲜度判定用）。</summary>
        public DateTime LastLatencyUtc = DateTime.MinValue;

        public void OnPresent(long ticks)
        {
            LastPresentTicks = ticks;
            LastPresentUtc = DateTime.UtcNow;

            _timestamps[_index] = ticks;
            _index = (_index + 1) % SampleCount;
            if (_count < SampleCount) _count++;

            if (_count >= 2)
            {
                var prev = _timestamps[(_index - 2 + SampleCount) % SampleCount];
                var frameTime = (double)(ticks - prev) / TimeSpan.TicksPerSecond;
                // 帧时间下限 1ms（FPS ≤ 1000）：双源/多 surface 的重复事件会产生
                // 0.1ms 的假帧，混进统计会让 Avg/Max/1%low 全部失真。
                if (frameTime >= 0.001 && frameTime < 10)
                {
                    _lastFrameTimeMs = frameTime * 1000.0;
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

        /// <summary>
        /// 帧提交信号入队，等待同帧的 Win32k 合成事件(0xC9)配对。只在提交信号是
        /// 非合成事件时调用（合成事件本身就是提交信号的模式下没有独立的提交时刻，
        /// 延迟无定义，不入队）。队列上限 32，溢出丢最旧 —— 全屏独占/MPO 直通
        /// 不产生合成事件，没有上限会无限增长。
        /// </summary>
        public void EnqueueSubmit(long ticks)
        {
            _pendingSubmits.Enqueue(ticks);
            if (_pendingSubmits.Count > MaxPendingSubmits) _pendingSubmits.Dequeue();
        }

        /// <summary>
        /// Win32k TokenCompositionSurfaceObject（DWM 合成该帧，时间戳≈帧上屏时刻）
        /// → 与最旧的待配对提交按 FIFO 配对，Δ 在 [0, 1000ms] 内记为
        /// 「提交→合成」渲染延迟样本。无待配对提交（桌面闪烁等）或 Δ 超窗
        /// （切出/停顿后的陈旧配对）直接丢弃。
        /// </summary>
        public void TryRecordComposed(long ticks)
        {
            if (_pendingSubmits.Count == 0) return;
            var submit = _pendingSubmits.Dequeue();
            var deltaMs = (double)(ticks - submit) / TimeSpan.TicksPerMillisecond;
            if (deltaMs >= 0 && deltaMs <= MaxPairLatencyMs)
            {
                LastRenderLatencyMs = deltaMs;
                LastLatencyUtc = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// 1% low / 0.1% low：取最慢 `percentile` 帧的「平均帧时间」再换算 FPS
        /// （1 / 平均帧时间）。这是 PresentMon/CapFrameX 的标准口径 —— 对最差帧的
        /// 瞬时 FPS 取平均（旧实现）会因 1/x 的凸性系统性高估，且帧时间分布越散
        /// 读数越乱。
        /// 样本不足时返回 -1（上层显示 "--"），不与 0 混淆。
        /// </summary>
        private double CalcPercentileLow(double percentile)
        {
            if (_frameTimes.Count < 100) return -1;
            var sorted = _frameTimes.ToList();
            sorted.Sort(); // ascending: smallest (fastest) frame times first

            int n = sorted.Count;
            // 最差 `percentile` 帧数；太少时退化为最少 3 帧，保证读数稳定
            int worst = Math.Max(3, (int)Math.Ceiling(n * percentile));
            if (worst > n) worst = n;

            double sum = 0;
            for (int i = n - worst; i < n; i++)
                sum += sorted[i];
            double avgFrameTime = sum / worst;
            return avgFrameTime > 0 ? 1.0 / avgFrameTime : -1;
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

        public void Decay(DateTime nowUtc)
        {
            if (_count > 0) _count--;
            // Zero the readout as soon as presents stop (menus, loading, dead session).
            // Without this the stale (count-1)/duration value could linger for minutes.
            if (_count < 2 || (LastPresentUtc != DateTime.MinValue && (nowUtc - LastPresentUtc).TotalSeconds > 2))
                _lastFps = 0;
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
        var stats = GetFpsStats();
        return (stats.fps, stats.process);
    }

    /// <summary>
    /// Returns FPS plus 1% low and 0.1% low for the target (focused) process.
    /// </summary>
    public (float fps, string process, float low1, float low01, float frameTimeMs, float renderLatencyMs) GetFpsStats()
    {
        if (_paused) return (0, "", 0, 0, -1, -1);
        EnsureRunning();

        if (_trackers.IsEmpty) return (0, "", -1, -1, -1, -1);

        int targetPid;

        if (_manualFocusPid != 0 && _trackers.ContainsKey(_manualFocusPid))
        {
            targetPid = _manualFocusPid;
        }
        else
        {
            // Only report the foreground process. There is deliberately NO fallback
            // to "any process with FPS > 0" anymore: desktop processes that present
            // once a second (caret blink, widgets, …) used to win that race and the
            // overlay got stuck showing "1 FPS" while the actual game was untracked.
            targetPid = GetForegroundWindowPid();
            if (targetPid == 0 || !_trackers.ContainsKey(targetPid) || _trackers[targetPid].Fps <= 0)
                return (0, "", -1, -1, -1, -1);
        }

        if (targetPid != 0 && _trackers.TryGetValue(targetPid, out var tracker))
        {
            float low1 = -1, low01 = -1;
            // 1% low 至少 100 帧、0.1% low 至少 1000 帧才有统计意义（否则取到的是
            // 单帧噪声，读数乱跳）。样本不足/计算无效时返回 -1 → 覆盖层显示 "--"。
            if (tracker.TotalFrames >= 100)
            {
                var v = tracker.OnePercentLow;
                if (v > 0) { low1 = (float)Math.Round(v); if (low1 < 1) low1 = -1; }
            }
            if (tracker.TotalFrames >= 1000)
            {
                var v = tracker.PointOnePercentLow;
                if (v > 0) { low01 = (float)Math.Round(v); if (low01 < 1) low01 = -1; }
            }
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
                }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
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
            // 提交信号入队（Win32k 合成信号除外 —— 它同时是合成时刻，配对会得到
            // 帧间隔而非延迟）；0xC9 无条件尝试与最旧的提交配对。
            if (TryRecordPresent(tracker, id, data.TimeStamp.Ticks) && id != Win32kPresentEventId)
                tracker.EnqueueSubmit(data.TimeStamp.Ticks);
            if (id == Win32kPresentEventId)
                tracker.TryRecordComposed(data.TimeStamp.Ticks);
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