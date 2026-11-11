using System.Text.Json;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tubatools.Xiaoheihe.Services;

namespace Tubatools.Xiaoheihe;

internal sealed partial class XiaoheiheView
{
    private XhhUser? _mineProfile;
    private StackPanel? _mineListSurface;
    private readonly Dictionary<string, Button> _mineTabs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _mineLoadedSections = new(StringComparer.Ordinal);
    private int _mineMessageType;
    private XhhSignState? _signState;
    private TextBlock? _signStatusText;
    private Button? _signButton;

    private XhhUser? _currentUser;
    private bool _userFollowing;
    private readonly List<XhhPost> _userPosts = [];
    private StackPanel? _userPostsSurface;

    private async Task ShowMineAsync()
    {
        _pageTitle.Text = "我的";
        _pageSubtitle.Text = "个人主页、签到、收藏与历史";
        _refreshAction = () => RefreshMineAsync();
        if (!_client.IsLoggedIn)
        {
            BuildMineLoginPage();
            return;
        }
        if (!await EnsureMineProfileAsync())
        {
            BuildMineLoginPage();
            return;
        }
        await LoadSignStateAsync();
        BuildMinePage();
        await LoadMineSectionAsync(_mineSection, force: false);
    }

    private void ResetMineCaches()
    {
        _mineProfile = null;
        _signState = null;
        _mineLoadedSections.Clear();
        _minePosts.Clear();
        _favPosts.Clear();
        _historyPosts.Clear();
        _messages.Clear();
        _ownedGames.Clear();
    }

    private async Task<bool> EnsureMineProfileAsync()
    {
        if (_mineProfile is not null) return true;
        try
        {
            JsonElement result = await _client.UserProfileAsync(_client.HeyboxId, _lifetime.Token);
            JsonElement profile = result.Obj("account_detail");
            if (profile.ValueKind != JsonValueKind.Object) profile = result.Obj("profile");
            if (profile.ValueKind != JsonValueKind.Object) profile = result;
            XhhUser user = XhhParser.ParseUser(profile);
            if (string.IsNullOrWhiteSpace(user.Name))
                user = XhhParser.ParseUser(result.Obj("user"));
            _mineProfile = user;
            _session ??= new XhhSession { Cookie = _client.CookieHeader, HeyboxId = _client.HeyboxId };
            _session.Nickname = user.Name;
            _session.Avatar = user.Avatar;
            _session.Level = user.Level;
            _session.HeyboxId = string.IsNullOrWhiteSpace(_session.HeyboxId) ? _client.HeyboxId : _session.HeyboxId;
            _store.Save(_session);
            RefreshAccountChrome();
            return true;
        }
        catch (XhhAuthException)
        {
            ClearSession();
            return false;
        }
    }

    private void BuildMineLoginPage()
    {
        _refreshAction = null;
        var surface = Surface(900);
        var card = Card(28);
        var stack = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 620 };
        stack.Children.Add(new FontIcon
        {
            Glyph = "\uE77B",
            FontSize = 52,
            Foreground = _palette.AccentText,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        TextBlock title = T("登录小黑盒", 26, weight: FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(title);
        TextBlock subtitle = T("登录后可同步个人主页、签到、收藏、历史、消息与 Steam 游戏库。", 13, secondary: true, wrap: true);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        subtitle.TextAlignment = TextAlignment.Center;
        stack.Children.Add(subtitle);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        Button qr = AccentBtn("扫码登录", "\uED14");
        qr.Click += async (_, _) => await NavigateAsync("account");
        buttons.Children.Add(qr);
        Button web = Btn("网页登录 / 补全风控", "\uE774");
        web.Click += async (_, _) => await NavigateAsync("account");
        buttons.Children.Add(web);
        stack.Children.Add(buttons);

        stack.Children.Add(Divider());
        stack.Children.Add(FeatureLine("\uE8FB", "每日签到与任务状态"));
        stack.Children.Add(FeatureLine("\uE734", "我的收藏与浏览历史"));
        stack.Children.Add(FeatureLine("\uE8BD", "回复、赞与消息中心"));
        stack.Children.Add(FeatureLine("\uE7FC", "Steam 游戏库与热门游戏"));
        stack.Children.Add(T("帖子详情需要风控令牌，建议在「账号 → 网页登录」中打开一次官方页面并读取登录状态。",
            11, tertiary: true, wrap: true));
        card.Child = stack;
        surface.Children.Add(card);
        ShowPage(Scroll(surface));
    }

    private FrameworkElement FeatureLine(string glyph, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Foreground = _palette.AccentText });
        row.Children.Add(T(text, 13));
        return row;
    }

    private void BuildMinePage()
    {
        var surface = Surface(980);
        surface.Children.Add(BuildMineProfileCard());
        surface.Children.Add(BuildSignCard());
        surface.Children.Add(BuildMineTabs());
        _mineListSurface = new StackPanel { Spacing = 12 };
        surface.Children.Add(_mineListSurface);
        ShowPage(Scroll(surface));
        RenderMineTabs();
    }

    private FrameworkElement BuildMineProfileCard()
    {
        XhhUser user = _mineProfile ?? new XhhUser("", "小黑盒用户", "");
        var card = Card(20);
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(Avatar(user.Avatar, 64));

        var info = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        nameRow.Children.Add(T(string.IsNullOrWhiteSpace(user.Name) ? "小黑盒用户" : user.Name, 22, weight: FontWeights.SemiBold));
        if (user.Level > 0) nameRow.Children.Add(Pill($"Lv.{user.Level}", accent: true));
        info.Children.Add(nameRow);
        if (!string.IsNullOrWhiteSpace(user.Id)) info.Children.Add(T($"小黑盒 ID：{user.Id}", 11, secondary: true));
        if (!string.IsNullOrWhiteSpace(user.Bio)) info.Children.Add(T(XhhFormat.Clip(user.Bio, 120), 12, secondary: true, wrap: true));

        var stats = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Padding = new Thickness(0, 5, 0, 0) };
        stats.Children.Add(Stat("\uE8FA", $"关注 {XhhFormat.Count(user.FollowCount)}"));
        stats.Children.Add(Stat("\uE716", $"粉丝 {XhhFormat.Count(user.FanCount)}"));
        stats.Children.Add(Stat("\uE8A5", $"帖子 {XhhFormat.Count(user.LinkCount)}"));
        info.Children.Add(stats);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        var buttons = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        Button refresh = Btn("刷新资料", "\uE72C");
        refresh.Click += async (_, _) => await RunAsync(RefreshMineAsync);
        buttons.Children.Add(refresh);
        Button logout = Btn("退出登录", "\uE7E8");
        logout.Click += async (_, _) => await RunAsync(LogoutAsync);
        buttons.Children.Add(logout);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        card.Child = grid;
        return card;
    }

    private FrameworkElement BuildSignCard()
    {
        var card = Card(16);
        var grid = new Grid { ColumnSpacing = 13 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(11),
            Background = _palette.AccentSoft,
            Child = new FontIcon { Glyph = "\uE8FB", FontSize = 19, Foreground = _palette.AccentText }
        });

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(T("每日签到", 15, weight: FontWeights.SemiBold));
        string status = _signState is null
            ? "签到状态加载中…"
            : _signState.SignedToday
                ? $"今日已签到 · 连续 {_signState.SignDays} 天"
                : _signState.SignDays > 0 ? $"今日尚未签到 · 已连续 {_signState.SignDays} 天" : "今日尚未签到";
        _signStatusText = T(status, 11, secondary: true, wrap: true);
        text.Children.Add(_signStatusText);
        if (_signState is { Tasks.Count: > 0 })
            text.Children.Add(T("任务：" + string.Join(" · ", _signState.Tasks.Take(3)), 10, tertiary: true, wrap: true));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var signButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        _signButton = AccentBtn(_signState?.SignedToday == true ? "已签到" : "立即签到", "\uE8FB");
        _signButton.IsEnabled = _signState?.SignedToday != true;
        _signButton.Click += async (_, _) => await RunAsync(SignInAsync);
        signButtons.Children.Add(_signButton);
        Button refreshSign = Btn("刷新", "\uE72C");
        refreshSign.Click += async (_, _) => await RunAsync(LoadSignStateAsync);
        signButtons.Children.Add(refreshSign);
        Grid.SetColumn(signButtons, 2);
        grid.Children.Add(signButtons);
        card.Child = grid;
        return card;
    }

    private FrameworkElement BuildMineTabs()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Padding = new Thickness(0, 4, 0, 0) };
        _mineTabs.Clear();
        (string Key, string Label, string Glyph)[] tabs =
        [
            ("posts", "我的帖子", "\uE8A5"),
            ("fav", "收藏", "\uE734"),
            ("history", "历史", "\uE81C"),
            ("messages", "消息", "\uE8BD"),
            ("games", "游戏库", "\uE7FC")
        ];
        foreach (var tab in tabs)
        {
            Button button = Btn(tab.Label, tab.Glyph);
            button.Padding = new Thickness(12, 6, 12, 6);
            button.Click += async (_, _) => await SwitchMineSectionAsync(tab.Key);
            _mineTabs[tab.Key] = button;
            row.Children.Add(button);
        }
        return row;
    }

    private void RenderMineTabs()
    {
        foreach (var (key, button) in _mineTabs)
        {
            bool selected = key == _mineSection;
            button.Background = selected ? _palette.AccentSoft : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            button.BorderBrush = selected ? _palette.Accent : _palette.CardStroke;
            button.BorderThickness = new Thickness(1);
    }
    }

    private async Task SwitchMineSectionAsync(string section)
    {
        _mineSection = section;
        RenderMineTabs();
        await LoadMineSectionAsync(section, force: false);
    }

    private async Task LoadMineSectionAsync(string section, bool force)
    {
        if (_mineListSurface is null) return;
        if (!force && _mineLoadedSections.Contains(section))
        {
            RenderMineList(section);
            return;
        }

        _mineListSurface.Children.Clear();
        _mineListSurface.Children.Add(new ProgressRing
        {
            IsActive = true,
            Width = 30,
            Height = 30,
            Margin = new Thickness(0, 32, 0, 32),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        switch (section)
        {
            case "posts":
            {
                JsonElement result = await _client.UserLinksAsync(_client.HeyboxId, 0, 20, _lifetime.Token);
                _minePosts.Clear();
                _minePosts.AddRange(XhhParser.ParsePostList(result));
                break;
            }
            case "fav":
            {
                JsonElement result = await _client.FavLinksAsync(0, 30, _lifetime.Token);
                _favPosts.Clear();
                _favPosts.AddRange(XhhParser.ParsePostList(result));
                break;
            }
            case "history":
            {
                JsonElement result = await _client.HistoryAsync(0, 30, _lifetime.Token);
                _historyPosts.Clear();
                _historyPosts.AddRange(XhhParser.ParsePostList(result));
                break;
            }
            case "messages":
            {
                JsonElement result = await _client.MessagesAsync(_mineMessageType, 0, 20, _lifetime.Token);
                _messages.Clear();
                _messages.AddRange(XhhParser.ParseMessages(result, _mineMessageType));
                break;
            }
            case "games":
            {
                JsonElement result = await _client.OwnedGamesAsync(0, 30, _lifetime.Token);
                _ownedGames.Clear();
                _ownedGames.AddRange(XhhParser.ParseGames(result));
                break;
            }
        }

        _mineLoadedSections.Add(section);
        RenderMineList(section);
    }

    private void RenderMineList(string section)
    {
        if (_mineListSurface is null) return;
        _mineListSurface.Children.Clear();

        if (section == "messages")
        {
            var filter = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Padding = new Thickness(0, 0, 0, 2) };
            foreach (var (type, label) in new[] { (0, "回复我的"), (1, "赞与收藏"), (2, "消息中心") })
            {
                Button button = Btn(label);
                button.Padding = new Thickness(10, 4, 10, 4);
                if (type == _mineMessageType)
                {
                    button.Background = _palette.AccentSoft;
                    button.BorderBrush = _palette.Accent;
                    button.BorderThickness = new Thickness(1);
                }
                int selected = type;
                button.Click += async (_, _) =>
                {
                    _mineMessageType = selected;
                    _mineLoadedSections.Remove("messages");
                    await RunAsync(async () => await LoadMineSectionAsync("messages", force: true));
                };
                filter.Children.Add(button);
            }
            _mineListSurface.Children.Add(filter);
        }

        if (section is "posts" or "fav" or "history")
        {
            List<XhhPost> posts = section == "posts" ? _minePosts : section == "fav" ? _favPosts : _historyPosts;
            if (posts.Count == 0)
            {
                _mineListSurface.Children.Add(EmptyState("\uE8A5", "这里还没有内容", section == "posts" ? "发布帖子后会显示在这里。" : "浏览或收藏帖子后会显示在这里。"));
                return;
            }
            foreach (XhhPost post in posts)
                _mineListSurface.Children.Add(BuildPostCard(post, () => OpenPostFromMineAsync(post)));
            return;
        }

        if (section == "messages")
        {
            if (_messages.Count == 0)
            {
                _mineListSurface.Children.Add(EmptyState("\uE8BD", "暂时没有消息", "有人回复或点赞后会显示在这里。"));
                return;
            }
            foreach (XhhMessageItem message in _messages)
                _mineListSurface.Children.Add(BuildMessageRow(message));
            return;
        }

        if (section == "games")
        {
            if (_ownedGames.Count == 0)
            {
                _mineListSurface.Children.Add(EmptyState("\uE7FC", "游戏库为空", "绑定 Steam 账号后，已拥有的游戏会显示在这里。"));
                return;
            }
            var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
            for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < _ownedGames.Count; i++)
            {
                if (i % 3 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                XhhGame game = _ownedGames[i];
                FrameworkElement card = BuildGameCard(game, () => OpenGameFromMineAsync(game), 0);
                Grid.SetColumn(card, i % 3);
                Grid.SetRow(card, i / 3);
                grid.Children.Add(card);
            }
            _mineListSurface.Children.Add(grid);
        }
    }

    private FrameworkElement BuildMessageRow(XhhMessageItem message)
    {
        Border card = Card(14);
        var grid = new Grid { ColumnSpacing = 11 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(Avatar(message.Avatar, 34));
        var text = new StackPanel { Spacing = 3 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(T(string.IsNullOrWhiteSpace(message.Title) ? "消息" : message.Title, 13, weight: FontWeights.SemiBold));
        if (!string.IsNullOrWhiteSpace(message.TimeText)) titleRow.Children.Add(T(message.TimeText, 10, tertiary: true));
        text.Children.Add(titleRow);
        if (!string.IsNullOrWhiteSpace(message.UserName)) text.Children.Add(T(message.UserName, 11, secondary: true));
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            TextBlock content = T(XhhFormat.Clip(message.Content, 160), 12, secondary: true, wrap: true);
            content.MaxLines = 3;
            content.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(content);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        card.Child = grid;
        if (!string.IsNullOrWhiteSpace(message.PostId))
            MakeClickable(card, async () => await OpenPostFromMineAsync(new XhhPost { Id = message.PostId }));
        return card;
    }

    private async Task LoadSignStateAsync()
    {
        try
        {
            JsonElement sign = await _client.SignListAsync(_lifetime.Token);
            JsonElement tasks = default;
            try { tasks = await _client.TaskListV2Async(_lifetime.Token); }
            catch (XhhException) { }
            _signState = XhhParser.ParseSignState(sign, tasks);
        }
        catch (XhhException)
        {
            _signState = null;
        }
    }

    private async Task SignInAsync()
    {
        if (!_client.IsLoggedIn) return;
        JsonElement result;
        try
        {
            result = await _client.SignInAsync(_lifetime.Token);
        }
        catch (XhhException ex)
        {
            ShowNotice("网页端签到接口暂时不可用：" + ex.Message + "。请在官方 App 中签到。", InfoBarSeverity.Warning);
            return;
        }
        await LoadSignStateAsync();
        string message = result.FirstStr("msg", "message", "tips", "desc");
        if (_signState?.SignedToday == true)
            ShowNotice(string.IsNullOrWhiteSpace(message) ? "签到完成。" : message, InfoBarSeverity.Success);
        else
            ShowNotice(string.IsNullOrWhiteSpace(message)
                ? "签到请求已发送，但服务端没有更新签到状态；请稍后在官方 App 中确认。"
                : message, InfoBarSeverity.Warning);
        if (_signStatusText is not null)
            _signStatusText.Text = _signState?.SignedToday == true
                ? $"今日已签到 · 连续 {_signState.SignDays} 天"
                : "今日尚未签到";
        if (_signButton is not null)
        {
            _signButton.IsEnabled = _signState?.SignedToday != true;
            _signButton.Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    new FontIcon { Glyph = "\uE8FB", FontSize = 14 },
                    new TextBlock { Text = _signState?.SignedToday == true ? "已签到" : "立即签到", FontSize = 13 }
                }
            };
        }
    }

    private async Task RefreshMineAsync()
    {
        _mineProfile = null;
        _signState = null;
        _mineLoadedSections.Clear();
        _minePosts.Clear();
        _favPosts.Clear();
        _historyPosts.Clear();
        _messages.Clear();
        _ownedGames.Clear();
        await ShowMineAsync();
    }

    private async Task LogoutAsync()
    {
        bool confirmed = await _host.ConfirmAsync("退出登录", "退出后需要重新扫码或网页登录，确定继续吗？");
        if (!confirmed) return;
        try { await _client.LogoutAsync(_lifetime.Token); }
        catch (XhhException) { }
        ClearSession();
        _mineProfile = null;
        _signState = null;
        _mineLoadedSections.Clear();
        _minePosts.Clear();
        _favPosts.Clear();
        _historyPosts.Clear();
        _messages.Clear();
        _ownedGames.Clear();
        ShowNotice("已退出登录。", InfoBarSeverity.Informational);
        if (_currentPage == "account") ShowAccountPage();
        else await ShowMineAsync();
    }

    private async Task OpenPostFromMineAsync(XhhPost post)
    {
        PushBack(async () =>
        {
            _currentPage = "mine";
            RefreshNav();
            await ShowMineAsync();
        });
        await OpenPostAsync(post);
    }

    private async Task OpenGameFromMineAsync(XhhGame game)
    {
        PushBack(async () =>
        {
            _currentPage = "mine";
            RefreshNav();
            await ShowMineAsync();
        });
        await OpenGameAsync(game.AppId, game.Name);
    }

    // ------------------------------------------------------------------
    // 用户主页
    // ------------------------------------------------------------------

    private async Task OpenUserAsync(XhhUser user, Func<Task>? back = null)
    {
        if (back is not null) PushBack(back);
        _currentUser = user;
        _userPosts.Clear();
        _userFollowing = false;
        _pageTitle.Text = "用户主页";
        _pageSubtitle.Text = string.IsNullOrWhiteSpace(user.Name) ? "小黑盒用户" : user.Name;
        _refreshAction = () => ReloadUserAsync();
        _userPostsSurface = new StackPanel { Spacing = 12 };
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

        try
        {
            if (!string.IsNullOrWhiteSpace(user.Id))
            {
                JsonElement profile = await _client.UserProfileAsync(user.Id, _lifetime.Token);
                JsonElement detail = profile.Obj("account_detail");
                if (detail.ValueKind != JsonValueKind.Object) detail = profile.Obj("profile");
                if (detail.ValueKind != JsonValueKind.Object) detail = profile;
                XhhUser parsed = XhhParser.ParseUser(detail);
                if (!string.IsNullOrWhiteSpace(parsed.Name)) user = parsed;
                string state = detail.FirstStr("follow_state");
                _userFollowing = state == "following" || detail.Bool("is_follow") || detail.Bool("followed");
                _currentUser = user;

                JsonElement links = await _client.UserLinksAsync(user.Id, 0, 20, _lifetime.Token);
                _userPosts.AddRange(XhhParser.ParsePostList(links));
            }
        }
        catch (XhhException ex)
        {
            ShowNotice(ex.Message, InfoBarSeverity.Warning);
        }

        RenderUserPage();
    }

    private async Task ReloadUserAsync()
    {
        if (_currentUser is null) return;
        await OpenUserAsync(_currentUser);
    }

    private void RenderUserPage()
    {
        if (_currentUser is null || _userPostsSurface is null) return;
        var surface = Surface(980);
        surface.Children.Add(BuildUserProfileCard(_currentUser));
        surface.Children.Add(Section("发布的帖子", "最近的社区内容"));
        _userPostsSurface = new StackPanel { Spacing = 12 };
        if (_userPosts.Count == 0)
        {
            _userPostsSurface.Children.Add(EmptyState("\uE8A5", "暂时没有帖子", "该用户还没有发布可见内容。"));
        }
        else
        {
            foreach (XhhPost post in _userPosts)
                _userPostsSurface.Children.Add(BuildPostCard(post, () => OpenPostFromUserAsync(post)));
        }
        surface.Children.Add(_userPostsSurface);
        ShowPage(Scroll(surface));
    }

    private FrameworkElement BuildUserProfileCard(XhhUser user)
    {
        var card = Card(20);
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Avatar(user.Avatar, 64));

        var info = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        nameRow.Children.Add(T(string.IsNullOrWhiteSpace(user.Name) ? "小黑盒用户" : user.Name, 22, weight: FontWeights.SemiBold));
        if (user.Level > 0) nameRow.Children.Add(Pill($"Lv.{user.Level}", accent: true));
        info.Children.Add(nameRow);
        if (!string.IsNullOrWhiteSpace(user.Id)) info.Children.Add(T($"小黑盒 ID：{user.Id}", 11, secondary: true));
        if (!string.IsNullOrWhiteSpace(user.Bio)) info.Children.Add(T(user.Bio, 12, secondary: true, wrap: true));
        var stats = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Padding = new Thickness(0, 5, 0, 0) };
        stats.Children.Add(Stat("\uE8FA", $"关注 {XhhFormat.Count(user.FollowCount)}"));
        stats.Children.Add(Stat("\uE716", $"粉丝 {XhhFormat.Count(user.FanCount)}"));
        stats.Children.Add(Stat("\uE8A5", $"帖子 {XhhFormat.Count(user.LinkCount)}"));
        info.Children.Add(stats);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        var buttons = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        Button follow = _userFollowing ? Btn("已关注", "\uE8FB") : AccentBtn("关注", "\uE8FA");
        follow.IsEnabled = _client.IsLoggedIn && !string.IsNullOrWhiteSpace(user.Id);
        follow.Click += async (_, _) => await RunAsync(ToggleUserFollowAsync);
        buttons.Children.Add(follow);
        Button open = Btn("浏览器打开", "\uE774");
        open.Click += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(user.Id))
                await _host.Services.OpenUriAsync(new Uri($"https://www.xiaoheihe.cn/app/bbs/user/{user.Id}"));
        };
        buttons.Children.Add(open);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        card.Child = grid;
        return card;
    }

    private async Task ToggleUserFollowAsync()
    {
        if (_currentUser is null || string.IsNullOrWhiteSpace(_currentUser.Id)) return;
        bool next = !_userFollowing;
        await _client.FollowUserAsync(_currentUser.Id, next, _lifetime.Token);
        _userFollowing = next;
        ShowNotice(next ? "已关注该用户。" : "已取消关注。", InfoBarSeverity.Success);
        RenderUserPage();
    }

    private async Task OpenPostFromUserAsync(XhhPost post)
    {
        XhhUser user = _currentUser ?? new XhhUser(post.AuthorId, post.AuthorName, post.AuthorAvatar, post.AuthorLevel);
        PushBack(async () => await OpenUserAsync(user));
        await OpenPostAsync(post);
    }
}
