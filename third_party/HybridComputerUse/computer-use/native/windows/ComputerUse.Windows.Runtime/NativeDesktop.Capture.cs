using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ComputerUse.Windows.Runtime;

internal static partial class NativeDesktop
{
    internal static bool PixelsIdentical(ScreenFrame first, ScreenFrame second)
    {
        return first.OriginX == second.OriginX &&
            first.OriginY == second.OriginY &&
            first.NativeWidth == second.NativeWidth &&
            first.NativeHeight == second.NativeHeight &&
            first.FrameId == second.FrameId &&
            first.Pixels.Length > 0 &&
            first.Pixels.AsSpan().SequenceEqual(second.Pixels);
    }

    private static ScreenFrame CaptureFrame(
        Rectangle bounds,
        string frameId,
        bool includePixels)
    {
        using var nativeBitmap = new Bitmap(
            bounds.Width,
            bounds.Height,
            PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(nativeBitmap))
        {
            graphics.CopyFromScreen(
                bounds.X,
                bounds.Y,
                0,
                0,
                bounds.Size);
        }
        return EncodeFrame(nativeBitmap, bounds, frameId, includePixels);
    }

    private static ScreenFrame EncodeFrame(
        Bitmap nativeBitmap,
        Rectangle bounds,
        string frameId,
        bool includePixels)
    {
        var deliveredSize = WindowsDesktopImageGeometry.DeliveredSize(
            bounds.Width,
            bounds.Height);
        using var stream = new MemoryStream();
        if (deliveredSize.Width == bounds.Width &&
            deliveredSize.Height == bounds.Height)
        {
            nativeBitmap.Save(stream, ImageFormat.Png);
        }
        else
        {
            using var deliveredBitmap = new Bitmap(
                deliveredSize.Width,
                deliveredSize.Height,
                PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(deliveredBitmap))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.DrawImage(
                    nativeBitmap,
                    new Rectangle(Point.Empty, deliveredSize),
                    new Rectangle(Point.Empty, bounds.Size),
                    GraphicsUnit.Pixel);
            }
            deliveredBitmap.Save(stream, ImageFormat.Png);
        }
        return new ScreenFrame(
            bounds.X,
            bounds.Y,
            deliveredSize.Width,
            deliveredSize.Height,
            bounds.Width,
            bounds.Height,
            frameId,
            Convert.ToBase64String(stream.ToArray()))
        {
            Pixels = includePixels ? PixelBytes(nativeBitmap) : Array.Empty<byte>(),
        };
    }

    private static byte[] PixelBytes(Bitmap bitmap)
    {
        var data = bitmap.LockBits(
            new Rectangle(Point.Empty, bitmap.Size),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = checked(bitmap.Width * 4);
            var pixels = new byte[checked(rowBytes * bitmap.Height)];
            for (var row = 0; row < bitmap.Height; row++)
            {
                Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * rowBytes, rowBytes);
            }
            return pixels;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    internal static WindowsDesktopLayoutSnapshot CurrentLayout()
    {
        var desktopBounds = DesktopBounds();
        var foreground = GetForegroundWindow();
        var windows = new List<WindowsDesktopLayoutWindow>();
        var zOrder = 0;
        foreach (var window in ListWindows())
        {
            if (!WindowsDesktopLayoutIdentityPolicy.IncludesWindow(
                    window.ClassName,
                    window.OwnerHandle,
                    window.Style,
                    window.ExtendedStyle) ||
                !desktopBounds.IntersectsWith(window.Bounds))
            {
                continue;
            }
            windows.Add(new WindowsDesktopLayoutWindow(
                zOrder,
                window.Handle.ToInt64(),
                window.OwnerHandle.ToInt64(),
                window.ProcessId,
                window.ClassName,
                window.Bounds.X,
                window.Bounds.Y,
                window.Bounds.Width,
                window.Bounds.Height,
                window.Style,
                window.ExtendedStyle));
            zOrder++;
        }
        return WindowsDesktopLayoutDiagnostics.Create(
            desktopBounds.X,
            desktopBounds.Y,
            desktopBounds.Width,
            desktopBounds.Height,
            foreground.ToInt64(),
            windows);
    }

    internal static void LogStaleLayout(
        string requestedFrameId,
        WindowsDesktopLayoutSnapshot current)
    {
        WindowsDesktopLayoutSnapshot? requested;
        lock (PublishedLayoutLock)
        {
            PublishedLayouts.TryGetValue(requestedFrameId, out requested);
        }
        Console.Error.WriteLine(
            WindowsDesktopLayoutDiagnostics.Difference(
                requestedFrameId,
                requested,
                current).ToJsonString());
    }

    private static void RememberPublishedLayout(
        WindowsDesktopLayoutSnapshot layout)
    {
        lock (PublishedLayoutLock)
        {
            if (PublishedLayouts.ContainsKey(layout.FrameId))
            {
                return;
            }
            PublishedLayouts[layout.FrameId] = layout;
            PublishedLayoutOrder.Enqueue(layout.FrameId);
            while (PublishedLayoutOrder.Count > PublishedLayoutHistoryLimit)
            {
                var expired = PublishedLayoutOrder.Dequeue();
                PublishedLayouts.Remove(expired);
            }
        }
    }
}
