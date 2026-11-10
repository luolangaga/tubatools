using System;

namespace TubaWinUi3.Models;

public sealed class PerformanceBenchmarkResult
{
    public DateTime TestTime { get; set; }

    public string DurationMode { get; set; } = "";

    public TimeSpan TotalDuration { get; set; }

    public CpuBenchmarkResult Cpu { get; set; } = new();

    public GpuBenchmarkResult Gpu { get; set; } = new();

    public MemoryBenchmarkResult Memory { get; set; } = new();

    public DiskBenchmarkResult Disk { get; set; } = new();

    public BrowserBenchmarkResult Browser { get; set; } = new();

    public int GamingScore { get; set; }

    public string GamingGrade { get; set; } = "";

    public int OfficeScore { get; set; }

    public string OfficeGrade { get; set; } = "";

    /// <summary>WinUI 性能测试结果（独立总分，不并入游戏/办公总分）。</summary>
    public WinPerformanceResult Win { get; set; } = new();

    public string CpuName { get; set; } = "";

    public string GpuName { get; set; } = "";

    public string GpuInfo { get; set; } = "";

    public string OsName { get; set; } = "";

    public string MotherboardName { get; set; } = "";

    public string MemoryInfo { get; set; } = "";

    public string DiskInfo { get; set; } = "";

    public string DisplayInfo { get; set; } = "";
}
