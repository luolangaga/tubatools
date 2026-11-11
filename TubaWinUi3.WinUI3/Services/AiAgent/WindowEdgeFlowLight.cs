using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>
/// A scrolling yellow dashed frame pinned to the window the AI is driving. Four thin strips sit
/// just inside the window's visible bounds; a travelling dash pattern marks which window is under
/// control. Time-based (not stepped), so the scroll speed is independent of the frame rate.
/// </summary>
internal sealed class WindowEdgeFlowLight : IDisposable
{
    internal const string WindowTitle = "Tuba AI computer edge flow";

    private const double ScrollSpeedDip = 55;
    private const double DashPeriodDip = 30;
    private const double DashLengthDip = 17;
    private const double ThicknessDip = 3;
    private const byte BaseAlpha = 18;
    private const byte PeakAlpha = 190;

    private sealed class Strip : IDisposable
    {
        internal LayeredOverlaySurface Surface = null!;
        internal int[] Perimeter = [];
        internal byte[] Depth = [];
        internal int OffsetX, OffsetY;
        public void Dispose() => Surface.Dispose();
    }

    private readonly List<Strip> _strips = [];
    private readonly byte[] _pattern = new byte[256];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IntPtr _window;
    private int _boundsX, _boundsY, _boundsWidth, _boundsHeight;
    private int _thickness;
    private int _period;
    private double _scale = 1;
    private bool _built;
    private bool _visible;

    internal void SetTarget(IntPtr window) => _window = window;

    internal void Advance()
    {
        if (!Refresh()) return;
        var offset = _period > 0 ? (int)(_clock.Elapsed.TotalSeconds * ScrollSpeedDip * _scale) % _period : 0;
        foreach (var strip in _strips)
        {
            WriteStrip(strip, offset);
            strip.Surface.Present(_boundsX + strip.OffsetX, _boundsY + strip.OffsetY);
        }
    }

    private bool Refresh()
    {
        if (_window == IntPtr.Zero || !IsWindow(_window) || !IsWindowVisible(_window) || IsIconic(_window) ||
            !TryGetBounds(_window, out var bounds) || bounds.Width < 80 || bounds.Height < 40)
            return Hide();

        var scale = Math.Max(1, GetDpiForWindow(_window) / 96d);
        var thickness = Math.Max(2, (int)Math.Round(ThicknessDip * scale));
        thickness = Math.Min(thickness, Math.Max(2, Math.Min(bounds.Width, bounds.Height) / 4));
        if (!_built || thickness != _thickness || bounds.Width != _boundsWidth ||
            bounds.Height != _boundsHeight || Math.Abs(scale - _scale) > 0.01)
            Build(bounds, thickness, scale);
        _boundsX = bounds.X;
        _boundsY = bounds.Y;
        _visible = true;
        return true;
    }

    private bool Hide()
    {
        if (_visible)
        {
            _visible = false;
            foreach (var strip in _strips) strip.Surface.Hide();
        }
        return false;
    }

    private void Build(RectInt32 bounds, int thickness, double scale)
    {
        DisposeStrips();
        _built = true;
        _visible = true;
        _boundsX = bounds.X;
        _boundsY = bounds.Y;
        _boundsWidth = bounds.Width;
        _boundsHeight = bounds.Height;
        _thickness = thickness;
        _scale = scale;
        BuildPattern(scale);

        var width = bounds.Width;
        var height = bounds.Height;
        var t = Math.Max(1, Math.Min(thickness, Math.Min(width, height) / 4));
        _thickness = t;
        var inner = Math.Max(1, height - t * 2);

        var top = CreateStrip(0, 0, width, t);
        for (var py = 0; py < t; py++)
        for (var px = 0; px < width; px++)
        {
            var i = py * width + px;
            top.Perimeter[i] = px;
            top.Depth[i] = DepthAlpha(py, t);
        }
        _strips.Add(top);

        var bottom = CreateStrip(0, height - t, width, t);
        for (var py = 0; py < t; py++)
        for (var px = 0; px < width; px++)
        {
            var i = py * width + px;
            bottom.Perimeter[i] = width + inner + width - 1 - px;
            bottom.Depth[i] = DepthAlpha(t - 1 - py, t);
        }
        _strips.Add(bottom);

        var left = CreateStrip(0, t, t, inner);
        for (var py = 0; py < inner; py++)
        for (var px = 0; px < t; px++)
        {
            var i = py * t + px;
            left.Perimeter[i] = 2 * width + inner + inner - 1 - py;
            left.Depth[i] = DepthAlpha(px, t);
        }
        _strips.Add(left);

        var right = CreateStrip(width - t, t, t, inner);
        for (var py = 0; py < inner; py++)
        for (var px = 0; px < t; px++)
        {
            var i = py * t + px;
            right.Perimeter[i] = width + py;
            right.Depth[i] = DepthAlpha(t - 1 - px, t);
        }
        _strips.Add(right);
    }

    private static Strip CreateStrip(int offsetX, int offsetY, int width, int height)
    {
        var strip = new Strip
        {
            OffsetX = offsetX,
            OffsetY = offsetY,
            Surface = new LayeredOverlaySurface(WindowTitle, width, height)
        };
        var count = width * height;
        strip.Perimeter = new int[count];
        strip.Depth = new byte[count];
        return strip;
    }

    private void BuildPattern(double scale)
    {
        _period = Math.Clamp((int)Math.Round(DashPeriodDip * scale), 8, 255);
        var dash = Math.Clamp((int)Math.Round(DashLengthDip * scale), 3, _period);
        for (var i = 0; i < _period; i++)
        {
            var v = i < dash ? i / (double)dash : 1;
            var shape = v >= 1 ? 0 : SmoothStep(0, 0.30, v) * (1 - SmoothStep(0.62, 1, v));
            _pattern[i] = (byte)Math.Clamp((int)Math.Round(BaseAlpha + (PeakAlpha - BaseAlpha) * shape), 0, 255);
        }
    }

    private static byte DepthAlpha(int depth, int thickness) =>
        thickness <= 1 || depth < thickness - 1 ? (byte)255 : (byte)104;

    private unsafe void WriteStrip(Strip strip, int offset)
    {
        var pixels = (byte*)strip.Surface.Bits;
        var perimeter = strip.Perimeter;
        var depth = strip.Depth;
        var pattern = _pattern;
        var period = _period;
        var count = strip.Surface.Width * strip.Surface.Height;
        for (var i = 0; i < count; i++)
        {
            var index = (perimeter[i] - offset) % period;
            if (index < 0) index += period;
            var alpha = pattern[index] * depth[i] / 255;
            var o = i * 4;
            if (alpha == 0)
            {
                pixels[o] = 0; pixels[o + 1] = 0; pixels[o + 2] = 0; pixels[o + 3] = 0;
                continue;
            }
            pixels[o] = (byte)(158 * alpha / 255);
            pixels[o + 1] = (byte)(238 * alpha / 255);
            pixels[o + 2] = (byte)alpha;
            pixels[o + 3] = (byte)alpha;
        }
    }

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static bool TryGetBounds(IntPtr window, out RectInt32 bounds)
    {
        try
        {
            if (DwmGetWindowAttribute(window, 9, out var visible, Marshal.SizeOf<NativeRect>()) == 0)
            {
                bounds = new RectInt32(visible.Left, visible.Top, visible.Right - visible.Left, visible.Bottom - visible.Top);
                return bounds.Width > 0 && bounds.Height > 0;
            }
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        _ = GetWindowRect(window, out var rect);
        bounds = new RectInt32(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private void DisposeStrips()
    {
        foreach (var strip in _strips) strip.Dispose();
        _strips.Clear();
    }

    public void Dispose()
    {
        DisposeStrips();
        _built = false;
        _visible = false;
        _window = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect value, int size);
}
