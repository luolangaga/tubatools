using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

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
        var package = new DataPackage();
        package.SetText(_errorDetail);
        Clipboard.SetContent(package);
        CopyButton.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (CopyButton.Content is StackPanel sp)
        {
            sp.Children.Add(new FontIcon { FontSize = 12, Glyph = "\uE73E" });
            sp.Children.Add(new TextBlock { FontSize = 12, Text = "已复制" });
        }
    }

    private async void ReportButton_Click(object sender, RoutedEventArgs e)
    {
        var body = Uri.EscapeDataString(
            "## 异常信息\n\n```\n" + _errorDetail + "\n```\n\n" +
            "## 环境\n\n- OS: Windows\n- 应用版本: " + GetAppVersion() + "\n");
        var url = $"{RepoIssuesUrl}?title=[Bug]+未处理异常&body={body}";
        await Launcher.LaunchUriAsync(new Uri(url));
    }

    private void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(Environment.ProcessPath!);
        App.MainWindow?.Close();
    }

    private static string GetAppVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v is not null ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
    }
}
