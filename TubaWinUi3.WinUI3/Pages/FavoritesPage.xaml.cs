using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Text;
using System.Collections.ObjectModel;
using System.Diagnostics;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class FavoritesPage : Page
{
    private readonly ObservableCollection<ToolItem> _tools = [];
    private readonly List<ToolItem> _frequentTools = [];
    private CancellationTokenSource? _iconLoadCts;
    private bool _isEditing;

    public FavoritesPage()
    {
        InitializeComponent();
        ToolsGrid.ItemsSource = _tools;
    }

    private void ToolsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var panel = ToolsGrid.ItemsPanelRoot as ItemsWrapGrid;
        if (panel is null) return;

        double minItemWidth = 280;
        double spacing = 12;
        double availableWidth = ToolsGrid.ActualWidth - ToolsGrid.Padding.Left - ToolsGrid.Padding.Right;

        if (availableWidth <= 0) return;

        int columns = Math.Max(1, (int)((availableWidth + spacing) / (minItemWidth + spacing)));
        double itemWidth = (availableWidth - (columns - 1) * spacing) / columns;
        panel.ItemWidth = Math.Max(minItemWidth, itemWidth);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = LoadFrequentToolsAsync();
        _ = LoadToolsAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ExitEditMode();
    }

    private async Task LoadFrequentToolsAsync()
    {
        _frequentTools.Clear();

        var frequentRecords = LaunchHistoryService.GetFrequentTools(12);
        if (frequentRecords.Count == 0)
        {
            FrequentSection.Visibility = Visibility.Collapsed;
            return;
        }

        List<ToolItem> allTools;
        try
        {
            allTools = await Task.Run(() => ToolCatalog.GetAllToolsCached().ToList());
        }
        catch
        {
            FrequentSection.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var record in frequentRecords)
        {
            var tool = allTools.FirstOrDefault(t =>
                t.Path.Equals(record.Path, StringComparison.OrdinalIgnoreCase));
            if (tool is not null)
                _frequentTools.Add(tool);
        }

        if (_frequentTools.Count == 0)
        {
            FrequentSection.Visibility = Visibility.Collapsed;
            return;
        }

        FrequentPanel.Children.Clear();
        foreach (var tool in _frequentTools)
        {
            var card = CreateFrequentCard(tool);
            FrequentPanel.Children.Add(card);
        }

        FrequentSection.Visibility = Visibility.Visible;
        FrequentSubtitle.Text = _frequentTools.Count >= 12
            ? $"基于使用频率智能排序 · 前 {_frequentTools.Count} 个"
            : $"基于使用频率智能排序 · {_frequentTools.Count} 个";

        _ = ToolIconService.LoadIconsAsync(_frequentTools.ToList(), DispatcherQueue);
    }

    private Border CreateFrequentCard(ToolItem tool)
    {
        var card = new Border
        {
            Padding = new Thickness(10, 10, 10, 8),
            Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Width = 100,
            Tag = tool
        };

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 6
        };

        var iconBorder = new Border
        {
            Width = 48,
            Height = 48,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(10)
        };

        var iconGrid = new Grid();
        var image = new Image
        {
            Width = 36,
            Height = 36,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
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
            FontSize = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Glyph = tool.IconGlyph ?? "",
            Visibility = tool.IconGlyph is not null ? Visibility.Visible : Visibility.Collapsed
        };
        iconGrid.Children.Add(image);
        iconGrid.Children.Add(fontIcon);
        iconBorder.Child = iconGrid;
        stack.Children.Add(iconBorder);

        var nameBlock = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 12,
            MaxLines = 2,
            Text = tool.Name,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap,
            Width = 80
        };
        stack.Children.Add(nameBlock);

        card.Child = stack;

        var toolTip = new ToolTip
        {
            Content = string.IsNullOrWhiteSpace(tool.Description) ? tool.Name : $"{tool.Name}\n{tool.Description}"
        };
        ToolTipService.SetToolTip(card, toolTip);

        card.PointerPressed += (s, e) =>
        {
            LaunchTool(tool, runAsAdmin: false);
        };

        return card;
    }

    private async Task LoadToolsAsync()
    {
        _iconLoadCts?.Cancel();
        _tools.Clear();

        var favPaths = FavoritesService.GetFavorites();
        if (favPaths.Count == 0)
        {
            ToolCountText.Text = "暂无收藏";
            ClearAllButton.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            ToolsGrid.Visibility = Visibility.Collapsed;
            return;
        }

        List<ToolItem> favTools;
        try
        {
            favTools = await Task.Run(() =>
            {
                var all = ToolCatalog.GetCategories()
                    .SelectMany(ToolCatalog.GetTools)
                    .ToList();
                // 按收藏列表顺序匹配,而不是目录分类顺序
                return favPaths
                    .Select(p => all.FirstOrDefault(t => t.Path.Equals(p, StringComparison.OrdinalIgnoreCase)))
                    .OfType<ToolItem>() // 收藏了但工具已不存在的路径跳过
                    .ToList();
            });
        }
        catch
        {
            ToolCountText.Text = "加载失败";
            return;
        }

        foreach (var tool in favTools)
        {
            _tools.Add(tool);
        }

        ToolCountText.Text = $"已收藏 {_tools.Count} 个工具";
        var hasTools = _tools.Count > 0;
        ClearAllButton.Visibility = hasTools ? Visibility.Visible : Visibility.Collapsed;
        EditOrderButton.Visibility = hasTools ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasTools ? Visibility.Collapsed : Visibility.Visible;
        ToolsGrid.Visibility = hasTools ? Visibility.Visible : Visibility.Collapsed;

        if (favTools.Count > 0)
        {
            _iconLoadCts = new CancellationTokenSource();
            _ = ToolIconService.LoadIconsAsync(favTools, DispatcherQueue);
        }
    }

    /// <summary>编辑排序模式开关:进入后切换到专用排序列表,整行自实现拖拽。</summary>
    private void EditOrderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isEditing)
            ExitEditMode();
        else
            EnterEditMode();
    }

    private void EnterEditMode()
    {
        _isEditing = true;
        EditOrderIcon.Glyph = "\uE73E"; // CheckMark:完成
        EditOrderButtonText.Text = "完成";
        ClearAllButton.Visibility = Visibility.Collapsed;
        ToolsGrid.Visibility = Visibility.Collapsed;
        FrequentSection.Visibility = Visibility.Collapsed;

        EditRowsPanel.Children.Clear();
        foreach (var tool in _tools)
            EditRowsPanel.Children.Add(CreateEditRow(tool));
        EditModePanel.Visibility = Visibility.Visible;
    }

    private void ExitEditMode()
    {
        if (!_isEditing) return;
        _isEditing = false;
        FinishDrag(); // 拖动到一半退出时归位
        EditOrderIcon.Glyph = "\uE70F"; // Edit:编辑排序
        EditOrderButtonText.Text = "编辑排序";
        EditModePanel.Visibility = Visibility.Collapsed;
        ClearAllButton.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolsGrid.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FrequentSection.Visibility = _frequentTools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // 拖拽过程已实时保存,这里兜底保证最终顺序落盘
        FavoritesService.SaveOrder(_tools.Select(t => t.Path));
    }

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

    private Border CreateEditRow(ToolItem tool)
    {
        var row = new Border
        {
            Height = EditRowHeight,
            Padding = new Thickness(12, 0, 12, 0),
            Background = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardStrokeColorDefaultBrush"],
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

        // 图标(与常用推荐卡片相同的双元素绑定)
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
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
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

        // 名称 + 分类
        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        textStack.Children.Add(new TextBlock
        {
            Text = tool.Name,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        textStack.Children.Add(new TextBlock
        {
            Text = tool.Category,
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
            FavoritesService.SaveOrder(_tools.Select(t => t.Path));
        }
        _dragRow = null;
        _dragTranslate = null;
        _dragging = false;
    }

    private void ToolsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ToolItem tool)
        {
            ShowToolDetail(tool);
        }
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ToolItem tool })
        {
            LaunchTool(tool, runAsAdmin: false);
        }
    }

    private void RunAsAdminButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ToolItem tool })
        {
            LaunchTool(tool, runAsAdmin: true);
        }
    }

    private void RemoveFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ToolItem tool })
        {
            FavoritesService.RemoveFavorite(tool.Path);
            _tools.Remove(tool);
            ToolCountText.Text = _tools.Count > 0 ? $"已收藏 {_tools.Count} 个工具" : "暂无收藏";
            ClearAllButton.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EditOrderButton.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ToolsGrid.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
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

    private void FavItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ToolItem tool)
        {
            var flyout = (MenuFlyout)Resources["FavItemFlyout"];
            PopulateArchSubmenu(flyout, tool);
            UpdateTutorialVisibility(flyout, tool);
            UpdateBuiltinLinkFlyoutItems(flyout, tool);
            UpdateFavoriteMenuItem(flyout, tool);
            flyout.ShowAt(fe, e.GetPosition(fe));
        }
    }

    private static void UpdateFavoriteMenuItem(MenuFlyout flyout, ToolItem tool)
    {
        var item = flyout.Items.OfType<MenuFlyoutItem>().FirstOrDefault(i => i.Name == "FavMenuToggleFavorite");
        if (item is null) return;
        item.Text = tool.IsFavorite ? "取消收藏" : "收藏";
        if (item.Icon is FontIcon icon)
            icon.Glyph = tool.IsFavorite ? "\uE735" : "\uE734";
    }

    private void FavMenu_ToggleFavorite(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
        {
            FavoritesService.ToggleFavorite(tool.Path);
            tool.IsFavorite = !tool.IsFavorite;
            if (!tool.IsFavorite)
            {
                _tools.Remove(tool);
                ToolCountText.Text = _tools.Count > 0 ? $"已收藏 {_tools.Count} 个工具" : "暂无收藏";
                ClearAllButton.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                ToolsGrid.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private static void UpdateTutorialVisibility(MenuFlyout flyout, ToolItem tool)
    {
        var tutorialItem = flyout.Items.OfType<MenuFlyoutItem>()
            .FirstOrDefault(i => i.Text.Contains("教程"));
        if (tutorialItem is not null)
            tutorialItem.Visibility = tool.HasTutorial ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void UpdateBuiltinLinkFlyoutItems(MenuFlyout flyout, ToolItem tool)
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
    }

    private void FavMenu_SendToDesktop(object sender, RoutedEventArgs e)
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

    private void FavMenu_RunAsAdmin(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
            LaunchTool(tool, runAsAdmin: true);
    }

    private void FavMenu_OpenDirectory(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool })
            OpenToolDirectory(tool);
    }

    private void FavMenu_OpenTutorial(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ToolItem tool } && tool.HasTutorial)
            BrowserPage.Open(tool.TutorialUrl!, $"{tool.Name} - 使用教程");
    }

    private static void OpenToolDirectory(ToolItem tool)
    {
        try
        {
            var dir = tool.EffectiveWorkingDir;
            if (Directory.Exists(dir))
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch { }
    }

    private void PopulateArchSubmenu(MenuFlyout flyout, ToolItem tool)
    {
        var submenu = flyout.Items.OfType<MenuFlyoutSubItem>().FirstOrDefault(i => i.Name == "FavArchSubmenu");
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

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "清空全部收藏",
            Content = "确定要取消所有工具的收藏吗？此操作不可撤销。",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        if (dialog.ShowAsync() is not null)
        {
            dialog.PrimaryButtonClick += (_, _) =>
            {
                FavoritesService.RemoveAll();
                _ = LoadToolsAsync();
            };
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

        var exePath = tool.EffectivePath;
        if (!File.Exists(exePath))
        {
            ShowStatus("启动失败", $"找不到文件：{exePath}", InfoBarSeverity.Error);
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
}
