using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Controls;

/// <summary>天梯图列宽。表头与数据行必须用同一组值，性能条才能和标题对齐。</summary>
internal static class RankingColumns
{
    public const double Rank = 44;
    public const double Metric = 236;
    public const double Secondary = 92;
}

/// <summary>
/// 天梯图的一次性画刷缓存。
/// ThemeColors 每次取值都要解析主题资源字典，一页 300+ 行、每行十余个属性会重复上千次；
/// 页面按当前主题创建一次 Palette 复用即可（主题切换时重建）。
/// </summary>
internal sealed class RankingPalette
{
    private readonly Dictionary<uint, SolidColorBrush> _cache = new();

    public RankingPalette()
    {
        CardBg = Make(ThemeColors.CardBg);
        Border = Make(ThemeColors.BorderColor);
        PrimaryText = Make(ThemeColors.PrimaryText);
        SecondaryText = Make(ThemeColors.SecondaryText);
        DimText = Make(ThemeColors.DimText);
        Hover = Make(ThemeColors.RowHover);
        HeaderBg = Make(ThemeColors.HeaderBg);
        Subtle = Make(ThemeColors.SubtleBg);
        Accent = Make(ThemeColors.AccentBlue);
    }

    public Brush CardBg { get; }
    public Brush Border { get; }
    public Brush PrimaryText { get; }
    public Brush SecondaryText { get; }
    public Brush DimText { get; }
    public Brush Hover { get; }
    public Brush HeaderBg { get; }
    public Brush Subtle { get; }
    public Brush Accent { get; }

    /// <summary>实色画刷（按颜色缓存）。</summary>
    public Brush Solid(Color color) => Get(color, 255);

    /// <summary>带透明度的实色画刷（按 颜色+alpha 缓存）。</summary>
    public Brush Alpha(Color color, byte alpha) => Get(color, alpha);

    private Brush Get(Color color, byte alpha)
    {
        var key = ((uint)alpha << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        if (_cache.TryGetValue(key, out var brush)) return brush;

        brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        _cache[key] = brush;
        return brush;
    }

    private static SolidColorBrush Make(Color color) => new(color);
}

/// <summary>表头列标题：主标题 + 可选说明（例如“条形 = 相对榜首”）。</summary>
internal sealed class RankingHeaderLabel : StackPanel
{
    private readonly TextBlock _title;

    public RankingHeaderLabel(string text, string? sub, RankingPalette palette)
    {
        Spacing = 2;
        VerticalAlignment = VerticalAlignment.Center;

        _title = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.DimText,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Children.Add(_title);

        if (!string.IsNullOrEmpty(sub))
        {
            Children.Add(new TextBlock
            {
                Text = sub,
                FontSize = 11,
                Foreground = palette.DimText,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }
    }

    public void SetText(string text) => _title.Text = text;
}

/// <summary>两个天梯页共用的行内视觉元素，保证 CPU / GPU 两页观感与语义一致。</summary>
internal static class RankingVisuals
{
    private static readonly byte[] TierStripeAlpha = [255, 215, 180, 145, 115];

    /// <summary>名次徽章：前三名（无筛选时）用奖牌色，其余为弱化数字。</summary>
    public static FrameworkElement CreateRankBadge(int position, Color? medal, RankingPalette palette)
    {
        if (medal is Color color)
        {
            return new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(6),
                Background = palette.Alpha(color, 34),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = position.ToString(),
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = palette.Solid(color),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        return new TextBlock
        {
            Text = position.ToString(),
            FontSize = 12,
            Foreground = palette.DimText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    /// <summary>品牌标识：有矢量 logo 用 logo，否则退回首字母色块。</summary>
    public static FrameworkElement CreateBrandMark(string brand, SvgImageSource? logo, Color color, RankingPalette palette)
    {
        if (logo is not null)
        {
            return new Image
            {
                Source = logo,
                Width = 20,
                Height = 20,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }

        return new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            Background = palette.Alpha(color, 30),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = brand.Length > 0 ? brand[..1] : "?",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = palette.Solid(color),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    /// <summary>
    /// 性能条单元：左侧按“相对榜首比例”填充的条形，右侧是原始分数。
    /// 条形用两段星号列实现，宽度随窗口自动伸缩，无需等布局完成后再算像素宽度。
    /// </summary>
    public static Grid CreateMetricCell(long value, long max, Color accent, RankingPalette palette, string tooltip)
    {
        var fraction = RankingScale.Fraction(value, max);

        var track = new Border
        {
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = palette.Subtle,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (fraction > 0)
        {
            var split = new Grid();
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
            var fill = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = palette.Solid(accent)
            };
            split.Children.Add(fill);
            Grid.SetColumn(fill, 0);
            track.Child = split;
        }

        var valueText = new TextBlock
        {
            Text = value > 0 ? value.ToString("N0") : "—",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.PrimaryText,
            TextAlignment = TextAlignment.Right,
            Width = 72,
            VerticalAlignment = VerticalAlignment.Center
        };

        var cell = new Grid { ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
        cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cell.Children.Add(track);
        Grid.SetColumn(track, 0);
        cell.Children.Add(valueText);
        Grid.SetColumn(valueText, 1);

        ToolTipService.SetToolTip(cell, tooltip);
        AutomationProperties.SetName(cell, tooltip);
        return cell;
    }

    /// <summary>梯队档头：左侧色条 + “第 N 梯队 · 名称（区间）”，右侧是该档数量与分数范围。</summary>
    public static Border CreateTierHeader(RankingTier tier, string rangeText, RankingPalette palette)
    {
        var stripeAlpha = TierStripeAlpha[Math.Clamp(tier.Level - 1, 0, TierStripeAlpha.Length - 1)];
        var stripe = new Border
        {
            Width = 3,
            Height = 18,
            CornerRadius = new CornerRadius(2),
            Background = palette.Alpha(ThemeColors.DimText, stripeAlpha),
            VerticalAlignment = VerticalAlignment.Center
        };

        var title = new TextBlock
        {
            Text = $"第 {tier.Level} 梯队 · {tier.Title}",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.PrimaryText,
            VerticalAlignment = VerticalAlignment.Center
        };

        var hint = new TextBlock
        {
            Text = tier.Hint,
            FontSize = 11,
            Foreground = palette.DimText,
            VerticalAlignment = VerticalAlignment.Center
        };

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        left.Children.Add(stripe);
        left.Children.Add(title);
        left.Children.Add(hint);

        var range = new TextBlock
        {
            Text = rangeText,
            FontSize = 11,
            Foreground = palette.DimText,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(14, 7, 14, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(left);
        Grid.SetColumn(left, 0);
        Grid.SetColumnSpan(left, 2);
        grid.Children.Add(range);
        Grid.SetColumn(range, 2);

        return new Border
        {
            Margin = new Thickness(0, 10, 0, 0),
            Background = palette.Alpha(ThemeColors.SubtleBg, 140),
            BorderBrush = palette.Border,
            BorderThickness = new Thickness(0, 1, 0, 1),
            Child = grid
        };
    }

    /// <summary>空状态：图标 + 说明 + 可选“清除筛选”。</summary>
    public static Border CreateEmptyState(string title, string hint, Action? clear, RankingPalette palette)
    {
        var stack = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(24, 56, 24, 56)
        };

        stack.Children.Add(new FontIcon
        {
            Glyph = "\uE721",
            FontSize = 26,
            Foreground = palette.DimText,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            Foreground = palette.SecondaryText,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        stack.Children.Add(new TextBlock
        {
            Text = hint,
            FontSize = 12,
            Foreground = palette.DimText,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 360,
            TextAlignment = TextAlignment.Center
        });

        if (clear is not null)
        {
            var button = new Button
            {
                Content = "清除筛选",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            };
            button.Click += (_, _) => clear();
            stack.Children.Add(button);
        }

        return new Border { Child = stack };
    }

    /// <summary>型号列里的“本机”标签。</summary>
    public static Border CreateLocalBadge(RankingPalette palette)
    {
        return new Border
        {
            Padding = new Thickness(6, 1, 6, 1),
            CornerRadius = new CornerRadius(4),
            Background = palette.Alpha(ThemeColors.AccentBlue, 32),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "本机",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = palette.Accent
            }
        };
    }

    /// <summary>行的统一外壳：底色、分隔线、悬停反馈与无障碍名称。</summary>
    public static Border CreateRowShell(FrameworkElement content, RankingPalette palette, string automationName)
    {
        var row = new Border
        {
            Padding = new Thickness(14, 9, 14, 9),
            MinHeight = 56,
            Background = palette.CardBg,
            BorderBrush = palette.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = content
        };

        row.PointerEntered += (_, _) => row.Background = palette.Hover;
        row.PointerExited += (_, _) => row.Background = palette.CardBg;
        AutomationProperties.SetName(row, automationName);
        return row;
    }
}
