using System.Runtime.InteropServices;
using System.Text;

namespace TubaWinUi3.Tui.Services;

internal static class WindowsClipboard
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string title,
        int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);

    public static bool SetText(string text)
    {
        var bytes = Encoding.Unicode.GetBytes((text ?? string.Empty) + '\0');
        var globalMemory = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
        if (globalMemory == IntPtr.Zero)
        {
            return false;
        }

        var lockedMemory = GlobalLock(globalMemory);
        if (lockedMemory == IntPtr.Zero)
        {
            GlobalFree(globalMemory);
            return false;
        }

        Marshal.Copy(bytes, 0, lockedMemory, bytes.Length);
        GlobalUnlock(globalMemory);

        var owner = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0,
            new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (owner == IntPtr.Zero || !OpenClipboard(owner))
        {
            if (owner != IntPtr.Zero) DestroyWindow(owner);
            GlobalFree(globalMemory);
            return false;
        }

        var transferred = false;
        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            transferred = SetClipboardData(CfUnicodeText, globalMemory) != IntPtr.Zero;
            return transferred;
        }
        finally
        {
            CloseClipboard();
            DestroyWindow(owner);
            if (!transferred)
            {
                GlobalFree(globalMemory);
            }
        }
    }

}
