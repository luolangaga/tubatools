using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TubaWinUi3.Models;

public sealed class BenchmarkLeaderboardData
{
    public string UpdatedAt { get; set; } = "";

    public int TotalReports { get; set; }

    /// <summary>新结构：被任一榜单收录的报告并集（摘要，不含硬件详情大字段）。</summary>
    public List<BenchmarkLeaderboardRankEntry>? Reports { get; set; }

    /// <summary>新结构：各榜 id 有序列表（按下标即名次），详情按需从 DetailsPath 加载。</summary>
    public Dictionary<string, List<string>>? Boards { get; set; }

    public int TotalListed { get; set; }

    /// <summary>旧结构（兼容旧 leaderboard.json：每条含全量字段）。</summary>
    public Dictionary<string, List<BenchmarkLeaderboardRankEntry>>? Leaderboards { get; set; }
}

public sealed class BenchmarkLeaderboardPage
{
    public string SortBy { get; set; } = "";

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalEntries { get; set; }

    public int TotalPages { get; set; }

    public List<BenchmarkLeaderboardRankEntry> Entries { get; set; } = new();
}

public sealed class BenchmarkLeaderboardRankEntry
{
    public int Rank { get; set; }

    public string Id { get; set; } = "";

    public string Author { get; set; } = "";

    public string CpuName { get; set; } = "";

    public string GpuName { get; set; } = "";

    public int GamingScore { get; set; }

    public int OfficeScore { get; set; }

    public int CpuSingleCoreScore { get; set; }

    public int CpuMultiCoreScore { get; set; }

    public int GpuRenderScore { get; set; }

    public int MemoryCapacityScore { get; set; }

    public int DiskSeqReadScore { get; set; }

    public int DiskSeqWriteScore { get; set; }

    public int Disk4KReadScore { get; set; }

    public int Disk4KWriteScore { get; set; }

    public int BrowserTotalScore { get; set; }

    public string GamingGrade { get; set; } = "";

    public string OfficeGrade { get; set; } = "";

    public string SubmittedAt { get; set; } = "";

    public string OsName { get; set; } = "";

    public string GpuInfo { get; set; } = "";

    public string MotherboardName { get; set; } = "";

    public string MemoryInfo { get; set; } = "";

    public string DiskInfo { get; set; } = "";

    public string DisplayInfo { get; set; } = "";

    public string RepoPath { get; set; } = "";

    /// <summary>详情文件路径（leaderboard/details/{id}.json），按需加载硬件详情。</summary>
    public string DetailsPath { get; set; } = "";

    public BenchmarkReportEntry ToReportEntry()
    {
        return new BenchmarkReportEntry
        {
            Id = Id,
            Author = Author,
            CpuName = CpuName,
            GpuName = GpuName,
            GamingScore = GamingScore,
            OfficeScore = OfficeScore,
            CpuSingleCoreScore = CpuSingleCoreScore,
            CpuMultiCoreScore = CpuMultiCoreScore,
            GpuRenderScore = GpuRenderScore,
            MemoryCapacityScore = MemoryCapacityScore,
            DiskSeqReadScore = DiskSeqReadScore,
            DiskSeqWriteScore = DiskSeqWriteScore,
            Disk4KReadScore = Disk4KReadScore,
            Disk4KWriteScore = Disk4KWriteScore,
            BrowserTotalScore = BrowserTotalScore,
            GamingGrade = GamingGrade,
            OfficeGrade = OfficeGrade,
            OsName = OsName,
            GpuInfo = GpuInfo,
            MotherboardName = MotherboardName,
            MemoryInfo = MemoryInfo,
            DiskInfo = DiskInfo,
            DisplayInfo = DisplayInfo,
            RepoPath = RepoPath,
            DetailsPath = DetailsPath,
            SubmittedAt = DateTimeOffset.TryParse(SubmittedAt, out var dt) ? dt : DateTimeOffset.MinValue
        };
    }
}
