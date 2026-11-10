using System.Management;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services;

/// <summary>SMART 健康状态（对应 CrystalDiskInfo DISK_STATUS_*）。</summary>
public enum DiskStatus
{
    Unknown = 0,
    Good,
    Caution,
    Bad,
}

/// <summary>磁盘 SMART 直读结果。</summary>
public sealed class SmartInfo
{
    public DiskStatus Status { get; init; }
    /// <summary>健康度百分比 0-100（NVMe: 100-PercentageUsed；SSD: 寿命属性；HDD: 按状态映射）。</summary>
    public int? LifePercent { get; init; }
    public int? TemperatureC { get; init; }
    public ulong? PowerOnHours { get; init; }
    public ulong? PowerOnCount { get; init; }
    public ulong? DataReadBytes { get; init; }
    public ulong? DataWrittenBytes { get; init; }
    public bool IsNvme { get; init; }
    /// <summary>SMART 是否读取成功（可判定）。</summary>
    public bool HasSmart { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// 磁盘 SMART 健康度直读 — 移植自 CrystalDiskInfo (MIT) 的判定方案：
/// ATA 用 DeviceIoControl(DFP_RECEIVE_DRIVE_DATA) 读取属性/阈值，失败回退 WMI
/// MSStorageDriver_FailurePredictData；NVMe 用 IOCTL_STORAGE_QUERY_PROPERTY 读取
/// SMART/Health Information Log；判定逻辑移植 CDI CheckDiskStatus。
/// (对应 NexBox smart.rs，Rust → C#)
/// </summary>
public static class DiskSmartReader
{
    // ─── IOCTL / ATA 命令常量（CDI AtaSmart.h / ntdddisk.h）───
    private const uint DFP_RECEIVE_DRIVE_DATA = 0x0007C088;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    private const byte SmartCmd = 0xB0;
    private const byte ReadAttributes = 0xD0;
    private const byte ReadThresholds = 0xD1;
    private const byte SmartCylLow = 0x4F;
    private const byte SmartCylHi = 0xC2;
    private const byte AtaMaster = 0xA0;
    private const uint ReadAttributeBufferSize = 512;
    private const int MaxAttribute = 30;

    // ─── NVMe StorageQuery 常量（CDI StorageQuery.h）───
    private const uint StorageAdapterProtocolSpecificProperty = 49;
    private const uint PropertyStandardQuery = 0;
    private const uint ProtocolTypeNvme = 3;
    private const uint NvmeDataTypeLogPage = 2;
    private const uint NvmeLogPageSmartHealthInfo = 2;

    // ─── 判定阈值默认值（CDI HealthDlg 默认）───
    private const ushort Threshold05 = 1;
    private const ushort ThresholdC5 = 1;
    private const ushort ThresholdC6 = 1;
    private const ushort ThresholdFF = 10;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint OpenExisting = 0x3;
    private const uint FileAttributeNormal = 0x80;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode,
        byte[]? lpInBuffer, uint nInBufferSize, byte[] lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern uint GetLastError();

    // ─── 单个 SMART 属性（12 字节，CDI NVMeInterpreter.h L15-23）───

    internal readonly record struct SmartAttribute(
        byte Id, ushort StatusFlags, byte CurrentValue, byte WorstValue, byte[] RawValue)
    {
        /// <summary>小端 16 位原始值（对应 CDI B8toB16le）。</summary>
        public ushort Raw16 => (ushort)(RawValue[0] | (RawValue[1] << 8));
    }

    internal readonly record struct SmartThreshold(byte Id, byte ThresholdValue);

    // ─── 设备打开 ───

    private static IntPtr OpenPhysicalDrive(uint index)
    {
        var path = $"\\\\.\\PhysicalDrive{index}";
        // 先尝试读写权限（与 CDI 一致），失败时降级为只读
        var h = CreateFileW(path, GenericRead | GenericWrite, FileShareRead | FileShareWrite,
            IntPtr.Zero, OpenExisting, FileAttributeNormal, IntPtr.Zero);
        if (h == new IntPtr(-1))
            h = CreateFileW(path, GenericRead, FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, FileAttributeNormal, IntPtr.Zero);
        return h;
    }

    // ─── ATA SMART 读取（CDI GetSmartAttributePd / GetSmartThresholdPd）───

    /// <summary>构造 SENDCMDINPARAMS 输入参数（36 字节）：bFeaturesReg 为 0xD0 属性或 0xD1 阈值。</summary>
    private static byte[] BuildSendCmd(byte feature)
    {
        var buf = new byte[36];
        BitConverter.GetBytes(ReadAttributeBufferSize).CopyTo(buf, 0); // cBufferSize
        buf[4] = feature;                                    // bFeaturesReg
        buf[5] = 1;                                          // bSectorCountReg
        buf[6] = 1;                                          // bSectorNumberReg
        buf[7] = SmartCylLow;                                // bCylLowReg
        buf[8] = SmartCylHi;                                 // bCylHighReg
        buf[9] = AtaMaster;                                  // bDriveHeadReg
        buf[10] = SmartCmd;                                  // bCommandReg
        return buf;
    }

    /// <summary>读取 ATA SMART 数据或阈值（512 字节原始缓冲）。</summary>
    private static byte[] ReadAtaSmartRaw(uint index, byte feature)
    {
        var h = OpenPhysicalDrive(index);
        if (h == new IntPtr(-1))
            throw new InvalidOperationException($"打开 PhysicalDrive{index} 失败 (错误码 {GetLastError()})");
        try
        {
            var input = BuildSendCmd(feature);
            // 输出缓冲: SENDCMDOUTPARAMS(16B) + SMART 数据(512B)
            var output = new byte[16 + 512];
            var ok = DeviceIoControl(h, DFP_RECEIVE_DRIVE_DATA, input, (uint)input.Length,
                output, (uint)output.Length, out _, IntPtr.Zero);
            if (!ok)
                throw new InvalidOperationException($"DeviceIoControl SMART 读取失败 (错误码 {GetLastError()})");
            var raw = new byte[512];
            Array.Copy(output, 16, raw, 0, 512);
            return raw;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    // ─── NVMe SMART 读取（CDI GetSmartAttributeNVMeStorageQuery）───

    private static byte[] ReadNvmeSmartRaw(uint index)
    {
        var h = OpenPhysicalDrive(index);
        if (h == new IntPtr(-1))
            throw new InvalidOperationException($"打开 PhysicalDrive{index} 失败 (错误码 {GetLastError()})");
        try
        {
            // TStorageQueryWithBuffer：Query(8B) + ProtocolSpecific(40B) + Buffer(4096B)
            var buf = new byte[8 + 40 + 4096];
            BitConverter.GetBytes(StorageAdapterProtocolSpecificProperty).CopyTo(buf, 0);
            BitConverter.GetBytes(PropertyStandardQuery).CopyTo(buf, 4);
            BitConverter.GetBytes(ProtocolTypeNvme).CopyTo(buf, 8);
            BitConverter.GetBytes(NvmeDataTypeLogPage).CopyTo(buf, 12);
            BitConverter.GetBytes(NvmeLogPageSmartHealthInfo).CopyTo(buf, 16);
            BitConverter.GetBytes(0u).CopyTo(buf, 20);      // ProtocolDataRequestSubValue
            BitConverter.GetBytes(40u).CopyTo(buf, 24);     // ProtocolDataOffset
            BitConverter.GetBytes(4096u).CopyTo(buf, 28);   // ProtocolDataLength

            var ok = DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, buf, (uint)buf.Length,
                buf, (uint)buf.Length, out _, IntPtr.Zero);
            // 与 CDI 一致：首次失败后以 ProtocolDataRequestSubValue=0xFFFFFFFF 重试
            if (!ok)
            {
                BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(buf, 20);
                ok = DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, buf, (uint)buf.Length,
                    buf, (uint)buf.Length, out _, IntPtr.Zero);
            }
            if (!ok)
                throw new InvalidOperationException($"IOCTL_STORAGE_QUERY_PROPERTY 失败 (错误码 {GetLastError()})");
            // SMART 数据位于 Query(8) + ProtocolSpecific(40) 之后，即偏移 48
            var raw = new byte[512];
            Array.Copy(buf, 48, raw, 0, 512);
            return raw;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    // ─── 解析层（CDI FillSmartData）───

    /// <summary>从 512 字节 SMART 原始缓冲解析属性数组（前 2 字节是 revision，属性从偏移 2 开始）。</summary>
    internal static List<SmartAttribute> ParseAttributes(byte[] raw)
    {
        var attrs = new List<SmartAttribute>(MaxAttribute);
        for (var i = 0; i < MaxAttribute; i++)
        {
            var off = 2 + i * 12;
            if (off + 12 > raw.Length)
                break;
            var id = raw[off];
            if (id == 0)
                continue;
            var rawValue = new byte[6];
            Array.Copy(raw, off + 5, rawValue, 0, 6);
            attrs.Add(new SmartAttribute(id,
                (ushort)(raw[off + 1] | (raw[off + 2] << 8)),
                raw[off + 3], raw[off + 4], rawValue));
        }
        return attrs;
    }

    internal static List<SmartThreshold> ParseThresholds(byte[] raw)
    {
        var ths = new List<SmartThreshold>(MaxAttribute);
        for (var i = 0; i < MaxAttribute; i++)
        {
            var off = 2 + i * 12;
            if (off + 12 > raw.Length)
                break;
            var id = raw[off];
            if (id != 0)
                ths.Add(new SmartThreshold(id, raw[off + 1]));
        }
        return ths;
    }

    private static SmartAttribute? FindAttribute(IReadOnlyList<SmartAttribute> attrs, byte id)
        => attrs.FirstOrDefault(a => a.Id == id) is { } found ? found : null;

    private static byte? FindThreshold(IReadOnlyList<SmartThreshold> ths, byte id)
    {
        foreach (var t in ths)
            if (t.Id == id)
                return t.ThresholdValue;
        return null;
    }

    // ─── 判定层（移植 CDI CheckDiskStatus）───

    /// <summary>该属性 ID 是否属于关键属性范围（CDI L12622-12634）。</summary>
    internal static bool IsCriticalId(byte id) =>
        (id >= 0x01 && id <= 0x0D) || id == 0x16
        || (id >= 0xBB && id <= 0xBD) || (id >= 0xBF && id <= 0xC1)
        || (id >= 0xC3 && id <= 0xD1) || (id >= 0xD3 && id <= 0xD4)
        || (id >= 0xDC && id <= 0xE4) || (id >= 0xE6 && id <= 0xE7)
        || id == 0xF0 || id == 0xFA || id == 0xFE;

    /// <summary>是否为 SSD 寿命属性（CDI 各厂商分支汇总）。</summary>
    internal static bool IsLifeAttribute(byte id) =>
        id is 0xA9 or 0xAD or 0xB1 or 0xBB or 0xCA or 0xD1 or 0xE6 or 0xE7 or 0xE8 or 0xE9 or 0xC9;

    /// <summary>计算 SSD 寿命（百分比）：0xE6(WDC/SanDisk) 用 RawValue 特例，其余取 CurrentValue。</summary>
    internal static int ComputeLife(in SmartAttribute attr) => attr.Id switch
    {
        0xE6 => 100 - attr.RawValue[1],   // WDC / SanDisk: 100 - RawValue[1]
        0xE7 => 100 - attr.RawValue[0],   // SandForce 等增量式寿命
        _ => attr.CurrentValue,
    };

    /// <summary>普通盘（HDD/SSD）核心判定（CDI CheckDiskStatus L12568-12829）。</summary>
    internal static (DiskStatus Status, int? Life) CheckAtaStatus(
        IReadOnlyList<SmartAttribute> attrs, IReadOnlyList<SmartThreshold> ths,
        bool isSsd, bool isThresholdCorrect, ushort thresholdFf)
    {
        // 预检：机械盘必须拥有有效阈值才能判定
        if (!isSsd && !isThresholdCorrect)
            return (DiskStatus.Unknown, null);

        var error = 0;
        var caution = 0;
        var flagUnknown = true;
        int? life = null;

        for (var j = 0; j < attrs.Count; j++)
        {
            var attr = attrs[j];
            // 重复 ID 检测
            for (var k = 0; k < j; k++)
            {
                if (attrs[k].Id != 0 && attrs[j].Id == attrs[k].Id)
                    return (DiskStatus.Unknown, null);
            }

            var id = attr.Id;
            var threshold = FindThreshold(ths, id);

            // 温度属性(0xC2) 与 SSD RawValues8 不参与 error
            if (id != 0xC2)
            {
                var currentBelowThreshold = threshold is { } t && t != 0 && attr.CurrentValue < t;
                var inCriticalRange = IsCriticalId(id);
                if (isSsd)
                {
                    if (currentBelowThreshold)
                        error++;
                }
                else if (inCriticalRange && currentBelowThreshold)
                {
                    error++;
                }
            }

            if (isSsd && threshold is { } tt && tt != 0)
                flagUnknown = false;

            if (id is 0x05 or 0xC5 or 0xC6)
            {
                // 4 字节全 FF 视为不可用
                var rawAllFf = attr.RawValue[0] == 0xFF && attr.RawValue[1] == 0xFF
                    && attr.RawValue[2] == 0xFF && attr.RawValue[3] == 0xFF;
                if (!rawAllFf)
                {
                    var th = id switch { 0x05 => Threshold05, 0xC5 => ThresholdC5, _ => ThresholdC6 };
                    if (th > 0 && attr.Raw16 >= th && !isSsd)
                        caution = 1;
                }
                if (!isSsd)
                    flagUnknown = false;
            }
            else if (IsLifeAttribute(id))
            {
                flagUnknown = false;
                var lifeVal = ComputeLife(attr);
                var lifeClamped = Math.Clamp(lifeVal, 0, 100);
                if (lifeVal == 0)
                    error = 1;
                else if (lifeClamped <= thresholdFf)
                    caution = 1;
                life = lifeClamped;
            }
        }

        var status = error > 0 ? DiskStatus.Bad
            : flagUnknown ? DiskStatus.Unknown
            : caution > 0 ? DiskStatus.Caution
            : DiskStatus.Good;
        return (status, life);
    }

    /// <summary>NVMe 分支判定（CDI CheckDiskStatus L12529-12566）。</summary>
    internal static DiskStatus CheckNvmeStatus(
        IReadOnlyList<SmartAttribute> attrs, int life, string model, ushort thresholdFf)
    {
        // 排除虚拟机 NVMe
        if (model.StartsWith("Parallels", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("VMware", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("QEMU", StringComparison.OrdinalIgnoreCase))
            return DiskStatus.Unknown;

        // Critical Warning（NVMe Id=1）> 0 → BAD
        if (FindAttribute(attrs, 1) is { } criticalWarning && criticalWarning.RawValue[0] > 0)
            return DiskStatus.Bad;

        // Available Spare / Spare Threshold（NVMe Id=3 / Id=4）
        if (FindAttribute(attrs, 3) is { } spare && FindAttribute(attrs, 4) is { } spareThreshold)
        {
            var spareVal = spare.RawValue[0];
            var thresholdVal = spareThreshold.RawValue[0];
            if (thresholdVal != 0 && thresholdVal <= 100)
            {
                if (spareVal < thresholdVal)
                    return DiskStatus.Bad;
                if (spareVal == thresholdVal && thresholdVal != 100)
                    return DiskStatus.Caution;
            }
        }

        return life > thresholdFf ? DiskStatus.Good : DiskStatus.Caution;
    }

    // ─── 温度 / 通电时间解析 ───

    internal static int? ParseAtaTemperature(IReadOnlyList<SmartAttribute> attrs)
    {
        // 高字节可能记录最低或最高温度，当前温度取原始值的首字节。
        var current = FindAttribute(attrs, 0xC2)?.RawValue[0];
        if (current is > 0 and < 150) return current.Value;
        var airflow = FindAttribute(attrs, 0xBE)?.RawValue[0];
        return airflow is > 0 and < 150 ? airflow.Value : null;
    }

    private static int? ParseNvmeTemperature(byte[] raw)
    {
        var kelvin = raw[1] | (raw[2] << 8);
        var celsius = kelvin - 273;
        return celsius <= 0 || celsius > 200 ? null : celsius;
    }

    /// <summary>ATA 通电小时数（属性 0x09），仅取 RawValue 低 4 字节小端（高位常为厂商冗余）。</summary>
    private static ulong? ParseAtaPowerOnHours(IReadOnlyList<SmartAttribute> attrs)
    {
        if (FindAttribute(attrs, 0x09) is not { } attr)
            return null;
        return BitConverter.ToUInt64(new byte[] { attr.RawValue[0], attr.RawValue[1], attr.RawValue[2], attr.RawValue[3], 0, 0, 0, 0 });
    }

    private static ulong? ParseAtaPowerCycles(IReadOnlyList<SmartAttribute> attrs)
    {
        if (FindAttribute(attrs, 0x0C) is not { } attr)
            return null;
        return BitConverter.ToUInt64(new byte[] { attr.RawValue[0], attr.RawValue[1], attr.RawValue[2], attr.RawValue[3], 0, 0, 0, 0 });
    }

    private static ulong? ParseNvmePowerOnHours(byte[] raw)
    {
        var bytes = new byte[8];
        Array.Copy(raw, 128, bytes, 0, 8);
        return BitConverter.ToUInt64(bytes);
    }

    private static ulong? ParseNvmePowerCycles(byte[] raw)
    {
        var bytes = new byte[8];
        Array.Copy(raw, 112, bytes, 0, 8);
        return BitConverter.ToUInt64(bytes);
    }

    /// <summary>NVMe 累计数据单元数（偏移 32/48，8 字节小端，单位千个 512B 单元）。</summary>
    private static ulong? ParseNvmeDataBytes(byte[] raw, int offset)
    {
        if (offset + 8 > raw.Length)
            return null;
        var bytes = new byte[8];
        Array.Copy(raw, offset, bytes, 0, 8);
        return BitConverter.ToUInt64(bytes) * 512_000;
    }

    /// <summary>ATA 累计读写量（0xF1 写入 / 0xF2 读取），取低 40 位 × 512 字节。</summary>
    private static ulong? ParseAtaDataBytes(IReadOnlyList<SmartAttribute> attrs, byte id)
    {
        if (FindAttribute(attrs, id) is not { } attr)
            return null;
        var bytes = new byte[8];
        Array.Copy(attr.RawValue, 0, bytes, 0, 5);
        return BitConverter.ToUInt64(bytes) * 512;
    }

    // ─── WMI 后备路径（对应 CDI GetSmartAttributeWmi，无需管理员）───

    private static byte[]? ReadAtaSmartWmi(string pnpId, bool threshold)
    {
        var className = threshold ? "MSStorageDriver_FailurePredictThresholds" : "MSStorageDriver_FailurePredictData";
        var pnpUpper = pnpId.Trim().ToUpperInvariant();
        if (pnpUpper.Length == 0)
            return null;

        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", $"SELECT * FROM {className}");
            using var rows = searcher.Get();
            foreach (ManagementBaseObject row in rows)
            {
                var instance = (row["InstanceName"] as string ?? "").ToUpperInvariant();
                if (!instance.StartsWith(pnpUpper, StringComparison.Ordinal))
                    continue;
                if (row["VendorSpecific"] is ushort[] values && values.Length >= 512)
                {
                    var raw = new byte[512];
                    for (var i = 0; i < 512; i++)
                        raw[i] = (byte)values[i];
                    return raw;
                }
            }
        }
        catch (ManagementException)
        {
            // 磁盘/系统不支持该 WMI 类（如未启用 SMART 的虚拟盘）：按无数据降级，不影响其他磁盘
            return null;
        }
        return null;
    }

    // ─── 对外统一入口 ───

    /// <summary>
    /// 读取一块物理磁盘的 SMART 健康信息。NVMe 优先 IOCTL，失败回退 ATA；
    /// ATA 的 DeviceIoControl 失败回退 WMI。失败时返回 HasSmart=false。
    /// </summary>
    public static SmartInfo ReadDiskSmart(uint index, bool isNvme, bool isSsd, string model, string pnpId)
    {
        // 1. NVMe 优先
        if (isNvme && TryReadNvmeSmart(index, model) is { } nvmeInfo)
            return nvmeInfo;

        // 2. ATA 路径：优先 DeviceIoControl，失败回退 WMI
        byte[]? attrsRaw = null, thsRaw = null;
        try { attrsRaw = ReadAtaSmartRaw(index, ReadAttributes); } catch (Exception) { }
        try { thsRaw = ReadAtaSmartRaw(index, ReadThresholds); } catch (Exception) { }
        if ((attrsRaw is null || thsRaw is null) && !string.IsNullOrWhiteSpace(pnpId))
        {
            attrsRaw ??= ReadAtaSmartWmi(pnpId, false);
            thsRaw ??= ReadAtaSmartWmi(pnpId, true);
        }

        if (attrsRaw is null)
        {
            // ATA/WMI 全部失败：最后尝试 NVMe 直读（部分 NVMe 盘的 WMI 字符串不含 NVMe 标记）
            if (!isNvme && TryReadNvmeSmart(index, model) is { } probedInfo)
                return probedInfo;
            return new SmartInfo
            {
                Status = DiskStatus.Unknown,
                HasSmart = false,
                Error = $"PhysicalDrive{index} SMART 读取失败（需管理员权限）",
            };
        }

        var attributes = ParseAttributes(attrsRaw);
        if (thsRaw is not null)
        {
            var thresholds = ParseThresholds(thsRaw);
            var isThresholdCorrect = thresholds.Any(t => t.ThresholdValue != 0);
            var (status2, life2) = CheckAtaStatus(attributes, thresholds, isSsd, isThresholdCorrect, ThresholdFF);
            // HDD 无寿命属性时按状态映射健康度百分比（GOOD=100 / CAUTION=50 / BAD=0）
            var lifePercent = life2 is { } l
                ? Math.Clamp(l, 0, 100)
                : isSsd ? null : status2 switch
                {
                    DiskStatus.Good => 100,
                    DiskStatus.Caution => 50,
                    DiskStatus.Bad => 0,
                    _ => (int?)null,
                };
            return new SmartInfo
            {
                Status = status2,
                LifePercent = lifePercent,
                TemperatureC = ParseAtaTemperature(attributes),
                PowerOnHours = ParseAtaPowerOnHours(attributes),
                PowerOnCount = ParseAtaPowerCycles(attributes),
                DataReadBytes = ParseAtaDataBytes(attributes, 0xF2),
                DataWrittenBytes = ParseAtaDataBytes(attributes, 0xF1),
                IsNvme = false,
                HasSmart = true,
            };
        }

        // 无阈值数据：仅能解析温度/通电，健康判定为 UNKNOWN
        return new SmartInfo
        {
            Status = DiskStatus.Unknown,
            LifePercent = null,
            TemperatureC = ParseAtaTemperature(attributes),
            PowerOnHours = ParseAtaPowerOnHours(attributes),
            PowerOnCount = ParseAtaPowerCycles(attributes),
            DataReadBytes = ParseAtaDataBytes(attributes, 0xF2),
            DataWrittenBytes = ParseAtaDataBytes(attributes, 0xF1),
            IsNvme = false,
            HasSmart = true,
        };
    }

    /// <summary>尝试 NVMe 直读（IOCTL_STORAGE_QUERY_PROPERTY），失败返回 null 并降级。</summary>
    private static SmartInfo? TryReadNvmeSmart(uint index, string model)
    {
        try
        {
            var raw = ReadNvmeSmartRaw(index);
            var attrs = ParseNvmeAttributes(raw);
            // Life = 100 - PercentageUsed
            var percentageUsed = raw[5];
            var life = Math.Clamp(100 - percentageUsed, 0, 100);
            var status = CheckNvmeStatus(attrs, life, model, ThresholdFF);
            return new SmartInfo
            {
                Status = status,
                LifePercent = life,
                TemperatureC = ParseNvmeTemperature(raw),
                PowerOnHours = ParseNvmePowerOnHours(raw),
                PowerOnCount = ParseNvmePowerCycles(raw),
                DataReadBytes = ParseNvmeDataBytes(raw, 32),
                DataWrittenBytes = ParseNvmeDataBytes(raw, 48),
                IsNvme = true,
                HasSmart = true,
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SMART] PhysicalDrive{index} NVMe 读取失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>按 CDI NVMeInterpreter 的映射，将 NVMe log 解析为判定所需属性数组。</summary>
    private static List<SmartAttribute> ParseNvmeAttributes(byte[] raw)
    {
        var attrs = new List<SmartAttribute>
        {
            new(1, 0, 0, 0, [raw[0], 0, 0, 0, 0, 0]),                    // Critical Warning
            new(3, 0, 0, 0, [raw[3], 0, 0, 0, 0, 0]),                    // Available Spare
            new(4, 0, 0, 0, [raw[4], 0, 0, 0, 0, 0]),                    // Spare Threshold
        };
        return attrs;
    }
}