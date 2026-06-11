using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using TubaWinUi3;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.UI;
using static TubaWinUi3.Services.ConfigManager;

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
    private bool _driverBusy;
    private bool _cpuzBusy;
    private bool _backdropInitializing;

    private Border[] _backdropOptions = [];
    private Border[] _presetColorBorders = [];

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

    private string? _pendingHighlightKey;

    private static readonly Dictionary<string, string> SettingKeyToCardName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Theme"] = "SettingsThemeCard",
        ["CompactMode"] = "SettingsCompactModeCard",
        ["BrandLogo"] = "SettingsBrandLogoCard",
        ["DefaultPage"] = "SettingsDefaultPageCard",
        ["FastMode"] = "SettingsFastModeCard",
        ["Watermark"] = "SettingsWatermarkCard",
        ["RememberWindow"] = "SettingsRememberWindowCard",
        ["Background"] = "SettingsBackgroundCard",
        ["Backdrop"] = "SettingsBackdropCard",
        ["Update"] = "SettingsUpdateCard",
        ["ConfigManager"] = "SettingsConfigManagerCard",
        ["CustomToolManager"] = "SettingsCustomToolCard",
        ["MonitorDriver"] = "SettingsMonitorDriverCard",
        ["ExportApp"] = "SettingsExportAppCard",
    };

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is SearchNavigationTarget target && target.HighlightSettingKey is not null)
        {
            _pendingHighlightKey = target.HighlightSettingKey;
        }
    }

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
        InitBackdropSettings();
        InitDriverStatus();
        InitCpuzDataSourceStatus();

        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= SettingsPage_Loaded;

        if (_pendingHighlightKey is not null)
        {
            _ = HighlightSettingAsync(_pendingHighlightKey);
            _pendingHighlightKey = null;
        }
    }

    private async Task HighlightSettingAsync(string settingKey)
    {
        if (!SettingKeyToCardName.TryGetValue(settingKey, out var cardName)) return;

        await Task.Delay(300);

        var border = FindName(cardName) as Border;
        if (border is null) return;

        border.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = true,
            VerticalAlignmentRatio = 0.5
        });

        await Task.Delay(500);
        SearchHighlightService.HighlightBorder(border);
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
            var bgDir = ConfigManager.GetBackgroundsDir();
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
        // 简洁模式影响硬件信息的拼接分隔符，需令缓存失效
        HardwareInfoService.InvalidateCache();
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

    private void CustomToolManagerButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new CustomToolManagerWindow();
        window.Activate();
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

    private void ConfigManagerButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConfigManagerDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        _ = dialog.ShowAsync();
    }

    private void ThrowErrorButton_Click(object sender, RoutedEventArgs e)
    {
        throw new InvalidOperationException("这是一条手动抛出的测试异常，用于验证全局错误页面是否正常工作。");
    }

    private void InitDriverStatus()
    {
        UpdateDriverStatusUI();
    }

    private void UpdateDriverStatusUI()
    {
        var (installed, version) = LiteMonitorService.GetDriverStatus();
        var ready = LiteMonitorService.IsDriverReady();

        if (ready)
        {
            DriverStatusText.Text = version != null
                ? $"PawnIO 驱动已安装 (v{version})"
                : "PawnIO 驱动已安装";
            DriverInstallButton.Visibility = Visibility.Collapsed;
            DriverDivider.Visibility = Visibility.Visible;
            DriverUninstallPanel.Visibility = Visibility.Visible;
        }
        else if (installed)
        {
            DriverStatusText.Text = version != null
                ? $"PawnIO 驱动版本过低 (v{version})，需要重新安装"
                : "PawnIO 驱动需要更新";
            DriverInstallButton.Visibility = Visibility.Visible;
            DriverDivider.Visibility = Visibility.Collapsed;
            DriverUninstallPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            DriverStatusText.Text = "PawnIO 驱动未安装";
            DriverInstallButton.Visibility = Visibility.Visible;
            DriverDivider.Visibility = Visibility.Collapsed;
            DriverUninstallPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void DriverInstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_driverBusy) return;
        _driverBusy = true;
        DriverInstallButton.IsEnabled = false;
        DriverUninstallButton.IsEnabled = false;

        try
        {
            var ok = await LiteMonitorService.Instance.EnsureDriverAsync(XamlRoot);
            UpdateDriverStatusUI();

            if (!ok)
            {
                DriverStatusText.Text = "驱动安装未成功";
            }
        }
        finally
        {
            _driverBusy = false;
            DriverInstallButton.IsEnabled = true;
            DriverUninstallButton.IsEnabled = true;
        }
    }

    private async void DriverUninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_driverBusy) return;
        _driverBusy = true;
        DriverInstallButton.IsEnabled = false;
        DriverUninstallButton.IsEnabled = false;

        try
        {
            var ok = await LiteMonitorService.UninstallDriverAsync(XamlRoot);
            UpdateDriverStatusUI();

            if (ok)
            {
                await ShowMessageAsync("卸载成功", "PawnIO 驱动已卸载。");
            }
            else
            {
                await ShowMessageAsync("卸载失败", "驱动卸载未成功，请重试。");
            }
        }
        finally
        {
            _driverBusy = false;
            DriverInstallButton.IsEnabled = true;
            DriverUninstallButton.IsEnabled = true;
        }
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

    private void InitCpuzDataSourceStatus()
    {
        UpdateCpuzDataSourceUI();
    }

    private void UpdateCpuzDataSourceUI()
    {
        var useCpuz = AppSettings.GetBool("UseCpuzDataSource", false);
        var cpuzAvailable = CpuzInfoService.FindCpuzExe() != null;

        if (useCpuz && CpuzInfoService.CachedInfo != null)
        {
            CpuzDataSourceStatusText.Text = "当前使用 CPU-Z 数据源（真实硬件读取）";
            CpuzDataSourceButtonText.Text = "切回默认";
            CpuzDataSourceIcon.Glyph = "\uE73E";
        }
        else if (useCpuz)
        {
            CpuzDataSourceStatusText.Text = cpuzAvailable
                ? "CPU-Z 数据源已启用，等待获取数据..."
                : "CPU-Z 数据源已启用，但未找到 CPU-Z";
            CpuzDataSourceButtonText.Text = "切回默认";
            CpuzDataSourceIcon.Glyph = "\uE950;";
        }
        else
        {
            CpuzDataSourceStatusText.Text = cpuzAvailable
                ? "当前使用 WMI 数据源，可切换为 CPU-Z 获取真实信息"
                : "当前使用 WMI 数据源（未找到 CPU-Z 工具）";
            CpuzDataSourceButtonText.Text = "切换";
            CpuzDataSourceIcon.Glyph = "\uE950";
        }
    }

    private async void CpuzDataSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cpuzBusy) return;

        var useCpuz = AppSettings.GetBool("UseCpuzDataSource", false);

        if (useCpuz)
        {
            AppSettings.Set("UseCpuzDataSource", false);
            UpdateCpuzDataSourceUI();
            return;
        }

        var cpuzExe = CpuzInfoService.FindCpuzExe();
        if (cpuzExe == null)
        {
            await ShowMessageAsync("未找到 CPU-Z", "在工具目录中未找到 CPU-Z 可执行文件，无法使用此功能。\n\n请确保 Tools/处理器工具/CPUZ/ 目录下存在 cpuz_x64.exe。");
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "切换硬件信息数据源",
            PrimaryButtonText = "确认切换",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(new TextBlock
        {
            Text = "当前硬件信息通过 WMI（Windows 管理规范）获取，数据来源于厂商在 SMBIOS/DMI 中填写的内容。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85
        });

        var problemBorder = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(40, 255, 185, 0)
                    : Color.FromArgb(30, 200, 130, 0)),
            BorderBrush = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(80, 255, 185, 0)
                    : Color.FromArgb(60, 200, 130, 0)),
            BorderThickness = new Thickness(1)
        };
        problemBorder.Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "⚠ WMI 数据可能被伪造",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 14
                },
                new TextBlock
                {
                    Text = "部分厂商或商家可能通过修改 BIOS/SMBIOS 信息来伪造 CPU 型号、内存品牌、主板型号等，导致 WMI 读取到的信息与实际硬件不符。",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85,
                    FontSize = 13
                }
            }
        };
        stack.Children.Add(problemBorder);

        var solutionBorder = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(40, 0, 200, 100)
                    : Color.FromArgb(25, 0, 160, 80)),
            BorderBrush = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(80, 0, 200, 100)
                    : Color.FromArgb(60, 0, 160, 80)),
            BorderThickness = new Thickness(1)
        };
        solutionBorder.Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "✓ CPU-Z 读取原理",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 14
                },
                new TextBlock
                {
                    Text = "CPU-Z 通过 CPUID 指令直接读取 CPU 硬件寄存器，通过 PCI 枚举直接扫描硬件，通过 SPD 芯片直接读取内存条信息——这些是底层硬件级别的数据，厂商无法通过修改 SMBIOS 来伪造。",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85,
                    FontSize = 13
                }
            }
        };
        stack.Children.Add(solutionBorder);

        var warnBorder = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(40, 100, 150, 255)
                    : Color.FromArgb(25, 60, 120, 255)),
            BorderBrush = new SolidColorBrush(
                ThemeService.CurrentTheme == AppTheme.Dark
                    ? Color.FromArgb(80, 100, 150, 255)
                    : Color.FromArgb(60, 60, 120, 255)),
            BorderThickness = new Thickness(1)
        };
        warnBorder.Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "⏱ 注意事项",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 14
                },
                new TextBlock
                {
                    Text = "• 使用 CPU-Z 获取信息需要约 3~8 秒，期间会短暂启动 CPU-Z 进程\n• 获取完成后会自动关闭 CPU-Z 进程\n• 切换后可在设置中随时切回 WMI 数据源",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85,
                    FontSize = 13
                }
            }
        };
        stack.Children.Add(warnBorder);

        dialog.Content = new ScrollViewer
        {
            MaxHeight = 400,
            Content = stack
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        _cpuzBusy = true;
        CpuzDataSourceButton.IsEnabled = false;
        CpuzDataSourceStatusText.Text = "正在通过 CPU-Z 获取硬件信息，请稍候...";

        try
        {
            var cpuzInfo = await CpuzInfoService.FetchAsync(timeoutMs: 30000);

            if (cpuzInfo != null)
            {
                AppSettings.Set("UseCpuzDataSource", true);
                UpdateCpuzDataSourceUI();
            }
            else
            {
                CpuzInfoService.KillCpuzProcesses();
                await ShowMessageAsync("获取失败", "CPU-Z 未能成功获取硬件信息。\n\n可能原因：\n• CPU-Z 运行超时\n• CPU-Z 被安全软件拦截\n• 当前架构不支持此版本 CPU-Z");
                UpdateCpuzDataSourceUI();
            }
        }
        catch (Exception ex)
        {
            CpuzInfoService.KillCpuzProcesses();
            await ShowMessageAsync("获取失败", $"CPU-Z 获取过程中出现错误：\n{ex.Message}");
            UpdateCpuzDataSourceUI();
        }
        finally
        {
            _cpuzBusy = false;
            CpuzDataSourceButton.IsEnabled = true;
        }
    }

    private void InitBackdropSettings()
    {
        _backdropInitializing = true;

        _backdropOptions = [BackdropMicaOption, BackdropMicaAltOption, BackdropAcrylicOption];
        _presetColorBorders = [PresetColor0, PresetColor1, PresetColor2, PresetColor3, PresetColor4, PresetColor5, PresetColor6, PresetColor7];

        var currentType = BackdropService.GetBackdropType();
        UpdateBackdropOptionSelection(currentType);
        UpdateAcrylicPanelVisibility(currentType);

        UpdateTintColorPreview();

        _backdropInitializing = false;
    }

    private void UpdateBackdropOptionSelection(BackdropType selected)
    {
        foreach (var border in _backdropOptions)
        {
            if (border is null) continue;
            var tag = border.Tag?.ToString();
            var isSelected = tag == selected.ToString();
            border.BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 215))
                : (Brush)App.Current.Resources["SubtleFillColorSecondaryBrush"];
        }
    }

    private void UpdateAcrylicPanelVisibility(BackdropType type)
    {
        var showAcrylic = type == BackdropType.Acrylic;
        BackdropAcrylicDivider.Visibility = showAcrylic ? Visibility.Visible : Visibility.Collapsed;
        BackdropAcrylicPanel.Visibility = showAcrylic ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTintColorPreview()
    {
        var tintColor = BackdropService.GetTintColor();
        if (tintColor.A == 0)
        {
            var isDark = ThemeService.CurrentTheme == AppTheme.Dark ||
                         (ThemeService.CurrentTheme == AppTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
            CustomColorPreview.Background = new SolidColorBrush(isDark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243));
        }
        else
        {
            CustomColorPreview.Background = new SolidColorBrush(tintColor);
        }

        UpdatePresetColorSelection(tintColor);
    }

    private void UpdatePresetColorSelection(Color tintColor)
    {
        var currentHex = tintColor.A == 0 ? "" : $"#{tintColor.A:X2}{tintColor.R:X2}{tintColor.G:X2}{tintColor.B:X2}";
        foreach (var border in _presetColorBorders)
        {
            if (border is null) continue;
            var tag = border.Tag?.ToString();
            var isSelected = string.Equals(tag, currentHex, StringComparison.OrdinalIgnoreCase);
            border.BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromArgb(255, 0, 120, 215))
                : (Brush)App.Current.Resources["CardStrokeColorDefaultBrush"];
        }
    }

    private void BackdropOption_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_backdropInitializing) return;
        if (sender is not Border border) return;
        if (!Enum.TryParse<BackdropType>(border.Tag?.ToString(), out var type)) return;

        BackdropService.SetBackdropType(type);
        UpdateBackdropOptionSelection(type);
        UpdateAcrylicPanelVisibility(type);
    }

    private void BackdropOption_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Opacity = 0.85;
        }
    }

    private void BackdropOption_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Opacity = 1.0;
        }
    }

    private void PresetColor_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not Border border) return;
        var hex = border.Tag?.ToString();
        if (string.IsNullOrEmpty(hex)) return;

        if (TryParseHexColor(hex, out var color))
        {
            BackdropService.SetTintColor(color);
            UpdateTintColorPreview();
        }
    }

    private void PresetColorReset_Tapped(object sender, TappedRoutedEventArgs e)
    {
        BackdropService.SetTintColor(Color.FromArgb(0, 0, 0, 0));
        UpdateTintColorPreview();
    }

    private void PresetColor_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Scale = new System.Numerics.Vector3(1.1f, 1.1f, 1.0f);
        }
    }

    private void PresetColor_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.Scale = new System.Numerics.Vector3(1.0f, 1.0f, 1.0f);
        }
    }

    private async void CustomColorButton_Click(object sender, RoutedEventArgs e)
    {
        var currentColor = BackdropService.GetTintColor();
        var isDark = ThemeService.CurrentTheme == AppTheme.Dark ||
                     (ThemeService.CurrentTheme == AppTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        var colorPicker = new ColorPicker
        {
            IsAlphaEnabled = false,
            Color = currentColor.A == 0
                ? (isDark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243))
                : currentColor,
            IsColorSliderVisible = true,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true,
            MinWidth = 280
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "自定义着色颜色",
            Content = new ScrollViewer
            {
                Content = colorPicker,
                MaxHeight = 400
            },
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var selected = colorPicker.Color;
            BackdropService.SetTintColor(selected);
            UpdateTintColorPreview();
        }
    }

    private void TintOpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
    }

    private static bool TryParseHexColor(string hex, out Color color)
    {
        color = default;
        try
        {
            if (hex.StartsWith('#')) hex = hex[1..];
            if (hex.Length == 8)
            {
                var a = byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber);
                var r = byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber);
                var g = byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber);
                var b = byte.Parse(hex[6..8], System.Globalization.NumberStyles.HexNumber);
                color = Color.FromArgb(a, r, g, b);
                return true;
            }
            if (hex.Length == 6)
            {
                var r = byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber);
                var g = byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber);
                var b = byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber);
                color = Color.FromArgb(255, r, g, b);
                return true;
            }
        }
        catch { }
        return false;
    }
}
