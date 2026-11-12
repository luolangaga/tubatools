using System.Globalization;
using System.Text.RegularExpressions;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

internal static class FurMarkBenchmarkLog
{
    private const string SummaryMarker = "[ Demo Quick Stats ]";

    // Accept any summary block that carries a score and frame rates. The run duration, the FPS
    // ordering and the exit code are deliberately not checked: FurMark reports different exit
    // codes and slightly different stats across driver/CPU combinations for a perfectly valid run.
    // Prefer the newest complete block so a truncated trailing summary never wins over a full one.
    internal static bool TryParse(string log, out GpuBenchmarkResult result)
    {
        var starts = new List<int>();
        for (int i = log.IndexOf(SummaryMarker, StringComparison.OrdinalIgnoreCase); i >= 0;
             i = log.IndexOf(SummaryMarker, i + SummaryMarker.Length, StringComparison.OrdinalIgnoreCase))
            starts.Add(i);
        for (int i = starts.Count - 1; i >= 0; i--)
        {
            string block = log[starts[i]..(i + 1 < starts.Count ? starts[i + 1] : log.Length)];
            if (TryParseBlock(block, out result)) return true;
        }
        result = new GpuBenchmarkResult();
        return false;
    }

    private static bool TryParseBlock(string block, out GpuBenchmarkResult result)
    {
        result = new GpuBenchmarkResult();
        var score = Regex.Match(block, @"\bSCORE\s*:\s*(\d+)", RegexOptions.IgnoreCase);
        if (!score.Success || !int.TryParse(score.Groups[1].Value, out int points) || points <= 0) return false;
        var fps = Regex.Match(block, @"FPS\s*\(min/avg/max\)\s*:\s*([\d.,]+)\s*/\s*([\d.,]+)\s*/\s*([\d.,]+)", RegexOptions.IgnoreCase);
        if (!fps.Success) return false;
        var values = new double[3];
        for (int i = 0; i < values.Length; i++)
            if (!double.TryParse(fps.Groups[i + 1].Value.Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out values[i]) || !double.IsFinite(values[i]) || values[i] < 0) return false;
        if (values[1] <= 0) return false;
        result.FurMarkScore = points;
        result.MinFps = values[0];
        result.AvgFps = values[1];
        result.MaxFps = values[2];
        return true;
    }
}
