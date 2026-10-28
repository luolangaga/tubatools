using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Navigation;
using CommunityToolkit.WinUI.Controls;
using SkiaSharp;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TubaWinUi3.Pages;

public sealed partial class TrafficMonitorPage : Page
{
    private enum DisplayMode { Live, Paused, Review }
    private sealed record ChartSample(DateTime Time, long Download, long Upload);
    public ObservableCollection<TrafficMonitorRow> Rows { get; } = [];
    private readonly TrafficSnapshotRecorder _recorder = new();
    private readonly List<ChartSample> _history = [];
    private readonly Dictionary<string, TrafficMonitorRow> _rowCache = [];
    private TrafficSnapshot? _lastSample;
    private TrafficSnapshot? _displaySample;
    private DisplayMode _mode;
    private bool _ready, _disposed, _recording, _sliderReady, _selectingAdapter, _refreshing, _detailOpen;
    private int _adapterIndex = -1;
    private string _adapterDescription = "";
    private Window? _connWindow, _chartWindow;
    private CartesianChart? _popoutChart;
    private ChartSample[] _chartSamples = [];
    private ChartSample[] _pausedHistory = [];
    private bool _chartNeedsRecreate = true;

    public TrafficMonitorPage()
    {
        InitializeComponent();
        ChartInitializer.EnsureConfigured();
        foreach (var section in new[] { OverviewSection, ConnectionsSection, TrendSection, RecordingSection })
        {
            var saved = AppSettings.Get("TrafficMonitor_" + section.Name);
            if (bool.TryParse(saved, out var expanded)) section.IsExpanded = expanded;
        }
        OverviewSummary.Visibility = OverviewSection.IsExpanded ? Visibility.Collapsed : Visibility.Visible;
        Loaded += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => PageScroll.ChangeView(null, 0, null, true));
        ConfigureChart(TrafficChart);
        AttachChartLifecycle(TrafficChart);
        _ready = true;
        PageScroll.ViewChanged += (_, args) =>
        {
            if (!args.IsIntermediate && TrendSection.IsExpanded) QueueChartRedraw(TrafficChart);
        };
        ActualThemeChanged += (_, _) =>
        {
            if (!_ready) return;
            ConfigureChart(TrafficChart);
            if (_popoutChart is not null) ConfigureChart(_popoutChart);
            RefreshChart();
            if (_connWindow?.Content is FrameworkElement content) content.RequestedTheme = ActualTheme;
            if (_chartWindow?.Content is FrameworkElement chart) chart.RequestedTheme = ActualTheme;
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _disposed = false;
        TrafficMonitorService.Tick -= OnServiceTick;
        TrafficMonitorService.Tick += OnServiceTick;
        _ = RefreshAdaptersAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _disposed = true;
        TrafficMonitorService.Tick -= OnServiceTick;
        if (!App.IsLiteMode) TrafficMonitorService.Stop();
        ClosePopoutWindows();
    }

    private void MinimizeToTrayButton_Click(object sender, RoutedEventArgs e)
    {
        ClosePopoutWindows();
        App.IsLiteMode = true;
        TrayIconService.Show("流量监控器", TrafficMonitorService.Stop);
        App.MainWindow?.Close();
    }

    private async void RefreshAdapters_Click(object sender, RoutedEventArgs e) => await RefreshAdaptersAsync();

    private async Task RefreshAdaptersAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        RefreshAdaptersBtn.IsEnabled = false;
        try
        {
            var adapters = await Task.Run(NetworkAdapterProxyService.GetAdapters);
            if (_disposed) return;
            _selectingAdapter = true;
            AdapterCombo.Items.Clear();
            foreach (var a in adapters)
            {
                var item = new ComboBoxItem { Content = $"{a.Name} · {(a.IsUp ? "已连接" : "未连接")}", Tag = a };
                ToolTipService.SetToolTip(item, $"{a.Description}\n{string.Join(" / ", a.Addresses)}\n链路速率 {NetworkAdapterProxyService.FormatSpeed(a.Speed)}");
                AdapterCombo.Items.Add(item);
            }
            var items = AdapterCombo.Items.Cast<ComboBoxItem>().ToArray();
            var pick = items.FirstOrDefault(i => ((AdapterInfo)i.Tag).Index == _adapterIndex)
                ?? items.FirstOrDefault(i => ((AdapterInfo)i.Tag).HasInternet)
                ?? items.FirstOrDefault(i => ((AdapterInfo)i.Tag).IsUp)
                ?? items.FirstOrDefault();
            AdapterCombo.SelectedItem = pick;
            _selectingAdapter = false;
            if (pick?.Tag is AdapterInfo adapter)
            {
                if (_adapterIndex != adapter.Index || !TrafficMonitorService.IsRunning)
                    await SelectAdapterAsync(adapter);
            }
            else
            {
                TrafficMonitorService.Stop();
                _adapterIndex = -1;
                ResetSession();
                AdapterStatusText.Text = "未检测到网卡，请连接网络后刷新";
                EmptyText.Text = "没有可监控的网卡";
            }
        }
        catch (Exception ex) { ShowNotice("无法读取网卡：" + ex.Message, InfoBarSeverity.Error); }
        finally { _selectingAdapter = false; _refreshing = false; RefreshAdaptersBtn.IsEnabled = true; }
    }

    private async void AdapterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _selectingAdapter || AdapterCombo.SelectedItem is not ComboBoxItem { Tag: AdapterInfo adapter }) return;
        await SelectAdapterAsync(adapter);
    }

    private async Task SelectAdapterAsync(AdapterInfo adapter)
    {
        if (_adapterIndex == adapter.Index && TrafficMonitorService.IsRunning) return;
        if (_recorder.Count > 0)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "切换监控网卡？",
                Content = "切换会重新统计累计流量并清除当前记录。需要保留时，请先导出记录。",
                PrimaryButtonText = "切换并清除", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                _selectingAdapter = true;
                AdapterCombo.SelectedItem = AdapterCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => ((AdapterInfo)i.Tag).Index == _adapterIndex);
                _selectingAdapter = false;
                return;
            }
        }
        TrafficMonitorService.Stop();
        _adapterIndex = adapter.Index;
        _adapterDescription = $"{adapter.Description} · 链路 {NetworkAdapterProxyService.FormatSpeed(adapter.Speed)}";
        ResetSession();
        AdapterStatusText.Text = _adapterDescription + " · 等待采样";
        TrafficMonitorService.Start(adapter.Index);
    }

    private void ResetSession()
    {
        _lastSample = _displaySample = null;
        _history.Clear();
        _pausedHistory = [];
        Rows.Clear();
        _rowCache.Clear();
        _recorder.Clear();
        _recording = false;
        _mode = DisplayMode.Live;
        SpeedInText.Text = SpeedOutText.Text = "—";
        OverviewSummary.Text = "等待采样";
        TotalInText.Text = TotalOutText.Text = "本次累计 0 B";
        EmptyState.Visibility = Visibility.Visible;
        EmptyText.Text = "正在获取网络活动…";
        ConnCountText.Text = "等待连接…";
        NoticeBar.IsOpen = false;
        RefreshChart();
        UpdateRecordUi();
        UpdateDisplayMode();
    }

    private void OnServiceTick(TrafficSnapshot sample) => DispatcherQueue.TryEnqueue(() => ApplySample(sample));

    private void ApplySample(TrafficSnapshot sample)
    {
        if (_disposed || sample.AdapterIndex != _adapterIndex) return;
        if (sample.ErrorMessage.Length > 0)
        {
            AdapterStatusText.Text = sample.ErrorMessage;
            _lastSample = null;
            if (_mode == DisplayMode.Live)
            {
                _displaySample = null;
                SpeedInText.Text = SpeedOutText.Text = "—";
                Rows.Clear();
                _rowCache.Clear();
                EmptyState.Visibility = Visibility.Visible;
                EmptyText.Text = "网络暂不可用，恢复后将自动继续监控";
                ExportViewItem.IsEnabled = false;
                ConnCountText.Text = "等待网络恢复";
                ConnectionsSection.Description = ConnCountText.Text;
                OverviewSummary.Text = "等待网络恢复";
            }
            return;
        }
        _lastSample = sample;
        _history.Add(new(sample.Time, sample.SpeedIn, sample.SpeedOut));
        _history.RemoveAll(s => s.Time < sample.Time.AddMinutes(-5));
        if (_recording) _recorder.Add(sample);
        UpdateRecordUi();
        if (_mode != DisplayMode.Live) return;
        DisplaySnapshot(sample);
    }

    private void DisplaySnapshot(TrafficSnapshot sample)
    {
        _displaySample = sample;
        TotalInText.Text = "本次累计 " + FormatBytes(sample.TotalIn);
        TotalOutText.Text = "本次累计 " + FormatBytes(sample.TotalOut);
        SpeedInText.Text = FormatRate(sample.SpeedIn);
        SpeedOutText.Text = FormatRate(sample.SpeedOut);
        OverviewSummary.Text = $"↓ {FormatRate(sample.SpeedIn)}  ↑ {FormatRate(sample.SpeedOut)}";
        var state = _mode switch { DisplayMode.Paused => "画面已暂停", DisplayMode.Review => "记录回放", _ => "实时监控" };
        AdapterStatusText.Text = $"{_adapterDescription} · {state} · {sample.Time:HH:mm:ss}";
        RefreshRows();
        RefreshChart();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) RefreshRows(); }
    private void Search_TextChanged(object sender, TextChangedEventArgs e) { if (_ready) RefreshRows(); }
    private void ActiveOnly_Changed(object sender, RoutedEventArgs e) { if (_ready) RefreshRows(); }

    private void RefreshRows()
    {
        if (_displaySample is not { } sample) return;
        var entries = TrafficMonitorView.Build(sample.Connections, SearchBox.Text, ViewCombo.SelectedIndex == 0,
            ActiveOnlyBox.IsChecked == true, (TrafficSort)Math.Max(0, SortCombo.SelectedIndex));
        var wanted = entries.Select(e => e.Key).ToHashSet();
        for (int i = Rows.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Rows[i].Entry.Key)) { _rowCache.Remove(Rows[i].Entry.Key); Rows.RemoveAt(i); }
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!_rowCache.TryGetValue(entry.Key, out var row))
            {
                row = new TrafficMonitorRow(entry);
                _rowCache[entry.Key] = row;
                Rows.Insert(i, row);
            }
            else
            {
                row.Update(entry);
                var current = Rows.IndexOf(row);
                if (current != i) Rows.Move(current, i);
            }
        }
        var shownConnections = entries.Sum(e => e.Connections.Count);
        var count = Math.Max(sample.ConnectionCount, sample.Connections.Count);
        ConnCountText.Text = ViewCombo.SelectedIndex == 0
            ? $"{entries.Count} 个进程 · {shownConnections} / {count} 条连接"
            : $"{shownConnections} / {count} 条连接";
        ConnectionsSection.Description = ConnCountText.Text;
        EmptyState.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = sample.Connections.Count == 0 ? "当前网卡没有已建立的 TCP 连接" : "没有符合筛选条件的网络活动";
        var unavailable = sample.Connections.Count(c => !c.HasTrafficStats);
        ScopeText.Text = unavailable > 0
            ? $"{unavailable} 条连接统计不可用（—），可能需要管理员权限；带 * 的汇总仅包含可用统计。"
            : count > sample.Connections.Count
                ? $"当前展示活跃度最高的 {sample.Connections.Count} 条 / 共 {count} 条 TCP 连接；汇总基于展示范围。"
                : "明细仅含已建立的 TCP 连接；累计从连接启用统计起计算，整卡流量包含所有协议。";
        ExportViewItem.IsEnabled = shownConnections > 0;
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (_mode != DisplayMode.Live) { BackToLive(); return; }
        if (_displaySample is null) return;
        _mode = DisplayMode.Paused;
        _pausedHistory = _history.ToArray();
        UpdateDisplayMode();
        DisplaySnapshot(_displaySample);
    }

    private void BackLive_Click(object sender, RoutedEventArgs e) => BackToLive();
    private void BackToLive()
    {
        _mode = DisplayMode.Live;
        _pausedHistory = [];
        UpdateDisplayMode();
        if (_lastSample is not null) DisplaySnapshot(_lastSample);
        UpdateRecordUi();
    }

    private void UpdateDisplayMode()
    {
        PauseButton.Content = _mode == DisplayMode.Live ? "暂停显示" : "恢复实时";
        BackLiveBtn.Visibility = _mode == DisplayMode.Live ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_adapterIndex < 0) { ShowNotice("请先选择可用网卡", InfoBarSeverity.Warning); return; }
        _recording = !_recording;
        if (_recording && _lastSample is { } sample && _recorder.Latest?.Time != sample.Time) _recorder.Add(sample);
        UpdateRecordUi();
    }

    private async void ClearRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder.Count == 0) return;
        var result = await new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "清除流量记录？", Content = $"将清除当前 {_recorder.Count} 次采样。需要保留时，请先导出记录。",
            PrimaryButtonText = "清除记录", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
        }.ShowAsync();
        if (result != ContentDialogResult.Primary) return;
        _recording = false;
        _recorder.Clear();
        BackToLive();
    }

    private void UpdateRecordUi()
    {
        var count = _recorder.Count;
        RecordButton.Content = _recording ? "停止记录" : count > 0 ? "继续记录" : "开始记录";
        ClearRecordBtn.IsEnabled = ExportRecordItem.IsEnabled = count > 0;
        _sliderReady = false;
        SnapshotSlider.IsEnabled = count > 0;
        SnapshotSlider.Maximum = Math.Max(1, count - 1);
        if (_mode == DisplayMode.Live) SnapshotSlider.Value = Math.Max(0, count - 1);
        else if (_mode == DisplayMode.Review && _displaySample is { } shown)
        {
            var idx = Array.FindIndex(_recorder.ToArray(), s => s.Time == shown.Time);
            SnapshotSlider.Value = Math.Max(0, idx);
        }
        _sliderReady = true;
        RecStatusText.Text = count == 0 ? "尚未记录 · 后台约每秒采样一次，支持时间轴回放"
            : $"{(_recording ? "● 正在记录" : "记录已停止")} · {count} / {TrafficSnapshotRecorder.MaxSnapshots} 次采样 · 最新 {_recorder.Latest?.Time:HH:mm:ss}"
                + (_mode == DisplayMode.Review ? $" · 正在回放 {_displaySample?.Time:HH:mm:ss}" : " · 拖动时间轴回看");
        RecordingSection.Description = RecStatusText.Text;
    }

    private void SnapshotSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_ready || !_sliderReady || !_recorder.TryGet(Math.Min(_recorder.Count - 1, (int)Math.Round(e.NewValue)), out var sample)) return;
        _mode = DisplayMode.Review;
        UpdateDisplayMode();
        DisplaySnapshot(sample);
        UpdateRecordUi();
    }

    private int RangeSeconds => (ChartRangeCombo.SelectedItem as ComboBoxItem)?.Tag is string value && int.TryParse(value, out var seconds) ? seconds : 120;
    private void ChartRange_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) RefreshChart(); }
    private void RefreshChart()
    {
        IEnumerable<ChartSample> source = _mode == DisplayMode.Review
            ? _recorder.ToArray().Select(s => new ChartSample(s.Time, s.SpeedIn, s.SpeedOut))
            : _mode == DisplayMode.Paused ? _pausedHistory : _history;
        var end = _displaySample?.Time ?? DateTime.Now;
        _chartSamples = source.Where(s => s.Time <= end && s.Time >= end.AddSeconds(-RangeSeconds)).ToArray();
        SetChartValues(TrafficChart);
        if (_popoutChart is not null) SetChartValues(_popoutChart);
        ChartStatusText.Text = _chartSamples.Length == 0 ? "等待采样 · 整卡流量包含所有协议"
            : $"{_chartSamples[0].Time:HH:mm:ss} — {_chartSamples[^1].Time:HH:mm:ss} · 峰值 ↓ {FormatRate(_chartSamples.Max(s => s.Download))}  ↑ {FormatRate(_chartSamples.Max(s => s.Upload))}";
    }

    private static SKColor ToSk(Windows.UI.Color c) => new(c.R, c.G, c.B);
    private void ConfigureChart(CartesianChart chart)
    {
        var text = ToSk(ThemeColors.DimText);
        chart.Series = new[]
        {
            MakeSeries("下载", ToSk(ThemeColors.Series3)), MakeSeries("上传", ToSk(ThemeColors.Series1))
        };
        chart.XAxes = new[] { new Axis
        {
            Labeler = v => v >= DateTime.MinValue.Ticks && v <= DateTime.MaxValue.Ticks ? new DateTime((long)v).ToString("HH:mm:ss") : "",
            UnitWidth = TimeSpan.FromSeconds(1).Ticks, MinStep = TimeSpan.FromSeconds(30).Ticks,
            TextSize = 11, LabelsPaint = new SolidColorPaint(text), SeparatorsPaint = null
        } };
        chart.YAxes = new[] { new Axis
        {
            MinLimit = 0, TextSize = 11, LabelsPaint = new SolidColorPaint(text),
            Labeler = v => FormatRate((long)Math.Max(0, v)),
            SeparatorsPaint = new SolidColorPaint(new SKColor(text.Red, text.Green, text.Blue, 35)) { StrokeThickness = 1 }
        } };
        chart.AnimationsSpeed = TimeSpan.Zero;
        chart.EasingFunction = null;
    }

    private static LineSeries<DateTimePoint> MakeSeries(string name, SKColor color) => new()
    {
        Name = name, GeometrySize = 0, LineSmoothness = 0,
        Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
        Fill = new SolidColorPaint(new SKColor(color.Red, color.Green, color.Blue, 24))
    };

    private void SetChartValues(CartesianChart chart)
    {
        var series = chart.Series.Cast<LineSeries<DateTimePoint>>().ToArray();
        series[0].Values = _chartSamples.Select(s => new DateTimePoint(s.Time, s.Download)).ToArray();
        series[1].Values = _chartSamples.Select(s => new DateTimePoint(s.Time, s.Upload)).ToArray();
    }

    private void AttachChartLifecycle(CartesianChart chart)
    {
        chart.Loaded += (_, _) => QueueChartRedraw(chart);
        chart.SizeChanged += (_, _) => QueueChartRedraw(chart);
        chart.Unloaded += (_, _) =>
        {
            if (ReferenceEquals(chart, TrafficChart)) _chartNeedsRecreate = true;
        };
    }

    private void QueueChartRedraw(CartesianChart chart)
    {
        // SettingsExpander/ItemsRepeater 会卸载 Skia 子画布，即使外层仍 IsLoaded。
        // 仅对引擎调用 Load 不能恢复画布，必须在重新进入可见区域后创建新控件。
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_disposed || !chart.IsLoaded || chart.ActualWidth <= 0 || chart.ActualHeight <= 0) return;
            if (ReferenceEquals(chart, TrafficChart))
            {
                var bounds = chart.TransformToVisual(PageScroll).TransformBounds(new Windows.Foundation.Rect(0, 0, chart.ActualWidth, chart.ActualHeight));
                if (!TrendSection.IsExpanded || bounds.Bottom <= 0 || bounds.Top >= PageScroll.ActualHeight) return;
                if (_chartNeedsRecreate || !chart.CoreChart.IsLoaded)
                {
                    RecreateMainChart();
                    return;
                }
            }
            if (!chart.CoreChart.IsLoaded) chart.CoreChart.Load();
            chart.CoreChart.Update(new LiveChartsCore.Kernel.ChartUpdateParams
            {
                IsAutomaticUpdate = false,
                Throttling = false
            });
        });
    }

    private void RecreateMainChart()
    {
        var old = TrafficChart;
        if (old.Parent is not Grid host) return;
        var chart = new CartesianChart { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)) };
        ConfigureChart(chart);
        SetChartValues(chart);
        Grid.SetRow(chart, Grid.GetRow(old));
        host.Children.Remove(old);
        TrafficChart = chart;
        _chartNeedsRecreate = false;
        AttachChartLifecycle(chart);
        host.Children.Add(chart);
    }

    private void ConnPopOut_Click(object sender, RoutedEventArgs e)
    {
        if (_connWindow is not null) { _connWindow.Activate(); return; }
        // 独立视图共享数据，不跨 XamlRoot 搬运控件，避免关闭弹窗时的 COM 回挂错误。
        var list = new ListView
        {
            ItemsSource = Rows, SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true,
            ItemTemplate = (DataTemplate)Resources["TrafficRowTemplate"],
            ItemContainerStyle = (Style)Resources["TrafficListItemStyle"]
        };
        list.ItemClick += Connection_ItemClick;
        AutomationProperties.SetAutomationId(list, "TrafficPopoutList");
        AutomationProperties.SetName(list, "网络活动（跟随主页面筛选）");
        var grid = MakePopoutRoot();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(new TextBlock { Text = "网络活动 · 跟随主页面的筛选与回放", Margin = new Thickness(0, 0, 0, 12) });
        Grid.SetRow(list, 1);
        grid.Children.Add(list);
        _connWindow = CreatePopoutWindow("流量监控器 · 网络活动", grid, 820, 640);
        _connWindow.Closed += (_, _) => _connWindow = null;
        _connWindow.Activate();
    }

    private void ChartPopOut_Click(object sender, RoutedEventArgs e)
    {
        if (_chartWindow is not null) { _chartWindow.Activate(); return; }
        _popoutChart = new CartesianChart();
        ConfigureChart(_popoutChart);
        AttachChartLifecycle(_popoutChart);
        SetChartValues(_popoutChart);
        var root = MakePopoutRoot();
        root.Children.Add(_popoutChart);
        _chartWindow = CreatePopoutWindow("流量监控器 · 流量趋势（跟随主页面）", root, 800, 400);
        _chartWindow.Closed += (_, _) => { _popoutChart = null; _chartWindow = null; };
        _chartWindow.Activate();
    }

    private Grid MakePopoutRoot()
    {
        var root = (Grid)XamlReader.Load("<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{ThemeResource SolidBackgroundFillColorBaseBrush}'/>");
        root.Padding = new Thickness(16);
        root.RequestedTheme = ActualTheme;
        return root;
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    private static Window CreatePopoutWindow(string title, Grid root, int width, int height)
    {
        var window = new Window { Title = title, Content = root };
        BackdropService.ApplyBackdrop(window);
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window)) / 96d;
        window.AppWindow.Resize(new((int)(width * scale), (int)(height * scale)));
        return window;
    }

    private void ClosePopoutWindows() { _connWindow?.Close(); _chartWindow?.Close(); }

    private async void RowDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TrafficMonitorRow row } button) await ShowDetailsAsync(row.Entry, button.XamlRoot);
    }
    private async void Connection_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TrafficMonitorRow row && sender is FrameworkElement element) await ShowDetailsAsync(row.Entry, element.XamlRoot);
    }

    private async Task ShowDetailsAsync(TrafficViewEntry entry, XamlRoot root)
    {
        if (_detailOpen) return;
        _detailOpen = true;
        try
        {
            var details = new StringBuilder($"{entry.ProcessName} · PID {entry.ProcessId}\n{entry.Connections.Count} 条连接\n\n");
            foreach (var c in entry.Connections)
                details.AppendLine($"{c.Protocol}  {TrafficConnectionInfo.FormatEndpoint(c.LocalAddress, c.LocalPort)}\n→ {c.DisplayRemote}\n{c.RemoteDomain}\n↓ {(c.HasTrafficStats ? FormatRate(c.SpeedIn) : "统计不可用")}   ↑ {(c.HasTrafficStats ? FormatRate(c.SpeedOut) : "统计不可用")}\n");
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new ScrollViewer { MaxHeight = 320, Content = new TextBlock { Text = details.ToString(), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } });
            if (!entry.IsProcess && IPAddress.TryParse(entry.Connections[0].RemoteAddress, out var ip))
            {
                var ping = new Button { Content = "测试当前 ICMP 延迟" };
                AutomationProperties.SetAutomationId(ping, "TrafficPing");
                var resultText = new TextBlock { Text = "ICMP 无响应不代表连接异常；测试结果不属于历史快照。", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
                ping.Click += async (_, _) =>
                {
                    ping.IsEnabled = false;
                    resultText.Text = "正在测试…";
                    var ms = await TrafficMonitorService.PingAsync(ip);
                    resultText.Text = ms is { } v ? $"ICMP 往返延迟：{v} ms" : "未收到 ICMP 响应，目标可能禁用了 Ping。";
                    ping.IsEnabled = true;
                };
                panel.Children.Add(ping);
                panel.Children.Add(resultText);
            }
            var dialog = new ContentDialog
            {
                XamlRoot = root, Title = "网络活动详情", Content = panel,
                PrimaryButtonText = "只看此进程", SecondaryButtonText = "复制详情", CloseButtonText = "关闭"
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                ViewCombo.SelectedIndex = 1;
                SearchBox.Text = $"pid:{entry.ProcessId}";
            }
            else if (result == ContentDialogResult.Secondary)
            {
                var data = new DataPackage(); data.SetText(details.ToString()); Clipboard.SetContent(data);
                ShowNotice("连接详情已复制", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex) { ShowNotice("无法打开连接详情：" + ex.Message, InfoBarSeverity.Error); }
        finally { _detailOpen = false; }
    }

    private async void ExportView_Click(object sender, RoutedEventArgs e)
    {
        if (_displaySample is not { } sample) return;
        await SaveCsvAsync(TrafficMonitorView.ExportView(Rows.Select(r => r.Entry).ToArray(), sample.Time), "网络活动");
    }
    private async void ExportRecord_Click(object sender, RoutedEventArgs e)
        => await SaveCsvAsync(TrafficMonitorView.ExportRecording(_recorder.ToArray()), "流量记录");

    private async Task SaveCsvAsync(string csv, string prefix)
    {
        try
        {
            var path = Win32Dialogs.PickSave("CSV 表格\0*.csv\0\0", "csv", $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            if (path is null) return;
            await File.WriteAllTextAsync(path, csv, new UTF8Encoding(true));
            ShowNotice("已导出到 " + path, InfoBarSeverity.Success);
        }
        catch (Exception ex) { ShowNotice("导出失败：" + ex.Message, InfoBarSeverity.Error); }
    }

    private void ShowNotice(string text, InfoBarSeverity severity)
    {
        NoticeBar.Message = text; NoticeBar.Severity = severity; NoticeBar.IsOpen = true;
    }

    private void PageLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_ready) return;
        var narrow = e.NewSize.Width < 720;
        Grid.SetRow(SearchBox, narrow ? 1 : 0);
        Grid.SetColumn(SearchBox, narrow ? 0 : 1);
        Grid.SetColumnSpan(SearchBox, narrow ? 4 : 1);
        // 列表有明确高度，外层滚动不会把 ListView 无限测量，仍可虚拟化。
        ConnectionsBody.RowDefinitions[3].Height = new GridLength(Math.Clamp(e.NewSize.Height * 0.45, 200, 440));
        SpeedInText.FontSize = SpeedOutText.FontSize = e.NewSize.Width < 620 ? 24 : 28;
    }

    private void Section_StateChanged(object? sender, EventArgs e)
    {
        if (_ready && sender is SettingsExpander section)
        {
            AppSettings.Set("TrafficMonitor_" + section.Name, section.IsExpanded.ToString());
            OverviewSummary.Visibility = OverviewSection.IsExpanded ? Visibility.Collapsed : Visibility.Visible;
            if (ReferenceEquals(section, TrendSection) && section.IsExpanded)
            {
                _chartNeedsRecreate = true;
                RefreshChart();
                QueueChartRedraw(TrafficChart);
            }
        }
    }

    internal static string FormatBytes(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):0.##} GiB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):0.##} MiB";
        if (bytes >= 1L << 10) return $"{bytes / (double)(1L << 10):0.##} KiB";
        return $"{Math.Max(0, bytes)} B";
    }
    internal static string FormatRate(long bytes) => FormatBytes(bytes) + "/s";
}

public sealed partial class TrafficMonitorRow : INotifyPropertyChanged
{
    public TrafficViewEntry Entry { get; private set; }
    public TrafficMonitorRow(TrafficViewEntry entry) => Entry = entry;
    public string Title => $"{Entry.ProcessName} · {Entry.ProcessId}";
    public string Subtitle => Entry.IsProcess
        ? $"{Entry.Connections.Count} 条 TCP 连接" + (Entry.PartialStats ? " · 部分统计不可用" : !Entry.HasStats ? " · 统计不可用" : "")
        : $"{Entry.Connections[0].Protocol} · {(string.IsNullOrEmpty(Entry.Connections[0].RemoteDomain) ? Entry.Connections[0].DisplayRemote : Entry.Connections[0].RemoteDomain + " · " + Entry.Connections[0].DisplayRemote)}";
    public string Description => Title + "\n" + Subtitle;
    public string Download => Entry.HasStats ? TrafficMonitorPage.FormatRate(Entry.SpeedIn) + (Entry.PartialStats ? " *" : "") : "—";
    public string Upload => Entry.HasStats ? TrafficMonitorPage.FormatRate(Entry.SpeedOut) + (Entry.PartialStats ? " *" : "") : "—";
    public string DownloadTotal => Entry.HasStats ? TrafficMonitorPage.FormatBytes(Entry.TotalIn) : "统计不可用";
    public string UploadTotal => Entry.HasStats ? TrafficMonitorPage.FormatBytes(Entry.TotalOut) : "统计不可用";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(TrafficViewEntry entry) { Entry = entry; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("")); }
}
