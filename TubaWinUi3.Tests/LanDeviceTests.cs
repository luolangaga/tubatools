using System.Net;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class LanDeviceTests
{
    private static LanNetwork Network(
        string local = "192.168.1.10",
        int prefix = 24,
        string? gateway = "192.168.1.1",
        byte[]? mac = null) => new(
            "WLAN", IPAddress.Parse(local), prefix,
            gateway is null ? null : IPAddress.Parse(gateway), 12,
            mac ?? [0x00, 0x11, 0x22, 0x33, 0x44, 0x55]);

    private static LanDeviceService.ArpEntry Entry(string ip, string mac, uint type = 3, uint index = 12) =>
        new(
            IPAddress.Parse(ip),
            mac.Split(':').Select(x => Convert.ToByte(x, 16)).ToArray(),
            type,
            index);

    [Fact]
    public void EnumeratesSlash24WithoutNetworkAndBroadcast()
    {
        var hosts = LanDeviceService.EnumerateHosts(IPAddress.Parse("192.168.1.10"), 24, 1024);
        Assert.Equal(254, hosts.Count);
        Assert.Equal("192.168.1.1", hosts[0].ToString());
        Assert.Equal("192.168.1.254", hosts[^1].ToString());
    }

    [Fact]
    public void EnumeratesSmallPrefixes()
    {
        var slash31 = LanDeviceService.EnumerateHosts(IPAddress.Parse("10.0.0.2"), 31, 1024);
        Assert.Equal(new[] { "10.0.0.2", "10.0.0.3" }, slash31.Select(h => h.ToString()));

        var slash32 = LanDeviceService.EnumerateHosts(IPAddress.Parse("10.0.0.9"), 32, 1024);
        Assert.Equal("10.0.0.9", Assert.Single(slash32).ToString());
    }

    [Fact]
    public void EnumerateHonoursHostCap()
    {
        var hosts = LanDeviceService.EnumerateHosts(IPAddress.Parse("10.10.5.7"), 16, 100);
        Assert.Equal(100, hosts.Count);
        Assert.Equal("10.10.0.1", hosts[0].ToString());
        Assert.Equal("10.10.0.100", hosts[^1].ToString());
    }

    [Fact]
    public void AddressConversionRoundTrips()
    {
        var address = IPAddress.Parse("172.16.254.3");
        Assert.Equal(address, LanDeviceService.UIntToIpv4(LanDeviceService.Ipv4ToUInt(address)));
        Assert.Equal(0xAC10FE03u, LanDeviceService.Ipv4ToUInt(address));
    }

    [Fact]
    public void ArpDwordIsReadInLittleEndianMemoryOrder()
    {
        // 192.168.1.1 在 ARP 行里按网络字节序存放（内存 [C0 A8 01 01]），
        // 小端机把它当 uint 读出来是 0x0101A8C0。
        Assert.Equal("192.168.1.1", LanDeviceService.FromNetworkDword(0x0101A8C0).ToString());
        Assert.Equal("10.0.0.9", LanDeviceService.FromNetworkDword(0x0900000A).ToString());
    }

    [Fact]
    public void FormatsMacInUpperColonNotation() =>
        Assert.Equal("0A:1B:2C:3D:4E:5F", LanDeviceService.FormatMac([0x0A, 0x1B, 0x2C, 0x3D, 0x4E, 0x5F]));

    [Fact]
    public void DetectsRandomisedAndMulticastMacs()
    {
        Assert.True(LanDeviceService.IsRandomMac([0x02, 0, 0, 0, 0, 1]));
        Assert.True(LanDeviceService.IsRandomMac([0xA6, 0, 0, 0, 0, 1]));
        Assert.False(LanDeviceService.IsRandomMac([0x00, 0, 0, 0, 0, 1]));
        Assert.False(LanDeviceService.IsRandomMac([0xFC, 0, 0, 0, 0, 1]));
        Assert.False(LanDeviceService.IsRandomMac([]));

        Assert.True(LanDeviceService.IsMulticast(0x01));
        Assert.False(LanDeviceService.IsMulticast(0x02));
    }

    [Fact]
    public void SubnetLabelMasksThePrefix()
    {
        Assert.Equal("192.168.1.0/24", Network().SubnetLabel);
        Assert.Equal("10.20.0.0/16", Network("10.20.30.40", 16).SubnetLabel);
    }

    [Fact]
    public void BuildDevicesKeepsSameSubnetAddressesAndSortsThem()
    {
        var devices = LanDeviceService.BuildDevices(
        [
            Entry("192.168.1.20", "00:11:22:33:44:66"),
            Entry("192.168.1.1", "00:11:22:33:44:77", type: 4),
            Entry("10.0.0.9", "00:11:22:33:44:88"),
            Entry("192.168.1.255", "00:11:22:33:44:99"),
            Entry("192.168.1.30", "01:00:5E:00:00:01"),
            Entry("192.168.1.40", "00:11:22:33:44:AA", type: 2),
            Entry("192.168.1.10", "00:11:22:33:44:BB")
        ], Network());

        Assert.Equal(new[] { "192.168.1.10", "192.168.1.1", "192.168.1.20" }, devices.Select(d => d.Ip));
        Assert.True(devices[0].IsSelf);
        Assert.True(devices[1].IsGateway);
        Assert.False(devices[2].IsGateway);
        Assert.Equal(1, devices.Count(d => d.CanKick));
    }

    [Fact]
    public void DevicesWithoutGatewayStillWork()
    {
        var devices = LanDeviceService.BuildDevices([Entry("192.168.1.20", "00:11:22:33:44:66")], Network(gateway: null));
        Assert.Equal(2, devices.Count);
        Assert.DoesNotContain(devices, d => d.IsGateway);
    }

    [Fact]
    public void BadgePrefersRoleOverRandomMac()
    {
        var self = new LanDevice { Ip = "192.168.1.10", Mac = "02:00:00:00:00:01", IsSelf = true, IsRandomMac = true };
        var gateway = new LanDevice { Ip = "192.168.1.1", Mac = "02:00:00:00:00:02", IsGateway = true, IsRandomMac = true };
        var phone = new LanDevice { Ip = "192.168.1.20", Mac = "02:00:00:00:00:03", IsRandomMac = true, Vendor = "" };
        var unknown = new LanDevice { Ip = "192.168.1.21", Mac = "00:00:00:00:00:04" };

        Assert.Equal("本机", self.Badge);
        Assert.Equal("网关", gateway.Badge);
        Assert.Equal("随机 MAC", phone.Badge);
        Assert.Equal("", unknown.Badge);
        Assert.False(self.CanKick);
        Assert.False(gateway.CanKick);
        Assert.True(phone.CanKick);
        Assert.Equal("未知设备", unknown.DisplayName);
        Assert.Equal("192.168.1.21 · 00:00:00:00:00:04", unknown.EndpointLabel);
    }

    [Fact]
    public void HostNameUpdatesDisplayName()
    {
        var device = new LanDevice { Ip = "192.168.1.20", Mac = "00:11:22:33:44:66" };
        string? changed = null;
        device.PropertyChanged += (_, e) => changed ??= e.PropertyName;
        device.HostName = "客厅电视";
        Assert.Equal("客厅电视", device.DisplayName);
        Assert.Equal(nameof(LanDevice.HostName), changed);
    }

    [Fact]
    public void VendorLookupUsesOuiPrefixOnly()
    {
        Assert.Equal("树莓派", LanOui.Lookup("B8:27:EB:01:02:03"));
        Assert.Equal("树莓派", LanOui.Lookup("b8:27:eb:aa:bb:cc"));
        Assert.Equal("", LanOui.Lookup("B8:27:EC:01:02:03"));
        Assert.Equal("", LanOui.Lookup(""));
    }
}
