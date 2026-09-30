using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class ConfigManagerDialog : ContentDialog
{
    private bool _locationInitializing;
    private string? _pendingCustomPath;

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

    public ConfigManagerDialog()
    {
        InitializeComponent();
        InitPlaceholderList();
        RefreshUI();
    }

    private void InitPlaceholderList()
    {
        var placeholders = PathResolver.GetAvailablePlaceholders();
        var lines = placeholders.Select(p => $"{p} — {PathResolver.GetPlaceholderDescription(p)}  例: {PathResolver.GetPlaceholderExample(p)}");
        PlaceholderListText.Text = string.Join("\n", lines);
    }

    private void RefreshUI()
    {
        _locationInitializing = true;
        var loc = ConfigManager.GetConfigLocation();
        AppDataRadio.IsChecked = loc == ConfigLocation.AppData;
        AppRootRadio.IsChecked = loc == ConfigLocation.AppRoot;
        CustomRadio.IsChecked = loc == ConfigLocation.Custom;

        if (loc == ConfigLocation.Custom)
        {
            CustomPathPanel.Visibility = Visibility.Visible;
            CustomPathTextBox.Text = ConfigManager.GetCustomPath() ?? "";
            UpdateCustomPathPreview();
        }
        else
        {
            CustomPathPanel.Visibility = Visibility.Collapsed;
        }

        _locationInitializing = false;

        DataDirText.Text = ConfigManager.GetDataDir();
        DataSizeText.Text = $"占用空间: {ConfigManager.GetDataSize()}";
        StatusText.Text = "";
    }

    private void CustomPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_locationInitializing) return;
        _pendingCustomPath = CustomPathTextBox.Text.Trim();
        UpdateCustomPathPreview();
    }

    private async void ApplyCustomPathButton_Click(object sender, RoutedEventArgs e)
    {
        var customPath = CustomPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(customPath))
        {
            StatusText.Text = "请输入自定义路径";
            return;
        }

        await PerformLocationSwitch(ConfigLocation.Custom, customPath);
    }

    private void UpdateCustomPathPreview()
    {
        var path = CustomPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            CustomPathPreviewText.Text = "请输入自定义路径";
            return;
        }

        try
        {
            var expanded = PathResolver.ExpandPath(path);
            if (!Path.IsPathRooted(expanded))
                expanded = Path.Combine(ToolCatalog.AppDirectory, expanded);
            CustomPathPreviewText.Text = $"解析后: {expanded}";
        }
        catch (Exception ex)
        {
            CustomPathPreviewText.Text = $"路径无效: {ex.Message}";
        }
    }

    private async void LocationRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_locationInitializing) return;

        var radio = sender as RadioButton;
        if (radio?.Tag is not string tag) return;

        var targetLocation = tag switch
        {
            "Custom" => ConfigLocation.Custom,
            "AppRoot" => ConfigLocation.AppRoot,
            _ => ConfigLocation.AppData
        };

        CustomPathPanel.Visibility = targetLocation == ConfigLocation.Custom
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (targetLocation == ConfigLocation.Custom)
        {
            _pendingCustomPath = CustomPathTextBox.Text.Trim();
            UpdateCustomPathPreview();
            return;
        }

        var currentLocation = ConfigManager.GetConfigLocation();
        if (targetLocation == currentLocation) return;

        await PerformLocationSwitch(targetLocation, null);
    }

    private async Task PerformLocationSwitch(ConfigLocation targetLocation, string? customPath)
    {
        var currentLocation = ConfigManager.GetConfigLocation();

        if (targetLocation == ConfigLocation.Custom && string.IsNullOrWhiteSpace(customPath))
        {
            StatusText.Text = "请输入自定义路径";
            return;
        }

        Hide();

        var confirmDialog = new ContentDialog
        {
            Title = "切换配置目录",
            Content = "切换配置目录需要重启应用才能生效。\n\n是否将现有数据迁移到新目录？\n选择「迁移并切换」将复制配置文件到新目录并删除旧数据；\n选择「仅切换」将从空配置开始。",
            PrimaryButtonText = "迁移并切换",
            SecondaryButtonText = "仅切换",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var result = await confirmDialog.ShowAsync();

        if (result == ContentDialogResult.None)
        {
            _locationInitializing = true;
            AppDataRadio.IsChecked = currentLocation == ConfigLocation.AppData;
            AppRootRadio.IsChecked = currentLocation == ConfigLocation.AppRoot;
            CustomRadio.IsChecked = currentLocation == ConfigLocation.Custom;
            CustomPathPanel.Visibility = currentLocation == ConfigLocation.Custom
                ? Visibility.Visible
                : Visibility.Collapsed;
            _locationInitializing = false;
            await ShowAsync();
            return;
        }

        var migrate = result == ContentDialogResult.Primary;

        var success = await Task.Run(() => ConfigManager.MigrateData(targetLocation, migrate, customPath));

        if (success)
        {
            RefreshUI();
            var restartDialog = new ContentDialog
            {
                Title = "切换成功",
                Content = "配置目录已切换，需要重启应用才能生效。\n点击「立即重启」将自动重新打开应用。",
                PrimaryButtonText = "立即重启",
                CloseButtonText = "稍后手动重启",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            var restartResult = await restartDialog.ShowAsync();
            if (restartResult == ContentDialogResult.Primary)
            {
                RestartApp();
                return;
            }
        }
        else
        {
            _locationInitializing = true;
            AppDataRadio.IsChecked = currentLocation == ConfigLocation.AppData;
            AppRootRadio.IsChecked = currentLocation == ConfigLocation.AppRoot;
            CustomRadio.IsChecked = currentLocation == ConfigLocation.Custom;
            CustomPathPanel.Visibility = currentLocation == ConfigLocation.Custom
                ? Visibility.Visible
                : Visibility.Collapsed;
            _locationInitializing = false;
            StatusText.Text = "切换失败，请检查文件权限或磁盘空间";
        }

        await ShowAsync();
    }

    private void CopyPathButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = ConfigManager.GetDataDir();
            var result = ClipboardService.TrySetText(path);
            StatusText.Text = result.Success ? "已复制路径到剪贴板" : "复制失败，请稍后重试";
        }
        catch { StatusText.Text = "复制失败"; }
    }

    private void OpenDirButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = ConfigManager.GetDataDir();
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch { StatusText.Text = "打开文件夹失败"; }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var defaultName = $"TubaWinUi3_Config_{DateTime.Now:yyyyMMdd}.zip";
        var buffer = defaultName + new string('\0', 1024 - defaultName.Length);
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
            lpstrFilter = "压缩包\0*.zip\0所有文件\0*.*\0\0",
            lpstrFile = buffer,
            nMaxFile = 1024,
            lpstrTitle = "导出配置",
            lpstrDefExt = "zip",
            Flags = OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR,
            nFilterIndex = 1
        };

        if (!GetSaveFileName(ref ofn)) return;

        var exportPath = ofn.lpstrFile.TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(exportPath)) return;
        if (!exportPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            exportPath += ".zip";

        ExportButton.IsEnabled = false;
        ImportButton.IsEnabled = false;
        CleanCacheButton.IsEnabled = false;
        StatusText.Text = "正在导出配置...";

        var success = await ConfigManager.ExportConfigAsync(exportPath);

        if (success)
        {
            StatusText.Text = $"已导出到 {Path.GetFileName(exportPath)}";
        }
        else
        {
            StatusText.Text = "导出失败";
        }

        ExportButton.IsEnabled = true;
        ImportButton.IsEnabled = true;
        CleanCacheButton.IsEnabled = true;
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
            lpstrFilter = "压缩包\0*.zip\0所有文件\0*.*\0\0",
            lpstrFile = new string(new char[1024]),
            nMaxFile = 1024,
            lpstrTitle = "导入配置",
            Flags = OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR,
            nFilterIndex = 1
        };

        if (!GetOpenFileName(ref ofn)) return;

        var zipPath = ofn.lpstrFile.TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath)) return;

        Hide();

        var confirmDialog = new ContentDialog
        {
            Title = "导入配置",
            Content = "导入配置将覆盖当前所有设置，是否继续？",
            PrimaryButtonText = "导入",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            await ShowAsync();
            return;
        }

        var success = await ConfigManager.ImportConfigAsync(zipPath);

        if (success)
        {
            RefreshUI();
            StatusText.Text = "导入成功，部分设置需要重启应用后生效";
        }
        else
        {
            StatusText.Text = "导入失败，请检查文件格式";
        }

        await ShowAsync();
    }

    private async void CleanCacheButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();

        var confirmDialog = new ContentDialog
        {
            Title = "清除图标缓存",
            Content = "确定清除所有图标缓存？下次启动工具时会重新生成。",
            PrimaryButtonText = "清除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        if (await confirmDialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ToolIconService.CleanAllCache();
            RefreshUI();
            StatusText.Text = "图标缓存已清除";
        }

        await ShowAsync();
    }

    private static void RestartApp()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;
            System.Diagnostics.Process.Start(exePath);
            // 重启应用：旧实例必须真的退出（App.RequestExit：「关闭时最小化到系统托盘」
            // 会把直接关窗口解读成隐藏，留下两个实例）
            App.RequestExit();
        }
        catch { }
    }
}
