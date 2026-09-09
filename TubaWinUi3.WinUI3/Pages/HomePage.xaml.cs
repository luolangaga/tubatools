using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TubaWinUi3.Pages;

public sealed partial class HomePage : Page
{
    private readonly BulkObservableCollection<ToolItem> _tools = new();
    private string? _category;
    private string? _selectedTag;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _highlightCts;
    private bool _compactMode;
    private string? _highlightToolPath;
    private string _searchQuery = string.Empty;
    private string? _lastLoadedCategory;
    private string? _lastLoadedTag;
    private string _lastLoadedQuery = string.Empty;
    private int _lastCacheVersion = -1;
    private int _tagBarCacheVersion = -1;

    // 标签栏：只保存标签名列表；按钮每次布局时按需重建（重建成本极低），
    // CommandBar 放不下的按钮会自动折叠进溢出菜单。
    private readonly List<string> _allTags = [];
    private bool _tagsPopulated;

    // 编辑排序模式（与收藏页同模式）：专用纵向列表 + 整行自实现拖拽
    private bool _isEditing;
    private const double EditRowHeight = 64;
    private const double EditRowSpacing = 8;
    private static double EditRowStride => EditRowHeight + EditRowSpacing;
    private Border? _dragRow;
    private uint _dragPointerId;
    private double _dragStartY; // 按下时指针相对 EditRowsPanel 的 Y
    private int _dragStartIndex;
    private int _dragCurrentIndex;
    private bool _dragging;
    private TranslateTransform? _dragTranslate;

    public HomePage()
    {
        InitializeComponent();
        ToolsGrid.ItemsSource = _tools;
        CompactGrid.ItemsSource = _tools;

        _compactMode = CompactModeService.IsCompactModeEnabled();
        ApplyCompactMode();

        // 页面加载后安装 Win32 拖放钩子（绕过 UIPI，钩子为全局共享单例）
        Loaded += (_, _) => InstallDropHook();
        Unloaded += (_, _) => UninstallDropHook();
    }

    private void SubscribeStaticEvents()
    {
        CompactModeService.CompactModeChanged += OnCompactModeChanged;
        ToolCatalog.ToolsChanged += OnToolsChanged;
    }

    private void UnsubscribeStaticEvents()
    {
        CompactModeService.CompactModeChanged -= OnCompactModeChanged;
        ToolCatalog.ToolsChanged -= OnToolsChanged;
    }

    private void OnCompactModeChanged(bool enabled)
    {
        _compactMode = enabled;
        ApplyCompactMode();
    }

    private void OnToolsChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ToolCatalog.InvalidateTagsCache();
            _ = LoadToolsAsync();
        });
    }

    private void ApplyCompactMode()
    {
        if (_compactMode)
        {
            ToolsGrid.Visibility = Visibility.Collapsed;
            CompactGrid.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            ToolsGrid.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            CompactGrid.Visibility = Visibility.Collapsed;
        }
        UpdateItemWidth();
    }

    private void UpdateItemWidth()
    {
        var grid = _compactMode ? CompactGrid : ToolsGrid;
        var panel = grid.ItemsPanelRoot as ItemsWrapGrid;
        if (panel is null) return;

        double minItemWidth = _compactMode ? 100 : 280;
        double spacing = _compactMode ? 10 : 12;
        double availableWidth = grid.ActualWidth - grid.Padding.Left - grid.Padding.Right;

        if (availableWidth <= 0) return;

        int columns = Math.Max(1, (int)((availableWidth + spacing) / (minItemWidth + spacing)));
        double itemWidth = (availableWidth - (columns - 1) * spacing) / columns;
        panel.ItemWidth = Math.Max(minItemWidth, itemWidth);
    }

    private void ToolsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_compactMode) UpdateItemWidth();
    }

    private void CompactGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_compactMode) UpdateItemWidth();
    }

    private void UpdateGridVisibility(bool hasTools)
    {
        if (_compactMode)
        {
            ToolsGrid.Visibility = Visibility.Collapsed;
            var wasVisible = CompactGrid.Visibility == Visibility.Visible;
            CompactGrid.Visibility = hasTools ? Visibility.Visible : Visibility.Collapsed;
            if (hasTools && !wasVisible)
                PlayGridEntrance(CompactGrid);
        }
        else
        {
            var wasVisible = ToolsGrid.Visibility == Visibility.Visible;
            ToolsGrid.Visibility = hasTools ? Visibility.Visible : Visibility.Collapsed;
            CompactGrid.Visibility = Visibility.Collapsed;
            if (hasTools && !wasVisible)
                PlayGridEntrance(ToolsGrid);
        }
    }

    private void PlayGridEntrance(GridView grid)
    {
        var storyboard = new Storyboard();
        var duration = TimeSpan.FromMilliseconds(320);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation { From = 0.0, To = 1.0, Duration = duration, EasingFunction = easing };
        Storyboard.SetTarget(fade, grid);
        Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(fade);

        var slide = new DoubleAnimation { From = 24.0, To = 0.0, Duration = duration, EasingFunction = easing };
        Storyboard.SetTarget(slide, grid);
        Storyboard.SetTargetProperty(slide, "(UIElement.RenderTransform).(TranslateTransform.Y)");
        storyboard.Children.Add(slide);

        storyboard.Begin();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SubscribeStaticEvents();

        // 离开页面期间（如在设置页切换简洁模式）事件收不到，回到页面时重新同步，
        // 否则缓存页面会停留在构造时的旧模式，必须重启才生效
        var compactMode = CompactModeService.IsCompactModeEnabled();
        if (compactMode != _compactMode)
        {
            _compactMode = compactMode;
            ApplyCompactMode();
        }

        if (e.Parameter is SearchNavigationTarget target && target.HighlightToolPath is not null)
        {
            _highlightToolPath = target.HighlightToolPath;
            try
            {
                var tool = ToolCatalog.GetAllToolsCached()
                    .FirstOrDefault(t => t.Path.Equals(_highlightToolPath, StringComparison.OrdinalIgnoreCase));
                _category = tool?.Category;
            }
            catch { }
        }
        else if (e.Parameter is string category)
        {
            _highlightToolPath = null;
            _category = category;
        }
        else
        {
            _highlightToolPath = null;
            _category = null;
        }

        _searchQuery = string.Empty;
        _selectedTag = null;
        UpdateTitle();

        var needsReload = _category != _lastLoadedCategory ||
                          _selectedTag != _lastLoadedTag ||
                          _searchQuery != _lastLoadedQuery ||
                          ToolCatalog.CacheVersion != _lastCacheVersion;

        if (needsReload)
        {
            _ = LoadToolsAsync();
        }
        else if (_highlightToolPath is not null)
        {
            StartHighlight(_highlightToolPath);
            _highlightToolPath = null;
        }

        if (_category is null)
        {
            if (ToolCatalog.CacheVersion != _tagBarCacheVersion || !_tagsPopulated)
                _ = PopulateTagBarAsync();
            else
                ApplyTagBarLayout();
        }
        else
        {
            TagBarArea.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ExitEditMode(); // 离开页面时退出编辑排序并兜底落盘
        UnsubscribeStaticEvents();
    }

    private async Task PopulateTagBarAsync()
    {
        IReadOnlyList<string> tags;
        try
        {
            tags = await Task.Run(() => ToolCatalog.GetAllTags());
        }
        catch
        {
            TagBarArea.Visibility = Visibility.Collapsed;
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            _allTags.Clear();
            _allTags.AddRange(tags);
            _tagsPopulated = true;
            _tagBarCacheVersion = ToolCatalog.CacheVersion;
            ApplyTagBarLayout();
        });
    }

    /// <summary>
    /// 依据当前标签列表重建 CommandBar 按钮并同步可见性。
    /// 按钮每次布局时全新创建；仅“全部”页（无分类）且存在标签时显示。
    /// 放不下的按钮由 CommandBar 自动折叠进溢出菜单。
    /// </summary>
    private void ApplyTagBarLayout()
    {
        bool hasTags = _allTags.Count > 0;
        if (_category is not null || !hasTags)
        {
            TagBarArea.Visibility = Visibility.Collapsed;
            return;
        }

        // 重建（选中态按 _selectedTag 重新应用）
        TagBarCommandBar.PrimaryCommands.Clear();
        TagBarCommandBar.PrimaryCommands.Add(CreateTagToggleButton("全部", null as string, _selectedTag is null));
        foreach (var tag in _allTags)
            TagBarCommandBar.PrimaryCommands.Add(CreateTagToggleButton(tag, tag, tag == _selectedTag));

        TagBarArea.Visibility = Visibility.Visible;
    }

    private AppBarToggleButton CreateTagToggleButton(string content, string? tag, bool isChecked)
    {
        var button = new AppBarToggleButton
        {
            Label = content,
            Tag = tag,
            IsChecked = isChecked
        };
        button.Click += TagToggleButton_Click;
        return button;
    }

    private void TagToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not AppBarToggleButton button)
            return;

        // 点击已选中的标签保持选中（保持单选语义）
        if (button.Tag as string == _selectedTag)
        {
            button.IsChecked = true;
            return;
        }

        _selectedTag = button.Tag as string;

        // ToggleButton 无自动互斥，手动取消其他按钮
        foreach (var command in TagBarCommandBar.PrimaryCommands)
        {
            if (command is AppBarToggleButton other && other != button)
                other.IsChecked = false;
        }

        UpdateTitle();
        _ = LoadToolsAsync();
    }

    private async void DownloadToolsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ToolsBundleDownloadDialog
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            await dialog.ShowDownloadAsync();

            if (dialog.DownloadSucceeded)
            {
                ToolCatalog.RefreshToolsRoot();
                ToolCatalog.InvalidateTagsCache();
                _ = LoadToolsAsync();
            }
        }
        catch { }
    }

    private void UpdateTitle()
    {
        var query = _searchQuery;
        var title = _category?.Replace("工具", "") ?? "全部";
        if (query.Length > 0)
            title = $"搜索：{query}";
        else if (_selectedTag is not null)
            title = $"标签：{_selectedTag}";
        CategoryTitle.Text = title;
        CategorySubtitle.Text = query.Length > 0
            ? "显示所有分类中匹配的工具。"
            : _selectedTag is not null
                ? $"显示带有「{_selectedTag}」标签的工具。"
                : _category is null
                    ? "从左侧选择分类，点击卡片看详情，点击打开运行工具。"
                    : $"正在浏览\u201C{_category.Replace("工具", "")}\u201D分类。";
    }

    private async Task LoadToolsAsync()
    {
        if (_isEditing)
            ExitEditMode(); // 数据源变化（工具增删/切换分类）前先退出排序编辑，避免与旧列表错位

        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        _tools.Clear();

        var query = _searchQuery;

        try
        {
            IReadOnlyList<ToolItem> tools = await Task.Run(async () =>
            {
                // single-flight 扫描：与 MainWindow 预热共享同一次并行扫描，
                // 并发调用不会重复扫全量
                await ToolCatalog.GetAllToolsAsync();

                if (query.Length > 0 || _selectedTag is not null)
                    return ToolCatalog.Search(query, _selectedTag);
                if (_category is not null)
                    return ToolCatalog.GetTools(_category);
                return ToolCatalog.GetAllToolsCached();
            }, cts.Token);

            cts.Token.ThrowIfCancellationRequested();

            _tools.AddRange(tools);

            _lastLoadedCategory = _category;
            _lastLoadedTag = _selectedTag;
            _lastLoadedQuery = query;
            _lastCacheVersion = ToolCatalog.CacheVersion;

            UpdateDragReorderState();

            ToolCountText.Text = _tools.Count > 0 ? $"{_tools.Count} 个工具" : "";
            ToolCountText.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateGridVisibility(_tools.Count > 0);
            EmptyStateText.Text = query.Length > 0
                ? $"未找到与\u201C{query}\u201D相关的工具。"
                : _selectedTag is not null
                    ? $"未找到带有「{_selectedTag}」标签的工具。"
                    : _category is not null
                        ? "此分类下没有可用工具。"
                        : "没有找到任何工具，请检查 Tools 目录。";

            DownloadToolsButton.Visibility = _tools.Count == 0
                && string.IsNullOrEmpty(query)
                && _selectedTag is null
                && _category is null
                && RuntimeHelper.IsMsixPackaged
                && !ToolsBundleService.IsToolsBundleReady()
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            StartIconLoading(tools);

            if (_highlightToolPath is not null)
            {
                StartHighlight(_highlightToolPath);
                _highlightToolPath = null;
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 仅纯分类视图允许拖拽排序 / 编辑排序：搜索 / 标签 / 全部工具视图的顺序是派生结果，不落盘。
    /// </summary>
    private void UpdateDragReorderState()
    {
        var canReorder = _searchQuery.Length == 0 && _selectedTag is null && _category is not null;
        foreach (var grid in new[] { ToolsGrid, CompactGrid })
        {
            grid.CanDragItems = canReorder;
            grid.CanReorderItems = canReorder;
            grid.AllowDrop = canReorder;
        }
        // 右上角排序按钮只在可排序的纯分类视图且非编辑态下显示
        EditOrderButton.Visibility = canReorder && _tools.Count > 0 && !_isEditing
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// 卡片拖拽排序完成：把当前视觉顺序写回 tools.json 的 order 字段（收录工具主序），
    /// 同时用 AppSettings ToolOrder_{category} 记录完整名字序（自定义工具的回退序）。
    /// </summary>
    private void Grid_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult == DataPackageOperation.None) return; // 拖拽被取消
        if (_searchQuery.Length > 0 || _selectedTag is not null || string.IsNullOrEmpty(_category)) return;
        PersistToolOrder(_category);
    }

    /// <summary>把 _tools 当前视觉顺序落盘（卡片拖拽与编辑排序共用）。</summary>
    private void PersistToolOrder(string category)
    {
        // 工具目录序：Path 是可执行文件（或占位路径）取其父目录；
        // 内置挂载的 Path 本身就是虚拟目录（tools.json builtin 条目），直接参与排序
        var orderedDirs = _tools
            .Select(t => t.IsBuiltinLink
                ? t.Path
                : (System.IO.Directory.Exists(t.Path) ? t.Path : System.IO.Path.GetDirectoryName(t.Path)))
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d!)
            .ToList();

        // 自定义工具（未收录 tools.json）的相对序：按名字列表回退
        var nameOrder = _tools.Select(t => t.Name).ToList();
        AppSettings.Set($"ToolOrder_{category}", System.Text.Json.JsonSerializer.Serialize(nameOrder));

        _ = Task.Run(() =>
        {
            ToolMetadataService.SaveToolOrder(orderedDirs);
            // 仅清内存扫描缓存 + 版本递增，不触发 ToolsChanged 事件（避免本页重载闪烁）；
            // 下次进入页面时按 CacheVersion 变化按需重扫，自然读到新 order
            ToolCatalog.InvalidateTagsCache();
        });
    }

    /// <summary>编辑排序模式开关（右上角按钮，模式与收藏页一致）。</summary>
    private void EditOrderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isEditing)
            ExitEditMode();
        else
            EnterEditMode();
    }

    private void EnterEditMode()
    {
        if (_tools.Count == 0) return;
        _isEditing = true;
        EditOrderIcon.Glyph = "\uE73E"; // CheckMark:完成
        EditOrderButtonText.Text = "完成";
        EditOrderButton.Visibility = Visibility.Visible;

        // 隐藏网格与标签栏，切到专用排序列表
        ToolsGrid.Visibility = Visibility.Collapsed;
        CompactGrid.Visibility = Visibility.Collapsed;
        TagBarArea.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;

        EditRowsPanel.Children.Clear();
        foreach (var tool in _tools)
            EditRowsPanel.Children.Add(CreateEditRow(tool));
        EditModeScroll.Visibility = Visibility.Visible;
    }

    private void ExitEditMode()
    {
        if (!_isEditing) return;
        _isEditing = false;
        FinishDrag(); // 拖动到一半退出时归位
        EditOrderIcon.Glyph = "\uE70F"; // Edit:编辑排序
        EditOrderButtonText.Text = "编辑排序";
        EditModeScroll.Visibility = Visibility.Collapsed;

        // 纯分类视图下兜底落盘（拖拽过程已实时保存）
        if (_searchQuery.Length == 0 && _selectedTag is null && _category is not null)
            PersistToolOrder(_category);

        UpdateGridVisibility(_tools.Count > 0);
        EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_category is null)
            ApplyTagBarLayout();
        UpdateDragReorderState();
    }

    private Border CreateEditRow(ToolItem tool)
    {
        var row = new Border
        {
            Height = EditRowHeight,
            Padding = new Thickness(12, 0, 12, 0),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Tag = tool
        };
        row.PointerPressed += EditRow_PointerPressed;
        row.PointerMoved += EditRow_PointerMoved;
        row.PointerReleased += EditRow_PointerReleased;
        row.PointerCaptureLost += EditRow_PointerCaptureLost;

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 拖动手柄
        grid.Children.Add(new FontIcon
        {
            Glyph = "\uE700",
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8
        });

        // 图标(与卡片相同的双元素绑定)
        var iconGrid = new Grid
        {
            Width = 36,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var image = new Image
        {
            Width = 28,
            Height = 28,
            Stretch = Stretch.Uniform
        };
        image.SetBinding(Image.SourceProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Path = new PropertyPath(nameof(ToolItem.IconPath)),
            Source = tool,
            Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay
        });
        image.SetBinding(Image.VisibilityProperty, new Microsoft.UI.Xaml.Data.Binding
        {
            Path = new PropertyPath(nameof(ToolItem.IconPath)),
            Source = tool,
            Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay,
            Converter = (Microsoft.UI.Xaml.Data.IValueConverter)Resources["NullToCollapse"]
        });
        var fontIcon = new FontIcon
        {
            FontSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Glyph = tool.IconGlyph ?? "",
            Visibility = tool.IconGlyph is not null ? Visibility.Visible : Visibility.Collapsed
        };
        iconGrid.Children.Add(image);
        iconGrid.Children.Add(fontIcon);
        Grid.SetColumn(iconGrid, 1);
        grid.Children.Add(iconGrid);

        // 名称 + 描述/分类
        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        textStack.Children.Add(new TextBlock
        {
            Text = tool.Name,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        textStack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(tool.Description) ? tool.Category : tool.Description,
            FontSize = 12,
            Opacity = 0.7,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(textStack, 2);
        grid.Children.Add(textStack);

        row.Child = grid;
        return row;
    }

    private void EditRow_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging || sender is not Border row) return;
        _dragRow = row;
        _dragPointerId = e.Pointer.PointerId;
        _dragStartY = e.GetCurrentPoint(EditRowsPanel).Position.Y;
        _dragStartIndex = EditRowsPanel.Children.IndexOf(row);
        _dragCurrentIndex = _dragStartIndex;
        _dragging = false;
        row.CapturePointer(e.Pointer);
    }

    private void EditRow_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragRow is null || e.Pointer.PointerId != _dragPointerId) return;

        // 移动超过阈值才开始拖动,避免把普通点击当成拖动
        var deltaY = e.GetCurrentPoint(EditRowsPanel).Position.Y - _dragStartY;
        if (!_dragging)
        {
            if (Math.Abs(deltaY) < 6) return;
            _dragging = true;
            _dragTranslate = new TranslateTransform();
            _dragRow.RenderTransform = _dragTranslate;
            _dragRow.Opacity = 0.75;
        }

        // 限制拖动范围,行不会飞出列表
        var minDelta = -_dragStartIndex * EditRowStride;
        var maxDelta = (EditRowsPanel.Children.Count - 1 - _dragStartIndex) * EditRowStride;
        var clampedDelta = Math.Clamp(deltaY, minDelta, maxDelta);

        // 目标索引变化时实时重排行与数据源
        var target = _dragStartIndex + (int)Math.Round(clampedDelta / EditRowStride);
        if (target != _dragCurrentIndex)
        {
            EditRowsPanel.Children.Move((uint)_dragCurrentIndex, (uint)target);
            _tools.Move(_dragCurrentIndex, target);
            _dragCurrentIndex = target;
        }

        // 被拖行始终跟随指针:抵消重排带来的布局位移
        _dragTranslate!.Y = clampedDelta - (_dragCurrentIndex - _dragStartIndex) * EditRowStride;
    }

    private void EditRow_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId == _dragPointerId)
            FinishDrag();
    }

    private void EditRow_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId == _dragPointerId)
            FinishDrag();
    }

    private void FinishDrag()
    {
        if (_dragRow is null) return;
        if (_dragging)
        {
            _dragRow.RenderTransform = null;
            _dragRow.Opacity = 1.0;
            // 顺序已实时同步到 _tools,立即落盘
            if (_category is not null && _searchQuery.Length == 0 && _selectedTag is null)
                PersistToolOrder(_category);
        }
        _dragRow = null;
        _dragTranslate = null;
        _dragging = false;
    }

    private void StartHighlight(string toolPath)
    {
        _highlightCts?.Cancel();
        _highlightCts = new CancellationTokenSource();
        _ = HighlightToolAsync(toolPath, _highlightCts.Token);
    }

    private async Task HighlightToolAsync(string toolPath, CancellationToken ct)
    {
        var grid = _compactMode ? CompactGrid : ToolsGrid;
        var index = -1;
        for (var i = 0; i < _tools.Count; i++)
        {
            if (_tools[i].Path.Equals(toolPath, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index < 0 || ct.IsCancellationRequested) return;

        grid.ScrollIntoView(_tools[index]);
        try { await Task.Delay(100, ct); } catch (OperationCanceledException) { return; }

        var container = grid.ContainerFromItem(_tools[index]) as GridViewItem;
        if (container is null || ct.IsCancellationRequested) return;

        container.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = true,
            VerticalAlignmentRatio = 0.5
        });

        try { await Task.Delay(500, ct); } catch (OperationCanceledException) { return; }

        if (ct.IsCancellationRequested) return;

        var border = FindChildBorder(container);
        if (border is not null)
            SearchHighlightService.HighlightBorder(border);
    }

    private static Border? FindChildBorder(DependencyObject parent)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Border border) return border;
            var result = FindChildBorder(child);
            if (result is not null) return result;
        }
        return null;
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
    }

    private void CompactGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ToolItem tool)
            LaunchTool(tool, runAsAdmin: false);
    }

    private void CompactGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var tool = FindAncestorDataContext<ToolItem>(e.OriginalSource as FrameworkElement);
        if (tool is not null)
            LaunchTool(tool, runAsAdmin: false);
    }

    private void CompactItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ToolItem tool)
        {
            var flyout = (MenuFlyout)CompactGrid.Resources["CompactItemFlyout"];
            PopulateArchSubmenu(flyout, tool);
            UpdateBuiltinLinkFlyoutItems(flyout, tool, "CompactMenu");
            UpdateFavoriteMenuItem(flyout, tool, "CompactMenuToggleFavorite");
            flyout.ShowAt(fe, e.GetPosition(fe));
        }
    }

    private void CompactMenu_SendToDesktop(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
        {
            try
            {
                CreateDesktopShortcut(tool);
                ShowStatus("已创建", $"已将「{tool.Name}」快捷方式发送到桌面", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("创建失败", ex.Message, InfoBarSeverity.Error);
            }
        }
    }

    private void CompactMenu_RunAsAdmin(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
            LaunchTool(tool, runAsAdmin: true);
    }

    private void CompactMenu_OpenDirectory(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
            OpenToolDirectory(tool);
    }

    private void CompactMenu_OpenTutorial(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool } && tool.HasTutorial)
            BrowserPage.Open(tool.TutorialUrl!, $"{tool.Name} - 使用教程");
    }

    private void NormalItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ToolItem tool)
        {
            var flyout = (MenuFlyout)ToolsGrid.Resources["NormalItemFlyout"];
            PopulateArchSubmenu(flyout, tool);
            UpdateBuiltinLinkFlyoutItems(flyout, tool, "NormalMenu");
            UpdateFavoriteMenuItem(flyout, tool, "NormalMenuToggleFavorite");
            flyout.ShowAt(fe, e.GetPosition(fe));
        }
    }

    private void NormalMenu_SendToDesktop(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
        {
            try
            {
                CreateDesktopShortcut(tool);
                ShowStatus("已创建", $"已将「{tool.Name}」快捷方式发送到桌面", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("创建失败", ex.Message, InfoBarSeverity.Error);
            }
        }
    }

    private void NormalMenu_RunAsAdmin(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
            LaunchTool(tool, runAsAdmin: true);
    }

    private void NormalMenu_OpenDirectory(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
            OpenToolDirectory(tool);
    }

    private void NormalMenu_OpenTutorial(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool } && tool.HasTutorial)
            BrowserPage.Open(tool.TutorialUrl!, $"{tool.Name} - 使用教程");
    }

    private void NormalMenu_DeleteTool(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
            _ = DeleteToolAsync(tool);
    }

    private void CompactMenu_DeleteTool(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
            _ = DeleteToolAsync(tool);
    }

    private void PopulateArchSubmenu(MenuFlyout flyout, ToolItem tool)
    {
        var isCompact = ReferenceEquals(flyout, CompactGrid.Resources["CompactItemFlyout"]);
        var submenuName = isCompact ? "CompactArchSubmenu" : "NormalArchSubmenu";

        var submenu = flyout.Items.OfType<MenuFlyoutSubItem>().FirstOrDefault(i => i.Name == submenuName);
        if (submenu is null) return;

        submenu.Items.Clear();

        if (tool.ArchOptions.Count <= 1)
        {
            submenu.Visibility = Visibility.Collapsed;
            return;
        }

        submenu.Visibility = Visibility.Visible;
        foreach (var opt in tool.ArchOptions)
        {
            var label = string.IsNullOrEmpty(opt.Arch) ? "默认" : opt.Arch;
            var item = new ToggleMenuFlyoutItem
            {
                Text = label,
                IsChecked = opt == tool.SelectedArch,
                DataContext = opt
            };
            item.Click += (s, e) =>
            {
                if (s is ToggleMenuFlyoutItem { DataContext: ArchOption selected })
                    tool.SelectedArch = selected;
            };
            submenu.Items.Add(item);
        }
    }

    private static void UpdateBuiltinLinkFlyoutItems(MenuFlyout flyout, ToolItem tool, string prefix)
    {
        var isBuiltin = tool.IsBuiltinLink;
        var sendToDesktop = flyout.Items.OfType<MenuFlyoutItem>()
            .FirstOrDefault(i => i.Text.Contains("桌面快捷方式"));
        // 内置工具只要有注册 Id 也能发桌面快捷方式（--open-builtin 启动），
        // 仅缺注册信息的旧链接才隐藏
        if (sendToDesktop is not null)
            sendToDesktop.Visibility = isBuiltin && string.IsNullOrWhiteSpace(tool.BuiltinToolId)
                ? Visibility.Collapsed
                : Visibility.Visible;

        var runAsAdmin = flyout.Items.OfType<MenuFlyoutItem>()
            .FirstOrDefault(i => i.Text.Contains("管理员"));
        if (runAsAdmin is not null)
            runAsAdmin.Visibility = isBuiltin ? Visibility.Collapsed : Visibility.Visible;

        var openDir = flyout.Items.OfType<MenuFlyoutItem>()
            .FirstOrDefault(i => i.Text.Contains("所在目录"));
        if (openDir is not null)
            openDir.Visibility = isBuiltin ? Visibility.Collapsed : Visibility.Visible;

        var tutorialItem = flyout.Items.OfType<MenuFlyoutItem>()
            .FirstOrDefault(i => i.Text.Contains("教程"));
        if (tutorialItem is not null)
            tutorialItem.Visibility = tool.HasTutorial ? Visibility.Visible : Visibility.Collapsed;

        var deleteItem = flyout.Items.OfType<MenuFlyoutItem>()
            .FirstOrDefault(i => i.Text.Contains("删除工具"));
        if (deleteItem is not null)
            deleteItem.Visibility = isBuiltin ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task DeleteToolAsync(ToolItem tool)
    {
        var dialog = new ContentDialog
        {
            Title = $"删除「{tool.Name}」",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "确定要删除此工具吗？此操作不可撤销！",
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold
                    },
                    new TextBlock
                    {
                        Text = "将会删除工具所在目录及所有相关文件：",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.72
                    },
                    new TextBlock
                    {
                        Text = tool.Path,
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.52,
                        FontSize = 12,
                        IsTextSelectionEnabled = true
                    }
                }
            },
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        try
        {
            var toolDir = System.IO.Path.GetDirectoryName(tool.Path);
            if (!string.IsNullOrWhiteSpace(toolDir) && System.IO.Directory.Exists(toolDir))
            {
                var categoryDir = System.IO.Path.GetDirectoryName(toolDir);
                await Task.Run(() => System.IO.Directory.Delete(toolDir, true));

                if (!string.IsNullOrWhiteSpace(categoryDir) &&
                    System.IO.Directory.Exists(categoryDir) &&
                    !System.IO.Directory.EnumerateFileSystemEntries(categoryDir).Any())
                {
                    await Task.Run(() => System.IO.Directory.Delete(categoryDir, false));
                }
            }
            else if (System.IO.File.Exists(tool.Path))
            {
                await Task.Run(() => System.IO.File.Delete(tool.Path));
            }

            FavoritesService.RemoveFavorite(tool.Path);
            await ToolMetadataService.RemoveMetadataAsync(tool.Path);
            ToolCatalog.InvalidateTagsCache();

            if (App.MainWindow is MainWindow mainWindow)
                mainWindow.RefreshToolCategories();

            _tools.Remove(tool);
            ToolCountText.Text = _tools.Count > 0 ? $"{_tools.Count} 个工具" : "";
            ToolCountText.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateGridVisibility(_tools.Count > 0);

            ShowStatus("已删除", $"「{tool.Name}」已删除", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("删除失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private static void OpenToolDirectory(ToolItem tool)
    {
        var dir = tool.EffectiveWorkingDir;
        if (System.IO.Directory.Exists(dir))
            _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    private void ToolsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ToolItem tool)
        {
            ShowToolDetail(tool);
        }
    }

    private void ToolsGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var tool = FindAncestorDataContext<ToolItem>(e.OriginalSource as FrameworkElement);
        if (tool is not null)
            LaunchTool(tool, runAsAdmin: false);
    }

    private static T? FindAncestorDataContext<T>(FrameworkElement? element) where T : class
    {
        while (element is not null)
        {
            if (element.DataContext is T t) return t;
            element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element) as FrameworkElement;
        }
        return null;
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ToolItem btnTool })
        {
            LaunchTool(btnTool, runAsAdmin: false);
        }
    }

    private void RunAsAdminButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ToolItem tool })
        {
            LaunchTool(tool, runAsAdmin: true);
        }
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ToolItem tool)
        {
            FavoritesService.ToggleFavorite(tool.Path);
            tool.IsFavorite = !tool.IsFavorite;
            AnimateFavoriteButton(fe);
        }
    }

    private static void UpdateFavoriteMenuItem(MenuFlyout flyout, ToolItem tool, string menuItemName)
    {
        var item = flyout.Items.OfType<MenuFlyoutItem>().FirstOrDefault(i => i.Name == menuItemName);
        if (item is null) return;
        item.Text = tool.IsFavorite ? "取消收藏" : "收藏";
        if (item.Icon is FontIcon icon)
            icon.Glyph = tool.IsFavorite ? "\uE735" : "\uE734";
    }

    private void NormalMenu_ToggleFavorite(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
        {
            FavoritesService.ToggleFavorite(tool.Path);
            tool.IsFavorite = !tool.IsFavorite;
        }
    }

    private void CompactMenu_ToggleFavorite(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
        {
            FavoritesService.ToggleFavorite(tool.Path);
            tool.IsFavorite = !tool.IsFavorite;
        }
    }

    private static void AnimateFavoriteButton(FrameworkElement target)
    {
        if (FastModeService.IsFastModeEnabled()) return;
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(target);
        if (visual is null) return;
        var compositor = visual.Compositor;

        var scaleUp = compositor.CreateVector3KeyFrameAnimation();
        scaleUp.InsertKeyFrame(0f, new System.Numerics.Vector3(1f, 1f, 1f));
        scaleUp.InsertKeyFrame(0.4f, new System.Numerics.Vector3(1.35f, 1.35f, 1f));
        scaleUp.InsertKeyFrame(1f, new System.Numerics.Vector3(1f, 1f, 1f));
        scaleUp.Duration = TimeSpan.FromMilliseconds(350);

        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
        opacityAnim.InsertKeyFrame(0f, 1f);
        opacityAnim.InsertKeyFrame(0.3f, 0.4f);
        opacityAnim.InsertKeyFrame(1f, 1f);
        opacityAnim.Duration = TimeSpan.FromMilliseconds(350);

        visual.CenterPoint = new System.Numerics.Vector3((float)target.ActualSize.X / 2, (float)target.ActualSize.Y / 2, 0f);
        visual.StartAnimation("Scale", scaleUp);
        visual.StartAnimation("Opacity", opacityAnim);
    }

    private void SendToDesktopButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ToolItem tool })
        {
            try
            {
                CreateDesktopShortcut(tool);
                ShowStatus("已创建", $"已将「{tool.Name}」快捷方式发送到桌面", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("创建失败", ex.Message, InfoBarSeverity.Error);
            }
        }
    }

    private static void CreateDesktopShortcut(ToolItem tool)
    {
        if (tool.IsBuiltinLink)
        {
            if (string.IsNullOrWhiteSpace(tool.BuiltinToolId))
                throw new InvalidOperationException("内置工具缺少注册信息，无法创建快捷方式。");
            var builtin = BuiltinToolRegistry.GetById(tool.BuiltinToolId);
            if (builtin is null)
                throw new InvalidOperationException("找不到对应的内置工具，无法创建快捷方式。");
            WindowsSearchIndexService.CreateDesktopShortcut(builtin);
            return;
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var archSuffix = tool.SelectedArch is not null && !string.IsNullOrEmpty(tool.SelectedArch.Arch)
            ? $" ({tool.SelectedArch.Arch})" : "";
        var shortcutPath = Path.Combine(desktop, $"{tool.Name}{archSuffix}.lnk");

        var psScript = $"""
            $ws = New-Object -ComObject WScript.Shell
            $s = $ws.CreateShortcut('{shortcutPath}')
            $s.TargetPath = '{tool.EffectivePath}'
            $s.WorkingDirectory = '{tool.EffectiveWorkingDir}'
            $s.Description = '{tool.Name}{archSuffix}'
            $s.Save()
            """;

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{psScript.Replace("\"", "\\\"")}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        process?.WaitForExit(5000);

        if (process is not null && process.ExitCode != 0)
        {
            var err = process.StandardError.ReadToEnd();
            throw new InvalidOperationException(err);
        }
    }

    private void StartIconLoading(IReadOnlyList<ToolItem> tools)
    {
        if (tools.Count == 0) return;
        _loadCts?.Cancel();
        var ct = _loadCts?.Token ?? CancellationToken.None;
        _ = ToolIconService.LoadIconsAsync(tools, DispatcherQueue);
        _ = CheckWingetInstallStatusAsync(tools, ct);
    }

    private async Task CheckWingetInstallStatusAsync(IReadOnlyList<ToolItem> tools, CancellationToken ct)
    {
        var wingetTools = tools.Where(t => !string.IsNullOrWhiteSpace(t.WingetId) && !t.IsWingetInstalled).ToList();
        if (wingetTools.Count == 0) return;

        // winget 进程逐个启动很重（首次运行还可能下载源），延后 3s 开始并限制最多 2 个并发
        try { await Task.Delay(3000, ct); } catch (OperationCanceledException) { return; }

        using var gate = new SemaphoreSlim(2);
        foreach (var tool in wingetTools)
        {
            ct.ThrowIfCancellationRequested();
            await gate.WaitAsync(ct);
            try
            {
                var installed = await WingetService.IsInstalledAsync(tool.WingetId!, ct);
                if (installed)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        tool.IsWingetInstalled = true;
                        tool.IsWingetInstalling = false;
                    });
                }
            }
            catch (OperationCanceledException) { break; }
            catch { }
            finally
            {
                gate.Release();
            }
        }
    }

    private void ShowToolDetail(ToolItem tool)
    {
        if (tool.IsBuiltinLink)
        {
            ToolDetailTip.Title = tool.Name;
            ToolDetailTip.Subtitle = tool.Category;
            DetailDescriptionText.Text = string.IsNullOrWhiteSpace(tool.Description)
                ? "暂无介绍。"
                : tool.Description;
            DetailPublisherText.Text = $"类型：{tool.BuiltinKindText ?? "内置"}";
            DetailVersionText.Text = "";
            DetailPathText.Text = "";
            ToolDetailTip.IsOpen = true;
            return;
        }

        ToolDetailTip.Title = tool.Name;
        ToolDetailTip.Subtitle = tool.Category;
        DetailDescriptionText.Text = string.IsNullOrWhiteSpace(tool.Description)
            ? "暂无介绍。"
            : tool.Description;
        DetailPublisherText.Text = $"发布者：{ValueOrUnknown(tool.Publisher)}";
        DetailVersionText.Text = $"版本：{ValueOrUnknown(tool.Version)}";
        DetailPathText.Text = tool.Path;
        ToolDetailTip.IsOpen = true;
    }

    private void LaunchTool(ToolItem tool, bool runAsAdmin)
    {
        if (tool.IsBuiltinLink)
        {
            _ = LaunchBuiltinToolAsync(tool);
            return;
        }

        if (!string.IsNullOrWhiteSpace(tool.RemoteUrl))
        {
            Pages.BrowserPage.Open(tool.RemoteUrl, tool.Name);
            LaunchHistoryService.RecordLaunch(tool.Path);
            ShowStatus("已打开", tool.Name, InfoBarSeverity.Success);
            return;
        }

        if (!string.IsNullOrWhiteSpace(tool.DownloadUrl) && !File.Exists(tool.EffectivePath))
        {
            _ = ShowDownloadDialogAsync(tool);
            return;
        }

        if (!string.IsNullOrWhiteSpace(tool.WingetId) && !File.Exists(tool.EffectivePath))
        {
            _ = HandleWingetToolAsync(tool);
            return;
        }

        var exePath = tool.EffectivePath;
        if (!File.Exists(exePath))
        {
            ShowStatus("启动失败", $"找不到文件：{exePath}", InfoBarSeverity.Error);
            return;
        }

        var ext = Path.GetExtension(exePath);
        if (ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var command = ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
                ? $"powershell -ExecutionPolicy Bypass -Command \"& '{exePath}'\""
                : $"cmd.exe /c \"{exePath}\"";
            ScriptRunnerWindow.ShowAndRun(command, tool.EffectiveWorkingDir, $"安装 {tool.Name}");
            LaunchHistoryService.RecordLaunch(tool.Path);
            ShowStatus("已启动", tool.Name, InfoBarSeverity.Success);
            return;
        }

        try
        {
            ToolProcessLauncher.Launch(exePath, tool.EffectiveWorkingDir, runAsAdmin);

            LaunchHistoryService.RecordLaunch(tool.Path);
            ShowStatus(runAsAdmin ? "已以管理员身份启动" : "已启动", tool.Name, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("启动失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task LaunchBuiltinToolAsync(ToolItem tool)
    {
        var builtinTool = BuiltinToolRegistry.GetById(tool.BuiltinToolId!);
        if (builtinTool is null)
        {
            ShowStatus("启动失败", "找不到对应的内置工具", InfoBarSeverity.Error);
            return;
        }

        try
        {
            var context = new BuiltinToolContext
            {
                XamlRoot = XamlRoot,
                OnProgress = msg => DispatcherQueue.TryEnqueue(() =>
                    ShowStatus(builtinTool.Name, msg, InfoBarSeverity.Informational))
            };
            MainWindow.ActiveToolName = builtinTool.Name;
            await builtinTool.ExecuteAsync(context);
            LaunchHistoryService.RecordLaunch(tool.Path);
            ShowStatus("已启动", tool.Name, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("启动失败", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            MainWindow.ActiveToolName = null;
        }
    }

    private async Task HandleWingetToolAsync(ToolItem tool)
    {
        if (tool.IsWingetInstalling) return;

        if (!tool.IsWingetInstalled)
        {
            var installed = await WingetService.IsInstalledAsync(tool.WingetId!);
            if (installed)
            {
                tool.IsWingetInstalled = true;
                LaunchInstalledWingetTool(tool);
                return;
            }

            tool.IsWingetInstalling = true;
            ShowStatus("正在安装", $"正在通过 winget 安装「{tool.Name}」...", InfoBarSeverity.Informational);

            var progress = new Progress<WingetInstallProgress>(p =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    tool.WingetInstallStatus = p.StatusLine;
                    if (p.Percent > 0) tool.WingetInstallProgress = p.Percent;
                });
            });

            var result = await WingetService.InstallAsync(tool.WingetId!, progress);
            tool.IsWingetInstalling = false;

            if (result.Success)
            {
                tool.IsWingetInstalled = true;
                ShowStatus("安装完成", $"「{tool.Name}」安装成功，点击打开即可使用。", InfoBarSeverity.Success);
            }
            else
            {
                ShowStatus("安装失败", result.Message, InfoBarSeverity.Error);
            }
            return;
        }

        LaunchInstalledWingetTool(tool);
    }

    private void LaunchInstalledWingetTool(ToolItem tool)
    {
        var exePath = WingetService.FindInstalledExePath(tool.WingetId!);
        if (exePath is not null && File.Exists(exePath))
        {
            try
            {
                ToolProcessLauncher.Launch(exePath, Path.GetDirectoryName(exePath));
                ShowStatus("已启动", tool.Name, InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("启动失败", ex.Message, InfoBarSeverity.Error);
            }
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" winget run --id {tool.WingetId} --accept-source-agreements",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            ShowStatus("已启动", tool.Name, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("启动失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task ShowDownloadDialogAsync(ToolItem tool)
    {
        var toolDir = Path.GetDirectoryName(tool.Path) ?? Path.Combine(ToolCatalog.ToolsRoot, tool.Category, tool.Folder);
        Directory.CreateDirectory(toolDir);

        var dialog = new ToolDownloadDialog(
            tool.Name,
            tool.Description ?? "",
            tool.DownloadUrl!,
            tool.DownloadFilter,
            toolDir);

        await dialog.ShowAsync();
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

    private static string ValueOrUnknown(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "未知" : value;
    }

    #region 拖放导入工具（Win32 API 绕过 UIPI）

    private static readonly HashSet<string> DropImportableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".exe", ".bat", ".cmd", ".ps1", ".vbs"
    };

    private bool _dropEventSubscribed;

    private void InstallDropHook()
    {
        // 获取主窗口句柄并安装拖放钩子；此调用依赖 App.MainWindow 的 WinRT 封送，
        // 启动早期可能尚未就绪而抛异常，不能让一个非关键钩子打断启动流程。
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            if (hwnd != IntPtr.Zero) Win32DropHelper.EnsureInstalled(hwnd);
        }
        catch
        {
            // 拖放钩子失败不影响其余功能
        }

        if (!_dropEventSubscribed)
        {
            _dropEventSubscribed = true;
            Win32DropHelper.FilesDropped += OnDropFiles;
        }
    }

    private void UninstallDropHook()
    {
        _dropEventSubscribed = false;
        Win32DropHelper.FilesDropped -= OnDropFiles;
    }

    private void OnDropFiles(IReadOnlyList<string> files)
    {
        var importable = files.FirstOrDefault(f => DropImportableExtensions.Contains(Path.GetExtension(f)));
        if (importable is null) return;

        DispatcherQueue.TryEnqueue(() => _ = ImportDroppedFileAsync(importable));
    }

    /// <summary>拖入文件后弹出导入弹窗；确认后导入、刷新导航与本页工具列表。</summary>
    private async Task ImportDroppedFileAsync(string filePath)
    {
        try
        {
            await ImportDroppedFileCoreAsync(filePath);
        }
        catch (Exception ex)
        {
            // 兜底：弹窗链路任何异常都不能静默（旧版开独立窗口不依赖页面状态，
            // 弹窗必须挂主窗口 XamlRoot 才能保证任何页面状态下都能弹出）
            ShowStatus("导入失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task ImportDroppedFileCoreAsync(string filePath)
    {
        // 弹窗挂主窗口的 XamlRoot（与旧窗口 Content.XamlRoot 等价），
        // 不依赖 HomePage 是否正处于可视状态（离开首页后 HomePage.XamlRoot 为 null）
        var xamlRoot = App.MainWindow?.Content?.XamlRoot ?? XamlRoot;
        if (xamlRoot is null)
        {
            ShowStatus("导入失败", "无法获取窗口，请重试", InfoBarSeverity.Error);
            return;
        }

        var isZip = Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<ImportableExecutable> executables;
        try
        {
            executables = isZip
                ? CustomToolPackageService.GetExecutables(filePath)
                : [new ImportableExecutable(Path.GetFileName(filePath))];
        }
        catch (Exception ex)
        {
            ShowStatus("无法读取文件", ex.Message, InfoBarSeverity.Error);
            return;
        }

        if (executables.Count == 0)
        {
            await ShowMessageAsync("未找到可导入工具", "压缩包里需要至少包含一个 .exe 文件。");
            return;
        }

        var dialog = new CustomToolImportDialog(xamlRoot, filePath, executables);
        var request = await dialog.ShowImportAsync();
        if (request is null) return;

        try
        {
            var result = isZip
                ? await CustomToolPackageService.ImportAsync(request)
                : await CustomToolPackageService.ImportSingleFileAsync(
                    filePath,
                    request.ToolName,
                    request.Category,
                    request.Description,
                    request.Publisher,
                    request.Tags);

            // 新建分类：记录图标并追加到分类顺序，保持固定位置而非按字母序插入
            if (dialog.IsNewCategory)
            {
                AppSettings.Set($"CategoryGlyph_{request.Category}", dialog.NewCategoryGlyph ?? "\uE8B7");
                ToolCatalog.AppendCategoryOrder(request.Category);
            }

            ToolCatalog.InvalidateTagsCache();
            if (App.MainWindow is MainWindow mainWindow)
                mainWindow.RefreshToolCategories();

            await LoadToolsAsync();
            ShowStatus("导入成功", $"已导入 {Path.GetFileName(result.ToolDirectory)}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            // 分类下没有工具就删掉：新建分类导入失败时清理留下的空白目录
            if (ToolCatalog.PruneCategoryIfEmpty(request.Category))
            {
                ToolCatalog.InvalidateTagsCache();
                if (App.MainWindow is MainWindow mainWindow)
                    mainWindow.RefreshToolCategories();
            }
            ShowStatus("导入失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = App.MainWindow?.Content?.XamlRoot ?? XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "确定",
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        await dialog.ShowAsync();
    }

    #endregion
}

public sealed class ZeroToIndeterminateConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int progress)
            return progress <= 0;
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed class BoolToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
            return b ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}