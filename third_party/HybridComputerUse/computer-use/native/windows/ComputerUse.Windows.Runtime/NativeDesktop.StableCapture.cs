using System.Drawing;

namespace ComputerUse.Windows.Runtime;

// The screenshot retry loop, apart from GDI capture and encoding, so that a
// portable test can drive it with scripted layout changes.
internal static partial class NativeDesktop
{
    private const int CaptureLayoutRetryCount = 3;
    private const int CaptureLayoutRetryMilliseconds = 25;

    internal static ScreenFrame Capture() => Capture(null, includePixels: false);

    internal static ScreenFrame Capture(Rectangle requestedBounds) =>
        Capture(requestedBounds, includePixels: false);

    internal static ScreenFrame CaptureForSettling() => Capture(null, includePixels: true);

    private static ScreenFrame Capture(Rectangle? requestedBounds, bool includePixels)
    {
        if (!TryCapture(requestedBounds, out var frame, includePixels) || frame is null)
        {
            throw new InvalidOperationException("requested_capture_bounds_unavailable");
        }
        return frame;
    }

    /// Captures requestedBounds, or the whole desktop when it is null. Each
    /// attempt resolves the capture rectangle from the layout it sampled, so
    /// a monitor added or removed between attempts cannot pair an old crop
    /// with the new layout's frame id.
    internal static bool TryCapture(
        Rectangle? requestedBounds,
        out ScreenFrame? frame,
        bool includePixels = false)
    {
        for (var attempt = 1; attempt <= CaptureLayoutRetryCount; attempt++)
        {
            var layoutBefore = CurrentLayout();
            var desktopBounds = new Rectangle(
                layoutBefore.DesktopX,
                layoutBefore.DesktopY,
                layoutBefore.DesktopWidth,
                layoutBefore.DesktopHeight);
            if (!WindowsCaptureBounds.TryResolve(
                    desktopBounds,
                    requestedBounds ?? desktopBounds,
                    out var bounds))
            {
                frame = null;
                return false;
            }
            var candidate = CaptureFrame(bounds, layoutBefore.FrameId, includePixels);
            var layoutAfter = CurrentLayout();
            if (string.Equals(
                    layoutBefore.FrameId,
                    layoutAfter.FrameId,
                    StringComparison.Ordinal))
            {
                RememberPublishedLayout(layoutBefore);
                frame = candidate;
                return true;
            }
            if (attempt < CaptureLayoutRetryCount)
            {
                Thread.Sleep(CaptureLayoutRetryMilliseconds);
            }
        }
        throw new ProviderFailureException(
            "desktop_layout_unstable",
            "observation",
            "Desktop layout changed during every bounded screenshot capture attempt.");
    }
}
