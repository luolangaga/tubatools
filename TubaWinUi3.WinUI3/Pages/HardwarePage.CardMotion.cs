using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace TubaWinUi3.Pages;

public sealed partial class HardwarePage
{
    private sealed record DiagramCardFlight(Point Origin, Point Destination, double DelayMs, double DurationMs);
    private readonly Dictionary<string, DiagramCardFlight> _diagramCardFlights = [];
    private long _diagramFlightStartedAt;
    private bool _diagramFlightRendering;
    private const float DiagramCardInitialScale = .06f;

    private void RevealDiagramCallouts(Button? stableCard = null)
    {
        if (!_pageAlive) return;
        _calloutRevealTask = RevealDiagramCalloutsAsync(stableCard);
    }

    private async Task RevealDiagramCalloutsAsync(Button? stableCard)
    {
        if (!_pageAlive) return;
        var version = ++_calloutRevealVersion;
        CancelDiagramCardFlights();
        var cards = _diagramCallouts.OrderBy(item => Canvas.GetTop(item.Card)).ToArray();
        foreach (var item in cards) item.Card.IsHitTestVisible = false;

        // Use the final restored computer pose for the cards' launch points.
        if (stableCard is not null && !ReduceDiagramMotion) await Task.Delay(16);
        if (!_pageAlive || version != _calloutRevealVersion || !UseSettingsStyle) return;
        HardwareDiagram.UpdateLayout();

        var reducedMotion = ReduceDiagramMotion;
        var duration = stableCard is null ? 720 : 600;
        var stagger = stableCard is null ? 36 : 22;
        var flightIndex = 0;
        _diagramFlightStartedAt = Stopwatch.GetTimestamp();
        foreach (var (card, label, _, key) in cards)
        {
            ElementCompositionPreview.SetIsTranslationEnabled(card, true);
            var visual = ElementCompositionPreview.GetElementVisual(card);
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Translation");
            visual.StopAnimation("Scale");
            visual.StopAnimation("RotationAngle");
            visual.RotationAngle = 0;
            visual.CenterPoint = new Vector3((float)card.ActualWidth / 2, (float)card.ActualHeight / 2, 0);
            card.Opacity = 1;
            if (reducedMotion || ReferenceEquals(card, stableCard))
            {
                visual.Opacity = 1;
                visual.Scale = Vector3.One;
                visual.Properties.InsertVector3("Translation", Vector3.Zero);
                continue;
            }

            var origin = DiagramModelAnchor(label);
            var destination = new Point(Canvas.GetLeft(card) + card.ActualWidth / 2,
                Canvas.GetTop(card) + card.ActualHeight / 2);
            var offset = new Vector3((float)(origin.X - destination.X), (float)(origin.Y - destination.Y), 0);
            var delayMs = flightIndex++ * stagger;
            _diagramCardFlights[key] = new(origin, destination, delayMs, duration);
            var compositor = visual.Compositor;
            // x(t)=t, y(t)=smoothstep(t): connector geometry uses the same flight curve.
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(1f / 3, 0), new Vector2(2f / 3, 1));
            var delay = TimeSpan.FromMilliseconds(delayMs);
            var flightDuration = TimeSpan.FromMilliseconds(duration);

            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0);
            fade.InsertKeyFrame(.28f, 1, compositor.CreateLinearEasingFunction());
            fade.InsertKeyFrame(1, 1);
            fade.Duration = flightDuration; fade.DelayTime = delay;
            visual.Opacity = 0;
            visual.StartAnimation("Opacity", fade);

            var move = compositor.CreateVector3KeyFrameAnimation();
            move.InsertKeyFrame(0, offset); move.InsertKeyFrame(1, Vector3.Zero, easing);
            move.Duration = flightDuration; move.DelayTime = delay;
            visual.Properties.InsertVector3("Translation", offset);
            visual.StartAnimation("Translation", move);

            var grow = compositor.CreateVector3KeyFrameAnimation();
            var initialScale = new Vector3(DiagramCardInitialScale, DiagramCardInitialScale, 1);
            grow.InsertKeyFrame(0, initialScale); grow.InsertKeyFrame(1, Vector3.One, easing);
            grow.Duration = flightDuration; grow.DelayTime = delay;
            visual.Scale = initialScale;
            visual.StartAnimation("Scale", grow);
        }

        HardwareConnectors.Opacity = 1;
        foreach (var line in _connectorLines.Values) { line.StrokeDashArray = null; line.StrokeDashOffset = 0; }
        if (_diagramCardFlights.Count > 0)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += DiagramCardFlight_Rendering;
            _diagramFlightRendering = true;
        }
        DrawHardwareConnectors();
        if (!reducedMotion && flightIndex > 0) await Task.Delay((flightIndex - 1) * stagger + duration + 32);
        if (!_pageAlive || version != _calloutRevealVersion || !UseSettingsStyle) return;
        CancelDiagramCardFlights();
        foreach (var (card, _, _, _) in cards)
        {
            var visual = ElementCompositionPreview.GetElementVisual(card);
            visual.StopAnimation("Opacity"); visual.Opacity = 1;
            visual.StopAnimation("Translation"); visual.Properties.InsertVector3("Translation", Vector3.Zero);
            visual.StopAnimation("Scale"); visual.Scale = Vector3.One;
            card.IsHitTestVisible = true;
        }
        DrawHardwareConnectors();
    }

    private Point DiagramModelAnchor(string label)
    {
        var anchor = _modelAnchors.TryGetValue(label, out var point) ? point : new Point(.5, .62);
        return HardwareModelViewport.TransformToVisual(HardwareDiagram).TransformPoint(
            new Point(anchor.X * HardwareModelViewport.ActualWidth, anchor.Y * HardwareModelViewport.ActualHeight));
    }

    private bool TryGetDiagramCardFlight(string key, out Point center, out double scale, out double opacity)
    {
        center = default; scale = 1; opacity = 1;
        if (!_diagramCardFlights.TryGetValue(key, out var flight)) return false;
        var time = Math.Clamp((Stopwatch.GetElapsedTime(_diagramFlightStartedAt).TotalMilliseconds - flight.DelayMs) / flight.DurationMs, 0, 1);
        var progress = time * time * (3 - 2 * time);
        center = new Point(flight.Origin.X + (flight.Destination.X - flight.Origin.X) * progress,
            flight.Origin.Y + (flight.Destination.Y - flight.Origin.Y) * progress);
        scale = DiagramCardInitialScale + (1 - DiagramCardInitialScale) * progress;
        opacity = Math.Clamp((time - .08) / .28, 0, 1);
        return true;
    }

    private void DiagramCardFlight_Rendering(object? sender, object e)
    {
        if (!_pageAlive || !UseSettingsStyle || _hardwareFocus is not null) { CancelDiagramCardFlights(); return; }
        DrawHardwareConnectors();
    }

    private void CancelDiagramCardFlights()
    {
        if (_diagramFlightRendering) Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= DiagramCardFlight_Rendering;
        _diagramFlightRendering = false;
        _diagramCardFlights.Clear();
    }

    private void AnimateCalloutHover(Button card, bool hovered)
    {
        if (!card.IsHitTestVisible || _hardwareFocus is not null) return;
        var visual = ElementCompositionPreview.GetElementVisual(card);
        if (ReduceDiagramMotion)
        {
            visual.StopAnimation("Scale"); visual.Scale = Vector3.One;
            visual.StopAnimation("Translation"); visual.Properties.InsertVector3("Translation", Vector3.Zero);
            visual.StopAnimation("RotationAngle"); visual.RotationAngle = 0;
            return;
        }
        visual.CenterPoint = new Vector3((float)card.ActualWidth / 2, (float)card.ActualHeight / 2, 0);
        visual.RotationAxis = Vector3.UnitZ;
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(1f / 3, 0), new Vector2(2f / 3, 1));
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(1, hovered ? new Vector3(1.012f, 1.012f, 1) : Vector3.One);
        scale.Duration = TimeSpan.FromMilliseconds(240);
        visual.StartAnimation("Scale", scale);
        var drift = compositor.CreateVector3KeyFrameAnimation();
        var tilt = compositor.CreateScalarKeyFrameAnimation();
        if (hovered)
        {
            drift.InsertKeyFrame(0, Vector3.Zero);
            drift.InsertKeyFrame(.25f, new Vector3(.6f, -1.1f, 0), ease);
            drift.InsertKeyFrame(.5f, new Vector3(0, -2.2f, 0), ease);
            drift.InsertKeyFrame(.75f, new Vector3(-.6f, -1.1f, 0), ease);
            drift.InsertKeyFrame(1, Vector3.Zero, ease);
            tilt.InsertKeyFrame(0, 0);
            tilt.InsertKeyFrame(.25f, .0025f, ease); tilt.InsertKeyFrame(.5f, 0, ease);
            tilt.InsertKeyFrame(.75f, -.0025f, ease); tilt.InsertKeyFrame(1, 0, ease);
            drift.Duration = tilt.Duration = TimeSpan.FromMilliseconds(3200);
            drift.IterationBehavior = tilt.IterationBehavior = AnimationIterationBehavior.Forever;
        }
        else
        {
            // Omit the first frame to settle from the compositor's current animated pose.
            drift.InsertKeyFrame(1, Vector3.Zero, ease); tilt.InsertKeyFrame(1, 0, ease);
            drift.Duration = tilt.Duration = TimeSpan.FromMilliseconds(280);
        }
        visual.StartAnimation("Translation", drift);
        visual.StartAnimation("RotationAngle", tilt);
    }
}
