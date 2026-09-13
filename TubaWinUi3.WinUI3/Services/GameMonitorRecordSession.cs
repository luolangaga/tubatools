using System.Diagnostics;
using System.Text;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>一次成功落盘的记录结果（供结果弹窗 / 记录查看窗口使用）。</summary>
public sealed record RecordResult(
    List<string> Paths,
    MonitorRecordMeta Meta,
    List<MonitorRecordSample> Samples,
    double HeadTrim,
    double TailTrim);

/// <summary>
/// 独立于页面的游戏监控记录会话：
/// 采样只追加内存 List（绝不边采边写），停止时一次性写 JSON / Markdown / CSV。
/// 与 GameOverlayPage 内置记录同口径（同一套 GameMonitorRecorder 纯函数），
/// 供后台自动覆盖层（GameOverlayAutoService）复用 —— 检测到游戏自动记录，
/// 游戏退出后自动落盘并打开记录查看窗口。
/// </summary>
public sealed class GameMonitorRecordSession
{    private readonly List<MonitorRecordMetric> _metrics = new();
    private readonly List<MonitorRecordSample> _samples = new();
    private readonly Stopwatch _watch = new();
    private MonitorRecordOutput _outputs = GameMonitorRecorder.DefaultOutputs;
    private DateTime _startTime;
    private int _intervalMs = 1000;
    private string _target = "";
    private string _cpu = "", _gpu = "", _fpsProcess = "";
    private bool _truncated;

    public bool Active { get; private set; }

    /// <summary>已记录时长（UI 状态展示用）。</summary>
    public TimeSpan Elapsed => _watch.Elapsed;

    /// <summary>已采集样本数（UI 状态展示用）。</summary>
    public int SampleCount => _samples.Count;

    public void Start(List<MonitorRecordMetric> metrics, MonitorRecordOutput outputs, int intervalMs, string target)
    {
        StopDiscard();
        _metrics.AddRange(metrics);
        _outputs = outputs == MonitorRecordOutput.None ? GameMonitorRecorder.DefaultOutputs : outputs;
        _intervalMs = Math.Max(200, intervalMs);
        _target = target ?? "";
        _startTime = DateTime.Now;
        _truncated = false;
        _cpu = _gpu = _fpsProcess = "";
        Active = true;
        _watch.Restart();
    }

    /// <summary>复用覆盖层采样（不额外增加硬件读取开销）。非活跃时忽略。</summary>
    public void Append(MonitorSample sample)
    {
        if (!Active || _metrics.Count == 0 || sample is null) return;

        if (string.IsNullOrEmpty(_cpu)) _cpu = sample.CpuName;
        if (string.IsNullOrEmpty(_gpu)) _gpu = sample.GpuName;
        if (string.IsNullOrEmpty(_fpsProcess)) _fpsProcess = sample.FpsProcess;

        var values = new float[_metrics.Count];
        for (int i = 0; i < _metrics.Count; i++) values[i] = _metrics[i].Read(sample);
        _samples.Add(new MonitorRecordSample
        {
            Seconds = _watch.Elapsed.TotalSeconds,
            Values = values
        });
    }

    /// <summary>达到单次记录硬上限（内存占用有界）。</summary>
    public bool ExceededMaxDuration => Active && _watch.Elapsed.TotalMinutes >= GameMonitorRecorder.MaxDurationMinutes;

    /// <summary>标记本次记录因达到时长上限被截断（写入 meta.Truncated）。</summary>
    public void MarkTruncated() => _truncated = true;

    /// <summary>停止并写盘；没有任何有效采样时返回 null（不产生文件）。</summary>
    public RecordResult? Stop()
    {
        if (!Active) return null;
        Active = false;
        _watch.Stop();

        var metrics = new List<MonitorRecordMetric>(_metrics);
        // 掐掉首尾「FPS 还没出数」的采样，别让零值段进文件
        var samples = GameMonitorRecorder.TrimIdleEdges(
            metrics, new List<MonitorRecordSample>(_samples), out var headTrim, out var tailTrim);
        _samples.Clear();

        if (metrics.Count == 0 || samples.Count == 0) return null;

        var meta = new MonitorRecordMeta
        {
            StartTime = _startTime,
            EndTime = DateTime.Now,
            IntervalMs = _intervalMs,
            TargetWindow = _target,
            CpuName = _cpu,
            GpuName = _gpu,
            FpsProcess = _fpsProcess,
            Truncated = _truncated,
            DurationSeconds = _watch.Elapsed.TotalSeconds
        };

        return new RecordResult(WriteFiles(metrics, samples, meta), meta, samples, headTrim, tailTrim);
    }

    /// <summary>丢弃当前会话（不写盘）：指标非法、样本为空时避免留下半成品。</summary>
    public void StopDiscard()
    {
        Active = false;
        _samples.Clear();
        _metrics.Clear();
        _watch.Stop();
        _watch.Reset();
    }

    private List<string> WriteFiles(
        List<MonitorRecordMetric> metrics,
        List<MonitorRecordSample> samples,
        MonitorRecordMeta meta)
    {
        var dir = GameMonitorRecorder.GetOutputDir();
        Directory.CreateDirectory(dir);
        var baseName = GameMonitorRecorder.BuildFileNameBase(meta.StartTime);
        var utf8 = new UTF8Encoding(false);
        var paths = new List<string>();

        if (_outputs.HasFlag(MonitorRecordOutput.Json))
        {
            var p = UniquePath(Path.Combine(dir, baseName + ".json"));
            File.WriteAllText(p, GameMonitorRecorder.BuildJson(metrics, samples, meta), utf8);
            paths.Add(p);
        }
        if (_outputs.HasFlag(MonitorRecordOutput.Markdown))
        {
            var p = UniquePath(Path.Combine(dir, baseName + ".md"));
            File.WriteAllText(p, GameMonitorRecorder.BuildMarkdown(metrics, samples, meta), utf8);
            paths.Add(p);
        }
        if (_outputs.HasFlag(MonitorRecordOutput.Csv))
        {
            var p = UniquePath(Path.Combine(dir, baseName + ".csv"));
            File.WriteAllText(p, GameMonitorRecorder.BuildCsv(metrics, samples, meta), utf8);
            paths.Add(p);
        }
        return paths;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
