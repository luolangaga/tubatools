using System.Diagnostics;
using System.IO;
using System.Reflection;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class BenchmarkResultValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MissingOrNonFiniteMeasurement_IsRejected(double value)
        => Assert.Throws<InvalidDataException>(() => BenchmarkResultValidation.RequirePositive(value, "测试"));

    [Fact]
    public void IncompleteCpu_DoesNotReceiveAScore()
        => Assert.Throws<InvalidDataException>(() => BenchmarkResultValidation.Cpu(new CpuBenchmarkResult { SingleCoreIterations = 728 }));

    [Theory]
    [InlineData("\"728.4\",\"8351.2\"", 728, 8351)]
    [InlineData("728, 8351\r\n", 728, 8351)]
    public void CompleteCpuOutput_UsesExistingRounding(string output, int single, int multi)
        => Assert.Equal((single, multi), PerformanceBenchmarkService.ParseCpuzBenchCsv(output));

    [Theory]
    [InlineData("")]
    [InlineData("error 12 exit 34")]
    [InlineData("0,8351")]
    [InlineData("728,0")]
    [InlineData("728")]
    [InlineData("999999999999999999999,8351")]
    public void CpuErrorOutput_IsNeverParsedAsMeasurements(string output)
        => Assert.Throws<InvalidDataException>(() => PerformanceBenchmarkService.ParseCpuzBenchCsv(output));

    [Fact]
    public void PartialLatencyMatrix_IsRejected_AndCpuIsNotMutated()
    {
        var cpu = new CpuBenchmarkResult();
        var matrix = new InterCoreLatencyMatrix { CoreCount = 2, Latencies = new double[,] { { 0, 12 }, { -1, 0 } }, AverageNs = 12 };
        Assert.Throws<InvalidDataException>(() => PerformanceBenchmarkService.ApplyLatencyResult(cpu, matrix));
        Assert.Null(cpu.LatencyMatrix);
        Assert.Equal(0, cpu.LatencyScore);
    }

    [Fact]
    public void CompleteLatencyMatrix_KeepsExistingScoringCurve()
    {
        var cpu = new CpuBenchmarkResult();
        PerformanceBenchmarkService.ApplyLatencyResult(cpu, new InterCoreLatencyMatrix { CoreCount = 2, Latencies = new double[,] { { 0, 80 }, { 80, 0 } }, AverageNs = 80 });
        Assert.Equal(PerformanceBenchmarkService.NormalizeLatency(80), cpu.LatencyScore);
    }

    [Fact]
    public void MissingLatency_IsNotAssignedADefaultScore()
    {
        Assert.Equal(0, PerformanceBenchmarkService.NormalizeLatency(0));
        Assert.Equal(0, PerformanceBenchmarkService.NormalizeLatency(double.NaN));
    }

    [Fact]
    public void PartialDiskAndGpuOutput_IsRejected()
    {
        Assert.Throws<InvalidDataException>(() => BenchmarkResultValidation.Disk(new DiskBenchmarkResult { SeqReadMBs = 5911, SeqWriteMBs = 5000 }));
        Assert.Throws<InvalidDataException>(() => BenchmarkResultValidation.Gpu(new GpuBenchmarkResult { FurMarkScore = 4000 }));
    }

    [Fact]
    public void ValidSlowMeasurements_AreAcceptedEvenIfNormalizedScoreIsZero()
    {
        BenchmarkResultValidation.Disk(new DiskBenchmarkResult { SeqReadMBs = 0.1, SeqWriteMBs = 0.1, Random4KReadIops = 1, Random4KWriteIops = 1 });
    }

    [Fact]
    public void IncompleteOrEmptyWinRounds_AreRejected()
    {
        var result = new WinPerformanceResult();
        result.Runs.Add(new WinPerformanceRunResult { ListLoadMs = 1 });
        Assert.Throws<InvalidDataException>(() => BenchmarkResultValidation.Win(result));
        for (int i = 1; i < 5; i++) result.Runs.Add(new WinPerformanceRunResult());
        Assert.Throws<InvalidDataException>(() => BenchmarkResultValidation.Win(result));
    }

    [Fact]
    public void DiskProcessCancellation_StopsProcessAndPropagatesCancellation()
    {
        using var process = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var runner = typeof(PerformanceBenchmarkService).GetMethod("ReadDiskSpdOutput", BindingFlags.Static | BindingFlags.NonPublic)!;
        var error = Assert.Throws<TargetInvocationException>(() => runner.Invoke(null, [process, 1, cts.Token]));
        Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);
        Assert.True(process.WaitForExit(5000));
    }

    [Fact]
    public void TestSessions_CannotOverlap()
    {
        Assert.True(PerformanceBenchmarkService.TryBeginSession());
        try { Assert.False(PerformanceBenchmarkService.TryBeginSession()); }
        finally { PerformanceBenchmarkService.EndSession(); }
        Assert.False(PerformanceBenchmarkService.IsRunning);
    }
}
