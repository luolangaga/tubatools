using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace TubaWinUi3.Services;

public sealed class PartitionInfo
{
    public required string DriveLetter { get; init; }
    public double TotalGb { get; init; }
    public double AvailableGb { get; init; }
    public double UsedGb { get; init; }
    public double UsagePercent { get; init; }
    public required string Filesystem { get; init; }
}

public sealed class DiskHealthInfo
{
    public uint Index { get; init; }
    public required string Model { get; init; }
    public required string MediaType { get; init; }
    public double SizeGb { get; init; }
    public required string InterfaceType { get; init; }
    public required string HealthStatus { get; init; }
    public required string OperationalStatus { get; init; }
    public int? TemperatureC { get; init; }
    public double? WearPercentage { get; init; }
    public ulong? PowerOnHours { get; init; }
    public ulong? PowerOnCount { get; init; }
    public ulong? DataReadBytes { get; init; }
    public ulong? DataWrittenBytes { get; init; }
    public required string Status { get; init; }
    public int PartitionCount { get; init; }
    public required string SerialNumber { get; init; }
    public required string PartitionStyle { get; init; }
    public bool IsBootDisk { get; init; }
    public IReadOnlyList<PartitionInfo> Partitions { get; init; } = [];
    public double TotalUsageGb { get; init; }
    public double TotalCapacityGb { get; init; }
    public int? HealthPercent { get; init; }
    public bool IsSsd { get; init; }
    public bool IsNvme { get; init; }
    public bool HasSmart { get; init; }
    /// <summary>该项读取失败时的错误信息（null = 读取成功），页面据此在卡片上标记而不是整页失败。</summary>
    public string? Error { get; init; }

    /// <summary>该项是否读取失败（Error 非空）。</summary>
    public bool HasError => Error is not null;
}

public sealed class DiskHealthResponse
{
    public IReadOnlyList<DiskHealthInfo> Disks { get; init; } = [];
    public int TotalCount { get; init; }
    public int HealthyCount { get; init; }
    public int WarningCount { get; init; }
    public int UnhealthyCount { get; init; }
}

public sealed class DiskOptimizeResult
{
    public required string DriveLetter { get; init; }
    /// <summary>retrim=TRIM/优化(固态盘), auto=由系统自动选择。</summary>
    public required string Operation { get; init; }
    public bool IsSsd { get; init; }
    /// <summary>true = 已在后台低优先级启动、立即返回（机械盘整理可能耗时数小时）。</summary>
    public bool Background { get; init; }
    public bool Success { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// 磁盘健康：WMI 静态磁盘信息 + CrystalDiskInfo 方案 SMART 直读（DiskSmartReader，
/// 对应 NexBox smart.rs）+ 分区容量映射；SSD 走 TRIM/优化（前台），机械盘走碎片整理（后台低优先级）。
/// </summary>
public static class DiskHealthService
{
    /// <summary>合理硬盘容量上限（64 TB），超过视为异常（防止 WMI Size 虚高值展示）。</summary>
    private const double MaxReasonableDiskGb = 65536.0;

    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint OpenExisting = 0x3;
    private const uint FileAttributeNormal = 0x80;
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;

    private static readonly Encoding Gbk = CreateGbk();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize, byte[] lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static Encoding CreateGbk()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("GBK");
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    public static Task<DiskHealthResponse> GetHealthAsync() => Task.Run(GetHealth);

    private static DiskHealthResponse GetHealth()
    {
        // 1. 静态磁盘信息（WMI）。整表查询失败视为无磁盘，避免整页异常
        List<ManagementBaseObject> rows;
        try
        {
            rows = QueryRows(
                "SELECT Index, Model, Size, InterfaceType, SerialNumber, MediaType, Status, PNPDeviceID FROM Win32_DiskDrive");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DiskHealth] Win32_DiskDrive 查询失败: {ex.Message}");
            return new DiskHealthResponse();
        }
        if (rows.Count == 0)
            return new DiskHealthResponse();

        // 2/3. 分区表与卷映射是辅助信息，单项失败只降级为空映射，不影响健康度判定
        var partMeta = ReadPartitionMeta();
        var volumeMap = ReadVolumeMap();

        // 4. 每块盘：SMART 直读健康判定；单块盘失败只标记该盘，不拖垮整页
        var disks = new List<DiskHealthInfo>();
        var healthy = 0;
        var warning = 0;
        var unhealthy = 0;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            try
            {
                var disk = BuildDiskHealth(row, (uint)i, partMeta, volumeMap);
                switch (disk.HealthStatus)
                {
                    case "healthy": healthy++; break;
                    case "warning": warning++; break;
                    case "unhealthy": unhealthy++; break;
                }
                disks.Add(disk);
            }
            catch (Exception ex)
            {
                disks.Add(BuildDegradedDisk(row, ex));
            }
        }

        return new DiskHealthResponse
        {
            Disks = disks,
            TotalCount = rows.Count,
            HealthyCount = healthy,
            WarningCount = warning,
            UnhealthyCount = unhealthy,
        };
    }

    /// <summary>分区表信息（Win32_DiskPartition：GPT/MBR 与引导盘判定）；查询失败返回空映射。</summary>
    private static Dictionary<uint, (bool IsGpt, bool IsBoot)> ReadPartitionMeta()
    {
        var partMeta = new Dictionary<uint, (bool IsGpt, bool IsBoot)>();
        try
        {
            foreach (var row in QueryRows("SELECT DiskIndex, Type, BootPartition FROM Win32_DiskPartition"))
            {
                var idx = GetUInt(row, "DiskIndex");
                if (idx is null)
                    continue;
                var isGpt = GetStr(row, "Type").ToUpperInvariant().Contains("GPT");
                var isBoot = GetStr(row, "BootPartition").Equals("true", StringComparison.OrdinalIgnoreCase);
                (bool IsGpt, bool IsBoot) meta = partMeta.TryGetValue(idx.Value, out var existing)
                    ? existing : (false, false);
                partMeta[idx.Value] = (meta.IsGpt || isGpt, meta.IsBoot || isBoot);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DiskHealth] Win32_DiskPartition 查询失败: {ex.Message}");
        }
        return partMeta;
    }

    /// <summary>卷 → 物理盘映射；枚举失败返回空映射（卡片不显示分区，健康度不受影响）。</summary>
    private static Dictionary<uint, List<PartitionInfo>> ReadVolumeMap()
    {
        try
        {
            return EnumerateVolumesByDisk();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DiskHealth] 卷映射失败: {ex.Message}");
            return new Dictionary<uint, List<PartitionInfo>>();
        }
    }

    /// <summary>单块盘：判定 NVMe/SSD → SMART 直读 → 分区容量装配。</summary>
    private static DiskHealthInfo BuildDiskHealth(
        ManagementBaseObject row, uint fallbackIndex,
        Dictionary<uint, (bool IsGpt, bool IsBoot)> partMeta,
        Dictionary<uint, List<PartitionInfo>> volumeMap)
    {
        var diskIndex = GetUInt(row, "Index") ?? fallbackIndex;
        var model = GetStr(row, "Model", "未知");
        var mediaType = GetStr(row, "MediaType");
        var wmiSizeGb = (GetULong(row, "Size") ?? 0) / 1_073_741_824.0;
        var interfaceType = GetStr(row, "InterfaceType", "未知");
        var serial = GetStr(row, "SerialNumber");
        var status = GetStr(row, "Status", "未知");
        var pnp = GetStr(row, "PNPDeviceID");

        // 判定 NVMe / SSD
        var isNvme = pnp.ToLowerInvariant().Contains("nvme")
            || interfaceType.ToLowerInvariant().Contains("nvme")
            || model.ToLowerInvariant().Contains("nvme");
        var isSsd = isNvme
            || mediaType.ToLowerInvariant().Contains("ssd")
            || mediaType.ToLowerInvariant().Contains("solid state");

        // 直读 SMART（CrystalDiskInfo 方案：ATA IOCTL 失败时回退 WMI）
        var smart = DiskSmartReader.ReadDiskSmart(diskIndex, isNvme, isSsd, model, pnp);

        var healthStatus = smart.Status switch
        {
            DiskStatus.Good => "healthy",
            DiskStatus.Caution => "warning",
            DiskStatus.Bad => "unhealthy",
            _ => "unknown",
        };
        var operationalStatus = smart.Status switch
        {
            DiskStatus.Good => "OK",
            DiskStatus.Caution => "Degraded",
            DiskStatus.Bad => "Failure",
            _ => "Unknown",
        };

        var partitions = volumeMap.TryGetValue(diskIndex, out var list) ? list : [];
        var totalCapacityGb = partitions.Sum(p => p.TotalGb);
        var totalUsageGb = partitions.Sum(p => p.UsedGb);
        // 容量优先取分区容量之和（与资源管理器一致），回退 WMI Size 并做上限钳制
        var sizeGb = totalCapacityGb > 0
            ? totalCapacityGb
            : wmiSizeGb > 0 && wmiSizeGb <= MaxReasonableDiskGb ? wmiSizeGb : 0.0;

        var (isGpt, isBoot) = partMeta.TryGetValue(diskIndex, out var meta) ? meta : (false, false);
        var partitionStyle = isGpt ? "GPT" : partitions.Count > 0 ? "MBR" : "Unknown";

        return new DiskHealthInfo
        {
            Index = diskIndex,
            Model = model,
            MediaType = mediaType.Length == 0 ? "Unknown" : mediaType,
            SizeGb = sizeGb,
            InterfaceType = interfaceType,
            HealthStatus = healthStatus,
            OperationalStatus = operationalStatus,
            TemperatureC = smart.TemperatureC,
            WearPercentage = smart.LifePercent,
            PowerOnHours = smart.PowerOnHours,
            PowerOnCount = smart.PowerOnCount,
            DataReadBytes = smart.DataReadBytes,
            DataWrittenBytes = smart.DataWrittenBytes,
            Status = status,
            PartitionCount = partitions.Count,
            SerialNumber = serial,
            PartitionStyle = partitionStyle,
            IsBootDisk = isBoot,
            Partitions = partitions,
            TotalUsageGb = totalUsageGb,
            TotalCapacityGb = totalCapacityGb,
            HealthPercent = smart.LifePercent,
            IsSsd = smart.IsNvme || isSsd,
            IsNvme = smart.IsNvme,
            HasSmart = smart.HasSmart,
        };
    }

    /// <summary>单块盘读取失败时构造「降级卡片」：保留 WMI 可读的基本信息，错误信息随卡片展示。</summary>
    private static DiskHealthInfo BuildDegradedDisk(ManagementBaseObject row, Exception ex)
    {
        var index = GetUInt(row, "Index") ?? 0;
        var model = GetStr(row, "Model", "未知");
        var mediaType = GetStr(row, "MediaType");
        var wmiSizeGb = (GetULong(row, "Size") ?? 0) / 1_073_741_824.0;
        var interfaceType = GetStr(row, "InterfaceType", "未知");
        var serial = GetStr(row, "SerialNumber");
        var status = GetStr(row, "Status", "未知");
        Debug.WriteLine($"[DiskHealth] PhysicalDrive{index} ({model}) 读取失败: {ex}");
        return new DiskHealthInfo
        {
            Index = index,
            Model = model,
            MediaType = mediaType.Length == 0 ? "Unknown" : mediaType,
            SizeGb = wmiSizeGb > 0 && wmiSizeGb <= MaxReasonableDiskGb ? wmiSizeGb : 0.0,
            InterfaceType = interfaceType,
            HealthStatus = "unknown",
            OperationalStatus = "Unknown",
            Status = status,
            PartitionCount = 0,
            SerialNumber = serial,
            PartitionStyle = "Unknown",
            IsBootDisk = false,
            IsSsd = false,
            IsNvme = false,
            HasSmart = false,
            Error = ex.Message,
        };
    }

    private static List<ManagementBaseObject> QueryRows(string query)
    {
        var result = new List<ManagementBaseObject>();
        using var searcher = new ManagementObjectSearcher(query);
        using var rows = searcher.Get();
        foreach (ManagementBaseObject row in rows)
            result.Add(row);
        return result;
    }

    private static uint? GetUInt(ManagementBaseObject row, string prop) => row[prop] switch
    {
        null => null,
        uint v => v,
        ushort v => v,
        int v => (uint)v,
        ulong v => (uint)v,
        _ => null,
    };

    private static ulong? GetULong(ManagementBaseObject row, string prop) => row[prop] switch
    {
        null => null,
        ulong v => v,
        uint v => v,
        int v => v < 0 ? null : (ulong)v,
        _ => null,
    };

    private static string GetStr(ManagementBaseObject row, string prop, string fallback = "")
        => row[prop]?.ToString() ?? fallback;

    // ─────────────────────────── 卷 → 物理盘映射 ───────────────────────────

    /// <summary>枚举固定盘符（C:、D:…）并映射到物理盘号，收集分区容量/文件系统信息。</summary>
    private static Dictionary<uint, List<PartitionInfo>> EnumerateVolumesByDisk()
    {
        var map = new Dictionary<uint, List<PartitionInfo>>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed)
                continue;
            var name = drive.Name.TrimEnd('\\', ':');
            if (name.Length != 1 || !char.IsAsciiLetter(name[0]))
                continue;
            var letter = name[0].ToString();
            var diskNumber = GetDiskNumberForVolume(letter);
            if (diskNumber is null)
                continue;

            var total = (ulong)Math.Max(drive.TotalSize, 0);
            var free = (ulong)Math.Max(drive.AvailableFreeSpace, 0);
            var used = total > free ? total - free : 0;
            var usagePct = total > 0 ? used * 100.0 / total : 0.0;

            var partition = new PartitionInfo
            {
                DriveLetter = letter,
                TotalGb = total / 1_073_741_824.0,
                AvailableGb = free / 1_073_741_824.0,
                UsedGb = used / 1_073_741_824.0,
                UsagePercent = usagePct,
                Filesystem = drive.DriveFormat ?? "",
            };
            if (!map.TryGetValue(diskNumber.Value, out var list))
                map[diskNumber.Value] = list = new List<PartitionInfo>();
            list.Add(partition);
        }
        return map;
    }

    /// <summary>通过 IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS 查询卷所属的物理盘号。</summary>
    private static uint? GetDiskNumberForVolume(string letter)
    {
        var h = CreateFileW($"\\\\.\\{letter}:", GenericRead, FileShareRead | FileShareWrite,
            IntPtr.Zero, OpenExisting, FileAttributeNormal, IntPtr.Zero);
        if (h == new IntPtr(-1))
            return null;
        try
        {
            // VOLUME_DISK_EXTENTS: DWORD NumberOfDiskExtents + DISK_EXTENT Extents[1]
            // DISK_EXTENT: DWORD DiskNumber; LARGE_INTEGER StartingOffset (8 字节对齐 → 偏移 8);
            //              LARGE_INTEGER ExtentLength。注意 DiskNumber 在偏移 8（偏移 4 是对齐填充）！
            var output = new byte[32];
            if (!DeviceIoControl(h, IoctlVolumeGetVolumeDiskExtents, IntPtr.Zero, 0,
                    output, (uint)output.Length, out _, IntPtr.Zero))
                return null;
            return ParseVolumeDiskNumber(output);
        }
        finally
        {
            CloseHandle(h);
        }
    }

    /// <summary>
    /// 从 VOLUME_DISK_EXTENTS 原始缓冲解析首个盘的物理盘号。
    /// 布局（ntdddisk.h）: DWORD NumberOfDiskExtents(0) + DISK_EXTENT{ DWORD DiskNumber(8,
    /// 前 4 字节为 LARGE_INTEGER 对齐填充), LARGE_INTEGER StartingOffset(8), LARGE_INTEGER ExtentLength(16) }。
    /// </summary>
    internal static uint? ParseVolumeDiskNumber(byte[] output)
    {
        if (output.Length < 32)
            return null;
        var count = BitConverter.ToUInt32(output, 0);
        if (count == 0)
            return null;
        return BitConverter.ToUInt32(output, 8);
    }

    // ─────────────────────────── 磁盘优化（TRIM / 碎片整理） ───────────────────────────

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static async Task<DiskOptimizeResult> OptimizeAsync(
        string driveLetter, uint index, string interfaceType, string model)
    {
        if (!IsAdministrator())
            throw new InvalidOperationException("此操作需要管理员权限，请以管理员身份运行图吧工具箱后重试");

        var letter = driveLetter.Trim().TrimEnd(':');
        if (letter.Length == 0)
            throw new InvalidOperationException("无效的盘符");

        // 通过 SMART 直读判定介质（不信任 MediaType 字符串，NVMe 盘的 WMI MediaType 常为空）
        var isSsd = DetectSsd(index, interfaceType, model);
        var operation = isSsd ? "retrim" : "auto";
        var background = operation != "retrim";

        var message = await Task.Run(() =>
            background ? RunDefragBackground(letter, operation) : RunDefragSync(letter, operation))
            .ConfigureAwait(false);

        return new DiskOptimizeResult
        {
            DriveLetter = letter,
            Operation = operation,
            IsSsd = isSsd,
            Background = background,
            Success = true,
            Message = message,
        };
    }

    /// <summary>SSD 判定：接口/型号含 NVMe 或 SMART 直读确认 NVMe → 固态；否则交给系统自动选择。</summary>
    private static bool DetectSsd(uint index, string interfaceType, string model)
    {
        var it = interfaceType.ToLowerInvariant();
        if (it.Contains("nvme") || model.ToLowerInvariant().Contains("nvme"))
            return true;
        var smart = DiskSmartReader.ReadDiskSmart(index, true, false, model, "");
        return smart.IsNvme;
    }

    /// <summary>defrag.exe 参数：retrim → /L，defrag → /U，auto → /O（系统自动选择介质策略）。</summary>
    private static string DefragFlag(string operation) => operation switch
    {
        "retrim" => "/L",
        "defrag" => "/U",
        _ => "/O",
    };

    /// <summary>前台同步执行（TRIM 等快速操作），等待完成后返回输出末尾几行。</summary>
    private static string RunDefragSync(string letter, string operation)
    {
        var psi = new ProcessStartInfo("defrag.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add($"{letter}:");
        psi.ArgumentList.Add(DefragFlag(operation));

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("执行整理命令失败");
        // defrag 输出使用系统本地代码页（中文为 GBK），读原始字节后手动解码避免乱码
        var stdoutBytes = ReadToEndBytes(process.StandardOutput.BaseStream);
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            var stdout = Gbk.GetString(stdoutBytes);
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var tail = string.Join("\n", lines.TakeLast(4));
            return tail.Trim().Length == 0 ? "操作完成" : tail;
        }
        var error = stderr.Trim();
        throw new InvalidOperationException(error.Length == 0 ? Gbk.GetString(stdoutBytes).Trim() : error);
    }

    private static byte[] ReadToEndBytes(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>后台低优先级启动（碎片整理/自动，可能耗时数小时）：BELOW_NORMAL 优先级，立即返回。</summary>
    private static string RunDefragBackground(string letter, string operation)
    {
        var psi = new ProcessStartInfo("defrag.exe") { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add($"{letter}:");
        psi.ArgumentList.Add(DefragFlag(operation));

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("启动整理进程失败");
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch
        {
            // 设置优先级失败不影响后台整理
        }
        // 不等待 defrag 退出，让其继续在后台低优先级运行
        return "已在后台开始整理，可继续正常使用电脑";
    }
}