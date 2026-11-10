namespace ComputerUse.Windows.Runtime;

internal sealed record PostActionOwnerIdentity(
    nint Handle,
    int ProcessId,
    string ProcessGeneration,
    long LifecycleGeneration);

internal static class PostActionOwnerTransitionPolicy
{
    internal static PostActionOwnerIdentity? Capture(
        nint ownerHandle,
        Func<nint, int?> resolveProcessId,
        Func<int, string?> resolveProcessGeneration,
        Func<nint, long> resolveLifecycleGeneration)
    {
        ArgumentNullException.ThrowIfNull(resolveProcessId);
        ArgumentNullException.ThrowIfNull(resolveProcessGeneration);
        ArgumentNullException.ThrowIfNull(resolveLifecycleGeneration);
        if (ownerHandle == 0)
        {
            return null;
        }
        try
        {
            var processId = resolveProcessId(ownerHandle);
            if (processId is null || processId <= 0)
            {
                return null;
            }
            var processGeneration = resolveProcessGeneration(processId.Value);
            if (string.IsNullOrEmpty(processGeneration))
            {
                return null;
            }
            return new PostActionOwnerIdentity(
                ownerHandle,
                processId.Value,
                processGeneration,
                resolveLifecycleGeneration(ownerHandle));
        }
        catch
        {
            return null;
        }
    }

    internal static bool Matches(
        PostActionOwnerIdentity? expected,
        nint currentHandle,
        int currentProcessId,
        string currentProcessGeneration,
        long currentLifecycleGeneration)
    {
        return expected is not null &&
            WindowsWindowIdentityPolicy.Matches(
                expected.Handle,
                expected.ProcessId,
                expected.ProcessGeneration,
                expected.LifecycleGeneration,
                currentHandle,
                currentProcessId,
                currentProcessGeneration,
                currentLifecycleGeneration);
    }
}
