using System.Text.Json;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tubatools.Xiaoheihe.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;

namespace Tubatools.Xiaoheihe;

internal sealed partial class XiaoheiheView
{
    private TextBox? _gameSearchBox;
    private StackPanel? _gameSearchSurface;
    private StackPanel? _gameSurface;
    private XhhGameDetail? _gameDetail;
    private bool _gameFollowing;
    private string _gameAppId = "";
    private string _gameName = "";
    private string _gameType = "";
    private MediaPlayer? _inlineGameMediaPlayer;
    private bool _inlineGamePaused;

    private async Task ShowGamesAsync()
    {
        _pageTitle.Text = "游戏";
        _pageSubtitle.Text = "热门游戏、游戏详情与我的 Steam 游戏库";
        _refreshAction = () => RefreshGamesAsync();
        BuildGamesPage();

        if (_gameSection == "hot" && _hotGames.Count == 0)
        {
            JsonElement result = await _client.HotGamesAsync(0, 30, _lifetime.Token);
            _hotGames.AddRange(XhhParser.ParseGames(result));
        }
        else if (_gameSection == "mine" && _client.IsLoggedIn && _gameLibrary.Count == 0)
        {
            JsonElement result = await _client.OwnedGamesAsync(0, 30, _lifetime.Token);
            _gameLibrary.AddRange(XhhParser.ParseGames(result));
        }
        RenderGames();
    }

    private void BuildGamesPage()
    {
        var surface = Surface(1060);

        var searchCard = Card(16);
        var searchRow = new Grid { ColumnSpacing = 10 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _gameSearchBox = new TextBox { PlaceholderText = "搜索游戏，例如：黑神话、Apex、影之刃零", FontSize = 14 };
        _gameSearchBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) await RunAsync(SearchGamesAsync);
        };
        searchRow.Children.Add(_gameSearchBox);
        Button search = AccentBtn("搜索游戏", "\uE721");
        search.Click += async (_, _) => await RunAsync(SearchGamesAsync);
        Grid.SetColumn(search, 1);
        searchRow.Children.Add(search);
        searchCard.Child = searchRow;
        surface.Children.Add(searchCard);

        _gameSearchSurface = new StackPanel { Spacing = 10 };
        surface.Children.Add(_gameSearchSurface);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Padding = new Thickness(0, 2, 0, 2) };
        Button hot = Btn("热门游戏", "\uE7FC");
        Button mine = Btn("我的游戏库", "\uE8A5");
        hot.Click += async (_, _) => await SwitchGameSectionAsync("hot");
        mine.Click += async (_, _) => await SwitchGameSectionAsync("mine");
        tabs.Children.Add(hot);
        tabs.Children.Add(mine);
        surface.Children.Add(tabs);

        var hint = new StackPanel { Spacing = 2 };
        hint.Children.Add(T("游戏中心", 19, weight: FontWeights.SemiBold));
        hint.Children.Add(T("查看热门榜、游戏资料与我的 Steam 游戏库；点击卡片进入详情。", 12, secondary: true));
        surface.Children.Add(hint);

        _gameSurface = new StackPanel { Spacing = 10 };
        surface.Children.Add(_gameSurface);
        ShowPage(Scroll(surface));
        RenderGameTabs(hot, mine);
    }

    private void RenderGameTabs(Button hot, Button mine)
    {
        StyleTab(hot, _gameSection == "hot");
        StyleTab(mine, _gameSection == "mine");
    }

    private void StyleTab(Button button, bool selected)
    {
        button.Background = selected ? _palette.AccentSoft : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.BorderBrush = selected ? _palette.Accent : _palette.CardStroke;
        button.BorderThickness = new Thickness(1);
    }

    private async Task SwitchGameSectionAsync(string section)
    {
        _gameSection = section;
        if (section == "mine" && !_client.IsLoggedIn)
        {
            ShowNotice("登录后可查看 Steam 游戏库。", InfoBarSeverity.Informational, "去登录", async () => await NavigateAsync("account"));
            _gameSection = "hot";
        }
        BuildGamesPage();
        if (_gameSection == "hot" && _hotGames.Count == 0)
        {
            JsonElement result = await _client.HotGamesAsync(0, 30, _lifetime.Token);
            _hotGames.AddRange(XhhParser.ParseGames(result));
        }
        else if (_gameSection == "mine" && _gameLibrary.Count == 0)
        {
            JsonElement result = await _client.OwnedGamesAsync(0, 30, _lifetime.Token);
            _gameLibrary.AddRange(XhhParser.ParseGames(result));
        }
        RenderGames();
    }

    private void RenderGames()
    {
        if (_gameSurface is null) return;
        _gameSurface.Children.Clear();
        List<XhhGame> games = _gameSection == "mine" ? _gameLibrary : _hotGames;
        if (_gameSection == "mine" && !_client.IsLoggedIn)
        {
            _gameSurface.Children.Add(EmptyState("\uE77B", "尚未登录", "登录后可以同步 Steam 游戏库。"));
            return;
        }
        if (games.Count == 0)
        {
            _gameSurface.Children.Add(EmptyState("\uE7FC", _gameSection == "mine" ? "游戏库为空" : "暂时没有游戏数据",
                _gameSection == "mine" ? "绑定 Steam 账号后会显示已拥有的游戏。" : "稍后刷新，或搜索游戏名称。")); 
            return;
        }

        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < games.Count; i++)
        {
            if (i % 3 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            XhhGame game = games[i];
            FrameworkElement card = BuildGameCard(game, () => OpenGameFromGamesAsync(game), 0);
            Grid.SetColumn(card, i % 3);
            Grid.SetRow(card, i / 3);
            grid.Children.Add(card);
        }
        _gameSurface.Children.Add(grid);
    }

    private async Task SearchGamesAsync()
    {
        if (_gameSearchBox is null || _gameSearchSurface is null) return;
        string query = _gameSearchBox.Text.Trim();
        if (query.Length == 0)
        {
            _gameSearchSurface.Children.Clear();
            return;
        }
        _gameSearchSurface.Children.Clear();
        _gameSearchSurface.Children.Add(new ProgressRing
        {
            IsActive = true,
            Width = 28,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Left
        });
        JsonElement result = await _client.SearchAsync(query, "game", 0, 20, _lifetime.Token);
        var items = XhhParser.ParseSearchItems(result).Where(i => i.Game is not null).Select(i => i.Game!).ToList();
        _gameSearchSurface.Children.Clear();
        if (items.Count == 0)
        {
            _gameSearchSurface.Children.Add(EmptyState("\uE721", "没有找到游戏", "换一个名称或英文名试试。"));
            return;
        }
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < items.Count; i++)
        {
            if (i % 3 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            XhhGame game = items[i];
            FrameworkElement card = BuildGameCard(game, () => OpenGameFromSearchGamesAsync(game), 0);
            Grid.SetColumn(card, i % 3);
            Grid.SetRow(card, i / 3);
            grid.Children.Add(card);
        }
        _gameSearchSurface.Children.Add(grid);
    }

    private async Task OpenGameFromGamesAsync(XhhGame game)
    {
        PushBack(async () => await ShowGamesAsync());
        await OpenGameAsync(game.AppId, game.Name, game.GameType);
    }

    private async Task OpenGameFromSearchGamesAsync(XhhGame game)
    {
        PushBack(async () => await ShowGamesAsync());
        await OpenGameAsync(game.AppId, game.Name, game.GameType);
    }

    private async Task OpenGameAsync(string appId, string name, string gameType = "")
    {
        _gameAppId = appId;
        _gameName = name;
        _gameType = gameType;
        _pageTitle.Text = "游戏详情";
        _pageSubtitle.Text = name;
        _refreshAction = () => ReloadGameAsync();
        var surface = Surface(980);
        surface.Children.Add(new ProgressRing
        {
            IsActive = true,
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 40, 0, 40),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        ShowPage(Scroll(surface));
        JsonElement result = await _client.GameDetailAsync(appId, gameType, _lifetime.Token);
        _gameDetail = XhhParser.ParseGameDetail(result);
        try
        {
            JsonElement introduction = await _client.GameIntroductionAsync(appId, _lifetime.Token);
            string description = XhhParser.ParseGameIntroduction(introduction);
            if (!string.IsNullOrWhiteSpace(description))
            {
                _gameDetail = new XhhGameDetail
                {
                    Game = _gameDetail.Game,
                    Description = description,
                    Info = _gameDetail.Info,
                    Media = _gameDetail.Media,
                    Screenshots = _gameDetail.Screenshots
                };
            }
        }
        catch (XhhException)
        {
        }
        _gameFollowing = _gameDetail.Game.FollowState == "following";
        RenderGameDetail();
    }

    private async Task ReloadGameAsync()
    {
        if (string.IsNullOrWhiteSpace(_gameAppId)) return;
        await OpenGameAsync(_gameAppId, _gameName, _gameType);
    }

    private void RenderGameDetail()
    {
        if (_gameDetail is null) return;
        XhhGame game = _gameDetail.Game;
        var surface = Surface(980);

        var hero = Card(0);
        var heroGrid = new Grid { ColumnSpacing = 18 };
        heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var cover = RemoteImage(game.Image, 320, 180, 12);
        cover.Margin = new Thickness(0);
        heroGrid.Children.Add(cover);

        var info = new StackPanel { Spacing = 8, Padding = new Thickness(0, 16, 18, 16), VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        nameRow.Children.Add(T(string.IsNullOrWhiteSpace(game.Name) ? _gameName : game.Name, 24, weight: FontWeights.SemiBold));
        if (!string.IsNullOrWhiteSpace(game.Score)) nameRow.Children.Add(Pill(game.Score, accent: true));
        info.Children.Add(nameRow);
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        meta.Children.Add(T(game.PriceText, 13, accent: game.IsFree));
        if (!string.IsNullOrWhiteSpace(game.ReleaseDate)) meta.Children.Add(Stat("\uE787", game.ReleaseDate));
        if (!string.IsNullOrWhiteSpace(game.Online)) meta.Children.Add(Stat("\uE774", $"在线 {game.Online}"));
        if (game.FollowCount > 0) meta.Children.Add(Stat("\uE8E1", $"关注 {XhhFormat.Count(game.FollowCount)}"));
        info.Children.Add(meta);
        if (!string.IsNullOrWhiteSpace(_gameDetail.Description))
        {
            TextBlock desc = T(XhhFormat.Clip(_gameDetail.Description, 260), 12, secondary: true, wrap: true);
            desc.MaxLines = 3;
            desc.TextTrimming = TextTrimming.CharacterEllipsis;
            info.Children.Add(desc);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button follow = _gameFollowing ? Btn("已关注", "\uE8FB") : AccentBtn("关注游戏", "\uE8FA");
        follow.IsEnabled = _client.IsLoggedIn;
        follow.Click += async (_, _) => await RunAsync(ToggleGameFollowAsync);
        buttons.Children.Add(follow);
        Button open = Btn("浏览器打开", "\uE774");
        open.Click += async (_, _) => await _host.Services.OpenUriAsync(new Uri($"https://www.xiaoheihe.cn/app/game/detail/{game.AppId}"));
        buttons.Children.Add(open);
        info.Children.Add(buttons);
        Grid.SetColumn(info, 1);
        heroGrid.Children.Add(info);
        hero.Child = heroGrid;
        surface.Children.Add(hero);

        if (_gameDetail.Info.Count > 0)
        {
            var infoCard = Card(16);
            var infoStack = new StackPanel { Spacing = 2 };
            infoStack.Children.Add(Section("游戏信息"));
            foreach (var (label, value) in _gameDetail.Info)
                infoStack.Children.Add(BuildInfoRow(label, value));
            infoCard.Child = infoStack;
            surface.Children.Add(infoCard);
        }

        if (!string.IsNullOrWhiteSpace(_gameDetail.Description))
        {
            var descCard = Card(18);
            var descStack = new StackPanel { Spacing = 10 };
            descStack.Children.Add(Section("游戏简介"));
            descStack.Children.Add(BuildGameDescription(_gameDetail.Description));
            descCard.Child = descStack;
            surface.Children.Add(descCard);
        }

        if (_gameDetail.Media.Count > 0)
        {
            var shotCard = Card(16);
            var shotStack = new StackPanel { Spacing = 10 };
            shotStack.Children.Add(Section(_gameDetail.Media.Any(m => m.Type == "movie") ? "截图与视频" : "截图"));
            shotStack.Children.Add(BuildGameMediaStrip(_gameDetail.Media, 170));
            shotCard.Child = shotStack;
            surface.Children.Add(shotCard);
        }
        else if (_gameDetail.Screenshots.Count > 0)
        {
            var shotCard = Card(16);
            var shotStack = new StackPanel { Spacing = 10 };
            shotStack.Children.Add(Section("截图"));
            shotStack.Children.Add(BuildImageStrip(_gameDetail.Screenshots, 170));
            shotCard.Child = shotStack;
            surface.Children.Add(shotCard);
        }

        ShowPage(Scroll(surface));
        TryStartConnectedAnimation(cover);
    }

    private FrameworkElement BuildGameDescription(string description)
    {
        string text = (description ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (text.Length == 0) return T("暂无简介。", 13, secondary: true);
        text = text.Replace("【", "\n【");

        var panel = new StackPanel { Spacing = 10 };
        var prose = new List<string>();
        var requirements = new List<(string Label, string Value)>();
        bool inRequirements = false;

        void FlushProse()
        {
            if (prose.Count == 0) return;
            string body = string.Join("\n", prose).Trim();
            prose.Clear();
            if (body.Length == 0) return;
            TextBlock paragraph = T(body, 13, wrap: true);
            paragraph.LineHeight = 23;
            paragraph.IsTextSelectionEnabled = true;
            panel.Children.Add(paragraph);
        }

        void FlushRequirements()
        {
            if (requirements.Count == 0) return;
            var card = Card(12);
            var rows = new StackPanel { Spacing = 7 };
            foreach (var (label, value) in requirements)
            {
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                TextBlock key = T(label, 12, secondary: true, weight: FontWeights.SemiBold);
                key.VerticalAlignment = VerticalAlignment.Top;
                TextBlock content = T(value, 12, wrap: true);
                Grid.SetColumn(content, 1);
                row.Children.Add(key);
                row.Children.Add(content);
                rows.Children.Add(row);
            }
            card.Child = rows;
            panel.Children.Add(card);
            requirements.Clear();
        }

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                FlushProse();
                continue;
            }
            if (IsGameDescriptionHeading(line))
            {
                FlushProse();
                FlushRequirements();
                inRequirements = line.Contains("配置", StringComparison.Ordinal) ||
                                 line.Contains("需求", StringComparison.Ordinal);
                TextBlock heading = T(line.Trim('【', '】'), 15, weight: FontWeights.SemiBold);
                heading.Margin = new Thickness(0, 7, 0, 0);
                panel.Children.Add(heading);
                continue;
            }
            if (TryParseRequirement(line, out string label, out string value))
            {
                FlushProse();
                requirements.Add((label, value));
                continue;
            }
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("•", StringComparison.Ordinal))
            {
                FlushProse();
                FlushRequirements();
                string bullet = line.StartsWith("- ", StringComparison.Ordinal)
                    ? line[2..]
                    : line.TrimStart('•', '-', '*', ' ');
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                row.Children.Add(T("•", 13, secondary: true));
                row.Children.Add(T(bullet, 13, wrap: true));
                panel.Children.Add(row);
                continue;
            }
            if (inRequirements && requirements.Count > 0)
            {
                prose.Add(line);
                continue;
            }
            prose.Add(line);
        }
        FlushProse();
        FlushRequirements();
        if (panel.Children.Count == 0) panel.Children.Add(T("暂无简介。", 13, secondary: true));
        return panel;
    }

    private static bool IsGameDescriptionHeading(string line)
    {
        string[] labels =
        [
            "最低配置", "推荐配置", "配置要求", "系统需求", "游戏简介",
            "关于这款游戏", "游戏特色", "游戏介绍", "更多信息", "联系我们"
        ];
        foreach (string label in labels)
            if (line.Equals(label, StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith(label + "：", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith(label + ":", StringComparison.OrdinalIgnoreCase))
                return true;
        return line.StartsWith("【", StringComparison.Ordinal) &&
               line.EndsWith("】", StringComparison.Ordinal) &&
               line.Length <= 24;
    }

    private static bool TryParseRequirement(string line, out string label, out string value)
    {
        label = "";
        value = "";
        int index = line.IndexOfAny([':', '：']);
        if (index <= 0 || index > 24) return false;
        string key = line[..index].Trim();
        string val = line[(index + 1)..].Trim();
        if (key.Length == 0 || val.Length == 0) return false;
        string[] keys =
        [
            "操作系统", "处理器", "内存", "显卡", "存储空间", "DirectX", "网络",
            "附注事项", "声卡", "硬盘", "CPU", "GPU", "OS", "系统", "图形"
        ];
        if (!keys.Any(k => key.Contains(k, StringComparison.OrdinalIgnoreCase))) return false;
        label = key;
        value = val;
        return true;
    }

    private FrameworkElement BuildGameMediaStrip(IReadOnlyList<XhhGameMedia> media, double height)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var ordered = media.OrderBy(m => m.Type == "movie" ? 0 : 1).Take(12).ToList();
        int movieIndex = 0;
        foreach (XhhGameMedia item in ordered)
        {
            double width = height * 1.6;
            string preview = item.Type == "movie"
                ? item.Thumbnail
                : string.IsNullOrWhiteSpace(item.Url) ? item.Thumbnail : item.Url;
            if (item.Type == "movie" && preview.Contains("format/webp", StringComparison.OrdinalIgnoreCase))
                preview = preview.Replace("format/webp", "format/jpg", StringComparison.OrdinalIgnoreCase);
            preview = NormalizeImageUrl(preview);
            FrameworkElement image = RemoteImage(preview, width, height, 8);
            if (item.Type == "movie" && !string.IsNullOrWhiteSpace(item.Url))
            {
                string video = item.Url;
                if (movieIndex == 0)
                {
                    var grid = new Grid { Width = width, Height = height };
                    grid.Children.Add(image);
                    var mediaPlayer = new MediaPlayer
                    {
                        IsMuted = true,
                        IsLoopingEnabled = true,
                        AutoPlay = true
                    };
                    var player = new MediaPlayerElement
                    {
                        Width = width,
                        Height = height,
                        Stretch = Stretch.UniformToFill,
                        AreTransportControlsEnabled = false,
                        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                    };
                    player.SetMediaPlayer(mediaPlayer);
                    grid.Children.Add(player);
                    _inlineGameMediaPlayer = mediaPlayer;
                    _inlineGamePaused = false;
                    grid.Unloaded += (_, _) =>
                    {
                        try
                        {
                            mediaPlayer.Pause();
                            mediaPlayer.Source = null;
                        }
                        catch (Exception) { }
                        if (ReferenceEquals(_inlineGameMediaPlayer, mediaPlayer))
                            _inlineGameMediaPlayer = null;
                    };
                    _ = LoadInlineGameVideoAsync(mediaPlayer, video);
                    var overlay = new Border
                    {
                        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                    };
                    overlay.Tapped += async (_, _) => await RunAsync(() => ShowVideoAsync(video));
                    grid.Children.Add(overlay);
                    panel.Children.Add(grid);
                }
                else
                {
                    var grid = new Grid { Width = width, Height = height };
                    grid.Children.Add(image);
                    grid.Children.Add(new Border
                    {
                        Width = 44,
                        Height = 44,
                        CornerRadius = new CornerRadius(22),
                        Background = new SolidColorBrush(Microsoft.UI.Colors.Black) { Opacity = 0.58 },
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new FontIcon
                        {
                            Glyph = "\uE768",
                            FontSize = 18,
                            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                        }
                    });
                    grid.Tapped += async (_, _) => await RunAsync(() => ShowVideoAsync(video));
                    panel.Children.Add(grid);
                }
                movieIndex++;
            }
            else
            {
                string full = string.IsNullOrWhiteSpace(item.Url) ? preview : NormalizeImageUrl(item.Url);
                image.Tapped += async (_, _) => await RunAsync(() => ShowImageAsync(full));
                panel.Children.Add(image);
            }
        }
        return new ScrollViewer
        {
            Content = panel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
    }

    private async Task LoadInlineGameVideoAsync(MediaPlayer player, string url)
    {
        try
        {
            if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                Task<AdaptiveMediaSourceCreationResult> create =
                    AdaptiveMediaSource.CreateFromUriAsync(new Uri(url)).AsTask();
                Task completed = await Task.WhenAny(create, Task.Delay(6000));
                if (completed != create) return;
                AdaptiveMediaSourceCreationResult adaptive = await create;
                if (adaptive.Status != AdaptiveMediaSourceCreationStatus.Success) return;
                if (_inlineGamePaused) return;
                player.Source = MediaSource.CreateFromAdaptiveMediaSource(adaptive.MediaSource);
            }
            else
            {
                if (_inlineGamePaused) return;
                player.Source = MediaSource.CreateFromUri(new Uri(url));
            }
            player.IsMuted = true;
            if (!_inlineGamePaused) player.Play();
        }
        catch (Exception)
        {
        }
    }

    private async Task ToggleGameFollowAsync()
    {
        if (_gameDetail is null) return;
        bool next = !_gameFollowing;
        await _client.FollowGameAsync(_gameDetail.Game.AppId, next, _lifetime.Token);
        _gameFollowing = next;
        ShowNotice(next ? "已关注该游戏。" : "已取消关注。", InfoBarSeverity.Success);
        RenderGameDetail();
    }

    private async Task RefreshGamesAsync()
    {
        _hotGames.Clear();
        _gameLibrary.Clear();
        _gameDetail = null;
        await ShowGamesAsync();
    }
}
