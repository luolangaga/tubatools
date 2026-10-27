using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TubaWinUi3.Services.SpaceCleanup;

/// <summary>Read-only MFT access: unbuffered physical reads for plaintext NTFS, read-only
/// metadata queries at background memory/I/O priority for encrypted volumes. Never performs
/// raw reads through the mounted volume, which can implicitly flush dirty metadata.</summary>
internal sealed class SpaceNtfsVolume : IDisposable
{
    internal readonly record struct Run(long Vcn, long Lcn, long Clusters);
    private readonly SafeFileHandle _volume;
    private readonly SafeFileHandle? _disk;
    private readonly long _partitionOffset, _partitionLength;
    private readonly List<Run> _runs = [];
    private readonly byte[] _buffer = new byte[1024 * 1024];
    private IntPtr _native;
    private readonly int _sector, _cluster;
    private bool _cached;
    private readonly byte[] _recordInput = new byte[8];
    private byte[]? _recordOutput;
    internal bool RawRecords => !_cached;
    internal Action<Exception>? RecordSkipped { get; set; }
    internal int RecordSize { get; }
    internal long Length { get; }
    internal long Records => Length / RecordSize;

    internal SpaceNtfsVolume(string root)
    {
        _volume = CreateFileW(@"\\.\" + root[..2], 0x80000000, 7, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (_volume.IsInvalid) { _volume.Dispose(); throw new IOException("NTFS 卷级只读扫描需要管理员权限。", new Win32Exception(Marshal.GetLastWin32Error())); }
        try
        {
            var info = new byte[128];
            Control(0x90064, [], info);
            var extents = new byte[512]; Control(0x560000, [], extents);
            if (BinaryPrimitives.ReadUInt32LittleEndian(extents) != 1) throw new IOException("此卷跨越多个物理磁盘，当前无写入扫描不支持该映射。");
            uint disk = BinaryPrimitives.ReadUInt32LittleEndian(extents.AsSpan(8));
            _partitionOffset = BinaryPrimitives.ReadInt64LittleEndian(extents.AsSpan(16));
            _partitionLength = BinaryPrimitives.ReadInt64LittleEndian(extents.AsSpan(24));
            _disk = CreateFileW(@"\\.\PhysicalDrive" + disk, 0x80000000, 7, IntPtr.Zero, 3, 0x20000000, IntPtr.Zero);
            if (_disk.IsInvalid) throw new IOException("无法打开物理磁盘的只读句柄。", new Win32Exception(Marshal.GetLastWin32Error()));
            _sector = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(40)));
            _cluster = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(44)));
            RecordSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(48)));
            Length = BinaryPrimitives.ReadInt64LittleEndian(info.AsSpan(56));
            if (_sector < 512 || _sector > 65536 || _cluster < _sector || RecordSize < 512 || RecordSize > 65536 || Length < RecordSize * 6L)
                throw new IOException("不支持的 NTFS 扇区或记录大小。");
            _native = VirtualAlloc(IntPtr.Zero, (nuint)_buffer.Length, 0x3000, 4);
            if (_native == IntPtr.Zero) throw new OutOfMemoryException();
            ReadPhysical(0, _sector);
            var boot = new byte[16]; Marshal.Copy(_native, boot, 0, boot.Length);
            if (!boot.AsSpan(3, 8).SequenceEqual("NTFS    "u8))
            {
                // An unlocked BitLocker volume is plaintext through NTFS, encrypted through
                // PhysicalDrive. Use read-only MFT queries, never volume-sector reads that
                // force NTFS to flush dirty metadata. The worker runs at background memory/
                // I/O priority so these cached pages lose residency before foreground data.
                _cached = true;
                return;
            }
            // Bootstrap from the on-disk MFT start cluster, not cached filesystem records.
            // Never flush the filesystem to manufacture a fresh snapshot.
            var pending = new Queue<ulong>(); pending.Enqueue(0);
            var seen = new HashSet<ulong>();
            ulong baseReference = 0;
            while (pending.TryDequeue(out ulong reference))
            {
                if (!seen.Add(reference & 0x0000FFFFFFFFFFFF)) continue;
                if (seen.Count > 4096) throw new IOException("MFT 扩展映射过于复杂，未使用缓存扫描回退。");
                byte[] recordBytes;
                if ((reference & 0x0000FFFFFFFFFFFF) == 0)
                {
                    ReadPhysical(checked(BinaryPrimitives.ReadInt64LittleEndian(info.AsSpan(64)) * _cluster), (RecordSize + _sector - 1) / _sector * _sector);
                    recordBytes = new byte[RecordSize]; Marshal.Copy(_native, recordBytes, 0, recordBytes.Length);
                }
                else recordBytes = ReadRecord(reference);
                var parsed = SpaceNtfsScanner.Parse(recordBytes, reference & 0x0000FFFFFFFFFFFF, false, true);
                if ((reference & 0x0000FFFFFFFFFFFF) == 0) baseReference = parsed.Reference;
                else if (parsed.Reference != reference || parsed.Base != baseReference) throw new IOException("MFT 扩展映射已在扫描期间改变。");
                var record = recordBytes.AsSpan(); int size = recordBytes.Length;
                int position = BinaryPrimitives.ReadUInt16LittleEndian(record[20..]);
                while (position <= size - 8)
                {
                    var attribute = record[position..]; uint type = BinaryPrimitives.ReadUInt32LittleEndian(attribute);
                    if (type == uint.MaxValue) break;
                    int count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(attribute[4..]));
                    if (count < 24 || count > attribute.Length) throw new IOException("MFT 映射属性边界无效。");
                    attribute = attribute[..count]; bool nonResident = attribute[8] != 0;
                    if (type == 0x80 && attribute[9] == 0 && nonResident)
                    {
                        _runs.AddRange(ParseRuns(attribute));
                        if (BinaryPrimitives.ReadInt64LittleEndian(attribute[16..]) == 0)
                            Length = Math.Min(Length, BinaryPrimitives.ReadInt64LittleEndian(attribute[56..]));
                    }
                    if (type == 0x20)
                    {
                        byte[] list;
                        if (nonResident)
                        {
                            long length = Math.Min(BinaryPrimitives.ReadInt64LittleEndian(attribute[48..]), BinaryPrimitives.ReadInt64LittleEndian(attribute[56..]));
                            if (length < 0 || length > 4 * 1024 * 1024) throw new IOException("MFT 属性列表大小不受支持。");
                            list = new byte[(int)length]; int copied = 0;
                            foreach (var run in ParseRuns(attribute))
                            {
                                long remaining = Math.Min(run.Clusters * _cluster, list.Length - copied), offset = 0;
                                while (remaining > 0)
                                {
                                    int bytes = (int)Math.Min(_buffer.Length, remaining);
                                    ReadPhysical(run.Lcn * _cluster + offset, (bytes + _sector - 1) / _sector * _sector);
                                    Marshal.Copy(_native, list, copied, bytes); copied += bytes; offset += bytes; remaining -= bytes;
                                }
                            }
                            if (copied != list.Length) throw new IOException("MFT 属性列表不完整。");
                        }
                        else
                        {
                            int start = BinaryPrimitives.ReadUInt16LittleEndian(attribute[20..]);
                            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(attribute[16..]));
                            if (start < 24 || length > count - start) throw new IOException("MFT 属性列表边界无效。");
                            list = attribute.Slice(start, length).ToArray();
                        }
                        int entry = 0;
                        while (entry <= list.Length - 26)
                        {
                            uint typeCode = BinaryPrimitives.ReadUInt32LittleEndian(list.AsSpan(entry));
                            if (typeCode is 0 or uint.MaxValue) break;
                            int step = BinaryPrimitives.ReadUInt16LittleEndian(list.AsSpan(entry + 4));
                            if (step < 26 || step > list.Length - entry) throw new IOException($"MFT 属性列表项无效（{entry}/{list.Length}，类型 {typeCode:X}，长度 {step}）。");
                            pending.Enqueue(BinaryPrimitives.ReadUInt64LittleEndian(list.AsSpan(entry + 16))); entry += step;
                        }
                    }
                    position += count;
                }
            }
            _runs.Sort((a, b) => a.Vcn.CompareTo(b.Vcn));
            long end = 0;
            foreach (var run in _runs)
            { if (run.Vcn != end || run.Lcn <= 0 || run.Clusters <= 0) throw new IOException("MFT 数据映射不连续，已停止只读扫描。"); end = checked(end + run.Clusters); }
            if (end * _cluster < Length) throw new IOException("MFT 数据映射不完整，未使用缓存扫描回退。");
        }
        catch { Dispose(); throw; }
    }

    internal IEnumerable<(ulong Index, Memory<byte> Bytes)> Enumerate(CancellationToken token)
    {
        if (_cached)
        {
            var input = new byte[8]; var output = new byte[RecordSize + 16];
            ulong next = (ulong)Records - 1;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                BinaryPrimitives.WriteUInt64LittleEndian(input, next);
                uint returned = Control(0x90068, input, output);
                ulong actual = BinaryPrimitives.ReadUInt64LittleEndian(output) & 0x0000FFFFFFFFFFFF;
                int size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(8)));
                if (actual > next || size < 48 || size > returned - 12) throw new IOException("MFT 返回记录边界无效。");
                yield return (actual, output.AsMemory(12, size));
                if (actual == 0) yield break;
                next = actual - 1;
            }
        }
        long position = 0; int runIndex = 0;
        while (position < Length)
        {
            token.ThrowIfCancellationRequested();
            int bytes = (int)Math.Min(_buffer.Length, Length - position), copied = 0;
            while (copied < bytes)
            {
                token.ThrowIfCancellationRequested();
                long logical = position + copied;
                while (runIndex < _runs.Count && logical >= (_runs[runIndex].Vcn + _runs[runIndex].Clusters) * _cluster) runIndex++;
                if (runIndex >= _runs.Count) throw new IOException("MFT 批量读取超出映射。");
                var run = _runs[runIndex];
                int count = (int)Math.Min(bytes - copied, (run.Vcn + run.Clusters) * _cluster - logical);
                ReadPhysical(run.Lcn * _cluster + logical - run.Vcn * _cluster, (count + _sector - 1) / _sector * _sector);
                Marshal.Copy(_native, _buffer, copied, count); copied += count;
            }
            for (int offset = 0; offset + RecordSize <= bytes; offset += RecordSize)
                yield return ((ulong)((position + offset) / RecordSize), _buffer.AsMemory(offset, RecordSize));
            position += bytes;
        }
    }
    internal IEnumerable<(ulong Index, Memory<byte> Bytes)> EnumerateDirectories(CancellationToken token)
    {
        if (!_cached) { foreach (var item in Enumerate(token)) yield return item; yield break; }
        // On encrypted volumes use the EXISTING USN/MFT enumeration to find directories in
        // bulk, then query only their records. Never create/resize/modify the USN journal.
        var root = ReadRecord(5); yield return (5, root);
        var input = new byte[24]; BinaryPrimitives.WriteInt64LittleEndian(input.AsSpan(16), long.MaxValue);
        var output = new byte[1024 * 1024]; ulong previous = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!DeviceIoControl(_volume, 0x900B3, input, 24, output, (uint)output.Length, out uint returned, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error(); if (error == 38) yield break;
                if (previous == 0 && error is 1 or 50 or 1179)
                { foreach (var item in Enumerate(token)) yield return item; yield break; }
                throw new IOException("只读 MFT/USN 目录枚举失败。", new Win32Exception(error));
            }
            if (returned < 8) yield break;
            ulong next = BinaryPrimitives.ReadUInt64LittleEndian(output);
            if (next <= previous) throw new IOException("MFT/USN 枚举没有向前推进。");
            int position = 8;
            while (position <= returned - 60)
            {
                int count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(position)));
                if (count < 60 || count > returned - position) throw new IOException("USN 记录边界无效。");
                ushort version = BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(position + 4));
                if (version != 2) throw new IOException("当前 NTFS 文件引用版本不受支持。");
                var attributes = (FileAttributes)BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(position + 52));
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    ulong reference = BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(position + 8));
                    if ((reference & 0x0000FFFFFFFFFFFF) >= 16)
                    {
                        Memory<byte>? record = null;
                        try { record = QueryRecord(reference); }
                        catch (IOException ex) { RecordSkipped?.Invoke(ex); }
                        if (record is { } value) yield return (reference & 0x0000FFFFFFFFFFFF, value);
                    }
                }
                position += count;
            }
            BinaryPrimitives.WriteUInt64LittleEndian(input, next); previous = next;
        }
    }
    internal byte[] ReadRecord(ulong reference)
    {
        if (_cached) return QueryRecord(reference).ToArray();
        long position = checked((long)(reference & 0x0000FFFFFFFFFFFF) * RecordSize);
        if (position < 0 || position > Length - RecordSize) throw new IOException("MFT 引用超出范围。");
        var result = new byte[RecordSize]; int copied = 0;
        while (copied < result.Length)
        {
            var run = _runs.FirstOrDefault(r => position >= r.Vcn * _cluster && position < (r.Vcn + r.Clusters) * _cluster);
            if (run.Clusters == 0) throw new IOException("MFT 引用没有对应磁盘映射。");
            int count = (int)Math.Min(result.Length - copied, (run.Vcn + run.Clusters) * _cluster - position);
            long physical = run.Lcn * _cluster + position - run.Vcn * _cluster;
            int leading = (int)(physical % _sector), aligned = (leading + count + _sector - 1) / _sector * _sector;
            ReadPhysical(physical - leading, aligned);
            Marshal.Copy(IntPtr.Add(_native, leading), result, copied, count);
            copied += count; position += count;
        }
        return result;
    }
    // The enumerator consumes one record before requesting another. Reuse its metadata
    // buffer rather than allocating two 1 KiB arrays for each of hundreds of thousands
    // of directories. Callers needing an independently owned record use ReadRecord.
    private Memory<byte> QueryRecord(ulong reference)
    {
        var output = _recordOutput ??= new byte[RecordSize + 16];
        BinaryPrimitives.WriteUInt64LittleEndian(_recordInput, reference & 0x0000FFFFFFFFFFFF);
        uint returned = Control(0x90068, _recordInput, output);
        int size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(8)));
        if ((BinaryPrimitives.ReadUInt64LittleEndian(output) & 0x0000FFFFFFFFFFFF) != (reference & 0x0000FFFFFFFFFFFF) || size < 48 || size > returned - 12) throw new IOException("MFT 引用已失效。");
        return output.AsMemory(12, size);
    }
    internal static List<Run> ParseRuns(ReadOnlySpan<byte> attribute)
    {
        if (attribute.Length < 64) throw new IOException("MFT 非驻留属性不完整。");
        long vcn = BinaryPrimitives.ReadInt64LittleEndian(attribute[16..]), lcn = 0;
        int position = BinaryPrimitives.ReadUInt16LittleEndian(attribute[32..]); var runs = new List<Run>();
        if (position < 64 || position >= attribute.Length) throw new IOException("MFT 数据运行偏移无效。");
        while (position < attribute.Length && attribute[position] != 0)
        {
            byte header = attribute[position++]; int lengthSize = header & 15, offsetSize = header >> 4;
            if (lengthSize is < 1 or > 8 || offsetSize is < 1 or > 8 || position + lengthSize + offsetSize > attribute.Length) throw new IOException("MFT 数据运行边界无效。");
            ulong length = 0, offset = 0;
            for (int i = 0; i < lengthSize; i++) length |= (ulong)attribute[position++] << (i * 8);
            for (int i = 0; i < offsetSize; i++) offset |= (ulong)attribute[position++] << (i * 8);
            if (offsetSize < 8 && (offset & (1UL << (offsetSize * 8 - 1))) != 0) offset |= ulong.MaxValue << (offsetSize * 8);
            lcn = checked(lcn + unchecked((long)offset)); long clusters = checked((long)length);
            if (clusters <= 0 || lcn <= 0) throw new IOException("MFT 稀疏或负磁盘映射不受支持。");
            runs.Add(new(vcn, lcn, clusters)); vcn = checked(vcn + clusters);
        }
        return runs;
    }
    private void ReadPhysical(long offset, int size)
    {
        if (size <= 0 || size > _buffer.Length || offset < 0 || offset % _sector != 0 || size % _sector != 0) throw new IOException("MFT 读取未按扇区对齐。");
        if (offset > _partitionLength - size || _partitionOffset < 0) throw new IOException("MFT 读取超出卷的物理映射。");
        // Do NOT issue raw reads through the mounted filesystem's volume handle: NTFS can
        // flush dirty MFT/log pages before servicing such reads. The physical-disk handle
        // bypasses that path, uses GENERIC_READ only, and never flushes or locks the volume.
        if (_disk is null || !SetFilePointerEx(_disk, checked(_partitionOffset + offset), out _, 0) || !ReadFile(_disk, _native, (uint)size, out uint read, IntPtr.Zero) || read != size)
            throw new IOException("只读卷读取失败。", new Win32Exception(Marshal.GetLastWin32Error()));
    }
    private uint Control(uint code, byte[] input, byte[] output)
    {
        if (!DeviceIoControl(_volume, code, input, (uint)input.Length, output, (uint)output.Length, out uint returned, IntPtr.Zero))
            throw new IOException("读取 MFT 映射失败。", new Win32Exception(Marshal.GetLastWin32Error()));
        return returned;
    }
    public void Dispose() { if (_native != IntPtr.Zero) { VirtualFree(_native, 0, 0x8000); _native = IntPtr.Zero; } _disk?.Dispose(); _volume.Dispose(); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle volume, uint code, byte[] input, uint inputSize, [Out] byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetFilePointerEx(SafeFileHandle handle, long distance, out long position, uint method);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReadFile(SafeFileHandle handle, IntPtr buffer, uint bytes, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAlloc(IntPtr address, nuint size, uint type, uint protection);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool VirtualFree(IntPtr address, nuint size, uint type);
}
