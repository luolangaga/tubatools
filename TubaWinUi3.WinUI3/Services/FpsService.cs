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

        // 帧时间环形缓冲（存的是有效帧间隔，秒）。容量只是**上限**，真正的过期口径是
        // 「时间」而不是「帧数」。容量必须 ≥ 最长统计窗口（0.1% low = 30s）在最高刷新率下
        // 的帧数，否则缓冲会先于时间窗口截断，「窗口已填满」的判定永远不成立：
        // 16384 帧 @300fps ≈ 54.6s、@240fps ≈ 68s、@144fps ≈ 114s、@60fps ≈ 273s。
        private const int FrameWindowCapacity = 16384;

        // 1% low / 0.1% low 的统计窗口（秒）。为什么分开、为什么是这两个数：
        //   1% low 要「反应快」→ 短窗口：一次卡顿几秒内进来，十几秒内滚出去；
        //   0.1% low 要「有统计意义」→ 长窗口：0.1% 至少要上千帧才成立，
        //   600 帧的「0.1%」其实只是「最慢的那 1 帧」。
        // 旧实现是**共用一个固定 2048 帧的窗口**，60fps 下 ≈34 秒 —— 一次卡顿要在读数里
        // 泡满 34 秒才滚干净，这就是「刷新特别慢」的根源；而且帧数窗口在 30fps 下变成 68 秒、
        // 144fps 下只有 14 秒，同一段画面在高低帧率下口径完全不同。
        private const double Low1WindowSeconds = 10.0;
        private const double Low01WindowSeconds = 30.0;

        // 窗口内最少帧数，不够就返回 -1（上层显示 "--"）。用「窗口内帧数」而不是
        // 「会话累计帧数」做门槛，门槛与统计口径才一致。
        private const int Low1MinFrames = 100;
        private const int Low01MinFrames = 900;

        private readonly double[] _frameWindow = new double[FrameWindowCapacity];
        private int _windowIndex;
        private int _windowCount;
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
        // 平均 FPS = 总帧数 / 总帧时间（对瞬时 FPS 求均值会被假帧抬高，口径不稳）。
        // Avg/Min/Max 是会话级累计口径（报告用）；1% low / 0.1% low 是滚动窗口口径（覆盖层用）。
        public double AvgFps => _totalFrames > 0 && _totalFrameTime > 0 ? _totalFrames / _totalFrameTime : 0;
        public double MinFps => _minFps == double.MaxValue ? 0 : _minFps;
        public double MaxFps => _maxFps;
        public double OnePercentLow => CalcPercentileLow(0.01, Low1WindowSeconds, Low1MinFrames, requireFullWindow: true);
        public double PointOnePercentLow => CalcPercentileLow(0.001, Low01WindowSeconds, Low01MinFrames, requireFullWindow: true);
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
                    _frameWindow[_windowIndex] = frameTime;
                    _windowIndex = (_windowIndex + 1) % FrameWindowCapacity;
                    if (_windowCount < FrameWindowCapacity) _windowCount++;

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
        /// 取最近 <paramref name="windowSeconds"/> 秒内的帧时间（最新在前）。
        /// 逐帧往前累加帧间隔，累计值 = 这段时间的帧时间之和 ≈ 墙钟时长，
        /// 所以不需要额外存每帧的时间戳。帧间隔 ≥10s 的断档在写入时已被丢弃，
        /// 长时间切出去再回来不会把「一大坨空档」算进窗口。
        /// <paramref name="filled"/> = 窗口是否被时间**填满**：刚开测的头几秒缓冲里
        /// 只有半截数据，调用方（实时读数）用这个标记把半截窗口的读数屏蔽成 "--"。
        /// </summary>
        private List<double> CollectWindow(double windowSeconds, out bool filled)
        {
            var list = new List<double>(Math.Min(_windowCount, 8192));
            double elapsed = 0;
            filled = false;
            for (int k = 0; k < _windowCount; k++)
            {
                var v = _frameWindow[(_windowIndex - 1 - k + FrameWindowCapacity * 2) % FrameWindowCapacity];
                list.Add(v);
                elapsed += v;
                if (elapsed >= windowSeconds) { filled = true; break; }
            }
            return list;
        }

        /// <summary>
        /// 1% low / 0.1% low：取滚动窗口里最慢 `percentile` 帧的「平均帧时间」再换算
        /// FPS（1 / 平均帧时间）。这是 PresentMon / CapFrameX 的标准口径 —— 对最差帧的
        /// 瞬时 FPS 直接取平均会因 1/x 的凸性系统性高估。
        /// 窗口按**时间**过期（见 <see cref="Low1WindowSeconds"/>），启动/加载/菜单的旧帧
        /// 会自然滚出，读数反映当前画面而不是整个会话。
        /// <paramref name="requireFullWindow"/>：实时读数（覆盖层/监控页/记录采样）必须等
        /// 时间窗口被**填满**才出数 —— 刚开测的头几秒窗口只有半截，启动期（着色器编译、
        /// 垂直同步爬坡、加载关卡）的坏帧会把最差百分位放大成离谱读数，这段时间显示 "--"。
        /// 帧数不足 <paramref name="minFrames"/> 时同样返回 -1（上层显示 "--"）。
        /// </summary>
        private double CalcPercentileLow(double percentile, double windowSeconds, int minFrames, bool requireFullWindow)
        {
            var window = CollectWindow(windowSeconds, out var filled);
            if (window.Count < minFrames) return -1;
            if (requireFullWindow && !filled) return -1;

            window.Sort(); // 升序：最快的帧在前，最慢的帧在尾部
            int n = window.Count;

            // 真百分位帧数。这里刻意**不设**「最少 3 帧」之类的下限：那会把口径悄悄放大
            // （n=100 时 1% 实际变成 3%，n=1000 时 0.1% 变成 0.3%），读数还会随着窗口
            // 填充进度漂移 —— 同一段画面在第 10 秒和第 30 秒算出来的不是同一个指标。
            // 样本不足的问题交给 minFrames 门槛和窗口时长解决。
            int worst = (int)Math.Ceiling(n * percentile);
            if (worst < 1) worst = 1;
            if (worst > n) worst = n;

            double sum = 0;
            for (int i = n - worst; i < n; i++)
                sum += window[i];
            double avgFrameTime = sum / worst;
            return avgFrameTime > 0 ? 1.0 / avgFrameTime : -1;
        }

        public FpsSnapshot TakeSnapshot(string processName)
        {
            // 快照/报告同样用滚动窗口 —— 会话期 2 小时后再读报告，帧时间表不该
            // 还泡着启动画面和加载关卡的数据。窗口口径与 0.1% low 对齐（30 秒）。
            var windowTimes = CollectWindow(Low01WindowSeconds, out _);
            windowTimes.Reverse(); // CollectWindow 返回「最新在前」，快照按时间顺序输出

            // 报告是「整段会话」语义：就算会话比窗口短（短时压测），也按已有帧算——
            // CapFrameX 对整段录制就是这么算的。所以这里不设 requireFullWindow，
            // 短会话的报告不会因为滚动窗口没填满就开天窗。
            return new FpsSnapshot
            {
                ProcessName = processName,
                CurrentFps = _lastFps,
                AvgFps = AvgFps,
                MinFps = MinFps,
                MaxFps = MaxFps,
                OnePercentLow = CalcPercentileLow(0.01, Low1WindowSeconds, Low1MinFrames, requireFullWindow: false),
                PointOnePercentLow = CalcPercentileLow(0.001, Low01WindowSeconds, Low01MinFrames, requireFullWindow: false),
                TotalFrames = _totalFrames,
                TotalSeconds = _totalFrameTime,
                FrameTimes = windowTimes
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
            // 样本充足性由 tracker 内部按「窗口内帧数」判定（1% low ≥100 帧 / 0.1% low ≥900 帧），
            // 不足或算不出来时返回 -1 → 覆盖层显示 "--"。这里不再按会话累计帧数二次拦，
            // 否则门槛（累计）和口径（窗口）会对不上。
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