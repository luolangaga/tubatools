using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using TubaWinUi3.Services;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace TubaWinUi3.Pages;

/// <summary>
/// 插件页的全部动效都放在这里：页面入场、卡片悬停/按压、Tab 下划线、hero 漂浮、
/// 详情页开合与 ConnectedAnimation。统一遵循系统“显示动画”开关，关闭时直接落位。
/// </summary>
public sealed partial class PluginsPage
{
    private static readonly Vector2 FluentEaseOut = new(.16f, 1);
    private static readonly Vector2 FluentEaseOutControl = new(.3f, 1);

    private void AnimatePageEntrance()
    {
        if (!_animationsEnabled || PageRoot is null) return;
        var compositor = ElementCompositionPreview.GetElementVisual(PageRoot).Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(FluentEaseOut, FluentEaseOutControl);
        var items = new List<(FrameworkElement Element, int Delay)>
        {
            (PageHeader, 0),
            (HeroCard, 40),
            (NavBar, 90),
            (ContentArea, 140)
        };

        foreach (var (element, delay) in items)
        {
            if (element is null || element.Visibility == Visibility.Collapsed) continue;
            var visual = ElementCompositionPreview.GetElementVisual(element);
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            visual.StopAnimation("Translation");
            visual.StopAnimation("Opacity");
            visual.Properties.InsertVector3("Translation", new Vector3(0, 24, 0));
            visual.Opacity = 0;

            var move = compositor.CreateVector3KeyFrameAnimation();
            move.Duration = TimeSpan.FromMilliseconds(560);
            move.DelayTime = TimeSpan.FromMilliseconds(delay);
            move.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            move.InsertKeyFrame(1, Vector3.Zero, ease);
            visual.StartAnimation("Translation", move);

            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.Duration = TimeSpan.FromMilliseconds(480);
            fade.DelayTime = TimeSpan.FromMilliseconds(delay);
            fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            fade.InsertKeyFrame(1, 1, ease);
            visual.StartAnimation("Opacity", fade);
        }
    }

    /// <summary>卡片悬停：底色轻微浮现、整卡上浮 2px、图标放大——只动 Composition，不触发布局。</summary>
    private void AttachCardMotion(Border card, Border hover, Border icon)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(card, true);
        var cardVisual = ElementCompositionPreview.GetElementVisual(card);
        cardVisual.Properties.InsertVector3("Translation", Vector3.Zero);
        var hoverVisual = ElementCompositionPreview.GetElementVisual(hover);
        hoverVisual.Opacity = 0;

        bool hovered = false;
        bool pressed = false;

        void Apply()
        {
            if (!_animationsEnabled)
            {
                hoverVisual.Opacity = hovered ? 0.55f : 0;
                cardVisual.Properties.InsertVector3("Translation", Vector3.Zero);
                return;
            }
            AnimateScalar(hoverVisual, "Opacity", pressed ? 0.9f : hovered ? 0.55f : 0f, 160);
            AnimateVector3(cardVisual, "Translation", new Vector3(0, pressed ? -1 : hovered ? -2 : 0, 0), 200);
            try { FluentMotion.AnimateIcon(icon, hovered && !pressed); } catch { }
        }

        card.PointerEntered += (_, _) => { hovered = true; Apply(); };
        card.PointerExited += (_, _) => { hovered = false; pressed = false; Apply(); };
        card.PointerPressed += (_, _) => { pressed = true; Apply(); };
        card.PointerReleased += (_, _) => { pressed = false; Apply(); };
        card.PointerCaptureLost += (_, _) => { pressed = false; Apply(); };
    }

    /// <summary>搜索结果/已安装卡片入场：卡片网格逐张上浮淡入，加载完成不是“硬切”。</summary>
    private void AnimateCardsIn(IReadOnlyList<Border> cards, int startIndex)
    {
        if (!_animationsEnabled || cards.Count == 0) return;
        var compositor = ElementCompositionPreview.GetElementVisual(PageRoot).Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(FluentEaseOut, FluentEaseOutControl);
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var delay = Math.Min(i, 8) * 45 + (startIndex > 0 ? 0 : 30);
            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(card);
                ElementCompositionPreview.SetIsTranslationEnabled(card, true);
                visual.StopAnimation("Opacity");
                visual.StopAnimation("Translation");
                visual.Opacity = 0;
                visual.Properties.InsertVector3("Translation", new Vector3(0, 16, 0));

                var fade = compositor.CreateScalarKeyFrameAnimation();
                fade.Duration = TimeSpan.FromMilliseconds(420);
                fade.DelayTime = TimeSpan.FromMilliseconds(delay);
                fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
                fade.InsertKeyFrame(1, 1, ease);
                visual.StartAnimation("Opacity", fade);

                var move = compositor.CreateVector3KeyFrameAnimation();
                move.Duration = TimeSpan.FromMilliseconds(480);
                move.DelayTime = TimeSpan.FromMilliseconds(delay);
                move.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
                move.InsertKeyFrame(1, Vector3.Zero, ease);
                visual.StartAnimation("Translation", move);
            }
            catch { }
        }
    }

    /// <summary>异步才能拿到的图标/封面不要硬切，元素出现时做一次短暂淡入。</summary>
    private void FadeInElement(FrameworkElement element, int duration = 220)
    {
        if (element is null) return;
        // 先把 XAML 属性落成 1，Composition 动画失败时也绝不会留下一个透明图标。
        element.Opacity = 1;
        if (!_animationsEnabled) return;
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;
            var ease = compositor.CreateCubicBezierEasingFunction(FluentEaseOut, FluentEaseOutControl);
            visual.StopAnimation("Opacity");
            visual.Opacity = 0;
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.Duration = TimeSpan.FromMilliseconds(duration);
            fade.InsertKeyFrame(1, 1, ease);
            visual.StartAnimation("Opacity", fade);
        }
        catch { /* 元素还没进可视树时直接显示即可。 */ }
    }

    private void StartAvailableSkeletonPulse()
    {
        if (!_animationsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(AvailableSkeleton);
        var compositor = visual.Compositor;
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(1500);
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.InsertKeyFrame(0, .42f);
        animation.InsertKeyFrame(.5f, .82f);
        animation.InsertKeyFrame(1, .42f);
        visual.StartAnimation("Opacity", animation);
    }

    private void StopAvailableSkeletonPulse()
    {
        if (AvailableSkeleton is null) return;
        var visual = ElementCompositionPreview.GetElementVisual(AvailableSkeleton);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1;
    }

    /// <summary>刷新按钮在原地缓慢旋转，给“数据正在加载”一个不打断布局的反馈。</summary>
    private void StartRefreshSpin()
    {
        if (!_animationsEnabled || RefreshGlyph is null) return;
        var visual = ElementCompositionPreview.GetElementVisual(RefreshGlyph);
        var compositor = visual.Compositor;
        visual.CenterPoint = new Vector3(
            RefreshGlyph.ActualWidth > 0 ? (float)RefreshGlyph.ActualWidth / 2 : 8,
            RefreshGlyph.ActualHeight > 0 ? (float)RefreshGlyph.ActualHeight / 2 : 8, 0);
        visual.StopAnimation("RotationAngleInDegrees");
        var spin = compositor.CreateScalarKeyFrameAnimation();
        spin.Duration = TimeSpan.FromMilliseconds(900);
        spin.IterationBehavior = AnimationIterationBehavior.Forever;
        spin.InsertKeyFrame(0, 0);
        spin.InsertKeyFrame(1, 360);
        visual.StartAnimation("RotationAngleInDegrees", spin);
    }

    private void StopRefreshSpin()
    {
        if (RefreshGlyph is null) return;
        var visual = ElementCompositionPreview.GetElementVisual(RefreshGlyph);
        visual.StopAnimation("RotationAngleInDegrees");
        visual.RotationAngleInDegrees = 0;
    }

    /// <summary>列表标题/计数随新数据轻微上浮淡入，避免搜索完成后只有列表自己跳出来。</summary>
    private void AnimateSectionHeader(FrameworkElement header, FrameworkElement count)
    {
        if (!_animationsEnabled) return;
        var compositor = ElementCompositionPreview.GetElementVisual(PageRoot).Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(FluentEaseOut, FluentEaseOutControl);
        foreach (var (element, delay) in new[] { (header, 0), (count, 30) })
        {
            if (element is null) continue;
            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                ElementCompositionPreview.SetIsTranslationEnabled(element, true);
                visual.StopAnimation("Opacity");
                visual.StopAnimation("Translation");
                visual.Opacity = 0;
                visual.Properties.InsertVector3("Translation", new Vector3(0, -5, 0));
                var fade = compositor.CreateScalarKeyFrameAnimation();
                fade.Duration = TimeSpan.FromMilliseconds(240);
                fade.DelayTime = TimeSpan.FromMilliseconds(delay);
                fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
                fade.InsertKeyFrame(1, 1, ease);
                visual.StartAnimation("Opacity", fade);
                var move = compositor.CreateVector3KeyFrameAnimation();
                move.Duration = TimeSpan.FromMilliseconds(280);
                move.DelayTime = TimeSpan.FromMilliseconds(delay);
                move.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
                move.InsertKeyFrame(1, Vector3.Zero, ease);
                visual.StartAnimation("Translation", move);
            }
            catch { }
        }
    }

    private void PositionNavUnderline(bool animate)
    {
        if (NavBar is null || NavUnderline is null) return;
        var button = _discover ? DiscoverTab : InstalledTab;
        double target;
        try
        {
            var point = button.TransformToVisual(NavBar).TransformPoint(new Point(0, 0));
            target = point.X + Math.Max(0, (button.ActualWidth - NavUnderline.Width) / 2);
        }
        catch { return; }

        ElementCompositionPreview.SetIsTranslationEnabled(NavUnderline, true);
        var visual = ElementCompositionPreview.GetElementVisual(NavUnderline);
        if (!animate || !_animationsEnabled)
        {
            visual.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", new Vector3((float)target, 0, 0));
            _navUnderlineX = (int)Math.Round(target);
            return;
        }

        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, 1), new Vector2(.3f, 1));
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(280);
        animation.InsertKeyFrame(0, new Vector3((float)_navUnderlineX, 0, 0), ease);
        animation.InsertKeyFrame(1, new Vector3((float)target, 0, 0), ease);
        visual.StartAnimation("Translation", animation);
        _navUnderlineX = (int)Math.Round(target);
    }

    private int _paneAnimationId;
    private async void AnimatePaneSwitch(FrameworkElement from, FrameworkElement to)
    {
        var animationId = ++_paneAnimationId;
        to.Visibility = Visibility.Visible;
        if (!_animationsEnabled)
        {
            from.Visibility = Visibility.Collapsed;
            to.Opacity = 1;
            return;
        }

        ElementCompositionPreview.SetIsTranslationEnabled(to, true);
        var fromVisual = ElementCompositionPreview.GetElementVisual(from);
        var toVisual = ElementCompositionPreview.GetElementVisual(to);
        fromVisual.StopAnimation("Opacity");
        fromVisual.Opacity = 1;
        toVisual.StopAnimation("Opacity");
        toVisual.StopAnimation("Translation");
        toVisual.Opacity = 0;
        toVisual.Properties.InsertVector3("Translation", new Vector3(0, 14, 0));

        var compositor = toVisual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(FluentEaseOut, FluentEaseOutControl);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = TimeSpan.FromMilliseconds(360);
        fade.InsertKeyFrame(1, 1, ease);
        toVisual.StartAnimation("Opacity", fade);
        var move = compositor.CreateVector3KeyFrameAnimation();
        move.Duration = TimeSpan.FromMilliseconds(420);
        move.InsertKeyFrame(1, Vector3.Zero, ease);
        toVisual.StartAnimation("Translation", move);

        var outFade = compositor.CreateScalarKeyFrameAnimation();
        outFade.Duration = TimeSpan.FromMilliseconds(260);
        outFade.InsertKeyFrame(1, 0, ease);
        fromVisual.StartAnimation("Opacity", outFade);

        await Task.Delay(380);
        if (animationId != _paneAnimationId) return;
        // 只有最新一次切换能收起旧 pane；快速连点时旧动画不能把新 pane 折叠掉。
        var active = _discover ? AvailablePane : InstalledPane;
        if (from == active)
        {
            from.Visibility = Visibility.Visible;
            fromVisual.Opacity = 1;
        }
        else
        {
            from.Visibility = Visibility.Collapsed;
            fromVisual.Opacity = 1;
        }
    }

    private void StartHeroFloat()
    {
        if (_heroFloatOn || !_animationsEnabled) return;
        _heroFloatOn = true;
        var tiles = new[] { HeroTile0, HeroTile1, HeroTile2 };
        foreach (var tile in tiles)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(tile, true);
            var visual = ElementCompositionPreview.GetElementVisual(tile);
            var compositor = visual.Compositor;
            var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(.45f, 0), new Vector2(.55f, 1));
            var animation = compositor.CreateVector3KeyFrameAnimation();
            animation.Duration = TimeSpan.FromMilliseconds(6200);
            animation.IterationBehavior = AnimationIterationBehavior.Forever;
            animation.DelayTime = TimeSpan.FromMilliseconds(Array.IndexOf(tiles, tile) * 720);
            animation.InsertKeyFrame(0, Vector3.Zero, ease);
            animation.InsertKeyFrame(.5f, new Vector3(0, -6, 0), ease);
            animation.InsertKeyFrame(1, Vector3.Zero, ease);
            visual.StartAnimation("Translation", animation);
        }
    }

    private void StopHeroFloat()
    {
        if (!_heroFloatOn) return;
        _heroFloatOn = false;
        foreach (var tile in new[] { HeroTile0, HeroTile1, HeroTile2 })
        {
            var visual = ElementCompositionPreview.GetElementVisual(tile);
            visual.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
        }
    }

    private async Task AnimateDetailOpenAsync(bool tryConnected)
    {
        DetailHost.Visibility = Visibility.Visible;
        DetailSurface.Opacity = 1;
        DetailScrim.Opacity = 1;
        if (!_animationsEnabled)
        {
            AnimateDetailContentIn(false);
            return;
        }

        // 在等布局之前就把 surface/scrim 视觉透明度压到 0，避免第一帧闪一下完整详情页。
        var surfaceVisual = ElementCompositionPreview.GetElementVisual(DetailSurface);
        var scrimVisual = ElementCompositionPreview.GetElementVisual(DetailScrim);
        var pageVisual = ElementCompositionPreview.GetElementVisual(PageContent);
        ElementCompositionPreview.SetIsTranslationEnabled(DetailSurface, true);
        surfaceVisual.Opacity = 0;
        surfaceVisual.Properties.InsertVector3("Translation", new Vector3(0, 28, 0));
        surfaceVisual.StopAnimation("Scale");
        surfaceVisual.Scale = Vector3.One;
        scrimVisual.Opacity = 0;

        await Task.Delay(24); // 先让新 surface 完成一次布局，ConnectedAnimation 的目标位置才有效。
        pageVisual.StopAnimation("Opacity");
        pageVisual.StopAnimation("Scale");
        pageVisual.Scale = Vector3.One;

        var compositor = surfaceVisual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(FluentEaseOut, FluentEaseOutControl);
        var surfaceFade = compositor.CreateScalarKeyFrameAnimation();
        surfaceFade.Duration = TimeSpan.FromMilliseconds(380);
        surfaceFade.InsertKeyFrame(1, 1, ease);
        surfaceVisual.StartAnimation("Opacity", surfaceFade);

        var surfaceMove = compositor.CreateVector3KeyFrameAnimation();
        surfaceMove.Duration = TimeSpan.FromMilliseconds(520);
        surfaceMove.InsertKeyFrame(1, Vector3.Zero, ease);
        surfaceVisual.StartAnimation("Translation", surfaceMove);

        var scrimFade = compositor.CreateScalarKeyFrameAnimation();
        scrimFade.Duration = TimeSpan.FromMilliseconds(430);
        scrimFade.InsertKeyFrame(1, 1, ease);
        scrimVisual.StartAnimation("Opacity", scrimFade);

        // 不再缩放整页：大尺寸 surface 的缩放会在高 DPI 下明显掉帧，改为只淡出底层。
        var pageFade = compositor.CreateScalarKeyFrameAnimation();
        pageFade.Duration = TimeSpan.FromMilliseconds(420);
        pageFade.InsertKeyFrame(1, .62f, ease);
        pageVisual.StartAnimation("Opacity", pageFade);

        var connected = false;
        if (tryConnected)
        {
            await Task.Delay(150);
            connected = TryStartConnectedAnimation();
        }
        AnimateDetailContentIn(connected);
        await Task.Delay(280);
    }

    private bool TryStartConnectedAnimation()
    {
        if (_detailSourceIcon is null) return false;
        try
        {
            var service = ConnectedAnimationService.GetForCurrentView();
            var animation = service.GetAnimation("PluginIcon");
            if (animation is null) return false;
            animation.Configuration = new BasicConnectedAnimationConfiguration();
            animation.TryStart(DetailIconTile);
            return true;
        }
        catch { return false; }
    }

    private void AnimateDetailContentIn(bool connected)
    {
        if (!_animationsEnabled || DetailSurface is null) return;
        var compositor = ElementCompositionPreview.GetElementVisual(DetailSurface).Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(FluentEaseOut, FluentEaseOutControl);

        void Animate(FrameworkElement element, float fromY, float fromScale, int delay, int duration = 360)
        {
            try
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                ElementCompositionPreview.SetIsTranslationEnabled(element, true);
                visual.StopAnimation("Translation");
                visual.StopAnimation("Opacity");
                visual.StopAnimation("Scale");
                visual.Properties.InsertVector3("Translation", new Vector3(0, fromY, 0));
                visual.Opacity = 0;
                if (fromScale != 1)
                {
                    visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
                    visual.Scale = new Vector3(fromScale, fromScale, 1);
                }

                var fade = compositor.CreateScalarKeyFrameAnimation();
                fade.Duration = TimeSpan.FromMilliseconds(duration);
                fade.DelayTime = TimeSpan.FromMilliseconds(delay);
                fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
                fade.InsertKeyFrame(1, 1, ease);
                visual.StartAnimation("Opacity", fade);

                var move = compositor.CreateVector3KeyFrameAnimation();
                move.Duration = TimeSpan.FromMilliseconds(duration);
                move.DelayTime = TimeSpan.FromMilliseconds(delay);
                move.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
                move.InsertKeyFrame(1, Vector3.Zero, ease);
                visual.StartAnimation("Translation", move);

                if (fromScale != 1)
                {
                    var scale = compositor.CreateVector3KeyFrameAnimation();
                    scale.Duration = TimeSpan.FromMilliseconds(duration);
                    scale.DelayTime = TimeSpan.FromMilliseconds(delay);
                    scale.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
                    scale.InsertKeyFrame(1, Vector3.One, ease);
                    visual.StartAnimation("Scale", scale);
                }
            }
            catch { }
        }

        if (!connected) Animate(DetailIconTile, 0, .94f, 0, 420);
        Animate(DetailTopBar, -6, 1, 0, 320);
        Animate(DetailTitle, 10, 1, 70, 380);
        Animate(DetailInstallButton, 8, 1, 120, 360);
        if (DetailLeft.Visibility == Visibility.Visible) Animate(DetailLeft, 16, 1, 180, 420);
        if (DetailSidebar.Visibility == Visibility.Visible) Animate(DetailSidebar, 16, 1, 240, 420);
    }

    private async Task AnimateDetailCloseAsync()
    {
        if (!_animationsEnabled)
        {
            DetailHost.Visibility = Visibility.Collapsed;
            return;
        }
        var surfaceVisual = ElementCompositionPreview.GetElementVisual(DetailSurface);
        var scrimVisual = ElementCompositionPreview.GetElementVisual(DetailScrim);
        var pageVisual = ElementCompositionPreview.GetElementVisual(PageContent);
        var compositor = surfaceVisual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(.4f, 0), new Vector2(.6f, 1));

        // 关闭不是“闪一下消失”：内容先轻微下沉淡出，底层页面同步回弹，整段约 0.4s。
        ElementCompositionPreview.SetIsTranslationEnabled(DetailSurface, true);
        surfaceVisual.StopAnimation("Translation");
        surfaceVisual.StopAnimation("Opacity");
        surfaceVisual.StopAnimation("Scale");
        surfaceVisual.Scale = Vector3.One;
        surfaceVisual.Properties.InsertVector3("Translation", Vector3.Zero);

        var surfaceFade = compositor.CreateScalarKeyFrameAnimation();
        surfaceFade.Duration = TimeSpan.FromMilliseconds(260);
        surfaceFade.InsertKeyFrame(1, 0, ease);
        surfaceVisual.StartAnimation("Opacity", surfaceFade);

        var surfaceMove = compositor.CreateVector3KeyFrameAnimation();
        surfaceMove.Duration = TimeSpan.FromMilliseconds(380);
        surfaceMove.InsertKeyFrame(1, new Vector3(0, 20, 0), ease);
        surfaceVisual.StartAnimation("Translation", surfaceMove);

        var scrimFade = compositor.CreateScalarKeyFrameAnimation();
        scrimFade.Duration = TimeSpan.FromMilliseconds(360);
        scrimFade.InsertKeyFrame(1, 0, ease);
        scrimVisual.StartAnimation("Opacity", scrimFade);

        var pageFade = compositor.CreateScalarKeyFrameAnimation();
        pageFade.Duration = TimeSpan.FromMilliseconds(360);
        pageFade.InsertKeyFrame(1, 1, ease);
        pageVisual.StartAnimation("Opacity", pageFade);

        await Task.Delay(400);
        DetailHost.Visibility = Visibility.Collapsed;
        surfaceVisual.StopAnimation("Opacity");
        surfaceVisual.StopAnimation("Translation");
        surfaceVisual.StopAnimation("Scale");
        surfaceVisual.Opacity = 1;
        surfaceVisual.Scale = Vector3.One;
        surfaceVisual.Properties.InsertVector3("Translation", Vector3.Zero);
        scrimVisual.StopAnimation("Opacity");
        scrimVisual.Opacity = 0;
    }

    private void StartSkeletonPulse()
    {
        if (!_animationsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(ReadmeSkeleton);
        var compositor = visual.Compositor;
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(1400);
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.InsertKeyFrame(0, .45f);
        animation.InsertKeyFrame(.5f, .95f);
        animation.InsertKeyFrame(1, .45f);
        visual.StartAnimation("Opacity", animation);
    }

    private void StopSkeletonPulse()
    {
        var visual = ElementCompositionPreview.GetElementVisual(ReadmeSkeleton);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1;
    }

    private void AnimateReadmeReveal()
    {
        if (!_animationsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(ReadmeView);
        ElementCompositionPreview.SetIsTranslationEnabled(ReadmeView, true);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(FluentEaseOut, FluentEaseOutControl);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Translation");
        visual.Opacity = 0;
        visual.Properties.InsertVector3("Translation", new Vector3(0, 10, 0));
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = TimeSpan.FromMilliseconds(320);
        fade.InsertKeyFrame(1, 1, ease);
        visual.StartAnimation("Opacity", fade);
        var move = compositor.CreateVector3KeyFrameAnimation();
        move.Duration = TimeSpan.FromMilliseconds(320);
        move.InsertKeyFrame(1, Vector3.Zero, ease);
        visual.StartAnimation("Translation", move);
    }

    private void AnimateInstallSuccess()
    {
        if (!_animationsEnabled) return;
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(DetailInstallButton);
            visual.CenterPoint = new Vector3((float)DetailInstallButton.ActualWidth / 2, (float)DetailInstallButton.ActualHeight / 2, 0);
            visual.StopAnimation("Scale");
            var compositor = visual.Compositor;
            var ease = compositor.CreateCubicBezierEasingFunction(FluentEaseOut, FluentEaseOutControl);
            var pop = compositor.CreateVector3KeyFrameAnimation();
            pop.Duration = TimeSpan.FromMilliseconds(420);
            pop.InsertKeyFrame(0, Vector3.One, ease);
            pop.InsertKeyFrame(.38f, new Vector3(1.07f, 1.07f, 1), ease);
            pop.InsertKeyFrame(1, Vector3.One, ease);
            visual.StartAnimation("Scale", pop);
            FluentMotion.AnimateIcon(DetailInstallIcon, false, celebrate: true);
        }
        catch { }
    }

    private static void AnimateScalar(Visual visual, string property, float target, int milliseconds, int delay = 0)
    {
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(FluentEaseOut, FluentEaseOutControl);
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(milliseconds);
        animation.DelayTime = TimeSpan.FromMilliseconds(delay);
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        animation.InsertKeyFrame(1, target, ease);
        visual.StartAnimation(property, animation);
    }

    private static void AnimateVector3(Visual visual, string property, Vector3 target, int milliseconds, int delay = 0)
    {
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(FluentEaseOut, FluentEaseOutControl);
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(milliseconds);
        animation.DelayTime = TimeSpan.FromMilliseconds(delay);
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        animation.InsertKeyFrame(1, target, ease);
        visual.StartAnimation(property, animation);
    }
}
