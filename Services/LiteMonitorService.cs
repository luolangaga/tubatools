using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Management;
using System.Net.Security;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TubaWinUi3.Services;

public sealed class MonitorSample
{
    public float CpuLoad = -1, CpuTemp = -1, CpuClock = -1, CpuPower = -1;
    public string CpuName = "";

    public float GpuLoad = -1, GpuTemp = -1, GpuClock = -1, GpuPower = -1, GpuVramLoad = -1, GpuVramUsedGB = -1;
    public string GpuName = "";

    public float MemLoad = -1, MemUsedGB = -1, MemTotalGB = -1;

    public float DiskReadMBs = -1, DiskWriteMBs = -1, DiskTemp = -1;

    public float NetUpMBs = -1, NetDownMBs = -1;

    public float BatPercent = -1, BatPower = -1;
    public bool BatCharging;

    public float Fps = -1;
    public string FpsProcess = "";
}

public sealed class LiteMonitorService : IDisposable
{
    private static Computer? s_computer;
    private static readonly object s_lock = new();
    private static bool s_initDone;

    private FpsDetector? _fpsDetector;
    private static string? s_assetDir;
    private static string AssetDir => s_assetDir ??= Path.Combine(ToolCatalog.AppDirectory, "LiteMonitorAssets");

    private static readonly string[] s_fpsUrls =
    [
        "https://litemonitor.cn/update/LiteMonitorFPS.exe",
        "https://gitee.com/Diorser/LiteMonitor/raw/master/resources/assets/LiteMonitorFPS.exe",
        "https://github.com/Diorser/LiteMonitor/raw/master/resources/assets/LiteMonitorFPS.exe"
    ];

    private static readonly string[] s_driverUrls =
    [
        "https://litemonitor.cn/update/driver.zip",
        "https://gitee.com/Diorser/LiteMonitor/raw/master/resources/assets/driver.zip",
        "https://github.com/Diorser/LiteMonitor/raw/master/resources/assets/driver.zip"
    ];

    private static readonly string[] s_virtualNicKW =
        ["virtual", "vmware", "hyper-v", "hyper v", "vbox", "loopback", "tunnel", "tap", "tun", "bluetooth", "zerotier", "tailscale", "wan miniport"];

    public static LiteMonitorService Instance { get; } = new();

    private LiteMonitorService() { }

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
                        var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TubaWinUi3", "sensor_dump.txt");
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
                _fpsDetector ??= new FpsDetector();
                var (fps, proc) = _fpsDetector.GetFps();
                sample.Fps = fps;
                sample.FpsProcess = proc;
            }
            catch { }
        }

        return sample;
    }

    public List<(int pid, string name, float fps)> GetFpsProcessList()
    {
        if (_fpsDetector == null) return [];
        return _fpsDetector.GetProcessList();
    }

    public void SetFpsFocus(int pid) => _fpsDetector?.SetFocus(pid);
    public void ClearFpsFocus() => _fpsDetector?.ClearFocus();

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

    public static bool IsDriverReady()
    {
        var (installed, version) = GetPawnIOInfo();
        var required = new Version(2, 2, 0, 0);
        return installed && (version == null || version >= required);
    }

    public async Task<bool> EnsureDriverAsync(XamlRoot xamlRoot)
    {
        if (IsDriverReady()) return true;

        Directory.CreateDirectory(AssetDir);
        var tempZip = Path.Combine(Path.GetTempPath(), $"LiteMonitor_driver_{Guid.NewGuid()}.zip");

        bool ok = await DownloadWithDialogAsync(xamlRoot, "安装硬件监控驱动",
            "CPU 温度/频率/功耗等数据需要 PawnIO 驱动支持。\n点击安装自动获取并安装（约3MB）。",
            s_driverUrls, tempZip);
        if (!ok) return false;

        try
        {
            ZipFile.ExtractToDirectory(tempZip, AssetDir, true);
            var setupPath = FindFile(AssetDir, "PawnIO_setup.exe", "pawnio.exe", "PawnIO.exe");
            if (setupPath == null) return false;

            var psi = new ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = "-install -silent",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var proc = Process.Start(psi);
            if (proc != null) await proc.WaitForExitAsync();
            var ready = IsDriverReady();
            if (ready) ReinitLhm();
            return ready;
        }
        catch { return false; }
        finally { try { File.Delete(tempZip); } catch { } }
    }

    public async Task<bool> EnsureFpsComponentAsync(XamlRoot xamlRoot)
    {
        var exePath = Path.Combine(AssetDir, "LiteMonitorFPS.exe");
        if (File.Exists(exePath) && IsValidExe(exePath)) return true;

        Directory.CreateDirectory(AssetDir);
        var tempPath = Path.Combine(Path.GetTempPath(), $"LiteMonitorFPS_{Guid.NewGuid()}.exe");

        bool ok = await DownloadWithDialogAsync(xamlRoot, "下载 FPS 检测组件",
            "FPS 帧率检测需要 PresentMon 组件，点击下载自动获取。", s_fpsUrls, tempPath);
        if (!ok) return false;

        try { File.Copy(tempPath, exePath, true); return IsValidExe(exePath); }
        catch { return false; }
        finally { try { File.Delete(tempPath); } catch { } }
    }

    private static async Task<bool> DownloadWithDialogAsync(XamlRoot xamlRoot, string title, string desc, string[] urls, string savePath)
    {
        var tcs = new TaskCompletionSource<bool>();

        var dialog = new ContentDialog
        {
            Title = title,
            XamlRoot = xamlRoot,
            PrimaryButtonText = "下载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = desc, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        var progressBar = new ProgressBar { IsIndeterminate = true };
        stack.Children.Add(progressBar);
        var statusLabel = new TextBlock { Opacity = 0.68, FontSize = 12, Text = "点击「下载」开始" };
        stack.Children.Add(statusLabel);
        dialog.Content = stack;

        var cts = new CancellationTokenSource();

        dialog.PrimaryButtonClick += async (d, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                bool success = false;
                Exception? lastErr = null;

                foreach (var url in urls)
                {
                    if (cts.Token.IsCancellationRequested) break;
                    try
                    {
                        statusLabel.Text = $"正在连接 {new Uri(url).Host}...";
                        progressBar.IsIndeterminate = true;

                        var handler = new SocketsHttpHandler
                        {
                            SslOptions = new SslClientAuthenticationOptions
                            {
                                RemoteCertificateValidationCallback = delegate { return true; }
                            }
                        };
                        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
                        http.DefaultRequestHeaders.UserAgent.ParseAdd("TubaWinUi3/1.0");

                        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                        resp.EnsureSuccessStatusCode();

                        var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
                        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
                        {
                            statusLabel.Text = $"{new Uri(url).Host} 返回了非二进制文件，切换源...";
                            continue;
                        }

                        var total = resp.Content.Headers.ContentLength ?? -1;
                        if (total > 0 && total < 100 * 1024)
                        {
                            statusLabel.Text = $"文件过小({total / 1024}KB)，可能是无效文件，切换源...";
                            continue;
                        }

                        using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
                        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                        using var fs = File.Create(savePath);

                        var buf = new byte[81920];
                        long read = 0;
                        int chunk;
                        var sw = Stopwatch.StartNew();
                        long lastBytes = 0;
                        var lastTime = sw.Elapsed;

                        while ((chunk = await stream.ReadAsync(buf, cts.Token)) > 0)
                        {
                            await fs.WriteAsync(buf.AsMemory(0, chunk), cts.Token);
                            read += chunk;

                            var now = sw.Elapsed;
                            if ((now - lastTime).TotalMilliseconds > 300)
                            {
                                if (total > 0)
                                {
                                    var pct = (double)read / total;
                                    progressBar.IsIndeterminate = false;
                                    progressBar.Value = pct * 100;
                                }
                                var speed = (read - lastBytes) / Math.Max((now - lastTime).TotalSeconds, 0.001) / 1048576.0;
                                statusLabel.Text = $"{read / 1048576.0:F1}MB / {(total > 0 ? $"{total / 1048576.0:F1}MB" : "??")}  {speed:F1}MB/s";
                                lastBytes = read;
                                lastTime = now;
                            }
                        }

                        fs.Close();
                        if (!ValidateDownload(savePath))
                        {
                            try { File.Delete(savePath); } catch { }
                            statusLabel.Text = "下载文件校验失败，切换源...";
                            continue;
                        }

                        statusLabel.Text = "下载完成！";
                        progressBar.Value = 100;
                        success = true;
                        break;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { lastErr = ex; statusLabel.Text = "切换下载源..."; continue; }
                }

                tcs.TrySetResult(success);
            }
            catch (OperationCanceledException) { tcs.TrySetResult(false); }
            catch { tcs.TrySetResult(false); }
            finally { deferral.Complete(); }
        };

        dialog.CloseButtonClick += (_, _) =>
        {
            cts.Cancel();
            tcs.TrySetResult(false);
        };

        _ = dialog.ShowAsync();
        return await tcs.Task;
    }

    private static bool ValidateDownload(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length < 100 * 1024) return false;
            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 64) return false;
            var buf = new byte[2];
            _ = fs.Read(buf, 0, 2);
            bool isExe = buf[0] == 0x4D && buf[1] == 0x5A;
            bool isZip = buf[0] == 0x50 && buf[1] == 0x4B;
            return isExe || isZip;
        }
        catch { return false; }
    }

    private static (bool Installed, Version? Version) GetPawnIOInfo()
    {
        try
        {
            var keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(keyPath);
                if (key == null) continue;
                var ver = key.GetValue("DisplayVersion") as string;
                if (Version.TryParse(ver, out var v)) return (true, v);
                return (true, null);
            }
        }
        catch { }
        return (false, null);
    }

    private static string? FindFile(string dir, params string[] names)
    {
        foreach (var name in names)
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path)) return path;
        }
        try
        {
            foreach (var f in Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories))
                if (names.Any(n => Path.GetFileName(f).Equals(n, StringComparison.OrdinalIgnoreCase)))
                    return f;
        }
        catch { }
        return null;
    }

    private static bool IsValidExe(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length < 100 * 1024) return false;
            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 64) return false;
            var buf = new byte[2];
            _ = fs.Read(buf, 0, 2);
            return buf[0] == 0x4D && buf[1] == 0x5A;
        }
        catch { return false; }
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

    public void Dispose()
    {
        _fpsDetector?.Dispose();
        lock (s_lock)
        {
            try { s_computer?.Close(); } catch { }
            s_computer = null;
            s_initDone = false;
        }
    }

    private sealed class FpsDetector : IDisposable
    {
        private Process? _proc;
        private volatile bool _running;
        private volatile bool _isStarting;
        private volatile bool _isRestarting;
        private volatile int _manualFocusPid;
        private readonly ConcurrentDictionary<int, int> _frameCounts = new();
        private readonly ConcurrentDictionary<int, Queue<FrameSample>> _accHistory = new();
        private readonly ConcurrentDictionary<int, Queue<float>> _olympicHistory = new();
        private readonly ConcurrentDictionary<int, float> _fpsMap = new();
        private readonly ConcurrentDictionary<int, string> _nameCache = new();
        private int _focusPid;
        private int _pendingPid;
        private int _pendingCount;
        private readonly Stopwatch _sw = new();
        private DateTime _lastAccess = DateTime.MinValue;
        private DateTime _lastData = DateTime.MinValue;
        private const string SessionName = "TubaWinUi3_FPS_Session";
        private const int AccSize = 4;
        private const int OlympicSize = 6;
        private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) { "LiteMonitor", "LiteMonitorFPS", "PresentMon", "Unknown", "TubaWinUi3", "dwm" };

        private struct FrameSample { public int Count; public double Duration; }

        public FpsDetector()
        {
            _sw.Start();
            Task.Run(async () => { while (true) { await Task.Delay(500); CalculateFps(); } });
            Task.Run(async () => { while (true) { await Task.Delay(3000); CheckHealth(); } });
        }

        public (float fps, string process) GetFps()
        {
            _lastAccess = DateTime.Now;
            if (!_running && !_isStarting && !_isRestarting)
            {
                _isStarting = true;
                Task.Run(() => StartService());
            }

            if (!_running || _fpsMap.IsEmpty) return (0, "");

            if (_manualFocusPid != 0 && _fpsMap.ContainsKey(_manualFocusPid))
            {
                _focusPid = _manualFocusPid;
                if (_fpsMap.TryGetValue(_focusPid, out var manualVal))
                {
                    var name = GetProcessName(_focusPid);
                    return ((float)Math.Round(manualVal), name);
                }
            }

            int bestPid = 0;
            float bestFps = -1;
            foreach (var kv in _fpsMap)
            {
                if (kv.Value > bestFps) { bestFps = kv.Value; bestPid = kv.Key; }
            }

            if (_focusPid == 0 || !_fpsMap.ContainsKey(_focusPid))
            {
                _focusPid = bestPid;
                _pendingCount = 0;
            }
            else if (bestPid != _focusPid && bestFps > _fpsMap.GetValueOrDefault(_focusPid) * 1.1f)
            {
                if (_pendingPid == bestPid) _pendingCount++;
                else { _pendingPid = bestPid; _pendingCount = 1; }
                if (_pendingCount >= 2) { _focusPid = bestPid; _pendingCount = 0; }
            }
            else _pendingCount = 0;

            if (_fpsMap.TryGetValue(_focusPid, out var val))
            {
                var name = GetProcessName(_focusPid);
                return ((float)Math.Round(val), name);
            }
            return (0, "");
        }

        public List<(int pid, string name, float fps)> GetProcessList()
        {
            var list = new List<(int pid, string name, float fps)>();
            foreach (var kv in _fpsMap)
            {
                var name = GetProcessName(kv.Key);
                if (!Excluded.Contains(name))
                    list.Add((pid: kv.Key, name, fps: kv.Value));
            }
            return list.OrderByDescending(x => x.fps).ToList();        }

        public void SetFocus(int pid) { _manualFocusPid = pid; _focusPid = pid; }
        public void ClearFocus() { _manualFocusPid = 0; }

        private void StartService()
        {
            try
            {
                if (!IsAdmin()) return;
                var exePath = Path.Combine(AssetDir, "LiteMonitorFPS.exe");
                if (!File.Exists(exePath)) return;

                KillZombies();

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"-session_name {SessionName} -stop_existing_session -output_stdout",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _proc = Process.Start(psi);
                if (_proc == null) return;

                _running = true;
                _lastData = DateTime.Now;
                _proc.ErrorDataReceived += (_, _) => { };
                _proc.BeginErrorReadLine();
                _ = Task.Run(async () =>
                {
                    try { await _proc.WaitForExitAsync(); }
                    finally { _running = false; }
                });
                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (true)
                        {
                            var line = await _proc.StandardOutput.ReadLineAsync();
                            if (line == null) break;
                            if (!string.IsNullOrWhiteSpace(line)) { ParseLine(line); _lastData = DateTime.Now; }
                        }
                    }
                    catch { }
                });
            }
            catch { }
            finally { _isStarting = false; }
        }

        private void ParseLine(string line)
        {
            try
            {
                if (line.Length == 0 || line[0] == 'A') return;
                int first = line.IndexOf(',');
                if (first < 0) return;
                int second = line.IndexOf(',', first + 1);
                int len = second < 0 ? line.Length - first - 1 : second - first - 1;
                if (len > 0 && int.TryParse(line.AsSpan(first + 1, len), out int pid))
                    _frameCounts.AddOrUpdate(pid, 1, (_, v) => v + 1);
            }
            catch { }
        }

        private void CalculateFps()
        {
            if (!_running) return;
            double elapsed = _sw.Elapsed.TotalSeconds;
            _sw.Restart();
            if (elapsed <= 0) return;

            if (_frameCounts.IsEmpty)
            {
                if (!_fpsMap.IsEmpty) _fpsMap.Clear();
                return;
            }

            foreach (var kv in _frameCounts)
            {
                int pid = kv.Key;
                int count = kv.Value;
                _frameCounts[pid] = 0;

                var name = GetProcessName(pid);
                if (Excluded.Contains(name)) { _fpsMap.TryRemove(pid, out _); continue; }

                var acc = _accHistory.GetOrAdd(pid, _ => new Queue<FrameSample>());
                acc.Enqueue(new FrameSample { Count = count, Duration = elapsed });
                while (acc.Count > AccSize) acc.Dequeue();

                long totalCount = 0;
                double totalTime = 0;
                foreach (var fs in acc) { totalCount += fs.Count; totalTime += fs.Duration; }

                float rawFps = totalTime > 0 ? (float)(totalCount / totalTime) : 0;
                if (acc.Count < 2 && count / elapsed > 100) rawFps = (float)(count / elapsed);

                var oly = _olympicHistory.GetOrAdd(pid, _ => new Queue<float>());
                oly.Enqueue(rawFps);
                while (oly.Count > OlympicSize) oly.Dequeue();

                float finalFps;
                if (oly.Count >= 4)
                {
                    float min = float.MaxValue, max = float.MinValue, sum = 0;
                    foreach (var f in oly) { if (f < min) min = f; if (f > max) max = f; sum += f; }
                    finalFps = (sum - min - max) / (oly.Count - 2);
                }
                else
                {
                    float sum = 0;
                    foreach (var f in oly) sum += f;
                    finalFps = oly.Count > 0 ? sum / oly.Count : 0;
                }

                if (finalFps < 1f)
                {
                    _fpsMap.TryRemove(pid, out _);
                    _nameCache.TryRemove(pid, out _);
                }
                else _fpsMap[pid] = finalFps;
            }
        }

        private void CheckHealth()
        {
            if (_lastAccess > DateTime.MinValue && (DateTime.Now - _lastAccess).TotalSeconds > 5)
            {
                Dispose();
                return;
            }
            if (_isRestarting) return;

            bool isDead = false;
            if (_running && (DateTime.Now - _lastData).TotalSeconds > 3)
                isDead = true;
            if (_proc == null || _proc.HasExited)
                isDead = true;

            if (isDead && !_isRestarting)
            {
                _isRestarting = true;
                Task.Run(async () =>
                {
                    try
                    {
                        Dispose();
                        await Task.Delay(1000);
                        StartService();
                    }
                    finally { _isRestarting = false; }
                });
            }
        }

        private string GetProcessName(int pid)
        {
            if (_nameCache.TryGetValue(pid, out var cached)) return cached;
            try { var name = Process.GetProcessById(pid).ProcessName; _nameCache.TryAdd(pid, name); return name; }
            catch { return "Unknown"; }
        }

        private static bool IsAdmin()
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        public static void KillZombies()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("LiteMonitorFPS")) try { p.Kill(); } catch { }
                if (!Environment.HasShutdownStarted)
                    Process.Start(new ProcessStartInfo { FileName = "logman", Arguments = $"stop {SessionName} -ets", UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(100);
            }
            catch { }
        }

        public void Dispose()
        {
            try { _proc?.Kill(); KillZombies(); _running = false; } catch { }
        }
    }
}
