using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class UpdateService
{
    private const string Owner = "luolangaga";
    private const string Repo = "tubatool";
    private const string GitHubReleaseApi = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    private const string GitCodeOwner = "luolangaga";
    private const string GitCodeRepo = "tubatool";
    private const string GitCodeReleaseApiBase = $"https://api.gitcode.com/api/v5/repos/{GitCodeOwner}/{GitCodeRepo}/releases";

    private static string? _cachedEtag;
    private static string? _cachedJson;
    private static DateTime _lastCheckTime = DateTime.MinValue;

    public static string CurrentArchitecture { get; } = RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => "x64"
    };

    private static HttpClient CreateHttpClient(TimeSpan? timeout = null)
    {
        var client = ProxyService.CreateClient(timeout ?? TimeSpan.FromSeconds(30));
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-UpdateChecker");
        return client;
    }

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is not null ? new Version(v.Major, v.Minor, v.Build) : new Version(1, 0, 0);
        }
    }

    public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (_cachedJson is not null && DateTime.Now - _lastCheckTime < TimeSpan.FromMinutes(10))
            return ParseUpdateJson(_cachedJson);

        var json = await FetchReleaseJsonAsync(ct);
        if (json is null) return null;

        _cachedJson = json;
        _lastCheckTime = DateTime.Now;

        var updateInfo = ParseUpdateJson(json);
        if (updateInfo is null) return null;

        var tagName = $"v{updateInfo.Version}";
        var gitCodeTask = FetchGitCodeAssetsAsync(tagName, ct);

        try
        {
            var gitCodeAssets = await gitCodeTask;
            if (gitCodeAssets is not null)
            {
                foreach (var asset in updateInfo.Assets)
                {
                    if (gitCodeAssets.TryGetValue(asset.Name, out var gitCodeUrl))
                        asset.GitCodeDownloadUrl = gitCodeUrl;
                }
            }
        }
        catch { }

        return updateInfo;
    }

    private static async Task<string?> FetchReleaseJsonAsync(CancellationToken ct)
    {
        string? gitCodeJson = null;
        try
        {
            using var gitCodeClient = CreateHttpClient(TimeSpan.FromSeconds(15));
            var gitCodeUrl = $"{GitCodeReleaseApiBase}/latest";
            var gitCodeResponse = await gitCodeClient.GetAsync(gitCodeUrl, ct);
            if (gitCodeResponse.IsSuccessStatusCode)
                gitCodeJson = await gitCodeResponse.Content.ReadAsStringAsync(ct);
        }
        catch { }

        if (gitCodeJson is not null && ParseUpdateJson(gitCodeJson) is not null)
            return gitCodeJson;

        // GitCode 的 /latest 不保证跳过预发布版（返回预发布时上面的解析已判为不可用），
        // 改为从发行版列表里挑最新正式版，避免国内用户拿不到更新。
        var gitCodeStableJson = await FetchGitCodeLatestStableJsonAsync(ct);
        if (gitCodeStableJson is not null && ParseUpdateJson(gitCodeStableJson) is not null)
            return gitCodeStableJson;

        try
        {
            using var httpClient = CreateHttpClient(TimeSpan.FromSeconds(30));
            var request = new HttpRequestMessage(HttpMethod.Get, GitHubReleaseApi);
            if (_cachedEtag is not null)
                request.Headers.Add("If-None-Match", _cachedEtag);

            var response = await httpClient.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
                return _cachedJson;

            if (response.IsSuccessStatusCode)
            {
                _cachedEtag = response.Headers.ETag?.Tag;
                return await response.Content.ReadAsStringAsync(ct);
            }
        }
        catch { }

        return gitCodeJson;
    }

    /// <summary>
    /// 取 GitCode 发行版列表里最新的正式版 JSON（跳过草稿与预发布），失败或无正式版时返回 null。
    /// </summary>
    private static async Task<string?> FetchGitCodeLatestStableJsonAsync(CancellationToken ct)
    {
        try
        {
            using var client = CreateHttpClient(TimeSpan.FromSeconds(15));
            // direction=desc：GitCode 列表默认最旧在前，不能依赖数组顺序
            var url = $"{GitCodeReleaseApiBase}?per_page=30&direction=desc";
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            return PickNewestStableReleaseJson(await response.Content.ReadAsStringAsync(ct));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从发行版列表 JSON 中挑出最新的正式版（单个发行版的原始 JSON）。
    /// 与 /releases/latest 语义一致：跳过草稿和预发布，因此更新通道不会拿到预览版；
    /// 也不依赖数组顺序（GitHub 最新在前、GitCode 最旧在前），统一按时间戳判定。
    /// </summary>
    internal static string? PickNewestStableReleaseJson(string releasesJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(releasesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            string? best = null;
            var bestTime = DateTimeOffset.MinValue;

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out var draftEl) && draftEl.GetBoolean()) continue;
                if (release.TryGetProperty("prerelease", out var preEl) && preEl.GetBoolean()) continue;
                if (!release.TryGetProperty("tag_name", out var tagEl) || string.IsNullOrEmpty(tagEl.GetString()))
                    continue;

                var time = release.TryGetProperty("published_at", out var publishedEl) &&
                           publishedEl.TryGetDateTimeOffset(out var publishedAt)
                    ? publishedAt
                    : release.TryGetProperty("created_at", out var createdEl) &&
                      createdEl.TryGetDateTimeOffset(out var createdAt)
                        ? createdAt
                        : DateTimeOffset.MinValue;

                if (best is null || time > bestTime)
                {
                    best = release.GetRawText();
                    bestTime = time;
                }
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<Dictionary<string, string>?> FetchGitCodeAssetsAsync(string tagName, CancellationToken ct = default)
    {
        try
        {
            using var client = CreateHttpClient(TimeSpan.FromSeconds(15));

            var url = $"{GitCodeReleaseApiBase}/tags/{Uri.EscapeDataString(tagName)}";
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("assets", out var assetsEl)) return null;

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assetsEl.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                var downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(downloadUrl))
                    result[name] = downloadUrl;
            }

            return result.Count > 0 ? result : null;
        }
        catch
        {
            return null;
        }
    }

    internal static UpdateInfo? ParseUpdateJson(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 更新通道只跟随正式版：预发布版（预览版）不作为更新来源
            if (root.TryGetProperty("prerelease", out var prereleaseEl) &&
                prereleaseEl.ValueKind == JsonValueKind.True)
                return null;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var versionStr = tagName.TrimStart('v', 'V');

            if (!Version.TryParse(versionStr, out var remoteVersion))
                return null;

            if (remoteVersion <= CurrentVersion)
                return null;

            var assets = new List<UpdateAsset>();
            if (root.TryGetProperty("assets", out var assetsEl))
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    var originalUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                    var size = asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;
                    var contentType = asset.TryGetProperty("content_type", out var ctEl) ? ctEl.GetString() : null;
                    var assetType = asset.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

                    if (string.Equals(assetType, "source", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase))
                    {
                        assets.Add(new UpdateAsset
                        {
                            Name = name,
                            BrowserDownloadUrl = originalUrl,
                            OriginalDownloadUrl = originalUrl,
                            Size = size,
                            ContentType = contentType
                        });
                    }
                }
            }

            var htmlUrl = root.TryGetProperty("html_url", out var htmlEl) ? htmlEl.GetString() ?? "" : "";
            var publishedAt = root.TryGetProperty("published_at", out var pubEl)
                ? pubEl.GetDateTimeOffset()
                : root.TryGetProperty("created_at", out var createdEl)
                    ? createdEl.GetDateTimeOffset()
                    : DateTimeOffset.UtcNow;

            return new UpdateInfo
            {
                Version = versionStr,
                HtmlUrl = htmlUrl,
                Body = root.TryGetProperty("body", out var body) ? body.GetString() : null,
                PublishedAt = publishedAt,
                Assets = assets
            };
        }
        catch
        {
            return null;
        }
    }

    private static string SkipVersionFilePath => ConfigManager.GetSkippedVersionPath();

    public static string? GetSkippedVersion()
    {
        try
        {
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            return settings.Values["SkippedUpdateVersion"] as string;
        }
        catch { }

        try
        {
            if (File.Exists(SkipVersionFilePath))
                return File.ReadAllText(SkipVersionFilePath).Trim();
        }
        catch { }

        return null;
    }

    public static void SetSkippedVersion(string version)
    {
        try
        {
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            settings.Values["SkippedUpdateVersion"] = version;
            return;
        }
        catch { }

        try
        {
            var dir = Path.GetDirectoryName(SkipVersionFilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(SkipVersionFilePath, version);
        }
        catch { }
    }

    public static DownloadItem EnqueueUpdateDownload(
        UpdateAsset asset, bool useGitCode, bool isPortableMode)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TubaWinUi3_Update");

        if (useGitCode && !string.IsNullOrEmpty(asset.GitCodeDownloadUrl))
        {
            return DownloadQueueService.Enqueue(
                displayName: $"软件更新 v{CurrentVersion} → v{asset.Name}",
                downloadUrl: asset.GitCodeDownloadUrl,
                destinationPath: tempDir,
                postProcessor: new UpdateInstallProcessor(isPortableMode),
                description: $"GitCode 下载 · {asset.Name} · {FormatSize(asset.Size)}",
                glyph: "\uE895");
        }

        if (!string.IsNullOrEmpty(asset.OriginalDownloadUrl))
        {
            if (!string.IsNullOrEmpty(asset.GitCodeDownloadUrl))
            {
                return DownloadQueueService.EnqueueWithResolver(
                    displayName: $"软件更新 v{CurrentVersion}",
                    urlResolver: async ct =>
                    {
                        Exception? lastError = null;
                        var urls = new List<(string Url, string Label)>
                        {
                            (asset.GitCodeDownloadUrl!, "GitCode"),
                            (asset.OriginalDownloadUrl!, "GitHub")
                        };

                        foreach (var (url, _) in urls)
                        {
                            try
                            {
                                using var client = CreateHttpClient(TimeSpan.FromSeconds(15));
                                using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                                resp.EnsureSuccessStatusCode();
                                var size = resp.Content.Headers.ContentLength ?? asset.Size;
                                return new ResolvedDownloadUrl(url, asset.Name, size);
                            }
                            catch (Exception ex) { lastError = ex; }
                        }
                        throw lastError!;
                    },
                    destinationPath: tempDir,
                    postProcessor: new UpdateInstallProcessor(isPortableMode),
                    description: $"GitHub 下载 · {asset.Name} · {FormatSize(asset.Size)}",
                    glyph: "\uE895");
            }

            return DownloadQueueService.Enqueue(
                displayName: $"软件更新 v{CurrentVersion}",
                downloadUrl: asset.OriginalDownloadUrl,
                destinationPath: tempDir,
                postProcessor: new UpdateInstallProcessor(isPortableMode),
                description: $"GitHub 下载 · {asset.Name} · {FormatSize(asset.Size)}",
                glyph: "\uE895");
        }

        throw new InvalidOperationException("无可用的下载链接");
    }

    [Obsolete("Use EnqueueUpdateDownload instead for download queue integration")]
    public static async Task<string> DownloadFromGitCodeAsync(
        UpdateAsset asset, IProgress<DownloadProgress>? progress, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(asset.GitCodeDownloadUrl))
            throw new InvalidOperationException("GitCode 下载链接不可用");

        return await DownloadFileAsync(asset.GitCodeDownloadUrl, asset, progress, ct);
    }

    [Obsolete("Use EnqueueUpdateDownload instead for download queue integration")]
    public static async Task<string> DownloadUpdateAsync(
        UpdateAsset asset, IProgress<DownloadProgress>? progress,
        CancellationToken ct = default)
    {
        var urls = new List<string>();

        if (!string.IsNullOrEmpty(asset.GitCodeDownloadUrl))
            urls.Add(asset.GitCodeDownloadUrl);

        if (!string.IsNullOrEmpty(asset.OriginalDownloadUrl))
            urls.Add(asset.OriginalDownloadUrl);

        Exception? lastError = null;

        foreach (var downloadUrl in urls)
        {
            try
            {
                return await DownloadFileAsync(downloadUrl, asset, progress, ct);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw lastError!;
    }

    private static async Task<string> DownloadFileAsync(
        string downloadUrl, UpdateAsset asset, IProgress<DownloadProgress>? progress,
        CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TubaWinUi3_Update");
        Directory.CreateDirectory(tempDir);

        var filePath = Path.Combine(tempDir, asset.Name);
        if (File.Exists(filePath))
            File.Delete(filePath);

        using var client = CreateHttpClient(TimeSpan.FromMinutes(30));
        var sw = Stopwatch.StartNew();

        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? asset.Size;
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

                progress?.Report(new DownloadProgress
                {
                    BytesReceived = bytesRead,
                    TotalBytes = totalBytes,
                    Percentage = percentage,
                    SpeedMbps = speedMbps,
                    Elapsed = now,
                    EstimatedRemaining = remaining
                });

                lastReport = now;
                lastBytes = bytesRead;
            }
        }

        progress?.Report(new DownloadProgress
        {
            BytesReceived = bytesRead,
            TotalBytes = totalBytes,
            Percentage = 100,
            SpeedMbps = 0,
            Elapsed = sw.Elapsed,
            EstimatedRemaining = TimeSpan.Zero
        });

        return filePath;
    }

    private static string? _pendingUpdateVersion;
    private static DownloadItem? _pendingDownloadItem;

    public static event Action<UpdateInfo>? UpdateDownloaded;

    public static string? PendingUpdateVersion => _pendingUpdateVersion;
    public static DownloadItem? PendingDownloadItem => _pendingDownloadItem;

    private static string UpdateTempDir => Path.Combine(Path.GetTempPath(), "TubaWinUi3_Update");

    public static string? FindDownloadedUpdateFile()
    {
        try
        {
            var dir = UpdateTempDir;
            if (!Directory.Exists(dir)) return null;
            return Directory.GetFiles(dir)
                .FirstOrDefault(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                  || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    public static bool IsUpdateAlreadyDownloaded(UpdateInfo update)
    {
        var file = FindDownloadedUpdateFile();
        if (file is null) return false;
        var fileName = Path.GetFileName(file);
        return update.Assets.Any(a =>
            string.Equals(a.Name, fileName, StringComparison.OrdinalIgnoreCase));
    }

    public static void LaunchDownloadedUpdate()
    {
        LaunchScannedUpdate();
    }

    /// <summary>
    /// 扫描更新目录中的安装包并安全启动（exe 启动 / zip 打开所在文件夹）。永不抛出。
    /// </summary>
    private static bool LaunchScannedUpdate()
    {
        var file = FindDownloadedUpdateFile();
        if (file is null) return false;

        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            if (TryLaunchInstaller(file))
            {
                // 安装器已经起来，本进程必须真的退出（走 App.RequestExit：「关闭时最小化到
                // 系统托盘」会把直接关窗口解读成隐藏，安装器随后会撞上正在运行的程序）
                App.RequestExit();
                return true;
            }
            return false;
        }
        else
        {
            var folder = Path.GetDirectoryName(file)!;
            Process.Start("explorer.exe", folder);
            return true;
        }
    }

    public static DownloadItem? AutoDownloadUpdate(UpdateInfo update)
    {
        var isPortable = !RuntimeHelper.IsInstalled;
        var asset = isPortable
            ? FindBestPortableAsset(update.Assets)
            : FindBestInstallerAsset(update.Assets);

        if (asset is null) return null;

        var tempDir = Path.Combine(Path.GetTempPath(), "TubaWinUi3_Update");

        if (!string.IsNullOrEmpty(asset.GitCodeDownloadUrl))
        {
            var item = DownloadQueueService.EnqueueWithResolver(
                displayName: $"软件更新 v{CurrentVersion}",
                urlResolver: async ct =>
                {
                    Exception? lastError = null;
                    var urls = new List<(string Url, string Label)>
                    {
                        (asset.GitCodeDownloadUrl!, "GitCode"),
                        (asset.OriginalDownloadUrl!, "GitHub")
                    };

                    foreach (var (url, _) in urls)
                    {
                        try
                        {
                            using var client = CreateHttpClient(TimeSpan.FromSeconds(15));
                            using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                            resp.EnsureSuccessStatusCode();
                            var size = resp.Content.Headers.ContentLength ?? asset.Size;
                            return new ResolvedDownloadUrl(url, asset.Name, size);
                        }
                        catch (Exception ex) { lastError = ex; }
                    }
                    throw lastError!;
                },
                destinationPath: tempDir,
                postProcessor: new DelegatePostProcessor("更新就绪", (_, _, _, _) => Task.CompletedTask),
                description: $"GitCode 优先 · {asset.Name} · {FormatSize(asset.Size)}",
                glyph: "\uE895");

            _pendingUpdateVersion = update.Version;
            _pendingDownloadItem = item;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DownloadItem.State) && item.State == DownloadItemState.Completed)
                    UpdateDownloaded?.Invoke(update);
            };
            return item;
        }

        if (!string.IsNullOrEmpty(asset.OriginalDownloadUrl))
        {
            var item = DownloadQueueService.Enqueue(
                displayName: $"软件更新 v{CurrentVersion}",
                downloadUrl: asset.OriginalDownloadUrl,
                destinationPath: tempDir,
                postProcessor: new DelegatePostProcessor("更新就绪", (_, _, _, _) => Task.CompletedTask),
                description: $"GitHub 下载 · {asset.Name} · {FormatSize(asset.Size)}",
                glyph: "\uE895");

            _pendingUpdateVersion = update.Version;
            _pendingDownloadItem = item;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DownloadItem.State) && item.State == DownloadItemState.Completed)
                    UpdateDownloaded?.Invoke(update);
            };
            return item;
        }

        return null;
    }

    public static void LaunchUpdate()
    {
        LaunchDownloadedUpdate();
    }

    /// <summary>
    /// 启动待定更新安装包。返回是否成功启动；失败时不会抛出异常
    /// （文件可能被杀毒软件、清理工具或磁盘错误影响）。
    /// </summary>
    public static bool LaunchUpdateFromItem()
    {
        if (_pendingDownloadItem is not null)
        {
            var fileName = _pendingDownloadItem.ResolvedFileName;
            if (string.IsNullOrEmpty(fileName))
                return false;

            var filePath = Path.Combine(UpdateTempDir, fileName);
            if (!File.Exists(filePath))
                return false; // 待定安装包已不存在（被清理/删除），不再回退启动目录中的旧文件

            if (filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (TryLaunchInstaller(filePath))
                {
                    // 同上：安装器已启动，本进程必须真的退出
                    App.RequestExit();
                    return true;
                }
                return false;
            }
            else
            {
                Process.Start("explorer.exe", UpdateTempDir);
                return true;
            }
        }

        // 待定项不可用（如启动时检测到上一会话已下载的更新）时回退到目录扫描
        return LaunchScannedUpdate();
    }

    /// <summary>待定更新文件（或目录扫描到的更新文件）是否存在且有效。</summary>
    public static bool IsPendingUpdateReady()
    {
        if (_pendingDownloadItem is not null && !string.IsNullOrEmpty(_pendingDownloadItem.ResolvedFileName))
        {
            var filePath = Path.Combine(UpdateTempDir, _pendingDownloadItem.ResolvedFileName);
            return IsInstallerFileValid(filePath);
        }

        var file = FindDownloadedUpdateFile();
        return file is not null && IsInstallerFileValid(file);
    }

    /// <summary>在资源管理器中打开更新下载目录。</summary>
    public static void OpenUpdateFolder()
    {
        try
        {
            Directory.CreateDirectory(UpdateTempDir);
            Process.Start("explorer.exe", UpdateTempDir);
        }
        catch { }
    }

    /// <summary>
    /// 校验更新文件是否可用：存在、非空；exe 额外校验 PE 魔数（MZ 头）。
    /// 返回 false 时文件大概率已损坏或被外部改动，应删除后重新下载。
    /// </summary>
    public static bool IsInstallerFileValid(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length <= 0) return false;

            if (filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                var magic = new byte[2];
                return fs.Read(magic, 0, 2) == 2 && magic[0] == (byte)'M' && magic[1] == (byte)'Z';
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 校验并安全启动下载好的更新安装包（永不抛出）。
    /// 刚下载到临时目录的 exe 可能被杀毒软件扫描/隔离、清理工具删除或受磁盘错误影响，
    /// 直接 Process.Start 会抛 Win32Exception 导致 UI 崩溃；这里先做存在性 + PE 魔数校验，
    /// 启动失败后短暂等待重试一次，仍失败则删除失效文件。
    /// </summary>
    private static bool TryLaunchInstaller(string filePath)
    {
        if (!IsInstallerFileValid(filePath))
        {
            TryDeleteInvalidFile(filePath);
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = filePath,
            // 显式指定工作目录，避免依赖应用当前目录（可能指向已失效的卷）
            WorkingDirectory = Path.GetDirectoryName(filePath) ?? Path.GetTempPath(),
            UseShellExecute = true
        };

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                Process.Start(psi);
                return true;
            }
            catch (Exception) when (attempt == 0)
            {
                // 文件可能在校验与启动之间被外部改动（杀软扫描等瞬时占用），等待后重试一次
                Thread.Sleep(800);
            }
            catch (Exception)
            {
                // 重试仍失败：文件已不可用
                break;
            }
        }

        TryDeleteInvalidFile(filePath);
        return false;
    }

    private static void TryDeleteInvalidFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
        catch { }
    }

    public static void ClearPendingUpdate()
    {
        _pendingUpdateVersion = null;
        _pendingDownloadItem = null;
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

    public static UpdateAsset? FindBestAsset(List<UpdateAsset> assets)
    {
        var arch = CurrentArchitecture;

        var match = assets.FirstOrDefault(a =>
            a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (match is not null) return match;

        match = assets.FirstOrDefault(a =>
            a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
            (a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
             a.Name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) ||
             a.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)));

        if (match is not null) return match;

        match = assets.FirstOrDefault(a =>
            a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase));

        return match;
    }

    public static UpdateAsset? FindBestPortableAsset(List<UpdateAsset> assets)
    {
        if (RuntimeHelper.IsLiteBuild)
            return FindBestLiteAsset(assets);

        var arch = CurrentArchitecture;

        var match = assets.FirstOrDefault(a =>
            a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            !a.Name.Contains("Lite", StringComparison.OrdinalIgnoreCase));

        if (match is not null) return match;

        match = assets.FirstOrDefault(a =>
            a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
            (a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
             a.Name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase) ||
             a.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)));

        if (match is not null) return match;

        match = assets.FirstOrDefault(a =>
            a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
            !a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        if (match is not null) return match;

        return FindBestAsset(assets);
    }

    public static UpdateAsset? FindBestLiteAsset(List<UpdateAsset> assets)
    {
        var arch = CurrentArchitecture;

        var match = assets.FirstOrDefault(a =>
            a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
            a.Name.Contains("Lite", StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        if (match is not null) return match;

        match = assets.FirstOrDefault(a =>
            a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
            a.Name.Contains("Lite", StringComparison.OrdinalIgnoreCase));

        return match;
    }

    public static UpdateAsset? FindBestInstallerAsset(List<UpdateAsset> assets)
    {
        var arch = CurrentArchitecture;

        var match = assets.FirstOrDefault(a =>
            a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        return match;
    }
}
