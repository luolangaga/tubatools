using System.Drawing;
using System.Globalization;

namespace ComputerUse.Windows.Runtime;

internal enum WindowsVisibilityReadState
{
    Supported,
    Unsupported,
    ReadError,
}

internal sealed class WindowsVisibilityRead
{
    private WindowsVisibilityRead(
        WindowsVisibilityReadState state,
        bool? isOffscreen,
        string? reason = null,
        string? exceptionType = null,
        string? hresult = null)
    {
        State = state;
        IsOffscreen = isOffscreen;
        Reason = reason;
        ExceptionType = exceptionType;
        HResult = hresult;
    }

    internal WindowsVisibilityReadState State { get; }
    internal bool? IsOffscreen { get; }
    internal string? Reason { get; }
    internal string? ExceptionType { get; }
    internal string? HResult { get; }

    // Exactly one client read. Unsupported out-values and provider-side causes
    // hidden by UIA are not evidence of a boolean value or a propagated error.
    internal static WindowsVisibilityRead Read(
        Func<(bool Supported, bool Value)> tryGet)
    {
        ArgumentNullException.ThrowIfNull(tryGet);
        try
        {
            var (supported, value) = tryGet();
            return supported
                ? new(WindowsVisibilityReadState.Supported, value)
                : new(WindowsVisibilityReadState.Unsupported, null, "unsupported");
        }
        catch (Exception error)
        {
            var type = error.GetType().FullName ?? error.GetType().Name;
            return new(
                WindowsVisibilityReadState.ReadError,
                null,
                "read_error",
                type.Length <= 128 ? type : type[..128],
                "0x" + error.HResult.ToString("X8", CultureInfo.InvariantCulture));
        }
    }

    // Compatibility projection only, never permission for coordinate input.
    internal bool? EffectiveVisibility(
        bool hasKeyboardFocus, Rectangle bounds, Rectangle windowBounds)
    {
        return IsOffscreen is bool offscreen
            ? WindowsUiaVisibility.IsEffectivelyVisible(
                offscreen, hasKeyboardFocus, bounds, windowBounds)
            : null;
    }
}
