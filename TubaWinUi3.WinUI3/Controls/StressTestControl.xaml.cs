using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Controls;

public sealed partial class StressTestControl : UserControl
{
    private const int ChartMaxPoints = 120;
    private const int DownsampleThreshold = 800;
    private const int MaxLogLines = 200;

    private static string FurMarkSettingsPath => Path.Combine(ConfigManager.GetDataDir(), "furmark_settings.json");
    private static string NetSettingsPath => Path.Combine(ConfigManager.GetDataDir(), "net_stress_settings.json");

    // 监控曲线配色：统一取共享语义色 / 图表系列色（ThemeColors），不再硬编码品牌色。
    // 折线画刷由代码构造，不会随主题自动刷新，故由 ActualThemeChanged 重建系列。
    private static SKColor TempC => Sk(ThemeColors.AccentRed);
    private static SKColor UsageC => Sk(ThemeColors.Series1);
    private static SKColor ClockC => Sk(ThemeColors.AccentOrange);
    private static SKColor PowerC => Sk(ThemeColors.AccentGreen);
    private static SKColor GpuTempC => Sk(ThemeColors.AccentOrange);
    private static SKColor GpuClockC => Sk(ThemeColors.Series2);
    private static SKColor GpuPowerC => Sk(ThemeColors.AccentRed);
    private static SKColor NetTxC => Sk(ThemeColors.Series1);
    private static SKColor NetRxC => Sk(ThemeColors.AccentRed);

    private static SKColor Sk(Windows.UI.Color c) => new(c.R, c.G, c.B, 255);

    private readonly ObservableCollection<double> _cpuTempChart = [];
    private readonly ObservableCollection<double> _cpuUsageChart = [];
    private readonly ObservableCollection<double> _cpuClockChart = [];
    private readonly ObservableCollection<double> _cpuPowerChart = [];
    private readonly ObservableCollection<double> _gpuTempChart = [];
    private readonly ObservableCollection<double> _gpuClockChart = [];
    private readonly ObservableCollection<double> _gpuPowerChart = [];
    private readonly ObservableCollection<double> _netTxChart = [];
    private readonly ObservableCollection<double> _netRxChart = [];

    private readonly List<double> _cpuTempReport = [];
    private readonly List<double> _cpuUsageReport = [];
    private readonly List<double> _cpuClockReport = [];
    private readonly List<double> _cpuPowerReport = [];
    private readonly List<double> _gpuTempReport = [];
    private readonly List<double> _gpuClockReport = [];
    private readonly List<double> _gpuPowerReport = [];
    private readonly List<double> _netTxReport = [];
    private readonly List<double> _netRxReport = [];

    private DispatcherTimer? _monitorTimer;
    private DispatcherTimer? _elapsedTimer;
    private DateTime _startTime;
    private int _targetMinutes;

    private Process? _primeProcess;
    private Process? _furmarkProcess;

    private bool _isRunning;
    private bool _settingsReady;

    private double _cpuTempPeak, _cpuUsagePeak, _cpuClockPeak, _cpuPowerPeak;
    private double _gpuTempPeak, _gpuClockPeak, _gpuPowerPeak;
    private double _cpuTempSum, _cpuUsageSum, _cpuClockSum, _cpuPowerSum;
    private double _gpuTempSum, _gpuClockSum, _gpuPowerSum;
    private int _sampleCount;
    private int _cpuTempCount, _cpuUsageCount, _cpuClockCount, _cpuPowerCount;
    private int _gpuTempCount, _gpuClockCount, _gpuPowerCount;
    private int _monitorRetryCount;
    private bool _monitorInFlight;
    private bool _cpuSelectedThisRun;
    private int _cpuSensorMissingTicks;
    private bool _sensorDriverHintShown;

    private NetworkStressRunner? _netRunner;
    private bool _netRunnerActive;
    private bool _netOnlyMode;
    private string _modeName = "烤机";
    private double _netTxPeak, _netRxPeak;
    private double _netTxSum, _netRxSum;
    private int _netSampleCount;
    private string _nicSpeedText = "--";
    private bool _netErrorLogged;
    private int _netHeartbeatCount;
    private long _lastNetSent, _lastNetRecv;
    private DateTime _lastNetSampleUtc;

    public event EventHandler? StressStarted;
    public event EventHandler? StressStopped;

    public Window? OwnerWindow { get; set; }

    public bool IsRunning => _isRunning;

    public StressTestControl()
    {
        InitializeComponent();
        InitCharts();
        // 曲线画刷在代码里构造，切换主题后按现有数据集合重建系列
        ActualThemeChanged += (_, _) => InitCharts();
        _settingsReady = true;
        LoadFurMarkSettings();
        UpdateDxt5Availability();
        LoadNetSettings();
        UpdateNetReference();
    }

    public void Cleanup() => StopStress();

    private sealed class NetSettings
    {
        public double DataRateMbps { get; set; } = 100;
        public int DurationMinutes { get; set; } = 15;
        public int TargetModeIndex { get; set; }
    }

    private void LoadNetSettings()
    {
        try
        {
            if (File.Exists(NetSettingsPath))
            {
                var s = JsonSerializer.Deserialize<NetSettings>(File.ReadAllText(NetSettingsPath));
                if (s is not null) ApplyNetSettings(s);
            }
        }
        catch { }
    }

    private void ApplyNetSettings(NetSettings s)
    {
        if (s.DataRateMbps >= 1) NetDataRateBox.Value = Math.Clamp(s.DataRateMbps, 1, 10000);
        if (s.DurationMinutes >= 1) NetDurationBox.Value = Math.Clamp(s.DurationMinutes, 1, 1440);
        if (NetTargetModeBox.Items.Count > s.TargetModeIndex && s.TargetModeIndex >= 0) NetTargetModeBox.SelectedIndex = s.TargetModeIndex;
    }

    private void SaveNetSettings()
    {
        try
        {
            var s = new NetSettings
            {
                DataRateMbps = SafeNetDataRate(),
                DurationMinutes = SafeNetDuration(),
                TargetModeIndex = Math.Clamp(NetTargetModeBox.SelectedIndex, 0, 2),
            };
            var dir = Path.GetDirectoryName(NetSettingsPath);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.WriteAllText(NetSettingsPath, JsonSerializer.Serialize(s));
        }
        catch { }
    }

    private sealed class FurMarkSettings
    {
        public int ApiIndex { get; set; }
        public int ResolutionIndex { get; set; } = 1;
        public int MsaaIndex { get; set; } = 2;
        public int VramIndex { get; set; }
        public int GpuIndex { get; set; }
        public int VsyncIndex { get; set; }
        public int BkgImgIndex { get; set; }
        public int FurImgIndex { get; set; }
        public bool Fullscreen { get; set; } = true;
        public bool BackfaceCulling { get; set; } = true;
        public bool Dxt5 { get; set; }
        public bool Osi { get; set; } = true;
        public bool GpuMonitor { get; set; } = true;
        public bool GlCoreProfile { get; set; }
    }

    private void LoadFurMarkSettings()
    {
        try
        {
            if (File.Exists(FurMarkSettingsPath))
            {
                var s = JsonSerializer.Deserialize<FurMarkSettings>(File.ReadAllText(FurMarkSettingsPath));
                if (s is not null) ApplyFurMarkSettings(s);
            }
        }
        catch { }
    }

    private void ApplyFurMarkSettings(FurMarkSettings s)
    {
        if (ApiBox.Items.Count > s.ApiIndex && s.ApiIndex >= 0) ApiBox.SelectedIndex = s.ApiIndex;
        if (ResolutionBox.Items.Count > s.ResolutionIndex && s.ResolutionIndex >= 0) ResolutionBox.SelectedIndex = s.ResolutionIndex;
        if (MsaaBox.Items.Count > s.MsaaIndex && s.MsaaIndex >= 0) MsaaBox.SelectedIndex = s.MsaaIndex;
        if (VramBox.Items.Count > s.VramIndex && s.VramIndex >= 0) VramBox.SelectedIndex = s.VramIndex;
        if (VsyncBox.Items.Count > s.VsyncIndex && s.VsyncIndex >= 0) VsyncBox.SelectedIndex = s.VsyncIndex;
        if (BkgImgBox.Items.Count > s.BkgImgIndex && s.BkgImgIndex >= 0) BkgImgBox.SelectedIndex = s.BkgImgIndex;
        if (FurImgBox.Items.Count > s.FurImgIndex && s.FurImgIndex >= 0) FurImgBox.SelectedIndex = s.FurImgIndex;
        GpuIndexBox.Value = Math.Clamp(s.GpuIndex, 0, 15);
        FullscreenToggle.IsOn = s.Fullscreen;
        BfcToggle.IsOn = s.BackfaceCulling;
        Dxt5Toggle.IsOn = s.Dxt5;
        OsiToggle.IsOn = s.Osi;
        GpumonToggle.IsOn = s.GpuMonitor;
        GlCoreToggle.IsOn = s.GlCoreProfile;
    }

    private void SaveFurMarkSettings()
    {
        try
        {
            var s = new FurMarkSettings
            {
                ApiIndex = ApiBox.SelectedIndex,
                ResolutionIndex = ResolutionBox.SelectedIndex,
                MsaaIndex = MsaaBox.SelectedIndex,
                VramIndex = VramBox.SelectedIndex,
                GpuIndex = SafeGpuIndex(),
                VsyncIndex = VsyncBox.SelectedIndex,
                BkgImgIndex = BkgImgBox.SelectedIndex,
                FurImgIndex = FurImgBox.SelectedIndex,
                Fullscreen = FullscreenToggle.IsOn,
                BackfaceCulling = BfcToggle.IsOn,
                Dxt5 = Dxt5Toggle.IsOn,
                Osi = OsiToggle.IsOn,
                GpuMonitor = GpumonToggle.IsOn,
                GlCoreProfile = GlCoreToggle.IsOn,
            };
            var dir = Path.GetDirectoryName(FurMarkSettingsPath);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.WriteAllText(FurMarkSettingsPath, JsonSerializer.Serialize(s));
        }
        catch { }
    }

    private void ResetFurMarkSettings_Click(object sender, RoutedEventArgs e)
    {
        ApplyFurMarkSettings(new FurMarkSettings());
        SaveFurMarkSettings();
        Log("FurMark 参数已恢复默认");
    }

    private void FurMarkSetting_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsReady) return;
        SaveFurMarkSettings();
        UpdateDxt5Availability();
    }
    private void FurMarkToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady) return;
        SaveFurMarkSettings();
    }
    private void FurMarkNumber_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_settingsReady) return;
        SaveFurMarkSettings();
    }

    private void NetSetting_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsReady) return;
        SaveNetSettings();
        UpdateNetReference();
    }

    private void NetSetting_NumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_settingsReady) return;
        SaveNetSettings();
        UpdateNetReference();
    }

    private void ResetNetSettings_Click(object sender, RoutedEventArgs e)
    {
        ApplyNetSettings(new NetSettings());
        SaveNetSettings();
        UpdateNetReference();
        Log("网卡烤机参数已恢复默认");
    }

    private double SafeNetDataRate() => double.IsNaN(NetDataRateBox.Value) ? 100 : Math.Clamp(NetDataRateBox.Value, 1, 10000);
    private int SafeNetDuration() => double.IsNaN(NetDurationBox.Value) ? 15 : Math.Clamp((int)NetDurationBox.Value, 1, 1440);

    private void UpdateNetReference()
    {
        if (NetRefText is null) return;
        var mbps = SafeNetDataRate();
        var minutes = SafeNetDuration();
        var totalGb = mbps * 60d * minutes / 1024d;
        var linkMbps = ParseLinkMbps(GetNicLinkSpeed());

        NetRefText.Text = $"参考指标 — 每秒 {mbps:0.#} MB/s × {minutes} 分钟 ≈ 总传输量 {totalGb:0.#} GB。"
            + (linkMbps > 0
                ? $"当前链路约 {linkMbps:0.#} Mbps（上限 {linkMbps / 8:0.#} MB/s），建议每秒数据量 ≤ {Math.Max(1, linkMbps / 8 * 0.5):0.#} MB/s。"
                : "WiFi 实际可达通常只有链路速率的 20%-50%，广播模式更慢。")
            + $"（链路上限换算：100M=12 MB/s，千兆=117 MB/s，2.5G=290 MB/s，5G=585 MB/s，10G=1170 MB/s）";
    }

    private string GetNicLinkSpeed()
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .OrderByDescending(n => n.Speed)
                .FirstOrDefault();
            if (nic is null || nic.Speed <= 0) return "未知";
            var speed = nic.Speed >= 1_000_000_000 ? $"{nic.Speed / 1e9:0.#} Gbps" : $"{nic.Speed / 1e6:0.#} Mbps";
            return $"{nic.Name} ({speed})";
        }
        catch { return "未知"; }
    }

    private static string FormatBps(double bps) => bps >= 1e9 ? $"{bps / 1e9:0.0} GB/s" : bps >= 1e6 ? $"{bps / 1e6:0.0} MB/s" : bps >= 1e3 ? $"{bps / 1e3:0.0} KB/s" : $"{bps:0} B/s";

    private static string FormatBytes(double b) => b >= 1024 * 1024 * 1024 ? $"{b / (1024 * 1024 * 1024):0.0} GB" : b >= 1024 * 1024 ? $"{b / (1024 * 1024):0.0} MB" : b >= 1024 ? $"{b / 1024:0} KB" : $"{b:0} B";

    private void UpdateDxt5Availability()
    {
        if (ApiBox is null || Dxt5Toggle is null) return;
        var isGl = ApiBox.SelectedIndex is 0 or 2;
        Dxt5Toggle.IsEnabled = isGl;
        GlCoreToggle.IsEnabled = isGl;
        if (!isGl && Dxt5Toggle.IsOn) Dxt5Toggle.IsOn = false;
        if (!isGl && GlCoreToggle.IsOn) GlCoreToggle.IsOn = false;
    }

    private int SafeGpuIndex() => double.IsNaN(GpuIndexBox.Value) ? 0 : Math.Clamp((int)GpuIndexBox.Value, 0, 15);

    private string BuildFurMarkArgs(int minutes)
    {
        static string TagOf(ComboBox box) => ((ComboBoxItem)box.SelectedItem).Tag?.ToString() ?? "";

        var api = TagOf(ApiBox);
        var args = new List<string> { $"--demo {api}" };

        if (FullscreenToggle.IsOn)
            args.Add("--fullscreen");
        else
        {
            var res = TagOf(ResolutionBox).Split(',');
            args.Add($"--width {res[0]}");
            args.Add($"--height {res[1]}");
        }

        args.Add($"--msaa {TagOf(MsaaBox)}");

        var vram = int.TryParse(TagOf(VramBox), out var v) ? v : 0;
        if (vram > 0) args.Add($"--furmark-vram-test-gb {vram}");

        var gpuIdx = SafeGpuIndex();
        if (gpuIdx > 0) args.Add($"--gpu-index {gpuIdx}");

        args.Add($"--vsync {TagOf(VsyncBox)}");
        args.Add("--hpgfx 1");

        var bkg = int.TryParse(TagOf(BkgImgBox), out var b) ? b : 0;
        if (bkg > 0) args.Add($"--furmark-bkg-img-id {bkg}");

        var fur = int.TryParse(TagOf(FurImgBox), out var f) ? f : 0;
        if (fur > 0) args.Add($"--furmark-fur-img-id {fur}");

        if (!BfcToggle.IsOn) args.Add("--furmark-bfc 0");
        if (Dxt5Toggle.IsOn && api.Contains("gl")) args.Add("--furmark-dxt5");
        if (GlCoreToggle.IsOn && api.Contains("gl")) args.Add("--opengl-core-profile 1");
        if (!OsiToggle.IsOn) args.Add("--no-osi");
        if (!GpumonToggle.IsOn) args.Add("--no-gpumon");

        args.Add($"--max-time {minutes * 60}");
        args.Add("--no-score-box");
        return string.Join(" ", args);
    }

    private void InitCharts()
    {
        var fast = TimeSpan.FromMilliseconds(150);

        CpuTempChart.Series = [MakeSeries(_cpuTempChart, TempC)];
        CpuTempChart.XAxes = [new Axis { IsVisible = false }];
        CpuTempChart.YAxes = [new Axis { IsVisible = false, MinLimit = 20, MaxLimit = 120 }];
        CpuTempChart.AnimationsSpeed = fast;
        CpuTempChart.EasingFunction = null;

        CpuUsageChart.Series = [MakeSeries(_cpuUsageChart, UsageC)];
        CpuUsageChart.XAxes = [new Axis { IsVisible = false }];
        CpuUsageChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0, MaxLimit = 100 }];
        CpuUsageChart.AnimationsSpeed = fast;
        CpuUsageChart.EasingFunction = null;

        CpuClockChart.Series = [MakeSeries(_cpuClockChart, ClockC)];
        CpuClockChart.XAxes = [new Axis { IsVisible = false }];
        CpuClockChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0 }];
        CpuClockChart.AnimationsSpeed = fast;
        CpuClockChart.EasingFunction = null;

        CpuPowerChart.Series = [MakeSeries(_cpuPowerChart, PowerC)];
        CpuPowerChart.XAxes = [new Axis { IsVisible = false }];
        CpuPowerChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0 }];
        CpuPowerChart.AnimationsSpeed = fast;
        CpuPowerChart.EasingFunction = null;

        GpuTempChart.Series = [MakeSeries(_gpuTempChart, GpuTempC)];
        GpuTempChart.XAxes = [new Axis { IsVisible = false }];
        GpuTempChart.YAxes = [new Axis { IsVisible = false, MinLimit = 20, MaxLimit = 120 }];
        GpuTempChart.AnimationsSpeed = fast;
        GpuTempChart.EasingFunction = null;

        GpuClockChart.Series = [MakeSeries(_gpuClockChart, GpuClockC)];
        GpuClockChart.XAxes = [new Axis { IsVisible = false }];
        GpuClockChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0 }];
        GpuClockChart.AnimationsSpeed = fast;
        GpuClockChart.EasingFunction = null;

        GpuPowerChart.Series = [MakeSeries(_gpuPowerChart, GpuPowerC)];
        GpuPowerChart.XAxes = [new Axis { IsVisible = false }];
        GpuPowerChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0 }];
        GpuPowerChart.AnimationsSpeed = fast;
        GpuPowerChart.EasingFunction = null;

        NetChart.Series = [MakeSeries(_netTxChart, NetTxC), MakeSeries(_netRxChart, NetRxC)];
        NetChart.XAxes = [new Axis { IsVisible = false }];
        NetChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0 }];
        NetChart.AnimationsSpeed = fast;
        NetChart.EasingFunction = null;
    }

    private static LineSeries<double> MakeSeries(ObservableCollection<double> values, SKColor color)
    {
        return new LineSeries<double>
        {
            Values = values,
            Stroke = new SolidColorPaint(color) { StrokeThickness = 2.5f },
            Fill = new SolidColorPaint(new SKColor(color.Red, color.Green, color.Blue, 50)),
            GeometrySize = 0,
            LineSmoothness = 0.4,
            IsHoverable = true,
        };
    }

    private static bool HasMonitorData(MonitorSample s) =>
        s.CpuTemp > 0 || s.CpuLoad >= 0 || s.CpuClock > 0 || s.GpuTemp > 0 || s.GpuClock > 0;

    private void StartStress_Click(object sender, RoutedEventArgs e) => StartStress();
    private void Stop_Click(object sender, RoutedEventArgs e) => StopStress();

    private void TripleStress_Click(object sender, RoutedEventArgs e)
    {
        CpuSelBox.IsChecked = true;
        GpuSelBox.IsChecked = true;
        NetSelBox.IsChecked = true;
        StartStress();
    }

    private bool CpuSelected => CpuSelBox.IsChecked == true;
    private bool GpuSelected => GpuSelBox.IsChecked == true;
    private bool NetSelected => NetSelBox.IsChecked == true;

    public void StartStress(int? durationMinutes = null)
    {
        if (_isRunning) return;

        var cpuSel = CpuSelected;
        var gpuSel = GpuSelected;
        var netSel = NetSelected;
        if (!cpuSel && !gpuSel && !netSel)
        {
            Log("请至少勾选一项烤机项目（CPU / GPU / 网卡）");
            return;
        }
        _netOnlyMode = netSel && !cpuSel && !gpuSel;
        _modeName = BuildModeName(cpuSel, gpuSel, netSel);

        _isRunning = true;
        _monitorRetryCount = 0;
        _monitorInFlight = false;
        _cpuSelectedThisRun = cpuSel;
        _cpuSensorMissingTicks = 0;
        _sensorDriverHintShown = false;
        _sampleCount = 0;
        _cpuTempPeak = _cpuUsagePeak = _cpuClockPeak = _cpuPowerPeak = double.MinValue;
        _gpuTempPeak = _gpuClockPeak = _gpuPowerPeak = double.MinValue;
        _cpuTempSum = _cpuUsageSum = _cpuClockSum = _cpuPowerSum = 0;
        _gpuTempSum = _gpuClockSum = _gpuPowerSum = 0;
        _cpuTempCount = _cpuUsageCount = _cpuClockCount = _cpuPowerCount = 0;
        _gpuTempCount = _gpuClockCount = _gpuPowerCount = 0;

        _cpuTempChart.Clear(); _cpuUsageChart.Clear(); _cpuClockChart.Clear(); _cpuPowerChart.Clear();
        _gpuTempChart.Clear(); _gpuClockChart.Clear(); _gpuPowerChart.Clear();
        _cpuTempReport.Clear(); _cpuUsageReport.Clear(); _cpuClockReport.Clear(); _cpuPowerReport.Clear();
        _gpuTempReport.Clear(); _gpuClockReport.Clear(); _gpuPowerReport.Clear();
        _netTxChart.Clear(); _netRxChart.Clear();
        _netTxReport.Clear(); _netRxReport.Clear();
        _netTxPeak = _netRxPeak = 0; _netTxSum = _netRxSum = 0; _netSampleCount = 0;
        _netRunnerActive = false;
        _netErrorLogged = false;
        _lastNetSent = _lastNetRecv = 0;
        _lastNetSampleUtc = DateTime.UtcNow;
        NetTxRateText.Text = NetRxRateText.Text = NetSentText.Text = "--";
        NetLinkText.Text = "--";

        SetButtonsEnabled(false);
        StopBtn.IsEnabled = true;
        ExportBtn.IsEnabled = false;

        var cpuDuration = durationMinutes ?? (int)CpuDurationBox.Value;
        var gpuDuration = durationMinutes ?? (int)GpuDurationBox.Value;
        var netDuration = durationMinutes ?? SafeNetDuration();
        _targetMinutes = Math.Max(
            Math.Max(cpuSel ? cpuDuration : 0, gpuSel ? gpuDuration : 0),
            netSel ? netDuration : 0);

        var started = new List<string>();
        if (cpuSel) { started.Add("CPU (Prime95)"); StartPrime95(cpuDuration); }
        if (gpuSel) { started.Add("GPU (FurMark)"); StartFurMarkGpuStress(gpuDuration); }
        if (netSel)
        {
            started.Add("网卡");
            if (!StartNetworkStress(netDuration) && _netOnlyMode)
            {
                _isRunning = false;
                SetButtonsEnabled(true);
                StopBtn.IsEnabled = false;
                Log("网卡烤机无法启动，本次烤机未开始");
                return;
            }
        }

        Log($"烤机开始 — {string.Join(" + ", started)}，总时长 {_targetMinutes} 分钟");
        Log($"传感器引擎：{LiteMonitorService.EngineDescription}");

        _startTime = DateTime.UtcNow;

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += ElapsedTimer_Tick;
        _elapsedTimer.Start();

        _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _monitorTimer.Tick += MonitorTimer_Tick;
        _monitorTimer.Start();

        StressStarted?.Invoke(this, EventArgs.Empty);
    }

    public void StopStress()
    {
        _netRunnerActive = false;
        var netRunner = _netRunner;
        _netRunner = null;
        if (netRunner is not null)
        {
            netRunner.Stop();
            netRunner.Dispose();
            Log("网卡烤机已停止");
        }

        if (!_isRunning) return;

        _isRunning = false;
        _monitorTimer?.Stop(); _monitorTimer = null;
        _elapsedTimer?.Stop(); _elapsedTimer = null;

        KillPrime95();
        KillProcess(ref _furmarkProcess);

        SetButtonsEnabled(true);
        StopBtn.IsEnabled = false;
        ExportBtn.IsEnabled = _sampleCount > 0;

        Log("烤机已停止");
        StressStopped?.Invoke(this, EventArgs.Empty);
    }

    // 终止 Prime95 压力测试进程；进程由本工具以普通权限启动，正常可 Kill，兜底 taskkill
    private void KillPrime95()
    {
        var proc = _primeProcess;
        _primeProcess = null;
        if (proc is null) return;

        try
        {
            if (!proc.HasExited) proc.Kill();
            Log("Prime95 已停止");
        }
        catch
        {
            try
            {
                var psi = new ProcessStartInfo("taskkill", $"/PID {proc.Id} /F") { CreateNoWindow = true, UseShellExecute = false };
                using var tk = Process.Start(psi);
                tk?.WaitForExit(5000);
                Log(tk is not null && tk.ExitCode == 0
                    ? "Prime95 已通过 taskkill 终止"
                    : "Prime95 仍在运行，请手动关闭其窗口");
            }
            catch
            {
                Log("Prime95 仍在运行，请手动关闭其窗口");
            }
        }

        try { proc.Dispose(); } catch { }
    }

    private void ElapsedTimer_Tick(object? sender, object e)
    {
        var elapsed = DateTime.UtcNow - _startTime;
        ElapsedText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";

        UpdateNetworkStats();

        if (_targetMinutes > 0 && elapsed.TotalMinutes >= _targetMinutes)
        {
            Log($"已达到设定时长 {_targetMinutes} 分钟，自动停止");
            StopStress();
        }
    }

    private async void MonitorTimer_Tick(object? sender, object e) => await UpdateMonitorAsync();

    private async Task UpdateMonitorAsync()
    {
        // 与「游戏监控」同样的防重入：LHM 全硬件树 + WMI 采样在烤机高负载下会超过 1 秒，
        // 上一轮没结束就跳过本 tick —— 否则 async void 会不断叠加读取，把 UI 线程压死、数据一直刷新不出来
        if (!_isRunning || _monitorInFlight) return;
        _monitorInFlight = true;
        try
        {
            await UpdateMonitorCoreAsync();
        }
        finally
        {
            _monitorInFlight = false;
        }
    }

    private async Task UpdateMonitorCoreAsync()
    {
        // 与「游戏监控」共用同一硬件监控引擎（LibreHardwareMonitor）
        var sample = await Task.Run(() => LiteMonitorService.Instance.Read());

        if (!HasMonitorData(sample))
        {
            _monitorRetryCount++;
            if (_monitorRetryCount == 5) Log("等待硬件监控数据就绪中...");
            else if (_monitorRetryCount == 15) Log("硬件监控数据仍不可用：传感器可能不被当前主板/驱动支持");
            return;
        }

        _monitorRetryCount = 0;
        _sampleCount++;

        var cpuTemp = sample.CpuTemp; var cpuUsage = sample.CpuLoad; var cpuClock = sample.CpuClock; var cpuPower = sample.CpuPower;
        var gpuTemp = sample.GpuTemp; var gpuClock = sample.GpuClock; var gpuPower = sample.GpuPower;

        TrackCpuSensorAvailability(sample);

        if (cpuTemp > 0) { _cpuTempSum += cpuTemp; _cpuTempCount++; if (cpuTemp > _cpuTempPeak) _cpuTempPeak = cpuTemp; }
        if (cpuUsage > 0) { _cpuUsageSum += cpuUsage; _cpuUsageCount++; if (cpuUsage > _cpuUsagePeak) _cpuUsagePeak = cpuUsage; }
        if (cpuClock > 0) { _cpuClockSum += cpuClock; _cpuClockCount++; if (cpuClock > _cpuClockPeak) _cpuClockPeak = cpuClock; }
        if (cpuPower > 0) { _cpuPowerSum += cpuPower; _cpuPowerCount++; if (cpuPower > _cpuPowerPeak) _cpuPowerPeak = cpuPower; }
        if (gpuTemp > 0) { _gpuTempSum += gpuTemp; _gpuTempCount++; if (gpuTemp > _gpuTempPeak) _gpuTempPeak = gpuTemp; }
        if (gpuClock > 0) { _gpuClockSum += gpuClock; _gpuClockCount++; if (gpuClock > _gpuClockPeak) _gpuClockPeak = gpuClock; }
        if (gpuPower > 0) { _gpuPowerSum += gpuPower; _gpuPowerCount++; if (gpuPower > _gpuPowerPeak) _gpuPowerPeak = gpuPower; }

        PushChart(_cpuTempChart, Val(cpuTemp));
        PushChart(_cpuUsageChart, Val(cpuUsage));
        PushChart(_cpuClockChart, Val(cpuClock));
        PushChart(_cpuPowerChart, Val(cpuPower));
        PushChart(_gpuTempChart, Val(gpuTemp));
        PushChart(_gpuClockChart, Val(gpuClock));
        PushChart(_gpuPowerChart, Val(gpuPower));

        PushReport(_cpuTempReport, Val(cpuTemp));
        PushReport(_cpuUsageReport, Val(cpuUsage));
        PushReport(_cpuClockReport, Val(cpuClock));
        PushReport(_cpuPowerReport, Val(cpuPower));
        PushReport(_gpuTempReport, Val(gpuTemp));
        PushReport(_gpuClockReport, Val(gpuClock));
        PushReport(_gpuPowerReport, Val(gpuPower));

        CpuTempText.Text = Fi(cpuTemp, "°C");
        CpuUsageText.Text = Fi(cpuUsage, "%");
        CpuClockText.Text = Fi(cpuClock, " MHz");
        CpuPowerText.Text = F(cpuPower, "W");

        GpuTempText.Text = Fi(gpuTemp, "°C");
        GpuClockText.Text = Fi(gpuClock, " MHz");
        GpuPowerText.Text = F(gpuPower, "W");
    }

    // CPU 温度/频率/功耗走 MSR，需要 PawnIO 传感器驱动；连续读不到就提示一次并给出一键安装入口
    private void TrackCpuSensorAvailability(MonitorSample sample)
    {
        if (!_cpuSelectedThisRun || sample.CpuTemp > 0 || sample.CpuClock > 0 || sample.CpuPower > 0)
        {
            _cpuSensorMissingTicks = 0;
            return;
        }

        if (++_cpuSensorMissingTicks != 6 || _sensorDriverHintShown) return;
        _sensorDriverHintShown = true;
        Log($"CPU 温度/频率/功耗 始终没有数据 — {LiteMonitorService.EngineDescription}，CPU 温度传感器 {sample.CpuTempSensors} 个");
        _ = PromptSensorDriverAsync(sample.CpuTempSensors);
    }

    private async Task PromptSensorDriverAsync(int cpuTempSensors)
    {
        try
        {
            if (PawnIoService.IsDeviceAvailable())
            {
                Log(cpuTempSensors == 0
                    ? "传感器库里没有 CPU 温度传感器：该 CPU 型号不被当前传感器库识别"
                    : $"PawnIO 驱动已在运行，CPU 温度传感器已创建 {cpuTempSensors} 个却都读不到值，请连同上方诊断信息反馈");
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "读不到 CPU 温度/频率/功耗",
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "CPU 温度、频率、功耗来自 LibreHardwareMonitor 的 MSR 读取，需要 PawnIO 传感器驱动，"
                         + "「游戏监控」的同类数据同样依赖它。\n\n"
                         + "当前系统未加载该驱动，是否下载并运行官方安装包（namazso/PawnIO.Setup）？"
                         + "安装完成后本页会自动恢复读取。"
                },
                PrimaryButtonText = "下载并安装",
                CloseButtonText = "忽略",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            Log("正在下载 PawnIO 安装包...");
            var (ok, message) = await PawnIoService.InstallAsync();
            Log(message);
            if (!ok) return;

            LiteMonitorService.ReinitLhm();
            Log("传感器引擎已重新初始化，稍后即可读到 CPU 温度/频率/功耗");
        }
        catch (Exception ex)
        {
            Log($"传感器驱动处理失败: {ex.Message}");
        }
    }

    private static void PushChart(ObservableCollection<double> list, double value)
    {
        if (value <= 0) return;
        list.Add(value);
        if (list.Count > ChartMaxPoints) list.RemoveAt(0);
    }

    private static void PushReport(List<double> list, double value)
    {
        if (value <= 0) return;
        list.Add(value);
        if (list.Count > DownsampleThreshold)
        {
            for (int i = 1; i < list.Count - 1; i++)
                list.RemoveAt(i);
        }
    }

    private static double Val(double v) => v > 0 ? Math.Round(v, 1) : 0;

    private static string F(double v, string unit)
    {
        if (v < 0) return "--";
        var s = v.ToString("0.0", CultureInfo.InvariantCulture);
        var parts = s.Split('.');
        return parts.Length == 2 ? $"{parts[0]}.{parts[1]}{unit}" : $"{s}{unit}";
    }

    private static string Fi(double v, string unit) => v < 0 ? "--" : $"{v.ToString("0", CultureInfo.InvariantCulture)}{unit}";

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_sampleCount == 0 && _netSampleCount == 0) return;

        var elapsed = DateTime.UtcNow - _startTime;
        var modeStr = _modeName;

        var cpuTempAvg = Avg(_cpuTempSum, _cpuTempCount); var cpuUsageAvg = Avg(_cpuUsageSum, _cpuUsageCount); var cpuClockAvg = Avg(_cpuClockSum, _cpuClockCount); var cpuPowerAvg = Avg(_cpuPowerSum, _cpuPowerCount);
        var gpuTempAvg = Avg(_gpuTempSum, _gpuTempCount); var gpuClockAvg = Avg(_gpuClockSum, _gpuClockCount); var gpuPowerAvg = Avg(_gpuPowerSum, _gpuPowerCount);

        var netSentTotal = _netRunner?.BytesSent ?? 0;
        var netTxAvg = Avg(_netTxSum, _netSampleCount); var netRxAvg = Avg(_netRxSum, _netSampleCount);

        var html = GenerateReportHtml(modeStr, elapsed, cpuTempAvg, cpuUsageAvg, cpuClockAvg, cpuPowerAvg, gpuTempAvg, gpuClockAvg, gpuPowerAvg, netSentTotal, netTxAvg, netRxAvg);

        try
        {
            var win = OwnerWindow ?? App.MainWindow;
            if (win is not null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(win);
                var picker = new Windows.Storage.Pickers.FileSavePicker();
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                picker.SuggestedFileName = $"烤机报告_{DateTime.Now:yyyyMMdd_HHmmss}";
                picker.FileTypeChoices.Add("HTML 报告", new List<string> { ".html" });

                var file = await picker.PickSaveFileAsync();
                if (file is not null)
                {
                    await Windows.Storage.FileIO.WriteTextAsync(file, html);
                    Log($"报告已导出: {file.Path}");
                    Process.Start(new ProcessStartInfo(file.Path) { UseShellExecute = true });
                    return;
                }
            }
        }
        catch { }

        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TubaWinUi3_Reports");
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, $"烤机报告_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            await File.WriteAllTextAsync(filePath, html);
            Log($"报告已导出: {filePath}");
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex2) { Log($"导出失败: {ex2.Message}"); }
    }

    private double Avg(double sum, int count) => count > 0 && sum > 0 ? sum / count : -1;

    private string GenerateReportHtml(string mode, TimeSpan elapsed,
        double cpuTempAvg, double cpuUsageAvg, double cpuClockAvg, double cpuPowerAvg,
        double gpuTempAvg, double gpuClockAvg, double gpuPowerAvg,
        double netSentTotal, double netTxAvg, double netRxAvg)
    {
        var cpuTempPeak = Peak(_cpuTempPeak); var cpuUsagePeak = Peak(_cpuUsagePeak); var cpuClockPeak = Peak(_cpuClockPeak); var cpuPowerPeak = Peak(_cpuPowerPeak);
        var gpuTempPeak = Peak(_gpuTempPeak); var gpuClockPeak = Peak(_gpuClockPeak); var gpuPowerPeak = Peak(_gpuPowerPeak);
        var netTxPeak = Peak(_netTxPeak); var netRxPeak = Peak(_netRxPeak);

        string ChartJs(string canvasId, string label, string color, string unit, List<double> data)
        {
            var vals = string.Join(",", data.Select(v => v.ToString("0.#", CultureInfo.InvariantCulture)));
            var labels = string.Join(",", Enumerable.Range(0, data.Count).Select(_ => "''"));
            return $@"
            new Chart(document.getElementById('{canvasId}'), {{
                type: 'line',
                data: {{
                    labels: [{labels}],
                    datasets: [{{
                        label: '{label}',
                        data: [{vals}],
                        borderColor: '{color}',
                        backgroundColor: '{color}26',
                        fill: true,
                        tension: 0.3,
                        pointRadius: 0,
                        borderWidth: 2
                    }}]
                }},
                options: {{
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {{
                        legend: {{ display: false }},
                        tooltip: {{
                            backgroundColor: 'rgba(32,32,32,0.9)',
                            titleFont: {{ family: 'Segoe UI', size: 12 }},
                            bodyFont: {{ family: 'Segoe UI', size: 13 }},
                            padding: 10,
                            cornerRadius: 6,
                            callbacks: {{ label: ctx => ctx.parsed.y + '{unit}' }}
                        }}
                    }},
                    scales: {{
                        x: {{ display: false }},
                        y: {{
                            grid: {{ color: 'rgba(128,128,128,0.1)' }},
                            ticks: {{ font: {{ family: 'Segoe UI', size: 10 }}, color: '#888' }}
                        }}
                    }},
                    interaction: {{ intersect: false, mode: 'index' }}
                }}
            }});";
        }

        var charts = "";
        charts += ChartJs("cpuTempChart", "CPU 温度", "#f87171", "°C", _cpuTempReport);
        charts += ChartJs("cpuUsageChart", "CPU 占用", "#60a5fa", "%", _cpuUsageReport);
        charts += ChartJs("cpuClockChart", "CPU 频率", "#fbbf24", " MHz", _cpuClockReport);
        charts += ChartJs("cpuPowerChart", "CPU 功耗", "#34d399", "W", _cpuPowerReport);
        charts += ChartJs("gpuTempChart", "GPU 温度", "#fb923c", "°C", _gpuTempReport);
        charts += ChartJs("gpuClockChart", "GPU 频率", "#a78bfa", " MHz", _gpuClockReport);
        charts += ChartJs("gpuPowerChart", "GPU 功耗", "#f472b6", "W", _gpuPowerReport);
        charts += ChartJs("netTxChart", "网卡发送", "#38bdf8", " MB/s", _netTxReport);
        charts += ChartJs("netRxChart", "网卡接收", "#fb7185", " MB/s", _netRxReport);

        string S(string title, string val, string pk, string avg, string c) =>
            $@"<div class=""stat-card""><div class=""stat-title"" style=""color:{c}"">{title}</div><div class=""stat-value"">{val}</div><div class=""stat-detail"">峰值 {pk} · 均值 {avg}</div></div>";

        return $@"<!DOCTYPE html>
<html lang=""zh-CN""><head><meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1.0"">
<title>烤机报告 - {mode}</title>
<script src=""https://cdn.jsdelivr.net/npm/chart.js@4""></script>
<style>
*{{margin:0;padding:0;box-sizing:border-box}}
body{{font-family:'Segoe UI','Microsoft YaHei',sans-serif;background:#f5f5f5;color:#1a1a1a;padding:32px}}
.container{{max-width:960px;margin:0 auto}}
h1{{font-size:28px;font-weight:600;margin-bottom:4px}}
.subtitle{{font-size:14px;color:#666;margin-bottom:24px}}
.info-bar{{display:flex;gap:16px;margin-bottom:24px;flex-wrap:wrap}}
.info-tag{{background:#fff;border:1px solid #e0e0e0;border-radius:8px;padding:8px 16px;font-size:13px}}
.info-tag strong{{color:#0078d4}}
.stats-grid{{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:24px}}
.stat-card{{background:#fff;border:1px solid #e0e0e0;border-radius:8px;padding:16px;text-align:center}}
.stat-title{{font-size:12px;font-weight:600;text-transform:uppercase;letter-spacing:.5px;margin-bottom:4px}}
.stat-value{{font-size:24px;font-weight:700;margin-bottom:4px}}
.stat-detail{{font-size:11px;color:#888}}
.section{{background:#fff;border:1px solid #e0e0e0;border-radius:8px;padding:20px;margin-bottom:16px}}
.section h2{{font-size:16px;font-weight:600;margin-bottom:16px}}
.charts-grid{{display:grid;grid-template-columns:repeat(2,1fr);gap:16px}}
.chart-box{{position:relative;height:180px}}
.chart-label{{font-size:11px;color:#888;text-align:center;margin-bottom:4px}}
.footer{{text-align:center;font-size:11px;color:#aaa;margin-top:24px}}
@media(max-width:640px){{.stats-grid{{grid-template-columns:repeat(2,1fr)}}.charts-grid{{grid-template-columns:1fr}}}}
</style></head><body>
<div class=""container"">
<h1>烤机报告</h1>
<div class=""subtitle"">{mode} · 由图吧工具箱CE 生成</div>
<div class=""info-bar"">
<div class=""info-tag"">测试模式 <strong>{mode}</strong></div>
<div class=""info-tag"">运行时长 <strong>{(int)elapsed.TotalMinutes}分{elapsed.Seconds}秒</strong></div>
<div class=""info-tag"">采样次数 <strong>{_sampleCount}</strong></div>
<div class=""info-tag"">生成时间 <strong>{DateTime.Now:yyyy/MM/dd HH:mm:ss}</strong></div>
</div>
<h2 style=""font-size:16px;font-weight:600;margin-bottom:12px"">CPU 传感器</h2>
<div class=""stats-grid"">
{S("CPU 温度",Fi(cpuTempAvg,"°C"),Fi(cpuTempPeak,"°C"),Fi(cpuTempAvg,"°C"),"#f87171")}
{S("CPU 占用",Fi(cpuUsageAvg,"%"),Fi(cpuUsagePeak,"%"),Fi(cpuUsageAvg,"%"),"#60a5fa")}
{S("CPU 频率",Fi(cpuClockAvg," MHz"),Fi(cpuClockPeak," MHz"),Fi(cpuClockAvg," MHz"),"#fbbf24")}
{S("CPU 功耗",F(cpuPowerAvg,"W"),F(cpuPowerPeak,"W"),F(cpuPowerAvg,"W"),"#34d399")}
</div>
<h2 style=""font-size:16px;font-weight:600;margin-bottom:12px"">GPU 传感器</h2>
<div class=""stats-grid"">
{S("GPU 温度",Fi(gpuTempAvg,"°C"),Fi(gpuTempPeak,"°C"),Fi(gpuTempAvg,"°C"),"#fb923c")}
{S("GPU 频率",Fi(gpuClockAvg," MHz"),Fi(gpuClockPeak," MHz"),Fi(gpuClockAvg," MHz"),"#a78bfa")}
{S("GPU 功耗",F(gpuPowerAvg,"W"),F(gpuPowerPeak,"W"),F(gpuPowerAvg,"W"),"#f472b6")}
<div class=""stat-card""><div class=""stat-title"" style=""color:#888"">运行时长</div><div class=""stat-value"">{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}</div><div class=""stat-detail"">设定 {_targetMinutes} 分钟</div></div>
</div>
<div class=""section""><h2>CPU 监控曲线</h2><div class=""charts-grid"">
<div><div class=""chart-label"">温度 (°C)</div><div class=""chart-box""><canvas id=""cpuTempChart""></canvas></div></div>
<div><div class=""chart-label"">占用 (%)</div><div class=""chart-box""><canvas id=""cpuUsageChart""></canvas></div></div>
<div><div class=""chart-label"">频率 (MHz)</div><div class=""chart-box""><canvas id=""cpuClockChart""></canvas></div></div>
<div><div class=""chart-label"">功耗 (W)</div><div class=""chart-box""><canvas id=""cpuPowerChart""></canvas></div></div>
</div></div>
<div class=""section""><h2>GPU 监控曲线</h2><div class=""charts-grid"">
<div><div class=""chart-label"">温度 (°C)</div><div class=""chart-box""><canvas id=""gpuTempChart""></canvas></div></div>
<div><div class=""chart-label"">频率 (MHz)</div><div class=""chart-box""><canvas id=""gpuClockChart""></canvas></div></div>
<div><div class=""chart-label"">功耗 (W)</div><div class=""chart-box""><canvas id=""gpuPowerChart""></canvas></div></div>
</div></div>
<h2 style=""font-size:16px;font-weight:600;margin-bottom:12px"">网卡</h2>
<div class=""stats-grid"">
{S("发送峰值",F(netTxPeak/1e6," MB/s"),F(netTxPeak/1e6," MB/s"),F(netTxAvg/1e6," MB/s"),"#38bdf8")}
{S("接收峰值",F(netRxPeak/1e6," MB/s"),F(netRxPeak/1e6," MB/s"),F(netRxAvg/1e6," MB/s"),"#fb7185")}
<div class=""stat-card""><div class=""stat-title"" style=""color:#38bdf8"">发送总量</div><div class=""stat-value"">{FormatBytes(netSentTotal)}</div><div class=""stat-detail"">设定 {_targetMinutes} 分钟</div></div>
<div class=""stat-card""><div class=""stat-title"" style=""color:#888"">链路速率</div><div class=""stat-value"">{_nicSpeedText}</div><div class=""stat-detail"">发送均值 {F(netTxAvg/1e6," MB/s")} · 接收均值 {F(netRxAvg/1e6," MB/s")}</div></div>
</div>
<div class=""section""><h2>网卡监控曲线</h2><div class=""charts-grid"">
<div><div class=""chart-label"">发送速率 (MB/s)</div><div class=""chart-box""><canvas id=""netTxChart""></canvas></div></div>
<div><div class=""chart-label"">接收速率 (MB/s)</div><div class=""chart-box""><canvas id=""netRxChart""></canvas></div></div>
</div></div>
<div class=""footer"">图吧工具箱CE · 数据来源: LibreHardwareMonitor 传感器（与「游戏监控」同引擎）· {DateTime.Now:yyyy/MM/dd}</div>
</div>
<script>{charts}</script></body></html>";
    }

    private static double Peak(double v) => v > double.MinValue ? v : 0;

    private void StartPrime95(int stressMinutes)
    {
        var primeExe = FindExecutable("prime95.exe", "prime95");
        if (primeExe is null)
        {
            Log("未找到 Prime95 (Tools/处理器工具/Prime95/prime95.exe)，CPU 烤机已跳过");
            return;
        }

        // Prime95 按工作目录单实例：已运行实例无法被第二个进程接管，直接跳过避免干扰
        if (Process.GetProcessesByName("prime95").Length > 0)
        {
            Log("检测到 Prime95 已在运行，为避免干扰现有实例，CPU 烤机已跳过（可先手动关闭 Prime95 再重试）");
            return;
        }

        var workDir = Path.GetDirectoryName(primeExe);
        if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir))
        {
            Log($"Prime95 所在目录无效: {workDir}");
            return;
        }

        try
        {
            WritePrime95TortureConfig(workDir, stressMinutes);
        }
        catch (Exception ex)
        {
            Log($"写入 Prime95 烤机配置失败: {ex.Message}，将使用 Prime95 上次保存的参数");
        }

        try
        {
            // 实测确认：Prime95 烤机不需要管理员权限，普通权限直接启动即可（也保证停止时能正常 Kill 子进程）
            _primeProcess = Process.Start(new ProcessStartInfo
            {
                FileName = primeExe,
                Arguments = "-t",
                UseShellExecute = true,
                WorkingDirectory = workDir
            });
            Log($"Prime95 已启动 (CPU 压力测试, {stressMinutes} 分钟)");
        }
        catch (Exception ex)
        {
            _primeProcess = null;
            Log($"Prime95 启动失败: {ex.Message}");
        }
    }

    // 将烤机参数合并写入 prime.txt：保留用户已有配置（PrimeNet/Worktodo 等），仅替换烤机相关键。
    // 键名与 prime95 源码一致（Prime95Doc.cpp / commonb.c tortureTest），prime95.exe -t 启动时读取。
    private static void WritePrime95TortureConfig(string workDir, int stressMinutes)
    {
        var path = Path.Combine(workDir, "prime.txt");
        var existing = File.Exists(path)
            ? File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList()
            : [];

        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TortureCores"] = GetPhysicalCoreCount().ToString(),
            ["TortureHyperthreading"] = "1",
            ["MinTortureFFT"] = "4",
            ["MaxTortureFFT"] = "4096",
            ["TortureMem"] = (GetTotalRamMb() >= 8192 ? 1024 : GetTotalRamMb() >= 4096 ? 512 : 128).ToString(),
            ["TortureTime"] = Math.Clamp(stressMinutes, 3, 180).ToString(),
            ["TortureWeak"] = "0",
        };

        var result = new List<string>();
        foreach (var line in existing)
        {
            // 跳过将要替换的烤机键；prime.txt 可能含 [Internals] 等无等号的 section 行，原样保留
            var eq = line.IndexOf('=');
            if (eq > 0 && keys.ContainsKey(line[..eq].Trim())) continue;
            result.Add(line);
        }

        // 实测验证：prime95 的 ini 解析只读取第一个 section（如 [Internals]）之前的全局区，
        // 键追加到文件末尾会被静默忽略并回退默认值，因此新键必须插到第一个 section 之前
        var firstSection = result.FindIndex(l => l.StartsWith('['));
        if (firstSection < 0) firstSection = result.Count;
        result.InsertRange(firstSection, keys.Select(kv => $"{kv.Key}={kv.Value}"));
        File.WriteAllLines(path, result, Encoding.UTF8);
    }

    private static int GetPhysicalCoreCount()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT NumberOfCores FROM Win32_Processor");
            var cores = 0;
            foreach (ManagementObject obj in searcher.Get())
            {
                cores += Convert.ToInt32(obj["NumberOfCores"]);
                obj.Dispose();
            }
            if (cores > 0) return cores;
        }
        catch { }
        return Math.Max(1, Environment.ProcessorCount / 2);
    }

    private static long GetTotalRamMb()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                var bytes = Convert.ToInt64(obj["TotalPhysicalMemory"]);
                obj.Dispose();
                return bytes / 1048576;
            }
        }
        catch { }
        return 8192; // 未知时按内存充足取值，TortureMem 给到 1024 MB
    }

    private void StartFurMarkGpuStress(int minutes)
    {
        var furmarkExe = PerformanceBenchmarkService.FindFurMarkExe();
        if (furmarkExe is null) { Log("未找到 FurMark (烤鸡工具/FurMark_win64/furmark.exe)"); return; }
        try
        {
            var arguments = BuildFurMarkArgs(minutes);
            _furmarkProcess = Process.Start(new ProcessStartInfo { FileName = furmarkExe, Arguments = arguments, UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(furmarkExe) });
            Log($"FurMark 已启动 (GPU 压力测试, {minutes} 分钟)");
            Log($"FurMark 参数: {arguments}");
        }
        catch (Exception ex) { Log($"FurMark 启动失败: {ex.Message}"); }
    }

    private bool StartNetworkStress(int minutes)
    {
        if (NetworkInterface.GetAllNetworkInterfaces().All(n =>
                n.OperationalStatus != OperationalStatus.Up ||
                n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
        {
            Log("未检测到活动的网卡（有线/无线），无法进行网卡烤机");
            return false;
        }

        var mbps = SafeNetDataRate();
        var runner = new NetworkStressRunner();
        _netRunner = runner;
        _netRunnerActive = true;
        _netErrorLogged = false;
        _lastNetSent = _lastNetRecv = 0;
        _lastNetSampleUtc = DateTime.UtcNow;
        _netHeartbeatCount = 0;
        _nicSpeedText = GetNicLinkSpeed();
        NetLinkText.Text = _nicSpeedText;

        if (!runner.Start(mbps, minutes, (NetStressTarget)Math.Clamp(NetTargetModeBox.SelectedIndex, 0, 2)))
        {
            Log($"网卡烤机启动失败: {runner.ErrorMessage}");
            _netRunnerActive = false;
            return false;
        }
        Log($"网卡烤机启动 — 目标 {runner.TargetName}，每秒 {mbps:0.#} MB/s，时长 {minutes} 分钟，链路 {_nicSpeedText}");
        if (runner.IsUnicast) Log("单播模式：接收为 0 属正常（路由器不回包），本模式以发送压测为主");
        if (runner.WarningMessage is not "") Log($"网卡烤机警告: {runner.WarningMessage}");

        var linkMbps = ParseLinkMbps(_nicSpeedText);
        if (linkMbps > 0 && mbps > linkMbps / 8)
            Log($"提示：目标 {mbps:0.#} MB/s 超过链路速率上限 {linkMbps / 8:0.#} MB/s，实测将受网络本身限制（WiFi 通常只能达到链路的 20%-50%），属正常现象");
        return true;
    }

    private static double ParseLinkMbps(string nicText)
    {
        var m = System.Text.RegularExpressions.Regex.Match(nicText, @"([\d.]+)\s*(G|M)bps");
        if (!m.Success) return 0;
        var v = double.TryParse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
        return m.Groups[2].Value == "G" ? v * 1000 : v;
    }

    private void UpdateNetworkStats()
    {
        if (_netRunner is null || !_netRunnerActive)
        {
            if (_netRunner is null) { NetTxRateText.Text = NetRxRateText.Text = NetSentText.Text = "--"; }
            return;
        }

        var runner = _netRunner;
        var sent = runner.BytesSent;
        var recv = runner.BytesReceived;

        var now = DateTime.UtcNow;
        var dt = (now - _lastNetSampleUtc).TotalSeconds;
        var txBps = 0d;
        var rxBps = 0d;
        if (dt >= 0.25)
        {
            txBps = (sent - _lastNetSent) / dt;
            rxBps = (recv - _lastNetRecv) / dt;
            _lastNetSent = sent;
            _lastNetRecv = recv;
            _lastNetSampleUtc = now;
        }
        if (txBps <= 0) txBps = runner.SendRateBps;
        if (rxBps <= 0) rxBps = runner.ReceiveRateBps;

        _netTxSum += txBps; _netRxSum += rxBps; _netSampleCount++;
        if (txBps > _netTxPeak) _netTxPeak = txBps;
        if (rxBps > _netRxPeak) _netRxPeak = rxBps;

        NetTxRateText.Text = FormatBps(txBps);
        NetRxRateText.Text = runner.IsUnicast ? "—" : FormatBps(rxBps);
        NetSentText.Text = FormatBytes(sent);

        PushChart(_netTxChart, txBps / 1e6);
        if (!runner.IsUnicast) PushChart(_netRxChart, rxBps / 1e6);
        PushReport(_netTxReport, txBps / 1e6);
        if (!runner.IsUnicast) PushReport(_netRxReport, rxBps / 1e6);

        if (++_netHeartbeatCount % 5 == 0)
            Log(runner.IsUnicast
                ? $"网卡烤机运行中 — 已发送 {FormatBytes(sent)}，发送 {FormatBps(txBps)}，目标 {SafeNetDataRate():0.#} MB/s"
                : $"网卡烤机运行中 — 已发送 {FormatBytes(sent)}，发送 {FormatBps(txBps)}，接收 {FormatBps(rxBps)}，目标 {SafeNetDataRate():0.#} MB/s");

        if (runner.ErrorMessage is not "" && !_netErrorLogged)
        {
            _netErrorLogged = true;
            Log($"网卡烤机异常: {runner.ErrorMessage}");
        }

        if (!runner.IsActive)
        {
            _netRunnerActive = false;
            Log(runner.ErrorMessage is not ""
                ? $"网卡烤机异常结束: {runner.ErrorMessage}"
                : $"网卡烤机完成 — {runner.FinishedReason}");

            if (_netOnlyMode)
            {
                Log("网卡烤机结束，自动停止");
                StopStress();
            }
        }
    }

    private static string? FindExecutable(params string[] names)
    {
        var toolsRoot = ToolCatalog.ToolsRoot;
        if (Directory.Exists(toolsRoot))
            foreach (var name in names) { var m = TryFindFile(toolsRoot, name); if (m is not null) return m; }

        foreach (var name in names)
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                if (Directory.Exists(root)) { var c = TryFindFile(root, name); if (c is not null) return c; }

        return null;
    }

    /// <summary>递归查找单个文件；遇到无权限目录时跳过，不中断整个查找。</summary>
    /// <remarks>不能退回 Directory.GetFiles(root, name, SearchOption.AllDirectories)：该重载 IgnoreInaccessible=false，
    /// 在 Program Files 下碰到拒绝访问的目录（如系统 ReparsePoint「Windows NT\附件」）会抛 UnauthorizedAccessException，导致一键三烤直接报错。</remarks>
    internal static string? TryFindFile(string root, string name)
    {
        try
        {
            return Directory.EnumerateFiles(root, name, new System.IO.EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.None,
            }).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static void KillProcess(ref Process? proc)
    {
        if (proc is null || proc.HasExited) return;
        try { proc.Kill(); } catch { }
        try { proc.Dispose(); } catch { }
        proc = null;
    }

    private string BuildModeName(bool cpu, bool gpu, bool net)
    {
        var parts = new List<string>();
        if (cpu) parts.Add("CPU");
        if (gpu) parts.Add("GPU");
        if (net) parts.Add("网卡");
        return parts.Count switch
        {
            3 => "一键三烤",
            0 => "烤机",
            1 => $"{parts[0]} 单烤",
            _ => $"{parts[0]}+{parts[1]} 双烤"
        };
    }

    private void SetButtonsEnabled(bool enabled)
    {
        TripleStressBtn.IsEnabled = enabled;
        CpuSelBox.IsEnabled = enabled;
        GpuSelBox.IsEnabled = enabled;
        NetSelBox.IsEnabled = enabled;
        StartStressBtn.IsEnabled = enabled;
    }

    // 与游戏监控的 game_overlay_auto.log 同理：界面日志关掉就没了，烤机读不到传感器时得留下证据
    private static string LogFilePath => Path.Combine(ConfigManager.GetDataDir(), "stress_test.log");

    private void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        LogText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = line + "\n" });
        while (LogText.Inlines.Count > MaxLogLines)
            LogText.Inlines.RemoveAt(0);
        LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
        try
        {
            var dir = Path.GetDirectoryName(LogFilePath);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.AppendAllText(LogFilePath, line + Environment.NewLine);
        }
        catch { }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogText.Inlines.Clear();
}
