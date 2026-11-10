namespace ComputerUse.Windows.Runtime;

internal static class TraversalAndActivationTests
{
    internal static void Run()
    {
        var siblingThree = new TestSiblingNode("three", null);
        var siblingTwo = new TestSiblingNode("two", siblingThree);
        var siblingOne = new TestSiblingNode("one", siblingTwo);
        var boundedSiblings = BoundedSiblingTraversal.Collect(
            () => siblingOne,
            value => value.Next,
            2);
        if (boundedSiblings.Count != 2 ||
            boundedSiblings[0] != siblingOne ||
            boundedSiblings[1] != siblingTwo)
        {
            throw new InvalidDataException(
                "Sibling traversal did not stop at the remaining node budget.");
        }
        var zeroBudgetRead = false;
        var zeroBudgetSiblings = BoundedSiblingTraversal.Collect<TestSiblingNode>(
            () =>
            {
                zeroBudgetRead = true;
                return siblingOne;
            },
            value => value.Next,
            0);
        if (zeroBudgetRead || zeroBudgetSiblings.Count != 0)
        {
            throw new InvalidDataException(
                "Zero-budget sibling traversal touched the provider tree.");
        }
        var allSiblings = BoundedSiblingTraversal.Collect(
            () => siblingOne,
            value => value.Next,
            8);
        if (allSiblings.Count != 3 || allSiblings[2] != siblingThree)
        {
            throw new InvalidDataException(
                "Sibling traversal did not preserve complete provider order.");
        }
        using (new FlaUI.Core.CacheRequest().Activate())
        {
            if (!FlaUI.Core.CacheRequest.IsCachingActive)
            {
                throw new InvalidDataException(
                    "Focused traversal test did not establish an ambient UIA cache.");
            }
            var cacheActiveDuringTraversal = UiaProvider.RunWithoutUiaCache(
                () => FlaUI.Core.CacheRequest.IsCachingActive);
            if (cacheActiveDuringTraversal)
            {
                throw new InvalidDataException(
                    "ControlView traversal inherited the ambient UIA cache.");
            }
            if (!FlaUI.Core.CacheRequest.IsCachingActive)
            {
                throw new InvalidDataException(
                    "ControlView traversal did not restore the ambient UIA cache.");
            }
        }
        var unsupportedRole = UiaSnapshotNodeReads.RoleOrUnknown(
            () => throw new NotSupportedException("Synthetic unsupported ControlType."));
        if (unsupportedRole != "unknown")
        {
            throw new InvalidDataException(
                "Unsupported snapshot ControlType did not preserve the node.");
        }
        var supportedRole = UiaSnapshotNodeReads.RoleOrUnknown(() => "button");
        if (supportedRole != "button")
        {
            throw new InvalidDataException(
                "Supported snapshot ControlType was not preserved.");
        }
        var unavailablePattern = UiaSnapshotNodeReads.OptionalPattern<object>(
            () => throw new NotSupportedException("Synthetic unsupported pattern."));
        if (unavailablePattern is not null)
        {
            throw new InvalidDataException(
                "Unsupported snapshot pattern was reported as actionable.");
        }

        foreach (var minimized in new[] { false, true })
        foreach (var initiallyForeground in new[] { false, true })
        {
            var activationEvents = new List<string>();
            var foreground = initiallyForeground ? (nint)42 : (nint)7;
            var verified = WindowsForegroundActivationPolicy.PrepareAndCheckForeground(
                42,
                () => { activationEvents.Add("minimized"); return minimized; },
                () => { activationEvents.Add("restore"); foreground = 42; },
                () => { activationEvents.Add("foreground"); return foreground; });
            var expected = minimized
                ? new[] { "minimized", "restore", "foreground" }
                : new[] { "minimized", "foreground" };
            if (!activationEvents.SequenceEqual(expected) ||
                verified != (minimized || initiallyForeground))
            {
                throw new InvalidDataException("Activation changed non-minimized layout or skipped foreground verification.");
            }
        }
    }
}
