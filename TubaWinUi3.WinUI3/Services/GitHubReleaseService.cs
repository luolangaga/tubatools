using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TubaWinUi3.Services;

public sealed record GitHubAssetInfo(string Name, string OriginalUrl, long Size);

public sealed record GitHubReleaseInfo(
    string TagName,
    string? Name,
    string? Body,
    IReadOnlyList<GitHubAssetInfo> Assets);

public sealed record ProxySpeedResult(string Name, string? Url, double Ms, bool Ok);

public static class GitHubReleaseService
{
    private static readonly HttpClient _apiClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static GitHubReleaseService()
    {
        _apiClient.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-GitHubRelease");
    }

    public static readonly string[] GitHubProxies =
    [
        "https://gh-proxy.com/",
        "https://github-proxy.lixxing.top/",
        "https://y.whereisdoge.work/",
        "https://github-proxy.memory-echoes.cn/",
        "https://j.1lin.dpdns.org/",
        "https://github.cnxiaobai.com/",
        "https://gh.aaa.team/",
        "https://gh.jasonzeng.dev/",
        "https://gitproxy.mrhjx.cn/",
        "https://github.chenc.dev/",
        "https://ghproxy.053000.xyz/",
        "https://gh.monlor.com/",
        "https://proxy.yaoyaoling.net/",
        "https://gp.zkitefly.eu.org/",
        "https://gp.871201.xyz/",
        "https://fastgit.cc/",
        "https://gh.1k.ink/",
        "https://github.geekery.cn/",
        "https://hub.ddayh.com/",
        "https://jiashu.1win.eu.org/",
        "https://tvv.tw/",
        "https://ghp.keleyaa.com/",
        "https://github.788787.xyz/",
        "https://gh.con.sh/",
        "https://gh.bugdey.us.kg/",
        "https://ghproxy.xzhouqd.com/",
        "https://github.mlmle.cn/",
        "https://proxy.baguoyuyan.com/",
        "https://github.ihnic.com/",
        "https://gh.idayer.com/",
        "https://github.crdz.eu.org/",
        "https://ggg.clwap.dpdns.org/",
        "https://getgit.love8yun.eu.org/",
        "https://gh.996986.xyz/",
        "https://github.boringhex.top/",
        "https://gh.198962.xyz/",
        "https://gh.chjina.com/",
        "https://github.kkproxy.dpdns.org/",
        "https://ghproxy.mf-dust.dpdns.org/"
    ];

    public static string ProxyUrl(string proxyBase, string originalUrl)
    {
        var base_ = proxyBase.TrimEnd('/');
        return $"{base_}/{originalUrl}";
    }

    public static async Task<GitHubReleaseInfo?> FetchLatestReleaseAsync(
        string repo, CancellationToken ct = default)
    {
        return await FetchReleaseByTagAsync(repo, null, ct);
    }

    public static async Task<GitHubReleaseInfo?> FetchReleaseByTagAsync(
        string repo, string? tag, CancellationToken ct = default)
    {
        var apiUrl = tag is not null
            ? $"https://api.github.com/repos/{repo}/releases/tags/{tag}"
            : $"https://api.github.com/repos/{repo}/releases/latest";

        try
        {
            var json = await _apiClient.GetStringAsync(apiUrl, ct);
            return ParseReleaseJson(json);
        }
        catch
        {
            return null;
        }
    }

    public static GitHubReleaseInfo? ParseReleaseJson(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagEl)
                ? tagEl.GetString() ?? "" : "";
            var name = root.TryGetProperty("name", out var nameEl)
                ? nameEl.GetString() : null;
            var body = root.TryGetProperty("body", out var bodyEl)
                ? bodyEl.GetString() : null;

            if (!root.TryGetProperty("assets", out var assetsEl))
                return new GitHubReleaseInfo(tagName, name, body, []);

            var assets = new List<GitHubAssetInfo>();
            foreach (var asset in assetsEl.EnumerateArray())
            {
                var aName = asset.GetProperty("name").GetString() ?? "";
                var aUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                var aSize = asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0L;
                assets.Add(new GitHubAssetInfo(aName, aUrl, aSize));
            }

            return new GitHubReleaseInfo(tagName, name, body, assets);
        }
        catch
        {
            return null;
        }
    }

    public static GitHubAssetInfo? FindBestAsset(
        IReadOnlyList<GitHubAssetInfo> assets, string arch, AssetMatchStrategy strategy)
    {
        return strategy switch
        {
            AssetMatchStrategy.UniGetUI => FindUniGetUIAsset(assets, arch),
            AssetMatchStrategy.OptimizerDuck => FindOptimizerDuckAsset(assets, arch),
            AssetMatchStrategy.ContextMenuMgr => FindContextMenuMgrAsset(assets, arch),
            _ => FindGenericAsset(assets, arch)
        };
    }

    private static GitHubAssetInfo? FindUniGetUIAsset(
        IReadOnlyList<GitHubAssetInfo> assets, string arch)
    {
        foreach (var a in assets)
        {
            if (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("Installer", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains($".{arch}.", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        foreach (var a in assets)
        {
            if (a.Name.Equals("UniGetUI.Installer.exe", StringComparison.OrdinalIgnoreCase) &&
                arch == "x64")
                return a;
        }

        foreach (var a in assets)
        {
            if (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("Installer", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        return null;
    }

    private static GitHubAssetInfo? FindOptimizerDuckAsset(
        IReadOnlyList<GitHubAssetInfo> assets, string arch)
    {
        foreach (var a in assets)
        {
            if (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains($"-{arch}-", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        foreach (var a in assets)
        {
            if (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("Windows", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        return null;
    }

    private static GitHubAssetInfo? FindContextMenuMgrAsset(
        IReadOnlyList<GitHubAssetInfo> assets, string arch)
    {
        foreach (var a in assets)
        {
            if (a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("self-contained", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        foreach (var a in assets)
        {
            if (a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("self-contained", StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        return null;
    }

    private static GitHubAssetInfo? FindGenericAsset(
        IReadOnlyList<GitHubAssetInfo> assets, string arch)
    {
        foreach (var a in assets)
        {
            if (a.Name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        foreach (var a in assets)
        {
            if (a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        foreach (var a in assets)
        {
            if (a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return a;
        }

        return null;
    }

    public static async Task<List<ProxySpeedResult>> TestProxiesAsync(
        string originalUrl, int maxConcurrent = 8, CancellationToken ct = default)
    {
        var results = new List<ProxySpeedResult>();
        var allUrls = new List<(string Name, string Url)>();

        allUrls.Add(("GitHub 直连", originalUrl));

        foreach (var proxy in GitHubProxies)
        {
            var name = new Uri(proxy).Host;
            allUrls.Add((name, ProxyUrl(proxy, originalUrl)));
        }

        var semaphore = new SemaphoreSlim(maxConcurrent);
        var tasks = allUrls.Select(async pair =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                return await TestSingleProxyAsync(pair.Name, pair.Url);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var taskResults = await Task.WhenAll(tasks);
        results.AddRange(taskResults);

        return results;
    }

    internal static async Task<ProxySpeedResult> TestSingleProxyAsync(string name, string url)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-GitHubRelease");
            var sw = Stopwatch.StartNew();
            using var response = await client.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, url),
                HttpCompletionOption.ResponseHeadersRead);
            sw.Stop();
            return new ProxySpeedResult(name, url, sw.Elapsed.TotalMilliseconds,
                response.IsSuccessStatusCode);
        }
        catch
        {
            return new ProxySpeedResult(name, url, double.MaxValue, false);
        }
    }

    public static string GetBestUrl(List<ProxySpeedResult> results, string originalUrl)
    {
        var best = results
            .Where(r => r.Ok)
            .OrderBy(r => r.Ms)
            .FirstOrDefault();

        return best?.Url ?? originalUrl;
    }

    public static string GetCurrentArch()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64"
        };
    }
}

public enum AssetMatchStrategy
{
    Generic,
    UniGetUI,
    OptimizerDuck,
    ContextMenuMgr
}
