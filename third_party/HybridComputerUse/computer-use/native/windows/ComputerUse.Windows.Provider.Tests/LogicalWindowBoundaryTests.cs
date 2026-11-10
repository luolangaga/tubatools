using System.Drawing;
using ComputerUse.Windows.Runtime;

internal static class LogicalWindowBoundaryTests
{
    private sealed class Node(string id, long handle = 0, Node? parent = null)
    {
        internal string Id = id;
        internal long Handle = handle;
        internal Node? Parent = parent;
        internal Node? First;
        internal Node? Next;
        internal bool Transient;
        internal Rectangle Bounds;
    }

    private static readonly Rectangle Outer = new(0, 0, 1000, 800);
    private static readonly Rectangle Inner = new(200, 100, 600, 500);

    internal static void Run()
    {
        CheckProjectionUniqueness();
        CheckNativePropertyStates();
        CheckNativeMapping();
        CheckAncestryAndTransient();
        CheckReparentAfterFocus();
        CheckTraversalBudget();
        LogicalWindowTraversalTests.Run();
        CheckAmbiguousCandidateIsolation();
        CheckObservationRetry();
    }

    private static OwnedNativeWindowCandidate Window(
        long handle, Rectangle bounds, uint thread = 3, int pid = 7) =>
        new(handle, 0, pid, true, bounds, thread);

    private static LogicalWindowBoundary<Node> Boundary(
        IReadOnlyList<OwnedNativeWindowCandidate> candidates,
        Func<Node, Node?>? parent = null,
        OwnershipProbeBudget? budget = null)
    {
        var owners = candidates.ToDictionary(
            item => item.Handle,
            item => OwnedNativeWindowProjection.ResolveLogicalHandle(candidates, item.Handle));
        return new LogicalWindowBoundary<Node>(
            node => node.Id,
            node => node.Handle == 0 ? null : owners.GetValueOrDefault(node.Handle, node.Handle),
            parent ?? (node => node.Parent),
            node => node.Transient
                ? OwnedNativeWindowProjection.UniqueTransientOwner(candidates, 7, node.Bounds)
                : null,
            budget ?? new OwnershipProbeBudget(1000, TimeSpan.FromSeconds(3)));
    }

    private static void CheckProjectionUniqueness()
    {
        var a = Window(10, Outer);
        var b = Window(20, Inner);
        var popup = new OwnedNativeWindowCandidate(
            30, 0, 7, false, new Rectangle(300, 200, 50, 50), 3);
        foreach (var candidates in new[] { new[] { a, b, popup }, new[] { popup, b, a } })
        {
            var owner = OwnedNativeWindowProjection.ResolveLogicalHandle(candidates, popup.Handle);
            Check(owner == b.Handle, "Nearest unique geometry resolves an ambiguous GUI thread.");
            foreach (var preferred in new[] { 0L, a.Handle, b.Handle, popup.Handle })
            {
                Check(OwnedNativeWindowProjection.ResolveLogicalHandle(
                    candidates, popup.Handle, preferred) == owner,
                    "Request identity and enumeration order cannot change ownership.");
            }
        }
        var tied = new[] { a, b with { Bounds = Outer }, popup };
        Check(OwnedNativeWindowProjection.ResolveLogicalHandle(tied, popup.Handle, a.Handle)
            == popup.Handle, "Tied ownerless popup must not be absorbed by the requester.");
        Check(OwnedNativeWindowProjection.UniqueTransientOwner(tied, 7, popup.Bounds) is null,
            "A detached handleless popup must not resolve a tie.");
        var explicitlyOwned = popup with { OwnerHandle = a.Handle };
        Check(OwnedNativeWindowProjection.ResolveLogicalHandle(
            new[] { a, b, explicitlyOwned }, popup.Handle, b.Handle) == a.Handle,
            "Native ownership takes precedence over geometry and request.");
        var independent = explicitlyOwned with { IndependentWindow = true };
        Check(OwnedNativeWindowProjection.ResolveLogicalHandle(
            new[] { a, b, independent }, popup.Handle) == popup.Handle,
            "Independent owned dialogs remain separate.");
        var foreign = explicitlyOwned with { ProcessId = 99 };
        Check(OwnedNativeWindowProjection.ResolveLogicalHandle(
            new[] { a, foreign }, popup.Handle) == popup.Handle,
            "Native ownership cannot absorb an independent foreign-process surface.");
    }

    private static void CheckNativeMapping()
    {
        var rootQueries = 0;
        var classifications = 0;
        var childRoot = 10L;
        NativeWindowOwnership Sample() => new(
            new Dictionary<long, long> { [10] = 10, [20] = 20, [30] = 10 },
            handle => { rootQueries++; return handle < 100 ? childRoot : 100; },
            _ => { classifications++; return null; });
        var sample = Sample();
        Check(sample.Owner(10) == 10 && sample.Owner(30) == 10 && rootQueries == 0,
            "Enumerated native roots use the immutable map without GetAncestor.");
        Check(sample.Owner(11) == 10 && sample.Owner(11) == 10 && rootQueries == 1,
            "Repeated child HWND reads share only the current sample's mapping.");
        childRoot = 20;
        Check(Sample().Owner(11) == 20, "A new action sample must detect HWND reparenting.");
        Check(sample.Owner(101) is null && sample.Owner(102) is null && classifications == 1,
            "Unknown transient classification is memoized per native root, not discarded.");
        Throws<WindowOwnershipUnavailableException>(() =>
            new NativeWindowOwnership(new Dictionary<long, long>(), _ => 0, _ => null).Owner(4));
    }

    private static void CheckAncestryAndTransient()
    {
        var candidates = new[] { Window(10, Outer), Window(20, Inner) };
        var desktop = new Node("desktop");
        var a = new Node("a", 10, desktop);
        var b = new Node("b", 20, a);
        var hiddenRawContainer = new Node("hidden", 0, b);
        var control = new Node("control", 0, hiddenRawContainer);
        var boundary = Boundary(candidates);
        Check(boundary.Owner(a) == 10 && boundary.Owner(control) == 20,
            "RawView must expose an independent boundary hidden by ControlView.");
        var local = new Node("local", 0, new Node("virtual", 0, a));
        Check(boundary.Owner(local) == 10, "Virtual ordinary descendants remain canonical.");
        var menuA = new Node("menuA", 0, desktop)
        {
            Transient = true, Bounds = new Rectangle(20, 20, 50, 50),
        };
        var menuB = new Node("menuB", 0, desktop)
        {
            Transient = true, Bounds = new Rectangle(300, 200, 50, 50),
        };
        Check(boundary.Owner(menuA) == 10 && boundary.Owner(menuB) == 20,
            "Spatial transient caching must not assign its owner to the desktop ancestor.");
        Check(boundary.Owner(new Node("menuChild", 0, menuA)) == 10,
            "Detached transient descendants use the same owner as observation.");
        Throws<WindowOwnershipUnavailableException>(() => boundary.Owner(new Node("unknown", 0, desktop)));
        var cycle = new Node("cycle");
        cycle.Parent = cycle;
        Throws<WindowOwnershipUnavailableException>(() => Boundary(candidates).Owner(cycle));
        Throws<WindowOwnershipUnavailableException>(() =>
            Boundary(candidates, _ => throw new InvalidOperationException("UIA read failed")).Owner(local));
        var parentReads = 0;
        var cached = Boundary(candidates, node => { parentReads++; return node.Parent; });
        cached.Owner(local);
        var before = parentReads;
        cached.Owner(local);
        Check(parentReads == before, "Within-sample ancestry memoization avoids repeated provider reads.");
    }

    private static void CheckReparentAfterFocus()
    {
        var candidates = new[] { Window(10, Outer), Window(20, Inner) };
        foreach (var unreadable in new[] { false, true })
        {
            var a = new Node("a", 10);
            var b = new Node("b", 20);
            var original = new Node("same-runtime-id", 0, a);
            Check(Boundary(candidates).Owner(original) == a.Handle, "Preflight starts in A.");
            Node? focused = null;
            var focuses = 0;
            var ownershipChecks = 0;
            var keys = 0;
            var failure = Throws<ProviderFailureException>(() =>
            {
                ExactFocusSettlement.RequestAndVerify(
                    () => { focuses++; original.Parent = b; focused = original; },
                    () => ReferenceEquals(original, focused),
                    verifyOwnership: () =>
                    {
                        ownershipChecks++;
                        var owner = unreadable
                            ? Boundary(candidates, _ => throw new InvalidOperationException("retired ancestry"))
                                .Owner(original)
                            : Boundary(candidates).Owner(original);
                        if (owner != a.Handle)
                        {
                            throw new ProviderFailureException("stale_element_ref", "precondition",
                                "Live ownership differs.");
                        }
                    });
                keys++;
            });
            Check(focuses == 1 && ownershipChecks == 1 && keys == 0 &&
                ReferenceEquals(original, focused), "Same focused object is not sufficient after reparent.");
            Check(failure is
            {
                Code: "focus_verification_failed", Phase: "emission",
                InputEventsEmitted: false, MutationUnknown: true, FactsComplete: false,
                FocusPreparation: FocusPreparationState.RequestAttemptedOutcomeUnknown,
            }, "Post-focus ownership failure is terminal focus uncertainty, never clean preflight.");
            foreach (var intent in new[] { "type", "keypress", "set_value" })
            {
                FocusPreparationTests.CheckEnvelope(failure, intent);
            }
        }
        var checks = 0;
        ExactFocusSettlement.RequestAndVerify(() => { }, () => true,
            verifyOwnership: () => checks++);
        Check(checks == 1, "Stable focus performs exactly one fresh ownership verification.");
    }

    private static void CheckTraversalBudget()
    {
        var root = new Node("root", 10);
        var rejected = new Node("rejected", 20);
        var retained = new Node("retained", 0, root);
        root.First = rejected;
        rejected.Next = retained;
        rejected.First = new Node("must-not-be-read", 20);
        var boundary = Boundary(new[] { Window(10, Outer), Window(20, Inner) });
        var reads = 0;
        var records = new List<string>();
        var gaps = new List<string>();
        var budget = new OwnershipProbeBudget(2, TimeSpan.FromSeconds(3));
        LogicalWindowTraversal.Visit(new[] { root }, budget, 96,
            node => boundary.Owner(node) == 10,
            (node, _, _) => { records.Add(node.Id); return true; },
            node => { reads++; return node.First; },
            node => { reads++; return node.Next; }, gaps.Add);
        Check(budget.Used == 2 && records.SequenceEqual(new[] { root.Id }) &&
            gaps.Contains("node_budget") && reads == 1,
            "Rejected nodes consume budget; traversal neither descends them nor hides truncation.");
        records.Clear();
        gaps.Clear();
        LogicalWindowTraversal.Visit(new[] { root },
            new OwnershipProbeBudget(10, TimeSpan.FromSeconds(3)), 96,
            node => boundary.Owner(node) == 10,
            (node, path, _) => { records.Add(node.Id + ":" + string.Join('.', path)); return true; },
            node => node.First, node => node.Next, gaps.Add);
        Check(records.SequenceEqual(new[] { "root:0", "retained:0.1" }) && gaps.Count == 0,
            "Filtering must preserve canonical sibling indices and following same-window controls.");
        var inspected = new List<string>();
        records.Clear();
        gaps.Clear();
        LogicalWindowTraversal.Visit(new[] { new Node("first"), new Node("second") },
            new OwnershipProbeBudget(1, TimeSpan.FromSeconds(3)), 96,
            node => { inspected.Add(node.Id); return true; },
            (node, _, _) => { records.Add(node.Id); return true; },
            node => node.First, node => node.Next, gaps.Add);
        Check(inspected.SequenceEqual(new[] { "first" }) &&
            records.SequenceEqual(new[] { "first" }) && gaps.Contains("node_budget"),
            "All roots share one budget; a later root cannot bypass it.");

        var deep = new Node("depth0") {
            First = new Node("depth1") { First = new Node("depth2") },
        };
        inspected.Clear();
        records.Clear();
        gaps.Clear();
        LogicalWindowTraversal.Visit(new[] { deep },
            new OwnershipProbeBudget(10, TimeSpan.FromSeconds(3)), 1,
            node => { inspected.Add(node.Id); return true; },
            (node, _, _) => { records.Add(node.Id); return true; },
            node => node.First, node => node.Next, gaps.Add);
        Check(inspected.SequenceEqual(new[] { "depth0", "depth1" }) &&
            records.SequenceEqual(inspected) && gaps.Contains("depth_budget"),
            "Depth limit rejects descendants before ownership reads or emission.");

        var wide = new Node("wide");
        Node? last = null;
        for (var i = 0; i < 1000; i++)
        {
            var item = new Node("reject" + i);
            if (last is null) { wide.First = item; }
            else { last.Next = item; }
            last = item;
        }
        const int limit = 12;
        var rejectedBudget = new OwnershipProbeBudget(limit, TimeSpan.FromSeconds(3));
        var includesCount = 0;
        var rejectedChildReads = 0;
        reads = 0;
        gaps.Clear();
        LogicalWindowTraversal.Visit(new[] { wide }, rejectedBudget, 96,
            node => { includesCount++; return ReferenceEquals(node, wide); },
            (_, _, _) => true,
            node => {
                reads++;
                if (!ReferenceEquals(node, wide)) { rejectedChildReads++; }
                return node.First;
            },
            node => { reads++; return node.Next; }, gaps.Add);
        Check(rejectedBudget.Used == limit && includesCount == limit &&
            rejectedChildReads == 0 && reads <= 2 * limit && gaps.Contains("node_budget"),
            "A rejected sibling flood consumes the same bounded work budget.");
        var elapsed = TimeSpan.Zero;
        var timed = new OwnershipProbeBudget(5, TimeSpan.FromSeconds(1), () => elapsed);
        timed.Take();
        elapsed = TimeSpan.FromSeconds(2);
        Throws<ObservationDeadlineExceededException>(timed.Take);
    }

    private static void CheckAmbiguousCandidateIsolation()
    {
        var a = new Node("a", 10);
        var b = new Node("b", 20);
        var menu = new Node("ambiguous-menu") {
            Transient = true, Bounds = new Rectangle(20, 20, 50, 50),
            First = new Node("unverified-descendant"),
        };
        var normal = new Node("confirmed-control", 0, a);
        var boundary = Boundary(new[] { Window(10, Outer), Window(20, Outer) });
        var output = new List<string>();
        var gaps = new List<string>();
        var budget = new OwnershipProbeBudget(10, TimeSpan.FromSeconds(3));
        LogicalWindowTraversal.Visit(new[] { a, menu, b, normal }, budget, 96,
            node => boundary.Owner(node) == 10,
            (node, _, _) => { output.Add(node.Id); return true; },
            node => node.First, node => node.Next, gaps.Add);
        Check(output.SequenceEqual(new[] { "a", "confirmed-control" }) &&
            gaps.SequenceEqual(new[] { "ownership_unavailable" }) && budget.Used == 4,
            "Ambiguous candidate is omitted with explicit incompleteness; confirmed controls survive.");
        Throws<WindowOwnershipUnavailableException>(() => boundary.Owner(menu));
    }

    private static void CheckObservationRetry()
    {
        var attempts = 0;
        var value = WindowOwnershipObservation.Read(() =>
        {
            attempts++;
            if (attempts == 1) { throw new WindowOwnershipUnavailableException("retired transient"); }
            return "fresh complete tree";
        });
        Check(attempts == 2 && value == "fresh complete tree", "Only a fresh complete retry is published.");
        attempts = 0;
        Throws<WindowOwnershipUnavailableException>(() => WindowOwnershipObservation.Read<int>(() =>
        {
            attempts++;
            throw new WindowOwnershipUnavailableException("persistent unknown");
        }));
        Check(attempts == 2, "Persistent ownership failure stops after one retry.");
        attempts = 0;
        Throws<InvalidOperationException>(() => WindowOwnershipObservation.Read<int>(() =>
        {
            attempts++;
            throw new InvalidOperationException("unrelated failure");
        }));
        Check(attempts == 1, "The ownership retry is not a general mutation retry.");
    }

    private static void CheckNativePropertyStates()
    {
        var unsupported = NativeHandleRead.Read(() => new NativeHandleRead(false, 0));
        var virtualHandle = NativeHandleRead.Read(() => new NativeHandleRead(true, 0));
        Check(!unsupported.Supported && virtualHandle.Supported,
            "Unsupported native property must remain distinct from a supported zero HWND.");
        Throws<WindowOwnershipUnavailableException>(() => NativeHandleRead.Read(
            () => throw new InvalidOperationException("native provider unavailable")));
        var a = new Node("a", 10);
        var unsupportedNode = new Node("unsupported", 0, a);
        var boundary = Boundary(new[] { Window(10, Outer) });
        Check(boundary.Owner(unsupportedNode) == a.Handle,
            "Unsupported controls require positive ancestor proof.");
        unsupportedNode.Parent = null;
        Throws<WindowOwnershipUnavailableException>(() =>
            Boundary(new[] { Window(10, Outer) }).Owner(unsupportedNode));
        var menuBounds = new Rectangle(20, 20, 50, 50);
        Check(OwnedNativeWindowProjection.UniqueTransientOwner(
            new[] { Window(10, Outer) }, 0, menuBounds) == 10,
            "Explicit process-neutral transient with a unique owner remains supported.");
        Check(OwnedNativeWindowProjection.UniqueTransientOwner(
            new[] { Window(10, Outer), Window(90, Outer, pid: 99) }, 0, menuBounds) is null,
            "Process-neutral transient must include foreign windows in uniqueness proof.");
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new InvalidDataException($"Expected {typeof(T).Name}.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidDataException(message); }
    }
}
