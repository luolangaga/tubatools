using System.Runtime.InteropServices;
using System.Text;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>一个可见的顶层窗口（供「窗口捕获 / 游戏覆盖层」选择）。</summary>
public readonly struct Win32WindowInfo
{
    public IntPtr Hwnd { get; init; }
    public string Title { get; init; }
    public uint Pid { get; init; }
}

/// <summary>
/// 可见顶层窗口枚举（复用仓库既有的 EnumWindows 写法，供 AI 试炼场「窗口捕获」等使用）。
/// 只读枚举，不做任何窗口操作。
/// </summary>
public static class Win32WindowCatalog
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>列出可见、有标题、非本进程的顶层窗口。</summary>
    public static List<Win32WindowInfo> ListVisibleWindows()
    {
        var list = new List<Win32WindowInfo>();
        var selfPid = (uint)Environment.ProcessId;
        try
        {
            EnumWindows((hwnd, _) =>
            {
                try
                {
                    if (!IsWindowVisible(hwnd)) return true;
                    var len = GetWindowTextLengthW(hwnd);
                    if (len <= 0) return true;
                    var sb = new StringBuilder(len + 2);
                    if (GetWindowTextW(hwnd, sb, sb.Capacity) <= 0) return true;
                    var title = sb.ToString();
                    if (string.IsNullOrWhiteSpace(title)) return true;
                    GetWindowThreadProcessId(hwnd, out var pid);
                    if (pid == selfPid) return true;
                    list.Add(new Win32WindowInfo { Hwnd = hwnd, Title = title, Pid = pid });
                }
                catch { }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        return list;
    }
}
