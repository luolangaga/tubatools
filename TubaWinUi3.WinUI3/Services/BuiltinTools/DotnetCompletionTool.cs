using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Controls;
using TubaWinUi3.Models;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class DotnetCompletionTool : IBuiltinTool
{
    public string Id => "dotnet-completion";
    public string Name => LocalizationService.L("Builtin_dotnet-completion_Name", ".NET 环境补全");
    public string Description => LocalizationService.L("Builtin_dotnet-completion_Desc", "检测并补全 .NET Runtime/SDK/Framework，从官网获取最新版本，一键下载安装缺失组件。");
    public string Glyph => "\uE950";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(DotnetCompletionPage));
        return Task.CompletedTask;
    }
}

public sealed partial class DotnetCompletionPage : Page
{
    private StackPanel _loadingPanel = null!;
    private ProgressRing _loadingRing = null!;
  private TextBlock _loadingText = null!;
    private StackPanel _contentPanel = null!;
    private InfoBar _errorBar = null!;
    private TextBlock _archText = null!;
    private TextBlock _runtimeCountText = null!;
    private TextBlock _sdkCountText = null!;
    private TextBlock _missingCountText = null!;
    private StackPanel _itemsList = null!;
    private ComboBox _filterCombo = null!;
    private AutoSuggestBox _searchBox = null!;
    private string _filterType = "全部";
    private string _searchFilter = "";
    private readonly Dictionary<string, bool> _expandedStates = new();
    private readonly Dictionary<DotnetInstallableItem, DotnetItemRowUi> _rowUis = new();
    private bool _syncingQueue;

    /// <summary>行 UI 引用：下载/安装进度只原位更新这些元素，不再整表重建。</summary>
    private sealed class DotnetItemRowUi
    {
        public required Border StatusBadge { get; init; }
        public required TextBlock StatusText { get; init; }
        public required Button ActionButton { get; init; }
        public DotnetInstallStatus LastStatus { get; set; }
    }

    public DotnetCompletionPage()
    {
        InitializeComponent();
        Content = BuildContent();

        Unloaded += (_, _) =>
        {
            DotnetCompletionService.DataChanged -= OnDataChanged;
            DownloadQueueService.Queue.CollectionChanged -= OnQueueChanged;
            UnsubscribeQueueItems();
        };

        _ = LoadDataAsync();
    }

    private void SubscribeQueueSync()
    {
        DownloadQueueService.Queue.CollectionChanged += OnQueueChanged;
        foreach (var qi in DownloadQueueService.Queue)
            qi.PropertyChanged += OnQueueItemPropertyChanged;
    }

    private void UnsubscribeQueueItems()
    {
        foreach (var qi in DownloadQueueService.Queue)
            qi.PropertyChanged -= OnQueueItemPropertyChanged;
    }

    private void OnQueueChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (DownloadItem old in e.OldItems)
                old.PropertyChanged -= OnQueueItemPropertyChanged;
        if (e.NewItems is not null)
            foreach (DownloadItem ni in e.NewItems)
                ni.PropertyChanged += OnQueueItemPropertyChanged;
        SyncQueueToItems();
    }

    private void OnQueueItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadItem.State) or nameof(DownloadItem.Progress))
            SyncQueueToItems();
    }

    private void SyncQueueToItems()
    {
        if (_syncingQueue) return;
        _syncingQueue = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var items = DotnetCompletionService.Installables;
                var statusChanged = false;
                var updated = new HashSet<DotnetInstallableItem>();

                foreach (var qi in DownloadQueueService.Queue)
                {
                    if (qi.Tag is not DotnetInstallableItem ditem) continue;
                    var match = items.FirstOrDefault(i =>
                        i.ComponentType == ditem.ComponentType && i.Version == ditem.Version);
                    if (match is null) continue;

                    var newStatus = qi.State switch
                    {
                        DownloadItemState.Queued or DownloadItemState.Resolving => DotnetInstallStatus.Downloading,
                        DownloadItemState.Downloading => DotnetInstallStatus.Downloading,
                        DownloadItemState.Processing => DotnetInstallStatus.Installing,
                        DownloadItemState.Completed => DotnetInstallStatus.Installed,
                        DownloadItemState.Failed => DotnetInstallStatus.Failed,
                        DownloadItemState.Paused => DotnetInstallStatus.Downloading,
                        DownloadItemState.Cancelled => DotnetInstallStatus.Failed,
                        _ => DotnetInstallStatus.NotInstalled
                    };

                    if (match.Status != newStatus)
                    {
                        match.Status = newStatus;
                        statusChanged = true;
                        updated.Add(match);
                    }

                    if (qi.Progress is not null &&
                        Math.Abs(match.DownloadProgress - qi.Progress.Percentage) > 0.001)
                    {
                        match.DownloadProgress = qi.Progress.Percentage;
                        updated.Add(match);
                    }
                }

                // 只原位刷新受影响的行，避免整表重建导致折叠条闪烁
                if (updated.Count > 0)
                {
                    foreach (var item in updated)
                        UpdateRowInPlace(item);
                    if (statusChanged)
                        UpdateStats();
                }
            }
            finally
            {
                _syncingQueue = false;
            }
        });
    }

    private ScrollViewer BuildContent()
    {
        var header = new ToolPageHeader
        {
            ToolId = "dotnet-completion",
            Title = ".NET 环境补全",
            Subtitle = "检测已安装的 .NET Runtime/SDK/Framework，从官网获取最新版本，一键补全缺失组件",
            Glyph = "\uE950"
        };

        var helpBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE9CE", FontSize = 14 },
            Padding = new Thickness(4),
            MinWidth = 28, MinHeight = 28,
            VerticalAlignment = VerticalAlignment.Center
        };
        helpBtn.Click += OnHelpClick;
        ToolTipService.SetToolTip(helpBtn, "查看 Runtime / SDK / Framework 区别说明");
        header.Actions.Add(helpBtn);

        _archText = new TextBlock { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        _runtimeCountText = new TextBlock { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(ThemeColors.AccentGreen) };
        _sdkCountText = new TextBlock { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(ThemeColors.AccentBlue) };
        _missingCountText = new TextBlock { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(ThemeColors.AccentOrange) };

        var statsGrid = new Grid { ColumnSpacing = 10 };
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var archCard = MakeStatCard("架构", _archText, "\uE912");
        var rtCard = MakeStatCard("已装 Runtime", _runtimeCountText, "\uE950");
        var sdkCard2 = MakeStatCard("已装 SDK", _sdkCountText, "\uE943");
        var missCard = MakeStatCard("可补全", _missingCountText, "\uE823");
        statsGrid.Children.Add(archCard); Grid.SetColumn(archCard, 0);
        statsGrid.Children.Add(rtCard); Grid.SetColumn(rtCard, 1);
        statsGrid.Children.Add(sdkCard2); Grid.SetColumn(sdkCard2, 2);
        statsGrid.Children.Add(missCard); Grid.SetColumn(missCard, 3);

        _errorBar = new InfoBar { IsClosable = true, IsOpen = false, Severity = InfoBarSeverity.Error };

        _filterCombo = new ComboBox { MinWidth = 130, SelectedIndex = 0 };
        _filterCombo.Items.Add("全部");
        _filterCombo.Items.Add("Runtime");
        _filterCombo.Items.Add("SDK");
        _filterCombo.Items.Add("ASP.NET Core");
        _filterCombo.Items.Add("Desktop");
        _filterCombo.Items.Add("Framework");
        _filterCombo.Items.Add("仅未安装");
        _filterCombo.SelectionChanged += (_, _) => { _filterType = _filterCombo.SelectedItem as string ?? "全部"; ApplyFilter(); };

        _searchBox = new AutoSuggestBox { PlaceholderText = "搜索版本号...", MinWidth = 200, QueryIcon = new SymbolIcon(Symbol.Find) };
        _searchBox.TextChanged += (_, _) => { _searchFilter = _searchBox.Text ?? ""; ApplyFilter(); };

        var refreshBtn = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "\uE72C", FontSize = 12 }, new TextBlock { Text = "刷新" } } }
        };
        refreshBtn.Click += async (_, _) => await LoadDataAsync();

        var actionBar = new Grid { ColumnSpacing = 10 };
        actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionBar.Children.Add(_filterCombo); Grid.SetColumn(_filterCombo, 0);
        actionBar.Children.Add(_searchBox); Grid.SetColumn(_searchBox, 1);
        actionBar.Children.Add(refreshBtn); Grid.SetColumn(refreshBtn, 2);

        _itemsList = new StackPanel { Spacing = 4 };

        _loadingRing = new ProgressRing { Width = 40, Height = 40, IsActive = true };
        _loadingText = new TextBlock { Text = "正在检测 .NET 环境...", FontSize = 14, Foreground = new SolidColorBrush(ThemeColors.SecondaryText) };
        _loadingPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8, Padding = new Thickness(0, 40, 0, 40), Children = { _loadingRing, _loadingText } };

        _contentPanel = new StackPanel { Spacing = 14, Visibility = Visibility.Collapsed };
        _contentPanel.Children.Add(statsGrid);
        _contentPanel.Children.Add(_errorBar);
        _contentPanel.Children.Add(actionBar);
        _contentPanel.Children.Add(_itemsList);

        var root = new StackPanel { Spacing = 14, Padding = new Thickness(24, 0, 24, 24), Children = { header, _loadingPanel, _contentPanel } };

        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled };
    }

    private async void OnHelpClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = ".NET 组件说明",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
            CloseButtonText = "知道了",
            Content = new ScrollViewer
            {
                MaxHeight = 400,
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        MakeHelpSection("Runtime（运行时）",
                            "应用程序运行所需的最小环境。只装 Runtime 就能跑 .NET 程序，但不能开发。适合只需要运行 .NET 应用的用户。"),
                        MakeHelpSection("SDK（软件开发工具包）",
                            "包含 Runtime + 编译器 + CLI 工具。开发 .NET 应用必须安装 SDK。SDK 包含对应版本的 Runtime，装了 SDK 就不需要单独装 Runtime。"),
                        MakeHelpSection("ASP.NET Core Runtime",
                            "用于运行 ASP.NET Core Web 应用的专用 Runtime。如果服务器只托管 Web 应用（不开发），装这个比完整 SDK 更轻量。它依赖基础 Runtime。"),
                        MakeHelpSection("Windows Desktop Runtime",
                            "包含 WPF / WinForms / WinUI 等 Windows 桌面 UI 框架的 Runtime。运行桌面应用必须安装。它依赖基础 Runtime，不包含开发工具。"),
                        MakeHelpSection(".NET Framework",
                            "Windows 系统自带的经典 .NET 运行时（4.x / 3.5 等）。许多老软件依赖它。通过 Windows 功能或独立安装包安装，与 .NET (Core) 5+ 是不同的运行时。")
                    }
                }
            }
        };
        await dialog.ShowAsync();
    }

    private static StackPanel MakeHelpSection(string title, string desc)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(ThemeColors.PrimaryText) });
        stack.Children.Add(new TextBlock { Text = desc, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(ThemeColors.SecondaryText) });
        return stack;
    }

    private async Task LoadDataAsync()
    {
        _loadingRing.IsActive = true;
        _loadingText.Text = "正在检测 .NET 环境...";
        _loadingPanel.Visibility = Visibility.Visible;
        _contentPanel.Visibility = Visibility.Collapsed;

        try { await DotnetCompletionService.LoadAsync(); }
        catch (Exception ex)
        {
            _errorBar.Title = "加载失败";
            _errorBar.Message = ex.Message;
            _errorBar.Severity = InfoBarSeverity.Error;
            _errorBar.IsOpen = true;
        }

        _loadingRing.IsActive = false;
        _loadingPanel.Visibility = Visibility.Collapsed;
        _contentPanel.Visibility = Visibility.Visible;

        UpdateStats();
        RenderList();
        DotnetCompletionService.DataChanged += OnDataChanged;
        SubscribeQueueSync();
    }

    private void OnDataChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // 列表已渲染且行数与数据一致时原位更新；结构变化（加载/刷新/筛选）才整表重建
            if (_rowUis.Count > 0 && _rowUis.Count == DotnetCompletionService.Installables.Count)
            {
                var statusChanged = false;
                foreach (var item in DotnetCompletionService.Installables)
                {
                    if (!_rowUis.TryGetValue(item, out _))
                    {
                        RenderList();
                        return;
                    }
                    if (UpdateRowInPlace(item))
                        statusChanged = true;
                }
                if (statusChanged)
                    UpdateStats();
            }
            else
            {
                UpdateStats();
                RenderList();
            }
        });
    }

    private void UpdateStats()
    {
        _archText.Text = DotnetCompletionService.CurrentArch.ToUpperInvariant();
        var items = DotnetCompletionService.Installables;
        var runtimeInstalled = items.Where(i => i.ComponentType != DotnetComponentType.Sdk && i.ComponentType != DotnetComponentType.DotnetFramework && i.Status == DotnetInstallStatus.Installed).Select(i => i.ChannelVersion).Distinct().Count();
        var sdkInstalled = items.Where(i => i.ComponentType == DotnetComponentType.Sdk && i.Status == DotnetInstallStatus.Installed).Select(i => i.ChannelVersion).Distinct().Count();
        var missing = items.Where(i => i.Status == DotnetInstallStatus.NotInstalled).Select(i => i.ChannelVersion + (int)i.ComponentType).Distinct().Count();
        _runtimeCountText.Text = runtimeInstalled.ToString();
        _sdkCountText.Text = sdkInstalled.ToString();
        _missingCountText.Text = missing.ToString();
    }

    private void ApplyFilter() => RenderList();

    private void RenderList()
    {
        _itemsList.Children.Clear();
        _rowUis.Clear();

        var items = DotnetCompletionService.Installables.AsEnumerable();

        if (_filterType != "全部" && _filterType != "仅未安装")
        {
            items = _filterType switch
            {
                "Runtime" => items.Where(i => i.ComponentType == DotnetComponentType.Runtime),
                "SDK" => items.Where(i => i.ComponentType == DotnetComponentType.Sdk),
                "ASP.NET Core" => items.Where(i => i.ComponentType == DotnetComponentType.AspNetCoreRuntime),
                "Desktop" => items.Where(i => i.ComponentType == DotnetComponentType.WindowsDesktopRuntime),
                "Framework" => items.Where(i => i.ComponentType == DotnetComponentType.DotnetFramework),
                _ => items
            };
        }

        if (_filterType == "仅未安装")
            items = items.Where(i => i.Status == DotnetInstallStatus.NotInstalled);

        if (!string.IsNullOrWhiteSpace(_searchFilter))
        {
            var f = _searchFilter.Trim();
            items = items.Where(i => i.Version.Contains(f, StringComparison.OrdinalIgnoreCase) || i.ChannelVersion.Contains(f, StringComparison.OrdinalIgnoreCase) || i.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        var grouped = items
            .GroupBy(i => i.ChannelVersion)
            .ToList();

        var orderedGroups = new List<IGrouping<string, DotnetInstallableItem>>();

        var frameworkGroups = grouped.Where(g => g.Key.StartsWith("Framework")).ToList();
        var dotnetGroups = grouped.Where(g => !g.Key.StartsWith("Framework"))
            .OrderByDescending(g =>
            {
                var parts = g.Key.Split('.');
                return parts.Length > 0 && int.TryParse(parts[0], out var v) ? v : 0;
            })
            .ThenByDescending(g =>
            {
                var parts = g.Key.Split('.');
                return parts.Length > 1 && int.TryParse(parts[1], out var v) ? v : 0;
            })
            .ToList();

        orderedGroups.AddRange(dotnetGroups);
        orderedGroups.AddRange(frameworkGroups);

        var isFirst = true;
        foreach (var group in orderedGroups)
        {
            var channel = DotnetCompletionService.Channels.FirstOrDefault(c => c.ChannelVersion == group.Key);
            var isFramework = group.Key.StartsWith("Framework");
            var phaseLabel = isFramework ? "Framework" : (channel is not null ? DotnetCompletionService.GetSupportPhaseLabel(channel.SupportPhase) : group.Key);
            var phaseColor = isFramework ? ThemeColors.AccentPurple : (channel is not null ? DotnetCompletionService.GetSupportPhaseColor(channel.SupportPhase) : ThemeColors.DimText);

            var headerBadge = new Border
            {
                Padding = new Thickness(8, 2, 8, 2), CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(30, phaseColor.R, phaseColor.G, phaseColor.B)),
                Child = new TextBlock { Text = phaseLabel, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(phaseColor) }
            };

            var headerText = new TextBlock
            {
                Text = isFramework ? $".NET {group.Key}" : $".NET {group.Key}",
                FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
            };

            var eolText = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.SecondaryText), VerticalAlignment = VerticalAlignment.Center };
            if (channel?.EolDate is not null)
                eolText.Text = $"EOL: {channel.EolDate[..10]}";

            var headerGrid = new Grid { ColumnSpacing = 8 };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.Children.Add(headerBadge); Grid.SetColumn(headerBadge, 0);
            headerGrid.Children.Add(headerText); Grid.SetColumn(headerText, 1);
            headerGrid.Children.Add(eolText); Grid.SetColumn(eolText, 2);

            var itemsPanel = new StackPanel { Spacing = 2 };
            foreach (var item in group)
                itemsPanel.Children.Add(CreateItemRow(item));

            var isExpanded = _expandedStates.TryGetValue(group.Key, out var exp) ? exp : isFirst;
            var expander = new Expander
            {
                Header = headerGrid,
                Content = itemsPanel,
                IsExpanded = isExpanded,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            expander.Expanding += (_, _) => _expandedStates[group.Key] = true;
            expander.Collapsed += (_, _) => _expandedStates[group.Key] = false;

            _itemsList.Children.Add(expander);
            isFirst = false;
        }

        if (!_itemsList.Children.Any())
        {
            _itemsList.Children.Add(new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(0, 40, 0, 40),
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE73E", FontSize = 28, Foreground = new SolidColorBrush(ThemeColors.AccentGreen) },
                    new TextBlock { Text = "所有 .NET 组件已安装", FontSize = 14, Foreground = new SolidColorBrush(ThemeColors.SecondaryText) }
                }
            });
        }
    }

    private Border CreateItemRow(DotnetInstallableItem item)
    {
        var typeLabel = DotnetCompletionService.GetComponentTypeLabel(item.ComponentType);

        Color typeBg, typeFg;
        switch (item.ComponentType)
        {
            case DotnetComponentType.Sdk:
                typeFg = ThemeColors.AccentBlue;
                typeBg = Color.FromArgb(40, typeFg.R, typeFg.G, typeFg.B);
                break;
            case DotnetComponentType.AspNetCoreRuntime:
                typeFg = ThemeColors.AccentPurple;
                typeBg = Color.FromArgb(40, typeFg.R, typeFg.G, typeFg.B);
                break;
            case DotnetComponentType.WindowsDesktopRuntime:
                typeFg = ThemeColors.AccentOrange;
                typeBg = Color.FromArgb(40, typeFg.R, typeFg.G, typeFg.B);
                break;
            case DotnetComponentType.DotnetFramework:
                typeFg = ThemeColors.AccentPurple;
                typeBg = Color.FromArgb(40, typeFg.R, typeFg.G, typeFg.B);
                break;
            default:
                typeFg = ThemeColors.AccentGreen;
                typeBg = Color.FromArgb(40, typeFg.R, typeFg.G, typeFg.B);
                break;
        }

        var typeBadge = new Border
        {
            Padding = new Thickness(8, 2, 8, 2), CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(typeBg),
            Child = new TextBlock { Text = typeLabel, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(typeFg) }
        };

        var nameText = new TextBlock { Text = item.DisplayName, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(ThemeColors.PrimaryText), VerticalAlignment = VerticalAlignment.Center };
        var versionText = new TextBlock { Text = item.Version, FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.SecondaryText), VerticalAlignment = VerticalAlignment.Center };
        var infoStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { nameText, versionText } };

        var statusText = new TextBlock { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var statusBadge = new Border
        {
            Padding = new Thickness(6, 1, 6, 1), CornerRadius = new CornerRadius(4),
            Child = statusText
        };
        ApplyStatusBadge(statusBadge, statusText, item);

        // 按钮点击始终挂载，实际行为按 Status 在 OnInstallClick 内拦截，方便原位切换状态
        var actionBtn = new Button { MinWidth = 80, Tag = item, Padding = new Thickness(12, 4, 12, 4) };
        actionBtn.Click += OnInstallClick;
        var (actionContent, actionEnabled, actionOpacity) = BuildActionContent(item);
        actionBtn.Content = actionContent;
        actionBtn.IsEnabled = actionEnabled;
        actionBtn.Opacity = actionOpacity;

        _rowUis[item] = new DotnetItemRowUi
        {
            StatusBadge = statusBadge,
            StatusText = statusText,
            ActionButton = actionBtn,
            LastStatus = item.Status
        };

        var moreBtn = new Button { Content = new FontIcon { Glyph = "\uE712", FontSize = 12 }, Padding = new Thickness(6, 4, 6, 4), Tag = item };
        moreBtn.Click += OnMoreClick;

        var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(12, 8, 12, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(typeBadge); Grid.SetColumn(typeBadge, 0);
        grid.Children.Add(infoStack); Grid.SetColumn(infoStack, 1);
        grid.Children.Add(statusBadge); Grid.SetColumn(statusBadge, 2);
        grid.Children.Add(actionBtn); Grid.SetColumn(actionBtn, 3);
        grid.Children.Add(moreBtn); Grid.SetColumn(moreBtn, 4);

        return new Border { BorderBrush = new SolidColorBrush(ThemeColors.BorderColor), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }

    private static (string Text, Color Color) GetStatusInfo(DotnetInstallableItem item) => item.Status switch
    {
        DotnetInstallStatus.Installed => ("已安装", ThemeColors.AccentGreen),
        DotnetInstallStatus.NotInstalled => ("未安装", ThemeColors.AccentOrange),
        DotnetInstallStatus.Downloading => (item.DownloadProgress > 0 ? $"下载 {item.DownloadProgress:F0}%" : "队列中", ThemeColors.AccentBlue),
        DotnetInstallStatus.Installing => ("安装中", ThemeColors.AccentBlue),
        DotnetInstallStatus.Failed => ("失败", ThemeColors.AccentRed),
        _ => ("未知", ThemeColors.DimText)
    };

    /// <summary>原位刷新状态徽标（不重建控件）。</summary>
    private static void ApplyStatusBadge(Border badge, TextBlock text, DotnetInstallableItem item)
    {
        var (label, color) = GetStatusInfo(item);
        text.Text = label;
        text.Foreground = new SolidColorBrush(color);
        badge.Background = new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B));
    }

    /// <summary>按状态生成操作按钮内容（下载进度 / 安装中 / 重试 等）。</summary>
    private static (UIElement Content, bool Enabled, double Opacity) BuildActionContent(DotnetInstallableItem item)
    {
        switch (item.Status)
        {
            case DotnetInstallStatus.Installed:
                return (new TextBlock { Text = item.InstalledVersion is not null ? $"已装 {item.InstalledVersion}" : "已安装", FontSize = 12 }, false, 0.6);
            case DotnetInstallStatus.Downloading:
                {
                    var pct = item.DownloadProgress > 0 ? $" {item.DownloadProgress:F0}%" : "";
                    return (new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new ProgressRing { Width = 14, Height = 14, IsActive = true }, new TextBlock { Text = $"下载中{pct}", FontSize = 12 } } }, false, 1);
                }
            case DotnetInstallStatus.Installing:
                return (new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new ProgressRing { Width = 14, Height = 14, IsActive = true }, new TextBlock { Text = "安装中", FontSize = 12 } } }, false, 1);
            case DotnetInstallStatus.Failed:
                return (new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new FontIcon { Glyph = "\uE783", FontSize = 12 }, new TextBlock { Text = "重试", FontSize = 12 } } }, true, 1);
            default:
                return (new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new FontIcon { Glyph = "\uE896", FontSize = 12 }, new TextBlock { Text = "安装", FontSize = 12 } } }, true, 1);
        }
    }

    /// <summary>原位刷新单个组件行的状态徽标与操作按钮。</summary>
    private bool UpdateRowInPlace(DotnetInstallableItem item)
    {
        if (!_rowUis.TryGetValue(item, out var row)) return false;

        ApplyStatusBadge(row.StatusBadge, row.StatusText, item);
        var (content, enabled, opacity) = BuildActionContent(item);
        row.ActionButton.Content = content;
        row.ActionButton.IsEnabled = enabled;
        row.ActionButton.Opacity = opacity;

        var statusChanged = row.LastStatus != item.Status;
        row.LastStatus = item.Status;
        return statusChanged;
    }

    private void OnInstallClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DotnetInstallableItem item) return;
        if (item.Status is not (DotnetInstallStatus.NotInstalled or DotnetInstallStatus.Failed)) return;
        DotnetCompletionService.EnqueueDownloadAndInstall(item);
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DotnetInstallableItem item) return;

        var menu = new MenuFlyout();

        if (item.Status is DotnetInstallStatus.NotInstalled or DotnetInstallStatus.Failed)
        {
            var downloadOnlyItem = new MenuFlyoutItem { Text = "仅下载（手动安装）", Icon = new FontIcon { Glyph = "\uE896" } };
            downloadOnlyItem.Click += (_, _) =>
            {
                DotnetCompletionService.EnqueueDownloadOnly(item);
            };
            menu.Items.Add(downloadOnlyItem);
        }

        var openWebItem = new MenuFlyoutItem { Text = "在浏览器中下载", Icon = new FontIcon { Glyph = "\uE774" } };
        openWebItem.Click += (_, _) => DotnetCompletionService.OpenDownloadPage(item);
        menu.Items.Add(openWebItem);

        if (menu.Items.Count > 0)
            menu.ShowAt(sender as FrameworkElement);
    }

    private static Border MakeStatCard(string label, TextBlock value, string glyph)
    {
        var iconBorder = new Border
        {
            Width = 36, Height = 36,
            Background = new SolidColorBrush(Color.FromArgb(26, ThemeColors.PrimaryText.R, ThemeColors.PrimaryText.G, ThemeColors.PrimaryText.B)),
            CornerRadius = new CornerRadius(8),
            Child = new FontIcon { FontSize = 16, Glyph = glyph }
        };
        var stack = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = label, FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.SecondaryText) }, value } };
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(iconBorder);
        grid.Children.Add(stack); Grid.SetColumn(stack, 1);
        return new Border { Padding = new Thickness(12), Background = new SolidColorBrush(ThemeColors.CardBg), BorderBrush = new SolidColorBrush(ThemeColors.BorderColor), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = grid };
    }
}
