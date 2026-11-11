using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tubatools.Xiaoheihe.Services;

namespace Tubatools.Xiaoheihe;

internal sealed partial class XiaoheiheView
{
    private FrameworkElement BuildPostCard(XhhPost post, Func<Task> action)
    {
        Border card = Card(16);
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bool hasCover = !string.IsNullOrWhiteSpace(post.Cover);
        if (hasCover) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Spacing = 8 };
        var pills = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (post.HasVideo) pills.Children.Add(Pill("视频", accent: true));
        if (!string.IsNullOrWhiteSpace(post.TopicName)) pills.Children.Add(Pill(post.TopicName));
        if (pills.Children.Count > 0) left.Children.Add(pills);
        TextBlock title = T(string.IsNullOrWhiteSpace(post.Title) ? "无标题" : post.Title, 17,
            weight: FontWeights.SemiBold, wrap: true);
        title.MaxLines = 2;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        left.Children.Add(title);

        if (!string.IsNullOrWhiteSpace(post.Summary))
        {
            TextBlock summary = T(post.Summary, 13, secondary: true, wrap: true);
            summary.MaxLines = 2;
            summary.TextTrimming = TextTrimming.CharacterEllipsis;
            left.Children.Add(summary);
        }

        var footer = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 2, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var author = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 520 };
        if (!string.IsNullOrWhiteSpace(post.AuthorName) || !string.IsNullOrWhiteSpace(post.AuthorAvatar))
        {
            author.Children.Add(Avatar(post.AuthorAvatar, 20));
            TextBlock authorName = T(post.AuthorName, 12, secondary: true);
            authorName.MaxWidth = 150;
            authorName.TextTrimming = TextTrimming.CharacterEllipsis;
            author.Children.Add(authorName);
            if (!string.IsNullOrWhiteSpace(post.AuthorLevelText)) author.Children.Add(Pill(post.AuthorLevelText));
        }
        if (!string.IsNullOrWhiteSpace(post.TimeText)) author.Children.Add(T(post.TimeText, 11, tertiary: true));
        if (author.Children.Count > 0) footer.Children.Add(author);

        var stats = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        if (post.CommentCount > 0) stats.Children.Add(Stat("\uE8BD", XhhFormat.Count(post.CommentCount)));
        if (post.LikeCount > 0) stats.Children.Add(Stat("\uE8E1", XhhFormat.Count(post.LikeCount)));
        if (post.CoinCount > 0) stats.Children.Add(Stat("\uE8C7", XhhFormat.Count(post.CoinCount)));
        Grid.SetColumn(stats, 2);
        footer.Children.Add(stats);
        left.Children.Add(footer);

        grid.Children.Add(left);

        if (hasCover)
        {
            FrameworkElement cover = RemoteImage(post.Cover, 156, 104);
            if (post.HasVideo)
            {
                var overlay = new Grid();
                overlay.Children.Add(cover);
                overlay.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Black) { Opacity = 0.55 },
                    CornerRadius = new CornerRadius(99),
                    Padding = new Thickness(8, 3, 8, 3),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new FontIcon { Glyph = "\uE768", FontSize = 16, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) }
                });
                Grid.SetColumn(overlay, 1);
                grid.Children.Add(overlay);
            }
            else
            {
                Grid.SetColumn(cover, 1);
                grid.Children.Add(cover);
            }
        }

        card.Child = grid;
        MakeClickable(card, action);
        return card;
    }

    private FrameworkElement BuildGameCard(XhhGame game, Func<Task> action, double width = 286)
    {
        Border card = Card(0);
        if (width > 0) card.Width = width;
        else card.HorizontalAlignment = HorizontalAlignment.Stretch;
        var panel = new StackPanel { Spacing = 0 };
        FrameworkElement cover;
        if (width > 0)
        {
            cover = RemoteImage(game.Image, width, 132, 11);
            panel.Children.Add(cover);
        }
        else
        {
            cover = new Border
            {
                Height = 132,
                CornerRadius = new CornerRadius(11),
                Background = _palette.Subtle,
                Child = new Image
                {
                    Source = LoadImage(game.Image, 800),
                    Stretch = Stretch.UniformToFill
                }
            };
            panel.Children.Add(cover);
        }

        var body = new StackPanel { Spacing = 7, Padding = new Thickness(14, 12, 14, 14) };
        var titleRow = new Grid { ColumnSpacing = 8 };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock name = T(string.IsNullOrWhiteSpace(game.Name) ? "未知游戏" : game.Name, 15,
            weight: FontWeights.SemiBold, wrap: false);
        name.MaxLines = 1;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        titleRow.Children.Add(name);
        if (!string.IsNullOrWhiteSpace(game.Score))
        {
            Border score = Pill(game.Score, accent: true);
            Grid.SetColumn(score, 1);
            titleRow.Children.Add(score);
        }
        body.Children.Add(titleRow);

        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        meta.Children.Add(T(game.PriceText, 12, accent: game.IsFree));
        if (!string.IsNullOrWhiteSpace(game.Online)) meta.Children.Add(Stat("\uE774", game.Online));
        if (game.FollowCount > 0) meta.Children.Add(Stat("\uE8E1", XhhFormat.Count(game.FollowCount)));
        body.Children.Add(meta);
        if (!string.IsNullOrWhiteSpace(game.ReleaseDate)) body.Children.Add(T(game.ReleaseDate, 11, tertiary: true));

        panel.Children.Add(body);
        card.Child = panel;
        MakeClickable(card, async () =>
        {
            PrepareConnectedAnimation("game-cover", cover);
            await action();
        });
        return card;
    }

    private FrameworkElement BuildUserRow(XhhUser user, Func<Task> action)
    {
        Border card = Card(13);
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(Avatar(user.Avatar, 42));

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        nameRow.Children.Add(T(string.IsNullOrWhiteSpace(user.Name) ? "匿名用户" : user.Name, 14, weight: FontWeights.SemiBold));
        if (!string.IsNullOrWhiteSpace(user.LevelText)) nameRow.Children.Add(Pill(user.LevelText));
        text.Children.Add(nameRow);
        string meta = string.Join(" · ", new[]
        {
            user.FollowCount > 0 ? $"关注 {XhhFormat.Count(user.FollowCount)}" : "",
            user.FanCount > 0 ? $"粉丝 {XhhFormat.Count(user.FanCount)}" : "",
            user.LinkCount > 0 ? $"帖子 {XhhFormat.Count(user.LinkCount)}" : ""
        }.Where(s => s.Length > 0));
        if (meta.Length > 0) text.Children.Add(T(meta, 11, secondary: true));
        if (!string.IsNullOrWhiteSpace(user.Bio)) text.Children.Add(T(XhhFormat.Clip(user.Bio, 80), 12, secondary: true, wrap: true));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var open = Btn("查看主页", "\uE774");
        open.VerticalAlignment = VerticalAlignment.Center;
        open.Click += async (_, _) => await action();
        Grid.SetColumn(open, 2);
        grid.Children.Add(open);
        card.Child = grid;
        return card;
    }

    private FrameworkElement BuildTopicChip(XhhTopic topic, Func<Task> action)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        if (!string.IsNullOrWhiteSpace(topic.Picture))
        {
            content.Children.Add(new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(6),
                Background = _palette.Subtle,
                Child = new Image { Source = LoadImage(topic.Picture, 64), Stretch = Stretch.UniformToFill }
            });
        }
        content.Children.Add(T(topic.Name, 12));
        if (topic.Hot > 0) content.Children.Add(T(XhhFormat.Count(topic.Hot), 10, tertiary: true));
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(10, 5, 10, 5),
            CornerRadius = new CornerRadius(4)
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private FrameworkElement BuildTopicRow(XhhTopic topic, Func<Task> action, bool selected = false)
    {
        Border card = Card(11, 9);
        if (selected)
        {
            card.Background = _palette.Selected;
            card.BorderBrush = _palette.Accent;
        }
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(8),
            Background = _palette.Subtle,
            Child = !string.IsNullOrWhiteSpace(topic.Picture)
                ? new Image { Source = LoadImage(topic.Picture, 80), Stretch = Stretch.UniformToFill }
                : (UIElement)new FontIcon { Glyph = "\uE716", FontSize = 14, Foreground = _palette.SecondaryText }
        });
        var name = T(topic.Name, 13, weight: FontWeights.SemiBold);
        name.MaxLines = 1;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        if (topic.Hot > 0)
        {
            TextBlock hot = T(XhhFormat.Count(topic.Hot), 10, tertiary: true);
            Grid.SetColumn(hot, 2);
            grid.Children.Add(hot);
        }
        card.Child = grid;
        MakeClickable(card, action);
        return card;
    }

    private FrameworkElement BuildImageStrip(IReadOnlyList<string> images, double height = 150)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (string image in images.Take(9))
        {
            string current = image;
            FrameworkElement item = RemoteImage(current, height * 1.35, height, 8);
            item.Tapped += async (_, _) => await RunAsync(() => ShowImageAsync(current));
            panel.Children.Add(item);
        }
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
    }

    private FrameworkElement BuildInfoRow(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 14, Padding = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(T(label, 12, secondary: true));
        TextBlock text = T(value, 12, wrap: true);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }
}
