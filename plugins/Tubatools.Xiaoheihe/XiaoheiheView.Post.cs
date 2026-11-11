using System.Text.Json;
using Microsoft.UI.Text;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Tubatools.Xiaoheihe.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.Streaming.Adaptive;

namespace Tubatools.Xiaoheihe;

internal sealed partial class XiaoheiheView
{
    private XhhPost? _currentPost;
    private XhhCommentPage? _commentPage;
    private int _commentPageIndex = 1;
    private string _commentSort = "hot";
    private bool _commentsLoaded;
    private bool _commentSortUpdating;
    private StackPanel? _postBodyPanel;
    private StackPanel? _commentSurface;
    private TextBox? _commentBox;
    private TextBlock? _replyHint;
    private ComboBox? _commentSortBox;
    private TextBlock? _commentCountText;
    private Button? _likeButton, _favouriteButton, _coinButton;
    private TextBlock? _likeCountText, _favouriteCountText, _coinCountText;
    private bool _postLiked, _postFavourited, _postCoined;
    private string _replyCommentId = "-1";
    private string _replyRootId = "-1";
    private string _replyUserName = "";
    private readonly HashSet<string> _likedComments = new(StringComparer.Ordinal);

    private async Task OpenPostAsync(XhhPost post)
    {
        _currentPost = post;
        _commentPage = null;
        _commentPageIndex = 1;
        _commentSort = "hot";
        _commentsLoaded = false;
        _postLiked = post.IsLiked;
        _postFavourited = post.IsFavourited;
        _postCoined = post.IsCoined;
        _replyCommentId = "-1";
        _replyRootId = "-1";
        _replyUserName = "";
        _pageTitle.Text = "帖子详情";
        _pageSubtitle.Text = string.IsNullOrWhiteSpace(post.TopicName) ? "小黑盒社区" : post.TopicName;
        _refreshAction = () => ReloadPostAsync();
        RenderPostPage();
        await RunAsync(async () =>
        {
            try
            {
                await LoadPostDetailAsync();
            }
            catch
            {
                _commentsLoaded = true;
                RenderComments();
                throw;
            }
        });
    }

    private async Task ReloadPostAsync()
    {
        if (_currentPost is null) return;
        _commentsLoaded = false;
        _commentPage = null;
        _commentPageIndex = 1;
        await LoadPostDetailAsync();
    }

    private async Task LoadPostDetailAsync()
    {
        if (_currentPost is null || string.IsNullOrWhiteSpace(_currentPost.Id)) return;
        JsonElement result = await _client.LinkTreeAsync(_currentPost.Id, 1, 20, _commentSort, true, _lifetime.Token);
        JsonElement link = result.Obj("link");
        if (link.ValueKind == JsonValueKind.Object)
            _currentPost = XhhParser.ParsePost(link);
        _commentPage = XhhParser.ParseCommentPage(result, 1);
        _commentPageIndex = 1;
        _commentsLoaded = true;
        _postLiked |= _currentPost.IsLiked;
        _postFavourited |= _currentPost.IsFavourited;
        _postCoined |= _currentPost.IsCoined;
        RenderPostPage();
    }

    private void RenderPostPage()
    {
        if (_currentPost is null) return;
        string draft = _commentBox?.Text ?? "";
        var surface = Surface(980);
        surface.Children.Add(BuildPostHeader());
        var bodyCard = Card(18);
        _postBodyPanel = new StackPanel { Spacing = 14 };
        bodyCard.Child = _postBodyPanel;
        surface.Children.Add(bodyCard);
        surface.Children.Add(BuildPostActionBar());
        surface.Children.Add(BuildComposer(draft));
        surface.Children.Add(BuildCommentsHeader());
        _commentSurface = new StackPanel { Spacing = 10 };
        surface.Children.Add(_commentSurface);
        ShowPage(Scroll(surface));
        RenderPostBody();
        RenderComments();
    }

    private FrameworkElement BuildPostHeader()
    {
        XhhPost post = _currentPost!;
        var card = Card(18);
        var stack = new StackPanel { Spacing = 12 };

        TextBlock title = T(string.IsNullOrWhiteSpace(post.Title) ? "无标题" : post.Title, 24,
            weight: FontWeights.SemiBold, wrap: true);
        title.IsTextSelectionEnabled = true;
        stack.Children.Add(title);

        var authorRow = new Grid { ColumnSpacing = 12 };
        authorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        authorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        authorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        authorRow.Children.Add(Avatar(post.AuthorAvatar, 38));

        var authorText = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        nameRow.Children.Add(T(string.IsNullOrWhiteSpace(post.AuthorName) ? "匿名用户" : post.AuthorName, 14,
            weight: FontWeights.SemiBold));
        if (!string.IsNullOrWhiteSpace(post.AuthorLevelText)) nameRow.Children.Add(Pill(post.AuthorLevelText));
        if (!string.IsNullOrWhiteSpace(post.TopicName)) nameRow.Children.Add(Pill(post.TopicName, accent: true));
        authorText.Children.Add(nameRow);
        string meta = string.Join(" · ", new[] { post.TimeText, post.IpLocation }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (meta.Length > 0) authorText.Children.Add(T(meta, 11, secondary: true));
        Grid.SetColumn(authorText, 1);
        authorRow.Children.Add(authorText);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrWhiteSpace(post.AuthorId))
        {
            Button profile = Btn("查看主页", "\uE77B");
            profile.Click += async (_, _) => await OpenAuthorAsync(post);
            buttons.Children.Add(profile);
        }
        Button copy = IconButton("\uE8C8", "复制帖子链接");
        copy.Click += async (_, _) =>
        {
            await _host.Services.CopyTextAsync(post.WebUrl);
            ShowNotice("帖子链接已复制。", InfoBarSeverity.Success);
        };
        buttons.Children.Add(copy);
        Button open = IconButton("\uE774", "在浏览器中打开");
        open.Click += async (_, _) => await _host.Services.OpenUriAsync(new Uri(post.WebUrl));
        buttons.Children.Add(open);
        Grid.SetColumn(buttons, 2);
        authorRow.Children.Add(buttons);

        stack.Children.Add(authorRow);
        card.Child = stack;
        return card;
    }

    private void RenderPostBody()
    {
        if (_postBodyPanel is null || _currentPost is null) return;
        _postBodyPanel.Children.Clear();
        XhhPost post = _currentPost;

        bool hasBody = false;
        if (IsRichArticle(post))
        {
            FrameworkElement? article = BuildArticle(post.Html);
            if (article is not null)
            {
                _postBodyPanel.Children.Add(article);
                hasBody = true;
            }
        }

        if (!hasBody)
        {
            if (!string.IsNullOrWhiteSpace(post.Text))
            {
                var text = EmojiText(post.Text, 14, wrap: true);
                text.LineHeight = 23;
                _postBodyPanel.Children.Add(text);
                hasBody = true;
            }

            if (post.Images.Count > 0)
            {
                var gallery = new StackPanel { Spacing = 10 };
                foreach (string image in post.Images)
                    gallery.Children.Add(BuildDetailImage(image));
                _postBodyPanel.Children.Add(gallery);
                hasBody = true;
            }
        }

        if (post.HasVideo || !string.IsNullOrWhiteSpace(post.VideoUrl))
        {
            var videoCard = Card(12);
            var videoStack = new StackPanel { Spacing = 10 };
            var thumb = new Grid { Height = 360, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Center };
            if (!string.IsNullOrWhiteSpace(post.VideoThumb))
            {
                thumb.Children.Add(RemoteImage(post.VideoThumb, 720, 360, 8));
            }
            else
            {
                thumb.Children.Add(new Border
                {
                    Height = 360,
                    CornerRadius = new CornerRadius(8),
                    Background = _palette.Subtle,
                    Child = new FontIcon { Glyph = "\uE714", FontSize = 42, Foreground = _palette.TertiaryText }
                });
            }
            thumb.Children.Add(new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius = new CornerRadius(32),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Black) { Opacity = 0.58 },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new FontIcon { Glyph = "\uE768", FontSize = 24, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) }
            });
            if (!string.IsNullOrWhiteSpace(post.VideoUrl))
                thumb.Tapped += async (_, _) => await RunAsync(() => ShowVideoAsync(post.VideoUrl));
            videoStack.Children.Add(thumb);

            var videoButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (!string.IsNullOrWhiteSpace(post.VideoUrl))
            {
                Button play = AccentBtn("播放视频", "\uE768");
                play.Click += async (_, _) => await RunAsync(() => ShowVideoAsync(post.VideoUrl));
                videoButtons.Children.Add(play);
            }
            Button open = Btn("浏览器打开", "\uE774");
            open.Click += async (_, _) => await _host.Services.OpenUriAsync(new Uri(post.WebUrl));
            videoButtons.Children.Add(open);
            videoStack.Children.Add(videoButtons);
            if (string.IsNullOrWhiteSpace(post.VideoUrl))
                videoStack.Children.Add(T("当前接口没有返回视频直链，请在浏览器或小黑盒客户端中打开。", 11, tertiary: true, wrap: true));
            videoCard.Child = videoStack;
            _postBodyPanel.Children.Add(videoCard);
        }

        if (!hasBody && !post.HasVideo && string.IsNullOrWhiteSpace(post.VideoUrl))
            _postBodyPanel.Children.Add(T("该帖子没有可显示的正文内容。", 13, secondary: true));
    }

    private FrameworkElement BuildDetailImage(string url)
    {
        var image = new Image
        {
            Source = LoadImage(url, 1600),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var border = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = _palette.Subtle,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = image,
            MinHeight = 80
        };
        ToolTipService.SetToolTip(border, "点击查看大图");
        border.Tapped += async (_, _) => await RunAsync(() => ShowImageAsync(url));
        return border;
    }

    private bool _mediaDialogOpen;
    private Grid? _mediaOverlay;
    private Panel? _mediaOverlayParent;
    private Action? _mediaCleanup;
    private CancellationTokenSource? _mediaCts;

    private (Grid Overlay, Grid Host) CreateMediaOverlay(string title, string actionText, Action action)
    {
        CloseMediaOverlay();
        var overlay = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black) { Opacity = 0.96 },
            RequestedTheme = ElementTheme.Dark,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Panel parent = _root.XamlRoot?.Content as Panel ?? _root;
        if (parent is Grid parentGrid)
        {
            Grid.SetRowSpan(overlay, Math.Max(1, parentGrid.RowDefinitions.Count));
            Grid.SetColumnSpan(overlay, Math.Max(1, parentGrid.ColumnDefinitions.Count));
        }
        else
        {
            overlay.Width = _root.XamlRoot?.Size.Width ?? 1280;
            overlay.Height = _root.XamlRoot?.Size.Height ?? 800;
        }
        var top = new Grid { Margin = new Thickness(24, 64, 24, 0), VerticalAlignment = VerticalAlignment.Top };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock titleText = T(title, 15, weight: FontWeights.SemiBold);
        titleText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
        top.Children.Add(titleText);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button original = Btn(actionText, "\uE774");
        original.Click += (_, _) => action();
        tools.Children.Add(original);
        Button close = IconButton("\uE711", "关闭");
        close.Click += (_, _) => CloseMediaOverlay();
        tools.Children.Add(close);
        Grid.SetColumn(tools, 1);
        top.Children.Add(tools);
        overlay.Children.Add(top);
        var host = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(32, 64, 32, 48)
        };
        overlay.Children.Add(host);
        overlay.Tapped += (_, _) => CloseMediaOverlay();
        host.Tapped += (_, e) => e.Handled = true;
        parent.Children.Add(overlay);
        _mediaOverlay = overlay;
        _mediaOverlayParent = parent;
        _mediaDialogOpen = true;
        overlay.Opacity = 1;
        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, overlay);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Begin();
        return (overlay, host);
    }

    private void CloseMediaOverlay()
    {
        if (_mediaOverlay is not null && _mediaOverlayParent is not null)
            _mediaOverlayParent.Children.Remove(_mediaOverlay);
        _mediaOverlay = null;
        _mediaOverlayParent = null;
        try { _mediaCleanup?.Invoke(); } catch (Exception) { }
        try { _mediaCts?.Cancel(); } catch (Exception) { }
        _mediaCts = null;
        _mediaCleanup = null;
        _mediaDialogOpen = false;
    }

    private async Task ShowImageAsync(string url)
    {
        if (_mediaDialogOpen) return;
        url = NormalizeImageUrl(url);
        (_, Grid host) = CreateMediaOverlay("查看图片", "查看原图",
            () => { _ = _host.Services.OpenUriAsync(new Uri(url)); });
        try
        {
            BitmapImage? bitmap = LoadImage(url, 2400) as BitmapImage;
            if (bitmap is null)
            {
                bitmap = new BitmapImage { DecodePixelWidth = 2400 };
                var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                bitmap.ImageOpened += (_, _) => loaded.TrySetResult(true);
                bitmap.ImageFailed += (_, _) => loaded.TrySetResult(false);
                bitmap.UriSource = new Uri(url);
                await Task.WhenAny(loaded.Task, Task.Delay(2000));
            }
            else if (bitmap.PixelWidth == 0)
            {
                var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                bitmap.ImageOpened += (_, _) => loaded.TrySetResult(true);
                bitmap.ImageFailed += (_, _) => loaded.TrySetResult(false);
                await Task.WhenAny(loaded.Task, Task.Delay(2000));
            }
            double pixelWidth = bitmap.PixelWidth > 0 ? bitmap.PixelWidth : 900;
            double pixelHeight = bitmap.PixelHeight > 0 ? bitmap.PixelHeight : 600;
            double screenWidth = _root.XamlRoot?.Size.Width ?? 1280;
            double screenHeight = _root.XamlRoot?.Size.Height ?? 800;
            double maxWidth = Math.Max(240, screenWidth - 96);
            double maxHeight = Math.Max(180, screenHeight - 96);
            double scale = Math.Min(1, Math.Min(maxWidth / pixelWidth, maxHeight / pixelHeight));
            double displayWidth = Math.Max(1, pixelWidth * scale);
            double displayHeight = Math.Max(1, pixelHeight * scale);
            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                Width = displayWidth,
                Height = displayHeight,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            };
            var viewer = new ScrollViewer
            {
                Content = image,
                ZoomMode = ZoomMode.Enabled,
                MinZoomFactor = 0.5f,
                MaxZoomFactor = 4f,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Top,
                Width = displayWidth,
                Height = Math.Min(displayHeight, maxHeight)
            };
            host.Children.Add(viewer);
        }
        catch (Exception ex)
        {
            host.Children.Add(T("图片加载失败：" + ex.Message, 14, secondary: true, wrap: true));
        }
    }

    private static async Task LoadNativeVideoAsync(MediaPlayer mediaPlayer, TextBlock status,
        ProgressRing ring, string url, CancellationToken ct)
    {
        try
        {
            if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                Task<AdaptiveMediaSourceCreationResult> create =
                    AdaptiveMediaSource.CreateFromUriAsync(new Uri(url)).AsTask();
                Task completed = await Task.WhenAny(create, Task.Delay(8000, ct));
                if (ct.IsCancellationRequested) return;
                if (completed != create)
                {
                    ring.IsActive = false;
                    status.Text = "HLS 视频流加载超时，请用浏览器打开。";
                    return;
                }
                AdaptiveMediaSourceCreationResult adaptive = await create;
                if (ct.IsCancellationRequested) return;
                if (adaptive.Status != AdaptiveMediaSourceCreationStatus.Success)
                {
                    ring.IsActive = false;
                    status.Text = "HLS 视频流加载失败，请用浏览器打开。";
                    return;
                }
                mediaPlayer.Source = MediaSource.CreateFromAdaptiveMediaSource(adaptive.MediaSource);
            }
            else
            {
                mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(url));
            }
            if (ct.IsCancellationRequested) return;
            mediaPlayer.Play();
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            ring.IsActive = false;
            status.Text = "视频加载失败：" + ex.Message;
        }
    }

    private void GetVideoSize(out double width, out double height)
    {
        double screenWidth = _root.XamlRoot?.Size.Width ?? 1280;
        double screenHeight = _root.XamlRoot?.Size.Height ?? 800;
        double maxWidth = Math.Max(360, screenWidth - 120);
        double maxHeight = Math.Max(220, screenHeight - 200);
        width = Math.Min(1000, maxWidth);
        height = width * 9 / 16;
        if (height > maxHeight)
        {
            height = maxHeight;
            width = height * 16 / 9;
        }
    }

    private async Task ShowVideoAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || _mediaDialogOpen) return;
        _inlineGamePaused = true;
        try
        {
            _inlineGameMediaPlayer?.Pause();
            _inlineGameMediaPlayer!.Source = null;
        }
        catch (Exception) { }
        (_, Grid host) = CreateMediaOverlay("播放视频", "浏览器打开",
            () => { _ = _host.Services.OpenUriAsync(new Uri(url)); });
        try
        {
            GetVideoSize(out double width, out double height);
            var mediaPlayer = new MediaPlayer { AutoPlay = true };
            var player = new MediaPlayerElement
            {
                Width = width,
                Height = height,
                AreTransportControlsEnabled = true,
                Stretch = Stretch.Uniform,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Black)
            };
            player.SetMediaPlayer(mediaPlayer);

            var status = new TextBlock
            {
                Text = "正在加载视频…",
                FontSize = 13,
                Foreground = _palette.SecondaryText,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            var ring = new ProgressRing { IsActive = true, Width = 32, Height = 32 };
            var loading = new StackPanel
            {
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            loading.Children.Add(ring);
            loading.Children.Add(status);
            var frame = new Grid { Width = width, Height = height };
            frame.Children.Add(player);
            frame.Children.Add(loading);
            host.Children.Add(frame);

            bool opened = false;
            bool failed = false;
            mediaPlayer.MediaOpened += (_, _) =>
            {
                opened = true;
                _root.DispatcherQueue.TryEnqueue(() => loading.Visibility = Visibility.Collapsed);
            };
            mediaPlayer.MediaFailed += (_, args) =>
            {
                failed = true;
                _root.DispatcherQueue.TryEnqueue(() =>
                {
                    ring.IsActive = false;
                    status.Text = "视频加载失败：" + (string.IsNullOrWhiteSpace(args.ErrorMessage)
                        ? "当前格式可能不受支持，请用浏览器打开。"
                        : args.ErrorMessage);
                });
            };
            mediaPlayer.PlaybackSession.PlaybackStateChanged += (_, _) =>
            {
                MediaPlaybackState state = mediaPlayer.PlaybackSession.PlaybackState;
                if (state is MediaPlaybackState.Playing or MediaPlaybackState.Buffering)
                    _root.DispatcherQueue.TryEnqueue(() => loading.Visibility = Visibility.Collapsed);
            };
            var mediaCts = new CancellationTokenSource();
            _mediaCts = mediaCts;
            _ = LoadNativeVideoAsync(mediaPlayer, status, ring, url, mediaCts.Token);
            _ = Task.Run(async () =>
            {
                await Task.Delay(2500);
                _root.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!failed) loading.Visibility = Visibility.Collapsed;
                });
            });
            _ = Task.Run(async () =>
            {
                await Task.Delay(8000);
                _root.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!opened && !failed && !mediaCts.IsCancellationRequested)
                    {
                        ring.IsActive = false;
                        status.Text = "视频加载超时，请稍后重试或使用浏览器打开。";
                    }
                });
            });
            _mediaCleanup = () =>
            {
                try { mediaCts.Cancel(); } catch (Exception) { }
                try
                {
                    mediaPlayer.Pause();
                    mediaPlayer.Source = null;
                }
                catch (Exception) { }
            };
        }
        catch (Exception ex)
        {
            host.Children.Add(T("视频播放失败：" + ex.Message, 14, secondary: true, wrap: true));
        }
    }

    private async Task ShowHlsVideoAsync(string url)
    {
        if (_mediaDialogOpen) return;
        (_, Grid host) = CreateMediaOverlay("播放视频", "浏览器打开",
            () => { _ = _host.Services.OpenUriAsync(new Uri(url)); });
        try
        {
            GetVideoSize(out double width, out double height);
            var ring = new ProgressRing { IsActive = true, Width = 32, Height = 32 };
            var status = new TextBlock
            {
                Text = "正在加载视频流…",
                FontSize = 13,
                Foreground = _palette.SecondaryText,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            var loading = new StackPanel
            {
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            loading.Children.Add(ring);
            loading.Children.Add(status);
            var webView = new WebView2 { Width = width, Height = height };
            var frame = new Grid { Width = width, Height = height };
            frame.Children.Add(webView);
            frame.Children.Add(loading);
            host.Children.Add(frame);
            _mediaCleanup = () =>
            {
                try
                {
                    webView.CoreWebView2?.Stop();
                    webView.Close();
                }
                catch (Exception) { }
            };
            _ = InitializeHlsWebViewAsync(webView, url, ring, status, loading);
        }
        catch (Exception ex)
        {
            host.Children.Add(T("视频播放失败：" + ex.Message, 14, secondary: true, wrap: true));
        }
    }

    private async Task InitializeHlsWebViewAsync(WebView2 webView, string url, ProgressRing ring,
        TextBlock status, FrameworkElement loading)
    {
        try
        {
            await webView.EnsureCoreWebView2Async();
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            string hlsScript = LoadEmbeddedText("hls.min.js");
            if (!string.IsNullOrWhiteSpace(hlsScript))
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(hlsScript);
            webView.NavigationCompleted += (_, args) =>
            {
                ring.IsActive = false;
                if (args.IsSuccess)
                    loading.Visibility = Visibility.Collapsed;
                else
                    status.Text = "视频页面加载失败，请用浏览器打开。";
            };
            webView.CoreWebView2.DocumentTitleChanged += (_, _) =>
            {
                if (webView.CoreWebView2.DocumentTitle == "HLS_ERROR")
                {
                    ring.IsActive = false;
                    loading.Visibility = Visibility.Visible;
                    status.Text = "视频流加载失败，请用浏览器打开。";
                }
            };

            string source = JsonSerializer.Serialize(url);
            string html = "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
                          "<style>html,body{margin:0;background:#000;height:100%;overflow:hidden}" +
                          "video{width:100%;height:100%;object-fit:contain}</style></head><body>" +
                          "<video id=\"v\" controls autoplay playsinline></video><script>" +
                          "const src=" + source + ";const v=document.getElementById('v');" +
                          "if(window.Hls&&Hls.isSupported()){const h=new Hls({enableWorker:true});" +
                          "h.loadSource(src);h.attachMedia(v);" +
                          "h.on(Hls.Events.MANIFEST_PARSED,()=>{v.play().catch(()=>{});});" +
                          "h.on(Hls.Events.ERROR,(e,d)=>{if(d.fatal){document.title='HLS_ERROR';}});" +
                          "}else{v.src=src;v.play().catch(()=>{});}</script></body></html>";
            webView.NavigateToString(html);
        }
        catch (Exception ex)
        {
            ring.IsActive = false;
            status.Text = "视频播放器初始化失败：" + ex.Message;
        }
    }

    private async Task InitializeInlineHlsAsync(WebView2 webView, string url)
    {
        try
        {
            await webView.EnsureCoreWebView2Async();
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            string hlsScript = LoadEmbeddedText("hls.min.js");
            if (!string.IsNullOrWhiteSpace(hlsScript))
                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(hlsScript);
            string source = JsonSerializer.Serialize(url);
            string html = "<!DOCTYPE html><html><head><meta charset=\"utf-8\">" +
                          "<style>html,body{margin:0;background:#000;height:100%;overflow:hidden}" +
                          "video{width:100%;height:100%;object-fit:cover}</style></head><body>" +
                          "<video id=\"v\" autoplay muted loop playsinline></video><script>" +
                          "const src=" + source + ";const v=document.getElementById('v');v.muted=true;" +
                          "if(window.Hls&&Hls.isSupported()){const h=new Hls({enableWorker:true});" +
                          "h.loadSource(src);h.attachMedia(v);" +
                          "h.on(Hls.Events.MANIFEST_PARSED,()=>{v.play().catch(()=>{});});" +
                          "}else{v.src=src;v.play().catch(()=>{});}</script></body></html>";
            webView.NavigateToString(html);
        }
        catch (Exception)
        {
        }
    }

    private static string LoadEmbeddedText(string suffix)
    {
        var assembly = typeof(XiaoheiheView).Assembly;
        string? name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (name is null) return "";
        using Stream? stream = assembly.GetManifestResourceStream(name);
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private FrameworkElement BuildPostActionBar()
    {
        XhhPost post = _currentPost!;
        var card = Card(12);
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _likeButton = ActionButton("\uE8E1", "点赞", XhhFormat.Count(post.LikeCount), out _likeCountText);
        _likeButton.Click += async (_, _) => await TogglePostActionAsync(1);
        grid.Children.Add(_likeButton);

        _favouriteButton = ActionButton("\uE734", "收藏", XhhFormat.Count(0), out _favouriteCountText);
        _favouriteButton.Click += async (_, _) => await TogglePostActionAsync(2);
        Grid.SetColumn(_favouriteButton, 1);
        grid.Children.Add(_favouriteButton);

        _coinButton = ActionButton("\uE8C7", "投币", XhhFormat.Count(post.CoinCount), out _coinCountText);
        _coinButton.Click += async (_, _) => await TogglePostActionAsync(3);
        Grid.SetColumn(_coinButton, 2);
        grid.Children.Add(_coinButton);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(Stat("\uE8BD", $"评论 {XhhFormat.Count(post.CommentCount)}"));
        Grid.SetColumn(right, 4);
        grid.Children.Add(right);
        card.Child = grid;
        UpdatePostActionState();
        return card;
    }

    private Button ActionButton(string glyph, string label, string count, out TextBlock countText)
    {
        countText = T(count, 11, secondary: true);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12 });
        panel.Children.Add(countText);
        return new Button
        {
            Content = panel,
            Padding = new Thickness(11, 6, 11, 6),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1)
        };
    }

    private void UpdatePostActionState()
    {
        if (_currentPost is null) return;
        ApplyActionStyle(_likeButton, _postLiked);
        ApplyActionStyle(_favouriteButton, _postFavourited);
        ApplyActionStyle(_coinButton, _postCoined);
        if (_likeCountText is not null) _likeCountText.Text = XhhFormat.Count(_currentPost.LikeCount);
        if (_favouriteCountText is not null) _favouriteCountText.Text = _postFavourited ? "已收藏" : "";
        if (_coinCountText is not null) _coinCountText.Text = XhhFormat.Count(_currentPost.CoinCount);
    }

    private void ApplyActionStyle(Button? button, bool active)
    {
        if (button is null) return;
        button.Background = active ? _palette.AccentSoft : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.BorderBrush = active ? _palette.Accent : _palette.CardStroke;
    }

    private async Task TogglePostActionAsync(int type)
    {
        if (_currentPost is null) return;
        if (!_client.IsLoggedIn)
        {
            ShowNotice("请先登录小黑盒账号。", InfoBarSeverity.Warning, "去登录", async () => await NavigateAsync("account"));
            return;
        }

        string id = _currentPost.Id;
        await RunAsync(async () =>
        {
            if (type == 1)
            {
                bool next = !_postLiked;
                await _client.FavourAsync(id, next, 1, _lifetime.Token);
                _postLiked = next;
                _currentPost!.LikeCount = Math.Max(0, _currentPost.LikeCount + (next ? 1 : -1));
            }
            else if (type == 2)
            {
                bool next = !_postFavourited;
                await _client.FavourAsync(id, next, 2, _lifetime.Token);
                _postFavourited = next;
            }
            else
            {
                bool next = !_postCoined;
                await _client.AwardAsync(id, next, _lifetime.Token);
                _postCoined = next;
                _currentPost!.CoinCount = Math.Max(0, _currentPost.CoinCount + (next ? 1 : -1));
            }
            UpdatePostActionState();
        });
    }

    private FrameworkElement BuildComposer(string draft)
    {
        var card = Card(16);
        var stack = new StackPanel { Spacing = 9 };

        var hintRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Visibility = Visibility.Collapsed };
        _replyHint = T("", 11, accent: true);
        hintRow.Children.Add(_replyHint);
        Button cancelReply = Btn("取消回复");
        cancelReply.Click += (_, _) =>
        {
            _replyCommentId = "-1";
            _replyRootId = "-1";
            _replyUserName = "";
            _replyHint!.Text = "";
            hintRow.Visibility = Visibility.Collapsed;
        };
        hintRow.Children.Add(cancelReply);
        if (_replyUserName.Length > 0)
        {
            _replyHint.Text = $"正在回复 @{_replyUserName}";
            hintRow.Visibility = Visibility.Visible;
        }
        stack.Children.Add(hintRow);

        _commentBox = new TextBox
        {
            PlaceholderText = "说点什么…",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 78,
            MaxLength = 2000,
            Text = draft
        };
        stack.Children.Add(_commentBox);

        var footer = new Grid { ColumnSpacing = 10 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string status = _client.IsLoggedIn ? "理性讨论，友善发言。" : "登录后可以发表评论。";
        footer.Children.Add(T(status, 11, tertiary: true, wrap: true));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button emoji = IconButton("\uE76E", "插入表情");
        emoji.Click += (_, _) => ShowEmojiFlyout(emoji, token =>
        {
            if (_commentBox is not null) InsertTextAtCaret(_commentBox, token);
        });
        actions.Children.Add(emoji);
        Button send = AccentBtn("发布评论", "\uE724");
        send.IsEnabled = _client.IsLoggedIn;
        send.Click += async (_, _) => await RunAsync(SendCommentAsync);
        actions.Children.Add(send);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        stack.Children.Add(footer);

        card.Child = stack;
        return card;
    }

    private async Task SendCommentAsync()
    {
        if (_currentPost is null || _commentBox is null) return;
        string text = _commentBox.Text.Trim();
        if (text.Length == 0)
        {
            ShowNotice("请输入评论内容。", InfoBarSeverity.Warning);
            return;
        }
        await _client.CommentCreateAsync(_currentPost.Id, text, _replyCommentId, _replyRootId, _lifetime.Token);
        _commentBox.Text = "";
        _replyCommentId = "-1";
        _replyRootId = "-1";
        _replyUserName = "";
        ShowNotice("评论已发布。", InfoBarSeverity.Success);
        await ReloadCommentsAsync();
    }

    private FrameworkElement BuildCommentsHeader()
    {
        var card = Card(13);
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _commentCountText = T(_commentPage is null
            ? "评论"
            : $"评论 ({XhhFormat.Count(_commentPage.TotalFloor > 0 ? _commentPage.TotalFloor : _commentPage.Comments.Count)})",
            17, weight: FontWeights.SemiBold);
        grid.Children.Add(_commentCountText);

        _commentSortUpdating = true;
        _commentSortBox = new ComboBox { Width = 108, SelectedIndex = _commentSort switch { "time_desc" => 1, "time_aes" => 2, _ => 0 } };
        _commentSortBox.Items.Add(new ComboBoxItem { Content = "热门", Tag = "hot" });
        _commentSortBox.Items.Add(new ComboBoxItem { Content = "最新", Tag = "time_desc" });
        _commentSortBox.Items.Add(new ComboBoxItem { Content = "最早", Tag = "time_aes" });
        _commentSortUpdating = false;
        _commentSortBox.SelectionChanged += async (_, _) =>
        {
            if (_commentSortUpdating) return;
            string sort = (_commentSortBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "hot";
            if (sort == _commentSort) return;
            _commentSort = sort;
            await RunAsync(ReloadCommentsAsync);
        };
        Grid.SetColumn(_commentSortBox, 1);
        grid.Children.Add(_commentSortBox);
        card.Child = grid;
        return card;
    }

    private void RenderComments()
    {
        if (_commentSurface is null) return;
        _commentSurface.Children.Clear();
        if (_commentCountText is not null && _commentPage is not null)
            _commentCountText.Text = $"评论 ({XhhFormat.Count(_commentPage.TotalFloor > 0 ? _commentPage.TotalFloor : _commentPage.Comments.Count)})";

        if (!_commentsLoaded)
        {
            _commentSurface.Children.Add(new ProgressRing
            {
                IsActive = true,
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 32, 0, 32),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }
        if (_commentPage is null || _commentPage.Comments.Count == 0)
        {
            _commentSurface.Children.Add(EmptyState("\uE8BD", "还没有评论", "来抢沙发，或者稍后刷新看看。"));
            return;
        }

        foreach (XhhComment comment in _commentPage.Comments)
            _commentSurface.Children.Add(BuildCommentCard(comment, _currentPost?.Id ?? "", null, 0));

        if (_commentPage.HasMore)
        {
            Button more = Btn("加载更多评论", "\uE72C");
            more.HorizontalAlignment = HorizontalAlignment.Center;
            more.Click += async (_, _) => await RunAsync(LoadMoreCommentsAsync);
            _commentSurface.Children.Add(more);
        }
        else
        {
            TextBlock end = T("已经看到全部评论了", 12, tertiary: true);
            end.HorizontalAlignment = HorizontalAlignment.Center;
            _commentSurface.Children.Add(end);
        }
    }

    private FrameworkElement BuildCommentCard(XhhComment comment, string linkId, string? rootId, int depth)
    {
        Border card = Card(depth == 0 ? 14 : 10, depth == 0 ? 12 : 8);
        if (depth > 0) card.Background = _palette.Subtle;
        var stack = new StackPanel { Spacing = 7 };

        var header = new Grid { ColumnSpacing = 9 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(Avatar(comment.Avatar, depth == 0 ? 30 : 24));

        var who = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        TextBlock commentName = T(string.IsNullOrWhiteSpace(comment.UserName) ? "匿名用户" : comment.UserName,
            depth == 0 ? 13 : 12, weight: FontWeights.SemiBold);
        commentName.MaxWidth = 220;
        commentName.TextTrimming = TextTrimming.CharacterEllipsis;
        nameRow.Children.Add(commentName);
        if (!string.IsNullOrWhiteSpace(comment.LevelText)) nameRow.Children.Add(Pill(comment.LevelText));
        if (comment.IsOwner) nameRow.Children.Add(Pill("楼主", accent: true));
        who.Children.Add(nameRow);
        string meta = string.Join(" · ", new[] { comment.FloorText, comment.TimeText, comment.IpLocation }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (meta.Length > 0) who.Children.Add(T(meta, 10, tertiary: true));
        Grid.SetColumn(who, 1);
        header.Children.Add(who);
        stack.Children.Add(header);

        if (comment.ReplyTo.Length > 0)
        {
            stack.Children.Add(T($"回复 @{comment.ReplyTo}", 11, accent: true));
        }

        if (!string.IsNullOrWhiteSpace(comment.Text))
        {
            var text = EmojiText(comment.Text, depth == 0 ? 13 : 12, wrap: true);
            text.LineHeight = depth == 0 ? 21 : 19;
            stack.Children.Add(text);
        }

        if (comment.Images.Count > 0)
            stack.Children.Add(BuildImageStrip(comment.Images, 105));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2, 0, 0) };
        bool supported = comment.SupportState == 1;
        Button like = Btn($"{(supported ? "已赞" : "赞")} {XhhFormat.Count(comment.Likes)}", "\uE8E1");
        like.Padding = new Thickness(8, 3, 8, 3);
        like.IsEnabled = _client.IsLoggedIn;
        like.Background = supported ? _palette.AccentSoft : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        like.Click += async (_, _) => await RunAsync(async () =>
        {
            bool next = comment.SupportState != 1;
            await _client.CommentSupportAsync(comment.Id, linkId, next ? 1 : 0, _lifetime.Token);
            if (next)
            {
                if (comment.SupportState != 1) comment.Likes++;
                comment.SupportState = 1;
            }
            else
            {
                if (comment.SupportState == 1) comment.Likes = Math.Max(0, comment.Likes - 1);
                comment.SupportState = 0;
            }
            SetButtonLabel(like, "\uE8E1", $"{(next ? "已赞" : "赞")} {XhhFormat.Count(comment.Likes)}");
            like.Background = next ? _palette.AccentSoft : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        });
        actions.Children.Add(like);

        Button reply = Btn("回复", "\uE97A");
        reply.Padding = new Thickness(8, 3, 8, 3);
        reply.Click += (_, _) =>
        {
            _replyCommentId = comment.Id;
            _replyRootId = rootId ?? comment.Id;
            _replyUserName = comment.UserName;
            if (_replyHint is not null)
            {
                _replyHint.Text = $"正在回复 @{comment.UserName}";
                if (_replyHint.Parent is StackPanel hintRow) hintRow.Visibility = Visibility.Visible;
            }
            _commentBox?.Focus(FocusState.Programmatic);
        };
        actions.Children.Add(reply);

        if (depth == 0 && comment.ChildCount > comment.Children.Count)
        {
            Button expand = Btn($"展开 {comment.ChildCount} 条回复", "\uE70D");
            expand.Padding = new Thickness(8, 3, 8, 3);
            expand.Click += async (_, _) => await RunAsync(async () =>
            {
                await LoadSubCommentsAsync(comment, linkId);
                RenderComments();
            });
            actions.Children.Add(expand);
        }
        stack.Children.Add(actions);

        if (comment.Children.Count > 0)
        {
            var children = new StackPanel { Spacing = 7, Margin = new Thickness(34, 4, 0, 0) };
            foreach (XhhComment child in comment.Children)
                children.Children.Add(BuildCommentCard(child, linkId, rootId ?? comment.Id, depth + 1));
            stack.Children.Add(children);
        }

        card.Child = stack;
        return card;
    }

    private async Task LoadSubCommentsAsync(XhhComment comment, string linkId)
    {
        JsonElement result = await _client.SubCommentsAsync(linkId, comment.Id, comment.Children.Count, 10, _lifetime.Token);
        var children = XhhParser.ParseSubComments(result);
        var known = comment.Children.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (XhhComment child in children)
            if (known.Add(child.Id)) comment.Children.Add(child);
    }

    private async Task ReloadCommentsAsync()
    {
        if (_currentPost is null) return;
        JsonElement result = await _client.LinkTreeAsync(_currentPost.Id, 1, 20, _commentSort, false, _lifetime.Token);
        _commentPage = XhhParser.ParseCommentPage(result, 1);
        _commentPageIndex = 1;
        _commentsLoaded = true;
        RenderComments();
    }

    private async Task LoadMoreCommentsAsync()
    {
        if (_currentPost is null || _commentPage is null || !_commentPage.HasMore) return;
        int next = _commentPageIndex + 1;
        JsonElement result = await _client.LinkTreeAsync(_currentPost.Id, next, 20, _commentSort, false, _lifetime.Token);
        XhhCommentPage page = XhhParser.ParseCommentPage(result, next);
        var merged = new List<XhhComment>(_commentPage.Comments);
        var known = merged.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (XhhComment comment in page.Comments)
            if (known.Add(comment.Id)) merged.Add(comment);
        _commentPage = new XhhCommentPage
        {
            Comments = merged,
            TotalFloor = page.TotalFloor > 0 ? page.TotalFloor : _commentPage.TotalFloor,
            TotalPage = page.TotalPage,
            Page = next,
            HasMore = page.HasMore,
            Sort = page.Sort
        };
        _commentPageIndex = next;
        RenderComments();
    }

    private async Task OpenAuthorAsync(XhhPost post)
    {
        var user = new XhhUser(post.AuthorId, post.AuthorName, post.AuthorAvatar, post.AuthorLevel);
        await OpenUserAsync(user, async () => await OpenPostAsync(post));
    }

    private static void SetButtonLabel(Button button, string glyph, string label)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12 });
        content.Children.Add(new TextBlock { Text = label, FontSize = 12 });
        button.Content = content;
    }
}
