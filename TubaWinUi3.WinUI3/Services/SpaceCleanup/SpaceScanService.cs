using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.IO.Enumeration;

namespace TubaWinUi3.Services.SpaceCleanup;

public sealed class SpaceNode
{
    // Files vastly outnumber directories. Keep their common record to 56 bytes on x64;
    // children, descendant counts and aggregate-view state only exist on directories.
    private object _name;
    private int _nameOffset;
    private ushort _nameLength, _flags;
    private DirectoryState? Directory => _name as DirectoryState;
    private sealed class DirectoryState
    {
        internal required string Name;
        internal SpaceChildren? Children;
        internal SpaceDiskCache? Cache;
        internal long FileCount;
        internal SpaceNode? AggregateSource;
        internal HashSet<SpaceNode>? AggregateExcluded;
    }
    public SpaceNode(string name, bool isDirectory, SpaceNode? parent = null)
    { _name = isDirectory ? new DirectoryState { Name = name } : name; Parent = parent; }
    public string Name => _name switch { string text => text, DirectoryState state => state.Name, SpaceCachedFile file => file.Name,
        _ => Encoding.UTF8.GetString((byte[])_name, _nameOffset, _nameLength) };
    public bool IsDirectory => Directory is not null;
    private SpaceNode? _parent;
    public SpaceNode? Parent { get => CachedFile is { } file ? file.Owner.Owner : _parent; internal set => _parent = value; }
    public string FullPath => Parent is null ? Name : IsAggregate ? Parent.FullPath : Path.Combine(Parent.FullPath, Name);
    public SpaceChildren Children => Directory is { } state
        ? state.Children ??= state.AggregateSource is { } source
            ? new SpaceChildren(this, source.Children, state.AggregateExcluded) : new SpaceChildren(this, state.Cache)
        : throw new InvalidOperationException("文件没有子目录。");
    public bool IsAggregate { get => (_flags & 1) != 0; internal set => SetFlag(1, value); }
    internal SpaceNode? AggregateSource { get => Directory?.AggregateSource; set => Directory!.AggregateSource = value; }
    internal HashSet<SpaceNode>? AggregateExcluded { get => Directory?.AggregateExcluded; set => Directory!.AggregateExcluded = value; }
    public long Bytes { get; internal set; }
    public long FileCount { get => Directory?.FileCount ?? 1; internal set { if (Directory is { } state) state.FileCount = value; } }
    public DateTime Modified { get; internal set; }
    public bool Complete { get => (_flags & 2) != 0; internal set => SetFlag(2, value); }
    public bool IsLink { get => (_flags & 4) != 0; internal set => SetFlag(4, value); }
    public int Tag { get => (_flags >> 3) & 7; set { SetCachedTag(value); CachedFile?.Owner.Cache?.Update(this, tagOnly: true); } }
    internal void SetCachedTag(int value) => _flags = (ushort)((_flags & ~56) | (Math.Clamp(value, 0, 4) << 3));
    public bool IsReleased => (_flags & 64) != 0 || CachedFile?.IsReleased == true;
    internal SpaceCachedFile? CachedFile => _name as SpaceCachedFile;
    internal void AttachCachedFile(SpaceCachedFile file) => _name = file;
    internal void AttachCache(SpaceDiskCache cache) { if (Directory is { } state) state.Cache = cache; }
    internal void ReplaceChildren(SpaceChildren children)
    { Directory!.Children = children; children.Reparent(this); }
    private void SetFlag(ushort mask, bool value) => _flags = value ? (ushort)(_flags | mask) : (ushort)(_flags & ~mask);
    internal void PackName(byte[] block, int offset, int length)
    { _name = block; _nameOffset = offset; _nameLength = (ushort)length; }
    internal bool MatchesName(string pattern, bool wildcard)
    {
        // Filtering must not materialize millions of temporary UI strings. NTFS names
        // fit in 255 UTF-16 units; keep a safe fallback for synthetic/other-filesystem names.
        if (_name is byte[] block)
        {
            var encoded = block.AsSpan(_nameOffset, _nameLength);
            if (Encoding.UTF8.GetCharCount(encoded) <= 512)
            {
                Span<char> decoded = stackalloc char[512];
                int count = Encoding.UTF8.GetChars(encoded, decoded);
                return Match(decoded[..count]);
            }
        }
        return Match(Name.AsSpan());
        bool Match(ReadOnlySpan<char> text) => wildcard
            ? FileSystemName.MatchesSimpleExpression(pattern, text, true)
            : text.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }
    // Includes a child-list slot, name storage and directory-only state/list overhead.
    internal long EstimatedBytes => 64L + (_name switch { string text => 24L + text.Length * 2L,
        DirectoryState state => 24L + state.Name.Length * 2L + 160, SpaceCachedFile => 0, _ => _nameLength });
    internal void ReleaseReferences()
    {
        if (Directory is { } state)
        { state.Children?.Detach(); state.Children = null; state.Cache = null; state.AggregateSource = null; state.AggregateExcluded = null; state.FileCount = 0; }
        else _name = string.Empty;
        Parent = null; Bytes = 0; SetFlag(64, true);
    }
}

/// <summary>Session-owned, memory-only filename blocks. UTF-8 avoids one UTF-16 object per
/// file. Nodes reference their block directly, so old/deleted trees remain collectible.
/// Unpaired UTF-16 surrogates are retained verbatim instead of changing filesystem paths.</summary>
internal sealed class SpaceNameStore
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private byte[]? _block;
    private int _used;
    internal void Pack(SpaceNode node, string name)
    {
        if (node.IsDirectory) return;
        int count;
        try { count = Utf8.GetByteCount(name); }
        catch (EncoderFallbackException) { return; }
        if (count > ushort.MaxValue) return;
        if (_block is null || _used + count > _block.Length) { _block = new byte[65536]; _used = 0; }
        Utf8.GetBytes(name.AsSpan(), _block.AsSpan(_used, count));
        node.PackName(_block, _used, count); _used += count;
    }
    internal void Release() { _block = null; _used = 0; }
}

public sealed class SpaceScanSession(string path, long memoryBudgetBytes = SpaceScanSession.DefaultMemoryBudgetBytes,
    bool useDiskCache = true, SpaceCacheOptions? cacheOptions = null) : IDisposable
{
    // Whole-volume scans have no arbitrary index-size cutoff. A caller can explicitly impose
    // a budget; the normal UI streams files into compressed disk pages until completion.
    public const long DefaultMemoryBudgetBytes = long.MaxValue;
    public long MemoryBudgetBytes { get; } = Math.Max(0, memoryBudgetBytes);
    public bool UsesDiskCache => _cache is not null;
    public long CacheBytes => _cache?.Length ?? 0;
    public long CacheMaximumBytes => _cache?.MaximumBytes ?? 0;
    public string? CachePath => _cache?.Path;
    public string? CacheDirectory => _cache?.Root;
    private SpaceDiskCache? _cache;
    internal SpaceDiskCache? DiskCache => _cache;
    internal void InitializeCache()
    { if (useDiskCache && _cache is null) { _cache = new SpaceDiskCache(Root.FullPath, cacheOptions ?? new()); Root.AttachCache(_cache); Root.ReplaceChildren(new SpaceChildren(Root, _cache)); } }
    internal void FinishCache() => _cache?.Flush();
    internal void UpdateCachedNode(SpaceNode node) => _cache?.Update(node);
    internal void PinVisible(IEnumerable<SpaceNode> nodes) => _cache?.Pin(nodes);
    internal long NodeMemory(SpaceNode node) => UsesDiskCache && !node.IsDirectory ? 0 : node.EstimatedBytes;
    internal bool IsCachePath(string value) => CacheDirectory is { } root &&
        (value.Equals(root, StringComparison.OrdinalIgnoreCase) || value.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    public long EstimatedTreeBytes { get; internal set; }
    public string? LimitReason { get; internal set; }
    public string Method { get; internal set; } = "目录元数据";
    public double? ProgressPercent { get; internal set; }
    public string Phase { get; internal set; } = "读取目录元数据";
    private readonly SpaceNameStore _names = new();
    internal SpaceNode CreateNode(string name, bool directory, SpaceNode? parent = null)
    { var node = new SpaceNode(name, directory, parent); if (_cache is { } cache) node.AttachCache(cache); else _names.Pack(node, name); return node; }
    internal void ReleaseNameWriter() => _names.Release();
    // Only after the scanner has stopped and all views have switched sessions. Native
    // recycled rows can outlive a scan; they must not pin its complete parent/child graph
    // or filename blocks while WinUI schedules their final COM releases.
    internal void ReleaseIndex()
    {
        lock (SyncRoot)
        {
            var pending = new Stack<SpaceNode>(); pending.Push(Root);
            while (pending.TryPop(out var directory))
            {
                foreach (var child in directory.Children.MemoryItems)
                    if (child.IsDirectory) pending.Push(child); else child.ReleaseReferences();
                directory.ReleaseReferences();
            }
            _cache?.Dispose(); _cache = null; _names.Release(); EstimatedTreeBytes = Files = Folders = 0;
        }
    }
    public void Dispose() => ReleaseIndex();
    public object SyncRoot { get; } = new();
    public SpaceNode Root { get; } = new(Path.GetFullPath(path), true);
    public long Files { get; internal set; }
    public long Folders { get; internal set; }
    public long Skipped { get; internal set; }
    public string CurrentPath { get; internal set; } = path;
    public bool Complete { get; internal set; }
    public bool Cancelled { get; internal set; }
    public TimeSpan Elapsed { get; internal set; }
    public List<string> Errors { get; } = [];
    private readonly ManualResetEventSlim _pause = new(true);
    public void Pause(bool pause) { if (pause) _pause.Reset(); else _pause.Set(); }
    internal void Wait(CancellationToken token) => _pause.Wait(token);
}

/// <summary>Read-only, cancellable enumeration. Never follows junctions or hydrates cloud files.
/// One tree is shared by the map, list, navigation and report; UI snapshots hold SyncRoot briefly.</summary>
public static class SpaceScanService
{
    public static Task ScanAsync(SpaceScanSession session, bool includeStreams, CancellationToken token)
        => Task.Run(() =>
        {
            using var background = new ScanBackgroundScope();
            using var privileges = new SpaceReadOnlyDirectory.BackupScope();
            try { privileges.Run(() => Scan(session, includeStreams, token)); }
            finally
            {
                // Large scans promote short-lived MFT lookup tables and decoded strings
                // alongside the index. Collect once AFTER those stack frames have returned,
                // so the UI doesn't retain their fragmented heap until a later gen-2 GC.
                // Never trim the working set: that can push dirty pages into the pagefile.
                if (session.EstimatedTreeBytes >= 128L * 1024 * 1024)
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
        });

    // Lower the priority of cache pages and disk requests generated by this worker, rather
    // than changing machine-wide cache/pagefile settings. Always restore the pool thread.
    private sealed class ScanBackgroundScope : IDisposable
    {
        private readonly bool _changed = SetThreadPriority(GetCurrentThread(), 0x10000);
        public void Dispose() { if (_changed) SetThreadPriority(GetCurrentThread(), 0x20000); }
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetThreadPriority(IntPtr thread, int priority);
    }

    private static void Scan(SpaceScanSession session, bool includeStreams, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var batch = new List<SpaceNode>(256);
        var enumerationBuffer = new byte[64 * 1024];
        var streamBuffer = includeStreams ? new byte[64 * 1024] : [];
        void CheckBudget(long nodeBytes)
        {
            if (session.EstimatedTreeBytes + nodeBytes > session.MemoryBudgetBytes)
                throw new ScanLimitException("达到扫描内存上限；请选择较小的文件夹继续分析。");
            session.EstimatedTreeBytes += nodeBytes;
        }
        var pending = new Stack<(SpaceNode Node, bool Finish)>();
        pending.Push((session.Root, false));
        try
        {
            token.ThrowIfCancellationRequested();
            session.InitializeCache();
            if (session.IsCachePath(session.Root.FullPath)) throw new IOException("不能分析本工具的临时缓存目录。");
            if (SpaceNtfsScanner.TryScan(session, includeStreams, CheckBudget, token))
            { lock (session.SyncRoot) session.Complete = true; return; }
            // Reject a selected junction as well as junctions encountered below it.
            if ((File.GetAttributes(session.Root.FullPath) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("不能扫描链接或挂载点，请选择它的实际目标目录。");
            session.Root.Modified = Directory.GetLastWriteTime(session.Root.FullPath);
            while (pending.TryPop(out var work))
            {
                token.ThrowIfCancellationRequested();
                session.Wait(token);
                if (work.Finish) { lock (session.SyncRoot) work.Node.Complete = true; continue; }
                var directory = work.Node;
                string directoryPath = directory.FullPath;
                lock (session.SyncRoot) session.CurrentPath = directoryPath;
                pending.Push((directory, true));
                long bytes = 0, files = 0;
                void Flush()
                {
                    if (batch.Count == 0) return;
                    lock (session.SyncRoot)
                    {
                        int committed = 0; long committedBytes = 0, committedFiles = 0;
                        try
                        {
                            foreach (var item in batch)
                            { directory.Children.Add(item); committed++; if (!item.IsDirectory) { committedBytes += item.Bytes; committedFiles++; } }
                        }
                        finally
                        {
                            for (var ancestor = directory; ancestor is not null; ancestor = ancestor.Parent)
                            { ancestor.Bytes += committedBytes; ancestor.FileCount += committedFiles; }
                            session.Files += committedFiles; batch.RemoveRange(0, committed);
                        }
                    }
                    bytes = 0; files = 0;
                }
                try
                {
                    foreach (var entry in SpaceReadOnlyDirectory.Enumerate(directoryPath, enumerationBuffer, token))
                    {
                        token.ThrowIfCancellationRequested(); session.Wait(token);
                        if (session.CacheDirectory is { } cacheRoot && entry.Name.Equals(Path.GetFileName(cacheRoot), StringComparison.OrdinalIgnoreCase)
                            && session.IsCachePath(Path.Combine(directoryPath, entry.Name))) continue;
                        bool isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
                        var node = session.CreateNode(entry.Name, isDirectory, directory);
                        CheckBudget(session.NodeMemory(node));
                        node.Modified = entry.Modified;
                        node.IsLink = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
                        node.Complete = !isDirectory || node.IsLink;
                        node.Bytes = isDirectory ? 0 : entry.Bytes;
                        try
                        {
                            if (node.IsDirectory)
                            {
                                if (node.IsLink) { lock (session.SyncRoot) session.Skipped++; }
                                else { pending.Push((node, false)); lock (session.SyncRoot) session.Folders++; }
                            }
                            else
                            {
                                // Optional streams use another access-time-suppressed metadata
                                // handle, never FindFirstStream's unowned/opening path.
                                if (includeStreams && (entry.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) == 0)
                                {
                                    try { node.Bytes += SpaceReadOnlyDirectory.NamedStreamBytes(node.FullPath, streamBuffer); }
                                    catch (IOException ex) { RecordError(session, node.FullPath, ex); }
                                }
                                bytes += node.Bytes; files++;
                            }
                            batch.Add(node);
                            if (batch.Count >= 256) Flush();
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { RecordError(session, node.FullPath, ex); }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A denied root is a failed scan, not a successful empty drive.
                    if (ReferenceEquals(directory, session.Root)) throw;
                    RecordError(session, directory.FullPath, ex);
                }
                finally { Flush(); }
            }
            lock (session.SyncRoot) { session.Complete = true; session.ProgressPercent = 100; }
        }
        catch (OperationCanceledException) { lock (session.SyncRoot) { session.Cancelled = true; session.Root.Complete = false; } }
        catch (ScanLimitException ex) { lock (session.SyncRoot) { session.LimitReason = ex.Message; session.Root.Complete = false; } }
        catch (SpaceCacheLimitException ex) { lock (session.SyncRoot) { session.Complete = false; session.LimitReason = ex.Message; session.Root.Complete = false; } }
        finally
        {
            try { session.FinishCache(); }
            catch (SpaceCacheLimitException ex) { session.Complete = false; session.LimitReason = ex.Message; session.Root.Complete = false; }
            session.ReleaseNameWriter(); lock (session.SyncRoot) session.Elapsed = watch.Elapsed;
        }
    }

    private sealed class ScanLimitException(string message) : Exception(message);
    private static void RecordError(SpaceScanSession session, string path, Exception ex)
    {
        lock (session.SyncRoot)
        { session.Skipped++; if (session.Errors.Count < 200) session.Errors.Add($"{path}：{ex.Message}"); }
    }

    public static IEnumerable<SpaceNode> Descendants(SpaceNode node)
    {
        yield return node;
        var pending = new Stack<IEnumerator<SpaceNode>>();
        if (node.IsDirectory) pending.Push(node.Children.GetEnumerator());
        try
        {
            while (pending.TryPeek(out var iterator))
            {
                if (!iterator.MoveNext()) { pending.Pop().Dispose(); continue; }
                var current = iterator.Current; yield return current;
                if (current.IsDirectory) pending.Push(current.Children.GetEnumerator());
            }
        }
        finally { foreach (var iterator in pending) iterator.Dispose(); }
    }

    private static IEnumerable<SpaceNode> MemoryDescendants(SpaceNode node)
    {
        var pending = new Stack<SpaceNode>(); pending.Push(node);
        while (pending.TryPop(out var current))
        { yield return current; if (current.IsDirectory) foreach (var child in current.Children.MemoryItems) pending.Push(child); }
    }

    public static List<SpaceNode> Largest(IEnumerable<SpaceNode> nodes, int maximum, Func<SpaceNode, long> weight,
        out int count, out long total, Func<SpaceNode, bool>? include = null)
    {
        var top = new PriorityQueue<SpaceNode, (long Weight, long Order)>();
        count = 0; total = 0; long order = 0;
        foreach (var node in nodes)
        {
            if (include is not null && !include(node)) continue;
            long value = weight(node); count++; total += value;
            top.Enqueue(node, (value, -order++)); if (top.Count > maximum) top.Dequeue();
        }
        return top.UnorderedItems.OrderByDescending(e => e.Priority.Weight).ThenByDescending(e => e.Priority.Order).Select(e => e.Element).ToList();
    }

    public static void RemoveNode(SpaceScanSession session, SpaceNode node)
    {
        lock (session.SyncRoot)
        {
            if (node.Parent is null || !node.Parent.Children.Remove(node)) return;
            long removedMemory = 0, removedFolders = 0;
            foreach (var item in MemoryDescendants(node))
            { removedMemory += session.NodeMemory(item); if (item.IsDirectory && !item.IsLink) removedFolders++; }
            for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            { parent.Bytes = Math.Max(0, parent.Bytes - node.Bytes); parent.FileCount = Math.Max(0, parent.FileCount - node.FileCount); }
            session.Files = Math.Max(0, session.Files - node.FileCount);
            session.Folders = Math.Max(0, session.Folders - removedFolders);
            session.EstimatedTreeBytes = Math.Max(0, session.EstimatedTreeBytes - removedMemory);
        }
    }

    public static SpaceNode? FindNearestDirectory(SpaceNode root, string path)
    {
        string relative = Path.GetRelativePath(root.FullPath, path);
        if (relative == ".") return root;
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(relative)) return null;
        var current = root;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = current.Children.FirstOrDefault(n => n.IsDirectory && !n.IsLink && n.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (next is null) break; current = next;
        }
        return current;
    }

    public static async Task RefreshAsync(SpaceScanSession session, SpaceNode node, bool includeStreams, CancellationToken token)
    {
        // Old and replacement trees coexist until commit. Budget their peak, not just the final tree.
        var fresh = new SpaceScanSession(node.FullPath, session.MemoryBudgetBytes - session.EstimatedTreeBytes, session.UsesDiskCache,
            session.UsesDiskCache ? new SpaceCacheOptions { Directory = session.CacheDirectory, MaximumBytes = session.CacheMaximumBytes } : null);
        try
        {
            await ScanAsync(fresh, includeStreams, token);
            token.ThrowIfCancellationRequested();
            if (fresh.LimitReason is { } reason) throw new InvalidOperationException(reason + " 原扫描结果已保留。");
            if (!fresh.Complete) return;
            await Task.Run(() =>
            {
                lock (session.SyncRoot)
                {
                    var tagged = MemoryDescendants(node).Where(n => n.Tag != 0).ToList();
                    if (session.DiskCache is { } sourceCache) tagged.AddRange(sourceCache.TaggedUnder(node));
                    var tags = tagged.ToDictionary(n => n.FullPath, n => n.Tag, StringComparer.OrdinalIgnoreCase);
                    if (tags.Count != 0) foreach (var child in Descendants(fresh.Root)) if (tags.TryGetValue(child.FullPath, out int tag)) child.Tag = tag;
                    long oldFolders = MemoryDescendants(node).LongCount(n => n.IsDirectory && !n.IsLink) - 1;
                    long oldMemory = MemoryDescendants(node).Skip(1).Sum(session.NodeMemory);
                    long delta = fresh.Root.Bytes - node.Bytes, fileDelta = fresh.Root.FileCount - node.FileCount;
                    // Re-encode into the surviving store before committing. The replacement's
                    // own cache can then be deleted without breaking navigation/deletion.
                    var pending = new Stack<SpaceNode>(); pending.Push(fresh.Root);
                    while (pending.TryPop(out var directory))
                    {
                        var replacement = new SpaceChildren(directory, session.DiskCache);
                        foreach (var child in directory.Children)
                        { replacement.Add(child); if (child.IsDirectory) pending.Push(child); }
                        if (session.DiskCache is { } targetCache) directory.AttachCache(targetCache);
                        directory.ReplaceChildren(replacement);
                    }
                    session.FinishCache();
                    node.Children.Clear();
                    node.ReplaceChildren(fresh.Root.Children);
                    fresh.Root.ReplaceChildren(new SpaceChildren(fresh.Root, null));
                    for (var ancestor = node; ancestor is not null; ancestor = ancestor.Parent)
                    { ancestor.Bytes += delta; ancestor.FileCount += fileDelta; }
                    session.Files += fileDelta;
                    session.EstimatedTreeBytes += fresh.EstimatedTreeBytes - oldMemory;
                    session.Folders += fresh.Folders - oldFolders;
                    session.Skipped += fresh.Skipped;
                    foreach (var error in fresh.Errors.Take(Math.Max(0, 200 - session.Errors.Count))) session.Errors.Add(error);
                }
            }, token);
        }
        finally { fresh.ReleaseIndex(); }
    }

}
