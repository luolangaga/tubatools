using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal static partial class NativeDesktop
{
    /// Returns an enlarged region of the GUI frame named by frameId. The
    /// zoom is look-only: it emits no input and publishes no frame. It reads
    /// the region only when the windows over it are those of that frame;
    /// otherwise it fails with stale_layout, and the failure envelope
    /// attaches a fresh full-desktop screenshot.
    internal static JsonObject Zoom(JsonObject action, string frameId)
    {
        var region = (action["region"] as JsonArray ?? new JsonArray())
            .Select(node => node?.GetValue<double>() ?? double.NaN)
            .ToArray();
        var desktop = DesktopBounds();
        WindowsZoomGeometry geometry;
        try
        {
            geometry = WindowsDesktopZoom.Geometry(region, desktop.Width, desktop.Height);
        }
        catch (ArgumentException error)
        {
            throw new ProviderFailureException(
                "invalid_zoom_region",
                "precondition",
                error.Message + "; nothing was captured.");
        }
        if (string.IsNullOrEmpty(frameId))
        {
            throw new ProviderFailureException(
                "stale_layout",
                "precondition",
                "Zoom regions are frame pixels, and no GUI frame was named.");
        }
        var screenRect = new Rectangle(
            desktop.X + geometry.Native.X,
            desktop.Y + geometry.Native.Y,
            geometry.Native.Width,
            geometry.Native.Height);
        var before = CurrentLayout();
        WindowsDesktopLayoutSnapshot? published;
        lock (PublishedLayoutLock)
        {
            PublishedLayouts.TryGetValue(frameId, out published);
        }
        if (!string.Equals(before.FrameId, frameId, StringComparison.Ordinal) &&
            !WindowsDesktopZoom.RegionStillValid(published, before, screenRect))
        {
            throw new ProviderFailureException(
                "stale_layout",
                "precondition",
                "The windows over the zoom region changed since the frame its " +
                "coordinates came from.");
        }
        using var crop = new Bitmap(
            geometry.Native.Width,
            geometry.Native.Height,
            PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(crop))
        {
            graphics.CopyFromScreen(screenRect.X, screenRect.Y, 0, 0, screenRect.Size);
        }
        if (!WindowsDesktopZoom.RegionStillValid(before, CurrentLayout(), screenRect))
        {
            throw new ProviderFailureException(
                "stale_layout",
                "precondition",
                "The windows over the zoom region changed during the zoom capture.");
        }
        using var image = new Bitmap(geometry.Width, geometry.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(
                crop,
                new Rectangle(0, 0, geometry.Width, geometry.Height),
                new Rectangle(0, 0, crop.Width, crop.Height),
                GraphicsUnit.Pixel);
        }
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        var regionJson = new JsonArray();
        foreach (var value in geometry.Region)
        {
            regionJson.Add(value);
        }
        return new JsonObject
        {
            ["ok"] = true,
            ["text"] = "GUI zoom; no input was sent.",
            // Not a frame: no frameId and no layout, so never provenance.
            ["snapshot"] = new JsonObject
            {
                ["app"] = new JsonObject { ["name"] = string.Empty, ["pid"] = 0 },
                ["screenshotPngBase64"] = Convert.ToBase64String(stream.ToArray()),
                ["screenshotMimeType"] = "image/png",
                ["screenshotEncoding"] = "png",
                ["coordinateSpace"] = "zoom_image_px",
                ["width"] = geometry.Width,
                ["height"] = geometry.Height,
            },
            ["mutation"] = new JsonObject
            {
                ["input_events_emitted"] = false,
                ["partial_input_possible"] = false,
                ["desktop_state_mutated"] = false,
                ["mutation_unknown"] = false,
            },
            ["gui_result"] = new JsonObject
            {
                ["dispatched"] = 0,
                ["input_events_emitted"] = false,
                ["input_actions_dispatched"] = 0,
                ["window_commands_dispatched"] = 0,
                ["desktop_state_mutated"] = false,
                ["mutation_unknown"] = false,
                ["partial_input_possible"] = false,
                ["effect_unknown"] = false,
                ["zoom"] = new JsonObject
                {
                    ["region"] = regionJson,
                    ["width"] = geometry.Width,
                    ["height"] = geometry.Height,
                },
            },
        };
    }
}
