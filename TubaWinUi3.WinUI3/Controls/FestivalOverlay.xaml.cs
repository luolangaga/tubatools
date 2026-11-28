using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Controls;

/// <summary>
/// 全窗节日氛围层：静态装饰用 XAML（emoji/雪堆/月亮光晕），
/// 粒子全部用 Composition 形状（不产生 XAML 元素）。
///
/// 约束：
/// - 根节点 IsHitTestVisible=false，永不遮挡工具卡点击；
/// - 只有启动页退场后才构建；低配 / 系统关闭动画时只保留静态装饰；
/// - 窗口失焦时整层 IsVisible=false，停止合成渲染；
/// - 尺寸变化 260ms 去抖后重建，避免拖拽窗口时每帧重排粒子。
/// </summary>
public sealed partial class FestivalOverlay : UserControl
{
    private const int FullParticleCount = 44;
    private const int LightParticleCount = 16;

    private readonly Random _random = new();
    private ContainerVisual? _particleRoot;
    private FestivalOccasion? _occasion;
    private bool _initialized;
    private bool _active = true;
    private bool _dismissed;
    private DispatcherQueueTimer? _resizeTimer;

    public FestivalOverlay()
    {
        InitializeComponent();
        SizeChanged += OnOverlaySizeChanged;
        Unloaded += OnOverlayUnloaded;
        FestivalService.FireworksRequested += OnFireworksRequested;
    }

    /// <summary>由 MainWindow 在启动页退场后调用；之前不构建任何粒子。</summary>
    public void Initialize()
    {
        _initialized = true;
        Refresh();
    }

    /// <summary>开关 / 预览 / 跨日变化时重建。幂等，可反复调用。</summary>
    public void Refresh()
    {
        if (!_initialized) return;
        var occasion = FestivalService.IsVisible ? FestivalService.Current : null;
        _occasion = occasion;
        Rebuild();
    }

    public void SetActive(bool active)
    {
        _active = active;
        if (_particleRoot is not null)
            _particleRoot.IsVisible = active && !_dismissed && Visibility == Visibility.Visible;
    }

    /// <summary>用户手动关闭全窗特效：只收装饰与粒子，首页的节日徽章/倒计时保持不动。</summary>
    public void Dismiss()
    {
        _dismissed = true;
        ClearVisuals();
        Visibility = Visibility.Collapsed;
    }

    public void Stop()
    {
        _occasion = null;
        _dismissed = true;
        ClearVisuals();
        Visibility = Visibility.Collapsed;
    }

    private void Rebuild()
    {
        var occasion = _occasion;
        ClearVisuals();
        _dismissed = false;
        if (occasion is null || FestivalService.IsEffectDismissed(occasion))
        {
            _dismissed = true;
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        BuildDecor(occasion);

        var intensity = FestivalService.Intensity;
        var style = occasion.Definition.ParticleStyle;
        if (intensity == FestivalIntensity.Full)
            BuildParticles(style, FullParticleCount);
        else if (intensity == FestivalIntensity.Light)
            BuildParticles(style, LightParticleCount);
    }

    private void ClearVisuals()
    {
        DecorCanvas.Children.Clear();
        if (_particleRoot is not null)
        {
            ElementCompositionPreview.SetElementChildVisual(ParticleHost, null);
            _particleRoot.IsVisible = false;
            _particleRoot = null;
        }
    }

    private void OnOverlaySizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_initialized || _occasion is null || _dismissed) return;
        if (_resizeTimer is null)
        {
            _resizeTimer = DispatcherQueue.CreateTimer();
            _resizeTimer.IsRepeating = false;
            _resizeTimer.Tick += (timer, _) =>
            {
                timer.Stop();
                if (_initialized && _occasion is not null) Rebuild();
            };
        }
        _resizeTimer.Stop();
        _resizeTimer.Interval = TimeSpan.FromMilliseconds(260);
        _resizeTimer.Start();
    }

    private void OnOverlayUnloaded(object sender, RoutedEventArgs e)
    {
        FestivalService.FireworksRequested -= OnFireworksRequested;
        _resizeTimer?.Stop();
        ClearVisuals();
    }

    private void OnFireworksRequested()
    {
        if (DispatcherQueue.HasThreadAccess) Burst();
        else DispatcherQueue.TryEnqueue(Burst);
    }

    // ---------------------------------------------------------------- decor

    private void BuildDecor(FestivalOccasion occasion)
    {
        DecorCanvas.Children.Clear();
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 80 || height < 80) return;

        switch (occasion.Definition.Id)
        {
            case FestivalCatalog.Spring:
                AddEmoji("🏮", 42, width - 132, 62, swing: true);
                AddEmoji("🏮", 42, width - 84, 74, swing: true);
                break;

            case FestivalCatalog.NewYear:
                AddYearText(width - 118, height - 82, (occasion.FestivalDate.Year + 1).ToString());
                AddEmoji("🎊", 44, width - 108, 64, floaty: true);
                break;
        }
    }

    private TextBlock AddEmoji(string emoji, double size, double left, double top,
        double opacity = 0.92, bool swing = false, bool floaty = false)
    {
        var text = new TextBlock
        {
            Text = emoji,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = size,
            Opacity = opacity,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(text, left);
        Canvas.SetTop(text, top);
        DecorCanvas.Children.Add(text);

        if (FestivalService.Intensity == FestivalIntensity.Full && (swing || floaty))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try { AnimateEmoji(text, size, swing, floaty); }
                catch { }
            });
        }
        return text;
    }

    private static void AnimateEmoji(TextBlock element, double size, bool swing, bool floaty)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;

        if (swing)
        {
            visual.CenterPoint = new Vector3((float)(size / 2), 0, 0);
            var rotation = compositor.CreateScalarKeyFrameAnimation();
            rotation.InsertKeyFrame(0f, -7f);
            rotation.InsertKeyFrame(0.5f, 7f);
            rotation.InsertKeyFrame(1f, -7f);
            rotation.Duration = TimeSpan.FromSeconds(4.2);
            rotation.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("RotationAngleInDegrees", rotation);
        }

        if (floaty)
        {
            var bob = compositor.CreateVector3KeyFrameAnimation();
            bob.InsertKeyFrame(0f, Vector3.Zero);
            bob.InsertKeyFrame(0.5f, new Vector3(0, 9, 0));
            bob.InsertKeyFrame(1f, Vector3.Zero);
            bob.Duration = TimeSpan.FromSeconds(5.8);
            bob.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("Offset", bob);
        }
    }

    private void AddYearText(double left, double top, string year)
    {
        var text = new TextBlock
        {
            Text = year,
            FontSize = 34,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 213, 79)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(text, left);
        Canvas.SetTop(text, top);
        DecorCanvas.Children.Add(text);
    }

    // ------------------------------------------------------------- particles

    private void BuildParticles(FestivalParticleStyle style, int count)
    {
        if (style == FestivalParticleStyle.None) return;
        var width = (float)ActualWidth;
        var height = (float)ActualHeight;
        if (width < 80 || height < 80) return;

        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        var root = compositor.CreateContainerVisual();
        root.IsVisible = _active;
        ElementCompositionPreview.SetElementChildVisual(ParticleHost, root);
        _particleRoot = root;

        for (var index = 0; index < count; index++)
        {
            try { CreateParticle(compositor, root, style, width, height); }
            catch { /* 单个粒子失败不影响整体 */ }
        }
    }

    private void CreateParticle(Compositor compositor, ContainerVisual root, FestivalParticleStyle style,
        float width, float height)
    {
        switch (style)
        {
            case FestivalParticleStyle.RedPacket:
            {
                var visual = CreateRedPacket(compositor);
                AnimateFall(compositor, visual, Next(0, width), Next(-height * 0.2f, height * 0.65f),
                    height + 20, Next(-24, 24), Next(8, 15), spin: true, fade: false, spinDegrees: 18);
                root.Children.InsertAtTop(visual);
                break;
            }

            case FestivalParticleStyle.Confetti:
            {
                var palette = new[]
                {
                    Color.FromArgb(230, 255, 213, 79),
                    Color.FromArgb(230, 239, 83, 80),
                    Color.FromArgb(230, 129, 212, 250),
                    Color.FromArgb(230, 179, 157, 219),
                    Color.FromArgb(230, 255, 255, 255)
                };
                var color = palette[_random.Next(palette.Length)];
                var visual = CreateRoundedRect(compositor, color, Next(4.5f, 7f), Next(8f, 12f), 1.5f);
                AnimateFall(compositor, visual, Next(0, width), Next(-height * 0.25f, height * 0.7f),
                    height + 16, Next(-40, 40), Next(9, 18), spin: true, fade: false, spinDegrees: 28);
                root.Children.InsertAtTop(visual);
                break;
            }
        }
    }

    private void AnimateFall(Compositor compositor, Visual visual, float x, float startY, float endY,
        float drift, float seconds, bool spin, bool fade, float spinDegrees = 360f)
    {
        visual.Offset = new Vector3(x, startY, 0);
        var offset = compositor.CreateVector3KeyFrameAnimation();
        offset.InsertKeyFrame(0f, new Vector3(x, startY, 0));
        offset.InsertKeyFrame(0.5f, new Vector3(x + drift, (startY + endY) / 2, 0));
        offset.InsertKeyFrame(1f, new Vector3(x - drift, endY, 0));
        offset.Duration = TimeSpan.FromSeconds(seconds);
        offset.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Offset", offset);

        if (spin)
        {
            var rotation = compositor.CreateScalarKeyFrameAnimation();
            rotation.InsertKeyFrame(0f, 0f);
            rotation.InsertKeyFrame(1f, spinDegrees);
            rotation.Duration = TimeSpan.FromSeconds(Next(5f, 11f));
            rotation.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("RotationAngleInDegrees", rotation);
        }
    }

    private void Burst()
    {
        var root = _particleRoot;
        if (root is null) return;
        if (FestivalService.Intensity is not (FestivalIntensity.Full or FestivalIntensity.Light)) return;

        var width = (float)ActualWidth;
        var height = (float)ActualHeight;
        if (width < 80 || height < 80) return;

        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        var center = new Vector3(width * 0.5f, height * 0.42f, 0);
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.8f), new Vector2(0.2f, 1f));
        var visuals = new List<Visual>();

        for (var index = 0; index < 78; index++)
        {
            var angle = Next(0, (float)Math.PI * 2);
            var distance = Next(40, 170);
            var color = index % 3 == 0
                ? Color.FromArgb(240, 255, 255, 255)
                : index % 2 == 0
                    ? Color.FromArgb(240, 255, 213, 79)
                    : Color.FromArgb(240, 255, 112, 89);
            var size = Next(4f, 8f);
            var visual = CreateEllipse(compositor, color, size, size);
            visual.Offset = center;
            root.Children.InsertAtTop(visual);

            var offset = compositor.CreateVector3KeyFrameAnimation();
            offset.InsertKeyFrame(0f, center);
            offset.InsertKeyFrame(1f, new Vector3(
                center.X + (float)Math.Cos(angle) * distance,
                center.Y + (float)Math.Sin(angle) * distance + 34, 0), easing);
            offset.Duration = TimeSpan.FromSeconds(1.7);
            visual.StartAnimation("Offset", offset);

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(0f, 1f);
            opacity.InsertKeyFrame(1f, 0f, easing);
            opacity.Duration = TimeSpan.FromSeconds(1.7);
            visual.StartAnimation("Opacity", opacity);

            visuals.Add(visual);
        }

        _ = RemoveBurstAsync(visuals, root);
    }

    private async Task RemoveBurstAsync(List<Visual> visuals, ContainerVisual root)
    {
        try { await Task.Delay(1900); } catch { }
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var visual in visuals)
            {
                try { root.Children.Remove(visual); } catch { }
            }
        });
    }

    // -------------------------------------------------------------- primitives

    private ShapeVisual CreateEllipse(Compositor compositor, Color color, float width, float height,
        Color? stroke = null, float strokeThickness = 0)
    {
        var geometry = compositor.CreateEllipseGeometry();
        geometry.Center = new Vector2(width / 2, height / 2);
        geometry.Radius = new Vector2(width / 2, height / 2);
        var shape = compositor.CreateSpriteShape(geometry);
        shape.FillBrush = compositor.CreateColorBrush(color);
        if (stroke is not null && strokeThickness > 0)
        {
            shape.StrokeBrush = compositor.CreateColorBrush(stroke.Value);
            shape.StrokeThickness = strokeThickness;
        }
        var visual = compositor.CreateShapeVisual();
        visual.Size = new Vector2(width, height);
        visual.Shapes.Add(shape);
        return visual;
    }

    private ShapeVisual CreateRoundedRect(Compositor compositor, Color color, float width, float height, float radius)
    {
        var geometry = compositor.CreateRoundedRectangleGeometry();
        geometry.Size = new Vector2(width, height);
        geometry.CornerRadius = new Vector2(radius, radius);
        var shape = compositor.CreateSpriteShape(geometry);
        shape.FillBrush = compositor.CreateColorBrush(color);
        var visual = compositor.CreateShapeVisual();
        visual.Size = new Vector2(width, height);
        visual.Shapes.Add(shape);
        return visual;
    }

    private ShapeVisual CreateRedPacket(Compositor compositor)
    {
        const float width = 14f;
        const float height = 20f;
        var visual = compositor.CreateShapeVisual();
        visual.Size = new Vector2(width, height);

        var body = compositor.CreateRoundedRectangleGeometry();
        body.Size = new Vector2(width, height);
        body.CornerRadius = new Vector2(2.5f, 2.5f);
        var bodyShape = compositor.CreateSpriteShape(body);
        bodyShape.FillBrush = compositor.CreateColorBrush(Color.FromArgb(235, 211, 47, 47));
        visual.Shapes.Add(bodyShape);

        var band = compositor.CreateRoundedRectangleGeometry();
        band.Size = new Vector2(width * 0.72f, height * 0.16f);
        band.CornerRadius = new Vector2(1f, 1f);
        var bandShape = compositor.CreateSpriteShape(band);
        bandShape.Offset = new Vector2(width * 0.14f, height * 0.3f);
        bandShape.FillBrush = compositor.CreateColorBrush(Color.FromArgb(235, 255, 213, 79));
        visual.Shapes.Add(bandShape);

        return visual;
    }

    private float Next(float min, float max) => min + _random.NextSingle() * (max - min);
}
