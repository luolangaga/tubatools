using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace TubaWinUi3.Services;

/// <summary>Captures physical keys only while this process is foreground. Never retains typed text.</summary>
internal sealed class KeyboardTestInput : IDisposable
{
    internal const int NumpadEnter = 0xE01C;
    internal const int Copilot = 0xE07E;
    private readonly DispatcherQueue _dispatcher;
    private readonly HookProc _callback;
    private readonly HashSet<int> _held = [];
    private readonly DispatcherQueueTimer _focusWatch;
    private IntPtr _hook;
    private bool _disposed;
    private bool _copilotChord;
    public event Action<int, bool, bool>? KeyChanged;

    public KeyboardTestInput(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _callback = Callback;
        _focusWatch = dispatcher.CreateTimer();
        _focusWatch.Interval = TimeSpan.FromMilliseconds(100);
        _focusWatch.Tick += (_, _) => { if (!IsForeground()) ReleaseAll(); };
        _hook = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法开始键盘检测");
        _focusWatch.Start();
    }

    internal static int ResolveKey(int vk, int scan, bool extended)
    {
        if (extended && scan == 0x7E) return Copilot;
        if (vk == 0x10) return scan == 0x36 ? 0xA1 : 0xA0;
        if (vk == 0x11) return extended ? 0xA3 : 0xA2;
        if (vk == 0x12) return extended ? 0xA5 : 0xA4;
        if (vk == 0x0D && extended) return NumpadEnter;
        if (vk == 0x0C && scan == 0x4C && !extended) return 0x65;
        // NumLock off still identifies the physical numpad, rather than the navigation cluster.
        if (!extended && vk is >= 0x21 and <= 0x2E)
            return scan switch { 0x47 => 0x67, 0x48 => 0x68, 0x49 => 0x69, 0x4B => 0x64,
                0x4C => 0x65, 0x4D => 0x66, 0x4F => 0x61, 0x50 => 0x62, 0x51 => 0x63,
                0x52 => 0x60, 0x53 => 0x6E, _ => vk };
        return vk;
    }

    private static bool IsForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        return pid == Environment.ProcessId;
    }

    private IntPtr Callback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !_disposed && IsForeground())
        {
            var native = Marshal.PtrToStructure<KeyboardData>(data);
            int msg = (int)message;
            bool down = msg is 0x100 or 0x104;
            if (down || msg is 0x101 or 0x105)
            {
                int key = ResolveKey((int)native.Vk, (int)native.Scan, (native.Flags & 1) != 0);
                _dispatcher.TryEnqueue(() => ProcessKey(key, down));
                // The foreground test owns keyboard input. In particular Space/Enter must
                // not invoke whichever toolbar button was last clicked. Mouse commands work normally.
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    private void ProcessKey(int key, bool down)
    {
        if (_disposed) return;
        // Windows Copilot keyboards may emit Win + Shift + F23 rather than a dedicated VK.
        if (key == 0x86 && ((_held.Contains(0x5B) || _held.Contains(0x5C)) &&
            (_held.Contains(0xA0) || _held.Contains(0xA1)) || _copilotChord))
        {
            _copilotChord = down;
            key = Copilot;
        }
        bool repeat = down && _held.Contains(key);
        if (down) _held.Add(key); else _held.Remove(key);
        KeyChanged?.Invoke(key, down, repeat);
    }

    public void ReleaseAll()
    {
        foreach (int key in _held.ToArray()) KeyChanged?.Invoke(key, false, false);
        _held.Clear();
        _copilotChord = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _focusWatch.Stop();
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        ReleaseAll();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData { public uint Vk, Scan, Flags, Time; public UIntPtr ExtraInfo; }
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
}
