using System.Diagnostics.CodeAnalysis;
using System.Management;
using System.Runtime.InteropServices;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "WMI System.Management requires reflection at runtime")]
[UnconditionalSuppressMessage("Aot", "IL3050:RequiresDynamicCode", Justification = "WMI COM interop requires dynamic code")]
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
    private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    private const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory(ref Guid riid, out IntPtr ppFactory);

    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid IID_IDXGIFactory = new("7b7166ec-21c7-44ae-b21a-c9ae321ae369");

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public IntPtr DedicatedVideoMemory;
        public IntPtr DedicatedSystemMemory;
        public IntPtr SharedSystemMemory;
        public LUID AdapterLuid;
    }

    private delegate int EnumAdapters1Delegate(IntPtr pFactory, uint adapterIndex, out IntPtr ppAdapter);
    private delegate int GetDescDelegate(IntPtr pAdapter, out DXGI_ADAPTER_DESC pDesc);

    private static unsafe (string name, ulong dedicatedVram, ulong sharedVram)[] EnumerateDxgiAdapters()
    {
        var results = new List<(string, ulong, ulong)>();

        IntPtr factoryPtr = IntPtr.Zero;
        var iidFactory1 = IID_IDXGIFactory1;
        int hr = CreateDXGIFactory1(ref iidFactory1, out factoryPtr);
        if (hr < 0)
        {
            var iidFactory = IID_IDXGIFactory;
            hr = CreateDXGIFactory(ref iidFactory, out factoryPtr);
            if (hr < 0) return results.ToArray();
        }

        try
        {
            for (uint i = 0; ; i++)
            {
                IntPtr vtable = Marshal.ReadIntPtr(factoryPtr);
                IntPtr methodPtr = Marshal.ReadIntPtr(vtable + 12 * IntPtr.Size);
                var enumAdapters1 = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(methodPtr);

                hr = enumAdapters1(factoryPtr, i, out IntPtr adapterPtr);
                if (hr < 0) break;

                try
                {
                    IntPtr adapterVtable = Marshal.ReadIntPtr(adapterPtr);
                    IntPtr getDescPtr = Marshal.ReadIntPtr(adapterVtable + 8 * IntPtr.Size);
                    var getDesc = Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(getDescPtr);

                    DXGI_ADAPTER_DESC desc = default;
                    hr = getDesc(adapterPtr, out desc);
                    if (hr >= 0)
                    {
                        results.Add((desc.Description, (ulong)(long)desc.DedicatedVideoMemory, (ulong)(long)desc.SharedSystemMemory));
                    }
                }
                finally
                {
                    Marshal.Release(adapterPtr);
                }
            }
        }
        finally
        {
            Marshal.Release(factoryPtr);
        }

        return results.ToArray();
    }

    private static IReadOnlyList<HardwareInfoSection>? _cache;
    private static readonly object _lock = new();

    private static string GetSeparator()
    {
        return AppSettings.GetBool("HardwareMultiDeviceNewLine", false) ? Environment.NewLine : " / ";
    }

    public static bool HasCache
    {
        get { lock (_lock) { return _cache != null; } }
    }

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
        return Task.Run(() => BuildSections(forceRefresh));
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

    public static IReadOnlyList<HardwareInfoSection> ApplyCpuzOverride(IReadOnlyList<HardwareInfoSection> wmiSections, CpuzInfo cpuz)
    {
        var sections = DeepCopy(wmiSections);
        var details = sections[2].Items;

        if (!string.IsNullOrWhiteSpace(cpuz.CpuName))
        {
            var cpuItem = details.FirstOrDefault(it => it.Label == "处理器");
            if (cpuItem != null)
            {
                var name = cpuz.CpuName;
                if (!string.IsNullOrWhiteSpace(cpuz.CpuCodeName))
                    name += $" ({cpuz.CpuCodeName})";
                if (cpuz.CpuCores > 0)
                    name += $" {CoresThreadsLabel(cpuz)}";
                cpuItem.Value = name;
                cpuItem.BrandKey = DetectCpuBrand(cpuz.CpuName);
                cpuItem.IsVerified = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(cpuz.BoardManufacturer) || !string.IsNullOrWhiteSpace(cpuz.BoardModel))
        {
            var boardItem = details.FirstOrDefault(it => it.Label == "主板");
            if (boardItem != null)
            {
                var board = Join(
                    CleanBoardManufacturer(cpuz.BoardManufacturer),
                    cpuz.BoardModel);
                if (!string.IsNullOrWhiteSpace(board))
                {
                    boardItem.Value = board;
                    boardItem.IsVerified = true;
                }
            }
        }

        var summary = sections[0].Items;
        if (!string.IsNullOrWhiteSpace(cpuz.BoardManufacturer) || !string.IsNullOrWhiteSpace(cpuz.BoardModel))
        {
            var summaryBoard = summary.FirstOrDefault(it => it.Label == "主板");
            if (summaryBoard != null)
            {
                var board = Join(
                    CleanBoardManufacturer(cpuz.BoardManufacturer),
                    cpuz.BoardModel);
                if (!string.IsNullOrWhiteSpace(board))
                {
                    summaryBoard.Value = board;
                    summaryBoard.IsVerified = true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(cpuz.BiosBrand) || !string.IsNullOrWhiteSpace(cpuz.BiosVersion))
        {
            var biosItem = summary.FirstOrDefault(it => it.Label == "BIOS");
            if (biosItem != null)
            {
                var bios = Join(cpuz.BiosBrand, cpuz.BiosVersion);
                if (!string.IsNullOrWhiteSpace(bios))
                {
                    biosItem.Value = bios;
                    biosItem.IsVerified = true;
                }
            }
        }

        if (cpuz.Gpus.Count > 0)
        {
            var gpuItem = details.FirstOrDefault(it => it.Label == "显卡");
            if (gpuItem != null)
            {
                var gpuLabel = string.Join(GetSeparator(), cpuz.Gpus
                    .Where(g => !string.IsNullOrWhiteSpace(g.Name))
                    .Select(g =>
                    {
                        var s = g.Name!;
                        if (!string.IsNullOrWhiteSpace(g.MemorySize))
                            s += $" ({g.MemorySize})";
                        return s;
                    }));
                if (!string.IsNullOrWhiteSpace(gpuLabel))
                {
                    gpuItem.Value = gpuLabel;
                    gpuItem.BrandKey = DetectGpuBrand(cpuz.Gpus[0].Name);
                    gpuItem.IsVerified = true;
                }
            }
        }

        if (cpuz.MemDevices.Count > 0 || !string.IsNullOrWhiteSpace(cpuz.MemoryType))
        {
            var memItem = details.FirstOrDefault(it => it.Label == "内存");
            if (memItem != null)
            {
                var memLabel = BuildCpuzMemoryLabel(cpuz);
                if (!string.IsNullOrWhiteSpace(memLabel))
                {
                    memItem.Value = memLabel;
                    memItem.IsVerified = true;
                }
            }
        }

        return sections;
    }

    private static string CoresThreadsLabel(CpuzInfo cpuz)
    {
        if (cpuz.CpuCores <= 0) return "";
        return cpuz.CpuThreads > cpuz.CpuCores
            ? $"{cpuz.CpuCores}C/{cpuz.CpuThreads}T"
            : $"{cpuz.CpuCores}C";
    }

    private static string BuildCpuzMemoryLabel(CpuzInfo cpuz)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(cpuz.MemoryType))
            parts.Add(cpuz.MemoryType);

        if (!string.IsNullOrWhiteSpace(cpuz.MemorySize))
            parts.Add(cpuz.MemorySize);

        if (!string.IsNullOrWhiteSpace(cpuz.MemorySpeed))
            parts.Add(cpuz.MemorySpeed);

        if (cpuz.MemDevices.Count > 0)
        {
            var mfr = cpuz.MemDevices
                .Select(e => CleanMemManufacturer(e.Manufacturer))
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
            if (!string.IsNullOrWhiteSpace(mfr) && !parts.Any(p => p.Contains(mfr.Split('(')[0])))
                parts.Insert(0, mfr);
        }

        return string.Join(" ", parts);
    }

    private static List<HardwareInfoSection> DeepCopy(IReadOnlyList<HardwareInfoSection> source)
    {
        var result = new List<HardwareInfoSection>(source.Count);
        foreach (var section in source)
        {
            var newSection = new HardwareInfoSection
            {
                Title = section.Title,
                Glyph = section.Glyph
            };
            foreach (var item in section.Items)
            {
                newSection.Items.Add(new HardwareInfoItem
                {
                    Label = item.Label,
                    Value = item.Value,
                    BrandKey = item.BrandKey,
                    IsVerified = item.IsVerified
                });
            }
            result.Add(newSection);
        }
        return result;
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
        var cpuName = FirstName("Win32_Processor");
        var cpuItem = Item("处理器", cpuName);
        cpuItem.BrandKey = DetectCpuBrand(cpuName);
        section.Items.Add(cpuItem);
        section.Items.Add(Item("内存", FormatMemory()));
        var gpuDisplay = BuildGpuDisplayText();
        var gpuItem = Item("显卡", gpuDisplay);
        gpuItem.BrandKey = DetectGpuBrand(gpuDisplay);
        section.Items.Add(gpuItem);
        var npuName = DetectNpuName();
        if (npuName != null)
            section.Items.Add(Item("NPU", npuName));
        section.Items.Add(Item("显示器", FormatDisplays()));
        section.Items.Add(Item("硬盘", FormatDisks()));
        section.Items.Add(Item("声卡", JoinNames("Win32_SoundDevice", item =>
        {
            var name = Get(item, "Name");
            return !ContainsAny(name, "Virtual", "虚拟", "Software", "Remote Audio", "Stereo Mix", "Wave", "VB-Audio", "VBAN", "Voicemeeter", "CABLE", "VAC", "Senary Audio", "Nahimic Easy Surround", "Nahimic mirroring", "USB 音频", "蓝牙音频", "蓝牙");
        })));
        section.Items.Add(Item("网卡", JoinNames("Win32_NetworkAdapter", item =>
            IsTrue(item, "PhysicalAdapter") &&
            !ContainsAny(Get(item, "Name"), "Virtual", "Bluetooth", "WAN Miniport"))));
    }

    private static string? DetectCpuBrand(string? cpuName)
    {
        if (string.IsNullOrWhiteSpace(cpuName)) return null;
        var name = cpuName.ToUpperInvariant();
        if (name.Contains("INTEL")) return "intel";
        if (name.Contains("AMD")) return "amd";
        if (name.Contains("APPLE") || name.Contains("M1") || name.Contains("M2") || name.Contains("M3") || name.Contains("M4")) return "apple";
        if (name.Contains("QUALCOMM") || name.Contains("SNAPDRAGON")) return "qualcomm";
        return null;
    }

    private static string? BuildGpuDisplayText()
    {
        (string name, ulong dedicatedVram, ulong sharedVram)[] dxgiAdapters;
        try { dxgiAdapters = EnumerateDxgiAdapters(); }
        catch { dxgiAdapters = Array.Empty<(string, ulong, ulong)>(); }

        var parts = new List<string>();
        foreach (var item in Query("Win32_VideoController"))
        {
            var name = Get(item, "Name");
            if (ContainsAny(name, "Microsoft Basic Render", "Microsoft Remote Display", "DDA Wrapper",
                "Idd Desk", "GameViewer Virtual Display", "Honor Virtual Display", "Virtual Display",
                "Virtual GPU", "Virtual Adapter", "虚拟", "Remote Display Adapter"))
                continue;

            var display = name;

            if (name != null && TryMatchDxgiAdapter(name, dxgiAdapters, out int dxgiIdx))
            {
                var dedicated = dxgiAdapters[dxgiIdx].dedicatedVram;
                var shared = dxgiAdapters[dxgiIdx].sharedVram;
                if (dedicated > 0)
                    display += $" ({dedicated / 1024d / 1024d / 1024d:0.#} GB)";
                else if (shared > 0)
                    display += $" (共享 {shared / 1024d / 1024d / 1024d:0.#} GB)";
            }

            parts.Add(display!);
        }

        return parts.Count > 0 ? string.Join(GetSeparator(), parts) : null;
    }

    private static string? DetectNpuName()
    {
        foreach (var item in Query("Win32_PnPEntity"))
        {
            var pnpClass = Get(item, "PNPClass");
            if (!string.Equals(pnpClass, "ComputeAccelerator", StringComparison.OrdinalIgnoreCase))
                continue;
            var name = Get(item, "Name");
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        return null;
    }

    private static string? DetectGpuBrand(string? gpuName)
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
            .Select(item => GetMemoryConfiguredClockSpeed(item))
            .Where(mhz => mhz > 0)
            .Distinct()
            .OrderByDescending(mhz => mhz)
            .ToList();

        var speedLabel = speeds.Count switch
        {
            0 => "",
            1 => prefix.Length > 0 ? $"{prefix}-{speeds[0]} MHz" : $"{speeds[0]} MHz",
            _ => prefix.Length > 0
                ? string.Join("/", speeds.Select(s => $"{prefix}-{s} MHz"))
                : string.Join("/", speeds.Select(s => $"{s} MHz"))
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

    private static int GetMemoryConfiguredClockSpeed(ManagementBaseObject item)
    {
        return ToInt(Get(item, "ConfiguredClockSpeed"));
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

        return string.Join(GetSeparator(), disks);
    }

    private static string FormatDisplays()
    {
        var monitorInfos = GetActiveDisplayInfos();

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
                monitorInfos.Add(new DisplayInfo(pnpNames[i]!, res, false, null));
            }
        }

        if (monitorInfos.Count == 0) return "未知";

        return string.Join(GetSeparator(), monitorInfos.Select(mi =>
        {
            if (string.IsNullOrWhiteSpace(mi.Label) && string.IsNullOrWhiteSpace(mi.Resolution))
                return "";
            var label = mi.IsPrimary && !string.IsNullOrWhiteSpace(mi.Label) ? $"主屏 {mi.Label}" : mi.Label;
            var sizeStr = mi.DiagonalInches.HasValue ? $"{mi.DiagonalInches.Value:F1}\"" : null;
            var resOrSize = new List<string>();
            if (!string.IsNullOrWhiteSpace(sizeStr)) resOrSize.Add(sizeStr);
            if (!string.IsNullOrWhiteSpace(mi.Resolution)) resOrSize.Add(mi.Resolution);
            var bracketContent = resOrSize.Count > 0 ? string.Join(" ", resOrSize) : null;
            if (string.IsNullOrWhiteSpace(label)) return bracketContent ?? "";
            if (string.IsNullOrWhiteSpace(bracketContent)) return label;
            return $"{label} [{bracketContent}]";
        }).Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private sealed record DisplayInfo(string Label, string? Resolution, bool IsPrimary, double? DiagonalInches);

    private static List<DisplayInfo> GetActiveDisplayInfos()
    {
        var results = new List<DisplayInfo>();
        var wmiLabels = GetWmiMonitorLabelsByPnpCode();
        var wmiSizes = GetWmiMonitorSizesByPnpCode();

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
                    var pnpCode = ExtractMonitorPnpCode(monitor?.DeviceID);
                    var label = ChooseDisplayLabel(monitor?.DeviceString, pnpCode, adapter.DeviceString, wmiLabels);
                    var diagonalInches = GetDiagonalInches(pnpCode, wmiSizes);

                    if (!string.IsNullOrWhiteSpace(label) || !string.IsNullOrWhiteSpace(resolution))
                    {
                        results.Add(new DisplayInfo(label, resolution, isPrimary, diagonalInches));
                    }
                }

                adapter = NewDisplayDevice();
            }
        }
        catch { }

        return results;
    }

    private static DISPLAY_DEVICE NewDisplayDevice() => new() { Size = Marshal.SizeOf<DISPLAY_DEVICE>() };

    private static DISPLAY_DEVICE? GetDisplayMonitor(string displayDeviceName)
    {
        DISPLAY_DEVICE? fallback = null;
        var monitor = NewDisplayDevice();
        for (uint i = 0; EnumDisplayDevices(displayDeviceName, i, ref monitor, 0); i++)
        {
            if (fallback == null) fallback = monitor;
            if ((monitor.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
            {
                return monitor;
            }

            monitor = NewDisplayDevice();
        }

        return fallback;
    }

    private static string? GetCurrentResolution(string displayDeviceName)
    {
        var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(displayDeviceName, ENUM_CURRENT_SETTINGS, ref mode) ||
            mode.dmPelsWidth == 0 ||
            mode.dmPelsHeight == 0)
        {
            return null;
        }

        return $"{mode.dmPelsWidth} x {mode.dmPelsHeight}";
    }

    private static string ChooseDisplayLabel(
        string? monitorDeviceString,
        string? pnpCode,
        string? adapterDeviceString,
        IReadOnlyDictionary<string, string> wmiLabels)
    {
        var monitorLabel = CleanDisplayLabel(monitorDeviceString);
        if (!string.IsNullOrWhiteSpace(pnpCode) &&
            wmiLabels.TryGetValue(pnpCode, out var wmiLabel) &&
            !string.IsNullOrWhiteSpace(wmiLabel))
        {
            return wmiLabel;
        }

        if (!string.IsNullOrWhiteSpace(monitorLabel) && !IsGenericMonitorLabel(monitorLabel))
        {
            return monitorLabel;
        }

        var pnpMfr = pnpCode?.Length >= 3 ? ResolveManufacturer(pnpCode[..3]) : null;
        if (!string.IsNullOrWhiteSpace(pnpMfr)) return pnpMfr;

        return "";
    }

    private static string CleanDisplayLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "";
        return label.Trim();
    }

    private static bool IsGenericMonitorLabel(string? label)
    {
        return string.IsNullOrWhiteSpace(label) ||
            ContainsAny(label, "Generic PnP", "通用 PnP", "通用即插即用", "Default Monitor", "默认监视器");
    }

    private static Dictionary<string, string> GetWmiMonitorLabelsByPnpCode()
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorID");
            foreach (ManagementBaseObject item in searcher.Get())
            {
                var pnpCode = ExtractMonitorPnpCode(Get(item, "InstanceName"));
                if (string.IsNullOrWhiteSpace(pnpCode) || labels.ContainsKey(pnpCode)) continue;

                var label = BuildWmiMonitorLabel(item);
                if (!string.IsNullOrWhiteSpace(label))
                {
                    labels[pnpCode] = label;
                }
            }
        }
        catch { }

        return labels;
    }

    private static Dictionary<string, (double WidthCm, double HeightCm)> GetWmiMonitorSizesByPnpCode()
    {
        var sizes = new Dictionary<string, (double WidthCm, double HeightCm)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBasicDisplayParams");
            foreach (ManagementBaseObject item in searcher.Get())
            {
                var pnpCode = ExtractMonitorPnpCode(Get(item, "InstanceName"));
                if (string.IsNullOrWhiteSpace(pnpCode) || sizes.ContainsKey(pnpCode)) continue;

                var widthCm = GetInt(item, "MaxHorizontalImageSize");
                var heightCm = GetInt(item, "MaxVerticalImageSize");
                if (widthCm > 0 && heightCm > 0)
                {
                    sizes[pnpCode] = (widthCm, heightCm);
                }
            }
        }
        catch { }

        return sizes;
    }

    private static int GetInt(ManagementBaseObject item, string propertyName)
    {
        try
        {
            var value = item[propertyName];
            if (value != null)
            {
                return Convert.ToInt32(value);
            }
        }
        catch { }
        return 0;
    }

    private static double? GetDiagonalInches(string? pnpCode, IReadOnlyDictionary<string, (double WidthCm, double HeightCm)> sizes)
    {
        if (string.IsNullOrWhiteSpace(pnpCode) || !sizes.TryGetValue(pnpCode, out var size))
            return null;

        if (size.WidthCm <= 0 || size.HeightCm <= 0)
            return null;

        var diagonalCm = Math.Sqrt(size.WidthCm * size.WidthCm + size.HeightCm * size.HeightCm);
        var diagonalInches = diagonalCm / 2.54;
        return diagonalInches;
    }

    private static string BuildWmiMonitorLabel(ManagementBaseObject item)
    {
        var mfr = DecodeWmiArray(item, "ManufacturerName");
        var product = DecodeWmiArray(item, "ProductName");
        var serial = DecodeWmiArray(item, "SerialNumberID");
        var pnpCode = ExtractMonitorPnpCode(Get(item, "InstanceName"));

        var mfrLabel = ResolveManufacturer(mfr);
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(mfrLabel)) parts.Add(mfrLabel);
        if (!string.IsNullOrWhiteSpace(product) && product != mfrLabel) parts.Add(product);
        if (parts.Count == 0 && !string.IsNullOrWhiteSpace(pnpCode))
        {
            var pnpMfr = ResolveManufacturer(pnpCode.Length >= 3 ? pnpCode[..3] : pnpCode);
            if (!string.IsNullOrWhiteSpace(pnpMfr)) parts.Add(pnpMfr);
        }

        var label = string.Join(" ", parts.Distinct());
        if (!string.IsNullOrWhiteSpace(serial) && serial != "0") label += $" (SN:{serial})";
        return label.Trim();
    }

    private static string ExtractMonitorPnpCode(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return "";

        var normalized = deviceId.Replace('#', '\\');
        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("DISPLAY", StringComparison.OrdinalIgnoreCase) ||
                parts[i].Equals("MONITOR", StringComparison.OrdinalIgnoreCase))
            {
                return parts[i + 1];
            }
        }

        return parts.FirstOrDefault(part => part.Length >= 3 && char.IsLetter(part[0]) && char.IsLetter(part[1]) && char.IsLetter(part[2])) ?? "";
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
            "ABO" or "ACE" or "ACI" or "ACR" or "API" => "Acer(宏碁)",
            "ACB" or "ACH" => "Achieva Shimian",
            "AOC" or "AOC_" or "NRC" or "OTS" => "AOC(冠捷)",
            "GBR" => "Arzopa",
            "ASR" => "华擎(ASRock)",
            "ASU" or "AUS" or "WWW" => "华硕(ASUS)",
            "AUO" or "AUO_" or "DMO" or "CHR" => "友达(AU Optronics)",
            "AVT" => "AVerMedia",
            "AYA" => "AYANEO",
            "BGO" => "Bangho",
            "TOL" => "Beyond TV",
            "CSP" => "Casper",
            "CPL" or "WOR" => "COMPAL",
            "CRM" => "海盗船(Corsair)",
            "CRU" => "CRUA",
            "CSO" => "华星光电(CSOT)",
            "CMN" or "CMI" => "奇美(Chimei InnoLux)",
            "DAE" or "DWE" or "PCK" => "大宇(Daewoo)",
            "DAH" => "大华(Dahua)",
            "DIS" or "DEL" or "LNK" => "Dell(戴尔)",
            "DTV" => "Digital TV",
            "DOS" or "DST" => "Dostyle",
            "EIZ" or "ENC" => "Eizo(艺卓)",
            "EIA" or "ELE" or "EMT" => "Element",
            "YUN" => "Elgato",
            "ELA" or "ELS" => "ELSA",
            "ETG" => "Etigroup",
            "EMA" or "EMI" => "eMachines",
            "FAY" => "Faytech",
            "FND" or "FDR" => "方正(Founder)",
            "FPT" => "FPT",
            "FNI" => "Funai",
            "FUR" => "Furrion",
            "GTW" or "GWY" => "Gateway",
            "GMX" => "GameMax",
            "GRE" => "GreBear",
            "GRR" or "GRU" => "Grundig",
            "HAI" or "HAI_" or "HAR" or "HRE" or "SRD" => "海信(Hisense)",
            "HSD" or "HSP" => "瀚宇彩晶(HannStar)",
            "HIK" => "海康威视(Hikvision)",
            "HEC" or "HIT" or "HTC" => "日立(Hitachi)",
            "HAT" or "HUI" or "HUN" => "绘王(Huion)",
            "HIQ" or "IQT" => "现代(Hyundai ImageQuest)",
            "INL" or "INX" => "群创(InnoLux Display)",
            "INS" => "Insignia",
            "HKM" => "Japannext",
            "JRP" => "晶丽泰(JINGLITAI)",
            "KAZ" => "KAZUK",
            "LAC" or "LCA" => "LaCie",
            "LCS" or "LEN" or "LEN_" or "LEO" or "LNV" or "QUA" or "QWA" => "联想(Lenovo)",
                "LGD" or "LPL" or "LGP" or "GSM" => "LG Display",
            "LOE" => "Loewe",
            "MEA" or "MEB" or "MED" => "Medion",
            "MAG" or "MAG_" or "MSI" => "微星(MSI)",
            "NLK" or "MST" => "MStar",
            "NLE" => "Newline",
            "NSL" => "Newskill",
            "NEW" => "Newsync",
            "NIX" or "NTI" or "NXG" => "Nixeus",
            "MRG" or "NRL" => "Nreal Air",
            "BDL" => "OneMeeting",
            "OPT" or "OTM" => "Optoma",
            "YLT" or "MEI" or "MEL" => "松下(Panasonic)",
            "PQA" => "PEAQ",
            "PFL" or "PFT" or "PHA" or "PHG" or "PHI" or "PHL" or "PHP" or "PHT" or "PTS" => "飞利浦(Philips)",
            "GDH" or "PLC" or "PHO" => "Philco",
            "PXO" or "ICB" or "HYC" or "PNS" or "WAM" => "Pixio",
            "HTB" or "PGS" or "PRT" => "Princeton",
            "MKN" or "POL" => "Polaroid",
            "NON" or "PCL" or "POS" => "Positivo",
            "ASB" or "PRE" => "Prestigio",
            "RAR" => "Raritan",
            "LGE" or "SAM" or "SDC" or "SEC" or "SEM" or "SIM" or "STN" or "_YM" => "三星(Samsung)",
            "XEC" => "SANSUI",
            "KDD" or "SEK" => "Seiki",
            "SHC" or "SHP" or "SHV" => "夏普(Sharp)",
            "SKY" => "创维(Skyworth)",
            "SNY" or "MS_" => "索尼(Sony)",
            "SOT" => "SOTEC",
            "SUE" => "SuperFrame",
            "TFK" => "TELEFUNKEN",
            "PKV" or "TMN" or "TTE" => "Thomson",
            "TRG" => "雷神(ThundeRobot)",
            "LCD" or "TOS" or "TSB" => "东芝(Toshiba)",
            "UPV" => "UPlusVision",
            "XYA" => "Valday",
            "IZI" or "VIZ" or "VZO" => "Vizio",
            "JRY" => "VIZTA",
            "WDE" or "WDT" or "WEH" or "WET" => "Westinghouse",
            "WIP" => "Wipro",
            "YSI" => "Yashi",
            "BOE" or "BOE_" => "京东方(BOE)",
            "HKC" => "HKC(惠科)",
            "IVO" => "天马(IVO)",
            "HWP" or "HEW" => "HP(惠普)",
            "CSW" or "GWR" or "GWR_" => "长城(Great Wall)",
            "HPC" => "惠浦(HPC)",
            "VSC" => "优派(ViewSonic)",
            "VIT" => "唯冠(VIT)",
            "IMA" => "理想(IMA)",
            "NEX" => "NEXO",
            "ELO" => "Elo Touch",
            "FUJ" or "FUS" => "富士通(Fujitsu)",
            "GGL" => "Google",
            "HHT" => "HHI",
            "JDI" or "JDI_" => "日本显示器(JDI)",
            "OEM" => "OEM",
            "PBN" => "Packard Bell",
            "QDS" => "Quanta Display",
            "SPT" => "Sceptre",
            "SUN" => "Sun",
            "UNM" => "Unisys",
            "VES" => "Vestel",
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

        return string.Join(GetSeparator(), names);
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

    #region Detail Data

    private static HardwareDetailData? _detailCache;

    public static Task<HardwareDetailData> LoadDetailAsync(bool forceRefresh = false)
    {
        return Task.Run(() => BuildDetailData(forceRefresh));
    }

    private static HardwareDetailData BuildDetailData(bool forceRefresh)
    {
        if (!forceRefresh && _detailCache != null)
            return _detailCache;

        var data = new HardwareDetailData
        {
            Cpu = BuildCpuDetail(),
            Motherboard = BuildMotherboardDetail(),
            Memory = BuildMemoryDetail(),
            Gpus = BuildGpuDetails(),
            Disks = BuildDiskDetails(),
            Displays = BuildDisplayDetails(),
            SoundDevices = BuildSoundDetails(),
            NetworkAdapters = BuildNetworkDetails(),
            Npu = BuildNpuDetail()
        };

        _detailCache = data;
        return data;
    }

    public static HardwareDetailData ApplyCpuzDetailOverride(HardwareDetailData data, CpuzInfo cpuz)
    {
        if (cpuz == null) return data;

        if (data.Cpu != null)
        {
            if (!string.IsNullOrWhiteSpace(cpuz.CpuName))
            {
                data.Cpu.Name = cpuz.CpuName;
                data.Cpu.BrandKey = DetectCpuBrand(cpuz.CpuName);
            }
            if (!string.IsNullOrWhiteSpace(cpuz.CpuCodeName))
                data.Cpu.CodeName = cpuz.CpuCodeName;
            if (!string.IsNullOrWhiteSpace(cpuz.CpuPackage))
                data.Cpu.Package = cpuz.CpuPackage;
            if (cpuz.CpuCores > 0)
                data.Cpu.Cores = cpuz.CpuCores;
            if (cpuz.CpuThreads > 0)
                data.Cpu.Threads = cpuz.CpuThreads;
            data.Cpu.IsVerified = true;
        }

        if (data.Motherboard != null)
        {
            if (!string.IsNullOrWhiteSpace(cpuz.BoardManufacturer))
                data.Motherboard.Manufacturer = CleanBoardManufacturer(cpuz.BoardManufacturer);
            if (!string.IsNullOrWhiteSpace(cpuz.BoardModel))
                data.Motherboard.Model = cpuz.BoardModel;
            if (!string.IsNullOrWhiteSpace(cpuz.BoardChipset))
                data.Motherboard.Chipset = cpuz.BoardChipset;
            if (!string.IsNullOrWhiteSpace(cpuz.BiosBrand))
                data.Motherboard.BiosBrand = cpuz.BiosBrand;
            if (!string.IsNullOrWhiteSpace(cpuz.BiosVersion))
                data.Motherboard.BiosVersion = cpuz.BiosVersion;
            data.Motherboard.IsVerified = true;
        }

        if (!string.IsNullOrWhiteSpace(cpuz.MemoryType))
            data.Memory.MemoryType = cpuz.MemoryType;
        if (!string.IsNullOrWhiteSpace(cpuz.MemorySize))
            data.Memory.TotalCapacity = cpuz.MemorySize;
        if (!string.IsNullOrWhiteSpace(cpuz.MemorySpeed))
        {
            foreach (var mod in data.Memory.Modules)
                mod.Speed = cpuz.MemorySpeed;
        }
        if (!string.IsNullOrWhiteSpace(cpuz.MemoryChannel))
            data.Memory.ChannelMode = cpuz.MemoryChannel;

        if (cpuz.MemDevices.Count > 0)
        {
            for (int i = 0; i < Math.Min(cpuz.MemDevices.Count, data.Memory.Modules.Count); i++)
            {
                var src = cpuz.MemDevices[i];
                var dst = data.Memory.Modules[i];
                if (!string.IsNullOrWhiteSpace(src.Designation)) dst.Designation = src.Designation;
                if (!string.IsNullOrWhiteSpace(src.Type)) dst.Type = src.Type;
                if (!string.IsNullOrWhiteSpace(src.Size)) dst.Capacity = src.Size;
                if (!string.IsNullOrWhiteSpace(src.Speed)) dst.Speed = src.Speed;
                if (!string.IsNullOrWhiteSpace(src.Manufacturer)) dst.Manufacturer = CleanMemManufacturer(src.Manufacturer);
                if (!string.IsNullOrWhiteSpace(src.PartNumber)) dst.PartNumber = src.PartNumber;
            }
        }

        if (cpuz.Gpus.Count > 0 && data.Gpus.Count > 0)
        {
            for (int i = 0; i < Math.Min(cpuz.Gpus.Count, data.Gpus.Count); i++)
            {
                var src = cpuz.Gpus[i];
                var dst = data.Gpus[i];
                if (!string.IsNullOrWhiteSpace(src.Name)) dst.Name = src.Name;
                if (!string.IsNullOrWhiteSpace(src.GpuCode)) dst.GpuCode = src.GpuCode;
                if (!string.IsNullOrWhiteSpace(src.MemorySize)) dst.MemorySize = src.MemorySize;
                if (!string.IsNullOrWhiteSpace(src.MemoryType)) dst.MemoryType = src.MemoryType;
                if (!string.IsNullOrWhiteSpace(src.MemoryBus)) dst.MemoryBus = src.MemoryBus;
                if (!string.IsNullOrWhiteSpace(src.DriverVersion)) dst.DriverVersion = src.DriverVersion;
                if (!string.IsNullOrWhiteSpace(src.DeviceId)) dst.DeviceId = src.DeviceId;
                dst.BrandKey = DetectGpuBrand(src.Name);
                dst.IsVerified = true;
            }
        }

        return data;
    }

    private static CpuDetail BuildCpuDetail()
    {
        var cpu = First("Win32_Processor");
        var detail = new CpuDetail();

        if (cpu != null)
        {
            detail.Name = Get(cpu, "Name");
            detail.Cores = ToInt(Get(cpu, "NumberOfCores"));
            detail.Threads = ToInt(Get(cpu, "NumberOfLogicalProcessors"));
            detail.MaxClockSpeed = FormatMhz(Get(cpu, "MaxClockSpeed"));
            detail.CurrentClockSpeed = FormatMhz(Get(cpu, "CurrentClockSpeed"));
            detail.L2CacheSize = FormatCacheSize(Get(cpu, "L2CacheSize"));
            detail.L3CacheSize = FormatCacheSize(Get(cpu, "L3CacheSize"));
            detail.ExtClock = FormatMhz(Get(cpu, "ExtClock"));
            detail.Architecture = MapCpuArchitecture(Get(cpu, "Architecture"));
            detail.Manufacturer = Get(cpu, "Manufacturer");
            detail.ProcessorId = Get(cpu, "ProcessorId");
            detail.BrandKey = DetectCpuBrand(detail.Name);
        }

        return detail;
    }

    private static string? FormatMhz(string? value)
    {
        var mhz = ToInt(value);
        if (mhz <= 0) return null;
        if (mhz >= 1000) return $"{mhz / 1000d:0.#} GHz";
        return $"{mhz} MHz";
    }

    private static string? FormatCacheSize(string? value)
    {
        var kb = ToInt(value);
        if (kb <= 0) return null;
        if (kb >= 1024) return $"{kb / 1024d:0.#} MB";
        return $"{kb} KB";
    }

    private static string? MapCpuArchitecture(string? value)
    {
        return ToInt(value) switch
        {
            0 => "x86",
            1 => "MIPS",
            2 => "Alpha",
            3 => "PowerPC",
            5 => "ARM",
            6 => "Itanium",
            9 => "x64",
            12 => "ARM64",
            _ => null
        };
    }

    private static MotherboardDetail BuildMotherboardDetail()
    {
        var board = First("Win32_BaseBoard");
        var bios = First("Win32_BIOS");

        return new MotherboardDetail
        {
            Manufacturer = CleanBoardManufacturer(Get(board, "Manufacturer")),
            Model = Get(board, "Product"),
            Version = Get(board, "Version"),
            BiosBrand = Get(bios, "Manufacturer"),
            BiosVersion = Get(bios, "SMBIOSBIOSVersion"),
            BiosDate = FormatBiosDate(Get(bios, "ReleaseDate"))
        };
    }

    private static string? FormatBiosDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 8) return value;
        try
        {
            var dateStr = value[..8];
            if (int.TryParse(dateStr, out var num) && num > 19000101)
                return $"{dateStr[..4]}-{dateStr[4..6]}-{dateStr[6..8]}";
        }
        catch { }
        return value;
    }

    private static MemoryDetail BuildMemoryDetail()
    {
        var allSlots = Query("Win32_PhysicalMemory").ToList();
        var modules = allSlots.Where(item => ToLong(Get(item, "Capacity")) > 0).ToList();

        var totalSlots = Query("Win32_PhysicalMemoryArray")
            .Select(item => ToInt(Get(item, "MemoryDevices")))
            .Where(v => v > 0)
            .Sum();
        if (totalSlots == 0) totalSlots = allSlots.Count;

        var totalBytes = modules.Sum(item => ToLong(Get(item, "Capacity")));
        var memType = ToInt(modules.Select(item => Get(item, "SMBIOSMemoryType")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
        var typeLabel = GetMemoryTypeLabel(memType);

        var detail = new MemoryDetail
        {
            TotalCapacity = totalBytes > 0 ? $"{totalBytes / 1024d / 1024d / 1024d:0.#} GB" : null,
            MemoryType = typeLabel,
            TotalSlots = totalSlots,
            UsedSlots = modules.Count
        };

        foreach (var mod in modules)
        {
            var configuredSpeed = GetMemoryConfiguredClockSpeed(mod);

            detail.Modules.Add(new MemoryModuleDetail
            {
                Designation = FirstUseful(Get(mod, "BankLabel"), Get(mod, "DeviceLocator")),
                Capacity = FormatCapacity(ToLong(Get(mod, "Capacity"))),
                Speed = configuredSpeed > 0 ? $"{configuredSpeed} MHz" : null,
                RatedSpeed = null,
                Manufacturer = CleanMemManufacturer(Get(mod, "Manufacturer")),
                PartNumber = Get(mod, "PartNumber"),
                Type = typeLabel,
                FormFactor = MapFormFactor(Get(mod, "FormFactor"))
            });
        }

        for (int i = modules.Count; i < totalSlots; i++)
        {
            detail.Modules.Add(new MemoryModuleDetail
            {
                Designation = $"插槽 {i + 1}",
                Capacity = "空"
            });
        }

        return detail;
    }

    private static string? FormatCapacity(long bytes)
    {
        if (bytes <= 0) return null;
        return $"{bytes / 1024d / 1024d / 1024d:0.#} GB";
    }

    private static string? MapFormFactor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().ToUpperInvariant() switch
        {
            "8" or "DIMM" => "DIMM",
            "12" or "SODIMM" => "SO-DIMM",
            "13" or "FB-DIMM" => "FB-DIMM",
            _ => value.Trim()
        };
    }

    private static bool TryMatchDxgiAdapter(string wmiName, (string name, ulong dedicatedVram, ulong sharedVram)[] dxgiAdapters, out int matchedIndex)
    {
        matchedIndex = -1;
        if (string.IsNullOrWhiteSpace(wmiName) || dxgiAdapters.Length == 0)
            return false;

        for (int i = 0; i < dxgiAdapters.Length; i++)
        {
            var dxgiName = dxgiAdapters[i].name;
            if (string.IsNullOrWhiteSpace(dxgiName)) continue;

            if (wmiName.Contains(dxgiName, StringComparison.OrdinalIgnoreCase) ||
                dxgiName.Contains(wmiName, StringComparison.OrdinalIgnoreCase))
            {
                matchedIndex = i;
                return true;
            }

            var wmiTokens = wmiName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 2).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var dxgiTokens = dxgiName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 2).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (wmiTokens.Intersect(dxgiTokens).Count() >= 2)
            {
                matchedIndex = i;
                return true;
            }
        }

        return false;
    }

    private static List<GpuDetail> BuildGpuDetails()
    {
        var gpus = new List<GpuDetail>();
        (string name, ulong dedicatedVram, ulong sharedVram)[] dxgiAdapters;

        try
        {
            dxgiAdapters = EnumerateDxgiAdapters();
        }
        catch
        {
            dxgiAdapters = Array.Empty<(string, ulong, ulong)>();
        }

        var usedDxgiIndices = new HashSet<int>();

        foreach (var item in Query("Win32_VideoController"))
        {
            var name = Get(item, "Name");
            if (ContainsAny(name, "Microsoft Basic Render", "Microsoft Remote Display", "DDA Wrapper",
                "Idd Desk", "GameViewer Virtual Display", "Honor Virtual Display", "Virtual Display",
                "Virtual GPU", "Virtual Adapter", "虚拟", "Remote Display Adapter"))
                continue;

            var width = Get(item, "CurrentHorizontalResolution");
            var height = Get(item, "CurrentVerticalResolution");
            var refresh = Get(item, "CurrentRefreshRate");

            string? vramText = null;
            if (name != null && TryMatchDxgiAdapter(name, dxgiAdapters, out int dxgiIdx))
            {
                var dedicated = dxgiAdapters[dxgiIdx].dedicatedVram;
                var shared = dxgiAdapters[dxgiIdx].sharedVram;
                if (dedicated > 0)
                    vramText = $"{dedicated / 1024d / 1024d / 1024d:0.#} GB";
                else if (shared > 0)
                    vramText = $"共享 {shared / 1024d / 1024d / 1024d:0.#} GB";
                usedDxgiIndices.Add(dxgiIdx);
            }

            gpus.Add(new GpuDetail
            {
                Name = name,
                AdapterRAM = vramText,
                DriverVersion = Get(item, "DriverVersion"),
                DriverDate = Get(item, "DriverDate"),
                VideoProcessor = Get(item, "VideoProcessor"),
                CurrentResolution = !string.IsNullOrWhiteSpace(width) && !string.IsNullOrWhiteSpace(height)
                    ? $"{width} x {height}" : null,
                CurrentRefreshRate = !string.IsNullOrWhiteSpace(refresh) && refresh != "0" ? $"{refresh} Hz" : null,
                BrandKey = DetectGpuBrand(name)
            });
        }

        return gpus;
    }

    private static List<DiskDetail> BuildDiskDetails()
    {
        var disks = new List<DiskDetail>();

        foreach (var item in Query("Win32_DiskDrive"))
        {
            var model = Get(item, "Model");
            if (string.IsNullOrWhiteSpace(model)) continue;

            var deviceId = Get(item, "DeviceID");
            var size = ToLong(Get(item, "Size"));
            var interfaceType = Get(item, "InterfaceType");
            var mediaType = Get(item, "MediaType");
            var diskMediaType = DetermineMediaType(mediaType, interfaceType);

            var disk = new DiskDetail
            {
                Model = model,
                MediaType = diskMediaType,
                Size = size > 0 ? $"{size / 1024d / 1024d / 1024d:0.#} GB" : null,
                InterfaceType = MapInterfaceType(interfaceType),
                FirmwareRevision = Get(item, "FirmwareRevision"),
                SerialNumber = Get(item, "SerialNumber")
            };

            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                try
                {
                    var partitions = GetDiskPartitions(deviceId);
                    disk.Partitions.AddRange(partitions);
                }
                catch { }
            }

            disks.Add(disk);
        }

        EnrichDiskTemperatures(disks);

        return disks;
    }

    private static void EnrichDiskTemperatures(List<DiskDetail> disks)
    {
        if (disks.Count == 0) return;
        try
        {
            var temps = LiteMonitorService.ReadDiskTemperatures();
            if (temps.Count == 0) return;
            foreach (var disk in disks)
            {
                if (string.IsNullOrWhiteSpace(disk.Model)) continue;
                foreach (var kvp in temps)
                {
                    if (kvp.Key.Contains(disk.Model, StringComparison.OrdinalIgnoreCase)
                        || disk.Model.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        disk.Temperature = kvp.Value;
                        break;
                    }
                }
            }
        }
        catch { }
    }

    private static string? DetermineMediaType(string? mediaType, string? interfaceType)
    {
        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            var mt = mediaType.Trim().ToUpperInvariant();
            if (mt.Contains("SSD") || mt.Contains("SOLID"))
                return "SSD";
            if (mt.Contains("HDD") || mt.Contains("HARD"))
                return "HDD";
            if (mt.Contains("FIXED") && !string.IsNullOrWhiteSpace(interfaceType)
                && interfaceType.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
                return "SSD";
        }

        if (!string.IsNullOrWhiteSpace(interfaceType)
            && interfaceType.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
            return "SSD";

        return null;
    }

    private static string? MapInterfaceType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().ToUpperInvariant() switch
        {
            "IDE" => "IDE/PATA",
            "SCSI" => "SCSI",
            "1394" => "IEEE 1394",
            _ => value.Trim()
        };
    }

    private static List<PartitionDetail> GetDiskPartitions(string deviceId)
    {
        var partitions = new List<PartitionDetail>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{deviceId.Replace("'", "''")}'}} WHERE ResultClass=Win32_DiskPartition");
            foreach (ManagementBaseObject part in searcher.Get())
            {
                var partDeviceId = Get(part, "DeviceID");
                var logicalDisk = GetPartitionLogicalDisk(partDeviceId);

                partitions.Add(new PartitionDetail
                {
                    Name = Get(part, "Name"),
                    DriveLetter = logicalDisk?.DriveLetter,
                    FileSystem = logicalDisk?.FileSystem,
                    Size = FormatCapacity(ToLong(Get(part, "Size"))),
                    FreeSpace = logicalDisk?.FreeSpace
                });
            }
        }
        catch { }

        return partitions;
    }

    private sealed record LogicalDiskInfo(string? DriveLetter, string? FileSystem, string? FreeSpace);

    private static LogicalDiskInfo? GetPartitionLogicalDisk(string? partitionDeviceId)
    {
        if (string.IsNullOrWhiteSpace(partitionDeviceId)) return null;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceId='{partitionDeviceId.Replace("'", "''")}'}} WHERE ResultClass=Win32_LogicalDisk");
            foreach (ManagementBaseObject ld in searcher.Get())
            {
                var freeBytes = ToLong(Get(ld, "FreeSpace"));
                return new LogicalDiskInfo(
                    Get(ld, "Name"),
                    Get(ld, "FileSystem"),
                    freeBytes > 0 ? $"{freeBytes / 1024d / 1024d / 1024d:0.#} GB" : null
                );
            }
        }
        catch { }

        return null;
    }

    private static List<DisplayDetail> BuildDisplayDetails()
    {
        var results = new List<DisplayDetail>();
        var wmiLabels = GetWmiMonitorLabelsByPnpCode();
        var wmiSizes = GetWmiMonitorSizesByPnpCode();

        try
        {
            var adapter = NewDisplayDevice();
            for (uint i = 0; EnumDisplayDevices(null, i, ref adapter, 0); i++)
            {
                if ((adapter.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                {
                    var resolution = GetCurrentResolution(adapter.DeviceName);
                    var refreshRate = GetCurrentRefreshRate(adapter.DeviceName);
                    var isPrimary = (adapter.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;
                    var monitor = GetDisplayMonitor(adapter.DeviceName);
                    var pnpCode = ExtractMonitorPnpCode(monitor?.DeviceID);
                    var label = ChooseDisplayLabel(monitor?.DeviceString, pnpCode, adapter.DeviceString, wmiLabels);
                    var diagonalInches = GetDiagonalInches(pnpCode, wmiSizes);

                    results.Add(new DisplayDetail
                    {
                        Name = label,
                        Resolution = resolution,
                        RefreshRate = refreshRate,
                        IsPrimary = isPrimary,
                        DiagonalInches = diagonalInches.HasValue ? $"{diagonalInches.Value:F1}\"" : null
                    });
                }

                adapter = NewDisplayDevice();
            }
        }
        catch { }

        return results;
    }

    private static string? GetCurrentRefreshRate(string displayDeviceName)
    {
        var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(displayDeviceName, ENUM_CURRENT_SETTINGS, ref mode) || mode.dmDisplayFrequency == 0)
            return null;
        return $"{mode.dmDisplayFrequency} Hz";
    }

    private static List<SoundDetail> BuildSoundDetails()
    {
        var devices = new List<SoundDetail>();

        foreach (var item in Query("Win32_SoundDevice"))
        {
            var name = Get(item, "Name");
            if (ContainsAny(name, "Virtual", "虚拟", "Software", "Remote Audio", "Stereo Mix", "Wave", "VB-Audio", "VBAN", "Voicemeeter", "CABLE", "VAC", "Senary Audio", "Nahimic Easy Surround", "Nahimic mirroring", "USB 音频", "蓝牙音频", "蓝牙"))
                continue;

            devices.Add(new SoundDetail
            {
                Name = name,
                Manufacturer = Get(item, "Manufacturer"),
                Status = Get(item, "Status")
            });
        }

        return devices;
    }

    private static List<NetworkDetail> BuildNetworkDetails()
    {
        var adapters = new List<NetworkDetail>();

        foreach (var item in Query("Win32_NetworkAdapter"))
        {
            if (!IsTrue(item, "PhysicalAdapter")) continue;
            var name = Get(item, "Name");
            if (ContainsAny(name, "Virtual", "Bluetooth", "WAN Miniport")) continue;

            var speed = ToLong(Get(item, "Speed"));
            adapters.Add(new NetworkDetail
            {
                Name = name,
                Manufacturer = Get(item, "Manufacturer"),
                MacAddress = Get(item, "MACAddress"),
                Speed = speed > 0 ? FormatNetworkSpeed(speed) : null,
                AdapterType = Get(item, "AdapterType")
            });
        }

        return adapters;
    }

    private static string FormatNetworkSpeed(long bps)
    {
        if (bps >= 1_000_000_000) return $"{bps / 1_000_000_000d:0.#} Gbps";
        if (bps >= 1_000_000) return $"{bps / 1_000_000d:0.#} Mbps";
        if (bps >= 1_000) return $"{bps / 1_000d:0.#} Kbps";
        return $"{bps} bps";
    }

    private static NpuDetail? BuildNpuDetail()
    {
        foreach (var item in Query("Win32_PnPEntity"))
        {
            var pnpClass = Get(item, "PNPClass");
            if (!string.Equals(pnpClass, "ComputeAccelerator", StringComparison.OrdinalIgnoreCase))
                continue;

            var name = Get(item, "Name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            return new NpuDetail
            {
                Name = name,
                Manufacturer = Get(item, "Manufacturer"),
                DriverVersion = Get(item, "DriverVersion"),
                DriverDate = Get(item, "DriverDate"),
                DeviceId = Get(item, "DeviceId")
            };
        }

        return null;
    }

    #endregion
}
