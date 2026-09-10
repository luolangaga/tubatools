using System.Globalization;
using System.Text;
using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>一份记录文件（JSON / CSV）的解析结果。</summary>
public sealed class MonitorRecordData
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool IsCsv { get; set; }
    public MonitorRecordMeta Meta { get; set; } = new();
    public List<MonitorRecordMetric> Metrics { get; set; } = [];
    public List<MonitorRecordSample> Samples { get; set; } = [];
}

/// <summary>
/// 「游戏监控」记录文件读取器（纯逻辑，可单测）：把 <see cref="GameMonitorRecorder"/> 导出的
/// JSON / CSV 解析成统一模型，并生成记录查看页用的可视化载荷（统计 + 抽稀曲线）。
/// </summary>
public static class GameMonitorRecordReader
{
    /// <summary>图表最多绘制的点数（超出按等间隔抽稀，统计仍基于全量数据）。</summary>
    public const int MaxChartPoints = 3000;

    public static readonly string[] Extensions = [".json", ".csv"];

    public static bool IsRecordFile(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>列出目录下的记录文件（按修改时间倒序，最新的在前）。</summary>
    public static List<string> ListRecordFiles(string dir)
    {
        var list = new List<string>();
        try
        {
            if (!Directory.Exists(dir)) return list;
            foreach (var file in Directory.EnumerateFiles(dir))
                if (IsRecordFile(file)) list.Add(file);
            list.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
        }
        catch { }
        return list;
    }

    public static MonitorRecordData Read(string path)
    {
        var text = File.ReadAllText(path);
        var data = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? ParseCsv(text)
            : ParseJson(text);
        data.FilePath = path;
        data.FileName = Path.GetFileName(path);
        return data;
    }

    // ---------------------------------------------------------------- JSON

    public static MonitorRecordData ParseJson(string json)
    {
        var data = new MonitorRecordData();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var meta = data.Meta;

        meta.StartTime = ParseTime(GetString(root, "startTime"));
        meta.EndTime = ParseTime(GetString(root, "endTime"));
        meta.DurationSeconds = GetDouble(root, "durationSeconds");
        meta.IntervalMs = GetDouble(root, "intervalMs");
        meta.Truncated = root.TryGetProperty("truncated", out var tr) && tr.ValueKind == JsonValueKind.True;
        if (root.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.Object)
        {
            meta.TargetWindow = GetString(target, "window");
            meta.CpuName = GetString(target, "cpu");
            meta.GpuName = GetString(target, "gpu");
            meta.FpsProcess = GetString(target, "fpsProcess");
        }

        if (root.TryGetProperty("metrics", out var metricsEl) && metricsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in metricsEl.EnumerateArray())
                data.Metrics.Add(MakeMetric(GetString(m, "key"), GetString(m, "label"), GetString(m, "unit"), GetString(m, "group")));
        }

        if (root.TryGetProperty("samples", out var samplesEl) && samplesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in samplesEl.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array) continue;
                double seconds = 0;
                int index = 0;
                var values = new float[data.Metrics.Count];
                for (int i = 0; i < values.Length; i++) values[i] = GameMonitorRecorder.Unavailable;
                foreach (var cell in row.EnumerateArray())
                {
                    if (index == 0)
                        seconds = cell.ValueKind == JsonValueKind.Number ? cell.GetDouble() : 0;
                    else if (index - 1 < values.Length && cell.ValueKind == JsonValueKind.Number)
                        values[index - 1] = (float)cell.GetDouble();
                    index++;
                }
                data.Samples.Add(new MonitorRecordSample { Seconds = seconds, Values = values });
            }
        }

        if (meta.DurationSeconds <= 0 && data.Samples.Count > 0)
            meta.DurationSeconds = data.Samples[^1].Seconds;
        return data;
    }

    // ----------------------------------------------------------------- CSV

    public static MonitorRecordData ParseCsv(string csv)
    {
        var data = new MonitorRecordData { IsCsv = true };
        var meta = data.Meta;
        List<string>? header = null;
        string[]? keyRow = null;

        foreach (var rawLine in csv.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line.StartsWith('#'))
            {
                var fields = SplitCsv(line[1..]);
                if (fields.Count < 2) continue;
                var key = fields[0].Trim();
                var value = fields[1].Trim();
                switch (key)
                {
                    case "开始时间": meta.StartTime = ParseTime(value); break;
                    case "结束时间": meta.EndTime = ParseTime(value); break;
                    case "采样间隔": meta.IntervalMs = ParseNumber(value); break;
                    case "目标窗口": meta.TargetWindow = NullIfDash(value); break;
                    case "CPU": meta.CpuName = NullIfDash(value); break;
                    case "GPU": meta.GpuName = NullIfDash(value); break;
                    case "FPS 进程": meta.FpsProcess = NullIfDash(value); break;
                    case "指标":
                        keyRow = fields[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        break;
                    case "备注": meta.Truncated = true; break;
                }
                continue;
            }

            if (header is null)
            {
                header = SplitCsv(line);
                for (int i = 1; i < header.Count; i++)
                {
                    var label = header[i].Trim();
                    var key = keyRow is not null && i - 1 < keyRow.Length ? keyRow[i - 1] : null;
                    data.Metrics.Add(MakeMetric(key, label, UnitFromLabel(label), null));
                }
                continue;
            }

            var cells = SplitCsv(line);
            if (cells.Count == 0) continue;
            var values = new float[data.Metrics.Count];
            for (int i = 0; i < values.Length; i++) values[i] = GameMonitorRecorder.Unavailable;
            double seconds = ParseNumber(cells[0]);
            for (int i = 1; i < cells.Count && i - 1 < values.Length; i++)
            {
                var text = cells[i].Trim();
                if (text.Length == 0) continue;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    values[i - 1] = (float)v;
            }
            data.Samples.Add(new MonitorRecordSample { Seconds = seconds, Values = values });
        }

        if (meta.DurationSeconds <= 0)
        {
            if (meta.StartTime != default && meta.EndTime != default)
                meta.DurationSeconds = (meta.EndTime - meta.StartTime).TotalSeconds;
            else if (data.Samples.Count > 0)
                meta.DurationSeconds = data.Samples[^1].Seconds;
        }
        return data;
    }

    // ------------------------------------------------------- 可视化数据

    /// <summary>记录查看页的一组可视化数据（抽稀后的曲线 + 基于全量的统计）。</summary>
    public sealed class MonitorRecordView
    {
        public MonitorRecordData Data { get; init; } = new();
        /// <summary>抽稀后的时间轴（秒），与每个指标的值一一对应。</summary>
        public List<double> Times { get; } = [];
        public List<MonitorMetricView> Metrics { get; } = [];
        /// <summary>抽稀步长（1 = 全量绘制）。</summary>
        public int Step { get; set; } = 1;
        /// <summary>绘图前被掐掉的开头 / 结尾「FPS 尚未出数」时长（秒），0 = 未裁剪。</summary>
        public double HeadTrimmedSeconds { get; set; }
        public double TailTrimmedSeconds { get; set; }
    }

    /// <summary>单个指标的可视化数据：抽稀值（不可用 = null）+ 全量统计文本。</summary>
    public sealed class MonitorMetricView
    {
        public MonitorRecordMetric Metric { get; init; } = new() { Key = "", Group = "", Label = "", Unit = "" };
        public List<double?> Values { get; } = [];
        public string Min { get; set; } = "--";
        public string Avg { get; set; } = "--";
        public string Max { get; set; } = "--";
        public string P1 { get; set; } = "--";
        public string P99 { get; set; } = "--";
        public int Count { get; set; }
        public bool HasData => Count > 0;
    }

    /// <summary>把解析结果整理成 LiveCharts 直接可用的一组曲线（原生图表，不经过中间序列化）。</summary>
    public static MonitorRecordView BuildView(MonitorRecordData data, int maxPoints = MaxChartPoints)
    {
        // 首尾「FPS 还没出数」的采样（游戏未进前台时的 0 帧）不参与绘图与统计。
        // 对已裁剪过的文件是幂等的；老记录文件也能在这里顺带被修干净。
        data.Samples = GameMonitorRecorder.TrimIdleEdges(
            data.Metrics, data.Samples, out var headTrimmed, out var tailTrimmed);

        var indexes = GameMonitorRecorder.DownsampleIndexes(data.Samples.Count, maxPoints);
        var view = new MonitorRecordView
        {
            Data = data,
            HeadTrimmedSeconds = headTrimmed,
            TailTrimmedSeconds = tailTrimmed,
            Step = data.Samples.Count > maxPoints
                ? (int)Math.Ceiling(data.Samples.Count / (double)maxPoints)
                : 1
        };

        foreach (var index in indexes) view.Times.Add(Math.Round(data.Samples[index].Seconds, 2));

        for (int m = 0; m < data.Metrics.Count; m++)
        {
            var metric = data.Metrics[m];
            var item = new MonitorMetricView { Metric = metric };
            foreach (var index in indexes)
            {
                var row = data.Samples[index];
                var v = m < row.Values.Length ? row.Values[m] : GameMonitorRecorder.Unavailable;
                item.Values.Add(v < 0 || float.IsNaN(v) || float.IsInfinity(v) ? null : Math.Round(v, 2));
            }

            var values = GameMonitorRecorder.Collect(data.Metrics, data.Samples, m);
            item.Count = values.Count;
            if (values.Count > 0)
            {
                item.Min = GameMonitorRecorder.FormatValue(values[0], metric.Unit);
                item.Avg = GameMonitorRecorder.FormatValue(GameMonitorRecorder.Average(values), metric.Unit);
                item.Max = GameMonitorRecorder.FormatValue(values[^1], metric.Unit);
                item.P1 = GameMonitorRecorder.FormatValue(GameMonitorRecorder.Percentile(values, 1), metric.Unit);
                item.P99 = GameMonitorRecorder.FormatValue(GameMonitorRecorder.Percentile(values, 99), metric.Unit);
            }
            view.Metrics.Add(item);
        }
        return view;
    }

    // ------------------------------------------------------------- helpers

    /// <summary>按 key / 标签 / 单位构造指标；命中内置声明时沿用其分组与显示名。</summary>
    private static MonitorRecordMetric MakeMetric(string? key, string label, string unit, string? group)
    {
        var known = string.IsNullOrWhiteSpace(key) ? null : GameMonitorRecorder.Find(key);
        if (known is null && !string.IsNullOrWhiteSpace(label))
            known = GameMonitorRecorder.AllMetrics.FirstOrDefault(m => string.Equals(m.Label, StripUnit(label), StringComparison.OrdinalIgnoreCase));
        if (known is not null)
            return new MonitorRecordMetric { Key = known.Key, Group = known.Group, Label = known.Label, Unit = known.Unit };
        return new MonitorRecordMetric
        {
            Key = string.IsNullOrWhiteSpace(key) ? "col" + Guid.NewGuid().ToString("N")[..6] : key,
            Group = string.IsNullOrWhiteSpace(group) ? "其他" : group,
            Label = string.IsNullOrWhiteSpace(label) ? "未命名" : StripUnit(label),
            Unit = unit
        };
    }

    /// <summary>把 "CPU 温度 (°C)" 拆成标签 + 单位。</summary>
    private static string UnitFromLabel(string label)
    {
        var open = label.LastIndexOf('(');
        if (open > 0 && label.EndsWith(')'))
        {
            var unit = label[(open + 1)..^1].Trim();
            if (unit.Length is > 0 and <= 6) return unit;
        }
        return "";
    }

    private static string StripUnit(string label)
    {
        var open = label.LastIndexOf('(');
        return open > 0 && label.EndsWith(')') ? label[..open].Trim() : label.Trim();
    }

    private static string NullIfDash(string value) => value == "--" ? "" : value;

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double GetDouble(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static DateTime ParseTime(string text) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt
            : DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2) ? dt2 : default;

    private static double ParseNumber(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsDigit(ch) || ch == '.' || ch == '-' || ch == '+') sb.Append(ch);
            else if (sb.Length > 0) break;
        }
        return double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>RFC 4180 解析（支持引号包裹与双引号转义）。</summary>
    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == ',') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result;
    }
}
