using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>
/// 正版软件商店服务：本地 catalog + winget CLI 搜索 + 安装包直链解析 + GitHub 镜像回退
/// </summary>
public static class WingetStoreService
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static PcSetupCatalog? _cachedCatalog;

    // ---- GitHub 直链镜像回退 ----

    private static readonly HttpClient _probeClient = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>原始直链 → 探测后选中的可达链接（会话内缓存）</summary>
    private static readonly Dictionary<string, string> _resolvedUrlCache = [];

    /// <summary>
    /// 为 GitHub Release 直链生成镜像候选：原链 + GitCode 同名仓库 + 仓库名差尾缀 s 的变体。
    /// 非 GitHub 链接只保留原链（不做探测，避免无谓等待）。
    /// </summary>
    public static List<string> BuildMirrorCandidates(string url)
    {
        var candidates = new List<string> { url };

        const string githubHost = "github.com/";
        var hostIdx = url.IndexOf(githubHost, StringComparison.OrdinalIgnoreCase);
        if (hostIdx < 0) return candidates;

        var rest = url[(hostIdx + githubHost.Length)..];
        var ownerEnd = rest.IndexOf('/');
        if (ownerEnd <= 0) return candidates;

        var owner = rest[..ownerEnd];
        var repoPath = rest[ownerEnd..].TrimStart('/');
        var repoEnd = repoPath.IndexOf('/');
        if (repoEnd <= 0) return candidates;

        var repo = repoPath[..repoEnd];
        var tail = repoPath[repoEnd..];

        var gitcodeBase = $"https://gitcode.com/{owner}/";
        candidates.Add(gitcodeBase + repo + tail);
        // GitHub 仓库名带尾缀 s 时 GitCode 镜像常不带（反之亦然），两个变体都试
        candidates.Add(gitcodeBase + (repo.EndsWith('s') ? repo[..^1] : repo + "s") + tail);

        return candidates;
    }

    /// <summary>
    /// 探测直链可达性：GitHub 原链优先，连不上（国内常见黑洞）则回退到 GitCode 镜像。
    /// 结果按 URL 缓存，同会话内重复安装不再探测。
    /// </summary>
    public static async Task<string> ResolveReachableUrlAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url)) return url;

        var candidates = BuildMirrorCandidates(url);
        if (candidates.Count == 1) return url;

        lock (_resolvedUrlCache)
        {
            if (_resolvedUrlCache.TryGetValue(url, out var cached))
                return cached;
        }

        foreach (var candidate in candidates)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, candidate);
                req.Headers.UserAgent.ParseAdd("TubaWinUi3/1.0");
                using var resp = await _probeClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!resp.IsSuccessStatusCode) continue;

                lock (_resolvedUrlCache)
                    _resolvedUrlCache[url] = candidate;
                return candidate;
            }
            catch
            {
                // 当前候选不可达，尝试下一个
            }
        }

        // 全部失败：保留原链，由下载队列给出明确错误
        return url;
    }

    /// <summary>
    /// 加载本地 pcsetup_catalog.json
    /// </summary>
    public static async Task<List<StoreCategory>> LoadCatalogAsync()
    {
        if (_cachedCatalog is not null)
            return _cachedCatalog.Categories;

        try
        {
            var path = FindCatalogFile();
            if (path is null) return [];

            await using var fs = File.OpenRead(path);
            _cachedCatalog = await JsonSerializer.DeserializeAsync<PcSetupCatalog>(fs, _jsonOpts);

            if (_cachedCatalog is not null)
            {
                foreach (var cat in _cachedCatalog.Categories)
                {
                    foreach (var pkg in cat.Packages)
                    {
                        pkg.Category = cat.Name;
                        pkg.Glyph = cat.Glyph;
                    }
                    if (cat.SubCategories is not null)
                    {
                        foreach (var sub in cat.SubCategories)
                        {
                            foreach (var pkg in sub.Packages)
                            {
                                pkg.Category = cat.Name;
                                pkg.Glyph = cat.Glyph;
                            }
                        }
                    }
                }
            }

            return _cachedCatalog?.Categories ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// 从本地 catalog 中按名称/描述模糊搜索
    /// </summary>
    public static List<StorePackage> SearchLocal(string query, List<StoreCategory> catalog)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var q = query.Trim();
        var results = new List<StorePackage>();

        foreach (var cat in catalog)
        {
            foreach (var pkg in cat.Packages)
            {
                if (Matches(pkg, q))
                    results.Add(pkg);
            }
            if (cat.SubCategories is not null)
            {
                foreach (var sub in cat.SubCategories)
                {
                    foreach (var pkg in sub.Packages)
                    {
                        if (Matches(pkg, q))
                            results.Add(pkg);
                    }
                }
            }
        }

        return results
            .OrderByDescending(p => p.IsRecommended)
            .ThenBy(p => p.Name)
            .ToList();
    }

    /// <summary>
    /// 在线搜索：优先读取 WinGet 官方源索引（cdn.winget.microsoft.com，不依赖本机 winget）；
    /// 官方源不可达时回退到本机 winget CLI，保证老网络环境下仍能搜索。
    /// </summary>
    public static async Task<WingetSearchResultList> SearchOnlineAsync(string query,
        IProgress<string>? status = null, CancellationToken ct = default)
    {
        var empty = new WingetSearchResultList { Results = [] };
        if (string.IsNullOrWhiteSpace(query)) return empty;

        var rest = await WingetRestService.SearchAsync(query, status, ct).ConfigureAwait(false);
        if (rest.Error is null) return rest;

        LogService.Warn(LogService.App, $"WinGet 官方源搜索失败，尝试本机 winget：{rest.Error}");
        if (!await IsWingetAvailableAsync().ConfigureAwait(false)) return rest;

        var fallback = await SearchViaCliAsync(query, ct).ConfigureAwait(false);
        return fallback.Error is null ? fallback : rest;
    }

    /// <summary>winget CLI 搜索（官方源不可用时的回退路径）。</summary>
    private static async Task<WingetSearchResultList> SearchViaCliAsync(string query, CancellationToken ct)
    {
        var empty = new WingetSearchResultList { Results = [] };
        if (string.IsNullOrWhiteSpace(query)) return empty;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = $"search \"{query}\" --source winget --accept-source-agreements",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // winget 重定向输出固定为 UTF-8，避免中文系统按 GBK 解码导致名称乱码
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process is null)
                return empty with { Error = "无法启动 winget 进程" };

            // 并发读取 stdout/stderr，避免 stderr 缓冲填满后阻塞子进程
            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var errOutput = await errTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
                return empty with { Error = string.IsNullOrWhiteSpace(errOutput) ? $"winget 搜索失败 (退出码 {process.ExitCode})" : errOutput.Trim() };

            var results = ParseWingetSearchOutput(output);
            return new WingetSearchResultList { Results = results };
        }
        catch (OperationCanceledException)
        {
            return empty with { Error = "搜索已取消" };
        }
        catch (Exception ex)
        {
            return empty with { Error = $"搜索失败：{ex.Message}" };
        }
    }

    /// <summary>
    /// 通过 winget show 获取安装包信息，再从输出解析直链
    /// </summary>
    public static async Task<(string? Url, string? FileName, string? Error)> GetInstallerUrlAsync(
        string packageId, CancellationToken ct = default)
    {
        try
        {
            var url = await GetInstallerUrlViaCliAsync(packageId, ct);
            if (!string.IsNullOrEmpty(url))
            {
                // GitHub 原链在国内常连不上，探测失败时自动回退 GitCode 镜像
                url = await ResolveReachableUrlAsync(url, ct);
                var ext = GuessExtension(url);
                return (url, $"{packageId}.{ext}", null);
            }

            return (null, null, "无法获取下载链接（winget 未返回安装程序 URL）");
        }
        catch (OperationCanceledException)
        {
            return (null, null, "获取下载链接已取消");
        }
        catch (Exception ex)
        {
            return (null, null, $"获取下载链接失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 通过 winget show 命令解析安装包 URL
    /// </summary>
    private static async Task<string?> GetInstallerUrlViaCliAsync(string packageId, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = $"show --id {packageId} --source winget --accept-source-agreements",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // winget 重定向输出固定为 UTF-8，避免中文系统按 GBK 解码导致前缀匹配失败
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            // 并发读 stdout/stderr，stderr 缓冲满不会阻塞 winget
            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            _ = await errTask.ConfigureAwait(false);
            if (process.ExitCode != 0) return null;

            // 实际输出（中文系统）：
            //   安装程序类型： wix
            //   安装程序 URL： https://dl.google.com/.../xxx.msi
            // 全角冒号 + 空格；也可能出现英文 "Installer Url:" 或 "InstallerUrl:"
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.Contains("URL", StringComparison.OrdinalIgnoreCase)) continue;
                if (!trimmed.StartsWith("安装程序", StringComparison.OrdinalIgnoreCase)
                    && !trimmed.StartsWith("Installer URL", StringComparison.OrdinalIgnoreCase)
                    && !trimmed.StartsWith("InstallerUrl", StringComparison.OrdinalIgnoreCase))
                    continue;

                // 找到行内第一个 http(s):// 开头的部分
                var httpIdx = FindHttpIndex(trimmed);
                if (httpIdx >= 0)
                    return trimmed[httpIdx..].Trim();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static readonly SemaphoreSlim InstallGate = new(1, 1);

    public static DownloadItem EnqueueDirectInstaller(StorePackage package)
    {
        if (!package.CanInstall || !package.IsDirectDownload)
            throw new ArgumentException("未配置有效的 HTTPS 安装包地址。", nameof(package));
        var destination = Path.Combine(Path.GetTempPath(), "TubaWinUi3_Store");
        Directory.CreateDirectory(destination);
        return DownloadQueueService.Enqueue(package.Name, package.DownloadUrl!, destination,
            new InstallerLaunchProcessor(), "官方安装包 · 下载完成后启动安装", package.Glyph);
    }

    /// <summary>
    /// 商店安装入口：直链包直接进下载队列；其余从 WinGet 官方源解析安装包地址后入队
    /// （每份源数据按 SHA-256 校验，安装包下载完再校验一次才启动）。
    /// 仅当官方源解析失败且本机确有 winget 时才回退到 winget install（覆盖 portable/MSIX 等直下不支持的格式）。
    /// </summary>
    public static async Task<StoreInstallResult> InstallStorePackageAsync(StorePackage package,
        IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (package.IsDirectDownload)
        {
            EnqueueDirectInstaller(package);
            return new StoreInstallResult(true, "已加入下载中心，下载完成后启动安装程序。");
        }

        string? sourceError = null;
        try
        {
            var plan = await WingetRestService.ResolveInstallerAsync(package.Id, status, ct).ConfigureAwait(false);
            EnqueueResolvedInstaller(package, plan);
            return new StoreInstallResult(true,
                $"已加入下载中心：{plan.Version} · {plan.Architecture} 官方安装包，下载并校验 SHA-256 后启动安装。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sourceError = ex.Message;
            LogService.Warn(LogService.App, $"从 WinGet 官方源解析 {package.Id} 失败：{ex.Message}");
        }

        if (await IsWingetAvailableAsync().ConfigureAwait(false))
        {
            status?.Report("官方源不可用，改为通过本机 WinGet 安装…");
            await InstallPackageAsync(package.Id, status, ct).ConfigureAwait(false);
            return new StoreInstallResult(false, "安装完成（通过本机 WinGet）");
        }

        throw new InvalidOperationException(
            (sourceError ?? "无法解析该应用的官方安装包。") + "\n\n本机也没有可用的 WinGet，请检查网络后重试。");
    }

    /// <summary>把官方源解析出的安装包交给下载队列：下载 → SHA-256 校验 → 启动安装程序。</summary>
    public static DownloadItem EnqueueResolvedInstaller(StorePackage package, WingetInstallPlan plan)
    {
        var destination = Path.Combine(Path.GetTempPath(), "TubaWinUi3_Store");
        Directory.CreateDirectory(destination);

        IDownloadPostProcessor processor = string.IsNullOrWhiteSpace(plan.InstallerSha256)
            ? new InstallerLaunchProcessor()
            : new VerifiedInstallerLaunchProcessor(plan.InstallerSha256);

        return DownloadQueueService.EnqueueWithResolver(
            package.Name,
            _ => Task.FromResult(new ResolvedDownloadUrl(plan.InstallerUrl, plan.FileName)),
            destination,
            processor,
            $"WinGet 官方源 · {plan.Version} · {plan.Architecture} · 下载完成后校验并启动安装",
            package.Glyph);
    }

    /// <summary>以精确包 ID 交给 WinGet 安装，保留依赖、校验和及 portable 包处理。</summary>
    internal static ProcessStartInfo BuildInstallStartInfo(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId)
            || !Regex.IsMatch(packageId, @"^[A-Za-z0-9][A-Za-z0-9._+\-]*$"))
            throw new ArgumentException("无效的 WinGet 包 ID。", nameof(packageId));
        var startInfo = new ProcessStartInfo
        {
            FileName = "winget", UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "install", "--id", packageId, "--exact", "--source", "winget",
                     "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity", "--silent" })
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    public static async Task InstallPackageAsync(string packageId, IProgress<string>? statusProgress,
        CancellationToken ct = default)
    {
        var startInfo = BuildInstallStartInfo(packageId);
        statusProgress?.Report("等待 WinGet 安装…");
        await InstallGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            statusProgress?.Report("WinGet 正在下载并安装…");
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动 WinGet，请安装或更新 Windows 应用安装程序。");
            // 启动后让 WinGet 完成；导航离开不应终止正在运行的安装程序。
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var detail = (output + "\n" + error).Trim();
                if (detail.Length > 3000) detail = detail[^3000..];
                throw new InvalidOperationException($"WinGet 安装失败（0x{unchecked((uint)process.ExitCode):X8}）。\n{detail}");
            }
            statusProgress?.Report("安装完成");
        }
        finally { InstallGate.Release(); }
    }

    /// <summary>
    /// 检查 winget 是否可用
    /// </summary>
    public static async Task<bool> IsWingetAvailableAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    #region Parsing

    /// <summary>
    /// 解析 winget search 的 stdout 输出为结构化结果。
    /// 实际输出（中文系统）：
    /// <code>
    /// 名称                                                  ID                                 版本           匹配
    /// -----------------------------------------------------------------------------------------------------------------------------
    /// Google Chrome                                         Google.Chrome                      152.0.7977.65  Moniker: chrome
    /// </code>
    /// 表头可能是中文（名称/ID/版本）或英文 (Name/Id/Version)，且带 \r 结尾。
    /// 注意：表头含中文全角字符（显示占 2 列宽、实际 1 个 char），不能用 char 位置切列，
    /// 需按 2+ 连续空白切分（winget 列间至少 2 空格、词内 1 空格）。
    /// </summary>
    internal static List<WingetSearchResult> ParseWingetSearchOutput(string output)
    {
        var results = new List<WingetSearchResult>();
        var lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // 找表头行：同时包含名称(Name/名称)和 ID(Id/ID) 且不含网址
        var headerIdx = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Contains("Id", StringComparison.OrdinalIgnoreCase)
                && (line.Contains("Name", StringComparison.OrdinalIgnoreCase) || line.Contains("名称", StringComparison.OrdinalIgnoreCase))
                && !line.Contains("http", StringComparison.OrdinalIgnoreCase))
            {
                headerIdx = i;
                break;
            }
        }

        if (headerIdx < 0 || headerIdx + 1 >= lines.Length) return results;

        // 表头列名：按 2+ 连续空白切分
        var headerTokens = SplitByWideGap(lines[headerIdx]);
        var nameIdx = headerTokens.FindIndex(c => c.Contains("Name", StringComparison.OrdinalIgnoreCase) || c.Contains("名称"));
        var idIdx = headerTokens.FindIndex(c => c.Trim().Equals("Id", StringComparison.OrdinalIgnoreCase) || c.Trim().Equals("ID"));
        var verIdx = headerTokens.FindIndex(c => c.Contains("Version", StringComparison.OrdinalIgnoreCase) || c.Contains("版本"));

        if (idIdx < 0) return results;
        nameIdx = nameIdx < 0 ? 0 : nameIdx;

        // 数据行
        for (var i = headerIdx + 2; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.All(c => c is '-' or '─' or ' ')) continue;
            // 尾部提示行（如 "1 个包匹配搜索。" / "Failed..."）
            if (line.Contains("个包", StringComparison.OrdinalIgnoreCase)
                || line.Contains("packages found", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Failed", StringComparison.OrdinalIgnoreCase))
                continue;

            var tokens = SplitByWideGap(line);

            // 列内容超宽（长中文名等）时 winget 退化为单空格分隔，宽空格切分会把
            // 整行并成一个 token；此时按 ID 正则从行内回退解析（名称取 ID 之前的原文）。
            string? id = tokens.Count > idIdx ? tokens[idIdx].Trim() : null;
            if (id is null || id.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(line, PackageIdPattern);
                if (!match.Success) continue;

                var name = line[..match.Index].Trim();
                var versionMatch = Regex.Matches(line, VersionPattern);
                var version = versionMatch.Count > 0 ? versionMatch[^1].Value : null;
                results.Add(new WingetSearchResult
                {
                    PackageIdentifier = match.Value,
                    PackageName = string.IsNullOrEmpty(name) ? match.Value : name,
                    LatestVersion = version
                });
                continue;
            }

            results.Add(new WingetSearchResult
            {
                PackageIdentifier = id,
                PackageName = nameIdx < tokens.Count ? tokens[nameIdx].Trim() : id,
                LatestVersion = verIdx >= 0 && verIdx < tokens.Count ? tokens[verIdx].Trim() : null
            });
        }

        return results;
    }

    /// <summary>
    /// winget 包 ID：Publisher.Package（点号后必须以字母开头，避免误取版本号 "9.2" 之类）。
    /// </summary>
    private const string PackageIdPattern = @"[A-Za-z0-9][A-Za-z0-9._-]*\.[A-Za-z][A-Za-z0-9._-]*";

    /// <summary>
    /// 行内版本号形如 1.5.3 / 20.00.38。
    /// </summary>
    private const string VersionPattern = @"\d+(?:\.\d+)+";

    /// <summary>
    /// 按 2+ 连续空白切分；单空格视为词内分隔（如 "Google Chrome"、"Moniker: chrome"）。
    /// </summary>
    private static List<string> SplitByWideGap(string line)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < line.Length)
        {
            var wsStart = i;
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            if (i == line.Length) break;
            var wordStart = i;
            while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
            var word = line[wordStart..i];
            var hasWideGap = wordStart - wsStart >= 2;
            if (tokens.Count > 0 && !hasWideGap)
                tokens[^1] += " " + word;
            else
                tokens.Add(word);
        }
        return tokens;
    }

    private static int FindHttpIndex(string s)
    {
        var idx = s.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return idx;
        return s.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Matches(StorePackage pkg, string query)
    {
        return pkg.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || pkg.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (pkg.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static string? FindCatalogFile()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 6; i++)
        {
            var candidate = Path.Combine(dir, "Metadata", "pcsetup_catalog.json");
            if (File.Exists(candidate)) return candidate;

            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir) break;
            dir = parent;
        }
        return null;
    }

    private static string GuessExtension(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath.ToLowerInvariant();
            if (path.EndsWith(".msi")) return "msi";
            if (path.EndsWith(".zip")) return "zip";
            if (path.EndsWith(".appx")) return "appx";
            if (path.EndsWith(".msix")) return "msix";
        }
        catch { }
        return "exe";
    }

    #endregion
}

/// <summary>
/// winget 搜索结果集合（含错误信息，便于区分"无结果"与"搜索失败"）
/// </summary>
public sealed record WingetSearchResultList
{
    public List<WingetSearchResult> Results { get; init; } = [];
    public string? Error { get; init; }
}

/// <summary>商店安装结果：Queued = 已交给下载队列（下载后启动安装），否则为已同步完成。</summary>
public sealed record StoreInstallResult(bool Queued, string Message);
