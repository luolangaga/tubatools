using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TubaWinUi3.Services;
using System.Runtime.InteropServices;
using System.Linq;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using static TubaWinUi3.Services.ConfigManager;

namespace TubaWinUi3.Pages;

public sealed class PopupSettings
{
    public bool ShowCpu = true, ShowGpu = true, ShowFps = true, ShowMem = true, ShowDisk = true, ShowNet = true, ShowBat = true;
    public bool ShowCpuChart = true, ShowCpuTemp = true, ShowCpuClock = true, ShowCpuPower = true;
    public bool ShowGpuChart = true, ShowGpuTemp = true, ShowGpuClock = true, ShowGpuPower = true;
    public bool ShowFpsChart = true;
    public bool ShowMemChart = false, ShowMemUsed = true;
    public bool ShowDiskChart = false, ShowDiskRead = true, ShowDiskWrite = true;
    public bool ShowNetChart = false, ShowNetUp = true, ShowNetDown = true;
    public byte Opacity = 240;
    public bool Topmost = true;

    public static PopupSettings Load()
    {
        var s = new PopupSettings();
        try
        {
            var path = ConfigManager.GetPopupSettingsPath();
            if (!System.IO.File.Exists(path)) return s;
            var json = System.IO.File.ReadAllText(path);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("show_cpu", out var p)) s.ShowCpu = p.GetBoolean();
            if (root.TryGetProperty("show_gpu", out p)) s.ShowGpu = p.GetBoolean();
            if (root.TryGetProperty("show_fps", out p)) s.ShowFps = p.GetBoolean();
            if (root.TryGetProperty("show_mem", out p)) s.ShowMem = p.GetBoolean();
            if (root.TryGetProperty("show_disk", out p)) s.ShowDisk = p.GetBoolean();
            if (root.TryGetProperty("show_net", out p)) s.ShowNet = p.GetBoolean();
            if (root.TryGetProperty("show_bat", out p)) s.ShowBat = p.GetBoolean();
            if (root.TryGetProperty("show_cpu_chart", out p)) s.ShowCpuChart = p.GetBoolean();
            if (root.TryGetProperty("show_cpu_temp", out p)) s.ShowCpuTemp = p.GetBoolean();
            if (root.TryGetProperty("show_cpu_clock", out p)) s.ShowCpuClock = p.GetBoolean();
            if (root.TryGetProperty("show_cpu_power", out p)) s.ShowCpuPower = p.GetBoolean();
            if (root.TryGetProperty("show_gpu_chart", out p)) s.ShowGpuChart = p.GetBoolean();
            if (root.TryGetProperty("show_gpu_temp", out p)) s.ShowGpuTemp = p.GetBoolean();
            if (root.TryGetProperty("show_gpu_clock", out p)) s.ShowGpuClock = p.GetBoolean();
            if (root.TryGetProperty("show_gpu_power", out p)) s.ShowGpuPower = p.GetBoolean();
            if (root.TryGetProperty("show_fps_chart", out p)) s.ShowFpsChart = p.GetBoolean();
            if (root.TryGetProperty("show_mem_chart", out p)) s.ShowMemChart = p.GetBoolean();
            if (root.TryGetProperty("show_mem_used", out p)) s.ShowMemUsed = p.GetBoolean();
            if (root.TryGetProperty("show_disk_chart", out p)) s.ShowDiskChart = p.GetBoolean();
            if (root.TryGetProperty("show_disk_read", out p)) s.ShowDiskRead = p.GetBoolean();
            if (root.TryGetProperty("show_disk_write", out p)) s.ShowDiskWrite = p.GetBoolean();
            if (root.TryGetProperty("show_net_chart", out p)) s.ShowNetChart = p.GetBoolean();
            if (root.TryGetProperty("show_net_up", out p)) s.ShowNetUp = p.GetBoolean();
            if (root.TryGetProperty("show_net_down", out p)) s.ShowNetDown = p.GetBoolean();
            if (root.TryGetProperty("opacity", out p)) s.Opacity = p.GetByte();
            if (root.TryGetProperty("topmost", out p)) s.Topmost = p.GetBoolean();
        }
        catch { }
        return s;
    }

    public void Save()
    {
        try
        {
            var dir = ConfigManager.GetDataDir();
            System.IO.Directory.CreateDirectory(dir);
            var path = ConfigManager.GetPopupSettingsPath();
            using var ms = new System.IO.MemoryStream();
            using var writer = new System.Text.Json.Utf8JsonWriter(ms);
            writer.WriteStartObject();
            writer.WriteBoolean("show_cpu", ShowCpu);
            writer.WriteBoolean("show_gpu", ShowGpu);
            writer.WriteBoolean("show_fps", ShowFps);
            writer.WriteBoolean("show_mem", ShowMem);
            writer.WriteBoolean("show_disk", ShowDisk);
            writer.WriteBoolean("show_net", ShowNet);
            writer.WriteBoolean("show_bat", ShowBat);
            writer.WriteBoolean("show_cpu_chart", ShowCpuChart);
            writer.WriteBoolean("show_cpu_temp", ShowCpuTemp);
            writer.WriteBoolean("show_cpu_clock", ShowCpuClock);
            writer.WriteBoolean("show_cpu_power", ShowCpuPower);
            writer.WriteBoolean("show_gpu_chart", ShowGpuChart);
            writer.WriteBoolean("show_gpu_temp", ShowGpuTemp);
            writer.WriteBoolean("show_gpu_clock", ShowGpuClock);
            writer.WriteBoolean("show_gpu_power", ShowGpuPower);
            writer.WriteBoolean("show_fps_chart", ShowFpsChart);
            writer.WriteBoolean("show_mem_chart", ShowMemChart);
            writer.WriteBoolean("show_mem_used", ShowMemUsed);
            writer.WriteBoolean("show_disk_chart", ShowDiskChart);
            writer.WriteBoolean("show_disk_read", ShowDiskRead);
            writer.WriteBoolean("show_disk_write", ShowDiskWrite);
            writer.WriteBoolean("show_net_chart", ShowNetChart);
            writer.WriteBoolean("show_net_up", ShowNetUp);
            writer.WriteBoolean("show_net_down", ShowNetDown);
            writer.WriteNumber("opacity", Opacity);
            writer.WriteBoolean("topmost", Topmost);
            writer.WriteEndObject();
            writer.Flush();
            System.IO.File.WriteAllText(path, System.Text.Encoding.UTF8.GetString(ms.ToArray()));
        }
        catch { }
    }
}

public sealed partial class LiteMonitorPage : Page
{
    private const int MaxHistory = 60;
    private readonly List<float> _cpuLoadH = [], _gpuLoadH = [], _fpsH = [];
    private DispatcherTimer? _timer;
    private readonly double[] _intervals = [0.5, 1, 2, 5];
    private readonly LiteMonitorService _svc = LiteMonitorService.Instance;

    private TextBlock _cpuLoad = null!, _cpuTemp = null!, _cpuClock = null!, _cpuPower = null!;
    private TextBlock _gpuLoad = null!, _gpuTemp = null!, _gpuClock = null!, _gpuPower = null!;
    private TextBlock _memLoad = null!, _memUsed = null!;
    private TextBlock _diskRead = null!, _diskWrite = null!;
    private TextBlock _netUp = null!, _netDown = null!;
    private TextBlock _fpsVal = null!, _fpsProc = null!;
    private TextBlock _batPct = null!, _batPow = null!;
    private Canvas _cpuChart = null!, _gpuChart = null!, _fpsChart = null!;

    private static readonly Color CpuAccent = Color.FromArgb(255, 66, 133, 244);
    private static readonly Color GpuAccent = Color.FromArgb(255, 234, 67, 53);
    private static readonly Color MemAccent = Color.FromArgb(255, 52, 168, 83);
    private static readonly Color DiskAccent = Color.FromArgb(255, 251, 188, 4);
    private static readonly Color NetAccent = Color.FromArgb(255, 103, 58, 183);
    private static readonly Color FpsAccent = Color.FromArgb(255, 0, 172, 238);
    private static readonly Color BatAccent = Color.FromArgb(255, 76, 175, 80);

    public LiteMonitorPage()
    {
        InitializeComponent();
        InitCards();
        StartTimer(TimeSpan.FromSeconds(1));
    }



    private void InitCards()
    {
        CpuCard.Child = BuildCard("处理器", "\uE950", CpuAccent, out _cpuLoad, out _cpuTemp, out _cpuClock, out _cpuPower, out _cpuChart);
        GpuCard.Child = BuildCard("显卡", "\uE7F4", GpuAccent, out _gpuLoad, out _gpuTemp, out _gpuClock, out _gpuPower, out _gpuChart);
        MemCard.Child = BuildMiniCard("内存", "\uE965", MemAccent, out _memLoad, out _memUsed);
        DiskCard.Child = BuildMiniCard("磁盘", "\uEDA2", DiskAccent, out _diskRead, out _diskWrite);
        NetCard.Child = BuildMiniCard("网络", "\uE968", NetAccent, out _netUp, out _netDown);
        FpsCard.Child = BuildCard("帧率", "\uE7FC", FpsAccent, out _fpsVal, out _fpsProc, out _, out _, out _fpsChart);
        BatCard.Child = BuildMiniCard("电池", "\uE85A", BatAccent, out _batPct, out _batPow);
    }

    private void StartTimer(TimeSpan interval)
    {
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += OnTick;
        RefreshCombo.SelectionChanged += (_, _) =>
        {
            if (_timer != null) _timer.Interval = TimeSpan.FromSeconds(_intervals[RefreshCombo.SelectedIndex]);
        };
        _timer.Start();
    }

    private int _fpsComboUpdateTick;

    private void OnTick(object? sender, object e)
    {
        var fpsOn = FpsToggle.IsChecked == true;
        var s = _svc.Read(fpsOn);

        UpdateUI(s, fpsOn);
    }

    private void UpdateUI(MonitorSample s, bool fpsOn)
    {

        Set(_cpuLoad, $"{s.CpuLoad:0}%"); Set(_cpuTemp, s.CpuTemp >= 0 ? $"{s.CpuTemp:0}°C" : "");
        Set(_cpuClock, s.CpuClock > 0 ? $"{s.CpuClock / 1000f:0.0} GHz" : "");
        Set(_cpuPower, s.CpuPower > 0 ? $"{s.CpuPower:0.0} W" : "");
        Set(_gpuLoad, $"{s.GpuLoad:0}%"); Set(_gpuTemp, s.GpuTemp >= 0 ? $"{s.GpuTemp:0}°C" : "");
        Set(_gpuClock, s.GpuClock > 0 ? $"{s.GpuClock:0} MHz" : "");
        Set(_gpuPower, s.GpuPower > 0 ? $"{s.GpuPower:0.0} W" : "");
        Set(_memLoad, $"{s.MemLoad:0}%");
        Set(_memUsed, s.MemUsedGB >= 0 ? $"{s.MemUsedGB:F1} / {s.MemTotalGB:F1} GB" : "");
        Set(_diskRead, s.DiskReadMBs >= 0 ? $"读 {s.DiskReadMBs:F1} MB/s" : "读 --");
        Set(_diskWrite, s.DiskWriteMBs >= 0 ? $"写 {s.DiskWriteMBs:F1} MB/s" : "写 --");
        Set(_netUp, s.NetUpMBs >= 0 ? $"↑ {s.NetUpMBs:F2} MB/s" : "↑ --");
        Set(_netDown, s.NetDownMBs >= 0 ? $"↓ {s.NetDownMBs:F2} MB/s" : "↓ --");
        Set(_fpsVal, s.Fps >= 0 ? $"{(int)s.Fps}" : "--");
        Set(_fpsProc, string.IsNullOrEmpty(s.FpsProcess) ? "" : s.FpsProcess);
        Set(_batPct, s.BatPercent >= 0 ? $"{s.BatPercent:0}%" : "未检测到");
        Set(_batPow, s.BatPower > 0 ? (s.BatCharging ? $"充电 {s.BatPower:0.0}W" : $"放电 {s.BatPower:0.0}W") : (s.BatPercent >= 0 ? "已接电源" : ""));

        if (fpsOn)
        {
            _fpsComboUpdateTick++;
            if (_fpsComboUpdateTick % 5 == 0) UpdateFpsProcessCombo();
        }

        AddHistory(_cpuLoadH, s.CpuLoad); AddHistory(_gpuLoadH, s.GpuLoad); AddHistory(_fpsH, s.Fps);
        DrawSparkline(_cpuChart, _cpuLoadH, CpuAccent, 0, 100);
        DrawSparkline(_gpuChart, _gpuLoadH, GpuAccent, 0, 100);
        DrawSparkline(_fpsChart, _fpsH, FpsAccent, 0, Math.Max(144, _fpsH.Count > 0 ? _fpsH.Max() * 1.2f : 144));
    }

    private bool _fpsComboUpdating;
    private string _fpsComboLastSnapshot = "";

    private void UpdateFpsProcessCombo()
    {
        var processes = _svc.GetFpsProcessList();
        var snapshot = string.Join("|", processes.Select(p => $"{p.pid}:{p.name}"));
        if (snapshot == _fpsComboLastSnapshot) return;
        _fpsComboLastSnapshot = snapshot;

        _fpsComboUpdating = true;
        try
        {
            var prevSel = FpsProcessCombo.SelectedIndex;

            FpsProcessCombo.Items.Clear();
            FpsProcessCombo.Items.Add("自动选择");
            foreach (var (pid, pname, pfps) in processes)
                FpsProcessCombo.Items.Add($"{pname} ({pid}) - {pfps:0} FPS");

            if (prevSel >= 0 && prevSel < FpsProcessCombo.Items.Count)
                FpsProcessCombo.SelectedIndex = prevSel;
            else
                FpsProcessCombo.SelectedIndex = 0;
        }
        finally { _fpsComboUpdating = false; }
    }

    private void FpsProcessCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fpsComboUpdating) return;
        if (FpsProcessCombo.SelectedIndex <= 0)
            _svc.ClearFpsFocus();
        else
        {
            var processes = _svc.GetFpsProcessList();
            var idx = FpsProcessCombo.SelectedIndex - 1;
            if (idx >= 0 && idx < processes.Count)
                _svc.SetFpsFocus(processes[idx].pid);
        }
    }

    private static void Set(TextBlock tb, string v) => tb.Text = v;

    private static void AddHistory(List<float> h, float v)
    {
        if (v < 0) return;
        h.Add(v);
        if (h.Count > MaxHistory) h.RemoveAt(0);
    }

    private async void FpsToggle_Click(object sender, RoutedEventArgs e)
    {
        if (FpsToggle.IsChecked != true) { FpsProcessCombo.IsEnabled = false; _svc.ClearFpsFocus(); return; }
        FpsToggle.IsEnabled = false;
        try
        {
            if (!IsRunningAsAdmin())
            {
                FpsToggle.IsChecked = false;
                FpsProcessCombo.IsEnabled = false;
                var tip = new ContentDialog
                {
                    Title = "需要管理员权限",
                    Content = "FPS 帧率检测需要以管理员身份运行程序。\n请关闭本程序，右键选择「以管理员身份运行」后再试。",
                    CloseButtonText = "知道了",
                    XamlRoot = XamlRoot,
                    RequestedTheme = ThemeService.CurrentElementTheme
                };
                await tip.ShowAsync();
                return;
            }

            var ok = await _svc.EnsureFpsComponentAsync(XamlRoot);
            if (!ok)
            {
                FpsToggle.IsChecked = false;
                FpsProcessCombo.IsEnabled = false;
            }
            else
            {
                FpsProcessCombo.IsEnabled = true;
            }
        }
        catch
        {
            FpsToggle.IsChecked = false;
            FpsProcessCombo.IsEnabled = false;
        }
        finally
        {
            FpsToggle.IsEnabled = true;
        }
    }

    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private void PopupSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var settings = PopupSettings.Load();
        var panel = new StackPanel { Spacing = 4 };

        StackPanel AddGroup(string title)
        {
            var header = new TextBlock
            {
                Text = title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Opacity = 0.8,
                Margin = new Thickness(0, 8, 0, 2)
            };
            panel.Children.Add(header);
            var group = new StackPanel { Spacing = 2, Margin = new Thickness(16, 0, 0, 0) };
            panel.Children.Add(group);
            return group;
        }

        var gCpu = AddGroup("CPU 处理器");
        var showCpu = new CheckBox { Content = "显示", IsChecked = settings.ShowCpu };
        var showCpuChart = new CheckBox { Content = "曲线图", IsChecked = settings.ShowCpuChart, Margin = new Thickness(16, 0, 0, 0) };
        var showCpuTemp = new CheckBox { Content = "温度", IsChecked = settings.ShowCpuTemp, Margin = new Thickness(16, 0, 0, 0) };
        var showCpuClock = new CheckBox { Content = "频率", IsChecked = settings.ShowCpuClock, Margin = new Thickness(16, 0, 0, 0) };
        var showCpuPower = new CheckBox { Content = "功耗", IsChecked = settings.ShowCpuPower, Margin = new Thickness(16, 0, 0, 0) };
        gCpu.Children.Add(showCpu);
        gCpu.Children.Add(showCpuChart);
        gCpu.Children.Add(showCpuTemp);
        gCpu.Children.Add(showCpuClock);
        gCpu.Children.Add(showCpuPower);

        var gGpu = AddGroup("GPU 显卡");
        var showGpu = new CheckBox { Content = "显示", IsChecked = settings.ShowGpu };
        var showGpuChart = new CheckBox { Content = "曲线图", IsChecked = settings.ShowGpuChart, Margin = new Thickness(16, 0, 0, 0) };
        var showGpuTemp = new CheckBox { Content = "温度", IsChecked = settings.ShowGpuTemp, Margin = new Thickness(16, 0, 0, 0) };
        var showGpuClock = new CheckBox { Content = "频率", IsChecked = settings.ShowGpuClock, Margin = new Thickness(16, 0, 0, 0) };
        var showGpuPower = new CheckBox { Content = "功耗", IsChecked = settings.ShowGpuPower, Margin = new Thickness(16, 0, 0, 0) };
        gGpu.Children.Add(showGpu);
        gGpu.Children.Add(showGpuChart);
        gGpu.Children.Add(showGpuTemp);
        gGpu.Children.Add(showGpuClock);
        gGpu.Children.Add(showGpuPower);

        var gFps = AddGroup("FPS 帧率");
        var showFps = new CheckBox { Content = "显示", IsChecked = settings.ShowFps };
        var showFpsChart = new CheckBox { Content = "曲线图", IsChecked = settings.ShowFpsChart, Margin = new Thickness(16, 0, 0, 0) };
        gFps.Children.Add(showFps);
        gFps.Children.Add(showFpsChart);

        var gMem = AddGroup("MEM 内存");
        var showMem = new CheckBox { Content = "显示", IsChecked = settings.ShowMem };
        var showMemChart = new CheckBox { Content = "曲线图", IsChecked = settings.ShowMemChart, Margin = new Thickness(16, 0, 0, 0) };
        var showMemUsed = new CheckBox { Content = "已用/总量", IsChecked = settings.ShowMemUsed, Margin = new Thickness(16, 0, 0, 0) };
        gMem.Children.Add(showMem);
        gMem.Children.Add(showMemChart);
        gMem.Children.Add(showMemUsed);

        var gDisk = AddGroup("磁盘");
        var showDisk = new CheckBox { Content = "显示", IsChecked = settings.ShowDisk };
        var showDiskChart = new CheckBox { Content = "曲线图", IsChecked = settings.ShowDiskChart, Margin = new Thickness(16, 0, 0, 0) };
        var showDiskRead = new CheckBox { Content = "读取速度", IsChecked = settings.ShowDiskRead, Margin = new Thickness(16, 0, 0, 0) };
        var showDiskWrite = new CheckBox { Content = "写入速度", IsChecked = settings.ShowDiskWrite, Margin = new Thickness(16, 0, 0, 0) };
        gDisk.Children.Add(showDisk);
        gDisk.Children.Add(showDiskChart);
        gDisk.Children.Add(showDiskRead);
        gDisk.Children.Add(showDiskWrite);

        var gNet = AddGroup("NET 网络");
        var showNet = new CheckBox { Content = "显示", IsChecked = settings.ShowNet };
        var showNetChart = new CheckBox { Content = "曲线图", IsChecked = settings.ShowNetChart, Margin = new Thickness(16, 0, 0, 0) };
        var showNetUp = new CheckBox { Content = "上传速度", IsChecked = settings.ShowNetUp, Margin = new Thickness(16, 0, 0, 0) };
        var showNetDown = new CheckBox { Content = "下载速度", IsChecked = settings.ShowNetDown, Margin = new Thickness(16, 0, 0, 0) };
        gNet.Children.Add(showNet);
        gNet.Children.Add(showNetChart);
        gNet.Children.Add(showNetUp);
        gNet.Children.Add(showNetDown);

        var gBat = AddGroup("BAT 电池");
        var showBat = new CheckBox { Content = "显示", IsChecked = settings.ShowBat };
        gBat.Children.Add(showBat);

        var gWin = AddGroup("窗口设置");
        var opacitySlider = new Slider { Minimum = 40, Maximum = 255, Value = settings.Opacity, StepFrequency = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
        var opacityLabel = new TextBlock { Text = $"透明度 {settings.Opacity * 100 / 255}%", Opacity = 0.7, FontSize = 12 };
        opacitySlider.ValueChanged += (_, _) => opacityLabel.Text = $"透明度 {(int)opacitySlider.Value * 100 / 255}%";
        gWin.Children.Add(opacitySlider);
        gWin.Children.Add(opacityLabel);
        var topmostCheck = new CheckBox { Content = "默认窗口置顶", IsChecked = settings.Topmost };
        gWin.Children.Add(topmostCheck);

        var dialog = new ContentDialog
        {
            Title = "悬浮窗设置",
            Content = new ScrollViewer { Content = panel },
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        dialog.PrimaryButtonClick += (_, _) =>
        {
            settings.ShowCpu = showCpu.IsChecked == true;
            settings.ShowGpu = showGpu.IsChecked == true;
            settings.ShowFps = showFps.IsChecked == true;
            settings.ShowMem = showMem.IsChecked == true;
            settings.ShowDisk = showDisk.IsChecked == true;
            settings.ShowNet = showNet.IsChecked == true;
            settings.ShowBat = showBat.IsChecked == true;
            settings.ShowCpuChart = showCpuChart.IsChecked == true;
            settings.ShowCpuTemp = showCpuTemp.IsChecked == true;
            settings.ShowCpuClock = showCpuClock.IsChecked == true;
            settings.ShowCpuPower = showCpuPower.IsChecked == true;
            settings.ShowGpuChart = showGpuChart.IsChecked == true;
            settings.ShowGpuTemp = showGpuTemp.IsChecked == true;
            settings.ShowGpuClock = showGpuClock.IsChecked == true;
            settings.ShowGpuPower = showGpuPower.IsChecked == true;
            settings.ShowFpsChart = showFpsChart.IsChecked == true;
            settings.ShowMemChart = showMemChart.IsChecked == true;
            settings.ShowMemUsed = showMemUsed.IsChecked == true;
            settings.ShowDiskChart = showDiskChart.IsChecked == true;
            settings.ShowDiskRead = showDiskRead.IsChecked == true;
            settings.ShowDiskWrite = showDiskWrite.IsChecked == true;
            settings.ShowNetChart = showNetChart.IsChecked == true;
            settings.ShowNetUp = showNetUp.IsChecked == true;
            settings.ShowNetDown = showNetDown.IsChecked == true;
            settings.Opacity = (byte)opacitySlider.Value;
            settings.Topmost = topmostCheck.IsChecked == true;
            settings.Save();
        };

        _ = dialog.ShowAsync();
    }

    private Window? _popupWindow;
    private DispatcherTimer? _popupTimer;

    private void PopupBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_popupWindow != null)
        {
            try { _popupWindow.Activate(); } catch { }
            return;
        }

        var isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        var fgColor = isDark ? Color.FromArgb(255, 240, 240, 240) : Color.FromArgb(255, 30, 30, 30);
        var dimColor = isDark ? Color.FromArgb(255, 150, 150, 150) : Color.FromArgb(255, 100, 100, 100);
        var topmostBtn = new Button
        {
            Content = new FontIcon { FontSize = 12, Glyph = "\uE840" },
            Padding = new Thickness(4),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Foreground = new SolidColorBrush(dimColor)
        };

        var closeBtn = new Button
        {
            Content = new FontIcon { FontSize = 12, Glyph = "\uE711" },
            Padding = new Thickness(4),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Foreground = new SolidColorBrush(dimColor)
        };

        var titleText = new TextBlock { Text = "硬件监控", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(fgColor), VerticalAlignment = VerticalAlignment.Center };

        var topGrid = new Grid();
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.Children.Add(titleText);
        Grid.SetColumn(topmostBtn, 1); topGrid.Children.Add(topmostBtn);
        Grid.SetColumn(closeBtn, 2); topGrid.Children.Add(closeBtn);

        var stack = new StackPanel { Spacing = 4, Padding = new Thickness(10, 4, 10, 10) };
        stack.Children.Add(topGrid);

        var cpuValue = new TextBlock { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(CpuAccent) };
        var gpuValue = new TextBlock { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(GpuAccent) };
        var fpsValue = new TextBlock { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(FpsAccent) };
        var memValue = new TextBlock { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(MemAccent) };
        var diskValue = new TextBlock { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(DiskAccent) };
        var netValue = new TextBlock { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(NetAccent) };
        var batValue = new TextBlock { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(BatAccent) };

        var cpuDetail = new TextBlock { FontSize = 10, Opacity = 0.7, Foreground = new SolidColorBrush(dimColor) };
        var gpuDetail = new TextBlock { FontSize = 10, Opacity = 0.7, Foreground = new SolidColorBrush(dimColor) };
        var memDetail = new TextBlock { FontSize = 10, Opacity = 0.7, Foreground = new SolidColorBrush(dimColor) };
        var diskDetail = new TextBlock { FontSize = 10, Opacity = 0.7, Foreground = new SolidColorBrush(dimColor) };
        var netDetail = new TextBlock { FontSize = 10, Opacity = 0.7, Foreground = new SolidColorBrush(dimColor) };

        var chartBg = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255));
        var cpuChartEl = new Canvas { Width = 180, Height = 28, Background = chartBg };
        var gpuChartEl = new Canvas { Width = 180, Height = 28, Background = chartBg };
        var fpsChartEl = new Canvas { Width = 180, Height = 28, Background = chartBg };
        var memChartEl = new Canvas { Width = 180, Height = 28, Background = chartBg };
        var diskChartEl = new Canvas { Width = 180, Height = 28, Background = chartBg };
        var netChartEl = new Canvas { Width = 180, Height = 28, Background = chartBg };

        var cpuRow = MakePopupRow("\uE950", CpuAccent, "CPU", cpuValue, cpuDetail, cpuChartEl);
        var gpuRow = MakePopupRow("\uE7F4", GpuAccent, "GPU", gpuValue, gpuDetail, gpuChartEl);
        var fpsRow = MakePopupRow("\uE7FC", FpsAccent, "FPS", fpsValue, null, fpsChartEl);
        var memRow = MakePopupRow("\uE965", MemAccent, "MEM", memValue, memDetail, memChartEl);
        var diskRow = MakePopupRow("\uEDA2", DiskAccent, "DISK", diskValue, diskDetail, diskChartEl);
        var netRow = MakePopupRow("\uE968", NetAccent, "NET", netValue, netDetail, netChartEl);
        var batRow = MakePopupRow("\uE85A", BatAccent, "BAT", batValue, null, null);

        stack.Children.Add(cpuRow);
        stack.Children.Add(gpuRow);
        stack.Children.Add(fpsRow);
        stack.Children.Add(memRow);
        stack.Children.Add(diskRow);
        stack.Children.Add(netRow);
        stack.Children.Add(batRow);

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 30, 30, 30)),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
            BorderThickness = new Thickness(1),
            Child = stack
        };

        var window = new Window();
        window.Content = border;
        window.AppWindow.Title = "硬件监控";
        window.AppWindow.Resize(new SizeInt32(320, 460));

        try
        {
            var mainPos = App.MainWindow.AppWindow.Position;
            window.AppWindow.Move(new PointInt32(mainPos.X + 10, mainPos.Y + 10));
        }
        catch { }

        window.AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        window.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        IntPtr popupHwnd = IntPtr.Zero;
        window.Activated += (_, _) =>
        {
            if (popupHwnd == IntPtr.Zero)
            {
                popupHwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            }
        };

        border.PointerPressed += (_, e) =>
        {
            if (popupHwnd == IntPtr.Zero)
                popupHwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (popupHwnd == IntPtr.Zero) return;

            e.Handled = true;
            var pt = e.GetCurrentPoint(border).Position;
            var lo = (int)((ushort)pt.X);
            var hi = (int)((ushort)pt.Y);
            var lParam = (hi << 16) | lo;
            ReleaseCapture();
            PostMessage(popupHwnd, WM_NCLBUTTONDOWN, HTCAPTION, lParam);
        };

        var cpuH = new List<float>();
        var gpuH = new List<float>();
        var fpsH = new List<float>();
        var memH = new List<float>();
        var diskReadH = new List<float>();
        var diskWriteH = new List<float>();
        var netUpH = new List<float>();
        var netDownH = new List<float>();
        var isTopmost = false;
        bool ticking = false;
        PopupSettings? cachedCfg = null;
        int cfgRefreshTick = 0;

        void ApplyTopmost()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var after = isTopmost ? new IntPtr(-1) : new IntPtr(-2);
            SetWindowPos(hwnd, after, 0, 0, 0, 0, 0x0002 | 0x0001 | 0x0010);
            topmostBtn.Foreground = isTopmost ? new SolidColorBrush(CpuAccent) : new SolidColorBrush(dimColor);
        }

        async void PopupTick(object? s2, object e2)
        {
            if (ticking) return;
            ticking = true;
            try
            {
                var fpsOn = FpsToggle.IsChecked == true;
                var sample = _svc.Read(fpsOn);
                cfgRefreshTick++;
                if (cachedCfg == null || cfgRefreshTick % 10 == 0) cachedCfg = PopupSettings.Load();
                var cfg = cachedCfg;
                if (popupHwnd != IntPtr.Zero) ApplyWindowOpacity(popupHwnd, cfg.Opacity);

                cpuRow.Visibility = cfg.ShowCpu ? Visibility.Visible : Visibility.Collapsed;
                gpuRow.Visibility = cfg.ShowGpu ? Visibility.Visible : Visibility.Collapsed;
                fpsRow.Visibility = cfg.ShowFps ? Visibility.Visible : Visibility.Collapsed;
                memRow.Visibility = cfg.ShowMem ? Visibility.Visible : Visibility.Collapsed;
                diskRow.Visibility = cfg.ShowDisk ? Visibility.Visible : Visibility.Collapsed;
                netRow.Visibility = cfg.ShowNet ? Visibility.Visible : Visibility.Collapsed;
                batRow.Visibility = cfg.ShowBat ? Visibility.Visible : Visibility.Collapsed;

                if (cfg.ShowCpu)
                {
                    cpuValue.Text = sample.CpuLoad >= 0 ? $"{sample.CpuLoad:0}%" : "--";
                    var parts = new List<string>();
                    if (cfg.ShowCpuTemp) parts.Add(sample.CpuTemp >= 0 ? $"{sample.CpuTemp:0}°C" : "温度 --");
                    if (cfg.ShowCpuClock) parts.Add(sample.CpuClock > 0 ? $"{sample.CpuClock / 1000f:0.0}GHz" : "频率 --");
                    if (cfg.ShowCpuPower) parts.Add(sample.CpuPower > 0 ? $"{sample.CpuPower:0.0}W" : "功耗 --");
                    cpuDetail.Text = string.Join("  ", parts);
                    if (sample.CpuLoad >= 0) AddHistory(cpuH, sample.CpuLoad);
                    if (cfg.ShowCpuChart) { DrawSparkline(cpuChartEl, cpuH, CpuAccent, 0, 100); cpuChartEl.Visibility = Visibility.Visible; }
                    else cpuChartEl.Visibility = Visibility.Collapsed;
                }
                if (cfg.ShowGpu)
                {
                    gpuValue.Text = sample.GpuLoad >= 0 ? $"{sample.GpuLoad:0}%" : "--";
                    var parts = new List<string>();
                    if (cfg.ShowGpuTemp) parts.Add(sample.GpuTemp >= 0 ? $"{sample.GpuTemp:0}°C" : "温度 --");
                    if (cfg.ShowGpuClock) parts.Add(sample.GpuClock > 0 ? $"{sample.GpuClock:0}MHz" : "频率 --");
                    if (cfg.ShowGpuPower) parts.Add(sample.GpuPower > 0 ? $"{sample.GpuPower:0.0}W" : "功耗 --");
                    gpuDetail.Text = string.Join("  ", parts);
                    if (sample.GpuLoad >= 0) AddHistory(gpuH, sample.GpuLoad);
                    if (cfg.ShowGpuChart) { DrawSparkline(gpuChartEl, gpuH, GpuAccent, 0, 100); gpuChartEl.Visibility = Visibility.Visible; }
                    else gpuChartEl.Visibility = Visibility.Collapsed;
                }
                if (cfg.ShowFps)
                {
                    fpsValue.Text = sample.Fps >= 0 ? $"{(int)sample.Fps}" : "--";
                    if (sample.Fps >= 0) AddHistory(fpsH, sample.Fps);
                    if (cfg.ShowFpsChart) { DrawSparkline(fpsChartEl, fpsH, FpsAccent, 0, Math.Max(144, fpsH.Count > 0 ? fpsH.Max() * 1.2f : 144)); fpsChartEl.Visibility = Visibility.Visible; }
                    else fpsChartEl.Visibility = Visibility.Collapsed;
                }
                if (cfg.ShowMem)
                {
                    memValue.Text = sample.MemLoad >= 0 ? $"{sample.MemLoad:0}%" : "--";
                    var parts = new List<string>();
                    if (cfg.ShowMemUsed) parts.Add(sample.MemUsedGB >= 0 ? $"{sample.MemUsedGB:F1}/{sample.MemTotalGB:F1}GB" : "已用 --");
                    memDetail.Text = string.Join("  ", parts);
                    if (sample.MemLoad >= 0) AddHistory(memH, sample.MemLoad);
                    if (cfg.ShowMemChart) { DrawSparkline(memChartEl, memH, MemAccent, 0, 100); memChartEl.Visibility = Visibility.Visible; }
                    else memChartEl.Visibility = Visibility.Collapsed;
                }
                if (cfg.ShowDisk)
                {
                    var readVal = sample.DiskReadMBs >= 0 ? $"{sample.DiskReadMBs:F0}" : "--";
                    var writeVal = sample.DiskWriteMBs >= 0 ? $"{sample.DiskWriteMBs:F0}" : "--";
                    diskValue.Text = sample.DiskReadMBs >= 0 || sample.DiskWriteMBs >= 0 ? $"R{readVal} W{writeVal}" : "--";
                    var parts = new List<string>();
                    if (cfg.ShowDiskRead) parts.Add(sample.DiskReadMBs >= 0 ? $"读 {sample.DiskReadMBs:F1}MB/s" : "读 --");
                    if (cfg.ShowDiskWrite) parts.Add(sample.DiskWriteMBs >= 0 ? $"写 {sample.DiskWriteMBs:F1}MB/s" : "写 --");
                    diskDetail.Text = string.Join("  ", parts);
                    if (sample.DiskReadMBs >= 0) AddHistory(diskReadH, sample.DiskReadMBs);
                    if (sample.DiskWriteMBs >= 0) AddHistory(diskWriteH, sample.DiskWriteMBs);
                    if (cfg.ShowDiskChart)
                    {
                        var diskMax = Math.Max(50, diskReadH.Count > 0 ? diskReadH.Max() * 1.2f : 50);
                        diskMax = Math.Max(diskMax, diskWriteH.Count > 0 ? diskWriteH.Max() * 1.2f : diskMax);
                        DrawSparkline(diskChartEl, diskReadH, DiskAccent, 0, Math.Max(diskMax, 1));
                        diskChartEl.Visibility = Visibility.Visible;
                    }
                    else diskChartEl.Visibility = Visibility.Collapsed;
                }
                if (cfg.ShowNet)
                {
                    netValue.Text = sample.NetDownMBs >= 0 ? $"↓{sample.NetDownMBs:F1}" : "--";
                    var parts = new List<string>();
                    if (cfg.ShowNetUp) parts.Add(sample.NetUpMBs >= 0 ? $"↑ {sample.NetUpMBs:F2}MB/s" : "↑ --");
                    if (cfg.ShowNetDown) parts.Add(sample.NetDownMBs >= 0 ? $"↓ {sample.NetDownMBs:F2}MB/s" : "↓ --");
                    netDetail.Text = string.Join("  ", parts);
                    if (sample.NetDownMBs >= 0) AddHistory(netDownH, sample.NetDownMBs);
                    if (cfg.ShowNetChart)
                    {
                        var netMax = Math.Max(1, netDownH.Count > 0 ? netDownH.Max() * 1.2f : 1);
                        DrawSparkline(netChartEl, netDownH, NetAccent, 0, netMax);
                        netChartEl.Visibility = Visibility.Visible;
                    }
                    else netChartEl.Visibility = Visibility.Collapsed;
                }
                if (cfg.ShowBat)
                {
                    batValue.Text = sample.BatPercent >= 0 ? $"{sample.BatPercent:0}%" : "--";
                }
            }
            finally { ticking = false; }
        }

        closeBtn.Click += (_, _) =>
        {
            _popupTimer?.Stop();
            _popupTimer = null;
            window.Close();
        };

        topmostBtn.Click += (_, _) =>
        {
            isTopmost = !isTopmost;
            ApplyTopmost();
        };

        var initSettings = PopupSettings.Load();
        if (initSettings.Topmost)
        {
            isTopmost = true;
            window.Activated += (_, _) => ApplyTopmost();
        }

        _popupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_intervals[RefreshCombo.SelectedIndex]) };
        _popupTimer.Tick += PopupTick;
        _popupTimer.Start();
        PopupTick(this, null!);

        window.Closed += (_, _) =>
        {
            _popupTimer?.Stop();
            _popupTimer = null;
            _popupWindow = null;
        };

        _popupWindow = window;
        window.Activate();
    }

    private static StackPanel MakePopupRow(string glyph, Color accent, string label, TextBlock value, TextBlock? detail, Canvas? chart)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        header.Children.Add(new FontIcon { FontSize = 11, Foreground = new SolidColorBrush(accent), Glyph = glyph });
        header.Children.Add(new TextBlock { FontSize = 10, Foreground = new SolidColorBrush(accent), Text = label, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(value);
        if (detail != null) header.Children.Add(detail);

        var row = new StackPanel { Spacing = 1 };
        row.Children.Add(header);
        if (chart != null) row.Children.Add(chart);
        return row;
    }

    private static Border BuildCard(string title, string glyph, Color accent,
        out TextBlock val1, out TextBlock val2, out TextBlock val3, out TextBlock val4, out Canvas chart)
    {
        val1 = new TextBlock { FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(accent) };
        val2 = new TextBlock { FontSize = 12, Opacity = 0.78 };
        val3 = new TextBlock { FontSize = 12, Opacity = 0.78 };
        val4 = new TextBlock { FontSize = 12, Opacity = 0.78 };
        chart = new Canvas { Width = 200, Height = 36, Margin = new Thickness(0, 6, 0, 0) };

        var iconBorder = new Border
        {
            Width = 36, Height = 36,
            Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(8),
            Child = new FontIcon { FontSize = 18, Foreground = new SolidColorBrush(accent), Glyph = glyph }
        };

        var infoStack = new StackPanel { Spacing = 2 };
        infoStack.Children.Add(val2);
        infoStack.Children.Add(val3);
        infoStack.Children.Add(val4);

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(iconBorder);
        Grid.SetColumn(infoStack, 1);
        grid.Children.Add(infoStack);

        var inner = new StackPanel { Spacing = 4 };
        inner.Children.Add(new TextBlock { FontSize = 12, Opacity = 0.6, Text = title });
        inner.Children.Add(val1);
        inner.Children.Add(grid);
        inner.Children.Add(chart);

        return new Border
        {
            Padding = new Thickness(14),
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(30, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = inner
        };
    }

    private static Border BuildMiniCard(string title, string glyph, Color accent,
        out TextBlock val1, out TextBlock val2)
    {
        val1 = new TextBlock { FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(accent) };
        val2 = new TextBlock { FontSize = 12, Opacity = 0.78 };

        var iconBorder = new Border
        {
            Width = 32, Height = 32,
            Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(8),
            Child = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(accent), Glyph = glyph }
        };

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(iconBorder);
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(val1);
        stack.Children.Add(val2);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        var inner = new StackPanel { Spacing = 4 };
        inner.Children.Add(new TextBlock { FontSize = 12, Opacity = 0.6, Text = title });
        inner.Children.Add(grid);

        return new Border
        {
            Padding = new Thickness(14),
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(30, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = inner
        };
    }

    private static readonly Dictionary<Color, SolidColorBrush> _brushCache = [];
    private static readonly Dictionary<(Color, byte), SolidColorBrush> _fillBrushCache = [];

    private static SolidColorBrush GetBrush(Color color)
    {
        if (!_brushCache.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            _brushCache[color] = brush;
        }
        return brush;
    }

    private static SolidColorBrush GetFillBrush(Color color)
    {
        var key = (color, (byte)35);
        if (!_fillBrushCache.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(Color.FromArgb(35, color.R, color.G, color.B));
            _fillBrushCache[key] = brush;
        }
        return brush;
    }

    private static void DrawSparkline(Canvas canvas, List<float> data, Color color, float minVal, float maxVal)
    {
        canvas.Children.Clear();
        if (data.Count < 2) return;

        var w = canvas.Width;
        var h = canvas.Height;
        var range = Math.Max(maxVal - minVal, 0.001f);
        var step = w / Math.Max(data.Count - 1, 1);

        var pc = new PointCollection();
        for (int i = 0; i < data.Count; i++)
        {
            var x = i * step;
            var y = h - ((data[i] - minVal) / range) * h;
            y = Math.Max(0, Math.Min(h, y));
            pc.Add(new Point(x, y));
        }

        if (pc.Count >= 2)
            canvas.Children.Add(new Polyline { Points = pc, Stroke = GetBrush(color), StrokeThickness = 1.5 });

        var fc = new PointCollection();
        for (int i = 0; i < data.Count; i++)
        {
            var x = i * step;
            var y = h - ((data[i] - minVal) / range) * h;
            y = Math.Max(0, Math.Min(h, y));
            fc.Add(new Point(x, y));
        }
        fc.Add(new Point((data.Count - 1) * step, h));
        fc.Add(new Point(0, h));
        canvas.Children.Add(new Polygon { Points = fc, Fill = GetFillBrush(color) });
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, int lParam);

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private static readonly IntPtr HTCAPTION = new(2);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int LWA_ALPHA = 0x02;

    private static void ApplyWindowOpacity(IntPtr hwnd, byte opacity)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
            SetLayeredWindowAttributes(hwnd, 0, opacity, LWA_ALPHA);
        }
        catch { }
    }
}