using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class CommunityToolsPage : Page
{
    private List<CommunityTool> _allTools = [];
    private List<CommunityTool> _filteredTools = [];
    private string? _currentCategory;
    private string? _currentSearch;
    private bool _loadFailed;
    private CancellationTokenSource? _loadCts;
    private bool _sourceReady;
    private DispatcherTimer? _statusBarTimer;

    /// <summary>已订阅进度变化的队列项（避免重复订阅）。</summary>
    private readonly HashSet<string> _subscribedItemIds = [];

    public CommunityToolsPage()
    {
        InitializeComponent();
        Loaded += CommunityToolsPage_Loaded;
        Unloaded += CommunityToolsPage_Unloaded;
    }

    private async void CommunityToolsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _sourceReady = false;
        SourceSelector.SelectedIndex = CommunityToolService.CurrentSource == CommunityDataSource.GitCode ? 0 : 1;
        _sourceReady = true;

        DownloadQueueService.QueueChanged += OnQueueChanged;
        await LoadToolsAsync();
    }

    private void CommunityToolsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        DownloadQueueService.QueueChanged -= OnQueueChanged;
        _loadCts?.Cancel();

        // 页面离开后不再跟踪下载进度（订阅全部断开；重新进入时会重新订阅）
        foreach (var item in DownloadQueueService.Queue)
            item.PropertyChanged -= Item_PropertyChanged;
        _subscribedItemIds.Clear();
    }

    private async void SourceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_sourceReady) return;
        var newSource = SourceSelector.SelectedIndex == 1 ? CommunityDataSource.GitHub : CommunityDataSource.GitCode;
        if (newSource == CommunityToolService.CurrentSource) return;
        CommunityToolService.CurrentSource = newSource;
        CommunityToolService.InvalidateCache();
        await LoadToolsAsync();
    }

    private async Task LoadToolsAsync()
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        LoadingProgress.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        ToolsGrid.Visibility = Visibility.Collapsed;

        try
        {
            var tools = await CommunityToolService.GetPluginsAsync(ct: cts.Token);

            if (cts.Token.IsCancellationRequested) return;

            _allTools = tools;
            _loadFailed = false;

            CommunityToolInstallService.PruneStaleRecords();
            await ApplyAuthorFlagsAsync();
            foreach (var tool in _allTools)
                RefreshLocalState(tool);

            UpdateCategoryFilter();
            ApplyFilter();

            StatusText.Text = _allTools.Count > 0 ? $"共 {_allTools.Count} 个社区工具" : "暂无社区工具";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (cts.Token.IsCancellationRequested) return;

            _allTools = [];
            _filteredTools = [];
            _loadFailed = true;

            ToolsGrid.ItemsSource = null;
            ToolsGrid.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            EmptyStateTitle.Text = "加载失败";
            EmptyStateText.Text = ex.InnerException?.Message ?? ex.Message;
            EmptyRetryLink.Visibility = Visibility.Visible;
            EmptySubmitLink.Visibility = Visibility.Collapsed;
            StatusText.Text = "加载失败";

            ShowStatus("加载社区工具失败", ex.InnerException?.Message ?? ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            LoadingProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async Task ApplyAuthorFlagsAsync()
    {
        string? userName = null;
        if (GitHubAuthService.IsLoggedIn)
        {
            try { userName = (await GitHubAuthService.GetCurrentUserAsync())?.Login; }
            catch { }
        }

        foreach (var tool in _allTools)
        {
            tool.IsAuthor = userName is not null &&
                !string.IsNullOrWhiteSpace(tool.Author) &&
                string.Equals(tool.Author, userName, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>刷新单个工具的本地状态：安装/更新、收藏、图标、队列忙碌态。</summary>
    private void RefreshLocalState(CommunityTool tool)
    {
        tool.InstallStatus = CommunityToolInstallService.CheckStatus(tool);

        if (tool.CanUninstall)
        {
            var launchPath = CommunityToolInstallService.ResolveLaunchPath(tool);
            if (!string.IsNullOrWhiteSpace(launchPath))
            {
                tool.IsFavorite = FavoritesService.IsFavorite(launchPath);

                // 已安装的工具优先用本地提取的 exe 图标，拿不到再回退远端图标 URL
                var cachedIcon = ToolIconService.GetCachedIconPath(launchPath);
                if (!string.IsNullOrWhiteSpace(cachedIcon))
                    tool.IconPath = cachedIcon;
            }
        }

        ApplyBusyState(tool, CommunityToolInstallService.FindActiveItem(tool));
    }

    private void UpdateCategoryFilter()
    {
        var prevSelection = _currentCategory;
        CategoryFilter.SelectionChanged -= CategoryFilter_SelectionChanged;
        CategoryFilter.Items.Clear();
        CategoryFilter.Items.Add("全部分类");

        var categories = _allTools.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();
        foreach (var cat in categories)
        {
            CategoryFilter.Items.Add(cat);
        }

        if (prevSelection is not null && categories.Contains(prevSelection))
            CategoryFilter.SelectedIndex = categories.IndexOf(prevSelection) + 1;
        else
            CategoryFilter.SelectedIndex = 0;

        _currentCategory = CategoryFilter.SelectedIndex == 0 ? null : (string?)CategoryFilter.SelectedItem;
        CategoryFilter.SelectionChanged += CategoryFilter_SelectionChanged;
    }

    private void ApplyFilter()
    {
        _filteredTools = _allTools;

        if (_currentCategory is not null)
            _filteredTools = _filteredTools.Where(t => t.Category == _currentCategory).ToList();

        if (!string.IsNullOrWhiteSpace(_currentSearch))
        {
            var q = _currentSearch.Trim().ToLowerInvariant();
            _filteredTools = _filteredTools.Where(t =>
                t.Name.ToLowerInvariant().Contains(q) ||
                (t.Description?.ToLowerInvariant().Contains(q) == true) ||
                t.Tags.Any(tag => tag.ToLowerInvariant().Contains(q))
            ).ToList();
        }

        ToolsGrid.ItemsSource = _filteredTools;
        ToolsGrid.Visibility = _filteredTools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = _filteredTools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolCardLayout.Apply(ToolsGrid);

        if (_filteredTools.Count == 0 && _allTools.Count > 0)
        {
            EmptyStateTitle.Text = "没有匹配的社区工具";
            EmptyStateText.Text = "试试更换关键字或清空筛选条件。";
            EmptyRetryLink.Visibility = Visibility.Collapsed;
            EmptySubmitLink.Visibility = Visibility.Collapsed;
        }
        else if (_allTools.Count == 0)
        {
            EmptyStateTitle.Text = "暂无社区工具";
            EmptyStateText.Text = _loadFailed ? "" : "成为第一个贡献者！";
            EmptyRetryLink.Visibility = _loadFailed ? Visibility.Visible : Visibility.Collapsed;
            EmptySubmitLink.Visibility = _loadFailed ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _currentCategory = CategoryFilter.SelectedIndex <= 0 ? null : CategoryFilter.SelectedItem as string;
        ApplyFilter();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _currentSearch = sender.Text;
        ApplyFilter();
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _currentSearch = args.QueryText;
        ApplyFilter();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        CommunityToolService.InvalidateCache();
        await LoadToolsAsync();
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        var loggedIn = await GitHubAuthService.EnsureAuthenticatedAsync(XamlRoot);
        if (!loggedIn) return;

        var window = new CommunitySubmitWindow();
        window.SubmitSucceeded += () => DispatcherQueue.TryEnqueue(async () =>
        {
            CommunityToolService.InvalidateCache();
            await LoadToolsAsync();
        });
        window.Activate();
    }

    private void ToolsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ToolCardLayout.Apply(ToolsGrid);
    }

    // ---------- 卡片交互（与常规工具卡一致：单击详情、双击打开/下载、右键菜单） ----------

    private async void ToolsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CommunityTool tool)
            await ShowToolDetailAsync(tool);
    }

    private void ToolsGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var tool = FindAncestorDataContext<CommunityTool>(e.OriginalSource as FrameworkElement);
        if (tool is not null)
            _ = PrimaryActionAsync(tool);
    }

    private static T? FindAncestorDataContext<T>(FrameworkElement? element) where T : class
    {
        while (element is not null)
        {
            if (element.DataContext is T t) return t;
            element = VisualTreeHelper.GetParent(element) as FrameworkElement;
        }
        return null;
    }

    private void Item_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not CommunityTool tool) return;

        var canActOnLocal = tool.CanUninstall && !tool.IsBusy;

        MenuToggleFavorite.Visibility = tool.FavoriteButtonVisibility;
        MenuToggleFavorite.Text = tool.IsFavorite ? "取消收藏" : "收藏";
        if (MenuToggleFavorite.Icon is FontIcon favIcon)
            favIcon.Glyph = tool.IsFavorite ? "\uE735" : "\uE734";

        MenuSendToDesktop.Visibility = canActOnLocal ? Visibility.Visible : Visibility.Collapsed;
        MenuRunAsAdmin.Visibility = canActOnLocal ? Visibility.Visible : Visibility.Collapsed;
        MenuOpenDirectory.Visibility = canActOnLocal ? Visibility.Visible : Visibility.Collapsed;
        MenuCopySource.Visibility = string.IsNullOrWhiteSpace(tool.File) && string.IsNullOrWhiteSpace(tool.DownloadUrl)
            ? Visibility.Collapsed
            : Visibility.Visible;
        MenuCancelInstall.Visibility = tool.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        MenuSeparator1.Visibility = tool.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        MenuUninstall.Visibility = canActOnLocal ? Visibility.Visible : Visibility.Collapsed;
        MenuRemoveRequest.Visibility = tool.RemoveRequestButtonVisibility;
        MenuSeparator2.Visibility = MenuUninstall.Visibility == Visibility.Visible || MenuRemoveRequest.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

        var flyout = (MenuFlyout)ToolsGrid.Resources["CommunityItemFlyout"];
        flyout.ShowAt(fe, e.GetPosition(fe));
    }

    // ---------- 主操作：打开 / 下载 / 更新 ----------

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CommunityTool tool })
            _ = PrimaryActionAsync(tool);
    }

    private async Task PrimaryActionAsync(CommunityTool tool)
    {
        if (tool.IsBusy) return;

        if (tool.CanLaunch)
        {
            OpenTool(tool);
            return;
        }

        await InstallToolAsync(tool);
    }

    private void OpenTool(CommunityTool tool, bool runAsAdmin = false)
    {
        try
        {
            CommunityToolInstallService.Launch(tool, runAsAdmin);
        }
        catch (Exception ex)
        {
            ShowStatus("打开失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task InstallToolAsync(CommunityTool tool)
    {
        if (tool.IsBusy) return;

        // 索引加载失败时列表只有摘要：下载前按需补全 plugin.json 详情
        var installSource = await EnsureDetailAsync(tool);

        if (string.IsNullOrWhiteSpace(installSource.DownloadUrl) && string.IsNullOrWhiteSpace(installSource.File))
        {
            ShowStatus("无法下载", "该工具没有提供下载源", InfoBarSeverity.Warning);
            return;
        }

        // 更新已安装的工具不再重复"信任"确认；首次安装需要用户确认来源可信
        if (installSource.InstallStatus != CommunityToolInstallStatus.UpdateAvailable && !await ConfirmTrustAsync(installSource))
            return;

        try
        {
            var item = CommunityToolInstallService.EnqueueInstall(installSource);
            TrackItem(item);
            ApplyBusyState(tool, item);
            ShowStatus("已加入下载队列", $"「{tool.Name}」开始下载，完成后自动安装", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            ShowStatus("无法开始下载", ex.Message, InfoBarSeverity.Error);
        }
    }

    /// <summary>索引加载失败走目录扫描时列表只有摘要（无下载源/详情）：操作前按需加载 plugin.json。</summary>
    private static async Task<CommunityTool> EnsureDetailAsync(CommunityTool tool)
    {
        if (!string.IsNullOrWhiteSpace(tool.File) || !string.IsNullOrWhiteSpace(tool.DownloadUrl))
            return tool;

        try
        {
            var detail = await CommunityToolService.LoadToolDetailAsync(tool);
            if (detail is not null)
            {
                detail.InstallStatus = CommunityToolInstallService.CheckStatus(detail);
                return detail;
            }
        }
        catch { }

        return tool;
    }

    private async Task<bool> ConfirmTrustAsync(CommunityTool tool)
    {
        var authorName = string.IsNullOrWhiteSpace(tool.Author) ? "未知用户" : tool.Author;

        var dialog = new ContentDialog
        {
            Title = $"下载「{tool.Name}」",
            PrimaryButtonText = $"我信任 {authorName}，开始下载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        dialog.Resources["ContentDialogMaxWidth"] = 480;

        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(tool.Description) ? "无描述" : tool.Description,
                        FontSize = 14,
                        Opacity = 0.7,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = $"分类：{tool.Category}  ·  版本：{ValueOrUnknown(tool.Version)}  ·  提交者：{authorName}",
                        FontSize = 12,
                        Opacity = 0.6
                    }
                }
            }
        });

        var warningIcon = new FontIcon { Glyph = "\uE7BA", FontSize = 14, Foreground = new SolidColorBrush(ThemeColors.AccentRed) };
        var warningText = new TextBlock
        {
            Text = $"社区包无法保证其安全性，图吧工具箱不对社区包负责，但会尽量避免违规工具。如果你信任 {authorName} 可以开始下载。",
            FontSize = 12,
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        var warningStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        warningStack.Children.Add(warningIcon);
        warningStack.Children.Add(warningText);

        stack.Children.Add(new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Color.FromArgb(25, ThemeColors.AccentRed.R, ThemeColors.AccentRed.G, ThemeColors.AccentRed.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, ThemeColors.AccentRed.R, ThemeColors.AccentRed.G, ThemeColors.AccentRed.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = warningStack
        });

        dialog.Content = stack;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    // ---------- 卸载 / 申请下架 ----------

    private void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CommunityTool tool })
            _ = UninstallToolAsync(tool);
    }

    private async Task UninstallToolAsync(CommunityTool tool)
    {
        if (tool.IsBusy) return;

        var dialog = new ContentDialog
        {
            Title = $"卸载「{tool.Name}」",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "将删除工具所在目录及所有相关文件，此操作不可撤销。",
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = CommunityToolInstallService.GetToolDirectory(tool),
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.52,
                        FontSize = 12,
                        IsTextSelectionEnabled = true
                    }
                }
            },
            PrimaryButtonText = "卸载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await CommunityToolInstallService.UninstallAsync(tool);
            RefreshLocalState(tool);
            ShowStatus("已卸载", $"「{tool.Name}」已从本机删除", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("卸载失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void RemoveRequestButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: CommunityTool tool })
            _ = RemoveRequestAsync(tool);
    }

    /// <summary>作者申请把工具从社区仓库下架（创建删除 PR，不影响已安装用户）。</summary>
    private async Task RemoveRequestAsync(CommunityTool tool)
    {
        var loggedIn = await GitHubAuthService.EnsureAuthenticatedAsync(XamlRoot);
        if (!loggedIn) return;

        var confirm = new ContentDialog
        {
            Title = $"申请下架「{tool.Name}」",
            Content = new TextBlock
            {
                Text = "将创建一个删除 Pull Request，审核通过后工具将从社区中移除。已安装该工具的用户不受影响。",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "提交下架申请",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        using var cts = new CancellationTokenSource();
        var progressText = new TextBlock { Text = "准备中...", FontSize = 14, TextWrapping = TextWrapping.Wrap };

        var progressDialog = new ContentDialog
        {
            Title = "正在提交下架申请",
            Content = new StackPanel
            {
                Spacing = 8,
                Children = { progressText, new ProgressBar { IsIndeterminate = true } }
            },
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        progressDialog.Resources["ContentDialogMaxWidth"] = 480;

        var closed = false;
        void CloseDialog()
        {
            if (closed) return;
            closed = true;
            progressDialog.Hide();
        }

        progressDialog.CloseButtonClick += (_, _) => cts.Cancel();

        var progress = new Progress<string>(msg => DispatcherQueue.TryEnqueue(() => progressText.Text = msg));

        string? prUrl = null;
        Exception? error = null;
        var operation = Task.Run(async () =>
        {
            try { prUrl = await CommunityToolService.DeletePluginAsync(tool, progress, cts.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { error = ex; }

            DispatcherQueue.TryEnqueue(CloseDialog);
        });

        await progressDialog.ShowAsync();
        await operation;

        if (error is not null)
        {
            ShowStatus("下架申请失败", error.Message, InfoBarSeverity.Error);
        }
        else if (prUrl is not null)
        {
            ShowStatus("下架申请已提交", "已创建删除请求，审核通过后工具将从社区移除。", InfoBarSeverity.Success);
            var link = new HyperlinkButton { Content = "查看 Pull Request" };
            try { link.NavigateUri = new Uri(prUrl); } catch { }
            StatusBar.Content = link;

            CommunityToolService.InvalidateCache();
            await LoadToolsAsync();
        }
    }

    // ---------- 收藏 / 右键菜单操作 ----------

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CommunityTool tool }) return;
        ToggleFavorite(tool);
    }

    private void MenuToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: CommunityTool tool })
            ToggleFavorite(tool);
    }

    private static void ToggleFavorite(CommunityTool tool)
    {
        var launchPath = CommunityToolInstallService.ResolveLaunchPath(tool);
        if (string.IsNullOrWhiteSpace(launchPath)) return;

        FavoritesService.ToggleFavorite(launchPath);
        tool.IsFavorite = !tool.IsFavorite;
    }

    private void MenuSendToDesktop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { DataContext: CommunityTool tool }) return;

        try
        {
            var item = CommunityToolInstallService.GetCatalogItem(tool)
                ?? throw new InvalidOperationException("工具尚未就绪，请稍后重试。");
            WindowsSearchIndexService.CreateDesktopShortcut(item);
            ShowStatus("已创建", $"已将「{tool.Name}」快捷方式发送到桌面", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("创建失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void MenuRunAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: CommunityTool tool })
            OpenTool(tool, runAsAdmin: true);
    }

    private void MenuOpenDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { DataContext: CommunityTool tool }) return;

        var item = CommunityToolInstallService.GetCatalogItem(tool);
        var dir = item?.EffectiveWorkingDir ?? CommunityToolInstallService.GetToolDirectory(tool);
        if (Directory.Exists(dir))
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    private void MenuCancelInstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: CommunityTool tool })
            CommunityToolInstallService.CancelInstall(tool);
    }

    private void MenuUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: CommunityTool tool })
            _ = UninstallToolAsync(tool);
    }

    private void MenuRemoveRequest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: CommunityTool tool })
            _ = RemoveRequestAsync(tool);
    }

    private void MenuCopySource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { DataContext: CommunityTool tool }) return;

        var sources = CommunityToolService.GetAllDownloadUrls(tool);
        var url = sources.Count > 0 ? sources[0].Url : null;
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowStatus("无法复制", "该工具没有提供下载源", InfoBarSeverity.Warning);
            return;
        }

        var result = ClipboardService.TrySetText(url);
        ShowStatus(result.Success ? "已复制" : "复制失败", url,
            result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    // ---------- 详情提示（与首页 ToolDetailTip 同结构） ----------

    private async Task ShowToolDetailAsync(CommunityTool tool)
    {
        var detail = await EnsureDetailAsync(tool);

        DetailTip.Title = detail.Name;
        DetailTip.Subtitle = $"{detail.CategoryDisplay} · {detail.StatusText}";
        DetailDescriptionText.Text = string.IsNullOrWhiteSpace(detail.Description) ? "暂无介绍。" : detail.Description;
        DetailPublisherText.Text = $"发布者：{(string.IsNullOrWhiteSpace(detail.Publisher) ? ValueOrUnknown(detail.Author) : detail.Publisher)}";
        DetailVersionText.Text = $"版本：{ValueOrUnknown(detail.Version)}";
        DetailStatusText.Text = $"提交者：{ValueOrUnknown(detail.Author)}";

        if (!string.IsNullOrWhiteSpace(detail.Homepage) &&
            Uri.TryCreate(detail.Homepage, UriKind.Absolute, out var uri))
        {
            DetailHomepageLink.NavigateUri = uri;
            DetailHomepageLink.Visibility = Visibility.Visible;
        }
        else
        {
            DetailHomepageLink.Visibility = Visibility.Collapsed;
        }

        DetailTip.IsOpen = true;
    }

    // ---------- 下载队列进度 ----------

    private void OnQueueChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_allTools.Count == 0) return;
            foreach (var tool in _allTools)
                RefreshLocalState(tool);
        });
    }

    private void TrackItem(DownloadItem? item)
    {
        if (item is null) return;
        if (!_subscribedItemIds.Add(item.Id)) return;
        item.PropertyChanged += Item_PropertyChanged;
    }

    private void UntrackItem(DownloadItem item)
    {
        if (_subscribedItemIds.Remove(item.Id))
            item.PropertyChanged -= Item_PropertyChanged;
    }

    private static string DescribeItemState(DownloadItem item) => item.State switch
    {
        DownloadItemState.Queued => "排队中",
        DownloadItemState.Resolving => "解析中...",
        DownloadItemState.Downloading => item.Progress is { } p ? $"下载中 {p.Percentage:F0}%" : "下载中...",
        DownloadItemState.Paused => "已暂停",
        DownloadItemState.Processing => string.IsNullOrWhiteSpace(item.ProcessingStatus) ? "安装中..." : item.ProcessingStatus!,
        _ => ""
    };

    private static void ApplyBusyState(CommunityTool tool, DownloadItem? item)
    {
        if (item is null)
        {
            if (tool.IsBusy) tool.SetBusy(false);
            return;
        }

        tool.SetBusy(true, DescribeItemState(item));
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DownloadItem item) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            var tool = item.Tag as CommunityTool;
            if (tool is not null && _allTools.Count > 0)
            {
                tool = _allTools.FirstOrDefault(t => string.Equals(t.Id, tool.Id, StringComparison.OrdinalIgnoreCase)) ?? tool;
            }
            if (tool is null) return;

            switch (item.State)
            {
                case DownloadItemState.Completed:
                    UntrackItem(item);
                    tool.SetBusy(false);
                    RefreshLocalState(tool);
                    ShowStatus("安装完成", $"「{tool.Name}」已安装，可在「{tool.CategoryDisplay}」分类中使用", InfoBarSeverity.Success);
                    break;

                case DownloadItemState.Failed:
                    UntrackItem(item);
                    tool.SetBusy(false);
                    ShowStatus("安装失败", string.IsNullOrWhiteSpace(item.ErrorMessage) ? "下载或安装失败，请重试" : item.ErrorMessage!, InfoBarSeverity.Error);
                    break;

                case DownloadItemState.Cancelled:
                    UntrackItem(item);
                    tool.SetBusy(false);
                    ShowStatus("已取消", $"「{tool.Name}」的下载已取消", InfoBarSeverity.Informational);
                    break;

                default:
                    tool.SetBusy(true, DescribeItemState(item));
                    break;
            }
        });
    }

    // ---------- 通用 ----------

    private static string ValueOrUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "未知" : value;

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusBar.Content = null;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;

        _statusBarTimer?.Stop();
        _statusBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusBarTimer.Tick += (s, e) =>
        {
            StatusBar.IsOpen = false;
            ((DispatcherTimer)s!).Stop();
        };
        _statusBarTimer.Start();
    }
}
