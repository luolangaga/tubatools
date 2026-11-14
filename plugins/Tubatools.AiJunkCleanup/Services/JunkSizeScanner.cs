using System.Globalization;

namespace Tubatools.AiJunkCleanup.Services;

internal sealed record JunkSize(long Bytes, bool Partial, int Files);

/// <summary>测量文件/目录的实际大小。目录列出上限 50 万条、单次预算默认 6 秒，避免一个大目录卡住界面。</summary>
internal static class JunkSizeScanner
{
    private const int MaxEntries = 500_000;

    internal static async Task<JunkSize?> MeasureAsync(string path, bool isDirectory, CancellationToken ct, TimeSpan? budget = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!isDirectory && File.Exists(path))
        {
            try { return new JunkSize(new FileInfo(path).Length, false, 1); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }
        if (!Directory.Exists(path)) return null;
        return await Task.Run(() => MeasureDirectory(path, budget ?? TimeSpan.FromSeconds(6), ct), ct);
    }

    private static JunkSize MeasureDirectory(string path, TimeSpan budget, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(budget);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
            MaxRecursionDepth = 64
        };
        long total = 0;
        var files = 0;
        var partial = false;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", options))
            {
                if (deadline.IsCancellationRequested || ct.IsCancellationRequested) { partial = true; break; }
                try { total += new FileInfo(file).Length; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                files++;
                if (files >= MaxEntries) { partial = true; break; }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            partial = true;
        }
        return new JunkSize(total, partial, files);
    }

    internal static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        var value = bytes / 1024d;
        var units = new[] { "KB", "MB", "GB", "TB", "PB" };
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value.ToString(value >= 100 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
