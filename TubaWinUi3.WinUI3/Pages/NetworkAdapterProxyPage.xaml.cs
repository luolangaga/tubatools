using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Net.NetworkInformation;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class NetworkAdapterProxyPage : Page
{
    private sealed class AdapterCardRefs
    {
        public TextBlock? IpText;
        public TextBlock? GwText;
        public TextBlock? StatusText;
        public Border? StatusBadge;
        public Border? LeftBar;
        public FontIcon? Icon;
        public Border? IconBg;
        public TextBlock? SpeedLabel;
    }

    private sealed class SpeedRefs
    {
        public TextBlock? DlText;
        public TextBlock? UlText;
        public ProgressBar? DlBar;
        public ProgressBar? UlBar;
    }

    // 语义状态色（跟随系统主题，经 ThemeColors 取官方语义色）
    private static Color AccentBlue => ThemeColors.AccentBlue;
    private static Color AccentGreen => ThemeColors.AccentGreen;
    private static Color AccentOrange => ThemeColors.AccentOrange;
    private static Color AccentRed => ThemeColors.AccentRed;

    private List<AdapterInfo> _adapters = [];
    private DispatcherTimer? _refreshTimer;
    private readonly Dictionary<int, SpeedRefs> _speedRefs = new();
    private readonly Dictionary<int, AdapterCardRefs> _cardRefs = new();
    private bool _cardsBuilt;
    private bool _monitoring = true;
    private bool _autoScheduling = true;
    private bool _initialized;

    public NetworkAdapterProxyPage()
    {
        InitializeComponent();
        // 代码构建的画刷不会随主题自动刷新，切换主题后按缓存数据重渲染
        ActualThemeChanged += (_, _) =>
        {
            if (_cardsBuilt)
            {
                BuildAdapterCards();
                BuildSpeedPanel();
                UpdateAdapterCards();
            }
        };

        NetworkAdapterProxyService.StatsUpdated += OnStatsUpdated;
        NetworkAdapterProxyService.ScheduleUpdated += OnScheduleUpdated;
        Unloaded += OnPageUnloaded;

        _ = InitializeAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e) => Teardown();

    private void Teardown()
    {
        if (App.IsLiteMode) return;

        StopServices();
        _refreshTimer?.Stop();
        NetworkAdapterProxyService.StatsUpdated -= OnStatsUpdated;
        NetworkAdapterProxyService.ScheduleUpdated -= OnScheduleUpdated;
    }

    private static void StopServices()
    {
        NetworkAdapterProxyService.StopAutoScheduling();
        NetworkAdapterProxyService.StopMonitoring();
    }

    private void MinimizeToTrayButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;

        App.IsLiteMode = true;
        TrayIconService.Show("网络调度器", StopServices);
        App.MainWindow?.Close();
    }

    private void MonitorToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _monitoring = MonitorToggle.IsOn;
        if (_monitoring)
        {
            NetworkAdapterProxyService.StartMonitoring(2000);
            _refreshTimer?.Start();
        }
        else
        {
            NetworkAdapterProxyService.StopMonitoring();
            _refreshTimer?.Stop();
        }
    }

    private void AutoScheduleToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _autoScheduling = AutoScheduleToggle.IsOn;
        if (_autoScheduling)
        {
            NetworkAdapterProxyService.StartAutoScheduling(5000);
            ScheduleStatusText.Text = "每 5 秒根据实时负载自动调整路由优先级";
        }
        else
        {
            NetworkAdapterProxyService.StopAutoScheduling();
            ScheduleStatusText.Text = "自动调度已关闭，路由优先级保持不变";
            ScheduleEntriesPanel.Children.Clear();
            ScheduleSummaryText.Text = "";
        }
    }

    private void OnScheduleUpdated(AutoScheduleResult result)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_initialized) RenderSchedule(result);
        });
    }

    private void RenderSchedule(AutoScheduleResult result)
    {
        ScheduleEntriesPanel.Children.Clear();

        foreach (var entry in result.Entries.OrderByDescending(e => e.LoadPercent))
        {
            var accent = entry.LoadPercent > 70 ? AccentRed
                       : entry.LoadPercent > 40 ? AccentOrange
                       : AccentGreen;

            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            var nameText = new TextBlock
            {
                Text = entry.Name, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
                VerticalAlignment = VerticalAlignment.Center
            };

            var loadBadge = new Border
            {
                Style = (Style)Application.Current.Resources["StatusPillStyle"],
                Background = new SolidColorBrush(Color.FromArgb(25, accent.R, accent.G, accent.B)),
                Child = new TextBlock
                {
                    Text = $"{entry.LoadPercent:0.0}%", FontSize = 12,
                    Foreground = new SolidColorBrush(accent),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            };

            var bar = new ProgressBar
            {
                Value = Math.Min(100, entry.LoadPercent),
                Foreground = new SolidColorBrush(accent),
                Background = new SolidColorBrush(Color.FromArgb(20, accent.R, accent.G, accent.B)),
                Height = 4
            };

            row.Children.Add(nameText);
            row.Children.Add(loadBadge); Grid.SetColumn(loadBadge, 1);
            row.Children.Add(bar); Grid.SetColumn(bar, 2);

            ScheduleEntriesPanel.Children.Add(row);
        }

        ScheduleSummaryText.Text = result.Summary;
    }

    private async Task InitializeAsync()
    {
        _adapters = await Task.Run(() => NetworkAdapterProxyService.GetAdapters());
        BuildAdapterCards();
        BuildSpeedPanel();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += async (_, _) =>
        {
            _adapters = await Task.Run(() => NetworkAdapterProxyService.GetAdapters());
            if (!_cardsBuilt || _cardRefs.Count != _adapters.Count)
            {
                BuildAdapterCards();
                BuildSpeedPanel();
            }
            else
            {
                UpdateAdapterCards();
            }
        };
        _refreshTimer.Start();

        NetworkAdapterProxyService.StartMonitoring(2000);

        if (_autoScheduling)
            NetworkAdapterProxyService.StartAutoScheduling(5000);

        _initialized = true;

        _ = LoadConnectionSummaryAsync();
    }

    private async Task LoadConnectionSummaryAsync()
    {
        ConnLoadingBar.Visibility = Visibility.Visible;
        var conns = await Task.Run(() => NetworkAdapterProxyService.GetActiveConnections());
        DispatcherQueue.TryEnqueue(() =>
        {
            ConnLoadingBar.Visibility = Visibility.Collapsed;
            if (conns.Count == 0)
            {
                ConnSummaryText.Text = "暂无已建立的 TCP 连接";
            }
            else
            {
                var wifiCount = conns.Count(c => c.AdapterType == "Wi-Fi");
                var wiredCount = conns.Count(c => c.AdapterType == "有线");
                var otherCount = conns.Count - wifiCount - wiredCount;
                var parts = new List<string>();
                if (wifiCount > 0) parts.Add($"Wi-Fi {wifiCount} 条");
                if (wiredCount > 0) parts.Add($"有线 {wiredCount} 条");
                if (otherCount > 0) parts.Add($"其他 {otherCount} 条");
                ConnSummaryText.Text = $"共 {conns.Count} 条 TCP 连接 · {string.Join("，", parts)}";
            }
        });
    }

    private void ViewConnectionsBtn_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.NavigateToToolPage(typeof(NetworkConnectionsPage));
    }

    private void OnStatsUpdated(List<AdapterStats> stats)
    {
        DispatcherQueue.TryEnqueue(RefreshSpeed);
    }

    #region Adapter Cards

    private void BuildAdapterCards()
    {
        var grid = AdapterCardsGrid;
        grid.Children.Clear();
        _cardRefs.Clear();
        _cardsBuilt = true;

        if (_adapters.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = "未检测到 Wi-Fi 或以太网适配器", FontSize = 14,
                Foreground = new SolidColorBrush(ThemeColors.DimText)
            };
            grid.Children.Add(hint);
            Grid.SetColumn(hint, 0);
            Grid.SetColumnSpan(hint, 2);
            return;
        }

        for (int i = 0; i < _adapters.Count && i < 2; i++)
        {
            var (card, refs) = CreateAdapterCard(_adapters[i]);
            _cardRefs[_adapters[i].Index] = refs;
            grid.Children.Add(card);
            Grid.SetColumn(card, i);
        }
    }

    private void UpdateAdapterCards()
    {
        foreach (var a in _adapters)
        {
            if (!_cardRefs.TryGetValue(a.Index, out var refs)) continue;
            var accent = a.AccentColor;
            var isUp = a.IsUp;
            var hasNet = a.HasInternet;

            if (refs.IpText != null)
                refs.IpText.Text = a.Addresses.Count > 0 ? string.Join(", ", a.Addresses.Select(x => x.ToString())) : "无 IP";
            if (refs.GwText != null)
                refs.GwText.Text = a.Gateways.Count > 0 ? $"网关 {a.Gateways[0]}" : "无网关";

            var statusColor = hasNet ? AccentGreen : (isUp ? AccentOrange : AccentRed);
            var statusText = hasNet ? "已联网" : (isUp ? "已连接" : "未连接");

            if (refs.StatusText != null)
            {
                refs.StatusText.Text = statusText;
                refs.StatusText.Foreground = new SolidColorBrush(statusColor);
            }
            if (refs.StatusBadge != null)
                refs.StatusBadge.Background = new SolidColorBrush(Color.FromArgb(30, statusColor.R, statusColor.G, statusColor.B));
            if (refs.LeftBar != null)
                refs.LeftBar.Background = new SolidColorBrush(isUp ? accent : ThemeColors.Neutral);
            if (refs.Icon != null)
                refs.Icon.Foreground = new SolidColorBrush(isUp ? accent : ThemeColors.DimText);
            if (refs.IconBg != null)
                refs.IconBg.Background = new SolidColorBrush(Color.FromArgb((byte)(isUp ? 30 : 15), accent.R, accent.G, accent.B));
            if (refs.SpeedLabel != null)
                refs.SpeedLabel.Text = a.Speed > 0 ? NetworkAdapterProxyService.FormatSpeed(a.Speed / 8) : "";
        }
    }

    private (Border card, AdapterCardRefs refs) CreateAdapterCard(AdapterInfo a)
    {
        var accent = a.AccentColor;
        var isUp = a.IsUp;
        var hasNet = a.HasInternet;

        var icon = new FontIcon
        {
            Glyph = a.IsWifi ? "\uEC85" : "\uE8BD",
            FontSize = 20,
            Foreground = new SolidColorBrush(isUp ? accent : ThemeColors.DimText)
        };

        var iconBg = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb((byte)(isUp ? 30 : 15), accent.R, accent.G, accent.B)),
            Child = icon
        };

        var name = new TextBlock
        {
            Text = a.Name, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };

        var statusColor = hasNet ? AccentGreen : (isUp ? AccentOrange : AccentRed);
        var statusText = hasNet ? "已联网" : (isUp ? "已连接" : "未连接");

        var statusTextBlock = new TextBlock
        {
            Text = statusText, FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(statusColor)
        };

        var statusBadge = new Border
        {
            Style = (Style)Application.Current.Resources["StatusPillStyle"],
            Background = new SolidColorBrush(Color.FromArgb(30, statusColor.R, statusColor.G, statusColor.B)),
            Child = statusTextBlock
        };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        header.Children.Add(name);
        header.Children.Add(statusBadge);

        var ip = new TextBlock
        {
            Text = a.Addresses.Count > 0 ? string.Join(", ", a.Addresses.Select(x => x.ToString())) : "无 IP",
            FontSize = 12, FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var gw = new TextBlock
        {
            Text = a.Gateways.Count > 0 ? $"网关 {a.Gateways[0]}" : "无网关",
            FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var speedLabel = new TextBlock
        {
            Text = a.Speed > 0 ? NetworkAdapterProxyService.FormatSpeed(a.Speed / 8) : "",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var leftBar = new Border
        {
            Width = 3, CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(isUp ? accent : ThemeColors.Neutral)
        };

        var info = new StackPanel { Spacing = 2 };
        info.Children.Add(header);
        info.Children.Add(ip);
        info.Children.Add(gw);
        if (a.Speed > 0) info.Children.Add(speedLabel);

        var body = new Grid { ColumnSpacing = 10 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        body.Children.Add(leftBar); Grid.SetColumn(leftBar, 0);
        body.Children.Add(iconBg); Grid.SetColumn(iconBg, 1);
        body.Children.Add(info); Grid.SetColumn(info, 2);

        var card = new Border
        {
            Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            Child = body
        };

        var refs = new AdapterCardRefs
        {
            IpText = ip, GwText = gw, StatusText = statusTextBlock,
            StatusBadge = statusBadge, LeftBar = leftBar,
            Icon = icon, IconBg = iconBg, SpeedLabel = speedLabel
        };

        return (card, refs);
    }

    #endregion

    #region Speed Panel

    private void BuildSpeedPanel()
    {
        var grid = SpeedGrid;
        grid.Children.Clear();
        _speedRefs.Clear();

        for (int i = 0; i < _adapters.Count && i < 2; i++)
        {
            var a = _adapters[i];
            var accent = a.AccentColor;

            var dlText = new TextBlock { Text = "0 B/s", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(AccentBlue) };
            var ulText = new TextBlock { Text = "0 B/s", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(AccentOrange) };
            var dlBar = new ProgressBar { Height = 3, Foreground = new SolidColorBrush(AccentBlue), Background = new SolidColorBrush(Color.FromArgb(20, AccentBlue.R, AccentBlue.G, AccentBlue.B)) };
            var ulBar = new ProgressBar { Height = 3, Foreground = new SolidColorBrush(AccentOrange), Background = new SolidColorBrush(Color.FromArgb(20, AccentOrange.R, AccentOrange.G, AccentOrange.B)) };

            _speedRefs[a.Index] = new SpeedRefs { DlText = dlText, UlText = ulText, DlBar = dlBar, UlBar = ulBar };

            var panel = new StackPanel { Spacing = 4 };
            var header = new TextBlock { Text = a.Name, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(accent) };

            var dlRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            dlRow.Children.Add(new TextBlock { Text = "↓", FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText) });
            dlRow.Children.Add(dlText);

            var ulRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            ulRow.Children.Add(new TextBlock { Text = "↑", FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText) });
            ulRow.Children.Add(ulText);

            panel.Children.Add(header);
            panel.Children.Add(dlRow);
            panel.Children.Add(dlBar);
            panel.Children.Add(ulRow);
            panel.Children.Add(ulBar);

            grid.Children.Add(panel);
            Grid.SetColumn(panel, i);
        }
    }

    private void RefreshSpeed()
    {
        foreach (var (ifIndex, refs) in _speedRefs)
        {
            var stats = NetworkAdapterProxyService.GetStatsForAdapter(ifIndex);
            if (stats == null) continue;

            if (refs.DlText != null) refs.DlText.Text = NetworkAdapterProxyService.FormatSpeedFriendly(stats.SpeedDownload);
            if (refs.UlText != null) refs.UlText.Text = NetworkAdapterProxyService.FormatSpeedFriendly(stats.SpeedUpload);

            var adapter = _adapters.FirstOrDefault(a => a.Index == ifIndex);
            if (adapter != null && adapter.Speed > 0)
            {
                if (refs.DlBar != null) refs.DlBar.Value = Math.Min(100, (double)stats.SpeedDownload * 8 / adapter.Speed * 100);
                if (refs.UlBar != null) refs.UlBar.Value = Math.Min(100, (double)stats.SpeedUpload * 8 / adapter.Speed * 100);
            }
        }
    }

    #endregion

    private async void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        ResetBtn.IsEnabled = false;
        await Task.Run(() => NetworkAdapterProxyService.ResetRouting());
        ResetBtn.IsEnabled = true;
        ShowToast("已恢复默认路由", InfoBarSeverity.Success);
    }

    private DispatcherTimer? _toastBarTimer;

    private void ShowToast(string msg, InfoBarSeverity sev)
    {
        ToastBar.Title = msg;
        ToastBar.Severity = sev;
        ToastBar.IsOpen = true;

        _toastBarTimer?.Stop();
        _toastBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _toastBarTimer.Tick += (s, e) =>
        {
            ToastBar.IsOpen = false;
            ((DispatcherTimer)s!).Stop();
        };
        _toastBarTimer.Start();
    }

    private void OnBackRequested(object? sender, EventArgs e)
    {
        Teardown();
        App.MainWindow?.NavigateBack();
    }
}
