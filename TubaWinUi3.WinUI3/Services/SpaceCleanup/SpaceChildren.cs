using System.Collections;

namespace TubaWinUi3.Services.SpaceCleanup;

/// <summary>Directories remain in memory; files stream from compressed pages. Enumeration
/// materializes individual entries only, never an entire on-disk child list.</summary>
public sealed class SpaceChildren : IReadOnlyList<SpaceNode>
{
    private readonly List<SpaceNode> _memory = [];
    private readonly SpaceChildren? _source;
    private readonly HashSet<SpaceNode>? _excluded;
    private readonly HashSet<long>? _excludedFiles;
    private long _head;
    private int _files;
    internal SpaceNode Owner { get; private set; }
    internal SpaceDiskCache? Cache { get; private set; }
    internal int Generation { get; private set; }
    internal IReadOnlyList<SpaceNode> MemoryItems => _memory;
    internal IEnumerable<SpaceNode> Directories => _source is null ? _memory.Where(n => n.IsDirectory)
        : _source.Directories.Where(n => _excluded?.Contains(n) != true);
    internal SpaceChildren(SpaceNode owner, SpaceDiskCache? cache) { Owner = owner; Cache = cache; }
    internal SpaceChildren(SpaceNode owner, SpaceChildren source, HashSet<SpaceNode>? excluded)
    {
        Owner = owner; _source = source; _excluded = excluded;
        _excludedFiles = excluded?.Where(n => n.CachedFile is not null).Select(n => n.CachedFile!.Address).ToHashSet();
    }
    public int Count => _source is not null ? Math.Max(0, _source.Count - (_excluded?.Count ?? 0)) : _memory.Count + _files;
    public SpaceNode this[int index] => this.Skip(index).First();
    public void Add(SpaceNode node)
    {
        if (_source is not null) throw new InvalidOperationException("汇总视图是只读的。");
        if (!node.IsDirectory && Cache is { } cache) { _head = cache.Append(this, node, _head); _files++; }
        else _memory.Add(node);
    }
    public void AddRange(IEnumerable<SpaceNode> nodes) { foreach (var node in nodes) Add(node); }
    public bool Remove(SpaceNode node)
    {
        if (node.CachedFile is { } entry && ReferenceEquals(entry.Owner, this) && !node.IsReleased)
        { Cache!.Delete(node); _files--; return true; }
        return _memory.Remove(node);
    }
    public void Clear() { Generation++; _head = 0; _files = 0; _memory.Clear(); }
    internal void Detach() { Clear(); Cache = null; }
    internal void Reparent(SpaceNode owner)
    { Owner = owner; foreach (var child in _memory) child.Parent = owner; }
    public IEnumerator<SpaceNode> GetEnumerator()
    {
        if (_source is not null)
        {
            foreach (var node in _source)
                if (node.CachedFile is { } file ? _excludedFiles?.Contains(file.Address) != true : _excluded?.Contains(node) != true) yield return node;
            yield break;
        }
        foreach (var node in _memory) yield return node;
        long address = _head; int generation = Generation;
        while (address != 0 && generation == Generation && Cache is { IsDisposed: false } cache)
        {
            var entry = cache.Read(this, address); address = entry.Previous;
            if (entry.Node is { } node) yield return node;
        }
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
