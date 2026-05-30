using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Diagnostics;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class HomePage : Page
{
    private readonly ObservableCollection<ToolItem> _tools = [];
    private string? _category;
    private string? _selectedTag;
    private int _loadedCount;
    private const int PageSize = 40;
    private bool _isLoadingMore;
    private bool _allLoaded;
    private CancellationTokenSource? _iconLoadCts;
    private bool _compactMode;

    public HomePage()
    {
        InitializeComponent();
        ToolsGrid.ItemsSource = _tools;
        CompactGrid.ItemsSource = _tools;
        ToolsRootText.Text = ToolCatalog.ToolsRoot;
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
            ToolsScrollViewer.Visibility = Visibility.Collapsed;
            CompactScrollViewer.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            ToolsScrollViewer.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            CompactScrollViewer.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateGridVisibility(bool hasTools)
    {
        if (_compactMode)
        {
            ToolsGrid.Visibility = Visibility.Collapsed;
            CompactGrid.Visibility = hasTools ? Visibility.Visible : Visibility.Collapsed;
            ToolsScrollViewer.Visibility = Visibility.Collapsed;
            CompactScrollViewer.Visibility = hasTools ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            ToolsGrid.Visibility = hasTools ? Visibility.Visible : Visibility.Collapsed;
            CompactGrid.Visibility = Visibility.Collapsed;
            ToolsScrollViewer.Visibility = hasTools ? Visibility.Visible : Visibility.Collapsed;
            CompactScrollViewer.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _category = e.Parameter as string;
        SearchBox.Text = string.Empty;
        _selectedTag = null;
        ResetAndLoad();
        ApplyBackground();
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
            ResetAndLoad();
        }
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

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason is AutoSuggestionBoxTextChangeReason.UserInput or AutoSuggestionBoxTextChangeReason.ProgrammaticChange)
        {
            ResetAndLoad();
        }
    }

    private void ClearTagFilter()
    {
        _selectedTag = null;
        foreach (var child in TagBarPanel.Children)
        {
            if (child is RadioButton rb)
                rb.IsChecked = rb.Tag is null;
        }
    }

    private void CompactGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ToolItem tool)
        {
            if (tool.ArchOptions.Count > 1)
            {
                _ = ShowArchPickerAndLaunchAsync(tool, runAsAdmin: false);
            }
            else
            {
                LaunchTool(tool, runAsAdmin: false);
            }
        }
    }

    private void CompactItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ToolItem)
        {
            var flyout = (MenuFlyout)CompactGrid.Resources["CompactItemFlyout"];
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
        {
            if (tool.ArchOptions.Count > 1)
            {
                _ = ShowArchPickerAndLaunchAsync(tool, runAsAdmin: true);
            }
            else
            {
                LaunchTool(tool, runAsAdmin: true);
            }
        }
    }

    private async Task ShowArchPickerAndLaunchAsync(ToolItem tool, bool runAsAdmin)
    {
        var dialog = new ContentDialog
        {
            Title = $"选择架构 - {tool.Name}",
            PrimaryButtonText = "打开",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };

        var panel = new StackPanel { Spacing = 8 };
        var radioButtons = new List<RadioButton>();

        foreach (var opt in tool.ArchOptions)
        {
            var rb = new RadioButton
            {
                Content = string.IsNullOrEmpty(opt.Arch) ? "默认" : opt.Arch,
                Tag = opt,
                IsChecked = opt == tool.SelectedArch
            };
            radioButtons.Add(rb);
            panel.Children.Add(rb);
        }

        dialog.Content = panel;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var selected = radioButtons.FirstOrDefault(rb => rb.IsChecked == true)?.Tag as ArchOption
                ?? tool.SelectedArch;
            if (selected is not null)
            {
                tool.SelectedArch = selected;
            }
            LaunchTool(tool, runAsAdmin: false);
        }
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
        if (sender is FrameworkElement { DataContext: ToolItem tool })
        {
            FavoritesService.ToggleFavorite(tool.Path);
            tool.IsFavorite = !tool.IsFavorite;

            var idx = _tools.IndexOf(tool);
            if (idx >= 0)
            {
                _tools[idx] = tool;
            }
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

    private void ScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        if (sv.VerticalOffset >= sv.ScrollableHeight - 200 && !_isLoadingMore && !_allLoaded && SearchBox.Text.Trim().Length == 0 && _selectedTag is null)
        {
            LoadMore();
        }
    }

    private void ResetAndLoad()
    {
        _tools.Clear();
        _loadedCount = 0;
        _allLoaded = false;
        _isLoadingMore = false;
        _iconLoadCts?.Cancel();
        LoadMore();

        var query = SearchBox.Text.Trim();
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

    private void LoadMore()
    {
        _isLoadingMore = true;
        var query = SearchBox.Text.Trim();

        if (query.Length > 0 || _selectedTag is not null)
        {
            var results = ToolCatalog.Search(query, _selectedTag);
            foreach (var tool in results)
                _tools.Add(tool);
            _allLoaded = true;

            ToolCountText.Text = _tools.Count > 0 ? $"{_tools.Count} 个工具" : "无结果";
            EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateGridVisibility(_tools.Count > 0);
            EmptyStateText.Text = query.Length > 0
                ? $"未找到与\u201C{query}\u201D相关的工具。"
                : $"未找到带有「{_selectedTag}」标签的工具。";
            _isLoadingMore = false;
            StartIconLoading(results);
            return;
        }

        if (_category is not null)
        {
            if (_loadedCount == 0)
            {
                var tools = ToolCatalog.GetTools(_category);
                foreach (var tool in tools)
                    _tools.Add(tool);
                _allLoaded = true;
                StartIconLoading(tools);
            }
            ToolCountText.Text = $"{_tools.Count} 个工具";
            EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateGridVisibility(_tools.Count > 0);
            EmptyStateText.Text = "此分类下没有可用工具。";
            _isLoadingMore = false;
            return;
        }

        if (_compactMode)
        {
            var allTools = ToolCatalog.GetAllToolsLazy(0, int.MaxValue);
            foreach (var tool in allTools)
                _tools.Add(tool);
            _allLoaded = true;

            ToolCountText.Text = $"{_tools.Count} 个工具";
            EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateGridVisibility(_tools.Count > 0);
            EmptyStateText.Text = "没有找到任何工具，请检查 Tools 目录。";

            _isLoadingMore = false;
            StartIconLoading(allTools);
            return;
        }

        var batch = ToolCatalog.GetAllToolsLazy(_loadedCount, PageSize);
        foreach (var tool in batch)
            _tools.Add(tool);

        _loadedCount += batch.Count;
        _allLoaded = batch.Count < PageSize;

        ToolCountText.Text = _allLoaded
            ? $"{_tools.Count} 个工具"
            : $"{_tools.Count} / {ToolCatalog.GetAllToolsCount()} 个工具";

        EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateGridVisibility(_tools.Count > 0);
        EmptyStateText.Text = "没有找到任何工具，请检查 Tools 目录。";

        _isLoadingMore = false;
        StartIconLoading(batch);
    }

    private void StartIconLoading(IReadOnlyList<ToolItem> tools)
    {
        if (tools.Count == 0) return;
        _iconLoadCts?.Cancel();
        _iconLoadCts = new CancellationTokenSource();
        var ct = _iconLoadCts.Token;
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

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = tool.EffectivePath,
                WorkingDirectory = tool.EffectiveWorkingDir,
                UseShellExecute = true,
                Verb = runAsAdmin ? "runAs" : null
            });

            ShowStatus(runAsAdmin ? "已以管理员身份启动" : "已启动", tool.Name, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus("启动失败", ex.Message, InfoBarSeverity.Error);
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