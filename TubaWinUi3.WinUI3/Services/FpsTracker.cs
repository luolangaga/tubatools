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

/// <summary>
/// 单进程帧追踪器（PresentMon 口径）：由 ETW present 事件驱动（OnPresent），
/// 维护瞬时 FPS、会话级 Avg/Min/Max 与滚动窗口口径的 1% low / 0.1% low。
/// 纯逻辑、无 UI / ETW 依赖 —— 主程序 FpsService 与后台后端（TubaWinUI3.BackEnd，
/// 经 csproj Compile 链接）共享同一份实现：改这里等于同时改两端，口径永不漂移。
/// </summary>
internal sealed class FpsTracker
{
    private const int SampleCount = 60;
    private readonly long[] _timestamps = new long[SampleCount];
    private int _index;
    private int _count;
    private double _lastFps;
    private double _lastFrameTimeMs;

    // 帧时间环形缓冲（存绝对时间戳 + 有效帧间隔，秒）。容量只是**上限**，真正的过期
    // 口径是「绝对时间」（CollectWindow 按 ticks 剪枝）而不是「帧数」。容量必须 ≥
    // 最长统计窗口（0.1% low = 30s）在最高刷新率下的帧数，否则缓冲会先于时间窗口截断：
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

    private readonly (long Ticks, double FrameTime)[] _frameWindow = new (long Ticks, double FrameTime)[FrameWindowCapacity];
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
                _frameWindow[_windowIndex] = (ticks, frameTime);
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
    /// 每帧存了**绝对时间戳**（QPC ticks），按墙钟剪枝：从最新帧往回收集，
    /// 一遇到过期帧就停 —— 停帧期间（暂停/切出/加载）没有新帧，旧帧赖在环形缓冲里
    /// 不会被挤掉，靠「帧间隔累加」近似时间的旧实现要等新帧把旧帧一根根挤出去
    /// （30s 窗口 @60fps 要挤 30 秒），读数滞后实际画面「慢半拍」；
    /// 绝对时间剪枝下切回来的第一帧就把过期旧数据全部丢弃，读数立即反映当前画面。
    /// <paramref name="filled"/> = 窗口是否被时间**填满**（窗口内最早帧与最新帧的
    /// 跨度 ≥ windowSeconds）：刚开测的头几秒缓冲里只有半截数据，调用方（实时读数）
    /// 用这个标记把半截窗口的读数屏蔽成 "--"。
    /// </summary>
    private List<double> CollectWindow(double windowSeconds, out bool filled)
    {
        var list = new List<double>(Math.Min(_windowCount, 8192));
        filled = false;
        if (_windowCount == 0) return list;

        long windowTicks = (long)(windowSeconds * TimeSpan.TicksPerSecond);
        var newestTick = _frameWindow[(_windowIndex - 1 + FrameWindowCapacity) % FrameWindowCapacity].Ticks;
        long minTick = newestTick - windowTicks;

        for (int k = 0; k < _windowCount; k++)
        {
            var entry = _frameWindow[(_windowIndex - 1 - k + FrameWindowCapacity * 2) % FrameWindowCapacity];
            if (entry.Ticks < minTick) break; // 从最新往回遍历，一遇到过期就全部过期
            list.Add(entry.FrameTime);
        }

        if (list.Count > 0)
        {
            // 窗口内最早帧的时间戳（list.Count 个 = 从最新数第 list.Count 个）
            var oldestTick = _frameWindow[(_windowIndex - list.Count + FrameWindowCapacity * 2) % FrameWindowCapacity].Ticks;
            filled = newestTick - oldestTick >= windowTicks;
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

