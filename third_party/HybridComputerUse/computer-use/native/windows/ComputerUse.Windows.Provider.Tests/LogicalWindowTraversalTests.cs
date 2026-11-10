using ComputerUse.Windows.Runtime;

internal static class LogicalWindowTraversalTests
{
    private sealed class Node(string id)
    {
        internal readonly string Id = id;
        internal Node? First;
        internal Node? Next;
    }

    internal static void Run()
    {
        CheckBreadthAndPaths();
        CheckRootFairness();
        CheckRootOffset();
        CheckDepthAndOwnership();
        CheckDeadlineIsNotRetried();
        CheckSharedRetryClock();
        CheckOwnershipDeadlinePropagation();
    }

    private static void CheckBreadthAndPaths()
    {
        var table = new Node("table");
        var toolbar = new Node("toolbar") { First = new Node("button") };
        var root = new Node("root") { First = table };
        table.Next = toolbar;
        Node? last = null;
        for (var index = 0; index < 1000; index++)
        {
            var cell = new Node($"cell-{index}");
            if (last is null) { table.First = cell; }
            else { last.Next = cell; }
            last = cell;
        }
        var emitted = new List<string>();
        var gaps = new List<string>();
        var reads = 0;
        LogicalWindowTraversal.Visit(new[] { root },
            new OwnershipProbeBudget(6, TimeSpan.FromSeconds(5)), 96,
            _ => true,
            (node, path, depth) => {
                emitted.Add($"{node.Id}:{string.Join('.', path)}:{depth}");
                return true;
            },
            node => { reads++; return node.First; },
            node => { reads++; return node.Next; }, gaps.Add);
        Check(emitted.SequenceEqual(new[] {
            "root:0:0", "table:0.0:1", "toolbar:0.1:1",
            "cell-0:0.0.0:2", "button:0.1.0:2", "cell-1:0.0.1:2",
        }) && reads <= 12 && gaps.SequenceEqual(new[] { "node_budget" }),
            "A wide table must not hide a sibling toolbar's child or alter canonical paths.");
    }

    private static void CheckRootFairness()
    {
        var first = new Node("first-root") {
            First = new Node("first-child") { Next = new Node("later-child") },
        };
        var second = new Node("second-root") { First = new Node("second-child") };
        var emitted = new List<string>();
        var gaps = new List<string>();
        LogicalWindowTraversal.Visit(new[] { first, second },
            new OwnershipProbeBudget(4, TimeSpan.FromSeconds(5)), 96,
            _ => true,
            (node, path, _) => { emitted.Add(node.Id + ":" + string.Join('.', path)); return true; },
            node => node.First,
            node => {
                Check(node != first && node != second, "Fixed roots must not acquire siblings.");
                return node.Next;
            }, gaps.Add);
        Check(emitted.SequenceEqual(new[] {
            "first-root:0", "second-root:1", "first-child:0.0", "second-child:1.0",
        }) && gaps.SequenceEqual(new[] { "node_budget" }),
            "Each root branch must receive a child before a wide branch receives its next sibling.");
    }

    private static void CheckRootOffset()
    {
        var root = new Node("transient") { First = new Node("item") };
        var paths = new List<string>();
        LogicalWindowTraversal.Visit(new[] { root, new Node("other-transient") },
            new OwnershipProbeBudget(10, TimeSpan.FromSeconds(5)), 96,
            _ => true,
            (_, path, _) => { paths.Add(string.Join('.', path)); return true; },
            node => node.First, node => node.Next, _ => { }, rootIndexOffset: 3);
        Check(paths.SequenceEqual(new[] { "3", "4", "3.0" }),
            "Additional roots must retain their offset in root and descendant paths.");
    }

    private static void CheckDepthAndOwnership()
    {
        var bad = new Node("ambiguous") { First = new Node("unverified") };
        var good = new Node("good") { First = new Node("too-deep") };
        var root = new Node("root") { First = bad };
        bad.Next = good;
        var emitted = new List<string>();
        var gaps = new List<string>();
        var childReads = new List<string>();
        LogicalWindowTraversal.Visit(new[] { root },
            new OwnershipProbeBudget(20, TimeSpan.FromSeconds(5)), 1,
            node => node == bad
                ? throw new WindowOwnershipUnavailableException("retired") : true,
            (node, path, _) => { emitted.Add(node.Id + ":" + string.Join('.', path)); return true; },
            node => { childReads.Add(node.Id); return node.First; },
            node => node.Next, gaps.Add);
        Check(emitted.SequenceEqual(new[] { "root:0", "good:0.1" }) &&
            childReads.SequenceEqual(new[] { "root" }) &&
            gaps.SequenceEqual(new[] { "ownership_unavailable", "depth_budget" }),
            "Ownership failures preserve sibling paths; depth limits prevent child reads.");
    }

    private static void CheckDeadlineIsNotRetried()
    {
        var now = TimeSpan.Zero;
        var root = new Node("root") { First = new Node("late-child") };
        var attempts = 0;
        var emitted = new List<string>();
        var reads = 0;
        ThrowsDeadline(() => WindowOwnershipObservation.Read(() =>
        {
            attempts++;
            LogicalWindowTraversal.Visit(new[] { root },
                new OwnershipProbeBudget(10, TimeSpan.FromSeconds(1), () => now), 96,
                _ => true,
                (node, _, _) => { emitted.Add(node.Id); return true; },
                node => { reads++; now = TimeSpan.FromSeconds(1); return node.First; },
                node => { reads++; return node.Next; }, _ => { });
            return true;
        }));
        Check(attempts == 1 && reads == 1 && emitted.SequenceEqual(new[] { "root" }),
            "Deadline must preserve recorded nodes and escape without a second snapshot.");
    }

    private static void CheckSharedRetryClock()
    {
        var now = TimeSpan.Zero;
        var attempts = 0;
        ThrowsDeadline(() => WindowOwnershipObservation.Read(() =>
        {
            attempts++;
            var budget = new OwnershipProbeBudget(10, TimeSpan.FromSeconds(1), () => now);
            budget.Take();
            if (attempts == 1)
            {
                now = TimeSpan.FromMilliseconds(750);
                throw new WindowOwnershipUnavailableException("retired transient");
            }
            now += TimeSpan.FromMilliseconds(300);
            budget.CheckTime();
            return true;
        }));
        Check(attempts == 2,
            "A real ownership retry uses the remaining shared deadline, not a fresh duration.");
    }

    private static void CheckOwnershipDeadlinePropagation()
    {
        var now = TimeSpan.Zero;
        var budget = new OwnershipProbeBudget(10, TimeSpan.FromSeconds(1), () => now);
        var root = new Node("root");
        var semantic = new SemanticWindowRoots<Node>(new Dictionary<long, long> { [1] = 1 },
            _ => { now = TimeSpan.FromSeconds(1); return root; },
            (left, right) => left == right, budget);
        var boundary = new LogicalWindowBoundary<Node>(node => node.Id,
            semantic.Owner, _ => null, _ => null, budget);
        ThrowsDeadline(() => boundary.Owner(root));
    }

    private static void ThrowsDeadline(Action action)
    {
        try { action(); }
        catch (ObservationDeadlineExceededException) { return; }
        throw new InvalidDataException("Expected a distinct observation deadline exception.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidDataException(message); }
    }
}
