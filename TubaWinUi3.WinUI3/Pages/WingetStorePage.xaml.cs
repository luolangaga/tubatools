using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class WingetStorePage : Page
{
    private List<StoreCategory> _catalog = [];
    private string _currentCategory = "全部";
    private string _currentQuery = "";
    private CancellationTokenSource? _searchCts;

    public WingetStorePage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;

        // 代码构建的画刷不会随主题自动刷新，切换主题后按缓存目录重渲染
        ActualThemeChanged += (_, _) =>
        {
            if (_catalog.Count > 0)
                ApplyFilter();
        };
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _searchCts?.Cancel();
    }

    private bool _wingetAvailable;

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnPageLoaded;
        _ = CheckWingetAvailabilityAsync();
        await LoadCatalogAsync();
    }

    private async Task CheckWingetAvailabilityAsync()
    {
        _wingetAvailable = await WingetStoreService.IsWingetAvailableAsync();
        DispatcherQueue.TryEnqueue(() =>
        {
            PageHeader.Subtitle = _wingetAvailable
                ? "浏览并安装正版软件 · 搜索支持 WinGet 在线查询"
                : "浏览并安装正版软件 · 仅本地目录（未检测到 WinGet）";
        });
    }

    #region Catalog Loading

    private async Task LoadCatalogAsync()
    {
        ShowLoading("正在加载软件目录...");
        try
        {
            _catalog = await WingetStoreService.LoadCatalogAsync();
            if (_catalog.Count == 0)
            {
                ShowEmpty("无法加载软件目录");
                return;
            }

            BuildCategoryBar();

            var totalCount = _catalog.Sum(c =>
            {
                var n = c.Packages.Count;
                if (c.SubCategories is not null)
                    n += c.SubCategories.Sum(s => s.Packages.Count);
                return n;
            });
            PageHeader.Subtitle = $"共 {_catalog.Count} 个分类 · {totalCount} 款正版软件";

            ShowCategory("全部");
        }
        catch (Exception ex)
        {
            ShowEmpty($"加载失败：{ex.Message}");
        }
    }

    #endregion

    #region Category Bar

    private void BuildCategoryBar()
    {
        CategoryBar.Children.Clear();

        AddCategoryChip("全部", "\uE719");
        foreach (var cat in _catalog)
            AddCategoryChip(cat.Name, cat.Glyph);
    }

    private void AddCategoryChip(string name, string glyph)
    {
        var btn = new RadioButton
        {
            GroupName = "StoreCategory",
            IsChecked = name == "全部",
            Tag = name,
            Style = (Style)Resources["StoreCategoryChipStyle"],
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(glyph) ? "\uE719" : glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        btn.Content = panel;

        btn.Checked += (_, _) =>
        {
            _currentCategory = name;
            ApplyFilter();
        };

        CategoryBar.Children.Add(btn);
    }

    #endregion

    #region Search

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _ = DebouncedSearchAsync(cts.Token);
    }

    private async Task DebouncedSearchAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(350, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var query = SearchBox.Text?.Trim() ?? "";
        _currentQuery = query;
        await ExecuteSearchAsync(query);
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _searchCts?.Cancel();
        _searchCts = null;
        _currentQuery = args.QueryText?.Trim() ?? "";
        _ = ExecuteSearchAsync(_currentQuery);
    }

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            _searchCts?.Cancel();
            _searchCts = null;
            SearchBox.Text = "";
            _currentQuery = "";
            ApplyFilter();
        }
    }

    private async Task ExecuteSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ApplyFilter();
            return;
        }

        ShowLoading("正在搜索...");
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        try
        {
            var localResults = WingetStoreService.SearchLocal(query, _catalog);

            List<WingetSearchResult> onlineResults = [];
            string? onlineError = null;
            if (_wingetAvailable)
            {
                try
                {
                    var result = await WingetStoreService.SearchOnlineAsync(query, ct);
                    onlineResults = result.Results;
                    onlineError = result.Error;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { onlineError = $"在线搜索出错：{ex.Message}"; }
            }
            else
            {
                onlineError = "未检测到 WinGet，仅显示本地目录结果";
            }

            ct.ThrowIfCancellationRequested();

            var localIds = new HashSet<string>(localResults.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
            var combined = new List<StorePackage>(localResults);

            foreach (var online in onlineResults)
            {
                if (localIds.Contains(online.PackageIdentifier)) continue;
                combined.Add(new StorePackage
                {
                    Id = online.PackageIdentifier,
                    Name = online.PackageName,
                    Description = online.Publisher ?? online.PackageIdentifier,
                    Category = "在线搜索",
                    Glyph = "\uE774",
                    IsOnlineResult = true
                });
            }

            if (combined.Count == 0)
            {
                ShowEmpty(onlineError ?? "未找到相关软件");
                return;
            }

            ShowContent();
            SearchErrorBar.Message = onlineError ?? "";
            SearchErrorBar.IsOpen = !string.IsNullOrEmpty(onlineError);
            BuildSingleGroup($"搜索 \"{query}\"", combined);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ShowEmpty($"搜索失败：{ex.Message}");
        }
    }

    #endregion

    #region Filter & Display

    private void ApplyFilter()
    {
        if (_catalog.Count == 0) return;

        if (_currentCategory == "全部")
        {
            ShowContent();
            BuildAllGroups();
        }
        else
        {
            var cat = _catalog.FirstOrDefault(c => c.Name == _currentCategory);
            if (cat is null) return;

            ShowContent();
            var allPkgs = GetAllPackages(cat);
            BuildSingleGroup(cat.Name, allPkgs);
        }
    }

    private static List<StorePackage> GetAllPackages(StoreCategory cat)
    {
        var list = new List<StorePackage>(cat.Packages);
        if (cat.SubCategories is not null)
        {
            foreach (var sub in cat.SubCategories)
                list.AddRange(sub.Packages);
        }
        return list;
    }

    private void BuildAllGroups()
    {
        GroupsPanel.Children.Clear();

        foreach (var cat in _catalog)
        {
            var allPkgs = GetAllPackages(cat);
            if (allPkgs.Count == 0) continue;
            GroupsPanel.Children.Add(CreateGroupSection(cat.Name, cat.Glyph, allPkgs));
        }
    }

    private void BuildSingleGroup(string title, List<StorePackage> packages)
    {
        GroupsPanel.Children.Clear();
        GroupsPanel.Children.Add(CreateGroupSection(title, "\uE719", packages));
    }

    private UIElement CreateGroupSection(string title, string glyph, List<StorePackage> packages)
    {
        var section = new StackPanel { Spacing = 16 };

        // 标题行：图标 + 标题 + 数量
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

        header.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(glyph) ? "\uE719" : glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 14,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            VerticalAlignment = VerticalAlignment.Center
        });

        header.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["SectionHeaderStyle"],
            VerticalAlignment = VerticalAlignment.Center
        });

        header.Children.Add(new Border
        {
            Style = (Style)Application.Current.Resources["StatusPillStyle"],
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = packages.Count.ToString(),
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
            }
        });

        section.Children.Add(header);

        // 软件卡片网格
        var grid = new GridView
        {
            IsItemClickEnabled = true,
            SelectionMode = ListViewSelectionMode.None,
            Padding = new Thickness(0),
            ItemsPanel = CreateItemsPanelTemplate(),
            ItemContainerStyle = (Style)Resources["StoreCardItemStyle"],
        };

        grid.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is StorePackage pkg)
                Frame.Navigate(typeof(WingetStoreDetailPage), pkg, new DrillInNavigationTransitionInfo());
        };

        grid.SizeChanged += (_, _) =>
        {
            if (grid.ItemsPanelRoot is not ItemsWrapGrid wrapGrid) return;
            var available = grid.ActualWidth - grid.Padding.Left - grid.Padding.Right;
            if (available <= 0) return;
            var spacing = 12.0;
            var minItemWidth = 240.0;
            var cols = Math.Max(1, (int)((available + spacing) / (minItemWidth + spacing)));
            var itemWidth = (available - (cols - 1) * spacing) / cols;
            wrapGrid.ItemWidth = Math.Max(minItemWidth, itemWidth);
        };

        foreach (var pkg in packages)
            grid.Items.Add(CreatePackageCard(pkg));

        section.Children.Add(grid);
        return section;
    }

    /// <summary>
    /// 简约信息卡：名称 + 推荐徽章 + 两行描述 + 获取按钮，无图标
    /// </summary>
    private FrameworkElement CreatePackageCard(StorePackage pkg)
    {
        var accent = ThemeColors.AccentBlue;
        var accentBrush = new SolidColorBrush(accent);
        var accentTintBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x20, accent.R, accent.G, accent.B));

        var idleBrush = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        var idleStroke = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
        var hoverBrush = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];

        var card = new Grid
        {
            Padding = new Thickness(16),
            Background = idleBrush,
            BorderBrush = idleStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 名称行：名称 + 推荐 badge
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        nameRow.Children.Add(new TextBlock
        {
            Text = pkg.Name,
            Style = (Style)Application.Current.Resources["CardTitleStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (pkg.IsRecommended)
        {
            nameRow.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["StatusPillStyle"],
                Background = accentTintBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "推荐",
                    FontSize = 12,
                    Foreground = accentBrush
                }
            });
        }

        card.Children.Add(nameRow);

        // 描述：固定两行高度（40px），保证所有卡片等高
        var descText = new TextBlock
        {
            Text = pkg.Description ?? "来自 WinGet 官方源",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            LineHeight = 20,
            Height = 40,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(descText, 1);
        card.Children.Add(descText);

        // 底部：获取按钮
        var installBtn = new Button
        {
            Height = 32,
            Padding = new Thickness(16, 0, 16, 0),
            CornerRadius = new CornerRadius(4),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
            Tag = pkg,
            Content = StoreVisuals.BuildInstallContent("获取", "\uE896", null)
        };
        installBtn.Click += InstallButton_Click;
        Grid.SetRow(installBtn, 2);
        card.Children.Add(installBtn);

        // 悬浮：accent 描边 + 背景提亮
        card.PointerEntered += (_, _) =>
        {
            card.Background = hoverBrush;
            card.BorderBrush = accentBrush;
        };
        card.PointerExited += (_, _) =>
        {
            card.Background = idleBrush;
            card.BorderBrush = idleStroke;
        };

        return card;
    }

    #endregion

    #region Install

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not StorePackage pkg) return;
        if (pkg.InstallState is "resolving" or "queued") return;

        btn.IsEnabled = false;
        btn.Content = StoreVisuals.BuildResolvingContent("获取链接...");
        pkg.InstallState = "resolving";

        try
        {
            var progress = new Progress<string>(status => DispatcherQueue.TryEnqueue(() =>
            {
                if (status == "已加入下载队列")
                {
                    btn.IsEnabled = false;
                    btn.Content = StoreVisuals.BuildInstallContent("已加入队列", "\uE73E",
                        new SolidColorBrush(ThemeColors.AccentGreen));
                    pkg.InstallState = "queued";
                }
                else
                {
                    btn.Content = StoreVisuals.BuildResolvingContent(status);
                }
            }));

            var item = await WingetStoreService.InstallPackageAsync(
                pkg.Id, pkg.Name, pkg.Glyph,
                progress,
                CancellationToken.None);

            if (item is not null)
            {
                pkg.InstallState = "queued";
                btn.IsEnabled = false;
                btn.Content = StoreVisuals.BuildInstallContent("已加入队列", "\uE73E",
                    new SolidColorBrush(ThemeColors.AccentGreen));
            }
            else
            {
                pkg.InstallState = "error";
                btn.IsEnabled = true;
                btn.Content = StoreVisuals.BuildInstallContent("重试", "\uE72C", null);
            }
        }
        catch (Exception ex)
        {
            pkg.InstallState = "error";
            btn.IsEnabled = true;
            btn.Content = StoreVisuals.BuildInstallContent("重试", "\uE72C", null);

            var dialog = new ContentDialog
            {
                Title = "安装失败",
                Content = $"无法安装 {pkg.Name}：\n{ex.Message}",
                CloseButtonText = "确定",
                XamlRoot = XamlRoot
            };
            _ = dialog.ShowAsync();
        }
    }

    #endregion

    #region UI State

    private void ShowLoading(string text = "正在加载...")
    {
        LoadingText.Text = text;
        LoadingPanel.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
        ContentScroller.Visibility = Visibility.Collapsed;
        SearchErrorBar.IsOpen = false;
        GroupsPanel.Children.Clear();
    }

    private void ShowEmpty(string text)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Visible;
        ContentScroller.Visibility = Visibility.Collapsed;
        SearchErrorBar.IsOpen = false;
        GroupsPanel.Children.Clear();
        EmptyText.Text = text;
    }

    private void ShowContent()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Collapsed;
        ContentScroller.Visibility = Visibility.Visible;
    }

    private void ShowCategory(string name)
    {
        foreach (var child in CategoryBar.Children)
        {
            if (child is RadioButton rb)
                rb.IsChecked = (string)rb.Tag == name;
        }
        _currentCategory = name;
        ApplyFilter();
    }

    #endregion

    #region Helpers

    private static ItemsPanelTemplate CreateItemsPanelTemplate()
    {
        // ItemHeight 不固定：卡片高度由内容决定，避免挤压
        return (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                <ItemsWrapGrid Orientation="Horizontal" ItemWidth="240" />
            </ItemsPanelTemplate>
            """);
    }

    #endregion
}
