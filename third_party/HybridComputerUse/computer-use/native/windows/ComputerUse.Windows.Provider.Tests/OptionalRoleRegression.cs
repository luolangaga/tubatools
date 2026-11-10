using System.Drawing;
using ComputerUse.Windows.Runtime;

internal static class OptionalRoleRegression
{
    private enum Role { Button, Window }
    private enum RoleState { Supported, Unsupported, ReadError }
    private sealed class UnsupportedRoleException : NotSupportedException;
    private sealed class Node(string id, NativeHandleRead handle, Node? parent = null)
    {
        internal readonly string Id = id;
        internal readonly NativeHandleRead Handle = handle;
        internal Node? Parent = parent;
        internal Role Role = Role.Button;
        internal RoleState State;
        internal bool WindowPattern;
        internal bool Pending;
        internal bool Inferred;
        internal int ParentReads;
        internal int PatternReads;
        internal readonly Exception Unsupported = new UnsupportedRoleException();
        internal readonly Exception ReadError = new IOException("ControlType transport failure");

        internal Role Getter() => State switch {
            RoleState.Unsupported => throw Unsupported,
            RoleState.ReadError => throw ReadError,
            _ => Role,
        };
        internal (bool Supported, Role Value) ReadRole() => State switch {
            RoleState.Unsupported => (false, default),
            RoleState.ReadError => throw ReadError,
            _ => (true, Role),
        };
        internal bool Pattern() { PatternReads++; return WindowPattern; }
    }

    internal static int Run()
    {
        var tests = new (string Name, Action Test)[] {
            ("supported-native-chain", () => Positive(RoleState.Supported)),
            ("unsupported-native-chain", () => Positive(RoleState.Unsupported)),
            ("unsupported-container-descendants", Descendants),
            ("unsupported-distinct-from-read-error", ErrorIdentity),
            ("short-circuit-nonwindow", ShortCircuit),
            ("window-pattern-supported-unavailable-error", PatternStates),
            ("native-handle-bypasses-role-getter", NativeBypass),
            ("native-B-beats-parent-A", NativePrecedence),
            ("semantic-exact-B-beats-parent-A", SemanticPrecedence),
            ("unsupported-cold-hot-inferred-rejected", Inferred),
            ("semantic-cold-hot-inferred-rejected", SemanticInferred),
            ("unsupported-no-root-cycle-rejected", NoRoot),
            ("unsupported-foreign-root-not-A", Foreign),
            ("read-error-preserves-independent-controls", ReadErrorBranch),
        };
        var failures = 0;
        foreach (var (name, test) in tests)
        {
            try { test(); Console.WriteLine($"PASS {name}"); }
            catch (Exception error) {
                failures++; Console.WriteLine($"FAIL {name}: {error}");
            }
        }
        Console.WriteLine($"SUMMARY total={tests.Length} passed={tests.Length-failures} failed={failures}");
        return failures == 0 ? 0 : 1;
    }

    private static Node Root(string id, long handle) => new(id, new(true, handle));
    private static Node Unknown(string id, Node? parent = null) =>
        new(id, new(false, 0), parent) { State = RoleState.Unsupported };
    private static LogicalWindowBoundary<Node> Boundary(
        Dictionary<long, LogicalWindowOwner>? owners = null,
        Func<long, long>? nativeRoot = null,
        Dictionary<long, Node>? roots = null)
    {
        owners ??= new() { [10] = new(10, true) };
        roots ??= owners.ToDictionary(x => x.Key, x => Root("native-" + x.Key, x.Key));
        var budget = new OwnershipProbeBudget(500, TimeSpan.FromSeconds(10));
        var native = new NativeWindowOwnership(owners, nativeRoot ?? (h => h),
            h => new LogicalWindowOwner(h, true));
        var semantic = new SemanticWindowRoots<Node>(
            owners.ToDictionary(x => x.Key, x => x.Value.Handle), h => roots[h],
            (a, b) => a.Id == b.Id, budget, h => owners[h].NativeProof);
        return new LogicalWindowBoundary<Node>(
            n => n.Id,
            n => LogicalWindowNativeOwner.Resolve(n.Handle, 99,
                () => UiaSemanticWindowPredicate.Read(n.ReadRole, Role.Window, n.Pattern),
                () => false,
                h => {
                    var owner = native.ReadOwner(h);
                    n.Inferred = owner is { NativeProof: false };
                    return owner?.Handle;
                },
                () => semantic.Owner(n), () => n.Pending = true),
            n => { n.ParentReads++; return n.Parent; },
            _ => null, budget, n => n.Pending, n => !n.Inferred);
    }
    private static void Check(bool ok, string why) {
        if (!ok) throw new InvalidDataException(why);
    }
    private static WindowOwnershipUnavailableException Unavailable(Action action) {
        try { action(); } catch (WindowOwnershipUnavailableException error) { return error; }
        throw new InvalidDataException("Expected ownership unavailable");
    }
    private static void Positive(RoleState state) {
        var child = Root("native-child", 11);
        var container = new Node("container", new(false, 0), child) { State = state };
        var target = new Node("button", new(false, 0), container);
        var boundary = Boundary(nativeRoot: h => h == 11 ? 10 : h);
        Check(boundary.Owner(target) == 10, "Positive native ancestry must retain button");
        Check(container.ParentReads == 1, "Container raw parent must be read once");
    }
    private static void Descendants() {
        var native = Root("native-A", 10);
        var container = Unknown("container", native);
        var button = new Node("button", new(false, 0), container);
        var emitted = new List<string>();
        var gaps = new List<string>();
        var boundary = Boundary();
        LogicalWindowTraversal.Visit(new[] { native },
            new OwnershipProbeBudget(20, TimeSpan.FromSeconds(10)), 10,
            n => boundary.Owner(n) == 10,
            (n, _, _) => { emitted.Add(n.Id); return true; },
            n => n == native ? container : n == container ? button : null,
            _ => null, gaps.Add);
        Check(emitted.SequenceEqual(new[] { "native-A", "container", "button" }) && gaps.Count == 0,
            $"Expected complete native-owned branch; emitted={string.Join(",", emitted)} gaps={string.Join(",", gaps)}");
    }
    private static void ErrorIdentity() {
        foreach (var state in new[] { RoleState.ReadError }) {
            var n = Unknown("container", Root("A", 10)); n.State = state;
            var error = Unavailable(() => Boundary().Owner(n));
            Check(ReferenceEquals(error.InnerException, n.ReadError),
                "A visible read error must preserve its exact cause");
            Check(n.ParentReads == 0 && n.PatternReads == 0,
                "Direct getter failure must precede Pattern and ancestry");
        }
        var unsupported = Unknown("unsupported", Root("A", 10));
        Check(Boundary().Owner(unsupported) == 10 && unsupported.PatternReads == 0,
            "Explicit unsupported role continues only to proven native ancestry");
    }
    private static void ShortCircuit() {
        var n = new Node("ordinary", new(false, 0));
        Check(UiaSemanticWindowPredicate.Read(n.ReadRole, Role.Window,
            () => throw new Exception("Must not read Pattern")) == false, "Non-window should short-circuit");
    }
    private static void PatternStates() {
        foreach (var available in new[] { false, true }) {
            var n = new Node("window", new(false, 0)) { Role = Role.Window, WindowPattern = available };
            Check(UiaSemanticWindowPredicate.Read(n.ReadRole, Role.Window, n.Pattern) == available
                && n.PatternReads == 1, "Pattern availability retained");
        }
        var failure = new IOException("Pattern read error");
        try {
            UiaSemanticWindowPredicate.Read(() => (true, Role.Window), Role.Window, () => throw failure);
            throw new InvalidDataException("Pattern error swallowed");
        } catch (IOException actual) { Check(ReferenceEquals(actual, failure), "Pattern error identity"); }
    }
    private static void NativeBypass() {
        var n = new Node("native", new(true, 10)) { State = RoleState.ReadError };
        Check(Boundary().Owner(n) == 10 && n.PatternReads == 0, "Known HWND requires no role");
    }
    private static void NativePrecedence() {
        var b = Root("B", 20); b.Parent = Root("A", 10);
        Check(Boundary(new() { [10]=new(10,true), [20]=new(20,true) }).Owner(b) == 20,
            "Native B must beat A");
    }
    private static void SemanticPrecedence() {
        var n = new Node("B", new(false, 0), Root("A", 10)) { Role=Role.Window, WindowPattern=true };
        Check(Boundary(new() { [10]=new(10,true), [20]=new(20,true) },
            roots: new() { [10]=Root("A",10), [20]=Root("B",20) }).Owner(n) == 20,
            "Exact semantic B must beat A");
    }
    private static void Inferred() => CheckInferred(false);
    private static void SemanticInferred() => CheckInferred(true);
    private static void CheckInferred(bool knownWindow) {
        foreach (var thread in new[] { 0U, 7U })
        foreach (var warm in new[] { false, true }) {
            var candidates = new[] {
                new OwnedNativeWindowCandidate(10,0,7,true,new Rectangle(0,0,500,500),7),
                new OwnedNativeWindowCandidate(30,0,7,false,new Rectangle(20,20,100,100),thread),
            };
            var owners = candidates.ToDictionary(c => c.Handle,
                c => OwnedNativeWindowProjection.ResolveLogicalOwner(candidates,c.Handle));
            Check(owners[30] == new LogicalWindowOwner(10,false), "Real inferred projection required");
            var popup = Root("popup",30);
            var n = Unknown("container",popup);
            if (knownWindow) { n.State=RoleState.Supported; n.Role=Role.Window; n.WindowPattern=true; }
            var b = Boundary(owners);
            if(warm) Check(b.Owner(popup)==10,"Ordinary popup retained");
            Unavailable(() => b.Owner(n));
        }
    }
    private static void NoRoot() {
        Unavailable(() => Boundary().Owner(Unknown("detached")));
        var cycle=Unknown("cycle"); cycle.Parent=cycle;
        Unavailable(() => Boundary().Owner(cycle));
    }
    private static void Foreign() {
        var n=Unknown("container",Root("foreign",90));
        try { Check(Boundary().Owner(n)!=10,"Foreign native root must not become A"); }
        catch(WindowOwnershipUnavailableException) { }
    }
    private static void ReadErrorBranch() {
        var a=Root("A",10);
        var bad=Unknown("bad",a); bad.State=RoleState.ReadError;
        var emitted=new List<string>(); var gaps=new List<string>(); var b=Boundary();
        LogicalWindowTraversal.Visit(new[] {bad,a},
            new OwnershipProbeBudget(10,TimeSpan.FromSeconds(10)),10,
            n=>b.Owner(n)==10,(n,_,_)=>{emitted.Add(n.Id);return true;},
            _=>null,_=>null,gaps.Add);
        Check(emitted.SequenceEqual(new[]{"A"}) && gaps.SequenceEqual(new[]{"ownership_unavailable"}),
            "Read errors preserve independent controls without claiming completeness");
    }
}
