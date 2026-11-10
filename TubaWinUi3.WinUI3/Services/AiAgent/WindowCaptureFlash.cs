using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>
/// Blinks a crisp red rectangle around the window an AX/GUI capture just read.
/// The surface covers the whole virtual screen and exists for the whole control session, so a
/// capture never sees a new/moved top-level window (HCU rejects those as layout changes).
/// It is click-through and excluded from screen capture.
/// </summary>
internal sealed class WindowCaptureFlash : IDisposable
{
    internal const string WindowTitle = "Tuba AI capture flash";
    private const int DurationMilliseconds = 700;
    private const int BlinkOn = 120;
    private const int BlinkPeriod = 210;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private LayeredOverlaySurface? _surface;
    private int _originX, _originY;
    private int[] _border = [];
    private int[] _drawn = [];
    private int _thickness;
    private long _started;
    private bool _active;
    private bool? _on;

    /// <summary>Create the transparent full-screen surface before the first capture of a session.</summary>
    internal void Prepare()
    {
        if (_surface is not null) return;
        _originX = GetSystemMetrics(SmXVirtualScreen);
        _originY = GetSystemMetrics(SmYVirtualScreen);
        var width = Math.Max(1, GetSystemMetrics(SmCxVirtualScreen));
        var height = Math.Max(1, GetSystemMetrics(SmCyVirtualScreen));
        _surface = new LayeredOverlaySurface(WindowTitle, width, height);
        _surface.Present(_originX, _originY);
    }

    internal void Flash(IntPtr target)
    {
        if (target == IntPtr.Zero || !IsWindowVisible(target) || IsIconic(target)) return;
        if (!TryGetBounds(target, out var rect) || rect.Width < 40 || rect.Height < 40) return;
        Prepare();
        var scale = Math.Max(1, GetDpiForWindow(target) / 96d);
        _thickness = Math.Max(2, (int)Math.Round(3 * scale));
        SetFrame(rect.Left - _originX - _thickness, rect.Top - _originY - _thickness,
            rect.Width + _thickness * 2, rect.Height + _thickness * 2);
        _started = _clock.ElapsedMilliseconds;
        _active = true;
        _on = null;
        Render(0);
    }

    private void SetFrame(int left, int top, int width, int height)
    {
        ClearPixels();
        if (_surface is null) return;
        var surfaceWidth = _surface.Width;
        var surfaceHeight = _surface.Height;
        left = Math.Max(0, Math.Min(left, surfaceWidth - 1));
        top = Math.Max(0, Math.Min(top, surfaceHeight - 1));
        var right = Math.Min(surfaceWidth, left + Math.Max(1, width));
        var bottom = Math.Min(surfaceHeight, top + Math.Max(1, height));
        var thickness = Math.Max(1, _thickness);
        var pixels = new List<int>((right - left + bottom - top) * thickness * 2);
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            if (y < top + thickness || y >= bottom - thickness || x < left + thickness || x >= right - thickness)
                pixels.Add(y * surfaceWidth + x);
        }
        _border = pixels.ToArray();
        _drawn = [];
    }

    internal void Advance()
    {
        if (!_active) return;
        var elapsed = _clock.ElapsedMilliseconds - _started;
        if (elapsed > DurationMilliseconds)
        {
            _active = false;
            Render(DurationMilliseconds + 1);
            return;
        }
        Render((int)elapsed);
    }

    private unsafe void Render(int elapsed)
    {
        if (_surface is null) return;
        var on = _active && elapsed <= DurationMilliseconds && elapsed % BlinkPeriod < BlinkOn;
        if (_on == on) return;
        _on = on;
        var pixels = (byte*)_surface.Bits;
        if (on)
        {
            foreach (var index in _border)
            {
                var offset = index * 4;
                pixels[offset] = 48;       // B
                pixels[offset + 1] = 59;   // G
                pixels[offset + 2] = 255;  // R
                pixels[offset + 3] = 255;  // A
            }
            _drawn = _border;
        }
        else ClearPixels();
        _surface.Present(_originX, _originY);
    }

    private unsafe void ClearPixels()
    {
        if (_surface is null) return;
        if (_drawn.Length > 0)
        {
            var pixels = (byte*)_surface.Bits;
            foreach (var index in _drawn)
            {
                var offset = index * 4;
                pixels[offset] = 0; pixels[offset + 1] = 0; pixels[offset + 2] = 0; pixels[offset + 3] = 0;
            }
            _drawn = [];
        }
        if (_border.Length > 0)
        {
            var pixels = (byte*)_surface.Bits;
            foreach (var index in _border)
            {
                var offset = index * 4;
                pixels[offset] = 0; pixels[offset + 1] = 0; pixels[offset + 2] = 0; pixels[offset + 3] = 0;
            }
            _border = [];
        }
    }

    private static bool TryGetBounds(IntPtr window, out NativeRect rect)
    {
        rect = default;
        try
        {
            if (DwmGetWindowAttribute(window, 9, out rect, Marshal.SizeOf<NativeRect>()) == 0) return true;
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return GetWindowRect(window, out rect);
    }

    public void Dispose()
    {
        _active = false;
        _surface?.Dispose();
        _surface = null;
        _border = [];
        _drawn = [];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect value, int size);
}
