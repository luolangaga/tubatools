using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Diagnostics;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class FavoritesPage : Page
{
    private readonly ObservableCollection<ToolItem> _tools = [];
    private CancellationTokenSource? _iconLoadCts;

    public FavoritesPage()
    {
        InitializeComponent();
        ToolsGrid.ItemsSource = _tools;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadTools();
    }

    private void LoadTools()
    {
        _iconLoadCts?.Cancel();
        _tools.Clear();

        var favPaths = FavoritesService.GetFavorites();
        var favTools = ToolCatalog.GetCategories()
            .SelectMany(ToolCatalog.GetTools)
            .Where(t => favPaths.Contains(t.Path, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var tool in favTools)
        {
            _tools.Add(tool);
        }

        ToolCountText.Text = _tools.Count > 0 ? $"已收藏 {_tools.Count} 个工具" : "暂无收藏";
        ClearAllButton.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolsGrid.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (favTools.Count > 0)
        {
            _iconLoadCts = new CancellationTokenSource();
            _ = ToolIconService.LoadIconsAsync(favTools, DispatcherQueue);
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
        if (sender is Button { DataContext: ToolItem tool })
        {
            LaunchTool(tool, runAsAdmin: false);
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

    private void RemoveFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ToolItem tool })
        {
            FavoritesService.RemoveFavorite(tool.Path);
            _tools.Remove(tool);
            ToolCountText.Text = _tools.Count > 0 ? $"已收藏 {_tools.Count} 个工具" : "暂无收藏";
            ClearAllButton.Visibility = _tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "清空全部收藏",
            Content = "确定要取消所有工具的收藏吗？此操作不可撤销。",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (dialog.ShowAsync() is not null)
        {
            dialog.PrimaryButtonClick += (_, _) =>
            {
                FavoritesService.RemoveAll();
                LoadTools();
            };
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
