using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tubatools.MinecraftLauncher.Services;

namespace Tubatools.MinecraftLauncher.Ui;

/// <summary>原生 ContentDialog 封装：错误详情、版本信息与安装失败汇总。</summary>
internal static class McDialogs
{
    public static async Task ShowTextAsync(XamlRoot root, McPalette palette, ElementTheme theme,
        string title, string text, double maxHeight = 380)
    {
        var content = new ScrollViewer
        {
            MaxHeight = maxHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Foreground = palette.Text
            }
        };

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Title = title,
            Content = content,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close
        };

        try { await dialog.ShowAsync(); }
        catch (Exception)
        {
            // 已有对话框打开时忽略。
        }
    }

    public static async Task<bool> ConfirmAsync(XamlRoot root, McPalette palette, ElementTheme theme,
        string title, string message, string primary, string close)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = palette.Text,
                FontSize = 13
            },
            PrimaryButtonText = primary,
            CloseButtonText = close,
            DefaultButton = ContentDialogButton.Primary
        };

        try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
        catch (Exception) { return false; }
    }

    public static async Task ShowVersionAsync(XamlRoot root, McPalette palette, ElementTheme theme, McVersion version)
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 420 };
        panel.Children.Add(McUi.KeyValueRow("实例", version.Id, palette, true));
        panel.Children.Add(McUi.KeyValueRow("游戏版本", "Minecraft " + version.VanillaName, palette));
        panel.Children.Add(McUi.KeyValueRow("加载器", version.Loaders.Length == 0 ? "原版" : version.Loaders, palette));
        panel.Children.Add(McUi.KeyValueRow("版本类型", version.TypeLabel, palette));
        panel.Children.Add(McUi.KeyValueRow("发布时间", McUi.FormatDate(version.ReleaseTime), palette));
        panel.Children.Add(McUi.KeyValueRow("继承自", version.InheritsFrom ?? "无", palette));
        panel.Children.Add(McUi.KeyValueRow("主类", version.MainClass, palette, true));
        panel.Children.Add(McUi.KeyValueRow("资源索引", version.AssetIndexId ?? "无", palette));
        panel.Children.Add(McUi.KeyValueRow("客户端 JAR", version.ClientJarPath, palette, true));
        panel.Children.Add(McUi.KeyValueRow("版本目录", version.Directory, palette, true));

        var open = new Button
        {
            Content = "打开版本目录",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 0)
        };
        open.Click += (_, _) => McLauncherView.OpenFolder(version.Directory);
        panel.Children.Add(open);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Title = "版本设置 · " + version.Id,
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            },
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close
        };

        try { await dialog.ShowAsync(); }
        catch (Exception) { }
    }

    public static async Task ShowFailuresAsync(XamlRoot root, McPalette palette, ElementTheme theme,
        string title, IReadOnlyList<McDownloadFailure> failures)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = "以下文件被跳过，可稍后重新安装补齐：",
            Foreground = palette.Secondary,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap
        });
        foreach (var failure in failures.Take(60))
        {
            var row = new StackPanel { Spacing = 1 };
            row.Children.Add(new TextBlock
            {
                Text = failure.Label,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = palette.Text,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            row.Children.Add(new TextBlock
            {
                Text = failure.Error,
                FontSize = 11.5,
                Foreground = palette.Secondary,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(row);
        }

        if (failures.Count > 60)
            panel.Children.Add(new TextBlock
            {
                Text = $"……另有 {failures.Count - 60} 条失败记录。",
                Foreground = palette.Secondary,
                FontSize = 12
            });

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            },
            CloseButtonText = "知道了",
            DefaultButton = ContentDialogButton.Close
        };

        try { await dialog.ShowAsync(); }
        catch (Exception) { }
    }
}
