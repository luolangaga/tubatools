using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed class CpuRankingPage : Page
{
    private readonly Window _window;
    private string _category = "desktop";
    private string _brand = "全部";
    private string _keyword = "";
    private string _sortBy = "rating";
    private bool _isRefreshing;
    private double _lastScrollOffset;
    private bool _navCollapsed;

    private static readonly Color Gold = Color.FromArgb(255, 255, 215, 0);
    private readonly Color Silver = Color.FromArgb(255, 192, 192, 192);
    private readonly Color Bronze = Color.FromArgb(255, 205, 127, 50);
    private static readonly Color IntelBlue = Color.FromArgb(255, 0, 114, 198);
    private static readonly Color AmdRed = Color.FromArgb(255, 237, 28, 36);
    private static readonly Color AppleGray = Color.FromArgb(255, 160, 160, 160);
    private static readonly Color QualcommPurple = Color.FromArgb(255, 99, 71, 217);

    private static SvgImageSource? IntelLogo;
    private static SvgImageSource? AmdLogo;
    private static SvgImageSource? AppleLogo;
    private static SvgImageSource? QualcommLogo;

    private StackPanel _listContainer = null!;
    private ScrollViewer _listScroll = null!;
    private InfoBar _infoBar = null!;
    private ProgressBar _loadingBar = null!;
    private TextBlock _subtitleText = null!;
    private FrameworkElement _headerRow = null!;
    private FrameworkElement _filterRow = null!;
    private FrameworkElement _statsRow = null!;

    public CpuRankingPage(Window window)
    {
        _window = window;
        CpuRankingService.Load();
        LoadBrandLogos();

        var root = BuildUI();
        Content = root;

        RefreshList();
    }

    private static void LoadBrandLogos()
    {
        if (IntelLogo is not null) return;

        var brandsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Brands");
        IntelLogo = LoadSvg(System.IO.Path.Combine(brandsDir, "intel.svg"));
        AmdLogo = LoadSvg(System.IO.Path.Combine(brandsDir, "amd.svg"));
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
        "Intel" => IntelLogo,
        "AMD" => AmdLogo,
        "Apple" => AppleLogo,
        "Qualcomm" => QualcommLogo,
        _ => null
    };

    private Grid BuildUI()
    {
        var mainGrid = new Grid
        {
            Padding = new Thickness(28, 20, 28, 20),
            RowSpacing = 14
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
        var title = new TextBlock
        {
            Text = "CPU 天梯图",
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        };

        _subtitleText = new TextBlock
        {
            Text = $"数据来源 NanoReview · 更新于 {CpuRankingService.LastUpdated ?? "内置数据"} · 按 Cinebench 2024 排列",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            Margin = new Thickness(0, 4, 0, 0)
        };

        var refreshBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 13 },
                    new TextBlock { Text = "刷新数据", FontSize = 13 }
                }
            },
            Padding = new Thickness(14, 6, 14, 6),
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center
        };

        refreshBtn.Click += async (_, _) => await RefreshDataAsync();

        var headerGrid = new Grid { ColumnSpacing = 12 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(new StackPanel { Spacing = 2, Children = { title, _subtitleText } });
        headerGrid.Children.Add(refreshBtn); Grid.SetColumn(refreshBtn, 1);

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

        var outer = new StackPanel { Spacing = 12 };
        outer.Children.Add(headerGrid);
        outer.Children.Add(_loadingBar);
        outer.Children.Add(_infoBar);

        return outer;
    }

    private async Task RefreshDataAsync()
    {
        if (_isRefreshing) return;

        if (!CpuRankingService.CanRefresh)
        {
            var remaining = CpuRankingService.CooldownTime - (DateTime.Now - CpuRankingService.LastRefreshTime);
            _infoBar.Title = "提示";
            _infoBar.Message = $"数据已是最新，{remaining.Minutes} 分钟后可再次刷新";
            _infoBar.Severity = InfoBarSeverity.Warning;
            _infoBar.IsOpen = true;
            return;
        }

        _isRefreshing = true;
        _loadingBar.Visibility = Visibility.Visible;
        _infoBar.IsOpen = false;

        var refreshResult = await CpuRankingService.RefreshFromNetworkAsync();

        _isRefreshing = false;
        _loadingBar.Visibility = Visibility.Collapsed;

        if (refreshResult.Success)
        {
            _infoBar.Title = "刷新成功";
            _infoBar.Message = refreshResult.Message;
            _infoBar.Severity = InfoBarSeverity.Success;
            _infoBar.IsOpen = true;

            _subtitleText.Text = $"数据来源 NanoReview · 更新于 {CpuRankingService.LastUpdated} · 按 Cinebench 2024 排列";
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

        var brandBar = BuildBrandFilter();
        grid.Children.Add(brandBar);
        Grid.SetColumn(brandBar, 1);

        var searchBox = new AutoSuggestBox
        {
            PlaceholderText = "搜索 CPU 名称、制程...",
            QueryIcon = new SymbolIcon(Symbol.Find),
            MinWidth = 200
        };
        searchBox.TextChanged += (s, e) =>
        {
            _keyword = searchBox.Text;
            RefreshList();
        };
        grid.Children.Add(searchBox);
        Grid.SetColumn(searchBox, 2);

        var sortCombo = new ComboBox
        {
            MinWidth = 120,
            SelectedIndex = 0,
            Header = null
        };
        sortCombo.Items.Add("综合评分");
        sortCombo.Items.Add("单核性能");
        sortCombo.Items.Add("多核性能");
        sortCombo.Items.Add("排名顺序");
        sortCombo.SelectionChanged += (s, e) =>
        {
            _sortBy = sortCombo.SelectedIndex switch
            {
                0 => "rating",
                1 => "singleCore",
                2 => "multiCore",
                _ => "rank"
            };
            RefreshList();
        };
        grid.Children.Add(sortCombo);
        Grid.SetColumn(sortCombo, 3);

        return grid;
    }

    private Border BuildCategoryToggle()
    {
        var desktopBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE964", FontSize = 14 },
                    new TextBlock { Text = "桌面", FontSize = 13 }
                }
            },
            Padding = new Thickness(16, 8, 16, 8),
            CornerRadius = new CornerRadius(6, 0, 0, 6),
            Tag = "desktop"
        };

        var laptopBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE7F7", FontSize = 14 },
                    new TextBlock { Text = "笔记本", FontSize = 13 }
                }
            },
            Padding = new Thickness(16, 8, 16, 8),
            CornerRadius = new CornerRadius(0, 6, 6, 0),
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

        inactive.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        inactive.Foreground = new SolidColorBrush(ThemeColors.DimText);
        inactive.BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    }

    private StackPanel BuildBrandFilter()
    {
        var brands = new[] { "全部", "Intel", "AMD", "Apple", "Qualcomm" };
        var brandColors = new Dictionary<string, Color?>
        {
            ["全部"] = null, ["Intel"] = IntelBlue, ["AMD"] = AmdRed, ["Apple"] = AppleGray, ["Qualcomm"] = QualcommPurple
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
                    Spacing = 6,
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
                Padding = new Thickness(12, 6, 12, 6),
                CornerRadius = new CornerRadius(6),
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
            ["全部"] = null, ["Intel"] = IntelBlue, ["AMD"] = AmdRed, ["Apple"] = AppleGray, ["Qualcomm"] = QualcommPurple
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
                btn.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                btn.Foreground = new SolidColorBrush(ThemeColors.DimText);
                btn.BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                btn.BorderThickness = new Thickness(0);
            }
        }
    }

    private Grid BuildStatsCards()
    {
        var entries = CpuRankingService.GetByCategory(_category);
        var total = entries.Count;
        var intelCount = entries.Count(e => e.Brand == "Intel");
        var amdCount = entries.Count(e => e.Brand == "AMD");
        var topRating = entries.Count > 0 ? entries.MaxBy(e => e.Rating)?.Rating ?? 0 : 0;

        var totalCard = MakeStatCard("总计", $"{total} 款", "\uE9D9", ThemeColors.AccentBlue, null);
        var intelCard = MakeStatCard("Intel", $"{intelCount} 款", "\uE912", IntelBlue, IntelLogo);
        var amdCard = MakeStatCard("AMD", $"{amdCount} 款", "\uE9D5", AmdRed, AmdLogo);
        var topCard = MakeStatCard("最高分", $"{topRating} 分", "\uE8CA", Color.FromArgb(255, 251, 191, 36), null);

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(totalCard);
        grid.Children.Add(intelCard); Grid.SetColumn(intelCard, 1);
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
            iconChild = new FontIcon { FontSize = 18, Foreground = new SolidColorBrush(accent), Glyph = glyph };

        var iconBorder = new Border
        {
            Width = 38, Height = 38,
            Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(8),
            Child = iconChild
        };

        var labelBlock = new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var valueBlock = new TextBlock { Text = value, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(accent) };

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
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        AddHeader(headerGrid, "#", 0);
        AddHeader(headerGrid, "CPU", 1);
        AddHeader(headerGrid, "品牌", 2);
        AddHeader(headerGrid, "评分", 3);
        AddHeader(headerGrid, "等级", 4);
        AddHeader(headerGrid, "单核/多核", 5);
        AddHeader(headerGrid, "核心数", 6);
        AddHeader(headerGrid, "功耗", 7);

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
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };
        grid.Children.Add(tb);
        Grid.SetColumn(tb, column);
    }

    private void RefreshList()
    {
        if (_listContainer is null) return;

        var entries = CpuRankingService.GetByCategory(_category);
        entries = CpuRankingService.Filter(entries, _brand, _keyword);

        entries = _sortBy switch
        {
            "singleCore" => entries.OrderByDescending(e => e.SingleCore).ToList(),
            "multiCore" => entries.OrderByDescending(e => e.MultiCore).ToList(),
            "rating" => entries.OrderByDescending(e => e.Rating).ToList(),
            _ => entries.OrderBy(e => e.Rank).ToList()
        };

        _listContainer.Children.Clear();

        foreach (var entry in entries)
        {
            _listContainer.Children.Add(CreateRow(entry));
        }

        RefreshStats(entries);
    }

    private void RefreshStats(List<CpuRankingEntry> filtered)
    {
        var mainGrid = Content as Grid;
        if (mainGrid is null) return;

        var statsRow = mainGrid.Children.FirstOrDefault(c => Grid.GetRow(c as FrameworkElement) == 2);
        if (statsRow is Grid statsGrid)
        {
            statsGrid.Children.Clear();

            var total = filtered.Count;
            var intelCount = filtered.Count(e => e.Brand == "Intel");
            var amdCount = filtered.Count(e => e.Brand == "AMD");
            var topRating = filtered.Count > 0 ? filtered.MaxBy(e => e.Rating)?.Rating ?? 0 : 0;

            var totalCard = MakeStatCard("总计", $"{total} 款", "\uE9D9", ThemeColors.AccentBlue, null);
            var intelCard = MakeStatCard("Intel", $"{intelCount} 款", "\uE912", IntelBlue, IntelLogo);
            var amdCard = MakeStatCard("AMD", $"{amdCount} 款", "\uE9D5", AmdRed, AmdLogo);
            var topCard = MakeStatCard("最高分", $"{topRating} 分", "\uE8CA", Color.FromArgb(255, 251, 191, 36), null);

            statsGrid.Children.Add(totalCard);
            statsGrid.Children.Add(intelCard); Grid.SetColumn(intelCard, 1);
            statsGrid.Children.Add(amdCard); Grid.SetColumn(amdCard, 2);
            statsGrid.Children.Add(topCard); Grid.SetColumn(topCard, 3);
        }
    }

    private Border CreateRow(CpuRankingEntry entry)
    {
        var rankColor = entry.Rank <= 3 ? (entry.Rank == 1 ? Gold : entry.Rank == 2 ? Silver : Bronze) : ThemeColors.DimText;
        var brandColor = entry.Brand switch
        {
            "Intel" => IntelBlue, "AMD" => AmdRed, "Apple" => AppleGray, "Qualcomm" => QualcommPurple, _ => ThemeColors.DimText
        };

        var brandLogo = GetBrandLogo(entry.Brand);

        FrameworkElement rankBadge;
        if (entry.Rank <= 3)
        {
            rankBadge = new Border
            {
                Width = 32, Height = 32,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(40, rankColor.R, rankColor.G, rankColor.B)),
                Child = new TextBlock
                {
                    Text = entry.Rank.ToString(), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(rankColor),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
        }
        else
        {
            rankBadge = new TextBlock
            {
                Text = entry.Rank.ToString(), FontSize = 13, Foreground = new SolidColorBrush(ThemeColors.DimText),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 32
            };
        }

        var nameText = new TextBlock
        {
            Text = entry.Name, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText), VerticalAlignment = VerticalAlignment.Center
        };

        var processText = new TextBlock
        {
            Text = entry.Process, FontSize = 10, Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var namePanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        namePanel.Children.Add(nameText);
        namePanel.Children.Add(processText);

        FrameworkElement brandContent;
        if (brandLogo is not null)
        {
            brandContent = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 4,
                Children =
                {
                    new Image { Source = brandLogo, Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = entry.Brand, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(brandColor), VerticalAlignment = VerticalAlignment.Center }
                }
            };
        }
        else
        {
            brandContent = new TextBlock { Text = entry.Brand, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(brandColor), VerticalAlignment = VerticalAlignment.Center };
        }

        var brandBadge = new Border
        {
            Padding = new Thickness(8, 3, 8, 3), CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(30, brandColor.R, brandColor.G, brandColor.B)),
            Child = brandContent
        };

        var ratingBar = BuildRatingBar(entry.Rating);

        var gradeColor = entry.Grade switch
        {
            "A+" => ThemeColors.AccentBlue, "A" => Color.FromArgb(255, 96, 165, 250),
            "B" => ThemeColors.AccentOrange, "C" => ThemeColors.AccentRed, _ => ThemeColors.DimText
        };

        var gradeBadge = new Border
        {
            Padding = new Thickness(6, 3, 6, 3), CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(30, gradeColor.R, gradeColor.G, gradeColor.B)),
            Child = new TextBlock { Text = entry.Grade, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(gradeColor), VerticalAlignment = VerticalAlignment.Center }
        };

        var scoreText = new TextBlock
        {
            Text = $"{entry.SingleCore} / {entry.MultiCore}", FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText), VerticalAlignment = VerticalAlignment.Center
        };

        var coresText = new TextBlock
        {
            Text = entry.Cores, FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var tdpText = new TextBlock
        {
            Text = entry.Tdp, FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var rowGrid = new Grid { ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        rowGrid.Children.Add(rankBadge); Grid.SetColumn(rankBadge, 0);
        rowGrid.Children.Add(namePanel); Grid.SetColumn(namePanel, 1);
        rowGrid.Children.Add(brandBadge); Grid.SetColumn(brandBadge, 2);
        rowGrid.Children.Add(ratingBar); Grid.SetColumn(ratingBar, 3);
        rowGrid.Children.Add(gradeBadge); Grid.SetColumn(gradeBadge, 4);
        rowGrid.Children.Add(scoreText); Grid.SetColumn(scoreText, 5);
        rowGrid.Children.Add(coresText); Grid.SetColumn(coresText, 6);
        rowGrid.Children.Add(tdpText); Grid.SetColumn(tdpText, 7);

        return new Border
        {
            Padding = new Thickness(14, 8, 14, 8),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = rowGrid
        };
    }

    private static StackPanel BuildRatingBar(int rating)
    {
        var barWidth = 60;
        var filledWidth = (int)(barWidth * rating / 100.0);

        var barColor = rating >= 90 ? ThemeColors.AccentBlue
            : rating >= 75 ? Color.FromArgb(255, 96, 165, 250)
            : rating >= 60 ? ThemeColors.AccentOrange
            : ThemeColors.AccentRed;

        var filled = new Rectangle
        {
            Width = Math.Max(filledWidth, 2), Height = 6,
            Fill = new SolidColorBrush(barColor), RadiusX = 3, RadiusY = 3,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var track = new Border
        {
            Width = barWidth, Height = 6,
            Background = new SolidColorBrush(Color.FromArgb(30, ThemeColors.DimText.R, ThemeColors.DimText.G, ThemeColors.DimText.B)),
            CornerRadius = new CornerRadius(3), Child = filled
        };

        var ratingText = new TextBlock
        {
            Text = rating.ToString(), FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(barColor), VerticalAlignment = VerticalAlignment.Center, Width = 18
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(ratingText);
        panel.Children.Add(track);

        return panel;
    }
}