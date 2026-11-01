using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services;

/// <summary>
/// 局域网设备发现：读取系统 ARP 表 + 一次 ICMP 探测（只读，不修改任何设备）。
/// 「踢下线」由路由器侧的 MAC 黑名单完成，这里是识别设备、给出入口。
/// </summary>
public static class LanDeviceService
{
    public const int MaxSweepHosts = 1024;
    private const int PingTimeoutMs = 400;
    private const int PingConcurrency = 64;
    private const int ErrorInsufficientBuffer = 122;
    private const uint ArpTypeDynamic = 3;
    private const uint ArpTypeStatic = 4;

    public static LanNetwork? GetActiveNetwork()
    {
        var preferredIndex = GetDefaultRouteInterfaceIndex();
        LanNetwork? fallback = null;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)) continue;

            IPInterfaceProperties properties;
            int interfaceIndex;
            try
            {
                properties = nic.GetIPProperties();
                interfaceIndex = properties.GetIPv4Properties()?.Index ?? 0;
            }
            catch { continue; }
            if (interfaceIndex == 0) continue;

            var unicast = properties.UnicastAddresses.FirstOrDefault(u =>
                u.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(u.Address) &&
                !u.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal));
            if (unicast is null) continue;

            var gateway = properties.GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));

            var network = new LanNetwork(
                nic.Name,
                unicast.Address,
                unicast.PrefixLength,
                gateway,
                interfaceIndex,
                nic.GetPhysicalAddress().GetAddressBytes());

            if (interfaceIndex == preferredIndex) return network;
            fallback ??= network;
        }

        return fallback;
    }

    public static async Task<LanScanResult> ScanAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var network = GetActiveNetwork() ?? throw new InvalidOperationException("没有可用的局域网连接，请先连接 WiFi 或网线。");
        var hosts = EnumerateHosts(network.LocalAddress, network.PrefixLength, MaxSweepHosts);
        if (hosts.Count == 0) throw new InvalidOperationException("当前网络地址无效，无法扫描。");

        progress?.Report($"正在探测 {network.SubnetLabel} 内的 {hosts.Count} 个地址…");
        await Parallel.ForEachAsync(
            hosts,
            new ParallelOptions { MaxDegreeOfParallelism = PingConcurrency, CancellationToken = cancellationToken },
            ProbeAsync);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("正在读取系统 ARP 表…");

        var entries = ReadArpTable();
        cancellationToken.ThrowIfCancellationRequested();

        var devices = BuildDevices(entries, network);
        return new LanScanResult
        {
            Network = network,
            Devices = devices,
            ProbedHosts = hosts.Count,
            Truncated = hosts.Count >= MaxSweepHosts
        };
    }

    /// <summary>反查主机名（后台进行，失败保持空字符串）。</summary>
    public static async Task ResolveHostNamesAsync(IEnumerable<LanDevice> devices, CancellationToken cancellationToken = default)
    {
        using var gate = new SemaphoreSlim(16);
        var tasks = devices.Where(d => !d.IsSelf).Select(async device =>
        {
            try
            {
                await gate.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) { return; }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(800);
                var entry = await Dns.GetHostEntryAsync(device.Ip).WaitAsync(timeout.Token);
                var name = entry.HostName?.Trim();
                if (!string.IsNullOrEmpty(name) && !name.Equals(device.Ip, StringComparison.OrdinalIgnoreCase))
                    device.HostName = name;
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            finally { gate.Release(); }
        });

        await Task.WhenAll(tasks);
    }

    private static async ValueTask ProbeAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            await ping.SendPingAsync(address, PingTimeoutMs).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { }
    }

    internal static List<LanDevice> BuildDevices(IEnumerable<ArpEntry> entries, LanNetwork network)
    {
        var mask = PrefixMask(network.PrefixLength);
        var networkValue = Ipv4ToUInt(network.LocalAddress) & mask;
        var gatewayValue = network.Gateway is null ? 0u : Ipv4ToUInt(network.Gateway);
        var result = new List<LanDevice>();
        var seen = new HashSet<uint>();

        var selfMac = FormatMac(network.PhysicalAddress);
        var selfVendor = LanOui.Lookup(selfMac);
        result.Add(new LanDevice
        {
            Ip = network.LocalAddress.ToString(),
            Mac = selfMac,
            Vendor = selfVendor,
            IsSelf = true,
            IsGateway = network.LocalAddress.Equals(network.Gateway),
            IsRandomMac = IsRandomMac(network.PhysicalAddress),
            AddressValue = Ipv4ToUInt(network.LocalAddress)
        });
        seen.Add(Ipv4ToUInt(network.LocalAddress));

        foreach (var entry in entries)
        {
            if (entry.PhysicalAddress.Length < 6) continue;
            if (entry.Type is not (ArpTypeDynamic or ArpTypeStatic)) continue;
            var value = Ipv4ToUInt(entry.Address);
            if ((value & mask) != networkValue) continue;
            if ((value & 0xFF) == 0xFF) continue;
            if ((value & 0xFF) == 0) continue;
            if (IsMulticast(entry.PhysicalAddress[0])) continue;
            if (!seen.Add(value)) continue;

            var mac = FormatMac(entry.PhysicalAddress);
            result.Add(new LanDevice
            {
                Ip = entry.Address.ToString(),
                Mac = mac,
                Vendor = LanOui.Lookup(mac),
                IsGateway = value == gatewayValue,
                IsRandomMac = IsRandomMac(entry.PhysicalAddress),
                AddressValue = value
            });
        }

        return result
            .OrderByDescending(d => d.IsSelf)
            .ThenByDescending(d => d.IsGateway)
            .ThenBy(d => d.AddressValue)
            .ToList();
    }

    #region 地址与 MAC 计算（纯函数，供测试）

    internal static uint Ipv4ToUInt(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    internal static IPAddress UIntToIpv4(uint value) =>
        new(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });

    /// <summary>ARP 表里的 dwAddr 按网络字节序存放在 DWORD 里（小端机读出来是反的）。</summary>
    internal static IPAddress FromNetworkDword(uint value) =>
        new(new[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24) });

    internal static uint PrefixMask(int prefixLength)
    {
        prefixLength = Math.Clamp(prefixLength, 0, 32);
        return prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
    }

    internal static List<IPAddress> EnumerateHosts(IPAddress address, int prefixLength, int maxHosts)
    {
        var result = new List<IPAddress>();
        if (address.AddressFamily != AddressFamily.InterNetwork || maxHosts <= 0) return result;

        var mask = PrefixMask(prefixLength);
        var value = Ipv4ToUInt(address);
        var network = value & mask;
        var broadcast = network | ~mask;
        var first = prefixLength >= 31 ? network : network + 1;
        var last = prefixLength >= 31 ? broadcast : broadcast - 1;
        if (first > last) return result;

        for (var current = first; ; current++)
        {
            result.Add(UIntToIpv4(current));
            if (current == last || result.Count >= maxHosts) break;
        }

        return result;
    }

    internal static string FormatMac(byte[] bytes) =>
        bytes.Length == 0 ? "" : string.Join(":", bytes.Select(b => b.ToString("X2")));

    internal static bool IsMulticast(byte firstOctet) => (firstOctet & 0x01) != 0;

    /// <summary>本地管理位 = 1 通常意味着系统随机化了 MAC（Android/iOS 的私有 WiFi 地址）。</summary>
    internal static bool IsRandomMac(byte[] bytes) => bytes.Length >= 6 && (bytes[0] & 0x02) != 0;

    #endregion

    #region Win32 互操作

    [StructLayout(LayoutKind.Sequential)]
    private struct MibIpnetrow
    {
        public uint InterfaceIndex;
        public uint PhysicalAddressLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] PhysicalAddress;
        public uint Address;
        public uint Type;
    }

    internal readonly record struct ArpEntry(IPAddress Address, byte[] PhysicalAddress, uint Type, uint InterfaceIndex);

    internal static List<ArpEntry> ReadArpTable()
    {
        var result = new List<ArpEntry>();
        var size = 0;
        GetIpNetTable(IntPtr.Zero, ref size, false);
        if (size <= 0) return result;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var error = GetIpNetTable(buffer, ref size, false);
                if (error == ErrorInsufficientBuffer) continue;
                if (error != 0) return result;

                var count = Marshal.ReadInt32(buffer);
                var rowSize = Marshal.SizeOf<MibIpnetrow>();
                var pointer = IntPtr.Add(buffer, 4);
                for (var i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<MibIpnetrow>(pointer);
                    if (row.PhysicalAddressLength is > 0 and <= 8 && row.PhysicalAddress is not null)
                    {
                        var mac = new byte[row.PhysicalAddressLength];
                        Array.Copy(row.PhysicalAddress, mac, mac.Length);
                        // dwAddr 按网络字节序存放，小端机读成 uint 是反的，必须换回来再比较。
                        result.Add(new ArpEntry(FromNetworkDword(row.Address), mac, row.Type, row.InterfaceIndex));
                    }
                    pointer = IntPtr.Add(pointer, rowSize);
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        return result;
    }

    private static int GetDefaultRouteInterfaceIndex()
    {
        // 8.8.8.8 的网络字节序值与主机字节序值相同（0x08080808），无需转换。
        return GetBestInterface(0x08080808u, out var index) == 0 ? (int)index : 0;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetIpNetTable(IntPtr pIpNetTable, ref int pdwSize, [MarshalAs(UnmanagedType.Bool)] bool bOrder);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetBestInterface(uint destAddr, out uint bestIfIndex);

    #endregion
}

public sealed class LanNetwork(
    string interfaceName,
    IPAddress localAddress,
    int prefixLength,
    IPAddress? gateway,
    int interfaceIndex,
    byte[] physicalAddress)
{
    public string InterfaceName { get; } = interfaceName;
    public IPAddress LocalAddress { get; } = localAddress;
    public int PrefixLength { get; } = prefixLength;
    public IPAddress? Gateway { get; } = gateway;
    public int InterfaceIndex { get; } = interfaceIndex;
    public byte[] PhysicalAddress { get; } = physicalAddress;

    public string SubnetLabel
    {
        get
        {
            var mask = LanDeviceService.PrefixMask(PrefixLength);
            return $"{LanDeviceService.UIntToIpv4(LanDeviceService.Ipv4ToUInt(LocalAddress) & mask)}/{PrefixLength}";
        }
    }
}

public sealed class LanScanResult
{
    public required LanNetwork Network { get; init; }
    public required IReadOnlyList<LanDevice> Devices { get; init; }
    public int ProbedHosts { get; init; }
    public bool Truncated { get; init; }
}

public sealed class LanDevice : INotifyPropertyChanged
{
    private string _hostName = "";

    public required string Ip { get; init; }
    public required string Mac { get; init; }
    public string Vendor { get; init; } = "";
    public bool IsSelf { get; init; }
    public bool IsGateway { get; init; }
    public bool IsRandomMac { get; init; }
    public uint AddressValue { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string HostName
    {
        get => _hostName;
        set
        {
            if (_hostName == value) return;
            _hostName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HostName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }

    public string DisplayName => !string.IsNullOrWhiteSpace(HostName)
        ? HostName
        : Vendor.Length > 0 ? Vendor : "未知设备";

    public string Endpoint => $"{Ip} · {Mac}";

    public string Badge => IsSelf ? "本机" : IsGateway ? "网关" : IsRandomMac ? "随机 MAC" : "";

    public string EndpointLabel => Badge.Length == 0 ? Endpoint : $"{Endpoint} · {Badge}";

    public string IconGlyph => IsGateway ? "\uE968" : IsSelf ? "\uE7F4" : "\uE8B7";

    public bool CanKick => !IsSelf && !IsGateway;
}

/// <summary>常见厂商 OU 前缀（本地精简表，未知前缀保持空白，不做猜测）。</summary>
internal static class LanOui
{
    private static readonly Dictionary<string, string> Vendors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["00:05:69"] = "VMware",
        ["00:0C:29"] = "VMware",
        ["00:50:56"] = "VMware",
        ["08:00:27"] = "Oracle VirtualBox",
        ["00:15:5D"] = "Microsoft",
        ["00:1C:42"] = "Parallels",
        ["00:16:3E"] = "Xen",
        ["52:54:00"] = "QEMU",
        ["B8:27:EB"] = "树莓派",
        ["DC:A6:32"] = "树莓派",
        ["E4:5F:01"] = "树莓派",
        ["24:0A:C4"] = "乐鑫科技",
        ["3C:71:BF"] = "乐鑫科技",
        ["AC:67:B2"] = "乐鑫科技",
        ["84:CC:A8"] = "乐鑫科技",
        ["00:E0:4C"] = "瑞昱 Realtek",
        ["00:1B:21"] = "英特尔 Intel",
        ["8C:16:45"] = "英特尔 Intel",
        ["A4:BB:6D"] = "英特尔 Intel",
        ["00:1B:63"] = "苹果 Apple",
        ["3C:22:FB"] = "苹果 Apple",
        ["8C:85:90"] = "苹果 Apple",
        ["F0:18:98"] = "苹果 Apple",
        ["00:50:F2"] = "微软 Microsoft",
        ["00:12:5A"] = "微软 Microsoft",
        ["28:18:78"] = "微软 Microsoft",
        ["00:1A:11"] = "谷歌 Google",
        ["3C:5A:B4"] = "谷歌 Google",
        ["F4:F5:D8"] = "谷歌 Google",
        ["2C:56:DC"] = "华硕 ASUS",
        ["AC:22:0B"] = "华硕 ASUS",
        ["00:1D:0F"] = "TP-LINK",
        ["50:C7:BF"] = "TP-LINK",
        ["A4:2B:B0"] = "TP-LINK",
        ["C8:3A:35"] = "腾达 Tenda",
        ["00:26:5A"] = "D-Link",
        ["00:0F:E2"] = "新华三 H3C",
        ["00:E0:FC"] = "华为 Huawei",
        ["64:09:80"] = "小米 Xiaomi",
        ["F4:8E:38"] = "戴尔 Dell",
        ["00:1A:A0"] = "戴尔 Dell",
        ["1C:1B:0D"] = "技嘉 GIGABYTE",
        ["B4:2E:99"] = "技嘉 GIGABYTE",
        ["98:B6:E9"] = "任天堂 Nintendo",
        ["58:BD:A3"] = "任天堂 Nintendo"
    };

    public static string Lookup(string mac) =>
        mac.Length >= 8 && Vendors.TryGetValue(mac[..8], out var vendor) ? vendor : "";
}
