namespace ComputerUse.Windows.Runtime;

internal static class LogicalWindowNativeOwner
{
    internal static long? Resolve(
        NativeHandleRead read,
        long desktopHandle,
        Func<bool?> isSemanticWindow,
        Func<bool> isDesktop,
        Func<long, long?> nativeOwner,
        Func<long?> semanticRootOwner,
        Action requireNativeAncestor)
    {
        var handle = read.Value;
        if (handle == desktopHandle && handle != 0) { return null; }
        if (!read.Supported || handle == 0)
        {
            var semanticWindow = isSemanticWindow();
            if (!semanticWindow.HasValue)
            {
                requireNativeAncestor();
                return null;
            }
            if (semanticWindow.Value)
            {
                if (isDesktop()) { return null; }
                var owner = semanticRootOwner();
                if (owner.HasValue) { return owner; }
                requireNativeAncestor();
            }
            return null;
        }
        return nativeOwner(handle);
    }
}

// Private sample-local root objects; no RuntimeId component is interpreted as HWND.
internal sealed class SemanticWindowRoots<T> where T : class
{
    private readonly IReadOnlyDictionary<long, long> _candidates;
    private readonly Func<long, T> _fromHandle;
    private readonly Func<T, T, bool> _compare;
    private readonly OwnershipProbeBudget _budget;
    private readonly Func<long, bool> _nativeProof;
    private readonly Dictionary<long, Lazy<T>> _roots = new();

    internal SemanticWindowRoots(
        IReadOnlyDictionary<long, long> candidates,
        Func<long, T> fromHandle,
        Func<T, T, bool> compare,
        OwnershipProbeBudget budget,
        Func<long, bool>? nativeProof = null)
    {
        _candidates = candidates;
        _fromHandle = fromHandle;
        _compare = compare;
        _budget = budget;
        _nativeProof = nativeProof ?? (_ => true);
    }

    internal long? Owner(T element)
    {
        long? owner = null;
        try
        {
            foreach (var candidate in _candidates)
            {
                if (!_roots.TryGetValue(candidate.Key, out var root))
                {
                    root = new Lazy<T>(() =>
                    {
                        _budget.Take();
                        var value = _fromHandle(candidate.Key);
                        _budget.CheckTime();
                        return value ?? throw new WindowOwnershipUnavailableException(
                            "A native root object is unavailable.");
                    });
                    _roots.Add(candidate.Key, root);
                }
                var nativeRoot = root.Value;
                _budget.Take();
                var same = _compare(element, nativeRoot);
                _budget.CheckTime();
                if (!same) { continue; }
                if (!_nativeProof(candidate.Key))
                {
                    throw new WindowOwnershipUnavailableException(
                        "The matching native root has only inferred logical ownership.");
                }
                if (owner.HasValue)
                {
                    throw new WindowOwnershipUnavailableException(
                        "Multiple native roots identify the same semantic window.");
                }
                owner = candidate.Value;
            }
            return owner;
        }
        catch (ObservationDeadlineExceededException) { throw; }
        catch (WindowOwnershipUnavailableException) { throw; }
        catch (Exception error)
        {
            throw new WindowOwnershipUnavailableException(
                "Semantic-window native root comparison is unavailable.", error);
        }
    }
}
