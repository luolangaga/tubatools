namespace TubaWinUi3.Models;

public sealed class HardwareDetailData
{
    /// <summary>Windows 系统信息（版本、授权、DirectX、.NET、缩放、运行时间等）。</summary>
    public WindowsDetail? Windows { get; init; }

    /// <summary>整机身份（厂商/型号/序列号/UUID/机箱/固件模式）。</summary>
    public ComputerDetail? Computer { get; init; }

    public CpuDetail? Cpu { get; init; }
    public MotherboardDetail? Motherboard { get; init; }
    public MemoryDetail Memory { get; init; } = new();
    public List<GpuDetail> Gpus { get; init; } = [];
    public List<DiskDetail> Disks { get; init; } = [];
    public List<DisplayDetail> Displays { get; init; } = [];
    public List<SoundDetail> SoundDevices { get; init; } = [];
    public List<NetworkDetail> NetworkAdapters { get; init; } = [];
    public NpuDetail? Npu { get; init; }

    /// <summary>安全特性（TPM / 安全启动 / VBS / 内存完整性）。</summary>
    public SecurityDetail? Security { get; init; }

    /// <summary>电池（仅有电池的机器才非空）。</summary>
    public BatteryDetail? Battery { get; init; }

    /// <summary>USB 控制器与设备。</summary>
    public UsbDetail? Usb { get; init; }

    /// <summary>存在驱动异常的其他设备（ConfigManagerErrorCode 非 0）。</summary>
    public List<DeviceIssueDetail> OtherDevices { get; init; } = [];
}

public sealed class WindowsDetail
{
    public string? ProductName { get; set; }
    /// <summary>功能更新版本（注册表 DisplayVersion，如 "25H2"）。</summary>
    public string? DisplayVersion { get; set; }
    /// <summary>内部版本（Build.UBR，如 "26100.4946"）。</summary>
    public string? Build { get; set; }
    public string? Architecture { get; set; }
    public string? InstallDate { get; set; }
    public string? InstallLanguage { get; set; }
    public string? Uptime { get; set; }
    public string? ComputerName { get; set; }
    /// <summary>域或工作组。</summary>
    public string? DomainOrWorkgroup { get; set; }
    public string? ProductId { get; set; }
    public string? RegisteredOwner { get; set; }
    public string? LicenseStatus { get; set; }
    public string? LicenseChannel { get; set; }
    public string? PartialProductKey { get; set; }
    public string? DirectX { get; set; }
    public string? DotNetFramework { get; set; }
    public string? DisplayScaling { get; set; }
    public string? SystemDrive { get; set; }
    public string? WindowsDirectory { get; set; }
    /// <summary>可用 / 总量 物理内存。</summary>
    public string? PhysicalMemory { get; set; }
    /// <summary>可用 / 总量 虚拟内存。</summary>
    public string? VirtualMemory { get; set; }
    public string? PageFile { get; set; }
}

public sealed class ComputerDetail
{
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Family { get; set; }
    public string? Sku { get; set; }
    public string? SystemType { get; set; }
    /// <summary>PCSystemType（台式机/笔记本/工作站…）。</summary>
    public string? PcType { get; set; }
    /// <summary>机箱类型（SMBIOS ChassisTypes）。</summary>
    public string? Chassis { get; set; }
    public string? SerialNumber { get; set; }
    public string? Uuid { get; set; }
    public string? ProductVersion { get; set; }
    /// <summary>固件模式：UEFI / Legacy BIOS。</summary>
    public string? FirmwareMode { get; set; }
}

public sealed class CpuDetail
{
    public string? Name { get; set; }
    public string? CodeName { get; set; }
    public string? Package { get; set; }
    public int Cores { get; set; }
    public int Threads { get; set; }
    /// <summary>固件/WMI 报告的标称时钟，不是最高睿频。</summary>
    public string? NominalClockSpeed { get; set; }
    public string? CurrentClockSpeed { get; set; }
    public string? L2CacheSize { get; set; }
    public string? L3CacheSize { get; set; }
    public string? ExtClock { get; set; }
    public string? Architecture { get; set; }
    public string? Manufacturer { get; set; }
    public string? ProcessorId { get; set; }
    /// <summary>插槽（SocketDesignation）。</summary>
    public string? Socket { get; set; }
    /// <summary>数据位宽（"64 位"）。</summary>
    public string? DataWidth { get; set; }
    public bool? VirtualizationEnabled { get; set; }
    public bool? SlatEnabled { get; set; }
    public string? BrandKey { get; set; }
    public bool IsVerified { get; set; }
}

public sealed class MotherboardDetail
{
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Version { get; set; }
    public string? Chipset { get; set; }
    public string? SerialNumber { get; set; }
    public string? BiosBrand { get; set; }
    public string? BiosVersion { get; set; }
    public string? BiosDate { get; set; }
    public string? BiosSerialNumber { get; set; }
    /// <summary>SMBIOS 版本（如 "3.6"）。</summary>
    public string? SmbiosVersion { get; set; }
    public bool IsVerified { get; set; }
}

public sealed class MemoryDetail
{
    public string? TotalCapacity { get; set; }
    public string? MemoryType { get; set; }
    /// <summary>总位宽（"128-bit"）。</summary>
    public string? BusWidth { get; set; }
    /// <summary>理论带宽（"153.6 GB/s"）。</summary>
    public string? Bandwidth { get; set; }
    public string? ChannelMode { get; set; }
    public bool ChannelModeInferred { get; set; }
    /// <summary>主板支持的最大内存容量。</summary>
    public string? MaxCapacity { get; set; }
    public int TotalSlots { get; set; }
    public int UsedSlots { get; set; }
    public List<MemoryModuleDetail> Modules { get; init; } = [];
}

public sealed class MemoryModuleDetail
{
    public string? Designation { get; set; }
    public string? Capacity { get; set; }
    /// <summary>当前运行频率。</summary>
    public string? Speed { get; set; }
    /// <summary>额定频率（SPD Speed）。</summary>
    public string? RatedSpeed { get; set; }
    public string? Manufacturer { get; set; }
    public string? PartNumber { get; set; }
    public string? SerialNumber { get; set; }
    public string? Voltage { get; set; }
    public string? Type { get; set; }
    public string? FormFactor { get; set; }
}

public sealed class GpuDetail
{
    public string? Name { get; set; }
    public string? GpuCode { get; set; }
    public string? AdapterRAM { get; set; }
    public string? MemorySize { get; set; }
    public string? MemoryType { get; set; }
    public string? MemoryBus { get; set; }
    public string? DriverVersion { get; set; }
    public string? DriverDate { get; set; }
    public string? VideoProcessor { get; set; }
    public string? CurrentResolution { get; set; }
    public string? CurrentRefreshRate { get; set; }
    public string? DeviceId { get; set; }
    public string? PnpDeviceId { get; set; }
    /// <summary>适配器厂商（AdapterCompatibility）。</summary>
    public string? AdapterCompatibility { get; set; }
    public string? Status { get; set; }
    public string? BrandKey { get; set; }
    public bool IsVerified { get; set; }
}

public sealed class DiskDetail
{
    /// <summary>物理磁盘序号（\\.\PhysicalDriveN 的 N）。</summary>
    public int Index { get; set; }
    public string? Model { get; set; }
    public string? MediaType { get; set; }
    public string? Size { get; set; }
    public string? InterfaceType { get; set; }
    public string? FirmwareRevision { get; set; }
    public string? SerialNumber { get; set; }
    public string? PnpDeviceId { get; set; }
    /// <summary>分区样式（MBR / GPT / RAW）。</summary>
    public string? PartitionStyle { get; set; }
    /// <summary>转速（"7200 RPM" / "固态（无转速）"）。</summary>
    public string? RotationRate { get; set; }
    /// <summary>设备状态（"正常"）。</summary>
    public string? Status { get; set; }
    public float? Temperature { get; set; }

    // SMART 直读（可能为空，视权限与磁盘支持）
    /// <summary>SMART 健康（如 "良好（98%）"）。</summary>
    public string? SmartHealth { get; set; }
    public string? PowerOnHours { get; set; }
    public string? PowerOnCount { get; set; }
    /// <summary>累计写入量（按量级格式化）。</summary>
    public string? TotalWritten { get; set; }

    public List<PartitionDetail> Partitions { get; init; } = [];
}

public sealed class PartitionDetail
{
    public string? Name { get; set; }
    public string? DriveLetter { get; set; }
    public string? FileSystem { get; set; }
    public string? Size { get; set; }
    public string? FreeSpace { get; set; }
}

public sealed class DisplayDetail
{
    public string? Name { get; set; }
    public string? Resolution { get; set; }
    public string? RefreshRate { get; set; }
    public bool IsPrimary { get; set; }
    public string? DiagonalInches { get; set; }
    /// <summary>制造日期（"2025 年第 28 周"）。</summary>
    public string? MadeDate { get; set; }
}

public sealed class SoundDetail
{
    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? Status { get; set; }
}

public sealed class NetworkDetail
{
    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? MacAddress { get; set; }
    public string? Speed { get; set; }
    public string? AdapterType { get; set; }
    /// <summary>连接状态（"已连接"）。</summary>
    public string? ConnectionStatus { get; set; }
    /// <summary>IP 地址（逗号分隔，已过滤链路本地）。</summary>
    public string? IpAddresses { get; set; }
    public string? Gateway { get; set; }
}

public sealed class SecurityDetail
{
    public string? TpmVersion { get; set; }
    public string? TpmManufacturer { get; set; }
    public string? TpmState { get; set; }
    /// <summary>安全启动（"已启用" / "未启用"）。</summary>
    public string? SecureBoot { get; set; }
    /// <summary>虚拟化安全（VBS）。</summary>
    public string? Vbs { get; set; }
    /// <summary>内存完整性（HVCI）。</summary>
    public string? Hvci { get; set; }
    /// <summary>虚拟机监控程序。</summary>
    public string? Hypervisor { get; set; }
}

public sealed class BatteryDetail
{
    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? Chemistry { get; set; }
    /// <summary>设备状态（"正常"）。</summary>
    public string? Status { get; set; }
    public string? SerialNumber { get; set; }
    /// <summary>设计容量（"70.0 Wh"）。</summary>
    public string? DesignCapacity { get; set; }
    /// <summary>当前满充容量。</summary>
    public string? FullChargeCapacity { get; set; }
    /// <summary>健康度（满充 / 设计）。</summary>
    public string? Health { get; set; }
    /// <summary>循环次数（"110 次"）。</summary>
    public string? CycleCount { get; set; }
}

public sealed class UsbDetail
{
    public List<string> Controllers { get; init; } = [];
    public List<string> Devices { get; init; } = [];
    /// <summary>设备总数（列表可能被截断显示）。</summary>
    public int DeviceTotalCount { get; init; }
}

/// <summary>驱动异常的其他设备。</summary>
public sealed class DeviceIssueDetail
{
    public string? Name { get; set; }
    /// <summary>状态描述（"未安装驱动"）。</summary>
    public string? Status { get; set; }
    public string? DeviceId { get; set; }
}

public sealed class NpuDetail
{
    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? DriverVersion { get; set; }
    public string? DriverDate { get; set; }
    public string? DeviceId { get; set; }

    /// <summary>算力（如 "48 TOPS"），通过型号查 NpuCatalog 得到，未知为 null。</summary>
    public string? ComputeCapability { get; set; }
}
