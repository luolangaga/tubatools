using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services;

/// <summary>Supports both upstream DiskSpd and CrystalDiskMark's score-as-exit-code build.</summary>
internal static class DiskSpdResultParser
{
    internal static void ValidateCompletion(string output, string error, int exitCode, int durationSec)
    {
        // CrystalDiskMark's bundled executable returns its Score as the process exit code.
        // A nonzero code is accepted only when it matches that explicit completion marker.
        var score = Regex.Match(output, @"(?m)^Score:\s*(\d+)\s*$");
        bool scoreExit = score.Success && uint.TryParse(score.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out uint value)
            && unchecked((uint)exitCode) == value;
        if (exitCode != 0 && !scoreExit)
            throw new InvalidDataException($"测试进程异常退出（0x{unchecked((uint)exitCode):X8}）。{error.Trim()}");

        var elapsed = Regex.Match(output, @"(?mi)^actual test time:\s*([\d.]+)s\s*$");
        if (!elapsed.Success || !double.TryParse(elapsed.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            || !double.IsFinite(seconds) || seconds < durationSec - 0.05)
            throw new InvalidDataException("DiskSpd 未完成规定时长的测量，本次结果无效。");

        foreach (string section in new[] { "Total IO", "Read IO", "Write IO" })
            _ = ReadSummary(output, section);
    }

    internal static double Measurement(string output, bool write, bool iops)
    {
        var values = ReadSummary(output, write ? "Write IO" : "Read IO");
        double value = iops ? values.Iops : values.MiBs;
        BenchmarkResultValidation.RequirePositive(value, "DiskSpd 测量");
        return value;
    }

    private static (double MiBs, double Iops) ReadSummary(string output, string section)
    {
        var header = Regex.Match(output, @"(?m)^" + Regex.Escape(section) + @"\s*\r?$");
        if (!header.Success) throw new InvalidDataException($"DiskSpd 缺少 {section} 结果表。");
        string tail = output[(header.Index + header.Length)..];
        var next = Regex.Match(tail, @"(?m)^(?:Total|Read|Write) IO\s*\r?$");
        if (next.Success) tail = tail[..next.Index];
        var row = Regex.Match(tail, @"(?m)^total:\s*\d+\s*\|\s*\d+\s*\|\s*([\d.]+)\s*\|\s*([\d.]+)\s*\r?$");
        if (!row.Success || !double.TryParse(row.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double mib)
            || !double.TryParse(row.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double iops)
            || !double.IsFinite(mib) || !double.IsFinite(iops))
            throw new InvalidDataException($"DiskSpd 的 {section} 汇总行不完整，本次结果无效。");
        return (mib, iops);
    }
}
