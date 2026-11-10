using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public sealed class TrustedDownloadSourceTests
{
    private const string Repository = "luolangaga/tubatool";
    private const string Name = "TubaWinUi3_Setup_999.0.0_x64.exe";
    private static readonly string Hash = new('A', 64);

    private static string Release(bool gitCode, string? digest, string name = Name, string tag = "v999.0.0",
        bool? prerelease = null, string? releaseStatus = "lasted", string? url = null) => JsonSerializer.Serialize(new
        {
            tag_name = tag, prerelease, release_status = releaseStatus,
            assets = new[] { new { name, type = "ATTACH_FILE", size = 3,
                browser_download_url = url ?? (gitCode
                    ? $"https://api.gitcode.com/api/v5/repos/{Repository}/releases/{tag}/attach_files/{name}/download"
                    : $"https://github.com/{Repository}/releases/download/{tag}/{name}"), digest } }
        });

    private sealed class Replies(Func<Uri, HttpResponseMessage> reply) : HttpMessageHandler
    {
        internal List<Uri> Calls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(request.RequestUri!); return Task.FromResult(reply(request.RequestUri!));
        }
    }
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
        { Content = new StringContent(value, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtomGitDigestIsPreferred_WithoutContactingGitHub(bool prefixed)
    {
        using var handler = new Replies(_ => Json(Release(true, (prefixed ? "sha256:" : "") + Hash.ToLowerInvariant())));
        using var client = new HttpClient(handler);
        var result = await TrustedDownloads.LatestAssetsAsync(Repository, default, client, Name);
        Assert.Equal(Hash, Assert.Single(result).Sha256);
        Assert.Equal("api.atomgit.com", Assert.Single(handler.Calls).Host);
        Assert.Equal("?type=latest", handler.Calls[0].Query);
    }

    [Fact]
    public async Task GitCodeDigestDoesNotRequireTheOptionalSizeField()
    {
        var json = JsonNode.Parse(Release(true, Hash))!;
        json["assets"]![0]!.AsObject().Remove("size");
        using var handler = new Replies(_ => Json(json.ToJsonString()));
        using var client = new HttpClient(handler);
        var asset = Assert.Single(await TrustedDownloads.LatestAssetsAsync(Repository, default, client, Name));
        Assert.Equal(Hash, asset.Sha256);
        Assert.Equal(0, asset.Size);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task RateLimitedAtomGitFallsBackToGitCodeBeforeGitHub()
    {
        using var handler = new Replies(uri => uri.Host == "api.atomgit.com"
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : Json(Release(true, Hash)));
        using var client = new HttpClient(handler);
        Assert.Single(await TrustedDownloads.LatestAssetsAsync(Repository, default, client, Name));
        Assert.Equal(new[] { "api.atomgit.com", "api.gitcode.com" }, handler.Calls.Select(u => u.Host).ToArray());
    }

    [Fact]
    public async Task PreviewLatestFallsBackToTheNewestStableReleaseInTheList()
    {
        var preview = Release(true, Hash, tag: "v1001.0.0", prerelease: true, releaseStatus: "pre");
        var older = Release(true, Hash, tag: "v998.0.0");
        var current = Release(true, Hash);
        using var handler = new Replies(uri => Json(uri.AbsolutePath.EndsWith("/latest")
            ? preview : $"[{older},{preview},{current}]"));
        using var client = new HttpClient(handler);
        var asset = Assert.Single(await TrustedDownloads.LatestAssetsAsync(Repository, default, client, Name));
        Assert.Contains("v999.0.0", asset.Url);
        Assert.Equal(2, handler.Calls.Count);
        Assert.All(handler.Calls, uri => Assert.Equal("api.atomgit.com", uri.Host));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("md5:bad")]
    [InlineData("sha256:bad")]
    public async Task MissingOrInvalidGitCodeDigestUsesGitHubWithoutDisablingValidation(string? digest)
    {
        using var handler = new Replies(uri => Json(uri.Host == "api.github.com"
            ? Release(false, "sha256:" + Hash) : Release(true, digest)));
        using var client = new HttpClient(handler);
        var asset = Assert.Single(await TrustedDownloads.LatestAssetsAsync(Repository, default, client, Name));
        Assert.Equal(Hash, asset.Sha256);
        Assert.Equal("api.github.com", handler.Calls.Last().Host);
    }

    [Fact]
    public async Task DigestForADifferentFileDoesNotPreventFallbackForTheRequestedFile()
    {
        using var handler = new Replies(uri => Json(uri.Host == "api.github.com"
            ? Release(false, Hash) : Release(true, Hash, name: "another.exe")));
        using var client = new HttpClient(handler);
        Assert.Equal(Name, Assert.Single(await TrustedDownloads.LatestAssetsAsync(Repository, default, client, Name)).Name);
        Assert.Equal("api.github.com", handler.Calls.Last().Host);
    }

    [Fact]
    public async Task NoPublisherDigestRemainsAnError()
    {
        using var handler = new Replies(uri => Json(Release(uri.Host != "api.github.com", null)));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => TrustedDownloads.LatestAssetsAsync(Repository, default, client, Name));
    }

    [Fact]
    public async Task CancellationDoesNotTriggerOtherProviderRequests()
    {
        using var handler = new Replies(_ => throw new InvalidOperationException("Must not send"));
        using var client = new HttpClient(handler); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TrustedDownloads.LatestAssetsAsync(Repository, cancel.Token, client, Name));
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData("http://api.gitcode.com/file")]
    [InlineData("https://gitcode.com.attacker.invalid/file")]
    [InlineData("https://user:secret@gitcode.com/file")]
    public void MetadataRejectsUnsafeAssetUrls(string url) =>
        Assert.Empty(TrustedDownloads.ParseReleaseAssets(Repository, Release(true, Hash, url: url), true));

    [Fact]
    public async Task MatchingHashDoesNotAskForOverride()
    {
        using var file = new MemoryStream("DATA"u8.ToArray());
        var expected = Convert.ToHexString(SHA256.HashData(file.ToArray()));
        await DownloadIntegrityPolicy.VerifyAsync(file, expected, Name, default,
            (_, _) => throw new InvalidOperationException("Matching data must not prompt"));
        Assert.Equal(0, file.Position);
    }

    [Fact]
    public async Task MismatchCanBeCancelledOrAllowedForOnlyThatInvocation()
    {
        using var file = new MemoryStream("DATA"u8.ToArray());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DownloadIntegrityPolicy.VerifyAsync(file, Hash, Name, default,
            (_, _) => Task.FromResult(false)));
        DownloadIntegrityMismatch? shown = null;
        await DownloadIntegrityPolicy.VerifyAsync(file, Hash, Name, default, (mismatch, _) =>
        { shown = mismatch; return Task.FromResult(true); });
        Assert.NotNull(shown);
        Assert.Equal(Name, shown.FileName); Assert.Equal(Hash, shown.ExpectedSha256);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(file.ToArray())), shown.ActualSha256);
        // Previous approval cannot silently authorize this verification or a different download.
        await Assert.ThrowsAsync<InvalidDataException>(() => DownloadIntegrityPolicy.VerifyAsync(file, Hash, Name, default));
    }

    [Theory]
    [InlineData(false, "仍然安装", false)]
    [InlineData(true, "", false)]
    [InlineData(true, "安装", false)]
    [InlineData(true, "仍然安装", true)]
    public void StrongOverrideRequiresAcknowledgementAndExactConfirmation(bool acknowledgement, string text, bool accepted) =>
        Assert.Equal(accepted, DownloadIntegrityPolicy.IsStrongConfirmation(acknowledgement, text));

    [Fact]
    public void CancellingIntegrityPromptFinishesQueueItemWithoutDeletingDownload()
    {
        var root = Path.Combine(Path.GetTempPath(), "IntegrityCancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "setup.exe");
        File.WriteAllText(file, "TEST_DOWNLOAD");
        var pending = typeof(DownloadQueueService).GetField("_pendingCount", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = DownloadQueueService.PendingCount;
        try
        {
            pending.SetValue(null, previous + 1);
            var item = DownloadItem.CreateDirect("Integrity test", "https://example.invalid/setup.exe", root);
            item.ResolvedFileName = "setup.exe";
            item.SetState(DownloadItemState.Processing);
            DownloadQueueService.OnIntegrityInstallationCancelled(item);
            Assert.Equal(DownloadItemState.Cancelled, item.State);
            Assert.Equal(previous, DownloadQueueService.PendingCount);
            Assert.True(File.Exists(file));
        }
        finally { pending.SetValue(null, previous); File.Delete(file); Directory.Delete(root); }
    }

    [Fact]
    public async Task NewlyDownloadedFfmpegIsVerifiedAndExtractedWithoutAMarker()
    {
        var root = Path.Combine(Path.GetTempPath(), "FfmpegIntegrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "download.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry("bin/ffmpeg.exe").Open())) writer.Write("TEST_FFMPEG");
                using (var writer = new StreamWriter(zip.CreateEntry("bin/ffprobe.exe").Open())) writer.Write("TEST_FFPROBE");
            }
            var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archive)));
            var executable = Path.Combine(root, "ffmpeg.exe"); var probe = Path.Combine(root, "ffprobe.exe");
            var processor = new FfmpegExtractProcessor(executable, probe) { ExpectedSha256 = hash };
            await processor.ExecuteAsync(archive, root, null, default);
            Assert.Equal("TEST_FFMPEG", await File.ReadAllTextAsync(executable));
            Assert.Equal("TEST_FFPROBE", await File.ReadAllTextAsync(probe));
            Assert.False(File.Exists(executable + ".verified-sha256"));
        }
        finally { Directory.Delete(root, true); }
    }
}
