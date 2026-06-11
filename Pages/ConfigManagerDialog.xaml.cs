using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class ConfigManagerDialog : ContentDialog
{
    private bool _locationInitializing;

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
        RefreshUI();
    }

    private void RefreshUI()
    {
        _locationInitializing = true;
        var loc = ConfigManager.GetConfigLocation();
        AppDataRadio.IsChecked = loc == ConfigLocation.AppData;
        AppRootRadio.IsChecked = loc == ConfigLocation.AppRoot;
        _locationInitializing = false;

        DataDirText.Text = ConfigManager.GetDataDir();
        DataSizeText.Text = $"占用空间: {ConfigManager.GetDataSize()}";
        StatusText.Text = "";
    }

    private async void LocationRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_locationInitializing) return;

        var radio = sender as RadioButton;
        if (radio?.Tag is not string tag) return;

        var targetLocation = tag == "AppRoot" ? ConfigLocation.AppRoot : ConfigLocation.AppData;
        var currentLocation = ConfigManager.GetConfigLocation();

        if (targetLocation == currentLocation) return;

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
            _locationInitializing = false;
            await ShowAsync();
            return;
        }

        var migrate = result == ContentDialogResult.Primary;

        var success = await Task.Run(() => ConfigManager.MigrateData(targetLocation, migrate));

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
            Windows.ApplicationModel.DataTransfer.DataPackage dp = new();
            dp.SetText(path);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            StatusText.Text = "已复制路径到剪贴板";
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
            App.MainWindow?.Close();
        }
        catch { }
    }
}
