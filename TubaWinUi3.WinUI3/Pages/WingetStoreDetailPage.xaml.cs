using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>
/// 正版软件商店 — 软件详情页（点击商店卡片进入）
/// </summary>
public sealed partial class WingetStoreDetailPage : Page
{
    private StorePackage? _pkg;

    public WingetStoreDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is StorePackage pkg)
            Bind(pkg);
    }

    private void Bind(StorePackage pkg)
    {
        _pkg = pkg;

        NameText.Text = pkg.Name;
        MetaText.Text = string.IsNullOrEmpty(pkg.Category) ? pkg.Id : $"{pkg.Category} · {pkg.Id}";
        DescText.Text = (pkg.Description ?? "（暂无简介）")
            + (string.IsNullOrWhiteSpace(pkg.InstallNotes) ? "" : $"\n\n安装方式\n{pkg.InstallNotes}");
        IdText.Text = pkg.IsDirectDownload ? "官方安装包（Windows x64）" : pkg.CanInstall ? pkg.Id : "WinGet 暂未收录";
        CategoryText.Text = string.IsNullOrEmpty(pkg.Category) ? "在线搜索" : pkg.Category;

        SourceText.Text = pkg.IsDirectDownload
            ? pkg.DownloadUrl
            : pkg.CanInstall
                ? "WinGet 官方源 · 直接下载安装包，校验 SHA-256 后启动安装"
                : pkg.UnavailableReason;
        InstallButton.IsEnabled = pkg.CanInstall && pkg.InstallState is not ("installing" or "queued" or "done");
        InstallButton.Content = pkg.InstallState switch
        {
            "installing" => "正在安装…", "queued" => "已加入队列", "done" => "已安装", "error" => "重试", _ => pkg.ActionLabel
        };

    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pkg is not StorePackage pkg) return;
        if (!pkg.CanInstall || pkg.InstallState is "installing" or "queued" or "done") return;
        InstallButton.IsEnabled = false;
        InstallButton.Content = StoreVisuals.BuildResolvingContent("正在安装…");
        pkg.InstallState = "installing";
        try
        {
            var progress = new Progress<string>(status =>
            {
                pkg.StatusText = status;
                ToolTipService.SetToolTip(InstallButton, status);
                if (pkg.InstallState == "installing")
                    InstallButton.Content = StoreVisuals.BuildResolvingContent(status.StartsWith("等待") ? "等待安装…" : "正在安装…");
            });
            var result = await WingetStoreService.InstallStorePackageAsync(pkg, progress);
            pkg.InstallState = result.Queued ? "queued" : "done";
            pkg.StatusText = result.Message;
            InstallButton.Content = result.Queued ? "已加入队列" : "已安装";
            ToolTipService.SetToolTip(InstallButton, result.Message);
            return;
        }
        catch (Exception ex)
        {
            pkg.InstallState = "error";
            pkg.StatusText = ex.Message;
            InstallButton.IsEnabled = true;
            InstallButton.Content = "重试";
            if (XamlRoot is null) return;
            await new ContentDialog
            {
                Title = $"无法安装 {pkg.Name}", Content = new TextBlock
                {
                    Text = ex.Message, TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true, MaxHeight = 400
                },
                CloseButtonText = "确定", XamlRoot = XamlRoot
            }.ShowAsync();
        }
    }

}
