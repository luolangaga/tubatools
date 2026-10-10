using Microsoft.UI.Xaml;
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
    private string _category = "desktop";
    private string _brand = "全部";
    private string _keyword = "";
    private string _sortBy = "tflops";
    private bool _isRefreshing;
    private double _lastScrollOffset;
    private bool _navCollapsed;
    private bool _suppressNavToggle;
    private bool _suppressFilterEvents;
    private GpuRankingEntry? _locateTarget;
    private AutoSuggestBox _searchBox = null!;
    private StackPanel _brandStack = null!;
    private ComboBox _sortCombo = null!;
    private Button _desktopBtn = null!;
    private Button _laptopBtn = null!;

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

    public GpuRankingPage()
    {
        InitializeComponent();
        GpuRankingService.Load();
        LoadBrandLogos();

        var root = BuildUI();
        Content = root;

        RefreshList();

        // 代码构建的画刷不会随主题自动刷新，切换主题后按当前筛选状态重渲染
        ActualThemeChanged += (_, _) =>
        {
            if (_brandStack is not null)
                UpdateBrandButtons(_brandStack, _brand);
            if (_desktopBtn is not null && _laptopBtn is not null)
            {
                var desktopActive = _category != "laptop";
                UpdateCategoryButtons(desktopActive ? _desktopBtn : _laptopBtn,
                                      desktopActive ? _laptopBtn : _desktopBtn);
            }
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

        return mainGrid;
    }

    private StackPanel BuildHeader()
    {
        _toolHeader = new ToolPageHeader
        {
            ToolId = "gpu-ranking",
            HeaderPadding = new Thickness(0, 16, 0, 0),
            Title = "GPU 天梯图",
            Subtitle = $"数据来源 TopCPU.net · 更新于 {GpuRankingService.LastUpdated ?? "内置数据"} · FP32 浮点性能排列",
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

            _toolHeader.Subtitle = $"数据来源 TopCPU.net · 更新于 {GpuRankingService.LastUpdated} · FP32 浮点性能排列";
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
        var grid = new Grid { ColumnSpacing = 16, VerticalAlignment = VerticalAlignment.Center };
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
            PlaceholderText = "搜索 GPU 名称...",
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
        _sortCombo.Items.Add("TFLOPS");
        _sortCombo.Items.Add("评分");
        _sortCombo.Items.Add("排名顺序");
        _sortCombo.SelectionChanged += (s, e) =>
        {
            if (_suppressFilterEvents) return;
            _sortBy = _sortCombo.SelectedIndex switch
            {
                0 => "tflops",
                1 => "rating",
                _ => "rank"
            };
            RefreshList();
        };
        grid.Children.Add(_sortCombo);
        Grid.SetColumn(_sortCombo, 3);

        return grid;
    }

    private Border BuildCategoryToggle()
    {
        var desktopBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE964", FontSize = 14 },
                    new TextBlock { Text = "桌面", FontSize = 14 }
                }
            },
            Padding = new Thickness(16, 8, 16, 8),
            CornerRadius = new CornerRadius(8, 0, 0, 8),
            Tag = "desktop"
        };

        var laptopBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE7F7", FontSize = 14 },
                    new TextBlock { Text = "笔记本", FontSize = 14 }
                }
            },
            Padding = new Thickness(16, 8, 16, 8),
            CornerRadius = new CornerRadius(0, 8, 8, 0),
            Tag = "laptop"
        };

        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        stack.Children.Add(desktopBtn);
        stack.Children.Add(laptopBtn);

        desktopBtn.Click += (s, e) =>
        {
            _category = "desktop";
            UpdateCategoryButtons(desktopBtn, laptopBtn);
            RefreshList();
        };
        laptopBtn.Click += (s, e) =>
        {
            _category = "laptop";
            UpdateCategoryButtons(laptopBtn, desktopBtn);
            RefreshList();
        };

        UpdateCategoryButtons(desktopBtn, laptopBtn);
        _desktopBtn = desktopBtn;
        _laptopBtn = laptopBtn;

        return new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
            Child = stack
        };
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
        var brandColors = new Dictionary<string, Color?>
        {
            ["全部"] = null, ["Nvidia"] = NvidiaGreen, ["AMD"] = AmdRed,
            ["Intel"] = IntelBlue, ["Apple"] = AppleGray, ["Qualcomm"] = QualcommPurple
        };

        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        foreach (var label in brands)
        {
            var c = brandColors[label];
            var logo = GetBrandLogo(label);

            FrameworkElement btnContent;
            if (label == "全部")
            {
                btnContent = new TextBlock { Text = "全部", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
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
                        new TextBlock { Text = label, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }
                    }
                };
            }
            else
            {
                btnContent = new TextBlock { Text = label, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
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
        var entries = GpuRankingService.GetByCategory(_category);
        var total = entries.Count;
        var nvidiaCount = entries.Count(e => e.Brand == "Nvidia");
        var amdCount = entries.Count(e => e.Brand == "AMD");
        var topRating = entries.Count > 0 ? entries.MaxBy(e => e.Rating)?.Rating ?? 0 : 0;

        var totalCard = MakeStatCard("总计", $"{total} 款", "\uE9D9", ThemeColors.AccentBlue, null);
        var nvidiaCard = MakeStatCard("Nvidia", $"{nvidiaCount} 款", "\uE912", NvidiaGreen, NvidiaLogo);
        var amdCard = MakeStatCard("AMD", $"{amdCount} 款", "\uE9D5", AmdRed, AmdLogo);
        var topCard = MakeStatCard("最高分", $"{topRating} 分", "\uE8CA", ThemeColors.Series4, null);

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(totalCard);
        grid.Children.Add(nvidiaCard); Grid.SetColumn(nvidiaCard, 1);
        grid.Children.Add(amdCard); Grid.SetColumn(amdCard, 2);
        grid.Children.Add(topCard); Grid.SetColumn(topCard, 3);

        return grid;
    }

    private static Border MakeStatCard(string label, string value, string glyph, Color accent, SvgImageSource? logo)
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

        var labelBlock = new TextBlock { Text = label, FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var valueBlock = new TextBlock { Text = value, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(accent) };

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(labelBlock);
        stack.Children.Add(valueBlock);

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
        var headerGrid = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(14, 8, 14, 8)
        };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

        AddHeader(headerGrid, "", 0);
        AddHeader(headerGrid, "排名", 1);
        AddHeader(headerGrid, "GPU", 2);
        AddHeader(headerGrid, "TFLOPS", 3);
        AddHeader(headerGrid, "显存", 4);

        var headerBorder = new Border
        {
            Background = new SolidColorBrush(ThemeColors.HeaderBg),
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Child = headerGrid
        };

        _listContainer = new StackPanel { Spacing = 2 };

        _listScroll = new ScrollViewer
        {
            Content = _listContainer,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        _listScroll.ViewChanged += OnListScrollChanged;

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

    private void OnListScrollChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_listScroll is null) return;

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

    private void CollapseNav()
    {
        _navCollapsed = true;
        if (_statsRow is not null) _statsRow.Visibility = Visibility.Collapsed;
        if (_filterRow is not null) _filterRow.Visibility = Visibility.Collapsed;
        if (_headerRow is not null) _headerRow.Visibility = Visibility.Collapsed;
    }

    private void ExpandNav()
    {
        _navCollapsed = false;
        if (_headerRow is not null) _headerRow.Visibility = Visibility.Visible;
        if (_filterRow is not null) _filterRow.Visibility = Visibility.Visible;
        if (_statsRow is not null) _statsRow.Visibility = Visibility.Visible;
    }

    private static void AddHeader(Grid grid, string text, int column)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        grid.Children.Add(tb);
        Grid.SetColumn(tb, column);
    }

    private void RefreshList()
    {
        if (_listContainer is null) return;

        var entries = GpuRankingService.GetByCategory(_category);
        entries = GpuRankingService.Filter(entries, _brand, _keyword);

        entries = _sortBy switch
        {
            "tflops" => entries.OrderByDescending(e => double.TryParse(e.Tflops, out var t) ? t : 0).ToList(),
            "rating" => entries.OrderByDescending(e => e.Rating).ToList(),
            _ => entries.OrderBy(e => e.Rank).ToList()
        };

        _listContainer.Children.Clear();

        Border? locateRow = null;
        var displayRank = 0;
        foreach (var entry in entries)
        {
            displayRank++;
            var row = CreateRow(entry, entry.Rank > 0 ? entry.Rank : displayRank);
            if (_locateTarget is not null && ReferenceEquals(entry, _locateTarget))
                locateRow = row;
            _listContainer.Children.Add(row);
        }

        RefreshStats(entries);

        if (_locateTarget is not null)
        {
            _locateTarget = null;
            if (locateRow is not null) _ = BringRowIntoViewAsync(locateRow);
        }
    }

    private void OnRowTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border { Tag: GpuRankingEntry entry })
            LocateInFullRanking(entry);
    }

    private void LocateInFullRanking(GpuRankingEntry entry)
    {
        ExpandNav();

        _locateTarget = entry;
        _suppressFilterEvents = true;
        _keyword = "";
        _searchBox.Text = "";
        _brand = "全部";
        UpdateBrandButtons(_brandStack, "全部");
        _sortBy = "rank";
        _sortCombo.SelectedIndex = 2;
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

    private void RefreshStats(List<GpuRankingEntry> filtered)
    {
        var mainGrid = Content as Grid;
        if (mainGrid is null) return;

        var statsRow = mainGrid.Children.FirstOrDefault(c => Grid.GetRow(c as FrameworkElement) == 2);
        if (statsRow is Grid statsGrid)
        {
            statsGrid.Children.Clear();

            var total = filtered.Count;
            var nvidiaCount = filtered.Count(e => e.Brand == "Nvidia");
            var amdCount = filtered.Count(e => e.Brand == "AMD");
            var topRating = filtered.Count > 0 ? filtered.MaxBy(e => e.Rating)?.Rating ?? 0 : 0;

            var totalCard = MakeStatCard("总计", $"{total} 款", "\uE9D9", ThemeColors.AccentBlue, null);
            var nvidiaCard = MakeStatCard("Nvidia", $"{nvidiaCount} 款", "\uE912", NvidiaGreen, NvidiaLogo);
            var amdCard = MakeStatCard("AMD", $"{amdCount} 款", "\uE9D5", AmdRed, AmdLogo);
            var topCard = MakeStatCard("最高分", $"{topRating} 分", "\uE8CA", ThemeColors.Series4, null);

            statsGrid.Children.Add(totalCard);
            statsGrid.Children.Add(nvidiaCard); Grid.SetColumn(nvidiaCard, 1);
            statsGrid.Children.Add(amdCard); Grid.SetColumn(amdCard, 2);
            statsGrid.Children.Add(topCard); Grid.SetColumn(topCard, 3);
        }
    }

    private Border CreateRow(GpuRankingEntry entry, int rank)
    {
        var rankColor = rank <= 3 ? (rank == 1 ? Gold : rank == 2 ? Silver : Bronze) : ThemeColors.DimText;
        var brandColor = GetBrandColor(entry.Brand);
        var brandLogo = GetBrandLogo(entry.Brand);

        FrameworkElement rankBadge;
        if (rank <= 3)
        {
            rankBadge = new Border
            {
                Width = 32, Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(40, rankColor.R, rankColor.G, rankColor.B)),
                Child = new TextBlock
                {
                    Text = rank.ToString(), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(rankColor),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
        }
        else
        {
            rankBadge = new TextBlock
            {
                Text = rank.ToString(), FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 32
            };
        }

        var nameText = new TextBlock
        {
            Text = entry.Name, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText), VerticalAlignment = VerticalAlignment.Center
        };

        var vramSubText = !string.IsNullOrWhiteSpace(entry.TimeSpy)
            ? new TextBlock
            {
                Text = entry.TimeSpy, FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText),
                VerticalAlignment = VerticalAlignment.Center
            }
            : null;

        var namePanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        namePanel.Children.Add(nameText);
        if (vramSubText is not null) namePanel.Children.Add(vramSubText);

        var tflopsColor = entry.Rating >= 80 ? NvidiaGreen
            : entry.Rating >= 50 ? ThemeColors.AccentBlue
            : entry.Rating >= 20 ? ThemeColors.AccentOrange
            : ThemeColors.DimText;

        var tflopsText = new TextBlock
        {
            Text = !string.IsNullOrWhiteSpace(entry.Tflops) ? entry.Tflops : "-",
            FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(tflopsColor), VerticalAlignment = VerticalAlignment.Center
        };

        var vramText = new TextBlock
        {
            Text = !string.IsNullOrWhiteSpace(entry.TimeSpy) ? entry.TimeSpy : "-",
            FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };

        FrameworkElement logoCell;
        if (brandLogo is not null)
        {
            logoCell = new Image { Source = brandLogo, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }
        else
        {
            logoCell = new Border
            {
                Width = 20, Height = 20,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(30, brandColor.R, brandColor.G, brandColor.B)),
                Child = new TextBlock
                {
                    Text = entry.Brand.Length > 0 ? entry.Brand[..1] : "?",
                    FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(brandColor),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        var rowGrid = new Grid { ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

        rowGrid.Children.Add(logoCell); Grid.SetColumn(logoCell, 0);
        rowGrid.Children.Add(rankBadge); Grid.SetColumn(rankBadge, 1);
        rowGrid.Children.Add(namePanel); Grid.SetColumn(namePanel, 2);
        rowGrid.Children.Add(tflopsText); Grid.SetColumn(tflopsText, 3);
        rowGrid.Children.Add(vramText); Grid.SetColumn(vramText, 4);

        var row = new Border
        {
            Padding = new Thickness(14, 8, 14, 8),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = rowGrid,
            Tag = entry
        };

        ToolTipService.SetToolTip(row, "点击在完整排名中定位");
        row.Tapped += OnRowTapped;
        row.PointerEntered += (s, e) => row.Background = new SolidColorBrush(ThemeColors.RowHover);
        row.PointerExited += (s, e) => row.Background = new SolidColorBrush(ThemeColors.CardBg);

        return row;
    }

}
