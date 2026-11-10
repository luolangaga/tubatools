using System.Drawing;

namespace ComputerUse.Windows.Runtime;

internal static class WindowsUiaVisibility
{
    internal static bool IsEffectivelyVisible(
        bool isOffscreen,
        bool hasKeyboardFocus,
        Rectangle bounds,
        Rectangle windowBounds)
    {
        if (!isOffscreen)
        {
            return true;
        }
        if (!hasKeyboardFocus || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }
        var intersection = Rectangle.Intersect(bounds, windowBounds);
        return intersection.Width > 0 && intersection.Height > 0;
    }
}
