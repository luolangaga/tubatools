using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private int _loadedCount;
    private const int PageSize = 40;
    private bool _isLoadingMore;
    private bool _allLoaded;
    private CancellationTokenSource? _iconLoadCts;

    public HomePage()
    {
        InitializeComponent();
        ToolsGrid.ItemsSource = _tools;
        ToolsRootText.Text = ToolCatalog.ToolsRoot;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _category = e.Parameter as string;
        SearchBox.Text = string.Empty;
        ResetAndLoad();
        ApplyBackground();
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

    private void LaunchSplitButton_Click(SplitButton sender, SplitButtonClickEventArgs e)
    {
        if (sender.DataContext is ToolItem tool)
        {
            LaunchTool(tool, runAsAdmin: false);
        }
    }

    private void LaunchSplitButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is SplitButton splitBtn && splitBtn.DataContext is ToolItem tool)
        {
            if (splitBtn.Flyout is MenuFlyout flyout)
            {
                flyout.Opening += (s, _) =>
                {
                    flyout.Items.Clear();
                    foreach (var variant in tool.AlternateVersions)
                    {
                        var item = new MenuFlyoutItem
                        {
                            Text = $"打开（{variant.Arch}）",
                            DataContext = variant
                        };
                        item.Click += AlternateVersion_Click;
                        flyout.Items.Add(item);
                    }
                };
            }
        }
    }

    private void AlternateVersion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { DataContext: ArchVariant variant })
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = variant.Path,
                    WorkingDirectory = Path.GetDirectoryName(variant.Path) ?? ToolCatalog.ToolsRoot,
                    UseShellExecute = true
                });

                ShowStatus("已启动", variant.Name, InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowStatus("启动失败", ex.Message, InfoBarSeverity.Error);
            }
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
        var shortcutPath = Path.Combine(desktop, $"{tool.Name}.lnk");

        var psScript = $"""
            $ws = New-Object -ComObject WScript.Shell
            $s = $ws.CreateShortcut('{shortcutPath}')
            $s.TargetPath = '{tool.Path}'
            $s.WorkingDirectory = '{Path.GetDirectoryName(tool.Path) ?? ToolCatalog.ToolsRoot}'
            $s.Description = '{tool.Name}'
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
        if (sv.VerticalOffset >= sv.ScrollableHeight - 200 && !_isLoadingMore && !_allLoaded && SearchBox.Text.Trim().Length == 0)
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
        CategoryTitle.Text = query.Length > 0 ? $"搜索：{query}" : (_category ?? "全部工具");
        CategorySubtitle.Text = query.Length > 0
            ? "显示所有分类中匹配的工具。"
            : _category is null
                ? "从左侧选择分类，点击卡片看详情，点击打开运行工具。"
                : $"正在浏览\u201C{_category}\u201D分类。";
    }

    private void LoadMore()
    {
        _isLoadingMore = true;
        var query = SearchBox.Text.Trim();

        if (query.Length > 0)
        {
            var results = ToolCatalog.Search(query);
            foreach (var tool in results)
                _tools.Add(tool);
            _allLoaded = true;

            ToolCountText.Text = _tools.Count > 0 ? $"{_tools.Count} 个工具" : "无结果";
            EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ToolsGrid.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyStateText.Text = $"未找到与\u201C{query}\u201D相关的工具。";
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
            ToolsGrid.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyStateText.Text = "此分类下没有可用工具。";
            _isLoadingMore = false;
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
        ToolsGrid.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = tool.Path,
                WorkingDirectory = Path.GetDirectoryName(tool.Path) ?? ToolCatalog.ToolsRoot,
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

        if (dialog.DownloadSucceeded)
        {
            ShowStatus("下载完成", $"「{tool.Name}」已下载到工具目录，刷新后可直接打开。", InfoBarSeverity.Success);
        }
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