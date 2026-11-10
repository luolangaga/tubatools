using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal static partial class NativeDesktop
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const uint GwOwner = 4;
    private const int SwRestore = 9;
    private const int DwmwaCloaked = 14;
    private const string ApplicationFrameClass = "ApplicationFrameWindow";
    private const string UwpCoreWindowClass = "Windows.UI.Core.CoreWindow";
    private const int PublishedLayoutHistoryLimit = 32;
    private const int WindowActivationDirectTimeoutMilliseconds = 250;
    private const int WindowActivationPollMilliseconds = 25;
    private const int WindowActivationTimeoutMilliseconds = 750;
    private static readonly object PublishedLayoutLock = new();
    private static readonly Dictionary<
        string,
        WindowsDesktopLayoutSnapshot> PublishedLayouts = new(
            StringComparer.Ordinal);
    private static readonly Queue<string> PublishedLayoutOrder = new();

    internal sealed record ScreenFrame(
        int OriginX,
        int OriginY,
        int Width,
        int Height,
        int NativeWidth,
        int NativeHeight,
        string FrameId,
        string PngBase64)
    {
        internal byte[] Pixels { get; init; } = Array.Empty<byte>();
        internal string SampledAt { get; } = DateTimeOffset.UtcNow.ToString("O");
    }

    internal sealed record WindowRecord(
        nint Handle,
        nint OwnerHandle,
        int ProcessId,
        string Title,
        string ClassName,
        Rectangle Bounds,
        bool Active,
        uint Style,
        uint ExtendedStyle,
        uint ThreadId,
        bool Cloaked = false,
        int HostedProcessId = 0)
    {
        internal TopLevelWindow ToTopLevel() => new(
            Handle.ToInt64(),
            ProcessId,
            Title,
            ClassName,
            ExtendedStyle,
            Active,
            Cloaked,
            HostedProcessId);
    }

    internal static nint RootWindow(nint handle) => GetAncestor(handle, 2);
    internal static nint OwnerWindow(nint handle) => GetWindow(handle, GwOwner);
    internal static nint DesktopWindow() => GetDesktopWindow();
    internal static nint ForegroundWindow() => GetForegroundWindow();
    internal static nint WindowAtPoint(Point point) =>
        WindowFromPoint(new NativePoint { X = point.X, Y = point.Y });

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    // Declared here, not beside the input code, so a portable test can
    // supply the native boundary and run the production input methods.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        [In] Input[] inputs,
        int inputSize);

    private static bool TextModifierIsDown() =>
        new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(key => GetAsyncKeyState(key) < 0);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    private static long TextInputTicks() => Environment.TickCount64;
    private static uint TextVirtualKeyScan(uint key) => MapVirtualKeyW(key, 0);

    private static TextFocus? ReadTextFocus()
    {
        var owner = GetForegroundWindow();
        if (owner == 0) return null;
        var ownerThread = GetWindowThreadProcessId(owner, out var ownerProcess);
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (ownerThread == 0 || !GetGUIThreadInfo(ownerThread, ref info) ||
            info.Focus == 0 || !IsWindowUnicode(info.Focus)) return null;
        var focusThread = GetWindowThreadProcessId(info.Focus, out var focusProcess);
        if (focusThread == 0 || ownerProcess == 0 || focusProcess == 0 ||
            GetForegroundWindow() != owner) return null;
        return new(owner, info.Focus, ownerProcess, ownerThread, focusProcess,
            focusThread, RootWindow(info.Focus));
    }

    private static (bool Success, int Error) SendTextMessage(
        nint window, uint message, uint value, uint parameter, uint timeout)
    {
        Marshal.SetLastPInvokeError(0);
        var success = SendMessageTimeoutW(window, message, value,
            unchecked((nint)parameter), 0x23, timeout, out _) != 0;
        return (success, Marshal.GetLastPInvokeError());
    }

    private static string ReadTextControlClass(nint window)
    {
        var name = new StringBuilder(256);
        return RealGetWindowClassW(window, name, (uint)name.Capacity) == 0 ? "" : name.ToString();
    }

    private static bool IsTextControlReadOnly(nint window) =>
        (ReadTextControlStyleFlags(window) & 0x0800) != 0; // ES_READONLY

    private static bool IsTextControlMultiline(nint window) =>
        (ReadTextControlStyleFlags(window) & 0x0004) != 0; // ES_MULTILINE

    private static long ReadTextControlStyleFlags(nint window)
    {
        Marshal.SetLastPInvokeError(0);
        var style = ReadTextControlStyle(window, -16);
        var error = Marshal.GetLastPInvokeError();
        if (style == 0 && error != 0)
            throw new ProviderFailureException("desktop_input_failed", "preparation",
                $"The focused text control's style is unavailable (Win32 error {error}).");
        return style.ToInt64();
    }

    private static (bool Success, int Error) SendTextSelection(nint window, nint buffer, uint timeout)
    {
        Marshal.SetLastPInvokeError(0);
        var success = SendMessageTimeoutW(window, TextSelectionMessage, 1, buffer,
            0x23, timeout, out _) != 0;
        return (success, Marshal.GetLastPInvokeError());
    }

    private static (bool Success, int Error) PostTextWake(TextFocus target)
    {
        // Wake the original receiver even if focus moved after partial input.
        // Never follow the current focus to a different receiver.
        if (GetWindowThreadProcessId(target.Foreground, out var ownerProcess) != target.OwnerThread ||
            ownerProcess != target.OwnerProcess ||
            GetWindowThreadProcessId(target.Focus, out var focusProcess) != target.FocusThread ||
            focusProcess != target.FocusProcess)
            return (false, 1400);
        Marshal.SetLastPInvokeError(0);
        var success = PostMessageW(target.Focus, 0, 0, 0);
        return (success, Marshal.GetLastPInvokeError());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        internal uint Size;
        internal uint Flags;
        internal nint Active;
        internal nint Focus;
        internal nint Capture;
        internal nint MenuOwner;
        internal nint MoveSize;
        internal nint Caret;
        internal Rect CaretRect;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern bool IsWindowUnicode(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RealGetWindowClassW(nint window, StringBuilder className, uint capacity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint ReadTextControlStyle(nint window, int index);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeoutW(nint window, uint message,
        nuint value, nint parameter, uint flags, uint timeout, out nuint result);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessageW(nint window, uint message, nuint value, nint parameter);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint handle, uint flags);

    // A read-only operation enumerates the desktop's windows once; every
    // listing inside the scope filters that sample, so all parts of one
    // observation see the same windows and the same foreground window.
    [ThreadStatic]
    private static IReadOnlyList<WindowRecord>? _operationWindows;

    internal static IDisposable ReadOnlyWindowScope()
    {
        var previous = _operationWindows;
        _operationWindows = EnumerateWindows(null);
        return new WindowScope(() => _operationWindows = previous);
    }

    // A retry inside the scope must see windows that retired meanwhile.
    internal static void RefreshReadOnlyWindowScope()
    {
        if (_operationWindows is not null)
        {
            _operationWindows = EnumerateWindows(null);
        }
    }

    private sealed class WindowScope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    internal static IReadOnlyList<WindowRecord> ListWindows(int? processId = null)
    {
        var sample = _operationWindows;
        if (sample is not null)
        {
            return processId.HasValue
                ? sample.Where(window => window.ProcessId == processId.Value).ToArray()
                : sample;
        }
        return EnumerateWindows(processId);
    }

    private static IReadOnlyList<WindowRecord> EnumerateWindows(int? processId)
    {
        var windows = new List<WindowRecord>();
        var foreground = GetForegroundWindow();
        EnumWindows(
            (handle, parameter) =>
            {
                if (!IsWindowVisible(handle) || !GetWindowRect(handle, out var rect))
                {
                    return true;
                }
                _ = parameter;
                var threadId = GetWindowThreadProcessId(handle, out var pid);
                if (pid == 0 || (processId.HasValue && pid != processId.Value))
                {
                    return true;
                }
                var bounds = Rectangle.FromLTRB(
                    rect.Left,
                    rect.Top,
                    rect.Right,
                    rect.Bottom);
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    return true;
                }
                var length = GetWindowTextLength(handle);
                var title = new StringBuilder(Math.Max(1, length + 1));
                _ = GetWindowText(handle, title, title.Capacity);
                var className = new StringBuilder(256);
                _ = GetClassName(handle, className, className.Capacity);
                var cloaked = DwmGetWindowAttribute(
                        handle,
                        DwmwaCloaked,
                        out var cloakedReason,
                        sizeof(int)) == 0 &&
                    cloakedReason != 0;
                windows.Add(new WindowRecord(
                    handle,
                    GetWindow(handle, GwOwner),
                    unchecked((int)pid),
                    title.ToString(),
                    className.ToString(),
                    bounds,
                    handle == foreground,
                    unchecked((uint)GetWindowLongPtr(handle, GwlStyle).ToInt64()),
                    unchecked((uint)GetWindowLongPtr(
                        handle,
                        GwlExStyle).ToInt64()),
                    threadId,
                    cloaked,
                    HostedProcessId(handle, className.ToString())));
                return true;
            },
            0);
        return windows;
    }

    // A UWP app (Settings, Calculator) draws in a CoreWindow that its own
    // process owns, inside a frame that ApplicationFrameHost owns. The
    // CoreWindow's process is the application the user sees.
    private static int HostedProcessId(nint frame, string className)
    {
        if (!string.Equals(
                className,
                ApplicationFrameClass,
                StringComparison.Ordinal))
        {
            return 0;
        }
        var core = FindWindowEx(frame, 0, UwpCoreWindowClass, null);
        if (core == 0)
        {
            return 0;
        }
        _ = GetWindowThreadProcessId(core, out var processId);
        return processId is > 0 and <= int.MaxValue ? (int)processId : 0;
    }

    // IsWindow sees hidden and zero-size windows that ListWindows leaves out.
    internal static bool WindowExists(nint handle) => handle != 0 && IsWindow(handle);

    // Returns when the foreground process has no pending input, or after the
    // timeout. A process without a message loop returns at once.
    internal static void WaitForForegroundInputIdle(int timeoutMilliseconds)
    {
        try
        {
            if (!TryGetWindowProcessId(GetForegroundWindow(), out var processId))
            {
                return;
            }
            using var process = Process.GetProcessById(processId);
            _ = process.WaitForInputIdle(timeoutMilliseconds);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            // Console and exited processes have no input queue to wait for.
        }
    }

    internal static bool TryGetWindowProcessId(
        nint handle,
        out int processId)
    {
        processId = 0;
        if (handle == 0 || !IsWindow(handle))
        {
            return false;
        }
        _ = GetWindowThreadProcessId(handle, out var nativeProcessId);
        if (nativeProcessId == 0 || nativeProcessId > int.MaxValue)
        {
            return false;
        }
        processId = (int)nativeProcessId;
        return true;
    }

    internal static void ActivateWindow(nint handle)
    {
        if (handle == 0 || !IsWindow(handle))
        {
            throw new ProviderFailureException(
                "window_target_stale",
                "precondition",
                "The exact native window no longer exists.");
        }

        if (WindowsForegroundActivationPolicy.PrepareAndCheckForeground(
                handle,
                () => IsIconic(handle),
                () => { _ = ShowWindow(handle, SwRestore); },
                GetForegroundWindow))
        {
            return;
        }

        var foregroundBefore = GetForegroundWindow();
        var directAccepted = SetForegroundWindow(handle);
        if (WaitForForeground(
                handle,
                WindowActivationDirectTimeoutMilliseconds))
        {
            return;
        }

        var currentThread = GetCurrentThreadId();
        var foregroundThread = GetWindowThreadProcessId(
            foregroundBefore,
            out var foregroundProcess);
        var targetThread = GetWindowThreadProcessId(
            handle,
            out var targetProcess);
        var attachedForeground = false;
        var attachedTarget = false;
        var bringAccepted = false;
        var retryAccepted = false;
        try
        {
            if (WindowsForegroundActivationPolicy.ShouldAttach(
                    currentThread,
                    foregroundThread))
            {
                attachedForeground = AttachThreadInput(
                    currentThread,
                    foregroundThread,
                    true);
            }
            if (WindowsForegroundActivationPolicy.ShouldAttach(
                    currentThread,
                    targetThread))
            {
                attachedTarget = AttachThreadInput(
                    currentThread,
                    targetThread,
                    true);
            }

            bringAccepted = BringWindowToTop(handle);
            retryAccepted = SetForegroundWindow(handle);
            if (WaitForForeground(
                    handle,
                    WindowActivationTimeoutMilliseconds))
            {
                return;
            }
        }
        finally
        {
            if (attachedTarget)
            {
                _ = AttachThreadInput(currentThread, targetThread, false);
            }
            if (attachedForeground)
            {
                _ = AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        var foregroundAfter = GetForegroundWindow();
        var afterThread = GetWindowThreadProcessId(
            foregroundAfter,
            out var afterProcess);
        Console.Error.WriteLine(
            "window_activation_failed " +
            $"target=0x{handle.ToInt64():x} target_pid={targetProcess} " +
            $"target_tid={targetThread} foreground_before=" +
            $"0x{foregroundBefore.ToInt64():x} foreground_before_pid=" +
            $"{foregroundProcess} foreground_before_tid={foregroundThread} " +
            $"foreground_after=0x{foregroundAfter.ToInt64():x} " +
            $"foreground_after_pid={afterProcess} " +
            $"foreground_after_tid={afterThread} current_tid={currentThread} " +
            $"direct_accepted={directAccepted} " +
            $"attached_foreground={attachedForeground} " +
            $"attached_target={attachedTarget} " +
            $"bring_accepted={bringAccepted} retry_accepted={retryAccepted}");
        throw new ProviderFailureException(
            "window_activation_failed",
            "emission",
            "The exact native window did not become the foreground window; no input was emitted.");
    }

    private static bool WaitForForeground(nint handle, int timeoutMilliseconds)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        do
        {
            if (WindowsForegroundActivationPolicy.IsVerified(
                    handle,
                    GetForegroundWindow()))
            {
                return true;
            }
            Thread.Sleep(WindowActivationPollMilliseconds);
        }
        while (Environment.TickCount64 < deadline);
        return WindowsForegroundActivationPolicy.IsVerified(
            handle,
            GetForegroundWindow());
    }

    internal static JsonObject SnapshotJson(ScreenFrame frame)
    {
        return new JsonObject
        {
            ["app"] = new JsonObject { ["name"] = string.Empty, ["pid"] = 0 },
            ["sampledAt"] = frame.SampledAt,
            ["screenshotPngBase64"] = frame.PngBase64,
            ["screenshotMimeType"] = "image/png",
            ["screenshotEncoding"] = "png",
            ["coordinateSpace"] = "screen_image_px",
            ["frameId"] = frame.FrameId,
            ["width"] = frame.Width,
            ["height"] = frame.Height,
            ["desktopLayout"] = new JsonObject
            {
                ["origin_x"] = frame.OriginX,
                ["origin_y"] = frame.OriginY,
            },
        };
    }

    internal static Rectangle DesktopBounds()
    {
        return new Rectangle(
            GetSystemMetrics(SmXVirtualScreen),
            GetSystemMetrics(SmYVirtualScreen),
            GetSystemMetrics(SmCxVirtualScreen),
            GetSystemMetrics(SmCyVirtualScreen));
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint handle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        nint handle,
        StringBuilder className,
        int count);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(
        nint parent,
        nint childAfter,
        string? className,
        string? windowName);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint handle,
        int attribute,
        out int value,
        int size);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint handle, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint handle, int index);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint handle, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint handle);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint handle);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(
        uint attachThread,
        uint attachToThread,
        bool attach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
