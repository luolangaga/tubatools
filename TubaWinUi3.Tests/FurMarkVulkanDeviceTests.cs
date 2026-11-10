using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

public sealed class FurMarkVulkanDeviceTests
{
    private const string GpuInfoOutput = """
        [ GPU info ]
        GPU 0:
        - deviceID: 8086-7D67
        - bios: 26.0.61
        - memory: 18347MB
        - driver: 32.0.101.9150
        GPU 1:
        - deviceID: 10DE-2D58
        - memory: 8151MB
        - driver: 32.0.15.6094
        """;

    private const string VulkanInfoOutput = """
        [ Vulkan info ]
        - # of devices: 2
        - [device 0]
         - name: NVIDIA GeForce RTX 5070 Laptop GPU
         - deviceID: 10DE-2D58 (4318-11608)
         - API version: 1.4.312
        - [device 1]
         - name: Intel(R) Graphics
         - deviceID: 8086-7D67 (32902-32103)
         - API version: 1.4.312
        """;

    [Fact]
    public void ParseFurMarkVulkanNames_PreservesIndicesWhenModelsAreIdentical()
    {
        var devices = PerformanceBenchmarkService.ParseFurMarkVulkanNames("""
            [ Vulkan info ]
            - # of devices: 3
            - [device 0]
             - name: Intel(R) Arc(TM) B390 GPU
             - deviceID: 8086-B080
            - [device 1]
            - [device 2]
             - name: Intel(R) Arc(TM) B390 GPU
            """);

        Assert.Equal(2, devices.Count);
        Assert.Equal("Intel(R) Arc(TM) B390 GPU", devices[0]);
        Assert.Equal("Intel(R) Arc(TM) B390 GPU", devices[2]);
    }

    [Fact]
    public void MergeFurMarkGpuInfo_PairsDetailsByDeviceIdInsteadOfIndex()
    {
        var gpus = PerformanceBenchmarkService.MergeFurMarkGpuInfo(GpuInfoOutput, VulkanInfoOutput);

        Assert.Equal(2, gpus.Count);

        Assert.Equal(0, gpus[0].Index);
        Assert.Equal("NVIDIA GeForce RTX 5070 Laptop GPU", gpus[0].Name);
        Assert.Equal("10DE-2D58", gpus[0].DeviceId);
        Assert.Equal("8151MB", gpus[0].Memory);
        Assert.Equal("32.0.15.6094", gpus[0].Driver);

        Assert.Equal(1, gpus[1].Index);
        Assert.Equal("Intel(R) Graphics", gpus[1].Name);
        Assert.Equal("8086-7D67", gpus[1].DeviceId);
        Assert.Equal("18347MB", gpus[1].Memory);
        Assert.Equal("32.0.101.9150", gpus[1].Driver);
    }

    [Fact]
    public void MergeFurMarkGpuInfo_DoesNotBorrowDetailsFromAnotherAdapter()
    {
        var gpus = PerformanceBenchmarkService.MergeFurMarkGpuInfo("""
            [ GPU info ]
            GPU 0:
            - deviceID: 8086-B080
            - memory: 18413MB
            - driver: 32.0.101.8974
            """, """
            [ Vulkan info ]
            - # of devices: 1
            - [device 0]
             - name: NVIDIA GeForce RTX 5070 Laptop GPU
             - deviceID: 10DE-2D58 (4318-11608)
            """);

        var gpu = Assert.Single(gpus);
        Assert.Equal("NVIDIA GeForce RTX 5070 Laptop GPU", gpu.Name);
        Assert.Equal("10DE-2D58", gpu.DeviceId);
        Assert.Equal("", gpu.Memory);
        Assert.Equal("", gpu.Driver);
    }

    [Fact]
    public void MergeFurMarkGpuInfo_FallsBackToGpuInfoWhenVulkanIsUnavailable()
    {
        var gpus = PerformanceBenchmarkService.MergeFurMarkGpuInfo(GpuInfoOutput, "");

        Assert.Equal(2, gpus.Count);
        Assert.Equal("GPU 0", gpus[0].Name);
        Assert.Equal("8086-7D67", gpus[0].DeviceId);
        Assert.Equal("18347MB", gpus[0].Memory);
        Assert.Equal("GPU 1", gpus[1].Name);
        Assert.Equal("10DE-2D58", gpus[1].DeviceId);
        Assert.Equal("8151MB", gpus[1].Memory);
    }

    [Fact]
    public void MergeFurMarkGpuInfo_PairsByIndexWhenVulkanHasNoDeviceId()
    {
        var gpus = PerformanceBenchmarkService.MergeFurMarkGpuInfo(GpuInfoOutput, """
            [ Vulkan info ]
            - # of devices: 2
            - [device 0]
             - name: NVIDIA GeForce RTX 5070 Laptop GPU
            - [device 1]
             - name: Intel(R) Graphics
            """);

        Assert.Equal(2, gpus.Count);
        Assert.Equal("18347MB", gpus[0].Memory);
        Assert.Equal("8151MB", gpus[1].Memory);
    }
}
