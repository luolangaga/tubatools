namespace ComputerUse.Windows.Runtime;

internal enum WindowLifecycleStatus { Unknown, Alive, Gone }

internal static class WindowsWindowIdentityPolicy
{
    internal static string RecoveryStatus(WindowLifecycleStatus status) => status switch
    {
        WindowLifecycleStatus.Alive => "present",
        WindowLifecycleStatus.Gone => "gone",
        _ => "unknown",
    };

    // Why a bound window is missing from the visible inventory. A hidden or
    // zero-size window leaves the inventory but keeps its native instance.
    internal static string MissingWindowMessage(WindowLifecycleStatus status) => status switch
    {
        WindowLifecycleStatus.Alive =>
            "The exact window still exists but is hidden or has no size, so it cannot be read; no input was emitted.",
        WindowLifecycleStatus.Gone => "The exact window disappeared; no input was emitted.",
        _ => "The exact window is not visible, and whether it still exists could not be verified; no input was emitted.",
    };

    // A stale token or a missing visible-inventory entry is not retirement.
    // The callbacks sample the recorded native instance, including hidden HWNDs.
    internal static WindowLifecycleStatus ReadLifecycle(
        bool bindingKnown,
        Func<bool> nativeWindowExists,
        Func<WindowLifecycleStatus> nativeIdentityStatus)
    {
        if (!bindingKnown) { return WindowLifecycleStatus.Unknown; }
        try
        {
            if (!nativeWindowExists()) { return WindowLifecycleStatus.Gone; }
            return nativeIdentityStatus();
        }
        catch
        {
            return WindowLifecycleStatus.Unknown;
        }
    }

    internal static WindowLifecycleStatus ReadExistingInstance(
        int expectedProcessId, string expectedProcessGeneration, long expectedLifecycleGeneration,
        int currentProcessId, string currentProcessGeneration, long currentLifecycleGeneration)
    {
        if (expectedProcessId <= 0 || currentProcessId <= 0 ||
            string.IsNullOrWhiteSpace(expectedProcessGeneration) ||
            string.IsNullOrWhiteSpace(currentProcessGeneration))
        {
            return WindowLifecycleStatus.Unknown;
        }
        if (expectedProcessId != currentProcessId ||
            !string.Equals(expectedProcessGeneration, currentProcessGeneration, StringComparison.Ordinal))
        {
            return WindowLifecycleStatus.Gone;
        }
        // Create/destroy callbacks are asynchronous. A changed counter alone
        // invalidates the ref, but a delayed create event cannot prove retirement.
        return expectedLifecycleGeneration == currentLifecycleGeneration
            ? WindowLifecycleStatus.Alive : WindowLifecycleStatus.Unknown;
    }

    internal static bool Matches(
        nint expectedHandle,
        int expectedProcessId,
        string expectedProcessGeneration,
        long expectedLifecycleGeneration,
        nint currentHandle,
        int currentProcessId,
        string currentProcessGeneration,
        long currentLifecycleGeneration)
    {
        return expectedHandle != 0 &&
            expectedProcessId > 0 &&
            !string.IsNullOrWhiteSpace(expectedProcessGeneration) &&
            expectedHandle == currentHandle &&
            expectedProcessId == currentProcessId &&
            string.Equals(
                expectedProcessGeneration,
                currentProcessGeneration,
                StringComparison.Ordinal) &&
            expectedLifecycleGeneration == currentLifecycleGeneration;
    }
}
