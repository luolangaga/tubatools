using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using TubaWinUi3.Services;
using Windows.Foundation;
using Windows.UI;

namespace TubaWinUi3.Controls;

/// <summary>天梯墙上的一个型号条目。</summary>
internal sealed record LadderItem(int Position, string Name, string Brand, long Metric, bool IsLocal);

/// <summary>
/// 天梯墙：型号按性能降序从左到右、从上到下密排，左侧是一条跟随内容的相对刻度轴。
/// 整面墙共用一把尺子，任何型号的高低都能和榜首直接对照，比逐行表格更接近传统天梯图的读法。
/// </summary>
internal static class RankingLadder
{
    internal static FrameworkElement Build(
        IReadOnlyList<LadderItem> items,
        long max,
        int columns,
        IReadOnlyList<string> brands,
        RankingPalette palette,
        Func<string, Color> brandColor,
        Action<LadderItem> activate)
    {
        columns = Math.Max(1, columns);
        var height = RankingLadderLayout.HeightFor(items.Count, columns);
        var ticks = RankingLadderLayout.BuildTicks(items.Select(i => i.Metric).ToList(), max, columns);

        var wall = new Grid { Height = height };
        wall.Children.Add(BuildGridlines(ticks, height, palette));

        var rows = new StackPanel { Spacing = 0 };
        var rowCount = (items.Count + columns - 1) / columns;
        for (var r = 0; r < rowCount; r++)
            rows.Children.Add(BuildRow(items, r, columns, max, palette, brandColor, activate));
        wall.Children.Add(rows);

        var body = new Grid { ColumnSpacing = 6 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RankingLadderLayout.RailWidth) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rail = BuildRail(ticks, height, items.Count, columns, palette);
        body.Children.Add(rail);
        Grid.SetColumn(rail, 0);
        body.Children.Add(wall);
        Grid.SetColumn(wall, 1);

        var outer = new StackPanel { Spacing = 0 };
        outer.Children.Add(BuildLegend(brands, palette, brandColor));
        outer.Children.Add(body);
        return outer;
    }

    /// <summary>刻度对应的横向参考线，画在型号条下面一层。</summary>
    private static Grid BuildGridlines(IReadOnlyList<LadderTick> ticks, double height, RankingPalette palette)
    {
        var layer = new Grid { Height = height, IsHitTestVisible = false };
        var brush = palette.Alpha(ThemeColors.BorderColor, 130);
        foreach (var tick in ticks)
        {
            layer.Children.Add(new Border
            {
                Height = 1,
                Background = brush,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, tick.Y, 0, 0),
                IsHitTestVisible = false
            });
        }
        return layer;
    }

    private static Grid BuildRow(IReadOnlyList<LadderItem> items, int row, int columns, long max,
        RankingPalette palette, Func<string, Color> brandColor, Action<LadderItem> activate)
    {
        var grid = new Grid { Height = RankingLadderLayout.RowHeight, ColumnSpacing = 10 };
        for (var c = 0; c < columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var c = 0; c < columns; c++)
        {
            var index = row * columns + c;
            if (index >= items.Count) break;

            var chip = BuildChip(items[index], max, palette, brandColor, activate);
            grid.Children.Add(chip);
            Grid.SetColumn(chip, c);
        }
        return grid;
    }

    private static Border BuildChip(LadderItem item, long max, RankingPalette palette,
        Func<string, Color> brandColor, Action<LadderItem> activate)
    {
        var color = brandColor(item.Brand);
        var percent = RankingScale.Percent(item.Metric, max);

        var position = new TextBlock
        {
            Text = item.Position.ToString(),
            FontSize = 10,
            Foreground = palette.DimText,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var dot = new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = palette.Solid(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0)
        };

        var name = new TextBlock
        {
            Text = item.Name,
            FontSize = 12,
            Foreground = palette.PrimaryText,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        var score = new TextBlock
        {
            Text = item.Metric > 0 ? item.Metric.ToString("N0") : "—",
            FontSize = 10,
            Foreground = palette.DimText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };

        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(position); Grid.SetColumn(position, 0);
        grid.Children.Add(dot); Grid.SetColumn(dot, 1);
        grid.Children.Add(name); Grid.SetColumn(name, 2);
        grid.Children.Add(score); Grid.SetColumn(score, 3);

        var chip = new Border
        {
            Height = RankingLadderLayout.RowHeight - 2,
            Padding = new Thickness(4, 0, 8, 0),
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
            Background = item.IsLocal ? palette.Alpha(ThemeColors.AccentBlue, 32) : null,
            Child = grid
        };

        var baseBackground = chip.Background;
        chip.PointerEntered += (_, _) => chip.Background = palette.Subtle;
        chip.PointerExited += (_, _) => chip.Background = baseBackground;
        chip.Tapped += (_, _) => activate(item);

        ToolTipService.SetToolTip(chip, $"{item.Name}\n{item.Brand} · {item.Metric:N0} · 相对榜首 {percent}%");
        AutomationProperties.SetName(chip, $"{item.Name}，第 {item.Position} 名，相对榜首 {percent}%");
        return chip;
    }

    /// <summary>左侧刻度轴：按梯队分段的性能色条（越高越亮）+ 百分比刻度。</summary>
    private static Grid BuildRail(IReadOnlyList<LadderTick> ticks, double height, int count, int columns,
        RankingPalette palette)
    {
        var rail = new Grid
        {
            Width = RankingLadderLayout.RailWidth,
            Height = height,
            IsHitTestVisible = false
        };

        var accent = ThemeColors.AccentBlue;
        var tiers = RankingTiers.Build(count);

        if (tiers.Count == 0)
        {
            rail.Children.Add(new Border
            {
                Width = 3,
                HorizontalAlignment = HorizontalAlignment.Right,
                CornerRadius = new CornerRadius(2),
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0.5, 0),
                    EndPoint = new Point(0.5, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new() { Offset = 0, Color = Color.FromArgb(190, accent.R, accent.G, accent.B) },
                        new() { Offset = 1, Color = Color.FromArgb(34, accent.R, accent.G, accent.B) }
                    }
                }
            });
        }
        else
        {
            // 每个梯队一段色条，透明度自上而下递减：一眼看出“分带”，又不需要额外文字
            var alphas = new byte[] { 205, 172, 142, 112, 78 };
            foreach (var tier in tiers)
            {
                var firstRow = tier.Start / columns;
                var lastRow = tier.End / columns;
                var segmentHeight = (lastRow - firstRow + 1) * RankingLadderLayout.RowHeight - 2;

                rail.Children.Add(new Border
                {
                    Width = 3,
                    CornerRadius = new CornerRadius(2),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, firstRow * RankingLadderLayout.RowHeight + 1, 0, 0),
                    Height = segmentHeight,
                    Background = palette.Alpha(accent, alphas[Math.Clamp(tier.Level - 1, 0, alphas.Length - 1)])
                });
            }
        }

        foreach (var tick in ticks)
        {
            rail.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, Math.Max(0, tick.Y - 7), 10, 0),
                Child = new TextBlock
                {
                    Text = $"{tick.Percent}%",
                    FontSize = 10,
                    Foreground = palette.DimText
                }
            });
        }

        return rail;
    }

    private static FrameworkElement BuildLegend(IReadOnlyList<string> brands, RankingPalette palette,
        Func<string, Color> brandColor)
    {
        var legend = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            Padding = new Thickness(2, 0, 2, 10)
        };

        legend.Children.Add(new TextBlock
        {
            Text = "按性能降序，从左到右 · 刻度 = 相对榜首性能",
            FontSize = 11,
            Foreground = palette.DimText,
            VerticalAlignment = VerticalAlignment.Center
        });

        foreach (var brand in brands)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            item.Children.Add(new Border
            {
                Width = 7,
                Height = 7,
                CornerRadius = new CornerRadius(3.5),
                Background = palette.Solid(brandColor(brand)),
                VerticalAlignment = VerticalAlignment.Center
            });
            item.Children.Add(new TextBlock
            {
                Text = brand,
                FontSize = 11,
                Foreground = palette.DimText,
                VerticalAlignment = VerticalAlignment.Center
            });
            legend.Children.Add(item);
        }

        return legend;
    }
}
