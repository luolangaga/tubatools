using System.Runtime.InteropServices;
using System.Text;

namespace TubaWinUi3.Services;

/// <summary>
/// 跨权限文件拖放助手：应用以管理员权限运行，explorer.exe 为非管理员，
/// UIPI 默认拦截从高/低权限进程之间拖放文件消息。此助手对目标窗口
/// 放行 WM_DROPFILES / WM_COPYGLOBALDATA 消息过滤，并子类化窗口
/// 拦截 WM_DROPFILES，用 DragQueryFile 取出拖入的文件路径。
/// 支持多个窗口（主窗口与内置工具独立窗口）各自安装，互不干扰。
/// </summary>
public static class Win32DropHelper
{
    /// <summary>文件被拖入任意已安装钩子的窗口时触发（WndProc 线程，订阅者自行调度到 UI 线程）。</summary>
    public static event Action<IReadOnlyList<string>>? FilesDropped;

    /// <summary>Window-scoped handlers can consume a drop before legacy, application-wide subscribers.</summary>
    public sealed class WindowDrop(IntPtr window, IReadOnlyList<string> files)
    {
        public IntPtr Window { get; } = window;
        public IReadOnlyList<string> Files { get; } = files;
        public bool Handled { get; set; }
    }
    public static event Action<WindowDrop>? WindowFilesDropped;
    public sealed class WindowPaste(IntPtr window)
    {
        public IntPtr Window { get; } = window;
        public bool Handled { get; set; }
    }
    public static event Action<WindowPaste>? WindowPasteRequested;

    /// <summary>Opt in to native paste messages (including clipboard-history integrations).</summary>
    public static void EnablePasteMessages(IntPtr hwnd)
    {
        EnsureInstalled(hwnd);
        if (hwnd != IntPtr.Zero) ChangeWindowMessageFilterEx(hwnd, WM_PASTE, MSGFLT_ALLOW, IntPtr.Zero);
    }

    private sealed class WindowHook
    {
        public IntPtr OldWndProc;
        public WndProcDelegate Delegate = null!; // 持有引用防止被 GC 回收导致崩溃
    }

    private static readonly Dictionary<IntPtr, WindowHook> _hooks = new();

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, IntPtr pChangeFilterStruct);

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder? lpszFile, uint cch);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(IntPtr hDrop);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, WndProcDelegate newProc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetWindowLongW(IntPtr hWnd, int nIndex, WndProcDelegate newProc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int GWLP_WNDPROC = -4;
    private const uint WM_DROPFILES = 0x0233;
    private const uint WM_COPYGLOBALDATA = 0x0049;
    private const uint WM_PASTE = 0x0302;
    private const uint MSGFLT_ALLOW = 1;

    /// <summary>对窗口安装拖放钩子（幂等；同一窗口只安装一次）。</summary>
    public static void EnsureInstalled(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || _hooks.ContainsKey(hwnd)) return;

        // 放行 UIPI 消息过滤：WM_DROPFILES 与 WM_COPYGLOBALDATA（文件拖放需要）
        ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES, MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);

        // 接受文件拖放
        DragAcceptFiles(hwnd, true);

        // 子类化窗口以拦截 WM_DROPFILES
        var hook = new WindowHook { Delegate = WndProcSubclass };
        hook.OldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, hook.Delegate);
        _hooks[hwnd] = hook;
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProcDelegate newProc)
        => IntPtr.Size == 8
            ? SetWindowLongPtrW(hWnd, nIndex, newProc)
            : SetWindowLongW(hWnd, nIndex, newProc);

    private static IntPtr WndProcSubclass(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_PASTE)
        {
            try
            {
                var paste = new WindowPaste(hWnd); WindowPasteRequested?.Invoke(paste);
                if (paste.Handled) return IntPtr.Zero;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Paste failed: " + ex.Message); }
        }
        if (msg == WM_DROPFILES)
        {
            HandleWmDropFiles(hWnd, wParam);
            return IntPtr.Zero;
        }
        // 其余消息转发给原始 WndProc（WinUI 3 框架），不能调 DefWindowProc
        return _hooks.TryGetValue(hWnd, out var hook)
            ? CallWindowProcW(hook.OldWndProc, hWnd, msg, wParam, lParam)
            : DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void HandleWmDropFiles(IntPtr window, IntPtr hDrop)
    {
        try
        {
            var count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
            if (count == 0) return;

            var files = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                var needed = DragQueryFile(hDrop, i, null, 0);
                if (needed == 0) continue;
                var sb = new StringBuilder((int)needed + 1);
                DragQueryFile(hDrop, i, sb, (uint)sb.Capacity);
                var path = sb.ToString();
                if (!string.IsNullOrWhiteSpace(path))
                    files.Add(path);
            }

            if (files.Count > 0)
            {
                var drop = new WindowDrop(window, files);
                WindowFilesDropped?.Invoke(drop);
                if (!drop.Handled) FilesDropped?.Invoke(files);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("File drop failed: " + ex.Message); }
        finally
        {
            DragFinish(hDrop);
        }
    }
}
