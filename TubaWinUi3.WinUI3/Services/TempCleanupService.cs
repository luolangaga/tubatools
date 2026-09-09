using System;
using System.IO;
using System.Threading;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>
/// 清理本软件散落在系统临时目录（%TEMP%）中的临时文件/目录。
/// 只匹配已知前缀或文件名，不碰其他程序的临时文件；被占用或无权访问的条目自动跳过，
/// 不影响正在运行的任务（删除失败仅计数，不中断批量）。
/// </summary>
public static class TempCleanupService
{
    private const int DeleteAttempts = 3;
    private const int DeleteRetryDelayMs = 300;

    /// <summary>app_crash.log（App.xaml.cs 崩溃日志，固定文件名）。</summary>
    private const string CrashLogName = "app_crash.log";

    /// <summary>
    /// 本软件在 %TEMP% 下创建的临时条目前缀（目录与文件共用），
    /// 与各服务的创建处保持一致：更新包、工具安装包暂存、文档转换中间产物、
    /// 崩溃/探测输出等。带下划线的社区工具暂存分列两条（TubaCommunity_ 与
    /// TubaCommunityVerify_），避免误伤形似但无关的第三方文件名。
    /// </summary>
    private static readonly string[] KnownPrefixes =
    {
        "TubaWinUi3_",
        "TubaCommunity_",
        "TubaCommunityVerify_",
        "officecli_",
        "pdfocr_",
        "office_",
        "doceng_",
        "ocr_",
        "tuba-",
    };

    public sealed record CleanupResult(int DeletedCount, int SkippedCount, long FreedBytes);

    /// <summary>判断 %TEMP% 下的条目名是否属于本软件创建的临时文件（纯函数，可单测）。</summary>
    public static bool IsAppTempEntry(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (string.Equals(name, CrashLogName, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var prefix in KnownPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>扫描并删除 %TEMP% 下所有本软件创建的临时条目。</summary>
    public static CleanupResult CleanTempFiles(CancellationToken cancellationToken = default)
    {
        var deleted = 0;
        var skipped = 0;
        long freed = 0;

        var tempRoot = Path.GetTempPath();
        if (!Directory.Exists(tempRoot)) return new CleanupResult(0, 0, 0);

        foreach (var entry in Directory.EnumerateFileSystemEntries(tempRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileName(entry);
            if (!IsAppTempEntry(name)) continue;

            try
            {
                if (Directory.Exists(entry))
                {
                    freed += GetDirectorySize(entry);
                    if (TryDeleteDirectory(entry)) deleted++;
                    else skipped++;
                }
                else if (File.Exists(entry))
                {
                    freed += new FileInfo(entry).Length;
                    if (TryDeleteFile(entry)) deleted++;
                    else skipped++;
                }
            }
            catch
            {
                skipped++;
            }
        }

        return new CleanupResult(deleted, skipped, freed);
    }

    /// <summary>把字节数格式化为 B/KB/MB/GB 短字符串。</summary>
    public static string FormatBytes(long size)
    {
        if (size >= 1L << 30) return $"{(double)size / (1L << 30):F2} GB";
        if (size >= 1L << 20) return $"{(double)size / (1L << 20):F1} MB";
        if (size >= 1L << 10) return $"{(double)size / (1L << 10):F1} KB";
        return $"{size} B";
    }

    private static long GetDirectorySize(string dir)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
        }
        catch { }
        return size;
    }

    private static bool TryDeleteDirectory(string dir)
    {
        // 只读文件会导致 Directory.Delete 抛异常，先递归清除属性（复用下载器的实现）
        for (var i = 0; i < DeleteAttempts && Directory.Exists(dir); i++)
        {
            try
            {
                ZipExtractHelper.TryClearReadOnlyAttributes(dir);
                Directory.Delete(dir, true);
                return true;
            }
            catch
            {
                if (i < DeleteAttempts - 1) Thread.Sleep(DeleteRetryDelayMs);
            }
        }
        return !Directory.Exists(dir);
    }

    private static bool TryDeleteFile(string file)
    {
        for (var i = 0; i < DeleteAttempts && File.Exists(file); i++)
        {
            try
            {
                File.Delete(file);
                return true;
            }
            catch
            {
                if (i < DeleteAttempts - 1) Thread.Sleep(DeleteRetryDelayMs);
            }
        }
        return !File.Exists(file);
    }
}