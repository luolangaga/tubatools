using System.Collections.ObjectModel;
using System.ComponentModel;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class WebsiteAccelerationPage : Page
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ObservableCollection<WebsiteConnectionRow> _connectionRows = [];
    private readonly IReadOnlyList<SteamAccelerationGroup> _siteGroups;
    private DateTimeOffset? _networkRun, _lastChartPoint;
    private bool _chartReady, _accelerationBusy, _probing;
    public WebsiteAccelerationPage()
    {
        InitializeComponent();
        _siteGroups = SteamAccelerationCatalog.Load();
        SiteGroups.ItemsSource = _siteGroups;
        foreach (var rule in _siteGroups.SelectMany(g => g.Rules)) rule.PropertyChanged += (_, _) => UpdateSelectionLabel();
        UpdateSelectionLabel();
        CatalogLabel.Text = $"Watt Toolkit 项目列表 · {_siteGroups.Count} 个分类 / {_siteGroups.Sum(g => g.Rules.Count)} 项服务 · 2026/11/01 快照";
        NetworkConnections.ItemsSource = _connectionRows;
        ActualThemeChanged += (_, _) => { _chartReady = false; _lastChartPoint = null; };
        _timer.Tick += (_, _) => RenderNetworkDashboard();
        Unloaded += (_, _) => _timer.Stop();
        Loaded += (_, _) => { _timer.Start(); AccelerationStatus.Text = SteamAccelerationService.Status; RenderNetworkDashboard(); };
    }
    private void PageViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (PageSurface != null) PageSurface.Width = Math.Max(0, Math.Min(1040, e.NewSize.Width));
        ResizeBody();
    }
    private void Body_SizeChanged(object sender, SizeChangedEventArgs e) => ResizeBody();
    private void ResizeBody()
    {
        if (SitesContent != null) SitesContent.Width = Math.Min(1040, Math.Max(0, SitesScroll.ActualWidth));
        if (DashboardCommands != null)
        {
            bool compact = SitesContent!.Width < 680;
            Grid.SetColumn(DashboardCommands, compact ? 0 : 1);
            Grid.SetRow(DashboardCommands, compact ? 1 : 0);
            Grid.SetColumnSpan(DashboardCommands, compact ? 2 : 1);
        }
    }
    private void Back_Click(object? sender, EventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }
    private void ShowError(Exception e) { Notice.Message = e.Message; Notice.Severity = InfoBarSeverity.Warning; Notice.IsOpen = true; }
    private void UpdateSelectionLabel()
    {
        if (SelectionLabel != null) SelectionLabel.Text = $"已选择 {_siteGroups.SelectMany(g => g.Rules).Count(r => r.IsSelected && r.Available)} / {_siteGroups.SelectMany(g => g.Rules).Count(r => r.Available)} 项可用服务";
    }

    private async void StartAcceleration_Click(object sender, RoutedEventArgs e)
    {
        if (_accelerationBusy) return;
        _accelerationBusy = true; StartAcceleration.IsEnabled = false;
        try { if (SteamAccelerationService.Running) await SteamAccelerationService.StopAsync(); await SteamAccelerationService.StartAsync(_siteGroups.SelectMany(g => g.Rules).Where(r => r.IsSelected && r.Available).ToList()); }
        catch (Exception ex) { ShowError(new InvalidOperationException("加速启动失败：" + ex.Message + "。请确认拥有管理员权限且 80/443 端口未被其他程序占用。")); }
        finally { AccelerationStatus.Text = SteamAccelerationService.Status; _accelerationBusy = false; StartAcceleration.IsEnabled = true; RenderNetworkDashboard(); }
    }

    private async void StopAcceleration_Click(object sender, RoutedEventArgs e)
    {
        if (_accelerationBusy) return;
        _accelerationBusy = true; StopAcceleration.IsEnabled = false; DashboardStopButton.IsEnabled = false;
        try { await SteamAccelerationService.StopAsync(); }
        catch (Exception ex) { ShowError(ex); }
        finally { AccelerationStatus.Text = SteamAccelerationService.Status; _accelerationBusy = false; StopAcceleration.IsEnabled = true; DashboardStopButton.IsEnabled = true; RenderNetworkDashboard(); }
    }

    private void RenderNetworkDashboard()
    {
        bool running = SteamAccelerationService.Running;
        NetworkConfiguration.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        NetworkDashboard.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        NetworkFailure.IsOpen = running && !string.IsNullOrEmpty(SteamAccelerationService.LastFailure);
        NetworkFailure.Message = SteamAccelerationService.LastFailure;
        if (!running || SitesScroll.Visibility != Visibility.Visible) return;
        var traffic = SteamAccelerationService.Traffic;
        if (_networkRun != traffic.StartedAt)
        {
            _networkRun = traffic.StartedAt; _lastChartPoint = null; _connectionRows.Clear();
        }
        NetworkUptime.Text = $"运行 {(long)(DateTimeOffset.Now - traffic.StartedAt).TotalMinutes} 分钟 · 每秒更新";
        NetworkDownloadRate.Text = SteamTrafficSession.FormatBytes(traffic.DownloadRate) + "/s";
        NetworkUploadRate.Text = SteamTrafficSession.FormatBytes(traffic.UploadRate) + "/s";
        NetworkDownloadTotal.Text = "累计下载 " + SteamTrafficSession.FormatBytes(traffic.Download);
        NetworkUploadTotal.Text = "累计上传 " + SteamTrafficSession.FormatBytes(traffic.Upload);
        NetworkRequestCounts.Text = $"{traffic.Requests} 次请求 · {traffic.Failures} 次失败";
        NetworkConnectionCount.Text = $"{traffic.ActiveConnections} 个活动连接";
        NetworkEmpty.Visibility = traffic.Connections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var ids = traffic.Connections.Select(c => c.Id).ToHashSet();
        for (int j = _connectionRows.Count - 1; j >= 0; j--) if (!ids.Contains(_connectionRows[j].Id)) _connectionRows.RemoveAt(j);
        foreach (var connection in traffic.Connections)
        {
            var existing = _connectionRows.FirstOrDefault(c => c.Id == connection.Id);
            if (existing != null) existing.Update(connection);
            else _connectionRows.Insert(0, new(connection));
        }
        if (!_chartReady) ConfigureNetworkChart();
        var point = traffic.History.LastOrDefault()?.Time;
        if (_lastChartPoint == point) return;
        _lastChartPoint = point;
        var series = ProxyTrafficChart.Series.Cast<LineSeries<DateTimePoint>>().ToArray();
        series[0].Values = traffic.History.Select(p => new DateTimePoint(p.Time.LocalDateTime, p.Download)).ToArray();
        series[1].Values = traffic.History.Select(p => new DateTimePoint(p.Time.LocalDateTime, p.Upload)).ToArray();
    }

    private void ConfigureNetworkChart()
    {
        static SKColor Sk(Windows.UI.Color c) => new(c.R, c.G, c.B);
        var text = Sk(ThemeColors.DimText);
        LineSeries<DateTimePoint> Series(string name, SKColor color) => new()
        {
            Name = name, GeometrySize = 0, LineSmoothness = 0,
            Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
            Fill = new SolidColorPaint(new SKColor(color.Red, color.Green, color.Blue, 22))
        };
        ProxyTrafficChart.Series = new[] { Series("下载", Sk(ThemeColors.Series3)), Series("上传", Sk(ThemeColors.Series1)) };
        ProxyTrafficChart.XAxes = new[] { new Axis
        {
            Labeler = v => v >= DateTime.MinValue.Ticks && v <= DateTime.MaxValue.Ticks ? new DateTime((long)v).ToString("HH:mm:ss") : "",
            UnitWidth = TimeSpan.FromSeconds(1).Ticks, MinStep = TimeSpan.FromSeconds(15).Ticks,
            TextSize = 11, LabelsPaint = new SolidColorPaint(text), SeparatorsPaint = null
        } };
        ProxyTrafficChart.YAxes = new[] { new Axis { MinLimit = 0, TextSize = 11,
            Labeler = v => SteamTrafficSession.FormatBytes(Math.Max(0, v)) + "/s",
            LabelsPaint = new SolidColorPaint(text), SeparatorsPaint = new SolidColorPaint(new SKColor(text.Red, text.Green, text.Blue, 30)) } };
        ProxyTrafficChart.AnimationsSpeed = TimeSpan.Zero;
        ProxyTrafficChart.EasingFunction = null;
        _chartReady = true;
    }

    private void SelectAllSites_Click(object sender, RoutedEventArgs e) { foreach (var rule in _siteGroups.SelectMany(g => g.Rules).Where(r => r.Available)) rule.IsSelected = true; }

    private void ClearSites_Click(object sender, RoutedEventArgs e) { foreach (var rule in _siteGroups.SelectMany(g => g.Rules)) rule.IsSelected = false; }

    private void SiteSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (SiteGroups == null) return;
        string query = SiteSearch.Text.Trim();
        SiteGroups.ItemsSource = _siteGroups.Select(g => new SteamAccelerationGroup(g.Name, g.Glyph, g.Rules.Where(r => g.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || r.DomainSummary.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList())).Where(g => g.Rules.Count > 0).ToList();
    }

    private async void Probe_Click(object sender, RoutedEventArgs e)
    {
        if (_probing) return;
        _probing = true;
        ProbeButton.IsEnabled = DashboardProbeButton.IsEnabled = false;
        ProbeResult.Text = DashboardProbeResult.Text = "正在检测三个网站…";
        try { ProbeResult.Text = DashboardProbeResult.Text = await SteamCompanionService.ProbeAsync(); }
        catch (Exception ex) { ShowError(ex); }
        finally { _probing = false; ProbeButton.IsEnabled = DashboardProbeButton.IsEnabled = true; }
    }
}

public sealed class WebsiteConnectionRow(SteamProxyConnection connection) : INotifyPropertyChanged
{
    private SteamProxyConnection _connection = connection;
    public long Id => _connection.Id;
    public string Host => _connection.Host;
    public string Detail => _connection.Detail;
    public string Upload => "↑ " + SteamTrafficSession.FormatBytes(_connection.Upload);
    public string Download => "↓ " + SteamTrafficSession.FormatBytes(_connection.Download);
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(SteamProxyConnection value)
    {
        if (_connection == value) return;
        _connection = value; PropertyChanged?.Invoke(this, new(string.Empty));
    }
}
