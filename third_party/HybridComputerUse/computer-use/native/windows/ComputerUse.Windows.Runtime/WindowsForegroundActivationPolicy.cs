namespace ComputerUse.Windows.Runtime;

internal static class WindowsForegroundActivationPolicy
{
    internal static bool PrepareAndCheckForeground(
        nint target, Func<bool> isMinimized, Action restore,
        Func<nint> foreground)
    {
        if (isMinimized())
        {
            restore();
        }
        return IsVerified(target, foreground());
    }

    internal static bool IsVerified(nint target, nint foreground)
    {
        return target != 0 && foreground == target;
    }

    internal static bool ShouldAttach(uint sourceThread, uint targetThread)
    {
        return sourceThread != 0 &&
            targetThread != 0 &&
            sourceThread != targetThread;
    }
}
