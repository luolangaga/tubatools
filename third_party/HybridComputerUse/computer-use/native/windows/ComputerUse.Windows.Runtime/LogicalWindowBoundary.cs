using System.Diagnostics;

namespace ComputerUse.Windows.Runtime;

internal sealed class WindowOwnershipUnavailableException : Exception
{
    internal WindowOwnershipUnavailableException(string message, Exception? inner = null)
        : base(message, inner) { }
}

internal sealed class ObservationDeadlineExceededException : Exception
{
    internal ObservationDeadlineExceededException()
        : base("Observation sample deadline exceeded.") { }
}

internal readonly record struct NativeHandleRead(bool Supported, long Value)
{
    internal static NativeHandleRead Read(Func<NativeHandleRead> read)
    {
        try { return read(); }
        catch (Exception error)
        {
            throw new WindowOwnershipUnavailableException(
                "The native handle property could not be read.", error);
        }
    }
}

// Counts attempted work, including rejected nodes. This is separate from the
// number of records published in a canonical snapshot.
internal sealed class OwnershipProbeBudget
{
    private readonly int _limit;
    private readonly TimeSpan _timeout;
    private readonly Func<TimeSpan> _elapsed;
    internal int Used { get; private set; }
    internal int Remaining => Math.Max(0, _limit - Used);

    internal OwnershipProbeBudget(
        int limit, TimeSpan timeout, Func<TimeSpan>? elapsed = null)
    {
        if (limit < 1) { throw new ArgumentOutOfRangeException(nameof(limit)); }
        _limit = limit;
        _timeout = timeout;
        // A caller can share one clock across ownership, traversal and retries.
        if (elapsed is null)
        {
            var watch = Stopwatch.StartNew();
            elapsed = () => watch.Elapsed;
        }
        _elapsed = elapsed;
    }

    internal void Take()
    {
        CheckTime();
        if (Used >= _limit)
        {
            throw new WindowOwnershipUnavailableException("Ownership probe budget exhausted.");
        }
        Used++;
    }

    internal void CheckTime()
    {
        if (_elapsed() >= _timeout)
        {
            throw new ObservationDeadlineExceededException();
        }
    }
}

internal readonly record struct LogicalWindowOwner(long Handle, bool NativeProof);

internal sealed class NativeWindowOwnership
{
    private readonly IReadOnlyDictionary<long, LogicalWindowOwner> _knownRoots;
    private readonly Func<long, long> _root;
    private readonly Func<long, LogicalWindowOwner?> _classifyUnlistedRoot;
    private readonly Dictionary<long, LogicalWindowOwner?> _handles = new();
    private readonly Dictionary<long, LogicalWindowOwner?> _roots = new();

    internal NativeWindowOwnership(
        IReadOnlyDictionary<long, long> knownRoots,
        Func<long, long> root,
        Func<long, long?> classifyUnlistedRoot)
        : this(knownRoots.ToDictionary(pair => pair.Key,
            pair => new LogicalWindowOwner(pair.Value, true)), root,
            handle => classifyUnlistedRoot(handle) is long owner
                ? new LogicalWindowOwner(owner, true) : null) { }

    internal NativeWindowOwnership(
        IReadOnlyDictionary<long, LogicalWindowOwner> knownRoots,
        Func<long, long> root,
        Func<long, LogicalWindowOwner?> classifyUnlistedRoot)
    {
        _knownRoots = knownRoots;
        _root = root;
        _classifyUnlistedRoot = classifyUnlistedRoot;
    }

    internal long? Owner(long handle) => ReadOwner(handle)?.Handle;

    internal LogicalWindowOwner? ReadOwner(long handle)
    {
        if (handle == 0) { return null; }
        if (_handles.TryGetValue(handle, out var owner)) { return owner; }
        if (_knownRoots.TryGetValue(handle, out var known))
        {
            _handles[handle] = known;
            return known;
        }
        var root = _root(handle);
        if (root == 0)
        {
            throw new WindowOwnershipUnavailableException("A native ownership root retired.");
        }
        if (!_roots.TryGetValue(root, out owner))
        {
            owner = _knownRoots.TryGetValue(root, out known)
                ? known
                : _classifyUnlistedRoot(root);
            _roots[root] = owner;
        }
        _handles[handle] = owner;
        return owner;
    }
}

// One instance belongs to one read-only sample, never to a lease or an action
// after focus. Raw ancestry is memoized so hidden UIA containers do not require
// a separate full ascent for each ControlView node.
internal sealed class LogicalWindowBoundary<T> where T : class
{
    private readonly Func<T, string> _identity;
    private readonly Func<T, long?> _nativeOwner;
    private readonly Func<T, T?> _parent;
    private readonly Func<T, long?> _detachedTransientOwner;
    private readonly OwnershipProbeBudget _budget;
    private readonly Func<T, bool>? _requiresNativeAncestor;
    private readonly Func<T, bool>? _nativeProof;
    private readonly Dictionary<string, (long Owner, bool Native)> _owners = new(StringComparer.Ordinal);

    internal LogicalWindowBoundary(
        Func<T, string> identity,
        Func<T, long?> nativeOwner,
        Func<T, T?> parent,
        Func<T, long?> detachedTransientOwner,
        OwnershipProbeBudget budget,
        Func<T, bool>? requiresNativeAncestor = null,
        Func<T, bool>? nativeProof = null)
    {
        _identity = identity;
        _nativeOwner = nativeOwner;
        _parent = parent;
        _detachedTransientOwner = detachedTransientOwner;
        _budget = budget;
        _requiresNativeAncestor = requiresNativeAncestor;
        _nativeProof = nativeProof;
    }

    internal long Owner(T target)
    {
        var path = new List<(T Node, string Identity)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        T? current = target;
        long? owner = null;
        var native = false;
        try
        {
            while (current is not null)
            {
                _budget.Take();
                var identity = _identity(current);
                if (identity.Length > 0 && _owners.TryGetValue(identity, out var cached))
                {
                    // An inferred cached owner must not bypass the semantic
                    // window guard for another wrapper of the same UIA object.
                    if (!cached.Native && _requiresNativeAncestor is not null)
                    {
                        _nativeOwner(current);
                        _budget.CheckTime();
                        if (_requiresNativeAncestor(current))
                        {
                            throw new WindowOwnershipUnavailableException(
                                "A semantic window has only inferred cached ownership.");
                        }
                    }
                    owner = cached.Owner;
                    native = cached.Native;
                    break;
                }
                if (path.Count >= 256 ||
                    (identity.Length > 0 && !seen.Add(identity)))
                {
                    throw new WindowOwnershipUnavailableException(
                        "UIA ancestry is cyclic or exceeds the ownership depth budget.");
                }
                path.Add((current, identity));
                owner = _nativeOwner(current);
                _budget.CheckTime();
                if (owner.HasValue) { native = _nativeProof?.Invoke(current) ?? true; break; }
                current = _parent(current);
                _budget.CheckTime();
            }
            // A semantic Window with no matching native root needs positive
            // native ancestry, including when an ancestor was cached spatially.
            if (!native && path.Any(entry => _requiresNativeAncestor?.Invoke(entry.Node) == true))
            {
                throw new WindowOwnershipUnavailableException(
                    "A semantic window has no verified native ancestor.");
            }
            if (!owner.HasValue)
            {
                // Only an actual transient may use spatial ownership. A known
                // native ancestor always takes precedence, including foreign roots.
                for (var index = 0; index < path.Count; index++)
                {
                    var entry = path[index];
                    owner = _detachedTransientOwner(entry.Node);
                    _budget.CheckTime();
                    if (owner.HasValue)
                    {
                        // The desktop (or an unanchored host above this popup)
                        // does not acquire the popup's owner.
                        path.RemoveRange(index + 1, path.Count - index - 1);
                        break;
                    }
                }
            }
            if (!owner.HasValue || owner.Value == 0)
            {
                throw new WindowOwnershipUnavailableException(
                    "No unique live logical owner could be established.");
            }
            foreach (var entry in path)
            {
                if (entry.Identity.Length > 0) { _owners[entry.Identity] = (owner.Value, native); }
            }
            return owner.Value;
        }
        catch (ObservationDeadlineExceededException) { throw; }
        catch (WindowOwnershipUnavailableException) { throw; }
        catch (Exception error)
        {
            throw new WindowOwnershipUnavailableException(
                "UIA ownership could not be read.", error);
        }
    }
}

internal static class InheritedWindowOwnership
{
    // A node without its own native window that is not a window itself
    // belongs to its parent's logical window; LogicalWindowBoundary.Owner
    // would walk to the parent and return the same answer. Roots, nodes with
    // a native handle and semantic windows need the ownership probe.
    internal static bool? FromParent(
        bool? parentOwned, long nativeWindowHandle, bool semanticWindow) =>
        parentOwned is bool owned && nativeWindowHandle == 0 && !semanticWindow
            ? owned
            : null;
}

internal static class WindowOwnershipObservation
{
    internal static T Read<T>(Func<T> freshSample)
    {
        try { return freshSample(); }
        catch (WindowOwnershipUnavailableException)
        {
            // A retired transient must not invalidate an otherwise stable tree.
            // Never publish the partial first attempt or retry a mutation here.
            return freshSample();
        }
    }
}

internal static class LogicalWindowTraversal
{
    internal static void Visit<T>(
        IReadOnlyList<T> roots,
        OwnershipProbeBudget visits,
        int maxDepth,
        Func<T, bool> includes,
        Func<T, int[], int, bool> emit,
        Func<T, T?> firstChild,
        Func<T, T?> nextSibling,
        Action<string> gap,
        int rootIndexOffset = 0) where T : class
    {
        var level = new List<(T Node, int[] Path)>();
        for (var index = 0; index < roots.Count; index++)
        {
            if (!CanRead()) { return; }
            var path = new[] { checked(rootIndexOffset + index) };
            if (Emit(roots[index], path, 0)) { level.Add((roots[index], path)); }
        }
        for (var depth = 0; level.Count > 0; depth++)
        {
            if (depth >= maxDepth) { gap("depth_budget"); return; }
            var nextLevel = new List<(T Node, int[] Path)>();
            var cursors = new Queue<(T Parent, int[] Path, T? Previous, int Index)>(
                level.Select(parent => (parent.Node, parent.Path, (T?)null, 0)));
            // Alternate parent branches so a wide child chain cannot consume
            // this layer's entire budget before another branch gets one child.
            while (cursors.TryDequeue(out var cursor))
            {
                if (!CanRead()) { return; }
                var child = cursor.Previous is null
                    ? firstChild(cursor.Parent)
                    : nextSibling(cursor.Previous);
                visits.CheckTime();
                if (child is null) { continue; }
                var path = new int[cursor.Path.Length + 1];
                cursor.Path.CopyTo(path, 0);
                path[^1] = cursor.Index;
                if (Emit(child, path, depth + 1)) { nextLevel.Add((child, path)); }
                cursors.Enqueue((cursor.Parent, cursor.Path, child, cursor.Index + 1));
            }
            level = nextLevel;
        }

        bool CanRead()
        {
            visits.CheckTime();
            if (visits.Remaining > 0) { return true; }
            gap("node_budget");
            return false;
        }

        bool Emit(T node, int[] path, int depth)
        {
            visits.Take();
            bool owned;
            try { owned = includes(node); }
            catch (WindowOwnershipUnavailableException)
            {
                // An ambiguous candidate must not erase independently verified
                // controls. The incomplete sample cannot prove uniqueness.
                gap("ownership_unavailable");
                visits.CheckTime();
                return false;
            }
            visits.CheckTime();
            if (!owned) { return false; }
            var emitted = emit(node, path, depth);
            visits.CheckTime();
            return emitted;
        }
    }
}
