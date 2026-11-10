using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TubaWinUi3.Services;

/// <summary>Only operates on external top-level application windows. Snapshots survive page navigation.</summary>
internal static class WindowManagerService
{
    internal sealed record WindowInfo(nint Handle, uint ProcessId, uint ThreadId, string Title, string ClassName,
        string ProcessName, string Path, Rect Bounds, bool Visible, bool Topmost)
    {
        public string Label => $"{Title} · {ProcessName} ({ProcessId})";
        public string Details => $"标题：{Title}\n进程：{ProcessName}（PID {ProcessId}）\n类名：{ClassName}\n程序路径：{Path}\n句柄：0x{Handle:X}\n位置：{Bounds.Left}, {Bounds.Top}　大小：{Bounds.Right - Bounds.Left} × {Bounds.Bottom - Bounds.Top}\n状态：{(Visible ? "可见" : "隐藏")} · {(Topmost ? "置顶" : "未置顶")}";
    }
    private sealed record Snapshot(WindowInfo Identity, int Style, int ExStyle, Placement Placement);
    private static readonly Dictionary<nint, Snapshot> Originals = new();
    private const int FrameMask = 0x00C00000 | 0x00040000; // caption + sizing frame
    private const int EdgeMask = 0x00000001 | 0x00000100 | 0x00000200 | 0x00020000;
    private const uint FrameFlags = 0x20 | 0x10 | 0x4000; // frame changed, no activate, async

    public static WindowInfo Read(nint hwnd)
    {
        uint thread = GetWindowThreadProcessId(hwnd, out uint pid);
        var cls = new StringBuilder(256);
        GetClassName(hwnd, cls, cls.Capacity);
        if (!IsWindow(hwnd) || thread == 0 || pid == (uint)Environment.ProcessId ||
            hwnd == GetShellWindow() || hwnd == GetDesktopWindow() || GetAncestor(hwnd, 2) != hwnd ||
            cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            throw new InvalidOperationException("请选择其他应用的窗口，不能选择工具箱自身或系统桌面。");
        var title = new StringBuilder(1024);
        GetWindowText(hwnd, title, title.Capacity);
        string name = "未知进程", path = "无法读取（权限受限或进程已退出）";
        try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; } catch (ArgumentException) { }
        nint process = OpenProcess(0x1000, false, pid);
        if (process != 0)
        {
            try { var b = new StringBuilder(32768); int size = b.Capacity; if (QueryFullProcessImageName(process, 0, b, ref size)) path = b.ToString(); }
            finally { CloseHandle(process); }
        }
        Check(GetWindowRect(hwnd, out var rect));
        return new(hwnd, pid, thread, title.Length == 0 ? "（无标题）" : title.ToString(), cls.ToString(), name, path,
            rect, IsWindowVisible(hwnd), (GetWindowLong(hwnd, -20) & 8) != 0);
    }

    public static IReadOnlyList<WindowInfo> List()
    {
        var list = new List<WindowInfo>();
        EnumWindows((h, _) =>
        {
            if (IsWindowVisible(h) || Originals.ContainsKey(h))
                try { var info = Read(h); if (info.Title != "（无标题）" || Originals.ContainsKey(h)) list.Add(info); } catch { }
            return true;
        }, 0);
        foreach (var h in Originals.Keys.Except(list.Select(w => w.Handle)).ToArray()) Originals.Remove(h);
        return list.OrderBy(w => w.Title).ToList();
    }

    public static nint UnderCursor()
    {
        if (!GetCursorPos(out var p)) return 0;
        return GetAncestor(WindowFromPoint(p), 2);
    }
    public static bool LeftPressed => (GetAsyncKeyState(1) & 0x8000) != 0;
    public static bool EscapePressed => (GetAsyncKeyState(0x1B) & 0x8000) != 0;
    private static WindowInfo Validate(WindowInfo selected)
    {
        var current = Read(selected.Handle);
        if (current.ProcessId != selected.ProcessId || current.ThreadId != selected.ThreadId || current.ClassName != selected.ClassName)
            throw new InvalidOperationException("原窗口已关闭或发生变化，请重新选择。");
        return current;
    }
    private static void Remember(WindowInfo info)
    {
        if (Originals.TryGetValue(info.Handle, out var old) && old.Identity.ProcessId == info.ProcessId && old.Identity.ThreadId == info.ThreadId && old.Identity.ClassName == info.ClassName) return;
        var placement = new Placement { Length = Marshal.SizeOf<Placement>() };
        Check(GetWindowPlacement(info.Handle, ref placement));
        Originals[info.Handle] = new(info, GetWindowLong(info.Handle, -16), GetWindowLong(info.Handle, -20), placement);
    }
    public static void Apply(WindowInfo selected, string action)
    {
        var info = Validate(selected);
        nint h = info.Handle;
        if (action == "close") { Check(PostMessage(h, 0x0010, 0, 0)); return; }
        Remember(info);
        switch (action)
        {
            case "borderless":
            case "fullscreen":
                Normalize(h);
                SetStyle(h, -16, GetWindowLong(h, -16) & ~FrameMask);
                SetStyle(h, -20, GetWindowLong(h, -20) & ~EdgeMask);
                if (action == "fullscreen")
                {
                    var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                    Check(GetMonitorInfo(MonitorFromWindow(h, 2), ref monitor));
                    var r = monitor.Monitor;
                    Check(SetWindowPos(h, 0, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, FrameFlags | 4));
                }
                else Check(SetWindowPos(h, 0, 0, 0, 0, 0, FrameFlags | 7));
                break;
            case "maximize": Check(ShowWindowAsync(h, 3)); break;
            case "minimize": Check(ShowWindowAsync(h, 6)); break;
            case "normal": Normalize(h); Check(ShowWindowAsync(h, 1)); break;
            case "hide": Check(ShowWindowAsync(h, 0)); break;
            case "show": Check(ShowWindowAsync(h, 5)); break;
            case "topmost": Check(SetWindowPos(h, info.Topmost ? -2 : -1, 0, 0, 0, 0, FrameFlags | 3)); break;
            case "restore":
                var saved = Originals[h];
                // Restore only decoration bits: never roll back unrelated live state (visibility/minimized).
                SetStyle(h, -16, (GetWindowLong(h, -16) & ~FrameMask) | (saved.Style & FrameMask));
                SetStyle(h, -20, (GetWindowLong(h, -20) & ~EdgeMask) | (saved.ExStyle & EdgeMask));
                var placement = saved.Placement;
                placement.Flags |= 4; // WPF_ASYNCWINDOWPLACEMENT, avoid hanging on an unresponsive app
                Check(SetWindowPlacement(h, ref placement));
                Check(SetWindowPos(h, saved.Identity.Topmost ? -1 : -2, 0, 0, 0, 0, FrameFlags | 3));
                Check(ShowWindowAsync(h, saved.Identity.Visible ? placement.ShowCmd : 0));
                Originals.Remove(h);
                break;
            default: throw new ArgumentException("未知窗口操作。");
        }
    }
    private static void SetStyle(nint h, int index, int value)
    {
        Marshal.SetLastPInvokeError(0);
        int previous = SetWindowLong(h, index, value);
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    private static void Normalize(nint h)
    {
        // Wait for restore before reading style bits; an asynchronous restore can race with
        // SetWindowLong and accidentally reapply WS_MAXIMIZE to the borderless window.
        for (int i = 0; i < 2 && (IsZoomed(h) || IsIconic(h)); i++)
            if (SendMessageTimeout(h, 0x0112, 0xF120, 0, 2 | 0x20, 1000, out _) == 0)
                throw new InvalidOperationException("目标窗口未响应还原请求，请稍后重试。");
    }
    private static void Check(bool ok) { if (!ok) throw new Win32Exception(Marshal.GetLastPInvokeError()); }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    internal readonly record struct InspectionState(Rect Bounds, uint Dpi, bool Visible, bool Topmost);

    internal static InspectionState? GetInspectionState(WindowInfo target)
    {
        var hwnd = target.Handle;
        if (GetWindowThreadProcessId(hwnd, out uint pid) != target.ThreadId || pid != target.ProcessId ||
            !GetWindowRect(hwnd, out var bounds))
            return null;

        // UI Automation bounds and DWM frame bounds both use physical screen pixels.
        if (DwmGetWindowAttribute(hwnd, 9, out Rect frame, Marshal.SizeOf<Rect>()) == 0 &&
            frame.Width > 0 && frame.Height > 0 && !IsIconic(hwnd))
            bounds = frame;

        bool visible = IsWindowVisible(hwnd) && !IsIconic(hwnd) &&
            (DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) != 0 || cloaked == 0);
        return new(bounds, GetDpiForWindow(hwnd), visible, (GetWindowLong(hwnd, -20) & 8) != 0);
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out Rect value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out int value, int size);
    [StructLayout(LayoutKind.Sequential)] private struct Placement { public int Length, Flags, ShowCmd; public Point Min, Max; public Rect Normal; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    private delegate bool EnumProc(nint h, nint param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, nint param);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint h);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint h);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint h);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)] private static extern nint SendMessageTimeout(nint h, uint msg, nint wparam, nint lparam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint h, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint h, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint h, StringBuilder text, int count);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(nint h, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong(nint h, int index, int value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint h, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ShowWindowAsync(nint h, int cmd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowPlacement(nint h, ref Placement placement);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPlacement(nint h, ref Placement placement);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint h, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(nint h, uint message, nint wparam, nint lparam);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
