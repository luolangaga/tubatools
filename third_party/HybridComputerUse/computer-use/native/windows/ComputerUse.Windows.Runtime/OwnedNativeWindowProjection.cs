using System.Drawing;

namespace ComputerUse.Windows.Runtime;

internal sealed record OwnedNativeWindowCandidate(
    long Handle,
    long OwnerHandle,
    int ProcessId,
    bool IndependentWindow,
    Rectangle Bounds,
    uint ThreadId = 0);

internal static class OwnedNativeWindowProjection
{
    internal static long ResolveLogicalHandle(
        IReadOnlyList<OwnedNativeWindowCandidate> candidates,
        long selectedHandle,
        long preferredLogicalHandle = 0) =>
        ResolveLogicalOwner(candidates, selectedHandle, preferredLogicalHandle).Handle;

    internal static LogicalWindowOwner ResolveLogicalOwner(
        IReadOnlyList<OwnedNativeWindowCandidate> candidates,
        long selectedHandle,
        long preferredLogicalHandle = 0)
    {
        // Compatibility input, deliberately not evidence of ownership.
        _ = preferredLogicalHandle;
        var byHandle = candidates.ToDictionary(
            candidate => candidate.Handle);
        if (!byHandle.TryGetValue(selectedHandle, out var selected))
        {
            return new(selectedHandle, true);
        }

        var current = selected;
        var nativeProof = true;
        var visited = new HashSet<long>();
        while (true)
        {
            if (!visited.Add(current.Handle))
            {
                return new(selectedHandle, true);
            }
            if (current.IndependentWindow)
            {
                return new(current.Handle, nativeProof);
            }
            if (current.OwnerHandle == 0)
            {
                var sameThreadWindows = current.ThreadId == 0
                    ? Array.Empty<OwnedNativeWindowCandidate>()
                    : candidates
                        .Where(candidate =>
                            candidate.Handle != current.Handle &&
                            candidate.ProcessId == current.ProcessId &&
                            candidate.ThreadId == current.ThreadId &&
                            candidate.IndependentWindow)
                        .ToArray();
                if (sameThreadWindows.Length == 1)
                {
                    nativeProof = false;
                    current = sameThreadWindows[0];
                    continue;
                }
                var containingWindows = candidates
                    .Where(candidate =>
                        candidate.Handle != current.Handle &&
                        candidate.ProcessId == current.ProcessId &&
                        candidate.IndependentWindow &&
                        Contains(candidate.Bounds, current.Bounds))
                    .ToArray();
                if (containingWindows.Length == 0)
                {
                    return new(current.Handle, nativeProof);
                }
                var smallestArea = containingWindows.Min(
                    candidate => Area(candidate.Bounds));
                var nearestWindows = containingWindows
                    .Where(candidate => Area(candidate.Bounds) == smallestArea)
                    .ToArray();
                if (nearestWindows.Length != 1)
                {
                    return new(current.Handle, nativeProof);
                }
                nativeProof = false;
                current = nearestWindows[0];
                continue;
            }
            if (!byHandle.TryGetValue(current.OwnerHandle, out var owner) ||
                owner.ProcessId != current.ProcessId)
            {
                return new(current.Handle, nativeProof);
            }
            current = owner;
        }
    }

    internal static long? UniqueTransientOwner(
        IReadOnlyList<OwnedNativeWindowCandidate> candidates,
        int processId,
        Rectangle bounds)
    {
        var containing = candidates.Where(candidate =>
            candidate.IndependentWindow &&
            (processId == 0 || candidate.ProcessId == processId) &&
            Contains(candidate.Bounds, bounds)).ToArray();
        if (containing.Length == 0) { return null; }
        var smallest = containing.Min(candidate => Area(candidate.Bounds));
        var nearest = containing.Where(candidate => Area(candidate.Bounds) == smallest).ToArray();
        return nearest.Length == 1 ? nearest[0].Handle : null;
    }

    internal static IReadOnlyList<long> Members(
        IReadOnlyList<OwnedNativeWindowCandidate> candidates,
        long logicalHandle,
        long preferredLogicalHandle = 0)
    {
        return candidates
            .Where(candidate =>
                ResolveLogicalHandle(
                    candidates,
                    candidate.Handle,
                    preferredLogicalHandle) ==
                logicalHandle)
            .Select(candidate => candidate.Handle)
            .ToArray();
    }

    private static bool Contains(Rectangle container, Rectangle transient)
    {
        return container.Width > 0 &&
            container.Height > 0 &&
            transient.Width > 0 &&
            transient.Height > 0 &&
            container.Contains(transient);
    }

    private static long Area(Rectangle bounds)
    {
        return (long)bounds.Width * bounds.Height;
    }
}
