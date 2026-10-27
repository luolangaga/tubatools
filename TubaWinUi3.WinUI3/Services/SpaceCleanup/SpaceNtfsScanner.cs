using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Collections;

namespace TubaWinUi3.Services.SpaceCleanup;

/// <summary>Whole NTFS volumes: read MFT metadata without per-directory/file opens,
/// timestamp changes or journal creation. File entries stream into the session's compressed cache.</summary>
internal static class SpaceNtfsScanner
{
    private const ulong SegmentMask = 0x0000FFFFFFFFFFFF;
    internal readonly record struct Link(ulong Parent, string Name);
    internal struct Record
    {
        internal ulong Reference, Base;
        internal bool Directory, Reparse, HasData, HasAttributeList;
        internal long Bytes, Streams;
        internal DateTime Modified;
        internal Link? Name;
        internal List<Link>? MoreNames;
        internal List<ulong>? AttributeRecords;
        internal readonly Links Names => new(Name, MoreNames);
    }
    // Common records have one name. Neither decoding nor enumerating it allocates an
    // extra object per MFT entry (millions on a development workstation).
    internal readonly struct Links(Link? first, List<Link>? more) : IEnumerable<Link>
    {
        public Enumerator GetEnumerator() => new(first, more);
        IEnumerator<Link> IEnumerable<Link>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        internal struct Enumerator(Link? first, List<Link>? more) : IEnumerator<Link>
        {
            private int _position = -2;
            public Link Current => _position == -1 ? first!.Value : more![_position];
            object IEnumerator.Current => Current;
            public bool MoveNext()
            {
                if (_position == -2) { _position = -1; if (first.HasValue) return true; }
                return ++_position < (more?.Count ?? 0);
            }
            public void Dispose() { }
            public void Reset() => _position = -2;
        }
    }
    private static long DataBytes(in Record record, List<Record>? extra)
    {
        if (!record.HasData && extra is not null)
            foreach (var item in extra) if (item.HasData) return item.Bytes;
        return record.Bytes;
    }

    internal static bool TryScan(SpaceScanSession session, bool streams, Action<long> budget, CancellationToken token)
    {
        string path = session.Root.FullPath;
        string root = Path.GetPathRoot(path) ?? "";
        if (root.Length != 3 || !Path.TrimEndingDirectorySeparator(path).Equals(Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase) ||
            !new DriveInfo(root).DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase)) return false;
        using var volume = new SpaceNtfsVolume(root);
        var directories = new Dictionary<ulong, (SpaceNode Node, ulong Parent)>();
        var reachable = new HashSet<SpaceNode>();
        var extensions = new Dictionary<ulong, List<Record>>();
        var extendedFiles = new Dictionary<ulong, (Record Record, List<SpaceNode> Nodes)>();
        ulong rootReference = 0;
        session.Method = volume.RawRecords ? "NTFS MFT（无缓存只读）" : "NTFS MFT（低优先级只读）";
        void Error(Exception ex)
        { lock (session.SyncRoot) { session.Skipped++; if (session.Errors.Count < 200) session.Errors.Add(ex.Message); } }
        volume.RecordSkipped = Error;
        void Progress(ulong index, bool files)
        {
            if ((index & 255) != 0) return;
            lock (session.SyncRoot)
            {
                double fraction = (double)index / Math.Max(1, volume.Records);
                if (!volume.RawRecords && files) fraction = 1 - fraction;
                session.ProgressPercent = (files ? 30 : 0) + (files ? 68 : 30) * fraction;
                session.CurrentPath = $"MFT · 文件记录 {index:N0} / {volume.Records:N0}";
            }
        }
        Record Decode(Memory<byte> bytes, ulong index) => Parse(bytes.Span, index, streams, volume.RawRecords);
        SpaceNode? File(Link link, Record record, long bytes)
        {
            if (!directories.TryGetValue(link.Parent, out var entry) || !reachable.Contains(entry.Node) || entry.Node.IsLink) return null;
            var parent = entry.Node;
            var node = session.CreateNode(link.Name, false, parent);
            budget(session.NodeMemory(node));
            node.Bytes = bytes; node.Modified = record.Modified; node.IsLink = record.Reparse; node.Complete = true;
            lock (session.SyncRoot)
            {
                parent.Children.Add(node); session.Files++;
                for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Parent) { ancestor.Bytes += bytes; ancestor.FileCount++; }
            }
            return node;
        }
        bool finished = false;
        try
        {
            lock (session.SyncRoot) { session.Phase = "读取 MFT 目录索引"; session.ProgressPercent = 0; }
            // Two sequential passes avoid hundreds of thousands of random parent-record reads.
            // Each pass uses the same 1 MiB unbuffered read surface, never the file cache.
            foreach (var (index, bytes) in volume.EnumerateDirectories(token))
            {
                session.Wait(token); Progress(index, false);
                if (index != 5 && index < 16) continue;
                if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.Span) != 0x454C4946 || (BinaryPrimitives.ReadUInt16LittleEndian(bytes.Span[22..]) & 3) != 3) continue;
                try
                {
                    var record = Decode(bytes, index); if (record.Base != 0) continue;
                    if (record.Name is null && record.AttributeRecords is not null)
                        foreach (ulong reference in record.AttributeRecords)
                        {
                            var extraBytes = volume.ReadRecord(reference);
                            var extra = Parse(extraBytes, reference & SegmentMask, streams, volume.RawRecords);
                            if (extra.Reference == reference && extra.Base == record.Reference && extra.Name is not null) { record.Name = extra.Name; break; }
                        }
                    if (index == 5)
                    { rootReference = record.Reference; session.Root.Modified = record.Modified; directories[rootReference] = (session.Root, rootReference); continue; }
                    if (record.Name is not { } name) { Error(new IOException($"MFT 目录 #{index} 缺少可读取的名称。")); continue; }
                    var node = session.CreateNode(name.Name, true);
                    budget(session.NodeMemory(node)); node.Modified = record.Modified; node.IsLink = record.Reparse;
                    directories[record.Reference] = (node, name.Parent);
                    lock (session.SyncRoot) session.Folders++;
                }
                catch (IOException ex) { Error(new IOException($"MFT #{index}：{ex.Message}", ex)); }
            }
            if (rootReference == 0) throw new IOException("无法读取 NTFS 根目录记录。");
            lock (session.SyncRoot)
            {
                session.Folders = 0;
                foreach (var (reference, entry) in directories)
                    if (reference != rootReference && directories.TryGetValue(entry.Parent, out var parent) && !parent.Node.IsLink && parent.Node != entry.Node)
                    { entry.Node.Parent = parent.Node; parent.Node.Children.Add(entry.Node); }
                var pending = new Stack<SpaceNode>(); pending.Push(session.Root);
                while (pending.TryPop(out var directory))
                {
                    if (session.CacheDirectory is { } cacheRoot && directory.Name.Equals(Path.GetFileName(cacheRoot), StringComparison.OrdinalIgnoreCase)
                        && session.IsCachePath(directory.FullPath)) { directory.Parent?.Children.Remove(directory); continue; }
                    if (!reachable.Add(directory)) continue;
                    if (directory != session.Root) { session.Folders++; if (directory.IsLink) session.Skipped++; }
                    if (!directory.IsLink) foreach (var child in directory.Children) pending.Push(child);
                }
                session.Phase = "读取 MFT 文件大小"; session.ProgressPercent = 30;
            }
            foreach (var (index, bytes) in volume.Enumerate(token))
            {
                token.ThrowIfCancellationRequested(); session.Wait(token); Progress(index, true);
                if (index < 16 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.Span) != 0x454C4946 || (BinaryPrimitives.ReadUInt16LittleEndian(bytes.Span[22..]) & 1) == 0) continue;
                Record record;
                try { record = Decode(bytes, index); }
                catch (IOException ex) { Error(new IOException($"MFT #{index}：{ex.Message}", ex)); continue; }
                if (record.Base != 0)
                {
                    if (!extensions.TryGetValue(record.Base, out var list)) extensions[record.Base] = list = [];
                    list.Add(record); continue;
                }
                if (record.Directory) continue;
                extensions.TryGetValue(record.Reference, out var additional);
                long count = DataBytes(record, additional);
                if (streams) count += record.Streams + (additional?.Sum(r => r.Streams) ?? 0);
                List<SpaceNode>? nodes = record.HasAttributeList || additional is not null ? [] : null;
                foreach (var link in record.Names) { var node = File(link, record, count); if (node is not null) nodes?.Add(node); }
                if (nodes is not null) extendedFiles[record.Reference] = (record, nodes);
            }
            int extendedDone = 0;
            lock (session.SyncRoot) { session.Phase = "整理硬链接与扩展属性"; session.ProgressPercent = 98; }
            foreach (var (reference, group) in extendedFiles)
            {
                token.ThrowIfCancellationRequested(); session.Wait(token);
                lock (session.SyncRoot) session.ProgressPercent = 98 + 2.0 * ++extendedDone / Math.Max(1, extendedFiles.Count);
                if (!extensions.TryGetValue(reference, out var extra)) continue;
                long bytes = DataBytes(group.Record, extra);
                if (streams) bytes += group.Record.Streams + extra.Sum(r => r.Streams);
                foreach (var node in group.Nodes)
                {
                    long delta = bytes - node.Bytes;
                    lock (session.SyncRoot) { node.Bytes = bytes; session.UpdateCachedNode(node); for (var parent = node.Parent; parent is not null; parent = parent.Parent) parent.Bytes += delta; }
                }
                var names = group.Record.Names.ToHashSet();
                foreach (var record in extra) foreach (var link in record.Names) if (names.Add(link)) File(link, group.Record, bytes);
            }
            finished = true;
            lock (session.SyncRoot) { session.ProgressPercent = 100; session.Phase = "扫描完成"; }
        }
        finally { lock (session.SyncRoot) foreach (var entry in directories.Values) entry.Node.Complete = finished; }
        return true;
    }

    internal static Record Parse(Span<byte> record, ulong index, bool streams, bool raw = false)
    {
        if (record.Length < 48 || BinaryPrimitives.ReadUInt32LittleEndian(record) != 0x454C4946) throw new IOException("无效的 NTFS FILE 记录。");
        if (raw)
        {
            int usa = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]), count = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
            if (count < 2 || usa < 8 || usa + count * 2 > record.Length || record.Length % (count - 1) != 0) throw new IOException("NTFS 多扇区修复数组无效。");
            int sector = record.Length / (count - 1); ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(record[usa..]);
            for (int i = 1; i < count; i++)
            {
                int end = i * sector - 2;
                if (BinaryPrimitives.ReadUInt16LittleEndian(record[end..]) != sequence) throw new IOException("NTFS 文件记录正在更新或扇区校验失败。");
                record.Slice(usa + i * 2, 2).CopyTo(record.Slice(end, 2));
            }
        }
        // FSCTL_GET_NTFS_FILE_RECORD supplies the driver's logical, already decoded record.
        // Reapplying raw-sector MST fixups here corrupts dirty cached records with an old USA.
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record[22..]);
        if ((flags & 1) == 0) throw new IOException("NTFS 文件记录已释放。");
        var result = new Record { Reference = (index & SegmentMask) | (ulong)BinaryPrimitives.ReadUInt16LittleEndian(record[16..]) << 48,
            Base = BinaryPrimitives.ReadUInt64LittleEndian(record[32..]), Directory = (flags & 2) != 0 };
        int used = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(record[24..]));
        int position = BinaryPrimitives.ReadUInt16LittleEndian(record[20..]);
        if (used > record.Length || position < 48 || position >= used) throw new IOException("NTFS 属性区边界无效。");
        bool standard = false;
        while (position <= used - 8)
        {
            var attribute = record[position..used]; uint type = BinaryPrimitives.ReadUInt32LittleEndian(attribute);
            if (type == uint.MaxValue) break;
            int size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(attribute[4..]));
            if (size < 24 || size > attribute.Length || (size & 7) != 0) throw new IOException("NTFS 属性长度无效。");
            attribute = attribute[..size];
            bool nonResident = attribute[8] != 0; int nameLength = attribute[9];
            if (type == 0x20) result.HasAttributeList = true;
            if (type == 0x80 && (nameLength == 0 || streams))
            {
                long bytes;
                if (nonResident)
                {
                    if (size < 64) throw new IOException("NTFS 非驻留数据属性不完整。");
                    if (BinaryPrimitives.ReadInt64LittleEndian(attribute[16..]) != 0) { position += size; continue; }
                    bytes = Math.Max(0, BinaryPrimitives.ReadInt64LittleEndian(attribute[48..]));
                }
                else bytes = BinaryPrimitives.ReadUInt32LittleEndian(attribute[16..]);
                if (nameLength == 0) { result.Bytes = bytes; result.HasData = true; } else result.Streams += bytes;
            }
            if (!nonResident && type is 0x10 or 0x30)
            {
                int valueLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(attribute[16..]));
                int valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[20..]);
                if (valueOffset < 24 || valueLength > size - valueOffset) throw new IOException("NTFS 驻留属性边界无效。");
                var value = attribute.Slice(valueOffset, valueLength);
                if (type == 0x10 && value.Length >= 36)
                { result.Modified = Time(BinaryPrimitives.ReadInt64LittleEndian(value[8..])); result.Reparse = (BinaryPrimitives.ReadUInt32LittleEndian(value[32..]) & 0x400) != 0; standard = true; }
                if (type == 0x30 && value.Length >= 66 && value[65] != 2)
                {
                    int characters = value[64];
                    if (characters * 2 > value.Length - 66) throw new IOException("NTFS 文件名长度无效。");
                    var link = new Link(BinaryPrimitives.ReadUInt64LittleEndian(value), new string(MemoryMarshal.Cast<byte, char>(value.Slice(66, characters * 2))));
                    if (result.Name is null) result.Name = link;
                    else if (result.Name != link) (result.MoreNames ??= []).Add(link);
                    if (!result.HasData) result.Bytes = Math.Max(0, BinaryPrimitives.ReadInt64LittleEndian(value[48..]));
                    if (!standard) { result.Modified = Time(BinaryPrimitives.ReadInt64LittleEndian(value[16..])); result.Reparse = (BinaryPrimitives.ReadUInt32LittleEndian(value[56..]) & 0x400) != 0; }
                }
            }
            if (!nonResident && type == 0x20)
            {
                int offset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[20..]);
                int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(attribute[16..]));
                if (offset < 24 || length > size - offset) throw new IOException("NTFS 属性列表边界无效。");
                var list = attribute.Slice(offset, length);
                while (list.Length >= 26)
                {
                    uint typeCode = BinaryPrimitives.ReadUInt32LittleEndian(list);
                    if (typeCode is 0 or uint.MaxValue) break;
                    int entryLength = BinaryPrimitives.ReadUInt16LittleEndian(list[4..]);
                    if (entryLength < 26 || entryLength > list.Length) throw new IOException("NTFS 属性列表项边界无效。");
                    ulong reference = BinaryPrimitives.ReadUInt64LittleEndian(list[16..]);
                    if (reference != result.Reference && (result.AttributeRecords?.Contains(reference) != true)) (result.AttributeRecords ??= []).Add(reference);
                    list = list[entryLength..];
                }
            }
            position += size;
        }
        return result;
    }
    private static DateTime Time(long time) => time >= 0 && time <= 2650467743999999999L ? DateTime.FromFileTimeUtc(time).ToLocalTime() : DateTime.MinValue;
}
