using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class PortViewerPage : Page, ILocalizablePage
{
    // 语义状态色（跟随系统主题，经 ThemeColors 取官方语义色 / 图表系列色）
    private static Color AccentBlue => ThemeColors.AccentBlue;
    private static Color AccentGreen => ThemeColors.AccentGreen;
    private static Color AccentOrange => ThemeColors.AccentOrange;
    private static Color AccentPurple => ThemeColors.AccentPurple;
    private static Color AccentRed => ThemeColors.AccentRed;

    private List<PortEntry>? _allEntries;
    private string _filter = "";
    private string _protocolFilter = "全部";

    public PortViewerPage()
    {
        InitializeComponent();

        HeaderBorder.Background = new SolidColorBrush(ThemeColors.HeaderBg);
        ListBorder.BorderBrush = new SolidColorBrush(ThemeColors.BorderColor);

        // 代码构建的画刷不会随主题自动刷新，切换主题后按缓存数据重渲染
        ActualThemeChanged += (_, _) =>
        {
            HeaderBorder.Background = new SolidColorBrush(ThemeColors.HeaderBg);
            ListBorder.BorderBrush = new SolidColorBrush(ThemeColors.BorderColor);
            if (_allEntries is not null) ApplyFilter();
        };

        _ = LoadDataAsync();
    }

    /// <summary>语言切换后刷新计数文案与列表（表头等 Uid 控件由 WinUI3Localizer 自动更新）。</summary>
    public void ApplyLocalization()
    {
        ApplyFilter();
    }
    private async Task LoadDataAsync()
    {
        LoadingRing.IsActive = true;
        LoadingPanel.Visibility = Visibility.Visible;
        ListBorder.Visibility = Visibility.Collapsed;
        HeaderBorder.Visibility = Visibility.Collapsed;

        _allEntries = await PortViewerService.ScanAsync();
        ApplyFilter();

        LoadingRing.IsActive = false;
        LoadingPanel.Visibility = Visibility.Collapsed;
        ListBorder.Visibility = Visibility.Visible;
        HeaderBorder.Visibility = Visibility.Visible;
    }

    private void ApplyFilter()
    {
        if (_allEntries is null) return;

        var filtered = _allEntries.AsEnumerable();

        if (_protocolFilter != "全部")
        {
            filtered = _protocolFilter switch
            {
                "TCP" => filtered.Where(e => e.Protocol == "TCP" && !e.IsIPv6),
                "UDP" => filtered.Where(e => e.Protocol == "UDP" && !e.IsIPv6),
                "TCP6" => filtered.Where(e => e.Protocol == "TCP" && e.IsIPv6),
                "UDP6" => filtered.Where(e => e.Protocol == "UDP" && e.IsIPv6),
                _ => filtered
            };
        }

        if (!string.IsNullOrWhiteSpace(_filter))
        {
            var f = _filter.Trim();
            filtered = filtered.Where(e =>
                e.LocalPort.ToString().Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.ProcessName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.ProcessId.ToString().Contains(f) ||
                e.LocalAddress.ToString().Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.ProtocolLabel.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.State.ToString().Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();
        CountText.Text = string.Format(LocalizationService.L("PortViewer_ConnectionCount", "{0} 个连接"), list.Count);
        RenderList(list);
    }

    private void RenderList(List<PortEntry> entries)
    {
        ListContainer.Children.Clear();

        foreach (var entry in entries)
        {
            var row = CreateRow(entry);
            ListContainer.Children.Add(row);
        }
    }

    private Border CreateRow(PortEntry entry)
    {
        // 协议徽章颜色：TCP 蓝、UDP 紫，IPv6 用第三个图表系列色强调区分
        Color protoBg, protoFg;
        if (entry.Protocol == "TCP")
        {
            protoBg = Color.FromArgb(40, AccentBlue.R, AccentBlue.G, AccentBlue.B);
            protoFg = AccentBlue;
        }
        else
        {
            protoBg = Color.FromArgb(40, AccentPurple.R, AccentPurple.G, AccentPurple.B);
            protoFg = AccentPurple;
        }
        if (entry.IsIPv6)
        {
            // IPv6 用图表系列色 3 覆盖，让 v4/v6 一眼可辨
            protoBg = Color.FromArgb(45, ThemeColors.Series3.R, ThemeColors.Series3.G, ThemeColors.Series3.B);
            protoFg = ThemeColors.Series3;
        }

        var protoBadge = new Border
        {
            Padding = new Thickness(8, 2, 8, 2),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(protoBg),
            Child = new TextBlock
            {
                Text = entry.ProtocolLabel,   // TCP / TCP6 / UDP / UDP6
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(protoFg)
            }
        };

        var portText = new TextBlock
        {
            Text = entry.LocalPort.ToString(),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var addrText = new TextBlock
        {
            Text = FormatLocalAddress(entry),
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var stateBadge = entry.Protocol == "TCP" ? MakeStateBadge(entry.State) : null;

        var procText = new TextBlock
        {
            Text = entry.ProcessName,
            FontSize = 12,
            Foreground = new SolidColorBrush(AccentGreen),
            VerticalAlignment = VerticalAlignment.Center
        };

        var pidText = new TextBlock
        {
            Text = $"PID {entry.ProcessId}",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var killBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE894", FontSize = 12 },
            Padding = new Thickness(6, 2, 6, 2),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Foreground = new SolidColorBrush(AccentRed),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = entry
        };
        killBtn.Click += async (_, _) =>
        {
            if (PortViewerService.KillProcess(entry.ProcessId, out var error))
            {
                await LoadDataAsync();
            }
            else
            {
                ErrorBar.Title = LocalizationService.L("PortViewer_KillFailed", "结束进程失败");
                ErrorBar.Message = string.Format(LocalizationService.L("PortViewer_KillFailedMsg", "无法结束进程 {0} (PID {1})：{2}"), entry.ProcessName, entry.ProcessId, error);
                ErrorBar.Severity = InfoBarSeverity.Error;
                ErrorBar.IsOpen = true;
            }
        };

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (stateBadge is not null)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var col = 0;
        grid.Children.Add(protoBadge); Grid.SetColumn(protoBadge, col++);
        grid.Children.Add(portText); Grid.SetColumn(portText, col++);
        grid.Children.Add(addrText); Grid.SetColumn(addrText, col++);
        if (stateBadge is not null) { grid.Children.Add(stateBadge); Grid.SetColumn(stateBadge, col++); }
        grid.Children.Add(procText); Grid.SetColumn(procText, col++);
        grid.Children.Add(pidText); Grid.SetColumn(pidText, col++);
        grid.Children.Add(killBtn); Grid.SetColumn(killBtn, col++);

        return new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private static string FormatLocalAddress(PortEntry entry)
    {
        // 全部地址监听更友好地显示为 *:port 形式
        var addr = entry.LocalAddress;
        if (entry.IsIPv6)
        {
            if (addr.IsIPv4MappedToIPv6 || addr.ToString() == "::" || addr.ToString() == "::0")
                return $"[::]:{entry.LocalPort}";
            return $"[{addr}]:{entry.LocalPort}";
        }
        if (addr.ToString() == "0.0.0.0")
            return $"*:{entry.LocalPort}";
        return $"{addr}:{entry.LocalPort}";
    }

    private static Border MakeStateBadge(PortTcpState state)
    {
        var (text, color) = state switch
        {
            PortTcpState.Listen => ("LISTENING", AccentGreen),
            PortTcpState.Established => ("ESTABLISHED", AccentBlue),
            PortTcpState.TimeWait => ("TIME_WAIT", AccentOrange),
            PortTcpState.CloseWait => ("CLOSE_WAIT", AccentOrange),
            PortTcpState.SynSent => ("SYN_SENT", AccentPurple),
            PortTcpState.SynReceived => ("SYN_RCVD", AccentPurple),
            _ => (state.ToString(), ThemeColors.DimText)
        };

        return new Border
        {
            Padding = new Thickness(6, 1, 6, 1),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B)),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _filter = sender.Text;
        ApplyFilter();
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _filter = sender.Text;
        ApplyFilter();
    }

    private void ProtoCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProtoCombo.SelectedItem is ComboBoxItem { Tag: string s })
            _protocolFilter = s;
        ApplyFilter();
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }
}
