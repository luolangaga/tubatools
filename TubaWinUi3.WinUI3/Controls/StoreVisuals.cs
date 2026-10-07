using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TubaWinUi3.Controls;

/// <summary>
/// 正版软件商店共享视觉辅助：安装按钮内容构建
/// </summary>
public static class StoreVisuals
{
    /// <summary>安装按钮内容：图标 + 文字（可指定前景色，如成功绿）</summary>
    public static UIElement BuildInstallContent(string text, string glyph, Brush? foreground)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (foreground is not null)
            icon.Foreground = foreground;

        panel.Children.Add(icon);
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        });

        return panel;
    }

    /// <summary>解析中按钮内容：转圈 + 文字</summary>
    public static UIElement BuildResolvingContent(string text)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(new ProgressRing
        {
            IsActive = true,
            Width = 14,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center
        });

        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        });

        return panel;
    }
}