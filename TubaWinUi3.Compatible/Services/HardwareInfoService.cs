using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using TubaWinUi3.Compatible.Models;

namespace TubaWinUi3.Compatible.Services
{
    public static class HardwareInfoService
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

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
        private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
        private const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;

        private static IReadOnlyList<HardwareInfoSection> _cache;
        private static readonly object _lock = new object();

        private static string GetSeparator()
        {
            return AppSettings.GetBool("CompactModeEnabled", false) ? Environment.NewLine : " / ";
        }

        public static bool HasCache
        {
            get { lock (_lock) { return _cache != null; } }
        }

        public static void Preload()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { LoadAsync(); }
                catch { }
            });
        }

        public static IReadOnlyList<HardwareInfoSection> LoadAsync(bool forceRefresh = false)
        {
            return BuildSections(forceRefresh);
        }

        private static IReadOnlyList<HardwareInfoSection> BuildSections(bool forceRefresh)
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
            return new List<HardwareInfoSection>
            {
                new HardwareInfoSection { Title = "型号信息", Glyph = "\uE772" },
                new HardwareInfoSection { Title = "系统信息", Glyph = "\uE770" },
                new HardwareInfoSection { Title = "详细信息", Glyph = "\uE917" }
            };
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
            var cpuName = FirstName("Win32_Processor");
            var cpuItem = Item("处理器", cpuName);
            cpuItem.BrandKey = DetectCpuBrand(cpuName);
            section.Items.Add(cpuItem);
            section.Items.Add(Item("内存", FormatMemory()));
            var gpuName = JoinNames("Win32_VideoController", item =>
                !ContainsAny(Get(item, "Name"), "Microsoft Basic Render", "Microsoft Remote Display", "DDA Wrapper",
                    "Idd Desk", "GameViewer Virtual Display", "Honor Virtual Display", "Virtual Display",
                    "Virtual GPU", "Virtual Adapter", "虚拟", "Remote Display Adapter"));
            var gpuItem = Item("显卡", gpuName);
            gpuItem.BrandKey = DetectGpuBrand(gpuName);
            section.Items.Add(gpuItem);
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

        private static string DetectCpuBrand(string cpuName)
        {
            if (string.IsNullOrWhiteSpace(cpuName)) return null;
            var name = cpuName.ToUpperInvariant();
            if (name.Contains("INTEL")) return "intel";
            if (name.Contains("AMD")) return "amd";
            if (name.Contains("APPLE") || name.Contains("M1") || name.Contains("M2") || name.Contains("M3") || name.Contains("M4")) return "apple";
            if (name.Contains("QUALCOMM") || name.Contains("SNAPDRAGON")) return "qualcomm";
            return null;
        }

        private static string DetectGpuBrand(string gpuName)
        {
            if (string.IsNullOrWhiteSpace(gpuName)) return null;
            var name = gpuName.ToUpperInvariant();
            if (name.Contains("NVIDIA") || name.Contains("GEFORCE") || name.Contains("RTX") || name.Contains("GTX")) return "nvidia";
            if (name.Contains("AMD") || name.Contains("RADEON")) return "amd";
            if (name.Contains("INTEL") || name.Contains("ARC") || name.Contains("UHD") || name.Contains("IRIS")) return "intel";
            if (name.Contains("APPLE")) return "apple";
            if (name.Contains("QUALCOMM") || name.Contains("ADRENO")) return "qualcomm";
            return null;
        }

        private static HardwareInfoItem Item(string label, string value)
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
            if (allSlots.Count == 0) return "未知";

            var modules = allSlots.Where(item => ToLong(Get(item, "Capacity")) > 0).ToList();

            var totalSlots = Query("Win32_PhysicalMemoryArray")
                .Select(item => ToInt(Get(item, "MemoryDevices")))
                .Where(v => v > 0)
                .Sum();
            if (totalSlots == 0) totalSlots = allSlots.Count;

            if (modules.Count == 0) return "空插槽 " + totalSlots + " 个";

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

            var speedLabel = "";
            if (speeds.Count == 1)
                speedLabel = prefix.Length > 0 ? prefix + "-" + speeds[0] + " MT/s" : speeds[0] + " MT/s";
            else if (speeds.Count > 1)
                speedLabel = prefix.Length > 0
                    ? string.Join("/", speeds.Select(s => prefix + "-" + s + " MT/s"))
                    : string.Join("/", speeds.Select(s => s + " MT/s"));

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(manufacturer)) parts.Add(manufacturer);
            parts.Add(string.Format("{0:0.#}GB", totalBytes / 1024.0 / 1024.0 / 1024.0));
            if (speedLabel.Length > 0) parts.Add(speedLabel);
            parts.Add("(" + modules.Count + "/" + totalSlots + " 插槽)");

            return string.Join(" ", parts);
        }

        private static string GetMemoryTypeLabel(int smbiosMemoryType)
        {
            switch (smbiosMemoryType)
            {
                case 18: return "DDR";
                case 19: return "DDR2";
                case 20: return "DDR2 FB-DIMM";
                case 24: return "DDR3";
                case 25: return "DDR3L";
                case 26: return "DDR4";
                case 27: return "LPDDR";
                case 28: return "LPDDR2";
                case 29: return "LPDDR3";
                case 30: return "LPDDR4";
                case 34: return "DDR5";
                case 35: return "LPDDR5";
                default: return "";
            }
        }

        private static int GetMemoryDataRateMts(ManagementBaseObject item, int smbiosMemoryType)
        {
            var configuredClockSpeed = ToInt(Get(item, "ConfiguredClockSpeed"));
            var speed = ToInt(Get(item, "Speed"));

            if (configuredClockSpeed <= 0) return speed;
            if (speed <= 0) return configuredClockSpeed;
            if (configuredClockSpeed >= speed) return configuredClockSpeed;

            if (UsesBaseClockForConfiguredSpeed(smbiosMemoryType))
            {
                var doubledClock = configuredClockSpeed * 2;
                if (doubledClock >= speed) return doubledClock;
            }

            return configuredClockSpeed;
        }

        private static bool UsesBaseClockForConfiguredSpeed(int smbiosMemoryType)
        {
            return smbiosMemoryType == 18 || smbiosMemoryType == 19 || smbiosMemoryType == 20 ||
                   smbiosMemoryType == 24 || smbiosMemoryType == 25 || smbiosMemoryType == 26 || smbiosMemoryType == 34;
        }

        private static string CleanMemManufacturer(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var cleaned = raw.Trim();

            var jedecDecoded = DecodeJedecManufacturer(cleaned);
            if (jedecDecoded != null) return jedecDecoded;

            var upper = cleaned.ToUpperInvariant();
            switch (upper)
            {
                case "KINGSTON": case "KINGSTON TECHNOLOGY": return "金士顿(Kingston)";
                case "CORSAIR": return "海盗船(Corsair)";
                case "CRUCIAL": case "CRUCIAL TECHNOLOGY": return "英睿达(Crucial)";
                case "SAMSUNG": case "SAMSUNG ELECTRONICS": return "三星(Samsung)";
                case "SK HYNIX": case "HYNIX": return "海力士(SK Hynix)";
                case "MICRON": case "MICRON TECHNOLOGY": return "美光(Micron)";
                case "ADATA": case "ADATA TECHNOLOGY": return "威刚(ADATA)";
                case "G.SKILL": case "GSKILL": return "芝奇(G.Skill)";
                case "TEAM": case "TEAMGROUP": case "TEAM GROUP": return "十铨(TeamGroup)";
                case "GEIL": return "金邦(Geil)";
                case "APACER": return "宇瞻(Apacer)";
                case "PATRIOT": return "博帝(Patriot)";
                case "SILICON POWER": return "广颖电通(Silicon Power)";
                case "KLEVV": return "科赋(Klevv)";
                case "BIWIN": return "佰维(Biwin)";
                case "GALAX": case "GALAXY": return "影驰(Galax)";
                case "COLORFUL": return "七彩虹(Colorful)";
                case "LONGSYS": return "江波龙(Longsys)";
                case "NETAC": return "朗科(Netac)";
                case "PNY": return "必恩威(PNY)";
                case "RAMAXEL": return "记忆科技(Ramaxel)";
                case "CXMT": return "长鑫存储(CXMT)";
                default: return cleaned;
            }
        }

        private static string DecodeJedecManufacturer(string raw)
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

            return null;
        }

        private static bool IsHex(string s)
        {
            foreach (char c in s)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        private static string JedecVendorFromCode(byte code)
        {
            switch (code)
            {
                case 0x02: return "美光(Micron)";
                case 0x06: return "富士通(Fujitsu)";
                case 0x07: return "Hitachi";
                case 0x08: return "松下(Panasonic)";
                case 0x0A: return "NEC";
                case 0x0B: return "东芝(Toshiba)";
                case 0x0E: return "三星(Samsung)";
                case 0x11: return "Hynix";
                case 0x13: return "Elpida";
                case 0x15: return "英飞凌(Infineon)";
                case 0x16: return "Kingston";
                case 0x19: return "Winbond";
                case 0x1A: return "Nanya";
                case 0x1C: return "力晶(Powerchip)";
                case 0x1E: return "茂德(ProMOS)";
                case 0x1F: return "海力士(SK Hynix)";
                case 0x25: return "宇瞻(Apacer)";
                case 0x26: return "威刚(ADATA)";
                case 0x27: return "Corsair";
                case 0x29: return "G.Skill";
                case 0x2C: return "金士顿(Kingston)";
                case 0x30: return "Ramaxel";
                case 0x31: return "Crucial";
                case 0x34: return "芝奇(G.Skill)";
                case 0x36: return "海盗船(Corsair)";
                case 0x3E: return "金邦(Geil)";
                case 0x41: return "博帝(Patriot)";
                case 0x42: return "十铨(TeamGroup)";
                case 0x43: return "科赋(Klevv)";
                case 0x44: return "影驰(Galax)";
                case 0x45: return "七彩虹(Colorful)";
                case 0x46: return "佰维(Biwin)";
                case 0x47: return "江波龙(Longsys)";
                case 0x48: return "朗科(Netac)";
                case 0x49: return "广颖电通(Silicon Power)";
                case 0x4A: return "必恩威(PNY)";
                case 0x4C: return "长鑫存储(CXMT)";
                case 0x80: return "三星(Samsung)";
                case 0x81: return "海力士(SK Hynix)";
                case 0x82: return "美光(Micron)";
                case 0x83: return "金士顿(Kingston)";
                case 0x84: return "英睿达(Crucial)";
                case 0x85: return "海盗船(Corsair)";
                case 0x86: return "芝奇(G.Skill)";
                case 0x87: return "威刚(ADATA)";
                case 0x88: return "十铨(TeamGroup)";
                case 0x89: return "宇瞻(Apacer)";
                case 0x8A: return "金邦(Geil)";
                case 0x8B: return "博帝(Patriot)";
                case 0x8C: return "影驰(Galax)";
                case 0x8D: return "七彩虹(Colorful)";
                case 0x8E: return "科赋(Klevv)";
                case 0x8F: return "佰维(Biwin)";
                case 0x90: return "江波龙(Longsys)";
                case 0x91: return "朗科(Netac)";
                case 0x92: return "广颖电通(Silicon Power)";
                case 0x93: return "必恩威(PNY)";
                case 0x95: return "长鑫存储(CXMT)";
                case 0x9E: return "记忆科技(Ramaxel)";
                default: return null;
            }
        }

        private static string JedecVendorFromExtendedCode(int fullCode)
        {
            switch (fullCode)
            {
                case 0x02: return "美光(Micron)";
                case 0x0E: return "三星(Samsung)";
                case 0x11: return "海力士(SK Hynix)";
                case 0x13: return "尔必达(Elpida)";
                case 0x15: return "英飞凌(Infineon)";
                case 0x16: return "金士顿(Kingston)";
                case 0x1A: return "南亚(Nanya)";
                case 0x1F: return "海力士(SK Hynix)";
                case 0x2C: return "金士顿(Kingston)";
                case 0x30: return "记忆科技(Ramaxel)";
                case 0x80: return "三星(Samsung)";
                case 0x81: return "海力士(SK Hynix)";
                case 0x82: return "美光(Micron)";
                case 0x83: return "金士顿(Kingston)";
                case 0x84: return "英睿达(Crucial)";
                case 0x85: return "海盗船(Corsair)";
                case 0x86: return "芝奇(G.Skill)";
                case 0x87: return "威刚(ADATA)";
                case 0x88: return "十铨(TeamGroup)";
                case 0x9E: return "记忆科技(Ramaxel)";
                default: return JedecVendorFromCode((byte)(fullCode & 0xFF));
            }
        }

        private static string FormatDisks()
        {
            var disks = Query("Win32_DiskDrive")
                .Select(item =>
                {
                    var model = Get(item, "Model");
                    var size = ToLong(Get(item, "Size")) / 1024.0 / 1024.0 / 1024.0;
                    return string.IsNullOrWhiteSpace(model) ? null : model + " (" + size.ToString("0.#") + "GB)";
                })
                .Where(value => !string.IsNullOrWhiteSpace(value));

            return string.Join(GetSeparator(), disks);
        }

        private static string FormatDisplays()
        {
            var monitorInfos = GetActiveDisplayInfos();

            if (monitorInfos.Count == 0)
            {
                var pnpNames = Query("Win32_PnPEntity")
                    .Where(item => Get(item, "PNPClass") == "Monitor")
                    .Select(item => Get(item, "Name"))
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct()
                    .ToList();

                if (pnpNames.Count > 0)
                    return string.Join(" / ", pnpNames);
            }

            if (monitorInfos.Count == 0) return "未知";

            var parts = new List<string>();
            foreach (var mi in monitorInfos)
            {
                if (string.IsNullOrWhiteSpace(mi.Label) && string.IsNullOrWhiteSpace(mi.Resolution)) continue;
                var label = mi.IsPrimary && !string.IsNullOrWhiteSpace(mi.Label) ? "主屏 " + mi.Label : mi.Label;
                var bracket = mi.Resolution;
                if (string.IsNullOrWhiteSpace(label)) { if (!string.IsNullOrWhiteSpace(bracket)) parts.Add(bracket); continue; }
                if (string.IsNullOrWhiteSpace(bracket)) { parts.Add(label); continue; }
                parts.Add(label + " [" + bracket + "]");
            }
            return string.Join(GetSeparator(), parts);
        }

        private sealed class DisplayInfo
        {
            public string Label;
            public string Resolution;
            public bool IsPrimary;

            public DisplayInfo(string label, string resolution, bool isPrimary)
            {
                Label = label; Resolution = resolution; IsPrimary = isPrimary;
            }
        }

        private static List<DisplayInfo> GetActiveDisplayInfos()
        {
            var results = new List<DisplayInfo>();
            var wmiLabels = GetWmiMonitorLabelsByPnpCode();

            try
            {
                var adapter = NewDisplayDevice();
                for (uint i = 0; EnumDisplayDevices(null, i, ref adapter, 0); i++)
                {
                    if ((adapter.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                    {
                        var resolution = GetCurrentResolution(adapter.DeviceName);
                        var isPrimary = (adapter.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;
                        var monitor = GetDisplayMonitor(adapter.DeviceName);
                        var pnpCode = ExtractMonitorPnpCode(monitor.DeviceID);
                        var label = ChooseDisplayLabel(monitor.DeviceString, pnpCode, adapter.DeviceString, wmiLabels);

                        if (!string.IsNullOrWhiteSpace(label) || !string.IsNullOrWhiteSpace(resolution))
                            results.Add(new DisplayInfo(label, resolution, isPrimary));
                    }

                    adapter = NewDisplayDevice();
                }
            }
            catch { }

            return results;
        }

        private static DISPLAY_DEVICE NewDisplayDevice()
        {
            var dd = new DISPLAY_DEVICE();
            dd.Size = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            return dd;
        }

        private static DISPLAY_DEVICE GetDisplayMonitor(string displayDeviceName)
        {
            DISPLAY_DEVICE fallback = new DISPLAY_DEVICE();
            bool hasFallback = false;
            var monitor = NewDisplayDevice();
            for (uint i = 0; EnumDisplayDevices(displayDeviceName, i, ref monitor, 0); i++)
            {
                if (!hasFallback) { fallback = monitor; hasFallback = true; }
                if ((monitor.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                    return monitor;
                monitor = NewDisplayDevice();
            }
            return hasFallback ? fallback : NewDisplayDevice();
        }

        private static string GetCurrentResolution(string displayDeviceName)
        {
            var mode = new DEVMODE();
            mode.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE));
            if (!EnumDisplaySettings(displayDeviceName, ENUM_CURRENT_SETTINGS, ref mode) ||
                mode.dmPelsWidth == 0 || mode.dmPelsHeight == 0)
                return null;
            return mode.dmPelsWidth + " x " + mode.dmPelsHeight;
        }

        private static string ChooseDisplayLabel(
            string monitorDeviceString, string pnpCode,
            string adapterDeviceString, Dictionary<string, string> wmiLabels)
        {
            var monitorLabel = (monitorDeviceString ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(pnpCode))
            {
                string wmiLabel;
                if (wmiLabels.TryGetValue(pnpCode, out wmiLabel) && !string.IsNullOrWhiteSpace(wmiLabel))
                    return wmiLabel;
            }

            if (!string.IsNullOrWhiteSpace(monitorLabel) && !IsGenericMonitorLabel(monitorLabel))
                return monitorLabel;

            var pnpMfr = pnpCode != null && pnpCode.Length >= 3 ? ResolveManufacturer(pnpCode.Substring(0, 3)) : null;
            if (!string.IsNullOrWhiteSpace(pnpMfr)) return pnpMfr;

            return "";
        }

        private static bool IsGenericMonitorLabel(string label)
        {
            return string.IsNullOrWhiteSpace(label) ||
                ContainsAny(label, "Generic PnP", "通用 PnP", "通用即插即用", "Default Monitor", "默认监视器");
        }

        private static Dictionary<string, string> GetWmiMonitorLabelsByPnpCode()
        {
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorID"))
                {
                    foreach (ManagementBaseObject item in searcher.Get())
                    {
                        var pnpCode = ExtractMonitorPnpCode(Get(item, "InstanceName"));
                        if (string.IsNullOrWhiteSpace(pnpCode) || labels.ContainsKey(pnpCode)) continue;
                        var label = BuildWmiMonitorLabel(item);
                        if (!string.IsNullOrWhiteSpace(label))
                            labels[pnpCode] = label;
                    }
                }
            }
            catch { }
            return labels;
        }

        private static string BuildWmiMonitorLabel(ManagementBaseObject item)
        {
            var mfr = DecodeWmiArray(item, "ManufacturerName");
            var product = DecodeWmiArray(item, "ProductName");
            var pnpCode = ExtractMonitorPnpCode(Get(item, "InstanceName"));

            var mfrLabel = ResolveManufacturer(mfr);
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(mfrLabel)) parts.Add(mfrLabel);
            if (!string.IsNullOrWhiteSpace(product) && product != mfrLabel) parts.Add(product);
            if (parts.Count == 0 && !string.IsNullOrWhiteSpace(pnpCode))
            {
                var pnpMfr = pnpCode.Length >= 3 ? ResolveManufacturer(pnpCode.Substring(0, 3)) : null;
                if (!string.IsNullOrWhiteSpace(pnpMfr)) parts.Add(pnpMfr);
            }

            return string.Join(" ", parts.Distinct()).Trim();
        }

        private static string ExtractMonitorPnpCode(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return "";
            var normalized = deviceId.Replace('#', '\\');
            var parts = normalized.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("DISPLAY", StringComparison.OrdinalIgnoreCase) ||
                    parts[i].Equals("MONITOR", StringComparison.OrdinalIgnoreCase))
                    return parts[i + 1];
            }
            foreach (var part in parts)
            {
                if (part.Length >= 3 && char.IsLetter(part[0]) && char.IsLetter(part[1]) && char.IsLetter(part[2]))
                    return part;
            }
            return "";
        }

        private static string DecodeWmiArray(ManagementBaseObject item, string propName)
        {
            try
            {
                var val = item[propName];
                if (val is ushort[])
                {
                    var arr = (ushort[])val;
                    var chars = new List<char>();
                    foreach (var c in arr) { if (c > 0) chars.Add((char)c); else break; }
                    return chars.Count > 0 ? new string(chars.ToArray()).Trim() : null;
                }
                return val != null ? val.ToString().Trim() : null;
            }
            catch { return null; }
        }

        private static string ResolveManufacturer(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            switch (code.Trim().ToUpperInvariant())
            {
                case "ABO": case "ACE": case "ACI": case "ACR": case "API": return "Acer(宏碁)";
                case "ACB": case "ACH": return "Achieva Shimian";
                case "AOC": case "AOC_": case "NRC": case "OTS": return "AOC(冠捷)";
                case "GBR": return "Arzopa";
                case "ASR": return "华擎(ASRock)";
                case "ASU": case "AUS": case "WWW": return "华硕(ASUS)";
                case "AUO": case "AUO_": case "DMO": case "CHR": return "友达(AU Optronics)";
                case "AVT": return "AVerMedia";
                case "AYA": return "AYANEO";
                case "BGO": return "Bangho";
                case "TOL": return "Beyond TV";
                case "CSP": return "Casper";
                case "CPL": case "WOR": return "COMPAL";
                case "CRM": return "海盗船(Corsair)";
                case "CRU": return "CRUA";
                case "CSO": return "华星光电(CSOT)";
                case "CMN": case "CMI": return "奇美(Chimei InnoLux)";
                case "DAE": case "DWE": case "PCK": return "大宇(Daewoo)";
                case "DAH": return "大华(Dahua)";
                case "DIS": case "DEL": case "LNK": return "Dell(戴尔)";
                case "DTV": return "Digital TV";
                case "DOS": case "DST": return "Dostyle";
                case "EIZ": case "ENC": return "Eizo(艺卓)";
                case "EIA": case "ELE": case "EMT": return "Element";
                case "YUN": return "Elgato";
                case "ELA": case "ELS": return "ELSA";
                case "ETG": return "Etigroup";
                case "EMA": case "EMI": return "eMachines";
                case "FAY": return "Faytech";
                case "FND": case "FDR": return "方正(Founder)";
                case "FPT": return "FPT";
                case "FNI": return "Funai";
                case "FUR": return "Furrion";
                case "GTW": case "GWY": return "Gateway";
                case "GMX": return "GameMax";
                case "GRE": return "GreBear";
                case "GRR": case "GRU": return "Grundig";
                case "HAI": case "HAI_": case "HAR": case "HRE": case "SRD": return "海信(Hisense)";
                case "HSD": case "HSP": return "瀚宇彩晶(HannStar)";
                case "HIK": return "海康威视(Hikvision)";
                case "HEC": case "HIT": case "HTC": return "日立(Hitachi)";
                case "HAT": case "HUI": case "HUN": return "绘王(Huion)";
                case "HIQ": case "IQT": return "现代(Hyundai ImageQuest)";
                case "INL": case "INX": return "群创(InnoLux Display)";
                case "INS": return "Insignia";
                case "HKM": return "Japannext";
                case "JRP": return "晶丽泰(JINGLITAI)";
                case "KAZ": return "KAZUK";
                case "LAC": case "LCA": return "LaCie";
                case "LCS": case "LEN": case "LEN_": case "LEO": case "LNV": case "QUA": case "QWA": return "联想(Lenovo)";
                case "LGD": case "LPL": case "LGP": case "GSM": return "LG Display";
                case "LOE": return "Loewe";
                case "MEA": case "MEB": case "MED": return "Medion";
                case "MAG": case "MAG_": case "MSI": return "微星(MSI)";
                case "NLK": case "MST": return "MStar";
                case "NLE": return "Newline";
                case "NSL": return "Newskill";
                case "NEW": return "Newsync";
                case "NIX": case "NTI": case "NXG": return "Nixeus";
                case "MRG": case "NRL": return "Nreal Air";
                case "BDL": return "OneMeeting";
                case "OPT": case "OTM": return "Optoma";
                case "YLT": case "MEI": case "MEL": return "松下(Panasonic)";
                case "PQA": return "PEAQ";
                case "PFL": case "PFT": case "PHA": case "PHG": case "PHI": case "PHL": case "PHP": case "PHT": case "PTS": return "飞利浦(Philips)";
                case "GDH": case "PLC": case "PHO": return "Philco";
                case "PXO": case "ICB": case "HYC": case "PNS": case "WAM": return "Pixio";
                case "HTB": case "PGS": case "PRT": return "Princeton";
                case "MKN": case "POL": return "Polaroid";
                case "NON": case "PCL": case "POS": return "Positivo";
                case "ASB": case "PRE": return "Prestigio";
                case "RAR": return "Raritan";
                case "LGE": case "SAM": case "SDC": case "SEC": case "SEM": case "SIM": case "STN": case "_YM": return "三星(Samsung)";
                case "XEC": return "SANSUI";
                case "KDD": case "SEK": return "Seiki";
                case "SHC": case "SHP": case "SHV": return "夏普(Sharp)";
                case "SKY": return "创维(Skyworth)";
                case "SNY": case "MS_": return "索尼(Sony)";
                case "SOT": return "SOTEC";
                case "SUE": return "SuperFrame";
                case "TFK": return "TELEFUNKEN";
                case "PKV": case "TMN": case "TTE": return "Thomson";
                case "TRG": return "雷神(ThundeRobot)";
                case "LCD": case "TOS": case "TSB": return "东芝(Toshiba)";
                case "UPV": return "UPlusVision";
                case "XYA": return "Valday";
                case "IZI": case "VIZ": case "VZO": return "Vizio";
                case "JRY": return "VIZTA";
                case "WDE": case "WDT": case "WEH": case "WET": return "Westinghouse";
                case "WIP": return "Wipro";
                case "YSI": return "Yashi";
                case "BOE": case "BOE_": return "京东方(BOE)";
                case "HKC": return "HKC(惠科)";
                case "IVO": return "天马(IVO)";
                case "HWP": case "HEW": return "HP(惠普)";
                case "CSW": case "GWR": case "GWR_": return "长城(Great Wall)";
                case "HPC": return "惠浦(HPC)";
                case "VSC": return "优派(ViewSonic)";
                case "VIT": return "唯冠(VIT)";
                case "IMA": return "理想(IMA)";
                case "NEX": return "NEXO";
                case "ELO": return "Elo Touch";
                case "FUJ": case "FUS": return "富士通(Fujitsu)";
                case "GGL": return "Google";
                case "HHT": return "HHI";
                case "JDI": case "JDI_": return "日本显示器(JDI)";
                case "OEM": return "OEM";
                case "PBN": return "Packard Bell";
                case "QDS": return "Quanta Display";
                case "SPT": return "Sceptre";
                case "SUN": return "Sun";
                case "UNM": return "Unisys";
                case "VES": return "Vestel";
                case "ZCM": return "Zenith";
                default: return code;
            }
        }

        private static string FormatUptime()
        {
            var tickCount = Environment.TickCount & 0x7FFFFFFF;
            var uptime = TimeSpan.FromMilliseconds(tickCount);
            return uptime.Days + "天" + uptime.Hours + "小时" + uptime.Minutes + "分钟" + uptime.Seconds + "秒";
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

        private static string CleanBoardManufacturer(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var cleaned = raw.Trim();
            switch (cleaned.ToUpperInvariant())
            {
                case "ASUS": case "ASUSTEK": case "ASUSTEK COMPUTER INC.": return "华硕(ASUS)";
                case "MSI": case "MICRO-STAR INTERNATIONAL": case "MICRO-STAR INTERNATIONAL CO., LTD": return "微星(MSI)";
                case "GIGABYTE": case "GIGABYTE TECHNOLOGY CO., LTD.": return "技嘉(Gigabyte)";
                case "ASROCK": case "ASROCK INC.": return "华擎(ASRock)";
                case "COLORFUL": case "COLORFUL TECHNOLOGY CO., LTD": return "七彩虹(Colorful)";
                case "MAXSUN": case "MAXSUN TECHNOLOGY CO., LTD.": return "铭瑄(Maxsun)";
                case "SOYO": case "SOYO TECHNOLOGY CO., LTD.": return "梅捷(Soyo)";
                case "ONDA": case "ONDA TECHNOLOGY CO., LTD.": return "昂达(Onda)";
                case "YESTON": case "YESTON TECHNOLOGY CO., LTD.": return "盈通(Yeston)";
                case "FOXCONN": case "FOXCONN TECHNOLOGY INC.": return "富士康(Foxconn)";
                case "INTEL": case "INTEL CORPORATION": return "英特尔(Intel)";
                case "DELL": case "DELL INC.": return "戴尔(Dell)";
                case "HP": case "HEWLETT-PACKARD": case "HP INC.": return "惠普(HP)";
                case "LENOVO": case "LENOVO PRODUCT": return "联想(Lenovo)";
                case "HUAWEI": return "华为(Huawei)";
                case "XIAOMI": return "小米(Xiaomi)";
                default: return cleaned;
            }
        }

        private static string JoinNames(string className, Func<ManagementBaseObject, bool> filter = null)
        {
            var names = Query(className)
                .Where(item => filter == null || filter(item))
                .Select(item => Get(item, "Name"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct();

            return string.Join(GetSeparator(), names);
        }

        private static ManagementBaseObject First(string className)
        {
            return Query(className).FirstOrDefault();
        }

        private static IEnumerable<ManagementBaseObject> Query(string className)
        {
            var result = new List<ManagementBaseObject>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM " + className))
                {
                    foreach (ManagementBaseObject item in searcher.Get())
                        result.Add(item);
                }
            }
            catch { }
            return result;
        }

        private static string Get(ManagementBaseObject item, string propertyName)
        {
            try
            {
                if (item == null) return null;
                var val = item[propertyName];
                return val != null ? val.ToString().Trim() : null;
            }
            catch { return null; }
        }

        private static bool IsTrue(ManagementBaseObject item, string propertyName)
        {
            bool value;
            return bool.TryParse(Get(item, propertyName), out value) && value;
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            foreach (var needle in needles)
            {
                if (value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static long ToLong(string value)
        {
            long number;
            return long.TryParse(value, out number) ? number : 0;
        }

        private static int ToInt(string value)
        {
            int number;
            return int.TryParse(value, out number) ? number : 0;
        }

        private static string Join(params string[] values)
        {
            var parts = new List<string>();
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    parts.Add(value);
            }
            return string.Join(" ", parts);
        }
    }
}
