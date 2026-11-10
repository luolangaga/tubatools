using System.Drawing;

namespace ComputerUse.Windows.Runtime;

internal static class WindowsCaptureBounds
{
    internal static bool TryResolve(
        Rectangle desktopBounds,
        Rectangle requestedBounds,
        out Rectangle captureBounds)
    {
        captureBounds = Rectangle.Intersect(desktopBounds, requestedBounds);
        return captureBounds.Width > 0 && captureBounds.Height > 0;
    }
}
