using ComputerUse.Windows.Runtime;

internal static class HandlelessSemanticWindowTests
{
    private sealed class Node(string id, NativeHandleRead handle, Node? parent = null)
    {
        internal string Id = id;
        internal NativeHandleRead Handle = handle;
        internal Node? Parent = parent;
        internal bool SemanticWindow;
        internal bool RequiresNativeAncestor;
        internal bool Desktop;
        internal bool Transient;
        internal bool HandleReadError;
        internal bool InferredOwner;
    }

    internal static void Run()
    {
        var failures = new List<Exception>();
        foreach (var supported in new[] { false, true })
        {
            try
            {
                var native = new NativeWindowOwnership(
                    new Dictionary<long, long> { [10] = 10 },
                    handle => handle == 11 ? 10 : 0,
                    _ => null);
                var child = new Node("child-hwnd", new NativeHandleRead(true, 11));
                var modal = new Node("semantic-window", new NativeHandleRead(supported, 0), child)
                {
                    SemanticWindow = true,
                };
                var target = new Node("target", new NativeHandleRead(true, 0), modal);
                var budget = new OwnershipProbeBudget(100, TimeSpan.FromSeconds(3));
                var roots = new SemanticWindowRoots<Node>(
                    new Dictionary<long, long> { [10] = 10 }, _ => Root("a", 10),
                    (left, right) => left.Id == right.Id, budget);
                var boundary = new LogicalWindowBoundary<Node>(
                    node => node.Id,
                    node => LogicalWindowNativeOwner.Resolve(
                        node.Handle, 99, () => node.SemanticWindow, () => false, native.Owner,
                        () => roots.Owner(node), () => node.RequiresNativeAncestor = true),
                    node => node.Parent,
                    _ => null,
                    budget,
                    node => node.RequiresNativeAncestor);
                if (boundary.Owner(target) != 10)
                {
                    throw new InvalidDataException("Expected positive GA_ROOT A ownership.");
                }
                Console.WriteLine($"PASS handleless-semantic supported={supported} -> child HWND -> native A");
            }
            catch (Exception error)
            {
                Console.WriteLine($"FAIL handleless-semantic supported={supported}: {error}");
                failures.Add(error);
            }
        }
        if (failures.Count > 0) { throw new AggregateException(failures); }
        CheckRootPrecedence();
        CheckUnavailableComparisons();
        CheckAncestryFailures();
        CheckProbeCost();
        CheckPostFocus();
        CheckProjectedPopupProvenance();
        Console.WriteLine("Handleless semantic-window counterexamples passed.");
    }

    private static Node Root(string id, long handle) => new(id, new NativeHandleRead(true, handle));
    private static Node Semantic(string id, Node? parent = null) =>
        new(id, new NativeHandleRead(false, 0), parent) { SemanticWindow = true };

    private static LogicalWindowBoundary<Node> Boundary(
        IReadOnlyDictionary<long, long> candidates,
        Func<long, Node> fromHandle,
        Func<Node, Node, bool>? compare = null,
        Func<long, long>? nativeRoot = null,
        Func<long, long?>? classify = null,
        OwnershipProbeBudget? budget = null,
        IReadOnlyDictionary<long, LogicalWindowOwner>? ownership = null)
    {
        budget ??= new OwnershipProbeBudget(1000, TimeSpan.FromSeconds(3));
        var roots = new SemanticWindowRoots<Node>(
            candidates, fromHandle, compare ?? ((a, b) => a.Id == b.Id), budget,
            handle => ownership is null || ownership[handle].NativeProof);
        ownership ??= candidates.ToDictionary(pair => pair.Key,
            pair => new LogicalWindowOwner(pair.Value, true));
        var native = new NativeWindowOwnership(ownership, nativeRoot ?? (handle => handle),
            handle => (classify ?? (value => value))(handle) is long owner
                ? new LogicalWindowOwner(owner, true) : null);
        return new LogicalWindowBoundary<Node>(
            node => node.Id,
            node => LogicalWindowNativeOwner.Resolve(
                NativeHandleRead.Read(() => node.HandleReadError
                    ? throw new InvalidOperationException("handle read failed") : node.Handle),
                99, () => node.SemanticWindow, () => node.Desktop,
                handle => {
                    var owner = native.ReadOwner(handle);
                    node.InferredOwner = owner is { NativeProof: false };
                    return owner?.Handle;
                },
                () => roots.Owner(node), () => node.RequiresNativeAncestor = true),
            node => node.Parent,
            node => node.Transient ? 10 : null,
            budget, node => node.RequiresNativeAncestor, node => !node.InferredOwner);
    }

    private static void CheckRootPrecedence()
    {
        var a = Root("a", 10);
        var b = Root("b", 20);
        var modal = Semantic("b", a);
        var candidates = new Dictionary<long, long> { [10] = 10, [20] = 20 };
        var boundary = Boundary(candidates, handle => handle == 10 ? a : b);
        Check(boundary.Owner(modal) == 20, "Exact native B must beat raw A.");
        var nativeCandidates = new[] {
            new OwnedNativeWindowCandidate(10, 0, 7, true, System.Drawing.Rectangle.Empty),
            new OwnedNativeWindowCandidate(20, 10, 7, true, System.Drawing.Rectangle.Empty),
        };
        Check(OwnedNativeWindowProjection.ResolveLogicalHandle(nativeCandidates, 20) == 20,
            "Independent native B with owner A must remain B.");
        var knownA = new Dictionary<long, long> { [10] = 10 };
        foreach (var root in new[] { 20L, 90L })
        {
            var child = Root("hidden-or-foreign-child", 21);
            child.Parent = a;
            modal = Semantic("unmapped", child);
            boundary = Boundary(knownA, _ => a, nativeRoot: _ => root, classify: h => h);
            Check(boundary.Owner(modal) == root,
                "Hidden or foreign raw native root cannot be absorbed by A.");
        }
        var duplicateLogical = new Dictionary<long, long> { [10] = 10, [20] = 10 };
        Throws(() => Boundary(duplicateLogical, _ => Root("same", 0)).Owner(Semantic("same", a)));
        Throws(() => Boundary(candidates, _ => Root("same", 0)).Owner(Semantic("same", a)));
    }

    private static void CheckUnavailableComparisons()
    {
        var a = Root("a", 10);
        var candidates = new Dictionary<long, long> { [10] = 10, [20] = 20 };
        foreach (var failure in new[] { "missing", "read", "compare" })
        {
            var boundary = Boundary(candidates,
                handle => handle == 10 ? a : failure == "missing" ? null!
                    : failure == "read" ? throw new InvalidOperationException("root failed") : Root("b", 20),
                (left, right) => right.Handle.Value == 20 && failure == "compare"
                    ? throw new InvalidOperationException("compare failed") : left.Id == right.Id);
            Throws(() => boundary.Owner(Semantic("a", a)));
            Check(boundary.Owner(new Node("ordinary", new NativeHandleRead(false, 0), a)) == 10,
                "Unavailable semantic branch must not erase ordinary controls.");
            var output = new List<string>();
            var gaps = new List<string>();
            LogicalWindowTraversal.Visit(new[] { Semantic("bad", a), a },
                new OwnershipProbeBudget(10, TimeSpan.FromSeconds(3)), 10,
                node => boundary.Owner(node) == 10,
                (node, _, _) => { output.Add(node.Id); return true; },
                _ => null, _ => null, gaps.Add);
            Check(output.SequenceEqual(new[] { "a" }) && gaps.SequenceEqual(new[] { "ownership_unavailable" }),
                "Observation preserves independent controls and explicit incomplete branch.");
        }
        var bad = Semantic("bad", a);
        bad.HandleReadError = true;
        Throws(() => Boundary(candidates, _ => a).Owner(bad));
        ThrowsRead(() => LogicalWindowNativeOwner.Resolve(new NativeHandleRead(false, 0), 99,
            () => throw new InvalidOperationException("pattern failed"), () => false, _ => 10,
            () => 10, () => { }));
        ThrowsRead(() => LogicalWindowNativeOwner.Resolve(new NativeHandleRead(false, 0), 99,
            () => true, () => throw new InvalidOperationException("desktop comparison failed"),
            _ => 10, () => 10, () => { }));
    }

    private static void CheckAncestryFailures()
    {
        var a = Root("a", 10);
        var candidates = new Dictionary<long, long> { [10] = 10 };
        var cycle = Semantic("cycle");
        cycle.Parent = cycle;
        Throws(() => Boundary(candidates, _ => a).Owner(cycle));
        Throws(() => Boundary(candidates, _ => a).Owner(Semantic("no-root")));
        var desktop = Semantic("desktop");
        desktop.Desktop = true;
        Throws(() => Boundary(candidates, _ => a).Owner(Semantic("modal", desktop)));
        var popup = new Node("menu", new NativeHandleRead(false, 0)) { Transient = true };
        var boundary = Boundary(candidates, _ => a);
        Check(boundary.Owner(popup) == 10, "Existing detached transient still supported.");
        Throws(() => boundary.Owner(Semantic("modal", popup)));
        Throws(() => Boundary(candidates, _ => a).Owner(Semantic("modal", popup)));
        Throws(() => Boundary(candidates, _ => a,
            budget: new OwnershipProbeBudget(1, TimeSpan.FromSeconds(3))).Owner(Semantic("modal", a)));
        var elapsed = TimeSpan.Zero;
        var budget = new OwnershipProbeBudget(100, TimeSpan.FromSeconds(1), () => elapsed);
        try
        {
            Boundary(candidates, _ => { elapsed = TimeSpan.FromSeconds(2); return a; },
                budget: budget).Owner(Semantic("modal", a));
            throw new InvalidDataException("Expected observation deadline exhaustion.");
        }
        catch (ObservationDeadlineExceededException) { }
    }

    private static void CheckProbeCost()
    {
        var a = Root("a", 10);
        var candidates = new Dictionary<long, long> { [10] = 10 };
        var reads = 0;
        var compares = 0;
        LogicalWindowBoundary<Node> Sample() => Boundary(candidates,
            _ => { reads++; return a; }, (left, right) => { compares++; return left.Id == right.Id; });
        var boundary = Sample();
        Check(boundary.Owner(new Node("ordinary", new NativeHandleRead(false, 0), a)) == 10 &&
            reads == 0 && compares == 0, "Ordinary controls must not probe candidate roots.");
        boundary.Owner(Semantic("first", a));
        boundary.Owner(Semantic("second", a));
        Check(reads == 1 && compares == 2, "Root objects are acquired once per sample.");
        Sample().Owner(Semantic("third", a));
        Check(reads == 2 && compares == 3, "New samples must reacquire root objects.");
        var desktop = Semantic("desktop");
        desktop.Desktop = true;
        Throws(() => boundary.Owner(desktop));
        Check(reads == 2 && compares == 3, "Desktop exclusion does not scan candidate roots.");
        var failures = 0;
        boundary = Boundary(candidates, _ => { failures++; throw new InvalidOperationException("unavailable"); });
        Throws(() => boundary.Owner(Semantic("first", a)));
        Throws(() => boundary.Owner(Semantic("second", a)));
        Check(failures == 1, "Failed root acquisition stays unavailable within its sample.");
    }

    private static void CheckPostFocus()
    {
        foreach (var unreadable in new[] { false, true })
        {
            var a = Root("a", 10);
            var b = Root("b", 20);
            var child = Root("child", 11);
            var modal = Semantic("modal", child);
            var owner = 10L;
            var candidates = new Dictionary<long, long> { [10] = 10, [20] = 20 };
            LogicalWindowBoundary<Node> Sample() => Boundary(candidates,
                handle => handle == 10 ? a : b, nativeRoot: _ => unreadable && owner == 20
                    ? throw new InvalidOperationException("retired") : owner);
            Check(Sample().Owner(modal) == 10, "Pre-focus ownership starts at A.");
            var keys = 0;
            ProviderFailureException? failure = null;
            try
            {
                ExactFocusSettlement.RequestAndVerify(() => owner = 20, () => true,
                    verifyOwnership: () => {
                        if (Sample().Owner(modal) != 10) { throw new InvalidOperationException("changed owner"); }
                    });
                keys++;
            }
            catch (ProviderFailureException error) { failure = error; }
            Check(keys == 0 && failure is { MutationUnknown: true, InputEventsEmitted: false,
                FocusPreparation: FocusPreparationState.RequestAttemptedOutcomeUnknown },
                "Same focused object reparented to B stops with existing terminal focus uncertainty.");
        }
    }

    private static void CheckProjectedPopupProvenance()
    {
        var failures = new List<string>();
        foreach (var mode in new[] { "thread", "geometry" })
        {
            var candidates = new[] {
                new OwnedNativeWindowCandidate(10, 0, 7, true,
                    new System.Drawing.Rectangle(0, 0, 1000, 800), 3),
                new OwnedNativeWindowCandidate(30, 0, 7, false,
                    new System.Drawing.Rectangle(10, 10, 100, 100), mode == "thread" ? 3U : 0U),
            };
            var projected = candidates.ToDictionary(item => item.Handle,
                item => OwnedNativeWindowProjection.ResolveLogicalHandle(candidates, item.Handle));
            var ownership = candidates.ToDictionary(item => item.Handle,
                item => OwnedNativeWindowProjection.ResolveLogicalOwner(candidates, item.Handle));
            Check(projected[30] == 10, "Real popup projection must infer A for this fixture.");
            Check(!ownership[30].NativeProof, "Geometry/thread projection is not native proof.");
            foreach (var warm in new[] { false, true })
            {
                var a = Root("a", 10);
                var popup = Root("popup", 30);
                popup.Transient = true;
                var boundary = Boundary(projected, handle => handle == 10 ? a : popup,
                    ownership: ownership);
                if (warm) { Check(boundary.Owner(popup) == 10, "Ordinary popup support must remain."); }
                try
                {
                    var owner = boundary.Owner(Semantic("unmatched-modal", popup));
                    failures.Add($"{mode} warm={warm} incorrectly accepted owner={owner}");
                }
                catch (WindowOwnershipUnavailableException)
                {
                    Console.WriteLine($"PASS semantic native proof rejects {mode} popup warm={warm}");
                }
                Throws(() => boundary.Owner(Semantic("popup", a)));
                var hwndChild = Root("hwnd-child", 31);
                var childBoundary = Boundary(projected, handle => handle == 10 ? a : popup,
                    nativeRoot: handle => handle == 31 ? 30 : handle, ownership: ownership);
                Check(childBoundary.Owner(hwndChild) == 10, "Ordinary child retains projected popup owner.");
                Throws(() => childBoundary.Owner(Semantic("child-modal", hwndChild)));
            }
            foreach (var independent in new[] { false, true })
            {
                var explicitCandidates = new[] {
                    candidates[0], candidates[1] with { OwnerHandle = 10, IndependentWindow = independent },
                };
                var owners = explicitCandidates.ToDictionary(item => item.Handle,
                    item => OwnedNativeWindowProjection.ResolveLogicalOwner(explicitCandidates, item.Handle));
                Check(owners[30] == new LogicalWindowOwner(independent ? 30 : 10, true),
                    "Explicit GW_OWNER is native proof; independent P stays P.");
                foreach (var warm in new[] { false, true })
                {
                    var a = Root("a", 10);
                    var popup = Root("popup", 30);
                    var boundary = Boundary(owners.ToDictionary(pair => pair.Key, pair => pair.Value.Handle),
                        handle => handle == 10 ? a : popup, ownership: owners);
                    if (warm) { boundary.Owner(popup); }
                    Check(boundary.Owner(Semantic("explicit-modal", popup)) == (independent ? 30 : 10),
                        "Pending semantic accepts native owner chain in cold and hot samples.");
                }
            }
            var chained = candidates.Concat(new[] {
                new OwnedNativeWindowCandidate(40, 30, 7, false, System.Drawing.Rectangle.Empty),
            }).ToArray();
            Check(OwnedNativeWindowProjection.ResolveLogicalOwner(chained, 40)
                == new LogicalWindowOwner(10, false), "Explicit owner leading to heuristic remains inferred.");
        }
        foreach (var failure in failures) { Console.WriteLine("FAIL " + failure); }
        Check(failures.Count == 0, "Semantic window inherited heuristic projected HWND owner.");
    }

    private static void Check(bool value, string message)
    {
        if (!value) { throw new InvalidDataException(message); }
    }

    private static void Throws(Action action)
    {
        try { action(); }
        catch (WindowOwnershipUnavailableException) { return; }
        throw new InvalidDataException("Expected unavailable ownership.");
    }

    private static void ThrowsRead(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidDataException("Read exception must not become no-match.");
    }
}
