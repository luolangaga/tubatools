using System.Text.Json;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tubatools.Xiaoheihe.Services;

namespace Tubatools.Xiaoheihe;

internal sealed partial class XiaoheiheView
{
    private bool _searchLoaded;
    private bool _searchWelcomeLoaded;

    private async Task ShowSearchPageAsync()
    {
        _pageTitle.Text = "搜索";
        _pageSubtitle.Text = "搜索帖子、游戏、用户与社区话题";
        _refreshAction = () => string.IsNullOrWhiteSpace(_searchQuery)
            ? LoadSearchWelcomeAsync()
            : LoadSearchResultsAsync(reset: true);

        BuildSearchPage();
        if (string.IsNullOrWhiteSpace(_searchQuery))
        {
            if (!_searchWelcomeLoaded) await LoadSearchWelcomeAsync();
            return;
        }
        if (_searchItems.Count == 0) await LoadSearchResultsAsync(reset: true);
    }

    private void BuildSearchPage()
    {
        var surface = Surface(980);

        var searchCard = Card(16);
        var searchLayout = new StackPanel { Spacing = 10 };
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _searchPageBox = new TextBox
        {
            PlaceholderText = "输入关键词，例如：黑神话、Steam、数码硬件",
            Text = _searchQuery,
            FontSize = 15
        };
        _searchPageBox.KeyDown += async (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            string query = _searchPageBox.Text.Trim();
            if (query.Length == 0) return;
            _searchQuery = query;
            _searchItems.Clear();
            _searchOffset = 0;
            _searchHasMore = true;
            _searchLoaded = false;
            await RunAsync(async () => await LoadSearchResultsAsync(reset: true));
        };
        row.Children.Add(_searchPageBox);

        var typeBox = new ComboBox { Width = 108, VerticalAlignment = VerticalAlignment.Center, SelectedIndex = 0 };
        typeBox.Items.Add(new ComboBoxItem { Content = "综合", Tag = "general" });
        typeBox.Items.Add(new ComboBoxItem { Content = "帖子", Tag = "link" });
        typeBox.Items.Add(new ComboBoxItem { Content = "游戏", Tag = "game" });
        typeBox.Items.Add(new ComboBoxItem { Content = "用户", Tag = "user" });
        typeBox.Items.Add(new ComboBoxItem { Content = "话题", Tag = "topic" });
        typeBox.SelectionChanged += async (_, _) =>
        {
            string type = (typeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "general";
            if (type == _searchType) return;
            _searchType = type;
            _searchItems.Clear();
            _searchOffset = 0;
            _searchHasMore = true;
            _searchLoaded = false;
            if (string.IsNullOrWhiteSpace(_searchQuery)) return;
            await RunAsync(async () => await LoadSearchResultsAsync(reset: true));
        };
        Grid.SetColumn(typeBox, 1);
        row.Children.Add(typeBox);

        var go = AccentBtn("搜索", "\uE721");
        go.VerticalAlignment = VerticalAlignment.Center;
        go.Click += async (_, _) =>
        {
            string query = _searchPageBox.Text.Trim();
            if (query.Length == 0) return;
            _searchQuery = query;
            _searchItems.Clear();
            _searchOffset = 0;
            _searchHasMore = true;
            _searchLoaded = false;
            await RunAsync(async () => await LoadSearchResultsAsync(reset: true));
        };
        Grid.SetColumn(go, 2);
        row.Children.Add(go);
        searchLayout.Children.Add(row);
        searchLayout.Children.Add(T("搜索排序由小黑盒服务端决定；帖子标题中的高亮标签会在本地清理。", 11, tertiary: true, wrap: true));
        searchCard.Child = searchLayout;
        surface.Children.Add(searchCard);

        _hotSearchSurface = new StackPanel { Spacing = 10 };
        surface.Children.Add(_hotSearchSurface);
        _searchSurface = new StackPanel { Spacing = 12 };
        surface.Children.Add(_searchSurface);

        RenderSearchPage();
        ShowPage(Scroll(surface));
    }

    private void RenderSearchPage()
    {
        if (_hotSearchSurface is null || _searchSurface is null) return;
        _hotSearchSurface.Children.Clear();
        _searchSurface.Children.Clear();

        if (string.IsNullOrWhiteSpace(_searchQuery))
        {
            _hotSearchSurface.Children.Add(Section("热门搜索", "点击关键词直接搜索"));
            if (!_searchWelcomeLoaded)
            {
                _hotSearchSurface.Children.Add(new ProgressRing
                {
                    IsActive = true,
                    Width = 28,
                    Height = 28,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 8, 0, 8)
                });
            }
            return;
        }

        _hotSearchSurface.Children.Add(Section("搜索结果", $"关键词：{_searchQuery}"));
        RenderSearchResults();
    }

    private void RenderSearchResults()
    {
        if (_searchSurface is null) return;
        _searchSurface.Children.Clear();
        if (!_searchLoaded)
        {
            _searchSurface.Children.Add(new ProgressRing
            {
                IsActive = true,
                Width = 32,
                Height = 32,
                Margin = new Thickness(0, 40, 0, 40),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }
        if (_searchItems.Count == 0)
        {
            _searchSurface.Children.Add(EmptyState("\uE721", "没有找到相关内容", "换一个关键词，或切换到其他搜索类型。"));
            return;
        }

        foreach (XhhSearchItem item in _searchItems)
        {
            switch (item.Type)
            {
                case "link" when item.Post is { } post:
                    _searchSurface.Children.Add(BuildPostCard(post, () => OpenPostFromSearchAsync(post)));
                    break;
                case "game" when item.Game is { } game:
                    _searchSurface.Children.Add(BuildGameCard(game, () => OpenGameFromSearchAsync(game), 360));
                    break;
                case "user" when item.User is { } user:
                    _searchSurface.Children.Add(BuildUserRow(user, () => OpenUserFromSearchAsync(user)));
                    break;
                case "topic" or "hashtag" when item.Topic is { } topic:
                    _searchSurface.Children.Add(BuildTopicRow(topic, async () => await OpenCommunityTopicAsync(topic)));
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(item.Text))
                        _searchSurface.Children.Add(Card(12).WithChild(T(item.Text, 13, wrap: true)));
                    break;
            }
        }

        if (_searchHasMore)
        {
            Button more = Btn("加载更多结果", "\uE72C");
            more.HorizontalAlignment = HorizontalAlignment.Center;
            more.Click += async (_, _) => await RunAsync(async () => await LoadSearchResultsAsync(reset: false));
            _searchSurface.Children.Add(more);
        }
        else
        {
            TextBlock end = T("没有更多结果了", 12, tertiary: true);
            end.HorizontalAlignment = HorizontalAlignment.Center;
            _searchSurface.Children.Add(end);
        }
    }

    private async Task LoadSearchResultsAsync(bool reset)
    {
        if (string.IsNullOrWhiteSpace(_searchQuery)) return;
        if (reset)
        {
            _searchItems.Clear();
            _searchOffset = 0;
            _searchHasMore = true;
            _searchLoaded = false;
            RenderSearchResults();
        }
        JsonElement result = await _client.SearchAsync(_searchQuery, _searchType, _searchOffset, 20, _lifetime.Token);
        var items = XhhParser.ParseSearchItems(result);
        _searchItems.AddRange(items);
        _searchOffset += items.Count;
        _searchHasMore = items.Count > 0;
        _searchLoaded = true;
        RenderSearchResults();
    }

    private async Task LoadSearchWelcomeAsync()
    {
        _searchWelcomeLoaded = false;
        RenderSearchPage();
        JsonElement result = await _client.SearchWelcomeAsync(_lifetime.Token);
        var keywords = new List<string>();

        void AddKeyword(string value)
        {
            value = value.Trim();
            if (value.Length > 0 && !keywords.Contains(value)) keywords.Add(value);
        }

        JsonElement defaultQ = result.Obj("default_q");
        if (defaultQ.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in defaultQ.EnumerateArray())
                AddKeyword(item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.FirstStr("name", "word", "q"));
        }
        else if (defaultQ.ValueKind == JsonValueKind.String)
        {
            AddKeyword(defaultQ.GetString() ?? "");
        }

        foreach (JsonElement list in result.Arr("Lists"))
        {
            foreach (JsonElement item in list.Arr("list"))
                AddKeyword(item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.FirstStr("name", "word", "q"));
            foreach (JsonElement item in list.Arr("items"))
                AddKeyword(item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : item.FirstStr("name", "word", "q"));
        }

        if (keywords.Count == 0)
            keywords.AddRange(["黑神话：悟空", "Steam", "数码硬件", "英雄联盟", "影之刃零"]);

        if (_hotSearchSurface is not null)
        {
            _hotSearchSurface.Children.Clear();
            _hotSearchSurface.Children.Add(Section("热门搜索", "点击关键词直接搜索"));
            var wrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(0, 0, 0, 8) };
            int count = 0;
            foreach (string keyword in keywords.Take(24))
            {
                Button chip = Btn(keyword, "\uE721");
                chip.Padding = new Thickness(11, 5, 11, 5);
                chip.Click += async (_, _) =>
                {
                    _searchQuery = keyword;
                    _searchItems.Clear();
                    _searchOffset = 0;
                    _searchHasMore = true;
                    _searchLoaded = false;
                    if (_searchPageBox is not null) _searchPageBox.Text = keyword;
                    await RunAsync(async () => await LoadSearchResultsAsync(reset: true));
                };
                wrap.Children.Add(chip);
                if (++count % 6 == 0)
                {
                    _hotSearchSurface.Children.Add(wrap);
                    wrap = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                }
            }
            if (wrap.Children.Count > 0) _hotSearchSurface.Children.Add(wrap);
        }
        _searchWelcomeLoaded = true;
    }

    private async Task OpenPostFromSearchAsync(XhhPost post)
    {
        PushBack(async () =>
        {
            _currentPage = "search";
            RefreshNav();
            await ShowSearchPageAsync();
        });
        await OpenPostAsync(post);
    }

    private async Task OpenGameFromSearchAsync(XhhGame game)
    {
        PushBack(async () =>
        {
            _currentPage = "search";
            RefreshNav();
            await ShowSearchPageAsync();
        });
        await OpenGameAsync(game.AppId, game.Name);
    }

    private async Task OpenUserFromSearchAsync(XhhUser user)
    {
        PushBack(async () =>
        {
            _currentPage = "search";
            RefreshNav();
            await ShowSearchPageAsync();
        });
        await OpenUserAsync(user);
    }

    private async Task OpenCommunityTopicAsync(XhhTopic topic)
    {
        _selectedTopic = topic;
        _selectedTopicId = topic.Id;
        _topicPosts.Clear();
        _topicOffset = 0;
        _topicHasMore = true;
        _topicLoaded = false;
        await NavigateAsync("community");
    }
}

internal static class BorderExtensions
{
    public static Border WithChild(this Border border, UIElement child)
    {
        border.Child = child;
        return border;
    }
}
