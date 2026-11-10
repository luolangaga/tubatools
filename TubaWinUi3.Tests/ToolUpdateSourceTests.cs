using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public sealed class ToolUpdateSourceTests
{
    private static ToolUpdateEntry Update(string localDir) => new(
        "测试工具", "test", 1, 2, "TubaWinUi3.WinUI3/Tools/处理器工具/测试 工具", "处理器工具", localDir);

    private static Task<List<(string RelPath, string Sha, string FileName)>?> Files(
        string path, CancellationToken ct) => Task.FromResult<List<(string, string, string)>?>(
            [("子目录/test #1.exe", "abc123", "test #1.exe")]);

    [Fact]
    public async Task GitHubFallback_UsesGitHubDownloadWithFullEncodedPath()
    {
        var update = Update(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var result = await ToolUpdateService.BuildFileListAsync(update,
            (_, _) => Task.FromResult<List<(string, string, string)>?>(null), Files, default);
        var file = Assert.Single(result);
        Assert.Equal("子目录/test #1.exe", file.FileName);
        Assert.Equal("https://raw.githubusercontent.com/luolangaga/tubatool/master/" +
            "TubaWinUi3.WinUI3/Tools/%E5%A4%84%E7%90%86%E5%99%A8%E5%B7%A5%E5%85%B7/" +
            "%E6%B5%8B%E8%AF%95%20%E5%B7%A5%E5%85%B7/%E5%AD%90%E7%9B%AE%E5%BD%95/test%20%231.exe", file.Url);
    }

    [Fact]
    public async Task GitCodeSuccess_KeepsContentAddressedDownloadAndDoesNotCallFallback()
    {
        var result = await ToolUpdateService.BuildFileListAsync(
            Update(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))), Files,
            (_, _) => throw new InvalidOperationException("不应回退"), default);
        Assert.Equal("https://raw.gitcode.com/luolangaga/tubatool/blobs/abc123/test%20%231.exe",
            Assert.Single(result).Url);
    }

    [Fact]
    public async Task IdenticalLocalFile_IsSkipped()
    {
        var root = Path.Combine(Path.GetTempPath(), "TubaUpdate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            byte[] content = [1, 2, 3];
            await File.WriteAllBytesAsync(Path.Combine(root, "main.exe"), content);
            var result = await ToolUpdateService.BuildFileListAsync(Update(root),
                (_, _) => Task.FromResult<List<(string, string, string)>?>(
                    [("main.exe", ToolDownloaderService.ComputeBlobSha(content), "main.exe")]),
                (_, _) => throw new InvalidOperationException("不应回退"), default);
            Assert.Empty(result);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CancelledListing_DoesNotStartFallback()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ToolUpdateService.BuildFileListAsync(
            Update(Path.GetTempPath()), (_, _) => Task.FromResult<List<(string, string, string)>?>(null),
            (_, _) => throw new InvalidOperationException("取消后不应回退"), cts.Token));
    }
}
