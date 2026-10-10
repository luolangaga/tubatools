using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace TubaWinUi3.Services;

/// <summary>仅动画视觉层，不触发布局；遵循系统动画设置。</summary>
internal static class FluentMotion
{
    public static void AnimateIcon(FrameworkElement element, bool hovered, bool celebrate = false)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        visual.StopAnimation("Scale");
        visual.StopAnimation("RotationAngleInDegrees");
        if (!new UISettings().AnimationsEnabled)
        {
            visual.Scale = Vector3.One;
            visual.RotationAngleInDegrees = 0;
            return;
        }

        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(.16f, 1), new Vector2(.3f, 1));
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.Duration = TimeSpan.FromMilliseconds(celebrate ? 420 : 180);
        if (celebrate)
        {
            scale.InsertKeyFrame(.35f, new Vector3(1.13f, .94f, 1), easing);
            scale.InsertKeyFrame(.65f, new Vector3(.98f, 1.08f, 1), easing);
        }
        scale.InsertKeyFrame(1, new Vector3(hovered ? 1.06f : 1), easing);
        visual.StartAnimation("Scale", scale);

        if (celebrate)
        {
            var wobble = compositor.CreateScalarKeyFrameAnimation();
            wobble.Duration = TimeSpan.FromMilliseconds(420);
            wobble.InsertKeyFrame(0, 0);
            wobble.InsertKeyFrame(.3f, -8, easing);
            wobble.InsertKeyFrame(.65f, 5, easing);
            wobble.InsertKeyFrame(1, 0, easing);
            visual.StartAnimation("RotationAngleInDegrees", wobble);
        }
        else visual.RotationAngleInDegrees = 0;
    }
}
