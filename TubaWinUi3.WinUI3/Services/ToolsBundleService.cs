using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public sealed record ToolsBundleUpdateInfo(
    bool HasUpdate,
    string Version,
    string? GitCodeUrl = null,
    string? GitHubUrl = null,
    long Size = 0,
    string? GitCodeLiteUrl = null,
    string? GitHubLiteUrl = null,
    long LiteSize = 0)
{
    /// <summary>发行版是否附带精简包（Tools_Lite.zip）。双源任一可用即视为可用。</summary>
    public bool HasLiteAsset =>
        !string.IsNullOrEmpty(GitCodeLiteUrl) || !string.IsNullOrEmpty(GitHubLiteUrl);

    /// <summary>按变种取主源（GitCode 优先）下载地址。</summary>
    public string? PrimaryUrl(bool lite) => lite
        ? (GitCodeLiteUrl ?? GitHubLiteUrl)
        : (GitCodeUrl ?? GitHubUrl);

    /// <summary>按变种取备源（GitHub）下载地址，与主源相同时返回 null（无需兜底）。</summary>
    public string? FallbackUrl(bool lite)
    {
        var fallback = lite ? GitHubLiteUrl : GitHubUrl;
        var primary = PrimaryUrl(lite);
        return string.IsNullOrEmpty(fallback) ||
               string.Equals(fallback, primary, StringComparison.OrdinalIgnoreCase)
            ? null
            : fallback;
    }

    public long AssetSize(bool lite) => lite ? LiteSize : Size;
}

public static class ToolsBundleService
{
    public const string KindFull = "Full";
    public const string KindLite = "Lite";

    private const string Owner = "luolangaga";
    private const string Repo = "tubatool";
    private const string GitHubReleasesApi = $"https://api.github.com/repos/{Owner}/{Repo}/releases";
    private const string GitCodeOwner = "luolangaga";
    private const string GitCodeRepo = "tubatool";
    private const string GitCodeReleaseApiBase = $"https://api.gitcode.com/api/v5/repos/{GitCodeOwner}/{GitCodeRepo}/releases";
    private const string ToolsAssetName = "Tools.zip";
    private const string ToolsLiteAssetName = "Tools_Lite.zip";
    private const int ReleasesPerPage = 100;
    private const int MaxReleasePages = 5;

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static string ToolsBundleDir => Path.Combine(
        RuntimeHelper.GetLocalAppDataRoot(),
        "TubaWinUi3", "Tools");

    static ToolsBundleService()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolsBundle");
    }

    public static bool IsToolsBundleReady()
    {
        try
        {
            if (!Directory.Exists(ToolsBundleDir)) return false;
            return Directory.EnumerateFileSystemEntries(ToolsBundleDir).Any();
        }
        catch { return false; }
    }

    public static string GetToolsBundleDir() => ToolsBundleDir;

    /// <summary>
    /// 内核包解压目标目录：MSIX 恒为包外 LocalAppData 的内核目录；
    /// 精简版便携（Lite）已随包内置 Tools 时就地升级（替换应用目录下的 Tools），
    /// 否则（旧精简版无内置工具）回退 LocalAppData 内核目录。
    /// </summary>
    public static string GetInstallTargetDir()
    {
        if (RuntimeHelper.IsLiteBuild)
        {
            var appTools = Path.Combine(ToolCatalog.AppDirectory, "Tools");
            if (Directory.Exists(appTools))
                return appTools;
        }
        return ToolsBundleDir;
    }

    public static string? GetCurrentVersion()
    {
        return AppSettings.Get("ToolsBundleVersion");
    }

    /// <summary>
    /// 已安装内核包的变种（完整版/精简版）。
    /// 历史数据兼容：仅记录过版本号、未记录变种的旧安装一律按完整版处理，
    /// 从而保证「完整版不可降级到精简版」对既有用户同样生效。
    /// </summary>
    public static string? GetInstalledKind()
    {
        var kind = AppSettings.Get("ToolsBundleKind");
        if (kind is KindFull or KindLite) return kind;
        return GetCurrentVersion() is not null ? KindFull : null;
    }

    public static void SetInstalledKind(string kind)
    {
        AppSettings.Set("ToolsBundleKind", kind);
    }

    public static Version? CurrentAppVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v is not null ? new Version(v.Major, v.Minor, v.Build) : new Version(1, 0, 0);
        }
    }

    public static async Task<ToolsBundleUpdateInfo?> CheckForToolsUpdateAsync(CancellationToken ct = default)
    {
        var currentVersion = GetCurrentVersion();

        string? gitCodeUrl = null, gitCodeLiteUrl = null;
        string? githubUrl = null, githubLiteUrl = null;
        long size = 0, liteSize = 0;
        string? versionStr = null;

        var gitCodeTask = FetchGitCodeLatestAsync(ct);
        var githubTask = FetchGitHubLatestAsync(ct);

        try
        {
            var gc = await gitCodeTask;
            if (gc is not null)
            {
                gitCodeUrl = gc.FullUrl;
                gitCodeLiteUrl = gc.LiteUrl;
                size = gc.FullSize;
                liteSize = gc.LiteSize;
                versionStr ??= gc.Version;
            }
        }
        catch { }

        try
        {
            var gh = await githubTask;
            if (gh is not null)
            {
                githubUrl = gh.FullUrl;
                githubLiteUrl = gh.LiteUrl;
                size = size > 0 ? size : gh.FullSize;
                liteSize = liteSize > 0 ? liteSize : gh.LiteSize;
                versionStr ??= gh.Version;
            }
        }
        catch { }

        if (versionStr is null) return null;

        if (currentVersion is not null && versionStr == currentVersion)
            return new ToolsBundleUpdateInfo(false, versionStr, gitCodeUrl, githubUrl, size,
                gitCodeLiteUrl, githubLiteUrl, liteSize);

        return new ToolsBundleUpdateInfo(true, versionStr, gitCodeUrl, githubUrl, size,
            gitCodeLiteUrl, githubLiteUrl, liteSize);
    }

    public static string? PickBestUrl(ToolsBundleUpdateInfo info)
    {
        if (!string.IsNullOrEmpty(info.GitCodeUrl)) return info.GitCodeUrl;
        if (!string.IsNullOrEmpty(info.GitHubUrl)) return info.GitHubUrl;
        return null;
    }

    public static Func<CancellationToken, Task<ResolvedDownloadUrl>> CreateUrlResolver(
        ToolsBundleUpdateInfo info, bool preferGitCode = true, bool lite = false)
    {
        return async ct =>
        {
            var primary = info.PrimaryUrl(lite);
            var url = preferGitCode
                ? primary
                : (info.FallbackUrl(lite) ?? primary);

            if (string.IsNullOrEmpty(url))
                throw new InvalidOperationException(
                    lite ? "没有可用的精简版内核下载链接" : "没有可用的下载链接");

            var fileName = lite ? ToolsLiteAssetName : ToolsAssetName;
            return new ResolvedDownloadUrl(url, fileName, info.AssetSize(lite));
        };
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

    /// <summary>单个发行版解析出的内核包资产（完整版必查，精简版可选）。</summary>
    internal sealed record ToolsBundleReleaseAssets(
        string Version, string? FullUrl, long FullSize, string? LiteUrl, long LiteSize);

    private static async Task<ToolsBundleReleaseAssets?> FetchGitCodeLatestAsync(CancellationToken ct)
    {
        try
        {
            return await WalkReleasesForToolsAsync(GitCodeReleaseApiBase, ct);
        }
        catch { return null; }
    }

    private static async Task<ToolsBundleReleaseAssets?> FetchGitHubLatestAsync(CancellationToken ct)
    {
        try
        {
            return await WalkReleasesForToolsAsync(GitHubReleasesApi, ct);
        }
        catch { return null; }
    }

    /// <summary>
    /// 从最新发行版开始逐版本向下扫描（分页），返回第一个带 Tools.zip 的发行版。
    /// 某个发行版没附带工具包更新时（例如纯应用更新），自动回退到更早的版本。
    /// Tools_Lite.zip 只在与 Tools.zip 同一发行版上识别（精简版始终与完整版同版本发布）。
    /// </summary>
    private static async Task<ToolsBundleReleaseAssets?> WalkReleasesForToolsAsync(
        string releasesApi, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolsBundle");

        for (var page = 1; page <= MaxReleasePages; page++)
        {
            var url = $"{releasesApi}?page={page}&per_page={ReleasesPerPage}";
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var match = ScanReleasesForTools(root);
                if (match is not null) return match;

                // 本页不满一页说明已到最后一页，仍未找到 Tools.zip
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < ReleasesPerPage) return null;
            }
            catch { return null; }
        }

        return null;
    }

    /// <summary>
    /// 按 JSON 数组顺序（发行版列表均为最新在前）扫描，返回第一个带 Tools.zip 的发行版。
    /// </summary>
    internal static ToolsBundleReleaseAssets? ScanReleasesForTools(JsonElement releases)
    {
        if (releases.ValueKind != JsonValueKind.Array) return null;

        foreach (var release in releases.EnumerateArray())
        {
            var match = ParseToolsAssets(release);
            if (match is not null) return match;
        }

        return null;
    }

    private static ToolsBundleReleaseAssets? ParseToolsAssets(JsonElement release)
    {
        var tagName = release.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
        if (tagName.Length == 0) return null;

        // 与 /releases/latest 语义一致：跳过草稿和预发布
        if (release.TryGetProperty("draft", out var draftEl) && draftEl.GetBoolean()) return null;
        if (release.TryGetProperty("prerelease", out var preEl) && preEl.GetBoolean()) return null;

        if (!release.TryGetProperty("assets", out var assetsEl)) return null;

        string? fullUrl = null, liteUrl = null;
        long fullSize = 0, liteSize = 0;

        foreach (var asset in assetsEl.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var isFull = name.Equals(ToolsAssetName, StringComparison.OrdinalIgnoreCase);
            var isLite = !isFull && name.Equals(ToolsLiteAssetName, StringComparison.OrdinalIgnoreCase);
            if (!isFull && !isLite) continue;

            var downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (string.IsNullOrEmpty(downloadUrl)) continue;

            var assetSize = asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;
            if (isFull)
            {
                fullUrl = downloadUrl;
                fullSize = assetSize;
            }
            else
            {
                liteUrl = downloadUrl;
                liteSize = assetSize;
            }
        }

        // 完整包是版本锚点：首个带 Tools.zip 的发行版生效，精简包缺失时仅为不可选
        if (fullUrl is null) return null;

        var versionStr = tagName.TrimStart('v', 'V');
        return new ToolsBundleReleaseAssets(versionStr, fullUrl, fullSize, liteUrl, liteSize);
    }
}
