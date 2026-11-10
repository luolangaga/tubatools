using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>只读盘点。USB hub 逻辑端口不等于机身插孔，控制器名称不用于猜测端口版本。</summary>
public static class HardwarePortService
{
    public static List<HardwarePortInfo> Read()
    {
        var result = new List<HardwarePortInfo>();
        TryRead(() => ReadUsb(result));
        Query("root\\cimv2", "SELECT * FROM Win32_PortConnector", row =>
        {
            var name = Convert.ToString(row["ExternalReferenceDesignator"]);
            if (string.IsNullOrWhiteSpace(name)) return;
            var kind = FirmwarePortKind(Convert.ToUInt16(row["PortType"]));
            result.Add(new(name, kind, "SMBIOS"));
        });
        // 控制器存在只能确认控制器，不能推断插孔数量、Thunderbolt 代数或 PD 功率。
        Query("root\\cimv2", "SELECT Name FROM Win32_PnPEntity WHERE Present = TRUE", row =>
        {
            var name = Convert.ToString(row["Name"]) ?? "";
            if (name.Contains("Thunderbolt", StringComparison.OrdinalIgnoreCase)
                || name.Contains("USB4", StringComparison.OrdinalIgnoreCase))
                result.Add(new(name, name.Contains("Thunderbolt", StringComparison.OrdinalIgnoreCase) ? "Thunderbolt" : "USB4", "PnP"));
        });
        TryRead(() =>
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet
                    || !IsPhysicalNic(nic.Id)) continue;
                result.Add(new(nic.Description, "Ethernet", "Network",
                    LinkSpeed: nic.OperationalStatus == OperationalStatus.Up && nic.Speed > 0 ? nic.Speed : null));
            }
        });
        return Normalize(result);
    }

    private static bool IsPhysicalNic(string id)
    {
        var physical = false;
        Query("root\\cimv2", "SELECT GUID, PNPDeviceID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE", row =>
        {
            var pnpId = Convert.ToString(row["PNPDeviceID"]) ?? "";
            physical |= string.Equals(Convert.ToString(row["GUID"]), id, StringComparison.OrdinalIgnoreCase)
                && (pnpId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)
                    || pnpId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)
                    || pnpId.StartsWith("ACPI\\", StringComparison.OrdinalIgnoreCase));
        });
        return physical;
    }

    internal static string FirmwarePortKind(ushort value) => value switch
    {
        >= 1 and <= 5 => "Parallel", >= 6 and <= 9 => "Serial", 10 => "SCSI", 11 => "MIDI",
        12 => "Joystick", 13 => "Keyboard", 14 => "Mouse", 15 => "SSA", 16 => "USB",
        17 => "IEEE 1394", >= 18 and <= 20 => "PCMCIA", 21 => "CardBus", 22 => "Access Bus",
        23 or 24 => "SCSI", 28 => "Video", 29 => "Audio", 30 => "Modem", 31 => "Network",
        32 or 33 => "Serial",
        _ => "Connector"
    };

    private static string L(string key, string fallback) => LocalizationService.L("HwPorts_" + key, fallback);

    public static string Hint => L("Hint", "已排除内置 USB 设备和外接集线器，并合并 USB 2/3 配对端口。系统报告可能包含未接线端口，因此只展示接口能力，不将逻辑端口数当作机身插孔数。速率为标准信号速率，非文件传输速度。");

    /// <summary>内部端口和外接 hub 不进入整机能力摘要；USB2/3 companion 以连通分量合并。</summary>
    internal static List<HardwarePortInfo> Normalize(IReadOnlyList<HardwarePortInfo> ports)
    {
        var usb = ports.Where(p => p.Source == "USB Hub" && p.UserConnectable == true && p.IsRootHub).ToList();
        var remaining = new HashSet<HardwarePortInfo>(usb);
        var merged = new List<HardwarePortInfo>();
        while (remaining.Count > 0)
        {
            var group = new List<HardwarePortInfo> { remaining.First() };
            remaining.Remove(group[0]);
            for (var i = 0; i < group.Count; i++)
            {
                foreach (var candidate in remaining.ToArray())
                {
                    var linked = candidate.UsbKey is not null && group[i].CompanionKeys?.Contains(candidate.UsbKey, StringComparer.OrdinalIgnoreCase) == true
                        || group[i].UsbKey is not null && candidate.CompanionKeys?.Contains(group[i].UsbKey, StringComparer.OrdinalIgnoreCase) == true;
                    if (linked) { group.Add(candidate); remaining.Remove(candidate); }
                }
            }
            var active = group.Where(p => p.ConnectionStatus == 1)
                .OrderByDescending(p => (p.SpeedFlags & 4) != 0 ? 100 : (p.SpeedFlags & 1) != 0 ? 50 : p.Speed ?? -1).FirstOrDefault();
            merged.Add((active ?? group[0]) with
            {
                Protocols = group.Aggregate(0u, (flags, p) => flags | p.Protocols),
                TypeC = group.Any(p => p.TypeC == true) ? true : group.All(p => p.TypeC == false) ? false : null
            });
        }
        merged.AddRange(ports.Where(p => p.Source is not ("USB Hub" or "Display")
            && !(p.Source == "SMBIOS" && p.Kind is "Video" or "USB")));
        return merged.Distinct().ToList();
    }

    public static string BuildSummary(IReadOnlyList<HardwarePortInfo> ports)
    {
        var parts = new List<string>();
        var usb = ports.Where(p => p.Source == "USB Hub").ToList();
        foreach (var group in usb.GroupBy(p => p.TypeC == true))
        {
            var name = group.Key ? "USB-C" : L("UsbOther", "USB（其他形态）");
            var protocols = group.Aggregate(0u, (flags, p) => flags | p.Protocols);
            var protocol = (protocols & 4) != 0 ? L("Usb3Summary", "USB 3.x · 5 Gb/s 或更高")
                : (protocols & 2) != 0 ? "USB 2.0 · 480 Mb/s" : UsbProtocols(protocols);
            parts.Add($"{name}：{protocol}");
        }
        parts.AddRange(ports.Where(p => p.Source == "PnP").Select(p => p.Kind).Distinct());
        parts.AddRange(ports.Where(p => p.Source == "Network").Select(Describe).Distinct());
        parts.AddRange(ports.Where(p => p.Source == "SMBIOS").Select(p => p.Name + " · " + p.Kind).Distinct());
        return parts.Count == 0 ? L("Empty", "系统未报告可用的接口信息；这不代表设备没有接口。") : string.Join("；", parts);
    }

    internal static string UsbProtocols(uint protocols)
    {
        var values = new List<string>();
        if ((protocols & 1) != 0) values.Add("USB 1.1 (1.5 / 12 Mb/s)");
        if ((protocols & 2) != 0) values.Add("USB 2.0 (480 Mb/s)");
        if ((protocols & 4) != 0) values.Add(L("Usb3", "USB 3.x（支持 SuperSpeed 5 Gb/s；更高速率上限未报告）"));
        return values.Count == 0 ? L("Unknown", "无法读取") : string.Join(" / ", values);
    }

    internal static string UsbSpeed(int? speed, uint flags) => (flags & 4) != 0
        ? L("SuperPlus", "SuperSpeedPlus（10 Gb/s 或更高，具体速率未报告）")
        : (flags & 1) != 0 ? "SuperSpeed · 5 Gb/s" : speed switch
        {
            0 => "Low-Speed · 1.5 Mb/s", 1 => "Full-Speed · 12 Mb/s", 2 => "High-Speed · 480 Mb/s",
            3 => "SuperSpeed · 5 Gb/s", _ => L("Unknown", "无法读取")
        };

    public static string Describe(HardwarePortInfo port)
    {
        if (port.Source == "USB Hub")
        {
            var shape = port.TypeC == true ? "USB-C" : L("UsbShapeUnknown", "USB（插头形态未报告）");
            var location = port.UserConnectable switch { true => L("External", "可供用户连接"), false => L("Internal", "内部 / 非用户接口"), _ => L("LocationUnknown", "位置未报告") };
            var state = port.ConnectionStatus switch
            {
                0 => L("Disconnected", "未连接"), 1 => L("Connected", "已连接"),
                null => L("Unknown", "无法读取"), _ => L("ConnectionError", "连接异常") + $" ({port.ConnectionStatus})"
            };
            var speed = port.ConnectionStatus == 1 ? UsbSpeed(port.Speed, port.SpeedFlags) : "—";
            return $"{shape} · {location}\n{L("Protocols", "端口协议 / 标准速率")}: {UsbProtocols(port.Protocols)}\n{state} · {L("CurrentSpeed", "当前链路速率")}: {speed}";
        }
        if (port.Source == "Network")
            return $"Ethernet · {L("CurrentSpeed", "当前链路速率")}: " + (port.LinkSpeed is > 0
                ? (port.LinkSpeed.Value / 1_000_000d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " Mb/s"
                : L("LinkDown", "未连接或无法读取"));
        if (port.Source == "PnP")
            return port.Kind + " · " + L("ControllerOnly", "已检测到控制器；端口数量、版本与速率上限未报告");
        return port.Kind + " · " + L("Firmware", "固件报告的接口；协议版本及速率未报告");
    }

    private static void Query(string scope, string query, Action<ManagementBaseObject> consume)
    {
        TryRead(() =>
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query),
                new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(5) });
            using var rows = searcher.Get();
            foreach (ManagementBaseObject row in rows)
                using (row) TryRead(() => consume(row));
        });
    }

    private static void TryRead(Action action)
    {
        try { action(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[HardwarePorts] {ex.Message}"); }
    }

    private static void ReadUsb(List<HardwarePortInfo> result)
    {
        var guid = new Guid("f18a0e88-c30c-11d0-8815-00a0c906bed8"); // GUID_DEVINTERFACE_USB_HUB
        var set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) return;
        try
        {
            for (uint i = 0; ; i++)
            {
                var data = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) break;
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
                if (size < 6 || size > 65536) continue;
                var detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero)) continue;
                    var path = Marshal.PtrToStringUni(detail + 4);
                    if (path is null) continue;
                    using var hub = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (hub.IsInvalid) continue;
                    var node = new byte[76]; // USB_NODE_INFORMATION, usbioctl.h (pack 1)
                    if (!Io(hub, 258, node, 7)) continue;
                    var count = node[6]; // NodeType (4), descriptor length/type (2), bNumberOfPorts
                    var caps = new byte[4];
                    var isRoot = Io(hub, 276, caps, 4) && (BitConverter.ToUInt32(caps) & 16) != 0;
                    for (uint port = 1; port <= count; port++) ReadUsbPort(hub, path, isRoot, i + 1, port, result);
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    private static void ReadUsbPort(SafeFileHandle hub, string path, bool isRoot, uint hubIndex, uint port, List<HardwarePortInfo> result)
    {
        var connection = new byte[4096];
        BitConverter.TryWriteBytes(connection.AsSpan(), port);
        // USB_NODE_CONNECTION_INFORMATION_EX is packed to 1 byte: Speed=23, Status=31.
        var hasConnection = Io(hub, 274, connection, 35);
        var v2 = new byte[16];
        BitConverter.TryWriteBytes(v2.AsSpan(), port);
        BitConverter.TryWriteBytes(v2.AsSpan(4), 16u);
        BitConverter.TryWriteBytes(v2.AsSpan(8), 4u); // Usb300 must be set on input
        var hasV2 = Io(hub, 279, v2, 16);
        var connector = new byte[4096];
        BitConverter.TryWriteBytes(connector.AsSpan(), port);
        var hasConnector = Io(hub, 278, connector, 18);
        if (!hasConnection && !hasV2 && !hasConnector) return;
        var properties = hasConnector ? BitConverter.ToUInt32(connector, 8) : 0u;
        var companions = new List<string>();
        if (hasConnector)
        {
            for (ushort index = 0; index < 16; index++)
            {
                if (index > 0)
                {
                    Array.Clear(connector);
                    BitConverter.TryWriteBytes(connector.AsSpan(), port);
                    BitConverter.TryWriteBytes(connector.AsSpan(12), index);
                    if (!Io(hub, 278, connector, 18)) break;
                }
                var companionPort = BitConverter.ToUInt16(connector, 14);
                if (companionPort == 0) break;
                var companionHub = System.Text.Encoding.Unicode.GetString(connector, 16, connector.Length - 16).Split('\0')[0];
                if (companionHub.Length > 0) companions.Add(UsbKey(companionHub, companionPort));
                if ((properties & 4) == 0) break;
            }
        }
        result.Add(new($"Hub {hubIndex} / Port {port}", "USB", "USB Hub",
            Protocols: hasV2 ? BitConverter.ToUInt32(v2, 8) : 0,
            Speed: hasConnection ? connection[23] : null,
            SpeedFlags: hasV2 ? BitConverter.ToUInt32(v2, 12) : 0,
            ConnectionStatus: hasConnection ? BitConverter.ToInt32(connection, 31) : null,
            TypeC: hasConnector ? (properties & 8) != 0 : null,
            UserConnectable: hasConnector ? (properties & 1) != 0 : null,
            UsbKey: UsbKey(path, port), CompanionKeys: companions.ToArray(), IsRootHub: isRoot));
    }

    private static string UsbKey(string path, uint port) => path.Replace("\\\\?\\", "").Replace("\\??\\", "").ToUpperInvariant() + "/" + port;

    private static bool Io(SafeFileHandle handle, uint function, byte[] buffer, uint minimum) =>
        DeviceIoControl(handle, (0x22u << 16) | (function << 2), buffer, (uint)buffer.Length,
            buffer, (uint)buffer.Length, out var returned, IntPtr.Zero) && returned >= minimum;

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceData { public int Size; public Guid Guid; public uint Flags; public IntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref DeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DeviceInterfaceData data, IntPtr detail, uint size, out uint required, IntPtr device);
    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, [In, Out] byte[] input, uint inputSize,
        [In, Out] byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
}
