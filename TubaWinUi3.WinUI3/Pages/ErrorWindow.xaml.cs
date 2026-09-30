using System.Diagnostics;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using TubaWinUi3.Services;
using Windows.Graphics;

namespace TubaWinUi3.Pages;

public sealed partial class ErrorWindow : Window
{
    private const string RepoIssuesUrl = "https://github.com/luolangaga/tubatool/issues/new";
    private string _errorDetail = "";
    private string _systemInfo = "";
    private string? _packagedZipPath;
    private static string? _cachedSystemInfo;
    private bool _sysInfoExpanded;

    public ErrorWindow()
    {
        InitializeComponent();

        AppWindow.Title = "图吧工具箱CE - 错误报告";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var screenArea = displayArea.WorkArea;
        var width = (int)(screenArea.Width * 0.55);
        var height = (int)(screenArea.Height * 0.7);
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            (screenArea.Width - width) / 2,
            (screenArea.Height - height) / 2));

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;

        var ex = App.ConsumePendingException();
        if (ex is not null)
            SetError(ex);

        LoadSystemInfo();
    }

    private void SetError(Exception ex)
    {
        _errorDetail = $"异常类型：{ex.GetType().FullName}\n" +
                       $"消息：{ex.Message}\n" +
                       $"堆栈：\n{ex.StackTrace}";

        if (ex.InnerException is not null)
        {
            _errorDetail += $"\n\n内部异常：{ex.InnerException.GetType().FullName}\n" +
                            $"消息：{ex.InnerException.Message}\n" +
                            $"堆栈：\n{ex.InnerException.StackTrace}";
        }

        ErrorText.Text = _errorDetail;
    }

    private void LoadSystemInfo()
    {
        try
        {
            var info = _cachedSystemInfo ??= ErrorReportService.CollectSystemInfo();
            _systemInfo = info;
            SysInfoText.Text = info;

            var firstLine = info.Split('\n')[0];
            SysInfoSummary.Text = firstLine;
        }
        catch
        {
            _systemInfo = "无法收集系统信息";
            SysInfoText.Text = _systemInfo;
            SysInfoSummary.Text = _systemInfo;
        }
    }

    private void SysInfoHeader_Click(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _sysInfoExpanded = !_sysInfoExpanded;
        SysInfoContent.Visibility = _sysInfoExpanded ? Visibility.Visible : Visibility.Collapsed;
        SysInfoChevron.Glyph = _sysInfoExpanded ? "\uE70E" : "\uE70D";
        SysInfoSummary.Visibility = _sysInfoExpanded ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        // 剪贴板写入失败不得让错误上报窗口本身崩溃（详见 ClipboardService）
        var result = ClipboardService.TrySetText(_errorDetail);
        CopyButtonText.Text = result.Success ? "已复制" : "复制失败，请重试";
    }

    private void CopySysInfoButton_Click(object sender, RoutedEventArgs e)
    {
        var result = ClipboardService.TrySetText(_systemInfo);
        CopySysInfoButtonText.Text = result.Success ? "已复制" : "复制失败，请重试";
    }

    private async void ReportButton_Click(object sender, RoutedEventArgs e)
    {
        var reproSteps = ReproStepsBox.Text.Trim();

        if (string.IsNullOrEmpty(reproSteps))
        {
            ReproStepsBox.Header = "⚠️ 复现步骤为必填项";
            var dialog = new ContentDialog
            {
                Title = "请填写复现步骤",
                Content = "提交 Issue 前请描述你遇到此错误时的操作步骤，这能帮助我们快速定位和修复问题。",
                CloseButtonText = "知道了",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme,
            };
            await dialog.ShowAsync();
            ReproStepsBox.Focus(FocusState.Programmatic);
            return;
        }

        ReproStepsBox.Header = null;

        var logSection = _packagedZipPath is null
            ? ""
            : $"> 错误日志压缩包已生成：{Path.GetFileName(_packagedZipPath)}（请在提交前拖入下方附件区上传）\n\n";
        var body = Uri.EscapeDataString(
            "## 复现步骤\n\n" + reproSteps + "\n\n" +
            logSection +
            "## 异常信息\n\n```\n" + _errorDetail + "\n```\n\n" +
            "## 系统信息\n\n```\n" + _systemInfo + "\n```\n");
        var url = $"{RepoIssuesUrl}?title=[Bug]+未处理异常&body={body}";
        await Launcher.LaunchUriAsync(new Uri(url));
    }

    private async void PackageLogsButton_Click(object sender, RoutedEventArgs e)
    {
        PackageLogsButton.IsEnabled = false;
        PackageLogsButtonText.Text = "正在打包…";
        try
        {
            var result = await ErrorReportService.CreateReportAsync(_errorDetail, _systemInfo);
            _packagedZipPath = result.ZipPath;

            var content = $"压缩包位置：\n{result.ZipPath}\n\n" +
                          $"大小：{TempCleanupService.FormatBytes(result.SizeBytes)}\n" +
                          $"Windows 事件日志：{result.EventCount} 条\n" +
                          $"应用日志文件：{result.LogFileCount} 个\n\n" +
                          "请把该压缩包拖入 GitHub Issue 的附件区上传。";
            if (!string.IsNullOrEmpty(result.Warning))
                content += $"\n\n⚠ {result.Warning}";

            var dialog = new ContentDialog
            {
                Title = "错误日志打包完成",
                Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = "打开文件夹",
                CloseButtonText = "关闭",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                ErrorReportService.RevealInExplorer(result.ZipPath);
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "打包失败",
                Content = ex.Message,
                CloseButtonText = "关闭",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme,
            };
            await dialog.ShowAsync();
        }
        finally
        {
            PackageLogsButton.IsEnabled = true;
            PackageLogsButtonText.Text = "打包日志";
        }
    }

    private void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(Environment.ProcessPath!);
        Close();
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
