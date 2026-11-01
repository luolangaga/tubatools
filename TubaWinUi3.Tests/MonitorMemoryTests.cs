using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public sealed class MonitorMemoryTests
{
    [Fact]
    public void SystemMemory_ProvidesValidMetricsWithoutInitializingSensors()
    {
        var sample = new MonitorSample();

        LiteMonitorService.ReadSystemMemory(sample);

        Assert.True(sample.MemTotalGB > 0);
        Assert.InRange(sample.MemUsedGB, 0, sample.MemTotalGB);
        Assert.InRange(sample.MemLoad, 0, 100);
        Assert.Equal(sample.MemUsedGB / sample.MemTotalGB * 100, sample.MemLoad, 3);
        Assert.Equal(-1, sample.CpuTemp);
    }

    [Fact]
    public void SystemMemory_PreservesAlreadyPopulatedSample()
    {
        var sample = new MonitorSample { MemTotalGB = 32, MemUsedGB = 8, MemLoad = 25 };

        LiteMonitorService.ReadSystemMemory(sample);

        Assert.Equal(32, sample.MemTotalGB);
        Assert.Equal(8, sample.MemUsedGB);
        Assert.Equal(25, sample.MemLoad);
    }
}
