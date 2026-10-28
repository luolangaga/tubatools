using System;
using System.IO;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>Only completed measurements enter scoring. Does not change benchmark workloads or score curves.</summary>
internal static class BenchmarkResultValidation
{
    internal static void RequirePositive(double value, string measurement)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new InvalidDataException($"{measurement}未获得有效数据，请重新测试。");
    }

    internal static void Cpu(CpuBenchmarkResult result)
    {
        RequirePositive(result.SingleCoreIterations, "CPU 单核测试");
        RequirePositive(result.MultiCoreIterations, "CPU 多核测试");
    }

    internal static void Latency(InterCoreLatencyMatrix result)
    {
        if (result.CoreCount < 2 || result.Latencies.GetLength(0) != result.CoreCount ||
            result.Latencies.GetLength(1) != result.CoreCount)
            throw new InvalidDataException("核间延迟测试矩阵不完整，请重新测试。");
        for (int i = 0; i < result.CoreCount; i++)
            for (int j = 0; j < result.CoreCount; j++)
                if (i != j) RequirePositive(result.Latencies[i, j], "核间延迟测试（数据不完整）");
        RequirePositive(result.AverageNs, "核间延迟测试");
    }

    internal static void Gpu(GpuBenchmarkResult result)
    {
        RequirePositive(result.FurMarkScore, "FurMark 分数");
        RequirePositive(result.AvgFps, "FurMark 平均帧率");
    }

    internal static void Disk(DiskBenchmarkResult result)
    {
        RequirePositive(result.SeqReadMBs, "硬盘顺序读取");
        RequirePositive(result.SeqWriteMBs, "硬盘顺序写入");
        RequirePositive(result.Random4KReadIops, "硬盘随机读取");
        RequirePositive(result.Random4KWriteIops, "硬盘随机写入");
    }

    internal static void Win(WinPerformanceResult result)
    {
        if (result.Runs.Count != result.RunCount)
            throw new InvalidDataException("WinUI 测试轮次不完整，请重新测试。");
        foreach (var run in result.Runs)
        {
            RequirePositive(run.ListLoadMs, "列表加载");
            RequirePositive(run.ImageListMs, "图标渲染");
            RequirePositive(run.TabSwitchMs, "标签切换");
            RequirePositive(run.ScrollMs, "滚动");
            RequirePositive(run.TreeExpandMs, "树形展开");
            RequirePositive(run.SortFilterMs, "排序过滤");
            RequirePositive(run.TextRenderMs, "文本渲染");
        }
    }
}
