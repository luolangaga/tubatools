using System.Text.Json.Serialization;

namespace Tubatools.MacOSInstaller.Core;

internal sealed class MacTarget
{
    public required string Darwin { get; init; }
    public required string DisplayName { get; init; }
    public required string ShortName { get; init; }
    public required string RecoveryBoardId { get; init; }
    public required string DefaultModel { get; init; }

    public static readonly MacTarget Sequoia = new()
    {
        Darwin = "24",
        DisplayName = "macOS 15 Sequoia",
        ShortName = "Sequoia",
        RecoveryBoardId = "Mac-CFF7D910A743CAAF",
        DefaultModel = "iMac20,1"
    };

    public static readonly MacTarget Tahoe = new()
    {
        Darwin = "25",
        DisplayName = "macOS 26 Tahoe",
        ShortName = "Tahoe",
        RecoveryBoardId = "Mac-27AD2F918AE68F61",
        DefaultModel = "MacPro7,1"
    };

    public static MacTarget FromDarwin(string? darwin) =>
        string.Equals(darwin, Tahoe.Darwin, StringComparison.Ordinal) ? Tahoe : Sequoia;
}

internal sealed class GpuInfo
{
    public string Name { get; set; } = "";
    public string Pnp { get; set; } = "";
    public long Ram { get; set; }
    public string Driver { get; set; } = "";
    public ushort VendorId { get; set; }
    public ushort DeviceId { get; set; }
}

internal sealed class NicInfo
{
    public string Name { get; set; } = "";
    public string Pnp { get; set; } = "";
    public string Mac { get; set; } = "";
    public string Type { get; set; } = "";

    public bool IsWireless =>
        Name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Wireless", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("WLAN", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("802.11", StringComparison.OrdinalIgnoreCase)
        || Type.Contains("Wireless", StringComparison.OrdinalIgnoreCase)
        || Type.Contains("802.11", StringComparison.OrdinalIgnoreCase);

    public bool IsEthernet =>
        !IsWireless
        && (Type.Contains("Ethernet", StringComparison.OrdinalIgnoreCase)
            || Type.Contains("802.3", StringComparison.OrdinalIgnoreCase)
            || Pnp.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase)
            || Pnp.StartsWith(@"USB\", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("Ethernet", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("Gigabit", StringComparison.OrdinalIgnoreCase)
            || Name.Contains("2.5G", StringComparison.OrdinalIgnoreCase));
}

internal sealed class StorageDiskInfo
{
    public string Model { get; set; } = "";
    public long Size { get; set; }
    public string Interface { get; set; } = "";
    public string Media { get; set; } = "";
    public string Pnp { get; set; } = "";
    public int Index { get; set; }
}

internal sealed class ControllerInfo
{
    public string Name { get; set; } = "";
    public string Pnp { get; set; } = "";
    public string Device { get; set; } = "";
}

internal sealed class HardwareProfile
{
    public string ComputerManufacturer { get; set; } = "";
    public string ComputerModel { get; set; } = "";
    public int PcSystemType { get; set; }
    public string SystemType { get; set; } = "";
    public ulong TotalMemory { get; set; }

    public string CpuName { get; set; } = "";
    public string CpuVendor { get; set; } = "";
    public int CpuCores { get; set; }
    public int CpuThreads { get; set; }

    public string BoardManufacturer { get; set; } = "";
    public string BoardProduct { get; set; } = "";
    public string BoardVersion { get; set; } = "";
    public string BiosVendor { get; set; } = "";
    public string BiosVersion { get; set; } = "";

    public bool HasBattery { get; set; }
    public bool IsLaptop => PcSystemType == 2 || HasBattery || SystemType.Contains("Mobile", StringComparison.OrdinalIgnoreCase);

    public List<GpuInfo> Gpus { get; set; } = [];
    public List<NicInfo> Nics { get; set; } = [];
    public List<string> SoundDevices { get; set; } = [];
    public List<StorageDiskInfo> Disks { get; set; } = [];
    public List<ControllerInfo> Controllers { get; set; } = [];

    public bool HasNvme =>
        Disks.Any(d => d.Interface.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
            || d.Model.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
            || d.Pnp.Contains("NVME", StringComparison.OrdinalIgnoreCase))
        || Controllers.Any(c => c.Name.Contains("NVM Express", StringComparison.OrdinalIgnoreCase)
            || c.Name.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
            || c.Pnp.Contains("NVME", StringComparison.OrdinalIgnoreCase));

    public bool HasIntelEthernet => Nics.Any(n => n.IsEthernet
        && n.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase));

    public bool HasRealtekEthernet => Nics.Any(n => n.IsEthernet
        && (n.Name.Contains("Realtek", StringComparison.OrdinalIgnoreCase)
            || n.Name.Contains("RTL", StringComparison.OrdinalIgnoreCase)));

    public bool HasWirelessNic => Nics.Any(n => n.IsWireless);

    public bool HasNvidiaGpu => Gpus.Any(g => g.VendorId == 0x10DE);
    public bool HasAmdGpu => Gpus.Any(g => g.VendorId == 0x1002);

    public string FirstMacAddress => Nics.FirstOrDefault(n => n.IsEthernet && !string.IsNullOrWhiteSpace(n.Mac))?.Mac
        ?? Nics.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n.Mac))?.Mac
        ?? "";
}

internal sealed class CompatibilityReport
{
    public bool CanBuild { get; set; } = true;
    public bool IsLaptop { get; set; }
    public string Model { get; set; } = "MacPro7,1";
    public string BoardId { get; set; } = "Mac-27AD2F918AE68F61";
    public string CpuClass { get; set; } = "";
    public bool UseIgpu { get; set; }
    public bool SupportedIntelIgpu { get; set; }
    public bool HasSupportedDgpu { get; set; }
    public bool DisabledNvidia { get; set; }
    public bool BasicDisplayOnly { get; set; }
    public ushort IgpuDeviceId { get; set; }
    public string IgpuPlatformIdBase64 { get; set; } = "";
    public string IgpuDeviceIdBase64 { get; set; } = "";
    public List<string> Kexts { get; set; } = [];
    public List<string> AcpiFiles { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> Notes { get; set; } = [];
    public int AudioLayout { get; set; } = 1;
    public string BootArgs { get; set; } = "-v keepsyms=1 debug=0x100";
}

internal sealed class BuildOptions
{
    public int AudioLayout { get; set; } = 1;
    public bool ForceUnsupported { get; set; }
    public bool VerboseBoot { get; set; } = true;
}

internal sealed class BuildOutcome
{
    public bool Success { get; set; }
    public string EfiDirectory { get; set; } = "";
    public string ConfigPath { get; set; } = "";
    public string Model { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Mlb { get; set; } = "";
    public string MacOSName { get; set; } = "";
    public List<string> Kexts { get; set; } = [];
    public List<string> AcpiFiles { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> ValidationErrors { get; set; } = [];
    public string RawValidation { get; set; } = "";
    public string LogPath { get; set; } = "";
}

internal sealed class UsbDisk
{
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public string Serial { get; set; } = "";
    public string UniqueId { get; set; } = "";
    public ulong Size { get; set; }
    public bool IsBoot { get; set; }
    public bool IsSystem { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsOffline { get; set; }
    public string PartitionStyle { get; set; } = "";

    public string SizeText => Size >= 1UL << 30
        ? $"{Size / (1UL << 30)} GB"
        : $"{Size / (1UL << 20)} MB";

    public string Display => $"磁盘 {Number}: {Name} · {SizeText}"
        + (IsBoot || IsSystem ? " · 系统盘" : "")
        + (IsReadOnly ? " · 只读" : "");
}

internal sealed class RecoveryDownloadResult
{
    public bool Success { get; set; }
    public string Directory { get; set; } = "";
    public string DmgPath { get; set; } = "";
    public string ChunklistPath { get; set; } = "";
    public long Size { get; set; }
    public List<string> VerifiedChunks { get; set; } = [];
    public string Error { get; set; } = "";
}

internal sealed class DiagnosticFinding
{
    public string Title { get; set; } = "";
    public string Cause { get; set; } = "";
    public List<string> Steps { get; set; } = [];
    public string Source { get; set; } = "";
}

internal sealed class DiagnosticResult
{
    public string LogPath { get; set; } = "";
    public string Cpu { get; set; } = "";
    public List<DiagnosticFinding> Findings { get; set; } = [];
    public List<string> Notes { get; set; } = [];
}

internal sealed class RawScan
{
    public RawComputer? Computer { get; set; }
    public RawCpu? Cpu { get; set; }
    public RawBoard? Board { get; set; }
    public RawBios? Bios { get; set; }
    public RawGpu[]? Gpus { get; set; }
    public RawNic[]? Nets { get; set; }
    public RawSound[]? Sound { get; set; }
    public RawDisk[]? Disks { get; set; }
    public RawController[]? Scsi { get; set; }
    public RawController[]? Usb { get; set; }
    public string? Battery { get; set; }
}

internal sealed class RawComputer
{
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public uint? PCSystemType { get; set; }
    public string? SystemType { get; set; }
    public ulong? TotalPhysicalMemory { get; set; }
}

internal sealed class RawCpu
{
    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public ushort? NumberOfCores { get; set; }
    public ushort? NumberOfLogicalProcessors { get; set; }
}

internal sealed class RawBoard
{
    public string? Manufacturer { get; set; }
    public string? Product { get; set; }
    public string? Version { get; set; }
}

internal sealed class RawBios
{
    public string? Manufacturer { get; set; }
    public string? SMBIOSBIOSVersion { get; set; }
}

internal sealed class RawGpu
{
    public string? Name { get; set; }
    public string? Pnp { get; set; }
    public long? Ram { get; set; }
    public string? Driver { get; set; }
}

internal sealed class RawNic
{
    public string? Name { get; set; }
    public string? Pnp { get; set; }
    public string? Mac { get; set; }
    public string? Type { get; set; }
}

internal sealed class RawSound
{
    public string? Name { get; set; }
    public string? Pnp { get; set; }
}

internal sealed class RawDisk
{
    public string? Model { get; set; }
    public long? Size { get; set; }
    public string? Interface { get; set; }
    public string? Media { get; set; }
    public string? Pnp { get; set; }
    public int? Index { get; set; }
}

internal sealed class RawController
{
    public string? Name { get; set; }
    public string? Pnp { get; set; }
    public string? Device { get; set; }
}
