using ComputerUse.Windows.Runtime;

internal static class CanonicalNodeIdentitySetTests
{
    private sealed record Node(string Object, int Hwnd);

    internal static void Run()
    {
        var comparisons = 0;
        var stable = new CanonicalNodeIdentitySet<Node>((left, right) =>
        {
            comparisons++;
            return left.Object == right.Object;
        });
        Check(stable.TryAdd(new Node("object-a", 10), "runtime:a") &&
            !stable.TryAdd(new Node("object-a-wrapper", 10), "runtime:a") &&
            stable.TryAdd(new Node("object-b", 10), "runtime:b") && comparisons == 0,
            "Equal stable identities deduplicate; distinct stable identities avoid native comparison.");

        var unknown = new CanonicalNodeIdentitySet<Node>(
            (left, right) => left.Object == right.Object);
        Check(unknown.TryAdd(new Node("shared-control-raw-object", 10), string.Empty) &&
            !unknown.TryAdd(new Node("shared-control-raw-object", 10), string.Empty) &&
            unknown.TryAdd(new Node("different-object-same-hwnd", 10), string.Empty),
            "Snapshot-only wrappers deduplicate by element identity, never by shared HWND.");
        foreach (var knownFirst in new[] { false, true })
        {
            var mixed = new CanonicalNodeIdentitySet<Node>(
                (left, right) => left.Object == right.Object);
            var firstId = knownFirst ? "runtime:mixed" : string.Empty;
            var secondId = knownFirst ? string.Empty : "runtime:mixed";
            Check(mixed.TryAdd(new Node("mixed-object", 20), firstId) &&
                !mixed.TryAdd(new Node("mixed-object", 20), secondId),
                "Either side missing identity must compare the underlying element.");
        }
        var bucketed = 0;
        var keyed = new CanonicalNodeIdentitySet<Node>((left, right) =>
        {
            bucketed++;
            return left.Object == right.Object;
        });
        for (var index = 0; index < 50; index++)
        {
            Check(keyed.TryAdd(new Node($"object-{index}", 40), string.Empty, $"button|item {index}"),
                "Distinct unidentified nodes must be accepted.");
        }
        Check(!keyed.TryAdd(new Node("object-7", 40), string.Empty, "button|item 7") && bucketed == 1,
            "Only nodes with equal observed properties need a native comparison.");
        CheckComparisonFailure(string.Empty);
        CheckComparisonFailure("runtime:retry");
    }

    private static void CheckComparisonFailure(string incomingIdentity)
    {
        var fail = false;
        var comparisons = 0;
        var set = new CanonicalNodeIdentitySet<Node>((left, right) =>
        {
            comparisons++;
            if (fail) { throw new InvalidOperationException("provider unavailable"); }
            return left.Object == right.Object;
        });
        Check(set.TryAdd(new Node("prior", 30), string.Empty), "Initial identity must be accepted.");
        fail = true;
        try
        {
            set.TryAdd(new Node("incoming", 30), incomingIdentity);
            throw new InvalidDataException("Comparison exception must propagate.");
        }
        catch (InvalidOperationException error) when (error.Message == "provider unavailable") { }
        fail = false;
        Check(set.TryAdd(new Node("incoming", 30), incomingIdentity) && comparisons == 2,
            "A failed comparison must not publish the pending identity or element.");
        Check(!set.TryAdd(new Node("incoming", 30), incomingIdentity),
            "A successfully retried identity must subsequently deduplicate.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidDataException(message); }
    }
}
