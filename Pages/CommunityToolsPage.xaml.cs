using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private CancellationTokenSource? _loadCts;

    public CommunityToolsPage()
    {
        InitializeComponent();

        CategoryFilter.SelectionChanged += CategoryFilter_SelectionChanged;
        Loaded += CommunityToolsPage_Loaded;
    }

    private bool _sourceReady;

    private async void CommunityToolsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _sourceReady = false;
        SourceSelector.SelectedIndex = CommunityToolService.CurrentSource == CommunityDataSource.GitCode ? 0 : 1;
        _sourceReady = true;
        await LoadToolsAsync();
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

            foreach (var tool in _allTools)
            {
                tool.InstallStatus = CommunityToolService.CheckInstallStatus(tool);
                tool.LocalPath = CommunityToolService.GetLocalPath(tool);
                tool.IsAuthor = await CheckIsAuthorAsync(tool);
            }

            UpdateCategoryFilter();
            ApplyFilter();

            ToolCountText.Text = $"共 {_allTools.Count} 个";
            StatusText.Text = _allTools.Count > 0 ? $"共 {_allTools.Count} 个社区工具" : "暂无社区工具";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusBar.Title = "加载失败";
            StatusBar.Message = ex.Message;
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.IsOpen = true;
            StatusText.Text = "加载失败";

            var errDialog = new ContentDialog
            {
                Title = "加载社区工具失败",
                CloseButtonText = "确定",
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            errDialog.Resources["ContentDialogMaxWidth"] = 560;
            errDialog.Content = new ScrollViewer
            {
                MaxHeight = 300,
                Content = new TextBlock
                {
                    Text = ex.InnerException?.Message ?? ex.Message,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    IsTextSelectionEnabled = true
                }
            };
            await errDialog.ShowAsync();
        }
        finally
        {
            LoadingProgress.Visibility = Visibility.Collapsed;
        }
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
        {
            CategoryFilter.SelectedIndex = categories.IndexOf(prevSelection) + 1;
        }
        else
        {
            CategoryFilter.SelectedIndex = 0;
        }

        _currentCategory = CategoryFilter.SelectedIndex == 0 ? null : (string?)CategoryFilter.SelectedItem;
        CategoryFilter.SelectionChanged += CategoryFilter_SelectionChanged;
    }

    private void ApplyFilter()
    {
        _filteredTools = _allTools;

        if (_currentCategory is not null)
        {
            _filteredTools = _filteredTools.Where(t => t.Category == _currentCategory).ToList();
        }

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

        if (_filteredTools.Count == 0 && _allTools.Count > 0)
        {
            EmptyStateText.Text = "没有匹配的社区工具";
            EmptySubmitLink.Visibility = Visibility.Collapsed;
        }
        else if (_allTools.Count == 0)
        {
            EmptyStateText.Text = "成为第一个贡献者！";
            EmptySubmitLink.Visibility = Visibility.Visible;
        }
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryFilter.SelectedIndex <= 0)
            _currentCategory = null;
        else
            _currentCategory = CategoryFilter.SelectedItem as string;

        ApplyFilter();
    }

    private async void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
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
        await ShowSubmitDialogAsync();
    }

    private void ToolsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CommunityTool tool)
        {
            ShowToolDetailAsync(tool);
        }
    }

    private void ToolsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ToolsGrid.ItemsPanelRoot is ItemsWrapGrid wrapGrid)
        {
            var padding = 56;
            wrapGrid.ItemWidth = Math.Max(220, (e.NewSize.Width - padding) / Math.Max(1, (int)((e.NewSize.Width - padding) / 280)));
        }
    }

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not CommunityTool tool) return;

        if (tool.InstallStatus == CommunityToolInstallStatus.Installed)
        {
            CommunityToolService.LaunchPlugin(tool);
            return;
        }

        await InstallToolAsync(tool);
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not CommunityTool tool) return;

        await DeleteToolAsync(tool);
    }

    private static async Task<bool> CheckIsAuthorAsync(CommunityTool tool)
    {
        if (string.IsNullOrWhiteSpace(tool.Author)) return false;
        if (!GitHubAuthService.IsLoggedIn) return false;
        try
        {
            var user = await GitHubAuthService.GetCurrentUserAsync();
            return user is not null && string.Equals(user.Login, tool.Author, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private async Task DeleteToolAsync(CommunityTool tool)
    {
        var loggedIn = await GitHubAuthService.EnsureAuthenticatedAsync(XamlRoot);
        if (!loggedIn) return;

        var user = await GitHubAuthService.GetCurrentUserAsync();
        if (user is null || !string.Equals(user.Login, tool.Author, StringComparison.OrdinalIgnoreCase))
        {
            await ShowMessageAsync("无法删除", "只能删除自己。");
            return;
        }

        var confirmDialog = new ContentDialog
        {
            Title = $"删除工具「{tool.Name}」",
            PrimaryButtonText = "确认删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        confirmDialog.Resources["ContentDialogMaxWidth"] = 480;

        var confirmStack = new StackPanel { Spacing = 12 };

        var warningBorder = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Color.FromArgb(25, 255, 68, 68)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 68, 68)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"确定要删除「{tool.Name}」吗？",
                        FontSize = 14,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "此操作将创建一个删除 Pull Request，审核通过后工具将从社区中移除。已安装的用户不受影响。",
                        FontSize = 13,
                        Opacity = 0.8,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
        confirmStack.Children.Add(warningBorder);

        confirmDialog.Content = confirmStack;

        var result = await confirmDialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var progressDialog = new ContentDialog
        {
            Title = $"正在删除「{tool.Name}」",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        progressDialog.Resources["ContentDialogMaxWidth"] = 480;

        var progressText = new TextBlock
        {
            Text = "准备中...",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        };
        var progressBar = new ProgressBar { IsIndeterminate = true };

        progressDialog.Content = new StackPanel
        {
            Spacing = 8,
            Children = { progressText, progressBar }
        };

        var progressCts = new CancellationTokenSource();
        var deleteTcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        progressDialog.CloseButtonClick += (s, e) =>
        {
            progressCts.Cancel();
            deleteTcs.TrySetResult(null);
        };

        var progress = new Progress<string>(msg =>
        {
            DispatcherQueue.TryEnqueue(() => { progressText.Text = msg; });
        });

        _ = Task.Run(async () =>
        {
            try
            {
                var prUrl = await CommunityToolService.DeletePluginAsync(tool, progress, progressCts.Token);
                deleteTcs.TrySetResult(prUrl);
            }
            catch (OperationCanceledException)
            {
                deleteTcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                deleteTcs.TrySetException(ex);
            }
        }, progressCts.Token);

        _ = Task.Run(async () =>
        {
            try
            {
                var prUrl = await deleteTcs.Task;
                DispatcherQueue.TryEnqueue(() =>
                {
                    progressDialog.Hide();

                    if (prUrl is not null)
                    {
                        _ = ShowDeleteResultAsync(prUrl);
                    }
                });
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    progressDialog.Hide();
                    _ = ShowDeleteErrorAsync(ex);
                });
            }
        });

        await progressDialog.ShowAsync();
    }

    private async Task ShowDeleteResultAsync(string prUrl)
    {
        var dialog = new ContentDialog
        {
            Title = "删除请求已提交",
            CloseButtonText = "确定",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        dialog.Resources["ContentDialogMaxWidth"] = 480;

        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(new TextBlock
        {
            Text = "删除 Pull Request 已创建，审核通过后工具将从社区中移除。",
            TextWrapping = TextWrapping.Wrap
        });

        try
        {
            stack.Children.Add(new HyperlinkButton
            {
                Content = "查看 Pull Request",
                NavigateUri = new Uri(prUrl)
            });
        }
        catch { }

        dialog.Content = stack;
        await dialog.ShowAsync();

        CommunityToolService.InvalidateCache();
        await LoadToolsAsync();
    }

    private async Task ShowDeleteErrorAsync(Exception ex)
    {
        var dialog = new ContentDialog
        {
            Title = "删除失败",
            CloseButtonText = "确定",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        dialog.Resources["ContentDialogMaxWidth"] = 560;
        dialog.Content = new ScrollViewer
        {
            MaxHeight = 300,
            Content = new TextBlock
            {
                Text = ex.InnerException?.Message ?? ex.Message,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                IsTextSelectionEnabled = true
            }
        };
        await dialog.ShowAsync();
    }

    private async Task InstallToolAsync(CommunityTool tool)
    {
        if (string.IsNullOrWhiteSpace(tool.DownloadUrl) && string.IsNullOrWhiteSpace(tool.File))
        {
            StatusBar.Title = "无法下载";
            StatusBar.Message = "该工具没有提供下载源";
            StatusBar.Severity = InfoBarSeverity.Warning;
            StatusBar.IsOpen = true;
            return;
        }

        var authorName = tool.Author ?? "未知用户";
        var versionText = tool.Version ?? "未知";

        var confirmDialog = new ContentDialog
        {
            Title = $"下载 {tool.Name}",
            PrimaryButtonText = $"我信任 {authorName}，开始下载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        confirmDialog.Resources["ContentDialogMaxWidth"] = 480;

        var confirmStack = new StackPanel { Spacing = 12 };

        var infoBorder = new Border
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
                    new TextBlock { Text = tool.Name, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = tool.Description ?? "无描述", FontSize = 13, Opacity = 0.7, TextWrapping = TextWrapping.Wrap }
                }
            }
        };
        confirmStack.Children.Add(infoBorder);
        ((StackPanel)infoBorder.Child).Children.Add(new TextBlock
        {
            Text = $"分类：{tool.Category}  ·  版本：{versionText}  ·  提交者：{authorName}",
            FontSize = 12,
            Opacity = 0.6
        });

        var warningIcon = new FontIcon { Glyph = "\uE7BA", FontSize = 14 };
        warningIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 68, 68));

        var warningText = new TextBlock
        {
            Text = $"社区包无法保证其安全性，图吧工具箱不对社区包负责，但会尽量避免违规工具。如果你信任 {authorName} 可以开始下载。",
            FontSize = 12,
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        var warningStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        warningStack.Children.Add(warningIcon);
        warningStack.Children.Add(warningText);

        var warningBorder = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Color.FromArgb(25, 255, 68, 68)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 68, 68)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = warningStack
        };
        confirmStack.Children.Add(warningBorder);

        confirmDialog.Content = confirmStack;

        var confirmResult = await confirmDialog.ShowAsync();
        if (confirmResult != ContentDialogResult.Primary) return;

        var downloadWindow = new GitHubDownloadWindow(tool);
        downloadWindow.DownloadSucceeded += () =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                tool.InstallStatus = CommunityToolInstallStatus.Installed;
                tool.LocalPath = CommunityToolService.GetLocalPath(tool);

                CommunityToolService.InvalidateCache();
            });
        };
        downloadWindow.Activate();
    }

    private async void ShowToolDetailAsync(CommunityTool tool)
    {
        var dialog = new ContentDialog
        {
            Title = tool.Name,
            CloseButtonText = "关闭",
            PrimaryButtonText = tool.InstallStatus == CommunityToolInstallStatus.Installed ? "打开" : (tool.CanInstall ? tool.LaunchButtonText : "关闭"),
            SecondaryButtonText = tool.CanDelete ? "删除" : null,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        dialog.Resources["ContentDialogMaxWidth"] = 500;

        var stack = new StackPanel { Spacing = 12 };

        if (!string.IsNullOrWhiteSpace(tool.Description))
        {
            stack.Children.Add(new TextBlock { Text = tool.Description, TextWrapping = TextWrapping.Wrap });
        }

        var infoGrid = new Grid
        {
            RowSpacing = 6,
            ColumnSpacing = 12
        };
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rows = new (string Label, string Value)[]
        {
            ("分类", tool.Category),
            ("版本", tool.Version ?? "未知"),
            ("发布者", tool.Publisher ?? "未知"),
            ("提交者", tool.Author ?? "未知"),
            ("标签", tool.TagsText),
            ("状态", tool.InstallStatusText),
            ("官网", tool.Homepage ?? "无"),
        };

        for (var i = 0; i < rows.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(rows[i].Value) || rows[i].Value == "未知") continue;
            var rowDef = new RowDefinition { Height = GridLength.Auto };
            infoGrid.RowDefinitions.Add(rowDef);

            var label = new TextBlock
            {
                Text = rows[i].Label,
                FontSize = 13,
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(label, i);
            Grid.SetColumn(label, 0);
            infoGrid.Children.Add(label);

            if (rows[i].Label == "官网" && rows[i].Value != "无")
            {
                var link = new HyperlinkButton
                {
                    Content = new TextBlock { Text = rows[i].Value, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 340 },
                    NavigateUri = Uri.TryCreate(rows[i].Value, UriKind.Absolute, out var uri) ? uri : null,
                    Padding = new Thickness(0)
                };
                Grid.SetRow(link, i);
                Grid.SetColumn(link, 1);
                infoGrid.Children.Add(link);
            }
            else
            {
                var val = new TextBlock
                {
                    Text = rows[i].Value,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(val, i);
                Grid.SetColumn(val, 1);
                infoGrid.Children.Add(val);
            }
        }

        stack.Children.Add(infoGrid);

        if (!string.IsNullOrWhiteSpace(tool.Homepage))
        {
            try
            {
                stack.Children.Add(new HyperlinkButton
                {
                    Content = "查看项目主页",
                    NavigateUri = new Uri(tool.Homepage)
                });
            }
            catch { }
        }

        dialog.Content = stack;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            if (tool.InstallStatus == CommunityToolInstallStatus.Installed)
                CommunityToolService.LaunchPlugin(tool);
            else if (tool.CanInstall)
                await InstallToolAsync(tool);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await DeleteToolAsync(tool);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileName(ref OPENFILENAME ofn);

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_NOCHANGEDIR = 0x00000008;

    private async Task ShowSubmitDialogAsync()
    {
        var loggedIn = await GitHubAuthService.EnsureAuthenticatedAsync(XamlRoot);
        if (!loggedIn) return;

        var user = await GitHubAuthService.GetCurrentUserAsync();

        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
            lpstrFilter = "压缩包\0*.zip\0所有文件\0*.*\0\0",
            lpstrFile = new string(new char[1024]),
            nMaxFile = 1024,
            lpstrTitle = "选择工具压缩包",
            Flags = OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR,
            nFilterIndex = 1
        };

        if (!GetOpenFileName(ref ofn)) return;

        var packagePath = ofn.lpstrFile.TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(packagePath)) return;

        var fileInfo = new FileInfo(packagePath);
        if (fileInfo.Length > CommunityToolService.MaxUploadSizeBytes)
        {
            await ShowMessageAsync("文件过大", $"压缩包大小不能超过 {CommunityToolService.MaxUploadSizeBytes / 1024 / 1024} MB。\n当前文件：{FormatSize(fileInfo.Length)}");
            return;
        }

        var executables = CustomToolPackageService.GetExecutables(packagePath);
        if (executables.Count == 0)
        {
            await ShowMessageAsync("未找到可执行文件", "压缩包里需要至少包含一个 .exe 文件。");
            return;
        }

        var primaryComboBox = new ComboBox
        {
            Header = "主程序",
            ItemsSource = executables,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var nameBox = new TextBox
        {
            Header = "工具名称",
            Text = Path.GetFileNameWithoutExtension(executables[0].FileName),
            PlaceholderText = "例如 CPU-Z"
        };

        var categoryComboBox = new ComboBox
        {
            Header = "分类",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var existingCategories = ToolCatalog.GetCategories();
        var standardCategories = new[] { "处理器工具", "显卡工具", "内存工具", "硬盘工具", "显示器工具", "声卡工具", "网卡工具", "外设工具", "综合工具", "系统工具", "游戏工具", "其他工具" };
        var allCategories = existingCategories.Concat(standardCategories).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(c => c).ToList();
        foreach (var cat in allCategories)
            categoryComboBox.Items.Add(cat);
        categoryComboBox.SelectedIndex = 0;

        var descBox = new TextBox
        {
            Header = "简介",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            PlaceholderText = "输入工具用途、特点或注意事项"
        };

        var publisherBox = new TextBox
        {
            Header = "作者/发布者",
            PlaceholderText = "可选"
        };

        var tagsBox = new TextBox
        {
            Header = "标签",
            PlaceholderText = "用逗号分隔，例如 CPU, 跑分, 稳定性测试"
        };

        var launchTargetBox = new TextBox
        {
            Header = "启动目标",
            PlaceholderText = "例如 cpuz.exe（可选，默认使用主程序）"
        };

        var homepageBox = new TextBox
        {
            Header = "官方网站",
            PlaceholderText = "https://...（可选）"
        };

        var versionBox = new TextBox
        {
            Header = "版本号",
            PlaceholderText = "例如 2.09（可选，默认 1.0）"
        };

        string? iconFilePath = null;
        var iconPreview = new Border
        {
            Width = 48,
            Height = 48,
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(8),
            Child = new FontIcon { Glyph = "\uE8B7", FontSize = 24, Opacity = 0.5 }
        };
        var iconText = new TextBlock
        {
            Text = "未选择图标",
            Opacity = 0.6,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        var iconPickButton = new Button
        {
            Content = "选择图标",
            Padding = new Thickness(10, 4, 10, 4),
            CornerRadius = new CornerRadius(6)
        };
        iconPickButton.Click += (s, e) =>
        {
            var iconOfn = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                hwndOwner = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
                lpstrFilter = "图标文件\0*.png;*.ico;*.jpg;*.bmp\0所有文件\0*.*\0\0",
                lpstrFile = new string(new char[1024]),
                nMaxFile = 1024,
                lpstrTitle = "选择工具图标",
                Flags = OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR,
                nFilterIndex = 1
            };
            if (GetOpenFileName(ref iconOfn))
            {
                var picked = iconOfn.lpstrFile.TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(picked) && File.Exists(picked))
                {
                    iconFilePath = picked;
                    iconText.Text = Path.GetFileName(picked);
                    try
                    {
                        iconPreview.Child = new Image
                        {
                            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(picked)),
                            Stretch = Stretch.Uniform
                        };
                    }
                    catch { }
                }
            }
        };

        var variantsList = new ListView
        {
            Header = "多架构文件（可选）",
            ItemsSource = executables,
            SelectionMode = ListViewSelectionMode.Multiple,
            MaxHeight = 150
        };

        var loginInfo = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(6),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uEC61", FontSize = 14, Foreground = new SolidColorBrush(Color.FromArgb(255, 74, 222, 128)) },
                    new TextBlock { Text = $"已登录：{user?.Login ?? "未知"}", FontSize = 13, VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };

        var packageInfo = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(6),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = Path.GetFileName(packagePath), FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = $"{FormatSize(fileInfo.Length)}  ·  {executables.Count} 个可执行文件", FontSize = 12, Opacity = 0.6 }
                }
            }
        };

        var content = new ScrollViewer
        {
            MaxHeight = 620,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    loginInfo,
                    packageInfo,
                    nameBox,
                    categoryComboBox,
                    new TextBlock { Text = "主程序", Opacity = 0.68, FontSize = 12 },
                    primaryComboBox,
                    variantsList,
                    new TextBlock { Text = "工具图标（可选）", Opacity = 0.68, FontSize = 12 },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { iconPreview, iconText, iconPickButton } },
                    descBox,
                    publisherBox,
                    tagsBox,
                    launchTargetBox,
                    homepageBox,
                    versionBox
                }
            }
        };

        var dialog = new ContentDialog
        {
            Title = "提交社区工具",
            Content = content,
            PrimaryButtonText = "预览并提交",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        dialog.Resources["ContentDialogMaxWidth"] = 560;

        var dialogResult = await dialog.ShowAsync();
        if (dialogResult != ContentDialogResult.Primary) return;

        if (primaryComboBox.SelectedItem is not ImportableExecutable primary)
        {
            await ShowMessageAsync("请选择主程序", "需要指定一个 exe 作为打开工具时运行的主程序。");
            return;
        }

        if (string.IsNullOrWhiteSpace(nameBox.Text))
        {
            await ShowMessageAsync("请填写工具名称", "工具名称是必填项。");
            return;
        }

        var category = categoryComboBox.SelectedItem as string ?? "其他工具";
        var launchTarget = string.IsNullOrWhiteSpace(launchTargetBox.Text)
            ? primary.FileName
            : launchTargetBox.Text;

        var selectedVariants = variantsList.SelectedItems
            .OfType<ImportableExecutable>()
            .Select(item => new { item.EntryPath, Arch = GuessArch(item.EntryPath) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Arch))
            .ToList();

        var pluginJson = BuildPluginJson(
            nameBox.Text, descBox.Text, category,
            tagsBox.Text, Path.GetFileName(packagePath),
            launchTarget, publisherBox.Text, homepageBox.Text,
            versionBox.Text, user?.Login ?? "",
            selectedVariants.Select(v => (v.EntryPath, v.Arch)).ToList(),
            iconFilePath is not null ? Path.GetFileName(iconFilePath) : null);

        var previewDialog = new ContentDialog
        {
            Title = "确认提交",
            PrimaryButtonText = "提交",
            CloseButtonText = "返回修改",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        previewDialog.Resources["ContentDialogMaxWidth"] = 560;

        var previewStack = new StackPanel { Spacing = 12 };

        var previewJsonBlock = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new ScrollViewer
            {
                MaxHeight = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Text = pluginJson
                }
            }
        };

        var submitTip = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Color.FromArgb(25, 96, 165, 250)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 96, 165, 250)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE946", FontSize = 14, Foreground = new SolidColorBrush(Color.FromArgb(255, 96, 165, 250)) },
                    new TextBlock
                    {
                        Text = "提交后将创建 Pull Request，审核通过后即可在社区中展示。",
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }
        };

        previewStack.Children.Add(previewJsonBlock);
        previewStack.Children.Add(submitTip);

        previewDialog.Content = previewStack;

        var previewResult = await previewDialog.ShowAsync();
        if (previewResult != ContentDialogResult.Primary) return;

        var submitWindow = new CommunitySubmitWindow(
            nameBox.Text, descBox.Text, category,
            tagsBox.Text, packagePath, launchTarget,
            publisherBox.Text, homepageBox.Text, versionBox.Text,
            iconFilePath);
        submitWindow.SubmitSucceeded += () =>
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                CommunityToolService.InvalidateCache();
                await LoadToolsAsync();
            });
        };
        submitWindow.Activate();
    }

    private static string GuessArch(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Contains("arm64", StringComparison.OrdinalIgnoreCase))
            return "ARM64";
        if (name.Contains("x64", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("64", StringComparison.OrdinalIgnoreCase))
            return "x64";
        if (name.Contains("x86", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("32", StringComparison.OrdinalIgnoreCase))
            return "x86";
        return "";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{(double)bytes / (1L << 30):F2} GB";
        if (bytes >= 1L << 20) return $"{(double)bytes / (1L << 20):F1} MB";
        if (bytes >= 1L << 10) return $"{(double)bytes / (1L << 10):F1} KB";
        return $"{bytes} B";
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "确定",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        await dialog.ShowAsync();
    }

    private static string BuildPluginJson(
        string name, string description, string category, string tags,
        string fileName, string launchTarget,
        string publisher, string homepage, string version, string author,
        List<(string EntryPath, string Arch)> archVariants,
        string? iconFileName = null)
    {
        var toolId = CommunityToolService.GenerateToolId(name);
        var tagList = tags.Split(',', '，', ';', '；')
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        var pluginDto = new CommunityToolPluginDto
        {
            Id = toolId,
            Name = name,
            Version = string.IsNullOrWhiteSpace(version) ? "1.0" : version,
            Description = description,
            Category = category,
            Tags = tagList,
            File = fileName,
            LaunchTarget = launchTarget,
            Author = author,
            SubmittedAt = DateTimeOffset.UtcNow
        };

        if (!string.IsNullOrWhiteSpace(publisher)) pluginDto.Publisher = publisher;
        if (!string.IsNullOrWhiteSpace(homepage)) pluginDto.Homepage = homepage;
        if (!string.IsNullOrWhiteSpace(iconFileName)) pluginDto.Icon = iconFileName;

        if (archVariants.Count > 0)
        {
            pluginDto.ArchVariants = archVariants.Select(v => new CommunityArchVariantDto
            {
                File = v.EntryPath.Replace('\\', '/').TrimStart('/'),
                Arch = v.Arch
            }).ToList();
        }

        return JsonSerializer.Serialize(pluginDto, TubaDefaultIndentedContext.Default.CommunityToolPluginDto);
    }
}
