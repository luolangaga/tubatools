using System.Diagnostics;
using System.Globalization;

namespace TubaWinUi3.Services;

internal sealed class PresentMonFrames
{
    internal readonly record struct Metrics(float Fps, float Low1, float Low01, float FrameTimeMs, float LatencyMs);
    private readonly record struct Frame(long Ticks, double FrameTimeMs, float LatencyMs, bool Hardware);
    private sealed class Track
    {
        internal readonly Queue<Frame> Frames = new();
        internal long LatestTicks;
        internal readonly FpsTracker AllReport = new();
        internal readonly FpsTracker HardwareReport = new();
        internal readonly SortedDictionary<double, int> AllTimes = new();
        internal readonly SortedDictionary<double, int> HardwareTimes = new();
        internal void Record(long ticks, double ms, bool hardware)
        {
            long timeSpanTicks = (long)(ticks * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);
            AllReport.AddFrame(timeSpanTicks, ms);
            AllTimes[ms] = AllTimes.GetValueOrDefault(ms) + 1;
            if (hardware) {
                HardwareReport.AddFrame(timeSpanTicks, ms);
                HardwareTimes[ms] = HardwareTimes.GetValueOrDefault(ms) + 1;
            }
        }
        internal double Score()
        {
            var values = HardwareTimes.Count > 0 ? HardwareTimes : AllTimes;
            int count = HardwareTimes.Count > 0 ? HardwareReport.TotalFrames : AllReport.TotalFrames;
            double Rank(double fraction) {
                int seen = 0, rank = Math.Max(1, (int)Math.Ceiling(count * fraction));
                foreach (var pair in values) { seen += pair.Value; if (seen >= rank) return pair.Key; }
                return 0;
            }
            return HardwareReport.TotalFrames * 3.0 + AllReport.TotalFrames
                + Math.Min(240, 1000 / Rank(.5)) * 20 - Math.Min(1000, Rank(.99)) * 2;
        }
    }
    private static readonly long WindowTicks = (long)(Stopwatch.Frequency * FrameScopeFrameStatistics.WindowMs / 1000);
    private readonly Dictionary<(int Pid, string SwapChain), Track> _tracks = new();
    private readonly object _gate = new();
    private int _columns, _pid, _swapChain, _timestamp, _frameTime, _latency, _mode;
    private long _lastPrune;

    internal bool ReadHeader(string line)
    {
        var fields = line.TrimStart('\uFEFF').Split(',');
        _columns = fields.Length;
        int Column(string name) => Array.FindIndex(fields, field => field.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
        _pid = Column("ProcessID");
        _swapChain = Column("SwapChainAddress");
        _timestamp = Column("QPCTime");
        if (_timestamp < 0) _timestamp = Column("TimeInQPC");
        _frameTime = Column("MsBetweenPresents");
        // Render completion and display latency are different PresentMon metrics.
        _latency = Column("MsUntilRenderComplete");
        _mode = Column("PresentMode");
        return fields[0].Equals("Application", StringComparison.OrdinalIgnoreCase) &&
               _pid > 0 && _swapChain > 0 && _timestamp > 0 && _frameTime > 0;
    }

    internal void Add(string line, long now)
    {
        var fields = line.Split(',');
        // PresentMon emits the executable name without CSV quoting. A comma in the
        // name adds fields before ProcessID; the numeric columns still align from the end.
        int offset = fields.Length - _columns;
        if (offset < 0 || !int.TryParse(fields[_pid + offset], out int pid) || pid <= 4 ||
            pid == Environment.ProcessId ||
            !long.TryParse(fields[_timestamp + offset], out long ticks) || ticks <= 0 ||
            ticks > now || now - ticks >= WindowTicks ||
            !double.TryParse(fields[_frameTime + offset], NumberStyles.Float, CultureInfo.InvariantCulture, out double frameMs) ||
            !double.IsFinite(frameMs) || frameMs <= 0 || frameMs > FrameScopeFrameStatistics.MaxTimeMs)
            return;

        float latency = -1;
        if (_latency >= 0 && float.TryParse(fields[_latency + offset], NumberStyles.Float, CultureInfo.InvariantCulture, out float renderMs) &&
            float.IsFinite(renderMs) && renderMs > 0)
            latency = renderMs;
        var key = (pid, fields[_swapChain + offset]);
        bool hardware = _mode >= 0 && fields[_mode + offset].Trim().StartsWith("Hardware:", StringComparison.OrdinalIgnoreCase);
        lock (_gate)
        {
            if (now - _lastPrune >= WindowTicks)
            {
                foreach (var stale in _tracks.Where(pair => now - pair.Value.LatestTicks >= WindowTicks * 300).ToArray())
                    _tracks.Remove(stale.Key);
                _lastPrune = now;
            }
            if (!_tracks.TryGetValue(key, out var track))
            {
                if (_tracks.Count >= 256) return;
                _tracks.Add(key, track = new Track());
            }
            if (track.LatestTicks >= ticks) return;
            track.LatestTicks = ticks;
            track.Record(ticks, frameMs, hardware);
            var frames = track.Frames;
            frames.Enqueue(new Frame(ticks, frameMs, latency, hardware));
            while (ticks - frames.Peek().Ticks >= WindowTicks)
                frames.Dequeue();
        }
    }

    internal List<(int Pid, float Fps)> GetProcesses(long now)
    {
        lock (_gate)
        {
            return _tracks.Keys.Select(key => key.Pid).Distinct()
                .Select(pid => (Pid: pid, Fps: GetMetrics(pid, now)?.Fps ?? 0))
                .Where(item => item.Fps > 0).ToList();
        }
    }

    internal List<(int Pid, TubaWinUi3.Models.FpsSnapshot Snapshot)> GetSnapshots()
    {
        lock (_gate)
        {
            return _tracks.GroupBy(pair => pair.Key.Pid).Select(group => {
                var tracks = group.OrderByDescending(pair => pair.Value.Score())
                    .ThenBy(pair => pair.Key.SwapChain, StringComparer.Ordinal).ToArray();
                var track = tracks[0].Value;
                var reporter = tracks.Length > 1 && track.HardwareReport.TotalFrames > 0
                    ? track.HardwareReport : track.AllReport;
                return (group.Key, reporter.TakeSnapshot(""));
            }).ToList();
        }
    }

    internal Metrics? GetMetrics(int pid, long now)
    {
        lock (_gate)
        {
            var tracks = _tracks.Where(pair => pair.Key.Pid == pid && now - pair.Value.LatestTicks < WindowTicks)
                .OrderBy(pair => pair.Key.SwapChain, StringComparer.Ordinal).ToArray();
            if (tracks.Length == 0) return null;

            long newest = tracks.Max(pair => pair.Value.LatestTicks);
            List<double>? selected = null;
            Frame latest = default;
            float latency = -1;
            double bestScore = double.NegativeInfinity;
            foreach (var (_, track) in tracks)
            {
                var frames = track.Frames.Where(frame => newest - frame.Ticks < WindowTicks).ToArray();
                if (frames.Length == 0) continue;

                var hardware = frames.Where(frame => frame.Hardware).Select(frame => frame.FrameTimeMs).ToList();
                var all = frames.Select(frame => frame.FrameTimeMs).ToList();
                var scoring = hardware.Count > 0 ? hardware : all;
                scoring.Sort();
                double score = FrameScopeFrameStatistics.TrackScore(hardware.Count, all.Count, scoring);
                if (score <= bestScore) continue;

                bestScore = score;
                selected = tracks.Length > 1 && hardware.Count > 0 ? hardware : all;
                var selectedFrames = tracks.Length > 1 && hardware.Count > 0
                    ? frames.Where(frame => frame.Hardware).ToArray() : frames;
                latest = selectedFrames[^1];
                latency = -1;
                for (int index = selectedFrames.Length - 1; index >= 0; index--)
                {
                    var frame = selectedFrames[index];
                    if (now - frame.Ticks >= WindowTicks) break;
                    if (frame.LatencyMs < 0) continue;
                    latency = frame.LatencyMs;
                    break;
                }
            }
            if (selected is null) return null;

            var stats = FrameScopeFrameStatistics.Snapshot(selected);
            return new Metrics((float)stats.Average!.Value, (float)stats.Low1!.Value, (float)stats.Low01!.Value, (float)Math.Round(latest.FrameTimeMs, 1),
                latency >= 0 ? (float)Math.Round(latency, 1) : -1);
        }
    }
}
