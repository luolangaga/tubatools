using System.Text.Json;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tubatools.Xiaoheihe.Services;

namespace Tubatools.Xiaoheihe;

internal sealed partial class XiaoheiheView
{
    private string _selectedTopicId = "";
    private string _topicSort = "hot-rank";
    private bool _topicLoaded;
    private readonly Dictionary<string, Border> _topicRows = new(StringComparer.Ordinal);

    private async Task ShowCommunityAsync()
    {
        _pageTitle.Text = "社区";
        _pageSubtitle.Text = "按话题浏览小黑盒社区";
        _refreshAction = () => RefreshCommunityAsync();
        BuildCommunityPage();

        if (_topicGroups.Count == 0)
        {
            JsonElement result = await _client.TopicCategoriesAsync(_lifetime.Token);
            _topicGroups.AddRange(XhhParser.ParseTopicGroups(result));
        }

        if (_selectedTopic is null && _topicGroups.SelectMany(g => g.Topics).FirstOrDefault() is { } first)
            _selectedTopic = first;

        RenderTopicGroups();
        UpdateTopicHeader();
        if (_selectedTopic is not null && _topicPosts.Count == 0)
            await LoadTopicFeedAsync(reset: true);
    }

    private void BuildCommunityPage()
    {
        var root = new Grid { ColumnSpacing = 16 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(268) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftCard = Card(0);
        leftCard.VerticalAlignment = VerticalAlignment.Stretch;
        var leftLayout = new Grid();
        leftLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var leftHeader = new StackPanel { Spacing = 3, Padding = new Thickness(16, 15, 16, 10) };
        leftHeader.Children.Add(T("社区", 18, weight: FontWeights.SemiBold));
        leftHeader.Children.Add(T("选择话题进入对应板块", 11, secondary: true));
        leftLayout.Children.Add(leftHeader);

        _topicListSurface = new StackPanel { Spacing = 4, Padding = new Thickness(10, 0, 10, 12) };
        var leftScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _topicListSurface
        };
        Grid.SetRow(leftScroll, 1);
        leftLayout.Children.Add(leftScroll);
        leftCard.Child = leftLayout;
        root.Children.Add(leftCard);

        var right = new StackPanel { Spacing = 12 };
        var topicBar = Card(16);
        var barGrid = new Grid { ColumnSpacing = 12 };
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleStack = new StackPanel { Spacing = 2 };
        _topicTitleText = T("选择一个社区话题", 20, weight: FontWeights.SemiBold);
        titleStack.Children.Add(_topicTitleText);
        titleStack.Children.Add(T("帖子、资讯与玩家讨论", 11, secondary: true));
        barGrid.Children.Add(titleStack);

        _topicSortBox = new ComboBox { Width = 118, VerticalAlignment = VerticalAlignment.Center, SelectedIndex = 0 };
        _topicSortBox.Items.Add(new ComboBoxItem { Content = "热门", Tag = "hot-rank" });
        _topicSortBox.Items.Add(new ComboBoxItem { Content = "最新", Tag = "new" });
        _topicSortBox.Items.Add(new ComboBoxItem { Content = "默认", Tag = "" });
        _topicSortBox.SelectionChanged += async (_, _) =>
        {
            _topicSort = (_topicSortBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "hot-rank";
            await RunAsync(async () => await LoadTopicFeedAsync(reset: true));
        };
        Grid.SetColumn(_topicSortBox, 1);
        barGrid.Children.Add(_topicSortBox);

        var open = IconButton("\uE774", "在浏览器中打开当前社区");
        open.VerticalAlignment = VerticalAlignment.Center;
        open.Click += async (_, _) =>
        {
            if (_selectedTopic is null) return;
            await _host.Services.OpenUriAsync(new Uri($"https://www.xiaoheihe.cn/app/bbs/topic/{_selectedTopic.Id}"));
        };
        Grid.SetColumn(open, 2);
        barGrid.Children.Add(open);
        topicBar.Child = barGrid;
        right.Children.Add(topicBar);

        _topicFeedSurface = new StackPanel { Spacing = 12 };
        right.Children.Add(_topicFeedSurface);
        var rightScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = right
        };
        Grid.SetColumn(rightScroll, 1);
        root.Children.Add(rightScroll);

        ShowPage(new Border { Child = root });
        RenderTopicFeed();
    }

    private void RenderTopicGroups()
    {
        if (_topicListSurface is null) return;
        _topicListSurface.Children.Clear();
        _topicRows.Clear();
        if (_topicGroups.Count == 0)
        {
            _topicListSurface.Children.Add(EmptyState("\uE716", "暂无社区数据", "登录后可看到「为你推荐」与「我的偏好」。"));
            return;
        }

        bool first = true;
        foreach (XhhTopicGroup group in _topicGroups)
        {
            var items = new StackPanel { Spacing = 3, Padding = new Thickness(0, 4, 0, 8) };
            foreach (XhhTopic topic in group.Topics)
            {
                bool selected = topic.Id == _selectedTopicId;
                FrameworkElement row = BuildTopicRow(topic, async () => await SelectTopicAsync(topic), selected);
                items.Children.Add(row);
                if (row is Border border) _topicRows[topic.Id] = border;
            }
            var expander = new Expander
            {
                Header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new FontIcon { Glyph = "\uE716", FontSize = 13, Foreground = _palette.AccentText },
                        new TextBlock { Text = group.Title, FontSize = 12, FontWeight = FontWeights.SemiBold }
                    }
                },
                Content = items,
                IsExpanded = first || group.Topics.Any(t => t.Id == _selectedTopicId),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            _topicListSurface.Children.Add(expander);
            first = false;
        }
    }

    private void UpdateTopicHeader()
    {
        if (_topicTitleText is null) return;
        _topicTitleText.Text = _selectedTopic is null ? "选择一个社区话题" : _selectedTopic.Name;
    }

    private async Task SelectTopicAsync(XhhTopic topic)
    {
        if (_selectedTopicId == topic.Id && _topicPosts.Count > 0) return;
        _selectedTopic = topic;
        _selectedTopicId = topic.Id;
        _topicPosts.Clear();
        _topicOffset = 0;
        _topicHasMore = true;
        _topicLoaded = false;
        UpdateTopicHeader();
        UpdateTopicSelectionVisuals();
        RenderTopicFeed();
        await RunAsync(async () => await LoadTopicFeedAsync(reset: true));
    }

    private void UpdateTopicSelectionVisuals()
    {
        foreach (var (id, row) in _topicRows)
        {
            bool selected = id == _selectedTopicId;
            row.Background = selected ? _palette.Selected : _palette.Card;
            row.BorderBrush = selected ? _palette.Accent : _palette.CardStroke;
            row.BorderThickness = new Thickness(1);
        }
    }

    private void RenderTopicFeed()
    {
        if (_topicFeedSurface is null) return;
        _topicFeedSurface.Children.Clear();
        if (!_topicLoaded)
        {
            _topicFeedSurface.Children.Add(new ProgressRing
            {
                IsActive = true,
                Width = 32,
                Height = 32,
                Margin = new Thickness(0, 40, 0, 40),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }
        if (_topicPosts.Count == 0)
        {
            _topicFeedSurface.Children.Add(EmptyState("\uE7C3", "这个社区还没有帖子", "换一个话题，或稍后刷新。"));
            return;
        }
        foreach (XhhPost post in _topicPosts)
            _topicFeedSurface.Children.Add(BuildPostCard(post, () => OpenPostFromCommunityAsync(post)));
        if (_topicHasMore)
        {
            Button more = Btn("加载更多帖子", "\uE72C");
            more.HorizontalAlignment = HorizontalAlignment.Center;
            more.Click += async (_, _) => await RunAsync(async () => await LoadTopicFeedAsync(reset: false));
            _topicFeedSurface.Children.Add(more);
        }
        else
        {
            TextBlock end = T("已经到底了", 12, tertiary: true);
            end.HorizontalAlignment = HorizontalAlignment.Center;
            _topicFeedSurface.Children.Add(end);
        }
    }

    private async Task LoadTopicFeedAsync(bool reset)
    {
        if (_selectedTopic is null) return;
        if (reset)
        {
            _topicPosts.Clear();
            _topicOffset = 0;
            _topicHasMore = true;
            _topicLoaded = false;
            RenderTopicFeed();
        }
        JsonElement result = await _client.TopicFeedsAsync(_selectedTopic.Id, _topicOffset, 20, _topicSort, _lifetime.Token);
        var posts = XhhParser.ParsePostList(result);
        var known = _topicPosts.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        foreach (XhhPost post in posts)
            if (known.Add(post.Id)) _topicPosts.Add(post);
        _topicOffset += posts.Count;
        _topicHasMore = posts.Count > 0;
        _topicLoaded = true;
        RenderTopicFeed();
    }

    private async Task RefreshCommunityAsync()
    {
        _topicGroups.Clear();
        _selectedTopic = null;
        _selectedTopicId = "";
        _topicPosts.Clear();
        _topicOffset = 0;
        _topicHasMore = true;
        _topicLoaded = false;
        await ShowCommunityAsync();
    }

    private async Task OpenPostFromCommunityAsync(XhhPost post)
    {
        string topicId = _selectedTopicId;
        PushBack(async () =>
        {
            _currentPage = "community";
            RefreshNav();
            BuildCommunityPage();
            RenderTopicGroups();
            UpdateTopicHeader();
            RenderTopicFeed();
            await Task.CompletedTask;
        });
        await OpenPostAsync(post);
    }
}
