using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Tubatools.MinecraftLauncher.Services;

internal sealed record McVersionSummary(string Id, string Type, string Url, DateTime ReleaseTime, string? Sha1);

internal sealed class McVersionManifest
{
    public List<McVersionSummary> Versions { get; } = [];
    public string? LatestRelease { get; set; }
    public string? LatestSnapshot { get; set; }
    public DateTime FetchedAt { get; set; }
}

/// <summary>Mojang 版本清单。与 PCL 一样优先使用镜像，失败自动回退官方源。</summary>
internal static class McVersionManifestClient
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);

    public static McVersionManifest Parse(string json)
    {
        var root = McJson.ParseObject(json);
        var manifest = new McVersionManifest
        {
            LatestRelease = root.Obj("latest")?.Str("release"),
            LatestSnapshot = root.Obj("latest")?.Str("snapshot"),
            FetchedAt = DateTime.Now
        };

        if (root["versions"] is JsonArray versions)
            foreach (var node in versions)
            {
                var id = node.Str("id");
                var url = node.Str("url");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url)) continue;
                manifest.Versions.Add(new McVersionSummary(
                    id,
                    node.Str("type") ?? "release",
                    url,
                    node.Date("releaseTime", DateTime.MinValue),
                    node.Str("sha1")));
            }

        return manifest;
    }

    public static async Task<McVersionManifest> FetchAsync(HttpClient http, string mode, string cacheDirectory,
        bool ignoreCache, CancellationToken ct)
    {
        var cachePath = Path.Combine(cacheDirectory, "version_manifest_v2.json");
        if (!ignoreCache && File.Exists(cachePath) &&
            DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < CacheTtl)
        {
            try
            {
                var cached = Parse(await File.ReadAllTextAsync(cachePath, ct));
                if (cached.Versions.Count > 0) return cached;
            }
            catch
            {
                // 缓存损坏时重新下载。
            }
        }

        var errors = new List<string>();
        foreach (var url in McMirror.ManifestUrls(mode))
        {
            try
            {
                var text = await McHttp.GetStringAsync(http, url, TimeSpan.FromSeconds(30), ct);
                var manifest = Parse(text);
                if (manifest.Versions.Count == 0)
                    throw new InvalidDataException("版本清单为空。");
                Directory.CreateDirectory(cacheDirectory);
                var temp = cachePath + ".tmp";
                await File.WriteAllTextAsync(temp, text, ct);
                File.Move(temp, cachePath, true);
                return manifest;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{url}：{ex.Message}");
            }
        }

        throw new HttpRequestException("无法获取 Minecraft 版本清单。\n" + string.Join("\n", errors));
    }
}

/// <summary>统一的 HttpClient 与单次请求超时。</summary>
internal static class McHttp
{
    private static readonly Lazy<HttpClient> Shared = new(() =>
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxConnectionsPerServer = 64,
            UseCookies = false
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Tubatools.MinecraftLauncher", "1.0"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/luolangaga/tubawinui3)"));
        return client;
    });

    public static HttpClient Client => Shared.Value;

    public static async Task<HttpResponseMessage> GetAsync(HttpClient http, string url, HttpCompletionOption option,
        TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await http.SendAsync(request, option, timeoutCts.Token);
    }

    public static async Task<string> GetStringAsync(HttpClient http, string url, TimeSpan timeout, CancellationToken ct)
    {
        using var response = await GetAsync(http, url, HttpCompletionOption.ResponseContentRead, timeout, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    public static string Sha1File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
    }
}

internal sealed record McDownloadItem(
    string Url,
    string TargetPath,
    string? Sha1 = null,
    long Size = -1,
    string Label = "",
    string? FallbackUrl = null);

internal sealed record McDownloadFailure(string Label, string Error);

internal sealed record McDownloadProgress(int Done, int Total, string Label);

internal static class McDownloader
{
    /// <summary>并发下载：已存在且大小一致的文件直接跳过；失败条目跳过并在结束时统一汇总。</summary>
    public static async Task<(int Completed, List<McDownloadFailure> Failures)> DownloadAsync(
        HttpClient http,
        IReadOnlyList<McDownloadItem> items,
        int concurrency,
        IProgress<McDownloadProgress>? progress,
        Action<string>? log,
        CancellationToken ct)
    {
        if (items.Count == 0) return (0, []);

        var failures = new List<McDownloadFailure>();
        var failureLock = new object();
        var completed = 0;
        var index = 0;

        await Parallel.ForEachAsync(items,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, concurrency), CancellationToken = ct },
            async (item, token) =>
            {
                var slot = Interlocked.Increment(ref index);
                var label = string.IsNullOrWhiteSpace(item.Label) ? Path.GetFileName(item.TargetPath) : item.Label;
                try
                {
                    if (await IsUpToDateAsync(item, token))
                    {
                        Interlocked.Increment(ref completed);
                        progress?.Report(new McDownloadProgress(Volatile.Read(ref completed), items.Count, label));
                        return;
                    }

                    await DownloadOneAsync(http, item, token);
                    Interlocked.Increment(ref completed);
                    progress?.Report(new McDownloadProgress(Volatile.Read(ref completed), items.Count, label));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lock (failureLock)
                        failures.Add(new McDownloadFailure(label, ex.Message));
                    log?.Invoke($"[下载失败] {label}：{ex.Message}");
                    Interlocked.Increment(ref completed);
                    progress?.Report(new McDownloadProgress(Volatile.Read(ref completed), items.Count, label));
                }
            });

        return (completed, failures);
    }

    private static async Task<bool> IsUpToDateAsync(McDownloadItem item, CancellationToken ct)
    {
        if (!File.Exists(item.TargetPath)) return false;
        var info = new FileInfo(item.TargetPath);
        if (item.Size > 0) return info.Length == item.Size;
        if (!string.IsNullOrWhiteSpace(item.Sha1))
            return string.Equals(McHttp.Sha1File(item.TargetPath), item.Sha1, StringComparison.OrdinalIgnoreCase);
        return info.Length > 0;
    }

    private static async Task DownloadOneAsync(HttpClient http, McDownloadItem item, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(item.TargetPath)!);
        var part = item.TargetPath + ".part";
        Exception? last = null;

        var urls = new List<string> { item.Url };
        if (!string.IsNullOrWhiteSpace(item.FallbackUrl) &&
            !string.Equals(item.FallbackUrl, item.Url, StringComparison.OrdinalIgnoreCase))
            urls.Add(item.FallbackUrl);

        // 镜像被判定不可用时直接把官方源提到最前。
        if (urls.Count > 1 && McMirrorHealth.MirrorDown && McMirrorHealth.IsMirror(urls[0]))
            urls.Reverse();

        foreach (var url in urls)
        {
            var isMirror = McMirrorHealth.IsMirror(url);
            var attempts = isMirror && urls.Count > 1 ? 1 : 2;
            for (var attempt = 0; attempt < attempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using (var response = await McHttp.GetAsync(http, url, HttpCompletionOption.ResponseHeadersRead,
                               TimeSpan.FromMinutes(3), ct))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                        await using var input = await response.Content.ReadAsStreamAsync(ct);
                        await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None,
                                         81920, true))
                            await input.CopyToAsync(output, ct);
                    }

                    if (!string.IsNullOrWhiteSpace(item.Sha1))
                    {
                        var actual = McHttp.Sha1File(part);
                        if (!string.Equals(actual, item.Sha1, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"SHA-1 校验失败（期望 {item.Sha1}，实际 {actual}）");
                    }

                    if (File.Exists(item.TargetPath))
                        File.SetAttributes(item.TargetPath, FileAttributes.Normal);
                    File.Move(part, item.TargetPath, true);
                    McMirrorHealth.ReportSuccess(url);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    TryDelete(part);
                    throw;
                }
                catch (Exception ex)
                {
                    last = ex;
                    McMirrorHealth.ReportFailure(url);
                    TryDelete(part);
                    if (attempt == attempts - 1) break;
                    await Task.Delay(400, ct);
                }
            }
        }

        throw new IOException(last?.Message ?? "下载失败", last);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
