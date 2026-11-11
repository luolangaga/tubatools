using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tubatools.Xiaoheihe.Services;

namespace Tubatools.Xiaoheihe;

internal sealed partial class XiaoheiheView
{
    private StackPanel? _homeFeedPanel;
    private StackPanel? _homeFooter;
    private bool _homeLoaded;

    private void BuildHomePage()
    {
        var surface = Surface();
        _homeSurface = surface;
        surface.Children.Add(BuildHero());
        surface.Children.Add(Section("最新推荐", "来自小黑盒社区的实时内容，点击卡片查看帖子与评论"));
        _homeFeedPanel = new StackPanel { Spacing = 12 };
        surface.Children.Add(_homeFeedPanel);
        _homeFooter = new StackPanel { Spacing = 10, Padding = new Thickness(0, 4, 0, 0) };
        surface.Children.Add(_homeFooter);
        RenderHomePosts();
        ShowPage(Scroll(surface));
    }

    private FrameworkElement BuildHero()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop { Offset = 0, Color = ColorHelper.FromArgb(0xFF, 0x10, 0x3A, 0x3A) });
        brush.GradientStops.Add(new GradientStop { Offset = 0.55, Color = ColorHelper.FromArgb(0xFF, 0x16, 0x2A, 0x4A) });
        brush.GradientStops.Add(new GradientStop { Offset = 1, Color = ColorHelper.FromArgb(0xFF, 0x25, 0x18, 0x3A) });

        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        top.Children.Add(new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(9, 3, 9, 3),
            Child = new TextBlock { Text = "HEYBOX", FontSize = 10, Foreground = new SolidColorBrush(Colors.White) }
        });
        top.Children.Add(new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(0x22, 0x7C, 0xFF, 0xD0)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(9, 3, 9, 3),
            Child = new TextBlock { Text = "今日热议", FontSize = 10, Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x9A, 0xFF, 0xDA)) }
        });
        left.Children.Add(top);
        left.Children.Add(new TextBlock
        {
            Text = "小黑盒社区",
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White)
        });
        left.Children.Add(new TextBlock
        {
            Text = "主机、PC、手游与硬件玩家的聚集地。浏览推荐、进入社区、搜索游戏，像桌面论坛一样阅读帖子与评论。",
            FontSize = 13,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 620
        });
        grid.Children.Add(left);

        var right = new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        right.Children.Add(new FontIcon
        {
            Glyph = "\uE7FC",
            FontSize = 54,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xCC, 0x9A, 0xFF, 0xDA))
        });
        var refresh = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 13 },
                    new TextBlock { Text = "换一批", FontSize = 12 }
                }
            },
            Background = new SolidColorBrush(ColorHelper.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 6, 12, 6)
        };
        refresh.Click += async (_, _) =>
        {
            _homePosts.Clear();
            _homeOffset = 0;
            _homeHasMore = true;
            _homeLoaded = false;
            await RunAsync(async () => await LoadHomeAsync(reset: true));
        };
        right.Children.Add(refresh);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        return new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(24, 22, 24, 22),
            Background = brush,
            Child = grid
        };
    }

    private void RenderHomePosts()
    {
        if (_homeFeedPanel is null || _homeFooter is null) return;
        _homeFeedPanel.Children.Clear();
        _homeFooter.Children.Clear();

        if (!_homeLoaded)
        {
            _homeFeedPanel.Children.Add(new ProgressRing
            {
                IsActive = true,
                Width = 34,
                Height = 34,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 48, 0, 48)
            });
            return;
        }

        if (_homePosts.Count == 0)
        {
            _homeFeedPanel.Children.Add(EmptyState("\uE80F", "暂时没有推荐内容", "点击右上角刷新，或稍后再试。"));
            return;
        }

        foreach (XhhPost post in _homePosts)
            _homeFeedPanel.Children.Add(BuildPostCard(post, () => OpenPostFromHomeAsync(post)));

        if (_homeHasMore)
        {
            Button more = Btn("加载更多推荐", "\uE72C");
            more.HorizontalAlignment = HorizontalAlignment.Center;
            more.Click += async (_, _) => await RunAsync(async () => await LoadHomeAsync(reset: false));
            _homeFooter.Children.Add(more);
        }
        else
        {
            TextBlock end = T("已经看完这批推荐了", 12, tertiary: true);
            end.HorizontalAlignment = HorizontalAlignment.Center;
            _homeFooter.Children.Add(end);
        }
    }

    private async Task LoadHomeAsync(bool reset)
    {
        if (reset)
        {
            _homePosts.Clear();
            _homeOffset = 0;
            _homeHasMore = true;
            _homeLoaded = false;
            RenderHomePosts();
        }

        JsonElement result = await _client.FeedsAsync(_homeOffset, reset ? 1 : 0, _lifetime.Token);
        var posts = XhhParser.ParsePostList(result);
        var known = _homePosts.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        foreach (XhhPost post in posts)
            if (known.Add(post.Id)) _homePosts.Add(post);

        _homeOffset += posts.Count;
        _homeHasMore = posts.Count > 0;
        _homeLoaded = true;
        RenderHomePosts();
    }

    private async Task ReloadHomeAsync()
    {
        _homePosts.Clear();
        _homeOffset = 0;
        _homeHasMore = true;
        _homeLoaded = false;
        RenderHomePosts();
        await LoadHomeAsync(reset: true);
    }

    private async Task OpenPostFromHomeAsync(XhhPost post)
    {
        PushBack(async () =>
        {
            _currentPage = "home";
            RefreshNav();
            BuildHomePage();
            await Task.CompletedTask;
        });
        await OpenPostAsync(post);
    }
}
