using System.Reflection;
using System.Runtime.InteropServices;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class HardwareSummaryTests
{
    [Fact]
    public void DisplayMode_UsesCompleteNativeLayout()
    {
        var mode = typeof(HardwareInfoService).GetNestedType("DEVMODE", BindingFlags.NonPublic)!;
        Assert.Equal(220, Marshal.SizeOf(mode));
        Assert.Equal(184, Marshal.OffsetOf(mode, "dmDisplayFrequency").ToInt32());
    }

    [Theory]
    [InlineData("Windows 11 家庭版 中文版", "26H2", "26300.9711", "Windows 11 家庭版 中文版 (26H2) | 26300.9711")]
    [InlineData("Windows 11 专业版", null, "26100", "Windows 11 专业版 | 26100")]
    [InlineData("Windows 11", "24H2", null, "Windows 11 (24H2)")]
    public void WindowsSummary_CombinesDetailFields(string name, string? release, string? build, string expected)
        => Assert.Equal(expected, HardwareInfoService.FormatWindowsSummary(name, release, build));

    [Theory]
    [InlineData(null, "未能读取")]
    [InlineData(42.5f, "42.5 °C")]
    [InlineData(0f, "0 °C")]
    [InlineData(255f, "未能读取")]
    [InlineData(float.NaN, "未能读取")]
    public void DiskTemperature_DoesNotRenderInvalidSensorValues(float? value, string expected)
        => Assert.Equal(expected, HardwareInfoService.FormatDiskTemperature(value));

    [Theory]
    [InlineData(null, 42, 42f)]
    [InlineData(float.NaN, 39, 39f)]
    [InlineData(255f, 41, 41f)]
    [InlineData(35f, 40, 35f)]
    [InlineData(null, 255, null)]
    [InlineData(null, null, null)]
    public void DiskTemperature_UsesSmartWhenSensorIsUnavailable(float? sensor, int? smart, float? expected)
        => Assert.Equal(expected, HardwareInfoService.ResolveDiskTemperature(sensor, smart));

    [Fact]
    public void DiskTemperature_DoesNotChooseAnAmbiguousModel()
    {
        var temperatures = new Dictionary<string, float> { ["SSD 1TB"] = 31, ["SSD 2TB"] = 42 };
        Assert.Equal(31f, HardwareInfoService.FindDiskTemperature("SSD 1TB", temperatures));
        Assert.Null(HardwareInfoService.FindDiskTemperature("SSD", temperatures));
        Assert.Null(HardwareInfoService.FindDiskTemperature("Other SSD", temperatures));
    }

    [Fact]
    public void DiskDetailSnapshot_ReplacesStaleUnknownTemperatureWithoutMutatingDisplayedRows()
    {
        var source = new List<HardwareInfoSection>
        {
            new() { Title = "型号", Glyph = "" }, new() { Title = "系统", Glyph = "" },
            new() { Title = "硬件", Glyph = "" }
        };
        source[2].Items.Add(new HardwareInfoItem { Label = "硬盘", Value = "SSD · 温度：未能读取" });
        var updated = HardwareInfoService.ApplyDiskDetailSnapshot(source,
            new List<DiskDetail> { new() { Model = "SSD", Size = "953.9 GB", Temperature = 42 } });
        Assert.Equal("SSD (953.9 GB) · 温度：42 °C", updated[2].Items[0].Value);
        Assert.Equal("SSD · 温度：未能读取", source[2].Items[0].Value);
        Assert.Single(updated[2].Items[0].DeviceValues);
    }

    [Fact]
    public void DiskDevices_KeepEachDriveAndItsTemperatureTogether()
    {
        var values = HardwareInfoService.BuildDiskDeviceValues(new List<DiskDetail>
        {
            new() { Model = "SSD / Pro", Size = "953.9 GB", Temperature = 35 },
            new() { Model = "SSD / Pro", Size = "1907.8 GB", Temperature = 33 }
        });
        Assert.Equal(2, values.Count);
        Assert.Equal("SSD / Pro (953.9 GB) · 温度：35 °C", values[0]);
        Assert.Equal("SSD / Pro (1907.8 GB) · 温度：33 °C", values[1]);
    }

    [Fact]
    public void CpuzMemoryDevices_SplitDimmsButKeepSolderedLpddrTogether()
    {
        var cpuz = new CpuzInfo { MemoryType = "DDR5", MemDevices =
            [new() { Size = "16 GB", PartNumber = "Module A" }, new() { Size = "16 GB", PartNumber = "Module B" }] };
        var dimms = HardwareInfoService.BuildCpuzMemoryDevices(cpuz, "32 GB DDR5");
        Assert.Equal(2, dimms.Count);
        Assert.Contains("Module A", dimms[0]);
        Assert.Contains("Module B", dimms[1]);
        cpuz.MemoryType = "LPDDR5";
        Assert.Equal("32 GB LPDDR5", Assert.Single(HardwareInfoService.BuildCpuzMemoryDevices(cpuz, "32 GB LPDDR5")));
    }

    [Fact]
    public void CpuzGpuOverride_PreservesIndependentCardsAndUnmodifiedSource()
    {
        var original = new List<HardwareInfoSection>
        {
            new() { Title = "型号", Glyph = "" }, new() { Title = "系统", Glyph = "" },
            new() { Title = "硬件", Glyph = "" }
        };
        original[2].Items.Add(new HardwareInfoItem { Label = "显卡", Value = "WMI GPU", DeviceValues = ["WMI GPU"] });
        original[2].Items.Add(new HardwareInfoItem { Label = "硬盘", Value = "Disk A / Disk B", DeviceValues = ["Disk A", "Disk B"] });
        var updated = HardwareInfoService.ApplyCpuzOverride(original,
            new CpuzInfo { Gpus = [new() { Name = "GPU A", MemorySize = "8 GB" }, new() { Name = "GPU B", MemorySize = "16 GB" }] });
        Assert.Equal(new[] { "GPU A (8 GB)", "GPU B (16 GB)" }, updated[2].Items[0].DeviceValues);
        Assert.Equal(new[] { "Disk A", "Disk B" }, updated[2].Items[1].DeviceValues);
        Assert.Equal("WMI GPU", Assert.Single(original[2].Items[0].DeviceValues));
    }

    [Fact]
    public void BatterySummary_DistinguishesUnavailableDataFromZeroCycles()
    {
        var battery = new BatteryDetail { Name = "Battery", DesignCapacity = "70.0 Wh", CycleCount = "0 次" };
        var text = HardwareInfoService.FormatBatterySummary(battery);
        Assert.Contains("设计容量：70.0 Wh", text);
        Assert.Contains("循环次数：0 次", text);
        Assert.Contains("满充容量：未能读取", text);
        Assert.Contains("健康度：未能读取", text);
        Assert.Contains("序列号：未能读取", text);
    }

    [Fact]
    public void BatterySummary_ShowsHealthWhenCapacitiesAvailable()
    {
        var battery = new BatteryDetail { Name = "Battery", DesignCapacity = "70.0 Wh",
            FullChargeCapacity = "63.0 Wh", Health = "90%" };
        var text = HardwareInfoService.FormatBatterySummary(battery);
        Assert.Contains("满充容量：63.0 Wh", text);
        Assert.Contains("健康度：90%", text);
    }

    [Fact]
    public void CpuzOverride_RetainsWmiCacheWithoutMutatingOriginal()
    {
        var original = new List<HardwareInfoSection>
        {
            new() { Title = "型号信息", Glyph = "" }, new() { Title = "系统信息", Glyph = "" },
            new() { Title = "详细信息", Glyph = "" }
        };
        original[2].Items.Add(new HardwareInfoItem { Label = "处理器", Value = "WMI CPU L3 缓存 32 MB", SupplementalValue = "L3 缓存 32 MB" });
        var result = HardwareInfoService.ApplyCpuzOverride(original, new CpuzInfo { CpuName = "AMD CPU", CpuCores = 8, CpuThreads = 16 });
        Assert.Contains("8C/16T", result[2].Items[0].Value);
        Assert.Contains("L3 缓存 32 MB", result[2].Items[0].Value);
        Assert.StartsWith("WMI", original[2].Items[0].Value);
    }
}
