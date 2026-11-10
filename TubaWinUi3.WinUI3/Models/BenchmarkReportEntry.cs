using System;

namespace TubaWinUi3.Models;

public sealed class BenchmarkReportEntry
{
    public string Id { get; set; } = "";

    public string Author { get; set; } = "";

    public DateTimeOffset SubmittedAt { get; set; }

    public string CpuName { get; set; } = "";

    public string GpuName { get; set; } = "";

    public string OsName { get; set; } = "";

    public string GpuInfo { get; set; } = "";

    public string MotherboardName { get; set; } = "";

    public string MemoryInfo { get; set; } = "";

    public string DiskInfo { get; set; } = "";

    public string DisplayInfo { get; set; } = "";

    public int GamingScore { get; set; }

    public string GamingGrade { get; set; } = "";

    public int OfficeScore { get; set; }

    public string OfficeGrade { get; set; } = "";

    public int CpuSingleCoreScore { get; set; }

    public int CpuMultiCoreScore { get; set; }

    public int GpuRenderScore { get; set; }

    public int MemoryCapacityScore { get; set; }

    public int DiskSeqReadScore { get; set; }

    public int DiskSeqWriteScore { get; set; }

    public int Disk4KReadScore { get; set; }

    public int Disk4KWriteScore { get; set; }

    public int BrowserTotalScore { get; set; }

    public string RepoPath { get; set; } = "";

    public string DetailsPath { get; set; } = "";

    public string SubmittedAtShort => SubmittedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public override string ToString()
    {
        return $"@{Author} | {CpuName} | 游戏{GamingScore} 办公{OfficeScore}";
    }
}
