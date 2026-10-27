using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class ToolExperienceTests
{
    [Fact]
    public void IntegratedGpuShowsSharedLimitAndReservedMemory()
    {
        var text = HardwareDisplayPolicy.FormatGpuMemory(128UL * 1024 * 1024, 16UL * 1024 * 1024 * 1024);
        Assert.Contains("共享上限 16 GB", text); Assert.Contains("预留 128 MB", text);
        Assert.DoesNotContain("0.1 GB", text);
    }
    [Theory]
    [InlineData(9600, 128, 153.6)]   // LPDDR5X-9600，8×16-bit
    [InlineData(5600, 128, 89.6)]    // DDR5-5600，双通道
    [InlineData(3200, 64, 25.6)]     // DDR4-3200，单通道
    [InlineData(0, 128, 0)]
    [InlineData(9600, 0, 0)]
    public void TheoreticalBandwidthFollowsTransferRateAndBusWidth(int transferRateMt, int busBits, double expected)
        => Assert.Equal(expected, HardwareDisplayPolicy.ComputeMemoryBandwidthGbs(transferRateMt, busBits), 3);
    [Fact]
    public void DiscreteGpuShowsDedicatedCapacity()
        => Assert.Equal("专用 8 GB", HardwareDisplayPolicy.FormatGpuMemory(8UL * 1024 * 1024 * 1024, 16UL * 1024 * 1024 * 1024));
    [Fact]
    public void MissingGpuMemoryIsNotZero() => Assert.Equal("显存未能读取", HardwareDisplayPolicy.FormatGpuMemory(0, 0));
    [Theory]
    [InlineData("IddDesk Device", true)]
    [InlineData("Intel Arc GPU", false)]
    [InlineData(null, false)]
    public void VirtualAdaptersAreConsistentlyIdentified(string? name, bool expected)
        => Assert.Equal(expected, HardwareDisplayPolicy.IsVirtualAdapter(name));
    [Fact]
    public void LpddrDevicesDoNotClaimReplaceableSlots()
    {
        var text = HardwareDisplayPolicy.FormatMemoryDevices(8, 8, 35);
        Assert.Contains("板载", text); Assert.DoesNotContain("8/8", text);
    }
    [Fact]
    public void Ddr5DoesNotClaimSolderedMemory() => Assert.DoesNotContain("板载", HardwareDisplayPolicy.FormatMemoryDevices(2, 4, 34));
    [Theory]
    [InlineData("tool.exe")]
    [InlineData("tool.lnk")]
    [InlineData("tool.ps1")]
    public void ExternalToolsHaveGlyphWhileIconIsLoading(string path)
        => Assert.False(string.IsNullOrWhiteSpace(ToolIconService.GetIconGlyph(path)));
    [Theory]
    [InlineData("显卡工具")]
    [InlineData("未知分类")]
    public void DownloadPlaceholdersHaveCategoryFallback(string category)
        => Assert.False(string.IsNullOrWhiteSpace(ToolIconService.GetCategoryGlyph(category)));
}
