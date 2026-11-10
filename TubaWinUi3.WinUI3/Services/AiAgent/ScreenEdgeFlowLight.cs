using System.Diagnostics;
using Windows.Graphics;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>
/// A calm, continuous aurora around the active monitor edge: four thin layered strips with a
/// soft inward falloff and two slowly travelling brightness waves. Time-based (not stepped),
/// so motion stays smooth at any refresh rate.
/// </summary>
internal sealed class ScreenEdgeFlowLight : IDisposable
{
    internal const string WindowTitle = "Tuba AI computer edge flow";

    private const double WaveSpeed = 0.9;
    private const double ColorSpeed = 0.55;
    private const double BreathSpeed = 0.85;

    private sealed class Strip : IDisposable
    {
        internal LayeredOverlaySurface Surface = null!;
        internal int X, Y;
        internal byte[] Angle = [];
        internal byte[] Depth = [];
        public void Dispose() => Surface.Dispose();
    }

    private readonly List<Strip> _strips = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly byte[] _depthAlpha = new byte[256];
    private RectInt32 _bounds;
    private int _thickness;
    private bool _built;

    private void SetTarget(RectInt32 bounds, double scale)
    {
        var thickness = Math.Max(10, (int)Math.Round(26 * scale));
        if (_built && bounds.X == _bounds.X && bounds.Y == _bounds.Y && bounds.Width == _bounds.Width &&
            bounds.Height == _bounds.Height && thickness == _thickness) return;

        DisposeStrips();
        _bounds = bounds;
        _thickness = thickness;
        thickness = Math.Min(thickness, Math.Min(bounds.Width, bounds.Height) / 3);
        _thickness = Math.Max(4, thickness);
        BuildDepthLut();
        BuildStrips();
        _built = true;
    }

    internal void SetTarget(IntPtr anchor)
    {
        if (anchor == IntPtr.Zero) return;
        var monitor = MonitorFromWindow(anchor, 2);
        if (monitor == IntPtr.Zero) return;
        var info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;
        var bounds = new RectInt32(info.Monitor.Left, info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
        var scale = Math.Max(1, GetDpiForWindow(anchor) / 96d);
        SetTarget(bounds, scale);
    }

    private void BuildDepthLut()
    {
        for (var d = 0; d < 256; d++)
        {
            var u = d / 255.0;
            var edge = 1 - SmoothStep(0, 1, u);
            var alpha = 22 + 196 * Math.Pow(edge, 1.85);
            if (u < 0.06) alpha += 26;
            _depthAlpha[d] = (byte)Math.Clamp(alpha, 0, 255);
        }
    }

    private void BuildStrips()
    {
        var width = _bounds.Width;
        var height = _bounds.Height;
        var thickness = _thickness;
        AddStrip(_bounds.X, _bounds.Y, width, thickness, width, height);
        AddStrip(_bounds.X, _bounds.Y + height - thickness, width, thickness, width, height);
        AddStrip(_bounds.X, _bounds.Y + thickness, thickness, Math.Max(1, height - thickness * 2), width, height);
        AddStrip(_bounds.X + width - thickness, _bounds.Y + thickness, thickness, Math.Max(1, height - thickness * 2), width, height);
    }

    private void AddStrip(int x, int y, int width, int height, int monitorWidth, int monitorHeight)
    {
        var strip = new Strip { X = x, Y = y, Surface = new LayeredOverlaySurface(WindowTitle, width, height) };
        var count = width * height;
        strip.Angle = new byte[count];
        strip.Depth = new byte[count];
        var centerX = monitorWidth / 2.0;
        var centerY = monitorHeight / 2.0;
        var limit = Math.Max(1, _thickness - 1);
        var index = 0;
        for (var py = 0; py < height; py++)
        for (var px = 0; px < width; px++)
        {
            var globalX = x - _bounds.X + px;
            var globalY = y - _bounds.Y + py;
            var angle = Math.Atan2(globalY + 0.5 - centerY, globalX + 0.5 - centerX) / (Math.PI * 2) + 0.5;
            strip.Angle[index] = (byte)Math.Clamp((int)(angle * 255), 0, 255);
            var depth = Math.Min(Math.Min(globalX, monitorWidth - 1 - globalX), Math.Min(globalY, monitorHeight - 1 - globalY));
            strip.Depth[index] = (byte)Math.Clamp((int)(depth * 255.0 / limit), 0, 255);
            index++;
        }
        _strips.Add(strip);
    }

    internal void Advance()
    {
        if (_strips.Count == 0) return;
        var t = _clock.Elapsed.TotalSeconds;
        var pulse = (0.70 + 0.30 * Math.Sin(t * BreathSpeed)) * 255;
        var wave = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var angle = i / 256.0 * Math.PI * 2;
            var value = 0.52 + 0.30 * Math.Sin(angle * 2 - t * WaveSpeed) + 0.18 * Math.Sin(angle * 3 + t * ColorSpeed);
            wave[i] = (byte)Math.Clamp((int)(value * 255), 0, 255);
        }

        foreach (var strip in _strips)
        {
            WriteStrip(strip, wave, (byte)Math.Clamp((int)pulse, 0, 255));
            strip.Surface.Present(strip.X, strip.Y);
        }
    }

    private unsafe void WriteStrip(Strip strip, byte[] wave, byte pulse)
    {
        var pixels = (byte*)strip.Surface.Bits;
        var angle = strip.Angle;
        var depth = strip.Depth;
        var count = strip.Surface.Width * strip.Surface.Height;
        for (var i = 0; i < count; i++)
        {
            var alpha = _depthAlpha[depth[i]] * wave[angle[i]] / 255;
            alpha = alpha * pulse / 255;
            var offset = i * 4;
            if (alpha == 0)
            {
                pixels[offset] = 0; pixels[offset + 1] = 0; pixels[offset + 2] = 0; pixels[offset + 3] = 0;
                continue;
            }
            pixels[offset] = (byte)(255 * alpha / 255);
            pixels[offset + 1] = (byte)(198 * alpha / 255);
            pixels[offset + 2] = (byte)(96 * alpha / 255);
            pixels[offset + 3] = (byte)alpha;
        }
    }

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private void DisposeStrips()
    {
        foreach (var strip in _strips) strip.Surface.Dispose();
        _strips.Clear();
    }

    public void Dispose()
    {
        DisposeStrips();
        _built = false;
    }

    private struct Rect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
}
