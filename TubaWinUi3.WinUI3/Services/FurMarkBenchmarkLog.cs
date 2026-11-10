using System.Globalization;
using System.Text.RegularExpressions;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

internal static class FurMarkBenchmarkLog
{
    // Read one final summary, never combine a partial summary with earlier measurements.
    internal static bool TryParse(string log, int expectedDurationMs, out GpuBenchmarkResult result)
    {
        result = new GpuBenchmarkResult();
        int summary = log.LastIndexOf("[ Demo Quick Stats ]", StringComparison.OrdinalIgnoreCase);
        if (summary < 0) return false;
        string stats = log[summary..];
        var score = Regex.Match(stats, @"\bSCORE\s*:\s*(\d+)", RegexOptions.IgnoreCase);
        var duration = Regex.Match(stats, @"\bduration\s*:\s*(\d+)\s*ms\b", RegexOptions.IgnoreCase);
        var fps = Regex.Match(stats, @"FPS\s*\(min/avg/max\)\s*:\s*([\d.,]+)\s*/\s*([\d.,]+)\s*/\s*([\d.,]+)", RegexOptions.IgnoreCase);
        if (!score.Success || !int.TryParse(score.Groups[1].Value, out int points) || points <= 0
            || !duration.Success || !long.TryParse(duration.Groups[1].Value, out long elapsedMs)
            || elapsedMs < expectedDurationMs || !fps.Success) return false;
        var values = new double[3];
        for (int i = 0; i < values.Length; i++)
            if (!double.TryParse(fps.Groups[i + 1].Value.Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i])
                || values[i] < 0 || (i > 0 && values[i] == 0)) return false;
        if (values[0] > values[1] || values[1] > values[2]) return false;
        result.FurMarkScore = points;
        result.MinFps = values[0];
        result.AvgFps = values[1];
        result.MaxFps = values[2];
        return true;
    }
}
