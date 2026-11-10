using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public sealed class LiteMonitorService : IDisposable
{
    private static Computer? s_computer;
    private static readonly object s_lock = new();
    private static bool s_initDone;

    private FpsService? _fpsService;

    public static LiteMonitorService Instance { get; } = new();

    private LiteMonitorService() { }

    public FpsService FpsService => _fpsService ??= new FpsService();

    public void EnsureInit()
    {
        lock (s_lock)
        {
            if (s_initDone && s_computer != null) return;
            if (s_initDone && Stopwatch.GetElapsedTime(s_lastInitAttempt).TotalSeconds < 30) return;
            s_lastInitAttempt = Stopwatch.GetTimestamp();
            s_initDone = true;
            try
            {
                // CPU 温度/频率/功耗走 MSR（PawnIO 驱动）：已安装但停着的驱动在重启后不会自动加载，这里补启动一次
                PawnIoService.EnsureDriver();
                s_computer?.Close();
                s_computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    // MemoryStatusEx supplies these values without a second LHM sampler.
                    IsMemoryEnabled = false,
                    IsNetworkEnabled = true,
                    IsStorageEnabled = true,
                    // MonitorSample does not consume motherboard / Super I/O sensors.
                    IsMotherboardEnabled = false,
                    IsBatteryEnabled = true,
                    IsControllerEnabled = false,
                    IsPsuEnabled = false
                };
                s_computer.Open();
                s_initError = null;
                s_debugLogged = false;
            }
            catch (Exception ex)
            {
                try { s_computer?.Close(); } catch { }
                s_computer = null;
                s_initError = ex is UnauthorizedAccessException
                    ? "传感器权限不足，请以管理员身份重试。"
                    : "传感器初始化失败，请检查驱动；30 秒后会自动重试。";
                LogService.Warn(LogService.GameOverlay, $"Sensor initialization failed: {ex.GetType().Name}");
            }
        }
    }

    public static void ReinitLhm()
    {
        lock (s_lock)
        {
            PawnIoService.ResetStartState();
            s_initDone = false;
        }
    }

    private static bool s_debugLogged;

    /// <summary>传感器引擎自检信息（LHM 版本 + PawnIO 驱动状态），日志与 sensor_dump.txt 共用。</summary>
    public static string EngineDescription =>
        $"LibreHardwareMonitor 源码 1d74d0ea (程序集 {typeof(Computer).Assembly.GetName().Version}) · PawnIO "
        + (PawnIoService.IsDeviceAvailable() ? "ready" : PawnIoService.IsInstalled() ? "installed but not loaded" : "missing");

    private readonly object _sampleGate = new();
    private MonitorSample? _latestSample;
    private long _lastSampleTick;
    private static string? s_initError;
    private static long s_lastInitAttempt;

    private readonly Dictionary<string, MonitorSample> _gpuSamples = new(StringComparer.Ordinal);

    public MonitorSample Read(bool fpsEnabled = false) => ReadCore(fpsEnabled, null);

    internal MonitorSample ReadForGpu(string gpuId) => ReadCore(false, gpuId);

    private MonitorSample ReadCore(bool fpsEnabled, string? gpuId)
    {
        MonitorSample sample;
        lock (_sampleGate)
        {
            // Consumers receive independent copies; FPS is read separately at the requested cadence.
            if (_latestSample is null || Stopwatch.GetElapsedTime(_lastSampleTick).TotalMilliseconds >= 200)
            {
                _latestSample = ReadHardwareSample();
                _latestSample.CapturedAt = DateTimeOffset.UtcNow;
                _lastSampleTick = Stopwatch.GetTimestamp();
            }
            sample = _latestSample.Copy();
            if (gpuId is not null)
            {
                _gpuSamples.TryGetValue(gpuId, out var gpu);
                sample.GpuName = gpu?.GpuName ?? "";
                sample.GpuTemp = gpu?.GpuTemp ?? -1;
                sample.GpuLoad = gpu?.GpuLoad ?? -1;
                sample.GpuClock = gpu?.GpuClock ?? -1;
                sample.GpuPower = gpu?.GpuPower ?? -1;
                sample.GpuVramLoad = gpu?.GpuVramLoad ?? -1;
                sample.GpuVramUsedGB = gpu?.GpuVramUsedGB ?? -1;
            }
        }
        if (fpsEnabled)
        {
            try
            {
                var (fps, proc, low1, low01, frameMs, latencyMs) = FpsService.GetFpsStats();
                sample.Fps = fps; sample.FpsProcess = proc;
                sample.FpsLow1 = low1; sample.FpsLow01 = low01;
                sample.FrameTimeMs = frameMs; sample.RenderLatencyMs = latencyMs;
            }
            catch { sample.SensorError ??= "帧率采集暂不可用，请检查管理员权限后重试。硬件监控仍可使用。"; }
        }
        return sample;
    }

    private MonitorSample ReadHardwareSample()
    {
        EnsureInit();
        var sample = new MonitorSample();
        _gpuSamples.Clear();
        lock (s_lock)
        {
            if (s_computer == null)
            {
                sample.SensorError = s_initError;
                ReadSystemMemory(sample);
                return sample;
            }
            // 单个硬件读失败（传感器驱动/设备异常，烤机高负载时更容易发生）不再拖垮整份采样：
            // 否则 CPU 温度等全部字段会一起变成 -1，界面显示成「全部读不到」
            foreach (IHardware hw in s_computer.Hardware)
            {
                try { hw.Update(); }
                catch { sample.SensorError ??= "部分传感器读取失败，请检查驱动或点击重试。其他可用数据仍会显示。"; }
            }
            foreach (IHardware hw in s_computer.Hardware)
            {
                try
                {
                    ReadHardware(hw, sample);
                    if (IsGpu(hw))
                    {
                        var gpu = new MonitorSample { GpuName = hw.Name };
                        ReadGpuSensors(hw, gpu);
                        _gpuSamples[hw.Identifier.ToString()] = gpu;
                    }
                }
                catch { sample.SensorError ??= "部分传感器数据解析失败，请点击重试或导出传感器日志。"; }
                if (!s_debugLogged)
                {
                    s_debugLogged = true;
                    var logPath = ConfigManager.GetSensorDumpPath();
                    try
                    {
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                        using var w = new System.IO.StreamWriter(logPath, false, System.Text.Encoding.UTF8);
                        w.WriteLine($"=== LHM Sensor Dump {DateTime.Now:HH:mm:ss} ===");
                        w.WriteLine(EngineDescription);
                        w.WriteLine($"Hardware count: {s_computer.Hardware.Count}");
                        foreach (var hw2 in s_computer.Hardware)
                        {
                            w.WriteLine($"[HW] {hw2.HardwareType} | {hw2.Name} | Sensors: {hw2.Sensors.Length}");
                            foreach (var sensor in hw2.Sensors)
                                w.WriteLine($"  {sensor.SensorType} | {sensor.Name} = {sensor.Value}");
                        }
                    }
                    catch { }
                }
            }
        }

        ReadSystemMemory(sample);
        ReadNetSpeedFromLhm(sample);


        return sample;
    }

    private static readonly string[] s_virtualNicKW =
        ["virtual", "vmware", "hyper-v", "hyper v", "vbox", "loopback", "tunnel", "tap", "tun", "bluetooth", "zerotier", "tailscale", "wan miniport"];

    private static readonly string[] s_cpuTempExcludeKW = ["distance", "average", "max", "soc", "vrm", "fan", "pump", "liquid", "coolant"];

    private static void ReadHardware(IHardware hw, MonitorSample s)
    {
        switch (hw.HardwareType)
        {
            case HardwareType.Cpu:
                s.CpuName = hw.Name;
                foreach (var sensor in hw.Sensors)
                {
                    // 计数不受值是否可读影响：能区分「传感器没被创建」（CPU 型号不认识）与「创建了但读不到值」（PawnIO 驱动没跑）
                    if (sensor.SensorType == SensorType.Temperature)
                        s.CpuTempSensors++;
                    if (!sensor.Value.HasValue) continue;
                    if (sensor.SensorType == SensorType.Load && (Has(sensor.Name, "total") || Has(sensor.Name, "package")))
                        s.CpuLoad = sensor.Value.Value;
                    if (sensor.SensorType == SensorType.Temperature && sensor.Value.Value > 0 && !s_cpuTempExcludeKW.Any(kw => Has(sensor.Name, kw)))
                        s.CpuTemp = Math.Max(s.CpuTemp, sensor.Value.Value);
                    if (sensor.SensorType == SensorType.Clock && Has(sensor.Name, "core") && !Has(sensor.Name, "bus"))
                        s.CpuClock = Math.Max(s.CpuClock, sensor.Value.Value);
                    if (sensor.SensorType == SensorType.Power)
                    {
                        if (Has(sensor.Name, "package") || Has(sensor.Name, "cores") || Has(sensor.Name, "soc") || Has(sensor.Name, "core"))
                        {
                            if (sensor.Value.Value > 0 && sensor.Value.Value <= 600)
                                s.CpuPower = Math.Max(s.CpuPower, sensor.Value.Value);
                        }
                        else if (s.CpuPower < 0 && sensor.Value.Value > 0 && sensor.Value.Value <= 600)
                            s.CpuPower = sensor.Value.Value;
                    }
                }
                break;

            case HardwareType.GpuNvidia:
            case HardwareType.GpuAmd:
            case HardwareType.GpuIntel:
                if (string.IsNullOrEmpty(s.GpuName) || HwPriority(hw) < HwPriorityOfName(s.GpuName))
                    s.GpuName = hw.Name;
                ReadGpuSensors(hw, s);
                break;

            case HardwareType.Memory:
                foreach (var sensor in hw.Sensors)
                {
                    if (!sensor.Value.HasValue) continue;
                    if (sensor.SensorType == SensorType.Load && (Has(sensor.Name, "memory") || sensor.Name.Equals("Load", StringComparison.OrdinalIgnoreCase)))
                        s.MemLoad = sensor.Value.Value;
                    if (sensor.SensorType == SensorType.Data && Has(sensor.Name, "used"))
                        s.MemUsedGB = sensor.Value.Value / (1024f * 1024 * 1024);
                    if (sensor.SensorType == SensorType.Data && Has(sensor.Name, "available"))
                        s.MemTotalGB = s.MemUsedGB >= 0 ? s.MemUsedGB + sensor.Value.Value / (1024f * 1024 * 1024) : -1;
                }
                break;

            case HardwareType.Battery:
                foreach (var sensor in hw.Sensors)
                {
                    if (!sensor.Value.HasValue) continue;
                    if (sensor.SensorType == SensorType.Level && Has(sensor.Name, "charge"))
                        s.BatPercent = sensor.Value.Value;
                    if (sensor.SensorType == SensorType.Power)
                    {
                        s.BatPower = Math.Abs(sensor.Value.Value);
                        s.BatCharging = sensor.Value.Value > 0;
                    }
                }
                break;

            case HardwareType.Storage:
                foreach (var sensor in hw.Sensors)
                {
                    if (!sensor.Value.HasValue) continue;
                    if (sensor.SensorType == SensorType.Throughput)
                    {
                        if (Has(sensor.Name, "read") && sensor.Value.Value > s.DiskReadMBs)
                            s.DiskReadMBs = sensor.Value.Value / 1048576f;
                        if (Has(sensor.Name, "write") && sensor.Value.Value > s.DiskWriteMBs)
                            s.DiskWriteMBs = sensor.Value.Value / 1048576f;
                    }
                    if (sensor.SensorType == SensorType.Temperature && s.DiskTemp < 0)
                    {
                        if (!Has(sensor.Name, "warning") && !Has(sensor.Name, "critical"))
                            s.DiskTemp = sensor.Value.Value;
                    }
                }
                break;
        }

        foreach (var sub in hw.SubHardware)
            ReadHardware(sub, s);
    }

    private void ReadNetSpeedFromLhm(MonitorSample s)
    {
        float bestUp = 0, bestDown = 0;
        lock (s_lock)
        {
            if (s_computer == null) return;
            foreach (var hw in s_computer.Hardware)
            {
                if (hw.HardwareType != HardwareType.Network) continue;
                if (IsVirtualNic(hw.Name)) continue;
                foreach (var sensor in hw.Sensors)
                {
                    if (sensor.SensorType != SensorType.Throughput || !sensor.Value.HasValue) continue;
                    if (Has(sensor.Name, "upload") || Has(sensor.Name, "up") || Has(sensor.Name, "sent") || Has(sensor.Name, "tx"))
                        bestUp = Math.Max(bestUp, sensor.Value.Value);
                    if (Has(sensor.Name, "download") || Has(sensor.Name, "down") || Has(sensor.Name, "received") || Has(sensor.Name, "rx"))
                        bestDown = Math.Max(bestDown, sensor.Value.Value);
                }
            }
        }
        s.NetUpMBs = bestUp / 1048576f;
        s.NetDownMBs = bestDown / 1048576f;
    }

    private static bool IsVirtualNic(string name)
    {
        foreach (var k in s_virtualNicKW)
            if (name.Contains(k, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    internal static void ReadSystemMemory(MonitorSample sample)
    {
        if (sample.MemTotalGB > 0) return;
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0) return;
        sample.MemTotalGB = status.TotalPhysical / 1073741824f;
        sample.MemUsedGB = (status.TotalPhysical - status.AvailablePhysical) / 1073741824f;
        sample.MemLoad = (float)(100d * (status.TotalPhysical - status.AvailablePhysical) / status.TotalPhysical);
    }

    private static bool Has(string source, string sub) =>
        !string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(sub) &&
        source.Contains(sub, StringComparison.OrdinalIgnoreCase);

    private static int HwPriority(IHardware hw)
    {
        if (hw.HardwareType == HardwareType.GpuNvidia) return 0;
        if (hw.HardwareType == HardwareType.GpuAmd) return hw.Name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        if (hw.HardwareType == HardwareType.GpuIntel) return hw.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase) ? 1 : 3;
        return 4;
    }

    private static int HwPriorityOfName(string name)
    {
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Contains("AMD Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase)) return 2;
        if (name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase)) return 2;
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Contains("Arc", StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Contains("Intel(R) UHD", StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.Contains("Intel(R) Iris", StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return 3;
        return 4;
    }

    private static bool IsGpu(IHardware hw) =>
        hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    private static void ReadGpuSensors(IHardware hw, MonitorSample s)
    {
        foreach (var sensor in hw.Sensors)
        {
            if (!sensor.Value.HasValue) continue;
            if (sensor.SensorType == SensorType.Load && (Has(sensor.Name, "core") || Has(sensor.Name, "d3d")) && s.GpuLoad < 0)
                s.GpuLoad = sensor.Value.Value;
            if (sensor.SensorType == SensorType.Temperature && s.GpuTemp < sensor.Value.Value)
                s.GpuTemp = sensor.Value.Value;
            if (sensor.SensorType == SensorType.Clock && (Has(sensor.Name, "graphics") || Has(sensor.Name, "core") || Has(sensor.Name, "shader")) && sensor.Value.Value <= 6000 && s.GpuClock < 0)
                s.GpuClock = sensor.Value.Value;
            if (sensor.SensorType == SensorType.Power && (Has(sensor.Name, "package") || Has(sensor.Name, "ppt") || Has(sensor.Name, "board") || Has(sensor.Name, "core") || Has(sensor.Name, "gpu")) && sensor.Value.Value > 0 && sensor.Value.Value <= 1200 && s.GpuPower < 0)
                s.GpuPower = sensor.Value.Value;
            if (sensor.SensorType == SensorType.Load && Has(sensor.Name, "memory"))
                s.GpuVramLoad = sensor.Value.Value;
            // LHM 0.9.7 reports all Data sensors in bytes.
            if (sensor.SensorType == SensorType.Data && Has(sensor.Name, "used") && (Has(sensor.Name, "memory") || Has(sensor.Name, "dedicated")))
                s.GpuVramUsedGB = sensor.Value.Value / (1024f * 1024 * 1024);
        }
    }

    internal static (string Id, string Name)[] GetGpuMonitorDevices()
    {
        Instance.EnsureInit();
        lock (s_lock)
            return s_computer?.Hardware.Where(hw => IsGpu(hw) && !IsVirtualGpu(hw.Name))
                .Select(hw => (hw.Identifier.ToString(), hw.Name)).ToArray() ?? [];
    }

    public static List<GpuInfo> GetAvailableGpus()
    {
        var result = new List<GpuInfo>();
        var lhmGpus = new List<GpuInfo>();
        Instance.EnsureInit();
        lock (s_lock)
        {
            if (s_computer != null)
            {
                try
                {
                    foreach (IHardware hw in s_computer.Hardware)
                        hw.Update();
                    int gpuIdx = 0;
                    foreach (IHardware hw in s_computer.Hardware)
                    {
                        if (hw.HardwareType is not (HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel))
                            continue;
                        if (IsVirtualGpu(hw.Name)) continue;
                        lhmGpus.Add(new GpuInfo
                        {
                            Index = gpuIdx++,
                            Name = hw.Name,
                            Type = hw.HardwareType.ToString()
                        });
                    }
                }
                catch { }
            }
        }

        var names = HardwareInfoService.GetPhysicalGpuNames();
        var gpuList = names.Count > 0 ? names : lhmGpus.Select(g => g.Name);
        int idx = 0;
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in gpuList)
        {
            if (!added.Add(name)) continue;
            var lhm = lhmGpus.FirstOrDefault(g => GpuNamesMatch(g.Name, name));
            result.Add(new GpuInfo
            {
                Index = idx++,
                Name = name,
                Type = lhm != null ? lhm.Type : ""
            });
        }
        return result;
    }

    private static bool GpuNamesMatch(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        var ta = a.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tb = b.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ta.Intersect(tb).Count() >= 2;
    }

    private static readonly string[] s_virtualGpuKW =
        ["Microsoft Basic Render", "Microsoft Remote Display", "DDA Wrapper",
         "Idd Desk", "GameViewer Virtual Display", "Honor Virtual Display", "Virtual Display",
         "Virtual GPU", "Virtual Adapter", "虚拟", "Remote Display Adapter"];

    private static bool IsVirtualGpu(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        foreach (var kw in s_virtualGpuKW)
            if (name.Contains(kw, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    internal readonly record struct DiskTemperatureSample(int? Index, string Model, float Temperature);

    internal static IReadOnlyList<DiskTemperatureSample> ReadDiskTemperatures()
    {
        var result = new List<DiskTemperatureSample>();
        lock (s_lock)
        {
            // 硬件概览只读磁盘，不为温度查询启动 CPU/GPU 监控或 PawnIO 驱动。
            // 已有监控实例时复用，否则使用读取后即关闭的 storage-only 实例。
            Computer? storageProbe = null;
            try
            {
                var computer = s_computer;
                if (computer is null)
                {
                    storageProbe = new Computer { IsStorageEnabled = true };
                    storageProbe.Open();
                    computer = storageProbe;
                }
                foreach (IHardware hw in computer.Hardware)
                {
                    if (hw.HardwareType != HardwareType.Storage) continue;
                    try { hw.Update(); } catch { continue; }
                    foreach (var sensor in hw.Sensors)
                    {
                        if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue) continue;
                        if (Has(sensor.Name, "warning") || Has(sensor.Name, "critical")) continue;
                        var value = sensor.Value.Value;
                        if (!float.IsFinite(value) || value <= -40 || value >= 150) continue;
                        var index = hw is LibreHardwareMonitor.Hardware.Storage.StorageDevice disk
                            && disk.Storage.StorageDeviceNumber is { } number && number <= int.MaxValue
                            ? (int?)number : null;
                        result.Add(new DiskTemperatureSample(index, hw.Name, value));
                        break;
                    }
                }
            }
            catch { }
            finally { try { storageProbe?.Close(); } catch { } }
        }
        return result;
    }

    public void Dispose()
    {
        lock (_sampleGate) { _latestSample = null; }
        _fpsService?.Dispose();
        lock (s_lock)
        {
            try { s_computer?.Close(); } catch { }
            s_computer = null;
            s_initDone = false;
        }
    }
}
