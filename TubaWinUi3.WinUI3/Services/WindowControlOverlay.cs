using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services;

/// <summary>A click-through outline window kept directly above the inspected HWND.</summary>
internal sealed class WindowControlOverlay : IDisposable
{
    private const string ClassName = "TubaWindowControlOverlay";
    private const int TopmostStyle = 8;
    private const uint NoActivate = 0x10, NoOwnerZOrder = 0x200, NoMoveOrSize = 3;
    private static readonly WindowProc Proc = PaintWindow;
    private static readonly Dictionary<nint, WindowControlOverlay> Instances = [];
    private static ushort _classAtom;
    private readonly nint _hwnd;
    private readonly List<Label> _labels = [];
    private nint _outlines, _highlight, _font;
    private bool _hasShape, _visible, _topmost;
    private WindowManagerService.Rect _position;
    private sealed record Label(Rectangle Bounds, string Text, int Padding, bool Selected);

    public WindowControlOverlay()
    {
        nint module = GetModuleHandle(null);
        if (_classAtom == 0)
        {
            var windowClass = new WindowClass
            {
                Proc = Marshal.GetFunctionPointerForDelegate(Proc), Instance = module, Name = ClassName
            };
            _classAtom = RegisterClass(ref windowClass);
            if (_classAtom == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        const uint layered = 0x80000, transparent = 0x20, toolWindow = 0x80, noActivate = 0x08000000;
        _hwnd = CreateWindowEx(layered | transparent | toolWindow | noActivate, ClassName, "", 0x80000000,
            0, 0, 0, 0, 0, 0, module, 0);
        if (_hwnd == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (!SetLayeredWindowAttributes(_hwnd, 0, 235, 2))
        {
            int error = Marshal.GetLastPInvokeError();
            DestroyWindow(_hwnd);
            throw new Win32Exception(error);
        }
        Instances.Add(_hwnd, this);
    }

    public void SetOutlines(WindowControlTree.Snapshot snapshot, WindowControlTree.Node? selected, bool showAll)
    {
        if (_outlines != 0) DeleteObject(_outlines);
        if (_highlight != 0) DeleteObject(_highlight);
        if (_font != 0) DeleteObject(_font);
        _labels.Clear();
        _outlines = CreateRectRgn(0, 0, 0, 0);
        _highlight = CreateRectRgn(0, 0, 0, 0);
        nint shape = CreateRectRgn(0, 0, 0, 0);
        var frame = snapshot.Window.Bounds;
        var clip = new Rectangle(0, 0, frame.Width, frame.Height);
        if (!_visible)
            SetWindowPos(_hwnd, 0, frame.Left, frame.Top, frame.Width, frame.Height, NoActivate | 4);

        const int union = 2, emptyRegion = 1, textFlags = 0x20 | 0x800; // SINGLELINE | NOPREFIX
        double scale = Math.Max(96, GetDpiForWindow(_hwnd)) / 96.0;
        int labelHeight = (int)Math.Round(19 * scale), padding = (int)Math.Round(6 * scale);
        _font = CreateFont(-(int)Math.Round(11 * scale), 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        nint dc = GetDC(_hwnd);
        nint oldFont = SelectObject(dc, _font);
        try
        {
            if (selected is { Offscreen: false })
                AddControl(selected, true);
            if (showAll)
                AddChildren(snapshot.Root);
        }
        finally
        {
            SelectObject(dc, oldFont);
            ReleaseDC(_hwnd, dc);
        }
        int complexity = CombineRgn(shape, _outlines, _highlight, union);
        foreach (var label in _labels)
        {
            var box = label.Bounds;
            nint tag = CreateRectRgn(box.Left, box.Top, box.Right, box.Bottom);
            complexity = CombineRgn(shape, shape, tag, union);
            DeleteObject(tag);
        }
        _hasShape = complexity > emptyRegion && SetWindowRgn(_hwnd, shape, true) != 0;
        if (!_hasShape)
        {
            DeleteObject(shape);
            Hide();
        }
        else InvalidateRect(_hwnd, 0, false);

        void AddChildren(WindowControlTree.Node node)
        {
            foreach (var child in node.Children)
            {
                if (!child.Offscreen && !ReferenceEquals(child, selected))
                    AddControl(child, false);
                AddChildren(child);
            }
        }

        void AddControl(WindowControlTree.Node node, bool isSelected)
        {
            var rect = node.Bounds;
            if (rect.Width <= 0 || rect.Height <= 0) return;
            rect.Offset(-frame.Left, -frame.Top);
            var outer = Rectangle.Intersect(rect, clip);
            if (outer.Width <= 0 || outer.Height <= 0) return;
            int width = Math.Max(1, (int)Math.Round((isSelected ? 2 : 1) * scale));
            nint ring = CreateRectRgn(outer.Left, outer.Top, outer.Right, outer.Bottom);
            rect.Inflate(-width, -width);
            var inner = Rectangle.Intersect(rect, clip);
            if (inner.Width > 0 && inner.Height > 0)
            {
                nint hole = CreateRectRgn(inner.Left, inner.Top, inner.Right, inner.Bottom);
                CombineRgn(ring, ring, hole, 4); // RGN_DIFF
                DeleteObject(hole);
            }
            nint region = isSelected ? _highlight : _outlines;
            CombineRgn(region, region, ring, union);
            DeleteObject(ring);

            if (!isSelected && outer.Width < 2 * labelHeight) return;
            string name = string.IsNullOrWhiteSpace(node.Name) ? node.Type : node.Name;
            var measured = new WindowManagerService.Rect();
            DrawText(dc, name, -1, ref measured, textFlags | 0x400); // DT_CALCRECT
            int labelWidth = Math.Min(clip.Width, Math.Min(measured.Width + 2 * padding, (int)(220 * scale)));
            int x = Math.Min(outer.Left, clip.Right - labelWidth);
            int y = Math.Clamp(outer.Top - labelHeight, 0, Math.Max(0, clip.Bottom - labelHeight));
            var box = new Rectangle(x, y, labelWidth, Math.Min(labelHeight, clip.Height));
            if (_labels.Any(label => label.Bounds.IntersectsWith(box))) return;
            _labels.Add(new(box, name, padding, isSelected));
        }
    }

    public void UpdatePosition(nint target, WindowManagerService.InspectionState current)
    {
        if (!_hasShape || !current.Visible) { Hide(); return; }
        if (_topmost != current.Topmost)
        {
            if (!SetWindowPos(_hwnd, current.Topmost ? -1 : -2, 0, 0, 0, 0, NoMoveOrSize | NoActivate | NoOwnerZOrder))
                return;
            _topmost = current.Topmost;
        }

        nint previous = GetWindow(target, 3); // GW_HWNDPREV
        var bounds = current.Bounds;
        if (_visible && previous == _hwnd && _position.Equals(bounds)) return;
        uint flags = NoActivate | NoOwnerZOrder | 0x40; // SWP_SHOWWINDOW
        if (previous == _hwnd)
            flags |= 4; // SWP_NOZORDER
        else if (previous == 0 || ((GetWindowLong(previous, -20) & TopmostStyle) != 0) != _topmost)
            previous = _topmost ? -1 : 0;

        if (SetWindowPos(_hwnd, previous, bounds.Left, bounds.Top, bounds.Width, bounds.Height, flags))
        {
            _position = bounds;
            _visible = true;
        }
    }

    public void Hide()
    {
        if (!_visible) return;
        ShowWindow(_hwnd, 0);
        _visible = false;
    }

    public void Dispose()
    {
        if (!Instances.Remove(_hwnd)) return;
        DestroyWindow(_hwnd);
        if (_outlines != 0) DeleteObject(_outlines);
        if (_highlight != 0) DeleteObject(_highlight);
        if (_font != 0) DeleteObject(_font);
    }

    private static nint PaintWindow(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message != 0x000F || !Instances.TryGetValue(hwnd, out var overlay))
            return DefWindowProc(hwnd, message, wParam, lParam);

        nint dc = BeginPaint(hwnd, out var paint);
        PaintRegion(overlay._outlines, 0xBAA246); // teal blue (COLORREF is BGR)
        nint oldFont = SelectObject(dc, overlay._font);
        SetBkMode(dc, 1); // transparent text background
        SetTextColor(dc, 0xFFFFFF);
        foreach (var label in overlay._labels)
            if (!label.Selected) PaintLabel(label, 0x50432C);
        PaintRegion(overlay._highlight, 0xCF9675);
        if (overlay._labels.FirstOrDefault() is { Selected: true } selected)
            PaintLabel(selected, 0x825133);
        SelectObject(dc, oldFont);
        EndPaint(hwnd, ref paint);
        return 0;

        void PaintRegion(nint region, uint color)
        {
            if (region == 0) return;
            nint brush = CreateSolidBrush(color);
            FillRgn(dc, region, brush);
            DeleteObject(brush);
        }

        void PaintLabel(Label label, uint color)
        {
            var box = label.Bounds;
            var rect = new WindowManagerService.Rect { Left = box.Left, Top = box.Top, Right = box.Right, Bottom = box.Bottom };
            nint brush = CreateSolidBrush(color);
            FillRect(dc, ref rect, brush);
            DeleteObject(brush);
            rect.Left += label.Padding;
            rect.Right -= label.Padding;
            const uint flags = 0x20 | 0x4 | 0x800 | 0x8000; // single line, center vertically, literal &, ellipsis
            DrawText(dc, label.Text, -1, ref rect, flags);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public nint Proc;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? Menu, Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct Paint
    {
        public nint Dc;
        public int Erase;
        public WindowManagerService.Rect Bounds;
        public int Restore, IncUpdate;
        public fixed byte Reserved[32];
    }

    private delegate nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClass(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint exStyle, string className, string text, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint BeginPaint(nint hwnd, out Paint paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint hwnd, ref Paint paint);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref WindowManagerService.Rect rect, nint brush);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawText(nint dc, string text, int length, ref WindowManagerService.Rect rect, uint format);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint result, nint first, nint second, int mode);
    [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern bool FillRgn(nint dc, nint region, nint brush);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateFont(int height, int width, int escapement, int orientation,
        int weight, uint italic, uint underline, uint strikeOut, uint charset, uint outputPrecision, uint clipPrecision, uint quality, uint pitch, string face);
}
