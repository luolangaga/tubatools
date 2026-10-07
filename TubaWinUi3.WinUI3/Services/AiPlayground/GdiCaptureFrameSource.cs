using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>
/// 屏幕 / 窗口捕获帧源（GDI，无需额外依赖）。
/// - 全屏：BitBlt 虚拟桌面（支持多屏）。
/// - 窗口：优先 PrintWindow(PW_RENDERFULLCONTENT)（可捕获 DWM 合成窗口），失败回退 BitBlt。
/// 只读捕获，不操作目标窗口。
/// </summary>
public sealed class GdiCaptureFrameSource : IFrameSource
{
    public enum CaptureMode { Screen, Window }

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int SRCCOPY = 0x00CC0020;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int w, int h, IntPtr hdcSrc, int xSrc, int ySrc, int rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private readonly CaptureMode _mode;
    private readonly IntPtr _hwnd;
    private bool _disposed;

    public GdiCaptureFrameSource(CaptureMode mode, IntPtr hwnd = default)
    {
        _mode = mode;
        _hwnd = hwnd;
    }

    public string DisplayName => _mode == CaptureMode.Screen ? "Screen" : "Window";

    public (int Width, int Height) Size
    {
        get
        {
            var (w, h) = TargetSize();
            return (w, h);
        }
    }

    private (int W, int H) TargetSize()
    {
        if (_mode == CaptureMode.Window && _hwnd != IntPtr.Zero && GetWindowRect(_hwnd, out var r))
        {
            var w = Math.Max(1, r.Right - r.Left);
            var h = Math.Max(1, r.Bottom - r.Top);
            return (w, h);
        }
        return (Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN)),
                Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN)));
    }

    public bool TryGrab(out FrameBuffer frame)
    {
        frame = null!;
        if (_disposed) return false;
        var (w, h) = TargetSize();
        if (w < 2 || h < 2) return false;

        IntPtr srcDc = IntPtr.Zero, memDc = IntPtr.Zero, bmp = IntPtr.Zero, oldBmp = IntPtr.Zero;
        try
        {
            srcDc = GetDC(IntPtr.Zero);
            memDc = CreateCompatibleDC(srcDc);
            bmp = CreateCompatibleBitmap(srcDc, w, h);
            oldBmp = SelectObject(memDc, bmp);

            bool captured = false;
            if (_mode == CaptureMode.Window && _hwnd != IntPtr.Zero)
            {
                captured = PrintWindow(_hwnd, memDc, PW_RENDERFULLCONTENT);
                if (!captured)
                {
                    // 回退：直接 BitBlt 屏幕对应区域（窗口被遮挡时可能为黑/旧内容）
                    GetWindowRect(_hwnd, out var r);
                    captured = BitBlt(memDc, 0, 0, w, h, srcDc, r.Left, r.Top, SRCCOPY);
                }
            }
            else
            {
                int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
                int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
                captured = BitBlt(memDc, 0, 0, w, h, srcDc, x, y, SRCCOPY);
            }
            if (!captured) return false;

            var bgra = new byte[w * h * 4];
            var bmpInfo = new BitmapInfoHeader
            {
                biSize = Marshal.SizeOf<BitmapInfoHeader>(),
                biWidth = w,
                biHeight = -h,          // 负 = top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            };
            var bitsHandle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
            try
            {
                var headerPtr = Marshal.AllocHGlobal(Marshal.SizeOf<BitmapInfoHeader>());
                try
                {
                    Marshal.StructureToPtr(bmpInfo, headerPtr, false);
                    if (GetDIBits(memDc, bmp, 0, (uint)h, bitsHandle.AddrOfPinnedObject(), headerPtr, 0) == 0)
                        return false;
                }
                finally { Marshal.FreeHGlobal(headerPtr); }
            }
            finally { bitsHandle.Free(); }

            frame = new FrameBuffer { Bgra = bgra, Width = w, Height = h, Stride = w * 4 };
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (oldBmp != IntPtr.Zero && memDc != IntPtr.Zero) SelectObject(memDc, oldBmp);
            if (bmp != IntPtr.Zero) DeleteObject(bmp);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            if (srcDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, srcDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, IntPtr lpvBits, IntPtr lpbmi, uint usage);

    public void Dispose() => _disposed = true;
}
