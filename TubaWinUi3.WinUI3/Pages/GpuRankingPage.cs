using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using TubaWinUi3.Models;
using TubaWinUi3.Controls;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class GpuRankingPage : Page
{
    private string _category = "all";
    private string _brand = "全部";
    private string _keyword = "";
    private string _sortBy = "timeSpy";
    private bool _isRefreshing;
    private double _lastScrollOffset;
    private bool _navCollapsed;
    private bool _suppressNavToggle;
    private bool _navToggling;
    private bool _suppressFilterEvents;
    private GpuRankingEntry? _locateTarget;
    private AutoSuggestBox _searchBox = null!;
    private StackPanel _brandStack = null!;
    private ComboBox _sortCombo = null!;
    private readonly List<Button> _categoryButtons = [];

    // 天梯视觉：画刷按当前主题缓存一次，条形长度编码“相对榜首”的性能占比
    private RankingPalette _palette = new();
    private Grid _listHeaderHost = null!;
    private RankingHeaderLabel _metricHeader = null!;
    private const double MetricWidth = 300;
    private long _metricMax = 1;
    private IReadOnlyList<string> _localHardwareNames = [];
    private readonly HashSet<string> _localModelNames = new(StringComparer.OrdinalIgnoreCase);

    // 视图：天梯墙（默认，密排 + 相对刻度）/ 列表（逐行明细）
    private string _viewMode = "ladder";
    private Border _tableHeader = null!;
    private Button _ladderViewBtn = null!;
    private Button _listViewBtn = null!;
    private int _wallColumns;
    private List<GpuRankingEntry> _currentEntries = [];

    // 视觉色板（名次徽章用图表系列色、品牌芯片用系列色/语义强调色，均取自 ThemeColors，随主题解析）
    private static Color Gold => ThemeColors.Series4;            // 名次 1：金
    private static Color Silver => ThemeColors.Neutral;          // 名次 2：银
    private static Color Bronze => ThemeColors.Series2;          // 名次 3：铜（深调，与金/银可辨）
    private static Color NvidiaGreen => ThemeColors.AccentGreen; // Nvidia
    private static Color AmdRed => ThemeColors.AccentRed;        // AMD
    private static Color IntelBlue => ThemeColors.Series1;       // Intel
    private static Color AppleGray => ThemeColors.Neutral;       // Apple
    private static Color QualcommPurple => ThemeColors.Series2;  // Qualcomm

    private static SvgImageSource? NvidiaLogo;
    private static SvgImageSource? AmdLogo;
    private static SvgImageSource? IntelLogo;
    private static SvgImageSource? AppleLogo;
    private static SvgImageSource? QualcommLogo;

    private StackPanel _listContainer = null!;
    private ScrollViewer _listScroll = null!;
    private InfoBar _infoBar = null!;
    private ProgressBar _loadingBar = null!;
    private ToolPageHeader _toolHeader = null!;
    private FrameworkElement _headerRow = null!;
    private FrameworkElement _filterRow = null!;
    private FrameworkElement _statsRow = null!;
    private Grid _statsGrid = null!;
    private StackPanel _localRanking = null!;

    public GpuRankingPage()
    {
        InitializeComponent();
        GpuRankingService.Load();
        LoadBrandLogos();

        var root = BuildUI();
        Content = root;

        RefreshList();
        _ = LoadLocalRankingAsync();

        // 代码构建的画刷不会随主题自动刷新，切换主题后按当前筛选状态重渲染
        ActualThemeChanged += (_, _) =>
        {
            _palette = new RankingPalette();
            if (_brandStack is not null)
                UpdateBrandButtons(_brandStack, _brand);
            UpdateCategoryStyles();
            BuildListHeader();
            RefreshList();
        };
    }

    private static void LoadBrandLogos()
    {
        if (NvidiaLogo is not null) return;

        var brandsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Brands");
        NvidiaLogo = LoadSvg(System.IO.Path.Combine(brandsDir, "nvidia.svg"));
        AmdLogo = LoadSvg(System.IO.Path.Combine(brandsDir, "amd.svg"));
        IntelLogo = LoadSvg(System.IO.Path.Combine(brandsDir, "intel.svg"));
        AppleLogo = LoadSvg(System.IO.Path.Combine(brandsDir, "apple.svg"));
        QualcommLogo = LoadSvg(System.IO.Path.Combine(brandsDir, "qualcomm.svg"));
    }

    private static SvgImageSource? LoadSvg(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var uri = new Uri($"ms-appx:///Assets/Brands/{System.IO.Path.GetFileName(path)}");
            return new SvgImageSource(uri);
        }
        catch { return null; }
    }

    private static SvgImageSource? GetBrandLogo(string brand) => brand switch
    {
        "Nvidia" => NvidiaLogo,
        "AMD" => AmdLogo,
        "Intel" => IntelLogo,
        "Apple" => AppleLogo,
        "Qualcomm" => QualcommLogo,
        _ => null
    };

    private static Color GetBrandColor(string brand) => brand switch
    {
        "Nvidia" => NvidiaGreen, "AMD" => AmdRed, "Intel" => IntelBlue,
        "Apple" => AppleGray, "Qualcomm" => QualcommPurple, _ => ThemeColors.DimText
    };

    private static string HeaderSubtitle() =>
        $"数据来源 TopCPU.net · 更新于 {GpuRankingService.LastUpdated ?? "内置数据"} · " +
        "3DMark Time Spy · 条形长度 = 相对榜首性能";

    private Grid BuildUI()
    {
        var mainGrid = new Grid
        {
            Padding = new Thickness(24, 4, 24, 32),
            RowSpacing = 24
        };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _headerRow = BuildHeader();
        mainGrid.Children.Add(_headerRow);
        Grid.SetRow(_headerRow, 0);

        _filterRow = BuildFilterBar();
        mainGrid.Children.Add(_filterRow);
        Grid.SetRow(_filterRow, 1);

        _statsRow = BuildStatsCards();
        mainGrid.Children.Add(_statsRow);
        Grid.SetRow(_statsRow, 2);

        var listRow = BuildListArea();
        mainGrid.Children.Add(listRow);
        Grid.SetRow(listRow, 3);

        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _localRanking = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        mainGrid.Children.Add(_localRanking);
        Grid.SetRow(_localRanking, 4);

        return mainGrid;
    }

    private StackPanel BuildHeader()
    {
        _toolHeader = new ToolPageHeader
        {
            ToolId = "gpu-ranking",
            HeaderPadding = new Thickness(0, 16, 0, 0),
            Title = "GPU 天梯图",
            Subtitle = HeaderSubtitle(),
            Glyph = "\uE9D5"
        };

        var refreshBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 12 },
                    new TextBlock { Text = "刷新数据" }
                }
            },
            Style = (Style)Application.Current.Resources["ToolHeaderActionButtonStyle"]
        };

        refreshBtn.Click += async (_, _) => await RefreshDataAsync();
        // 视图切换放页头右侧：筛选行已经排满，再塞一个分段控件会在 1100 宽的工具窗里溢出
        _toolHeader.Actions.Add(BuildViewToggle());
        _toolHeader.Actions.Add(refreshBtn);

        _infoBar = new InfoBar
        {
            Severity = InfoBarSeverity.Informational,
            IsOpen = false,
            IsClosable = true,
            Title = "",
            Message = ""
        };

        _loadingBar = new ProgressBar
        {
            IsIndeterminate = true,
            Visibility = Visibility.Collapsed
        };

        var outer = new StackPanel { Spacing = 16 };
        outer.Children.Add(_toolHeader);
        outer.Children.Add(_loadingBar);
        outer.Children.Add(_infoBar);

        return outer;
    }

    private async Task RefreshDataAsync()
    {
        if (_isRefreshing) return;

        if (!GpuRankingService.CanRefresh)
        {
            var remaining = GpuRankingService.CooldownTime - (DateTime.Now - GpuRankingService.LastRefreshTime);
            _infoBar.Title = "提示";
            _infoBar.Message = $"数据已是最新，{remaining.Minutes} 分钟后可再次刷新";
            _infoBar.Severity = InfoBarSeverity.Warning;
            _infoBar.IsOpen = true;
            return;
        }

        _isRefreshing = true;
        _loadingBar.Visibility = Visibility.Visible;
        _infoBar.IsOpen = false;

        var refreshResult = await GpuRankingService.RefreshFromNetworkAsync();

        _isRefreshing = false;
        _loadingBar.Visibility = Visibility.Collapsed;

        if (refreshResult.Success)
        {
            _infoBar.Title = "刷新成功";
            _infoBar.Message = refreshResult.Message;
            _infoBar.Severity = InfoBarSeverity.Success;
            _infoBar.IsOpen = true;

            _toolHeader.Subtitle = HeaderSubtitle();
            RefreshList();
        }
        else
        {
            _infoBar.Title = "刷新失败";
            _infoBar.Message = refreshResult.Message;
            _infoBar.Severity = InfoBarSeverity.Error;
            _infoBar.IsOpen = true;
        }
    }

    private Grid BuildFilterBar()
    {
        var grid = new Grid { ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var catToggle = BuildCategoryToggle();
        grid.Children.Add(catToggle);
        Grid.SetColumn(catToggle, 0);

        _brandStack = BuildBrandFilter();
        grid.Children.Add(_brandStack);
        Grid.SetColumn(_brandStack, 1);

        _searchBox = new AutoSuggestBox
        {
            PlaceholderText = "搜索 GPU 名称、显存...",
            QueryIcon = new SymbolIcon(Symbol.Find),
            MinWidth = 200
        };
        _searchBox.TextChanged += (s, e) =>
        {
            if (_suppressFilterEvents) return;
            _keyword = _searchBox.Text;
            RefreshList();
        };
        grid.Children.Add(_searchBox);
        Grid.SetColumn(_searchBox, 2);

        _sortCombo = new ComboBox
        {
            MinWidth = 120,
            SelectedIndex = 0,
            Header = null
        };
        _sortCombo.Items.Add("Time Spy 分数");
        _sortCombo.Items.Add("排名顺序");
        _sortCombo.SelectionChanged += (s, e) =>
        {
            if (_suppressFilterEvents) return;
            _sortBy = _sortCombo.SelectedIndex switch
            {
                0 => "timeSpy",
                _ => "rank"
            };
            RefreshList();
        };
        grid.Children.Add(_sortCombo);
        Grid.SetColumn(_sortCombo, 3);

        return grid;
    }

    /// <summary>「天梯 / 列表」切换：天梯看相对位置，列表看逐行明细。</summary>
    private Border BuildViewToggle()
    {
        var ladderBtn = new Button
        {
            Content = "天梯",
            Padding = new Thickness(14, 8, 14, 8),
            CornerRadius = new CornerRadius(8, 0, 0, 8),
            Tag = "ladder"
        };
        var listBtn = new Button
        {
            Content = "列表",
            Padding = new Thickness(14, 8, 14, 8),
            CornerRadius = new CornerRadius(0, 8, 8, 0),
            Tag = "list"
        };

        ladderBtn.Click += (_, _) => SwitchView("ladder");
        listBtn.Click += (_, _) => SwitchView("list");

        _ladderViewBtn = ladderBtn;
        _listViewBtn = listBtn;

        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        stack.Children.Add(ladderBtn);
        stack.Children.Add(listBtn);
        UpdateViewToggle();

        return new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
            Child = stack
        };
    }

    private void UpdateViewToggle()
    {
        if (_ladderViewBtn is null || _listViewBtn is null) return;
        var ladderActive = _viewMode != "list";
        UpdateCategoryButtons(ladderActive ? _ladderViewBtn : _listViewBtn,
                              ladderActive ? _listViewBtn : _ladderViewBtn);
    }

    private void SwitchView(string mode)
    {
        var changed = _viewMode != mode;
        _viewMode = mode;
        if (changed)
        {
            UpdateViewToggle();
            ExpandNav();
        }

        RefreshList();

        if (changed) _ = _listScroll?.ChangeView(null, 0, null, true);
        _lastScrollOffset = 0;
    }

    private Border BuildCategoryToggle()
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        foreach (var (category, label, glyph) in new[]
                 {
                     ("all", "全部", ""),
                     ("desktop", "桌面", "\uE964"),
                     ("laptop", "笔记本", "\uE7F7")
                 })
        {
            FrameworkElement content;
            if (glyph.Length == 0)
            {
                content = new TextBlock { Text = label, FontSize = 14 };
            }
            else
            {
                content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new FontIcon { Glyph = glyph, FontSize = 14 },
                        new TextBlock { Text = label, FontSize = 14 }
                    }
                };
            }

            var button = new Button
            {
                Content = content,
                Tag = category,
                Padding = new Thickness(14, 8, 14, 8),
                CornerRadius = new CornerRadius(8)
            };
            button.Click += (_, _) => { _category = category; UpdateCategoryStyles(); RefreshList(); };
            _categoryButtons.Add(button);
            stack.Children.Add(button);
        }
        UpdateCategoryStyles();
        return new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor), Padding = new Thickness(2), Child = stack };
    }

    private void UpdateCategoryStyles()
    {
        var active = _categoryButtons.FirstOrDefault(b => (string)b.Tag == _category);
        if (active is null) return;
        foreach (var inactive in _categoryButtons.Where(b => b != active)) UpdateCategoryButtons(active, inactive);
    }

    private static void UpdateCategoryButtons(Button active, Button inactive)
    {
        var accentColor = ThemeColors.AccentBlue;

        active.Background = new SolidColorBrush(Color.FromArgb(40, accentColor.R, accentColor.G, accentColor.B));
        active.Foreground = new SolidColorBrush(accentColor);
        active.BorderBrush = new SolidColorBrush(accentColor);

        inactive.Background = new SolidColorBrush(ThemeColors.SubtleBg);
        inactive.Foreground = new SolidColorBrush(ThemeColors.DimText);
        inactive.BorderBrush = new SolidColorBrush(ThemeColors.SubtleBg);
    }

    private StackPanel BuildBrandFilter()
    {
        var brands = new[] { "全部", "Nvidia", "AMD", "Intel", "Apple", "Qualcomm" };

        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        foreach (var label in brands)
        {
            var logo = GetBrandLogo(label);

            FrameworkElement btnContent;
            if (label == "全部")
            {
                btnContent = new TextBlock { Text = "全部", FontSize = 12, FontWeight = FontWeights.SemiBold };
            }
            else if (logo is not null)
            {
                btnContent = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new Image { Source = logo, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }
                    }
                };
            }
            else
            {
                btnContent = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold };
            }

            var btn = new Button
            {
                Content = btnContent,
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(8),
                Tag = label
            };

            btn.Click += (s, e) =>
            {
                _brand = label;
                UpdateBrandButtons(stack, label);
                RefreshList();
            };

            stack.Children.Add(btn);
        }

        UpdateBrandButtons(stack, "全部");
        return stack;
    }

    private static void UpdateBrandButtons(StackPanel stack, string selected)
    {
        var brandColors = new Dictionary<string, Color?>
        {
            ["全部"] = null, ["Nvidia"] = NvidiaGreen, ["AMD"] = AmdRed,
            ["Intel"] = IntelBlue, ["Apple"] = AppleGray, ["Qualcomm"] = QualcommPurple
        };

        foreach (var btn in stack.Children.OfType<Button>())
        {
            var label = (string)btn.Tag;
            var isSelected = label == selected;

            if (isSelected)
            {
                var c = brandColors[label] ?? ThemeColors.AccentBlue;
                btn.Background = new SolidColorBrush(Color.FromArgb(40, c.R, c.G, c.B));
                btn.Foreground = new SolidColorBrush(c);
                btn.BorderBrush = new SolidColorBrush(c);
                btn.BorderThickness = new Thickness(1);
            }
            else
            {
                btn.Background = new SolidColorBrush(ThemeColors.SubtleBg);
                btn.Foreground = new SolidColorBrush(ThemeColors.DimText);
                btn.BorderBrush = new SolidColorBrush(ThemeColors.SubtleBg);
                btn.BorderThickness = new Thickness(0);
            }
        }
    }

    private Grid BuildStatsCards()
    {
        _statsGrid = new Grid { ColumnSpacing = 10 };
        for (var i = 0; i < 4; i++)
            _statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        FillStatsCards(GpuRankingService.GetByCategory(_category));
        return _statsGrid;
    }

    /// <summary>统计卡：总数、两大品牌占比、榜首。占比与榜首型号让“天梯有多长、顶端是谁”一眼可见。</summary>
    private void FillStatsCards(List<GpuRankingEntry> filtered)
    {
        _statsGrid.Children.Clear();

        var total = filtered.Count;
        var nvidiaCount = filtered.Count(e => e.Brand == "Nvidia");
        var amdCount = filtered.Count(e => e.Brand == "AMD");
        var top = filtered.MaxBy(e => e.Rating);

        var totalCard = MakeStatCard("总计", $"{total} 款", "\uE9D9", ThemeColors.AccentBlue, null,
            _brand == "全部" ? "全部品牌" : $"{_brand} 品牌");
        var nvidiaCard = MakeStatCard("Nvidia", $"{nvidiaCount} 款", "\uE912", NvidiaGreen, NvidiaLogo, Share(nvidiaCount, total));
        var amdCard = MakeStatCard("AMD", $"{amdCount} 款", "\uE9D5", AmdRed, AmdLogo, Share(amdCount, total));
        var topCard = MakeStatCard("榜首", $"{top?.Rating ?? 0:N0} 分", "\uE8CA", ThemeColors.Series4, null, top?.Name);

        _statsGrid.Children.Add(totalCard);
        _statsGrid.Children.Add(nvidiaCard); Grid.SetColumn(nvidiaCard, 1);
        _statsGrid.Children.Add(amdCard); Grid.SetColumn(amdCard, 2);
        _statsGrid.Children.Add(topCard); Grid.SetColumn(topCard, 3);
    }

    private static string Share(int count, int total) =>
        total <= 0 ? "0%" : $"{Math.Round(count * 100.0 / total)}%";

    private static Border MakeStatCard(string label, string value, string glyph, Color accent,
        SvgImageSource? logo, string? sub = null)
    {
        FrameworkElement iconChild;
        if (logo is not null)
            iconChild = new Image { Source = logo, Width = 18, Height = 18 };
        else
            iconChild = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(accent), Glyph = glyph };

        var iconBorder = new Border
        {
            Width = 38, Height = 38,
            Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(8),
            Child = iconChild
        };

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText) });
        stack.Children.Add(new TextBlock
        {
            Text = value, FontSize = 18, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(accent)
        });
        if (!string.IsNullOrWhiteSpace(sub))
        {
            stack.Children.Add(new TextBlock
            {
                Text = sub, FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText),
                TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1
            });
        }

        var innerGrid = new Grid { ColumnSpacing = 10 };
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        innerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        innerGrid.Children.Add(iconBorder);
        innerGrid.Children.Add(stack); Grid.SetColumn(stack, 1);

        return new Border
        {
            Padding = new Thickness(14, 10, 14, 10),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = innerGrid
        };
    }

    private Grid BuildListArea()
    {
        _listHeaderHost = new Grid
        {
            Padding = new Thickness(14, 9, 14, 9),
            ColumnSpacing = 12
        };
        ApplyListColumns(_listHeaderHost);
        BuildListHeader();

        var headerBorder = new Border
        {
            Background = new SolidColorBrush(ThemeColors.HeaderBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1, 1, 1, 0),
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Child = _listHeaderHost
        };
        _tableHeader = headerBorder;

        _listContainer = new StackPanel { Spacing = 0 };

        _listScroll = new ScrollViewer
        {
            Content = _listContainer,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        _listScroll.ViewChanged += OnListScrollChanged;

        // 墙的列数跟随可用宽度：窄窗口单列，宽屏最多四列。
        // 延到下一拍重建，避免在 SizeChanged（布局过程中）换掉 Content 再次触发布局。
        _listScroll.SizeChanged += (_, e) =>
        {
            if (_viewMode != "ladder" || e.NewSize.Width < 1) return;
            if (RankingLadderLayout.ColumnsFor(e.NewSize.Width) == _wallColumns) return;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (_viewMode != "ladder" || _listScroll is null) return;
                if (RankingLadderLayout.ColumnsFor(_listScroll.ActualWidth) == _wallColumns) return;
                RebuildList();
            });
        };

        var listBorder = new Border
        {
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0, 0, 8, 8),
            Child = _listScroll
        };

        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.Children.Add(headerBorder); Grid.SetRow(headerBorder, 0);
        outer.Children.Add(listBorder); Grid.SetRow(listBorder, 1);

        return outer;
    }

    private static void ApplyListColumns(Grid grid)
    {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RankingColumns.Rank) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MetricWidth) });
    }

    private void BuildListHeader()
    {
        if (_listHeaderHost is null) return;

        _listHeaderHost.Children.Clear();

        var rank = new RankingHeaderLabel("排名", null, _palette);
        var model = new RankingHeaderLabel("显卡", null, _palette);
        _metricHeader = new RankingHeaderLabel("Time Spy 分数", "条形 = 相对榜首", _palette);

        _listHeaderHost.Children.Add(rank); Grid.SetColumn(rank, 0);
        _listHeaderHost.Children.Add(model); Grid.SetColumn(model, 1);
        _listHeaderHost.Children.Add(_metricHeader); Grid.SetColumn(_metricHeader, 2);
    }

    private void OnListScrollChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_listScroll is null || _navToggling) return;

        var currentOffset = _listScroll.VerticalOffset;
        var threshold = 30;

        if (_suppressNavToggle)
        {
            _lastScrollOffset = currentOffset;
            if (!e.IsIntermediate) _suppressNavToggle = false;
            return;
        }

        if (!_navCollapsed && currentOffset > _lastScrollOffset + threshold && currentOffset > 60)
        {
            CollapseNav();
        }
        else if (_navCollapsed && currentOffset < _lastScrollOffset - threshold || currentOffset <= 0)
        {
            ExpandNav();
        }

        _lastScrollOffset = currentOffset;
    }

    // 折叠/展开会改变列表视口高度：滚动在底部附近时偏移随即被钳制并再次触发 ViewChanged。
    // 不在切换后重新基线化 _lastScrollOffset，就会折叠↔展开无限震荡直到 LayoutCycleException（issue #165）。
    private void CollapseNav()
    {
        if (_navCollapsed) return;
        _navCollapsed = true;
        ApplyNavVisibility();
    }

    private void ExpandNav()
    {
        if (!_navCollapsed) return;
        _navCollapsed = false;
        ApplyNavVisibility();
    }

    private void ApplyNavVisibility()
    {
        _navToggling = true;
        try
        {
            var visibility = _navCollapsed ? Visibility.Collapsed : Visibility.Visible;
            if (_statsRow is not null) _statsRow.Visibility = visibility;
            if (_filterRow is not null) _filterRow.Visibility = visibility;
            if (_headerRow is not null) _headerRow.Visibility = visibility;

            // 同步应用新布局并把钳制后的偏移作为新基线，视口变化引发的回声事件不会再次触发切换
            _listScroll?.UpdateLayout();
            _lastScrollOffset = _listScroll?.VerticalOffset ?? _lastScrollOffset;
        }
        finally
        {
            _navToggling = false;
        }
    }

    private async Task LoadLocalRankingAsync()
    {
        try
        {
            await HardwareInfoService.LoadConfiguredCpuzAsync();
            _localHardwareNames = await Task.Run(HardwareInfoService.GetDisplayGpuNames);
        }
        catch { _localHardwareNames = []; }

        // 本机型号确定后需要重画一次：命中的型号条会带“本机”底纹
        RefreshList();
    }

    private void UpdateLocalRanking()
    {
        if (_localRanking is null) return;
        _localRanking.Children.Clear();
        _localModelNames.Clear();

        foreach (var name in _localHardwareNames)
        {
            var entry = LocalRankingMatcher.Find(name, GpuRankingService.All, e => e.Name);
            if (entry is null) continue;

            _localModelNames.Add(entry.Name);

            var inView = GpuRankingService.GetByCategory(_category)
                .FirstOrDefault(e => string.Equals(e.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
            if (inView is null) continue;

            var categoryCount = GpuRankingService.GetByCategory(_category).Count;
            var beat = inView.Rank > 0 ? RankingScale.BeatPercent(inView.Rank - 1, categoryCount) : -1;
            var details = $"第 {inView.Rank} 名 · Time Spy {inView.Rating:N0} · 显存 {inView.Vram}"
                + (beat >= 0 ? $" · 超过 {beat}% 的机型" : "");

            _localRanking.Children.Add(LocalRankingCard.Create("GPU", inView.Name, details,
                () => LocateInFullRanking(inView)));
        }

        _localRanking.Visibility = _localRanking.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshList()
    {
        if (_listContainer is null) return;
        UpdateLocalRanking();
        RebuildList();
    }

    private void RebuildList()
    {
        var entries = GpuRankingService.GetByCategory(_category);
        entries = GpuRankingService.Filter(entries, _brand, _keyword);

        entries = _sortBy switch
        {
            "timeSpy" => entries.OrderByDescending(e => e.Rating).ToList(),
            _ => entries.OrderBy(e => e.Rank).ToList()
        };

        _metricMax = MetricMax();
        _currentEntries = entries;
        _tableHeader.Visibility = _viewMode == "list" ? Visibility.Visible : Visibility.Collapsed;

        if (entries.Count == 0)
        {
            _listScroll.Content = RankingVisuals.CreateEmptyState(
                "没有符合条件的显卡",
                "换个关键词，或清除品牌筛选后再看",
                ClearFilters, _palette);
            FillStatsCards(entries);
            _locateTarget = null;
            return;
        }

        if (_viewMode == "ladder")
        {
            _listScroll.Content = BuildLadder(entries);
            FillStatsCards(entries);
            _locateTarget = null;
            return;
        }

        _listScroll.Content = _listContainer;
        _listContainer.Children.Clear();

        // 有筛选时不再用奖牌色，否则“筛选后的前三”会被误读成天梯前三
        var allowMedal = _brand == "全部" && string.IsNullOrWhiteSpace(_keyword);
        Border? locateRow = null;

        void AddRow(int index)
        {
            var entry = entries[index];
            var row = CreateRow(entry, index + 1, allowMedal);
            if (_locateTarget is not null && ReferenceEquals(entry, _locateTarget))
                locateRow = row;
            _listContainer.Children.Add(row);
        }

        var tiers = RankingTiers.Build(entries.Count);
        if (tiers.Count == 0)
        {
            for (var i = 0; i < entries.Count; i++) AddRow(i);
        }
        else
        {
            foreach (var tier in tiers)
            {
                _listContainer.Children.Add(CreateTierHeader(tier, entries));
                for (var i = tier.Start; i <= tier.End; i++) AddRow(i);
            }
        }

        FillStatsCards(entries);

        if (_locateTarget is not null)
        {
            _locateTarget = null;
            if (locateRow is not null) _ = BringRowIntoViewAsync(locateRow);
        }
    }

    /// <summary>条形基准取“当前分类全量”的最高分：筛选不会让弱型号的条形虚假变长。</summary>
    private long MetricMax()
    {
        var all = GpuRankingService.GetByCategory(_category);
        if (all.Count == 0) return 1;
        return Math.Max(1, all.Max(e => e.Rating));
    }

    /// <summary>天梯墙：型号按当前排序密排成多列，左侧刻度轴标出相对榜首的百分比位置。</summary>
    private FrameworkElement BuildLadder(List<GpuRankingEntry> entries)
    {
        var items = new List<LadderItem>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            items.Add(new LadderItem(i + 1, entry.Name, entry.Brand, entry.Rating, IsLocalModel(entry.Name)));
        }

        var width = _listScroll?.ActualWidth ?? 0;
        if (width < 1) width = 1200; // 首次布局前给一个中性宽度，随后由 SizeChanged 校正
        _wallColumns = RankingLadderLayout.ColumnsFor(width);

        return RankingLadder.Build(items, _metricMax, _wallColumns,
            ["Nvidia", "AMD", "Intel"], _palette, GetBrandColor, OnLadderActivated);
    }

    private void OnLadderActivated(LadderItem item)
    {
        var entry = _currentEntries.ElementAtOrDefault(item.Position - 1);
        if (entry is null) return;

        // 滚动后头部三行可能已折叠，提示条会看不见，先展开
        ExpandNav();

        var percent = RankingScale.Percent(entry.Rating, _metricMax);
        _infoBar.Title = "已选中";
        _infoBar.Message = $"{entry.Name} · 第 {item.Position} 名 · Time Spy {entry.Rating:N0} · "
            + $"显存 {entry.Vram} · 相对榜首 {percent}%";
        _infoBar.Severity = InfoBarSeverity.Informational;

        var locate = new Button { Content = "在列表中定位" };
        locate.Click += (_, _) =>
        {
            _locateTarget = entry;
            SwitchView("list");
        };
        _infoBar.ActionButton = locate;
        _infoBar.IsOpen = true;
    }

    private Border CreateTierHeader(RankingTier tier, List<GpuRankingEntry> entries)
    {
        long high = 0, low = long.MaxValue;
        for (var i = tier.Start; i <= tier.End; i++)
        {
            var value = entries[i].Rating;
            high = Math.Max(high, value);
            low = Math.Min(low, value);
        }
        if (low == long.MaxValue) low = 0;

        var range = tier.Count == 1 || low == high
            ? FormatScore(high)
            : $"{FormatScore(high)} – {FormatScore(low)}";

        return RankingVisuals.CreateTierHeader(tier, $"{tier.Count} 款 · {range}", _palette);
    }

    private static string FormatScore(long value) => value > 0 ? value.ToString("N0") : "—";

    private Border CreateRow(GpuRankingEntry entry, int position, bool allowMedal)
    {
        var brandColor = GetBrandColor(entry.Brand);
        var brandLogo = GetBrandLogo(entry.Brand);
        var percent = RankingScale.Percent(entry.Rating, _metricMax);

        var medal = allowMedal && position <= 3
            ? position == 1 ? Gold : position == 2 ? Silver : Bronze
            : (Color?)null;
        var rankBadge = RankingVisuals.CreateRankBadge(position, medal, _palette);

        // 型号列：品牌徽标 + 名称（可带“本机”标记）+ 副行（显存，合并视图下补充桌面/笔记本）
        var nameText = new TextBlock
        {
            Text = entry.Name,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = _palette.PrimaryText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var titleRow = new Grid { ColumnSpacing = 8 };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.Children.Add(nameText);
        if (IsLocalModel(entry.Name))
        {
            var localBadge = RankingVisuals.CreateLocalBadge(_palette);
            titleRow.Children.Add(localBadge);
            Grid.SetColumn(localBadge, 1);
        }

        var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(titleRow);

        var meta = BuildMeta(entry);
        if (meta.Length > 0)
        {
            textStack.Children.Add(new TextBlock
            {
                Text = meta,
                FontSize = 12,
                Foreground = _palette.DimText,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        var brandMark = RankingVisuals.CreateBrandMark(entry.Brand, brandLogo, brandColor, _palette);
        var modelCell = new Grid { ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
        modelCell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        modelCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modelCell.Children.Add(brandMark);
        Grid.SetColumn(brandMark, 0);
        modelCell.Children.Add(textStack);
        Grid.SetColumn(textStack, 1);

        var metricCell = RankingVisuals.CreateMetricCell(entry.Rating, _metricMax, brandColor, _palette,
            $"相对榜首 {percent}%");

        var rowGrid = new Grid { ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Center };
        ApplyListColumns(rowGrid);
        rowGrid.Children.Add(rankBadge); Grid.SetColumn(rankBadge, 0);
        rowGrid.Children.Add(modelCell); Grid.SetColumn(modelCell, 1);
        rowGrid.Children.Add(metricCell); Grid.SetColumn(metricCell, 2);

        var row = RankingVisuals.CreateRowShell(rowGrid, _palette,
            $"{entry.Name}，第 {position} 名，Time Spy {entry.Rating:N0}，相对榜首 {percent}%");

        ToolTipService.SetToolTip(row, BuildRowTooltip(entry, position, percent));
        AutomationProperties.SetHelpText(row, "点击在完整排名中定位");
        row.Tag = entry;
        row.Tapped += OnRowTapped;
        return row;
    }

    private string BuildMeta(GpuRankingEntry entry)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.Vram)) parts.Add(entry.Vram);
        if (_category == "all" && !string.IsNullOrWhiteSpace(entry.Category))
            parts.Add(entry.Category == "desktop" ? "桌面" : "笔记本");
        return string.Join(" · ", parts);
    }

    private static string BuildRowTooltip(GpuRankingEntry entry, int position, int percent)
    {
        var lines = new List<string> { entry.Name };
        if (!string.IsNullOrWhiteSpace(entry.Vram)) lines.Add($"显存 {entry.Vram}");
        lines.Add($"Time Spy {FormatScore(entry.Rating)}");
        if (entry.Rank > 0 && entry.Rank != position) lines.Add($"完整排名 第 {entry.Rank} 名");
        lines.Add($"相对榜首 {percent}%");
        lines.Add("点击在完整排名中定位");
        return string.Join("\n", lines);
    }

    private bool IsLocalModel(string name) => _localModelNames.Contains(name);

    private void ClearFilters()
    {
        _suppressFilterEvents = true;
        _keyword = "";
        _searchBox.Text = "";
        _brand = "全部";
        UpdateBrandButtons(_brandStack, "全部");
        _suppressFilterEvents = false;
        RefreshList();
    }

    private void OnRowTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border { Tag: GpuRankingEntry entry })
            LocateInFullRanking(entry);
    }

    private void LocateInFullRanking(GpuRankingEntry entry)
    {
        // 定位是逐行阅读，切回列表视图再跳转
        _viewMode = "list";
        UpdateViewToggle();
        ExpandNav();

        _locateTarget = entry;
        _suppressFilterEvents = true;
        _keyword = "";
        _searchBox.Text = "";
        _brand = "全部";
        UpdateBrandButtons(_brandStack, "全部");
        _sortBy = "rank";
        _sortCombo.SelectedIndex = 1;
        _suppressFilterEvents = false;

        RefreshList();

        _infoBar.Title = "已定位";
        _infoBar.Message = entry.Rank > 0
            ? $"{entry.Name} · 完整排名第 {entry.Rank} 名"
            : entry.Name;
        _infoBar.Severity = InfoBarSeverity.Informational;
        _infoBar.IsOpen = true;
    }

    private async Task BringRowIntoViewAsync(Border row)
    {
        _suppressNavToggle = true;
        _listScroll.UpdateLayout();
        row.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = true,
            VerticalAlignmentRatio = 0.5
        });

        await Task.Delay(600);
        _suppressNavToggle = false;

        if (_listContainer.Children.Contains(row))
            SearchHighlightService.HighlightBorder(row);
    }
}
