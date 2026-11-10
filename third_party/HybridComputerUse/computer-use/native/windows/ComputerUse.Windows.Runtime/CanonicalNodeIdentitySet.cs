namespace ComputerUse.Windows.Runtime;

// Deduplicates the nodes of one sample. A stable provider identity decides
// alone. A node without one needs a native element comparison, which is a
// cross-process call, so the set compares only nodes whose observed
// properties are equal (two reads of one element in one sample cannot differ
// in control type, name or frame) and that came from different traversal
// roots: one root's walk reaches each element once, while nested containers
// of some providers compare equal to their parent.
internal sealed class CanonicalNodeIdentitySet<T>(Func<T, T, bool> sameElement)
    where T : class
{
    private readonly HashSet<string> _identities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(T Element, bool Identified, int? Origin)>> _byProperties =
        new(StringComparer.Ordinal);

    internal bool TryAdd(
        T element, string providerIdentity, string propertyKey = "", int? origin = null)
    {
        var identified = providerIdentity.Length > 0;
        if (identified && _identities.Contains(providerIdentity))
        {
            return false;
        }
        _byProperties.TryGetValue(propertyKey, out var peers);
        if (peers is not null)
        {
            foreach (var (previous, previousIdentified, previousOrigin) in peers)
            {
                // Two distinct stable identities are distinct elements.
                if (identified && previousIdentified) { continue; }
                if (origin is not null && previousOrigin == origin) { continue; }
                if (sameElement(element, previous)) { return false; }
            }
        }
        if (identified) { _identities.Add(providerIdentity); }
        peers ??= _byProperties[propertyKey] = new List<(T Element, bool Identified, int? Origin)>();
        peers.Add((element, identified, origin));
        return true;
    }
}
