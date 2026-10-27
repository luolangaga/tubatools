using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
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
        // DEVMODEW 必须保留完整的 220 字节布局。
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFirmwareType(out uint firmwareType);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessorFeaturePresent(uint processorFeature);

    private const uint PfSecondLevelAddressTranslation = 20;
    private const uint PfVirtFirmwareEnabled = 21;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    #region Physical Disk Partition Style (IOCTL)

    private const uint GenericRead = 0x80000000;
    private const uint FileShareReadWrite = 0x3;
    private const uint OpenExistingDisposition = 0x3;
    private const uint IoctlDiskGetDriveLayoutEx = 0x00070050;
    private const int ErrorInsufficientBuffer = 122;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        byte[] lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    /// <summary>
    /// 直读物理磁盘分区样式（DRIVE_LAYOUT_INFORMATION_EX 的首字段即 PARTITION_STYLE：
    /// 0 = MBR / 1 = GPT / 2 = RAW）。WMI 没有这个值，只有 IOCTL 才拿得到。
    /// 需要管理员权限；读取失败返回 null（调用方不展示该行）。
    /// </summary>
    private static string? ReadPartitionStyle(int diskIndex)
    {
        if (diskIndex < 0) return null;

        try
        {
            using var handle = CreateFileW(
                $@"\\.\PhysicalDrive{diskIndex}",
                GenericRead,
                FileShareReadWrite,
                IntPtr.Zero,
                OpenExistingDisposition,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid) return null;

            var buffer = new byte[64];
            if (DeviceIoControl(handle, IoctlDiskGetDriveLayoutEx, IntPtr.Zero, 0, buffer, (uint)buffer.Length, out var returned, IntPtr.Zero))
                return MapPartitionStyle(BitConverter.ToUInt32(buffer, 0).ToString());

            if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer || returned == 0)
                return null;

            // 缓冲区不够时系统通过 lpBytesReturned 告诉真实大小，按真实大小重试。
            buffer = new byte[returned];
            if (!DeviceIoControl(handle, IoctlDiskGetDriveLayoutEx, IntPtr.Zero, 0, buffer, (uint)buffer.Length, out _, IntPtr.Zero))
                return null;

            return MapPartitionStyle(BitConverter.ToUInt32(buffer, 0).ToString());
        }
        catch
        {
            return null;
        }
    }

    #endregion

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
    private static readonly SemaphoreSlim _buildGate = new(1, 1);

    private static string GetSeparator()
    {
        return AppSettings.GetBool("HardwareMultiDeviceNewLine", true) ? Environment.NewLine : " / ";
    }

    public static bool HasCache
    {
        get { lock (_lock) { return _cache != null; } }
    }

    public static Task PreloadAsync()
    {
        return Task.Run(async () =>
        {
            try
            {
                _ = await LoadAsync();
            }
            catch { }
        });
    }

    public static void Preload()
    {
        _ = PreloadAsync();
    }

    public static Task<IReadOnlyList<HardwareInfoSection>> LoadAsync(bool forceRefresh = false)
    {
        return Task.Run(() => BuildSections(forceRefresh));
    }

    public static DateTimeOffset? LastCapturedAt { get; private set; }

    private static IReadOnlyList<HardwareInfoSection> BuildSections(bool forceRefresh)
    {
        lock (_lock)
        {
            if (!forceRefresh && _cache != null)
                return _cache;
        }

        // 合并并发盘点（启动预热与硬件页首开可能同时到达，避免重复跑 20+ 条 WMI 查询）：
        // 等锁期间别人构建完成 → 直接复用其结果
        _buildGate.Wait();
        try
        {
            lock (_lock)
            {
                if (!forceRefresh && _cache != null)
                    return _cache;
            }

            var sections = CreateEmptySections();

            var summaryTask = Task.Run(() => FillSummary(sections[0]));
            var systemTask = Task.Run(() => FillSystem(sections[1]));
            var detailsTask = Task.Run(() => FillDetails(sections[2]));

            Task.WaitAll(summaryTask, systemTask, detailsTask);

            lock (_lock)
            {
                _cache = sections;
                LastCapturedAt = DateTimeOffset.Now;
            }

            return sections;
        }
        finally
        {
            _buildGate.Release();
        }
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
                cpuItem.Value = Join(name, cpuItem.SupplementalValue);
                cpuItem.DeviceValues = [cpuItem.Value];
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
                var gpuDevices = cpuz.Gpus
                    .Where(g => !string.IsNullOrWhiteSpace(g.Name))
                    .Select(g =>
                    {
                        var s = g.Name!;
                        if (!string.IsNullOrWhiteSpace(g.MemorySize))
                            s += $" ({g.MemorySize})";
                        return s;
                    }).ToArray();
                var gpuLabel = string.Join(GetSeparator(), gpuDevices);
                if (!string.IsNullOrWhiteSpace(gpuLabel))
                {
                    gpuItem.Value = gpuLabel;
                    gpuItem.DeviceValues = gpuDevices;
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
                    memItem.DeviceValues = BuildCpuzMemoryDevices(cpuz, memLabel);
                    memItem.IsVerified = true;
                }
            }
        }

        return sections;
    }

    internal static string CoresThreadsLabel(CpuzInfo cpuz)
    {
        if (cpuz.CpuCores <= 0) return "";
        return cpuz.CpuThreads > cpuz.CpuCores
            ? $"{cpuz.CpuCores}C/{cpuz.CpuThreads}T"
            : $"{cpuz.CpuCores}C";
    }

    internal static string BuildCpuzMemoryLabel(CpuzInfo cpuz)
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

    internal static IReadOnlyList<string> BuildCpuzMemoryDevices(CpuzInfo cpuz, string aggregate)
    {
        if (cpuz.MemDevices.Count < 2 || (cpuz.MemoryType?.Contains("LPDDR", StringComparison.OrdinalIgnoreCase) ?? false))
            return [aggregate];
        return cpuz.MemDevices.Select(module => Join(CleanMemManufacturer(module.Manufacturer),
            module.PartNumber, module.Size, module.Type ?? cpuz.MemoryType,
            module.Speed ?? cpuz.MemorySpeed, module.Designation)).ToArray();
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
                    IsVerified = item.IsVerified,
                    SupplementalValue = item.SupplementalValue,
                    DeviceValues = item.DeviceValues.ToArray()
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

    private static bool? _laptopCache;

    /// <summary>
    /// 判断当前设备是否为笔记本。基于机箱类型 + 电池存在性双重判断。
    /// </summary>
    public static bool IsLaptop()
    {
        if (_laptopCache.HasValue) return _laptopCache.Value;
        _laptopCache = DetectLaptop();
        return _laptopCache.Value;
    }

    private static bool DetectLaptop()
    {
        try
        {
            foreach (var item in Query("Win32_SystemEnclosure"))
            {
                var chassisTypes = item["ChassisTypes"];
                if (chassisTypes is ushort[] arr)
                {
                    foreach (var t in arr)
                    {
                        // 8=Portable, 9=Laptop, 10=Notebook, 11=Handheld, 14=SubNotebook
                        if (t == 8 || t == 9 || t == 10 || t == 11 || t == 14 || t == 30 || t == 31 || t == 32)
                            return true;
                    }
                }
            }
        }
        catch { }

        // 兜底：笔记本通常有电池
        try
        {
            var battery = Query("Win32_Battery");
            if (!battery.GetEnumerator().MoveNext()) return false;
            var model = Get(First("Win32_ComputerSystem"), "Model");
            if (model != null &&
                (model.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                 model.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
                 model.Contains("HVM", StringComparison.OrdinalIgnoreCase) ||
                 model.Contains("KVM", StringComparison.OrdinalIgnoreCase)))
                return false;
            return true;
        }
        catch { }

        return false;
    }

    /// <summary>
    /// 获取当前设备的机型标识（厂商 + 型号），用于笔记本评分归组。
    /// </summary>
    public static string GetDeviceModel()
    {
        var computer = First("Win32_ComputerSystem");
        return Join(Get(computer, "Manufacturer"), Get(computer, "Model"));
    }

    /// <summary>
    /// 获取主 CPU 名称。
    /// </summary>
    public static string GetCpuName()
    {
        return FirstName("Win32_Processor");
    }

    /// <summary>
    /// 获取主显卡名称（已过滤虚拟显示器适配器）。
    /// </summary>
    public static string GetGpuName()
    {
        return BuildGpuDisplayText() ?? "未知";
    }

    /// <summary>
    /// 获取主板型号。
    /// </summary>
    public static string GetMotherboardModel()
    {
        return BoardModel();
    }

    /// <summary>
    /// 获取第一个硬盘型号。
    /// </summary>
    public static string GetPrimaryDiskModel()
    {
        foreach (var item in Query("Win32_DiskDrive"))
        {
            var model = Get(item, "Model");
            if (!string.IsNullOrWhiteSpace(model)) return model;
        }
        return "未知";
    }

    /// <summary>
    /// 获取内存描述。
    /// </summary>
    public static string GetMemoryDescription()
    {
        return FormatMemory();
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

        var windows = new WindowsDetail();
        FillWindowsVersion(windows, os);
        section.Items.Add(Item("系统", FormatWindowsSummary(windows.ProductName, windows.DisplayVersion, windows.Build)));
        section.Items.Add(Item("版本", Get(os, "Version")));
        section.Items.Add(Item("运行时间", FormatUptime()));
    }

    private static void FillDetails(HardwareInfoSection section)
    {
        var boardTask = Task.Run(() => BoardModel());
        var cpuTask = Task.Run(BuildCpuSummary);
        var memTask = Task.Run(BuildMemorySummary);
        var gpuTask = Task.Run(BuildGpuDeviceValues);
        var npuTask = Task.Run(() => DetectNpuName());
        var displayTask = Task.Run(BuildDisplayDeviceValues);
        var diskTask = Task.Run(() => BuildDiskDeviceValues(BuildDiskDetails(includeFullDetails: false)));
        var batteryTask = Task.Run(() => Safe(BuildBatteryDetail, null));
        var soundTask = Task.Run(() => DeviceNames("Win32_SoundDevice", item =>
        {
            var name = Get(item, "Name");
            return !ContainsAny(name, "Virtual", "虚拟", "Software", "Remote Audio", "Stereo Mix", "Wave", "VB-Audio", "VBAN", "Voicemeeter", "CABLE", "VAC", "Senary Audio", "Nahimic Easy Surround", "Nahimic mirroring", "USB 音频", "蓝牙音频", "蓝牙");
        }));
        var netTask = Task.Run(() => DeviceNames("Win32_NetworkAdapter", item =>
            IsTrue(item, "PhysicalAdapter") &&
            !ContainsAny(Get(item, "Name"), "Virtual", "Bluetooth", "WAN Miniport")));

        Task.WaitAll(boardTask, cpuTask, memTask, gpuTask, npuTask, displayTask, diskTask, soundTask, netTask, batteryTask);

        section.Items.Add(Item("主板", boardTask.Result));
        var cpuItem = cpuTask.Result;
        var cpuName = cpuItem.Value;
        cpuItem.BrandKey = DetectCpuBrand(cpuName);
        section.Items.Add(cpuItem);
        section.Items.Add(memTask.Result);
        var gpuItem = DeviceItem("显卡", gpuTask.Result);
        gpuItem.BrandKey = DetectGpuBrand(gpuItem.Value);
        section.Items.Add(gpuItem);
        var npuName = npuTask.Result;
        if (npuName != null)
        {
            var tops = NpuCatalog.LookupTops(npuName, cpuName);
            section.Items.Add(Item("NPU", tops != null ? $"{npuName}（{tops}）" : npuName));
        }
        section.Items.Add(DeviceItem("显示器", displayTask.Result));
        section.Items.Add(DeviceItem("硬盘", diskTask.Result));
        if (batteryTask.Result is { } battery)
            section.Items.Add(Item("电池", FormatBatterySummary(battery)));
        section.Items.Add(DeviceItem("声卡", soundTask.Result));
        section.Items.Add(DeviceItem("网卡", netTask.Result));
    }

    private static HardwareInfoItem BuildCpuSummary()
    {
        var processors = Query("Win32_Processor").ToList();
        var entries = processors.Select(cpu =>
        {
            var specs = new List<string>();
            var cores = ToInt(Get(cpu, "NumberOfCores"));
            var threads = ToInt(Get(cpu, "NumberOfLogicalProcessors"));
            if (cores > 0) specs.Add($"{cores} 核心");
            if (threads > 0) specs.Add($"{threads} 线程");
            var cache = FormatCpuCacheSummary(Get(cpu, "L2CacheSize"), Get(cpu, "L3CacheSize"));
            if (!string.IsNullOrWhiteSpace(cache)) specs.Add(cache);
            return Join(Get(cpu, "Name"), string.Join(" · ", specs));
        });
        var item = DeviceItem("处理器", entries);
        // CPU-Z 替换名称/核心数时，保留它未提供的 WMI 缓存信息。
        if (processors.Count == 1)
            item.SupplementalValue = FormatCpuCacheSummary(Get(processors[0], "L2CacheSize"), Get(processors[0], "L3CacheSize"));
        return item;
    }

    internal static string FormatCpuCacheSummary(string? l2, string? l3) => string.Join(" · ",
        new[] { FormatCacheSize(l2) is { } second ? $"L2 缓存 {second}" : null,
            FormatCacheSize(l3) is { } third ? $"L3 缓存 {third}" : null }
        .Where(value => value is not null));

    internal static string FormatBatterySummary(BatteryDetail battery) =>
        $"{battery.Name ?? "电池"} · 设计容量：{battery.DesignCapacity ?? "未能读取"}" +
        $" · 满充容量：{battery.FullChargeCapacity ?? "未能读取"}" +
        $" · 健康度：{battery.Health ?? "未能读取"}" +
        $" · 循环次数：{battery.CycleCount ?? "未能读取"}" +
        $" · 序列号：{battery.SerialNumber ?? "未能读取"}";

    internal static string FormatWindowsSummary(string? product, string? displayVersion, string? build)
    {
        var name = Join(product, string.IsNullOrWhiteSpace(displayVersion) ? null : $"({displayVersion})");
        return string.IsNullOrWhiteSpace(build) ? name : $"{name} | {build}";
    }

    internal static string? DetectCpuBrand(string? cpuName)
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
        var devices = BuildGpuDeviceValues();
        return devices.Count > 0 ? string.Join(GetSeparator(), devices) : null;
    }

    private static IReadOnlyList<string> BuildGpuDeviceValues()
    {
        (string name, ulong dedicatedVram, ulong sharedVram)[] dxgiAdapters;
        try { dxgiAdapters = EnumerateDxgiAdapters(); }
        catch { dxgiAdapters = Array.Empty<(string, ulong, ulong)>(); }

        var parts = new List<string>();
        foreach (var item in Query("Win32_VideoController"))
        {
            var name = Get(item, "Name");
            if (string.IsNullOrWhiteSpace(name) || HardwareDisplayPolicy.IsVirtualAdapter(name))
                continue;

            var display = name;

            if (name != null && TryMatchDxgiAdapter(name, dxgiAdapters, out int dxgiIdx))
            {
                var dedicated = dxgiAdapters[dxgiIdx].dedicatedVram;
                var shared = dxgiAdapters[dxgiIdx].sharedVram;
                display += $" ({HardwareDisplayPolicy.FormatGpuMemory(dedicated, shared)})";
            }

            parts.Add(display!);
        }

        return parts;
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

    internal static string? DetectGpuBrand(string? gpuName)
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

    private static HardwareInfoItem DeviceItem(string label, IEnumerable<string> values)
    {
        var devices = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        var item = Item(label, string.Join(GetSeparator(), devices));
        item.DeviceValues = devices;
        return item;
    }

    private static long GetTotalPhysicalMemoryBytes()
    {
        try
        {
            foreach (var obj in Query("Win32_ComputerSystem"))
            {
                var val = ToLong(Get(obj, "TotalPhysicalMemory"));
                if (val > 0) return val;
            }
        }
        catch { }
        return 0;
    }

    private static string FormatMemory() => BuildMemorySummary().Value;

    private static HardwareInfoItem BuildMemorySummary()
    {
        var allSlots = Query("Win32_PhysicalMemory").ToList();
        if (allSlots.Count == 0)
        {
            return Item("内存", "未知");
        }

        var modules = allSlots.Where(item => ToLong(Get(item, "Capacity")) > 0).ToList();

        var totalSlots = Query("Win32_PhysicalMemoryArray")
            .Select(item => ToInt(Get(item, "MemoryDevices")))
            .Where(v => v > 0)
            .Sum();
        if (totalSlots == 0) totalSlots = allSlots.Count;

        if (modules.Count == 0)
        {
            return Item("内存", $"空插槽 {totalSlots} 个");
        }

        var systemTotal = GetTotalPhysicalMemoryBytes();
        var totalBytes = systemTotal > 0 ? systemTotal : modules.Sum(item => ToLong(Get(item, "Capacity")));
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
        var (bandwidthRate, busBits) = ReadMemoryBusInfo(modules);
        if (busBits > 0) parts.Add($"{busBits}-bit");
        var memoryBandwidth = HardwareDisplayPolicy.ComputeMemoryBandwidthGbs(bandwidthRate, busBits);
        if (memoryBandwidth > 0) parts.Add($"理论带宽 {HardwareDisplayPolicy.FormatMemoryBandwidth(memoryBandwidth)}");
        parts.Add(HardwareDisplayPolicy.FormatMemoryDevices(modules.Count, totalSlots, memType));

        var result = Item("内存", string.Join(" ", parts));
        // 板载 LPDDR 的逻辑设备并不等于独立内存条，保持一张完整的板载内存卡片。
        if (modules.Count > 1 && memType is not (27 or 28 or 29 or 30 or 35))
            result.DeviceValues = modules.Select(module => Join(
                CleanMemManufacturer(Get(module, "Manufacturer")), Get(module, "PartNumber"),
                $"{ToLong(Get(module, "Capacity")) / 1024d / 1024d / 1024d:0.#} GB",
                GetMemoryTypeLabel(ToInt(Get(module, "SMBIOSMemoryType"))),
                GetMemoryConfiguredClockSpeed(module) > 0 ? $"{GetMemoryConfiguredClockSpeed(module)} MHz" : null,
                Get(module, "DeviceLocator"))).ToArray();
        return result;
    }

    internal static string GetMemoryTypeLabel(int smbiosMemoryType)
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

    /// <summary>总位宽 = 各已插内存 DataWidth 之和（DDR5 单条 64 bit，LPDDR 常见 16 bit×多颗）。</summary>
    private static (int TransferRateMt, int BusBits) ReadMemoryBusInfo(IEnumerable<ManagementBaseObject> modules)
    {
        int transferRate = 0, busBits = 0;
        foreach (var module in modules)
        {
            if (ToLong(Get(module, "Capacity")) <= 0) continue;

            var width = ToInt(Get(module, "DataWidth"));
            if (width <= 0) width = ToInt(Get(module, "TotalWidth"));
            if (width <= 0) continue;
            busBits += width;

            var rate = GetMemoryConfiguredClockSpeed(module);
            if (rate <= 0) rate = ToInt(Get(module, "Speed"));
            // ConfiguredClockSpeed / Speed 是传输速率（MT/s），不是时钟频率。
            transferRate = Math.Max(transferRate, rate);
        }

        return (transferRate, busBits);
    }

    internal static string? CleanMemManufacturer(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = raw.Trim();

        var jedecDecoded = DecodeJedecManufacturer(cleaned);
        if (jedecDecoded != null) return jedecDecoded;

        return cleaned.ToUpperInvariant() switch
        {
            // SMBIOS/SPD 占位串：BIOS 未填模组厂商时的默认值（DDR5 上并不少见），不能当成品牌显示
            "UNKNOWN" or "TO BE FILLED BY O.E.M." or "TO BE FILLED BY OEM" or "DEFAULT STRING"
                or "NOT SPECIFIED" or "NOT AVAILABLE" or "N/A" or "NONE" or "UNDEFINED" => null,
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
            "SILICON POWER" or "S-POWER" or "SP" => "广颖电通(Silicon Power)",
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
            // 国产 + 国际新晋内存模组厂，BIOS 经常直接以字符串返回
            "KINGBANK" or "KINGBANK TECHNOLOGY" => "金百达(Kingbank)",
            "KINGMAX" or "KINGMAX TECHNOLOGY" or "KINGMAX SEMICONDUCTOR" => "胜创(Kingmax)",
            "ASINT" => "ASint",
            "V-COLOR" or "VCOLOR" => "V-Color",
            "GLOWAY" => "光威(Gloway)",
            "A-DATA" or "A-DATA TECHNOLOGY" => "威刚(ADATA)",
            "ASGARD" => "阿斯加特(Asgard)",
            "JUHOR" => "玖合(JUHOR)",
            "TECLAST" => "台电(Teclast)",
            "MAXSUN" => "铭瑄(Maxsun)",
            "KIMTIGO" => "金泰克(Kimtigo)",
            "AIGO" => "爱国者(aigo)",
            "AVEXIR" => "宇帷(Avexir)",
            "TWINMOS" => "勤茂(TwinMOS)",
            "NEO FORZA" or "NEOFORZA" => "凌航(Neo Forza)",
            "ESSENCORE" => "科赋(Essencore)",
            _ => cleaned
        };
    }

    internal static string? DecodeJedecManufacturer(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();

        // 去掉奇校验位再查表：JEDEC JEP106 中 0x92 与 0x12 是同一厂商。
        // 此外 BIOS / Windows 把 SPD 字节序列化成 hex 串时常见的两种字节序都要兼容：
        //   1) 大端：[continuation][vendor]        → "0B12" 表示 bank=11, vendor=0x12
        //   2) 小端：[vendor][continuation]        → "120B" 同样表示同一厂商
        // 这两种形式在 WMI Win32_PhysicalMemory.Manufacturer 中都会出现。
        if (trimmed.Length == 2 && IsHex(trimmed))
        {
            var code = Convert.ToByte(trimmed, 16);
            var vendor = (byte)(code & 0x7F);
            return JedecVendorFromCode(vendor);
        }

        if (trimmed.Length == 4 && IsHex(trimmed))
        {
            var code = Convert.ToUInt16(trimmed, 16);

            // 字节序 1：高位字节在前（[continuation/bank][vendor]，如 "0B12"）
            // 字节序 2：低位字节在前（[vendor][continuation]，如 "120B"）
            // 两个字节都去掉奇校验位后，把 [bank][vendor] 打包成 0x0B12 这类 16 位
            // JEDEC 标识再查扩展表；先试“高位在前”，未命中再试字节序翻转。
            var hi = (byte)((code >> 8) & 0x7F);
            var lo = (byte)(code & 0x7F);
            var packed = ((int)hi << 8) | lo;
            var r1 = JedecVendorFromExtendedCode(packed);
            if (r1 != null) return r1;

            var swapped = ((int)lo << 8) | hi;
            var r2 = JedecVendorFromExtendedCode(swapped);
            if (r2 != null) return r2;

            return null;
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
                    var vendor = (byte)(code & 0x7F);
                    return JedecVendorFromCode(vendor);
                }
                if (hexPart.Length == 4 && IsHex(hexPart))
                {
                    var code = Convert.ToUInt16(hexPart, 16);

                    // 与上面无前缀分支相同的两字节打包语义，兼容 [bank][vendor] 与字节序翻转
                    var hi = (byte)((code >> 8) & 0x7F);
                    var lo = (byte)(code & 0x7F);
                    var packed = ((int)hi << 8) | lo;
                    var r1 = JedecVendorFromExtendedCode(packed);
                    if (r1 != null) return r1;

                    var swapped = ((int)lo << 8) | hi;
                    var r2 = JedecVendorFromExtendedCode(swapped);
                    if (r2 != null) return r2;

                    return null;
                }
            }
        }

        return null;
    }

    private static bool IsHex(string s)
    {
        return s.All(c => char.IsAsciiHexDigit(c));
    }

    internal static string? JedecVendorFromCode(byte code)
    {
        // JEDEC JEP106 page 0（无 0x7F 续接字节）。厂商 ID 为 7-bit 数据 + 1 位奇校验位，
        // 调用方负责去掉校验位（如 0x92 与 0x12 是同一厂商）。
        // 历史版本误用了一套与 JEP106 错位的“DRAM 厂商”表（0x02=美光、0x0E=三星、
        // 0x2C=金士顿等均不成立），本表已按 JEP106 原表（decode-dimms @vendors）逐条核对：
        // page 0 的 0x2C 实为美光、0x4E 实为三星、0x2D 实为海力士。
        // 这里的单字节裸值只收录明确的内存厂商：裸单字节也可能只是多字节 ID 的尾字节
        // （如金百达的 0x92），对无法确证的 code 一律返回 null；多字节编码走 JedecVendorRegistry 全表。
        return code switch
        {
            0x04 => "富士通(Fujitsu)",
            0x07 => "日立(Hitachi)",
            0x08 => "Inmos",
            0x0E => "飞思卡尔(Freescale/Motorola)",
            0x10 => "NEC",
            0x15 => "NXP(原飞利浦半导体)",
            0x17 => "德州仪器(TI)",
            0x18 => "东芝内存(Kioxia)",
            0x1C => "三菱(Mitsubishi)",
            0x1F => "Atmel",
            0x20 => "意法半导体(ST)",
            0x2C => "美光(Micron)",
            0x2D => "海力士(SK Hynix)",
            0x32 => "松下(Panasonic)",
            0x40 => "茂德(ProMOS/Mosel)",
            0x41 => "英飞凌(Infineon)",
            0x4E => "三星(Samsung)",
            0x55 => "ISSI",
            0x5A => "华邦(Winbond)",
            _ => null
        };
    }

    internal static string? JedecVendorFromExtendedCode(int fullCode)
    {
        // fullCode 的两种表示（调用方需保持一致）：
        //   - 小整数（如 0x2C）：去掉校验位后的单字节厂商码（等价 bank 0）；
        //   - 两字节打包值（如 0x0B12 / 0x120B）：把 [continuation/bank][vendor] 两个字节
        //     去掉奇校验位后拼接成的 16 位值，即 WMI Win32_PhysicalMemory.Manufacturer
        //     多字节 JEDEC ID 的常见形式（"0B12" 大端 / "120B" 小端经翻转后同样命中）。
        // 查 JEP106 全表（bank 0-16，JEP106BL）：主流内存模组厂注册在 page 1 以后，
        // 三星/美光/海力士在 bank 0，金士顿 page1、英睿达 page5、金百达 page11 等。
        // 注意：两字节打包码不得回退单字节表——否则 e.g. 0x120B (little-endian for
        // Kingbank) 会被 0x0B 误判成“东芝(Toshiba)”。
        return JedecVendorRegistry.Get(fullCode);
    }

    internal static IReadOnlyList<string> BuildDiskDeviceValues(IReadOnlyList<DiskDetail> disks) =>
        disks.Select(disk => $"{disk.Model}{(string.IsNullOrWhiteSpace(disk.Size) ? "" : $" ({disk.Size})")} · 温度：{FormatDiskTemperature(disk.Temperature)}").ToArray();

    internal static IReadOnlyList<HardwareInfoSection> ApplyDiskDetailSnapshot(
        IReadOnlyList<HardwareInfoSection> source, IReadOnlyList<DiskDetail> disks)
    {
        if (disks.Count == 0) return source;
        var updated = DeepCopy(source);
        var diskItem = updated.ElementAtOrDefault(2)?.Items.FirstOrDefault(item => item.Label == "硬盘");
        if (diskItem is not null)
        {
            diskItem.DeviceValues = BuildDiskDeviceValues(disks);
            diskItem.Value = string.Join(GetSeparator(), diskItem.DeviceValues);
        }
        return updated;
    }

    internal static float? FindDiskTemperature(string model, IReadOnlyDictionary<string, float> temperatures)
    {
        if (temperatures.TryGetValue(model, out var exact)) return exact;
        var matches = temperatures.Where(pair => !string.IsNullOrWhiteSpace(pair.Key) &&
            (pair.Key.Contains(model, StringComparison.OrdinalIgnoreCase) ||
             model.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))).ToList();
        return matches.Count == 1 ? matches[0].Value : null;
    }

    internal static string FormatDiskTemperature(float? temperature) =>
        IsValidDiskTemperature(temperature)
            ? $"{temperature:0.#} °C" : "未能读取";

    private static bool IsValidDiskTemperature(float? temperature) =>
        temperature is { } value && float.IsFinite(value) && value > -40 && value < 150;

    internal static float? ResolveDiskTemperature(float? sensor, int? smart) =>
        IsValidDiskTemperature(sensor) ? sensor : IsValidDiskTemperature(smart) ? smart : null;

    private static IReadOnlyList<string> BuildDisplayDeviceValues()
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

        if (monitorInfos.Count == 0) return [];

        return monitorInfos.Select(mi =>
        {
            if (string.IsNullOrWhiteSpace(mi.Label) && string.IsNullOrWhiteSpace(mi.Resolution))
                return "";
            var label = mi.IsPrimary && !string.IsNullOrWhiteSpace(mi.Label) ? $"主屏 {mi.Label}" : mi.Label;
            var sizeStr = mi.DiagonalInches.HasValue ? $"{mi.DiagonalInches.Value:F1}\"" : null;
            var resOrSize = new List<string>();
            if (!string.IsNullOrWhiteSpace(sizeStr)) resOrSize.Add(sizeStr);
            if (!string.IsNullOrWhiteSpace(mi.Resolution)) resOrSize.Add(mi.Resolution);
            if (!string.IsNullOrWhiteSpace(mi.RefreshRate)) resOrSize.Add(mi.RefreshRate);
            var bracketContent = resOrSize.Count > 0 ? string.Join(" ", resOrSize) : null;
            if (string.IsNullOrWhiteSpace(label)) return bracketContent ?? "";
            if (string.IsNullOrWhiteSpace(bracketContent)) return label;
            return $"{label} [{bracketContent}]";
        }).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
    }

    private sealed record DisplayInfo(string Label, string? Resolution, bool IsPrimary, double? DiagonalInches, string? RefreshRate = null);

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
                        results.Add(new DisplayInfo(label, resolution, isPrimary, diagonalInches, GetCurrentRefreshRate(adapter.DeviceName)));
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

    private static Dictionary<string, (int Year, int Week)> GetWmiMonitorMadeDatesByPnpCode()
    {
        var dates = new Dictionary<string, (int Year, int Week)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorID");
            foreach (ManagementBaseObject item in searcher.Get())
            {
                var pnpCode = ExtractMonitorPnpCode(Get(item, "InstanceName"));
                if (string.IsNullOrWhiteSpace(pnpCode) || dates.ContainsKey(pnpCode)) continue;

                var year = GetInt(item, "YearOfManufacture");
                var week = GetInt(item, "WeekOfManufacture");
                if (year > 0)
                    dates[pnpCode] = (year, week);
            }
        }
        catch { }

        return dates;
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

    internal static string ExtractMonitorPnpCode(string? deviceId)
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

    internal static string? ResolveManufacturer(string? code)
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
            "TOL" => "TCL",
            "CSP" => "Casper",
            "CPL" or "WOR" => "COMPAL",
            "CRM" => "海盗船(Corsair)",
            "CRU" => "CRUA",
            "CSO" or "CSW" => "华星光电(CSOT)",
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
            "HEC" => "海信(Hisense)",
            "HSD" or "HSP" => "瀚宇彩晶(HannStar)",
            "HIK" => "海康威视(Hikvision)",
            "HIT" or "HTC" => "日立(Hitachi)",
            "HRE" => "海尔(Haier)",
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
            "MAG" or "MAG_" => "美格(MAG)",
            "MSI" => "微星(MSI)",
            "NLK" or "MST" => "MStar",
            "NLE" => "Newline",
            "NSL" => "Newskill",
            "NEW" => "Newsync",
            "NIX" or "NTI" or "NXG" => "Nixeus",
            "MRG" or "NRL" => "Nreal Air",
            "BDL" => "OneMeeting",
            "OPT" or "OTM" => "Optoma",
            "YLT" or "MEI" => "松下(Panasonic)",
            "MEL" => "三菱(Mitsubishi)",
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
            "GWR" or "GWR_" => "长城(Great Wall)",
            "HPC" => "惠浦(HPC)",
            "VSC" => "优派(ViewSonic)",
            "VIT" => "唯冠(VIT)",
            "IMA" => "理想(IMA)",
            "NEX" => "NEXO",
            "ELO" => "Elo Touch",
            "FUJ" or "FUS" => "富士通(Fujitsu)",
            "GGL" => "Google",
            "HHT" => "鸿合(Hitevision)",
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

    internal static string? CleanBoardManufacturer(string? raw)
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

    private static IReadOnlyList<string> DeviceNames(string className, Func<ManagementBaseObject, bool>? filter = null)
    {
        var names = Query(className)
            .Where(item => filter?.Invoke(item) ?? true)
            .Select(item => Get(item, "Name"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct();

        return names.ToArray();
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

    internal static bool ContainsAny(string? value, params string[] needles)
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

    /// <summary>
    /// 详情数据的统一入口：与硬件信息页同源，并遵循「使用 CPU-Z 数据源」设置。
    /// 需要展示硬件参数的地方（硬件详情页、性能测试报告等）都应走这里，避免各取各的数据。
    /// </summary>
    public static async Task<HardwareDetailData> LoadDetailForDisplayAsync(bool forceRefresh = false)
    {
        var data = await LoadDetailAsync(forceRefresh);
        if (!AppSettings.GetBool("UseCpuzDataSource", false)) return data;

        var cpuz = CpuzInfoService.CachedInfo;
        if (cpuz == null)
        {
            try
            {
                cpuz = await CpuzInfoService.FetchAsync(timeoutMs: 30000);
            }
            catch { }
        }

        return cpuz != null ? ApplyCpuzDetailOverride(data, cpuz) : data;
    }

    /// <summary>
    /// 系统名称（与硬件信息页「系统信息」一致，如 "Microsoft Windows 11 专业版 64 位"）。
    /// </summary>
    public static async Task<string> GetSystemInfoTextAsync()
    {
        try
        {
            var sections = await LoadAsync();
            if (sections.Count > 1)
            {
                return sections[1].Items.FirstOrDefault(item => item.Label == "系统")?.Value ?? "";
            }
        }
        catch { }
        return "";
    }

    /// <summary>单个分区采集失败只让该分区为空，不拖垮整页（WMI 单条查询失败并不少见）。</summary>
    private static T Safe<T>(Func<T> factory, T fallback)
    {
        try
        {
            return factory();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HardwareDetail] 采集失败: {ex.Message}");
            return fallback;
        }
    }

    private static HardwareDetailData BuildDetailData(bool forceRefresh)
    {
        if (!forceRefresh && _detailCache != null)
            return _detailCache;

        // 各分区互相独立：并行采集，总耗时取最慢的一组。
        // 授权 / TPM 这类慢查询各自带超时（见 BuildWindowsDetail / BuildSecurityDetail）。
        var windowsTask = Task.Run(() => Safe(BuildWindowsDetail, null));
        var computerTask = Task.Run(() => Safe(BuildComputerDetail, null));
        var cpuTask = Task.Run(() => Safe(BuildCpuDetail, new CpuDetail()));
        var boardTask = Task.Run(() => Safe(BuildMotherboardDetail, new MotherboardDetail()));
        var memoryTask = Task.Run(() => Safe(BuildMemoryDetail, new MemoryDetail()));
        var gpuTask = Task.Run(() => Safe(BuildGpuDetails, []));
        var diskTask = Task.Run(() => Safe(() => BuildDiskDetails(), new List<DiskDetail>()));
        var displayTask = Task.Run(() => Safe(BuildDisplayDetails, []));
        var soundTask = Task.Run(() => Safe(BuildSoundDetails, []));
        var networkTask = Task.Run(() => Safe(BuildNetworkDetails, []));
        var npuTask = Task.Run(() => Safe(BuildNpuDetail, null));
        var securityTask = Task.Run(() => Safe(BuildSecurityDetail, null));
        var batteryTask = Task.Run(() => Safe(BuildBatteryDetail, null));
        var usbTask = Task.Run(() => Safe(BuildUsbDetail, null));
        var issueTask = Task.Run(() => Safe(BuildOtherDevices, []));

        Task.WaitAll(
        [
            windowsTask, computerTask, cpuTask, boardTask, memoryTask, gpuTask, diskTask,
            displayTask, soundTask, networkTask, npuTask, securityTask, batteryTask, usbTask, issueTask,
        ]);

        var data = new HardwareDetailData
        {
            Windows = windowsTask.Result,
            Computer = computerTask.Result,
            Cpu = cpuTask.Result,
            Motherboard = boardTask.Result,
            Memory = memoryTask.Result,
            Gpus = gpuTask.Result,
            Disks = diskTask.Result,
            Displays = displayTask.Result,
            SoundDevices = soundTask.Result,
            NetworkAdapters = networkTask.Result,
            Npu = npuTask.Result,
            Security = securityTask.Result,
            Battery = batteryTask.Result,
            Usb = usbTask.Result,
            OtherDevices = issueTask.Result,
        };

        lock (_lock)
        {
            _detailCache = data;
            // 详情可能在监控引擎就绪后才读到温度；返回概览时不能继续复用早期的「未能读取」。
            // 替换副本，避免后台采集修改正在显示的行。
            if (_cache is not null) _cache = ApplyDiskDetailSnapshot(_cache, data.Disks);
        }
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
        {
            data.Memory.ChannelMode = cpuz.MemoryChannel;
            data.Memory.ChannelModeInferred = false;
        }

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
            // SMBIOS/WMI 的时钟在现代 CPU 上常固定为标称值，并非最高睿频或实时频率。
            detail.NominalClockSpeed = FormatMhz(Get(cpu, "MaxClockSpeed"));
            detail.CurrentClockSpeed = Safe(ReadCurrentCpuClock, null);
            detail.L2CacheSize = FormatCacheSize(Get(cpu, "L2CacheSize"));
            detail.L3CacheSize = FormatCacheSize(Get(cpu, "L3CacheSize"));
            detail.ExtClock = FormatMhz(Get(cpu, "ExtClock"));
            detail.Architecture = MapCpuArchitecture(Get(cpu, "Architecture"));
            detail.Manufacturer = Get(cpu, "Manufacturer");
            detail.ProcessorId = Get(cpu, "ProcessorId");
            detail.Socket = Get(cpu, "SocketDesignation");
            var dataWidth = ToInt(Get(cpu, "DataWidth"));
            if (dataWidth > 0) detail.DataWidth = $"{dataWidth} 位";
            var hypervisor = Safe(() => ReadTriState(First("Win32_ComputerSystem"), "HypervisorPresent"), null);
            detail.VirtualizationEnabled = ResolveProcessorFeature(
                ReadTriState(cpu, "VirtualizationFirmwareEnabled"), IsProcessorFeaturePresent(PfVirtFirmwareEnabled), hypervisor, true);
            detail.SlatEnabled = ResolveProcessorFeature(
                ReadTriState(cpu, "SecondLevelAddressTranslationExtensions"), IsProcessorFeaturePresent(PfSecondLevelAddressTranslation), hypervisor, false);
            detail.BrandKey = DetectCpuBrand(detail.Name);
        }

        return detail;
    }

    internal static bool? ResolveProcessorFeature(bool? reported, bool osSupported, bool? hypervisor, bool virtualization)
    {
        if (osSupported || reported == true) return true;
        // Hyper-V/VBS 接管后，底层扩展可能对应用不可见。运行中的 hypervisor
        // 可证明虚拟化在使用，但不能据此断言来宾系统支持嵌套 SLAT。
        if (hypervisor == true) return virtualization ? true : null;
        return reported;
    }

    private static string? ReadCurrentCpuClock()
    {
        using var sample = QueryScoped("root\\CIMV2",
            "SELECT ProcessorFrequency, PercentProcessorPerformance FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name='_Total'", 3)
            .FirstOrDefault();
        return CalculateCpuClock(ToInt(Get(sample, "ProcessorFrequency")), ToInt(Get(sample, "PercentProcessorPerformance")));
    }

    internal static string? CalculateCpuClock(int nominalMhz, int performancePercent)
    {
        // 性能百分比允许超过 100（睿频）。计数器不可用时不回退静态 WMI 假装实时值。
        if (nominalMhz <= 0 || performancePercent <= 0) return null;
        return $"{nominalMhz * (performancePercent / 100d) / 1000d:0.00} GHz";
    }

    internal static string? FormatMhz(string? value)
    {
        var mhz = ToInt(value);
        if (mhz <= 0) return null;
        if (mhz >= 1000) return $"{mhz / 1000d:0.#} GHz";
        return $"{mhz} MHz";
    }

    internal static string? FormatCacheSize(string? value)
    {
        var kb = ToInt(value);
        if (kb <= 0) return null;
        if (kb >= 1024) return $"{kb / 1024d:0.#} MB";
        return $"{kb} KB";
    }

    internal static string? MapCpuArchitecture(string? value)
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
            SerialNumber = CleanSerial(Get(board, "SerialNumber")),
            BiosBrand = Get(bios, "Manufacturer"),
            BiosVersion = Get(bios, "SMBIOSBIOSVersion"),
            BiosDate = FormatBiosDate(Get(bios, "ReleaseDate")),
            BiosSerialNumber = CleanSerial(Get(bios, "SerialNumber")),
            SmbiosVersion = FormatSmbiosVersion(Get(bios, "SMBIOSMajorVersion"), Get(bios, "SMBIOSMinorVersion"))
        };
    }

    internal static string? FormatBiosDate(string? value)
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

        var systemTotal = GetTotalPhysicalMemoryBytes();
        var totalBytes = systemTotal > 0 ? systemTotal : modules.Sum(item => ToLong(Get(item, "Capacity")));

        var memType = ToInt(modules.Select(item => Get(item, "SMBIOSMemoryType")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
        var typeLabel = GetMemoryTypeLabel(memType);

        var (bandwidthRate, busBits) = ReadMemoryBusInfo(modules);
        var memoryBandwidth = HardwareDisplayPolicy.ComputeMemoryBandwidthGbs(bandwidthRate, busBits);

        var detail = new MemoryDetail
        {
            TotalCapacity = totalBytes > 0 ? $"{totalBytes / 1024d / 1024d / 1024d:0.#} GB" : null,
            MemoryType = typeLabel,
            BusWidth = busBits > 0 ? $"{busBits}-bit" : null,
            Bandwidth = memoryBandwidth > 0 ? HardwareDisplayPolicy.FormatMemoryBandwidth(memoryBandwidth) : null,
            ChannelMode = InferMemoryChannel(modules.Select(mod => Get(mod, "DeviceLocator"))),
            ChannelModeInferred = true,
            MaxCapacity = ReadMaxMemoryCapacity(),
            TotalSlots = totalSlots,
            UsedSlots = modules.Count
        };

        foreach (var mod in modules)
        {
            var configuredSpeed = GetMemoryConfiguredClockSpeed(mod);
            var ratedSpeed = ToInt(Get(mod, "Speed"));
            var voltageMv = ToInt(Get(mod, "ConfiguredVoltage"));

            // 频率优先展示实际运行频率，没有再回退额定频率（部分平台没有 ConfiguredClockSpeed）。
            var speed = configuredSpeed > 0 ? configuredSpeed : ratedSpeed;

            detail.Modules.Add(new MemoryModuleDetail
            {
                Designation = FirstUseful(Get(mod, "BankLabel"), Get(mod, "DeviceLocator")),
                Capacity = FormatCapacity(ToLong(Get(mod, "Capacity"))),
                Speed = speed > 0 ? $"{speed} MHz" : null,
                RatedSpeed = ratedSpeed > 0 && ratedSpeed != speed ? $"{ratedSpeed} MHz" : null,
                Manufacturer = CleanMemManufacturer(Get(mod, "Manufacturer")),
                PartNumber = Get(mod, "PartNumber"),
                SerialNumber = CleanSerial(Get(mod, "SerialNumber")),
                Voltage = voltageMv > 0 ? $"{voltageMv / 1000d:0.###} V" : null,
                Type = GetMemoryTypeLabel(ToInt(Get(mod, "SMBIOSMemoryType"))) is { Length: > 0 } moduleType ? moduleType : typeLabel,
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

    internal static string? FormatCapacity(long bytes)
    {
        if (bytes <= 0) return null;
        return $"{bytes / 1024d / 1024d / 1024d:0.#} GB";
    }

    internal static string? MapFormFactor(string? value)
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
            if (HardwareDisplayPolicy.IsVirtualAdapter(name))
                continue;

            var width = Get(item, "CurrentHorizontalResolution");
            var height = Get(item, "CurrentVerticalResolution");
            var refresh = Get(item, "CurrentRefreshRate");

            string? vramText = null;
            if (name != null && TryMatchDxgiAdapter(name, dxgiAdapters, out int dxgiIdx))
            {
                var dedicated = dxgiAdapters[dxgiIdx].dedicatedVram;
                var shared = dxgiAdapters[dxgiIdx].sharedVram;
                vramText = HardwareDisplayPolicy.FormatGpuMemory(dedicated, shared);
                usedDxgiIndices.Add(dxgiIdx);
            }

            gpus.Add(new GpuDetail
            {
                Name = name,
                AdapterRAM = vramText,
                DriverVersion = Get(item, "DriverVersion"),
                DriverDate = FormatBiosDate(Get(item, "DriverDate")),
                VideoProcessor = Get(item, "VideoProcessor"),
                CurrentResolution = !string.IsNullOrWhiteSpace(width) && !string.IsNullOrWhiteSpace(height)
                    ? $"{width} x {height}" : null,
                CurrentRefreshRate = !string.IsNullOrWhiteSpace(refresh) && refresh != "0" ? $"{refresh} Hz" : null,
                PnpDeviceId = Get(item, "PNPDeviceID"),
                AdapterCompatibility = Get(item, "AdapterCompatibility"),
                Status = MapDeviceStatus(Get(item, "Status")),
                BrandKey = DetectGpuBrand(name)
            });
        }

        return gpus;
    }

    private static List<DiskDetail> BuildDiskDetails(bool includeFullDetails = true)
    {
        var disks = new List<DiskDetail>();
        var seenModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in Query("Win32_DiskDrive"))
        {
            var model = Get(item, "Model");
            if (string.IsNullOrWhiteSpace(model)) continue;
            seenModels.Add(model);

            var deviceId = Get(item, "DeviceID");
            var index = ToInt(Get(item, "Index"));
            var size = ToLong(Get(item, "Size"));
            var interfaceType = Get(item, "InterfaceType");
            var mediaType = Get(item, "MediaType");
            var rotationRate = ToLong(Get(item, "NominalMediaRotationRate"));
            if (rotationRate == 0)
                rotationRate = ToLong(Get(item, "SpinRate"));
            var diskMediaType = DetermineMediaType(mediaType, interfaceType, model, rotationRate);

            var disk = new DiskDetail
            {
                Index = index,
                Model = model,
                MediaType = diskMediaType,
                Size = size > 0 ? $"{size / 1024d / 1024d / 1024d:0.#} GB" : null,
                InterfaceType = MapInterfaceType(interfaceType),
                FirmwareRevision = Get(item, "FirmwareRevision"),
                SerialNumber = CleanSerial(Get(item, "SerialNumber")),
                PnpDeviceId = Get(item, "PNPDeviceID"),
                PartitionStyle = includeFullDetails ? ReadPartitionStyle(index) : null,
                // NVMe/部分 SSD 不报 NominalMediaRotationRate（0），按介质类型补「固态（无转速）」
                RotationRate = FormatRotationRate(rotationRate)
                    ?? (string.Equals(diskMediaType, "SSD", StringComparison.OrdinalIgnoreCase) ? "固态（无转速）" : null),
                Status = MapDeviceStatus(Get(item, "Status"))
            };

            if (includeFullDetails && !string.IsNullOrWhiteSpace(deviceId))
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

        foreach (var item in Query("Win32_PnPEntity"))
        {
            if (Get(item, "PNPClass") != "DiskDrive") continue;
            var name = Get(item, "Name");
            if (string.IsNullOrWhiteSpace(name) || seenModels.Contains(name)) continue;
            seenModels.Add(name);

            var pnpDeviceId = Get(item, "DeviceID");
            var interfaceType = InferDiskInterfaceFromPnpId(pnpDeviceId);

            disks.Add(new DiskDetail
            {
                Index = -1, // 只有 PnP 名称，无法定位物理盘序号（不参与 SMART 直读）
                Model = name,
                MediaType = DetermineMediaType(null, interfaceType, name, 0),
                InterfaceType = MapInterfaceType(interfaceType),
                PnpDeviceId = pnpDeviceId,
                Partitions = []
            });
        }

        EnrichDiskTemperatures(disks);
        EnrichDiskSmart(includeFullDetails ? disks : disks.Where(disk => !IsValidDiskTemperature(disk.Temperature)).ToList());

        return disks;
    }

    /// <summary>
    /// SMART 直读（CrystalDiskInfo 方案的同一个 DiskSmartReader）：健康度 / 通电时间 / 写入量。
    /// 读取失败（权限、USB 桥、虚拟盘）静默跳过对应行。
    /// </summary>
    private static void EnrichDiskSmart(List<DiskDetail> disks)
    {
        foreach (var disk in disks)
        {
            if (disk.Index < 0) continue;
            try
            {
                var isNvme = ContainsAny(disk.InterfaceType, "NVMe") || ContainsAny(disk.PnpDeviceId, "NVMe");
                var isSsd = string.Equals(disk.MediaType, "SSD", StringComparison.OrdinalIgnoreCase);
                var smart = DiskSmartReader.ReadDiskSmart((uint)disk.Index, isNvme, isSsd, disk.Model ?? "", disk.PnpDeviceId ?? "");
                disk.Temperature = ResolveDiskTemperature(disk.Temperature, smart.TemperatureC);
                if (!smart.HasSmart) continue;

                disk.SmartHealth = smart.Status switch
                {
                    DiskStatus.Good => smart.LifePercent is int life ? $"良好（{life}%）" : "良好",
                    DiskStatus.Caution => "警告",
                    DiskStatus.Bad => "异常",
                    _ => null,
                };
                if (smart.PowerOnHours is ulong hours)
                    disk.PowerOnHours = hours >= 10000 ? $"{hours:N0} 小时" : $"{hours} 小时";
                if (smart.PowerOnCount is ulong count)
                    disk.PowerOnCount = $"{count:N0} 次";
                if (smart.DataWrittenBytes is ulong written && written > 0)
                    disk.TotalWritten = FormatByteSize(written);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HardwareDetail] 磁盘 SMART 读取失败（{disk.Model}）: {ex.Message}");
            }
        }
    }

    private static string FormatByteSize(ulong bytes)
    {
        const double tb = 1024d * 1024d * 1024d * 1024d;
        const double gb = 1024d * 1024d * 1024d;
        if (bytes >= tb) return $"{bytes / tb:0.##} TB";
        if (bytes >= gb) return $"{bytes / gb:0.#} GB";
        return $"{bytes / 1024d / 1024d:0.#} MB";
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
                disk.Temperature = FindDiskTemperature(disk.Model, temps);
            }
        }
        catch { }
    }

    internal static string? DetermineMediaType(string? mediaType, string? interfaceType, string? model, long rotationRate)
    {
        if (!string.IsNullOrWhiteSpace(interfaceType)
            && interfaceType.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
            return "SSD";

        if (rotationRate > 0)
            return rotationRate == 1 ? "SSD" : "HDD";

        if (!string.IsNullOrWhiteSpace(model))
        {
            var m = model.ToUpperInvariant();
            if (m.Contains("SSD") || m.Contains("NVME") || m.Contains("SOLID"))
                return "SSD";
        }

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            var mt = mediaType.Trim().ToUpperInvariant();
            if (mt.Contains("SSD") || mt.Contains("SOLID"))
                return "SSD";
            if (mt.Contains("HDD") || mt.Contains("HARD DISK DRIVE"))
                return "HDD";
        }

        if (rotationRate == 0
            && !string.IsNullOrWhiteSpace(interfaceType)
            && !interfaceType.Contains("USB", StringComparison.OrdinalIgnoreCase)
            && !interfaceType.Contains("1394", StringComparison.OrdinalIgnoreCase)
            && !interfaceType.Contains("IDE", StringComparison.OrdinalIgnoreCase))
            return "SSD";

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            var mt = mediaType.Trim().ToUpperInvariant();
            if (mt.Contains("FIXED") || mt.Contains("HARD"))
                return null;
        }

        return null;
    }

    internal static string? MapInterfaceType(string? value)
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

    internal static string? InferDiskInterfaceFromPnpId(string? pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId)) return null;
        var upper = pnpDeviceId.ToUpperInvariant();
        if (upper.Contains("NVME")) return "NVMe";
        if (upper.Contains("USBSTOR")) return "USB";
        if (upper.Contains("IDE") || upper.Contains("CHANNEL")) return "IDE";
        if (upper.Contains("SCSI")) return "SCSI";
        return null;
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
        var wmiMadeDates = GetWmiMonitorMadeDatesByPnpCode();

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
                    var madeDate = pnpCode.Length > 0 && wmiMadeDates.TryGetValue(pnpCode, out var made)
                        ? FormatMonitorMadeDate(made.Year, made.Week)
                        : null;

                    results.Add(new DisplayDetail
                    {
                        Name = label,
                        Resolution = resolution,
                        RefreshRate = refreshRate,
                        IsPrimary = isPrimary,
                        DiagonalInches = diagonalInches.HasValue ? $"{diagonalInches.Value:F1}\"" : null,
                        MadeDate = madeDate
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
        if (!EnumDisplaySettings(displayDeviceName, ENUM_CURRENT_SETTINGS, ref mode) || mode.dmDisplayFrequency <= 1)
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
        var addresses = ReadNetworkAddresses();

        foreach (var item in Query("Win32_NetworkAdapter"))
        {
            if (!IsTrue(item, "PhysicalAdapter")) continue;
            var name = Get(item, "Name");
            if (ContainsAny(name, "Virtual", "Bluetooth", "WAN Miniport")) continue;

            var speed = ToLong(Get(item, "Speed"));
            var index = ToInt(Get(item, "Index"));
            addresses.TryGetValue(index, out var address);

            adapters.Add(new NetworkDetail
            {
                Name = name,
                Manufacturer = Get(item, "Manufacturer"),
                MacAddress = Get(item, "MACAddress"),
                Speed = speed > 0 ? FormatNetworkSpeed(speed) : null,
                AdapterType = Get(item, "AdapterType"),
                ConnectionStatus = MapNetworkConnectionStatus(ToInt(Get(item, "NetConnectionStatus"))),
                IpAddresses = address.Ip,
                Gateway = address.Gateway
            });
        }

        return adapters;
    }

    /// <summary>已启用网卡的 IP / 网关（按适配器 Index 关联 Win32_NetworkAdapterConfiguration）。</summary>
    private static Dictionary<int, (string? Ip, string? Gateway)> ReadNetworkAddresses()
    {
        var map = new Dictionary<int, (string?, string?)>();

        try
        {
            foreach (var item in Query("Win32_NetworkAdapterConfiguration"))
            {
                if (!IsTrue(item, "IPEnabled")) continue;

                var index = ToInt(Get(item, "Index"));
                var ips = item["IPAddress"] as string[];
                var gateways = item["DefaultIPGateway"] as string[];
                map[index] = (JoinAddresses(ips), JoinAddresses(gateways));
            }
        }
        catch { }

        return map;
    }

    internal static string FormatNetworkSpeed(long bps)
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
                DriverDate = FormatBiosDate(Get(item, "DriverDate")),
                DeviceId = Get(item, "DeviceId"),
                // NPU 设备名多为笼统型号，算力需结合 CPU 型号判断代数
                ComputeCapability = NpuCatalog.LookupTops(name, FirstName("Win32_Processor"))
            };
        }

        return null;
    }

    private static WindowsDetail BuildWindowsDetail()
    {
        var detail = new WindowsDetail();
        var os = First("Win32_OperatingSystem");

        FillWindowsVersion(detail, os);
        var osBuild = ToInt(Get(os, "BuildNumber"));
        detail.Architecture = Get(os, "OSArchitecture");
        detail.InstallDate = FormatInstallDate(Get(os, "InstallDate"));
        detail.InstallLanguage = MapInstallLanguageCode(FormatLcid(Get(os, "OSLanguage")));
        detail.Uptime = FormatUptimeSpan(TimeSpan.FromMilliseconds(Environment.TickCount64));
        detail.ComputerName = FirstUseful(Get(os, "CSName"), Environment.MachineName);
        detail.ProductId = Get(os, "SerialNumber");
        detail.RegisteredOwner = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "RegisteredOwner");
        detail.SystemDrive = Get(os, "SystemDrive");
        detail.WindowsDirectory = Get(os, "WindowsDirectory");

        var totalPhysical = ToLong(Get(os, "TotalVisibleMemorySize")) * 1024;
        var freePhysical = ToLong(Get(os, "FreePhysicalMemory")) * 1024;
        if (totalPhysical > 0)
            detail.PhysicalMemory = $"{FormatCapacity(freePhysical)} / {FormatCapacity(totalPhysical)}";

        var totalVirtual = ToLong(Get(os, "TotalVirtualMemorySize")) * 1024;
        var freeVirtual = ToLong(Get(os, "FreeVirtualMemory")) * 1024;
        if (totalVirtual > 0)
            detail.VirtualMemory = $"{FormatCapacity(freeVirtual)} / {FormatCapacity(totalVirtual)}";

        var computer = First("Win32_ComputerSystem");
        var domain = Get(computer, "Domain");
        if (!string.IsNullOrWhiteSpace(domain) && !string.Equals(domain, detail.ComputerName, StringComparison.OrdinalIgnoreCase))
            detail.DomainOrWorkgroup = domain;

        foreach (var pageFile in Query("Win32_PageFileUsage"))
        {
            var name = Get(pageFile, "Name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var allocated = ToLong(Get(pageFile, "AllocatedBaseSize"));
            var current = ToLong(Get(pageFile, "CurrentUsage"));
            detail.PageFile = allocated > 0
                ? $"{name}（已用 {current / 1024d:0.#} / {allocated / 1024d:0.#} GB）"
                : name;
            break;
        }

        FillLicense(detail);
        detail.DirectX = FormatDirectXVersion(ReadRegistryString(@"SOFTWARE\Microsoft\DirectX", "Version"), osBuild);
        detail.DotNetFramework = MapDotNetFrameworkRelease(ToIntOrNull(ReadRegistryString(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release")));
        detail.DisplayScaling = FormatDisplayScaling(ReadSystemDpi());

        return detail;
    }

    /// <summary>
    /// Windows 授权信息（SoftwareLicensingProduct）。这个 WMI 提供程序偶尔很慢，
    /// 限定 4 秒超时；超时/失败时授权相关行不显示，其余系统信息不受影响。
    /// </summary>
    private static void FillLicense(WindowsDetail detail)
    {
        const string query = "SELECT LicenseStatus, ProductKeyChannel, PartialProductKey FROM SoftwareLicensingProduct " +
                             "WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL";

        ManagementBaseObject? licensed = null;
        ManagementBaseObject? fallback = null;
        foreach (var item in QueryScoped(@"root\cimv2", query, 4))
        {
            if (ToInt(Get(item, "LicenseStatus")) == 1)
            {
                licensed = item;
                break;
            }
            fallback ??= item;
        }

        var chosen = licensed ?? fallback;
        if (chosen is null) return;

        detail.LicenseStatus = MapLicenseStatus(ToInt(Get(chosen, "LicenseStatus")));
        detail.LicenseChannel = MapProductKeyChannel(Get(chosen, "ProductKeyChannel"));
        detail.PartialProductKey = Get(chosen, "PartialProductKey");
    }

    private static ComputerDetail BuildComputerDetail()
    {
        var computer = First("Win32_ComputerSystem");
        var product = First("Win32_ComputerSystemProduct");
        var bios = First("Win32_BIOS");

        return new ComputerDetail
        {
            Manufacturer = Get(computer, "Manufacturer"),
            Model = Get(computer, "Model"),
            Family = Get(computer, "SystemFamily"),
            Sku = FirstUseful(Get(computer, "SystemSKUNumber"), Get(product, "SKUNumber")),
            SystemType = Get(computer, "SystemType"),
            PcType = MapPcSystemType(ToInt(Get(computer, "PCSystemType"))),
            Chassis = DecodeChassisTypes(ReadChassisTypes()),
            SerialNumber = FirstUseful(CleanSerial(Get(bios, "SerialNumber")), CleanSerial(Get(product, "IdentifyingNumber"))),
            Uuid = Get(product, "UUID"),
            ProductVersion = Get(product, "Version"),
            FirmwareMode = GetFirmwareMode()
        };
    }

    private static SecurityDetail BuildSecurityDetail()
    {
        var detail = new SecurityDetail();

        var hypervisor = ReadTriState(First("Win32_ComputerSystem"), "HypervisorPresent");
        detail.Hypervisor = hypervisor switch { true => "已检测到", false => "未检测到", _ => null };

        foreach (var item in QueryScoped(
                     @"root\Microsoft\Windows\DeviceGuard",
                     "SELECT VirtualizationBasedSecurityStatus FROM Win32_DeviceGuard",
                     3))
        {
            detail.Vbs = MapVbsStatus(ToInt(Get(item, "VirtualizationBasedSecurityStatus")));
            break;
        }

        detail.Hvci = MapToggle(ReadRegistryDword(
            @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled"));
        detail.SecureBoot = MapToggle(ReadRegistryDword(
            @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled"));

        FillTpm(detail);
        return detail;
    }

    /// <summary>TPM（Win32_Tpm）：这个提供程序在部分机器上单次要数秒，限定 5 秒超时（并行执行，不叠加到其它分区上）。</summary>
    private static void FillTpm(SecurityDetail detail)
    {
        foreach (var item in QueryScoped(
                     @"root\cimv2\security\microsofttpm",
                     "SELECT SpecVersion, ManufacturerIdTxt, IsEnabled_InitialValue, IsActivated_InitialValue FROM Win32_Tpm",
                     5))
        {
            var spec = Get(item, "SpecVersion");
            detail.TpmVersion = string.IsNullOrWhiteSpace(spec) ? null : spec.Split(',')[0].Trim();
            detail.TpmManufacturer = Get(item, "ManufacturerIdTxt");

            var enabled = ReadTriState(item, "IsEnabled_InitialValue");
            var activated = ReadTriState(item, "IsActivated_InitialValue");
            detail.TpmState = enabled switch
            {
                true => activated == true ? "已启用并已激活" : "已启用",
                false => "已禁用",
                _ => null,
            };
            break;
        }
    }

    private static void FillWindowsVersion(WindowsDetail detail, ManagementBaseObject? os)
    {
        detail.ProductName = CleanProductName(Get(os, "Caption"));
        detail.Build = FormatWindowsBuild(Get(os, "BuildNumber"),
            ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR"));
        detail.DisplayVersion = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
    }

    private static BatteryDetail? BuildBatteryDetail()
    {
        var battery = First("Win32_Battery");
        if (battery is null) return null;

        var designed = ToLong(Get(battery, "DesignCapacity"));
        var full = ToLong(Get(battery, "FullChargeCapacity"));

        var detail = new BatteryDetail
        {
            Name = Get(battery, "Name"),
            Chemistry = MapBatteryChemistry(ToInt(Get(battery, "Chemistry"))),
            Status = MapDeviceStatus(Get(battery, "Status")),
        };

        // Win32_Battery 的容量字段经常为空，root\WMI 的电池类才是可靠来源。
        string? instanceName = null;
        foreach (var item in QueryScoped(@"root\WMI", "SELECT * FROM BatteryStaticData", 3))
        {
            instanceName = Get(item, "InstanceName");
            designed = designed > 0 ? designed : ToLong(Get(item, "DesignedCapacity"));
            detail.Manufacturer ??= Get(item, "ManufactureName");
            detail.SerialNumber ??= CleanSerial(Get(item, "SerialNumber"));
            detail.Name ??= Get(item, "DeviceName");
            break;
        }

        foreach (var item in QueryScoped(@"root\WMI", "SELECT * FROM BatteryFullChargedCapacity", 3))
        {
            if (instanceName is not null && !string.Equals(instanceName, Get(item, "InstanceName"), StringComparison.OrdinalIgnoreCase)) continue;
            full = full > 0 ? full : ToLong(Get(item, "FullChargedCapacity"));
            break;
        }

        foreach (var item in QueryScoped(@"root\WMI", "SELECT * FROM BatteryCycleCount", 3))
        {
            if (instanceName is not null && !string.Equals(instanceName, Get(item, "InstanceName"), StringComparison.OrdinalIgnoreCase)) continue;
            var raw = Get(item, "CycleCount");
            if (uint.TryParse(raw, out var cycles) && cycles != uint.MaxValue)
                detail.CycleCount = $"{cycles} 次";
            break;
        }

        detail.DesignCapacity = FormatEnergy(designed);
        detail.FullChargeCapacity = FormatEnergy(full);
        if (designed > 0 && full > 0)
            detail.Health = $"{Math.Min(100d, full * 100d / designed):0}%";

        return detail;
    }

    private const int MaxUsbDevicesShown = 12;

    private static UsbDetail? BuildUsbDetail()
    {
        var controllers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Query("Win32_USBController"))
        {
            var name = Get(item, "Name");
            if (!string.IsNullOrWhiteSpace(name)) controllers.Add(name.Trim());
        }

        var devices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Query("Win32_PnPEntity"))
        {
            if (!string.Equals(Get(item, "PNPClass"), "USB", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Get(item, "Name");
            // 控制器同时也会以 PnP 设备出现，避免同一个名字在两个小节里重复
            if (!string.IsNullOrWhiteSpace(name) && !controllers.Contains(name.Trim()))
                devices.Add(name.Trim());
        }

        if (controllers.Count == 0 && devices.Count == 0) return null;

        var detail = new UsbDetail { DeviceTotalCount = devices.Count };
        detail.Controllers.AddRange(controllers.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        detail.Devices.AddRange(devices
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxUsbDevicesShown));
        return detail;
    }

    private static List<DeviceIssueDetail> BuildOtherDevices()
    {
        var issues = new List<DeviceIssueDetail>();

        foreach (var item in Query("Win32_PnPEntity"))
        {
            var code = ToInt(Get(item, "ConfigManagerErrorCode"));
            if (code is 0 or 45) continue; // 0 = 正常；45 = 当前未连接（蓝牙配对等），不是故障

            var name = Get(item, "Name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            issues.Add(new DeviceIssueDetail
            {
                Name = name.Trim(),
                Status = MapDeviceErrorCode(code),
                DeviceId = Get(item, "DeviceID")
            });
        }

        return issues;
    }

    private static string MapDeviceErrorCode(int code) => code switch
    {
        1 => "未正确配置",
        3 => "驱动损坏或内存不足",
        10 => "无法启动",
        12 => "资源不足",
        14 => "需要重启",
        16 => "资源无法识别",
        18 => "需要重新安装驱动",
        19 => "注册表信息损坏",
        21 => "正在移除",
        22 => "已被禁用",
        24 => "设备不存在",
        28 => "驱动未安装",
        29 => "固件未提供资源",
        31 => "无法加载驱动",
        32 => "驱动启动被禁用",
        37 => "驱动初始化失败",
        43 => "设备报告故障",
        _ => $"错误代码 {code}",
    };

    private static string? MapPcSystemType(int type) => type switch
    {
        1 => "台式机",
        2 => "笔记本",
        3 => "工作站",
        4 => "企业服务器",
        5 => "SOHO 服务器",
        6 => "家电电脑",
        7 => "高性能服务器",
        8 => "最高端服务器",
        _ => null,
    };

    private static string? MapBatteryChemistry(int chemistry) => chemistry switch
    {
        1 => "其他",
        2 => "未知",
        3 => "铅酸",
        4 => "镍镉",
        5 => "镍氢",
        6 => "锂离子",
        7 => "镍锌",
        8 => "锂聚合物",
        _ => null,
    };

    private static string? MapToggle(int? value) => value switch
    {
        1 => "已启用",
        0 => "未启用",
        _ => null,
    };

    private static string? FormatEnergy(long milliWattHours)
    {
        if (milliWattHours <= 0 || milliWattHours >= uint.MaxValue) return null;
        return milliWattHours >= 1000 ? $"{milliWattHours / 1000d:0.0} Wh" : $"{milliWattHours} mWh";
    }

    /// <summary>CPU 的布尔属性：未填时保持 null（不显示「未启用」误导用户）。</summary>
    private static bool? ReadTriState(ManagementBaseObject? item, string propertyName)
    {
        var value = Get(item, propertyName);
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Equals("True", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("False", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    /// <summary>按 BIOS 的 Channel 名称推测；Controller 编号不计作额外通道，不能据此确定运行模式。</summary>
    internal static string? InferMemoryChannel(IEnumerable<string?> locators)
    {
        var channels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var locator in locators)
        {
            if (string.IsNullOrWhiteSpace(locator)) return null;
            var match = Regex.Match(locator, @"(?<![a-z0-9])channel[\s_\-]*([a-z]|\d+)(?=$|[\s_\-])", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            channels.Add(match.Groups[1].Value);
        }

        if (channels.Count == 0) return null;

        return channels.Count switch
        {
            1 => "单通道",
            2 => "双通道",
            3 => "三通道",
            4 => "四通道",
            _ => $"{channels.Count} 通道",
        };
    }

    private static string? ReadMaxMemoryCapacity()
    {
        try
        {
            foreach (var item in Query("Win32_PhysicalMemoryArray"))
            {
                // 两个字段的单位都是 KB（MaxCapacityEx 是它的 64 位版本，防止 4 TB 溢出）。
                var maxKb = Math.Max(ToLong(Get(item, "MaxCapacityEx")), ToLong(Get(item, "MaxCapacity")));
                if (maxKb > 0) return FormatCapacity(maxKb * 1024);
            }
        }
        catch { }

        return null;
    }

    private static int[] ReadChassisTypes()
    {
        try
        {
            foreach (var item in Query("Win32_SystemEnclosure"))
            {
                if (item["ChassisTypes"] is ushort[] array)
                    return array.Select(type => (int)type).ToArray();
                if (item["ChassisTypes"] is int[] ints)
                    return ints;
            }
        }
        catch { }

        return [];
    }

    private static string? GetFirmwareMode()
    {
        try
        {
            return GetFirmwareType(out var firmwareType)
                ? firmwareType switch { 1 => "Legacy BIOS", 2 => "UEFI", _ => null }
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static int ReadSystemDpi()
    {
        try
        {
            var dpi = GetDpiForSystem();
            return dpi is > 0 and <= 1000 ? (int)dpi : 96;
        }
        catch
        {
            return 96;
        }
    }

    /// <summary>OSLanguage（十进制 LCID）→ 十六进制字符串（授权/语言映射都按十六进制处理）。</summary>
    private static string? FormatLcid(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return int.TryParse(raw, out var lcid) && lcid > 0 ? lcid.ToString("X4") : null;
    }

    private static string? FormatInstallDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTime.TryParse(raw, out var date)) return date.ToString("yyyy-MM-dd");

        var digits = raw.Trim();
        return digits.Length >= 8 && digits[..8].All(char.IsDigit)
            ? $"{digits[..4]}-{digits[4..6]}-{digits[6..8]}"
            : digits;
    }

    private static int? ToIntOrNull(string? value) => int.TryParse(value, out var number) ? number : null;

    /// <summary>指定命名空间/超时的 WMI 查询（慢提供程序用；失败或超时返回空列表）。</summary>
    private static List<ManagementBaseObject> QueryScoped(string scopePath, string query, int timeoutSeconds)
    {
        var items = new List<ManagementBaseObject>();

        try
        {
            var options = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scopePath), new ObjectQuery(query), options);
            foreach (ManagementBaseObject item in searcher.Get())
                items.Add(item);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HardwareDetail] WMI 查询失败/超时（{scopePath}）: {ex.Message}");
        }

        return items;
    }

    /// <summary>64 位注册表视图读取（x86 构建下 SKLM\SOFTWARE 会被重定向到 Wow6432Node）。</summary>
    private static string? ReadRegistryString(string subKey, string valueName)
    {
        try
        {
            using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(
                Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static int? ReadRegistryDword(string subKey, string valueName)
    {
        try
        {
            using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(
                Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(valueName) is int value ? value : null;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Detail Mapping Helpers

    /// <summary>内部版本号：Build + UBR（系统更新号），如 "26100.4946"。</summary>
    internal static string? FormatWindowsBuild(string? build, string? ubr)
    {
        if (string.IsNullOrWhiteSpace(build)) return null;
        var b = build.Trim();
        return string.IsNullOrWhiteSpace(ubr) ? b : $"{b}.{ubr.Trim()}";
    }

    /// <summary>去掉 Caption 的 "Microsoft " 前缀（Win11 上注册表 ProductName 还停留在 Windows 10，不能用）。</summary>
    internal static string? CleanProductName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        const string prefix = "Microsoft ";
        return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[prefix.Length..].Trim()
            : trimmed;
    }

    /// <summary>SoftwareLicensingProduct.LicenseStatus → 中文状态。</summary>
    internal static string? MapLicenseStatus(int status) => status switch
    {
        0 => "未授权",
        1 => "已激活",
        2 => "初始宽限期",
        3 => "宽限期已过",
        4 => "非正版宽限期",
        5 => "通知模式",
        6 => "延长宽限期",
        _ => null,
    };

    /// <summary>授权通道（ProductKeyChannel）→ 中文；未知通道原样返回。</summary>
    internal static string? MapProductKeyChannel(string? channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) return null;
        return channel.Trim() switch
        {
            "Retail" => "零售版",
            "OEM" => "OEM 预装",
            "Volume:MAK" => "批量授权 (MAK)",
            "Volume:GVLK" => "批量授权 (KMS 客户端)",
            _ => channel.Trim(),
        };
    }

    /// <summary>Device Guard 的 VBS 状态（0 未启用 / 1 已启用未运行 / 2 已启用并运行）。</summary>
    internal static string? MapVbsStatus(int status) => status switch
    {
        0 => "未启用",
        1 => "已启用（未运行）",
        2 => "已启用并运行",
        _ => null,
    };

    /// <summary>.NET Framework 4.x Release 值 → 版本名（阈值来自微软官方 .NET 版本对照表）。</summary>
    internal static string? MapDotNetFrameworkRelease(int? release)
    {
        if (release is not > 0) return null;
        var value = release.Value;

        return value switch
        {
            >= 533320 => ".NET Framework 4.8.1",
            >= 528040 => ".NET Framework 4.8",
            >= 461808 => ".NET Framework 4.7.2",
            >= 461308 => ".NET Framework 4.7.1",
            >= 460798 => ".NET Framework 4.7",
            >= 394802 => ".NET Framework 4.6.2",
            >= 394254 => ".NET Framework 4.6.1",
            >= 393295 => ".NET Framework 4.6",
            >= 379893 => ".NET Framework 4.5.2",
            >= 378758 => ".NET Framework 4.5.1",
            >= 378389 => ".NET Framework 4.5",
            _ => null,
        };
    }

    /// <summary>
    /// DirectX 版本：注册表里是 d3d 文件版本（Win10/11 上仍是 "4.09.00.0904"），
    /// 必须结合系统内部版本号才能给出「DirectX 12」这类用户可读的结论。
    /// </summary>
    internal static string FormatDirectXVersion(string? raw, int osBuild)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return osBuild >= 9200 ? "DirectX 12" : osBuild >= 7600 ? "DirectX 11" : "DirectX 9.0c";

        var version = raw.Trim();
        return version switch
        {
            _ when version.StartsWith("6.00", StringComparison.Ordinal) => $"DirectX 10 ({version})",
            _ when version.StartsWith("6.01", StringComparison.Ordinal) => $"DirectX 11 ({version})",
            _ when version.StartsWith("6.02", StringComparison.Ordinal) => $"DirectX 11.1 ({version})",
            _ when version.StartsWith("6.03", StringComparison.Ordinal) =>
                osBuild >= 10240 ? $"DirectX 12 ({version})" : $"DirectX 11.2 ({version})",
            _ when version.StartsWith("4.", StringComparison.Ordinal) =>
                osBuild >= 8000 ? $"DirectX 12 ({version})" : $"DirectX 9.0c ({version})",
            _ => osBuild >= 8000 ? $"DirectX 12 ({version})" : $"DirectX 9.0c ({version})",
        };
    }

    /// <summary>系统 DPI → 缩放百分比（96 = 100%）。</summary>
    internal static string? FormatDisplayScaling(int? dpi)
    {
        if (dpi is not > 0) return null;
        return $"{Math.Round(dpi.Value / 96.0 * 100):0}%";
    }

    /// <summary>取第一个可识别的机箱类型（SMBIOS ChassisTypes；0 是占位值跳过）。</summary>
    internal static string? DecodeChassisTypes(int[]? types)
    {
        if (types is null || types.Length == 0) return null;

        foreach (var type in types)
        {
            var label = ChassisTypeLabel(type);
            if (label is not null) return label;
        }

        return null;
    }

    private static string? ChassisTypeLabel(int type) => type switch
    {
        1 => "其他",
        3 => "台式机",
        4 => "低矮台式机",
        5 => "超薄台式机",
        6 => "迷你塔式",
        7 => "塔式",
        8 => "便携式",
        9 => "笔记本",
        10 => "笔记本",
        11 => "手持设备",
        12 => "扩展坞",
        13 => "一体机",
        14 => "轻薄笔记本",
        15 => "节约空间型台式机",
        16 => "便当盒式",
        17 => "主机箱",
        18 => "扩展机箱",
        19 => "子机箱",
        20 => "总线扩展机箱",
        21 => "外设机箱",
        22 => "存储机箱",
        23 => "机架式机箱",
        24 => "密封式电脑",
        25 => "多系统机箱",
        26 => "CompactPCI",
        27 => "AdvancedTCA",
        28 => "刀片服务器",
        29 => "刀片机箱",
        30 => "平板",
        31 => "可变式",
        32 => "可拆卸式",
        33 => "IoT 网关",
        34 => "嵌入式电脑",
        35 => "迷你主机",
        36 => "电视棒",
        _ => null,
    };

    /// <summary>安装语言 LCID（十六进制字符串，如 "0804"）→ 本地语言名；无法解析时原样返回。</summary>
    internal static string? MapInstallLanguageCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        try
        {
            var lcid = Convert.ToInt32(code.Trim(), 16);
            return System.Globalization.CultureInfo.GetCultureInfo(lcid).NativeName;
        }
        catch
        {
            return code.Trim();
        }
    }

    /// <summary>运行时长：按量级显示（天/小时/分钟）。</summary>
    internal static string FormatUptimeSpan(TimeSpan uptime)
    {
        if (uptime < TimeSpan.Zero) uptime = TimeSpan.Zero;

        if (uptime.Days > 0) return $"{uptime.Days} 天 {uptime.Hours} 小时 {uptime.Minutes} 分钟";
        if (uptime.Hours > 0) return $"{uptime.Hours} 小时 {uptime.Minutes} 分钟";
        return $"{uptime.Minutes} 分钟";
    }

    /// <summary>NominalMediaRotationRate：1 是 SSD 哨兵值，0/负值无意义。</summary>
    internal static string? FormatRotationRate(long rate)
    {
        if (rate == 1) return "固态（无转速）";
        if (rate > 1) return $"{rate} RPM";
        return null;
    }

    /// <summary>Win32 PARTITION_STYLE 数值（0 MBR / 1 GPT / 2 RAW）→ 名称。</summary>
    internal static string? MapPartitionStyle(string? raw) => raw?.Trim() switch
    {
        "0" => "MBR",
        "1" => "GPT",
        "2" => "RAW",
        _ => null,
    };

    /// <summary>Win32 PnP 设备状态串 → 中文；未知状态原样返回。</summary>
    internal static string? MapDeviceStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Trim() switch
        {
            "OK" => "正常",
            "Error" => "错误",
            "Degraded" => "性能下降",
            "Unknown" => "未知",
            "Pred Fail" => "预测故障",
            "Starting" => "正在启动",
            "Stopping" => "正在停止",
            "Service" => "服务中",
            "Stressed" => "处于压力",
            "NonRecover" => "不可恢复",
            "No Contact" => "失联",
            "Lost Comm" => "通信丢失",
            _ => raw.Trim(),
        };
    }

    /// <summary>序列号清洗：剔除 BIOS 占位串（全 0/全 F、To be filled by O.E.M. 等）。</summary>
    internal static string? CleanSerial(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = raw.Trim();
        if (cleaned.Length == 0) return null;

        if (cleaned.All(c => c == '0') || cleaned.All(c => c is 'F' || c is 'f'))
            return null;

        return cleaned.ToUpperInvariant() switch
        {
            "TO BE FILLED BY O.E.M." or "TO BE FILLED BY OEM" or "TO BE FILLED" or
            "DEFAULT STRING" or "UNKNOWN" or "NONE" or "N/A" or "NOT SPECIFIED" or
            "NOT AVAILABLE" or "SYSTEM SERIAL NUMBER" or "SYSTEM" or "OEM" or "X" => null,
            _ => cleaned,
        };
    }

    /// <summary>SMBIOS 主/次版本（BIOS WMI 是数字字符串）→ "3.6"。</summary>
    internal static string? FormatSmbiosVersion(string? major, string? minor)
    {
        if (string.IsNullOrWhiteSpace(major)) return null;
        var majorTrimmed = major.Trim();
        return string.IsNullOrWhiteSpace(minor) ? majorTrimmed : $"{majorTrimmed}.{minor.Trim()}";
    }

    /// <summary>NDIS 连接状态（Win32_NetworkAdapter.NetConnectionStatus）→ 中文。</summary>
    internal static string? MapNetworkConnectionStatus(int status) => status switch
    {
        0 => "已断开",
        1 => "正在连接",
        2 => "已连接",
        3 => "正在断开",
        4 => "硬件不存在",
        5 => "硬件已禁用",
        6 => "硬件故障",
        7 => "媒体已断开",
        8 => "正在验证身份",
        9 => "身份验证成功",
        10 => "身份验证失败",
        11 => "地址无效",
        12 => "需要凭据",
        _ => null,
    };

    /// <summary>IP 地址列表：过滤链路本地/无效地址后拼接；没有有效地址返回 null。</summary>
    internal static string? JoinAddresses(IReadOnlyList<string>? addresses)
    {
        if (addresses is null || addresses.Count == 0) return null;

        var valid = addresses
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Select(address => address.Trim())
            .Where(address => !address.StartsWith("fe80", StringComparison.OrdinalIgnoreCase)
                           && address is not "0.0.0.0" and not "::" and not "::1")
            .ToList();

        return valid.Count > 0 ? string.Join(", ", valid) : null;
    }

    /// <summary>显示器制造日期（WmiMonitorID 的年 + 周）。</summary>
    internal static string? FormatMonitorMadeDate(int year, int week)
    {
        if (year <= 0) return null;
        return week > 0 ? $"{year} 年第 {week} 周" : $"{year} 年";
    }

    #endregion
}
