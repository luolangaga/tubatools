using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using SkiaSharp;
using TubaWinUi3.Services;
using Microsoft.UI.Text;
using Windows.Graphics;

namespace TubaWinUi3.Pages;

public sealed partial class TrafficMonitorPage : Page
{
    private const int ChartMaxPoints = 120;

    /// <summary>折线配色（系统图表系列色板，跟随主题）：下载用成功色系、上传用强调色系。</summary>
    private static SKColor DownloadC => ToSk(ThemeColors.Series3);
    private static SKColor UploadC => ToSk(ThemeColors.Series1);

    private static SKColor ToSk(Windows.UI.Color color) => new(color.R, color.G, color.B);

    private static readonly GridLength[] ColWidths =
    [
        new GridLength(1.7, GridUnitType.Star),
        new GridLength(2.4, GridUnitType.Star),
        new GridLength(1.0, GridUnitType.Star),
        new GridLength(1.0, GridUnitType.Star),
        new GridLength(1.0, GridUnitType.Star),
        new GridLength(1.0, GridUnitType.Star),
        new GridLength(0.9, GridUnitType.Star),
        new GridLength(0.85, GridUnitType.Star)
    ];

    private readonly ObservableCollection<double> _dlChart = [];
    private readonly ObservableCollection<double> _ulChart = [];
    private readonly Dictionary<string, ConnRow> _rows = [];
    private readonly Dictionary<string, (DateTime Time, long? Ms)> _latencyCache = [];
    private readonly HashSet<string> _pinging = [];
    private readonly TrafficSnapshotRecorder _recorder = new();

    private TrafficSnapshot? _lastSample;
    private bool _recording;
    private bool _reviewing;
    private bool _reviewChartFilled;
    private bool _sliderReady;
    private bool _disposed;

    public TrafficMonitorPage()
    {
        InitializeComponent();
        ChartInitializer.EnsureConfigured();

        InitChart();
        ConnHeaderGrid.Children.Add(MakeHeaderGrid());

        // 代码构建的画刷（图表系列色/行文本色）不会随主题自动刷新，切换主题后按缓存数据重渲染
        ActualThemeChanged += (_, _) =>
        {
            if (!IsLoaded) return;
            InitChart();
            if (_reviewing || _lastSample is not { } sample) return;
            ConnPanel.Children.Clear();
            _rows.Clear();
            UpdateCards(sample);
            UpdateConnections(sample);
        };
    }

    #region 生命周期

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _disposed = false;
        TrafficMonitorService.Tick += OnServiceTick;
        RefreshAdapters(preserveSelection: false);
        _sliderReady = true;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (App.IsLiteMode) return;

        TrafficMonitorService.Tick -= OnServiceTick;
        TrafficMonitorService.Stop();
        ClosePopoutWindows();
        _disposed = true;
    }

    private static void StopTrafficMonitoring() => TrafficMonitorService.Stop();

    private void MinimizeToTrayButton_Click(object sender, RoutedEventArgs e)
    {
        ClosePopoutWindows();
        App.IsLiteMode = true;
        TrayIconService.Show("流量监控器", StopTrafficMonitoring);
        App.MainWindow?.Close();
    }

    #endregion

    #region 网卡选择

    private void RefreshAdapters_Click(object sender, RoutedEventArgs e) => RefreshAdapters(preserveSelection: true);

    private void RefreshAdapters(bool preserveSelection)
    {
        var prevIndex = (AdapterCombo.SelectedItem as ComboBoxItem)?.Tag is AdapterInfo prev ? prev.Index : -1;

        AdapterCombo.Items.Clear();
        var adapters = NetworkAdapterProxyService.GetAdapters();
        foreach (var a in adapters)
        {
            AdapterCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{a.Name}  ·  {a.TypeLabel}  ·  {NetworkAdapterProxyService.FormatSpeed(a.Speed)}",
                Tag = a
            });
        }

        ComboBoxItem? pick = null;
        if (preserveSelection)
            pick = AdapterCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as AdapterInfo)?.Index == prevIndex);
        pick ??= AdapterCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => ((AdapterInfo)i.Tag).HasInternet);
        pick ??= AdapterCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => ((AdapterInfo)i.Tag).IsUp);

        if (pick is not null)
        {
            AdapterCombo.SelectedItem = pick;
        }
        else
        {
            TrafficMonitorService.Stop();
            AdapterStatusText.Text = "未检测到可用网卡";
        }
    }

    private void AdapterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AdapterCombo.SelectedItem is not ComboBoxItem { Tag: AdapterInfo adapter }) return;
        RestartForAdapter(adapter);
    }

    private void RestartForAdapter(AdapterInfo adapter)
    {
        TrafficMonitorService.Stop();

        _rows.Clear();
        ConnPanel.Children.Clear();
        _dlChart.Clear();
        _ulChart.Clear();
        _latencyCache.Clear();
        _pinging.Clear();
        _recorder.Clear();
        _lastSample = null;
        _recording = false;
        _reviewing = false;
        _reviewChartFilled = false;

        _sliderReady = false;
        SnapshotSlider.Value = 0;
        SnapshotSlider.Maximum = 0;
        SnapshotSlider.IsEnabled = false;
        _sliderReady = true;

        MarkerCanvas.Visibility = Visibility.Collapsed;
        ReviewBannerText.Visibility = Visibility.Collapsed;
        BackLiveBtn.Visibility = Visibility.Collapsed;
        ConnCountText.Text = "0 条";
        RecStatusText.Text = "未开始记录";
        UpdateRecordUi();

        AdapterStatusText.Text = $"{adapter.Description} · 速率 {NetworkAdapterProxyService.FormatSpeed(adapter.Speed)}";
        TrafficMonitorService.Start(adapter.Index);
    }

    #endregion

    #region 数据刷新（服务 Tick → UI）

    private void OnServiceTick(TrafficSnapshot sample) => DispatcherQueue.TryEnqueue(() => ApplySample(sample));

    private void ApplySample(TrafficSnapshot sample)
    {
        if (_disposed) return;
        _lastSample = sample;

        if (_recording)
        {
            _recorder.Add(sample);
            UpdateSliderMax();
            if (_reviewing) AppendReviewChart(sample);
        }
        UpdateRecordUi();

        if (_reviewing) return;

        UpdateCards(sample);
        UpdateChart(sample);
        UpdateConnections(sample);
    }

    private void UpdateCards(TrafficSnapshot s)
    {
        TotalInText.Text = NetworkAdapterProxyService.FormatBytes(s.TotalIn);
        TotalOutText.Text = NetworkAdapterProxyService.FormatBytes(s.TotalOut);
        SpeedInText.Text = $"{NetworkAdapterProxyService.FormatBytes(s.SpeedIn)}/s";
        SpeedOutText.Text = $"{NetworkAdapterProxyService.FormatBytes(s.SpeedOut)}/s";
    }

    private void UpdateChart(TrafficSnapshot s)
    {
        PushChart(_dlChart, s.SpeedIn / (double)(1 << 20));
        PushChart(_ulChart, s.SpeedOut / (double)(1 << 20));
    }

    private void PushChart(ObservableCollection<double> list, double value)
    {
        list.Add(value);
        if (list.Count > ChartMaxPoints) list.RemoveAt(0);
    }

    private void UpdateConnections(TrafficSnapshot s)
    {
        var seen = new HashSet<string>();
        foreach (var info in s.Connections)
        {
            seen.Add(info.Key);
            if (_rows.TryGetValue(info.Key, out var row))
            {
                // 域名可能异步解析出来后更新
                row.RemoteMainText.Text = RemoteMainText(info);
                row.RemoteSubText.Text = RemoteSubText(info);
                row.TotalInText.Text = NetworkAdapterProxyService.FormatBytes(info.TotalIn);
                row.TotalOutText.Text = NetworkAdapterProxyService.FormatBytes(info.TotalOut);
                row.SpeedInText.Text = $"{NetworkAdapterProxyService.FormatBytes(info.SpeedIn)}/s";
                row.SpeedOutText.Text = $"{NetworkAdapterProxyService.FormatBytes(info.SpeedOut)}/s";
            }
            else
            {
                AddRow(info, frozen: false);
            }
        }

        if (_rows.Count != seen.Count)
        {
            foreach (var key in _rows.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                ConnPanel.Children.Remove(_rows[key].Root);
                _rows.Remove(key);
            }
        }

        ConnCountText.Text = $"{_rows.Count} 条";
    }

    private void AddRow(TrafficConnectionInfo info, bool frozen)
    {
        var row = MakeRow(info, frozen);
        _rows[info.Key] = row;
        ConnPanel.Children.Add(row.Root);
    }

    #endregion

    #region 独立窗口

    private Window? _connWindow;
    private Window? _chartWindow;

    private void ConnPopOut_Click(object sender, RoutedEventArgs e)
    {
        if (_connWindow is not null) return;
        ConnContentHost.Children.Remove(ConnListContent);
        ConnListContent.RequestedTheme = ThemeService.CurrentElementTheme;

        _connWindow = CreatePopoutWindow("流量监控器 - 在线连接", 560, 760);
        _connWindow.Content = ConnListContent;
        _connWindow.Activate();

        ConnPopoutPlaceholder.Visibility = Visibility.Visible;
        ConnPopOutBtn.IsEnabled = false;
    }

    private void RestoreConnections_Click(object sender, RoutedEventArgs e) => RestoreConnections();

    private void RestoreConnections()
    {
        var w = _connWindow;
        _connWindow = null; // 先置空，避免 Closed 事件里递归关闭
        if (w is not null)
        {
            // 先清窗口 Content 槽位再回挂：槽位不清时 Content=null 会把刚挂回页面的
            // 元素从宿主里连带摘走（实测），回挂则必抛 0x800F1000（已由 ReattachToPage 兜住）
            w.Content = null;
            ReattachToPage(ConnListContent, ConnContentHost);
            w.Close();
        }
        else if (!ReattachToPage(ConnListContent, ConnContentHost))
        {
            return; // 摘回失败：内容仍留在独立窗口岛，保持占位提示与禁用弹出按钮
        }
        ConnPopoutPlaceholder.Visibility = Visibility.Collapsed;
        ConnPopOutBtn.IsEnabled = true;
    }

    private void ChartPopOut_Click(object sender, RoutedEventArgs e)
    {
        if (_chartWindow is not null) return;
        ChartContentHost.Children.Remove(ChartListContent);
        ChartListContent.RequestedTheme = ThemeService.CurrentElementTheme;

        _chartWindow = CreatePopoutWindow("流量监控器 - 吞吐折线", 640, 420);
        _chartWindow.Content = ChartListContent;
        _chartWindow.Activate();

        ChartPopoutPlaceholder.Visibility = Visibility.Visible;
        ChartPopOutBtn.IsEnabled = false;
    }

    private void RestoreChart_Click(object sender, RoutedEventArgs e) => RestoreChart();

    private void RestoreChart()
    {
        var w = _chartWindow;
        _chartWindow = null; // 先置空，避免 Closed 事件里递归关闭
        if (w is not null)
        {
            // 先清窗口 Content 槽位再回挂：槽位不清时 Content=null 会把刚挂回页面的
            // 元素从宿主里连带摘走（实测），回挂则必抛 0x800F1000（已由 ReattachToPage 兜住）
            w.Content = null;
            ReattachToPage(ChartListContent, ChartContentHost);
            w.Close();
        }
        else if (!ReattachToPage(ChartListContent, ChartContentHost))
        {
            return; // 摘回失败：内容仍留在独立窗口岛，保持占位提示与禁用弹出按钮
        }
        ChartPopoutPlaceholder.Visibility = Visibility.Collapsed;
        ChartPopOutBtn.IsEnabled = true;
    }

    private void ClosePopoutWindows()
    {
        var cw = _connWindow;
        _connWindow = null;
        if (cw is not null)
        {
            // 先清窗口 Content 槽位再回挂：槽位不清时 Content=null 会把刚挂回页面的
            // 元素从宿主里连带摘走（实测），回挂则必抛 0x800F1000（已由 ReattachToPage 兜住）
            cw.Content = null;
            ReattachToPage(ConnListContent, ConnContentHost);
            cw.Close();
        }
        var chw = _chartWindow;
        _chartWindow = null;
        if (chw is not null)
        {
            // 先清窗口 Content 槽位再回挂（见上）
            chw.Content = null;
            ReattachToPage(ChartListContent, ChartContentHost);
            chw.Close();
        }
        ConnPopoutPlaceholder.Visibility = Visibility.Collapsed;
        ChartPopoutPlaceholder.Visibility = Visibility.Collapsed;
        ConnPopOutBtn.IsEnabled = true;
        ChartPopOutBtn.IsEnabled = true;
    }

    /// <summary>把弹出窗口里的内容挂回页面宿主；已在宿主中则无事。返回是否已成功挂回。</summary>
    /// <remarks>
    /// 实测从独立窗口岛挂回页面面板时，Children.Add 会抛 REGDB_E_CLASSNOTREG
    /// （0x800F1000「没有检测到已安装的组件」）但元素实际已挂回，故吞掉异常后复查
    /// Parent 判断真实结果。调用前必须先清掉窗口的 Content 槽位（w.Content = null），
    /// 否则之后置空槽位会把刚挂回的从宿主里连带摘走。异常一律不许逃逸，避免打断导航/最小化。
    /// </remarks>
    private static bool ReattachToPage(FrameworkElement content, Panel host)
    {
        if (content.Parent is not null) return true;
        try
        {
            host.Children.Add(content);
        }
        catch (COMException ex)
        {
            Debug.WriteLine($"[TrafficMonitor] 收回弹出内容时抛错(可能已生效): {ex.Message}");
        }
        if (content.Parent is null) return false;
        try
        {
            content.RequestedTheme = ElementTheme.Default;
        }
        catch (COMException ex)
        {
            Debug.WriteLine($"[TrafficMonitor] 复位弹出内容主题失败: {ex.Message}");
        }
        return true;
    }

    private Window CreatePopoutWindow(string title, int width, int height)
    {
        var window = new Window();
        BackdropService.ApplyBackdrop(window); // 跟随用户选择的背景材质
        window.AppWindow.Title = title;
        window.AppWindow.Resize(new SizeInt32(width, height));
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }
        // 关闭窗口时自动收回内容
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(window, _connWindow)) { _connWindow = null; RestoreConnections(); }
            else if (ReferenceEquals(window, _chartWindow)) { _chartWindow = null; RestoreChart(); }
        };
        return window;
    }

    #endregion

    #region 录制与回放

    private void StartRecord_Click(object sender, RoutedEventArgs e)
    {
        _recorder.Clear();
        _recording = true;
        _reviewing = false;
        _reviewChartFilled = false;

        _sliderReady = false;
        SnapshotSlider.Value = 0;
        SnapshotSlider.Maximum = 0;
        SnapshotSlider.IsEnabled = false;
        _sliderReady = true;

        MarkerCanvas.Visibility = Visibility.Collapsed;
        ReviewBannerText.Visibility = Visibility.Collapsed;
        BackLiveBtn.Visibility = Visibility.Collapsed;
        RecStatusText.Text = "正在记录…";
        UpdateRecordUi();

        // 若此前在查看快照，立即恢复实时视图
        if (_lastSample is { } s)
        {
            UpdateCards(s);
            UpdateConnections(s);
        }
    }

    private void StopRecord_Click(object sender, RoutedEventArgs e)
    {
        _recording = false;
        RecStatusText.Text = _recorder.Count > 0
            ? $"已记录 {_recorder.Count} 条（至 {_recorder.Latest?.Time:HH:mm:ss}）"
            : "未开始记录";
        UpdateRecordUi();
    }

    private void ClearRecord_Click(object sender, RoutedEventArgs e)
    {
        _recorder.Clear();
        _recording = false;
        _reviewing = false;
        _reviewChartFilled = false;

        _sliderReady = false;
        SnapshotSlider.Value = 0;
        SnapshotSlider.Maximum = 0;
        SnapshotSlider.IsEnabled = false;
        _sliderReady = true;

        MarkerCanvas.Visibility = Visibility.Collapsed;
        ReviewBannerText.Visibility = Visibility.Collapsed;
        BackLiveBtn.Visibility = Visibility.Collapsed;
        RecStatusText.Text = "记录已清除";
        UpdateRecordUi();

        if (_lastSample is { } s)
        {
            UpdateCards(s);
            UpdateConnections(s);
        }
    }

    private void BackLive_Click(object sender, RoutedEventArgs e) => BackToLive();

    private void UpdateSliderMax()
    {
        var count = _recorder.Count;
        if (count == 0) return;

        SnapshotSlider.IsEnabled = true;
        SnapshotSlider.Maximum = count - 1;
        if (!_reviewing) SnapshotSlider.Value = count - 1;
        RecStatusText.Text = $"已记录 {count} 条 · {_recorder.Latest?.Time:HH:mm:ss}";
    }

    private void SnapshotSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_sliderReady) return;

        var count = _recorder.Count;
        if (count == 0) return;

        var idx = (int)Math.Round(e.NewValue);
        if (_recording && idx >= count - 1)
        {
            if (_reviewing) BackToLive();
            return;
        }

        if (!_recorder.TryGet(idx, out var snap)) return;
        EnterReview(idx, snap);
    }

    private void EnterReview(int idx, TrafficSnapshot snap)
    {
        _reviewing = true;

        if (!_reviewChartFilled)
        {
            FillReviewChart();
            _reviewChartFilled = true;
        }

        ShowSnapshot(idx, snap);
        // 先显示 Canvas 再定位：布局未就绪时由 SizeChanged 补定位
        MarkerCanvas.Visibility = Visibility.Visible;
        UpdateMarker(idx);
        ReviewBannerText.Visibility = Visibility.Visible;
        BackLiveBtn.Visibility = Visibility.Visible;
        RecStatusText.Text = $"快照 {snap.Time:HH:mm:ss} · 第 {idx + 1}/{_recorder.Count} 条";
    }

    private void BackToLive()
    {
        _reviewing = false;
        ReviewBannerText.Visibility = Visibility.Collapsed;
        BackLiveBtn.Visibility = Visibility.Collapsed;
        MarkerCanvas.Visibility = Visibility.Collapsed;

        if (_lastSample is { } s)
        {
            UpdateCards(s);
            UpdateConnections(s);
        }
        RecStatusText.Text = _recording
            ? $"已记录 {_recorder.Count} 条 · {_recorder.Latest?.Time:HH:mm:ss}"
            : $"已记录 {_recorder.Count} 条";
    }

    private void ShowSnapshot(int idx, TrafficSnapshot snap)
    {
        ConnPanel.Children.Clear();
        _rows.Clear();
        foreach (var info in snap.Connections) AddRow(info, frozen: true);
        ConnCountText.Text = $"快照 · {_rows.Count} 条";

        TotalInText.Text = NetworkAdapterProxyService.FormatBytes(snap.TotalIn);
        TotalOutText.Text = NetworkAdapterProxyService.FormatBytes(snap.TotalOut);
        SpeedInText.Text = $"{NetworkAdapterProxyService.FormatBytes(snap.SpeedIn)}/s";
        SpeedOutText.Text = $"{NetworkAdapterProxyService.FormatBytes(snap.SpeedOut)}/s";
    }

    private void FillReviewChart()
    {
        _dlChart.Clear();
        _ulChart.Clear();
        var count = _recorder.Count;
        for (int i = 0; i < count; i++)
        {
            if (!_recorder.TryGet(i, out var snap)) break;
            _dlChart.Add(snap.SpeedIn / (double)(1 << 20));
            _ulChart.Add(snap.SpeedOut / (double)(1 << 20));
        }
    }

    private void AppendReviewChart(TrafficSnapshot snap)
    {
        _dlChart.Add(snap.SpeedIn / (double)(1 << 20));
        _ulChart.Add(snap.SpeedOut / (double)(1 << 20));
    }

    private void UpdateMarker(int idx)
    {
        var count = _recorder.Count;
        if (count <= 1)
        {
            MarkerCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        var w = MarkerCanvas.ActualWidth;
        if (w <= 0) return; // 布局未就绪，等 SizeChanged 补定位
        Canvas.SetLeft(MarkerLine, Math.Max(0, w * idx / (count - 1) - 1));
    }

    private void MarkerCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Canvas 不参与布局，手动同步 Marker 竖线高度
        MarkerLine.Height = e.NewSize.Height;
        if (_reviewing) UpdateMarker((int)SnapshotSlider.Value);
    }

    private void UpdateRecordUi()
    {
        StartRecordBtn.IsEnabled = !_recording;
        StopRecordBtn.IsEnabled = _recording;
        ClearRecordBtn.IsEnabled = _recorder.Count > 0;
    }

    #endregion

    #region 连接行构建

    private static Grid MakeHeaderGrid()
    {
        var grid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var w in ColWidths) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });

        var cells = new[] { "进程", "远程地址:端口", "总下载", "总上传", "下载速度", "上传速度", "延迟", "操作" };
        for (int i = 0; i < cells.Length; i++)
        {
            var cell = new TextBlock
            {
                Text = cells[i],
                FontSize = 12,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        return grid;
    }

    private ConnRow MakeRow(TrafficConnectionInfo info, bool frozen)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        foreach (var w in ColWidths) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = w });

        var procName = new TextBlock
        {
            Text = info.ProcessName,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 150
        };
        var procPid = new TextBlock
        {
            Text = $"PID {info.ProcessId}",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        var procPanel = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        procPanel.Children.Add(procName);
        procPanel.Children.Add(procPid);

        var remoteText = new TextBlock
        {
            Text = RemoteMainText(info),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 240
        };
        var localText = new TextBlock
        {
            Text = RemoteSubText(info),
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var addrPanel = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        addrPanel.Children.Add(remoteText);
        addrPanel.Children.Add(localText);

        var totalIn = MakeValueText(NetworkAdapterProxyService.FormatBytes(info.TotalIn));
        var totalOut = MakeValueText(NetworkAdapterProxyService.FormatBytes(info.TotalOut));
        var speedIn = MakeValueText($"{NetworkAdapterProxyService.FormatBytes(info.SpeedIn)}/s");
        var speedOut = MakeValueText($"{NetworkAdapterProxyService.FormatBytes(info.SpeedOut)}/s");
        var latency = MakeValueText(LatencyTextFor(info.RemoteAddress));

        var pingBtn = new Button
        {
            Content = "测延迟",
            FontSize = 12,
            Padding = new Thickness(8, 4, 8, 4),
            IsEnabled = !frozen,
            Tag = info.RemoteAddress
        };
        pingBtn.Click += PingBtn_Click;

        Grid.SetColumn(procPanel, 0);
        Grid.SetColumn(addrPanel, 1);
        Grid.SetColumn(totalIn, 2);
        Grid.SetColumn(totalOut, 3);
        Grid.SetColumn(speedIn, 4);
        Grid.SetColumn(speedOut, 5);
        Grid.SetColumn(latency, 6);
        Grid.SetColumn(pingBtn, 7);

        grid.Children.Add(procPanel);
        grid.Children.Add(addrPanel);
        grid.Children.Add(totalIn);
        grid.Children.Add(totalOut);
        grid.Children.Add(speedIn);
        grid.Children.Add(speedOut);
        grid.Children.Add(latency);
        grid.Children.Add(pingBtn);

        return new ConnRow
        {
            Root = grid,
            RemoteIp = info.RemoteAddress,
            RemoteMainText = remoteText,
            RemoteSubText = localText,
            TotalInText = totalIn,
            TotalOutText = totalOut,
            SpeedInText = speedIn,
            SpeedOutText = speedOut,
            LatencyText = latency
        };
    }

    private static string RemoteMainText(TrafficConnectionInfo info)
        => info.RemoteDomain.Length > 0 ? $"{info.RemoteDomain}:{info.RemotePort}" : info.DisplayRemote;

    private static string RemoteSubText(TrafficConnectionInfo info)
    {
        var local = $"本地 {info.LocalAddress}:{info.LocalPort}";
        return info.RemoteDomain.Length > 0 ? $"{info.DisplayRemote} · {local}" : local;
    }

    private static TextBlock MakeValueText(string text) => new() { Text = text, FontSize = 12 };

    private string LatencyTextFor(string ip)
    {
        if (_latencyCache.TryGetValue(ip, out var c) && (DateTime.Now - c.Time).TotalSeconds < 15)
            return c.Ms is { } ms ? $"{ms} ms" : "超时";
        return "—";
    }

    private async void PingBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string ip }) return;
        if (!_pinging.Add(ip)) return; // 同一 IP 已有进行中的 ping，跳过

        foreach (var row in _rows.Values.Where(r => r.RemoteIp == ip))
            row.LatencyText.Text = "测试中…";

        var ms = await TrafficMonitorService.PingAsync(IPAddress.Parse(ip));
        _pinging.Remove(ip);
        _latencyCache[ip] = (DateTime.Now, ms);

        if (_disposed) return;
        foreach (var row in _rows.Values.Where(r => r.RemoteIp == ip))
            row.LatencyText.Text = ms is { } v ? $"{v} ms" : "超时";
    }

    private sealed class ConnRow
    {
        public required Grid Root { get; init; }
        public required string RemoteIp { get; init; }
        public required TextBlock RemoteMainText { get; init; }
        public required TextBlock RemoteSubText { get; init; }
        public required TextBlock TotalInText { get; init; }
        public required TextBlock TotalOutText { get; init; }
        public required TextBlock SpeedInText { get; init; }
        public required TextBlock SpeedOutText { get; init; }
        public required TextBlock LatencyText { get; init; }
    }

    #endregion

    #region 图表

    private void InitChart()
    {
        TrafficChart.Series =
        [
            MakeSeries(_dlChart, DownloadC),
            MakeSeries(_ulChart, UploadC)
        ];
        TrafficChart.XAxes = [new Axis { IsVisible = false }];
        TrafficChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0 }];
        TrafficChart.AnimationsSpeed = TimeSpan.FromMilliseconds(150);
        TrafficChart.EasingFunction = null;
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
            IsHoverable = true
        };
    }

    #endregion
}
