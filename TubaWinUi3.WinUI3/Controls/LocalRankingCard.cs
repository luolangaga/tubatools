using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace TubaWinUi3.Controls;

internal static class LocalRankingCard
{
    internal static Button Create(string kind, string name, string details, Action locate)
    {
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = $"本机 {kind} · {name}", FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = details, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new FontIcon { Glyph = "\uE977", FontSize = 24 });
        grid.Children.Add(text);
        Grid.SetColumn(text, 1);
        var arrow = new FontIcon { Glyph = "\uE72A", FontSize = 16 };
        grid.Children.Add(arrow);
        Grid.SetColumn(arrow, 2);
        var card = new Button
        {
            Content = grid, Padding = new Thickness(16, 12, 16, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8)
        };
        ToolTipService.SetToolTip(card, "点击定位到天梯中的对应型号");
        AutomationProperties.SetName(card, $"本机 {kind}：{name}，{details}，点击定位排名");
        card.Click += (_, _) => locate();
        return card;
    }
}
