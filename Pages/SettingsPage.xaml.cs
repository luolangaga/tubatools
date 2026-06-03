using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using TubaWinUi3;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TubaWinUi3.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _isCheckingUpdate;
    private bool _opacityChanging;
    private bool _compactModeInitializing;
    private bool _fastModeInitializing;
    private bool _rememberWindowInitializing;
    private bool _watermarkInitializing;
    private bool _watermarkTextInitializing;
    private bool _watermarkFontInitializing;
    private bool _defaultPageInitializing;
    private bool _brandLogoInitializing;

    private static readonly (string Tag, string DisplayName)[] DefaultPageOptions =
    [
        ("all", "全部工具"),
        ("favorites", "常用"),
        ("hardware", "硬件信息"),
        ("builtin", "内置工具"),
        ("monitor", "硬件监控"),
    ];

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

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetSaveFileName(ref OPENFILENAME ofn);

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    private const int OFN_OVERWRITEPROMPT = 0x00000002;
    private const int OFN_PATHMUSTEXIST = 0x00000800;

    public SettingsPage()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is not null
            ? $"版本 {version.Major}.{version.Minor}.{version.Build}"
            : "版本 1.0.0";

        LoadSettingsGif();
        LoadGitHubAvatar();
        InitThemeComboBox();
        InitCompactModeToggle();
        InitDefaultPageComboBox();
        InitFastModeToggle();
        InitRememberWindowToggle();
        InitBrandLogoToggle();
        InitWatermarkSettings();
        LoadBackgroundSettings();
    }

    private void LoadSettingsGif()
    {
        try
        {
            var gifPath = Path.Combine(AppContext.BaseDirectory, "Assets", "settings.gif");
            if (File.Exists(gifPath))
            {
                var bitmap = new BitmapImage(new Uri(gifPath)) { AutoPlay = true };
                SettingsGifImage.Source = bitmap;
            }
        }
        catch
        {
        }
    }

    private void LoadBackgroundSettings()
    {
        _opacityChanging = true;
        BgOpacitySlider.Minimum = 5;
        BgOpacitySlider.Maximum = 80;
        BgOpacitySlider.StepFrequency = 5;

        var path = BackgroundService.GetBackgroundPath();
        if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
        {
            ShowBgPreview(path);
        }

        var opacity = BackgroundService.GetBackgroundOpacity();
        BgOpacitySlider.Value = (int)(opacity * 100);
        _opacityChanging = false;
        BgOpacityText.Text = $"{(int)(opacity * 100)}%";
    }

    private void ShowBgPreview(string path)
    {
        try
        {
            BgPreviewImage.Source = new BitmapImage(new Uri(path));
            BgFileNameText.Text = System.IO.Path.GetFileName(path);
            BgPreviewPanel.Visibility = Visibility.Visible;
            BgPreviewBorder.Visibility = Visibility.Visible;
            ClearBgButton.Visibility = Visibility.Visible;
        }
        catch { }
    }

    private void HideBgPreview()
    {
        BgPreviewImage.Source = null;
        BgFileNameText.Text = string.Empty;
        BgPreviewPanel.Visibility = Visibility.Collapsed;
        BgPreviewBorder.Visibility = Visibility.Collapsed;
        ClearBgButton.Visibility = Visibility.Collapsed;
    }

    private async void ImportBgButton_Click(object sender, RoutedEventArgs e)
    {
        var ofn = new OPENFILENAME();
        ofn.lStructSize = Marshal.SizeOf(ofn);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        ofn.hwndOwner = hwnd;
        ofn.lpstrFilter = "图片文件\0*.jpg;*.jpeg;*.png;*.bmp\0所有文件\0*.*\0\0";
        ofn.lpstrFile = new string(new char[260]);
        ofn.nMaxFile = 260;
        ofn.lpstrTitle = "选择背景图片";
        ofn.Flags = OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR;
        ofn.nFilterIndex = 1;

        if (!GetOpenFileName(ref ofn))
            return;

        var sourcePath = ofn.lpstrFile.TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(sourcePath) || !System.IO.File.Exists(sourcePath))
            return;

        try
        {
            var bgDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TubaWinUi3", "Backgrounds");
            System.IO.Directory.CreateDirectory(bgDir);

            var destName = $"bg_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{System.IO.Path.GetExtension(sourcePath)}";
            var destPath = System.IO.Path.Combine(bgDir, destName);
            System.IO.File.Copy(sourcePath, destPath, true);

            BackgroundService.SetBackgroundPath(destPath);
            ShowBgPreview(destPath);
        }
        catch
        {
            BackgroundService.SetBackgroundPath(sourcePath);
            ShowBgPreview(sourcePath);
        }
    }

    private void ClearBgButton_Click(object sender, RoutedEventArgs e)
    {
        BackgroundService.SetBackgroundPath(null);
        HideBgPreview();
    }

    private void BgOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_opacityChanging) return;
        var percent = e.NewValue;
        BackgroundService.SetBackgroundOpacity(percent / 100.0);
        BgOpacityText.Text = $"{(int)percent}%";
    }

    private void LoadGitHubAvatar()
    {
        try
        {
            AuthorAvatar.ProfilePicture = new BitmapImage(new Uri("https://github.com/luolangaga.png"));
        }
        catch
        {
        }
    }

    private void InitThemeComboBox()
    {
        ThemeComboBox.Items.Add("跟随系统");
        ThemeComboBox.Items.Add("浅色");
        ThemeComboBox.Items.Add("深色");
        ThemeComboBox.SelectedIndex = ThemeService.CurrentTheme switch
        {
            AppTheme.Light => 1,
            AppTheme.Dark => 2,
            _ => 0
        };
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var theme = ThemeComboBox.SelectedIndex switch
        {
            1 => AppTheme.Light,
            2 => AppTheme.Dark,
            _ => AppTheme.Default
        };
        ThemeService.SetTheme(theme);
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCheckingUpdate) return;
        _isCheckingUpdate = true;
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查更新...";

        try
        {
            var update = await UpdateService.CheckForUpdateAsync();

            if (update is not null)
            {
                UpdateStatusText.Text = $"发现新版本 v{update.Version}";
                var dialog = new UpdateDialog();
                await dialog.ShowUpdateAsync(update);

                if (dialog.SkipThisVersion)
                {
                    UpdateService.SetSkippedVersion(update.Version);
                    UpdateStatusText.Text = $"已跳过 v{update.Version}";
                }
                else
                {
                    UpdateStatusText.Text = "点击检查是否有新版本";
                }
            }
            else
            {
                UpdateStatusText.Text = "已是最新版本";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"检查失败: {ex.Message}";
        }
        finally
        {
            _isCheckingUpdate = false;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void InitDefaultPageComboBox()
    {
        _defaultPageInitializing = true;
        DefaultPageComboBox.Items.Clear();
        var saved = AppSettings.Get("DefaultPage") ?? "all";

        for (var i = 0; i < DefaultPageOptions.Length; i++)
        {
            DefaultPageComboBox.Items.Add(DefaultPageOptions[i].DisplayName);
            if (DefaultPageOptions[i].Tag == saved)
                DefaultPageComboBox.SelectedIndex = i;
        }

        if (DefaultPageComboBox.SelectedIndex < 0)
            DefaultPageComboBox.SelectedIndex = 0;

        _defaultPageInitializing = false;
    }

    private void DefaultPageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_defaultPageInitializing) return;
        if (DefaultPageComboBox.SelectedIndex >= 0 && DefaultPageComboBox.SelectedIndex < DefaultPageOptions.Length)
            AppSettings.Set("DefaultPage", DefaultPageOptions[DefaultPageComboBox.SelectedIndex].Tag);
    }

    private void CompactModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_compactModeInitializing) return;
        CompactModeService.SetCompactModeEnabled(CompactModeToggle.IsOn);
    }

    private void InitCompactModeToggle()
    {
        _compactModeInitializing = true;
        CompactModeToggle.IsOn = CompactModeService.IsCompactModeEnabled();
        _compactModeInitializing = false;
    }

    private void FastModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_fastModeInitializing) return;
        var enabled = FastModeToggle.IsOn;
        FastModeService.SetFastModeEnabled(enabled);
        if (enabled)
            ContentPanel.Transitions.Clear();
        else
            ContentPanel.Transitions.Add(new RepositionThemeTransition());
    }

    private void InitFastModeToggle()
    {
        _fastModeInitializing = true;
        FastModeToggle.IsOn = FastModeService.IsFastModeEnabled();
        if (FastModeToggle.IsOn)
            ContentPanel.Transitions.Clear();
        _fastModeInitializing = false;
    }

    private void RememberWindowToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_rememberWindowInitializing) return;
        WindowSizeService.SetRememberEnabled(RememberWindowToggle.IsOn);
    }

    private void InitRememberWindowToggle()
    {
        _rememberWindowInitializing = true;
        RememberWindowToggle.IsOn = WindowSizeService.IsRememberEnabled();
        _rememberWindowInitializing = false;
    }

    private void BrandLogoToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_brandLogoInitializing) return;
        AppSettings.Set("ShowBrandLogo", BrandLogoToggle.IsOn);
    }

    private void InitBrandLogoToggle()
    {
        _brandLogoInitializing = true;
        BrandLogoToggle.IsOn = AppSettings.GetBool("ShowBrandLogo", true);
        _brandLogoInitializing = false;
    }

    private void InitWatermarkSettings()
    {
        _watermarkInitializing = true;
        var watermarkOn = AppSettings.GetBool("ScreenshotWatermark", true);
        WatermarkToggle.IsOn = watermarkOn;
        _watermarkInitializing = false;

        UpdateWatermarkDetailVisibility(watermarkOn);

        _watermarkTextInitializing = true;
        WatermarkTextBox.Text = AppSettings.Get("ScreenshotWatermarkText") ?? "图吧工具箱";
        _watermarkTextInitializing = false;

        _watermarkFontInitializing = true;
        InitWatermarkFontComboBox();
        _watermarkFontInitializing = false;
    }

    private void InitWatermarkFontComboBox()
    {
        WatermarkFontComboBox.Items.Clear();
        var savedFont = AppSettings.Get("ScreenshotWatermarkFont") ?? "微软雅黑";

        using var fc = new InstalledFontCollection();
        var preferredFonts = new[] { "微软雅黑", "宋体", "黑体", "楷体", "仿宋", "Arial", "Segoe UI" };
        var allFonts = new List<string>();

        foreach (var preferred in preferredFonts)
        {
            if (fc.Families.Any(f => f.Name == preferred) && !allFonts.Contains(preferred))
                allFonts.Add(preferred);
        }

        foreach (var family in fc.Families.OrderBy(f => f.Name))
        {
            if (!allFonts.Contains(family.Name))
                allFonts.Add(family.Name);
        }

        var selectedIndex = 0;
        for (var i = 0; i < allFonts.Count; i++)
        {
            WatermarkFontComboBox.Items.Add(allFonts[i]);
            if (allFonts[i] == savedFont)
                selectedIndex = i;
        }

        WatermarkFontComboBox.SelectedIndex = Math.Min(selectedIndex, allFonts.Count - 1);
    }

    private void UpdateWatermarkDetailVisibility(bool watermarkOn)
    {
        WatermarkDivider.Visibility = watermarkOn ? Visibility.Visible : Visibility.Collapsed;
        WatermarkDetailPanel.Visibility = watermarkOn ? Visibility.Visible : Visibility.Collapsed;
        WatermarkFontPanel.Visibility = watermarkOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void WatermarkToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_watermarkInitializing) return;
        var enabled = WatermarkToggle.IsOn;
        AppSettings.Set("ScreenshotWatermark", enabled);
        UpdateWatermarkDetailVisibility(enabled);
    }

    private void WatermarkTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_watermarkTextInitializing) return;
        var text = WatermarkTextBox.Text.Trim();
        AppSettings.Set("ScreenshotWatermarkText", string.IsNullOrEmpty(text) ? "图吧工具箱" : text);
    }

    private void WatermarkFontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_watermarkFontInitializing) return;
        if (WatermarkFontComboBox.SelectedItem is string font)
            AppSettings.Set("ScreenshotWatermarkFont", font);
    }

    private async void DeleteCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        var categories = ToolCatalog.GetCategories();
        if (categories.Count == 0)
        {
            await ShowMessageAsync("没有分类", "当前没有任何工具分类可以删除。");
            return;
        }

        var categoryComboBox = new ComboBox
        {
            ItemsSource = categories,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "选择要删除的分类：",
                    Opacity = 0.72
                },
                categoryComboBox,
                new Border
                {
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Microsoft.UI.Colors.Transparent),
                    BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Microsoft.UI.ColorHelper.FromArgb(255, 196, 43, 28)),
                    BorderThickness = new Microsoft.UI.Xaml.Thickness(1),
                    CornerRadius = new Microsoft.UI.Xaml.CornerRadius(6),
                    Padding = new Microsoft.UI.Xaml.Thickness(12, 8, 12, 8),
                    Child = new StackPanel
                    {
                        Spacing = 4,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "⚠ 警告",
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                                    Microsoft.UI.ColorHelper.FromArgb(255, 196, 43, 28))
                            },
                            new TextBlock
                            {
                                Text = "删除分类将会同时删除该分类下的所有工具及其文件！此操作不可撤销！",
                                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                                Opacity = 0.88
                            }
                        }
                    }
                }
            }
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "删除工具分类",
            Content = content,
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        if (categoryComboBox.SelectedItem is not string selectedCategory)
            return;

        var toolCount = ToolCatalog.GetTools(selectedCategory).Count;

        var confirmDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"确认删除「{selectedCategory}」",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = toolCount > 0
                            ? $"分类「{selectedCategory}」下有 {toolCount} 个工具，删除后这些工具的文件将全部被移除！"
                            : $"分类「{selectedCategory}」下没有工具。",
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "此操作不可撤销，确定要继续吗？",
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    }
                }
            },
            PrimaryButtonText = "确认删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var confirmResult = await confirmDialog.ShowAsync();
        if (confirmResult != ContentDialogResult.Primary)
            return;

        try
        {
            var categoryDir = Path.Combine(ToolCatalog.ToolsRoot, selectedCategory);
            if (Directory.Exists(categoryDir))
            {
                await Task.Run(() => Directory.Delete(categoryDir, true));
            }

            AppSettings.Remove($"CategoryGlyph_{selectedCategory}");
            ToolCatalog.InvalidateTagsCache();

            if (App.MainWindow is MainWindow mainWindow)
                mainWindow.RefreshToolCategories();

            DeleteCategoryStatusText.Text = $"已删除分类「{selectedCategory}」";
        }
        catch (Exception ex)
        {
            DeleteCategoryStatusText.Text = $"删除失败: {ex.Message}";
            await ShowMessageAsync("删除失败", ex.Message);
        }
    }

    private async void CreateCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            Header = "分类名称",
            PlaceholderText = "例如 我的工具"
        };

        var iconOptions = new (string Label, string Glyph)[]
        {
            ("工具", "\uE8B7"),
            ("处理器", "\uEEA1"),
            ("显卡", "\uF211"),
            ("显示器", "\uE7F4"),
            ("硬盘", "\uEDA2"),
            ("内存", "\uEEA0"),
            ("外设", "\uE962"),
            ("游戏", "\uE7FC"),
            ("烤鸡", "\uE9D9"),
            ("声卡", "\uE7F5"),
            ("网卡", "\uEDA3"),
            ("综合", "\uEC4E"),
            ("其他", "\uE712"),
            ("文件夹", "\uE8B7"),
            ("星标", "\uE734"),
            ("齿轮", "\uE713"),
            ("代码", "\uE943"),
            ("下载", "\uE896"),
            ("上传", "\uE898"),
            ("保存", "\uE74E"),
            ("编辑", "\uE70F"),
            ("搜索", "\uE721"),
            ("终端", "\uE756"),
            ("数据库", "\uEFC6"),
            ("安全", "\uE730"),
            ("网络", "\uE968"),
            ("系统", "\uE977"),
            ("磁盘", "\uEDA2"),
            ("USB", "\uE88E"),
            ("电源", "\uE83E"),
        };

        var iconGridView = new GridView
        {
            ItemsSource = iconOptions.Select(o => new { o.Label, o.Glyph }).ToList(),
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 200,
            Padding = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0)
        };

        iconGridView.ItemTemplate = CreateIconItemTemplate();

        var content = new StackPanel
        {
            Spacing = 12,
            Children = { nameBox, new TextBlock { Text = "选择图标", Opacity = 0.68, FontSize = 12 }, iconGridView }
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "新建工具分类",
            Content = content,
            PrimaryButtonText = "创建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        var categoryName = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            await ShowMessageAsync("名称不能为空", "请输入分类名称。");
            return;
        }

        var selectedIcon = iconGridView.SelectedItem;
        var selectedGlyph = selectedIcon is not null
            ? (string)selectedIcon.GetType().GetProperty("Glyph")!.GetValue(selectedIcon)!
            : "\uE8B7";

        try
        {
            var categoryDir = Path.Combine(ToolCatalog.ToolsRoot, categoryName);
            if (Directory.Exists(categoryDir))
            {
                await ShowMessageAsync("分类已存在", $"分类「{categoryName}」已经存在。");
                return;
            }

            Directory.CreateDirectory(categoryDir);
            AppSettings.Set($"CategoryGlyph_{categoryName}", selectedGlyph);

            if (App.MainWindow is MainWindow mainWindow)
                mainWindow.RefreshToolCategories();

            CreateCategoryStatusText.Text = $"已创建分类「{categoryName}」";
        }
        catch (Exception ex)
        {
            CreateCategoryStatusText.Text = $"创建失败: {ex.Message}";
            await ShowMessageAsync("创建失败", ex.Message);
        }
    }

    private static DataTemplate CreateIconItemTemplate()
    {
        var xaml = """
            <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                <Border Width='48' Height='48' Background='{ThemeResource SubtleFillColorSecondaryBrush}' CornerRadius='8' Padding='8'>
                    <FontIcon FontSize='22' Glyph='{Binding Glyph}' HorizontalAlignment='Center' VerticalAlignment='Center' />
                </Border>
            </DataTemplate>
            """;
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }

    private async void ImportToolButton_Click(object sender, RoutedEventArgs e)
    {
        var packagePath = PickOpenFile("选择工具压缩包", "压缩包\0*.zip\0所有文件\0*.*\0\0");
        if (string.IsNullOrWhiteSpace(packagePath))
            return;

        ImportToolButton.IsEnabled = false;
        ImportToolStatusText.Text = "正在读取压缩包...";

        try
        {
            var executables = CustomToolPackageService.GetExecutables(packagePath);
            if (executables.Count == 0)
            {
                ImportToolStatusText.Text = "压缩包中没有找到 exe 文件";
                await ShowMessageAsync("未找到可导入工具", "压缩包里需要至少包含一个 .exe 文件。");
                return;
            }

            var request = await ShowImportToolDialogAsync(packagePath, executables);
            if (request is null)
            {
                ImportToolStatusText.Text = "已取消导入";
                return;
            }

            ImportToolStatusText.Text = "正在导入工具...";
            var result = await CustomToolPackageService.ImportAsync(request);

            if (App.MainWindow is MainWindow mainWindow)
                mainWindow.RefreshToolCategories();

            ImportToolStatusText.Text = $"已导入 {Path.GetFileName(result.ToolDirectory)}";
            await ShowMessageAsync("导入完成", $"工具已导入到：\n{result.ToolDirectory}");
        }
        catch (Exception ex)
        {
            ImportToolStatusText.Text = $"导入失败: {ex.Message}";
            await ShowMessageAsync("导入失败", ex.Message);
        }
        finally
        {
            ImportToolButton.IsEnabled = true;
        }
    }

    private async void ExportAppButton_Click(object sender, RoutedEventArgs e)
    {
        var exportPath = PickSaveFile("导出当前软件", "压缩包\0*.zip\0所有文件\0*.*\0\0", "TubaWinUi3-Custom.zip", "zip");
        if (string.IsNullOrWhiteSpace(exportPath))
            return;

        if (!exportPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            exportPath += ".zip";

        ExportAppButton.IsEnabled = false;
        ExportAppStatusText.Text = "正在打包当前软件...";

        try
        {
            await CustomToolPackageService.ExportCurrentAppAsync(exportPath);
            ExportAppStatusText.Text = $"已导出 {Path.GetFileName(exportPath)}";
            await ShowMessageAsync("导出完成", $"已保存到：\n{exportPath}");
        }
        catch (Exception ex)
        {
            ExportAppStatusText.Text = $"导出失败: {ex.Message}";
            await ShowMessageAsync("导出失败", ex.Message);
        }
        finally
        {
            ExportAppButton.IsEnabled = true;
        }
    }

    private async Task<CustomToolImportRequest?> ShowImportToolDialogAsync(
        string packagePath,
        IReadOnlyList<ImportableExecutable> executables)
    {
        var primaryComboBox = new ComboBox
        {
            ItemsSource = executables,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var toolNameBox = new TextBox
        {
            Header = "工具名称",
            Text = Path.GetFileNameWithoutExtension(executables[0].FileName),
            PlaceholderText = "例如 CPU-Z"
        };

        var categories = ToolCatalog.GetCategories();
        var categoryBox = new TextBox
        {
            Header = "分类",
            Text = categories.FirstOrDefault() ?? "其他工具",
            PlaceholderText = "例如 处理器工具"
        };

        var categoryComboBox = new ComboBox
        {
            Header = "已有分类",
            ItemsSource = categories,
            SelectedIndex = categories.Count > 0 ? 0 : -1,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        categoryComboBox.SelectionChanged += (_, _) =>
        {
            if (categoryComboBox.SelectedItem is string category)
                categoryBox.Text = category;
        };

        var descriptionBox = new TextBox
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

        var archComboBox = new ComboBox
        {
            Header = "目标架构",
            ItemsSource = new[] { "自动检测", "x64", "x86", "ARM64" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var variantsList = new ListView
        {
            Header = "多架构文件",
            ItemsSource = executables,
            SelectionMode = ListViewSelectionMode.Multiple,
            MaxHeight = 180
        };

        var content = new ScrollViewer
        {
            MaxHeight = 620,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    toolNameBox,
                    categoryComboBox,
                    categoryBox,
                    new TextBlock { Text = "主程序", Opacity = 0.68, FontSize = 12 },
                    primaryComboBox,
                    archComboBox,
                    variantsList,
                    descriptionBox,
                    publisherBox,
                    tagsBox
                }
            }
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "导入自定义工具",
            Content = content,
            PrimaryButtonText = "导入",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return null;

        if (primaryComboBox.SelectedItem is not ImportableExecutable primary)
        {
            await ShowMessageAsync("请选择主程序", "需要指定一个 exe 作为打开工具时运行的主程序。");
            return null;
        }

        var selectedVariants = variantsList.SelectedItems
            .OfType<ImportableExecutable>()
            .Select(item => new ImportArchVariant(item.EntryPath, GuessArch(item.EntryPath)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Arch))
            .ToList();

        var manualArch = archComboBox.SelectedIndex switch
        {
            1 => "x64",
            2 => "x86",
            3 => "ARM64",
            _ => null
        };

        if (manualArch is not null && !selectedVariants.Any(v => v.EntryPath.Equals(primary.EntryPath, StringComparison.OrdinalIgnoreCase)))
        {
            selectedVariants.Add(new ImportArchVariant(primary.EntryPath, manualArch));
        }

        var tags = tagsBox.Text
            .Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new CustomToolImportRequest(
            packagePath,
            toolNameBox.Text,
            categoryBox.Text,
            primary.EntryPath,
            descriptionBox.Text,
            publisherBox.Text,
            tags,
            selectedVariants);
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

    private static string? PickOpenFile(string title, string filter)
    {
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
            lpstrFilter = filter,
            lpstrFile = new string(new char[1024]),
            nMaxFile = 1024,
            lpstrTitle = title,
            Flags = OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR,
            nFilterIndex = 1
        };

        return GetOpenFileName(ref ofn) ? ofn.lpstrFile.TrimEnd('\0') : null;
    }

    private static string? PickSaveFile(string title, string filter, string defaultFileName, string defaultExtension)
    {
        var buffer = defaultFileName + new string('\0', 1024 - defaultFileName.Length);
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
            lpstrFilter = filter,
            lpstrFile = buffer,
            nMaxFile = 1024,
            lpstrTitle = title,
            lpstrDefExt = defaultExtension,
            Flags = OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR,
            nFilterIndex = 1
        };

        return GetSaveFileName(ref ofn) ? ofn.lpstrFile.TrimEnd('\0') : null;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = "确定",
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        await dialog.ShowAsync();
    }

    private void ThrowErrorButton_Click(object sender, RoutedEventArgs e)
    {
        throw new InvalidOperationException("这是一条手动抛出的测试异常，用于验证全局错误页面是否正常工作。");
    }

    private void OpenSourceButton_Click(object sender, RoutedEventArgs e)
    {
        DrawerOverlay.Visibility = Visibility.Visible;
        if (FastModeService.IsFastModeEnabled())
        {
            DrawerOverlayBackground.Opacity = 1;
            DrawerPanelTransform.X = 0;
        }
        else
        {
            DrawerOpenStoryboard.Begin();
        }
    }

    private void DrawerCloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseDrawer();
    }

    private void DrawerOverlayBackground_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        CloseDrawer();
    }

    private void CloseDrawer()
    {
        if (FastModeService.IsFastModeEnabled())
        {
            DrawerOverlay.Visibility = Visibility.Collapsed;
            DrawerOverlayBackground.Opacity = 0;
            DrawerPanelTransform.X = 420;
            return;
        }
        DrawerCloseStoryboard.Completed += OnDrawerCloseCompleted;
        DrawerCloseStoryboard.Begin();
    }

    private void OnDrawerCloseCompleted(object? sender, object e)
    {
        DrawerCloseStoryboard.Completed -= OnDrawerCloseCompleted;
        DrawerOverlay.Visibility = Visibility.Collapsed;
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.Caption = "拖放 ZIP 文件以导入工具";
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var items = await e.DataView.GetStorageItemsAsync();
        var zipFile = items
            .OfType<Windows.Storage.StorageFile>()
            .FirstOrDefault(f => f.FileType.Equals(".zip", StringComparison.OrdinalIgnoreCase));

        if (zipFile is null)
            return;

        ImportToolButton.IsEnabled = false;
        ImportToolStatusText.Text = "正在读取拖放的压缩包...";

        try
        {
            var executables = CustomToolPackageService.GetExecutables(zipFile.Path);
            if (executables.Count == 0)
            {
                ImportToolStatusText.Text = "压缩包中没有找到 exe 文件";
                await ShowMessageAsync("未找到可导入工具", "压缩包里需要至少包含一个 .exe 文件。");
                return;
            }

            var request = await ShowImportToolDialogAsync(zipFile.Path, executables);
            if (request is null)
            {
                ImportToolStatusText.Text = "已取消导入";
                return;
            }

            ImportToolStatusText.Text = "正在导入工具...";
            var result = await CustomToolPackageService.ImportAsync(request);

            if (App.MainWindow is MainWindow mainWindow)
                mainWindow.RefreshToolCategories();

            ImportToolStatusText.Text = $"已导入 {Path.GetFileName(result.ToolDirectory)}";
            await ShowMessageAsync("导入完成", $"工具已导入到：\n{result.ToolDirectory}");
        }
        catch (Exception ex)
        {
            ImportToolStatusText.Text = $"导入失败: {ex.Message}";
            await ShowMessageAsync("导入失败", ex.Message);
        }
        finally
        {
            ImportToolButton.IsEnabled = true;
        }
    }
}
