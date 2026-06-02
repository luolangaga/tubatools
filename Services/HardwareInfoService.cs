using System.Management;
using System.Runtime.InteropServices;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class HardwareInfoService
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DISPLAY_DEVICE
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
    }

    private const int ENUM_CURRENT_SETTINGS = -1;

    private static IReadOnlyList<HardwareInfoSection>? _cache;
    private static readonly object _lock = new();

    public static void Preload()
    {
        Task.Run(() =>
        {
            try
            {
                _ = LoadAsync();
            }
            catch { }
        });
    }

    public static Task<IReadOnlyList<HardwareInfoSection>> LoadAsync(bool forceRefresh = false)
    {
        return Task.Run<IReadOnlyList<HardwareInfoSection>>(() =>
        {
            lock (_lock)
            {
                if (!forceRefresh && _cache != null)
                    return _cache;
            }

            var sections = CreateEmptySections();

            FillSummary(sections[0]);
            FillSystem(sections[1]);
            FillDetails(sections[2]);

            lock (_lock)
            {
                _cache = sections;
            }

            return sections;
        });
    }

    public static void InvalidateCache()
    {
        lock (_lock)
        {
            _cache = null;
        }
    }

    private static List<HardwareInfoSection> CreateEmptySections()
    {
        return
        [
            new HardwareInfoSection { Title = "型号信息", Glyph = "\uE772" },
            new HardwareInfoSection { Title = "系统信息", Glyph = "\uE770" },
            new HardwareInfoSection { Title = "详细信息", Glyph = "\uE917" }
        ];
    }

    private static void FillSummary(HardwareInfoSection section)
    {
        var computer = First("Win32_ComputerSystem");
        var board = First("Win32_BaseBoard");
        var bios = First("Win32_BIOS");

        section.Items.Add(Item("设备型号", Join(Get(computer, "Manufacturer"), Get(computer, "Model"))));
        section.Items.Add(Item("主板", Join(Get(board, "Manufacturer"), Get(board, "Product"))));
        section.Items.Add(Item("BIOS", Join(Get(bios, "Manufacturer"), Get(bios, "SMBIOSBIOSVersion"))));
    }

    private static void FillSystem(HardwareInfoSection section)
    {
        var os = First("Win32_OperatingSystem");

        section.Items.Add(Item("系统", Join(Get(os, "Caption"), Get(os, "OSArchitecture"))));
        section.Items.Add(Item("版本", Get(os, "Version")));
        section.Items.Add(Item("运行时间", FormatUptime()));
    }

    private static void FillDetails(HardwareInfoSection section)
    {
        section.Items.Add(Item("主板", BoardModel()));
        section.Items.Add(Item("处理器", FirstName("Win32_Processor")));
        section.Items.Add(Item("内存", FormatMemory()));
        section.Items.Add(Item("显卡", JoinNames("Win32_VideoController", item =>
            !ContainsAny(Get(item, "Name"), "Microsoft Basic Render", "Microsoft Remote Display", "DDA Wrapper",
                "Idd Desk", "GameViewer Virtual Display", "Honor Virtual Display", "Virtual Display",
                "Virtual GPU", "Virtual Adapter", "虚拟", "Remote Display Adapter"))));
        section.Items.Add(Item("显示器", FormatDisplays()));
        section.Items.Add(Item("硬盘", FormatDisks()));
        section.Items.Add(Item("声卡", JoinNames("Win32_SoundDevice", item =>
        {
            var name = Get(item, "Name");
            return !ContainsAny(name, "Virtual", "虚拟", "Software", "Remote Audio", "Stereo Mix", "Wave", "VB-Audio", "VBAN", "Voicemeeter", "CABLE", "VAC");
        })));
        section.Items.Add(Item("网卡", JoinNames("Win32_NetworkAdapter", item =>
            IsTrue(item, "PhysicalAdapter") &&
            !ContainsAny(Get(item, "Name"), "Virtual", "Bluetooth", "WAN Miniport"))));
    }

    private static HardwareInfoItem Item(string label, string? value)
    {
        return new HardwareInfoItem
        {
            Label = label,
            Value = string.IsNullOrWhiteSpace(value) ? "未知" : value
        };
    }

    private static string FormatMemory()
    {
        var allSlots = Query("Win32_PhysicalMemory").ToList();
        if (allSlots.Count == 0)
        {
            return "未知";
        }

        var modules = allSlots.Where(item => ToLong(Get(item, "Capacity")) > 0).ToList();

        var totalSlots = Query("Win32_PhysicalMemoryArray")
            .Select(item => ToInt(Get(item, "MemoryDevices")))
            .Where(v => v > 0)
            .Sum();
        if (totalSlots == 0) totalSlots = allSlots.Count;

        if (modules.Count == 0)
        {
            return $"空插槽 {totalSlots} 个";
        }

        var totalBytes = modules.Sum(item => ToLong(Get(item, "Capacity")));
        var manufacturer = modules.Select(item => CleanMemManufacturer(Get(item, "Manufacturer"))).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        var memType = ToInt(modules.Select(item => Get(item, "SMBIOSMemoryType")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
        var prefix = GetMemoryTypeLabel(memType);

        var speeds = modules
            .Select(item => GetMemoryDataRateMts(item, memType))
            .Where(mts => mts > 0)
            .Distinct()
            .OrderByDescending(mts => mts)
            .ToList();

        var speedLabel = speeds.Count switch
        {
            0 => "",
            1 => prefix.Length > 0 ? $"{prefix}-{speeds[0]} MT/s" : $"{speeds[0]} MT/s",
            _ => prefix.Length > 0
                ? string.Join("/", speeds.Select(s => $"{prefix}-{s} MT/s"))
                : string.Join("/", speeds.Select(s => $"{s} MT/s"))
        };

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(manufacturer)) parts.Add(manufacturer);
        parts.Add($"{totalBytes / 1024d / 1024d / 1024d:0.#}GB");
        if (speedLabel.Length > 0) parts.Add(speedLabel);
        parts.Add($"({modules.Count}/{totalSlots} 插槽)");

        return string.Join(" ", parts);
    }

    private static string GetMemoryTypeLabel(int smbiosMemoryType)
    {
        return smbiosMemoryType switch
        {
            18 => "DDR",
            19 => "DDR2",
            20 => "DDR2 FB-DIMM",
            24 => "DDR3",
            25 => "DDR3L",
            26 => "DDR4",
            27 => "LPDDR",
            28 => "LPDDR2",
            29 => "LPDDR3",
            30 => "LPDDR4",
            34 => "DDR5",
            35 => "LPDDR5",
            36 => "HBM3",
            _ => ""
        };
    }

    private static int GetMemoryDataRateMts(ManagementBaseObject item, int smbiosMemoryType)
    {
        var configuredClockSpeed = ToInt(Get(item, "ConfiguredClockSpeed"));
        var speed = ToInt(Get(item, "Speed"));

        if (configuredClockSpeed <= 0)
        {
            return speed;
        }

        if (speed <= 0)
        {
            return configuredClockSpeed;
        }

        if (configuredClockSpeed >= speed)
        {
            return configuredClockSpeed;
        }

        if (UsesBaseClockForConfiguredSpeed(smbiosMemoryType))
        {
            var doubledClock = configuredClockSpeed * 2;
            if (doubledClock >= speed)
            {
                return doubledClock;
            }
        }

        return configuredClockSpeed;
    }

    private static bool UsesBaseClockForConfiguredSpeed(int smbiosMemoryType)
    {
        return smbiosMemoryType is 18 or 19 or 20 or 24 or 25 or 26 or 34;
    }

    private static string? CleanMemManufacturer(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = raw.Trim();

        var jedecDecoded = DecodeJedecManufacturer(cleaned);
        if (jedecDecoded != null) return jedecDecoded;

        return cleaned.ToUpperInvariant() switch
        {
            "KINGSTON" or "KINGSTON TECHNOLOGY" => "金士顿(Kingston)",
            "CORSAIR" => "海盗船(Corsair)",
            "CRUCIAL" or "CRUCIAL TECHNOLOGY" => "英睿达(Crucial)",
            "SAMSUNG" or "SAMSUNG ELECTRONICS" => "三星(Samsung)",
            "SK HYNIX" or "HYNIX" => "海力士(SK Hynix)",
            "MICRON" or "MICRON TECHNOLOGY" => "美光(Micron)",
            "ADATA" or "ADATA TECHNOLOGY" => "威刚(ADATA)",
            "G.SKILL" or "GSKILL" => "芝奇(G.Skill)",
            "TEAM" or "TEAMGROUP" or "TEAM GROUP" => "十铨(TeamGroup)",
            "GEIL" => "金邦(Geil)",
            "APACER" => "宇瞻(Apacer)",
            "PATRIOT" => "博帝(Patriot)",
            "SILICON POWER" => "广颖电通(Silicon Power)",
            "KLEVV" => "科赋(Klevv)",
            "BIWIN" => "佰维(Biwin)",
            "GALAX" or "GALAXY" => "影驰(Galax)",
            "COLORFUL" => "七彩虹(Colorful)",
            "LONGSYS" => "江波龙(Longsys)",
            "NETAC" => "朗科(Netac)",
            "PNY" => "必恩威(PNY)",
            "GOODRAM" => "Goodram",
            "RAMAXEL" => "记忆科技(Ramaxel)",
            "CXMT" => "长鑫存储(CXMT)",
            _ => cleaned
        };
    }

    private static string? DecodeJedecManufacturer(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();

        if (trimmed.Length == 2 && IsHex(trimmed))
        {
            var code = Convert.ToByte(trimmed, 16);
            return JedecVendorFromCode(code);
        }

        if (trimmed.Length == 4 && IsHex(trimmed))
        {
            var code = Convert.ToUInt16(trimmed, 16);
            var bank = (code >> 8) & 0x7F;
            var vendor = code & 0x7F;
            var fullCode = bank * 128 + vendor;
            return JedecVendorFromExtendedCode(fullCode);
        }

        if (trimmed.Length >= 4 && trimmed.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
        {
            var upper = trimmed.ToUpperInvariant();
            if (upper.StartsWith("0X"))
            {
                var hexPart = upper.Substring(2);
                if (hexPart.Length == 2 && IsHex(hexPart))
                {
                    var code = Convert.ToByte(hexPart, 16);
                    return JedecVendorFromCode(code);
                }
                if (hexPart.Length == 4 && IsHex(hexPart))
                {
                    var code = Convert.ToUInt16(hexPart, 16);
                    var bank = (code >> 8) & 0x7F;
                    var vendor = code & 0x7F;
                    var fullCode = bank * 128 + vendor;
                    return JedecVendorFromExtendedCode(fullCode);
                }
            }
        }

        return null;
    }

    private static bool IsHex(string s)
    {
        return s.All(c => char.IsAsciiHexDigit(c));
    }

    private static string? JedecVendorFromCode(byte code)
    {
        return code switch
        {
            0x02 => "美光(Micron)",
            0x04 => "Moseley",
            0x05 => "Inmos",
            0x06 => "富士通(Fujitsu)",
            0x07 => "Hitachi",
            0x08 => "松下(Panasonic)",
            0x0A => "NEC",
            0x0B => "东芝(Toshiba)",
            0x0E => "三星(Samsung)",
            0x10 => "三菱(Mitsubishi)",
            0x11 => "Hynix",
            0x13 => "Elpida",
            0x15 => "英飞凌(Infineon)",
            0x16 => "Kingston",
            0x17 => "Fujitsu",
            0x19 => "Winbond",
            0x1A => "Nanya",
            0x1C => "力晶(Powerchip)",
            0x1E => "茂德(ProMOS)",
            0x1F => "海力士(SK Hynix)",
            0x20 => "Mikron",
            0x23 => "劲永(PQI)",
            0x25 => "宇瞻(Apacer)",
            0x26 => "威刚(ADATA)",
            0x27 => "Corsair",
            0x28 => "Avant",
            0x29 => "G.Skill",
            0x2C => "金士顿(Kingston)",
            0x30 => "Ramaxel",
            0x31 => "Crucial",
            0x34 => "芝奇(G.Skill)",
            0x36 => "海盗船(Corsair)",
            0x38 => "Smart",
            0x3B => "Crucial",
            0x3C => "Jedec",
            0x3E => "金邦(Geil)",
            0x40 => "Smart",
            0x41 => "博帝(Patriot)",
            0x42 => "十铨(TeamGroup)",
            0x43 => "科赋(Klevv)",
            0x44 => "影驰(Galax)",
            0x45 => "七彩虹(Colorful)",
            0x46 => "佰维(Biwin)",
            0x47 => "江波龙(Longsys)",
            0x48 => "朗科(Netac)",
            0x49 => "广颖电通(Silicon Power)",
            0x4A => "必恩威(PNY)",
            0x4B => "Goodram",
            0x4C => "长鑫存储(CXMT)",
            0x80 => "三星(Samsung)",
            0x81 => "海力士(SK Hynix)",
            0x82 => "美光(Micron)",
            0x83 => "金士顿(Kingston)",
            0x84 => "英睿达(Crucial)",
            0x85 => "海盗船(Corsair)",
            0x86 => "芝奇(G.Skill)",
            0x87 => "威刚(ADATA)",
            0x88 => "十铨(TeamGroup)",
            0x89 => "宇瞻(Apacer)",
            0x8A => "金邦(Geil)",
            0x8B => "博帝(Patriot)",
            0x8C => "影驰(Galax)",
            0x8D => "七彩虹(Colorful)",
            0x8E => "科赋(Klevv)",
            0x8F => "佰维(Biwin)",
            0x90 => "江波龙(Longsys)",
            0x91 => "朗科(Netac)",
            0x92 => "广颖电通(Silicon Power)",
            0x93 => "必恩威(PNY)",
            0x94 => "Goodram",
            0x95 => "长鑫存储(CXMT)",
            0x9E => "记忆科技(Ramaxel)",
            _ => null
        };
    }

    private static string? JedecVendorFromExtendedCode(int fullCode)
    {
        return fullCode switch
        {
            0x02 => "美光(Micron)",
            0x0E => "三星(Samsung)",
            0x11 => "海力士(SK Hynix)",
            0x13 => "尔必达(Elpida)",
            0x15 => "英飞凌(Infineon)",
            0x16 => "金士顿(Kingston)",
            0x1A => "南亚(Nanya)",
            0x1F => "海力士(SK Hynix)",
            0x2C => "金士顿(Kingston)",
            0x30 => "记忆科技(Ramaxel)",
            0x80 => "三星(Samsung)",
            0x81 => "海力士(SK Hynix)",
            0x82 => "美光(Micron)",
            0x83 => "金士顿(Kingston)",
            0x84 => "英睿达(Crucial)",
            0x85 => "海盗船(Corsair)",
            0x86 => "芝奇(G.Skill)",
            0x87 => "威刚(ADATA)",
            0x88 => "十铨(TeamGroup)",
            0x9E => "记忆科技(Ramaxel)",
            _ => JedecVendorFromCode((byte)(fullCode & 0xFF))
        };
    }

    private static string FormatDisks()
    {
        var disks = Query("Win32_DiskDrive")
            .Select(item =>
            {
                var model = Get(item, "Model");
                var size = ToLong(Get(item, "Size")) / 1024d / 1024d / 1024d;
                return string.IsNullOrWhiteSpace(model) ? null : $"{model} ({size:0.#}GB)";
            })
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return string.Join(" / ", disks);
    }

    private static string FormatDisplays()
    {
        var monitorInfos = new List<(string Label, string? Resolution)>();

        try
        {
            var wmiMonitors = new List<(string InstanceName, string Label)>();
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorID");
            foreach (ManagementBaseObject item in searcher.Get())
            {
                var mfr = DecodeWmiArray(item, "ManufacturerName");
                var product = DecodeWmiArray(item, "ProductName");
                var serial = DecodeWmiArray(item, "SerialNumberID");
                var instanceName = Get(item, "InstanceName") ?? "";
                var pnpId = instanceName.Split('\\').FirstOrDefault() ?? "";

                var mfrLabel = ResolveManufacturer(mfr);
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(mfrLabel)) parts.Add(mfrLabel);
                if (!string.IsNullOrWhiteSpace(product) && product != mfrLabel) parts.Add(product);
                if (parts.Count == 0 && !string.IsNullOrWhiteSpace(pnpId))
                {
                    var pnpMfr = ResolveManufacturer(pnpId.Length >= 3 ? pnpId.Substring(0, 3) : pnpId);
                    if (!string.IsNullOrWhiteSpace(pnpMfr)) parts.Add(pnpMfr);
                }

                var label = string.Join(" ", parts.Distinct());
                if (!string.IsNullOrWhiteSpace(serial) && serial != "0") label += $" (SN:{serial})";
                wmiMonitors.Add((instanceName, label));
            }

            var deviceResolutions = GetDisplayDeviceResolutions();

            foreach (var (instanceName, label) in wmiMonitors)
            {
                var pnpPart = instanceName.Split('\\').FirstOrDefault() ?? "";
                var resolution = deviceResolutions.FirstOrDefault(kv =>
                    kv.Key.Equals(pnpPart, StringComparison.OrdinalIgnoreCase)).Value;
                monitorInfos.Add((label, resolution));
            }

            if (monitorInfos.Count == 0)
            {
                foreach (var res in deviceResolutions.Values)
                {
                    monitorInfos.Add(("", res));
                }
            }
        }
        catch { }

        if (monitorInfos.Count == 0)
        {
            var pnpNames = Query("Win32_PnPEntity")
                .Where(item =>
                {
                    var pnpClass = Get(item, "PNPClass");
                    return pnpClass == "Monitor";
                })
                .Select(item => Get(item, "Name"))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct()
                .ToList();

            var fallbackRes = GetFallbackResolutions();
            for (int i = 0; i < pnpNames.Count; i++)
            {
                var res = i < fallbackRes.Count ? fallbackRes[i] : null;
                monitorInfos.Add((pnpNames[i]!, res));
            }
        }

        if (monitorInfos.Count == 0) return "未知";

        return string.Join(" / ", monitorInfos.Select(mi =>
        {
            if (string.IsNullOrWhiteSpace(mi.Label) && string.IsNullOrWhiteSpace(mi.Resolution))
                return "";
            if (string.IsNullOrWhiteSpace(mi.Label)) return mi.Resolution;
            if (string.IsNullOrWhiteSpace(mi.Resolution)) return mi.Label;
            return $"{mi.Label} [{mi.Resolution}]";
        }).Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static Dictionary<string, string> GetDisplayDeviceResolutions()
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var dd = new DISPLAY_DEVICE { Size = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++)
        {
            var adapterName = dd.DeviceName;
            var adapterDeviceId = dd.DeviceID;

            var mon = new DISPLAY_DEVICE { Size = Marshal.SizeOf<DISPLAY_DEVICE>() };
            for (uint j = 0; EnumDisplayDevices(adapterName, j, ref mon, 0); j++)
            {
                if ((mon.StateFlags & 1) != 0)
                {
                    var pnpDeviceId = ExtractPnpFromDeviceId(mon.DeviceID);
                    if (string.IsNullOrWhiteSpace(pnpDeviceId))
                    {
                        mon = new DISPLAY_DEVICE { Size = Marshal.SizeOf<DISPLAY_DEVICE>() };
                        continue;
                    }

                    var mode = new DEVMODE();
                    mode.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
                    if (EnumDisplaySettings(adapterName, ENUM_CURRENT_SETTINGS, ref mode))
                    {
                        var res = $"{mode.dmPelsWidth} x {mode.dmPelsHeight}";
                        if (!results.ContainsKey(pnpDeviceId))
                        {
                            results[pnpDeviceId] = res;
                        }
                    }
                }
                mon = new DISPLAY_DEVICE { Size = Marshal.SizeOf<DISPLAY_DEVICE>() };
            }

            dd = new DISPLAY_DEVICE { Size = Marshal.SizeOf<DISPLAY_DEVICE>() };
        }

        return results;
    }

    private static string ExtractPnpFromDeviceId(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return "";
        var parts = deviceId.Split('#');
        if (parts.Length >= 2)
        {
            return parts[1];
        }
        return deviceId;
    }

    private static List<string> GetFallbackResolutions()
    {
        var results = new List<string>();
        try
        {
            var dd = new DISPLAY_DEVICE { Size = Marshal.SizeOf<DISPLAY_DEVICE>() };
            for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++)
            {
                if ((dd.StateFlags & 1) != 0 || (dd.StateFlags & 2) != 0)
                {
                    var mode = new DEVMODE();
                    mode.dmSize = (ushort)Marshal.SizeOf<DEVMODE>();
                    if (EnumDisplaySettings(dd.DeviceName, ENUM_CURRENT_SETTINGS, ref mode))
                    {
                        results.Add($"{mode.dmPelsWidth} x {mode.dmPelsHeight}");
                    }
                }
                dd = new DISPLAY_DEVICE { Size = Marshal.SizeOf<DISPLAY_DEVICE>() };
            }
        }
        catch { }

        if (results.Count == 0)
        {
            results = Query("Win32_VideoController")
                .Select(item =>
                {
                    var width = Get(item, "CurrentHorizontalResolution");
                    var height = Get(item, "CurrentVerticalResolution");
                    return string.IsNullOrWhiteSpace(width) || string.IsNullOrWhiteSpace(height)
                        ? null
                        : $"{width} x {height}";
                })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct()
                .ToList()!;
        }

        return results;
    }

    private static string? DecodeWmiArray(ManagementBaseObject item, string propName)
    {
        try
        {
            var val = item[propName];
            if (val is ushort[] arr)
            {
                var chars = arr.TakeWhile(c => c > 0).Select(c => (char)c).ToArray();
                return chars.Length > 0 ? new string(chars).Trim() : null;
            }
            if (val is byte[] barr)
            {
                var chars = barr.TakeWhile(b => b > 0).Select(b => (char)b).ToArray();
                return chars.Length > 0 ? new string(chars).Trim() : null;
            }
            return val?.ToString()?.Trim();
        }
        catch { return null; }
    }

    private static string? ResolveManufacturer(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return code.Trim().ToUpperInvariant() switch
        {
            "ACI" or "ACR" => "Acer(宏碁)",
            "AUO" or "AUO_" => "友达(AU Optronics)",
            "BOE" or "BOE_" => "京东方(BOE)",
            "CMN" => "奇美(Chimei InnoLux)",
            "CSO" => "华星光电(CSO)",
            "DIS" => "Dell(戴尔)",
            "DEL" => "Dell(戴尔)",
            "HSD" => "瀚宇彩晶(HannStar)",
            "HKC" => "HKC(惠科)",
            "IVO" => "天马(IVO)",
            "INL" => "群创(Innolux)",
            "LGD" or "LPL" => "LG Display",
            "LEN" or "LEN_" => "联想(Lenovo)",
            "SEC" or "SDC" => "三星(Samsung)",
            "SHV" => "夏普(Sharp)",
            "SAM" => "三星(Samsung)",
            "SNY" => "索尼(Sony)",
            "HWP" or "HEW" => "HP(惠普)",
            "MEL" => "三菱(Mitsubishi)",
            "FNI" => "Funai",
            "ASN" => "明基(BenQ)",
            "BNQ" => "明基(BenQ)",
            "AOC" or "AOC_" => "AOC(冠捷)",
            "PHL" => "飞利浦(Philips)",
            "PTS" => "飞利浦(Philips)",
            "MST" => "明基(BenQ)",
            "HAI" or "HAI_" => "海信(Hisense)",
            "TCL" or "TCL_" => "TCL",
            "CSW" => "长城(Great Wall)",
            "GWR" or "GWR_" => "长城(Great Wall)",
            "HPC" => "惠浦(HPC)",
            "VSC" => "优派(ViewSonic)",
            "VIT" => "唯冠(VIT)",
            "IMA" => "理想(IMA)",
            "NEX" => "NEXO",
            "ELO" => "Elo Touch",
            "FUJ" => "富士通(Fujitsu)",
            "FUS" => "富士通(Fujitsu)",
            "GGL" => "Google",
            "HHT" => "HHI",
            "JDI" or "JDI_" => "日本显示器(JDI)",
            "MAG" or "MAG_" => "美格(MAG)",
            "MED" => "Medion",
            "NEC" or "NEC_" => "NEC(日电)",
            "OEM" => "OEM",
            "PBN" => "Packard Bell",
            "PCK" => "Daewoo",
            "QDS" => "Quanta Display",
            "SHP" => "夏普(Sharp)",
            "SPT" => "Sceptre",
            "SUN" => "Sun",
            "TOS" => "东芝(Toshiba)",
            "TSB" => "东芝(Toshiba)",
            "UNM" => "Unisys",
            "VES" => "Vestel",
            "WDE" => "Westinghouse",
            "ZCM" => "Zenith",
            _ => code
        };
    }

    private static string FormatUptime()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return $"{uptime.Days}天{uptime.Hours}小时{uptime.Minutes}分钟{uptime.Seconds}秒";
    }

    private static string FirstName(string className)
    {
        return Query(className).Select(item => Get(item, "Name")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "未知";
    }

    private static string BoardModel()
    {
        var board = First("Win32_BaseBoard");
        var mfr = CleanBoardManufacturer(Get(board, "Manufacturer"));
        var product = Get(board, "Product");
        return Join(mfr, product);
    }

    private static string? CleanBoardManufacturer(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = raw.Trim();
        return cleaned.ToUpperInvariant() switch
        {
            "ASUS" or "ASUSTEK" or "ASUSTEK COMPUTER INC." => "华硕(ASUS)",
            "MSI" or "MICRO-STAR INTERNATIONAL" or "MICRO-STAR INTERNATIONAL CO., LTD" => "微星(MSI)",
            "GIGABYTE" or "GIGABYTE TECHNOLOGY CO., LTD." => "技嘉(Gigabyte)",
            "ASROCK" or "ASROCK INC." => "华擎(ASRock)",
            "BIOSTAR" or "BIOSTAR MICROTECH INT'L CORP." => "映泰(Biostar)",
            "COLORFUL" or "COLORFUL TECHNOLOGY CO., LTD" => "七彩虹(Colorful)",
            "MAXSUN" or "MAXSUN TECHNOLOGY CO., LTD." => "铭瑄(Maxsun)",
            "SOYO" or "SOYO TECHNOLOGY CO., LTD." => "梅捷(Soyo)",
            "ONDA" or "ONDA TECHNOLOGY CO., LTD." => "昂达(Onda)",
            "JW" or "J&W TECHNOLOGY CO., LTD." => "杰微(J&W)",
            "YESTON" or "YESTON TECHNOLOGY CO., LTD." => "盈通(Yeston)",
            "FOXCONN" or "FOXCONN TECHNOLOGY INC." => "富士康(Foxconn)",
            "INTEL" or "INTEL CORPORATION" => "英特尔(Intel)",
            "DELL" or "DELL INC." => "戴尔(Dell)",
            "HP" or "HEWLETT-PACKARD" or "HP INC." => "惠普(HP)",
            "LENOVO" or "LENOVO PRODUCT" => "联想(Lenovo)",
            "ACER" or "ACER INCORPORATED" => "宏碁(Acer)",
            "SAMSUNG" or "SAMSUNG ELECTRONICS" => "三星(Samsung)",
            "TOSHIBA" => "东芝(Toshiba)",
            "SONY" => "索尼(Sony)",
            "FUJITSU" => "富士通(Fujitsu)",
            "APPLE" => "苹果(Apple)",
            "HUAWEI" => "华为(Huawei)",
            "XIAOMI" => "小米(Xiaomi)",
            "SUPERMICRO" or "SUPERMICRO COMPUTER INC." => "超微(Supermicro)",
            "EVGA" => "EVGA",
            "NZXT" => "NZXT",
            "ASRockRack" => "华擎服务器(ASRock Rack)",
            _ => cleaned
        };
    }

    private static string JoinNames(string className, Func<ManagementBaseObject, bool>? filter = null)
    {
        var names = Query(className)
            .Where(item => filter?.Invoke(item) ?? true)
            .Select(item => Get(item, "Name"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct();

        return string.Join(" / ", names);
    }

    private static ManagementBaseObject? First(string className)
    {
        return Query(className).FirstOrDefault();
    }

    private static IEnumerable<ManagementBaseObject> Query(string className)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM {className}");
            foreach (ManagementBaseObject item in searcher.Get())
            {
                yield return item;
            }
        }
        finally
        {
        }
    }

    private static string? Get(ManagementBaseObject? item, string propertyName)
    {
        try
        {
            return item?[propertyName]?.ToString()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsTrue(ManagementBaseObject item, string propertyName)
    {
        return bool.TryParse(Get(item, propertyName), out var value) && value;
    }

    private static bool ContainsAny(string? value, params string[] needles)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static long ToLong(string? value)
    {
        return long.TryParse(value, out var number) ? number : 0;
    }

    private static int ToInt(string? value)
    {
        return int.TryParse(value, out var number) ? number : 0;
    }

    private static string Join(params string?[] values)
    {
        return string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string? FirstUseful(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
