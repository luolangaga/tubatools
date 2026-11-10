using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace TubaWinUi3.Services;

internal sealed record TrustedReleaseAsset(string Name, string Url, string Sha256, long Size);

internal static class TrustedDownloads
{
    internal static string ParseSha256(string? digest)
    {
        if (digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true) digest = digest[7..];
        if (digest is not { Length: 64 } || !digest.All(Uri.IsHexDigit))
            throw new InvalidDataException("发布方没有提供可信 SHA-256，已阻止使用此下载文件。请稍后重试或从官方发布页手动更新。");
        return digest.ToUpperInvariant();
    }

    internal static async Task<IReadOnlyList<TrustedReleaseAsset>> LatestAssetsAsync(string repository, CancellationToken ct,
        HttpClient? client = null, string? requiredAsset = null)
    {
        if (repository is not ("luolangaga/tubatool" or "jellyfin/jellyfin-ffmpeg"))
            throw new ArgumentException("未知发布源", nameof(repository));
        using var ownedClient = client is null ? CreateClient() : null;
        client ??= ownedClient!;
        if (repository == "luolangaga/tubatool")
        {
            // AtomGit and GitCode expose the same release-asset digest (bare SHA-256 or sha256:...).
            // https://docs.gitcode.com/docs/apis/get-api-v-5-repos-owner-repo-releases/
            foreach (var host in new[] { "api.atomgit.com", "api.gitcode.com" })
            {
                var baseUrl = $"https://{host}/api/v5/repos/{repository}/releases";
                foreach (var url in new[] { baseUrl + "/latest?type=latest", baseUrl + "?per_page=30&direction=desc" })
                {
                    try
                    {
                        var json = await ReadReleaseJsonAsync(client, url, ct);
                        var assets = ParseReleaseAssets(repository, json, isGitCode: true);
                        if (ContainsRequestedAsset(assets, requiredAsset)) return assets;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                        or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized) { break; }
                    catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException)
                    { /* Try the other official API; never infer a hash from a local download. */ }
                }
            }
        }
        var github = await ReadReleaseJsonAsync(client, $"https://api.github.com/repos/{repository}/releases/latest", ct);
        var fallback = ParseReleaseAssets(repository, github, isGitCode: false);
        if (!ContainsRequestedAsset(fallback, requiredAsset))
            throw new InvalidDataException("官方发布源未提供所需文件的可信 SHA-256，已阻止安装。请稍后重试。");
        return fallback;
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false, Proxy = ProxyService.GetWebProxy() };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TubaWinUi3-SecureDownloads");
        return client;
    }

    private static bool ContainsRequestedAsset(IReadOnlyList<TrustedReleaseAsset> assets, string? name) =>
        assets.Count > 0 && (name is null || assets.Any(a => a.Name == name));

    private static async Task<string> ReadReleaseJsonAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new InvalidDataException("发布清单过大");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bounded = new MemoryStream();
        await RequestLimits.CopyBoundedAsync(stream, bounded, 2 * 1024 * 1024, ct);
        return System.Text.Encoding.UTF8.GetString(bounded.ToArray());
    }

    internal static IReadOnlyList<TrustedReleaseAsset> ParseReleaseAssets(string repository, string json, bool isGitCode)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            // GitCode's latest endpoint can return a preview; its list defaults to oldest-first.
            var stable = root.EnumerateArray().Where(IsStableRelease)
                .Select(r => (Release: r, Version: Version.TryParse(Text(r, "tag_name")?.TrimStart('v', 'V'), out var v) ? v : null))
                .Where(r => r.Version is not null).OrderByDescending(r => r.Version).FirstOrDefault();
            if (stable.Version is null) return [];
            root = stable.Release;
        }
        if (!IsStableRelease(root)) return [];
        if (repository == "luolangaga/tubatool" && (!Version.TryParse(Text(root, "tag_name")?.TrimStart('v', 'V'), out var version)
            || version <= UpdateService.CurrentVersion)) return [];
        var result = new List<TrustedReleaseAsset>();
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return result;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object || string.Equals(Text(asset, "type"), "source", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Text(asset, "name") ?? "";
            if (name.Length == 0 || name != Path.GetFileName(name) || name.Contains(':') || name.Contains('\\')) continue;
            var digest = Text(asset, "digest");
            if (string.IsNullOrEmpty(digest)) continue;
            long bytes = 0;
            if (asset.TryGetProperty("size", out var size) && size.ValueKind != JsonValueKind.Null
                && (size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out bytes) || bytes < 0)) continue;
            // GitCode documents size as optional. A valid publisher digest is sufficient when it
            // is omitted; do not force these releases back onto GitHub merely for a size field.
            if (!isGitCode && bytes <= 0) continue;
            var url = Text(asset, "browser_download_url") ?? "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0) continue;
            if (isGitCode)
            {
                if (!(uri.Host == "gitcode.com" || uri.Host.EndsWith(".gitcode.com", StringComparison.Ordinal)
                    || uri.Host == "atomgit.com" || uri.Host.EndsWith(".atomgit.com", StringComparison.Ordinal))) continue;
            }
            else if (uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/" + repository + "/releases/download/", StringComparison.Ordinal)) continue;
            try { result.Add(new(name, url, ParseSha256(digest), bytes)); }
            catch (InvalidDataException) { /* A malformed digest cannot authorize an asset. */ }
        }
        return result;
    }

    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsStableRelease(JsonElement release) => release.ValueKind == JsonValueKind.Object
        && (!release.TryGetProperty("prerelease", out var pre) || pre.ValueKind is JsonValueKind.False or JsonValueKind.Null)
        && (!release.TryGetProperty("draft", out var draft) || draft.ValueKind is JsonValueKind.False or JsonValueKind.Null)
        && !string.Equals(Text(release, "release_status"), "pre", StringComparison.OrdinalIgnoreCase);

    internal static async Task VerifyAsync(Stream file, string expectedSha256, CancellationToken ct)
    {
        var expected = Convert.FromHexString(ParseSha256(expectedSha256));
        file.Position = 0;
        var actual = await SHA256.HashDataAsync(file, ct);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            throw new InvalidDataException("下载文件的 SHA-256 不匹配，已阻止使用。请删除后重新下载。");
        file.Position = 0;
    }
}

internal static class UpdatePackageTrust
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IReadOnlyList<TrustedReleaseAsset>? _assets;
    private static DateTime _expires;

    internal static async Task<FileStream> OpenVerifiedAsync(string path, CancellationToken ct,
        Func<DownloadIntegrityMismatch, CancellationToken, Task<bool>>? confirmMismatch = null)
    {
        var expectedDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TubaWinUi3_Update"));
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), expectedDirectory, StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).ToLowerInvariant() is not (".exe" or ".zip"))
            throw new InvalidDataException("文件不在更新暂存目录中");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0) throw new InvalidDataException("更新文件不存在或为空");
        await Gate.WaitAsync(ct);
        TrustedReleaseAsset? asset;
        try
        {
            if (_assets is null || DateTime.UtcNow >= _expires || !_assets.Any(a => a.Name == info.Name))
            {
                _assets = await TrustedDownloads.LatestAssetsAsync("luolangaga/tubatool", ct, requiredAsset: info.Name);
                _expires = DateTime.UtcNow.AddMinutes(5);
            }
            asset = _assets.FirstOrDefault(a => a.Name == info.Name);
        }
        finally { Gate.Release(); }
        if (asset is null) throw new InvalidDataException("文件未匹配到带可信摘要的官方当前更新包，已阻止安装");
        SafeSharePath.RejectReparsePoints(path);
        // Keep this handle open through Process.Start: writers/renames/deletes are denied.
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        try
        {
            await DownloadIntegrityPolicy.VerifyAsync(file, asset.Sha256, info.Name, ct, confirmMismatch,
                asset.Size > 0 ? asset.Size : null);
            return file;
        }
        catch { await file.DisposeAsync(); throw; }
    }
}
