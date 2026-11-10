using ComputerUse.Windows.Runtime;

internal static class ProjectedRootSlotTests
{
    internal static void Run()
    {
        var a = new ProjectedUiaRootCandidate(7, 10, "runtime:A");
        var t1 = new ProjectedUiaRootCandidate(7, 11, "runtime:T1");
        var t2 = new ProjectedUiaRootCandidate(7, 12, "runtime:T2");
        var handles = new long[] { 10, 11, 12 };
        var complete = new[] { a, t1, t2 };
        var full = ProjectedUiaRootSelection.SelectIndices(complete, 7, handles)
            .Select((index, slot) => (complete[index].RuntimeIdentity, Slot: slot)).ToArray();
        var initial = new[] { a };
        var initialIndices = ProjectedUiaRootSelection.SelectIndices(initial, 7, handles);
        var precedingRoots = initialIndices.Select(index => initial[index].RuntimeIdentity).ToArray();
        var transientCandidates = new[] { a, t1, t1, t2 };
        var transientIndices = ProjectedUiaRootSelection.SelectIndices(
            transientCandidates, 7, handles, precedingRuntimeIdentities: precedingRoots);
        var split = initialIndices.Select((index, slot) => (initial[index].RuntimeIdentity, Slot: slot))
            .Concat(transientIndices.Select((index, slot) =>
                (transientCandidates[index].RuntimeIdentity, Slot: initialIndices.Count + slot)))
            .ToArray();
        Check(full.SequenceEqual(split),
            "Initial roots plus seeded transient selection must preserve full-pass root slots.");

        // T1 may already be a canonical descendant of A. Root-slot selection
        // still reserves its position; node deduplication is a separate step.
        var sampled = new CanonicalNodeIdentitySet<ProjectedUiaRootCandidate>(
            (left, right) => left.RuntimeIdentity == right.RuntimeIdentity);
        sampled.TryAdd(a, a.RuntimeIdentity);
        sampled.TryAdd(t1, t1.RuntimeIdentity);
        var emitted = transientIndices.Select((index, slot) => (
            Element: transientCandidates[index], Slot: initialIndices.Count + slot))
            .Where(item => sampled.TryAdd(item.Element, item.Element.RuntimeIdentity))
            .ToArray();
        Check(transientIndices.Select(index => transientCandidates[index].RuntimeIdentity)
                .SequenceEqual(new[] { "runtime:T1", "runtime:T2" }) &&
            emitted.Length == 1 && emitted[0].Element == t2 && emitted[0].Slot == 2,
            "A previously sampled T1 keeps its root slot so T2 remains slot two.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidDataException(message); }
    }
}
