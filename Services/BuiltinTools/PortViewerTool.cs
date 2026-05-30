using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class PortViewerTool : IBuiltinTool
{
    public string Id => "port-viewer";
    public string Name => "端口占用";
    public string Description => "查看系统所有 TCP/UDP 端口占用情况，定位占用进程。";
    public string Glyph => "\uE774";
    public string Category => "网络工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    private static readonly Color AccentBlue = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);
    private static readonly Color AccentOrange = Color.FromArgb(255, 251, 146, 60);
    private static readonly Color AccentPurple = Color.FromArgb(255, 167, 139, 250);
    private static readonly Color AccentRed = Color.FromArgb(255, 248, 113, 113);

    private List<PortEntry>? _allEntries;
    private string _filter = "";
    private string _protocolFilter = "全部";

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var dialog = new ContentDialog
        {
            Title = "端口占用",
            CloseButtonText = "关闭",
            XamlRoot = context.XamlRoot
        };
        dialog.Resources["ContentDialogMaxWidth"] = 900;
        dialog.Resources["ContentDialogMaxHeight"] = 720;

        var content = BuildDialogContent(context);
        dialog.Content = content;

        _ = LoadDataAsync(content);

        await dialog.ShowAsync();
    }

    private async Task LoadDataAsync(ScrollViewer root)
    {
        var state = GetState(root);
        if (state is null) return;

        state.LoadingRing.IsActive = true;
        state.LoadingPanel.Visibility = Visibility.Visible;
        state.ListPanel.Visibility = Visibility.Collapsed;

        _allEntries = await PortViewerService.ScanAsync();
        ApplyFilter(root);

        state.LoadingRing.IsActive = false;
        state.LoadingPanel.Visibility = Visibility.Collapsed;
        state.ListPanel.Visibility = Visibility.Visible;
    }

    private void ApplyFilter(ScrollViewer root)
    {
        var state = GetState(root);
        if (state is null || _allEntries is null) return;

        var filtered = _allEntries.AsEnumerable();

        if (_protocolFilter != "全部")
            filtered = filtered.Where(e => e.Protocol == _protocolFilter);

        if (!string.IsNullOrWhiteSpace(_filter))
        {
            var f = _filter.Trim();
            filtered = filtered.Where(e =>
                e.LocalPort.ToString().Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.ProcessName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.ProcessId.ToString().Contains(f) ||
                e.LocalAddress.ToString().Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.State.ToString().Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();
        state.CountText.Text = $"{list.Count} 个连接";
        RenderList(state, list, root);
    }

    private void RenderList(PortViewState state, List<PortEntry> entries, ScrollViewer root)
    {
        state.ListContainer.Children.Clear();

        foreach (var entry in entries)
        {
            var row = CreateRow(entry, root);
            state.ListContainer.Children.Add(row);
        }
    }

    private Border CreateRow(PortEntry entry, ScrollViewer root)
    {
        var protoBadge = new Border
        {
            Padding = new Thickness(8, 2, 8, 2),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(entry.Protocol == "TCP"
                ? Color.FromArgb(40, 96, 165, 250)
                : Color.FromArgb(40, 167, 139, 250)),
            Child = new TextBlock
            {
                Text = entry.Protocol,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(entry.Protocol == "TCP" ? AccentBlue : AccentPurple)
            }
        };

        var portText = new TextBlock
        {
            Text = entry.LocalPort.ToString(),
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var addrText = new TextBlock
        {
            Text = entry.LocalAddress.ToString(),
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
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var killBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE894", FontSize = 11 },
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
                await LoadDataAsync(root);
            }
            else
            {
                var state = GetState(root);
                if (state is not null)
                {
                    state.ErrorBar.Title = "结束进程失败";
                    state.ErrorBar.Message = $"无法结束进程 {entry.ProcessName} (PID {entry.ProcessId})：{error}";
                    state.ErrorBar.Severity = InfoBarSeverity.Error;
                    state.ErrorBar.IsOpen = true;
                }
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
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B)),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private ScrollViewer BuildDialogContent(BuiltinToolContext context)
    {
        var searchBox = new AutoSuggestBox
        {
            PlaceholderText = "搜索端口、进程名、PID...",
            MinWidth = 240,
            QueryIcon = new SymbolIcon(Symbol.Find)
        };
        searchBox.TextChanged += (s, e) =>
        {
            _filter = searchBox.Text;
            ApplyFilter(GetRoot(s));
        };
        searchBox.QuerySubmitted += (s, e) =>
        {
            _filter = searchBox.Text;
            ApplyFilter(GetRoot(s));
        };

        var protoCombo = new ComboBox { MinWidth = 100, SelectedIndex = 0 };
        protoCombo.Items.Add("全部");
        protoCombo.Items.Add("TCP");
        protoCombo.Items.Add("UDP");
        protoCombo.SelectionChanged += (s, e) =>
        {
            _protocolFilter = (string)protoCombo.SelectedItem;
            ApplyFilter(GetRoot(s));
        };

        var refreshBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 12 },
                    new TextBlock { Text = "刷新" }
                }
            }
        };

        var countText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var actionBar = new Grid { ColumnSpacing = 10 };
        actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionBar.Children.Add(searchBox);
        actionBar.Children.Add(protoCombo); Grid.SetColumn(protoCombo, 1);
        actionBar.Children.Add(refreshBtn); Grid.SetColumn(refreshBtn, 2);
        actionBar.Children.Add(countText); Grid.SetColumn(countText, 3);

        var headerGrid = new Grid { ColumnSpacing = 10, Padding = new Thickness(12, 6, 12, 6) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var colIdx = 0;
        AddHeader(headerGrid, "协议", colIdx++);
        AddHeader(headerGrid, "端口", colIdx++);
        AddHeader(headerGrid, "本地地址", colIdx++);
        AddHeader(headerGrid, "状态", colIdx++);
        AddHeader(headerGrid, "进程", colIdx++);
        AddHeader(headerGrid, "PID", colIdx++);
        AddHeader(headerGrid, "操作", colIdx++);

        var headerBorder = new Border
        {
            Background = new SolidColorBrush(ThemeColors.HeaderBg),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Child = headerGrid
        };

        var listContainer = new StackPanel();

        var listScroll = new ScrollViewer
        {
            Content = listContainer,
            MaxHeight = 480,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var listBorder = new Border
        {
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0, 0, 6, 6),
            Child = listScroll
        };

        var loadingRing = new ProgressRing { Width = 36, Height = 36, IsActive = true };
        var loadingText = new TextBlock { Text = "正在扫描端口...", FontSize = 13, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var loadingPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Padding = new Thickness(0, 40, 0, 40),
            Children = { loadingRing, loadingText }
        };

        var errorBar = new InfoBar
        {
            Severity = InfoBarSeverity.Error,
            IsOpen = false,
            IsClosable = true
        };

        var contentGrid = new Grid { RowSpacing = 12 };
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        contentGrid.Children.Add(errorBar); Grid.SetRow(errorBar, 0);
        contentGrid.Children.Add(actionBar); Grid.SetRow(actionBar, 1);
        contentGrid.Children.Add(headerBorder); Grid.SetRow(headerBorder, 2);
        contentGrid.Children.Add(listBorder); Grid.SetRow(listBorder, 3);
        contentGrid.Children.Add(loadingPanel); Grid.SetRow(loadingPanel, 4);

        var root = new StackPanel { Spacing = 14, MaxWidth = 860 };
        root.Children.Add(new TextBlock
        {
            Text = "通过 iphlpapi 扫描系统 TCP/UDP 连接，定位端口占用进程",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });
        root.Children.Add(contentGrid);

        var scrollViewer = new ScrollViewer { Content = root, MaxWidth = 900 };
        scrollViewer.Tag = new PortViewState
        {
            SearchBox = searchBox,
            ProtoCombo = protoCombo,
            RefreshBtn = refreshBtn,
            CountText = countText,
            ListContainer = listContainer,
            ListScroll = listScroll,
            LoadingRing = loadingRing,
            LoadingPanel = loadingPanel,
            ListPanel = listBorder,
            ErrorBar = errorBar
        };

        refreshBtn.Click += async (_, _) =>
        {
            await LoadDataAsync(scrollViewer);
        };

        return scrollViewer;
    }

    private static void AddHeader(Grid grid, string text, int column)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        grid.Children.Add(tb);
        Grid.SetColumn(tb, column);
    }

    private static ScrollViewer GetRoot(object sender) =>
        (sender as FrameworkElement)?.FindParent<ScrollViewer>() ?? null!;

    private static PortViewState? GetState(ScrollViewer root) => root?.Tag as PortViewState;

    private sealed class PortViewState
    {
        public AutoSuggestBox SearchBox = null!;
        public ComboBox ProtoCombo = null!;
        public Button RefreshBtn = null!;
        public TextBlock CountText = null!;
        public StackPanel ListContainer = null!;
        public ScrollViewer ListScroll = null!;
        public ProgressRing LoadingRing = null!;
        public StackPanel LoadingPanel = null!;
        public Border ListPanel = null!;
        public InfoBar ErrorBar = null!;
    }
}

file static class VisualTreeHelperExt
{
    public static T? FindParent<T>(this FrameworkElement child) where T : FrameworkElement
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is T result) return result;
            parent = VisualTreeHelper.GetParent(parent);
            if (parent is not FrameworkElement) break;
        }
        return null;
    }
}
