using System.Globalization;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SkiaSharp;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

/// <summary>
/// 「记录查看」：解析游戏监控导出的 JSON / CSV，用工程内原生的 LiveCharts2 图表回放历史数据。
/// 支持分组切换、区间裁剪、归一化对比、曲线显隐与全量统计（最小/平均/最大/P1/P99）。
/// </summary>
public sealed partial class GameMonitorRecordsPage : Page
{
    private const string AllGroups = "全部";

    /// <summary>曲线配色（在浅色/深色背景下都可辨识）。</summary>
    private static readonly Color[] Palette =
    [
        Color.FromArgb(255, 0x0F, 0x7A, 0xE8), // 蓝
        Color.FromArgb(255, 0xE8, 0x5D, 0x2A), // 橙
        Color.FromArgb(255, 0x1F, 0xA8, 0x5C), // 绿
        Color.FromArgb(255, 0xC7, 0x3A, 0x8E), // 品红
        Color.FromArgb(255, 0x7B, 0x5C, 0xE0), // 紫
        Color.FromArgb(255, 0x00, 0x9E, 0xA8), // 青
        Color.FromArgb(255, 0xD1, 0x8A, 0x00), // 琥珀
        Color.FromArgb(255, 0x5A, 0x6A, 0x7A), // 灰蓝
        Color.FromArgb(255, 0x9C, 0x27, 0x2B), // 深红
        Color.FromArgb(255, 0x2E, 0x6B, 0x3E), // 墨绿
    ];

    private string? _initialFile;
    private string _dir = "";
    private bool _ready;

    private GameMonitorRecordReader.MonitorRecordView? _view;
    private readonly List<FileItem> _fileItems = [];

    /// <summary>被用户点击图例隐藏的指标 key。</summary>
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _groups = [];
    private string _group = AllGroups;

    /// <summary>当前 X 轴刻度对应的真实秒数（随区间裁剪变化）。</summary>
    private List<double> _visibleTimes = [];

    private bool _suspendRange;
    private int _loadToken;
    private readonly List<Border> _legendChips = [];
    private double _legendWidth = -1;

    public GameMonitorRecordsPage()
    {
        ChartInitializer.EnsureConfigured();
        InitializeComponent();
        ActualThemeChanged += (_, _) =>
        {
            if (_ready && _view is not null) RenderChart();
        };
        Loaded += OnLoaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _initialFile = e.Parameter as string;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_ready) return;
        _ready = true;
        RefreshFiles(_initialFile);
    }

    // ------------------------------------------------------------ 文件列表

    private void RefreshFiles(string? selectPath)
    {
        _dir = GameMonitorRecorder.GetOutputDir();
        TxtDir.Text = _dir;

        _fileItems.Clear();
        foreach (var file in GameMonitorRecordReader.ListRecordFiles(_dir))
        {
            var info = new FileInfo(file);
            _fileItems.Add(new FileItem
            {
                Path = file,
                Name = Path.GetFileNameWithoutExtension(file),
                Info = $"{info.LastWriteTime:MM-dd HH:mm} · {FormatSize(info.Length)}"
            });
        }

        FileList.ItemsSource = null;
        FileList.ItemsSource = _fileItems;
        TxtRecordedCount.Text = _fileItems.Count == 0
            ? "输出目录下还没有记录文件"
            : $"共 {_fileItems.Count} 个记录文件";

        if (_fileItems.Count == 0)
        {
            _view = null;
            ClearPanes();
            SetupRange(0);
            ShowEmpty("\uE7C3", "还没有记录文件。\n先在「游戏监控」页勾选指标并录制一段，保存后回到这里即可查看。");
            TxtStatus.Text = "未找到记录文件";
            return;
        }

        var index = 0;
        if (!string.IsNullOrWhiteSpace(selectPath))
        {
            var found = _fileItems.FindIndex(f => string.Equals(f.Path, selectPath, StringComparison.OrdinalIgnoreCase));
            if (found >= 0) index = found;
        }

        FileList.SelectedIndex = -1;
        FileList.SelectedIndex = index;
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileList.SelectedItem is FileItem item) LoadFile(item.Path);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
        => RefreshFiles((FileList.SelectedItem as FileItem)?.Path);

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            await Windows.System.Launcher.LaunchFolderPathAsync(_dir);
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"打开目录失败：{ex.Message}";
        }
    }

    // ---------------------------------------------------------------- 加载

    private async void LoadFile(string path)
    {
        var token = ++_loadToken;
        LoadingOverlay.Visibility = Visibility.Visible;
        TxtLoading.Text = "正在解析…";
        TxtStatus.Text = $"正在解析 {Path.GetFileName(path)}";

        try
        {
            var view = await Task.Run(() =>
                GameMonitorRecordReader.BuildView(GameMonitorRecordReader.Read(path)));
            if (token != _loadToken) return;

            _view = view;
            _hidden.Clear();
            _group = AllGroups;
            BuildGroups();
            SetupRange(view.Times.Count);
            RenderAll();
            TxtStatus.Text = $"已加载 {view.Data.FileName}";
        }
        catch (Exception ex)
        {
            if (token != _loadToken) return;
            _view = null;
            ClearPanes();
            ShowEmpty("\uEA39", $"解析失败：{ex.Message}");
            TxtStatus.Text = "解析失败";
        }
        finally
        {
            if (token == _loadToken) LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    // -------------------------------------------------------------- 分组

    private void BuildGroups()
    {
        _groups.Clear();
        _groups.Add(AllGroups);
        if (_view is not null)
            foreach (var mv in _view.Metrics)
                if (!_groups.Contains(mv.Metric.Group)) _groups.Add(mv.Metric.Group);
        if (!_groups.Contains(_group)) _group = AllGroups;
        BuildTabs();
    }

    private void BuildTabs()
    {
        PnlTabs.Children.Clear();
        foreach (var group in _groups)
        {
            var selected = group == _group;
            var button = new Button
            {
                Content = group,
                FontSize = 12,
                Padding = new Thickness(12, 5, 12, 5),
                Tag = group
            };
            if (selected && Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style)
                && style is Style accent)
                button.Style = accent;
            else
                button.Opacity = 0.85;

            var captured = group;
            button.Click += (_, _) =>
            {
                if (_group == captured) return;
                _group = captured;
                _hidden.Clear();
                BuildTabs();
                RenderCards();
                RenderChart();
                RenderStats();
            };
            PnlTabs.Children.Add(button);
        }
    }

    private List<GameMonitorRecordReader.MonitorMetricView> VisibleMetrics()
    {
        if (_view is null) return [];
        return _group == AllGroups
            ? _view.Metrics.ToList()
            : _view.Metrics.Where(m => m.Metric.Group == _group).ToList();
    }

    // -------------------------------------------------------------- 渲染

    private void RenderAll()
    {
        RenderMeta();
        RenderCards();
        BuildTabs();
        RenderChart();
        RenderStats();
        DispatcherQueue.TryEnqueue(UpdateChartHeight);
    }

    private void RenderMeta()
    {
        PnlMeta.Children.Clear();
        if (_view is null) return;
        var meta = _view.Data.Meta;

        if (meta.StartTime != default) AddChip("\uE823", "开始", meta.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
        AddChip("\uE916", "时长", GameMonitorRecorder.FormatDuration(
            meta.DurationSeconds > 0 ? meta.DurationSeconds : _view.Data.Samples.Count > 0 ? _view.Data.Samples[^1].Seconds : 0));
        AddChip("\uE9D9", "间隔", meta.IntervalMs > 0 ? $"{meta.IntervalMs:0} ms" : "—");
        AddChip("\uE8EF", "样本", _view.Data.Samples.Count.ToString("N0", CultureInfo.InvariantCulture));
        AddChip("\uE7C1", "指标", _view.Data.Metrics.Count.ToString(CultureInfo.InvariantCulture));
        AddChip("\uE8B7", "格式", _view.Data.IsCsv ? "CSV" : "JSON");
        if (!string.IsNullOrWhiteSpace(meta.TargetWindow)) AddChip("\uE7C4", "目标窗口", meta.TargetWindow);
        if (!string.IsNullOrWhiteSpace(meta.FpsProcess)) AddChip("\uE9F5", "FPS 进程", meta.FpsProcess);
        if (!string.IsNullOrWhiteSpace(meta.CpuName)) AddChip("\uE950", "CPU", meta.CpuName);
        if (!string.IsNullOrWhiteSpace(meta.GpuName)) AddChip("\uE7F4", "GPU", meta.GpuName);
        if (meta.Truncated) AddChip("\uE7BA", "备注", "达到 2 小时上限，已自动截断", warn: true);

        // 首尾「FPS 尚未出数」的无效段在绘图前已被剔除，这里说明一下图表为什么不是从 0 秒开始
        var trimmed = new List<string>();
        if (_view.HeadTrimmedSeconds > 0) trimmed.Add($"开头 {_view.HeadTrimmedSeconds:0.#} s");
        if (_view.TailTrimmedSeconds > 0) trimmed.Add($"结尾 {_view.TailTrimmedSeconds:0.#} s");
        if (trimmed.Count > 0)
            AddChip("\uE7BA", "已剔除无效数据", string.Join(" · ", trimmed), warn: true);
    }

    private void RenderCards()
    {
        PnlCards.Children.Clear();
        if (_view is null) return;

        var meta = _view.Data.Meta;
        var duration = meta.DurationSeconds > 0
            ? meta.DurationSeconds
            : _view.Data.Samples.Count > 0 ? _view.Data.Samples[^1].Seconds : 0;

        AddCard("记录时长", GameMonitorRecorder.FormatDuration(duration), Palette[0]);
        AddCard("采样点", _view.Data.Samples.Count.ToString("N0", CultureInfo.InvariantCulture),
            Palette[1], $"抽稀步长 {_view.Step}");
        AddCard("采样间隔", meta.IntervalMs > 0 ? $"{meta.IntervalMs:0} ms" : "—", Palette[5]);

        // 当前分组的关键指标（取第一个有数据的指标作为头条）
        var metrics = VisibleMetrics();
        var headline = metrics.FirstOrDefault(m => m.HasData);
        if (headline is not null)
        {
            var unit = string.IsNullOrEmpty(headline.Metric.Unit) ? "" : " " + headline.Metric.Unit;
            AddCard(headline.Metric.Label, headline.Avg.Trim() + unit, Palette[2], $"最大 {headline.Max.Trim()}{unit}");
        }

        var count = metrics.Count;
        AddCard("本组指标", count.ToString(CultureInfo.InvariantCulture), Palette[7]);
    }

    private void RenderChart()
    {
        if (_view is null || _view.Times.Count == 0)
        {
            Chart.Series = Array.Empty<ISeries>();
            ShowEmpty("\uE9D2", "没有可展示的数据");
            return;
        }

        var metrics = VisibleMetrics();
        var plotted = metrics.Where(m => !_hidden.Contains(m.Metric.Key) && m.HasData).ToList();
        if (plotted.Count == 0)
        {
            Chart.Series = Array.Empty<ISeries>();
            ShowEmpty("\uE9D2", "当前分组没有可展示的曲线");
            return;
        }

        var count = _view.Times.Count;
        var from = Math.Clamp((int)Math.Round(SliderFrom.Value), 0, count - 1);
        var to = Math.Clamp((int)Math.Round(SliderTo.Value), 0, count - 1);
        if (to < from) (from, to) = (to, from);

        var times = Slice(_view.Times, from, to);
        _visibleTimes = times;

        var normalized = ChkNorm.IsChecked == true;
        var fill = ChkFill.IsChecked == true;

        var series = new List<ISeries>();
        var legend = new List<LegendItem>();
        var index = 0;

        foreach (var mv in metrics)
        {
            var color = Palette[index % Palette.Length];
            index++;
            if (_hidden.Contains(mv.Metric.Key) || !mv.HasData)
            {
                legend.Add(new LegendItem(mv.Metric.Key, mv.Metric.Label, color, true));
                continue;
            }

            var values = Slice(mv.Values, from, to);
            var data = normalized ? Normalize(values) : values;

            series.Add(new LineSeries<double?>
            {
                Name = mv.Metric.Label,
                Values = data,
                Stroke = new SolidColorPaint(Sk(color)) { StrokeThickness = normalized ? 1.8f : 2f },
                Fill = fill ? new SolidColorPaint(SkA(color, normalized ? (byte)18 : (byte)28)) : null,
                GeometrySize = 0,
                LineSmoothness = 0.15,
                IsHoverable = true
            });
            legend.Add(new LegendItem(mv.Metric.Key, mv.Metric.Label, color, false));
        }

        Chart.Series = series;
        Chart.AnimationsSpeed = TimeSpan.FromMilliseconds(160);
        Chart.EasingFunction = null;
        Chart.LegendPosition = LegendPosition.Hidden;
        BuildAxes(normalized, times.Count);
        BuildLegend(legend);
        PnlChartEmpty.Visibility = Visibility.Collapsed;
        Chart.Visibility = Visibility.Visible;

        TxtChartTitle.Text = _group == AllGroups ? "全部指标" : _group;
        TxtChartHint.Text = normalized
            ? $"已归一化到 0–100%（各指标按自身量程缩放）· {plotted.Count} 条曲线 · {times.Count:N0} 个点"
            : $"{plotted.Count} 条曲线 · {times.Count:N0} 个点 · 全量 {count:N0} 点";
    }

    private void BuildAxes(bool normalized, int visibleCount)
    {
        var dark = ActualTheme == ElementTheme.Dark;
        var text = dark ? Color.FromArgb(255, 0xC8, 0xC8, 0xC8) : Color.FromArgb(255, 0x5A, 0x5A, 0x5A);
        var grid = new SolidColorPaint(SkA(text, 40));

        var step = Math.Max(1, (int)Math.Ceiling(visibleCount / 8.0));

        Chart.XAxes =
        [
            new Axis
            {
                Labeler = value =>
                {
                    var i = (int)Math.Round(value);
                    return i >= 0 && i < _visibleTimes.Count ? FormatTime(_visibleTimes[i]) : "";
                },
                LabelsPaint = new SolidColorPaint(Sk(text)),
                SeparatorsPaint = grid,
                TextSize = 10,
                TicksPaint = null,
                MinStep = step,
                ShowSeparatorLines = true
            }
        ];

        Chart.YAxes =
        [
            new Axis
            {
                Labeler = value => normalized ? value.ToString("0") + "%" : FormatAxisValue(value),
                LabelsPaint = new SolidColorPaint(Sk(text)),
                SeparatorsPaint = grid,
                TextSize = 10,
                TicksPaint = null,
                MinLimit = normalized ? 0 : null,
                MaxLimit = normalized ? 100 : null,
                ShowSeparatorLines = true
            }
        ];
    }

    private void BuildLegend(List<LegendItem> items)
    {
        _legendChips.Clear();
        var dark = ActualTheme == ElementTheme.Dark;
        var borderTint = dark ? Color.FromArgb(60, 0xFF, 0xFF, 0xFF) : Color.FromArgb(38, 0, 0, 0);

        foreach (var item in items)
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4, 10, 4),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(borderTint),
                Background = new SolidColorBrush(dark
                    ? Color.FromArgb(20, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(14, 0, 0, 0)),
                Opacity = item.Hidden ? 0.4 : 1,
                Tag = item.Key
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(item.Color),
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock { Text = item.Label, FontSize = 12 });
            chip.Child = row;

            var key = item.Key;
            ToolTipService.SetToolTip(chip, item.Hidden ? "点击显示该曲线" : "点击隐藏该曲线");
            chip.Tapped += (_, _) =>
            {
                if (!_hidden.Remove(key)) _hidden.Add(key);
                RenderChart();
            };
            _legendChips.Add(chip);
        }

        _legendWidth = -1;
        LayoutLegend();
    }

    /// <summary>按可用宽度把图例标签排成多行（WinUI 没有现成的 WrapPanel，这里手动分行）。</summary>
    private bool _legendLayingOut;

    private void LayoutLegend()
    {
        // 页面已卸载（窗口被关）后 SizeChanged 仍可能触发，此时往断开的树上加元素会抛 0x80070490
        if (_legendLayingOut || !IsLoaded) return;

        var available = PnlLegend.ActualWidth;
        if (available <= 0) available = 720;
        if (Math.Abs(available - _legendWidth) < 1 && PnlLegend.Children.Count > 0) return;
        _legendWidth = available;

        _legendLayingOut = true;
        try
        {
            PnlLegend.Children.Clear();
            if (_legendChips.Count == 0) return;

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            double used = 0;
            foreach (var chip in _legendChips)
            {
                // chip 可能还挂在上一轮的旧行上（Clear 只摘行了，没摘 chip），
                // 不先脱钩就 Add 会抛 COMException（元素已有父级）
                if (chip.Parent is Panel oldParent) oldParent.Children.Remove(chip);

                chip.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                var width = chip.DesiredSize.Width;
                if (used > 0 && used + width > available)
                {
                    PnlLegend.Children.Add(row);
                    row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    used = 0;
                }
                row.Children.Add(chip);
                used += width + 6;
            }
            if (row.Children.Count > 0) PnlLegend.Children.Add(row);
        }
        finally
        {
            _legendLayingOut = false;
        }
    }

    private void Legend_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 延迟到布局完成后再重排，避免 SizeChanged 重入时容器处于中间态
        DispatcherQueue.TryEnqueue(() => LayoutLegend());
    }

    private void RightScroll_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateChartHeight();

    /// <summary>
    /// 右列整体放在 ScrollViewer 里（窗口再矮也不会把图表挤出可视区），
    /// 这里让图表卡片在空间富余时自动长高、空间不足时退到最小高度，剩下的交给滚动。
    /// </summary>
    private void UpdateChartHeight()
    {
        var viewport = RightScroll.ViewportHeight;
        if (viewport <= 0) return;

        var chrome = PnlMeta.ActualHeight + PnlCards.ActualHeight + PnlTabs.ActualHeight
                     + LegendCard.ActualHeight + 4 * 10;
        var target = Math.Clamp(viewport - chrome, 280, 640);
        if (Math.Abs(ChartCard.Height - target) < 8) return;
        ChartCard.Height = target;
    }

    private void RenderStats()
    {
        if (_view is null)
        {
            StatsList.ItemsSource = null;
            return;
        }

        var rows = new List<StatRow>();
        foreach (var mv in VisibleMetrics())
        {
            var unit = string.IsNullOrEmpty(mv.Metric.Unit) ? "" : $" ({mv.Metric.Unit})";
            rows.Add(new StatRow
            {
                Metric = mv.Metric.Label + unit,
                Min = mv.Min,
                Avg = mv.Avg,
                Max = mv.Max,
                P1 = mv.P1,
                P99 = mv.P99,
                Count = mv.Count.ToString("N0", CultureInfo.InvariantCulture)
            });
        }
        StatsList.ItemsSource = rows;
    }

    // -------------------------------------------------------------- 交互

    private void ChartOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _view is null) return;
        RenderChart();
    }

    private void Range_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suspendRange || _view is null) return;

        if (SliderFrom.Value > SliderTo.Value)
        {
            _suspendRange = true;
            if (ReferenceEquals(sender, SliderFrom)) SliderTo.Value = SliderFrom.Value;
            else SliderFrom.Value = SliderTo.Value;
            _suspendRange = false;
        }

        UpdateRangeLabels();
        RenderChart();
    }

    private void ResetRange_Click(object sender, RoutedEventArgs e)
    {
        if (_view is null || _view.Times.Count == 0) return;
        _suspendRange = true;
        SliderFrom.Value = SliderFrom.Minimum;
        SliderTo.Value = SliderTo.Maximum;
        _suspendRange = false;
        UpdateRangeLabels();
        RenderChart();
    }

    private void SetupRange(int count)
    {
        _suspendRange = true;
        var max = Math.Max(0, count - 1);
        var step = Math.Max(1, Math.Ceiling(max / 200.0));
        SliderFrom.Minimum = 0;
        SliderFrom.Maximum = max;
        SliderFrom.StepFrequency = step;
        SliderFrom.Value = 0;
        SliderTo.Minimum = 0;
        SliderTo.Maximum = max;
        SliderTo.StepFrequency = step;
        SliderTo.Value = max;
        SliderFrom.IsEnabled = count > 1;
        SliderTo.IsEnabled = count > 1;
        _suspendRange = false;
        UpdateRangeLabels();
    }

    private void UpdateRangeLabels()
    {
        var times = _view?.Times;
        if (times is null || times.Count == 0)
        {
            TxtFrom.Text = "—";
            TxtTo.Text = "—";
            return;
        }

        var from = Math.Clamp((int)Math.Round(SliderFrom.Value), 0, times.Count - 1);
        var to = Math.Clamp((int)Math.Round(SliderTo.Value), 0, times.Count - 1);
        TxtFrom.Text = FormatTime(times[from]);
        TxtTo.Text = FormatTime(times[to]);
    }

    // -------------------------------------------------------------- 构建

    private void ClearPanes()
    {
        PnlMeta.Children.Clear();
        PnlCards.Children.Clear();
        PnlTabs.Children.Clear();
        PnlLegend.Children.Clear();
        _legendChips.Clear();
        _legendWidth = -1;
        StatsList.ItemsSource = null;
        Chart.Series = Array.Empty<ISeries>();
        _visibleTimes = [];
        TxtChartTitle.Text = "图表";
        TxtChartHint.Text = "";
        TxtFrom.Text = "—";
        TxtTo.Text = "—";
        DispatcherQueue.TryEnqueue(UpdateChartHeight);
    }

    private void ShowEmpty(string glyph, string message)
    {
        Chart.Series = Array.Empty<ISeries>();
        Chart.Visibility = Visibility.Collapsed;
        IcoEmpty.Glyph = glyph;
        TxtEmpty.Text = message;
        PnlChartEmpty.Visibility = Visibility.Visible;
    }

    private void AddChip(string glyph, string label, string value, bool warn = false)
    {
        var dark = ActualTheme == ElementTheme.Dark;
        var accent = warn ? Color.FromArgb(255, 0xD1, 0x8A, 0x00) : default;

        var chip = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 4, 11, 4),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(warn
                ? Color.FromArgb(90, accent.R, accent.G, accent.B)
                : dark ? Color.FromArgb(55, 0xFF, 0xFF, 0xFF) : Color.FromArgb(32, 0, 0, 0)),
            Background = new SolidColorBrush(dark
                ? Color.FromArgb(18, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(10, 0, 0, 0))
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(warn ? accent : (dark
                ? Color.FromArgb(255, 0xB0, 0xB0, 0xB0)
                : Color.FromArgb(255, 0x60, 0x60, 0x60)))
        });
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Opacity = 0.65,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        chip.Child = row;
        PnlMeta.Children.Add(chip);
    }

    private void AddCard(string label, string value, Color accent, string? detail = null)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 14, 8),
            BorderThickness = new Thickness(1),
            MinWidth = 108,
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, accent.R, accent.G, accent.B)),
            Background = new SolidColorBrush(Color.FromArgb(22, accent.R, accent.G, accent.B))
        };

        var stack = new StackPanel { Spacing = 1 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Opacity = 0.7,
            Foreground = new SolidColorBrush(accent)
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        if (!string.IsNullOrEmpty(detail))
            stack.Children.Add(new TextBlock { Text = detail, FontSize = 10.5, Opacity = 0.6 });
        card.Child = stack;
        PnlCards.Children.Add(card);
    }

    // -------------------------------------------------------------- 辅助

    private static List<T> Slice<T>(List<T> source, int from, int to)
    {
        if (from <= 0 && to >= source.Count - 1) return source;
        var count = Math.Min(to, source.Count - 1) - from + 1;
        if (count <= 0) return [];
        return source.GetRange(from, count);
    }

    /// <summary>把一组曲线压到统一的 0–100% 量程，便于跨量纲对比（null 保持为断点）。</summary>
    private static List<double?> Normalize(List<double?> values)
    {
        double min = double.MaxValue, max = double.MinValue;
        foreach (var v in values)
        {
            if (v is not { } d) continue;
            if (d < min) min = d;
            if (d > max) max = d;
        }

        var result = new List<double?>(values.Count);
        if (min == double.MaxValue || max - min < 1e-9)
        {
            foreach (var v in values) result.Add(v is null ? null : 50d);
            return result;
        }

        var span = max - min;
        foreach (var v in values)
            result.Add(v is { } d ? Math.Round((d - min) / span * 100, 2) : null);
        return result;
    }

    private static string FormatTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatAxisValue(double value)
    {
        var abs = Math.Abs(value);
        if (abs >= 10000) return (value / 1000).ToString("0", CultureInfo.InvariantCulture) + "k";
        if (abs >= 100) return value.ToString("0", CultureInfo.InvariantCulture);
        if (abs >= 10) return value.ToString("0.#", CultureInfo.InvariantCulture);
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatSize(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024.0 * 1024):0.0} MB"
        : bytes >= 1024
            ? $"{bytes / 1024.0:0} KB"
            : $"{bytes} B";

    private static SKColor Sk(Color c) => new(c.R, c.G, c.B, 255);

    private static SKColor SkA(Color c, byte alpha) => new(c.R, c.G, c.B, alpha);

    // -------------------------------------------------------------- 模型

    /// <summary>左侧列表的一条记录文件。</summary>
    public sealed class FileItem
    {
        public string Path { get; init; } = "";
        public string Name { get; init; } = "";
        public string Info { get; init; } = "";
    }

    /// <summary>右下角统计表的一行。</summary>
    public sealed class StatRow
    {
        public string Metric { get; init; } = "";
        public string Min { get; init; } = "--";
        public string Avg { get; init; } = "--";
        public string Max { get; init; } = "--";
        public string P1 { get; init; } = "--";
        public string P99 { get; init; } = "--";
        public string Count { get; init; } = "0";
    }

    private sealed record LegendItem(string Key, string Label, Color Color, bool Hidden);
}
