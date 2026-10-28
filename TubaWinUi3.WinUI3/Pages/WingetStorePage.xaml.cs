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
    private string _currentCategory = "精选";
    private string _currentQuery = "";
    private CancellationTokenSource? _searchCts;
    private int _searchVersion;
    private bool _catalogLoaded;

    public WingetStorePage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        CancelSearch();
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (!_catalogLoaded)
        {
            await LoadCatalogAsync();
        }
        else if (!string.IsNullOrEmpty(_currentQuery))
            await SearchAsync(_currentQuery);
        else
            ApplyFilter();
    }

    private async Task LoadCatalogAsync()
    {
        ShowLoading("正在加载软件目录…");
        _catalog = await WingetStoreService.LoadCatalogAsync();
        if (_catalog.Count == 0)
        {
            ShowEmpty("无法加载软件目录，请重新加载。");
            return;
        }
        _catalogLoaded = true;
        CatalogSummary.Text = $"{_catalog.Sum(c => GetAllPackages(c).Count)} 款精选应用 · {_catalog.Count} 个分类 · AI 编程与日常办公，一站发现。";
        BuildCategoryBar();
        ApplyFilter();
    }

    private void BuildCategoryBar()
    {
        CategoryNavigation.MenuItems.Clear();
        CategoryNavigation.MenuItems.Add(new NavigationViewItem { Content = "精选", Tag = "精选", Icon = new SymbolIcon(Symbol.Favorite) });
        foreach (var category in _catalog)
            CategoryNavigation.MenuItems.Add(new NavigationViewItem
            {
                Content = category.Name,
                Tag = category.Name,
                Icon = new FontIcon { Glyph = category.Glyph }
            });
        CategoryNavigation.SelectedItem = CategoryNavigation.MenuItems[0];
    }

    private void CategoryNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string category }) return;
        CancelSearch();
        SearchBox.Text = "";
        _currentQuery = "";
        _currentCategory = category;
        ApplyFilter();
    }

    private static List<StorePackage> GetAllPackages(StoreCategory category) =>
        category.Packages.Concat(category.SubCategories?.SelectMany(s => s.Packages) ?? [])
            .DistinctBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToList();

    private void ApplyFilter()
    {
        if (_catalog.Count == 0) return;
        SearchErrorBar.IsOpen = false;
        HomeIntro.Visibility = _currentCategory == "精选" ? Visibility.Visible : Visibility.Collapsed;
        if (_currentCategory == "精选")
        {
            var agents = _catalog.FirstOrDefault(c => c.Name == "AI Agent");
            var essentials = _catalog.Where(c => c != agents).SelectMany(GetAllPackages)
                .Where(p => p.IsRecommended).Take(6);
            var sections = new List<StoreSection>();
            if (agents is not null)
                sections.Add(new StoreSection("AI Agent", "从编写代码到完成工作，探索六款智能助手。", GetAllPackages(agents)));
            sections.Add(new StoreSection("装机精选", "先装好这几款；更多选择在上方分类中。", essentials));
            StoreGroups.Source = sections;
        }
        else
        {
            var category = _catalog.FirstOrDefault(c => c.Name == _currentCategory);
            if (category is null) return;
            var packages = GetAllPackages(category);
            StoreGroups.Source = new[] { new StoreSection(category.Name, $"精选 {packages.Count} 款应用", packages) };
        }
        ShowContent();
    }

    private void CancelSearch()
    {
        _searchVersion++;
        _searchCts?.Cancel();
    }

    private async void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            await SearchAsync(sender.Text.Trim(), debounce: true);
    }

    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) =>
        await SearchAsync(args.QueryText.Trim());

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape) return;
        ResetButton_Click(sender, e);
        e.Handled = true;
    }

    private async Task SearchAsync(string query, bool debounce = false)
    {
        CancelSearch();
        var version = _searchVersion;
        using var cts = new CancellationTokenSource();
        _searchCts = cts;
        _currentQuery = query;
        try
        {
            if (string.IsNullOrWhiteSpace(query)) { ApplyFilter(); return; }
            if (debounce) await Task.Delay(350, cts.Token);
            ShowLoading("正在搜索精选目录与 WinGet 官方源…");
            var combined = WingetStoreService.SearchLocal(query, _catalog);
            var searchStatus = new Progress<string>(text =>
            {
                if (version == _searchVersion) LoadingText.Text = text;
            });
            var result = await WingetStoreService.SearchOnlineAsync(query, searchStatus, cts.Token);
            var onlineError = result.Error;
            var known = combined.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var online in result.Results)
                if (known.Add(online.PackageIdentifier))
                    combined.Add(new StorePackage
                    {
                        Id = online.PackageIdentifier, Name = online.PackageName,
                        Description = online.Publisher ?? online.PackageIdentifier,
                        Category = "在线搜索", Glyph = "\uE774", IsOnlineResult = true
                    });
            cts.Token.ThrowIfCancellationRequested();
            if (version != _searchVersion) return;
            HomeIntro.Visibility = Visibility.Collapsed;
            StoreGroups.Source = new[] { new StoreSection($"“{query}”的搜索结果", $"找到 {combined.Count} 款应用", combined) };
            if (combined.Count == 0) ShowEmpty("未找到相关应用，试试其他名称或 WinGet 包 ID。");
            else ShowContent();
            SearchErrorBar.Title = "在线搜索不可用";
            SearchErrorBar.Message = onlineError ?? "";
            SearchErrorBar.IsOpen = !string.IsNullOrEmpty(onlineError);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (version == _searchVersion) ShowEmpty($"搜索失败：{ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts)) _searchCts = null;
        }
    }

    private void PackageGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is StorePackage package)
            Frame.Navigate(typeof(WingetStoreDetailPage), package, new DrillInNavigationTransitionInfo());
    }

    private void PackageGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (PackageGrid.ItemsPanelRoot is not ItemsWrapGrid panel) return;
        var available = Math.Max(1, e.NewSize.Width - 16);
        var columns = Math.Clamp((int)(available / 280), 1, 4);
        panel.ItemWidth = available / columns;
        HomeIntro.Width = Math.Max(1, available - 12);
    }

    private void InstallButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StorePackage package } button) return;
        button.IsEnabled = package.CanInstall && package.InstallState is not ("installing" or "queued" or "done");
        button.Content = package.InstallState switch
        {
            "installing" => "正在安装…", "queued" => "已加入队列", "done" => "已安装", "error" => "重试", _ => package.ActionLabel
        };
        ToolTipService.SetToolTip(button, package.UnavailableReason ?? package.StatusText ?? $"安装 {package.Name} · {package.SourceLabel}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{button.Content} {package.Name}");
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not StorePackage pkg) return;
        if (!pkg.CanInstall || pkg.InstallState is "installing" or "queued" or "done") return;
        btn.IsEnabled = false;
        btn.Content = StoreVisuals.BuildResolvingContent("正在安装…");
        pkg.InstallState = "installing";
        try
        {
            var progress = new Progress<string>(status =>
            {
                pkg.StatusText = status;
                ToolTipService.SetToolTip(btn, status);
                if (pkg.InstallState == "installing")
                    btn.Content = StoreVisuals.BuildResolvingContent(status.StartsWith("等待") ? "等待安装…" : "正在安装…");
            });
            var result = await WingetStoreService.InstallStorePackageAsync(pkg, progress);
            pkg.InstallState = result.Queued ? "queued" : "done";
            pkg.StatusText = result.Message;
            btn.Content = result.Queued ? "已加入队列" : "已安装";
            ToolTipService.SetToolTip(btn, result.Message);
            if (result.Queued) return;
        }
        catch (Exception ex)
        {
            pkg.InstallState = "error";
            pkg.StatusText = ex.Message;
            btn.IsEnabled = true;
            btn.Content = "重试";
            if (XamlRoot is null) return;
            await new ContentDialog
            {
                Title = $"无法安装 {pkg.Name}", Content = new TextBlock
                {
                    Text = ex.Message, TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true, MaxHeight = 400
                },
                CloseButtonText = "确定", XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

    private void ShowLoading(string text)
    {
        LoadingText.Text = text;
        LoadingPanel.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
        PackageGrid.Visibility = Visibility.Collapsed;
        SearchErrorBar.IsOpen = false;
    }

    private void ShowEmpty(string text)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Visible;
        PackageGrid.Visibility = Visibility.Collapsed;
        EmptyText.Text = text;
    }

    private void ShowContent()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Collapsed;
        PackageGrid.Visibility = Visibility.Visible;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        CancelSearch();
        SearchBox.Text = "";
        _currentQuery = "";
        _currentCategory = "精选";
        CategoryNavigation.SelectedItem = CategoryNavigation.MenuItems.FirstOrDefault();
        ApplyFilter();
    }

    private async void ReloadButton_Click(object sender, RoutedEventArgs e) => await LoadCatalogAsync();
}
