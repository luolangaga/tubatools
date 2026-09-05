using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SkiaSharp;
using TubaWinUi3.Services;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace TubaWinUi3.Pages;

/// <summary>
/// 原生 WinUI3 网速测试：调用浙大测速节点（speedtest.zju.edu.cn）API，
/// 支持延迟/抖动、多线程并发下载/上传，圆形仪表 + 实时速率曲线的精美界面。
/// </summary>
public sealed partial class SpeedTestPage : Page
{
    // ─── 仪表几何常量（画布 330×330，圆心 (165,150)，270° 表盘） ───
    private const double Cx = 165, Cy = 150, TrackR = 98, NeedleLen = 86;
    private const double DialStartAngle = -135.0; // 指针起始（左下）

    private SpeedTestEngine _engine = new(SpeedTestNodes.Default);
    private readonly Stopwatch _testSw = new();
    private readonly ObservableCollection<ObservablePoint> _dlPts = new();
    private readonly ObservableCollection<ObservablePoint> _ulPts = new();
    private LineSeries<ObservablePoint>? _dlSeries, _ulSeries;

    private CancellationTokenSource? _cts;
    private DispatcherTimer? _animTimer;
    private bool _running;
    private double _lastTickSec;

    private SpeedTestNode _node = SpeedTestNodes.Default;
    private bool _useBytesPerSec;  // false = Mbps（比特），true = MB/s（字节）
    private bool _loadingUi;       // 初始化填充 ComboBox 时屏蔽 SelectionChanged

    private enum Phase { Idle, Ping, Download, Upload, Done, Stopped }
    private Phase _phase = Phase.Idle;
    private double _phaseBaseProgress;
    private double _phaseStartGlobalSec;

    private double _targetValue = double.NaN; // 动画目标读数（NaN = 显示占位）
    private double _displayValue;

    private double _pingMs = double.NaN, _jitterMs = double.NaN;
    private double _dlMbps = double.NaN, _ulMbps = double.NaN;

    // 阶段主题色
    private Color _dlColor, _ulColor, _pingColor;
    private Color _primaryColor, _textSecondary, _trackColor;

    // 仪表动态元素
    private Path? _progressArc;
    private RotateTransform? _needleRot;

    // Chip 状态跟踪（用于主题切换时重绘）
    private readonly HashSet<Border> _doneChips = new();
    private Border? _activeChip;

    // 常用网站连通性列表
    private readonly List<SiteRowView> _siteRows = new();
    private readonly HttpClient _siteHttp = CreateSiteHttp();
    private CancellationTokenSource? _siteCts;
    private bool _siteBusy;

    public SpeedTestPage()
    {
        InitializeComponent();
    }

    // ───────────────────────────── 初始化 / 清理 ─────────────────────────────

    private void SpeedTestPage_Loaded(object sender, RoutedEventArgs e)
    {
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _animTimer.Tick += AnimTimer_Tick;
        _animTimer.Start();
        _lastTickSec = Environment.TickCount64 / 1000.0;

        ActualThemeChanged += (_, _) => RecolorUi();

        ChartInitializer.EnsureConfigured();
        InitColors();
        BuildGauge();
        InitChart();

        // 节点 / 单位下拉框：恢复上次选择
        _loadingUi = true;
        NodeBox.ItemsSource = SpeedTestNodes.All;
        NodeBox.DisplayMemberPath = nameof(SpeedTestNode.Name);
        UnitBox.ItemsSource = new[] { "Mbps", "MB/s" };
        _node = SpeedTestNodes.ById(AppSettings.Get("SpeedTest_Node")) ?? SpeedTestNodes.Default;
        NodeBox.SelectedItem = _node;
        _useBytesPerSec = AppSettings.GetBool("SpeedTest_UnitBytes");
        UnitBox.SelectedIndex = _useBytesPerSec ? 1 : 0;
        _loadingUi = false;

        // 按所选节点重建引擎（同时规避 Unloaded 时已 Dispose 的实例被复用）
        _engine.Dispose();
        _engine = new SpeedTestEngine(_node);

        SetChipsIdle();
        SetButtonReady();
        ResetToIdle();
        BuildSiteList();
        _ = ProbeAllSitesAsync();

        _ = LoadPublicIpAsync();
    }

    private void SpeedTestPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _siteCts?.Cancel();
        _animTimer?.Stop();
        _running = false;
        _engine.Dispose();
        _siteHttp.Dispose();
    }

    private async Task LoadPublicIpAsync()
    {
        try
        {
            IpText.Text = "本机 IP：检测中…";
            using var ipCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var ip = await _engine.GetPublicIpAsync(ipCts.Token);
            IpText.Text = "本机 IP：" + ip;
        }
        catch
        {
            IpText.Text = "本机 IP：--";
        }
    }

    // ───────────────────────────── 节点 / 单位切换 ─────────────────────────────

    private void NodeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi || NodeBox.SelectedItem is not SpeedTestNode node) return;
        _node = node;
        AppSettings.Set("SpeedTest_Node", node.Id);

        // 重建引擎以切换节点；测速进行中下拉框已禁用，这里只会出现在空闲态
        _engine.Dispose();
        _engine = new SpeedTestEngine(node);
        _ = LoadPublicIpAsync();
    }

    private void UnitBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        bool useBytes = UnitBox.SelectedIndex == 1;
        if (useBytes == _useBytesPerSec) return;
        _useBytesPerSec = useBytes;
        AppSettings.Set("SpeedTest_UnitBytes", useBytes);
        RefreshUnitTexts();
    }

    /// <summary>内部速率统一为 Mbps，展示按所选单位换算（1 MB/s = 8 Mbps）。</summary>
    private string UnitLabel => _useBytesPerSec ? "MB/s" : "Mbps";

    private string FmtSpeed(double mbps) => FmtValue(_useBytesPerSec ? mbps / 8.0 : mbps);

    private double ChartPeakMbps() => Math.Max(
        _dlPts.Count > 0 ? _dlPts.Max(s => s.Y) ?? 0 : 0,
        _ulPts.Count > 0 ? _ulPts.Max(s => s.Y) ?? 0 : 0);

    /// <summary>切换单位后刷新所有已展示的速率文本（指标卡 / 仪表 / 结果横幅 / 图表刻度与提示）。</summary>
    private void RefreshUnitTexts()
    {
        RebuildChartTheme(); // 纵轴刻度随单位换算
        DlValue.Text = FmtSpeed(_dlMbps);
        UlValue.Text = FmtSpeed(_ulMbps);
        DlUnitText.Text = UlUnitText.Text = UnitLabel;
        if (_phase != Phase.Ping) UnitText.Text = UnitLabel;

        if (ResultBanner.Visibility == Visibility.Visible)
        {
            var (title, _, comment) = Evaluate(_dlMbps, _pingMs);
            ResultTitleText.Text = "网络状况：" + title;
            ResultDetailText.Text = BuildResultDetail(comment);
        }

        ChartHint.Text = _phase == Phase.Done
            ? "峰值速率 " + FmtSpeed(ChartPeakMbps()) + " " + UnitLabel + " · 图表由 LiveCharts 渲染，纵轴自动缩放"
            : "开始测速后，下载 / 上传实时速率将在此绘制（" + UnitLabel + "）";
    }

    private string BuildResultDetail(string comment)
    {
        var text = $"下载 {FmtSpeed(_dlMbps)} {UnitLabel} · 上传 {FmtSpeed(_ulMbps)} {UnitLabel}" +
                   $" · 延迟 {FmtValue(_pingMs)} ms · 抖动 {FmtValue(_jitterMs)} ms";
        if (comment.Length > 0) text += "　" + comment;
        return text;
    }

    // ───────────────────────────── 颜色 / 重绘 ─────────────────────────────

    private void InitColors()
    {
        _dlColor = ColorRes("SystemFillColorSuccessBrush", Color.FromArgb(255, 22, 163, 74));
        _ulColor = Color.FromArgb(255, 139, 92, 246); // 品牌紫，两套主题下均可读
        _pingColor = ColorRes("SystemFillColorCautionBrush", Color.FromArgb(255, 234, 160, 0));
        _primaryColor = ColorRes("TextFillColorPrimaryBrush", Color.FromArgb(255, 30, 30, 30));
        _textSecondary = ColorRes("TextFillColorSecondaryBrush", Color.FromArgb(255, 90, 90, 90));
        _trackColor = ColorRes("ControlStrokeColorDefaultBrush", Color.FromArgb(255, 190, 190, 190));

        StyleStatIcon(DlIconBg, "\uE896", _dlColor);
        StyleStatIcon(UlIconBg, "\uE898", _ulColor);
        StyleStatIcon(PingIconBg, "\uE823", _pingColor);
        StyleStatIcon(JitIconBg, "\uE81E", ColorRes("SystemAccentColor", Color.FromArgb(255, 0, 120, 212)));
        DlDot.Fill = new SolidColorBrush(_dlColor);
        UlDot.Fill = new SolidColorBrush(_ulColor);
        IpIcon.Foreground = BrushRes("TextFillColorSecondaryBrush", _textSecondary);
    }

    private void RecolorUi()
    {
        InitColors();
        BuildGauge();
        RebuildChartTheme();
        ReapplyChips();
        RecolorSiteRows();
    }

    private void ResetToIdle()
    {
        _phase = Phase.Idle;
        _targetValue = double.NaN;
        _displayValue = 0;
        UnitText.Text = UnitLabel;
        StageText.Text = "准备就绪";
        StatusText.Text = "点击下方按钮开始测速，完整测试约需 20~25 秒";
        PhaseBar.Value = 0;
        PctText.Text = "0%";
        ErrorBar.IsOpen = false;
        ResultBanner.Visibility = Visibility.Collapsed;
        _pingMs = _jitterMs = _dlMbps = _ulMbps = double.NaN;
        DlValue.Text = UlValue.Text = PingValue.Text = JitValue.Text = "--";
        DlUnitText.Text = UlUnitText.Text = UnitLabel;
        ChartHint.Text = "开始测速后，下载 / 上传实时速率将在此绘制（" + UnitLabel + "）";
        ValueText.Text = "--";
    }

    // ───────────────────────────── 仪表盘绘制 ─────────────────────────────

    private void BuildGauge()
    {
        GaugeCanvas.Children.Clear();
        double dim = ActualTheme == ElementTheme.Dark ? 0.55 : 0.4;

        // 底弧轨道
        var track = new Path
        {
            Stroke = new SolidColorBrush(Color.FromArgb((byte)(dim * 255), _trackColor.R, _trackColor.G, _trackColor.B)),
            StrokeThickness = 20,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
        track.Data = BuildArc(0, 1, TrackR);
        GaugeCanvas.Children.Add(track);

        // 进度弧（颜色随阶段切换）
        _progressArc = new Path
        {
            Stroke = new SolidColorBrush(_dlColor),
            StrokeThickness = 20,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
            Data = null
        };
        GaugeCanvas.Children.Add(_progressArc);

        // 刻度（长/短间隔）
        for (int i = 0; i <= 8; i++)
        {
            double f = i / 8.0;
            bool major = i % 2 == 0;
            var line = new Line
            {
                X1 = PolarX(f, TrackR + 16), Y1 = PolarY(f, TrackR + 16),
                X2 = PolarX(f, TrackR + (major ? 27 : 22)), Y2 = PolarY(f, TrackR + (major ? 27 : 22)),
                Stroke = new SolidColorBrush(Color.FromArgb((byte)((dim + 0.22) * 255), _primaryColor.R, _primaryColor.G, _primaryColor.B)),
                StrokeThickness = major ? 2.6 : 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            };
            GaugeCanvas.Children.Add(line);
        }

        // 指针
        var needle = new Line
        {
            X1 = Cx, Y1 = Cy, X2 = Cx, Y2 = Cy - NeedleLen,
            Stroke = new SolidColorBrush(_primaryColor),
            StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
        _needleRot = new RotateTransform { CenterX = Cx, CenterY = Cy, Angle = DialStartAngle };
        needle.RenderTransform = _needleRot;
        GaugeCanvas.Children.Add(needle);

        // 中心轴：外环 + 内芯
        var hubOuter = new Ellipse
        {
            Width = 32, Height = 32,
            Fill = new SolidColorBrush(Color.FromArgb(44, _primaryColor.R, _primaryColor.G, _primaryColor.B)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(hubOuter, Cx - 16);
        Canvas.SetTop(hubOuter, Cy - 16);
        GaugeCanvas.Children.Add(hubOuter);

        var hubInner = new Ellipse
        {
            Width = 15, Height = 15,
            Fill = new SolidColorBrush(_primaryColor),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(hubInner, Cx - 7.5);
        Canvas.SetTop(hubInner, Cy - 7.5);
        GaugeCanvas.Children.Add(hubInner);
    }

    private static double PolarX(double f, double r)
        => Cx + r * Math.Cos(Math.PI * (0.75 + f * 1.5));

    private static double PolarY(double f, double r)
        => Cy + r * Math.Sin(Math.PI * (0.75 + f * 1.5));

    private static Geometry BuildArc(double f0, double f1, double radius)
    {
        var fig = new PathFigure
        {
            StartPoint = new Point(PolarX(f0, radius), PolarY(f0, radius)),
            IsClosed = false,
            IsFilled = false
        };
        fig.Segments.Add(new ArcSegment
        {
            Point = new Point(PolarX(f1, radius), PolarY(f1, radius)),
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            // IsLargeArc 的语义是“扫过 >180°”，整圈 270° ⇒ 阈值 = 180/270 = 2/3。
            // 之前误用 0.5：当弧长处于 135°~180°（读数过半但未到 2/3）时被错误标记为大弧，
            // ArcSegment 会绕远路补弧 → 进度弧“飞出去”。此边界必须用 2/3。
            IsLargeArc = f1 - f0 > 2.0 / 3.0
        });
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return geo;
    }

    // ───────────────────────────── 开始 / 停止 ─────────────────────────────

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running) { StopTest(); return; }
        await RunTestAsync();
    }

    private void StopTest()
    {
        _cts?.Cancel();
        StatusText.Text = "正在停止…";
    }

    private async Task RunTestAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _running = true;
        SetButtonRunning();

        _testSw.Restart();
        ResetToIdle();
        SetChipsIdle();
        ErrorBar.IsOpen = false;
        _dlPts.Clear();
        _ulPts.Clear();

        try
        {
            // 1) 本机 IP
            StatusText.Text = "正在连接测速节点…";
            try
            {
                IpText.Text = "本机 IP：…";
                var ip = await _engine.GetPublicIpAsync(ct);
                IpText.Text = "本机 IP：" + ip;
            }
            catch (OperationCanceledException) { throw; }
            catch { IpText.Text = "本机 IP：--"; }

            // 2) 延迟 / 抖动
            _phase = Phase.Ping;
            _phaseBaseProgress = 0;
            BeginPhase("网络延迟", "ms", _pingColor, ChipPing);
            var (ping, jitter) = await _engine.MeasureLatencyAsync(
                (p, j, d, t) => EngineLive(() => OnPingLive(p, j, d, t)), ct);
            _pingMs = ping;
            _jitterMs = jitter;
            PingValue.Text = FmtValue(ping);
            JitValue.Text = FmtValue(jitter);
            SetChipDone(ChipPing, _pingColor);

            // 3) 下载
            _phase = Phase.Download;
            _phaseBaseProgress = 0.08;
            BeginPhase("下载速度", UnitLabel, _dlColor, ChipDownload);
            _targetValue = 0;
            _dlMbps = await _engine.MeasureDownloadAsync(
                (m, p, s) => EngineLive(() => OnDlLive(m, p, s)), ct);
            DlValue.Text = FmtSpeed(_dlMbps);
            SetChipDone(ChipDownload, _dlColor);

            // 4) 上传
            _phase = Phase.Upload;
            _phaseBaseProgress = 0.65;
            BeginPhase("上传速度", UnitLabel, _ulColor, ChipUpload);
            _targetValue = 0;
            _ulMbps = await _engine.MeasureUploadAsync(
                (m, p, s) => EngineLive(() => OnUlLive(m, p, s)), ct);
            UlValue.Text = FmtSpeed(_ulMbps);
            SetChipDone(ChipUpload, _ulColor);

            // 5) 完成
            _phase = Phase.Done;
            FinishTest();
        }
        catch (OperationCanceledException)
        {
            OnAborted("已手动停止测速", false);
        }
        catch (Exception ex)
        {
            OnAborted("测速失败：" + ex.Message, true);
        }
        finally
        {
            _running = false;
            SetButtonReady();
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>引擎回调跑在工作线程，统一切回 UI 线程安全更新。</summary>
    private void EngineLive(Action action)
    {
        if (DispatcherQueue is null) return;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            try { action(); }
            catch { /* 页面已卸载等场景直接忽略 */ }
        });
    }

    private void BeginPhase(string stage, string unit, Color accent, Border chip)
    {
        StageText.Text = stage;
        UnitText.Text = unit;
        StatusText.Text = stage + "测试进行中…";
        SetProgress(_phaseBaseProgress);
        _phaseStartGlobalSec = _testSw.Elapsed.TotalSeconds;
        _targetValue = double.NaN;
        ValueText.Text = "--";
        if (_progressArc is not null) _progressArc.Stroke = new SolidColorBrush(accent);
        SetChipActive(chip);
    }

    private void FinishTest()
    {
        double elapsed = _testSw.Elapsed.TotalSeconds;
        StatusText.Text = $"测速完成 · 总耗时 {elapsed:0} 秒";
        StageText.Text = "测速完成";
        UnitText.Text = UnitLabel;
        _targetValue = double.IsNaN(_dlMbps) ? 0 : _dlMbps;
        SetProgress(1);

        var (title, color, comment) = Evaluate(_dlMbps, _pingMs);
        ResultTitleText.Text = "网络状况：" + title;
        ResultDetailText.Text = BuildResultDetail(comment);
        ApplyResultBanner();
        ResultBanner.Visibility = Visibility.Visible;

        ChartHint.Text = "峰值速率 " + FmtSpeed(ChartPeakMbps()) + " " + UnitLabel +
                         " · 图表由 LiveCharts 渲染，纵轴自动缩放";
    }

    private void ApplyResultBanner()
    {
        var (_, color, _) = Evaluate(_dlMbps, _pingMs);
        ResultTitleText.Foreground = new SolidColorBrush(color);
        ResultBanner.Background = new SolidColorBrush(Color.FromArgb(36, color.R, color.G, color.B));
        ResultIcon.Foreground = new SolidColorBrush(color);
    }

    private void OnAborted(string status, bool isError)
    {
        StatusText.Text = status;
        StageText.Text = isError ? "测速失败" : "已停止";
        UnitText.Text = UnitLabel;
        _targetValue = double.NaN;
        ValueText.Text = "--";
        if (_activeChip is not null) SetChipIdle(_activeChip);
        if (isError) ErrorBar.IsOpen = true;
    }

    private static (string Title, Color Color, string Comment) Evaluate(double dl, double ping)
    {
        if (double.IsNaN(dl))
            return ("无法评定", Color.FromArgb(255, 160, 160, 160), "");
        if (dl >= 800)
            return ("极速", Color.FromArgb(255, 139, 92, 246), "带宽惊人，接近万兆级网络体验");
        if (dl >= 200)
            return ("优秀", Color.FromArgb(255, 22, 163, 74), "带宽充足，4K 流媒体与大型下载毫无压力");
        if (dl >= 50)
            return ("良好", Color.FromArgb(255, 0, 120, 212), "可满足高清视频与在线游戏需求");
        if (dl >= 10)
            return ("一般", Color.FromArgb(255, 234, 160, 0), "适合网页浏览与标清视频，建议优化网络");
        return ("较差", Color.FromArgb(255, 220, 53, 69), "网络较慢，建议检查设备或联系运营商");
    }

    // ───────────────────────────── 引擎实时回调（UI 线程） ─────────────────────────────

    private void OnPingLive(double pingMs, double jitterMs, int done, int total)
    {
        if (!_running || _phase != Phase.Ping) return;
        _pingMs = pingMs;
        _jitterMs = jitterMs;
        _targetValue = pingMs;
        PingValue.Text = FmtValue(pingMs);
        JitValue.Text = FmtValue(jitterMs);
        StatusText.Text = $"正在测量延迟：第 {done}/{total} 次 · 当前中位 {FmtValue(pingMs)} ms";
        SetProgress(_phaseBaseProgress + (done / (double)total) * 0.08);
    }

    private void OnDlLive(double mbps, double progress, double seconds)
    {
        if (!_running || _phase != Phase.Download) return;
        _targetValue = mbps;
        DlValue.Text = FmtSpeed(mbps);
        StatusText.Text = $"下载测试中：{FmtSpeed(mbps)} {UnitLabel} · 4 路并发";
        SetProgress(_phaseBaseProgress + progress * 0.57);
        _dlPts.Add(new ObservablePoint(_phaseStartGlobalSec + seconds, mbps));
        TrimSeries(_dlPts);
    }

    private void OnUlLive(double mbps, double progress, double seconds)
    {
        if (!_running || _phase != Phase.Upload) return;
        _targetValue = mbps;
        UlValue.Text = FmtSpeed(mbps);
        StatusText.Text = $"上传测试中：{FmtSpeed(mbps)} {UnitLabel} · 3 路并发";
        SetProgress(_phaseBaseProgress + progress * 0.35);
        _ulPts.Add(new ObservablePoint(_phaseStartGlobalSec + seconds, mbps));
        TrimSeries(_ulPts);
    }

    private void SetProgress(double frac)
    {
        frac = Math.Clamp(frac, 0, 1);
        PhaseBar.Value = frac * 100;
        PctText.Text = (frac * 100).ToString("0") + "%";
    }

    // ───────────────────────────── 动画循环（≈30 FPS） ─────────────────────────────

    private void AnimTimer_Tick(object? sender, object e)
    {
        double now = Environment.TickCount64 / 1000.0;
        double dt = Math.Min(0.1, now - _lastTickSec);
        _lastTickSec = now;

        if (!double.IsNaN(_targetValue))
        {
            double k = 1 - Math.Exp(-dt * 7);
            _displayValue += (_targetValue - _displayValue) * k;
            if (Math.Abs(_targetValue - _displayValue) < 0.05) _displayValue = _targetValue;
        }
        else
        {
            _displayValue = 0;
        }

        double frac = ValueToFraction(_displayValue);
        if (_needleRot is not null) _needleRot.Angle = DialStartAngle + frac * 270.0;
        if (_progressArc is not null)
            _progressArc.Data = frac > 0.004 ? BuildArc(0, frac, TrackR) : null;

        // 延迟阶段按 ms 展示；速率阶段按所选单位（内部始终 Mbps）换算展示
        string txt = double.IsNaN(_targetValue) && Math.Abs(_displayValue) < 0.01
            ? "--"
            : _phase == Phase.Ping ? FmtValue(_displayValue) : FmtSpeed(_displayValue);
        if (ValueText.Text != txt) ValueText.Text = txt;

        // 进行中步骤的呼吸光晕
        if (_activeChip is not null)
        {
            var halo = HaloOf(_activeChip);
            if (halo is not null)
                halo.Opacity = 0.3 + 0.25 * (0.5 + 0.5 * Math.Sin(now * 5.0));
        }
    }

    private double ValueToFraction(double v)
    {
        if (v <= 0) return 0;
        if (_phase == Phase.Ping) return Math.Min(1, v / 120.0); // 延迟：线性 0..120ms
        return Math.Clamp(1 - 1 / Math.Pow(1.12, Math.Sqrt(v)), 0, 1); // 速率：对数刻度
    }

    // ───────────────────────────── 阶段步骤条 ─────────────────────────────

    private enum ChipState { Pending, Active, Done }

    private void SetChipsIdle()
    {
        SetChipIdle(ChipPing);
        SetChipIdle(ChipDownload);
        SetChipIdle(ChipUpload);
    }

    private void SetChipIdle(Border chip)
    {
        _doneChips.Remove(chip);
        if (_activeChip == chip) _activeChip = null;
        SetChipCore(chip, ChipState.Pending, default);
    }

    private void SetChipActive(Border chip)
    {
        _doneChips.Remove(chip);
        _activeChip = chip;
        SetChipCore(chip, ChipState.Active, default);
    }

    private void SetChipDone(Border chip, Color color)
    {
        if (_activeChip == chip) _activeChip = null;
        _doneChips.Add(chip);
        SetChipCore(chip, ChipState.Done, color);
    }

    private void ReapplyChips()
    {
        ReapplyChip(ChipPing, _pingColor);
        ReapplyChip(ChipDownload, _dlColor);
        ReapplyChip(ChipUpload, _ulColor);
    }

    private void ReapplyChip(Border chip, Color doneColor)
    {
        if (_doneChips.Contains(chip)) SetChipCore(chip, ChipState.Done, doneColor);
        else if (_activeChip == chip) SetChipCore(chip, ChipState.Active, default);
        else SetChipCore(chip, ChipState.Pending, default);
    }

    private void SetChipCore(Border chip, ChipState state, Color doneColor)
    {
        var icon = ChipIconOf(chip);
        var text = ChipTextOf(chip);
        var halo = HaloOf(chip);
        if (icon is null || text is null) return;

        var onAccent = BrushRes("TextOnAccentFillColorPrimaryBrush", Microsoft.UI.Colors.White);
        var dim = BrushRes("TextFillColorSecondaryBrush", _textSecondary);

        switch (state)
        {
            case ChipState.Active:
            {
                var accentBrush = BrushRes("AccentFillColorDefaultBrush", Color.FromArgb(255, 0, 120, 212));
                chip.Background = accentBrush;
                icon.Foreground = onAccent;
                text.Foreground = accentBrush;
                text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                icon.Glyph = OriginalGlyph(chip);
                if (halo is not null) { halo.Fill = accentBrush; halo.Opacity = 0.55; }
                break;
            }
            case ChipState.Done:
            {
                chip.Background = new SolidColorBrush(doneColor);
                icon.Foreground = onAccent;
                text.Foreground = new SolidColorBrush(doneColor);
                text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                icon.Glyph = "\uE73E"; // 完成对勾
                if (halo is not null) halo.Opacity = 0;
                break;
            }
            default:
            {
                chip.Background = BrushRes("SubtleFillColorSecondaryBrush", Color.FromArgb(255, 240, 240, 240));
                icon.Foreground = dim;
                text.Foreground = dim;
                text.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
                icon.Glyph = OriginalGlyph(chip);
                if (halo is not null) { halo.Opacity = 0; halo.Fill = null; }
                break;
            }
        }

        RefreshLinks();
    }

    /// <summary>连接线点亮规则：步骤 1 完成 → 连线 1 变绿；步骤 2 完成 → 连线 2 变绿。</summary>
    private void RefreshLinks()
    {
        StepLink1.Background = _doneChips.Contains(ChipPing)
            ? BrushRes("SystemFillColorSuccessBrush", Color.FromArgb(255, 22, 163, 74))
            : BrushRes("DividerStrokeColorDefaultBrush", Color.FromArgb(255, 200, 200, 200));
        StepLink2.Background = _doneChips.Contains(ChipDownload)
            ? BrushRes("SystemFillColorSuccessBrush", Color.FromArgb(255, 22, 163, 74))
            : BrushRes("DividerStrokeColorDefaultBrush", Color.FromArgb(255, 200, 200, 200));
    }

    private string OriginalGlyph(Border chip)
        => chip == ChipPing ? "\uE823" : chip == ChipDownload ? "\uE896" : "\uE898";

    private FontIcon? ChipIconOf(Border chip) =>
        chip == ChipPing ? ChipPingIcon : chip == ChipDownload ? ChipDownloadIcon : ChipUploadIcon;

    private TextBlock? ChipTextOf(Border chip) =>
        chip == ChipPing ? ChipPingText : chip == ChipDownload ? ChipDownloadText : ChipUploadText;

    private Ellipse? HaloOf(Border chip) =>
        chip == ChipPing ? StepPingHalo : chip == ChipDownload ? StepDownloadHalo : StepUploadHalo;

    // ───────────────────────────── 实时曲线（LiveCharts2） ─────────────────────────────

    private void InitChart()
    {
        _dlSeries = new LineSeries<ObservablePoint>
        {
            Values = _dlPts,
            Stroke = new SolidColorPaint(Sk(_dlColor)) { StrokeThickness = 2.5f },
            Fill = new SolidColorPaint(SkA(_dlColor, 45)),
            GeometrySize = 0,
            LineSmoothness = 0.35,
            IsHoverable = false
        };
        _ulSeries = new LineSeries<ObservablePoint>
        {
            Values = _ulPts,
            Stroke = new SolidColorPaint(Sk(_ulColor)) { StrokeThickness = 2.5f },
            Fill = new SolidColorPaint(SkA(_ulColor, 45)),
            GeometrySize = 0,
            LineSmoothness = 0.35,
            IsHoverable = false
        };

        RateChart.Series = new ISeries[] { _dlSeries, _ulSeries };
        RateChart.AnimationsSpeed = TimeSpan.FromMilliseconds(120);
        RateChart.EasingFunction = null;
        RateChart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Hidden;

        RebuildChartTheme();
    }

    private void RebuildChartTheme()
    {
        RateChart.XAxes = new Axis[] { new Axis { IsVisible = false } };
        RateChart.YAxes = new Axis[]
        {
            new Axis
            {
                MinLimit = 0,
                // 曲线点存的是内部口径 Mbps；纵轴标签按所选展示单位换算
                Labeler = v =>
                {
                    double d = _useBytesPerSec ? v / 8.0 : v;
                    return d >= 100 ? d.ToString("0") : d.ToString("0.#");
                },
                LabelsPaint = new SolidColorPaint(Sk(_textSecondary)),
                SeparatorsPaint = new SolidColorPaint(SkA(_textSecondary, 36)),
                TextSize = 10,
                ShowSeparatorLines = true,
                TicksPaint = null
            }
        };
    }

    private static void TrimSeries(ObservableCollection<ObservablePoint> pts)
    {
        if (pts.Count <= 900) return;
        for (int i = 0; i < 150; i++) pts.RemoveAt(0);
    }

    private static SKColor Sk(Color c) => new(c.R, c.G, c.B, 255);

    private static SKColor SkA(Color c, byte alpha) => new(c.R, c.G, c.B, alpha);

    // ───────────────────────────── 通用辅助 ─────────────────────────────

    private static string FmtValue(double v)
        => double.IsNaN(v) || double.IsInfinity(v) ? "--" : v < 100 ? v.ToString("0.0") : v.ToString("0");

    private void StyleStatIcon(Border bg, string glyph, Color color)
    {
        bg.Background = new SolidColorBrush(Color.FromArgb(34, color.R, color.G, color.B));
        bg.Child = new FontIcon { Glyph = glyph, FontSize = 13, Foreground = new SolidColorBrush(color) };
    }

    private void SetButtonReady()
    {
        // 原生 AccentButtonStyle：悬停/按下/禁用反馈全部交给系统
        StartButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        // 清除停止态残留的红色前景局部值，让 AccentButtonStyle 的白色文字生效
        StartButton.ClearValue(Button.ForegroundProperty);
        StartIcon.ClearValue(FontIcon.ForegroundProperty);
        StartText.ClearValue(TextBlock.ForegroundProperty);
        StartIcon.Glyph = "\uE768";
        StartText.Text = "开始测速";
        NodeBox.IsEnabled = UnitBox.IsEnabled = true;
    }

    private void SetButtonRunning()
    {
        // 停止态：原生默认按钮样式 + 红色文字图标（不改 Background，保留系统悬停反馈）
        StartButton.Style = null;
        var red = ColorRes("SystemFillColorCriticalBrush", Color.FromArgb(255, 220, 53, 69));
        var redBrush = new SolidColorBrush(red);
        StartButton.Foreground = redBrush;
        StartIcon.Foreground = redBrush;
        StartText.Foreground = redBrush;
        StartIcon.Glyph = "\uE71A";
        StartText.Text = "停止测速";
        NodeBox.IsEnabled = UnitBox.IsEnabled = false;
    }

    private static Color ColorRes(string key, Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out var v))
        {
            if (v is Color c) return c;
            if (v is SolidColorBrush b) return b.Color;
        }
        return fallback;
    }

    private static Brush BrushRes(string key, Color fallback)
        => Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b ? b : new SolidColorBrush(fallback);

    // ───────────────────────────── 常用网站连通性 ─────────────────────────────

    private sealed class SiteDef
    {
        public required string Name { get; init; }
        public required string Domain { get; init; }
        public required string Url { get; init; }
        public required uint BadgeRgb { get; init; }   // 0xAARRGGBB 品牌色
        public string? Letter { get; init; }           // null = 微软四色方格
    }

    private static readonly SiteDef[] SiteDefs =
    {
        new() { Name = "百度",     Domain = "www.baidu.com",          Url = "https://www.baidu.com/",          BadgeRgb = 0xFF2932E1, Letter = "B" },
        new() { Name = "网易",     Domain = "www.163.com",            Url = "https://www.163.com/",            BadgeRgb = 0xFFDE1A22, Letter = "N" },
        new() { Name = "腾讯",     Domain = "www.qq.com",             Url = "https://www.qq.com/",             BadgeRgb = 0xFF1479D7, Letter = "T" },
        new() { Name = "哔哩哔哩", Domain = "www.bilibili.com",       Url = "https://www.bilibili.com/",       BadgeRgb = 0xFFFB7299, Letter = "B" },
        new() { Name = "GitHub",  Domain = "github.com",             Url = "https://github.com/",             BadgeRgb = 0xFF24292F, Letter = "G" },
        new() { Name = "GitCode", Domain = "gitcode.com",            Url = "https://gitcode.com/",            BadgeRgb = 0xFF1F7BF4, Letter = "G" },
        new() { Name = "微软",     Domain = "www.microsoft.com",      Url = "https://www.microsoft.com/",      BadgeRgb = 0xFF00A4EF, Letter = null },
        new() { Name = "Steam",   Domain = "store.steampowered.com", Url = "https://store.steampowered.com/", BadgeRgb = 0xFF171A21, Letter = "S" },
    };

    private sealed class SiteRowView
    {
        public required string Url { get; init; }
        public required Border Row { get; init; }
        public required TextBlock LatText { get; init; }
        public required Ellipse Dot { get; init; }
        public double? RttMs { get; set; } // null = 未测；NaN = 超时/不可达
    }

    private static HttpClient CreateSiteHttp()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(3),
            PooledConnectionLifetime = TimeSpan.FromSeconds(6),
            MaxConnectionsPerServer = 4
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
    }

    private void BuildSiteList()
    {
        SiteGrid.Children.Clear();
        _siteRows.Clear();

        for (int i = 0; i < SiteDefs.Length; i++)
        {
            var def = SiteDefs[i];
            var row = new Border
            {
                Background = BrushRes("SubtleFillColorSecondaryBrush", Color.FromArgb(255, 245, 245, 245)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 7, 12, 7)
            };
            ToolTipService.SetToolTip(row, $"{def.Name} · {def.Domain}");

            var line = new Grid { ColumnSpacing = 10 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(BuildBadge(def));

            var nameCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 0 };
            nameCol.Children.Add(new TextBlock
            {
                Text = def.Name,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            nameCol.Children.Add(new TextBlock
            {
                Text = def.Domain,
                FontSize = 10.5,
                Opacity = 0.55,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(nameCol, 1);
            line.Children.Add(nameCol);

            var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
            var lat = new TextBlock
            {
                Text = "检测中…",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                MinWidth = 60,
                VerticalAlignment = VerticalAlignment.Center
            };
            var dim = BrushRes("TextFillColorSecondaryBrush", Color.FromArgb(255, 120, 120, 120));
            dot.Fill = dim;
            lat.Foreground = dim;
            var tail = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center
            };
            tail.Children.Add(dot);
            tail.Children.Add(lat);
            Grid.SetColumn(tail, 2);
            line.Children.Add(tail);

            row.Child = line;
            Grid.SetRow(row, i / 2);
            Grid.SetColumn(row, i % 2);
            SiteGrid.Children.Add(row);
            _siteRows.Add(new SiteRowView { Url = def.Url, Row = row, LatText = lat, Dot = dot });
        }
    }

    private static Border BuildBadge(SiteDef def)
    {
        UIElement inner = def.Letter is string letter
            ? new TextBlock
            {
                Text = letter,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
            : BuildMsLogo();

        return new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(9),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(BadgeColor(def.BadgeRgb)),
            Child = inner
        };
    }

    private static Border BuildMsLogo()
    {
        var grid = new Grid { Width = 20, Height = 20 };
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var cells = new (byte R, byte G, byte B)[]
        {
            (0xF2, 0x50, 0x22), (0x7F, 0xBA, 0x00), // 红 / 绿
            (0x00, 0xA4, 0xEF), (0xFF, 0xB9, 0x00)  // 蓝 / 黄
        };
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 2; c++)
            {
                var (rr, gg, bb) = cells[r * 2 + c];
                var cell = new Border
                {
                    Margin = new Thickness(0.6),
                    CornerRadius = new CornerRadius(1.2),
                    Background = new SolidColorBrush(Color.FromArgb(255, rr, gg, bb))
                };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        return new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(9),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(255, 250, 250, 250)),
            Child = grid
        };
    }

    private static Color BadgeColor(uint argb)
        => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private async Task ProbeAllSitesAsync()
    {
        if (_siteBusy) return;
        _siteBusy = true;
        SiteRefreshButton.IsEnabled = false;
        _siteCts?.Cancel();
        _siteCts = new CancellationTokenSource();
        var ct = _siteCts.Token;

        var dim = BrushRes("TextFillColorSecondaryBrush", Color.FromArgb(255, 120, 120, 120));
        foreach (var view in _siteRows)
        {
            view.RttMs = null;
            view.Dot.Fill = dim;
            view.LatText.Foreground = dim;
            view.LatText.Text = "检测中…";
        }

        try
        {
            await Task.WhenAll(_siteRows.Select(v => ProbeSiteAsync(v, ct)));
        }
        catch (OperationCanceledException) { /* 手动刷新或页面卸载 */ }
        catch (Exception) { /* 单项失败已各自处理 */ }
        finally
        {
            _siteBusy = false;
            SiteRefreshButton.IsEnabled = true;
        }
    }

    private async Task ProbeSiteAsync(SiteRowView view, CancellationToken outer)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        cts.CancelAfter(TimeSpan.FromSeconds(4.5));
        var url = view.Url + (view.Url.Contains('?') ? "&" : "?") + "t=" + Stopwatch.GetTimestamp();
        var sw = Stopwatch.StartNew();
        try
        {
            using var resp = await _siteHttp.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, url) { Version = HttpVersion.Version11 },
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);
            // 首响应头到达即停止计时，不读取 body（连接随即关闭，保证每次都是全新连接的真实延迟）
            double ms = sw.Elapsed.TotalMilliseconds;
            EngineLive(() => ApplySiteResult(view, ms));
        }
        catch (OperationCanceledException) { EngineLive(() => ApplySiteResult(view, double.NaN)); }
        catch (HttpRequestException) { EngineLive(() => ApplySiteResult(view, double.NaN)); }
        catch (Exception) { EngineLive(() => ApplySiteResult(view, double.NaN)); }
    }

    private void ApplySiteResult(SiteRowView view, double rttMs)
    {
        Color color;
        string text;
        if (double.IsNaN(rttMs))
        {
            color = ColorRes("SystemFillColorCriticalBrush", Color.FromArgb(255, 220, 53, 69));
            text = "超时";
        }
        else if (rttMs < 150)
        {
            color = ColorRes("SystemFillColorSuccessBrush", Color.FromArgb(255, 22, 163, 74));
            text = $"{rttMs:0} ms";
        }
        else if (rttMs <= 400)
        {
            color = ColorRes("SystemFillColorCautionBrush", Color.FromArgb(255, 234, 160, 0));
            text = $"{rttMs:0} ms";
        }
        else
        {
            color = ColorRes("SystemFillColorCriticalBrush", Color.FromArgb(255, 220, 53, 69));
            text = $"{rttMs:0} ms";
        }

        view.RttMs = rttMs;
        var brush = new SolidColorBrush(color);
        view.Dot.Fill = brush;
        view.LatText.Foreground = brush;
        view.LatText.Text = text;
    }

    private void RecolorSiteRows()
    {
        if (_siteRows.Count == 0) return;
        var bg = BrushRes("SubtleFillColorSecondaryBrush", Color.FromArgb(255, 245, 245, 245));
        foreach (var view in _siteRows)
        {
            view.Row.Background = bg;
            if (view.RttMs is double ms) ApplySiteResult(view, ms);
        }
    }

    private void SiteRefreshButton_Click(object sender, RoutedEventArgs e) => _ = ProbeAllSitesAsync();

    private void BackButton_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateBack();
}
