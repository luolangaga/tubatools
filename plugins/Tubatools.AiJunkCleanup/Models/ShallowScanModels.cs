namespace Tubatools.AiJunkCleanup.Models;

internal sealed record ShallowScanProgress(
    bool Started,
    bool Complete,
    bool Failed,
    double? Percent,
    string Phase,
    long Files,
    TimeSpan Elapsed,
    string? Error,
    bool Indexing = false);

internal sealed record ShallowDirItem(int Level, string Path, long Bytes, long Files, long Modified = 0);

internal sealed record ShallowFileItem(string Path, long Bytes, long Modified);

internal sealed record ScanIndexData(
    IReadOnlyList<ShallowDirItem> Directories,
    IReadOnlyList<ShallowFileItem> Files);

internal sealed record ScanIndex(
    int Version,
    string Root,
    int Depth,
    long MinDirectoryBytes,
    long MinFileBytes,
    IReadOnlyList<ShallowDirItem> Directories,
    IReadOnlyList<ShallowFileItem> Files,
    long GeneratedAt);

internal sealed record ShallowScanData(
    long TotalBytes,
    long Files,
    long Folders,
    string Method,
    double ElapsedSeconds,
    IReadOnlyList<ShallowDirItem> Directories);

internal sealed record ShallowScanResult(
    int Version,
    string Root,
    int Depth,
    string Method,
    long TotalBytes,
    long Files,
    long Folders,
    double ElapsedSeconds,
    IReadOnlyList<ShallowDirItem> Directories,
    long GeneratedAt,
    int IndexDepth = 0,
    long IndexMinDirectoryBytes = 0,
    long IndexMinFileBytes = 0,
    int IndexDirectoryCount = 0,
    int IndexFileCount = 0,
    string? IndexPath = null);
