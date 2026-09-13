using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using TubaWinUi3.Models;

namespace TubaWinUi3.Pages;

/// <summary>
/// Win32 layered window that renders hardware monitoring widgets on top of a game window.
/// Uses GDI for rendering — compatible with fullscreen exclusive games.
/// Pattern follows AntiMotionSicknessOverlay.
/// </summary>
public sealed class GameOverlayWindow : IDisposable
{
    private static GameOverlayWindow? _instance;

    private IntPtr _hwnd;
    private int _width, _height;
    private bool _disposed;
    private Timer? _topmostTimer;
    private IntPtr _targetHwnd;
    private bool _desktopMode;
    private readonly List<WidgetInstance> _widgets = new();
    private float _bgOpacity = 0.7f;
    private OverlayPosition _position = OverlayPosition.TopLeft;
    // OLED 防烧屏：开启后悬浮窗沿小圆轨迹缓慢漂移，避免长时间静止灼烧像素
    private bool _oledEnabled;
    private Timer? _oledTimer;
    private float _oledAngle;
    private int _driftX, _driftY;
    // Fluent 卡片背景（圆角 + 1px 边框），预渲染一次；脏区域擦除时从这里拷回像素
    private SKBitmap? _bgCard;
    // Cached overlay surface (DIB + memory DC), recreated only when the size changes
    private IntPtr _surfaceDib, _surfaceDc, _surfaceOld, _surfaceBits;
    private int _surfaceW, _surfaceH;
    // Whether the surface has been filled with the background panel yet — filled once
    // on (re)creation; later frames only erase dirty regions with the same pixel.
    private bool _surfaceInited;
    // Last SetWindowPos position — skip the call when nothing moved
    private int _posX, _posY, _posW, _posH;
    // Chart history buffers
    private readonly ConcurrentDictionary<string, CircularBuffer> _chartData = new();

    #region Win32 P/Invoke

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_SHOWNOACTIVATE = 4;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, int cbAttribute);

    private static void DwmSetWindowAttr(IntPtr hwnd, uint attr, int val)
    {
        DwmSetWindowAttribute(hwnd, attr, ref val, sizeof(int));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public POINT pt;
    }

    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint ULW_ALPHA = 0x00000002;
    private const int AC_SRC_ALPHA = 0x01;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint DWMWA_EXCLUDED_FROM_PEEK = 12;

    #endregion

    public static bool IsRunning => _instance != null;
    public static GameOverlayWindow? Instance => _instance;

    public enum OverlayPosition
    {
        TopLeft, TopCenter, TopRight,
        MiddleLeft, Center, MiddleRight,
        BottomLeft, BottomCenter, BottomRight
    }

    public sealed class WidgetInstance
    {
        public OverlayWidgetType Type;
        public int X, Y, Width, Height;
        public int FontSize = 16;
        public string Prefix = "";
        public bool ShowPrefix = true;
        public int Layer;
        public string CurrentText = "--";
        public bool IsChart;
        // Dirty flag — widget content/geometry changed, so it needs re-rendering and
        // recomposition. Drives incremental rendering: an overlay with no dirty widget
        // skips the whole frame (idle ≈ 0 CPU cost).
        public bool Dirty;
        // Custom content
        public string CustomText = "";
        public string ImagePath = "";
        public uint ColorArgb = 0xFF00A0FF;
        public uint TextColorArgb = 0xFFFFFFFF;
        // Cached image bitmap
        public SKBitmap? CachedImage;
        // Per-widget cached render resources — recreated only when size/font changes,
        // so a frame no longer allocates a DIB + SKBitmap/SKCanvas per widget.
        public IntPtr Dib, DibDC, DibOld, DibBits;
        public int DibW, DibH;
        public SKBitmap? RenderBmp;
        public SKCanvas? RenderCanvas;
        public SKFont? RenderFont;
        public int RenderW, RenderH, RenderFs;
        // Reusable chart render resources — avoids per-frame SKPaint/SKPath/SKPoint[]
        // /SKShader allocation (GC churn) while a chart keeps updating every tick.
        public SKPaint[]? ChartPaints;
        public SKPoint[]? ChartPoints;
        public SKPath? AreaPath;
        public SKPath? GlowPath;
        public SKPath? LinePath;
        public SKShader? AreaShader;
        public int AreaShaderCy, AreaShaderCh;
        public uint AreaShaderColor;
    }

    public sealed class CircularBuffer
    {
        private readonly float[] _data;
        private int _index, _count;
        public int Count => _count;
        public int Capacity => _data.Length;

        public CircularBuffer(int capacity = 60) { _data = new float[capacity]; }

        public void Add(float value)
        {
            _data[_index] = value;
            _index = (_index + 1) % _data.Length;
            if (_count < _data.Length) _count++;
        }

        public float Get(int index) => index < _count ? _data[(_index - _count + index + _data.Length) % _data.Length] : 0;

        public (float min, float max) GetRange()
        {
            if (_count == 0) return (0, 1);
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < _count; i++)
            {
                var v = Get(i);
                if (v < min) min = v;
                if (v > max) max = v;
            }
            return (min, max == min ? min + 1 : max);
        }
    }

    private GameOverlayWindow() { }

    public static GameOverlayWindow ShowOverlay(IntPtr targetHwnd, List<WidgetInstance> widgets,
        float bgOpacity, OverlayPosition position, int width, int height, bool desktopMode = false,
        bool oledProtection = false)
    {
        _instance?.Dispose();

        // Ensure valid dimensions
        width = Math.Clamp(width, 100, 3840);
        height = Math.Clamp(height, 50, 2160);

        var overlay = new GameOverlayWindow
        {
            _targetHwnd = targetHwnd,
            _desktopMode = desktopMode,
            _bgOpacity = Math.Clamp(bgOpacity, 0.1f, 1f), // at least 10% visible
            _position = position,
            _width = width,
            _height = height
        };
        overlay._widgets.AddRange(widgets);
        // First frame renders everything
        foreach (var w in overlay._widgets) w.Dirty = true;
        overlay.CreateOverlayWindow();
        overlay.StartTopmostTimer();
        overlay.SetOledProtection(oledProtection);
        _instance = overlay;

        System.Diagnostics.Debug.WriteLine($"[GameOverlay] ShowOverlay: {width}x{height}, opacity={overlay._bgOpacity}, hwnd={overlay._hwnd}");
        return overlay;
    }

    public static void CloseOverlay()
    {
        _instance?.Dispose();
        _instance = null;
    }

    /// <summary>目标窗口是否仍然有效（供外部服务在绑定前校验信号里的 HWND）。</summary>
    public static bool IsWindowSafe(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindow(hwnd);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static WndProcDelegate? _wndProcDelegate; // prevent GC

    private void CreateOverlayWindow()
    {
        _wndProcDelegate = WndProc;

        // Use unique class name to avoid stale registration from crashed runs
        string className = "Tuba_GameOvl_" + Guid.NewGuid().ToString("N")[..8];
        var hInst = Marshal.GetHINSTANCE(typeof(GameOverlayWindow).Module);

        var wndClass = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            lpszClassName = className,
            hInstance = hInst
        };
        var atom = RegisterClassW(ref wndClass);
        if (atom == 0)
        {
            int err = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"[GameOverlay] RegisterClassW FAILED: {err}");
            return;
        }

        // Position: center of screen by default, or relative to target window.
        // In desktop mode the overlay is placed at the configured position over the whole screen.
        int x, y;
        int screenW = GetSystemMetrics(0);
        int screenH = GetSystemMetrics(1);
        if (_desktopMode)
        {
            var (ox, oy) = CalculateOffset(screenW, screenH);
            x = ox;
            y = oy;
        }
        else if (_targetHwnd != IntPtr.Zero && IsWindow(_targetHwnd) && GetWindowRect(_targetHwnd, out var rc))
        {
            var (ox, oy) = CalculateOffset(rc.Right - rc.Left, rc.Bottom - rc.Top);
            x = rc.Left + ox;
            y = rc.Top + oy;
        }
        else
        {
            x = (screenW - _width) / 2;
            y = (screenH - _height) / 2;
        }

        _hwnd = CreateWindowExW(
            WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            className, "", WS_POPUP | WS_VISIBLE,
            x, y, _width, _height,
            IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"[GameOverlay] CreateWindowExW FAILED: {err}");
            return;
        }

        try { DwmSetWindowAttr(_hwnd, DWMWA_EXCLUDED_FROM_PEEK, 1); } catch { }

        // Show window first, then render content into it
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, _width, _height, SWP_SHOWWINDOW);
        RenderFrame();

        System.Diagnostics.Debug.WriteLine($"[GameOverlay] Window created: hwnd={_hwnd}, size={_width}x{_height}, pos=({x},{y})");
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                return (IntPtr)HTTRANSPARENT;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void StartTopmostTimer()
    {
        // Re-assert topmost Z-order for fullscreen games. UpdateData already re-asserts
        // HWND_TOPMOST on every poll tick, so this timer only matters while polling is
        // paused — 1s is enough; 200ms would poke DWM 5x/s for the whole overlay lifetime.
        _topmostTimer = new Timer(_ =>
        {
            if (_hwnd != IntPtr.Zero && IsWindow(_hwnd))
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }, null, 1000, 1000);
    }

    public void UpdateData(MonitorSample sample)
    {
        foreach (var w in _widgets)
        {
            if (w.IsChart)
            {
                var (chartKey, value) = GetChartValue(w.Type, sample);
                if (chartKey != null)
                {
                    var buf = _chartData.GetOrAdd(chartKey, _ => new CircularBuffer(60));
                    if (value >= 0)
                    {
                        buf.Add(value);
                        w.Dirty = true; // new point appended -> redraw this chart
                    }
                }
            }
            else if (w.Type is OverlayWidgetType.CustomText or OverlayWidgetType.CustomImage or OverlayWidgetType.ColorBlock)
            {
                // Static content — never dirty after the first render
            }
            else
            {
                var value = FormatWidgetValue(w.Type, sample);
                var text = w.ShowPrefix && !string.IsNullOrEmpty(w.Prefix)
                    ? $"{w.Prefix}{value}"
                    : value;
                // Mark dirty only when the text actually changed — an identical value
                // lets RenderFrame skip the whole frame (idle overlay ≈ 0 cost).
                if (text != w.CurrentText)
                {
                    w.CurrentText = text;
                    w.Dirty = true;
                }
            }
        }

        if (_hwnd == IntPtr.Zero || !IsWindow(_hwnd)) return;

        // Reposition only when the target window / screen size actually moved — data
        // updates every tick, but poking DWM via SetWindowPos only when needed.
        if (TryGetBasePosition(out int x, out int y))
        {
            x += _driftX; y += _driftY; // OLED 防烧屏漂移
            if (x != _posX || y != _posY || _width != _posW || _height != _posH)
            {
                SetWindowPos(_hwnd, HWND_TOPMOST, x, y, _width, _height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
                _posX = x; _posY = y; _posW = _width; _posH = _height;
            }
        }

        RenderFrame();
    }

    /// <summary>
    /// 计算悬浮窗基准位置（桌面模式 = 全屏锚点；窗口模式 = 目标窗口锚点）。
    /// 返回 false 表示没有可用锚点（桌面模式以外的无目标窗口场景）。
    /// </summary>
    private bool TryGetBasePosition(out int x, out int y)
    {
        if (_desktopMode)
        {
            // Desktop mode: stay at the configured position over the whole screen
            // (track resolution changes since we don't have a target window rect).
            var (ox, oy) = CalculateOffset(GetSystemMetrics(0), GetSystemMetrics(1));
            x = ox; y = oy;
            return true;
        }
        if (_targetHwnd != IntPtr.Zero && IsWindow(_targetHwnd) && GetWindowRect(_targetHwnd, out var rc))
        {
            var (ox, oy) = CalculateOffset(rc.Right - rc.Left, rc.Bottom - rc.Top);
            x = rc.Left + ox; y = rc.Top + oy;
            return true;
        }
        x = y = 0;
        return false;
    }

    /// <summary>按基准位置 + OLED 漂移偏移重新落位。</summary>
    private void ApplyPosition()
    {
        if (_hwnd == IntPtr.Zero || !IsWindow(_hwnd)) return;
        if (!TryGetBasePosition(out var x, out var y)) return;
        x += _driftX; y += _driftY;
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, _width, _height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        _posX = x; _posY = y; _posW = _width; _posH = _height;
    }

    #region OLED burn-in protection

    /// <summary>
    /// OLED 防烧屏开关。开启后悬浮窗每 45 秒沿半径 8px 的小圆轨迹挪一步
    /// （每步 30°，整圈 9 分钟），避免监控内容长时间静止在同一批像素上灼烧面板；
    /// 关闭时立即回到基准位置。
    /// </summary>
    public void SetOledProtection(bool enabled)
    {
        if (_oledEnabled == enabled) return;
        _oledEnabled = enabled;
        if (enabled)
        {
            _oledTimer ??= new Timer(_ => OledDriftTick(), null, 45000, 45000);
        }
        else
        {
            _oledTimer?.Dispose();
            _oledTimer = null;
            _driftX = _driftY = 0;
            ApplyPosition();
        }
    }

    private void OledDriftTick()
    {
        if (!_oledEnabled) return;
        _oledAngle += MathF.PI / 6f;
        _driftX = (int)MathF.Round(MathF.Cos(_oledAngle) * 8f);
        _driftY = (int)MathF.Round(MathF.Sin(_oledAngle) * 8f);
        ApplyPosition();
    }

    #endregion

    private (int x, int y) CalculateOffset(int screenW, int screenH)
    {
        int m = 10;
        return _position switch
        {
            OverlayPosition.TopLeft => (m, m),
            OverlayPosition.TopCenter => ((screenW - _width) / 2, m),
            OverlayPosition.TopRight => (screenW - _width - m, m),
            OverlayPosition.MiddleLeft => (m, (screenH - _height) / 2),
            OverlayPosition.Center => ((screenW - _width) / 2, (screenH - _height) / 2),
            OverlayPosition.MiddleRight => (screenW - _width - m, (screenH - _height) / 2),
            OverlayPosition.BottomLeft => (m, screenH - _height - m),
            OverlayPosition.BottomCenter => ((screenW - _width) / 2, screenH - _height - m),
            OverlayPosition.BottomRight => (screenW - _width - m, screenH - _height - m),
            _ => (m, m)
        };
    }

    #region Rendering — per-pixel alpha via UpdateLayeredWindow

    private void RenderFrame()
    {
        if (_hwnd == IntPtr.Zero) return;

        // Incremental rendering: if no widget changed, skip the whole frame — an idle
        // overlay costs ≈ 0 (no GetDC, no redraw, no UpdateLayeredWindow to the compositor).
        bool anyDirty = false;
        foreach (var w in _widgets)
        {
            if (w.Dirty) { anyDirty = true; break; }
        }
        if (!anyDirty) return;

        var hdcScreen = GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero) return;
        try
        {
            if (!EnsureSurface(hdcScreen)) return;

            // Fluent 卡片背景（半透明圆角面板 + 细边框）预渲染在 _bgCard 里，
            // 首帧整幅拷入 surface；脏区域擦除 = 从 _bgCard 拷回对应像素。
            if (_bgCard == null || _bgCard.Width != _width || _bgCard.Height != _height)
                RenderBackgroundCard();

            bool drew = false;
            if (!_surfaceInited)
            {
                CopyBgRegion(0, 0, _width, _height);
                _surfaceInited = true;
                drew = true;
            }

            // Affected region = union of the dirty widgets' rects
            int minX = _width, minY = _height, maxX = 0, maxY = 0;
            foreach (var w in _widgets)
            {
                if (!w.Dirty) continue;
                if (w.X < minX) minX = w.X;
                if (w.Y < minY) minY = w.Y;
                if (w.X + w.Width > maxX) maxX = w.X + w.Width;
                if (w.Y + w.Height > maxY) maxY = w.Y + w.Height;
            }

            if (maxX > minX && maxY > minY)
            {
                // Erase the region (restore background card pixels), then re-blit every
                // widget overlapping it in layer order — dirty ones re-render into their
                // cached bitmap first, unchanged overlapping ones reuse their cached pixels
                // so the layer stacking stays correct.
                CopyBgRegion(minX, minY, maxX, maxY);
                foreach (var w in _widgets.OrderBy(x => x.Layer))
                {
                    if (w.X >= maxX || w.Y >= maxY || w.X + w.Width <= minX || w.Y + w.Height <= minY)
                        continue;
                    if (w.Dirty) RenderWidget(_surfaceDc, w);
                    BlitWidget(_surfaceDc, w);
                }
                drew = true;
            }

            foreach (var w in _widgets) w.Dirty = false;

            // Push the whole surface to the layered window with per-pixel alpha
            if (drew)
            {
                GetWindowRect(_hwnd, out var rc);
                var ptDst = new POINT { X = rc.Left, Y = rc.Top };
                var ptSrc = new POINT { X = 0, Y = 0 };
                var size = new SIZE { cx = _width, cy = _height };
                var blend = new BLENDFUNCTION
                {
                    BlendOp = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };
                UpdateLayeredWindow(_hwnd, hdcScreen, ref ptDst, ref size, _surfaceDc, ref ptSrc, 0, ref blend, ULW_ALPHA);
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    /// <summary>
    /// Fluent 卡片背景：半透明深色圆角面板（圆角 10px，跟随背景透明度滑杆）
    /// + 1px 白色低透明度细边框（Fluent 卡片描边）。预渲染到 _bgCard，
    /// 透明度变化时置空重建。
    /// </summary>
    private void RenderBackgroundCard()
    {
        _bgCard?.Dispose();
        var bmp = new SKBitmap(_width, _height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        byte a = (byte)(_bgOpacity * 255);
        var rect = new SKRect(0.5f, 0.5f, _width - 0.5f, _height - 0.5f);
        using var fill = new SKPaint { Color = new SKColor(32, 32, 32, a), IsAntialias = true };
        using var border = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 32),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        };
        canvas.DrawRoundRect(rect, 10, 10, fill);
        canvas.DrawRoundRect(rect, 10, 10, border);
        canvas.Flush();
        _bgCard = bmp;
    }

    /// <summary>
    /// 把 _bgCard 的一个子区域拷回 overlay surface（=「擦除」该区域的控件像素）。
    /// 按行 memcpy，圆角/边框像素与首帧完全一致，不会再出现方角穿帮。
    /// </summary>
    private unsafe void CopyBgRegion(int x0, int y0, int x1, int y1)
    {
        if (_bgCard == null || _surfaceBits == IntPtr.Zero) return;
        x0 = Math.Clamp(x0, 0, _width);
        y0 = Math.Clamp(y0, 0, _height);
        x1 = Math.Clamp(x1, 0, _width);
        y1 = Math.Clamp(y1, 0, _height);
        if (x1 <= x0 || y1 <= y0) return;

        var src = (byte*)_bgCard.GetPixels();
        var dst = (byte*)_surfaceBits;
        int rowBytes = (x1 - x0) * 4;
        for (int y = y0; y < y1; y++)
            Buffer.MemoryCopy(src + (y * _width + x0) * 4, dst + (y * _width + x0) * 4, rowBytes, rowBytes);
    }

    /// <summary>Re-renders a dirty widget into its cached bitmap (no blit).</summary>
    private void RenderWidget(IntPtr hdc, WidgetInstance w)
    {
        if (w.Width <= 0 || w.Height <= 0) return;
        if (w.IsChart) DrawChartToCanvas(w);
        else if (w.Type == OverlayWidgetType.ColorBlock) FillColorBlock(hdc, w);
        else if (w.Type == OverlayWidgetType.CustomImage) DrawImageToCanvas(w);
        else if (w.Type == OverlayWidgetType.CustomText) DrawTextToCanvas(w, w.CustomText);
        else DrawTextToCanvas(w);
    }

    /// <summary>Alpha-blends a widget's cached bitmap onto the surface at its position.</summary>
    private void BlitWidget(IntPtr hdc, WidgetInstance w)
    {
        if (w.Type == OverlayWidgetType.ColorBlock)
        {
            if (w.Dib == IntPtr.Zero || w.DibDC == IntPtr.Zero) return;
            var blend = new BLENDFUNCTION
            {
                BlendOp = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA
            };
            AlphaBlend(hdc, w.X, w.Y, w.Width, w.Height, w.DibDC, 0, 0, w.Width, w.Height, blend);
        }
        else if (w.RenderBmp != null)
        {
            BlitSkiaBitmap(hdc, w, w.RenderBmp);
        }
    }

    /// <summary>Reuses the overlay surface (DIB + memory DC); rebuilt only when the size changes.</summary>
    private bool EnsureSurface(IntPtr hdcScreen)
    {
        if (_surfaceDib != IntPtr.Zero && _surfaceW == _width && _surfaceH == _height)
            return true;

        ReleaseSurface();
        if (_width <= 0 || _height <= 0) return false;

        var bi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = _width,
                biHeight = -_height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            }
        };
        _surfaceDib = CreateDIBSection(hdcScreen, ref bi, 0, out var pBits, IntPtr.Zero, 0);
        if (_surfaceDib == IntPtr.Zero) return false;
        _surfaceBits = pBits;
        _surfaceDc = CreateCompatibleDC(hdcScreen);
        if (_surfaceDc == IntPtr.Zero)
        {
            DeleteObject(_surfaceDib);
            _surfaceDib = IntPtr.Zero;
            return false;
        }
        _surfaceOld = SelectObject(_surfaceDc, _surfaceDib);
        _surfaceW = _width;
        _surfaceH = _height;
        return true;
    }

    private void ReleaseSurface()
    {
        if (_surfaceDc != IntPtr.Zero && _surfaceOld != IntPtr.Zero)
            SelectObject(_surfaceDc, _surfaceOld);
        if (_surfaceDib != IntPtr.Zero) DeleteObject(_surfaceDib);
        if (_surfaceDc != IntPtr.Zero) DeleteDC(_surfaceDc);
        _surfaceOld = _surfaceDib = _surfaceDc = _surfaceBits = IntPtr.Zero;
        _surfaceW = _surfaceH = 0;
        _surfaceInited = false;
    }

    /// <summary>Reuses a widget's DIB + DC (shared by text blit / color block / image); rebuilt on size change.</summary>
    private static bool EnsureWidgetSurface(WidgetInstance w, IntPtr hdc)
    {
        if (w.Dib != IntPtr.Zero && w.DibW == w.Width && w.DibH == w.Height)
            return true;

        ReleaseWidgetSurface(w);

        var bi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w.Width,
                biHeight = -w.Height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            }
        };
        w.Dib = CreateDIBSection(hdc, ref bi, 0, out var pBits, IntPtr.Zero, 0);
        if (w.Dib == IntPtr.Zero) return false;
        w.DibBits = pBits;
        w.DibDC = CreateCompatibleDC(hdc);
        if (w.DibDC == IntPtr.Zero)
        {
            DeleteObject(w.Dib);
            w.Dib = IntPtr.Zero;
            return false;
        }
        w.DibOld = SelectObject(w.DibDC, w.Dib);
        w.DibW = w.Width;
        w.DibH = w.Height;
        return true;
    }

    private static void ReleaseWidgetSurface(WidgetInstance w)
    {
        if (w.DibDC != IntPtr.Zero && w.DibOld != IntPtr.Zero)
            SelectObject(w.DibDC, w.DibOld);
        if (w.Dib != IntPtr.Zero) DeleteObject(w.Dib);
        if (w.DibDC != IntPtr.Zero) DeleteDC(w.DibDC);
        w.DibOld = w.Dib = w.DibDC = w.DibBits = IntPtr.Zero;
        w.DibW = w.DibH = 0;
    }

    /// <summary>Reuses a widget's Skia canvas; rebuilt when the size or font size changes.</summary>
    private static SKCanvas EnsureWidgetCanvas(WidgetInstance w, out SKBitmap bmp)
    {
        if (w.RenderBmp is null || w.RenderCanvas is null ||
            w.RenderW != w.Width || w.RenderH != w.Height || w.RenderFs != w.FontSize)
        {
            ReleaseWidgetSkia(w);
            // Same pixel format as the original per-frame bitmaps (charts used explicit
            // Premul; text used the (w, h, isOpaque) ctor) — single shared form for all.
            w.RenderBmp = new SKBitmap(w.Width, w.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            w.RenderCanvas = new SKCanvas(w.RenderBmp);
            w.RenderW = w.Width;
            w.RenderH = w.Height;
            w.RenderFs = w.FontSize;
        }
        bmp = w.RenderBmp;
        return w.RenderCanvas!;
    }

    private static void ReleaseWidgetSkia(WidgetInstance w)
    {
        w.RenderCanvas?.Dispose();
        w.RenderBmp?.Dispose();
        w.RenderFont?.Dispose();
        w.RenderCanvas = null;
        w.RenderBmp = null;
        w.RenderFont = null;

        if (w.ChartPaints != null)
        {
            foreach (var p in w.ChartPaints) p?.Dispose();
            w.ChartPaints = null;
        }
        w.AreaShader?.Dispose();
        w.AreaShader = null;
        w.AreaPath?.Dispose();
        w.GlowPath?.Dispose();
        w.LinePath?.Dispose();
        w.AreaPath = w.GlowPath = w.LinePath = null;
        w.ChartPoints = null;
    }

    [DllImport("msimg32.dll")]
    private static extern bool AlphaBlend(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, BLENDFUNCTION blendFunction);

    private void DrawTextToCanvas(WidgetInstance w, string? textOverride = null)
    {
        if (w.Width <= 0 || w.Height <= 0) return;

        string text = textOverride ?? w.CurrentText;
        var canvas = EnsureWidgetCanvas(w, out _);
        // Clear the reused bitmap first — otherwise a shorter value (e.g. "9 FPS" right
        // after "120 FPS") would leave ghost pixels of the old text behind (the "覆盖" bug).
        canvas.Clear(SKColors.Transparent);

        if (string.IsNullOrEmpty(text))
        {
            canvas.Flush();
            return;
        }

        float fontSize = Math.Max(8, w.FontSize);
        if (w.RenderFont is null || w.RenderFont.Size != fontSize)
        {
            w.RenderFont?.Dispose();
            w.RenderFont = new SKFont(TypefaceBold, fontSize);
        }
        var font = w.RenderFont;

        // Vertical centering: baseline = ascent center within the widget height
        font.GetFontMetrics(out var fm);
        float textY = (w.Height - (fm.Descent - fm.Ascent)) / 2f - fm.Ascent;

        uint tc = w.TextColorArgb;
        byte ta = (byte)((tc >> 24) & 0xFF);
        byte tr = (byte)((tc >> 16) & 0xFF);
        byte tg = (byte)((tc >> 8) & 0xFF);
        byte tb = (byte)(tc & 0xFF);

        // Shadow — auto white or black depending on text luminance
        uint lum = tr * 299u + tg * 587u + tb * 114u;
        byte sa = (byte)(ta * 120 / 255);

        // Clip to the widget bounds so overflowing text can't spill onto neighboring widgets
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, w.Width, w.Height));
        using (var shadowPaint = new SKPaint { Color = new SKColor(lum < 100_000 ? (byte)255 : (byte)0, lum < 100_000 ? (byte)255 : (byte)0, lum < 100_000 ? (byte)255 : (byte)0, sa), IsAntialias = true })
            canvas.DrawText(text, 2, textY + 1, font, shadowPaint);

        // Main text — premultiplied ARGB, rendered correctly by SkiaSharp
        using (var textPaint = new SKPaint { Color = new SKColor(tr, tg, tb, ta), IsAntialias = true })
            canvas.DrawText(text, 1, textY, font, textPaint);
        canvas.Restore();

        canvas.Flush();
    }

    /// <summary>
    /// Renders a solid color block widget with per-pixel alpha so the chosen
    /// ARGB color (e.g. 透明黑) fades the game/desktop behind it instead of
    /// painting an opaque box.
    /// </summary>
    private void FillColorBlock(IntPtr hdc, WidgetInstance w)
    {
        if (w.Width <= 0 || w.Height <= 0) return;
        if (!EnsureWidgetSurface(w, hdc)) return;

        // Build a premultiplied BGRA DIB (cached, rendered once) — blitted by BlitWidget
        uint a = (w.ColorArgb >> 24) & 0xFF;
        uint r = (w.ColorArgb >> 16) & 0xFF;
        uint g = (w.ColorArgb >> 8) & 0xFF;
        uint b = w.ColorArgb & 0xFF;
        uint pixel = (uint)(a << 24 | (b * a / 255) << 16 | (g * a / 255) << 8 | (r * a / 255));
        unsafe
        {
            var span = new Span<uint>((void*)w.DibBits, w.DibW * w.DibH);
            span.Fill(pixel);
            // Bake a 1px white border into the cached bitmap for visibility
            if (w.DibW > 1 && w.DibH > 1)
            {
                for (int x = 0; x < w.DibW; x++)
                {
                    span[x] = 0xFFFFFFFF;                            // top
                    span[(w.DibH - 1) * w.DibW + x] = 0xFFFFFFFF;    // bottom
                }
                for (int y = 0; y < w.DibH; y++)
                {
                    span[y * w.DibW] = 0xFFFFFFFF;                   // left
                    span[y * w.DibW + w.DibW - 1] = 0xFFFFFFFF;      // right
                }
            }
        }
    }

    /// <summary>
    /// Draws a custom image widget via SkiaSharp (scaled to widget bounds).
    /// </summary>
    private void DrawImageToCanvas(WidgetInstance w)
    {
        if (w.Width <= 0 || w.Height <= 0) return;

        // Load lazily and cache
        if (w.CachedImage == null && !string.IsNullOrEmpty(w.ImagePath))
        {
            try
            {
                if (File.Exists(w.ImagePath))
                    w.CachedImage = SKBitmap.Decode(w.ImagePath);
            }
            catch { }
        }

        var canvas = EnsureWidgetCanvas(w, out _);
        canvas.Clear(SKColors.Transparent);
        if (w.CachedImage != null)
        {
            // Scale-to-fill (cover) with transparency
            canvas.DrawBitmap(w.CachedImage, new SKRect(0, 0, w.Width, w.Height));
        }
        canvas.Flush();
    }

    // Cached typefaces to avoid per-frame allocation/leak
    private static SKTypeface? _typefaceBold;
    private static SKTypeface? _typefaceNormal;
    private static string _fontFamily = "Microsoft YaHei UI";
    private static SKTypeface TypefaceBold => _typefaceBold ??= SKTypeface.FromFamilyName(_fontFamily, SKFontStyle.Bold);
    private static SKTypeface TypefaceNormal => _typefaceNormal ??= SKTypeface.FromFamilyName(_fontFamily, SKFontStyle.Normal);

    /// <summary>
    /// Changes the font family used by all overlay text and chart labels.
    /// Call from the settings UI when the user picks a different font.
    /// </summary>
    public static void SetFontFamily(string family)
    {
        if (string.IsNullOrWhiteSpace(family)) return;
        _fontFamily = family;
        _typefaceBold = null; // force re-creation on next access
        _typefaceNormal = null;
        // 缓存的 SKFont / 图表画笔持有旧 typeface 引用，字体切换后逐个重建，下一帧生效
        var inst = _instance;
        if (inst != null)
        {
            foreach (var w in inst._widgets)
            {
                w.RenderFont?.Dispose();
                w.RenderFont = null;
                if (w.ChartPaints != null)
                {
                    foreach (var p in w.ChartPaints) p?.Dispose();
                    w.ChartPaints = null;
                }
                w.Dirty = true;
            }
        }
    }

    public static string FontFamily => _fontFamily;

    // Chart paint slots (see GetChartPaints) — indexed so frames reuse the same SKPaint objects
    private const int P_BG = 0, P_TITLE = 1, P_GRID = 2, P_AREA = 3,
                      P_GLOW = 4, P_LINE = 5, P_DOT = 6, P_DOTRING = 7, P_LABEL = 8;

    /// <summary>Returns the widget's cached chart paints, creating them on first use.</summary>
    private static SKPaint[] GetChartPaints(WidgetInstance w)
    {
        if (w.ChartPaints == null)
        {
            // Fluent 配色：面板 = 卡片上的浅色浮层（白色低透明度），网格/文字降低对比，
            // 语义色（fps 绿 / 温度橙等）保持不变
            w.ChartPaints = new SKPaint[9]
            {
                new SKPaint { Color = new SKColor(255, 255, 255, 16), IsAntialias = true },
                new SKPaint { Color = new SKColor(235, 235, 235, 255), IsAntialias = true, Typeface = TypefaceBold },
                new SKPaint { Color = new SKColor(255, 255, 255, 26), StrokeWidth = 1, PathEffect = SKPathEffect.CreateDash(new float[] { 4, 5 }, 0) },
                new SKPaint { IsAntialias = true },
                new SKPaint { StrokeWidth = 4, IsAntialias = true, IsStroke = true, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round },
                new SKPaint { StrokeWidth = 2, IsAntialias = true, IsStroke = true, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round },
                new SKPaint { IsAntialias = true },
                new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true },
                new SKPaint { Color = new SKColor(185, 185, 185, 255), IsAntialias = true, Typeface = TypefaceNormal }
            };
        }
        return w.ChartPaints;
    }

    /// <summary>Returns a reusable point array sized for the current point count.</summary>
    private static SKPoint[] GetChartPoints(WidgetInstance w, int count)
    {
        if (w.ChartPoints == null || w.ChartPoints.Length < count)
            w.ChartPoints = new SKPoint[count];
        return w.ChartPoints;
    }

    /// <summary>Packs an SKColor into a uint for cheap equality (shader cache check).</summary>
    private static uint ColorKey(SKColor c) => (uint)(c.Alpha << 24 | c.Blue << 16 | c.Green << 8 | c.Red);

    /// <summary>
    /// 图表样式表：标题 / 数值小数位 / 线条颜色。fps、cputemp 两条保持既有
    /// 外观不变（F0、绿/橙）；帧时间与渲染延迟按毫秒 F1 显示。
    /// </summary>
    private static readonly Dictionary<string, (string Title, int Decimals, SKColor Color)> ChartStyles = new()
    {
        ["fps"] = ("FPS", 0, new SKColor(60, 230, 110)),            // 绿
        ["low1"] = ("1% Low", 0, new SKColor(255, 200, 60)),               // 琥珀
        ["low01"] = ("0.1% Low", 0, new SKColor(255, 95, 95)),             // 红
        ["cputemp"] = ("CPU °C", 0, new SKColor(255, 170, 40)),     // 橙
        ["frametime"] = ("帧时间 ms", 1, new SKColor(80, 190, 255)),     // 蓝
        ["renderlatency"] = ("渲染延迟 ms", 1, new SKColor(190, 130, 255)) // 紫
    };

    /// <summary>
    /// 图表 Y 轴范围（鲁棒自适应）。纯数据 min/max 自适应有两个毛病：
    /// ① 毛刺型序列（帧时间/渲染延迟）：单个 100ms 毛刺把正常 5ms 段压成贴底直线；
    /// ② 平稳序列（60±1 FPS）：噪声被放大成满屏锯齿，波动看起来巨大但毫无信息量。
    /// 修法：毛刺型序列上限取 P98；所有序列强制最小跨度（相对 mid 的 8%、绝对下限），
    /// 波动不足时居中扩展 —— 噪声不再是「趋势」。
    /// </summary>
    private static (float min, float max) ChartAxisRange(CircularBuffer buf, string key)
    {
        int take = Math.Min(buf.Count, 256);
        if (take == 0) return (0, 1);
        var vals = new float[take];
        for (int i = 0; i < take; i++) vals[i] = buf.Get(buf.Count - take + i);
        Array.Sort(vals);

        float lo = vals[0], hi = vals[take - 1];
        if (key is "frametime" or "renderlatency")
        {
            // 毛刺压制：上限用 P98（只留 2% 顶部空间给尖峰）
            var p98 = vals[Math.Max(0, (int)Math.Ceiling(take * 0.98) - 1)];
            if (p98 > lo) hi = p98;
        }

        float mid = (lo + hi) / 2f;
        float minHalf = key is "frametime" or "renderlatency" ? 2f : 1f;
        float half = Math.Max((hi - lo) / 2f, Math.Max(Math.Abs(mid) * 0.08f, minHalf));
        return (mid - half, mid + half);
    }

    /// <summary>Catmull-Rom 平滑折线（三次贝塞尔逼近）：像素级采样点画直线会显生硬。</summary>
    private static void AppendSmoothLines(SKPath path, SKPoint[] pts, int count)
    {
        if (count < 3)
        {
            for (int i = 1; i < count; i++) path.LineTo(pts[i]);
            return;
        }
        for (int i = 0; i < count - 1; i++)
        {
            var p0 = pts[Math.Max(0, i - 1)];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = pts[Math.Min(count - 1, i + 2)];
            path.CubicTo(
                p1.X + (p2.X - p0.X) / 6f, p1.Y + (p2.Y - p0.Y) / 6f,
                p2.X - (p3.X - p1.X) / 6f, p2.Y - (p3.Y - p1.Y) / 6f,
                p2.X, p2.Y);
        }
    }

    /// <summary>
    /// Renders a chart widget using SkiaSharp (the component library used by LiveCharts2)
    /// into the widget's cached bitmap; BlitWidget pushes it onto the overlay surface.
    /// SKPaint/SKPath/SKPoint[]/SKShader are reused across frames to avoid GC churn.
    /// </summary>
    private void DrawChartToCanvas(WidgetInstance w)
    {
        if (w.Width <= 0 || w.Height <= 0) return;

        var chartKey = w.Type switch
        {
            OverlayWidgetType.FpsChart => "fps",
            OverlayWidgetType.FpsLow1Chart => "low1",
            OverlayWidgetType.FpsLow01Chart => "low01",
            OverlayWidgetType.CpuTempChart => "cputemp",
            OverlayWidgetType.FpsTimeChart => "frametime",
            OverlayWidgetType.FpsRenderLatencyChart => "renderlatency",
            _ => null
        };
        if (chartKey == null || !_chartData.TryGetValue(chartKey, out var buf) || buf.Count < 2) return;

        var (min, max) = ChartAxisRange(buf, chartKey);
        int pad = 6;
        // Title font size follows the widget's FontSize (editable in the property panel),
        // auto-shrunk when the widget is too short; the chart area adapts to it.
        float titleFs = Math.Clamp(w.FontSize, 8, Math.Max(8, (w.Height - 2 * pad - 8) * 0.55f));
        int cy = pad + (int)titleFs + 4;
        int ch = w.Height - pad * 2 - (int)titleFs - 4;
        if (ch < 4) return;

        var style = ChartStyles[chartKey];
        var lineColor = style.Color;

        // Build SKBitmap sized to the widget FIRST — when (re)created it disposes the
        // widget's cached Skia resources (incl. the chart paints below), so paints must
        // be (re)created after this call. Otherwise we'd draw with disposed SKPaints
        // and crash the overlay on the first chart frame.
        var canvas = EnsureWidgetCanvas(w, out _);
        canvas.Clear(SKColors.Transparent);

        // --- Reusable paints (created once per widget, no per-frame allocation) ---
        var paints = GetChartPaints(w);
        var bgPaint = paints[P_BG];
        var titlePaint = paints[P_TITLE];
        titlePaint.TextSize = titleFs;
        var gridPaint = paints[P_GRID];
        var areaPaint = paints[P_AREA];
        var glowPaint = paints[P_GLOW];
        glowPaint.Color = lineColor.WithAlpha(55);
        var linePaint = paints[P_LINE];
        linePaint.Color = lineColor;
        var dotPaint = paints[P_DOT];
        dotPaint.Color = lineColor;
        var dotRingPaint = paints[P_DOTRING];
        var labelPaint = paints[P_LABEL];
        labelPaint.TextSize = Math.Max(8, titleFs * 0.8f);

        // --- Labels: measure text so numbers never overlap the title / line / dot ---
        string title = style.Title;
        float titleW = titlePaint.MeasureText(title);
        int decimals = style.Decimals;
        string valStr = buf.Get(buf.Count - 1).ToString("F" + decimals);
        string maxStr = max.ToString("F" + decimals), minStr = min.ToString("F" + decimals);
        float maxW = labelPaint.MeasureText(maxStr), minW = labelPaint.MeasureText(minStr);

        // Right gutter for the min/max labels; narrow widgets drop the labels instead
        float rightPad = Math.Max(maxW, minW) + 8;
        int cx = pad, cw = w.Width - pad * 2 - (int)rightPad;
        bool showMinMax = true;
        if (cw < 24) { rightPad = 0; cw = w.Width - pad * 2; showMinMax = false; }
        if (cw < 4) return;

        // --- Light rounded panel (Fluent elevation layer on the card; corners transparent) ---
        canvas.DrawRoundRect(new SKRect(0, 0, w.Width, w.Height), 8, 8, bgPaint);

        // --- Title + current value beside it (start after the measured title) ---
        // NOTE: DrawText's y is the BASELINE, not the top — draw at `pad + titleFs` so the
        // glyphs don't get clipped above the bitmap edge.
        int titleBaseline = pad + (int)titleFs;
        canvas.DrawText(title, pad, titleBaseline, titlePaint);
        canvas.DrawText(valStr, pad + titleW + 6, titleBaseline, titlePaint);

        // --- Horizontal grid lines ---
        for (int g = 1; g <= 3; g++)
        {
            int gy = cy + ch * g / 4;
            canvas.DrawLine(cx, gy, cx + cw, gy, gridPaint);
        }

        // --- Build points (reused array) ---
        int count = Math.Min(buf.Count, cw);
        float xStep = (float)cw / Math.Max(1, count - 1);
        var points = GetChartPoints(w, count);
        for (int i = 0; i < count; i++)
        {
            float sampleVal = buf.Get(buf.Count - count + i);
            float norm = max > min ? (sampleVal - min) / (max - min) : 0.5f;
            points[i] = new SKPoint(
                cx + i * xStep,
                Math.Clamp(cy + ch - norm * ch, cy, cy + ch)
            );
        }

        // --- Gradient area fill under the line (shader reused until geometry/color changes) ---
        if (w.AreaShader == null || w.AreaShaderCy != cy || w.AreaShaderCh != ch || w.AreaShaderColor != ColorKey(lineColor))
        {
            var old = w.AreaShader;
            w.AreaShader = SKShader.CreateLinearGradient(
                new SKPoint(0, cy), new SKPoint(0, cy + ch),
                new[] { lineColor.WithAlpha(90), lineColor.WithAlpha(0) },
                new[] { 0f, 1f }, SKShaderTileMode.Clamp);
            areaPaint.Shader = w.AreaShader;
            old?.Dispose();
            w.AreaShaderCy = cy;
            w.AreaShaderCh = ch;
            w.AreaShaderColor = ColorKey(lineColor);
        }
        w.AreaPath ??= new SKPath();
        w.AreaPath.Rewind();
        w.AreaPath.MoveTo(points[0].X, cy + ch);
        for (int i = 0; i < count; i++) w.AreaPath.LineTo(points[i]);
        w.AreaPath.LineTo(points[count - 1].X, cy + ch);
        w.AreaPath.Close();
        canvas.DrawPath(w.AreaPath, areaPaint);

        // --- Glow line (thicker, dimmer) — path reused via Rewind, Catmull-Rom smoothed ---
        w.GlowPath ??= new SKPath();
        w.GlowPath.Rewind();
        w.GlowPath.MoveTo(points[0]);
        AppendSmoothLines(w.GlowPath, points, count);
        canvas.DrawPath(w.GlowPath, glowPaint);

        // --- Main line (smoothed) ---
        w.LinePath ??= new SKPath();
        w.LinePath.Rewind();
        w.LinePath.MoveTo(points[0]);
        AppendSmoothLines(w.LinePath, points, count);
        canvas.DrawPath(w.LinePath, linePaint);

        // --- Current value dot ---
        var last = points[count - 1];
        canvas.DrawCircle(last, 4, dotPaint);
        canvas.DrawCircle(last, 4, dotRingPaint);

        // --- min/max labels (right-aligned inside the reserved gutter) ---
        if (showMinMax)
        {
            labelPaint.TextAlign = SKTextAlign.Right;
            canvas.DrawText(maxStr, w.Width - pad, cy + 12, labelPaint);
            canvas.DrawText(minStr, w.Width - pad, cy + ch - 3, labelPaint);
            labelPaint.TextAlign = SKTextAlign.Left;
        }

        canvas.Flush();
    }

    /// <summary>
    /// Copies an SKBitmap into a GDI memory DC at the given position (widget coordinate),
    /// reusing the widget's cached DIB (sized to the widget) instead of allocating per frame.
    /// </summary>
    private void BlitSkiaBitmap(IntPtr hdcDest, WidgetInstance w, SKBitmap bmp)
    {
        if (bmp.Width <= 0 || bmp.Height <= 0) return;
        if (!EnsureWidgetSurface(w, hdcDest)) return;

        // Copy BGRA premultiplied pixels (bmp is widget-sized)
        var pixels = bmp.Bytes;
        Marshal.Copy(pixels, 0, w.DibBits, Math.Min(pixels.Length, w.DibW * w.DibH * 4));

        // Alpha-blend into destination DC — a plain BitBlt(SRCCOPY) would paste the
        // transparent (premultiplied-black) pixels as opaque black, making the chart
        // background look like a solid black box.
        var blend = new BLENDFUNCTION
        {
            BlendOp = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = AC_SRC_ALPHA
        };
        AlphaBlend(hdcDest, w.X, w.Y, bmp.Width, bmp.Height, w.DibDC, 0, 0, bmp.Width, bmp.Height, blend);
    }

    #endregion

    #region Drawing primitives — removed (now using GDI directly in RenderFrame)

    #endregion

    #region Widget drawing — removed (now using GDI directly in RenderFrame)

    #endregion

    #region Widget text formatting

    /// <summary>
    /// Returns the default prefix label for a widget type, e.g. "FPS：", "CPU 温度：".
    /// Used when adding a widget so the overlay reads "FPS：120" instead of "120".
    /// </summary>
    public static string GetDefaultPrefix(OverlayWidgetType type)
    {
        return type switch
        {
            OverlayWidgetType.FpsText => "FPS: ",
            OverlayWidgetType.FpsLow1Text => "1% Low: ",
            OverlayWidgetType.FpsLow01Text => "0.1% Low: ",
            OverlayWidgetType.FpsTimeText => "帧时间: ",
            OverlayWidgetType.FpsRenderLatencyText => "渲染延迟: ",
            OverlayWidgetType.CpuTempText => "CPU 温度: ",
            OverlayWidgetType.CpuLoadText => "CPU 负载: ",
            OverlayWidgetType.CpuClockText => "CPU 频率: ",
            OverlayWidgetType.CpuPowerText => "CPU 功耗: ",
            OverlayWidgetType.CpuNameText => "CPU: ",
            OverlayWidgetType.GpuTempText => "GPU 温度: ",
            OverlayWidgetType.GpuLoadText => "GPU 负载: ",
            OverlayWidgetType.GpuClockText => "GPU 频率: ",
            OverlayWidgetType.GpuPowerText => "GPU 功耗: ",
            OverlayWidgetType.GpuVramText => "显存: ",
            OverlayWidgetType.GpuNameText => "GPU: ",
            OverlayWidgetType.MemLoadText => "内存负载: ",
            OverlayWidgetType.MemUsedText => "内存使用: ",
            OverlayWidgetType.DiskReadText => "磁盘读取: ",
            OverlayWidgetType.DiskWriteText => "磁盘写入: ",
            OverlayWidgetType.NetUpText => "网络上传: ",
            OverlayWidgetType.NetDownText => "网络下载: ",
            _ => ""
        };
    }

    /// <summary>
    /// Returns just the value portion (no prefix), e.g. "120", "65°C", "3.8 GHz".
    /// </summary>
    private static string FormatWidgetValue(OverlayWidgetType type, MonitorSample s)
    {
        return type switch
        {
            OverlayWidgetType.FpsText => s.Fps >= 0 ? $"{s.Fps:F0} FPS" : "-- FPS",
            OverlayWidgetType.FpsLow1Text => s.FpsLow1 >= 0 ? $"{s.FpsLow1:F0} FPS" : "-- FPS",
            OverlayWidgetType.FpsLow01Text => s.FpsLow01 >= 0 ? $"{s.FpsLow01:F0} FPS" : "-- FPS",
            OverlayWidgetType.FpsTimeText => s.FrameTimeMs >= 0 ? $"{s.FrameTimeMs:F1} ms" : "-- ms",
            OverlayWidgetType.FpsRenderLatencyText => s.RenderLatencyMs >= 0 ? $"{s.RenderLatencyMs:F1} ms" : "-- ms",
            OverlayWidgetType.CpuTempText => s.CpuTemp >= 0 ? $"{s.CpuTemp:F0}°C" : "--°C",
            OverlayWidgetType.CpuLoadText => s.CpuLoad >= 0 ? $"{s.CpuLoad:F0}%" : "--%",
            OverlayWidgetType.CpuClockText => s.CpuClock > 0 ? $"{s.CpuClock / 1000f:F1} GHz" : "-- GHz",
            OverlayWidgetType.CpuPowerText => s.CpuPower > 0 ? $"{s.CpuPower:F1} W" : "-- W",
            OverlayWidgetType.GpuTempText => s.GpuTemp >= 0 ? $"{s.GpuTemp:F0}°C" : "--°C",
            OverlayWidgetType.GpuLoadText => s.GpuLoad >= 0 ? $"{s.GpuLoad:F0}%" : "--%",
            OverlayWidgetType.GpuClockText => s.GpuClock > 0 ? $"{s.GpuClock:F0} MHz" : "-- MHz",
            OverlayWidgetType.GpuPowerText => s.GpuPower > 0 ? $"{s.GpuPower:F1} W" : "-- W",
            OverlayWidgetType.GpuVramText => s.GpuVramUsedGB >= 0 ? $"{s.GpuVramUsedGB:F1} GB" : "-- GB",
            OverlayWidgetType.MemLoadText => s.MemLoad >= 0 ? $"{s.MemLoad:F0}%" : "--%",
            OverlayWidgetType.MemUsedText => s.MemUsedGB >= 0 ? $"{s.MemUsedGB:F1} GB" : "-- GB",
            OverlayWidgetType.DiskReadText => s.DiskReadMBs >= 0 ? $"{s.DiskReadMBs:F1} MB/s" : "-- MB/s",
            OverlayWidgetType.DiskWriteText => s.DiskWriteMBs >= 0 ? $"{s.DiskWriteMBs:F1} MB/s" : "-- MB/s",
            OverlayWidgetType.NetUpText => s.NetUpMBs >= 0 ? $"{s.NetUpMBs:F2} MB/s" : "-- MB/s",
            OverlayWidgetType.NetDownText => s.NetDownMBs >= 0 ? $"{s.NetDownMBs:F2} MB/s" : "-- MB/s",
            OverlayWidgetType.CpuNameText => string.IsNullOrEmpty(s.CpuName) ? "CPU" : s.CpuName,
            OverlayWidgetType.GpuNameText => string.IsNullOrEmpty(s.GpuName) ? "GPU" : s.GpuName,
            _ => "--"
        };
    }

    private static (string? key, float value) GetChartValue(OverlayWidgetType type, MonitorSample s)
    {
        // 无效样本返回 -1（填充处以 value >= 0 过滤，不入 buffer）。
        // 不能用 0 兜底：0 会混进曲线把 min 压到 0，整个 Y 轴坐标系被毁掉
        //（典型：渲染延迟无数据时画成 0ms 贴底直线）。
        return type switch
        {
            OverlayWidgetType.FpsChart => ("fps", s.Fps),
            OverlayWidgetType.FpsLow1Chart => ("low1", s.FpsLow1),
            OverlayWidgetType.FpsLow01Chart => ("low01", s.FpsLow01),
            OverlayWidgetType.FpsTimeChart => ("frametime", s.FrameTimeMs),
            OverlayWidgetType.FpsRenderLatencyChart => ("renderlatency", s.RenderLatencyMs),
            OverlayWidgetType.CpuTempChart => ("cputemp", s.CpuTemp),
            _ => (null, -1)
        };
    }

    #endregion

    #region Public API

    public void SetTargetWindow(IntPtr hwnd) => _targetHwnd = hwnd;
    public void SetDesktopMode(bool desktopMode) => _desktopMode = desktopMode;
    public void SetPosition(OverlayPosition position) => _position = position;
    public void SetBackgroundOpacity(float opacity)
    {
        opacity = Math.Clamp(opacity, 0f, 1f);
        if (Math.Abs(opacity - _bgOpacity) < 0.001f) return;
        _bgOpacity = opacity;
        // Background card changed — regenerate it and refill the whole surface,
        // then re-render everything
        _bgCard?.Dispose();
        _bgCard = null;
        _surfaceInited = false;
        foreach (var w in _widgets) w.Dirty = true;
        RenderFrame();
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _topmostTimer?.Dispose();
        _topmostTimer = null;

        _oledTimer?.Dispose();
        _oledTimer = null;
        _oledEnabled = false;

        if (_hwnd != IntPtr.Zero && IsWindow(_hwnd))
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        _bgCard?.Dispose();
        _bgCard = null;

        // Dispose cached image bitmaps and per-widget render resources
        foreach (var w in _widgets)
        {
            w.CachedImage?.Dispose();
            w.CachedImage = null;
            ReleaseWidgetSurface(w);
            ReleaseWidgetSkia(w);
        }
        ReleaseSurface();

        if (_instance == this) _instance = null;
    }

    #endregion
}
