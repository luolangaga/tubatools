using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tubatools.MacOSInstaller.Core;

internal static class HardwareScanner
{
    private static readonly Regex PciId = new(@"VEN_([0-9A-Fa-f]{4})&DEV_([0-9A-Fa-f]{4})", RegexOptions.Compiled);

    private const string Script = """
        $ErrorActionPreference = 'SilentlyContinue'
        [Console]::OutputEncoding = [Text.Encoding]::UTF8
        $cs = Get-CimInstance Win32_ComputerSystem | Select-Object -First 1 Manufacturer,Model,PCSystemType,SystemType,TotalPhysicalMemory
        $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1 Name,Manufacturer,NumberOfCores,NumberOfLogicalProcessors
        $board = Get-CimInstance Win32_BaseBoard | Select-Object -First 1 Manufacturer,Product,Version
        $bios = Get-CimInstance Win32_BIOS | Select-Object -First 1 Manufacturer,SMBIOSBIOSVersion
        $gpus = @(Get-CimInstance Win32_VideoController | ForEach-Object {
            [pscustomobject]@{ Name = $_.Name; Pnp = $_.PNPDeviceID; Ram = [int64]$_.AdapterRAM; Driver = $_.DriverVersion }
        })
        $nets = @(Get-CimInstance Win32_NetworkAdapter | Where-Object { $_.PhysicalAdapter -eq $true } | ForEach-Object {
            [pscustomobject]@{ Name = $_.Name; Pnp = $_.PNPDeviceID; Mac = $_.MACAddress; Type = $_.AdapterType }
        })
        $sound = @(Get-CimInstance Win32_SoundDevice | ForEach-Object {
            [pscustomobject]@{ Name = $_.Name; Pnp = $_.PNPDeviceID }
        })
        $disks = @(Get-CimInstance Win32_DiskDrive | ForEach-Object {
            [pscustomobject]@{ Model = $_.Model; Size = [int64]$_.Size; Interface = $_.InterfaceType; Media = $_.MediaType; Pnp = $_.PNPDeviceID; Index = [int]$_.Index }
        })
        $scsi = @(Get-CimInstance Win32_SCSIController | ForEach-Object {
            [pscustomobject]@{ Name = $_.Name; Pnp = $_.PNPDeviceID; Device = $_.DeviceID }
        })
        $usb = @(Get-CimInstance Win32_USBController | ForEach-Object {
            [pscustomobject]@{ Name = $_.Name; Pnp = $_.PNPDeviceID; Device = $_.DeviceID }
        })
        $battery = @(Get-CimInstance Win32_Battery | Select-Object -First 1 -ExpandProperty Name)
        [pscustomobject]@{
            Computer = $cs; Cpu = $cpu; Board = $board; Bios = $bios; Gpus = $gpus; Nets = $nets;
            Sound = $sound; Disks = $disks; Scsi = $scsi; Usb = $usb; Battery = ($battery -join '')
        } | ConvertTo-Json -Depth 6 -Compress
        """;

    public static async Task<HardwareProfile> ScanAsync(
        ProcessRunner runner,
        Action<string> log,
        CancellationToken ct)
    {
        var result = await runner.RunPowerShellScriptAsync(Script, "hardware-scan", requireAdmin: false, killOnCancel: true, log, ct);
        if (result.Canceled) throw new OperationCanceledException(ct);
        if (!result.Success)
            throw new InvalidOperationException("硬件扫描失败：" + FirstError(result));

        string json = ExtractJson(result.Output);
        RawScan? raw = JsonSerializer.Deserialize<RawScan>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        if (raw is null) throw new InvalidOperationException("硬件扫描没有返回可解析的数据。");
        return Map(raw);
    }

    internal static string ExtractJson(string text)
    {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start) return text[start..(end + 1)];
        start = text.IndexOf('[');
        end = text.LastIndexOf(']');
        if (start >= 0 && end > start) return text[start..(end + 1)];
        throw new InvalidOperationException("没有找到 JSON 输出。");
    }

    private static HardwareProfile Map(RawScan raw)
    {
        var p = new HardwareProfile
        {
            ComputerManufacturer = raw.Computer?.Manufacturer?.Trim() ?? "",
            ComputerModel = raw.Computer?.Model?.Trim() ?? "",
            PcSystemType = (int)(raw.Computer?.PCSystemType ?? 0),
            SystemType = raw.Computer?.SystemType?.Trim() ?? "",
            TotalMemory = raw.Computer?.TotalPhysicalMemory ?? 0,
            CpuName = raw.Cpu?.Name?.Trim() ?? "",
            CpuVendor = raw.Cpu?.Manufacturer?.Trim() ?? "",
            CpuCores = raw.Cpu?.NumberOfCores ?? 0,
            CpuThreads = raw.Cpu?.NumberOfLogicalProcessors ?? 0,
            BoardManufacturer = raw.Board?.Manufacturer?.Trim() ?? "",
            BoardProduct = raw.Board?.Product?.Trim() ?? "",
            BoardVersion = raw.Board?.Version?.Trim() ?? "",
            BiosVendor = raw.Bios?.Manufacturer?.Trim() ?? "",
            BiosVersion = raw.Bios?.SMBIOSBIOSVersion?.Trim() ?? "",
            HasBattery = !string.IsNullOrWhiteSpace(raw.Battery)
        };

        foreach (RawGpu g in raw.Gpus ?? [])
        {
            string name = g.Name?.Trim() ?? "";
            string pnp = g.Pnp?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name)) continue;

            ushort vendor = 0, device = 0;
            Match m = PciId.Match(pnp);
            if (m.Success)
            {
                vendor = ushort.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber);
                device = ushort.Parse(m.Groups[2].Value, System.Globalization.NumberStyles.HexNumber);
            }

            // 有真实 PCI ID 的设备优先保留；只有没有 PCI ID 的软件渲染/远程桌面适配器才丢弃。
            bool virtualByName = name.Contains("Idd", StringComparison.OrdinalIgnoreCase)
                || name.Contains("GameViewer", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Indirect", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Virtual Desktop", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Parsec", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Sunshine", StringComparison.OrdinalIgnoreCase)
                || name.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase)
                || name.Contains("VMware", StringComparison.OrdinalIgnoreCase);
            bool virtualPnp = pnp.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase)
                || pnp.StartsWith(@"SWD\", StringComparison.OrdinalIgnoreCase)
                || pnp.StartsWith(@"INDIRECTDSP\", StringComparison.OrdinalIgnoreCase);
            if (virtualByName || (virtualPnp && vendor == 0))
                continue;

            p.Gpus.Add(new GpuInfo
            {
                Name = name,
                Pnp = pnp,
                Ram = g.Ram ?? 0,
                Driver = g.Driver?.Trim() ?? "",
                VendorId = vendor,
                DeviceId = device
            });
        }

        foreach (RawNic n in raw.Nets ?? [])
        {
            string name = n.Name?.Trim() ?? "";
            string pnp = n.Pnp?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name)) continue;

            bool virtualPnp = pnp.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase)
                || pnp.StartsWith(@"SWD\", StringComparison.OrdinalIgnoreCase)
                || pnp.StartsWith(@"BTH\", StringComparison.OrdinalIgnoreCase)
                || pnp.StartsWith(@"VMBUS\", StringComparison.OrdinalIgnoreCase)
                || pnp.StartsWith(@"VIRTUAL\", StringComparison.OrdinalIgnoreCase);
            bool virtualName = name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)
                || name.Contains("WAN Miniport", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase)
                || name.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
                || name.Contains("VMware", StringComparison.OrdinalIgnoreCase)
                || name.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase)
                || name.Contains("TAP-", StringComparison.OrdinalIgnoreCase)
                || name.Contains("TUN", StringComparison.OrdinalIgnoreCase);
            if (virtualPnp || virtualName)
                continue;

            p.Nics.Add(new NicInfo
            {
                Name = name,
                Pnp = pnp,
                Mac = (n.Mac ?? "").Replace("-", ":").Replace(".", ":").Trim(),
                Type = n.Type?.Trim() ?? ""
            });
        }

        foreach (RawSound s in raw.Sound ?? [])
        {
            if (!string.IsNullOrWhiteSpace(s.Name)) p.SoundDevices.Add(s.Name.Trim());
        }

        foreach (RawDisk d in raw.Disks ?? [])
        {
            p.Disks.Add(new StorageDiskInfo
            {
                Model = d.Model?.Trim() ?? "",
                Size = d.Size ?? 0,
                Interface = d.Interface?.Trim() ?? "",
                Media = d.Media?.Trim() ?? "",
                Pnp = d.Pnp?.Trim() ?? "",
                Index = d.Index ?? 0
            });
        }

        foreach (RawController c in raw.Scsi ?? [])
        {
            p.Controllers.Add(new ControllerInfo
            {
                Name = c.Name?.Trim() ?? "",
                Pnp = c.Pnp?.Trim() ?? "",
                Device = c.Device?.Trim() ?? ""
            });
        }

        return p;
    }

    internal static string FirstError(RunResult r)
    {
        string text = (r.StdErr + "\n" + r.Output).Trim();
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? $"退出代码 {r.ExitCode}" : lines[^1];
    }
}
