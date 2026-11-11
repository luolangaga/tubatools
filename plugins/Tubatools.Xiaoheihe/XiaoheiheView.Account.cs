using System.Text.Json;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Tubatools.Xiaoheihe.Services;

namespace Tubatools.Xiaoheihe;

internal sealed partial class XiaoheiheView
{
    private Image? _qrImage;
    private FrameworkElement? _qrPlaceholder;
    private TextBlock? _qrStatus;
    private Button? _qrStartButton;
    private TextBlock? _webStatus;
    private Grid? _webHost;
    private Button? _webOpenButton;
    private Button? _webReadButton;
    private TextBox? _cookieBox;
    private TextBlock? _accountStatusText;
    private TextBlock? _tokenStatusText;
    private SelectorBar? _loginMethodBar;
    private FrameworkElement? _qrLoginPanel;
    private FrameworkElement? _webLoginPanel;
    private FrameworkElement? _cookieLoginPanel;

    private void ShowAccountPage()
    {
        _pageTitle.Text = "账号";
        _pageSubtitle.Text = "登录、会话与风控令牌";
        _refreshAction = () => RefreshAccountPageAsync();
        StopQrTimer();
        DisposeWebView();
        BuildAccountPage();
    }

    private void BuildAccountPage()
    {
        var surface = Surface(940);
        surface.Children.Add(BuildAccountStatusCard());
        surface.Children.Add(BuildLoginSection());
        surface.Children.Add(BuildAboutExpander());
        ShowPage(Scroll(surface));
        UpdateTokenStatus();
        SelectLoginMethod(0);
    }

    private FrameworkElement BuildAccountStatusCard()
    {
        var card = Card(18);
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Avatar(_session?.Avatar, 56));

        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _accountStatusText = T(_client.IsLoggedIn
            ? $"{_session?.Nickname ?? "已登录"}{(string.IsNullOrWhiteSpace(_session?.HeyboxId) ? "" : $" · ID {_session.HeyboxId}")}"
            : "尚未登录", 18, weight: FontWeights.SemiBold);
        nameRow.Children.Add(_accountStatusText);
        nameRow.Children.Add(Pill(_client.IsLoggedIn ? "已登录" : "未登录", _client.IsLoggedIn));
        text.Children.Add(nameRow);
        _tokenStatusText = T("", 11, secondary: true, wrap: true);
        text.Children.Add(_tokenStatusText);
        text.Children.Add(T("登录态保存在插件私有目录，升级不会丢失。", 10, tertiary: true, wrap: true));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var buttons = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (_client.IsLoggedIn)
        {
            Button refresh = Btn("刷新资料", "\uE72C");
            refresh.Click += async (_, _) => await RunAsync(RefreshAccountPageAsync);
            buttons.Children.Add(refresh);
            Button logout = Btn("退出登录", "\uE7E8");
            logout.Click += async (_, _) => await RunAsync(LogoutAsync);
            buttons.Children.Add(logout);
        }
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        card.Child = grid;
        return card;
    }

    private FrameworkElement BuildLoginSection()
    {
        var card = Card(20);
        var stack = new StackPanel { Spacing = 14 };

        var header = new Grid { ColumnSpacing = 16 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Spacing = 2 };
        title.Children.Add(T("登录小黑盒", 20, weight: FontWeights.SemiBold));
        title.Children.Add(T("选择一种登录方式；推荐网页登录，可同时取得帖子详情所需的风控令牌。", 12, secondary: true, wrap: true));
        header.Children.Add(title);

        _loginMethodBar = new SelectorBar { VerticalAlignment = VerticalAlignment.Center };
        var qrItem = new SelectorBarItem { Text = "扫码登录" };
        var webItem = new SelectorBarItem { Text = "网页登录" };
        var cookieItem = new SelectorBarItem { Text = "手动 Cookie" };
        _loginMethodBar.Items.Add(qrItem);
        _loginMethodBar.Items.Add(webItem);
        _loginMethodBar.Items.Add(cookieItem);
        _loginMethodBar.SelectedItem = qrItem;
        _loginMethodBar.SelectionChanged += (sender, _) =>
        {
            int index = sender.SelectedItem is null ? 0 : sender.Items.IndexOf(sender.SelectedItem);
            SelectLoginMethod(index);
        };
        Grid.SetColumn(_loginMethodBar, 1);
        header.Children.Add(_loginMethodBar);
        stack.Children.Add(header);

        _qrLoginPanel = BuildQrPanel();
        _webLoginPanel = BuildWebPanel();
        _cookieLoginPanel = BuildCookiePanel();
        var host = new Grid();
        host.Children.Add(_qrLoginPanel);
        host.Children.Add(_webLoginPanel);
        host.Children.Add(_cookieLoginPanel);
        stack.Children.Add(host);
        stack.Children.Add(Divider());
        stack.Children.Add(T("扫码只取得 pkey；如果之后看到「需要风控令牌」的提示，请切到「网页登录」读取一次。",
            10, tertiary: true, wrap: true));
        card.Child = stack;
        return card;
    }

    private FrameworkElement BuildQrPanel()
    {
        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(0, 2, 0, 0) };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new FontIcon { Glyph = "\uED14", FontSize = 16, Foreground = _palette.AccentText });
        title.Children.Add(T("用小黑盒 App 扫码", 15, weight: FontWeights.SemiBold));
        panel.Children.Add(title);
        panel.Children.Add(T("打开小黑盒 App → 扫一扫，扫描二维码并在手机上确认。二维码约 120 秒有效。",
            12, secondary: true, wrap: true));

        var qrGrid = new Grid { Width = 236, Height = 236 };
        var placeholder = new StackPanel { Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        placeholder.Children.Add(new FontIcon { Glyph = "\uED14", FontSize = 36, Foreground = _palette.TertiaryText });
        placeholder.Children.Add(T("点击下方按钮获取二维码", 11, tertiary: true));
        _qrPlaceholder = placeholder;
        qrGrid.Children.Add(placeholder);
        _qrImage = new Image { Width = 220, Height = 220, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
        qrGrid.Children.Add(_qrImage);
        panel.Children.Add(new Border
        {
            Width = 236,
            Height = 236,
            CornerRadius = new CornerRadius(14),
            Background = _palette.Subtle,
            BorderBrush = _palette.CardStroke,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = qrGrid
        });

        _qrStatus = T("尚未获取二维码。", 12, secondary: true, wrap: true);
        _qrStatus.HorizontalAlignment = HorizontalAlignment.Center;
        _qrStatus.TextAlignment = TextAlignment.Center;
        panel.Children.Add(_qrStatus);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        _qrStartButton = AccentBtn("获取登录二维码", "\uED14");
        _qrStartButton.Click += async (_, _) => await RunAsync(StartQrLoginAsync);
        buttons.Children.Add(_qrStartButton);
        Button refresh = Btn("刷新二维码", "\uE72C");
        refresh.Click += async (_, _) => await RunAsync(StartQrLoginAsync);
        buttons.Children.Add(refresh);
        panel.Children.Add(buttons);

        var why = new Expander
        {
            Header = T("为什么建议再网页登录一次？", 11, secondary: true),
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        why.Content = new TextBlock
        {
            Text = "帖子详情、用户主页、收藏、历史等接口需要 x_xhh_tokenid。原生扫码通常只返回 pkey；网页登录会同时取得这两种凭据。",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = _palette.SecondaryText,
            Padding = new Thickness(0, 6, 0, 2)
        };
        panel.Children.Add(why);
        return panel;
    }

    private FrameworkElement BuildWebPanel()
    {
        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(0, 2, 0, 0) };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new FontIcon { Glyph = "\uE774", FontSize = 16, Foreground = _palette.AccentText });
        title.Children.Add(T("网页登录 / 获取风控令牌", 15, weight: FontWeights.SemiBold));
        panel.Children.Add(title);
        panel.Children.Add(T("打开小黑盒官方网页完成登录，然后读取 Cookie。会同时取得 pkey 与 x_xhh_tokenid。",
            12, secondary: true, wrap: true));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _webOpenButton = AccentBtn("打开官方网页", "\uE774");
        _webOpenButton.Click += async (_, _) => await RunAsync(OpenWebLoginAsync);
        buttons.Children.Add(_webOpenButton);
        _webReadButton = Btn("读取登录状态", "\uE896");
        _webReadButton.Click += async (_, _) => await RunAsync(ReadWebCookiesAsync);
        buttons.Children.Add(_webReadButton);
        panel.Children.Add(buttons);

        _webStatus = T("尚未打开网页。", 11, secondary: true, wrap: true);
        panel.Children.Add(_webStatus);

        _webHost = new Grid { Height = 540, Visibility = Visibility.Collapsed };
        panel.Children.Add(_webHost);
        panel.Children.Add(T("如果网页无法加载，请确认系统已安装 WebView2 Runtime。", 10, tertiary: true, wrap: true));
        return panel;
    }

    private FrameworkElement BuildCookiePanel()
    {
        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(0, 2, 0, 0) };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new FontIcon { Glyph = "\uE8C8", FontSize = 16, Foreground = _palette.AccentText });
        title.Children.Add(T("手动导入 Cookie", 15, weight: FontWeights.SemiBold));
        panel.Children.Add(title);
        panel.Children.Add(T("从浏览器开发者工具复制 api.xiaoheihe.cn 的 Cookie。至少需要 x_xhh_tokenid；登录态需要 user_pkey。",
            12, secondary: true, wrap: true));
        _cookieBox = new TextBox
        {
            PlaceholderText = "x_xhh_tokenid=...; user_pkey=...; heybox_id=...",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 90,
            MaxLength = 16000,
            Text = _client.CookieHeader
        };
        panel.Children.Add(_cookieBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button save = AccentBtn("保存 Cookie", "\uE74E");
        save.Click += async (_, _) => await RunAsync(SaveManualCookieAsync);
        buttons.Children.Add(save);
        Button clear = Btn("清空本地登录态", "\uE7E8");
        clear.Click += async (_, _) => await RunAsync(ClearLocalSessionAsync);
        buttons.Children.Add(clear);
        panel.Children.Add(buttons);
        panel.Children.Add(T("Cookie 只保存在本机插件私有目录，不会上传。", 10, tertiary: true, wrap: true));
        return panel;
    }

    private FrameworkElement BuildAboutExpander()
    {
        var expander = new Expander
        {
            Header = T("关于与使用说明", 13, weight: FontWeights.SemiBold),
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var stack = new StackPanel { Spacing = 6, Padding = new Thickness(0, 8, 0, 2) };
        stack.Children.Add(T("本插件是第三方社区客户端，接口来自公开逆向研究，仅用于个人学习与交流。请遵守小黑盒服务条款，不要高频请求或批量采集。",
            12, secondary: true, wrap: true));
        stack.Children.Add(T("评论、点赞、收藏、投币、关注、签到、发帖都使用当前登录账号；视频上传等复杂操作请使用官方网页或客户端。",
            11, tertiary: true, wrap: true));
        expander.Content = stack;
        return expander;
    }

    private void SelectLoginMethod(int index)
    {
        if (_qrLoginPanel is null || _webLoginPanel is null || _cookieLoginPanel is null) return;
        _qrLoginPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        _webLoginPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        _cookieLoginPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTokenStatus()
    {
        if (_tokenStatusText is null) return;
        string pkey = _client.IsLoggedIn ? "已登录" : "未登录";
        string token = _client.HasRiskToken ? "风控令牌已获取" : "风控令牌缺失（帖子详情可能不可用）";
        _tokenStatusText.Text = $"{pkey} · {token}";
    }

    private async Task StartQrLoginAsync()
    {
        HideNotice();
        JsonElement result = await _client.QrcodeUrlAsync(_lifetime.Token);
        string qrUrl = result.Str("qr_url");
        if (string.IsNullOrWhiteSpace(qrUrl))
        {
            ShowNotice("服务器没有返回二维码内容。", InfoBarSeverity.Warning);
            return;
        }
        int expire = (int)result.Long("expire");
        if (expire <= 0) expire = 120;
        _qrValue = ExtractQueryValue(qrUrl, "qr");
        if (string.IsNullOrWhiteSpace(_qrValue))
        {
            ShowNotice("二维码链接中缺少 qr 参数。", InfoBarSeverity.Warning);
            return;
        }
        _qrFinished = false;
        _qrExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expire;
        if (_qrPlaceholder is not null) _qrPlaceholder.Visibility = Visibility.Collapsed;
        if (_qrImage is not null)
        {
            _qrImage.Source = await QrCodeService.ToBitmapAsync(qrUrl);
            _qrImage.Visibility = Visibility.Visible;
        }
        if (_qrStatus is not null)
            _qrStatus.Text = $"等待扫码…（{expire} 秒内有效）";
        StartQrTimer();
    }

    private void StartQrTimer()
    {
        _qrTimer ??= _dispatcher.CreateTimer();
        _qrTimer.Interval = TimeSpan.FromSeconds(1.5);
        _qrTimer.Tick -= QrTimerTick;
        _qrTimer.Tick += QrTimerTick;
        _qrTimer.Start();
    }

    private void StopQrTimer()
    {
        if (_qrTimer is null) return;
        _qrTimer.Stop();
        _qrTimer.Tick -= QrTimerTick;
    }

    private async void QrTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        if (_qrFinished || _qrPolling) return;
        _qrPolling = true;
        try
        {
            await PollQrAsync();
        }
        catch (XhhTwoFactorException)
        {
            _qrFinished = true;
            StopQrTimer();
            if (_qrStatus is not null) _qrStatus.Text = "该账号开启了 Google 两步验证，请使用网页登录完成验证。";
            ShowNotice("该账号开启了 Google 两步验证，请使用网页登录。", InfoBarSeverity.Warning,
                "打开网页登录", () => { _ = OpenWebLoginAsync(); return Task.CompletedTask; });
        }
        catch (XhhException ex)
        {
            _qrFinished = true;
            StopQrTimer();
            if (_qrStatus is not null) _qrStatus.Text = ex.Message;
        }
        catch (Exception ex)
        {
            _qrFinished = true;
            StopQrTimer();
            if (_qrStatus is not null) _qrStatus.Text = ex.Message;
        }
        finally
        {
            _qrPolling = false;
        }
    }

    private async Task PollQrAsync()
    {
        if (string.IsNullOrWhiteSpace(_qrValue) || _qrFinished) return;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > _qrExpiresAt)
        {
            _qrFinished = true;
            StopQrTimer();
            if (_qrStatus is not null) _qrStatus.Text = "二维码已过期，请点击「刷新二维码」。";
            return;
        }

        XhhQrState qrState = await _client.QrStateAsync(_qrValue, _lifetime.Token);
        JsonElement state = qrState.Result;
        string error = state.Str("error");
        if (error == "wait")
        {
            if (_qrStatus is not null) _qrStatus.Text = "等待扫码…";
            return;
        }
        if (error == "ready")
        {
            if (_qrStatus is not null) _qrStatus.Text = "已扫码，请在手机上确认登录。";
            return;
        }
        if (error == "ok")
        {
            _qrFinished = true;
            StopQrTimer();
            if (_qrStatus is not null) _qrStatus.Text = "已确认，正在换取登录态…";
            JsonElement profile = state.Obj("profile");
            JsonElement detail = state.Obj("account_detail");
            string pkey = state.FirstStr("pkey", "user_pkey");
            if (string.IsNullOrWhiteSpace(pkey)) pkey = qrState.Payload.FirstStr("pkey", "user_pkey");
            if (string.IsNullOrWhiteSpace(pkey)) pkey = profile.FirstStr("pkey", "user_pkey");
            if (string.IsNullOrWhiteSpace(pkey)) pkey = detail.FirstStr("pkey", "user_pkey");
            if (string.IsNullOrWhiteSpace(pkey)) pkey = _client.GetCookie("user_pkey") ?? _client.GetCookie("pkey") ?? "";
            string heyboxId = profile.FirstStr("heybox_id", "userid");
            if (string.IsNullOrWhiteSpace(heyboxId)) heyboxId = detail.FirstStr("userid", "heybox_id");
            if (string.IsNullOrWhiteSpace(heyboxId)) heyboxId = state.FirstStr("heybox_id", "userid");
            if (string.IsNullOrWhiteSpace(heyboxId)) heyboxId = _client.GetCookie("user_heybox_id") ?? _client.GetCookie("heybox_id") ?? "";
            if (string.IsNullOrWhiteSpace(pkey) || string.IsNullOrWhiteSpace(heyboxId))
            {
                try
                {
                    string debugDir = _host.Services.DataDirectory;
                    Directory.CreateDirectory(debugDir);
                    File.WriteAllText(Path.Combine(debugDir, "login-qr-state-error.json"), qrState.Payload.GetRawText());
                }
                catch (Exception)
                {
                }
                if (_qrStatus is not null) _qrStatus.Text = "扫码成功，但服务器没有返回 pkey / heybox_id。";
                return;
            }
            await CompleteLoginAsync(pkey, heyboxId, state);
            return;
        }

        _qrFinished = true;
        StopQrTimer();
        string message = state.Str("error_msg");
        if (_qrStatus is not null) _qrStatus.Text = string.IsNullOrWhiteSpace(message) ? "二维码已失效。" : message;
    }

    private async Task CompleteLoginAsync(string pkey, string heyboxId, JsonElement fallback)
    {
        XhhClient fresh = string.IsNullOrWhiteSpace(pkey) ? _client : _client.WithPkey(pkey, heyboxId);
        JsonElement account = default;
        try { account = await fresh.RestoreLoginAsync(_lifetime.Token); }
        catch (XhhException) { }
        _client = fresh;
        SaveSession(account, fallback);
        RefreshAccountChrome();
        UpdateTokenStatus();
        ShowNotice(_client.HasRiskToken
            ? "登录成功。"
            : "登录成功。帖子详情需要风控令牌，请打开「网页登录」读取一次。",
            _client.HasRiskToken ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        if (_qrStatus is not null) _qrStatus.Text = "登录成功。";
        BuildAccountPage();
    }

    private void SaveSession(JsonElement account, JsonElement fallback)
    {
        JsonElement profile = account.ValueKind == JsonValueKind.Object ? account.Obj("profile") : default;
        if (profile.ValueKind != JsonValueKind.Object && fallback.ValueKind == JsonValueKind.Object)
            profile = fallback.Obj("profile");
        JsonElement detail = account.ValueKind == JsonValueKind.Object ? account.Obj("account_detail") : default;
        if (detail.ValueKind != JsonValueKind.Object && fallback.ValueKind == JsonValueKind.Object)
            detail = fallback.Obj("account_detail");
        XhhUser user = XhhParser.ParseUser(profile.ValueKind == JsonValueKind.Object ? profile : detail);
        if (string.IsNullOrWhiteSpace(user.Name))
            user = XhhParser.ParseUser(detail.ValueKind == JsonValueKind.Object ? detail : profile);
        string heyboxId = profile.Str("heybox_id");
        if (string.IsNullOrWhiteSpace(heyboxId)) heyboxId = detail.Str("userid");
        if (string.IsNullOrWhiteSpace(heyboxId)) heyboxId = fallback.FirstStr("heybox_id", "userid");
        if (string.IsNullOrWhiteSpace(heyboxId)) heyboxId = _client.HeyboxId;
        if (!string.IsNullOrWhiteSpace(heyboxId)) _client.HeyboxId = heyboxId;
        _session = new XhhSession
        {
            Cookie = _client.CookieHeader,
            HeyboxId = heyboxId,
            Nickname = user.Name,
            Avatar = user.Avatar,
            Level = user.Level,
            SavedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        _store.Save(_session);
    }

    private async Task TryRestoreSessionAsync()
    {
        try
        {
            JsonElement account = await _client.RestoreLoginAsync(_lifetime.Token);
            SaveSession(account, default);
            RefreshAccountChrome();
        }
        catch (XhhAuthException)
        {
            ClearSession();
        }
        catch (XhhException)
        {
            // 网络暂时不可用时保留本地缓存资料，打开页面后仍可重试。
        }
    }

    private async Task RefreshAccountPageAsync()
    {
        if (!_client.IsLoggedIn)
        {
            ShowAccountPage();
            return;
        }
        try
        {
            JsonElement account = await _client.RestoreLoginAsync(_lifetime.Token);
            SaveSession(account, default);
            RefreshAccountChrome();
            ShowNotice("账号资料已刷新。", InfoBarSeverity.Success);
        }
        catch (XhhAuthException)
        {
            ClearSession();
            ShowNotice("登录态已失效，请重新登录。", InfoBarSeverity.Warning);
        }
        ShowAccountPage();
    }

    private async Task OpenWebLoginAsync()
    {
        if (_webHost is null || _webStatus is null) return;
        DisposeWebView();
        _webHost.Visibility = Visibility.Visible;
        try
        {
            _webView = new WebView2 { Height = 540, HorizontalAlignment = HorizontalAlignment.Stretch };
            _webHost.Children.Add(_webView);
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.NavigationCompleted += WebViewNavigationCompleted;
            _webStatus.Text = "正在加载小黑盒官方网页…";
            _webView.Source = new Uri("https://www.xiaoheihe.cn/app/bbs/home");
        }
        catch (Exception ex)
        {
            _webStatus.Text = "WebView2 初始化失败：" + ex.Message;
            ShowNotice("无法打开网页登录：WebView2 运行时不可用。请确认系统已安装 WebView2 Runtime。", InfoBarSeverity.Error);
        }
    }

    private void WebViewNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (_webStatus is null) return;
        _webStatus.Text = args.IsSuccess
            ? "页面已加载。请登录小黑盒，然后点击「读取登录状态」。"
            : "页面加载失败，请检查网络后重试。";
    }

    private async Task ReadWebCookiesAsync()
    {
        if (_webView?.CoreWebView2 is null)
        {
            ShowNotice("请先点击「打开官方网页」。", InfoBarSeverity.Warning);
            return;
        }
        if (_webStatus is not null) _webStatus.Text = "正在读取 Cookie…";
        var cookies = new List<CoreWebView2Cookie>();
        foreach (string url in new[]
                 {
                     "https://www.xiaoheihe.cn",
                     "https://api.xiaoheihe.cn",
                     "https://login.xiaoheihe.cn"
                 })
        {
            try
            {
                var list = await _webView.CoreWebView2.CookieManager.GetCookiesAsync(url);
                cookies.AddRange(list);
            }
            catch (Exception)
            {
            }
        }

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (CoreWebView2Cookie cookie in cookies)
        {
            if (!cookie.Domain.Contains("xiaoheihe.cn", StringComparison.OrdinalIgnoreCase)) continue;
            dict[cookie.Name] = cookie.Value;
        }
        if (dict.Count == 0)
        {
            if (_webStatus is not null) _webStatus.Text = "没有读取到小黑盒 Cookie，请确认页面已加载并完成登录。";
            ShowNotice("没有读取到小黑盒 Cookie。", InfoBarSeverity.Warning);
            return;
        }

        string header = string.Join("; ", dict.Select(kv => $"{kv.Key}={kv.Value}"));
        _client.LoadCookie(header);
        bool hasToken = _client.HasRiskToken;
        bool hasPkey = _client.IsLoggedIn;
        if (hasPkey)
        {
            try
            {
                JsonElement account = await _client.RestoreLoginAsync(_lifetime.Token);
                SaveSession(account, default);
            }
            catch (XhhException)
            {
                _session ??= new XhhSession { Cookie = _client.CookieHeader, HeyboxId = _client.HeyboxId };
                _session.Cookie = _client.CookieHeader;
                _store.Save(_session);
            }
        }
        else
        {
            _session = new XhhSession
            {
                Cookie = _client.CookieHeader,
                HeyboxId = _client.HeyboxId,
                SavedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            _store.Save(_session);
        }

        RefreshAccountChrome();
        UpdateTokenStatus();
        string status = $"读取到 {dict.Count} 个 Cookie · x_xhh_tokenid：{(hasToken ? "已获取" : "缺失")} · user_pkey：{(hasPkey ? "已登录" : "未登录")}";
        if (_webStatus is not null) _webStatus.Text = status;
        ShowNotice(hasPkey && hasToken
            ? "登录状态与风控令牌已更新。"
            : hasPkey ? "已登录，但没有取得风控令牌；帖子详情可能仍不可用。" : "已取得风控令牌，但未检测到登录态。",
            hasPkey && hasToken ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        if (_accountStatusText is not null)
            _accountStatusText.Text = _client.IsLoggedIn
                ? $"{_session?.Nickname ?? "已登录"}{(string.IsNullOrWhiteSpace(_session?.HeyboxId) ? "" : $" · ID {_session.HeyboxId}")}"
                : "尚未登录";
    }

    private async Task SaveManualCookieAsync()
    {
        if (_cookieBox is null) return;
        string text = _cookieBox.Text.Trim();
        if (text.Length == 0)
        {
            ShowNotice("请先粘贴 Cookie。", InfoBarSeverity.Warning);
            return;
        }
        _client = new XhhClient(text);
        if (_client.IsLoggedIn)
        {
            try
            {
                JsonElement account = await _client.RestoreLoginAsync(_lifetime.Token);
                SaveSession(account, default);
            }
            catch (XhhException)
            {
                _session = new XhhSession { Cookie = _client.CookieHeader, HeyboxId = _client.HeyboxId, SavedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
                _store.Save(_session);
            }
        }
        else
        {
            _session = new XhhSession { Cookie = _client.CookieHeader, HeyboxId = _client.HeyboxId, SavedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            _store.Save(_session);
        }
        RefreshAccountChrome();
        UpdateTokenStatus();
        if (_accountStatusText is not null)
            _accountStatusText.Text = _client.IsLoggedIn
                ? $"{_session?.Nickname ?? "已登录"}{(string.IsNullOrWhiteSpace(_session?.HeyboxId) ? "" : $" · ID {_session.HeyboxId}")}"
                : "尚未登录";
        ShowNotice(_client.IsLoggedIn ? "Cookie 已保存。" : "Cookie 已保存，但没有检测到登录态。",
            _client.IsLoggedIn ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    private async Task ClearLocalSessionAsync()
    {
        bool confirmed = await _host.ConfirmAsync("清除本地登录态", "会删除插件保存的 Cookie 与账号信息，但不会退出小黑盒账号本身。");
        if (!confirmed) return;
        ClearSession();
        ShowNotice("本地登录态已清除。", InfoBarSeverity.Informational);
        ShowAccountPage();
    }

    private void DisposeWebView()
    {
        if (_webView is null) return;
        try { _webView.NavigationCompleted -= WebViewNavigationCompleted; } catch (Exception) { }
        try { _webView.Close(); } catch (Exception) { }
        try { _webHost?.Children.Remove(_webView); } catch (Exception) { }
        _webView = null;
        if (_webHost is not null) _webHost.Visibility = Visibility.Collapsed;
    }

    private static string ExtractQueryValue(string url, string key)
    {
        try
        {
            var uri = new Uri(url);
            foreach (string part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int index = part.IndexOf('=');
                if (index <= 0) continue;
                if (string.Equals(part[..index], key, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(part[(index + 1)..]);
            }
        }
        catch (UriFormatException)
        {
        }
        return "";
    }
}

internal static class TextBlockCenterExtensions
{
    public static TextBlock Center(this TextBlock block)
    {
        block.HorizontalAlignment = HorizontalAlignment.Center;
        return block;
    }
}
