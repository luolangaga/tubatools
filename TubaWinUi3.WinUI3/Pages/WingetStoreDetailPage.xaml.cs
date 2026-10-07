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
        DescText.Text = pkg.Description ?? "（暂无简介）";
        IdText.Text = pkg.Id;
        CategoryText.Text = string.IsNullOrEmpty(pkg.Category) ? "在线搜索" : pkg.Category;

        InstallButton.Content = StoreVisuals.BuildInstallContent("获取", "\uE896", null);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pkg is not StorePackage pkg) return;
        if (pkg.InstallState is "resolving" or "queued") return;

        InstallButton.IsEnabled = false;
        InstallButton.Content = StoreVisuals.BuildResolvingContent("获取链接...");
        pkg.InstallState = "resolving";

        try
        {
            var progress = new Progress<string>(status => DispatcherQueue.TryEnqueue(() =>
            {
                if (status == "已加入下载队列")
                {
                    InstallButton.IsEnabled = false;
                    InstallButton.Content = StoreVisuals.BuildInstallContent("已加入队列", "\uE73E",
                        new SolidColorBrush(ThemeColors.AccentGreen));
                    pkg.InstallState = "queued";
                }
                else
                {
                    InstallButton.Content = StoreVisuals.BuildResolvingContent(status);
                }
            }));

            var item = await WingetStoreService.InstallPackageAsync(
                pkg.Id, pkg.Name, pkg.Glyph,
                progress,
                CancellationToken.None);

            if (item is not null)
            {
                pkg.InstallState = "queued";
                InstallButton.IsEnabled = false;
                InstallButton.Content = StoreVisuals.BuildInstallContent("已加入队列", "\uE73E",
                    new SolidColorBrush(ThemeColors.AccentGreen));
            }
            else
            {
                pkg.InstallState = "error";
                InstallButton.IsEnabled = true;
                InstallButton.Content = StoreVisuals.BuildInstallContent("重试", "\uE72C", null);
            }
        }
        catch (Exception ex)
        {
            pkg.InstallState = "error";
            InstallButton.IsEnabled = true;
            InstallButton.Content = StoreVisuals.BuildInstallContent("重试", "\uE72C", null);

            var dialog = new ContentDialog
            {
                Title = "安装失败",
                Content = $"无法安装 {pkg.Name}：\n{ex.Message}",
                CloseButtonText = "确定",
                XamlRoot = XamlRoot
            };
            _ = dialog.ShowAsync();
        }
    }
}