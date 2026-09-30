using System.Diagnostics;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class ErrorPage : Page
{
    private const string RepoIssuesUrl = "https://github.com/luolangaga/tubatool/issues/new";
    private string _errorDetail = "";

    public ErrorPage()
    {
        InitializeComponent();

        LoadErrorGif();

        var ex = App.ConsumePendingException();
        if (ex is not null)
            SetError(ex);
    }

    private void LoadErrorGif()
    {
        try
        {
            var gifPath = Path.Combine(AppContext.BaseDirectory, "Assets", "error.gif");
            if (File.Exists(gifPath))
            {
                var bitmap = new BitmapImage(new Uri(gifPath)) { AutoPlay = true };
                ErrorGifImage.Source = bitmap;
            }
        }
        catch
        {
        }
    }

    public void SetError(Exception ex)
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

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        // 剪贴板写入失败不得让错误上报页本身崩溃（详见 ClipboardService）
        var result = ClipboardService.TrySetText(_errorDetail);
        CopyButton.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (CopyButton.Content is StackPanel sp)
        {
            if (result.Success)
            {
                sp.Children.Add(new FontIcon { FontSize = 12, Glyph = "\uE73E" });
                sp.Children.Add(new TextBlock { FontSize = 12, Text = "已复制" });
            }
            else
            {
                sp.Children.Add(new TextBlock { FontSize = 12, Text = "复制失败，请重试" });
            }
        }
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
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme,
            };
            await dialog.ShowAsync();
            ReproStepsBox.Focus(FocusState.Programmatic);
            return;
        }

        ReproStepsBox.Header = null;

        var body = Uri.EscapeDataString(
            "## 复现步骤\n\n" + reproSteps + "\n\n" +
            "## 异常信息\n\n```\n" + _errorDetail + "\n```\n\n" +
            "## 环境\n\n- OS: Windows\n- 应用版本: " + GetAppVersion() + "\n");
        var url = $"{RepoIssuesUrl}?title=[Bug]+未处理异常&body={body}";
        await Launcher.LaunchUriAsync(new Uri(url));
    }

    private void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(Environment.ProcessPath!);
        // 「重开」= 新实例接管，旧实例必须真的退出（App.RequestExit：「关闭时最小化到
        // 系统托盘」会把直接关窗口解读成隐藏，留下两个实例）
        App.RequestExit();
    }

    private static string GetAppVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v is not null ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
    }
}
