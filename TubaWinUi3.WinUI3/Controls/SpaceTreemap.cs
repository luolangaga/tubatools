using FluentCleaner.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using System.Diagnostics;
using TubaWinUi3.Services;
using TubaWinUi3.Services.SpaceCleanup;
using Windows.Foundation;
using Windows.System;
using Windows.UI.ViewManagement;

namespace TubaWinUi3.Controls;

/// <summary>Display-synchronised navigation from the last painted geometry, with continuous
/// colour/opacity and a moving parent context. Text stays at its native size while zooming.</summary>
public sealed class SpaceTreemap : UserControl
{
    private readonly SKXamlCanvas _canvas = new() { IgnorePixelScaling = true };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly UISettings _uiSettings = new();
    private List<SpaceTile> _tiles = [], _context = [];
    private Dictionary<SpaceNode, MapRect> _from = [], _contextRects = [];
    private readonly Dictionary<SpaceNode, MapRect> _drawn = [];
    private readonly Dictionary<SpaceNode, SKColor> _colors = [];
    private readonly Dictionary<SpaceNode, SKColor> _drawnColors = [];
    private Dictionary<SpaceNode, SKColor> _fromColors = [], _contextColors = [];
    private readonly Dictionary<SpaceNode, double> _drawnOpacity = [];
    private Dictionary<SpaceNode, double> _fromOpacity = [], _contextOpacity = [];
    private readonly Dictionary<SpaceNode, Label> _labels = [];
    private sealed record Label(string Name, float Width, string Size, float SizeWidth);
    private sealed class DrawingResources : IDisposable
    {
        internal readonly SKTypeface Typeface = SKTypeface.FromFamilyName("Microsoft YaHei UI");
        internal readonly SKFont Font;
        internal readonly SKPaint Fill = new() { IsAntialias = true };
        internal readonly SKPaint Stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
        internal readonly SKPaint Text = new() { IsAntialias = true };
        internal DrawingResources() => Font = new SKFont(Typeface, 12);
        public void Dispose() { Font.Dispose(); Typeface.Dispose(); Fill.Dispose(); Stroke.Dispose(); Text.Dispose(); }
    }
    private DrawingResources? _drawing;
    private readonly ToolTip _tooltip = new();
    private SpaceNode? _root, _selected, _hover;
    private MapRect _camera, _contextDestination;
    private double _changed, _duration = .18;
    private bool _zooming, _rendering, _startPending;
    public bool ColorByFileType { get; set; }
    public bool Scanning { get; set; }
    public bool AnimationEnabled { get; set; } = true;
    private bool CanAnimate => AnimationEnabled && _uiSettings.AnimationsEnabled;
    public event Action<SpaceNode>? Selected;
    public event Action<SpaceNode>? ZoomRequested;
    public event Action<SpaceNode, Point>? NodeContextRequested;
    public event Action? UpRequested;

    public SpaceTreemap()
    {
        Content = _canvas; IsTabStop = true;
        AutomationProperties.SetAutomationId(this, "SpaceTreemap");
        AutomationProperties.SetName(this, "磁盘空间树图。方向键选择，Enter 进入目录，Backspace 返回，Shift+F10 打开菜单；右侧文件树提供键盘和读屏操作。");
        ToolTipService.SetToolTip(this, _tooltip);
        _canvas.PaintSurface += Paint;
        Loaded += (_, _) => { _canvas.Invalidate(); if (_startPending || _clock.Elapsed.TotalSeconds - _changed < _duration) StartRendering(); };
        Unloaded += (_, _) => { StopRendering(); _drawing?.Dispose(); _drawing = null; };
        ActualThemeChanged += (_, _) => Refresh();
        PointerMoved += (_, e) =>
        {
            var node = Hit(e.GetCurrentPoint(this).Position);
            if (node == _hover) return; _hover = node;
            _tooltip.Content = node is null ? "" : $"{node.FullPath}\n{ScanResult.FormatBytes(node.Bytes)} · {node.FileCount:N0} 个文件\n{node.Modified:g}";
            _canvas.Invalidate();
        };
        PointerExited += (_, _) => { _hover = null; _canvas.Invalidate(); };
        Tapped += (_, e) => { Focus(FocusState.Pointer); if (Hit(e.GetPosition(this)) is { } node) Select(node); };
        DoubleTapped += (_, e) => { if (Hit(e.GetPosition(this)) is { IsDirectory: true, IsLink: false } node) ZoomRequested?.Invoke(node); };
        RightTapped += (_, e) => { if (Hit(e.GetPosition(this)) is { } node) { Select(node); NodeContextRequested?.Invoke(node, e.GetPosition(this)); e.Handled = true; } };
        KeyDown += OnKeyDown;
    }
    private void StartRendering()
    {
        if (_rendering || !IsLoaded || !CanAnimate) return;
        CompositionTarget.Rendering += RenderFrame; _rendering = true;
    }
    private void StopRendering()
    {
        if (!_rendering) return;
        CompositionTarget.Rendering -= RenderFrame; _rendering = false;
    }
    private void RenderFrame(object? sender, object args)
    {
        _canvas.Invalidate();
        if (!CanAnimate || (!_startPending && _clock.Elapsed.TotalSeconds - _changed >= _duration))
        {
            StopRendering(); _context.Clear(); _contextRects.Clear(); _contextColors.Clear(); _contextOpacity.Clear();
        }
    }
    public void Select(SpaceNode? node) { _selected = node; _canvas.Invalidate(); if (node is not null) Selected?.Invoke(node); }
    public void Reset()
    {
        // Selection/hover also hold a parent chain to the complete scan tree. Release them
        // along with animation snapshots before starting another full-volume scan.
        StopRendering(); _tiles.Clear(); _context.Clear(); _from.Clear(); _contextRects.Clear();
        _drawn.Clear(); _colors.Clear();
        _drawnColors.Clear(); _fromColors.Clear(); _contextColors.Clear(); _labels.Clear();
        _drawnOpacity.Clear(); _fromOpacity.Clear(); _contextOpacity.Clear();
        _root = _selected = _hover = null; _tooltip.Content = null; _zooming = _startPending = false;
        _canvas.Invalidate();
    }
    public void Refresh() { _colors.Clear(); _canvas.Invalidate(); }
    public void Update(List<SpaceTile> tiles, bool zoom = false, SpaceNode? root = null)
    {
        double now = _clock.Elapsed.TotalSeconds;
        _from = new(_drawn); _fromColors = new(_drawnColors); _fromOpacity = new(_drawnOpacity);
        _context.Clear(); _contextRects.Clear(); _contextColors.Clear(); _contextOpacity.Clear(); _colors.Clear();
        _zooming = zoom && root != _root && CanAnimate && ActualWidth > 0 && ActualHeight > 0;
        var viewport = new MapRect(0, 0, ActualWidth, ActualHeight);
        _camera = _contextDestination = viewport;
        if (_zooming)
        {
            if (root is not null && _drawn.TryGetValue(root, out var entered))
            {
                _camera = SpaceMapMotion.Content(entered);
                _contextDestination = SpaceMapMotion.Project(viewport, _camera, viewport);
            }
            else if (tiles.FirstOrDefault(t => t.Node == _root) is { } leaving && leaving.Rect.Width > 0 && leaving.Rect.Height > 0)
            {
                _contextDestination = SpaceMapMotion.Content(leaving.Rect);
                _camera = SpaceMapMotion.Project(viewport, _contextDestination, viewport);
            }
            else _zooming = false;
            if (_zooming)
            {
                _context = _tiles.ToList(); _contextRects = new(_drawn);
                _contextColors = new(_drawnColors); _contextOpacity = new(_drawnOpacity);
                _selected = _hover = null; _tooltip.Content = null;
            }
        }
        _duration = _zooming ? .38 : Scanning ? .18 : .22;
        var visible = tiles.Select(t => t.Node).ToHashSet();
        foreach (var tile in tiles)
            if (!_from.ContainsKey(tile.Node))
            {
                _from[tile.Node] = _zooming ? SpaceMapMotion.Project(tile.Rect, viewport, _camera) : tile.Rect;
                _fromOpacity[tile.Node] = 0;
            }
        _tiles = tiles; _root = root; _changed = now; _startPending = CanAnimate;
        foreach (var key in _labels.Keys.Where(n => !visible.Contains(n) && !_contextRects.ContainsKey(n)).ToArray()) _labels.Remove(key);
        var font = (_drawing ??= new()).Font;
        foreach (var tile in tiles)
        {
            string size = ScanResult.FormatBytes(tile.Bytes);
            if (!_labels.TryGetValue(tile.Node, out var label) || label.Size != size)
            { string name = tile.Node.Name; _labels[tile.Node] = new(name, font.MeasureText(name), size, font.MeasureText(size)); }
        }
        if (CanAnimate) StartRendering(); else StopRendering();
        _canvas.Invalidate();
    }
    private SpaceNode? Hit(Point point)
    { for (int i = _tiles.Count - 1; i >= 0; i--) if (_drawn.TryGetValue(_tiles[i].Node, out var rect) && rect.Contains(point.X, point.Y)) return _tiles[i].Node; return null; }
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var nodes = _tiles.Where(t => t.Depth == 0).Select(t => t.Node).ToList(); int index = _selected is null ? -1 : nodes.IndexOf(_selected);
        if (e.Key is VirtualKey.Right or VirtualKey.Down) { if (nodes.Count > 0) Select(nodes[Math.Min(nodes.Count - 1, index + 1)]); }
        else if (e.Key is VirtualKey.Left or VirtualKey.Up) { if (nodes.Count > 0) Select(nodes[Math.Max(0, index - 1)]); }
        else if (e.Key == VirtualKey.Enter && _selected is { IsDirectory: true, IsLink: false }) ZoomRequested?.Invoke(_selected);
        else if (e.Key == VirtualKey.Back) UpRequested?.Invoke();
        else if (e.Key == VirtualKey.F10 && _selected is not null) NodeContextRequested?.Invoke(_selected, new Point(24, 24));
        else return;
        e.Handled = true;
    }
    private void Paint(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        bool dark = ActualTheme == ElementTheme.Dark, contrast = ThemeColors.IsSystemHighContrast();
        var background = ToSk((Background as SolidColorBrush)?.Color ?? ThemeColors.CardBg);
        var foreground = ToSk((Foreground as SolidColorBrush)?.Color ?? ThemeColors.PrimaryText);
        canvas.Clear(background);
        double now = _clock.Elapsed.TotalSeconds;
        // TreeView/breadcrumb synchronisation can run after Update on the UI thread.
        // Start at the first presented frame so that work never consumes the zoom itself.
        if (_startPending) { _changed = now; _startPending = false; }
        double t = CanAnimate ? Math.Clamp((now - _changed) / _duration, 0, 1) : 1;
        double eased = SpaceMapMotion.Ease(t);
        var drawing = _drawing ??= new();
        var fill = drawing.Fill; var stroke = drawing.Stroke; var font = drawing.Font; var text = drawing.Text;
        var viewport = new MapRect(0, 0, ActualWidth, ActualHeight);
        var contextViewport = SpaceMapMotion.Between(viewport, _contextDestination, eased);
        _drawn.Clear(); _drawnColors.Clear(); _drawnOpacity.Clear();
        foreach (var tile in _tiles)
            _drawn[tile.Node] = SpaceMapMotion.Between(_from.GetValueOrDefault(tile.Node, tile.Rect), tile.Rect, eased);
        void Draw(SpaceTile tile, MapRect rect, double opacity, bool interaction)
        {
            if (interaction) _drawnOpacity[tile.Node] = opacity;
            if (rect.Width < 2 || rect.Height < 2 || opacity <= 0 || rect.X >= ActualWidth || rect.Y >= ActualHeight || rect.X + rect.Width <= 0 || rect.Y + rect.Height <= 0) return;
            var box = new SKRect((float)rect.X + 1, (float)rect.Y + 1, (float)(rect.X + rect.Width) - 1, (float)(rect.Y + rect.Height) - 1);
            canvas.Save();
            if (tile.Depth > 0 && tile.Node.Parent is { } parent &&
                (interaction ? _drawn : _contextRects).TryGetValue(parent, out var parentRect))
            {
                if (!interaction) parentRect = SpaceMapMotion.Project(parentRect, viewport, contextViewport);
                canvas.ClipRect(new SKRect((float)parentRect.X + 2, (float)parentRect.Y + 23, (float)(parentRect.X + parentRect.Width) - 2, (float)(parentRect.Y + parentRect.Height) - 2));
            }
            SKColor color;
            if (!interaction) color = _contextColors.GetValueOrDefault(tile.Node, background);
            else if (!_colors.TryGetValue(tile.Node, out color))
            {
                float lightness = dark ? (tile.Node.IsDirectory ? 23 : 29) : (tile.Node.IsDirectory ? 87 : 82);
                color = SKColor.FromHsl((float)SpaceColorPalette.Hue(tile.Node, ColorByFileType, _root), dark ? 49 : 56, lightness);
                _colors[tile.Node] = color;
            }
            if (interaction)
            {
                if (_zooming && _fromColors.TryGetValue(tile.Node, out var previous)) color = Blend(previous, color, eased);
                _drawnColors[tile.Node] = color;
            }
            bool selected = interaction && tile.Node == _selected, hover = interaction && tile.Node == _hover;
            fill.Color = Alpha(contrast ? background : color, opacity); canvas.DrawRoundRect(box, 2, 2, fill);
            stroke.Color = Alpha(selected || hover || contrast ? foreground : background, opacity * (selected || hover || contrast ? .9 : .7));
            stroke.StrokeWidth = selected ? 2 : 1; canvas.DrawRoundRect(box, 2, 2, stroke);
            if (rect.Width >= 48 && rect.Height >= 20)
            {
                canvas.ClipRect(box); font.Size = 12; text.Color = Alpha(foreground, opacity);
                float available = box.Width - 14;
                if (!_labels.TryGetValue(tile.Node, out var label))
                { string name = tile.Node.Name, size = ScanResult.FormatBytes(tile.Bytes); _labels[tile.Node] = label = new(name, font.MeasureText(name), size, font.MeasureText(size)); }
                if (tile.Node.IsDirectory && box.Width > 160)
                { text.Color = Alpha(foreground, opacity * .74); canvas.DrawText(label.Size, box.Right - label.SizeWidth - 7, box.Top + 16, font, text); available -= label.SizeWidth + 12; }
                text.Color = Alpha(foreground, opacity); canvas.DrawText(label.Width <= available ? label.Name : Fit(label.Name, available, font), box.Left + 7, box.Top + 16, font, text);
                if (!tile.Node.IsDirectory && box.Height > 42) { text.Color = Alpha(foreground, opacity * .74); canvas.DrawText(label.Size, box.Left + 7, box.Top + 34, font, text); }
            }
            canvas.Restore();
        }
        if (_zooming && t < 1)
            foreach (var tile in _context)
                if (!_drawn.ContainsKey(tile.Node) && _contextRects.TryGetValue(tile.Node, out var old))
                    Draw(tile, SpaceMapMotion.Project(old, viewport, contextViewport), (1 - eased) * _contextOpacity.GetValueOrDefault(tile.Node, 1), false);
        foreach (var tile in _tiles)
        {
            double opacity = CanAnimate ? Lerp(_fromOpacity.GetValueOrDefault(tile.Node, 1), 1, eased) : 1;
            Draw(tile, _drawn[tile.Node], opacity, true);
        }
    }
    private static string Fit(string name, float width, SKFont font)
    {
        if (font.MeasureText(name) <= width) return name;
        int low = 0, high = name.Length;
        while (low < high) { int middle = (low + high + 1) / 2; if (font.MeasureText(name[..middle] + "…") <= width) low = middle; else high = middle - 1; }
        if (low > 0 && char.IsHighSurrogate(name[low - 1])) low--;
        return name[..low] + "…";
    }
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    private static SKColor Blend(SKColor from, SKColor to, double t) => new(
        (byte)Lerp(from.Red, to.Red, t), (byte)Lerp(from.Green, to.Green, t), (byte)Lerp(from.Blue, to.Blue, t), (byte)Lerp(from.Alpha, to.Alpha, t));
    private static SKColor Alpha(SKColor color, double opacity) => color.WithAlpha((byte)Math.Clamp(255 * opacity, 0, 255));
    private static SKColor ToSk(Windows.UI.Color color) => new(color.R, color.G, color.B, color.A);
}
