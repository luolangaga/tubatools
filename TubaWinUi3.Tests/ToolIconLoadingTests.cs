using System.Drawing;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

[Collection("GlobalConfigTests")]
public sealed class ToolIconLoadingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TubaIconTests_" + Guid.NewGuid().ToString("N"));
    private readonly string? _previousOverride = ConfigManager.DataDirectoryOverride;

    public ToolIconLoadingTests()
    {
        Directory.CreateDirectory(_root);
        ConfigManager.DataDirectoryOverride = _root;
    }

    [Fact]
    public async Task ConcurrentRequests_ReuseOneValidCachedImage()
    {
        var executable = Path.Combine(_root, "tool.exe");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), executable);
        var paths = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => ToolIconService.ExtractIconToCacheAsync(executable)));

        var path = Assert.IsType<string>(paths[0]);
        Assert.All(paths, p => Assert.Equal(path, p));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "IconCache"), "*.png"));
        using var bitmap = new Bitmap(path);
        Assert.True(bitmap.Width > 0 && bitmap.Height > 0);
        var writeTime = File.GetLastWriteTimeUtc(path);
        Assert.Equal(path, await ToolIconService.ExtractIconToCacheAsync(executable));
        Assert.Equal(writeTime, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task CancelledLoad_DoesNotPublishOrCreateCache()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var tool = CreateTool(Path.Combine(_root, "missing.exe"));
        await ToolIconService.LoadIconsAsync([tool], null, cts.Token);
        Assert.Null(tool.IconPath);
        Assert.False(Directory.Exists(Path.Combine(_root, "IconCache")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ToolIconService.ExtractIconToCacheAsync(tool.Path, cts.Token));
    }

    private static ToolItem CreateTool(string path) => new()
    {
        Name = "test", Category = "test", Path = path, RelativePath = path, Extension = ".exe"
    };

    [Fact]
    public async Task CachedIcons_AreReadyBeforePublishingCards_WithoutBackgroundNotifications()
    {
        var executable = Path.Combine(_root, "cached.exe");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), executable);
        var path = await ToolIconService.ExtractIconToCacheAsync(executable);
        var writeTime = File.GetLastWriteTimeUtc(path!);
        var tool = CreateTool(executable);
        var notifications = 0;
        tool.PropertyChanged += (_, _) => notifications++;
        var cached = await ToolIconService.GetCachedIconsAsync([tool]);
        var entry = Assert.Single(cached);
        Assert.Same(tool, entry.Tool);
        Assert.Equal(path, entry.Path);
        Assert.Null(tool.IconPath);
        Assert.Equal(0, notifications);
        Assert.Equal(writeTime, File.GetLastWriteTimeUtc(entry.Path));
        tool.IconPath = entry.Path;
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task RapidCancelledNavigations_ReturningToCategory_ReusesDiskCache()
    {
        var executable = Path.Combine(_root, "return.exe");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), executable);
        var path = await ToolIconService.ExtractIconToCacheAsync(executable);
        var writeTime = File.GetLastWriteTimeUtc(path!);
        var tool = CreateTool(executable);
        for (var i = 0; i < 20; i++)
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await ToolIconService.LoadIconsAsync([tool], null, cts.Token);
        }
        Assert.Null(tool.IconPath);
        await ToolIconService.LoadIconsAsync([tool], null);
        Assert.Equal(path, tool.IconPath);
        Assert.Equal(writeTime, File.GetLastWriteTimeUtc(path!));
    }

    [Fact]
    public void CleanExpiredCache_RemovesOnlyExpiredEntries_AndToleratesInvalidRoot()
    {
        var cache = Path.Combine(_root, "IconCache");
        Directory.CreateDirectory(cache);

        var old = Path.Combine(cache, "old.png");
        var fresh = Path.Combine(cache, "fresh.png");
        File.WriteAllBytes(old, [1]);
        File.WriteAllBytes(fresh, [1]);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-ToolIconCacheMaxAgeDays - 1));
        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow);

        ToolIconService.CleanExpiredCacheAt(cache, fullCleanup: false);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));

        // 损坏的场景表现为「缓存根不是目录」，清理逻辑必须静默跳过而不是抛异常（#120）。
        var notADirectory = Path.Combine(_root, "IconCacheCorrupt");
        File.WriteAllText(notADirectory, "not a directory");
        ToolIconService.CleanExpiredCacheAt(notADirectory, fullCleanup: false);
    }

    private const int ToolIconCacheMaxAgeDays = 90;

    public void Dispose()
    {
        ConfigManager.DataDirectoryOverride = _previousOverride;
        Directory.Delete(_root, recursive: true);
    }
}
