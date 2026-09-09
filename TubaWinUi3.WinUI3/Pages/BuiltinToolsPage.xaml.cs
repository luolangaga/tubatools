using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class BuiltinToolsPage : Page
{
    private CancellationTokenSource? _activeCts;
    private CancellationTokenSource? _highlightCts;
    private string? _pendingHighlightId;
    private string? _autoExecuteBuiltinId;
    private bool _builtinToolOpenModeInitializing;
    private bool _compactMode;
    private readonly Dictionary<string, GridView> _gridsByCategory = new(StringComparer.CurrentCultureIgnoreCase);

    public BuiltinToolsPage()
    {
        InitializeComponent();
        InitBuiltinToolOpenModeComboBox();
        _compactMode = CompactModeService.IsCompactModeEnabled();
        LoadTools();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        CompactModeService.CompactModeChanged += OnCompactModeChanged;

        // 离开页面期间（如设置页切换简洁模式）事件收不到，回到页面时重新同步，
        // 否则缓存页面会一直停留在构造时的旧模式，必须重启才生效
        var compactMode = CompactModeService.IsCompactModeEnabled();
        if (compactMode != _compactMode)
        {
            _compactMode = compactMode;
            RebuildPivot();
        }

        if (e.Parameter is SearchNavigationTarget target && target.HighlightBuiltinId is not null)
        {
            _pendingHighlightId = target.HighlightBuiltinId;
        }
        // --open-builtin <id> 直接传入工具 ID 字符串
        else if (e.Parameter is string id && !string.IsNullOrWhiteSpace(id))
        {
            _pendingHighlightId = id;
            _autoExecuteBuiltinId = id;
        }

        // 与设置页共用同一配置，导航回来时同步选择框状态
        SyncBuiltinToolOpenMode();

        if (_pendingHighlightId is not null)
        {
            StartHighlight(_pendingHighlightId);
            _pendingHighlightId = null;
        }

        // 收藏可能在其他页面被改动，重进本页时同步星标状态
        RefreshFavoriteStates();

        // --open-builtin 模式：高亮后自动执行工具
        if (_autoExecuteBuiltinId is not null)
        {
            var builtinId = _autoExecuteBuiltinId;
            _autoExecuteBuiltinId = null;
            _ = AutoExecuteBuiltinToolAsync(builtinId);
        }
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        CompactModeService.CompactModeChanged -= OnCompactModeChanged;
    }

    private void OnCompactModeChanged(bool enabled)
    {
        if (_compactMode == enabled) return;
        _compactMode = enabled;
        RebuildPivot();
    }

    private void RebuildPivot()
    {
        // 重建分类页以应用对应的模板与容器样式（与首页一致：普通卡片 / 简洁列表）
        var selectedIndex = BuiltinPivot.SelectedIndex;
        LoadTools();
        if (selectedIndex >= 0 && selectedIndex < BuiltinPivot.Items.Count)
            BuiltinPivot.SelectedIndex = selectedIndex;
    }

    private void InitBuiltinToolOpenModeComboBox()
    {
        _builtinToolOpenModeInitializing = true;
        BuiltinToolOpenModeComboBox.Items.Clear();
        BuiltinToolOpenModeComboBox.Items.Add("嵌入页面");
        BuiltinToolOpenModeComboBox.Items.Add("独立窗口");
        BuiltinToolOpenModeComboBox.SelectedIndex = AppSettings.GetBool("BuiltinToolsOpenInWindow", false) ? 1 : 0;
        _builtinToolOpenModeInitializing = false;
    }

    private void SyncBuiltinToolOpenMode()
    {
        var expected = AppSettings.GetBool("BuiltinToolsOpenInWindow", false) ? 1 : 0;
        if (BuiltinToolOpenModeComboBox.SelectedIndex == expected) return;
        _builtinToolOpenModeInitializing = true;
        BuiltinToolOpenModeComboBox.SelectedIndex = expected;
        _builtinToolOpenModeInitializing = false;
    }

    private void BuiltinToolOpenModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_builtinToolOpenModeInitializing) return;
        AppSettings.Set("BuiltinToolsOpenInWindow", BuiltinToolOpenModeComboBox.SelectedIndex == 1);
    }

    private void StartHighlight(string builtinId)
    {
        _highlightCts?.Cancel();
        _highlightCts = new CancellationTokenSource();
        _ = HighlightBuiltinToolAsync(builtinId, _highlightCts.Token);
    }

    private async Task HighlightBuiltinToolAsync(string builtinId, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;

        var tool = BuiltinToolRegistry.GetById(builtinId);
        if (tool is null) return;

        // 先切到所属分类的 Pivot 页，再等待布局完成
        SelectCategory(tool.Category);

        try { await Task.Delay(100, ct); } catch (OperationCanceledException) { return; }
        if (ct.IsCancellationRequested) return;

        if (!_gridsByCategory.TryGetValue(tool.Category, out var grid)) return;

        var vm = grid.Items.OfType<BuiltinToolViewModel>().FirstOrDefault(v => v.Id == builtinId);
        if (vm is null) return;

        grid.ScrollIntoView(vm);
        try { await Task.Delay(100, ct); } catch (OperationCanceledException) { return; }

        var container = grid.ContainerFromItem(vm) as GridViewItem;
        if (container is null || ct.IsCancellationRequested) return;

        container.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = true,
            VerticalAlignmentRatio = 0.5
        });

        try { await Task.Delay(500, ct); } catch (OperationCanceledException) { return; }
        if (ct.IsCancellationRequested) return;

        if (container.ContentTemplateRoot is Border border)
            SearchHighlightService.HighlightBorder(border);
    }

    private void SelectCategory(string category)
    {
        foreach (var item in BuiltinPivot.Items)
        {
            if (item is PivotItem pivotItem &&
                pivotItem.Header is string header &&
                header.Equals(category, StringComparison.CurrentCultureIgnoreCase))
            {
                BuiltinPivot.SelectedItem = pivotItem;
                return;
            }
        }
    }

    /// <summary>
    /// --open-builtin 模式：等待 UI 完全就绪后自动执行指定的内置工具。
    /// </summary>
    private async Task AutoExecuteBuiltinToolAsync(string builtinId)
    {
        // 等待页面 Loaded + 布局完成 + 高亮动画
        if (!IsLoaded)
        {
            var tcs = new TaskCompletionSource();
            Loaded += (_, _) => tcs.TrySetResult();
            try { await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5)); } catch { return; }
        }
        // 额外等待一帧让布局稳定
        try { await Task.Delay(300); } catch { return; }

        var tool = BuiltinToolRegistry.GetById(builtinId);
        if (tool is null)
        {
            System.Diagnostics.Debug.WriteLine($"[BuiltinToolsPage] 未找到内置工具: {builtinId}");
            return;
        }

        try
        {
            var vm = new BuiltinToolViewModel(tool, ToolCatalog.GetBuiltinFavoriteKey(tool));
            await ExecuteToolAsync(vm);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BuiltinToolsPage] 自动执行内置工具失败 [{builtinId}]: {ex}");
        }
    }

    private void LoadTools()
    {
        BuiltinPivot.Items.Clear();
        _gridsByCategory.Clear();

        // 收藏键：优先用 tools.json 挂载产生的卡片路径（与分类页星标互通），
        // 未挂载的内置工具用规范虚拟目录键（收藏页按同规则兜底解析）
        var favoriteKeys = BuiltinToolRegistry.Tools.ToDictionary(
            t => t.Id, ToolCatalog.GetBuiltinFavoriteKey, StringComparer.Ordinal);

        var grouped = BuiltinToolRegistry.Tools
            .GroupBy(t => t.Category)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);

        foreach (var group in grouped)
        {
            BuiltinPivot.Items.Add(CreatePivotItem(group.Key, group.ToList(), favoriteKeys));
        }

        ToolCountText.Text = $"{BuiltinToolRegistry.Tools.Count} 个内置工具";
    }

    private PivotItem CreatePivotItem(string category, List<IBuiltinTool> tools,
        IReadOnlyDictionary<string, string> favoriteKeys)
    {
        var viewModels = tools.Select(t => new BuiltinToolViewModel(t, favoriteKeys[t.Id])).ToList();

        var grid = new GridView
        {
            ItemsSource = viewModels,
            ItemContainerStyle = (Style)Resources[_compactMode ? "BuiltinCompactCardStyle" : "BuiltinToolCardStyle"],
            ItemTemplate = (DataTemplate)Resources[_compactMode ? "BuiltinCompactCardTemplate" : "BuiltinNormalCardTemplate"],
            IsItemClickEnabled = true,
            SelectionMode = ListViewSelectionMode.None,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 8, 0, 0),
        };

        grid.ItemClick += BuiltinGrid_ItemClick;
        grid.SizeChanged += BuiltinGrid_SizeChanged;

        _gridsByCategory[category] = grid;

        return new PivotItem { Header = category, Content = grid };
    }

    private void BuiltinGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is BuiltinToolViewModel vm)
            _ = ExecuteToolAsync(vm);
    }

    private void BuiltinOpenButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BuiltinToolViewModel vm })
            _ = ExecuteToolAsync(vm);
    }

    private void BuiltinFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BuiltinToolViewModel vm })
            ToggleFavorite(vm);
    }

    private void BuiltinSendDesktopButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BuiltinToolViewModel vm })
            SendBuiltinToDesktop(vm);
    }

    private void BuiltinCard_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is BuiltinToolViewModel vm)
        {
            var flyout = (MenuFlyout)Resources[_compactMode ? "BuiltinCompactFlyout" : "BuiltinNormalFlyout"];
            // 菜单项不在可视树中，无法继承 DataContext，逐项显式绑定
            foreach (var item in flyout.Items.OfType<MenuFlyoutItem>())
                item.DataContext = vm;
            UpdateFavoriteMenuItem(flyout, vm);
            flyout.ShowAt(fe, e.GetPosition(fe));
        }
    }

    private void BuiltinMenu_ToggleFavorite(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: BuiltinToolViewModel vm })
            ToggleFavorite(vm);
    }

    private void BuiltinMenu_SendToDesktop(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: BuiltinToolViewModel vm })
            SendBuiltinToDesktop(vm);
    }

    private static void ToggleFavorite(BuiltinToolViewModel vm)
    {
        FavoritesService.ToggleFavorite(vm.FavoriteKey);
        vm.IsFavorite = !vm.IsFavorite;
    }

    private static void UpdateFavoriteMenuItem(MenuFlyout flyout, BuiltinToolViewModel vm)
    {
        var item = flyout.Items.OfType<MenuFlyoutItem>()
            .FirstOrDefault(i => i.Text.Contains("收藏"));
        if (item is null) return;
        item.Text = vm.IsFavorite ? "取消收藏" : "收藏";
        if (item.Icon is FontIcon icon)
            icon.Glyph = vm.IsFavorite ? "\uE735" : "\uE734";
    }

    private void SendBuiltinToDesktop(BuiltinToolViewModel vm)
    {
        try
        {
            WindowsSearchIndexService.CreateDesktopShortcut(vm.Tool);
            ShowStatus("已创建", $"已将「{vm.Name}」快捷方式发送到桌面", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("创建失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    /// <summary>重进页面时按收藏键同步各卡片星标（收藏可能在其他页面被改动）。</summary>
    private void RefreshFavoriteStates()
    {
        foreach (var grid in _gridsByCategory.Values)
        {
            foreach (var vm in grid.Items.OfType<BuiltinToolViewModel>())
                vm.IsFavorite = FavoritesService.IsFavorite(vm.FavoriteKey);
        }
    }

    private void BuiltinGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is GridView grid)
            UpdateItemWidth(grid);
    }

    private void UpdateItemWidth(GridView grid)
    {
        var panel = grid.ItemsPanelRoot as ItemsWrapGrid;
        if (panel is null) return;

        // 与首页一致：普通模式最小宽度 280、间距 12；简洁模式最小宽度 100、间距 10
        double minItemWidth = _compactMode ? 100 : 280;
        double spacing = _compactMode ? 10 : 12;
        double availableWidth = grid.ActualWidth - grid.Padding.Left - grid.Padding.Right;
        if (availableWidth <= 0) return;

        int columns = Math.Max(1, (int)((availableWidth + spacing) / (minItemWidth + spacing)));
        double itemWidth = (availableWidth - (columns - 1) * spacing) / columns;
        panel.ItemWidth = Math.Max(minItemWidth, itemWidth);
    }

    private async Task ExecuteToolAsync(BuiltinToolViewModel vm)
    {
        _activeCts?.Cancel();
        _activeCts = new CancellationTokenSource();

        var context = new BuiltinToolContext
        {
            XamlRoot = XamlRoot,
            OnProgress = msg => DispatcherQueue.TryEnqueue(() =>
            {
                StatusBar.Title = vm.Name;
                StatusBar.Message = msg;
                StatusBar.Severity = InfoBarSeverity.Informational;
                StatusBar.IsOpen = true;
            }),
            ConfirmDownload = (toolName, description, size) => ConfirmDownloadAsync(toolName, description, size),
            CancellationToken = _activeCts.Token
        };

        try
        {
            MainWindow.ActiveToolName = vm.Name;
            await vm.Tool.ExecuteAsync(context);
            StatusBar.IsOpen = false;
        }
        catch (OperationCanceledException)
        {
            ShowStatus("已取消", vm.Name, InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            ShowStatus("执行失败", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            MainWindow.ActiveToolName = null;
        }
    }

    private async Task<bool> ConfirmDownloadAsync(string toolName, string description, string size)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = $"即将下载「{toolName}」，是否继续？",
            TextWrapping = TextWrapping.Wrap
        });

        var secondaryBrush = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        if (!string.IsNullOrWhiteSpace(description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = secondaryBrush,
                FontSize = 13
            });
        }

        if (!string.IsNullOrWhiteSpace(size))
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"文件大小：{size}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = secondaryBrush,
                FontSize = 13
            });
        }

        var dialog = new ContentDialog
        {
            Title = "下载确认",
            Content = panel,
            PrimaryButtonText = "下载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private DispatcherTimer? _statusBarTimer;

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
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

public sealed class BuiltinToolViewModel : INotifyPropertyChanged
{
    public IBuiltinTool Tool { get; }

    /// <summary>收藏持久化键（虚拟目录路径，与收藏页/分类页共用）。</summary>
    public string FavoriteKey { get; }

    public BuiltinToolViewModel(IBuiltinTool tool, string favoriteKey)
    {
        Tool = tool;
        FavoriteKey = favoriteKey;
        _isFavorite = FavoritesService.IsFavorite(favoriteKey);
    }

    public string Id => Tool.Id;
    public string Name => Tool.Name;
    public string Description => Tool.Description;
    public string Glyph => Tool.Glyph;
    public string Category => Tool.Category;
    public string KindText => Tool.Kind switch
    {
        BuiltinToolKind.Dialog => "页面",
        BuiltinToolKind.BackgroundTask => "后台任务",
        BuiltinToolKind.ProgressTask => "进度任务",
        BuiltinToolKind.InstantAction => "即时操作",
        _ => "未知"
    };

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value) return;
            _isFavorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}