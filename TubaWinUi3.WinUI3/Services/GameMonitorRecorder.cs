using System.Globalization;
using System.Text;
using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>可记录指标的声明（「游戏监控」勾选列表与导出文件的唯一事实来源）。</summary>
public sealed class MonitorRecordMetric
{
    public required string Key { get; init; }
    public required string Group { get; init; }
    public required string Label { get; init; }
    public required string Unit { get; init; }

    /// <summary>从采样中取值；从历史文件解析出来的指标没有采集函数（只用于展示）。</summary>
    public Func<MonitorSample, float> Read { get; init; } = _ => GameMonitorRecorder.Unavailable;
}

/// <summary>内存中的一条采样：<see cref="Seconds"/> 为相对记录开始的秒数，<see cref="Values"/> 按选定指标顺序排列。</summary>
public sealed class MonitorRecordSample
{
    public double Seconds { get; init; }
    public float[] Values { get; init; } = [];
}

/// <summary>记录元信息（写入文件头部）。</summary>
public sealed class MonitorRecordMeta
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double IntervalMs { get; set; }
    public string TargetWindow { get; set; } = "";
    public string CpuName { get; set; } = "";
    public string GpuName { get; set; } = "";
    public string FpsProcess { get; set; } = "";
    /// <summary>true = 达到最长记录时长被自动截断。</summary>
    public bool Truncated { get; set; }
    public double DurationSeconds { get; set; }
}

/// <summary>导出文件类型（可组合）。</summary>
[Flags]
public enum MonitorRecordOutput
{
    None = 0,
    Json = 1,
    Markdown = 2,
    Csv = 4
}

/// <summary>
/// 「游戏监控」数据记录器（纯逻辑，无 UI 依赖）：采样仅存内存，记录结束（或达到上限自动结束）
/// 时一次性写出 JSON / Markdown / CSV，避免边采边写盘影响游戏性能。
/// </summary>
public static class GameMonitorRecorder
{
    /// <summary>最长记录时长（分钟）。到点自动停止并保存，防止长时间运行内存无限增长。</summary>
    public const int MaxDurationMinutes = 120;

    /// <summary>Markdown 明细最大行数，超出时等间隔抽稀（JSON 始终保留全部样本）。</summary>
    public const int MaxDetailRows = 600;

    /// <summary>传感器不可用时的哨兵值。</summary>
    public const float Unavailable = -1f;

    private const string GroupFps = "FPS";
    private const string GroupCpu = "CPU";
    private const string GroupGpu = "GPU";
    private const string GroupMem = "内存";
    private const string GroupDisk = "磁盘";
    private const string GroupNet = "网络";
    private const string GroupBat = "电池";

    public static readonly IReadOnlyList<MonitorRecordMetric> AllMetrics =
    [
        new() { Key = "fps",         Group = GroupFps,  Label = "FPS",      Unit = "",      Read = s => s.Fps },
        new() { Key = "frametime",   Group = GroupFps,  Label = "帧时间",    Unit = "ms",    Read = s => s.FrameTimeMs },
        new() { Key = "latency",     Group = GroupFps,  Label = "渲染延迟",  Unit = "ms",    Read = s => s.RenderLatencyMs },
        new() { Key = "fpslow1",     Group = GroupFps,  Label = "1% Low",   Unit = "",      Read = s => s.FpsLow1 },
        new() { Key = "fpslow01",    Group = GroupFps,  Label = "0.1% Low", Unit = "",      Read = s => s.FpsLow01 },

        new() { Key = "cpuload",     Group = GroupCpu,  Label = "CPU 负载", Unit = "%",     Read = s => s.CpuLoad },
        new() { Key = "cputemp",     Group = GroupCpu,  Label = "CPU 温度", Unit = "°C",    Read = s => s.CpuTemp },
        new() { Key = "cpuclock",    Group = GroupCpu,  Label = "CPU 频率", Unit = "MHz",   Read = s => s.CpuClock },
        new() { Key = "cpupower",    Group = GroupCpu,  Label = "CPU 功耗", Unit = "W",     Read = s => s.CpuPower },

        new() { Key = "gpuload",     Group = GroupGpu,  Label = "GPU 负载", Unit = "%",     Read = s => s.GpuLoad },
        new() { Key = "gputemp",     Group = GroupGpu,  Label = "GPU 温度", Unit = "°C",    Read = s => s.GpuTemp },
        new() { Key = "gpuclock",    Group = GroupGpu,  Label = "GPU 频率", Unit = "MHz",   Read = s => s.GpuClock },
        new() { Key = "gpupower",    Group = GroupGpu,  Label = "GPU 功耗", Unit = "W",     Read = s => s.GpuPower },
        new() { Key = "gpuvram",     Group = GroupGpu,  Label = "显存使用",  Unit = "GB",    Read = s => s.GpuVramUsedGB },

        new() { Key = "memload",     Group = GroupMem,  Label = "内存负载", Unit = "%",     Read = s => s.MemLoad },
        new() { Key = "memused",     Group = GroupMem,  Label = "内存使用", Unit = "GB",    Read = s => s.MemUsedGB },

        new() { Key = "diskread",    Group = GroupDisk, Label = "磁盘读取", Unit = "MB/s",  Read = s => s.DiskReadMBs },
        new() { Key = "diskwrite",   Group = GroupDisk, Label = "磁盘写入", Unit = "MB/s",  Read = s => s.DiskWriteMBs },
        new() { Key = "disktemp",    Group = GroupDisk, Label = "磁盘温度", Unit = "°C",    Read = s => s.DiskTemp },

        new() { Key = "netup",       Group = GroupNet,  Label = "网络上传", Unit = "MB/s",  Read = s => s.NetUpMBs },
        new() { Key = "netdown",     Group = GroupNet,  Label = "网络下载", Unit = "MB/s",  Read = s => s.NetDownMBs },

        new() { Key = "batpercent",  Group = GroupBat,  Label = "电池电量", Unit = "%",     Read = s => s.BatPercent },
        new() { Key = "batpower",    Group = GroupBat,  Label = "电池功率", Unit = "W",     Read = s => s.BatPower },
    ];

    public static MonitorRecordMetric? Find(string key) =>
        AllMetrics.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>记录文件输出目录的设置项键（AppSettings）。</summary>
    public const string RecordDirSetting = "GameOverlay_RecordDir";

    /// <summary>输出目录：用户自定义优先，否则 &lt;数据目录&gt;/GameMonitorRecords。</summary>
    public static string GetOutputDir()
    {
        var custom = AppSettings.Get(RecordDirSetting);
        return string.IsNullOrWhiteSpace(custom)
            ? Path.Combine(ConfigManager.GetDataDir(), "GameMonitorRecords")
            : custom;
    }

    /// <summary>默认勾选全部指标（设置项里逗号分隔）。</summary>
    public static string DefaultSelection => string.Join(",", AllMetrics.Select(m => m.Key));

    /// <summary>把设置项里的逗号分隔键解析为指标列表（保持 <see cref="AllMetrics"/> 顺序）。</summary>
    public static List<MonitorRecordMetric> ParseSelection(string? setting)
    {
        if (setting is null) return AllMetrics.ToList();
        var keys = setting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return AllMetrics.Where(m => keys.Contains(m.Key)).ToList();
    }

    public static string SerializeSelection(IEnumerable<string> keys) => string.Join(",", keys);

    /// <summary>默认导出：JSON（原始数据）+ Markdown（可读报告）。</summary>
    public const MonitorRecordOutput DefaultOutputs = MonitorRecordOutput.Json | MonitorRecordOutput.Markdown;

    private static readonly (MonitorRecordOutput Flag, string Key)[] OutputKeys =
    [
        (MonitorRecordOutput.Json, "json"),
        (MonitorRecordOutput.Markdown, "markdown"),
        (MonitorRecordOutput.Csv, "csv"),
    ];

    public static string SerializeOutputs(MonitorRecordOutput outputs) =>
        string.Join(",", OutputKeys.Where(o => outputs.HasFlag(o.Flag)).Select(o => o.Key));

    /// <summary>解析设置项（null = 默认值）。</summary>
    public static MonitorRecordOutput ParseOutputs(string? setting)
    {
        if (setting is null) return DefaultOutputs;
        var keys = setting.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = MonitorRecordOutput.None;
        foreach (var (flag, key) in OutputKeys)
            if (keys.Contains(key)) result |= flag;
        return result;
    }

    public static bool NeedsFps(IEnumerable<MonitorRecordMetric> metrics) =>
        metrics.Any(m => m.Group == GroupFps);

    /// <summary>输出文件名（不含扩展名）。</summary>
    public static string BuildFileNameBase(DateTime start) =>
        "游戏监控记录_" + start.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- JSON

    public static string BuildJson(
        IReadOnlyList<MonitorRecordMetric> metrics,
        IReadOnlyList<MonitorRecordSample> samples,
        MonitorRecordMeta meta)
    {
        using var ms = new MemoryStream();
        // Relaxed 编码器：中文指标名/窗口标题保持可读（本文件只落本地磁盘，不内嵌 HTML）
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions
               {
                   Indented = true,
                   Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
               }))
        {
            w.WriteStartObject();
            w.WriteString("app", "图吧工具箱 WinUI3 · 游戏监控");
            w.WriteNumber("version", 1);
            w.WriteString("startTime", meta.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            w.WriteString("endTime", meta.EndTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            w.WriteNumber("durationSeconds", Math.Round(meta.DurationSeconds, 2));
            w.WriteNumber("intervalMs", Math.Round(meta.IntervalMs, 1));
            w.WriteNumber("sampleCount", samples.Count);
            w.WriteBoolean("truncated", meta.Truncated);

            w.WriteStartObject("target");
            w.WriteString("window", meta.TargetWindow);
            w.WriteString("cpu", meta.CpuName);
            w.WriteString("gpu", meta.GpuName);
            w.WriteString("fpsProcess", meta.FpsProcess);
            w.WriteEndObject();

            w.WriteStartArray("metrics");
            foreach (var m in metrics)
            {
                w.WriteStartObject();
                w.WriteString("key", m.Key);
                w.WriteString("label", m.Label);
                w.WriteString("unit", m.Unit);
                w.WriteString("group", m.Group);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("samples");
            foreach (var s in samples)
            {
                w.WriteStartArray();
                w.WriteNumberValue(Math.Round(s.Seconds, 2));
                foreach (var v in s.Values)
                {
                    if (float.IsNaN(v) || float.IsInfinity(v)) w.WriteNullValue();
                    else w.WriteNumberValue(Math.Round(v, 2));
                }
                w.WriteEndArray();
            }
            w.WriteEndArray();

            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ------------------------------------------------------------ Markdown

    public static string BuildMarkdown(
        IReadOnlyList<MonitorRecordMetric> metrics,
        IReadOnlyList<MonitorRecordSample> samples,
        MonitorRecordMeta meta)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 游戏监控记录");
        sb.AppendLine();
        sb.AppendLine($"> 导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}　|　图吧工具箱 WinUI3 · 游戏监控");
        sb.AppendLine();

        sb.AppendLine("## 基本信息");
        sb.AppendLine();
        sb.AppendLine("| 项目 | 内容 |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine($"| 开始时间 | {Esc(meta.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))} |");
        sb.AppendLine($"| 结束时间 | {Esc(meta.EndTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))} |");
        sb.AppendLine($"| 记录时长 | {FormatDuration(meta.DurationSeconds)} |");
        sb.AppendLine($"| 采样间隔 | {meta.IntervalMs:0} ms（实际平均 {AverageIntervalMs(samples, meta):0} ms） |");
        sb.AppendLine($"| 采样点数 | {samples.Count} |");
        sb.AppendLine($"| 目标窗口 | {Esc(Empty(meta.TargetWindow))} |");
        sb.AppendLine($"| CPU | {Esc(Empty(meta.CpuName))} |");
        sb.AppendLine($"| GPU | {Esc(Empty(meta.GpuName))} |");
        sb.AppendLine($"| FPS 进程 | {Esc(Empty(meta.FpsProcess))} |");
        sb.AppendLine($"| 记录指标 | {string.Join("、", metrics.Select(m => m.Label))} |");
        if (meta.Truncated)
            sb.AppendLine($"| 备注 | 已达 {MaxDurationMinutes} 分钟上限，记录被自动停止 |");
        sb.AppendLine();

        sb.AppendLine("## 统计摘要");
        sb.AppendLine();
        sb.AppendLine("| 指标 | 最小 | 平均 | 最大 | P1 | P99 | 有效样本 |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        for (int i = 0; i < metrics.Count; i++)
        {
            var m = metrics[i];
            var values = Collect(metrics, samples, i);
            if (values.Count == 0)
            {
                sb.AppendLine($"| {m.Label}{UnitSuffix(m)} | -- | -- | -- | -- | -- | 0 |");
                continue;
            }
            sb.AppendLine(
                $"| {m.Label}{UnitSuffix(m)} | {FormatValue(values[0], m.Unit)} | {FormatValue(Average(values), m.Unit)} | " +
                $"{FormatValue(values[^1], m.Unit)} | {FormatValue(Percentile(values, 1), m.Unit)} | " +
                $"{FormatValue(Percentile(values, 99), m.Unit)} | {values.Count} |");
        }
        sb.AppendLine();

        sb.AppendLine("## 采样明细");
        sb.AppendLine();
        int step = samples.Count > MaxDetailRows ? (int)Math.Ceiling(samples.Count / (double)MaxDetailRows) : 1;
        if (step > 1)
            sb.AppendLine($"（共 {samples.Count} 条，此处每 {step} 条抽稀 1 条展示；完整数据见同名 JSON 文件）");
        else
            sb.AppendLine($"（共 {samples.Count} 条）");
        sb.AppendLine();
        sb.Append("| 时间 (s) | ");
        for (int i = 0; i < metrics.Count; i++) sb.Append($"{metrics[i].Label}{UnitSuffix(metrics[i])} | ");
        sb.AppendLine();
        sb.Append("| --- | ");
        for (int i = 0; i < metrics.Count; i++) sb.Append("--- | ");
        sb.AppendLine();
        foreach (var r in DetailRowIndexes(samples.Count))
        {
            var s = samples[r];
            sb.Append($"| {s.Seconds.ToString("0.0", CultureInfo.InvariantCulture)} | ");
            for (int i = 0; i < metrics.Count; i++)
            {
                var v = i < s.Values.Length ? s.Values[i] : Unavailable;
                sb.Append(v < 0 ? "--" : FormatValue(v, metrics[i].Unit)).Append(" | ");
            }
            sb.AppendLine();
        }
        sb.AppendLine();

        sb.AppendLine("## 说明");
        sb.AppendLine();
        sb.AppendLine($"- 传感器不可用时记为 `-1`（明细表中显示为 `--`），统计值已剔除这些样本。");
        sb.AppendLine($"- 完整原始数据见同名 `.json` 文件：`samples` 每行第一列为相对开始时间的秒数，其余数值按 `metrics` 数组的顺序排列。");
        sb.AppendLine($"- 记录时长上限 {MaxDurationMinutes} 分钟，到点自动停止并写出文件。");
        sb.AppendLine();

        return sb.ToString();
    }

    // ----------------------------------------------------------------- CSV

    /// <summary>
    /// 原始表格（Excel / pandas 友好）：顶部 `#` 注释行保留元信息，传感器不可用写空值，
    /// 保留全部样本不做抽稀，方便直接作图。
    /// </summary>
    public static string BuildCsv(
        IReadOnlyList<MonitorRecordMetric> metrics,
        IReadOnlyList<MonitorRecordSample> samples,
        MonitorRecordMeta meta)
    {
        var sb = new StringBuilder();
        void Comment(string key, string value) => sb.AppendLine($"# {Csv(key)},{Csv(value)}");

        sb.AppendLine("# 图吧工具箱 WinUI3 · 游戏监控");
        Comment("开始时间", meta.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Comment("结束时间", meta.EndTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Comment("记录时长", FormatDuration(meta.DurationSeconds));
        Comment("采样间隔", $"{meta.IntervalMs:0} ms");
        Comment("采样点数", samples.Count.ToString(CultureInfo.InvariantCulture));
        Comment("目标窗口", Empty(meta.TargetWindow));
        Comment("CPU", Empty(meta.CpuName));
        Comment("GPU", Empty(meta.GpuName));
        Comment("FPS 进程", Empty(meta.FpsProcess));
        Comment("指标", string.Join(",", metrics.Select(m => m.Key)));
        if (meta.Truncated) Comment("备注", $"已达 {MaxDurationMinutes} 分钟上限被自动停止");

        sb.Append(Csv("时间 (s)"));
        foreach (var m in metrics)
            sb.Append(',').Append(Csv(string.IsNullOrEmpty(m.Unit) ? m.Label : $"{m.Label} ({m.Unit})"));
        sb.Append('\n');

        foreach (var s in samples)
        {
            sb.Append(s.Seconds.ToString("0.0", CultureInfo.InvariantCulture));
            for (int i = 0; i < metrics.Count; i++)
            {
                var v = i < s.Values.Length ? s.Values[i] : Unavailable;
                sb.Append(',');
                if (v >= 0 && !float.IsNaN(v) && !float.IsInfinity(v))
                    sb.Append(FormatValue(v, metrics[i].Unit));
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>RFC 4180 字段转义（含逗号/引号/换行时加引号）。</summary>
    private static string Csv(string field)
    {
        if (field.IndexOfAny([',', '"', '\n', '\r']) < 0) return field;
        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    // ------------------------------------------------------------- helpers

    /// <summary>明细表要输出的样本下标：样本超出 <see cref="MaxDetailRows"/> 时等间隔抽稀，并始终包含最后一条。</summary>
    public static List<int> DetailRowIndexes(int sampleCount) => DownsampleIndexes(sampleCount, MaxDetailRows);

    /// <summary>等间隔抽稀下标（数量超过 <paramref name="maxPoints"/> 时按步长取样，始终包含最后一条）。</summary>
    public static List<int> DownsampleIndexes(int sampleCount, int maxPoints)
    {
        var list = new List<int>();
        if (sampleCount <= 0 || maxPoints <= 0) return list;
        int step = sampleCount > maxPoints ? (int)Math.Ceiling(sampleCount / (double)maxPoints) : 1;
        for (int i = 0; i < sampleCount; i += step) list.Add(i);
        if (list[^1] != sampleCount - 1) list.Add(sampleCount - 1);
        return list;
    }

    /// <summary>FPS 列的取值（列不存在或越界时视为不可用）。</summary>
    private static float FpsAt(MonitorRecordSample sample, int index) =>
        index >= 0 && index < sample.Values.Length ? sample.Values[index] : Unavailable;

    /// <summary>
    /// 掐掉首尾「FPS 还没出数」的采样。<br/>
    /// 被捕获的进程（游戏 / 应用）还没进入前台时 FPS 恒为 0，这段前置零值会把曲线起点压在 0，
    /// 也会污染 1% Low、平均帧率这类统计；停止录制后主循环残留的尾部采样同理。<br/>
    /// 仅当勾选了 FPS 指标、且整段确实出现过 fps &gt; 0 时才裁剪——全程没抓到进程（例如只录桌面）
    /// 时原样返回，避免把整段记录删空。只削首尾，记录中途偶发的 0 帧（切后台、加载）全部保留。
    /// </summary>
    /// <param name="headTrimmedSeconds">被掐掉的开头时长（秒，从记录起点算起）。</param>
    /// <param name="tailTrimmedSeconds">被掐掉的结尾时长（秒，到最后一条采样为止）。</param>
    public static List<MonitorRecordSample> TrimIdleEdges(
        IReadOnlyList<MonitorRecordMetric> metrics,
        List<MonitorRecordSample> samples,
        out double headTrimmedSeconds,
        out double tailTrimmedSeconds)
    {
        headTrimmedSeconds = 0;
        tailTrimmedSeconds = 0;
        if (samples.Count == 0) return samples;

        var fpsIndex = -1;
        for (int i = 0; i < metrics.Count; i++)
        {
            if (string.Equals(metrics[i].Key, "fps", StringComparison.OrdinalIgnoreCase))
            {
                fpsIndex = i;
                break;
            }
        }
        if (fpsIndex < 0) return samples;

        var everActive = false;
        foreach (var sample in samples)
        {
            if (FpsAt(sample, fpsIndex) > 0) { everActive = true; break; }
        }
        if (!everActive) return samples;

        var start = 0;
        while (start < samples.Count && FpsAt(samples[start], fpsIndex) <= 0) start++;
        var end = samples.Count - 1;
        while (end >= start && FpsAt(samples[end], fpsIndex) <= 0) end--;
        if (start == 0 && end == samples.Count - 1) return samples;

        headTrimmedSeconds = Math.Max(0, samples[start].Seconds - samples[0].Seconds);
        tailTrimmedSeconds = Math.Max(0, samples[^1].Seconds - samples[end].Seconds);
        return samples.GetRange(start, end - start + 1);
    }

    /// <summary>取某列的有效样本（剔除 NaN/Infinity 与不可用哨兵值），按数值升序返回。</summary>
    public static List<float> Collect(
        IReadOnlyList<MonitorRecordMetric> metrics,
        IReadOnlyList<MonitorRecordSample> samples,
        int index)
    {
        var list = new List<float>(samples.Count);
        foreach (var s in samples)
        {
            if (index >= s.Values.Length) continue;
            var v = s.Values[index];
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0) continue;
            list.Add(v);
        }
        list.Sort();
        return list;
    }

    public static double Average(List<float> sorted)
    {
        if (sorted.Count == 0) return double.NaN;
        double sum = 0;
        foreach (var v in sorted) sum += v;
        return sum / sorted.Count;
    }

    /// <summary>线性插值分位数（<paramref name="sorted"/> 需已升序）。</summary>
    public static double Percentile(List<float> sorted, double percent)
    {
        if (sorted.Count == 0) return double.NaN;
        if (sorted.Count == 1) return sorted[0];
        double rank = percent / 100.0 * (sorted.Count - 1);
        int lo = (int)Math.Floor(rank);
        int hi = (int)Math.Ceiling(rank);
        if (lo == hi) return sorted[lo];
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }

    public static string FormatDuration(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours} 小时 {ts.Minutes} 分 {ts.Seconds} 秒"
            : $"{ts.Minutes} 分 {ts.Seconds} 秒";
    }

    public static string FormatValue(double v, string unit)
    {
        if (double.IsNaN(v)) return "--";
        return unit switch
        {
            "MHz" => v.ToString("0", CultureInfo.InvariantCulture),
            "MB/s" => v.ToString("0.00", CultureInfo.InvariantCulture),
            _ => v.ToString(Math.Abs(v) >= 100 ? "0.0" : "0.00", CultureInfo.InvariantCulture),
        };
    }

    private static string UnitSuffix(MonitorRecordMetric m) => string.IsNullOrEmpty(m.Unit) ? "" : $" ({m.Unit})";

    private static double AverageIntervalMs(IReadOnlyList<MonitorRecordSample> samples, MonitorRecordMeta meta)
    {
        if (samples.Count > 1)
        {
            var span = samples[^1].Seconds - samples[0].Seconds;
            if (span > 0) return span / (samples.Count - 1) * 1000.0;
        }
        return meta.IntervalMs;
    }

    private static string Empty(string s) => string.IsNullOrWhiteSpace(s) ? "--" : s;

    private static string Esc(string s) => s.Replace("|", "\\|");
}
