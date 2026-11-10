using System.Drawing;

namespace ComputerUse.Windows.Runtime;

internal sealed record ProjectedUiaRootCandidate(
    int ProcessId,
    long NativeWindowHandle,
    string RuntimeIdentity,
    Rectangle Bounds = default,
    bool IndependentWindow = false,
    bool SpatialOwnershipEligible = false);

internal static class ProjectedUiaRootSelection
{
    internal static bool ShouldTraverseTransientHost(
        int nodeProcessId,
        long nativeWindowHandle,
        int processId,
        IReadOnlySet<long> knownNativeHandles)
    {
        // Selected native windows already have a canonical traversal. Other
        // native windows own separate trees, even in the same process. Neither
        // subtree belongs in the search for otherwise undiscovered popups.
        // Unknown and handleless hosts still need discovery and ownership checks.
        return (nodeProcessId == 0 || nodeProcessId == processId) &&
            (nativeWindowHandle == 0 || !knownNativeHandles.Contains(nativeWindowHandle));
    }

    internal static IReadOnlyList<int> SelectIndices(
        IReadOnlyList<ProjectedUiaRootCandidate> candidates,
        int processId,
        IReadOnlyCollection<long> projectedHandles,
        Rectangle logicalBounds = default,
        IReadOnlyCollection<string>? precedingRuntimeIdentities = null)
    {
        var allowedHandles = projectedHandles.ToHashSet();
        var seenRuntimeIdentities = new HashSet<string>(
            StringComparer.Ordinal);
        if (precedingRuntimeIdentities is not null)
        {
            foreach (var identity in precedingRuntimeIdentities)
            {
                if (identity.Length > 0) { seenRuntimeIdentities.Add(identity); }
            }
        }
        var selected = new List<int>();
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var processOwned =
                candidate.ProcessId == processId ||
                (candidate.ProcessId == 0 &&
                 candidate.SpatialOwnershipEligible);
            if (!processOwned)
            {
                continue;
            }
            var projectedHandle =
                allowedHandles.Contains(candidate.NativeWindowHandle);
            var spatiallyOwnedTransient =
                candidate.SpatialOwnershipEligible &&
                !candidate.IndependentWindow &&
                Contains(logicalBounds, candidate.Bounds);
            if (!projectedHandle && !spatiallyOwnedTransient)
            {
                continue;
            }
            if (!string.IsNullOrEmpty(candidate.RuntimeIdentity) &&
                !seenRuntimeIdentities.Add(candidate.RuntimeIdentity))
            {
                continue;
            }
            selected.Add(index);
        }
        return selected;
    }

    private static bool Contains(Rectangle owner, Rectangle transient)
    {
        return owner.Width > 0 &&
            owner.Height > 0 &&
            transient.Width > 0 &&
            transient.Height > 0 &&
            owner.Contains(transient);
    }
}
