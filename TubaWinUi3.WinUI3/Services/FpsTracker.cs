namespace TubaWinUi3.Services;

using TubaWinUi3.Models;

/// <summary>
/// DxgKrnl / Win32k present 事件家族常量与「一帧只计一次」判据。
/// 主程序（TraceEvent 库消费）与 NativeAOT 后端（原生 ETW API 消费）共享：
/// 事件 ID、去重窗口、分层权威源判据任何一边改动，两边自动同步。
/// </summary>
internal static class FpsPresentEvents
{
    public static readonly Guid DxgKrnlProviderId = new("802EC45A-1E99-4B83-9920-87C98277BA9D");
    public static readonly Guid Win32kProviderId = new("8C416C79-D49B-4F01-A467-E56D3AA8234C");

    // DxgKrnl per-frame present event family (event IDs verified against PresentMon's
    // Microsoft_Windows_DxgKrnl ETW header). Older code listened to 0xB8 only, which
    // fires for fullscreen/kernel-attributed presents — 无边框窗口 (borderless windowed)
    // games are mostly tracked via PresentHistory / MPO events instead, so their
    // process never appeared in the tracker and the UI latched onto some idle ~1Hz
    // process. We now accept the whole family and dedupe per frame.
    public const int PresentEventId = 0x00B8;                // Present (kernel present end; fullscreen/legacy)
    public const int PresentHistoryStartId = 0x00AB;         // PresentHistory_Start (modern; all modes)
    public const int PresentHistoryDetailedStartId = 0x00D7; // PresentHistoryDetailed_Start (all modes)
    public const int BltEventId = 0x00A6;                    // Blt_Info (MPO blt path)
    public const int MmioFlipEventId = 0x0074;               // MMIOFlip_Info (MPO flip path)
    public const int MmioFlipMpoEventId = 0x0103;            // MMIOFlip_MPO
    public const int MmioFlipMpo3EventId = 0x0182;           // MMIOFlip_MPO3
    public const int FlipEventId = 0x00A8;                   // Flip_Info (hardware flip)
    public const int FlipMpoEventId = 0x00FC;                // FlipMultiPlaneOverlay_Info
    public const int IndependentFlipEventId = 0x010A;        // IndependentFlip_Info
    public const int Win32kPresentEventId = 0x00C9;          // Win32k TokenCompositionSurfaceObject (composited/windowed frames)

    // The kernel emits several events per presented frame (e.g. Present + QueuePacket,
    // PresentHistory + MMIOFlip). Treat all events within 1ms as the same frame.
    public const long SameFrameWindowTicks = TimeSpan.TicksPerMillisecond;
    // Win32k (0xC9) 一帧可能对应多个 composition surface（多窗口/UI 层），事件可相差
    // 数毫秒 —— 用更宽的同帧窗口合并；4ms 仍能保留 240Hz 以上的真实帧。
    public const long Win32kSameFrameWindowTicks = TimeSpan.TicksPerMillisecond * 4;
    // How long an observed per-frame event source stays "authoritative" for a process
    // before falling back to the next tier (see TryRecordPresent).
    public const long ModeWindowTicks = TimeSpan.TicksPerMillisecond * 500;

    public static bool IsPresentEventId(int id) =>
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
    public static bool TryRecordPresent(FpsTracker tracker, int id, long ticks)
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
}

/// <summary>ETW fallback using the same statistics-v2 one-second windows as PresentMon.</summary>
internal sealed class FpsTracker
{
    private readonly object _gate = new();
    private readonly Queue<(long Ticks, double Ms)> _frames = new();
    private readonly List<FrameScopeFrameStatistics.Sample> _samples = new();
    private long _origin, _boundary;
    private int _totalFrames;
    private double _totalMs;
    private double _lastFrameMs;
    private bool _started;
    public long LastPresentTicks;
    public DateTime LastPresentUtc = DateTime.MinValue;
    public long LastHistoryTicks;
    public long LastWin32kTicks;
    // No latency can be reconstructed reliably from these uncorrelated events.
    public double LastFrameTimeMs => _lastFrameMs;
    public int TotalFrames => _totalFrames;
    public double TotalSeconds => _totalMs / 1000;
    public double Fps => Live().Average ?? 0;
    public double OnePercentLow => Live().Low1 ?? -1;
    public double PointOnePercentLow => Live().Low01 ?? -1;
    public double AvgFps => TakeSnapshot("").AvgFps;
    public double MinFps => TakeSnapshot("").MinFps;
    public double MaxFps => TakeSnapshot("").MaxFps;

    private FrameScopeFrameStatistics.Sample Live()
    {
        lock (_gate)
            return DateTime.UtcNow - LastPresentUtc >= TimeSpan.FromMilliseconds(FrameScopeFrameStatistics.WindowMs)
                ? default : SampleAt(LastPresentTicks);
    }
    private FrameScopeFrameStatistics.Sample SampleAt(long ticks)
    {
        long start = ticks - TimeSpan.TicksPerSecond;
        return FrameScopeFrameStatistics.Snapshot(_frames.Where(f => f.Ticks > start && f.Ticks <= ticks).Select(f => f.Ms).ToList());
    }
    public void OnPresent(long ticks)
    {
        lock (_gate)
        {
            if (_started && ticks <= LastPresentTicks) return;
            long previous = LastPresentTicks;
            bool hadPrevious = _started;
            _started = true;
            LastPresentTicks = ticks;
            LastPresentUtc = DateTime.UtcNow;
            if (!hadPrevious) return;
            double ms = (ticks - previous) / (double)TimeSpan.TicksPerMillisecond;
            AddFrameCore(ticks, ms);
        }
    }
    internal void AddFrame(long ticks, double ms)
    {
        lock (_gate)
        {
            if (_started && ticks <= LastPresentTicks) return;
            _started = true;
            LastPresentTicks = ticks;
            LastPresentUtc = DateTime.UtcNow;
            AddFrameCore(ticks, ms);
        }
    }
    private void AddFrameCore(long ticks, double ms)
    {
        if (ms <= 0 || ms > FrameScopeFrameStatistics.MaxTimeMs) return;
        if (_totalFrames == 0) { _origin = ticks; _boundary = ticks + TimeSpan.TicksPerSecond; }
        // Replay's boundaries include a frame exactly on the boundary.
        while (_boundary < ticks) { AppendSample(_boundary); _boundary += TimeSpan.TicksPerSecond; }
        _frames.Enqueue((ticks, ms));
        _lastFrameMs = ms;
        _totalFrames++;
        _totalMs += ms;
        if (_boundary == ticks) { AppendSample(_boundary); _boundary += TimeSpan.TicksPerSecond; }
        while (_frames.Count > 0 && _frames.Peek().Ticks <= ticks - TimeSpan.TicksPerSecond) _frames.Dequeue();
    }

    private void AppendSample(long ticks)
    {
        var sample = SampleAt(ticks);
        sample.TimeMs = (ticks - _origin) / (double)TimeSpan.TicksPerMillisecond;
        _samples.Add(sample);
    }
    public FpsSnapshot TakeSnapshot(string processName)
    {
        lock (_gate)
        {
            var samples = new List<FrameScopeFrameStatistics.Sample>(_samples);
            if (_totalFrames > 0 && (_samples.Count == 0 || _boundary - TimeSpan.TicksPerSecond != LastPresentTicks))
                samples.Add(SampleAt(LastPresentTicks));
            var summary = FrameScopeFrameStatistics.Summarize(samples);
            var retained = samples.Where((s, i) => summary.Included[i]).Where(s => s.Average.HasValue).ToList();
            return new FpsSnapshot {
                ProcessName = processName, CurrentFps = Fps,
                AvgFps = summary.Filtered.Average ?? 0,
                MinFps = retained.Count > 0 ? retained.Min(s => s.Average!.Value) : 0,
                MaxFps = retained.Count > 0 ? retained.Max(s => s.Average!.Value) : 0,
                OnePercentLow = summary.Filtered.Low1 ?? -1,
                PointOnePercentLow = summary.Filtered.Low01 ?? -1,
                TotalFrames = _totalFrames, TotalSeconds = TotalSeconds,
                FrameTimes = _frames.Select(f => f.Ms / 1000).ToList()
            };
        }
    }
    public void Decay(DateTime nowUtc) { /* Readouts expire against wall time in Live(). */ }
}
