using System.Drawing;
using System.Runtime.InteropServices;

namespace ComputerUse.Windows.Runtime;

internal static partial class NativeDesktop
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseXDown = 0x0080;
    private const uint MouseXUp = 0x0100;
    private const uint MouseWheel = 0x0800;
    private const uint MouseHWheel = 0x01000;
    private const uint MouseAbsolute = 0x8000;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint XButton1 = 0x0001;
    private const uint XButton2 = 0x0002;
    private const int KeyChordHoldMilliseconds = 20;

    internal static void MovePointer(double x, double y)
    {
        var bounds = DesktopBounds();
        ValidatePointerCoordinate(x, y, bounds);
        var normalizedX = Normalize(x, bounds.Left, bounds.Width);
        var normalizedY = Normalize(y, bounds.Top, bounds.Height);
        SendChecked(
            new Input
            {
                Type = InputMouse,
                Union = new InputUnion
                {
                    Mouse = new MouseInput
                    {
                        Dx = normalizedX,
                        Dy = normalizedY,
                        Flags = MouseMove | MouseAbsolute | MouseVirtualDesk,
                    },
                },
            });
    }

    internal static void Click(double x, double y, string button, int count)
    {
        MovePointer(x, y);
        var (down, up, data) = button.ToLowerInvariant() switch
        {
            "right" => (MouseRightDown, MouseRightUp, 0U),
            "wheel" or "middle" => (MouseMiddleDown, MouseMiddleUp, 0U),
            "back" => (MouseXDown, MouseXUp, XButton1),
            "forward" => (MouseXDown, MouseXUp, XButton2),
            _ => (MouseLeftDown, MouseLeftUp, 0U),
        };
        var inputs = new List<Input>(Math.Max(1, count) * 2);
        for (var index = 0; index < Math.Max(1, count); index++)
        {
            inputs.Add(MouseFlagInput(down, data));
            inputs.Add(MouseFlagInput(up, data));
        }
        try
        {
            SendChecked(inputs);
        }
        catch (ProviderFailureException error)
        {
            if (error.InputEventsEmitted)
            {
                // A partial send can leave the button down. Release it and
                // keep the original failure as the primary evidence.
                try
                {
                    SendMouseFlag(up, data);
                }
                catch (ProviderFailureException releaseError)
                {
                    throw new ProviderFailureException(
                        error.Code,
                        error.Phase,
                        $"{error.Message} Mouse-button release also failed: " +
                            releaseError.Message,
                        true,
                        true);
                }
            }
            throw WithPriorInput(error);
        }
    }

    internal static void Drag(IReadOnlyList<PointF> path)
    {
        if (path.Count < 2)
        {
            throw new InvalidOperationException("drag_path_requires_two_points");
        }
        // Reject the whole path before the button goes down.
        var bounds = DesktopBounds();
        foreach (var point in path)
        {
            ValidatePointerCoordinate(point.X, point.Y, bounds);
        }
        MovePointer(path[0].X, path[0].Y);
        Thread.Sleep(80);
        var buttonDown = false;
        ProviderFailureException? operationFailure = null;
        try
        {
            SendMouseFlag(MouseLeftDown);
            buttonDown = true;
            Thread.Sleep(180);
            foreach (var point in path.Skip(1))
            {
                MovePointer(point.X, point.Y);
                Thread.Sleep(12);
            }
            Thread.Sleep(100);
        }
        catch (ProviderFailureException error)
        {
            operationFailure = WithPriorInput(
                error,
                mutationUnknown: buttonDown || error.MutationUnknown);
        }
        catch (Exception error)
        {
            // A later point can fail validation after the button went down.
            // Earlier input stays visible; the finally block releases it.
            operationFailure = new ProviderFailureException(
                "desktop_input_failed",
                "execution",
                error.Message,
                true,
                true);
        }
        finally
        {
            if (buttonDown)
            {
                try
                {
                    SendMouseFlag(MouseLeftUp);
                }
                catch (ProviderFailureException releaseError)
                {
                    operationFailure = new ProviderFailureException(
                        releaseError.Code,
                        releaseError.Phase,
                        operationFailure is null
                            ? releaseError.Message
                            : $"{operationFailure.Message} Mouse-button release also failed: " +
                                releaseError.Message,
                        true,
                        true);
                }
            }
        }
        if (operationFailure is not null)
        {
            throw operationFailure;
        }
    }

    internal static void Scroll(double x, double y, double deltaX, double deltaY)
    {
        MovePointer(x, y);
        var inputs = new List<Input>(2);
        if (deltaY != 0)
        {
            inputs.Add(
                MouseFlagInput(
                    MouseWheel,
                    unchecked((uint)(int)Math.Round(-deltaY * 120))));
        }
        if (deltaX != 0)
        {
            inputs.Add(
                MouseFlagInput(
                    MouseHWheel,
                    unchecked((uint)(int)Math.Round(deltaX * 120))));
        }
        try
        {
            SendChecked(inputs);
        }
        catch (ProviderFailureException error)
        {
            throw WithPriorInput(error);
        }
    }

    internal static void PressKeys(IReadOnlyList<string> keys)
    {
        var virtualKeys = WindowsKeyMapping.ResolveChord(keys);
        var keyDown = new List<Input>(virtualKeys.Length);
        var keyUp = new List<Input>(virtualKeys.Length);
        foreach (var key in virtualKeys)
        {
            keyDown.Add(VirtualKeyInput(key, false));
        }
        for (var index = virtualKeys.Length - 1; index >= 0; index--)
        {
            keyUp.Add(VirtualKeyInput(virtualKeys[index], true));
        }
        try
        {
            SendChecked(keyDown);
        }
        catch (ProviderFailureException error)
        {
            if (!error.InputEventsEmitted)
            {
                throw;
            }
            _ = ReleaseKeysBestEffort(virtualKeys);
            throw WithPriorInput(error, mutationUnknown: true);
        }
        try
        {
            Thread.Sleep(KeyChordHoldMilliseconds);
            SendChecked(keyUp);
        }
        catch (ProviderFailureException error)
        {
            _ = ReleaseKeysBestEffort(virtualKeys);
            throw WithPriorInput(error, mutationUnknown: true);
        }
    }

    internal static void WithHeldKeys(IReadOnlyList<string> keys, Action action)
    {
        if (keys.Count == 0)
        {
            action();
            return;
        }

        var virtualKeys = WindowsKeyMapping.ResolveChord(keys);
        var keyDown = virtualKeys
            .Select(key => VirtualKeyInput(key, false))
            .ToArray();
        var keyUp = virtualKeys
            .Reverse()
            .Select(key => VirtualKeyInput(key, true))
            .ToArray();
        try
        {
            SendChecked(keyDown);
        }
        catch (ProviderFailureException error)
        {
            if (!error.InputEventsEmitted)
            {
                throw;
            }
            _ = ReleaseKeysBestEffort(virtualKeys);
            throw WithPriorInput(error, mutationUnknown: true);
        }

        Exception? operationError = null;
        try
        {
            action();
        }
        catch (Exception error)
        {
            operationError = error;
        }

        ProviderFailureException? releaseFailure = null;
        try
        {
            SendChecked(keyUp);
        }
        catch (ProviderFailureException error)
        {
            _ = ReleaseKeysBestEffort(virtualKeys);
            releaseFailure = WithPriorInput(error, mutationUnknown: true);
        }

        if (releaseFailure is not null)
        {
            throw new ProviderFailureException(
                releaseFailure.Code,
                releaseFailure.Phase,
                operationError is null
                    ? releaseFailure.Message
                    : $"{operationError.Message} Modifier release also failed: " +
                        releaseFailure.Message,
                true,
                true);
        }
        if (operationError is ProviderFailureException providerFailure)
        {
            throw WithPriorInput(providerFailure);
        }
        if (operationError is not null)
        {
            throw new ProviderFailureException(
                "desktop_input_failed",
                "execution",
                operationError.Message,
                true,
                true);
        }
    }

    private static int Normalize(double value, int origin, int length) =>
        WindowsDesktopImageGeometry.ToSendInputAbsolute(value, origin, length);

    private static void ValidatePointerCoordinate(
        double x,
        double y,
        Rectangle bounds)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            !bounds.Contains((int)Math.Round(x), (int)Math.Round(y)))
        {
            throw new InvalidOperationException("coordinate_out_of_bounds");
        }
    }

    private static Input VirtualKeyInput(ushort key, bool keyUp)
    {
        return new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = key,
                    // Navigation, Delete, Insert and Windows keys need the
                    // extended flag on both edges; without it NumLock state
                    // can turn them into keypad digits.
                    Flags = (keyUp ? KeyEventKeyUp : 0) |
                        (key is >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x5B or 0x5C
                            ? KeyEventExtendedKey : 0),
                },
            },
        };
    }

    private static void SendMouseFlag(uint flag, uint data = 0)
    {
        SendChecked(MouseFlagInput(flag, data));
    }

    private static Input MouseFlagInput(uint flag, uint data = 0)
    {
        return new Input
        {
            Type = InputMouse,
            Union = new InputUnion
            {
                Mouse = new MouseInput { Flags = flag, MouseData = data },
            },
        };
    }

    private static void SendChecked(Input input)
    {
        SendChecked(new[] { input });
    }

    // Counts input attempts, including uncertain character messages. Cached
    // window observations must expire even when delivery cannot be confirmed.
    private static long _inputGeneration;

    internal static long InputGeneration => Interlocked.Read(ref _inputGeneration);

    private static void SendChecked(IReadOnlyList<Input> inputs)
    {
        if (inputs.Count == 0)
        {
            return;
        }
        Interlocked.Increment(ref _inputGeneration);
        var values = inputs.ToArray();
        var sent = SendInput(
            unchecked((uint)values.Length),
            values,
            Marshal.SizeOf<Input>());
        if (sent != unchecked((uint)values.Length))
        {
            var inputEventsEmitted = sent > 0;
            throw new ProviderFailureException(
                inputEventsEmitted
                    ? "send_input_partial"
                    : "send_input_failed",
                "emission",
                $"SendInput failed with Win32 error " +
                $"{Marshal.GetLastWin32Error()}; sent={sent}, " +
                $"requested={values.Length}.",
                inputEventsEmitted,
                inputEventsEmitted);
        }
    }

    private static bool ReleaseKeysBestEffort(IReadOnlyList<ushort> virtualKeys)
    {
        var released = true;
        for (var index = virtualKeys.Count - 1; index >= 0; index--)
        {
            var value = new[] { VirtualKeyInput(virtualKeys[index], true) };
            var sent = SendInput(1, value, Marshal.SizeOf<Input>());
            released = released && sent == 1;
        }
        return released;
    }

    private static ProviderFailureException WithPriorInput(
        ProviderFailureException error,
        bool? mutationUnknown = null)
    {
        return new ProviderFailureException(
            error.Code,
            error.Phase,
            error.Message,
            true,
            mutationUnknown ?? error.MutationUnknown);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        internal uint Type;
        internal InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] internal MouseInput Mouse;
        [FieldOffset(0)] internal KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        internal int Dx;
        internal int Dy;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        internal ushort VirtualKey;
        internal ushort Scan;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }
}
