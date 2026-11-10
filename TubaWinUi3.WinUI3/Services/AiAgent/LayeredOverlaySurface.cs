using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>
/// A topmost layered window backed by a 32-bit premultiplied top-down DIB. Callers draw pixels
/// straight into <see cref="Bits"/> and call <see cref="Present"/>. Decorative surfaces are
/// click-through; content surfaces set <c>clickThrough:false</c> and provide button hit-testing.
/// </summary>
internal sealed class LayeredOverlaySurface : IDisposable
{
    private const int WsExTopmost = 0x00000008;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExLayered = 0x00080000;
    private const int WsPopup = unchecked((int)0x80000000);
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;
    private const uint UlwAlpha = 2;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpHideWindow = 0x0080;
    private const int WmNcHitTest = 0x0084;
    private const int WmMouseMove = 0x0200;
    private const int WmMouseLeave = 0x02A3;
    private const int WmLButtonUp = 0x0202;
    private const uint TmeLeave = 0x00000002;
    private static readonly IntPtr Topmost = new(-1);
    private static readonly Dictionary<IntPtr, LayeredOverlaySurface> InputInstances = new();
    private static readonly SubclassProc InputCallback = OnInput;

    private IntPtr _window;
    private IntPtr _dc;
    private IntPtr _oldBitmap;
    private IntPtr _bitmap;
    private IntPtr _bits;
    private bool _trackingLeave;
    private bool? _clickThrough;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo { public BitmapInfoHeader Header; public uint Color; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)]
    private struct Size { public int Width, Height; public Size(int width, int height) { Width = width; Height = height; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte Operation;
        public byte Flags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventData { public uint Size; public uint Flags; public IntPtr Hwnd; public uint HoverTime; }

    internal LayeredOverlaySurface(string title, int width, int height, bool clickThrough = true, bool captureExcluded = true)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        var exStyle = WsExTopmost | WsExToolWindow | WsExNoActivate | WsExLayered;
        if (clickThrough) exStyle |= WsExTransparent;
        _window = CreateWindowEx(exStyle, "STATIC", title, WsPopup, 0, 0, Width, Height, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (_window == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建桌面覆盖层。");
        if (captureExcluded) OverlayCaptureExclusion.Apply(_window);
        _dc = CreateCompatibleDC(IntPtr.Zero);
        if (_dc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建覆盖层绘图缓冲区。");
        RecreateBitmap();
        InputInstances[_window] = this;
        _ = SetWindowSubclass(_window, InputCallback, _window, IntPtr.Zero);
    }

    internal int Width { get; private set; }
    internal int Height { get; private set; }
    internal IntPtr Bits => _bits;
    internal Func<int, int, bool>? IsInteractive { get; set; }
    internal Action<int, int>? Clicked { get; set; }
    internal Action<int, int>? Moved { get; set; }
    internal Action? Left { get; set; }

    /// <summary>
    /// Toggles WS_EX_TRANSPARENT. On a layered window this does not affect painting, but it
    /// makes WindowFromPoint (and the computer-use pointer preflight) skip the surface.
    /// </summary>
    internal void SetClickThrough(bool transparent)
    {
        if (_window == IntPtr.Zero || _clickThrough == transparent) return;
        var style = GetWindowLongPtr(_window, -20).ToInt64();
        style = transparent ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLongPtr(_window, -20, new IntPtr(style));
        _clickThrough = transparent;
    }

    internal void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height && _bitmap != IntPtr.Zero) return;
        Width = width;
        Height = height;
        RecreateBitmap();
    }

    private void RecreateBitmap()
    {
        if (_dc != IntPtr.Zero && _oldBitmap != IntPtr.Zero)
        {
            _ = SelectObject(_dc, _oldBitmap);
            _oldBitmap = IntPtr.Zero;
        }
        if (_bitmap != IntPtr.Zero) { _ = DeleteObject(_bitmap); _bitmap = IntPtr.Zero; _bits = IntPtr.Zero; }
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = Width, Height = -Height,
                Planes = 1, BitCount = 32, Compression = BiRgb, SizeImage = (uint)(Width * Height * 4)
            }
        };
        _bitmap = CreateDIBSection(IntPtr.Zero, ref info, DibRgbColors, out _bits, IntPtr.Zero, 0);
        if (_bitmap == IntPtr.Zero || _bits == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建覆盖层图像。");
        _oldBitmap = SelectObject(_dc, _bitmap);
    }

    internal void Present(int x, int y)
    {
        if (_window == IntPtr.Zero || _bitmap == IntPtr.Zero) return;
        var destination = new Point(x, y);
        var size = new Size(Width, Height);
        var source = new Point(0, 0);
        var blend = new BlendFunction { Operation = 0, Flags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        if (!UpdateLayeredWindow(_window, IntPtr.Zero, ref destination, ref size, _dc, ref source, 0, ref blend, UlwAlpha))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法更新桌面覆盖层。");
        _ = SetWindowPos(_window, Topmost, x, y, Width, Height, SwpNoActivate | SwpNoOwnerZOrder | SwpShowWindow);
    }

    internal void Hide()
    {
        if (_window == IntPtr.Zero) return;
        _ = SetWindowPos(_window, IntPtr.Zero, 0, 0, 0, 0, SwpNoActivate | SwpNoOwnerZOrder | SwpHideWindow);
    }

    private static IntPtr OnInput(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, IntPtr id, IntPtr data)
    {
        if (InputInstances.TryGetValue(hwnd, out var surface))
        {
            switch (message)
            {
                case WmNcHitTest:
                    {
                        var point = ReadScreenPoint(lParam);
                        if (ScreenToClient(hwnd, ref point) && surface.IsInteractive?.Invoke(point.X, point.Y) == true)
                            return new IntPtr(1); // HTCLIENT
                        return new IntPtr(-1);    // HTTRANSPARENT
                    }
                case WmMouseMove:
                    {
                        var point = ReadClientPoint(lParam);
                        surface.Moved?.Invoke(point.X, point.Y);
                        if (!surface._trackingLeave)
                        {
                            var track = new TrackMouseEventData { Size = (uint)Marshal.SizeOf<TrackMouseEventData>(), Flags = TmeLeave, Hwnd = hwnd };
                            _ = TrackMouseEvent(ref track);
                            surface._trackingLeave = true;
                        }
                        break;
                    }
                case WmMouseLeave:
                    surface._trackingLeave = false;
                    surface.Left?.Invoke();
                    break;
                case WmLButtonUp:
                    {
                        var point = ReadClientPoint(lParam);
                        if (surface.IsInteractive?.Invoke(point.X, point.Y) == true)
                            surface.Clicked?.Invoke(point.X, point.Y);
                        break;
                    }
            }
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private static NativePoint ReadScreenPoint(IntPtr lParam) => new()
    {
        X = unchecked((short)(long)lParam),
        Y = unchecked((short)((long)lParam >> 16))
    };

    private static NativePoint ReadClientPoint(IntPtr lParam) => new()
    {
        X = unchecked((short)(long)lParam),
        Y = unchecked((short)((long)lParam >> 16))
    };

    public void Dispose()
    {
        if (_window != IntPtr.Zero) { InputInstances.Remove(_window); _ = DestroyWindow(_window); _window = IntPtr.Zero; }
        if (_dc != IntPtr.Zero && _oldBitmap != IntPtr.Zero) _ = SelectObject(_dc, _oldBitmap);
        _oldBitmap = IntPtr.Zero;
        if (_bitmap != IntPtr.Zero) { _ = DeleteObject(_bitmap); _bitmap = IntPtr.Zero; }
        _bits = IntPtr.Zero;
        if (_dc != IntPtr.Zero) { _ = DeleteDC(_dc); _dc = IntPtr.Zero; }
    }

    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, IntPtr id, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? moduleName);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr destinationDc, ref Point destination, ref Size size, IntPtr sourceDc, ref Point source, uint colorKey, ref BlendFunction blend, uint flags);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TrackMouseEventData data);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index) => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new(GetWindowLong32(hwnd, index));
    private static void SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
    {
        if (IntPtr.Size == 8) _ = SetWindowLongPtr64(hwnd, index, value);
        else _ = SetWindowLong32(hwnd, index, value.ToInt32());
    }
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, IntPtr id, IntPtr data);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}

/// <summary>Keeps host overlays out of screen captures, so AX/GUI screenshots show only the desktop.</summary>
internal static class OverlayCaptureExclusion
{
    internal const string AllowCaptureVariable = "TUBA_OVERLAY_ALLOW_CAPTURE";
    private const uint WdaExcludeFromCapture = 0x11;
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    internal static void Apply(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        // Diagnostic/recording escape hatch: keep the overlays visible in captures.
        if (Environment.GetEnvironmentVariable(AllowCaptureVariable) == "1") return;
        _ = SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
    }
}
