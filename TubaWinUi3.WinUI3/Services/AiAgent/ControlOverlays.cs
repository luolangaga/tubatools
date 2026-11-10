using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using SkiaSharp;
using Windows.Graphics;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>
/// Shared SkiaSharp-drawn layered overlay. The window owns no XAML: pixels are rendered into a
/// premultiplied BGRA DIB and composited by the OS, so it stays visible independently of WinUI
/// composition. Buttons are hit-tested in DIPs; WM_NCHITTEST makes the rest click-through.
/// </summary>
internal abstract class SkiaOverlayWindow : IDisposable
{
    protected const float CardRadius = 12;
    private readonly List<(string Id, SKRect Rect)> _buttons = [];
    private LayeredOverlaySurface? _surface;
    private float _scale = 1;
    private int _x, _y;
    private bool _dirty = true;
    private string? _hoverId;
    private long _lastSpin;
    protected bool Spinning { get; set; }

    protected static readonly SKColor CardFill = new(24, 24, 24, 250);
    protected static readonly SKColor CardBorder = new(255, 255, 255, 28);
    protected static readonly SKColor TextPrimary = new(245, 245, 245);
    protected static readonly SKColor TextSecondary = new(184, 184, 184);
    protected static readonly SKColor TextMuted = new(146, 146, 146);
    protected static readonly SKColor Accent = new(76, 194, 255);
    protected static readonly SKColor Caution = new(255, 184, 77);
    protected static readonly SKColor Danger = new(232, 17, 35);
    protected static readonly SKColor DangerHover = new(244, 63, 74);
    protected static readonly SKColor OnAccent = new(10, 18, 26);
    private static readonly SKTypeface TypefaceNormal = ResolveTypeface(SKFontStyle.Normal);
    private static readonly SKTypeface TypefaceBold = ResolveTypeface(SKFontStyle.Bold);

    protected static SKTypeface ResolveTypeface(SKFontStyle style) =>
        SKTypeface.FromFamilyName("Microsoft YaHei UI", style)
        ?? SKTypeface.FromFamilyName("Segoe UI", style)
        ?? SKTypeface.Default;

    protected static SKColor WithAlpha(SKColor color, byte alpha) => color.WithAlpha(alpha);

    protected static float DpiScale(IntPtr anchor) => (float)Math.Max(1, GetDpiForWindow(anchor) / 96.0);

    protected static (int X, int Y, int Width, int Height) WorkArea(IntPtr anchor)
    {
        var area = DisplayArea.GetFromWindowId(
            Microsoft.UI.Win32Interop.GetWindowIdFromWindow(anchor), DisplayAreaFallback.Nearest).WorkArea;
        return (area.X, area.Y, area.Width, area.Height);
    }

    protected void Ensure(string title, float widthDip, float heightDip, float scale)
    {
        var width = Math.Max(1, (int)Math.Round(widthDip * scale));
        var height = Math.Max(1, (int)Math.Round(heightDip * scale));
        if (_surface is not null && _surface.Width == width && _surface.Height == height && Math.Abs(_scale - scale) < 0.01f)
        {
            _scale = scale;
            return;
        }
        _surface?.Dispose();
        _surface = new LayeredOverlaySurface(title, width, height, clickThrough: true, captureExcluded: false)
        {
            IsInteractive = HitTestPixel,
            Clicked = ClickPixel,
            Moved = MovePixel,
            Left = Leave
        };
        _scale = scale;
        _dirty = true;
    }

    /// <summary>
    /// Polled from the overlay timer: only the button area may intercept the mouse and be
    /// visible to WindowFromPoint; the rest of the card stays click-through.
    /// </summary>
    internal void UpdateHitThrough()
    {
        if (_surface is null || !GetCursorPos(out var cursor)) return;
        var over = HitTest((cursor.X - _x) / _scale, (cursor.Y - _y) / _scale) is not null;
        _surface.SetClickThrough(!over);
    }

    protected void Position(int x, int y) { _x = x; _y = y; }

    protected void RequestRender() => _dirty = true;

    protected void RenderAndPresent()
    {
        if (_surface is null) return;
        var info = new SKImageInfo(_surface.Width, _surface.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var skSurface = SKSurface.Create(info, _surface.Bits, info.RowBytes);
        if (skSurface is null) return;
        var canvas = skSurface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(_scale);
        _buttons.Clear();
        Draw(canvas);
        canvas.Flush();
        _dirty = false;
        _surface.Present(_x, _y);
    }

    /// <summary>Keeps the window in the desktop layout but draws nothing (used on the AI page).</summary>
    protected void ClearAndPresent()
    {
        _buttons.Clear();
        if (_surface is null) return;
        var info = new SKImageInfo(_surface.Width, _surface.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var skSurface = SKSurface.Create(info, _surface.Bits, info.RowBytes);
        if (skSurface is null) return;
        skSurface.Canvas.Clear(SKColors.Transparent);
        skSurface.Canvas.Flush();
        _dirty = false;
        _surface.Present(_x, _y);
    }

    internal void Advance()
    {
        if (_surface is null) return;
        if (Spinning)
        {
            var now = Environment.TickCount64;
            if (now - _lastSpin >= 33) { _lastSpin = now; _dirty = true; }
        }
        if (_dirty) RenderAndPresent();
    }

    protected abstract void Draw(SKCanvas canvas);
    protected abstract void OnButton(string id);

    private string? HitTest(float x, float y)
    {
        foreach (var (id, rect) in _buttons)
            if (rect.Contains(x, y)) return id;
        return null;
    }

    private bool HitTestPixel(int x, int y) => HitTest(x / _scale, y / _scale) is not null;
    private void MovePixel(int x, int y)
    {
        var id = HitTest(x / _scale, y / _scale);
        if (id == _hoverId) return;
        _hoverId = id;
        _dirty = true;
        RenderAndPresent();
    }
    private void Leave()
    {
        if (_hoverId is null) return;
        _hoverId = null;
        _dirty = true;
        RenderAndPresent();
    }
    private void ClickPixel(int x, int y)
    {
        if (HitTest(x / _scale, y / _scale) is { } id) OnButton(id);
    }

    protected void RegisterButton(string id, SKRect rect) => _buttons.Add((id, rect));
    protected bool IsHovered(string id) => _hoverId == id;

    protected static SKPaint TextPaint(SKColor color, float size, bool bold = false) => new()
    {
        Color = color, IsAntialias = true,
        Typeface = bold ? TypefaceBold : TypefaceNormal, TextSize = size
    };

    protected static void DrawCard(SKCanvas canvas, float width, float height)
    {
        using var fill = new SKPaint { Color = CardFill, IsAntialias = true };
        using var border = new SKPaint { Color = CardBorder, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        var rect = new SKRect(0.5f, 0.5f, width - 0.5f, height - 0.5f);
        canvas.DrawRoundRect(rect, CardRadius, CardRadius, fill);
        canvas.DrawRoundRect(rect, CardRadius, CardRadius, border);
    }

    protected static void DrawText(SKCanvas canvas, string text, SKRect rect, SKPaint paint)
    {
        if (string.IsNullOrEmpty(text) || rect.Width <= 1) return;
        var measured = paint.MeasureText(text);
        if (measured > rect.Width)
        {
            const string ellipsis = "…";
            var low = 0;
            var high = text.Length;
            while (low < high)
            {
                var mid = (low + high + 1) / 2;
                if (paint.MeasureText(text[..mid] + ellipsis) <= rect.Width) low = mid;
                else high = mid - 1;
            }
            text = low > 0 ? text[..low] + ellipsis : ellipsis;
        }
        canvas.Save();
        canvas.ClipRect(rect);
        var metrics = paint.FontMetrics;
        var baseline = rect.MidY - (metrics.Ascent + metrics.Descent) / 2;
        canvas.DrawText(text, rect.Left, baseline, paint);
        canvas.Restore();
    }

    protected void DrawButton(SKCanvas canvas, string id, SKRect rect, string text, SKColor fill, SKColor hoverFill, float fontSize)
    {
        using var background = new SKPaint { Color = IsHovered(id) ? hoverFill : fill, IsAntialias = true };
        canvas.DrawRoundRect(rect, 8, 8, background);
        using var label = TextPaint(SKColors.White, fontSize, bold: true);
        var width = label.MeasureText(text);
        var metrics = label.FontMetrics;
        var baseline = rect.MidY - (metrics.Ascent + metrics.Descent) / 2;
        canvas.DrawText(text, rect.MidX - width / 2, baseline, label);
    }

    public void Dispose()
    {
        _surface?.Dispose();
        _surface = null;
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
}

/// <summary>The top-center "AI is controlling your computer" banner.</summary>
internal sealed class ControlBannerOverlay : SkiaOverlayWindow
{
    internal const string WindowTitle = "AI 电脑控制提示";
    private const float Width = 560, Height = 88;
    internal Action? OnStop;

    internal void Show(IntPtr anchor)
    {
        if (anchor == IntPtr.Zero) return;
        var scale = DpiScale(anchor);
        var area = WorkArea(anchor);
        var width = (int)Math.Round(Width * scale);
        Ensure(WindowTitle, Width, Height, scale);
        var x = area.X + (area.Width - width) / 2;
        var y = area.Y + (int)Math.Round(12 * scale);
        Position(x, y);
        RenderAndPresent();
    }

    protected override void Draw(SKCanvas canvas)
    {
        DrawCard(canvas, Width, Height);

        var tile = new SKRect(16, 16, 56, 56);
        using (var gradient = SKShader.CreateLinearGradient(
                   new SKPoint(tile.Left, tile.Top), new SKPoint(tile.Right, tile.Bottom),
                   [new SKColor(102, 208, 255), new SKColor(41, 118, 214)], null, SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = gradient, IsAntialias = true })
            canvas.DrawRoundRect(tile, 10, 10, paint);

        using (var icon = new SKPaint
               {
                   Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f,
                   StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, IsAntialias = true
               })
        {
            canvas.DrawRoundRect(new SKRect(25, 25, 47, 42), 2.5f, 2.5f, icon);
            canvas.DrawLine(36, 42, 36, 46, icon);
            canvas.DrawLine(31, 46, 41, 46, icon);
        }

        using (var title = TextPaint(TextPrimary, 16, bold: true))
            DrawText(canvas, AgentText.L("ComputerControlling", "AI 正在操作电脑"), new SKRect(70, 19, 390, 40), title);
        using (var hint = TextPaint(TextSecondary, 12))
            DrawText(canvas, AgentText.L("ComputerControlHint", "请暂时避免键鼠操作，可随时停止"), new SKRect(70, 45, 390, 64), hint);

        var stop = new SKRect(432, 26, 544, 62);
        RegisterButton("stop", stop);
        DrawButton(canvas, "stop", stop, AgentText.L("ComputerControlStop", "立即停止"), Danger, DangerHover, 13);
    }

    protected override void OnButton(string id)
    {
        if (id == "stop") OnStop?.Invoke();
    }
}

/// <summary>The top-right live task card below the banner.</summary>
internal sealed class ControlProgressOverlay : SkiaOverlayWindow
{
    internal const string WindowTitle = "AI 任务进度";
    private const float Width = 440;
    private const float Height = 152;
    private string _sessionId = "";
    private string _title = AgentText.L("ComputerProgressTitle", "AI 正在操作电脑");
    private string _phase = "", _detail = "", _target = "", _elapsed = "";
    private bool _canRespond, _caution, _show;
    private float _spin;
    internal Action<string>? OnStopSession;
    internal Action<string>? OnRespond;

    internal void Update(string sessionId, string title, string phase, string detail, string target,
        string elapsed, bool canRespond, bool caution, bool show, IntPtr anchor)
    {
        if (anchor == IntPtr.Zero) return;
        _sessionId = sessionId; _title = title; _phase = phase; _detail = detail;
        _target = target; _elapsed = elapsed; _canRespond = canRespond; _caution = caution;
        _show = show;
        Spinning = show && !caution;
        var scale = DpiScale(anchor);
        var area = WorkArea(anchor);
        Ensure(WindowTitle, Width, Height, scale);
        var x = area.X + area.Width - (int)Math.Round(14 * scale) - (int)Math.Round(Width * scale);
        var y = area.Y + (int)Math.Round(110 * scale);
        Position(x, y);
        if (_show) RenderAndPresent();
        else ClearAndPresent();
    }

    internal void AdvanceSpin()
    {
        if (!_show) return;
        if (Spinning) _spin = (_spin + 24) % 360;
        Advance();
    }

    protected override void Draw(SKCanvas canvas)
    {
        var height = Height;
        DrawCard(canvas, Width, height);

        using (var accent = new SKPaint { Color = _caution ? Caution : Accent, IsAntialias = true })
            canvas.DrawRoundRect(new SKRect(1.5f, 12, 4.5f, height - 12), 1.5f, 1.5f, accent);

        var ring = new SKRect(20, 14, 36, 30);
        using (var track = new SKPaint { Color = WithAlpha(Accent, 48), Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f, IsAntialias = true })
            canvas.DrawCircle(ring.MidX, ring.MidY, 7, track);
        using (var arc = new SKPaint
               {
                   Color = _caution ? Caution : Accent, Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f,
                   StrokeCap = SKStrokeCap.Round, IsAntialias = true
               })
            canvas.DrawArc(new SKRect(ring.MidX - 7, ring.MidY - 7, ring.MidX + 7, ring.MidY + 7), _spin, 250, false, arc);

        using (var title = TextPaint(TextPrimary, 14, bold: true))
            DrawText(canvas, _title, new SKRect(46, 13, 292, 32), title);
        using (var elapsed = TextPaint(TextMuted, 11))
            DrawText(canvas, _elapsed, new SKRect(292, 13, 344, 32), elapsed);

        using (var phase = TextPaint(_caution ? Caution : Accent, 13, bold: true))
            DrawText(canvas, _phase, new SKRect(46, 34, 344, 52), phase);
        if (!string.IsNullOrEmpty(_detail))
            using (var detail = TextPaint(TextSecondary, 12))
                DrawText(canvas, _detail, new SKRect(46, 54, 424, 72), detail);
        using (var target = TextPaint(TextMuted, 11))
            DrawText(canvas, _target, new SKRect(46, 74, 424, 92), target);

        var stop = new SKRect(352, 12, 424, 42);
        RegisterButton("stop", stop);
        DrawButton(canvas, "stop", stop, AgentText.L("ComputerProgressStop", "停止"), Danger, DangerHover, 12);

        if (_canRespond)
        {
            var respond = new SKRect(46, height - 44, 166, height - 12);
            RegisterButton("respond", respond);
            DrawButton(canvas, "respond", respond, AgentText.L("ComputerProgressRespond", "回答 / 审批"), Accent, new SKColor(106, 208, 255), 12);
        }
    }

    protected override void OnButton(string id)
    {
        if (id == "stop") OnStopSession?.Invoke(_sessionId);
        else if (id == "respond") OnRespond?.Invoke(_sessionId);
    }
}
