using System.Globalization;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SkiaSharp;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

/// <summary>
/// 「记录查看」：解析游戏监控导出的 JSON / CSV，用工程内原生的 LiveCharts2 图表回放历史数据。
/// 每个指标一张独立图表、各自使用自己的纵轴刻度尺（100 FPS 与 1 ms 延迟不再互相压平），
/// 默认只展示 FPS；支持分组切换、区间裁剪、指标增删与全量统计（最小/平均/最大/P1/P99）。
/// </summary>
public sealed partial class GameMonitorRecordsPage : Page
{
    private const string AllGroups = "全部";

    /// <summary>同屏最多绘制的图表数量（一个指标一张图）。</summary>
    private const int MaxCharts = 6;

    /// <summary>曲线配色（系统图表系列色板，跟随主题；在浅色/深色背景下都可辨识）。</summary>
    private static Color[] Palette =>
    [
        ThemeColors.Series1,
        ThemeColors.Series2,
        ThemeColors.Series3,
        ThemeColors.Series4,
    ];

    private string? _initialFile;
    private string _dir = "";
    private bool _ready;

    private GameMonitorRecordReader.MonitorRecordView? _view;
    private readonly List<FileItem> _fileItems = [];

    /// <summary>勾选展示的指标 key：每个 key 对应一张独立图表（各自纵轴量程）。</summary>
    private readonly HashSet<string> _shown = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已创建的图表卡片，与勾选指标一一对应；拖动区间时只换数据，不重建控件。</summary>
    private readonly List<ChartSlot> _slots = [];

    private readonly List<string> _groups = [];
    private string _group = AllGroups;

    /// <summary>当前 X 轴刻度对应的真实秒数（随区间裁剪变化，所有图表共用）。</summary>
    private List<double> _visibleTimes = [];

    private bool _suspendRange;
    private int _loadToken;
    private readonly List<Border> _legendChips = [];
    private double _legendWidth = -1;

    public GameMonitorRecordsPage()
    {
        ChartInitializer.EnsureConfigured();
        InitializeComponent();
        // 代码构建的画刷不会随主题自动刷新，切换主题后按缓存数据重渲染
        ActualThemeChanged += (_, _) =>
        {
            if (_ready && _view is not null) RenderAll();
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
            // 遮罩在 XAML 里默认可见，只有 LoadFile 的 finally 会收起；
            // 空目录不会走 LoadFile，不在这里关闭就会永远停在「正在解析…」
            LoadingOverlay.Visibility = Visibility.Collapsed;
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
            _group = AllGroups;
            SelectDefaultMetrics();
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

    // ------------------------------------------------------------ 指标选择

    /// <summary>「默认展示 FPS」：记录里有 FPS 就只画 FPS，没有才退回第一条有数据的指标。</summary>
    private void SelectDefaultMetrics()
    {
        _shown.Clear();
        var key = GameMonitorRecordReader.PickDefaultMetricKey(VisibleMetrics());
        if (key is not null) _shown.Add(key);
    }

    /// <summary>切换分组时展示该组指标（一张图一个，超出上限的截断）。</summary>
    private void SelectGroupMetrics()
    {
        _shown.Clear();
        foreach (var key in GameMonitorRecordReader.PickMetricKeys(VisibleMetrics(), MaxCharts))
            _shown.Add(key);
    }

    private void ToggleMetric(string key)
    {
        if (_shown.Remove(key))
        {
            RenderCharts();
            return;
        }

        if (_shown.Count >= MaxCharts)
        {
            TxtStatus.Text = $"最多同时显示 {MaxCharts} 个指标，请先取消一个再添加";
            return;
        }

        _shown.Add(key);
        RenderCharts();
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
                Padding = new Thickness(12, 4, 12, 4),
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
                _group = captured;
                SelectGroupMetrics();
                BuildTabs();
                RenderCards();
                RenderCharts();
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
        RenderCharts();
        RenderStats();
        RequestLayoutUpdate();
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

        AddCard("记录时长", GameMonitorRecorder.FormatDuration(duration), ThemeColors.Series1);
        AddCard("采样点", _view.Data.Samples.Count.ToString("N0", CultureInfo.InvariantCulture),
            ThemeColors.Series2, $"抽稀步长 {_view.Step}");
        AddCard("采样间隔", meta.IntervalMs > 0 ? $"{meta.IntervalMs:0} ms" : "—", ThemeColors.Series1);

        // 当前分组的关键指标（取第一个有数据的指标作为头条）
        var metrics = VisibleMetrics();
        var headline = metrics.FirstOrDefault(m => m.HasData);
        if (headline is not null)
        {
            var unit = string.IsNullOrEmpty(headline.Metric.Unit) ? "" : " " + headline.Metric.Unit;
            AddCard(headline.Metric.Label, headline.Avg.Trim() + unit, ThemeColors.Series3, $"最大 {headline.Max.Trim()}{unit}");
        }

        var count = metrics.Count;
        AddCard("本组指标", count.ToString(CultureInfo.InvariantCulture), ThemeColors.Series2);
    }

    private void RenderCharts()
    {
        if (_view is null || _view.Times.Count == 0)
        {
            ClearCharts();
            ShowEmpty("\uE9D2", "没有可展示的数据");
            return;
        }

        var metrics = VisibleMetrics();

        // 一张图一个指标：勾选项按分组顺序落成图表卡片
        var chosen = new List<(GameMonitorRecordReader.MonitorMetricView Metric, Color Color)>();
        var legend = new List<LegendItem>();
        for (var i = 0; i < metrics.Count; i++)
        {
            var mv = metrics[i];
            var color = Palette[i % Palette.Length];
            var on = mv.HasData && _shown.Contains(mv.Metric.Key) && chosen.Count < MaxCharts;
            if (on) chosen.Add((mv, color));
            legend.Add(new LegendItem(mv.Metric.Key, mv.Metric.Label, color, on, mv.HasData));
        }

        var count = _view.Times.Count;
        var from = Math.Clamp((int)Math.Round(SliderFrom.Value), 0, count - 1);
        var to = Math.Clamp((int)Math.Round(SliderTo.Value), 0, count - 1);
        if (to < from) (from, to) = (to, from);

        _visibleTimes = Slice(_view.Times, from, to);

        SyncSlots(chosen.Count);
        for (var i = 0; i < chosen.Count; i++)
            RenderSlot(_slots[i], chosen[i].Metric, chosen[i].Color, from, to);

        BuildLegend(legend);
        BuildHint(chosen.Count, metrics.Count);

        if (chosen.Count == 0)
        {
            ShowEmpty("\uE9D2", metrics.Count == 0
                ? "当前分组没有可展示的指标"
                : "未选择指标。\n点击上方指标标签即可添加对应的图表。");
        }
        else
        {
            PnlChartEmpty.Visibility = Visibility.Collapsed;
            PnlCharts.Visibility = Visibility.Visible;
        }

        RequestLayoutUpdate();
    }

    private void BuildHint(int chartCount, int metricCount)
    {
        TxtChartTitle.Text = _group == AllGroups ? "全部指标" : _group;
        TxtChartHint.Text = metricCount == 0
            ? "这份记录里没有可展示的指标"
            : chartCount == 0
                ? $"点击下方指标标签添加图表（一个指标一张图，最多同时 {MaxCharts} 个）"
                : $"{chartCount} 张图表 · 每个指标独立纵轴量程 · {_visibleTimes.Count:N0} 个点 · 点击标签增减"
                  + (chartCount >= MaxCharts && metricCount > MaxCharts ? $"（已达 {MaxCharts} 张上限）" : "");
    }

    /// <summary>按需增删图表卡片控件（指标增删时才动控件树）。</summary>
    private void SyncSlots(int count)
    {
        while (_slots.Count > count)
        {
            PnlCharts.Children.Remove(_slots[^1].Card);
            _slots.RemoveAt(_slots.Count - 1);
        }

        while (_slots.Count < count)
        {
            var slot = CreateSlot();
            PnlCharts.Children.Add(slot.Card);
            _slots.Add(slot);
        }
    }

    private void RenderSlot(ChartSlot slot, GameMonitorRecordReader.MonitorMetricView mv, Color color, int from, int to)
    {
        var text = ThemeColors.SecondaryText;
        var grid = new SolidColorPaint(SkA(text, 40));
        var unit = string.IsNullOrEmpty(mv.Metric.Unit) ? "" : " " + mv.Metric.Unit;

        slot.Title.Text = mv.Metric.Label + unit;
        slot.Title.Foreground = new SolidColorBrush(color);
        slot.Stats.Text = $"最大 {mv.Max.Trim()}{unit} · 平均 {mv.Avg.Trim()}{unit} · 最小 {mv.Min.Trim()}{unit}"
                          + $" · P1 {mv.P1.Trim()} · P99 {mv.P99.Trim()} · {mv.Count:N0} 样本";

        var fill = ChkFill.IsChecked == true;
        slot.Chart.Series = new List<ISeries>
        {
            new LineSeries<double?>
            {
                Name = mv.Metric.Label,
                Values = Slice(mv.Values, from, to),
                Stroke = new SolidColorPaint(Sk(color)) { StrokeThickness = 1.8f },
                Fill = fill ? new SolidColorPaint(SkA(color, 26)) : null,
                GeometrySize = 0,
                LineSmoothness = 0.15,
                IsHoverable = true
            }
        };
        slot.Chart.XAxes = new List<Axis> { CreateTimeAxis(text, grid) };
        slot.Chart.YAxes = new List<Axis> { CreateValueAxis(text, grid) };
    }

    /// <summary>创建一张指标图表卡片（标题 + 独立纵轴的图表）。</summary>
    private ChartSlot CreateSlot()
    {
        var title = new TextBlock
        {
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var stats = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(title);
        Grid.SetColumn(stats, 1);
        header.Children.Add(stats);

        var chart = new CartesianChart
        {
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            LegendPosition = LegendPosition.Hidden,
            // 区间拖动会连续换数据，动画只会拖后腿
            AnimationsSpeed = TimeSpan.Zero,
            EasingFunction = null
        };

        var body = new Grid { RowSpacing = 8 };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(header);
        Grid.SetRow(chart, 1);
        body.Children.Add(chart);

        var card = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Height = 180,
            Child = body
        };
        if (Application.Current.Resources.TryGetValue("FluentCardStyle", out var cardStyle) && cardStyle is Style fluentCard)
            card.Style = fluentCard;

        return new ChartSlot { Card = card, Title = title, Stats = stats, Chart = chart };
    }

    private Axis CreateTimeAxis(Color text, SolidColorPaint grid)
    {
        var step = Math.Max(1, (int)Math.Ceiling(_visibleTimes.Count / 8.0));
        return new Axis
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
        };
    }

    /// <summary>纵轴不设上下限：每个指标按自己的量程自适应，最高点即刻度尺顶部。</summary>
    private static Axis CreateValueAxis(Color text, SolidColorPaint grid) => new()
    {
        Labeler = FormatAxisValue,
        LabelsPaint = new SolidColorPaint(Sk(text)),
        SeparatorsPaint = grid,
        TextSize = 10,
        TicksPaint = null,
        ShowSeparatorLines = true
    };

    private void BuildLegend(List<LegendItem> items)
    {
        _legendChips.Clear();

        foreach (var item in items)
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 4, 12, 4),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
                Background = new SolidColorBrush(item.On ? ThemeColors.SubtleBg : ThemeColors.SubtleBgHover),
                Opacity = item.Enabled ? item.On ? 1 : 0.55 : 0.25,
                Tag = item.Key
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(item.Color),
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock { Text = item.Label, FontSize = 12 });
            chip.Child = row;

            var key = item.Key;
            if (!item.Enabled)
            {
                ToolTipService.SetToolTip(chip, "该指标在这份记录里没有数据");
            }
            else
            {
                ToolTipService.SetToolTip(chip, item.On ? "点击移除这张图表" : "点击添加这张图表");
                chip.Tapped += (_, _) => ToggleMetric(key);
            }
            _legendChips.Add(chip);
        }

        _legendWidth = -1;
        LayoutLegend();
    }

    /// <summary>按可用宽度把指标标签排成多行（WinUI 没有现成的 WrapPanel，这里手动分行）。</summary>
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

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
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
                    row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    used = 0;
                }
                row.Children.Add(chip);
                used += width + 8;
            }
            if (row.Children.Count > 0) PnlLegend.Children.Add(row);
        }
        finally
        {
            _legendLayingOut = false;
        }
    }

    private bool _layoutUpdateQueued;

    /// <summary>
    /// 把「跟随窗口尺寸的重排」并成一次、延后到本轮布局结束之后再执行。
    /// 在 SizeChanged 回调里直接动控件树（改图表高度、重排图例）等于在布局过程中重入布局，
    /// 而拖动窗口一秒能触发几十次 —— 实测这种重入会让进程被系统直接带走
    /// （事件日志 0xc000027b，故障模块 CoreMessagingXP/Microsoft.UI.Xaml，错误码 800f1000/80004003/80070057）。
    /// </summary>
    private void RequestLayoutUpdate()
    {
        if (_layoutUpdateQueued) return;
        _layoutUpdateQueued = true;

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _layoutUpdateQueued = false;

            // 窗口已关闭 / 页面已卸载：控件树已经断开，再动它只会抛异常
            if (!IsLoaded) return;

            try
            {
                UpdateChartHeight();
                LayoutLegend();
            }
            catch (Exception ex)
            {
                // 重排失败最多是图不好看，绝不能让异常从回调里逃出去把进程带崩
                System.Diagnostics.Debug.WriteLine($"[GameMonitorRecords] 尺寸变化重排失败（已忽略）: {ex.Message}");
            }
        });
    }

    private void Legend_SizeChanged(object sender, SizeChangedEventArgs e) => RequestLayoutUpdate();

    private void RightScroll_SizeChanged(object sender, SizeChangedEventArgs e) => RequestLayoutUpdate();

    /// <summary>
    /// 右列整体放在 ScrollViewer 里（窗口再矮也不会把图表挤出可视区），
    /// 这里让每张图表按剩余空间和图表数量自动定高，放不下的交给滚动。
    /// </summary>
    private void UpdateChartHeight()
    {
        var viewport = RightScroll.ViewportHeight;
        if (viewport <= 0 || _slots.Count == 0) return;

        var chrome = PnlMeta.ActualHeight + PnlCards.ActualHeight + PnlTabs.ActualHeight
                     + ControlCard.ActualHeight + StatsCard.ActualHeight + 6 * 10;
        var target = Math.Clamp((viewport - chrome) / Math.Min(_slots.Count, 3), 132, 320);
        foreach (var slot in _slots)
        {
            if (Math.Abs(slot.Card.Height - target) < 6) continue;
            slot.Card.Height = target;
        }
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
        RenderCharts();
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
        RenderCharts();
    }

    private void ResetRange_Click(object sender, RoutedEventArgs e)
    {
        if (_view is null || _view.Times.Count == 0) return;
        _suspendRange = true;
        SliderFrom.Value = SliderFrom.Minimum;
        SliderTo.Value = SliderTo.Maximum;
        _suspendRange = false;
        UpdateRangeLabels();
        RenderCharts();
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
        ClearCharts();
        TxtChartTitle.Text = "图表";
        TxtChartHint.Text = "";
        TxtFrom.Text = "—";
        TxtTo.Text = "—";
        RequestLayoutUpdate();
    }

    private void ClearCharts()
    {
        PnlCharts.Children.Clear();
        _slots.Clear();
        _visibleTimes = [];
    }

    private void ShowEmpty(string glyph, string message)
    {
        ClearCharts();
        PnlCharts.Visibility = Visibility.Collapsed;
        IcoEmpty.Glyph = glyph;
        TxtEmpty.Text = message;
        PnlChartEmpty.Visibility = Visibility.Visible;
    }

    private void AddChip(string glyph, string label, string value, bool warn = false)
    {
        var accent = warn ? ThemeColors.AccentOrange : default;

        var chip = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 12, 4),
            BorderThickness = new Thickness(1),
            BorderBrush = warn
                ? new SolidColorBrush(Color.FromArgb(90, accent.R, accent.G, accent.B))
                : new SolidColorBrush(ThemeColors.BorderColor),
            Background = new SolidColorBrush(warn
                ? Color.FromArgb(22, accent.R, accent.G, accent.B)
                : ThemeColors.SubtleBg)
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(warn ? accent : ThemeColors.DimText)
        });
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
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
            Padding = new Thickness(12, 8, 12, 8),
            BorderThickness = new Thickness(1),
            MinWidth = 108,
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, accent.R, accent.G, accent.B)),
            Background = new SolidColorBrush(Color.FromArgb(22, accent.R, accent.G, accent.B))
        };

        var stack = new StackPanel { Spacing = 1 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = new SolidColorBrush(accent)
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        if (!string.IsNullOrEmpty(detail))
            stack.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText) });
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

    /// <summary>一张指标图表卡片（一个指标对应一张图，纵轴各自独立）。</summary>
    private sealed class ChartSlot
    {
        public required Border Card { get; init; }
        public required TextBlock Title { get; init; }
        public required TextBlock Stats { get; init; }
        public required CartesianChart Chart { get; init; }
    }

    private sealed record LegendItem(string Key, string Label, Color Color, bool On, bool Enabled);
}
