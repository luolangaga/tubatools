using System.Diagnostics;

namespace ComputerUse.Windows.Runtime;

internal enum TreeDiscoveryStopReason
{
    Completed,
    NodeBudget,
    TimeBudget,
}

internal readonly record struct BoundedTreeDiscoveryResult(
    int AcquiredNodes,
    TreeDiscoveryStopReason StopReason,
    bool DepthLimited);

internal static class BoundedTreeDiscovery
{
    // Roots have depth one. The callback controls descent, not sibling discovery.
    // A synchronous provider call may exceed the deadline; no further call starts.
    internal static BoundedTreeDiscoveryResult Visit<T>(
        Func<T?> firstRoot,
        Func<T, T?> firstChild,
        Func<T, T?> nextSibling,
        Func<T, int, bool> visit,
        int maxNodes,
        int maxDepth,
        TimeSpan timeout,
        Func<TimeSpan>? elapsed = null) where T : class
    {
        if (maxNodes < 0) { throw new ArgumentOutOfRangeException(nameof(maxNodes)); }
        if (maxDepth < 1) { throw new ArgumentOutOfRangeException(nameof(maxDepth)); }
        if (timeout < TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(timeout)); }
        var watch = Stopwatch.StartNew();
        elapsed ??= () => watch.Elapsed;
        var acquired = 0;
        var stop = TreeDiscoveryStopReason.Completed;
        var depthLimited = false;
        var parents = new Stack<T>();
        var current = Read(firstRoot);
        while (current is not null && CheckTime())
        {
            var descend = visit(current, parents.Count + 1);
            if (!CheckTime()) { break; }
            if (descend && parents.Count + 1 >= maxDepth)
            {
                // We cannot prove this node is a leaf without exceeding the depth.
                depthLimited = true;
                descend = false;
            }
            if (descend)
            {
                var child = Read(() => firstChild(current));
                if (stop != TreeDiscoveryStopReason.Completed) { break; }
                if (child is not null)
                {
                    parents.Push(current);
                    current = child;
                    continue;
                }
            }
            while (true)
            {
                var sibling = Read(() => nextSibling(current));
                if (stop != TreeDiscoveryStopReason.Completed || sibling is not null)
                {
                    current = sibling;
                    break;
                }
                if (!parents.TryPop(out var parent))
                {
                    current = null;
                    break;
                }
                current = parent;
            }
        }
        return new(acquired, stop, depthLimited);

        bool CheckTime()
        {
            if (stop != TreeDiscoveryStopReason.Completed) { return false; }
            if (elapsed() < timeout) { return true; }
            stop = TreeDiscoveryStopReason.TimeBudget;
            return false;
        }

        T? Read(Func<T?> read)
        {
            if (!CheckTime()) { return null; }
            if (acquired >= maxNodes)
            {
                stop = TreeDiscoveryStopReason.NodeBudget;
                return null;
            }
            var node = read();
            if (node is not null) { acquired++; }
            CheckTime();
            return node;
        }
    }
}
