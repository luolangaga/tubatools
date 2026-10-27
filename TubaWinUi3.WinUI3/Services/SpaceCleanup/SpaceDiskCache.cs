using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TubaWinUi3.Services.SpaceCleanup;

public sealed record SpaceCacheOptions
{
    public string? Directory { get; init; }
    public long MaximumBytes { get; init; } = long.MaxValue;
    public long ReserveFreeBytes { get; init; }
}

internal sealed class SpaceCacheLimitException(string message) : Exception(message);

/// <summary>Disposable compressed metadata pages; no filenames/contents database, WAL,
/// memory mapping, per-record seeks, or durable flushes. Only directory heads stay in RAM.</summary>
internal sealed class SpaceDiskCache : IDisposable
{
    internal const int PageSize = 256 * 1024;
    private const int PageBits = 18, ResidentPages = 8;
    private readonly object _gate = new();
    private readonly SafeFileHandle _handle;
    private readonly SpaceCacheOptions _options;
    private readonly List<Page> _pages = [];
    private readonly byte[] _writing = new byte[PageSize];
    private readonly byte[] _compressed = new byte[BrotliEncoder.GetMaxCompressedLength(PageSize)];
    private readonly Dictionary<int, (byte[] Bytes, long Used)> _resident = [];
    private Dictionary<long, SpaceNode> _pinned = [];
    private readonly Dictionary<long, Change> _changes = [];
    private readonly Dictionary<long, SpaceChildren> _taggedOwners = [];
    private int _used;
    private long _clock, _length;
    private readonly record struct Page(long Offset, int Length, int RawLength);
    private readonly record struct Change(long Bytes, int Tag, bool Deleted);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal string Root { get; }
    internal string Path { get; }
    internal long Length { get { lock (_gate) return _length; } }
    internal long RawBytes { get; private set; }
    internal long MaximumBytes { get; }
    internal bool IsDisposed { get; private set; }

    internal SpaceDiskCache(string scanRoot, SpaceCacheOptions options)
    {
        _options = options;
        string root = options.Directory ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TubaWinUi3.SpaceCache");
        // Prefer the user's temp directory. If it is nearly full, try another fixed disk.
        if (options.Directory is null && Free(root) < 1024L * 1024 * 1024)
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed).OrderByDescending(d => Free(d.Name)))
            {
                if (Free(drive.Name) < 1024L * 1024 * 1024) continue;
                string alternative = System.IO.Path.Combine(drive.Name, "TubaWinUi3.SpaceCache");
                try { System.IO.Directory.CreateDirectory(alternative); root = alternative; break; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        Root = System.IO.Path.GetFullPath(root);
        System.IO.Directory.CreateDirectory(Root);
        if ((File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("临时缓存目录不能是链接或挂载点。");
        long free = Free(Root);
        MaximumBytes = Math.Max(0, options.MaximumBytes);
        if (MaximumBytes < 4096 || (options.ReserveFreeBytes > 0 && free <= options.ReserveFreeBytes))
        { TryRemoveRoot(); throw new SpaceCacheLimitException("无法满足指定的缓存空间预算。"); }
        Path = System.IO.Path.Combine(Root, "scan-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            _handle = File.OpenHandle(Path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read,
                FileOptions.DeleteOnClose | FileOptions.RandomAccess);
        }
        catch { TryRemoveRoot(); throw; }
    }

    private static long Free(string path)
    {
        try { return new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(path))!).AvailableFreeSpace; }
        catch { return 0; }
    }
    internal long Append(SpaceChildren owner, SpaceNode node, long previous)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            string name = node.Name;
            int bytes; bool unicode = false;
            try { bytes = Utf8.GetByteCount(name); }
            catch (EncoderFallbackException) { unicode = true; bytes = name.Length * 2; }
            int maximum = 40 + bytes;
            if (maximum > PageSize) throw new IOException("文件名超过缓存记录的长度限制。");
            if (_used + maximum > PageSize) FlushPage();
            long address = ((long)(_pages.Count + 1) << PageBits) | (uint)_used;
            var record = _writing.AsSpan(_used);
            BinaryPrimitives.WriteInt64LittleEndian(record, previous); int offset = 8;
            record[offset++] = (byte)((unicode ? 128 : 0) | (node.IsLink ? 1 : 0) | (node.Tag << 1) | ((int)node.Modified.Kind << 4));
            WriteInteger(record, ref offset, (ulong)Math.Max(0, node.Bytes));
            WriteInteger(record, ref offset, (ulong)node.Modified.Ticks);
            WriteInteger(record, ref offset, (ulong)bytes);
            if (unicode) MemoryMarshal.AsBytes(name.AsSpan()).CopyTo(record[offset..]);
            else Utf8.GetBytes(name, record.Slice(offset, bytes));
            _used += offset + bytes; RawBytes += offset + bytes;
            node.AttachCachedFile(new SpaceCachedFile(owner, address, previous, name));
            return address;
        }
    }
    private static void WriteInteger(Span<byte> buffer, ref int offset, ulong value)
    { while (value >= 128) { buffer[offset++] = (byte)(value | 128); value >>= 7; } buffer[offset++] = (byte)value; }
    private static ulong ReadInteger(ReadOnlySpan<byte> buffer, ref int offset)
    {
        ulong result = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (offset >= buffer.Length) throw new IOException("缓存记录不完整。");
            byte value = buffer[offset++]; result |= (ulong)(value & 127) << shift;
            if ((value & 128) == 0) return result;
        }
        throw new IOException("缓存整数无效。");
    }
    internal (SpaceNode? Node, long Previous) Read(SpaceChildren owner, long address)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_pinned.TryGetValue(address, out var pinned) && !pinned.IsReleased)
                return (pinned, pinned.CachedFile!.Previous);
            int page = checked((int)(address >> PageBits)) - 1, start = (int)(address & (PageSize - 1));
            var data = PageBytes(page).AsSpan(start);
            long previous = BinaryPrimitives.ReadInt64LittleEndian(data); int offset = 8;
            byte flags = data[offset++];
            long bytes = checked((long)ReadInteger(data, ref offset));
            long ticks = checked((long)ReadInteger(data, ref offset));
            int count = checked((int)ReadInteger(data, ref offset));
            if (_changes.TryGetValue(address, out var changed))
            { if (changed.Deleted) return (null, previous); bytes = changed.Bytes; flags = (byte)((flags & ~14) | (changed.Tag << 1)); }
            var encoded = data.Slice(offset, count);
            string name = (flags & 128) != 0 ? new string(MemoryMarshal.Cast<byte, char>(encoded)) : Utf8.GetString(encoded);
            var node = new SpaceNode(name, false, owner.Owner)
            { Bytes = bytes, Modified = new DateTime(ticks, (DateTimeKind)((flags >> 4) & 3)), IsLink = (flags & 1) != 0, Complete = true, Tag = (flags >> 1) & 7 };
            node.AttachCachedFile(new SpaceCachedFile(owner, address, previous, name));
            return (node, previous);
        }
    }
    private byte[] PageBytes(int index)
    {
        if (index == _pages.Count) return _writing;
        if (_resident.TryGetValue(index, out var cached)) { _resident[index] = (cached.Bytes, ++_clock); return cached.Bytes; }
        var page = _pages[index];
        byte[] bytes;
        if (_resident.Count >= ResidentPages)
        {
            int oldest = _resident.MinBy(p => p.Value.Used).Key;
            bytes = _resident[oldest].Bytes; _resident.Remove(oldest);
        }
        else bytes = new byte[PageSize];
        var input = _compressed.AsSpan(0, page.Length);
        int read = 0;
        while (read < input.Length)
        { int next = RandomAccess.Read(_handle, input[read..], page.Offset + read); if (next == 0) throw new IOException("缓存页不完整。"); read += next; }
        if (!BrotliDecoder.TryDecompress(input, bytes.AsSpan(0, page.RawLength), out int written) || written != page.RawLength)
            throw new IOException("缓存页解压失败。");
        _resident[index] = (bytes, ++_clock); return bytes;
    }
    internal void Flush()
    { lock (_gate) if (!IsDisposed) FlushPage(); }
    private void FlushPage()
    {
        if (_used == 0) return;
        if (!BrotliEncoder.TryCompress(_writing.AsSpan(0, _used), _compressed, out int bytes, quality: 3, window: PageBits))
            throw new IOException("压缩缓存页失败。");
        if (_length > MaximumBytes - bytes || (_options.ReserveFreeBytes > 0 && Free(Root) - bytes < _options.ReserveFreeBytes))
            throw new SpaceCacheLimitException("磁盘缓存已达到空间预算或磁盘剩余空间不足，已保留部分结果；退出工具会自动删除缓存。");
        RandomAccess.Write(_handle, _compressed.AsSpan(0, bytes), _length);
        _pages.Add(new Page(_length, bytes, _used)); _length += bytes; _used = 0;
    }
    internal void Update(SpaceNode node, bool tagOnly = false)
    {
        if (node.CachedFile is not { } entry) return;
        lock (_gate)
        {
            if (IsDisposed) return;
            _changes.TryGetValue(entry.Address, out var old);
            long bytes = tagOnly && _changes.ContainsKey(entry.Address) ? old.Bytes : node.Bytes;
            int tag = !tagOnly && _changes.ContainsKey(entry.Address) ? old.Tag : node.Tag;
            _changes[entry.Address] = new(bytes, tag, false);
            if (tag != 0) _taggedOwners[entry.Address] = entry.Owner; else _taggedOwners.Remove(entry.Address);
            if (_pinned.TryGetValue(entry.Address, out var pinned)) { pinned.Bytes = bytes; pinned.SetCachedTag(tag); }
        }
    }
    internal bool IsDeleted(long address)
    { lock (_gate) return _changes.TryGetValue(address, out var change) && change.Deleted; }
    internal void Delete(SpaceNode node)
    {
        lock (_gate)
        { long address = node.CachedFile!.Address; _changes[address] = new(node.Bytes, node.Tag, true); _pinned.Remove(address); _taggedOwners.Remove(address); }
    }
    internal List<SpaceNode> TaggedUnder(SpaceNode root)
    {
        lock (_gate)
        {
            var result = new List<SpaceNode>();
            foreach (var (address, owner) in _taggedOwners)
                for (var parent = owner.Owner; parent is not null; parent = parent.Parent)
                    if (ReferenceEquals(parent, root)) { if (Read(owner, address).Node is { } node) result.Add(node); break; }
            return result;
        }
    }
    internal void Pin(IEnumerable<SpaceNode> nodes)
    {
        lock (_gate)
        {
            var next = new Dictionary<long, SpaceNode>();
            foreach (var node in nodes)
                if (node.CachedFile is { } entry && ReferenceEquals(entry.Owner.Cache, this) && !node.IsReleased)
                { next[entry.Address] = node; if (next.Count >= 8192) break; }
            _pinned = next;
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (IsDisposed) return;
            IsDisposed = true; _pinned.Clear(); _resident.Clear(); _changes.Clear(); _taggedOwners.Clear();
            _handle.Dispose(); TryRemoveRoot();
        }
    }
    private void TryRemoveRoot()
    { try { if ((File.GetAttributes(Root) & FileAttributes.ReparsePoint) == 0) System.IO.Directory.Delete(Root, false); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}

internal sealed class SpaceCachedFile(SpaceChildren owner, long address, long previous, string name)
{
    internal SpaceChildren Owner { get; } = owner;
    internal int Generation { get; } = owner.Generation;
    internal long Address { get; } = address;
    internal long Previous { get; } = previous;
    internal string Name { get; } = name;
    internal bool IsReleased => Generation != Owner.Generation || Owner.Cache is not { IsDisposed: false } cache || cache.IsDeleted(Address);
}
