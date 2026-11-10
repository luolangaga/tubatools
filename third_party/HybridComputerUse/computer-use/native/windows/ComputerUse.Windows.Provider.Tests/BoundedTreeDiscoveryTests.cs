using ComputerUse.Windows.Runtime;

internal static class BoundedTreeDiscoveryTests
{
    private sealed class Node(string id, int pid = 7)
    {
        internal string Id = id;
        internal int Pid = pid;
        internal Node? First;
        internal Node? Next;
    }

    internal static void Run()
    {
        CheckWideDeepBudget();
        CheckDepthAndFiltering();
        CheckRawDescendant();
        CheckDeadline();
        CheckCompletionAndZeroBudget();
    }

    private static void CheckWideDeepBudget()
    {
        // Each path node has many siblings. Prefetching each sibling list would
        // multiply reads by depth before reaching the first deep descendant.
        var root = new Node("root");
        var parent = root;
        for (var depth = 0; depth < 20; depth++)
        {
            var first = new Node($"level-{depth}");
            parent.First = first;
            var sibling = first;
            for (var index = 0; index < 500; index++)
            {
                sibling.Next = new Node($"sibling-{depth}-{index}");
                sibling = sibling.Next;
            }
            parent = first;
        }
        const int limit = 80;
        var reads = 0;
        var visited = new List<string>();
        var result = BoundedTreeDiscovery.Visit(
            () => { reads++; return root; },
            node => { reads++; return node.First; },
            node => { reads++; return node.Next; },
            (node, _) => { visited.Add(node.Id); return true; },
            limit, 64, TimeSpan.FromSeconds(10));
        Check(result.AcquiredNodes == limit && visited.Count == limit &&
            result.StopReason == TreeDiscoveryStopReason.NodeBudget && reads <= 2 * limit,
            "Wide/deep discovery must bound actual acquisition without sibling prefetch.");
        Check(visited.Take(21).SequenceEqual(new[] { "root" }.Concat(
            Enumerable.Range(0, 20).Select(index => $"level-{index}"))),
            "Streaming discovery must preserve depth-first order.");
    }

    private static void CheckDepthAndFiltering()
    {
        var root = new Node("root") { First = new Node("hidden") };
        var childReads = 0;
        var result = BoundedTreeDiscovery.Visit(
            () => root,
            node => { childReads++; return node.First; },
            node => node.Next, (_, _) => true,
            10, 1, TimeSpan.FromSeconds(10));
        Check(childReads == 0 && result.AcquiredNodes == 1 && result.DepthLimited,
            "Depth boundary must not issue even one child read.");

        var foreign = new Node("foreign", 8) { First = new Node("foreign-child", 8) };
        var neutral = new Node("neutral", 0) { First = new Node("menu") };
        root.First = foreign;
        foreign.Next = neutral;
        var visited = new List<string>();
        result = BoundedTreeDiscovery.Visit(
            () => root,
            node => {
                Check(node.Pid != 8, "Foreign callback rejection must prevent descent.");
                return node.First;
            }, node => node.Next,
            (node, _) => { visited.Add(node.Id); return node.Pid is 0 or 7; },
            10, 8, TimeSpan.FromSeconds(10));
        Check(result.StopReason == TreeDiscoveryStopReason.Completed &&
            visited.SequenceEqual(new[] { "root", "foreign", "neutral", "menu" }),
            "Rejected nodes consume budget while neutral siblings and children remain discoverable.");
    }

    private static void CheckRawDescendant()
    {
        var root = new Node("known-native-root");
        var wrapper = new Node("raw-only-wrapper", 0);
        var menu = new Node("raw-only-menu");
        root.First = wrapper;
        wrapper.First = menu;
        var visited = new List<string>();
        var result = BoundedTreeDiscovery.Visit(
            () => root, node => node.First, node => node.Next,
            (node, _) => { visited.Add(node.Id); return true; },
            10, 8, TimeSpan.FromSeconds(10));
        Check(result.StopReason == TreeDiscoveryStopReason.Completed &&
            visited.SequenceEqual(new[] { root.Id, wrapper.Id, menu.Id }),
            "Discovery must not prune a known native root's Raw-only descendants.");
    }

    private static void CheckDeadline()
    {
        var now = TimeSpan.Zero;
        var root = new Node("root") { First = new Node("child") };
        var reads = 0;
        var callbacks = 0;
        var result = BoundedTreeDiscovery.Visit(
            () => { reads++; return root; },
            node => { reads++; now = TimeSpan.FromSeconds(2); return node.First; },
            node => { reads++; return node.Next; },
            (_, _) => { callbacks++; return true; },
            10, 8, TimeSpan.FromSeconds(1), () => now);
        Check(result.StopReason == TreeDiscoveryStopReason.TimeBudget &&
            result.AcquiredNodes == 2 && reads == 2 && callbacks == 1,
            "A slow provider read is counted but must not start another read or callback.");
        now = TimeSpan.Zero;
        reads = 0;
        result = BoundedTreeDiscovery.Visit(
            () => { reads++; return root; },
            node => { reads++; return node.First; },
            node => { reads++; return node.Next; },
            (_, _) => { now = TimeSpan.FromSeconds(1); return true; },
            10, 8, TimeSpan.FromSeconds(1), () => now);
        Check(result.StopReason == TreeDiscoveryStopReason.TimeBudget && reads == 1,
            "Callback time counts against the same deadline.");
    }

    private static void CheckCompletionAndZeroBudget()
    {
        var root = new Node("root") { Next = new Node("second-root") };
        var result = BoundedTreeDiscovery.Visit(
            () => root, node => node.First, node => node.Next, (_, _) => true,
            10, 8, TimeSpan.FromSeconds(10));
        Check(result == new BoundedTreeDiscoveryResult(2, TreeDiscoveryStopReason.Completed, false),
            "Completion must include the whole root sibling chain.");
        var reads = 0;
        result = BoundedTreeDiscovery.Visit(
            () => { reads++; return root; }, node => node.First, node => node.Next,
            (_, _) => true, 0, 8, TimeSpan.FromSeconds(10));
        Check(reads == 0 && result.StopReason == TreeDiscoveryStopReason.NodeBudget,
            "A zero node budget must not start root acquisition.");
        result = BoundedTreeDiscovery.Visit(
            () => { reads++; return root; },
            node => { reads++; return node.First; },
            node => { reads++; return node.Next; },
            (_, _) => true, 1, 8, TimeSpan.FromSeconds(10));
        Check(reads == 1 && result.AcquiredNodes == 1 &&
            result.StopReason == TreeDiscoveryStopReason.NodeBudget,
            "An exact node limit must not read a child or sibling to prove completion.");
        reads = 0;
        result = BoundedTreeDiscovery.Visit(
            () => { reads++; return root; }, node => node.First, node => node.Next,
            (_, _) => true, 10, 8, TimeSpan.Zero);
        Check(reads == 0 && result.StopReason == TreeDiscoveryStopReason.TimeBudget,
            "An expired deadline must not start root acquisition.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidDataException(message); }
    }
}
