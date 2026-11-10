using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services;

public sealed record ToolDownloadInfo(
    string DownloadUrl,
    string FileName,
    long Size,
    bool IsArchive,
    bool IsInstaller);

public sealed record ToolDownloadProgress(
    long BytesReceived,
    long TotalBytes,
    double Percentage,
    double SpeedMbps,
    TimeSpan? EstimatedRemaining);

public sealed record ToolUpdateInfo(
    string VersionTag,
    DateTimeOffset? PublishedDate,
    string DownloadUrl,
    string FileName,
    long Size,
    bool IsArchive,
    bool IsInstaller,
    bool HasUpdate = true);

public sealed record GitCodeDirProgress(
    int CurrentFile,
    int TotalFiles,
    string CurrentFileName,
    double Percentage);

public sealed record GitCodeDirResult(
    bool Success,
    int FilesDownloaded,
    int FilesSkipped,
    string? ErrorMessage);

public static class ToolDownloaderService
{
    private const string GitCodeOwner = "luolangaga";
    private const string GitCodeRepo = "tubatool";
    private const string GitHubOwner = "luolangaga";
    private const string GitHubRepo = "tubatool";
    private const string BlenderListingUrl = "https://download.blender.org/release/BlenderBenchmark2.0/launcher/";

    private static readonly Encoding Gb2312;

    static ToolDownloaderService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Gb2312 = Encoding.GetEncoding("GB2312");
    }

    private static HttpClient CreateHttpClient(TimeSpan? timeout = null)
    {
        var client = ProxyService.CreateClient(timeout ?? TimeSpan.FromSeconds(30));
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolDownloader");
        return client;
    }

    public static bool IsGitCodeDir(string? downloadUrl)
        => downloadUrl?.StartsWith("gc:", StringComparison.OrdinalIgnoreCase) == true;

    private const string GitCodeApiBase = $"https://api.gitcode.com/api/v5/repos/{GitCodeOwner}/{GitCodeRepo}";
    private const string GitCodeBlobsBase = $"https://raw.gitcode.com/{GitCodeOwner}/{GitCodeRepo}/blobs";

    public static async Task<GitCodeDirResult> SyncToolFromGitCodeDirAsync(
        string repoPath, string localDir, DateTime? localLastModified,
        IProgress<GitCodeDirProgress>? progress, CancellationToken ct = default)
    {
        try
        {
            var remoteFiles = await ListGitCodeDirAsync(repoPath, ct);
            if (remoteFiles is null || remoteFiles.Count == 0)
                return new GitCodeDirResult(false, 0, 0, $"仓库中未找到路径: {repoPath}");

            var downloaded = 0;
            var skipped = 0;

            for (var i = 0; i < remoteFiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (relPath, sha, fileName) = remoteFiles[i];

                progress?.Report(new GitCodeDirProgress(i + 1, remoteFiles.Count, fileName,
                    (double)(i + 1) / remoteFiles.Count * 100));

                var localPath = Path.Combine(localDir, relPath);
                if (File.Exists(localPath))
                {
                    try
                    {
                        var localContent = await File.ReadAllBytesAsync(localPath, ct);
                        if (ComputeBlobSha(localContent) == sha)
                        {
                            skipped++;
                            continue;
                        }
                    }
                    catch { }
                }

                var dir = Path.GetDirectoryName(localPath)!;
                Directory.CreateDirectory(dir);

                var success = false;
                for (var attempt = 0; attempt < 3 && !success; attempt++)
                {
                    try
                    {
                        var blobUrl = $"{GitCodeBlobsBase}/{sha}/{Uri.EscapeDataString(fileName)}";
                        using var client = CreateHttpClient(TimeSpan.FromMinutes(5));
                        var content = await client.GetByteArrayAsync(blobUrl, ct);
                        if (content is null || content.Length == 0) continue;

                        var tmpPath = localPath + ".tmp";
                        await File.WriteAllBytesAsync(tmpPath, content, ct);
                        try { if (File.Exists(localPath)) File.Delete(localPath); } catch { }
                        File.Move(tmpPath, localPath);
                        success = true;
                        downloaded++;
                    }
                    catch when (attempt < 2)
                    {
                        await Task.Delay(1000 * (attempt + 1), ct);
                    }
                }

                if (!success) skipped++;
            }

            return new GitCodeDirResult(true, downloaded, skipped, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new GitCodeDirResult(false, 0, 0, ex.Message);
        }
    }

    public static async Task<List<(string RelPath, string Sha, string FileName)>?> ListGitCodeDirAsync(
        string repoPath, CancellationToken ct)
    {
        try
        {
            var encodedPath = Uri.EscapeDataString(repoPath);
            var url = $"{GitCodeApiBase}/contents/{encodedPath}?ref=master";
            using var client = CreateHttpClient(TimeSpan.FromSeconds(15));
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolDownloader");

            var json = await client.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var files = new List<(string, string, string)>();

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var type = entry.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "" : "";
                var sha = entry.TryGetProperty("sha", out var shaEl) ? shaEl.GetString() ?? "" : "";
                var name = entry.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";

                if (type == "file" && !string.IsNullOrEmpty(sha))
                    files.Add((name, sha, name));
                else if (type == "dir" && !string.IsNullOrEmpty(name))
                {
                    var subPath = $"{repoPath}/{name}";
                    var subFiles = await ListGitCodeDirAsync(subPath, ct);
                    // 任一子目录失败就丢弃整个清单，交给调用方回退/重试，不能静默漏文件。
                    if (subFiles is null) return null;
                    foreach (var sf in subFiles)
                        files.Add(($"{name}/{sf.RelPath}", sf.Sha, sf.FileName));
                }
            }

            return files;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private const string GitHubApiBase = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}";
    private const string GitHubBlobsBase = $"https://raw.githubusercontent.com/{GitHubOwner}/{GitHubRepo}";

    public static async Task<GitCodeDirResult> SyncToolFromGitHubDirAsync(
        string repoPath, string localDir, DateTime? localLastModified,
        IProgress<GitCodeDirProgress>? progress, CancellationToken ct = default)
    {
        try
        {
            var remoteFiles = await ListGitHubDirAsync(repoPath, ct);
            if (remoteFiles is null || remoteFiles.Count == 0)
                return new GitCodeDirResult(false, 0, 0, $"GitHub 仓库中未找到路径: {repoPath}");

            var downloaded = 0;
            var skipped = 0;

            for (var i = 0; i < remoteFiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var (relPath, sha, fileName) = remoteFiles[i];

                progress?.Report(new GitCodeDirProgress(i + 1, remoteFiles.Count, fileName,
                    (double)(i + 1) / remoteFiles.Count * 100));

                var localPath = Path.Combine(localDir, relPath);
                if (File.Exists(localPath))
                {
                    try
                    {
                        var localContent = await File.ReadAllBytesAsync(localPath, ct);
                        if (ComputeBlobSha(localContent) == sha)
                        {
                            skipped++;
                            continue;
                        }
                    }
                    catch { }
                }

                var dir = Path.GetDirectoryName(localPath)!;
                Directory.CreateDirectory(dir);

                var success = false;
                for (var attempt = 0; attempt < 3 && !success; attempt++)
                {
                    try
                    {
                        var encodedRelPath = string.Join("/", relPath.Split('/').Select(Uri.EscapeDataString));
                        var rawUrl = $"{GitHubBlobsBase}/master/{repoPath}/{encodedRelPath}";
                        using var client = CreateHttpClient(TimeSpan.FromMinutes(5));
                        var content = await client.GetByteArrayAsync(rawUrl, ct);
                        if (content is null || content.Length == 0) continue;

                        var tmpPath = localPath + ".tmp";
                        await File.WriteAllBytesAsync(tmpPath, content, ct);
                        try { if (File.Exists(localPath)) File.Delete(localPath); } catch { }
                        File.Move(tmpPath, localPath);
                        success = true;
                        downloaded++;
                    }
                    catch when (attempt < 2)
                    {
                        await Task.Delay(1000 * (attempt + 1), ct);
                    }
                }

                if (!success) skipped++;
            }

            return new GitCodeDirResult(true, downloaded, skipped, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new GitCodeDirResult(false, 0, 0, ex.Message);
        }
    }

    public static async Task<List<(string RelPath, string Sha, string FileName)>?> ListGitHubDirAsync(
        string repoPath, CancellationToken ct)
    {
        try
        {
            var encodedPath = Uri.EscapeDataString(repoPath);
            var url = $"{GitHubApiBase}/contents/{encodedPath}?ref=master";
            using var client = CreateHttpClient(TimeSpan.FromSeconds(15));
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolDownloader");

            var json = await client.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var files = new List<(string, string, string)>();

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var type = entry.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "" : "";
                var sha = entry.TryGetProperty("sha", out var shaEl) ? shaEl.GetString() ?? "" : "";
                var name = entry.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";

                if (type == "file" && !string.IsNullOrEmpty(sha))
                    files.Add((name, sha, name));
                else if (type == "dir" && !string.IsNullOrEmpty(name))
                {
                    var subPath = $"{repoPath}/{name}";
                    var subFiles = await ListGitHubDirAsync(subPath, ct);
                    if (subFiles is null) return null;
                    foreach (var sf in subFiles)
                        files.Add(($"{name}/{sf.RelPath}", sf.Sha, sf.FileName));
                }
            }

            return files;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    public static async Task<DateTimeOffset?> CheckGitCodeDirUpdateAsync(
        string repoPath, DateTime? localLastModified, CancellationToken ct = default)
    {
        return null;
    }

    public static string ComputeBlobSha(byte[] content)
    {
        var header = System.Text.Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        using var sha1 = SHA1.Create();
        sha1.TransformBlock(header, 0, header.Length, null, 0);
        sha1.TransformFinalBlock(content, 0, content.Length);
        return Convert.ToHexString(sha1.Hash!).ToLowerInvariant();
    }

    public static async Task<ToolDownloadInfo?> ResolveDownloadUrlAsync(
        string downloadUrl, string? filter, CancellationToken ct = default)
    {
        if (downloadUrl.StartsWith("gc:", StringComparison.OrdinalIgnoreCase))
            return null;

        if (downloadUrl.StartsWith("gh:", StringComparison.OrdinalIgnoreCase))
        {
            var repo = downloadUrl[3..];
            return await ResolveGitHubReleaseAsync(repo, filter, ct);
        }

        if (downloadUrl.Contains("blender.org", StringComparison.OrdinalIgnoreCase) &&
            downloadUrl.Contains("launcher", StringComparison.OrdinalIgnoreCase))
        {
            return await ResolveBlenderBenchmarkAsync(ct);
        }

        if (downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return await ResolveDirectUrlAsync(downloadUrl, ct);
        }

        return null;
    }

    private static async Task<ToolDownloadInfo?> ResolveDirectUrlAsync(
        string url, CancellationToken ct)
    {
        try
        {
            using var client = CreateHttpClient(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await client.SendAsync(request, ct);

            var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "download";

            var size = response.Content.Headers.ContentLength ?? 0L;
            var isArchive = fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
            var isInstaller = fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                              fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);

            return new ToolDownloadInfo(url, fileName, size, isArchive, isInstaller);
        }
        catch
        {
            var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "download";

            var isArchive = fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
            var isInstaller = fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                              fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);

            return new ToolDownloadInfo(url, fileName, 0, isArchive, isInstaller);
        }
    }

    private static async Task<ToolDownloadInfo?> ResolveGitHubReleaseAsync(
        string repo, string? filter, CancellationToken ct)
    {
        var result = await ResolveGitCodeReleaseAsync(repo, filter, ct);
        if (result is not null) return result;

        return await ResolveGitHubDirectReleaseAsync(repo, filter, ct);
    }

    private static async Task<ToolDownloadInfo?> ResolveGitCodeReleaseAsync(
        string repo, string? filter, CancellationToken ct)
    {
        try
        {
            using var client = CreateHttpClient(TimeSpan.FromSeconds(15));

            var parts = repo.Split('/', 2);
            var owner = parts.Length > 0 ? parts[0] : "";
            var repoName = parts.Length > 1 ? parts[1] : "";

            var apiUrl = $"https://api.gitcode.com/api/v5/repos/{owner}/{repoName}/releases/latest";
            var json = await client.GetStringAsync(apiUrl, ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("assets", out var assetsEl))
                return null;

            JsonElement bestAsset = default;
            var found = false;

            if (!string.IsNullOrWhiteSpace(filter))
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (LikeMatch(name, filter))
                    {
                        bestAsset = asset;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        bestAsset = asset;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
                return null;

            var assetName = bestAsset.GetProperty("name").GetString() ?? "";
            var assetUrl = bestAsset.GetProperty("browser_download_url").GetString() ?? "";
            var assetSize = bestAsset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0L;

            var isArchive = assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
            var isInstaller = assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                              assetName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);

            return new ToolDownloadInfo(assetUrl, assetName, assetSize, isArchive, isInstaller);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ToolDownloadInfo?> ResolveGitHubDirectReleaseAsync(
        string repo, string? filter, CancellationToken ct)
    {
        try
        {
            using var client = CreateHttpClient(TimeSpan.FromSeconds(15));

            var apiUrl = $"https://api.github.com/repos/{repo}/releases/latest";
            var json = await client.GetStringAsync(apiUrl, ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("assets", out var assetsEl))
                return null;

            JsonElement bestAsset = default;
            var found = false;

            if (!string.IsNullOrWhiteSpace(filter))
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (LikeMatch(name, filter))
                    {
                        bestAsset = asset;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        bestAsset = asset;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
                return null;

            var assetName = bestAsset.GetProperty("name").GetString() ?? "";
            var assetUrl = bestAsset.GetProperty("browser_download_url").GetString() ?? "";
            var assetSize = bestAsset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0L;

            var isArchive = assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
            var isInstaller = assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                              assetName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);

            return new ToolDownloadInfo(assetUrl, assetName, assetSize, isArchive, isInstaller);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<ToolDownloadInfo?> ResolveBlenderBenchmarkAsync(CancellationToken ct)
    {
        try
        {
            using var client = CreateHttpClient(TimeSpan.FromSeconds(30));
            var html = await client.GetStringAsync(BlenderListingUrl, ct);

            var bestHref = "";
            var bestVersion = "";

            var idx = 0;
            while ((idx = html.IndexOf("benchmark-launcher-", idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var hrefStart = html.IndexOf('"', idx);
                if (hrefStart < 0) break;
                var hrefEnd = html.IndexOf('"', hrefStart + 1);
                if (hrefEnd < 0) break;

                var href = html[(hrefStart + 1)..hrefEnd];
                idx = hrefEnd + 1;

                if (!href.EndsWith("-windows.zip", StringComparison.OrdinalIgnoreCase)) continue;
                if (href.Contains("-cli-", StringComparison.OrdinalIgnoreCase)) continue;

                var versionPart = ExtractVersion(href);
                if (string.IsNullOrEmpty(versionPart)) continue;

                if (string.Compare(versionPart, bestVersion, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    bestVersion = versionPart;
                    bestHref = href;
                }
            }

            if (string.IsNullOrEmpty(bestHref))
                return null;

            var url = BlenderListingUrl + bestHref;
            var fileName = Path.GetFileName(bestHref);

            return new ToolDownloadInfo(url, fileName, 0, true, false);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<string> DownloadToFileAsync(
        string url, string destinationDir, string fileName,
        IProgress<ToolDownloadProgress>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destinationDir);
        var filePath = Path.Combine(destinationDir, fileName);
        if (File.Exists(filePath)) File.Delete(filePath);

        using var client = CreateHttpClient(TimeSpan.FromMinutes(30));
        var sw = Stopwatch.StartNew();

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var fs = File.Create(filePath);

        var buffer = new byte[81920];
        long bytesRead = 0;
        var lastReport = sw.Elapsed;
        long lastBytes = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;

            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
            bytesRead += read;

            var now = sw.Elapsed;
            if (now - lastReport > TimeSpan.FromMilliseconds(300))
            {
                var chunkBytes = bytesRead - lastBytes;
                var chunkTime = (now - lastReport).TotalSeconds;
                var speedMbps = chunkBytes / Math.Max(chunkTime, 0.001) * 8 / 1_000_000;
                var percentage = totalBytes > 0 ? (double)bytesRead / totalBytes * 100 : 0;
                var remaining = totalBytes > 0 && speedMbps > 0
                    ? TimeSpan.FromSeconds((totalBytes - bytesRead) / Math.Max(speedMbps * 1_000_000 / 8, 1))
                    : (TimeSpan?)null;

                progress?.Report(new ToolDownloadProgress(bytesRead, totalBytes, percentage, speedMbps, remaining));
                lastReport = now;
                lastBytes = bytesRead;
            }
        }

        progress?.Report(new ToolDownloadProgress(bytesRead, totalBytes, 100, 0, TimeSpan.Zero));
        return filePath;
    }

    public static async Task ExtractArchiveAsync(string archivePath, string destinationDir, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            if (File.Exists(archivePath))
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, destinationDir, true);
                File.Delete(archivePath);
            }
        }, ct);
    }

    private static bool LikeMatch(string input, string pattern)
    {
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(input, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string ExtractVersion(string href)
    {
        var match = System.Text.RegularExpressions.Regex.Match(href, @"(\d+\.\d+\.\d+)");
        return match.Success ? match.Groups[1].Value : "";
    }

    public static async Task<ToolUpdateInfo?> CheckForUpdateAsync(
        string? downloadUrl, string? downloadFilter,
        string? localVersion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl)) return null;

        if (downloadUrl.StartsWith("gc:", StringComparison.OrdinalIgnoreCase))
            return null;

        return await CheckGitHubUpdateAsync(downloadUrl, downloadFilter, localVersion, ct);
    }

    private static async Task<ToolUpdateInfo?> CheckGitHubUpdateAsync(
        string downloadUrl, string? filter, string? localVersion, CancellationToken ct)
    {
        if (!downloadUrl.StartsWith("gh:", StringComparison.OrdinalIgnoreCase)) return null;

        var repo = downloadUrl[3..];

        var result = await CheckGitCodeUpdateAsync(repo, filter, localVersion, ct);
        if (result is not null) return result;

        return await CheckGitHubDirectUpdateAsync(repo, filter, localVersion, ct);
    }

    private static async Task<ToolUpdateInfo?> CheckGitCodeUpdateAsync(
        string repo, string? filter, string? localVersion, CancellationToken ct)
    {
        try
        {
            using var client = CreateHttpClient(TimeSpan.FromSeconds(15));

            var parts = repo.Split('/', 2);
            var owner = parts.Length > 0 ? parts[0] : "";
            var repoName = parts.Length > 1 ? parts[1] : "";

            var apiUrl = $"https://api.gitcode.com/api/v5/repos/{owner}/{repoName}/releases/latest";
            var json = await client.GetStringAsync(apiUrl, ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
            DateTimeOffset? published = root.TryGetProperty("published_at", out var pubEl)
                && pubEl.ValueKind == JsonValueKind.String
                ? pubEl.GetDateTimeOffset() : null;

            if (!root.TryGetProperty("assets", out var assetsEl)) return null;

            JsonElement bestAsset = default;
            var found = false;

            if (!string.IsNullOrWhiteSpace(filter))
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (LikeMatch(name, filter)) { bestAsset = asset; found = true; break; }
                }
            }

            if (!found)
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    { bestAsset = asset; found = true; break; }
                }
            }

            if (!found) return null;

            var assetName = bestAsset.GetProperty("name").GetString() ?? "";
            var assetUrl = bestAsset.GetProperty("browser_download_url").GetString() ?? "";
            var assetSize = bestAsset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0L;

            var isArchive = assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
            var isInstaller = assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                              assetName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);

            var hasUpdate = true;
            if (!string.IsNullOrWhiteSpace(localVersion) && !string.IsNullOrWhiteSpace(tag))
            {
                var remoteVer = NormalizeVersion(tag);
                var localVer = NormalizeVersion(localVersion);
                if (!string.IsNullOrEmpty(remoteVer) && !string.IsNullOrEmpty(localVer)
                    && string.Compare(remoteVer, localVer, StringComparison.OrdinalIgnoreCase) <= 0)
                    hasUpdate = false;
            }

            return new ToolUpdateInfo(tag, published, assetUrl, assetName, assetSize, isArchive, isInstaller, hasUpdate);
        }
        catch { return null; }
    }

    private static async Task<ToolUpdateInfo?> CheckGitHubDirectUpdateAsync(
        string repo, string? filter, string? localVersion, CancellationToken ct)
    {
        try
        {
            using var client = CreateHttpClient(TimeSpan.FromSeconds(15));

            var apiUrl = $"https://api.github.com/repos/{repo}/releases/latest";
            var json = await client.GetStringAsync(apiUrl, ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
            DateTimeOffset? published = root.TryGetProperty("published_at", out var pubEl)
                && pubEl.ValueKind == JsonValueKind.String
                ? pubEl.GetDateTimeOffset() : null;

            if (!root.TryGetProperty("assets", out var assetsEl)) return null;

            JsonElement bestAsset = default;
            var found = false;

            if (!string.IsNullOrWhiteSpace(filter))
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (LikeMatch(name, filter)) { bestAsset = asset; found = true; break; }
                }
            }

            if (!found)
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    { bestAsset = asset; found = true; break; }
                }
            }

            if (!found) return null;

            var assetName = bestAsset.GetProperty("name").GetString() ?? "";
            var assetUrl = bestAsset.GetProperty("browser_download_url").GetString() ?? "";
            var assetSize = bestAsset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0L;

            var isArchive = assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
            var isInstaller = assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                              assetName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);

            var hasUpdate = true;
            if (!string.IsNullOrWhiteSpace(localVersion) && !string.IsNullOrWhiteSpace(tag))
            {
                var remoteVer = NormalizeVersion(tag);
                var localVer = NormalizeVersion(localVersion);
                if (!string.IsNullOrEmpty(remoteVer) && !string.IsNullOrEmpty(localVer)
                    && string.Compare(remoteVer, localVer, StringComparison.OrdinalIgnoreCase) <= 0)
                    hasUpdate = false;
            }

            return new ToolUpdateInfo(tag, published, assetUrl, assetName, assetSize, isArchive, isInstaller, hasUpdate);
        }
        catch { return null; }
    }

    private static string NormalizeVersion(string ver)
    {
        var match = System.Text.RegularExpressions.Regex.Match(ver, @"(\d+(?:\.\d+)+)");
        return match.Success ? match.Groups[1].Value : "";
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{(double)bytes / (1L << 30):F2} GB";
        if (bytes >= 1L << 20) return $"{(double)bytes / (1L << 20):F1} MB";
        if (bytes >= 1L << 10) return $"{(double)bytes / (1L << 10):F1} KB";
        return $"{bytes} B";
    }

    public static string FormatSpeed(double mbps)
    {
        if (mbps >= 1000) return $"{mbps / 1000:F2} Gbps";
        if (mbps >= 1) return $"{mbps:F2} Mbps";
        return $"{mbps * 1000:F0} Kbps";
    }

    public static string FormatTime(TimeSpan? time)
    {
        if (time is null || time.Value.TotalSeconds <= 0) return "--";
        var t = time.Value;
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }
}
