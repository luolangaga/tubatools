using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>Exercises real Win32 state transitions on a disposable external window, never user windows.</summary>
public class WindowManagerTests
{
    [Fact]
    public void RejectsInvalidWindows()
    {
        Assert.Throws<InvalidOperationException>(() => WindowManagerService.Read(0));
        Assert.Throws<InvalidOperationException>(() => WindowManagerService.Read(new nint(-1)));
    }

    [Fact]
    [Trait("Category", "DesktopIntegration")]
    public async Task ExternalWindowCanBeChangedHiddenAndRestored()
    {
        string title = "TubaWindowManagerTest-" + Guid.NewGuid().ToString("N");
        string script = "Add-Type -AssemblyName System.Windows.Forms; " +
            "$f = New-Object System.Windows.Forms.Form; $f.Text = '" + title + "'; " +
            "$f.StartPosition = 'Manual'; $f.SetBounds(120,140,480,320); " +
            "$f.Icon = [System.Drawing.SystemIcons]::Information; $f.Add_Shown({ $f.Text = '" + title + "-ready' }); " +
            "[System.Windows.Forms.Application]::Run($f)";
        using var process = Process.Start(new ProcessStartInfo("powershell.exe")
        {
            Arguments = "-NoProfile -STA -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
            UseShellExecute = false, CreateNoWindow = true
        })!;
        try
        {
            nint hwnd = 0;
            Assert.True(SpinWait.SpinUntil(() => (hwnd = FindWindow(null, title + "-ready")) != 0, TimeSpan.FromSeconds(20)), "Test window did not start");
            var original = WindowManagerService.Read(hwnd);
            var icon = await WindowIconService.LoadPngAsync(hwnd, original.Path);
            Assert.NotNull(icon);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, icon.Take(8).ToArray());
            // Reading an icon must not destroy the HICON owned by the other application.
            Assert.Equal(icon, await WindowIconService.LoadPngAsync(hwnd, original.Path));
            int style = GetWindowLong(hwnd, -16);
            Assert.Equal((uint)process.Id, original.ProcessId);
            Assert.Throws<InvalidOperationException>(() => WindowManagerService.Apply(original with { ProcessId = original.ProcessId + 1 }, "hide"));
            Assert.Contains(WindowManagerService.List(), w => w.Handle == hwnd);

            WindowManagerService.Apply(original, "borderless");
            Wait(() => (GetWindowLong(hwnd, -16) & 0x00C40000) == 0);
            WindowManagerService.Apply(original, "topmost");
            Wait(() => WindowManagerService.Read(hwnd).Topmost);
            WindowManagerService.Apply(original, "hide");
            Wait(() => !WindowManagerService.Read(hwnd).Visible);
            Assert.Contains(WindowManagerService.List(), w => w.Handle == hwnd);
            WindowManagerService.Apply(original, "restore");
            Wait(() => WindowManagerService.Read(hwnd).Visible && !WindowManagerService.Read(hwnd).Topmost &&
                (GetWindowLong(hwnd, -16) & 0x00C40000) == (style & 0x00C40000));
            Wait(() => { var r = WindowManagerService.Read(hwnd).Bounds; return r.Left == original.Bounds.Left && r.Top == original.Bounds.Top && r.Right == original.Bounds.Right && r.Bottom == original.Bounds.Bottom; });

            WindowManagerService.Apply(original, "fullscreen");
            Wait(() => (GetWindowLong(hwnd, -16) & 0x00C40000) == 0 && WindowManagerService.Read(hwnd).Bounds.Right - WindowManagerService.Read(hwnd).Bounds.Left > 480);
            WindowManagerService.Apply(original, "restore");
            Wait(() => (GetWindowLong(hwnd, -16) & 0x00C40000) == (style & 0x00C40000));
            WindowManagerService.Apply(original, "maximize");
            Wait(() => IsZoomed(hwnd));
            WindowManagerService.Apply(original, "minimize");
            Wait(() => IsIconic(hwnd));
            WindowManagerService.Apply(original, "normal");
            Wait(() => !IsIconic(hwnd) && !IsZoomed(hwnd));
            WindowManagerService.Apply(original, "restore");
            Wait(() => !IsZoomed(hwnd));
            // Simulate the application maximizing itself before our next operation.
            ShowWindowAsync(hwnd, 3);
            Wait(() => IsZoomed(hwnd));
            WindowManagerService.Apply(original, "borderless");
            Wait(() => !IsZoomed(hwnd));
            WindowManagerService.Apply(original, "restore");
            Wait(() => IsZoomed(hwnd));
            WindowManagerService.Apply(original, "close");
            Assert.True(process.WaitForExit(5000));
            Assert.Throws<InvalidOperationException>(() => WindowManagerService.Apply(original, "show"));
        }
        finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } }
    }
    private static void Wait(Func<bool> predicate) => Assert.True(SpinWait.SpinUntil(predicate, TimeSpan.FromSeconds(5)), "Window state did not change");
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? cls, string title);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint h, int index);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint h);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint h);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint h, int cmd);
}
