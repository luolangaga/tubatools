using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using TubaWinUi3.Services.SpaceCleanup;

namespace TubaWinUi3.Tests;

public sealed class SpaceCleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TubaSpaceTests-" + Guid.NewGuid().ToString("N"));
    public SpaceCleanupTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private string Write(string name, int bytes)
    { string path = Path.Combine(_root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, new byte[bytes]); return path; }

    [Fact]
    public async Task Scan_IsReadOnlyAndAggregatesEveryFileExactlyOnce()
    {
        Write("a.bin", 100); Write("sub/b.bin", 500); Write("sub/nested/空文件.txt", 0);
        var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(p => p,
            p => (new FileInfo(p).LastWriteTimeUtc, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))));
        var session = new SpaceScanSession(_root);
        await SpaceScanService.ScanAsync(session, false, default);
        Assert.True(session.Complete); Assert.Equal(600, session.Root.Bytes); Assert.Equal(3, session.Files); Assert.Equal(2, session.Folders);
        Assert.Equal(500, session.Root.Children.Single(n => n.Name == "sub").Bytes);
        Assert.All(SpaceScanService.Descendants(session.Root), n => Assert.True(n.Complete));
        var after = Directory.GetFiles(_root, "*", SearchOption.AllDirectories);
        Assert.Equal(before.Keys.Order(), after.Order());
        foreach (var file in after)
        { Assert.Equal(before[file].LastWriteTimeUtc, new FileInfo(file).LastWriteTimeUtc); Assert.Equal(before[file].Item2, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))); }
    }
    [Fact]
    public void MftRuns_DecodeSignedRelativeOffsetsAndRejectSparseMapping()
    {
        var attribute = new byte[80]; BinaryPrimitives.WriteUInt16LittleEndian(attribute.AsSpan(32), 64);
        new byte[] { 0x11, 4, 100, 0x11, 2, 0xF6, 0 }.CopyTo(attribute, 64);
        var runs = SpaceNtfsVolume.ParseRuns(attribute);
        Assert.Equal(new SpaceNtfsVolume.Run(0, 100, 4), runs[0]);
        Assert.Equal(new SpaceNtfsVolume.Run(4, 90, 2), runs[1]);
        attribute[64] = 0x01;
        Assert.Throws<IOException>(() => SpaceNtfsVolume.ParseRuns(attribute));
    }
    [Fact]
    public void RawMftParser_AppliesSectorFixupsAndRejectsTornRecords()
    {
        var bytes = MftRecord(MftName("a.bin", 5, 3), MftResident(0x80, new byte[7]));
        var parsed = SpaceNtfsScanner.Parse(bytes, 32, false, true);
        Assert.Equal(7, parsed.Bytes); Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(510)));
        bytes = MftRecord(MftName("a.bin", 5, 3));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(510), 0x1234);
        Assert.Throws<IOException>(() => SpaceNtfsScanner.Parse(bytes, 32, false, true));
    }
    [Fact]
    public void MftParser_UsesDataLengthAndOmitsDosAliases()
    {
        byte[] data = new byte[72];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0x80); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 72);
        data[8] = 1; BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(48), 8L * 1024 * 1024 * 1024);
        var bytes = MftRecord(MftName("真正文件.zip", 5, 3), MftName("FILE~1.ZIP", 5, 2), data);
        var record = SpaceNtfsScanner.Parse(bytes, 321, false);
        Assert.Equal((7UL << 48) | 321, record.Reference); Assert.False(record.Directory);
        Assert.Equal("真正文件.zip", Assert.Single(record.Names).Name);
        Assert.Equal(8L * 1024 * 1024 * 1024, record.Bytes); Assert.True(record.HasData);
    }
    [Fact]
    public void MftParser_PreservesHardlinkNamesAndOptionalStreams()
    {
        var bytes = MftRecord(MftName("one.bin", 5, 3), MftName("two.bin", 32, 1),
            MftResident(0x80, new byte[7]), MftResident(0x80, new byte[11], "metadata"));
        var basic = SpaceNtfsScanner.Parse(bytes, 32, false);
        Assert.Equal(2, basic.Names.Count()); Assert.Equal(7, basic.Bytes); Assert.Equal(0, basic.Streams);
        var streams = SpaceNtfsScanner.Parse(bytes, 32, true);
        Assert.Equal(7, streams.Bytes); Assert.Equal(11, streams.Streams);
    }
    [Fact]
    public void MftParser_RejectsInvalidSignatureAndAttributeBounds()
    {
        var bytes = MftRecord(MftName("a.bin", 5, 3));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0);
        Assert.Throws<IOException>(() => SpaceNtfsScanner.Parse(bytes, 32, false));
        bytes = MftRecord(MftName("a.bin", 5, 3));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 4000);
        Assert.Throws<IOException>(() => SpaceNtfsScanner.Parse(bytes, 32, false));
    }
    private static byte[] MftRecord(params byte[][] attributes)
    {
        var record = new byte[1024];
        BinaryPrimitives.WriteUInt32LittleEndian(record, 0x454C4946);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4), 48); BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(16), 7); BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(20), 56);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(22), 1); BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(48), 0xABCD);
        int offset = 56;
        foreach (byte[] attribute in attributes) { attribute.CopyTo(record, offset); offset += attribute.Length; }
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(offset), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(24), (uint)(offset + 4));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(510), 0xABCD); BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1022), 0xABCD);
        return record;
    }
    private static byte[] MftResident(uint type, byte[] value, string stream = "")
    {
        byte[] name = Encoding.Unicode.GetBytes(stream);
        int start = (24 + name.Length + 7) / 8 * 8;
        byte[] attribute = new byte[(start + value.Length + 7) / 8 * 8];
        BinaryPrimitives.WriteUInt32LittleEndian(attribute, type); BinaryPrimitives.WriteUInt32LittleEndian(attribute.AsSpan(4), (uint)attribute.Length);
        attribute[9] = (byte)stream.Length; BinaryPrimitives.WriteUInt16LittleEndian(attribute.AsSpan(10), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(attribute.AsSpan(16), (uint)value.Length); BinaryPrimitives.WriteUInt16LittleEndian(attribute.AsSpan(20), (ushort)start);
        name.CopyTo(attribute, 24); value.CopyTo(attribute, start); return attribute;
    }
    private static byte[] MftName(string name, ulong parent, byte space)
    {
        byte[] filename = Encoding.Unicode.GetBytes(name), value = new byte[66 + filename.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(value, parent); BinaryPrimitives.WriteInt64LittleEndian(value.AsSpan(48), 77);
        value[64] = (byte)name.Length; value[65] = space; filename.CopyTo(value, 66); return MftResident(0x30, value);
    }
    [Fact]
    public void NativeDirectoryRecord_ParsesMetadataWithoutOpeningFileContents()
    {
        var buffer = new byte[256];
        byte[] name = Encoding.Unicode.GetBytes("文件.bin");
        var modified = new DateTime(2026, 10, 1, 2, 3, 4, DateTimeKind.Utc);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(24), modified.ToFileTimeUtc());
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(40), 8L * 1024 * 1024 * 1024);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(56), (uint)FileAttributes.Hidden);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(60), (uint)name.Length);
        name.CopyTo(buffer, 68);
        var entry = SpaceReadOnlyDirectory.Parse(buffer, 0, out int next);
        Assert.Equal("文件.bin", entry.Name); Assert.Equal(8L * 1024 * 1024 * 1024, entry.Bytes);
        Assert.Equal(FileAttributes.Hidden, entry.Attributes); Assert.Equal(modified.ToLocalTime(), entry.Modified); Assert.Equal(0, next);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(60), 255);
        Assert.Throws<IOException>(() => SpaceReadOnlyDirectory.Parse(buffer, 0, out _));
    }
    [Fact]
    public async Task Scan_PreservesLastAccessTimesOfDirectoriesAndFiles()
    {
        string a = Write("sub/a.bin", 10), b = Write("sub/nested/b.txt", 20);
        string[] paths = [_root, Path.Combine(_root, "sub"), Path.Combine(_root, "sub/nested"), a, b];
        var old = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        foreach (string path in paths)
            if (Directory.Exists(path)) Directory.SetLastAccessTimeUtc(path, old); else File.SetLastAccessTimeUtc(path, old);
        DateTime Access(string path) => Directory.Exists(path) ? Directory.GetLastAccessTimeUtc(path) : File.GetLastAccessTimeUtc(path);
        var before = paths.ToDictionary(path => path, Access);
        var session = new SpaceScanSession(_root);
        await SpaceScanService.ScanAsync(session, false, default);
        Assert.True(session.Complete); Assert.Equal(0, session.Skipped);
        Assert.Equal(30, session.Root.Bytes);
        foreach (string path in paths) Assert.Equal(before[path], Access(path));
    }
    [Fact]
    public async Task Scan_AlternateStreamsPreserveLastAccessAndIncludeNamedStreamSize()
    {
        string file = Write("streams.bin", 10);
        File.WriteAllBytes(file + ":metadata", new byte[37]);
        var old = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastAccessTimeUtc(file, old); Directory.SetLastAccessTimeUtc(_root, old);
        var session = new SpaceScanSession(_root);
        await SpaceScanService.ScanAsync(session, true, default);
        Assert.True(session.Complete); Assert.Equal(0, session.Skipped); Assert.Equal(47, session.Root.Bytes);
        Assert.Equal(old, File.GetLastAccessTimeUtc(file)); Assert.Equal(old, Directory.GetLastAccessTimeUtc(_root));
    }
    [Fact]
    public void DefaultScan_HasNoFixedIndexSizeLimit()
    { Assert.Equal(long.MaxValue, new SpaceScanSession(_root).MemoryBudgetBytes); }
    [Fact]
    public async Task Scan_MemoryBudgetStopsWithConsistentPartialResults()
    {
        for (int i = 0; i < 20; i++) Write($"file-{i}.bin", 10);
        var session = new SpaceScanSession(_root, 600, useDiskCache: false);
        await SpaceScanService.ScanAsync(session, false, default);
        Assert.False(session.Complete); Assert.False(session.Cancelled);
        Assert.NotNull(session.LimitReason);
        Assert.InRange(session.EstimatedTreeBytes, 1, session.MemoryBudgetBytes);
        Assert.InRange(session.Files, 1, 19);
        Assert.Equal(session.Files, session.Root.Children.Count);
        Assert.Equal(session.Files * 10, session.Root.Bytes);
        Assert.False(session.Root.Complete);
    }
    [Fact]
    public async Task Refresh_MemoryBudgetPreservesOriginalTreeUntilReplacementIsComplete()
    {
        Write("keep.bin", 7);
        var session = new SpaceScanSession(_root, 800, useDiskCache: false);
        await SpaceScanService.ScanAsync(session, false, default);
        var original = Assert.Single(session.Root.Children);
        for (int i = 0; i < 10; i++) Write($"new-{i}.bin", 100);
        await Assert.ThrowsAsync<InvalidOperationException>(() => SpaceScanService.RefreshAsync(session, session.Root, false, default));
        Assert.Same(original, Assert.Single(session.Root.Children));
        Assert.Equal(7, session.Root.Bytes); Assert.Equal(1, session.Files);
    }
    [Fact]
    public async Task Refresh_UpdatesAncestorsWithoutRescanningUnchangedBranch()
    {
        Write("left/a.bin", 100); Write("right/b.bin", 200);
        var session = new SpaceScanSession(_root); await SpaceScanService.ScanAsync(session, false, default);
        var left = session.Root.Children.Single(n => n.Name == "left"); var right = session.Root.Children.Single(n => n.Name == "right");
        Write("left/new.bin", 400); await SpaceScanService.RefreshAsync(session, left, false, default);
        Assert.Equal(700, session.Root.Bytes); Assert.Equal(3, session.Files); Assert.Contains(right, session.Root.Children);
        Assert.All(left.Children, n => Assert.Same(left, n.Parent));
        SpaceScanService.RemoveNode(session, left.Children.Single(n => n.Name == "new.bin"));
        Assert.Equal(300, session.Root.Bytes); Assert.Equal(2, session.Files);
    }
    [Fact]
    public async Task PausedScan_CanBeCancelledWithoutResuming()
    {
        Write("a", 1); var session = new SpaceScanSession(_root); session.Pause(true);
        using var cancellation = new CancellationTokenSource();
        var task = SpaceScanService.ScanAsync(session, false, cancellation.Token);
        await Task.Delay(40); cancellation.Cancel(); await task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(session.Cancelled); Assert.False(session.Complete);
    }
    [Fact]
    public async Task Scan_DoesNotFollowDirectoryJunction()
    {
        Write("real/data", 23);
        string link = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(link, Path.Combine(_root, "real"));
        var session = new SpaceScanSession(_root); await SpaceScanService.ScanAsync(session, false, default);
        Assert.Equal(23, session.Root.Bytes); Assert.Equal(1, session.Skipped);
        Assert.True(session.Root.Children.Single(n => n.Name == "link").IsLink);
        Assert.Throws<IOException>(() => SpaceFileOperations.ValidateTarget(_root, Path.Combine(link, "data")));
        Directory.Delete(link);
    }
    [Fact]
    public void DeletePolicy_RejectsRootSiblingsAndEscapingPaths()
    {
        string file = Write("safe.txt", 10);
        Assert.Equal(file, SpaceFileOperations.ValidateTarget(_root, file));
        Assert.Throws<InvalidOperationException>(() => SpaceFileOperations.ValidateTarget(_root, _root));
        Assert.Throws<InvalidOperationException>(() => SpaceFileOperations.ValidateTarget(_root, _root + "-other\\data"));
        Assert.Throws<InvalidOperationException>(() => SpaceFileOperations.ValidateTarget(_root, Path.Combine(_root, "..", "other")));
    }
    [Fact]
    public async Task ShellPermanentDelete_DeletesOnlyTheValidatedFixture()
    {
        string target = Write("remove-me.txt", 7), retained = Write("keep-me.txt", 11);
        await SpaceFileOperations.DeleteAsync(_root, target, false, IntPtr.Zero);
        Assert.False(File.Exists(target)); Assert.True(File.Exists(retained)); Assert.Equal(11, new FileInfo(retained).Length);
    }
    [Fact]
    public async Task WholeDriveScan_AllowsDeletingDescendantsButNeverTheDriveRoot()
    {
        string target = Write("sub/remove-me.txt", 7), retained = Write("keep-me.txt", 11);
        string volume = Path.GetPathRoot(_root)!;
        Assert.Equal(Path.GetFullPath(target), SpaceFileOperations.ValidateTarget(volume, target));
        Assert.Throws<InvalidOperationException>(() => SpaceFileOperations.ValidateTarget(volume, volume));
        await SpaceFileOperations.DeleteAsync(volume, target, false, IntPtr.Zero);
        Assert.False(File.Exists(target)); Assert.True(File.Exists(retained));
    }
    [Fact]
    public void CompactNames_PreservePathsAcrossBlocksAndUnpairedSurrogates()
    {
        var session = new SpaceScanSession(_root);
        var nodes = new List<SpaceNode>();
        for (int i = 0; i < 3000; i++)
        {
            string name = $"{i}-压缩文件名-😀-package-lock.json";
            var node = session.CreateNode(name, false, session.Root); nodes.Add(node);
            Assert.Equal(name, node.Name); Assert.Equal(Path.Combine(_root, name), node.FullPath);
        }
        string unusual = "invalid-\uD800.bin";
        var preserved = session.CreateNode(unusual, false, session.Root);
        session.ReleaseNameWriter();
        Assert.Equal(unusual, preserved.Name);
        Assert.Equal("0-压缩文件名-😀-package-lock.json", nodes[0].Name);
        Assert.Equal("2999-压缩文件名-😀-package-lock.json", nodes[^1].Name);
        Assert.All(nodes, node => Assert.Equal(1, node.FileCount));
    }
    [Fact]
    public void CompactIndex_KeepsLargeFileSetsAndFilteredLayoutsWithinMemoryBudget()
    {
        long before = GC.GetTotalMemory(true);
        var session = new SpaceScanSession(_root);
        for (int i = 0; i < 100_000; i++)
        {
            var node = session.CreateNode($"package-library-content-{i:D8}-runtime-metadata-index.json", false, session.Root);
            node.Bytes = 100; session.Root.Children.Add(node); session.Root.Bytes += node.Bytes;
        }
        session.ReleaseNameWriter();
        long retained = GC.GetTotalMemory(true) - before;
        Assert.InRange(retained, 1, 18 * 1024 * 1024);
        var filter = new SpaceFilter("*.JSON >0b");
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var tiles = SpaceTreemapLayout.Build(session.Root, new MapRect(0, 0, 1200, 800), 3, filter);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - allocated, 1, 6 * 1024 * 1024);
        Assert.NotEmpty(tiles);
        Assert.Equal(100_000, session.Root.Children.Count);
        GC.KeepAlive(session);
    }
    [Fact]
    public async Task ReleasingAnOldIndex_DetachesCachedRowsAndFilenameBlocks()
    {
        Write("folder/file.txt", 30);
        var session = new SpaceScanSession(_root, useDiskCache: false); await SpaceScanService.ScanAsync(session, false, default);
        var folder = Assert.Single(session.Root.Children); var cachedFile = Assert.Single(folder.Children);
        cachedFile.Tag = 3;
        session.ReleaseIndex();
        Assert.Empty(session.Root.Children); Assert.Empty(folder.Children);
        Assert.Null(folder.Parent); Assert.Null(cachedFile.Parent); Assert.True(cachedFile.IsReleased);
        Assert.Equal(string.Empty, cachedFile.Name); Assert.Equal(0, cachedFile.Bytes); Assert.Equal(0, session.EstimatedTreeBytes);
        cachedFile.Tag = 1; Assert.True(cachedFile.IsReleased);
        Assert.Equal(Path.GetFullPath(_root), session.Root.FullPath);
    }
    [Fact]
    public async Task DiskCache_CompressesMetadataExcludesItselfAndDeletesOnDispose()
    {
        for (int i = 0; i < 1000; i++) Write($"package/representative-file-{i:D8}.json", 20);
        string cacheRoot = Path.Combine(_root, "scan-cache");
        var session = new SpaceScanSession(_root, cacheOptions: new() { Directory = cacheRoot });
        string cachePath;
        using (session)
        {
            await SpaceScanService.ScanAsync(session, false, default);
            Assert.True(session.Complete); Assert.True(session.UsesDiskCache);
            Assert.Equal(long.MaxValue, session.CacheMaximumBytes);
            Assert.Equal(0, new SpaceCacheOptions().ReserveFreeBytes);
            Assert.Equal(1000, session.Files); Assert.Equal(20_000, session.Root.Bytes);
            Assert.DoesNotContain(session.Root.Children, n => n.Name == "scan-cache");
            Assert.InRange(session.CacheBytes, 1, 40_000);
            cachePath = session.CachePath!; Assert.True(File.Exists(cachePath));
            Assert.Equal(session.CacheBytes, new FileInfo(cachePath).Length);
        }
        Assert.False(File.Exists(cachePath)); Assert.False(Directory.Exists(cacheRoot));
    }
    [Fact]
    public void DiskCache_PreservesUnicodePathsAndExactMetadataAcrossPages()
    {
        using var session = new SpaceScanSession(_root, cacheOptions: new() { Directory = Path.Combine(_root, "scan-cache") });
        session.InitializeCache();
        string[] names = ["文件-😀.zip", "invalid-\uD800.bin"];
        var modified = new DateTime(2026, 10, 26, 1, 2, 3, DateTimeKind.Utc);
        foreach (string name in names) session.Root.Children.Add(new SpaceNode(name, false, session.Root)
            { Bytes = long.MaxValue, Modified = modified, Tag = 4, IsLink = true });
        session.FinishCache();
        foreach (var node in session.Root.Children)
        {
            Assert.Contains(node.Name, names); Assert.Equal(Path.Combine(_root, node.Name), node.FullPath);
            Assert.Equal(long.MaxValue, node.Bytes); Assert.Equal(modified, node.Modified);
            Assert.Equal(DateTimeKind.Utc, node.Modified.Kind); Assert.Equal(4, node.Tag); Assert.True(node.IsLink);
        }
    }
    [Fact]
    public void DiskCache_LargeFileListsDoNotRetainAnObjectForEveryFile()
    {
        using var session = new SpaceScanSession(_root, cacheOptions: new() { Directory = Path.Combine(_root, "scan-cache") });
        session.InitializeCache();
        long before = GC.GetTotalMemory(true);
        for (int i = 0; i < 100_000; i++) session.Root.Children.Add(new SpaceNode($"representative-package-library-{i:D8}.json", false, session.Root)
            { Bytes = i, Modified = new DateTime(2026, 10, 26) });
        session.FinishCache();
        Assert.Empty(session.Root.Children.MemoryItems); Assert.Equal(100_000, session.Root.Children.Count);
        Assert.True(GC.GetTotalMemory(true) - before < 12 * 1024 * 1024);
        Assert.InRange(session.CacheBytes, 1, 2 * 1024 * 1024);
        var top = SpaceScanService.Largest(session.Root.Children, 20, n => n.Bytes, out int count, out _);
        Assert.Equal(100_000, count); Assert.Equal(99_999, top[0].Bytes);
        Assert.Equal(99_980, top[^1].Bytes);
        session.PinVisible(top);
        Assert.Same(top[0], session.Root.Children.First());
    }
    [Fact]
    public async Task DiskCache_QuotaStopsCleanlyWithoutDuplicatingPartialResults()
    {
        for (int i = 0; i < 6000; i++) Write(Guid.NewGuid().ToString("N") + ".bin", 1);
        using var session = new SpaceScanSession(_root, cacheOptions: new() { Directory = Path.Combine(_root, "scan-cache"), MaximumBytes = 4096 });
        await SpaceScanService.ScanAsync(session, false, default);
        Assert.False(session.Complete); Assert.NotNull(session.LimitReason);
        Assert.InRange(session.Files, 1, 5999); Assert.Equal(session.Files, session.Root.Children.Count);
        Assert.Equal(session.Files, session.Root.Bytes);
        Assert.True(session.CacheBytes <= 4096);
        Assert.Equal(session.Files, session.Root.Children.Select(n => n.Name).Distinct().LongCount());
    }
    [Fact]
    public async Task DiskCache_RefreshKeepsTagsAndSurvivesReplacementCacheDeletion()
    {
        Write("sub/kept.zip", 10);
        using var session = new SpaceScanSession(_root, cacheOptions: new() { Directory = Path.Combine(_root, "scan-cache") });
        await SpaceScanService.ScanAsync(session, false, default);
        var folder = Assert.Single(session.Root.Children); var oldRow = Assert.Single(folder.Children); oldRow.Tag = 3;
        session.PinVisible([oldRow]);
        oldRow.Bytes = 15; session.UpdateCachedNode(oldRow);
        Assert.Equal(3, Assert.Single(folder.Children).Tag);
        Write("sub/new.bin", 20);
        await SpaceScanService.RefreshAsync(session, folder, false, default);
        Assert.True(oldRow.IsReleased); Assert.Equal(30, session.Root.Bytes);
        var kept = folder.Children.Single(n => n.Name == "kept.zip"); Assert.Equal(3, kept.Tag);
        Assert.Same(folder, kept.Parent); Assert.Equal(Path.Combine(_root, "sub", "kept.zip"), kept.FullPath);
        Assert.Single(Directory.GetFiles(session.CacheDirectory!));
        SpaceScanService.RemoveNode(session, kept);
        Assert.Equal(20, session.Root.Bytes); Assert.Equal("new.bin", Assert.Single(folder.Children).Name);
    }
    [Fact]
    public async Task DiskCache_ReserveProtectsFreeDiskSpace()
    {
        Write("file.bin", 1);
        using var session = new SpaceScanSession(_root, cacheOptions: new() { Directory = Path.Combine(_root, "scan-cache"), ReserveFreeBytes = long.MaxValue });
        await SpaceScanService.ScanAsync(session, false, default);
        Assert.False(session.Complete); Assert.NotNull(session.LimitReason); Assert.Equal(0, session.CacheBytes);
    }
    [Fact]
    public void NavigationMotion_PreservesVisiblePositionWhenInterruptedAndFinishesExactly()
    {
        var visible = new MapRect(73, 112, 230, 180);
        var destination = new MapRect(0, 0, 1200, 700);
        Assert.Equal(visible, SpaceMapMotion.Between(visible, destination, SpaceMapMotion.Ease(0)));
        Assert.Equal(destination, SpaceMapMotion.Between(visible, destination, SpaceMapMotion.Ease(1)));
        var interrupted = SpaceMapMotion.Between(visible, destination, SpaceMapMotion.Ease(.42));
        Assert.Equal(interrupted, SpaceMapMotion.Between(interrupted, visible, SpaceMapMotion.Ease(0)));
        double previous = 0;
        for (int i = 0; i <= 100; i++)
        {
            double current = SpaceMapMotion.Ease(i / 100.0);
            Assert.InRange(current, previous, 1); previous = current;
        }
        Assert.True(SpaceMapMotion.Ease(.001) < .001); // No instantaneous kick at the first frame.
        Assert.True(1 - SpaceMapMotion.Ease(.999) < .00001);
    }
    [Fact]
    public void NavigationMotion_MapsFolderContentToViewportAndBackWithoutHeaderJump()
    {
        var viewport = new MapRect(0, 0, 1200, 700);
        var content = SpaceMapMotion.Content(new MapRect(170, 80, 370, 310));
        var child = new MapRect(content.X + 17, content.Y + 11, 90, 70);
        Assert.Equal(viewport, SpaceMapMotion.Project(content, content, viewport));
        var enlarged = SpaceMapMotion.Project(child, content, viewport);
        var restored = SpaceMapMotion.Project(enlarged, viewport, content);
        Assert.Equal(child.X, restored.X, 8); Assert.Equal(child.Y, restored.Y, 8);
        Assert.Equal(child.Width, restored.Width, 8); Assert.Equal(child.Height, restored.Height, 8);
    }
    [Fact]
    public void Squarify_PreservesAreaBoundsAndDoesNotOverlap()
    {
        double[] weights = [1000, 500, 200, 100, 1, 0]; var bounds = new MapRect(3, 5, 1200, 700);
        var rectangles = SpaceTreemapLayout.Squarify(weights, bounds);
        double total = weights.Sum();
        for (int i = 0; i < rectangles.Count; i++)
        {
            var rect = rectangles[i]; Assert.Equal(bounds.Area * weights[i] / total, rect.Area, 6);
            if (weights[i] == 0) continue;
            Assert.InRange(rect.X, bounds.X - .00001, bounds.X + bounds.Width + .00001);
            Assert.InRange(rect.Y + rect.Height, bounds.Y - .00001, bounds.Y + bounds.Height + .00001);
            for (int j = 0; j < i; j++)
            { var other = rectangles[j]; double overlap = Math.Max(0, Math.Min(rect.X + rect.Width, other.X + other.Width) - Math.Max(rect.X, other.X)) * Math.Max(0, Math.Min(rect.Y + rect.Height, other.Y + other.Height) - Math.Max(rect.Y, other.Y)); Assert.True(overlap < .00001); }
        }
    }
    [Theory]
    [InlineData("*.zip >100mb", "large.zip", 200L * 1024 * 1024, true)]
    [InlineData("*.zip >100mb", "small.zip", 200, false)]
    [InlineData("*.zip; *.iso", "disk.iso", 1, true)]
    [InlineData("!*.dll", "KERNEL32.DLL", 1, false)]
    [InlineData(">=1gb", "a", 1024L * 1024 * 1024, true)]
    [InlineData("\"hello world\"", "hello world.txt", 1, true)]
    public void Filter_CombinesWildcardsExclusionsSizesAndAlternatives(string expression, string name, long size, bool expected)
    { var filter = new SpaceFilter(expression); Assert.Null(filter.Error); Assert.Equal(expected, filter.Matches(new SpaceNode(name, false) { Bytes = size })); }
    [Fact]
    public void Filter_TagInheritanceAndDates()
    {
        var folder = new SpaceNode("folder", true) { Tag = 1 };
        var file = new SpaceNode("a.zip", false, folder) { Modified = new DateTime(2026, 10, 1) };
        Assert.True(new SpaceFilter("tag:red date>2026-01-01").Matches(file));
        Assert.False(new SpaceFilter("tag:blue").Matches(file));
        Assert.NotNull(new SpaceFilter(">hello").Error);
    }
    [Fact]
    public async Task FilteredLayout_KeepsMatchingDescendantAndRespectsTileLimit()
    {
        Write("sub/a.zip", 20); Write("sub/b.dll", 200); Write("c.txt", 100);
        var session = new SpaceScanSession(_root); await SpaceScanService.ScanAsync(session, false, default);
        var tiles = SpaceTreemapLayout.Build(session.Root, new MapRect(0, 0, 800, 600), 3, new SpaceFilter("*.zip"));
        Assert.Equal(2, tiles.Count); Assert.DoesNotContain(tiles, t => t.Node.Name is "b.dll" or "c.txt");
        Assert.Equal(20, tiles[0].Bytes);
        Assert.Single(SpaceTreemapLayout.Build(session.Root, new MapRect(0, 0, 800, 600), 3, new SpaceFilter(""), 1));
    }
    [Fact]
    public void BoundedLayout_PreservesSmallFilesAreaAndAllowsDrillDown()
    {
        var root = new SpaceNode(_root, true);
        for (int i = 0; i < 800; i++) root.Children.Add(new SpaceNode($"file-{i}", false, root) { Bytes = 100 });
        root.Bytes = 80000;
        var tiles = SpaceTreemapLayout.Build(root, new MapRect(0, 0, 1600, 1000), 1, new SpaceFilter(""), 20);
        Assert.Equal(80000, tiles.Sum(t => t.Bytes));
        var aggregate = Assert.Single(tiles.Where(t => t.Node.IsAggregate));
        Assert.Equal(781, aggregate.Node.Children.Count);
        Assert.Equal(root.FullPath, aggregate.Node.FullPath);
        Assert.All(tiles, t => Assert.Equal(t.Bytes / 80000.0, t.Rect.Area / 1600000, 6));
    }
    [Fact]
    public void Palette_DistinguishesLargeSystemBranchesAndHonoursTags()
    {
        var root = new SpaceNode(@"C:\", true);
        var nodes = new[] { new SpaceNode("XboxGames", true, root), new SpaceNode("Windows", true, root),
            new SpaceNode("pagefile.sys", false, root), new SpaceNode("hiberfil.sys", false, root) };
        Assert.Equal(4, nodes.Select(n => SpaceColorPalette.Hue(n)).Distinct().Count());
        var nested = new SpaceNode("Content", true, nodes[0]);
        Assert.InRange(Math.Abs(SpaceColorPalette.Hue(nested) - SpaceColorPalette.Hue(nodes[0])), 0, 10);
        nodes[0].Tag = 1; Assert.Equal(SpaceColorPalette.Hue(nodes[0]), SpaceColorPalette.Hue(nested));
    }
}
