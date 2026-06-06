using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class HomePage : Page
{
    private readonly BulkObservableCollection<ToolItem> _tools = new();
    private string? _category;
    private string? _selectedTag;
    private CancellationTokenSource? _loadCts;
    private bool _compactMode;
    private string? _highlightToolPath;
    private string _searchQuery = string.Empty;

    public HomePage()
    {
        InitializeComponent();
        ToolsGrid.ItemsSource = _tools;
        CompactGrid.ItemsSource = _tools;

        _compactMode = CompactModeService.IsCompactModeEnabled();
        ApplyCompactMode();
        CompactModeService.CompactModeChanged += OnCompactModeChanged;
    }

    private void OnCompactModeChanged(bool enabled)
    {
        _compactMode = enabled;
        ApplyCompactMode();
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
            CompactGrid.Visibility = hasTools ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            ToolsGrid.Visibility = hasTools ? Visibility.Visible : Visibility.Collapsed;
            CompactGrid.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is SearchNavigationTarget target && target.HighlightToolPath is not null)
        {
            _highlightToolPath = target.HighlightToolPath;
            try
            {
                var tool = ToolCatalog.GetAllToolsLazy(0, int.MaxValue)
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
        ApplyBackground();
        UpdateTitle();
        _ = LoadToolsAsync();
        if (_category is null)
            _ = PopulateTagBarAsync();
        else
            TagBarScrollViewer.Visibility = Visibility.Collapsed;
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
            TagBarScrollViewer.Visibility = Visibility.Collapsed;
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            TagBarPanel.Children.Clear();

            var allBtn = new RadioButton
            {
                Content = "全部",
                Tag = null as string,
                IsChecked = _selectedTag is null,
                Padding = new Thickness(10, 4, 10, 4),
                Style = (Style)Resources["TagRadioButtonStyle"]
            };
            allBtn.Click += TagRadioButton_Click;
            TagBarPanel.Children.Add(allBtn);

            TagBarScrollViewer.Visibility = tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var tag in tags)
            {
                var btn = new RadioButton
                {
                    Content = tag,
                    Tag = tag,
                    IsChecked = tag == _selectedTag,
                    Padding = new Thickness(10, 4, 10, 4),
                    Style = (Style)Resources["TagRadioButtonStyle"]
                };
                btn.Click += TagRadioButton_Click;
                TagBarPanel.Children.Add(btn);
            }
        });
    }

    private void TagRadioButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb)
        {
            _selectedTag = rb.Tag as string;
            foreach (var child in TagBarPanel.Children)
            {
                if (child is RadioButton other && other != rb)
                    other.IsChecked = false;
            }
            UpdateTitle();
            _ = LoadToolsAsync();
        }
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
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        _tools.Clear();

        var query = _searchQuery;

        try
        {
            IReadOnlyList<ToolItem> tools = await Task.Run(() =>
            {
                if (query.Length > 0 || _selectedTag is not null)
                    return ToolCatalog.Search(query, _selectedTag);
                if (_category is not null)
                    return ToolCatalog.GetTools(_category);
                return ToolCatalog.GetAllToolsLazy(0, int.MaxValue);
            }, cts.Token);

            cts.Token.ThrowIfCancellationRequested();

            _tools.AddRange(tools);

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

            StartIconLoading(tools);

            if (_highlightToolPath is not null)
            {
                _ = HighlightToolAsync(_highlightToolPath);
                _highlightToolPath = null;
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task HighlightToolAsync(string toolPath)
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

        if (index < 0) return;

        grid.ScrollIntoView(_tools[index]);
        await Task.Delay(100);

        var container = grid.ContainerFromItem(_tools[index]) as GridViewItem;
        if (container is null) return;

        var scrollViewer = FindChildScrollViewer(grid);
        if (scrollViewer is not null)
        {
            var transform = container.TransformToVisual(scrollViewer.Content as UIElement ?? scrollViewer);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            var targetOffset = scrollViewer.VerticalOffset + point.Y - scrollViewer.ViewportHeight / 2 + container.ActualHeight / 2;
            targetOffset = Math.Max(0, Math.Min(targetOffset, scrollViewer.ExtentHeight - scrollViewer.ViewportHeight));
            scrollViewer.ChangeView(null, targetOffset, null, disableAnimation: false);
            await Task.Delay(600);
        }

        var border = FindChildBorder(container);
        if (border is not null)
            SearchHighlightService.HighlightBorder(border);
    }

    private static ScrollViewer? FindChildScrollViewer(DependencyObject parent)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv) return sv;
            var result = FindChildScrollViewer(child);
            if (result is not null) return result;
        }
        return null;
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

    private void ApplyBackground()
    {
        var bmp = BackgroundService.LoadBackgroundImage();
        if (bmp is not null)
        {
            BackgroundImg.Source = bmp;
            BackgroundImg.Opacity = BackgroundService.GetBackgroundOpacity();
            BackgroundImg.Visibility = Visibility.Visible;
        }
        else
        {
            BackgroundImg.Source = null;
            BackgroundImg.Visibility = Visibility.Collapsed;
        }
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

    private void NormalItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ToolItem tool)
        {
            var flyout = (MenuFlyout)ToolsGrid.Resources["NormalItemFlyout"];
            PopulateArchSubmenu(flyout, tool);
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
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
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

        foreach (var tool in wingetTools)
        {
            ct.ThrowIfCancellationRequested();
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
        }
    }

    private void ShowToolDetail(ToolItem tool)
    {
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
        if (!string.IsNullOrWhiteSpace(tool.DownloadUrl))
        {
            _ = ShowDownloadDialogAsync(tool);
            return;
        }

        if (!string.IsNullOrWhiteSpace(tool.WingetId))
        {
            _ = HandleWingetToolAsync(tool);
            return;
        }

        if (!string.IsNullOrWhiteSpace(tool.RemoteUrl) && !File.Exists(tool.EffectivePath))
        {
            _ = HandleRemoteDownloadAsync(tool, runAsAdmin);
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
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = tool.EffectiveWorkingDir,
                UseShellExecute = true,
                Verb = runAsAdmin ? "runAs" : null
            });

            LaunchHistoryService.RecordLaunch(tool.Path);
            ShowStatus(runAsAdmin ? "已以管理员身份启动" : "已启动", tool.Name, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("启动失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task HandleRemoteDownloadAsync(ToolItem tool, bool runAsAdmin)
    {
        var toolDir = Path.GetDirectoryName(tool.Path) ?? Path.Combine(ToolCatalog.ToolsRoot, tool.Category, tool.Folder);
        Directory.CreateDirectory(toolDir);

        var dialog = new ContentDialog
        {
            Title = $"下载 {tool.Name}",
            PrimaryButtonText = "开始下载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        dialog.Resources["ContentDialogMaxWidth"] = 520;

        var progressPanel = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
        var progressBar = new ProgressBar { IsIndeterminate = false };
        var percentText = new TextBlock
        {
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
            Text = "0%"
        };
        var speedText = new TextBlock
        {
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = "--"
        };
        var sizeText = new TextBlock
        {
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = "--"
        };
        var timeText = new TextBlock
        {
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = "--"
        };

        progressPanel.Children.Add(progressBar);
        progressPanel.Children.Add(new StackPanel
        {
            Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal,
            Spacing = 16,
            Children = { speedText, sizeText, timeText }
        });
        progressPanel.Children.Add(percentText);

        var descText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Text = $"「{tool.Name}」尚未安装，需要从远程下载并解压到本地。"
        };

        var errorBar = new InfoBar
        {
            IsOpen = false,
            IsClosable = true,
            Severity = InfoBarSeverity.Error,
            Title = "下载失败"
        };

        var contentStack = new StackPanel { Spacing = 12, MinWidth = 420 };
        contentStack.Children.Add(descText);
        contentStack.Children.Add(progressPanel);
        contentStack.Children.Add(errorBar);
        dialog.Content = contentStack;

        var cts = new CancellationTokenSource();
        var downloadStarted = false;

        dialog.PrimaryButtonClick += async (s, e) =>
        {
            if (downloadStarted) { e.Cancel = true; return; }
            var deferral = e.GetDeferral();
            e.Cancel = true;

            downloadStarted = true;
            dialog.IsPrimaryButtonEnabled = false;

            try
            {
                descText.Visibility = Visibility.Collapsed;
                progressPanel.Visibility = Visibility.Visible;
                dialog.PrimaryButtonText = "下载中...";

                var progress = new Progress<ToolDownloadProgress>(p =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        progressBar.Value = p.Percentage;
                        percentText.Text = $"{p.Percentage:F1}%";
                        speedText.Text = ToolDownloaderService.FormatSpeed(p.SpeedMbps);
                        sizeText.Text = $"{ToolDownloaderService.FormatSize(p.BytesReceived)} / {ToolDownloaderService.FormatSize(p.TotalBytes)}";
                        timeText.Text = ToolDownloaderService.FormatTime(p.EstimatedRemaining);
                    });
                });

                var fileName = Path.GetFileName(new Uri(tool.RemoteUrl!).AbsolutePath);
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = $"{tool.Name}.zip";

                var filePath = await ToolDownloaderService.DownloadToFileAsync(
                    tool.RemoteUrl!, toolDir, fileName, progress, cts.Token);

                percentText.Text = "解压中...";
                progressBar.IsIndeterminate = true;
                await ToolDownloaderService.ExtractArchiveAsync(filePath, toolDir, cts.Token);

                dialog.Hide();

                ToolCatalog.InvalidateTagsCache();
                ToolMetadataService.InvalidateCache();

                var exePath = FindLaunchableInDir(toolDir, tool.Name);
                if (exePath is not null)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = exePath,
                            WorkingDirectory = Path.GetDirectoryName(exePath),
                            UseShellExecute = true,
                            Verb = runAsAdmin ? "runAs" : null
                        });
                        LaunchHistoryService.RecordLaunch(tool.Path);
                        ShowStatus("已启动", tool.Name, InfoBarSeverity.Success);
                    }
                    catch (Exception ex)
                    {
                        ShowStatus("启动失败", ex.Message, InfoBarSeverity.Error);
                    }
                }
                else
                {
                    ShowStatus("下载完成", $"「{tool.Name}」已下载解压，请刷新后重试打开。", InfoBarSeverity.Success);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                errorBar.Message = ex.Message;
                errorBar.IsOpen = true;
                progressPanel.Visibility = Visibility.Collapsed;
                descText.Visibility = Visibility.Visible;
                dialog.IsPrimaryButtonEnabled = true;
                dialog.PrimaryButtonText = "重试";
                downloadStarted = false;
            }
            finally
            {
                try { deferral.Complete(); } catch { }
            }
        };

        await dialog.ShowAsync();
    }

    private static string? FindLaunchableInDir(string dir, string toolName)
    {
        if (!Directory.Exists(dir)) return null;

        var launchables = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var ext = Path.GetExtension(f);
                return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                       ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
                       ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (launchables.Count == 0) return null;

        var match = launchables.FirstOrDefault(f =>
            Path.GetFileNameWithoutExtension(f).Equals(toolName, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match;

        match = launchables.FirstOrDefault(f =>
            Path.GetFileNameWithoutExtension(f).Contains(toolName, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match;

        return launchables.FirstOrDefault(f =>
        {
            var name = Path.GetFileNameWithoutExtension(f);
            return !name.Equals("uninstall", StringComparison.OrdinalIgnoreCase) &&
                   !name.Equals("uninst", StringComparison.OrdinalIgnoreCase);
        });
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
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    UseShellExecute = true
                });
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

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private static string ValueOrUnknown(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "未知" : value;
    }
}

public sealed class ArchOptionsCountConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count && count > 1)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
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