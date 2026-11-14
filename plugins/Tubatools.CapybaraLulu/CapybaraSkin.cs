using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using TubaWinUi3.PluginSdk;
using Windows.UI;

namespace Tubatools.CapybaraLulu;

/// <summary>
/// 只通过 fullTrust 的 RootVisual 往视觉树里“叠图层”：全局背景、标题栏图标、侧栏页眉页脚、
/// 右下角小伙伴与首页贴纸。每一处都记下原值，停用插件时原样还回去。
/// </summary>
internal sealed class CapybaraSkin
{
    private const double MarginMascot = 10;
    private static readonly string[] AmbientLines =
    [
        "噜噜噜～",
        "电脑还活着，问题不大。",
        "记得喝水，别忘了眨眼。",
        "摸鱼的时候噜噜帮你放风。",
        "这个工具箱，噜噜熟。",
        "有事没事都可以点点看。",
    ];

    private static readonly string[] DashboardLines =
    [
        "今天想折腾哪台电脑噜？",
        "噜噜已就位，从哪台开始？",
        "工具都在这儿，慢慢挑。"
    ];
    private static readonly string[] HardwareLines =
    [
        "温度正常，风扇也没在吼噜。",
        "鲁大师？噜噜才是大师。",
        "接口没插反吧？"
    ];
    private static readonly string[] BuiltinLines =
    [
        "四十多个工具，够你玩一下午。",
        "点之前先看一眼说明噜。",
        "这个按钮别乱按，噜。"
    ];
    private static readonly string[] BenchmarkLines =
    [
        "跑分的时候噜噜会躲远一点。",
        "分数不重要，散热重要。",
        "风扇起飞的时候记得捂耳朵。"
    ];
    private static readonly string[] PluginLines =
    [
        "装插件前记得看一眼权限。",
        "噜噜自己也是插件，请对噜噜好一点。",
        "插件是好东西，别装太多。"
    ];
    private static readonly string[] AiLines =
    [
        "我问过噜噜了，它也不知道。",
        "让 AI 干活，噜噜负责鼓掌。",
        "写提示词的时候把话说清楚噜。"
    ];
    private static readonly string[] SettingsLines =
    [
        "改设置前先备份，噜噜不骗你。",
        "这个开关关掉，噜噜会伤心的。",
        "藏得再深，噜噜也找得到。"
    ];
    private static readonly string[] ToolLines =
    [
        "工具开好了，噜噜在旁边看着。",
        "别急，等它加载完。",
        "有报错就把窗口拉大一点看。"
    ];

    private readonly IPluginHostAccess _host;
    private readonly CapybaraSettings _settings;
    private readonly List<Action> _restore = [];
    private Grid? _root;
    private NavigationView? _nav;
    private Frame? _frame;
    private TitleBar? _titleBar;
    private FrameworkElement? _hostBackground;
    private Visibility _hostBackgroundVisibility = Visibility.Visible;
    private IconSource? _titleBarIcon;
    private UIElement? _paneFooter;
    private Grid? _backgroundHost;
    private Image? _backgroundImage;
    private Rectangle? _veil;
    private GradientStop? _veilStop;
    private Grid? _overlay;
    private Image? _mascot;
    private CompositeTransform? _mascotTransform;
    private Grid? _bubbleHost;
    private Border? _bubble;
    private Polygon? _bubbleTail;
    private TextBlock? _bubbleText;
    private Border? _paneFooterCard;
    private Image? _paneFooterImage;
    private TextBlock? _paneFooterCaption;
    private Image? _heroSticker;
    private Panel? _heroParent;
    private DispatcherQueueTimer? _speechTimer;
    private DispatcherQueueTimer? _greetTimer;
    private Storyboard? _breathe;
    private Storyboard? _backgroundMotion;
    private Storyboard? _footerMotion;
    private Storyboard? _heroMotion;
    private Storyboard? _speakMotion;
    private Storyboard? _bubbleAnimation;
    private bool _applied;

    public CapybaraSkin(IPluginHostAccess host, CapybaraSettings settings)
    {
        _host = host;
        _settings = settings;
    }

    public void Apply()
    {
        _root = _host.RootVisual as Grid ?? throw new InvalidOperationException("主窗口根元素不是 Grid，无法铺设美化层。");
        _nav = _host.NavigationView as NavigationView;
        _frame = _host.NavigationFrame as Frame;
        _titleBar = FindByName<TitleBar>(_root, "AppTitleBar");
        // 先置位：任何一步抛异常时 Restore 都能把已经改过的部分还回去。
        _applied = true;

        try
        {
            if (_settings.Background) ApplyBackground();
            if (_settings.TitleBar) ApplyTitleBarIcon();
            if (_settings.Sidebar) ApplyPaneDecorations();
            if (_settings.Corner) ApplyCornerCompanion();

            if (_frame is not null)
            {
                _frame.Navigated += OnFrameNavigated;
                _restore.Add(() => _frame.Navigated -= OnFrameNavigated);
                UpdatePageMood(_frame.Content as Page, speak: false);
            }

            if (_settings.HeroSticker) AttachHeroSticker(_frame?.Content as Page);
            CapybaraHardwareDecor.Attach(_host, _settings);

            _host.ThemeChanged += OnThemeChanged;
            UpdateThemeVisuals();

            if (_settings.Corner && _settings.Speech) StartSpeechTimer();
        }
        catch
        {
            Restore();
            throw;
        }
    }

    public void Restore()
    {
        if (!_applied) return;
        _applied = false;
        try { _host.ThemeChanged -= OnThemeChanged; } catch { }
        try { _speechTimer?.Stop(); } catch { }
        _speechTimer = null;
        try { _greetTimer?.Stop(); } catch { }
        _greetTimer = null;
        try { _breathe?.Stop(); } catch { }
        try { _backgroundMotion?.Stop(); } catch { }
        try { _footerMotion?.Stop(); } catch { }
        try { _heroMotion?.Stop(); } catch { }
        try { _speakMotion?.Stop(); } catch { }
        try { _bubbleAnimation?.Stop(); } catch { }

        DetachHeroSticker();
        CapybaraHardwareDecor.Detach();
        RemoveFromParent(_overlay);
        RemoveFromParent(_backgroundHost);
        RemoveFromParent(_veil);

        if (_nav is not null)
        {
            try { _nav.PaneFooter = _paneFooter; } catch { }
        }
        if (_titleBar is not null)
        {
            try { _titleBar.IconSource = _titleBarIcon; } catch { }
        }
        if (_hostBackground is not null)
        {
            try { _hostBackground.Visibility = _hostBackgroundVisibility; } catch { }
        }

        for (var i = _restore.Count - 1; i >= 0; i--)
        {
            try { _restore[i](); } catch { }
        }
        _restore.Clear();

        _root = null;
        _nav = null;
        _frame = null;
        _titleBar = null;
        _hostBackground = null;
        _titleBarIcon = null;
        _paneFooter = null;
        _backgroundHost = null;
        _backgroundImage = null;
        _veil = null;
        _veilStop = null;
        _overlay = null;
        _mascot = null;
        _mascotTransform = null;
        _bubbleHost = null;
        _bubble = null;
        _bubbleTail = null;
        _bubbleText = null;
        _paneFooterCard = null;
        _paneFooterImage = null;
        _heroSticker = null;
        _heroParent = null;
        _breathe = null;
        _backgroundMotion = null;
        _footerMotion = null;
        _heroMotion = null;
        _speakMotion = null;
        _bubbleAnimation = null;
    }

    public void UpdateBackgroundOpacity(double opacity) => _backgroundImage?.Opacity = Math.Clamp(opacity, 0.08, 0.65);

    public void UpdateMascotSize(double width)
    {
        if (_mascot is null) return;
        var clamped = Math.Clamp(width, 92, 190);
        _mascot.Width = clamped;
        if (_mascotTransform is not null) _mascotTransform.CenterX = clamped / 2;
        UpdateBubblePlacement();
    }

    public void UpdateBubbleVisible(bool visible)
    {
        if (_bubbleHost is null) return;
        _bubbleHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>页面上点「让噜噜说句话」时用。</summary>
    public void SpeakRandom() => Speak(Pick(AmbientLines));

    public void SpeakRandomForCurrentPage() => Speak(Pick(LinesFor(_frame?.Content as Page)));

    // ---------------------------------------------------------------- 背景

    private void ApplyBackground()
    {
        if (_root is null) return;
        var dark = IsDark();
        var file = CapybaraAssets.BackgroundFile(_settings.BackgroundTheme, dark);

        // 图片放进一个会跟随尺寸裁剪的容器里：缓慢放大时不会溢出到标题栏/侧栏。
        _backgroundHost = new Grid
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetRow(_backgroundHost, 2);
        Canvas.SetZIndex(_backgroundHost, -3);
        _backgroundHost.SizeChanged += (_, e) =>
        {
            try
            {
                var clip = new RectangleGeometry();
                clip.Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
                _backgroundHost.Clip = clip;
            }
            catch { }
        };

        _backgroundImage = new Image
        {
            Source = CapybaraAssets.Load(file),
            Stretch = Stretch.UniformToFill,
            Opacity = _settings.ClampedOpacity,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(),
        };
        _backgroundHost.Children.Add(_backgroundImage);
        _root.Children.Insert(0, _backgroundHost);

        _veil = new Rectangle { IsHitTestVisible = false };
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1)
        };
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = 0 });
        _veilStop = new GradientStop { Color = VeilColor(), Offset = 1 };
        brush.GradientStops.Add(_veilStop);
        _veil.Fill = brush;
        Grid.SetRow(_veil, 2);
        Canvas.SetZIndex(_veil, -2);
        _root.Children.Insert(0, _veil);

        // 宿主自己的背景图先藏起来，避免两张叠在一起；停用时恢复原可见性。
        if (FindByName<FrameworkElement>(_root, "BackgroundImg") is { } hostBackground)
        {
            _hostBackground = hostBackground;
            _hostBackgroundVisibility = hostBackground.Visibility;
            hostBackground.Visibility = Visibility.Collapsed;
        }

        if (AnimationsEnabled() && _settings.Animations && _settings.BackgroundMotion)
            StartBackgroundMotion();
    }

    private void StartBackgroundMotion()
    {
        if (_backgroundImage is null) return;
        _backgroundMotion = new Storyboard { Duration = new Duration(TimeSpan.FromSeconds(26)) };
        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var animation = new DoubleAnimation
            {
                From = 1,
                To = 1.05,
                Duration = new Duration(TimeSpan.FromSeconds(26)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(animation, _backgroundImage);
            Storyboard.SetTargetProperty(animation, "(UIElement.RenderTransform).(ScaleTransform." + property + ")");
            _backgroundMotion.Children.Add(animation);
        }
        try { _backgroundMotion.Begin(); } catch { }
    }

    // ---------------------------------------------------------------- 标题栏

    private void ApplyTitleBarIcon()
    {
        if (_titleBar is null) return;
        _titleBarIcon = _titleBar.IconSource;
        _titleBar.IconSource = new ImageIconSource { ImageSource = CapybaraAssets.Load("lulu-head.png") };
    }

    // ---------------------------------------------------------------- 侧栏

    private void ApplyPaneDecorations()
    {
        if (_nav is null) return;
        _paneFooter = _nav.PaneFooter;

        // 只保留页脚：侧栏顶部不再放标题卡片，避免和宿主导航抢视线。
        var sleeper = new Image
        {
            Source = CapybaraAssets.Load("lulu-sleep.png"),
            Width = 140,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(),
        };
        _paneFooterImage = sleeper;
        var caption = new TextBlock
        {
            Text = "噜噜在旁边打盹，慢慢来。",
            FontSize = 11,
            Opacity = 0.68,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        _paneFooterCaption = caption;
        var footerStack = new StackPanel { Spacing = 2 };
        footerStack.Children.Add(sleeper);
        footerStack.Children.Add(caption);
        _paneFooterCard = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(4, 6, 4, 6),
            Margin = new Thickness(6, 4, 6, 10),
            BorderThickness = new Thickness(1),
            Child = footerStack,
        };
        _paneFooterCard.SizeChanged += (_, e) => FitPaneFooter(e.NewSize.Width);
        _nav.PaneFooter = _paneFooterCard;
        FitPaneFooter(160);

        if (AnimationsEnabled() && _settings.Animations)
        {
            _footerMotion = new Storyboard { Duration = new Duration(TimeSpan.FromSeconds(4)) };
            foreach (var property in new[] { "ScaleX", "ScaleY" })
            {
                var animation = new DoubleAnimation
                {
                    From = 1,
                    To = 1.025,
                    Duration = new Duration(TimeSpan.FromSeconds(4)),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Storyboard.SetTarget(animation, sleeper);
                Storyboard.SetTargetProperty(animation, "(UIElement.RenderTransform).(ScaleTransform." + property + ")");
                _footerMotion.Children.Add(animation);
            }
            try { _footerMotion.Begin(); } catch { }
        }
    }

    // ---------------------------------------------------------------- 角落小伙伴

    private void ApplyCornerCompanion()
    {
        if (_root is null) return;
        _overlay = new Grid
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetRow(_overlay, 2);
        Canvas.SetZIndex(_overlay, 300);

        var width = _settings.ClampedMascotSize;
        var onLeft = _settings.MascotSide.Equals(CapybaraSettings.SideLeft, StringComparison.OrdinalIgnoreCase);
        _mascot = new Image
        {
            Source = CapybaraAssets.Load("lulu-smile.png"),
            Width = width,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = onLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = onLeft ? new Thickness(MarginMascot, 0, 0, 0) : new Thickness(0, 0, MarginMascot, 0),
            IsHitTestVisible = false,
        };
        _mascotTransform = new CompositeTransform { CenterX = width / 2, CenterY = 240 };
        _mascot.RenderTransform = _mascotTransform;
        _overlay.Children.Add(_mascot);

        BuildBubble(onLeft, width);
        _overlay.Children.Add(_bubbleHost);
        _root.Children.Add(_overlay);

        if (AnimationsEnabled() && _settings.Animations)
        {
            var bob = new DoubleAnimation
            {
                From = 0,
                To = -6,
                Duration = new Duration(TimeSpan.FromSeconds(3.2)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(bob, _mascot);
            Storyboard.SetTargetProperty(bob, "(UIElement.RenderTransform).(CompositeTransform.TranslateY)");

            var sway = new DoubleAnimation
            {
                From = -2,
                To = 2,
                Duration = new Duration(TimeSpan.FromSeconds(4.6)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(sway, _mascot);
            Storyboard.SetTargetProperty(sway, "(UIElement.RenderTransform).(CompositeTransform.Rotation)");

            _breathe = new Storyboard();
            _breathe.Children.Add(bob);
            _breathe.Children.Add(sway);
            try { _breathe.Begin(); } catch { }
        }
    }

    private void BuildBubble(bool onLeft, double mascotWidth)
    {
        _bubbleText = new TextBlock
        {
            FontSize = 12,
            LineHeight = 17,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 210,
        };
        _bubble = new Border
        {
            Padding = new Thickness(12, 7, 12, 8),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Child = _bubbleText,
        };
        _bubbleTail = new Polygon
        {
            Points = onLeft
                ? new PointCollection
                {
                    new Windows.Foundation.Point(0, 0),
                    new Windows.Foundation.Point(12, 0),
                    new Windows.Foundation.Point(0, 12),
                }
                : new PointCollection
                {
                    new Windows.Foundation.Point(0, 0),
                    new Windows.Foundation.Point(12, 0),
                    new Windows.Foundation.Point(12, 12),
                },
            Width = 12,
            Height = 12,
            HorizontalAlignment = onLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = onLeft ? new Thickness(26, 0, 0, 0) : new Thickness(0, 0, 26, 0),
            IsHitTestVisible = false,
        };

        _bubbleHost = new Grid
        {
            HorizontalAlignment = onLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = onLeft
                ? new Thickness(mascotWidth + MarginMascot + 6, 0, 0, 30)
                : new Thickness(0, 0, mascotWidth + MarginMascot + 6, 30),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _bubbleHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _bubbleHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        Grid.SetRow(_bubble, 0);
        Grid.SetRow(_bubbleTail, 1);
        _bubbleHost.Children.Add(_bubble);
        _bubbleHost.Children.Add(_bubbleTail);
        _bubbleHost.Visibility = _settings.Bubble ? Visibility.Visible : Visibility.Collapsed;

        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0 });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)), Value = 1 });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.3)), Value = 1 });
        animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(5)), Value = 0 });
        Storyboard.SetTarget(animation, _bubbleHost);
        Storyboard.SetTargetProperty(animation, "Opacity");
        _bubbleAnimation = new Storyboard { Duration = new Duration(TimeSpan.FromSeconds(5)) };
        _bubbleAnimation.Children.Add(animation);
    }

    private void FitPaneFooter(double cardWidth)
    {
        var inner = Math.Max(24, cardWidth - 8);
        if (_paneFooterImage is not null) _paneFooterImage.Width = Math.Clamp(inner, 34, 140);
        if (_paneFooterCaption is not null)
            _paneFooterCaption.Visibility = cardWidth >= 120 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateBubblePlacement()
    {
        if (_mascot is null || _bubbleHost is null) return;
        var width = _mascot.Width;
        var onLeft = _settings.MascotSide.Equals(CapybaraSettings.SideLeft, StringComparison.OrdinalIgnoreCase);
        _bubbleHost.HorizontalAlignment = onLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        _bubbleHost.Margin = onLeft
            ? new Thickness(width + MarginMascot + 6, 0, 0, 30)
            : new Thickness(0, 0, width + MarginMascot + 6, 30);
    }

    private void StartSpeechTimer()
    {
        var seconds = _settings.ClampedSpeechInterval;
        if (seconds <= 0) return; // 只在切页面时说：不设定时器。
        if (_root?.DispatcherQueue is not { } queue) return;
        _speechTimer = queue.CreateTimer();
        _speechTimer.Interval = TimeSpan.FromSeconds(seconds);
        _speechTimer.IsRepeating = true;
        _speechTimer.Tick += (_, _) => Speak(Pick(AmbientLines));
        _speechTimer.Start();

        // 刚进工具箱时打个招呼，别等 50 秒。
        _greetTimer = queue.CreateTimer();
        _greetTimer.Interval = TimeSpan.FromSeconds(2.5);
        _greetTimer.IsRepeating = false;
        _greetTimer.Tick += (_, _) => Speak(Pick(LinesFor(_frame?.Content as Page)));
        _greetTimer.Start();
    }

    private void Speak(string text)
    {
        if (_bubbleText is null || _bubbleHost is null) return;
        _bubbleText.Text = text;
        try { _bubbleAnimation?.Stop(); _bubbleAnimation?.Begin(); } catch { }
        PlaySpeakNod();
    }

    private void PlaySpeakNod()
    {
        if (_mascot is null || !AnimationsEnabled() || !_settings.Animations) return;
        if (_speakMotion is null)
        {
            _speakMotion = new Storyboard();
            foreach (var property in new[] { "ScaleX", "ScaleY" })
            {
                var animation = new DoubleAnimationUsingKeyFrames();
                animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 1 });
                animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140)), Value = 1.06 });
                animation.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)), Value = 1 });
                Storyboard.SetTarget(animation, _mascot);
                Storyboard.SetTargetProperty(animation, "(UIElement.RenderTransform).(CompositeTransform." + property + ")");
                _speakMotion.Children.Add(animation);
            }
        }
        try { _speakMotion.Stop(); _speakMotion.Begin(); } catch { }
    }

    // ---------------------------------------------------------------- 页面心情 & 首页贴纸

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        if (!_applied) return;
        var page = e.Content as Page;
        UpdatePageMood(page, speak: true);
        if (_settings.HeroSticker)
        {
            DetachHeroSticker();
            AttachHeroSticker(page);
        }
    }

    private void UpdatePageMood(Page? page, bool speak)
    {
        var name = page?.GetType().Name ?? "";
        var file = name switch
        {
            "DashboardPage" => "lulu-cheer.png",
            "HardwarePage" => "lulu-wrench.png",
            "BuiltinToolsPage" => "lulu-smile.png",
            "PerformanceBenchmarkPage" => "lulu-daze.png",
            "PluginsPage" => "lulu-head.png",
            "AiAssistantPage" => "lulu-laptop.png",
            "SettingsPage" => "lulu-sleep.png",
            _ => "lulu-smile.png",
        };
        if (_mascot is not null)
        {
            try { _mascot.Source = CapybaraAssets.Load(file); } catch { }
        }
        if (speak && _settings.Speech) Speak(Pick(LinesFor(page)));
    }

    private static string[] LinesFor(Page? page) => (page?.GetType().Name ?? "") switch
    {
        "DashboardPage" => DashboardLines,
        "HardwarePage" => HardwareLines,
        "BuiltinToolsPage" => BuiltinLines,
        "PerformanceBenchmarkPage" => BenchmarkLines,
        "PluginsPage" => PluginLines,
        "AiAssistantPage" => AiLines,
        "SettingsPage" => SettingsLines,
        _ => ToolLines,
    };

    private void AttachHeroSticker(Page? page)
    {
        if (page?.GetType().Name != "DashboardPage") return;
        if (FindByName<Grid>(page, "HeaderPanel") is not { } header) return;
        _heroSticker = new Image
        {
            Source = CapybaraAssets.Load("lulu-cheer.png"),
            Width = 92,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 48, 6, 0),
            IsHitTestVisible = false,
            RenderTransform = new TranslateTransform(),
        };
        Grid.SetColumn(_heroSticker, Math.Max(0, header.ColumnDefinitions.Count - 1));
        header.Children.Add(_heroSticker);
        _heroParent = header;

        if (AnimationsEnabled() && _settings.Animations)
        {
            var animation = new DoubleAnimation
            {
                From = 0,
                To = -5,
                Duration = new Duration(TimeSpan.FromSeconds(2.4)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(animation, _heroSticker);
            Storyboard.SetTargetProperty(animation, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            _heroMotion = new Storyboard();
            _heroMotion.Children.Add(animation);
            try { _heroMotion.Begin(); } catch { }
        }
    }

    private void DetachHeroSticker()
    {
        try { _heroMotion?.Stop(); } catch { }
        _heroMotion = null;
        if (_heroSticker is null) return;
        try { _heroParent?.Children.Remove(_heroSticker); } catch { }
        _heroSticker = null;
        _heroParent = null;
    }

    // ---------------------------------------------------------------- 主题

    private void OnThemeChanged() => UpdateThemeVisuals();

    private void UpdateThemeVisuals()
    {
        try
        {
            var dark = IsDark();
            var bubbleBackground = new SolidColorBrush(dark ? Color.FromArgb(242, 30, 30, 30) : Color.FromArgb(244, 255, 255, 255));
            var bubbleBorder = new SolidColorBrush(dark ? Color.FromArgb(48, 255, 255, 255) : Color.FromArgb(34, 0, 0, 0));
            var bubbleText = new SolidColorBrush(dark ? Color.FromArgb(255, 244, 244, 244) : Color.FromArgb(255, 26, 26, 26));
            if (_bubble is not null)
            {
                _bubble.Background = bubbleBackground;
                _bubble.BorderBrush = bubbleBorder;
            }
            if (_bubbleTail is not null) _bubbleTail.Fill = bubbleBackground;
            if (_bubbleText is not null) _bubbleText.Foreground = bubbleText;

            if (_paneFooterCard is not null) StylePaneCard(_paneFooterCard, dark);
            if (_veilStop is not null) _veilStop.Color = VeilColor();

            if (_backgroundImage is not null && _settings.BackgroundTheme == CapybaraSettings.ThemeAuto)
            {
                var file = CapybaraAssets.BackgroundFile(CapybaraSettings.ThemeAuto, dark);
                _backgroundImage.Source = CapybaraAssets.Load(file);
            }
        }
        catch { }
    }

    private static void StylePaneCard(Border card, bool dark)
    {
        card.Background = new SolidColorBrush(dark ? Color.FromArgb(18, 255, 255, 255) : Color.FromArgb(16, 0, 0, 0));
        card.BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0));
    }

    private Color VeilColor() => IsDark() ? Color.FromArgb(228, 24, 24, 24) : Color.FromArgb(226, 243, 243, 243);

    private bool IsDark() => _root?.ActualTheme == ElementTheme.Dark;

    // ---------------------------------------------------------------- 工具

    private static bool AnimationsEnabled()
    {
        try { return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled; }
        catch { return true; }
    }

    private static string Pick(string[] lines) => lines[Random.Shared.Next(lines.Length)];

    private static void RemoveFromParent(FrameworkElement? element)
    {
        if (element?.Parent is not Panel panel) return;
        try { panel.Children.Remove(element); } catch { }
    }

    private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T typed && typed.Name == name) return typed;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindByName<T>(VisualTreeHelper.GetChild(root, i), name) is { } found) return found;
        }
        return null;
    }
}
