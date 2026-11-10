using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
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

    // 监控曲线配色：统一取共享语义色 / 图表系列色（ThemeColors），不再硬编码品牌色。
    // 折线画刷由代码构造，不会随主题自动刷新，故由 ActualThemeChanged 重建系列。
    private static SKColor TempC => Sk(ThemeColors.AccentRed);
    private static SKColor UsageC => Sk(ThemeColors.Series1);
    private static SKColor ClockC => Sk(ThemeColors.AccentOrange);
    private static SKColor PowerC => Sk(ThemeColors.AccentGreen);
    private static SKColor GpuTempC => Sk(ThemeColors.AccentOrange);
    private static SKColor GpuClockC => Sk(ThemeColors.Series2);
    private static SKColor GpuPowerC => Sk(ThemeColors.AccentRed);

    private static SKColor Sk(Windows.UI.Color c) => new(c.R, c.G, c.B, 255);

    private readonly ObservableCollection<double> _cpuTempChart = [];
    private readonly ObservableCollection<double> _cpuUsageChart = [];
    private readonly ObservableCollection<double> _cpuClockChart = [];
    private readonly ObservableCollection<double> _cpuPowerChart = [];
    private readonly ObservableCollection<double> _gpuTempChart = [];
    private readonly ObservableCollection<double> _gpuClockChart = [];
    private readonly ObservableCollection<double> _gpuPowerChart = [];

    private readonly List<double> _cpuTempReport = [];
    private readonly List<double> _cpuUsageReport = [];
    private readonly List<double> _cpuClockReport = [];
    private readonly List<double> _cpuPowerReport = [];
    private readonly List<double> _gpuTempReport = [];
    private readonly List<double> _gpuClockReport = [];
    private readonly List<double> _gpuPowerReport = [];

    private DispatcherTimer? _monitorTimer;
    private DispatcherTimer? _elapsedTimer;
    private DateTime _startTime;
    private int _targetMinutes;

    private Process? _primeProcess;
    private Process? _furmarkProcess;

    private bool _isRunning;
    private bool _settingsReady;
    private readonly bool _inspectionMode;
    private ComboBox? _inspectionDuration;

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

    private string _modeName = "烤机";

    public event EventHandler? StressStarted;
    public event EventHandler? StressStopped;
    internal event Action<MonitorSample>? InspectionSampled;

    private int? _savedGpuIndex;
    private string? _runMonitorGpuId;
    private string? _runMonitorGpuLabel;
    private bool _loadingGpuChoices;

    public Window? OwnerWindow { get; set; }

    public bool IsRunning => _isRunning;

    public StressTestControl() : this(false) { }

    public StressTestControl(bool inspectionMode)
    {
        _inspectionMode = inspectionMode;
        InitializeComponent();
        InitCharts();
        // 曲线画刷在代码里构造，切换主题后按现有数据集合重建系列
        ActualThemeChanged += (_, _) => InitCharts();
        StressGpuBox.Items.Add(new ComboBoxItem { Content = "自动（按渲染 API）" });
        StressGpuBox.SelectedIndex = 0;
        MonitorGpuBox.Items.Add(new ComboBoxItem { Content = "自动（优先匹配烤机显卡）" });
        MonitorGpuBox.SelectedIndex = 0;
        LoadFurMarkSettings();
        _settingsReady = true;
        UpdateDxt5Availability();
        if (inspectionMode) BuildInspectionLayout();
        Loaded += async (_, _) =>
        {
            if (_loadingGpuChoices || StressGpuBox.Items.Count > 1) return;
            _loadingGpuChoices = true;
            try { await LoadGpuChoicesAsync(); }
            finally { _loadingGpuChoices = false; }
        };
    }

    public void Cleanup() => StopStress();

    private sealed class FurMarkSettings
    {
        public int ApiIndex { get; set; }
        public int ResolutionIndex { get; set; } = 1;
        public int MsaaIndex { get; set; } = 2;
        public int VramIndex { get; set; }
        public int? GpuIndex { get; set; }
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
        _savedGpuIndex = s.GpuIndex is >= 0 ? s.GpuIndex : null;
        var match = StressGpuBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is FurMarkGpuInfo gpu && gpu.Index == _savedGpuIndex);
        StressGpuBox.SelectedItem = s.ApiIndex is 1 or 3 && match is not null ? match : StressGpuBox.Items[0];
        FullscreenToggle.IsOn = s.Fullscreen;
        BfcToggle.IsOn = s.BackfaceCulling;
        Dxt5Toggle.IsOn = s.Dxt5;
        OsiToggle.IsOn = s.Osi;
        GpumonToggle.IsOn = s.GpuMonitor;
        GlCoreToggle.IsOn = s.GlCoreProfile;
    }

    private void SaveFurMarkSettings()
    {
        if (_inspectionMode) return;
        try
        {
            var s = new FurMarkSettings
            {
                ApiIndex = ApiBox.SelectedIndex,
                ResolutionIndex = ResolutionBox.SelectedIndex,
                MsaaIndex = MsaaBox.SelectedIndex,
                VramIndex = VramBox.SelectedIndex,
                GpuIndex = _savedGpuIndex,
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
        _settingsReady = false;
        ApplyFurMarkSettings(new FurMarkSettings());
        _settingsReady = true;
        UpdateDxt5Availability();
        SaveFurMarkSettings();
        Log("FurMark 参数已恢复默认");
    }

    private void FurMarkSetting_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsReady) return;
        if (ReferenceEquals(sender, ApiBox) && ApiBox.SelectedIndex is 0 or 2)
            StressGpuBox.SelectedIndex = 0;
        UpdateDxt5Availability();
        SaveFurMarkSettings();
    }

    private void FurMarkToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady) return;
        SaveFurMarkSettings();
    }
    private void UpdateDxt5Availability()
    {
        if (ApiBox is null || Dxt5Toggle is null) return;
        var isGl = ApiBox.SelectedIndex is 0 or 2;
        Dxt5Toggle.IsEnabled = isGl;
        GlCoreToggle.IsEnabled = isGl;
        if (!isGl && Dxt5Toggle.IsOn) Dxt5Toggle.IsOn = false;
        if (!isGl && GlCoreToggle.IsOn) GlCoreToggle.IsOn = false;
    }

    private FurMarkGpuInfo? SelectedStressGpu => (StressGpuBox.SelectedItem as ComboBoxItem)?.Tag as FurMarkGpuInfo;

    private async Task LoadGpuChoicesAsync()
    {
        var monitorTask = Task.Run(LiteMonitorService.GetGpuMonitorDevices);
        var stressTask = Task.Run(PerformanceBenchmarkService.GetFurMarkVulkanDevices);
        try
        {
            foreach (var device in await monitorTask)
            {
                if (MonitorGpuBox.Items.OfType<ComboBoxItem>().Any(item => Equals(item.Tag, device.Id))) continue;
                MonitorGpuBox.Items.Add(new ComboBoxItem { Content = $"{device.Name} ({device.Id})", Tag = device.Id });
            }
        }
        catch (Exception ex) { Log($"无法枚举显卡传感器：{ex.Message}"); }

        try
        {
            foreach (var gpu in await stressTask)
                StressGpuBox.Items.Add(new ComboBoxItem { Content = $"{gpu.Name} (Vulkan #{gpu.Index})", Tag = gpu });
            if (!_isRunning && ApiBox.SelectedIndex is 1 or 3)
            {
                var saved = StressGpuBox.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => item.Tag is FurMarkGpuInfo gpu && gpu.Index == _savedGpuIndex);
                if (saved is not null) StressGpuBox.SelectedItem = saved;
            }
            if (StressGpuBox.Items.Count == 1)
                Log("未检测到可指定的 Vulkan 显卡。请确认 FurMark 已安装且显卡驱动支持 Vulkan。");
        }
        catch (Exception ex) { Log($"无法枚举 Vulkan 显卡：{ex.Message}"); }
    }

    private void StressGpu_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsReady || StressGpuBox.SelectedItem is null) return;
        _savedGpuIndex = SelectedStressGpu?.Index;
        if (SelectedStressGpu is not null && ApiBox.SelectedIndex is 0 or 2)
            ApiBox.SelectedIndex++;
        SaveFurMarkSettings();
    }

    private void SelectRunMonitorGpu(bool gpuStress)
    {
        var item = MonitorGpuBox.SelectedItem as ComboBoxItem;
        _runMonitorGpuId = item?.Tag as string;
        _runMonitorGpuLabel = _runMonitorGpuId is not null ? item?.Content?.ToString() : null;
        if (_runMonitorGpuId is not null || !gpuStress) return;
        var gpu = SelectedStressGpu;
        if (gpu is null && ApiBox.SelectedIndex is 1 or 3)
            gpu = StressGpuBox.Items.OfType<ComboBoxItem>().Select(choice => choice.Tag as FurMarkGpuInfo)
                .FirstOrDefault(device => device?.Index == (_savedGpuIndex ?? 0));
        if (gpu is null) return;

        var matches = MonitorGpuBox.Items.OfType<ComboBoxItem>()
            .Where(choice => choice.Tag is string &&
                (choice.Content as string)?.StartsWith(gpu.Name + " (", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        if (matches.Length == 1)
        {
            _runMonitorGpuId = (string)matches[0].Tag;
            _runMonitorGpuLabel = gpu.Name;
        }
        else
        {
            // 不把其他显卡的读数误认成烤机显卡。
            _runMonitorGpuId = "";
            _runMonitorGpuLabel = $"{gpu.Name}（未匹配传感器）";
            Log("未能唯一匹配烤机显卡的传感器。可停止烤机后手动选择监测显卡。");
        }
    }

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

        if (api.EndsWith("-vk", StringComparison.Ordinal) && SelectedStressGpu is { } gpu)
            args.Add($"--gpu-index {gpu.Index}");
        else if (api.EndsWith("-vk", StringComparison.Ordinal) && _savedGpuIndex > 0 && StressGpuBox.Items.Count == 1)
            args.Add($"--gpu-index {_savedGpuIndex}");

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

    private void CpuGpuStress_Click(object sender, RoutedEventArgs e)
    {
        CpuSelBox.IsChecked = true;
        GpuSelBox.IsChecked = true;
        StartStress();
    }

    private bool CpuSelected => CpuSelBox.IsChecked == true;
    private bool GpuSelected => GpuSelBox.IsChecked == true;

    public void StartStress(int? durationMinutes = null)
    {
        if (_isRunning || !StartStressBtn.IsEnabled) return;

        var cpuSel = CpuSelected;
        var gpuSel = GpuSelected;
        if (!cpuSel && !gpuSel)
        {
            Log("请至少勾选一项烤机项目（CPU / GPU）");
            return;
        }
        _modeName = BuildModeName(cpuSel, gpuSel);
        SelectRunMonitorGpu(gpuSel);
        GpuTempText.Text = GpuClockText.Text = GpuPowerText.Text = "--";
        if (_inspectionGpuLoad is { } load) load.Text = "--";
        if (_runMonitorGpuLabel is { } gpuLabel) Log($"监测显卡：{gpuLabel}");

        _isRunning = true;
        if (_inspectionMode) _ = OpenInspectionHwInfoAsync(onlyWhileRunning: true);
        _monitorRetryCount = 0;
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
        SetButtonsEnabled(false);
        StopBtn.IsEnabled = true;
        ExportBtn.IsEnabled = false;

        var cpuDuration = durationMinutes ?? (int)CpuDurationBox.Value;
        var gpuDuration = durationMinutes ?? (int)GpuDurationBox.Value;
        _targetMinutes = Math.Max(cpuSel ? cpuDuration : 0, gpuSel ? gpuDuration : 0);

        var started = new List<string>();
        if (cpuSel) { started.Add("CPU (Prime95)"); StartPrime95(cpuDuration); }
        if (gpuSel && StartFurMarkGpuStress(gpuDuration)) started.Add("GPU (FurMark)");
        if (started.Count == 0)
        {
            StopStress();
            return;
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
        var timer = _monitorTimer;
        var gpuId = _runMonitorGpuId;
        var sample = await Task.Run(() => gpuId is null
            ? LiteMonitorService.Instance.Read()
            : LiteMonitorService.Instance.ReadForGpu(gpuId));
        if (!_isRunning || !ReferenceEquals(timer, _monitorTimer)) return;
        if (_inspectionMode) InspectionSampled?.Invoke(sample);

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

        if (_inspectionGpuLoad is { } load) load.Text = sample.GpuLoad >= 0 ? $"{sample.GpuLoad:0}%" : "--";
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

            var dialog = new TubaWinUi3.Controls.FluentContentDialog
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
        if (_sampleCount == 0) return;

        var elapsed = DateTime.UtcNow - _startTime;
        var modeStr = _modeName;

        var cpuTempAvg = Avg(_cpuTempSum, _cpuTempCount); var cpuUsageAvg = Avg(_cpuUsageSum, _cpuUsageCount); var cpuClockAvg = Avg(_cpuClockSum, _cpuClockCount); var cpuPowerAvg = Avg(_cpuPowerSum, _cpuPowerCount);
        var gpuTempAvg = Avg(_gpuTempSum, _gpuTempCount); var gpuClockAvg = Avg(_gpuClockSum, _gpuClockCount); var gpuPowerAvg = Avg(_gpuPowerSum, _gpuPowerCount);

        var html = GenerateReportHtml(modeStr, elapsed, cpuTempAvg, cpuUsageAvg, cpuClockAvg, cpuPowerAvg, gpuTempAvg, gpuClockAvg, gpuPowerAvg);

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
        double gpuTempAvg, double gpuClockAvg, double gpuPowerAvg)
    {
        var cpuTempPeak = Peak(_cpuTempPeak); var cpuUsagePeak = Peak(_cpuUsagePeak); var cpuClockPeak = Peak(_cpuClockPeak); var cpuPowerPeak = Peak(_cpuPowerPeak);
        var gpuTempPeak = Peak(_gpuTempPeak); var gpuClockPeak = Peak(_gpuClockPeak); var gpuPowerPeak = Peak(_gpuPowerPeak);

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
<h2 style=""font-size:16px;font-weight:600;margin-bottom:12px"">GPU 传感器 · {System.Net.WebUtility.HtmlEncode(_runMonitorGpuLabel ?? "自动")}</h2>
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

        var cores = HardwareInfoService.GetPhysicalCoreCount();
        if (cores <= 0) cores = Math.Max(1, Environment.ProcessorCount / 2);
        var memoryMb = HardwareInfoService.GetInstalledMemoryBytes() / 1048576;
        if (memoryMb <= 0) memoryMb = 8192;

        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TortureCores"] = cores.ToString(),
            ["TortureHyperthreading"] = "1",
            ["MinTortureFFT"] = "4",
            ["MaxTortureFFT"] = "4096",
            ["TortureMem"] = (memoryMb >= 8192 ? 1024 : memoryMb >= 4096 ? 512 : 128).ToString(),
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

    private bool StartFurMarkGpuStress(int minutes)
    {
        var furmarkExe = PerformanceBenchmarkService.FindFurMarkExe();
        if (furmarkExe is null) { Log("未找到 FurMark (烤鸡工具/FurMark_win64/furmark.exe)"); return false; }
        try
        {
            var arguments = BuildFurMarkArgs(minutes);
            furmarkExe = FurMarkLaunchPath.Prepare(furmarkExe, Path.GetDirectoryName(furmarkExe)).ExePath;
            _furmarkProcess = Process.Start(new ProcessStartInfo { FileName = furmarkExe, Arguments = arguments, UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(furmarkExe) });
            if (_furmarkProcess is null) { Log("FurMark 未能启动"); return false; }
            Log($"FurMark 已启动 (GPU 压力测试, {minutes} 分钟)");
            Log($"FurMark 参数: {arguments}");
            return true;
        }
        catch (Exception ex) { Log($"FurMark 启动失败: {ex.Message}"); return false; }
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
    /// 在 Program Files 下碰到拒绝访问的目录（如系统 ReparsePoint「Windows NT\附件」）会抛 UnauthorizedAccessException，导致烤机直接报错。</remarks>
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

    private static string BuildModeName(bool cpu, bool gpu) =>
        cpu && gpu ? "CPU + GPU 双烤" : cpu ? "CPU 单烤" : "GPU 单烤";

    private void SetButtonsEnabled(bool enabled)
    {
        if (_inspectionDuration is not null) _inspectionDuration.IsEnabled = enabled;
        CpuGpuStressBtn.IsEnabled = enabled;
        CpuSelBox.IsEnabled = enabled;
        GpuSelBox.IsEnabled = enabled;
        StressGpuBox.IsEnabled = MonitorGpuBox.IsEnabled = MsaaBox.IsEnabled = ApiBox.IsEnabled = enabled;
        ResetFurMarkSettingsButton.IsEnabled = enabled;
        StartStressBtn.IsEnabled = enabled;
    }

    // 与游戏监控同理：界面日志关掉就没了，烤机读不到传感器时得留下证据
    private void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        LogText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = line + "\n" });
        while (LogText.Inlines.Count > MaxLogLines)
            LogText.Inlines.RemoveAt(0);
        LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
        LogService.Info(LogService.StressTest, msg);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogText.Inlines.Clear();
}
