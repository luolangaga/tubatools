using System.Runtime.InteropServices;
using Spectre.Console;

namespace TubaWinUi3.Tui;

internal sealed class ConsoleInput : IDisposable
{
    private const uint ProcessedInput = 0x0001;
    private const uint WindowInput = 0x0008;
    private const uint MouseInput = 0x0010;
    private const uint QuickEditMode = 0x0040;
    private const uint ExtendedFlags = 0x0080;
    private const uint VirtualTerminalInput = 0x0200;
    private readonly IntPtr _handle = GetStdHandle(-10);
    private readonly uint _originalMode;
    private readonly bool _native;
    private readonly bool _mouseReporting;

    public ConsoleInput()
    {
        _native = GetConsoleMode(_handle, out _originalMode)
            && SetConsoleMode(_handle, (_originalMode | ProcessedInput | MouseInput | WindowInput | ExtendedFlags)
                & ~(QuickEditMode | VirtualTerminalInput));
        _mouseReporting = _native && AnsiConsole.Profile.Capabilities.Ansi;
        if (_mouseReporting)
            Console.Write("\u001b[?1000h\u001b[?1006h");
    }

    public bool TryRead(out ConsoleKeyInfo? key, out MouseAction? mouse)
    {
        key = null;
        mouse = null;
        if (!_native)
        {
            if (!Console.KeyAvailable)
                return false;
            key = Console.ReadKey(intercept: true);
            return true;
        }

        while (GetNumberOfConsoleInputEvents(_handle, out var count) && count > 0
            && ReadConsoleInputW(_handle, out var record, 1, out var read) && read == 1)
        {
            if (record.EventType == 1 && record.KeyDown != 0)
            {
                if (record.KeyCode > byte.MaxValue)
                    continue;

                var modifiers = record.ControlKeyState;
                key = new ConsoleKeyInfo((char)record.UnicodeChar, (ConsoleKey)record.KeyCode,
                    (modifiers & 0x10) != 0, (modifiers & 0x03) != 0, (modifiers & 0x0c) != 0);
                return true;
            }

            if (record.EventType != 2)
                continue;

            var x = record.X - Console.WindowLeft;
            var y = record.Y - Console.WindowTop;
            if (record.MouseFlags is 0 or 2 && (record.ButtonState & 1) != 0)
                mouse = new MouseAction(x, y, 0, record.MouseFlags == 2);
            else if (record.MouseFlags == 4)
                mouse = new MouseAction(x, y, Math.Sign(unchecked((short)(record.ButtonState >> 16))), false);
            else
                continue;

            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_mouseReporting)
            Console.Write("\u001b[?1006l\u001b[?1000l");
        if (_native)
            SetConsoleMode(_handle, _originalMode | ExtendedFlags);
    }

    internal readonly record struct MouseAction(int X, int Y, int Wheel, bool DoubleClick);

    [StructLayout(LayoutKind.Explicit, Size = 20)]
    private struct InputRecord
    {
        [FieldOffset(0)] public ushort EventType;
        // KEY_EVENT_RECORD
        [FieldOffset(4)] public int KeyDown;
        [FieldOffset(10)] public ushort KeyCode;
        [FieldOffset(14)] public ushort UnicodeChar;
        [FieldOffset(16)] public uint ControlKeyState;
        // MOUSE_EVENT_RECORD
        [FieldOffset(4)] public short X;
        [FieldOffset(6)] public short Y;
        [FieldOffset(8)] public uint ButtonState;
        [FieldOffset(16)] public uint MouseFlags;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);

    [DllImport("kernel32.dll")]
    private static extern bool GetNumberOfConsoleInputEvents(IntPtr handle, out uint count);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern bool ReadConsoleInputW(IntPtr handle, out InputRecord record, uint length, out uint read);

    [DllImport("user32.dll")]
    internal static extern uint GetDoubleClickTime();
}
