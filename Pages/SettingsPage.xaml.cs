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
        InitDriverStatus();
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
}
