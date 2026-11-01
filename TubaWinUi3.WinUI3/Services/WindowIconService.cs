using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services;

internal static class WindowIconService
{
    private static readonly SemaphoreSlim Gate = new(4);

    public static async Task<byte[]?> LoadPngAsync(nint window, string executablePath)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try { return await Task.Run(() => LoadPng(window, executablePath)).ConfigureAwait(false); }
        finally { Gate.Release(); }
    }

    private static byte[]? LoadPng(nint window, string path)
    {
        try
        {
            // Window icons can differ from the executable's icon. Never block on a hung app.
            foreach (int kind in new[] { 1, 0 })
            {
                if (SendMessageTimeout(window, 0x007F, kind, 0, 2 | 0x20, 100, out var icon) != 0 && icon != 0)
                {
                    var png = CopyToPng(icon);
                    if (png is not null) return png;
                }
            }
            foreach (int index in new[] { -14, -34 })
            {
                nint icon = IntPtr.Size == 8 ? GetClassLongPtr(window, index) : (nint)GetClassLong(window, index);
                if (icon != 0 && CopyToPng(icon) is { } png) return png;
            }
            if (File.Exists(path))
            {
                using var icon = Icon.ExtractAssociatedIcon(path);
                if (icon is not null) return Encode(icon);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"窗口图标读取失败：{ex.Message}"); }
        return null;
    }

    private static byte[]? CopyToPng(nint borrowedIcon)
    {
        // Copy first: the original HICON belongs to the target application.
        nint copy = CopyIcon(borrowedIcon);
        if (copy == 0) return null;
        try { using var icon = Icon.FromHandle(copy); return Encode(icon); }
        finally { DestroyIcon(copy); }
    }
    private static byte[] Encode(Icon icon)
    {
        using var bitmap = icon.ToBitmap();
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static extern nint SendMessageTimeout(nint window, uint msg, nint wparam, nint lparam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern nint GetClassLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "GetClassLongW")] private static extern uint GetClassLong(nint window, int index);
    [DllImport("user32.dll")] private static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
}
