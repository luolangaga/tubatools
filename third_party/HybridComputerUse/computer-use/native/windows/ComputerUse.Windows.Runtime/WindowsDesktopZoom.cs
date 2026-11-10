using System.Drawing;

namespace ComputerUse.Windows.Runtime;

// GUI zoom geometry, shared with the Linux and macOS providers and pinned by
// testdata/contract/zoom_geometry.json. The region is in pixels of the
// delivered full-desktop frame; it is snapped outward to whole native pixels,
// and each axis maps back by its own ratio, width / (x1 - x0) and
// height / (y1 - y0).
internal sealed record WindowsZoomGeometry(
    Rectangle Native,
    double[] Region,
    int Width,
    int Height);

internal static class WindowsDesktopZoom
{
    internal const double MinSide = 8;
    internal const double MaxScale = 4;

    internal static WindowsZoomGeometry Geometry(
        IReadOnlyList<double> region,
        int nativeWidth,
        int nativeHeight)
    {
        var frame = WindowsDesktopImageGeometry.DeliveredSize(
            nativeWidth,
            nativeHeight);
        if (region.Count != 4)
        {
            throw new ArgumentException("zoom region must be [x0, y0, x1, y1]");
        }
        double x0 = region[0], y0 = region[1], x1 = region[2], y1 = region[3];
        if (!(0 <= x0 && x0 < x1 && x1 <= frame.Width &&
              0 <= y0 && y0 < y1 && y1 <= frame.Height))
        {
            throw new ArgumentException(
                $"zoom region must lie inside the {frame.Width}x{frame.Height} " +
                "frame with x0 < x1 and y0 < y1");
        }
        if (x1 - x0 < MinSide || y1 - y0 < MinSide)
        {
            throw new ArgumentException(
                $"zoom region sides must be at least {MinSide} frame pixels");
        }
        var perX = (double)nativeWidth / frame.Width;
        var perY = (double)nativeHeight / frame.Height;
        var left = Math.Max(0, (int)Math.Floor(x0 * perX + 1e-9));
        var top = Math.Max(0, (int)Math.Floor(y0 * perY + 1e-9));
        var right = Math.Min(nativeWidth, (int)Math.Ceiling(x1 * perX - 1e-9));
        var bottom = Math.Min(nativeHeight, (int)Math.Ceiling(y1 * perY - 1e-9));
        var cropWidth = right - left;
        var cropHeight = bottom - top;
        var enlarge = Math.Min(
            MaxScale,
            Math.Min(
                (double)WindowsDesktopImageGeometry.MaxDeliveredWidth / cropWidth,
                (double)WindowsDesktopImageGeometry.MaxDeliveredHeight / cropHeight));
        return new WindowsZoomGeometry(
            new Rectangle(left, top, cropWidth, cropHeight),
            new[] { left / perX, top / perY, right / perX, bottom / perY },
            Math.Max(1, (int)Math.Round(cropWidth * enlarge, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(cropHeight * enlarge, MidpointRounding.AwayFromZero)));
    }

    /// The windows over a rectangle in virtual-desktop coordinates, in
    /// stacking order, as the coordinate-relevant facts of the frame id.
    internal static IReadOnlyList<WindowsDesktopLayoutWindow> WindowsOver(
        WindowsDesktopLayoutSnapshot layout,
        Rectangle rect)
    {
        return layout.Windows
            .Where(window => new Rectangle(
                    window.X,
                    window.Y,
                    window.Width,
                    window.Height).IntersectsWith(rect))
            .Select(window => window with { ZOrder = 0 })
            .ToArray();
    }

    /// Whether the windows over rect, and their stacking, are those of the
    /// published layout of the frame the region came from.
    internal static bool RegionStillValid(
        WindowsDesktopLayoutSnapshot? published,
        WindowsDesktopLayoutSnapshot current,
        Rectangle rect)
    {
        if (published is null ||
            published.DesktopX != current.DesktopX ||
            published.DesktopY != current.DesktopY ||
            published.DesktopWidth != current.DesktopWidth ||
            published.DesktopHeight != current.DesktopHeight)
        {
            return false;
        }
        return WindowsOver(published, rect).SequenceEqual(WindowsOver(current, rect));
    }
}
