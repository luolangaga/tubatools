using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;

namespace Tubatools.MinecraftLauncher.Ui;

/// <summary>现代 Minecraft 风格控件：圆角玻璃卡片、扁平按钮、柔和胶囊标签与 Fluent 图标。</summary>
internal static class McUi
{
    public static TextBlock Text(string text, double size = 13.5, Windows.UI.Text.FontWeight? weight = null,
        Brush? brush = null, bool wrap = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        Foreground = brush,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis
    };

    public static TextBlock Header(string text, McPalette palette, double size = 15) =>
        Text(text, size, FontWeights.SemiBold, palette.Text);

    public static Grid Card(McPalette palette, UIElement content, string? title = null,
        FrameworkElement? action = null, Thickness? padding = null, double radius = 4, string? icon = null)
    {
        var panel = new StackPanel { Spacing = title is null ? 0 : 12 };
        if (title is not null)
        {
            var header = new Grid { ColumnSpacing = 10 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (!string.IsNullOrWhiteSpace(icon)) label.Children.Add(IconBadge(icon, palette));
            var titleText = Text(title, 14.5, FontWeights.SemiBold, palette.Text);
            titleText.TextTrimming = TextTrimming.CharacterEllipsis;
            label.Children.Add(titleText);
            header.Children.Add(label);
            if (action is not null)
            {
                action.HorizontalAlignment = HorizontalAlignment.Right;
                action.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(action, 1);
                header.Children.Add(action);
            }

            panel.Children.Add(header);
        }

        panel.Children.Add(content);
        return new Grid
        {
            Children =
            {
                new Border
                {
                    Background = palette.Card,
                    BorderBrush = palette.CardStroke,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(radius),
                    Padding = padding ?? new Thickness(18, 16, 18, 18),
                    Child = panel
                }
            }
        };
    }

    public static Button CustomButton(UIElement content, McPalette palette, bool accent = false,
        double height = 44, double minWidth = 0, Thickness? padding = null, double? fillAlpha = null,
        double radius = 4)
    {
        var fill = new Border
        {
            Background = accent ? AccentGradient(palette) : new SolidColorBrush(palette.StoneFill.Color),
            BorderBrush = accent ? new SolidColorBrush(McPalette.Color(0x1F, 0xFF, 0xFF, 0xFF)) : palette.CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(radius),
            Padding = padding ?? new Thickness(16, 0, 16, 0),
            Child = content,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 2)
        };
        if (fillAlpha is { } alpha) fill.Opacity = alpha;
        var depth = new Border
        {
            Background = new SolidColorBrush(McPalette.Color(0x66, 0x00, 0x00, 0x00)),
            CornerRadius = new CornerRadius(radius),
            Margin = new Thickness(0, 3, 0, 0),
            IsHitTestVisible = false
        };
        var surface = new Grid { Children = { depth, fill } };
        var button = new Button
        {
            Content = surface,
            Height = height,
            MinWidth = minWidth,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(radius),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            UseSystemFocusVisuals = false
        };
        button.PointerEntered += (_, _) =>
        {
            if (!button.IsEnabled) return;
            fill.Background = accent ? AccentHoverGradient(palette) : palette.StoneHover;
        };
        button.PointerExited += (_, _) =>
        {
            fill.Background = accent ? AccentGradient(palette) : new SolidColorBrush(palette.StoneFill.Color);
        };
        button.IsEnabledChanged += (_, _) => button.Opacity = button.IsEnabled ? 1 : 0.5;
        return button;
    }

    public static Button Button(string text, McPalette palette, bool accent = false, string? glyph = null,
        double height = 40, double minWidth = 0, string? icon = null, double fontSize = 13.5)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var iconSize = fontSize > 15 ? 20 : 15;
        var resolvedGlyph = !string.IsNullOrWhiteSpace(glyph) ? glyph : GlyphFor(icon);
        if (!string.IsNullOrWhiteSpace(resolvedGlyph))
            stack.Children.Add(new FontIcon
            {
                Glyph = resolvedGlyph,
                FontSize = iconSize,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
            });
        stack.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        });
        return CustomButton(stack, palette, accent, height, minWidth);
    }

    public static Button IconButton(string glyph, string tooltip, McPalette palette, double size = 30)
    {
        var fill = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            CornerRadius = new CornerRadius(4)
        };
        var button = new Button
        {
            Content = new Grid
            {
                Children =
                {
                    fill,
                    new FontIcon
                    {
                        Glyph = glyph,
                        FontSize = 14,
                        Foreground = palette.Secondary,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            },
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.PointerEntered += (_, _) => fill.Background = palette.Subtle;
        button.PointerExited += (_, _) => fill.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        return button;
    }

    /// <summary>保持旧调用名：把像素图标文件映射为现代 Fluent 图标。</summary>
    public static Button PixelIconButton(string iconFile, string tooltip, McPalette palette, double size = 28) =>
        IconButton(GlyphFor(iconFile), tooltip, palette, size);

    public static Border Chip(string text, McPalette palette, Brush? background = null, Brush? foreground = null) =>
        new()
        {
            Background = background ?? palette.Subtle,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Text(text, 11, FontWeights.SemiBold, foreground ?? palette.Secondary)
        };

    public static Border Separator(McPalette palette, double margin = 4) => new()
    {
        Height = 1,
        Background = palette.Separator,
        Margin = new Thickness(0, margin, 0, margin)
    };

    public static Image Icon(string? path, double size, FrameworkElement context)
    {
        var image = new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                image.Source = path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                    ? new SvgImageSource(new Uri(path))
                    : new BitmapImage(new Uri(path));
            }
            catch { }
        }

        return image;
    }

    public static FontIcon GlyphIcon(string glyph, double size, Brush brush) => new()
    {
        Glyph = glyph,
        FontSize = size,
        Foreground = brush,
        VerticalAlignment = VerticalAlignment.Center
    };

    public static Grid KeyValueRow(string label, string value, McPalette palette, bool selectable = false)
    {
        var grid = new Grid { ColumnSpacing = 14, MinHeight = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = Text(label, 12.5, FontWeights.Normal, palette.Tertiary);
        left.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(left);
        var right = Text(value, 13, FontWeights.Normal, palette.Text, true);
        right.VerticalAlignment = VerticalAlignment.Center;
        right.IsTextSelectionEnabled = selectable;
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    public static string FormatDate(DateTime value) =>
        value.Year < 2000 ? "未知" : value.ToString("yyyy-MM-dd HH:mm");

    private static Grid IconBadge(string icon, McPalette palette)
    {
        var glyph = GlyphFor(icon);
        var badge = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(4),
            Background = palette.AccentSoft,
            Child = new FontIcon
            {
                Glyph = glyph,
                FontSize = 14,
                Foreground = palette.Accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var host = new Grid();
        host.Children.Add(badge);
        return host;
    }

    public static string GlyphFor(string? icon) => icon switch
    {
        "icon_play.png" => "\uE768",
        "icon_grass.png" => "\uE8F1",
        "icon_download.png" => "\uE896",
        "icon_settings.png" => "\uE713",
        "icon_user.png" => "\uE77B",
        "icon_folder.png" => "\uE838",
        "icon_refresh.png" => "\uE72C",
        "icon_trash.png" => "\uE74D",
        "icon_copy.png" => "\uE8C8",
        "icon_script.png" => "\uE943",
        "icon_check.png" => "\uE73E",
        "icon_warning.png" => "\uE7BA",
        "icon_java.png" => "\uE943",
        _ => "\uE946"
    };

    private static LinearGradientBrush AccentGradient(McPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop { Color = McPalette.Color(0xFF, 0x66, 0xC2, 0x55), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = palette.AccentDark.Color, Offset = 1 });
        return brush;
    }

    private static LinearGradientBrush AccentHoverGradient(McPalette palette)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop { Color = McPalette.Color(0xFF, 0x7A, 0xD4, 0x68), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = palette.Accent.Color, Offset = 1 });
        return brush;
    }

    public static Grid BevelPanel(McPalette palette, Brush fill, Brush frame, UIElement content, double radius = 4) =>
        new()
        {
            Children =
            {
                new Border
                {
                    Background = frame,
                    CornerRadius = new CornerRadius(radius)
                },
                new Border
                {
                    Background = fill,
                    BorderBrush = palette.CardStroke,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(radius),
                    Child = content
                }
            }
        };
}
