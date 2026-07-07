using System.Diagnostics.CodeAnalysis;
using System.Management;
using LibreHardwareMonitor.Hardware;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode", Justification = "LibreHardwareMonitor uses reflection for sensor discovery")]
[UnconditionalSuppressMessage("Aot", "IL3050:RequiresDynamicCode", Justification = "LibreHardwareMonitor requires dynamic code generation")]
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
            s_initDone = true;
            try
            {
                s_computer?.Close();
                s_computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = true,
                    IsNetworkEnabled = true,
                    IsStorageEnabled = true,
                    IsMotherboardEnabled = true,
                    IsBatteryEnabled = true,
                    IsControllerEnabled = false,
                    IsPsuEnabled = false
                };
                s_computer.Open();
                s_debugLogged = false;
            }
            catch { s_computer = null; }
        }
    }

    public static void ReinitLhm()
    {
        lock (s_lock)
        {
            s_initDone = false;
        }
    }

    private static bool s_debugLogged;

    public MonitorSample Read(bool fpsEnabled = false)
    {
        EnsureInit();
        var sample = new MonitorSample();
        lock (s_lock)
        {
            if (s_computer == null) return sample;
            try
            {
                foreach (IHardware hw in s_computer.Hardware)
                    hw.Update();
                foreach (IHardware hw in s_computer.Hardware)
                {
                    ReadHardware(hw, sample);
                    if (!s_debugLogged)
                    {
                        s_debugLogged = true;
                        var logPath = ConfigManager.GetSensorDumpPath();
                        try
                        {
                            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                            using var w = new System.IO.StreamWriter(logPath, false, System.Text.Encoding.UTF8);
                            w.WriteLine($"=== LHM Sensor Dump {DateTime.Now:HH:mm:ss} ===");
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
            catch { }
        }

        ReadMemFromWmi(sample);
        ReadNetSpeedFromLhm(sample);

        if (fpsEnabled)
        {
            try
            {
                var (fps, proc) = FpsService.GetFps();
                sample.Fps = fps;
                sample.FpsProcess = proc;
            }
            catch { }
        }

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
                    if (sensor.SensorType == SensorType.SmallData && Has(sensor.Name, "used") && (Has(sensor.Name, "memory") || Has(sensor.Name, "dedicated")))
                        s.GpuVramUsedGB = sensor.Value.Value / 1024f;
                }
                break;

            case HardwareType.Memory:
                foreach (var sensor in hw.Sensors)
                {
                    if (!sensor.Value.HasValue) continue;
                    if (sensor.SensorType == SensorType.Load && (Has(sensor.Name, "memory") || sensor.Name.Equals("Load", StringComparison.OrdinalIgnoreCase)))
                        s.MemLoad = sensor.Value.Value;
                    if ((sensor.SensorType == SensorType.Data || sensor.SensorType == SensorType.SmallData) && Has(sensor.Name, "used"))
                        s.MemUsedGB = sensor.Value.Value;
                    if ((sensor.SensorType == SensorType.Data || sensor.SensorType == SensorType.SmallData) && Has(sensor.Name, "available"))
                        s.MemTotalGB = s.MemUsedGB >= 0 ? s.MemUsedGB + sensor.Value.Value : -1;
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

    private static void ReadMemFromWmi(MonitorSample s)
    {
        if (s.MemTotalGB > 0) return;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                var totalKB = Convert.ToInt64(obj["TotalVisibleMemorySize"]);
                var freeKB = Convert.ToInt64(obj["FreePhysicalMemory"]);
                s.MemTotalGB = totalKB / 1048576f;
                s.MemUsedGB = (totalKB - freeKB) / 1048576f;
                s.MemLoad = totalKB > 0 ? (float)(totalKB - freeKB) / totalKB * 100 : -1;
            }
        }
        catch { }
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
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.Contains("Arc", StringComparison.OrdinalIgnoreCase)) return 1;
        return 4;
    }

    public static Dictionary<string, float> ReadDiskTemperatures()
    {
        var result = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        Instance.EnsureInit();
        lock (s_lock)
        {
            if (s_computer == null) return result;
            try
            {
                foreach (IHardware hw in s_computer.Hardware)
                    hw.Update();
                foreach (IHardware hw in s_computer.Hardware)
                {
                    if (hw.HardwareType != HardwareType.Storage) continue;
                    foreach (var sensor in hw.Sensors)
                    {
                        if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue) continue;
                        if (Has(sensor.Name, "warning") || Has(sensor.Name, "critical")) continue;
                        if (result.ContainsKey(hw.Name)) continue;
                        result[hw.Name] = sensor.Value.Value;
                        break;
                    }
                }
            }
            catch { }
        }
        return result;
    }

    public void Dispose()
    {
        _fpsService?.Dispose();
        lock (s_lock)
        {
            try { s_computer?.Close(); } catch { }
            s_computer = null;
            s_initDone = false;
        }
    }
}