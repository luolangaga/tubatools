using System.Drawing;

namespace ComputerUse.Windows.Runtime;

internal sealed class PointerPreflightException : Exception
{
    internal PointerPreflightException(string code, string message) : base(message)
    {
        Code = code;
    }

    internal string Code { get; }
}

internal static class WindowsPointerPreflight
{
    internal static Rectangle ReadTarget(
        Func<bool?> readOffscreen,
        Func<bool> readEnabled,
        Func<Rectangle> readBounds)
    {
        var offscreen = readOffscreen();
        if (offscreen is null)
        {
            throw new PointerPreflightException(
                "visibility_unknown",
                "The target's live UIA visibility is unknown; no pointer input was emitted.");
        }
        if (offscreen.Value)
        {
            throw new PointerPreflightException(
                "target_offscreen",
                "The target is currently UIA-offscreen; no pointer input was emitted.");
        }
        if (!readEnabled())
        {
            throw new PointerPreflightException(
                "target_disabled",
                "The target is currently disabled; no pointer input was emitted.");
        }
        var bounds = readBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new PointerPreflightException(
                "geometry_unavailable",
                "The exact target has no usable live geometry.");
        }
        return bounds;
    }

    internal static Point Verify(
        Func<bool?> readOffscreen,
        Func<bool> readEnabled,
        Func<Rectangle> readBounds,
        Action verifyOwnership,
        Func<Point, bool> nativePointOwned,
        Func<Point, bool> exactElementAtPoint)
    {
        var bounds = ReadTarget(readOffscreen, readEnabled, readBounds);
        var point = new Point(
            checked(bounds.Left + bounds.Width / 2),
            checked(bounds.Top + bounds.Height / 2));
        verifyOwnership();
        if (!nativePointOwned(point) || !exactElementAtPoint(point))
        {
            throw new PointerPreflightException(
                "point_owner_mismatch",
                "The live pointer location is not owned by the exact target; no pointer input was emitted.");
        }
        return point;
    }
}
