using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class HardwarePortTests
{
    [Theory]
    [InlineData(16, "USB")]
    [InlineData(28, "Video")]
    [InlineData(29, "Audio")]
    [InlineData(31, "Network")]
    [InlineData(255, "Connector")]
    public void FirmwarePort_UsesWmiMapping(int type, string expected) =>
        Assert.Equal(expected, HardwarePortService.FirmwarePortKind((ushort)type));

    [Fact]
    public void Usb3Port_WithSlowDevice_SeparatesCapabilityFromConnection()
    {
        var port = new HardwarePortInfo("Hub 1 / Port 2", "USB", "USB Hub",
            Protocols: 6, Speed: 1, ConnectionStatus: 1, TypeC: true);
        var text = HardwarePortService.Describe(port);
        Assert.Contains("USB-C", text);
        Assert.Contains("USB 3.x", text);
        Assert.Contains("12 Mb/s", text);
        Assert.DoesNotContain("20 Gb/s", text);
    }

    [Fact]
    public void DisconnectedPort_DoesNotReportDefaultLowSpeed()
    {
        var text = HardwarePortService.Describe(new("Port 1", "USB", "USB Hub", Speed: 0, ConnectionStatus: 0));
        Assert.DoesNotContain("1.5 Mb/s", text);
        Assert.DoesNotContain("USB-C", text);
    }

    [Fact]
    public void SuperSpeedPlus_DoesNotAssumeExactLaneRate()
    {
        Assert.Contains("10 Gb/s", HardwarePortService.UsbSpeed(3, 4));
        Assert.NotEqual("10 Gb/s", HardwarePortService.UsbSpeed(3, 4));
        Assert.Equal("SuperSpeed · 5 Gb/s", HardwarePortService.UsbSpeed(2, 1));
    }

    [Fact]
    public void MissingUsbProtocol_DoesNotAssumeUsb2() =>
        Assert.DoesNotContain("USB 2.0", HardwarePortService.UsbProtocols(0));

    [Fact]
    public void EmptyInventory_ExplainsMissingData() =>
        Assert.False(string.IsNullOrWhiteSpace(HardwarePortService.BuildSummary([])));

    [Fact]
    public void Normalize_RemovesInternalDownstreamAndDisplayEntries()
    {
        HardwarePortInfo[] ports =
        [
            new("internal", "USB", "USB Hub", UserConnectable: false, IsRootHub: true),
            new("unknown", "USB", "USB Hub", UserConnectable: null, IsRootHub: true),
            new("dock", "USB", "USB Hub", UserConnectable: true, IsRootHub: false),
            new("monitor", "HDMI", "Display"), new("firmware video", "Video", "SMBIOS"),
            new("external", "USB", "USB Hub", UserConnectable: true, IsRootHub: true)
        ];
        Assert.Equal("external", Assert.Single(HardwarePortService.Normalize(ports)).Name);
    }

    [Fact]
    public void Normalize_MergesCompanionPortsAndPreservesActiveSpeed()
    {
        HardwarePortInfo[] ports =
        [
            new("usb2", "USB", "USB Hub", Protocols: 3, Speed: 2, ConnectionStatus: 1,
                UserConnectable: true, UsbKey: "HUB/1", CompanionKeys: ["HUB/2"], IsRootHub: true),
            new("usb3", "USB", "USB Hub", Protocols: 4, ConnectionStatus: 0, TypeC: true,
                UserConnectable: true, UsbKey: "HUB/2", CompanionKeys: ["HUB/1"], IsRootHub: true)
        ];
        var port = Assert.Single(HardwarePortService.Normalize(ports));
        Assert.Equal(7u, port.Protocols);
        Assert.True(port.TypeC);
        Assert.Equal(2, port.Speed);
        Assert.Equal(1, port.ConnectionStatus);
    }

    [Fact]
    public void Summary_AggregatesCapabilitiesWithoutReportingSocketCounts()
    {
        var ports = Enumerable.Range(1, 32).Select(i => new HardwarePortInfo($"Hub / Port {i}", "USB", "USB Hub",
            Protocols: 7, TypeC: i % 2 == 0)).ToList();
        var summary = HardwarePortService.BuildSummary(ports);
        Assert.Contains("USB-C", summary);
        Assert.Contains("5 Gb/s", summary);
        Assert.DoesNotContain("32", summary);
        Assert.DoesNotContain("Hub", summary);
        Assert.DoesNotContain("Port", summary);
    }
}
