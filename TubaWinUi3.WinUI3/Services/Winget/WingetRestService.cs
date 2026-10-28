using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace TubaWinUi3.Services;

/// <summary>
/// 「正版软件商店」的在线数据源：直接读写 WinGet 官方源（不依赖本机安装 winget）。
/// 搜索走源索引（source2.msix 内的 SQLite）；安装包走 包级 versionData → 合并清单，
/// 拿到官方 InstallerUrl / InstallerSha256 后交给下载队列下载并校验，最后启动安装程序。
/// </summary>
public static class WingetRestService
{
    /// <summary>WinGet 包 ID 只允许这些字符（与 BuildInstallStartInfo 的校验保持一致）。</summary>
    private static readonly Regex PackageIdPattern = new(@"^[A-Za-z0-9][A-Za-z0-9._+\-]*$",
        RegexOptions.Compiled);

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>源索引是否已缓存（离线也能搜索内置目录之外的已缓存结果）。</summary>
    public static bool HasCachedIndex => WingetIndexStore.CurrentIndexPath is not null;

    /// <summary>预热源索引（首次打开商店/首次搜索时调用），失败抛异常。</summary>
    public static Task WarmUpAsync(IProgress<string>? status = null, CancellationToken ct = default) =>
        WingetIndexStore.EnsureAsync(status, ct);

    /// <summary>在线搜索（等价 winget search --source winget）。</summary>
    public static async Task<WingetSearchResultList> SearchAsync(string query, IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return new WingetSearchResultList { Results = [] };

        try
        {
            await WingetIndexStore.EnsureAsync(status, ct).ConfigureAwait(false);
            var results = await Task.Run(() => WingetIndexStore.Search(query.Trim(), 40), ct)
                .ConfigureAwait(false);
            return new WingetSearchResultList { Results = results };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WingetSearchResultList { Results = [], Error = Describe(ex) };
        }
    }

    /// <summary>
    /// 解析包的最新版安装程序：索引 → versionData.mszyml（MSZIP）→ 合并清单 → 选择合适架构/类型。
    /// 每份下载内容都按源里声明的 SHA-256 校验。
    /// </summary>
    public static async Task<WingetInstallPlan> ResolveInstallerAsync(string packageId,
        IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(packageId) || !PackageIdPattern.IsMatch(packageId))
            throw new ArgumentException("无效的 WinGet 包 ID。", nameof(packageId));

        await WingetIndexStore.EnsureAsync(status, ct).ConfigureAwait(false);

        var entry = WingetIndexStore.FindPackage(packageId)
            ?? throw new InvalidOperationException($"WinGet 官方源未收录 {packageId}。");

        status?.Report($"正在获取 {packageId} 的版本信息…");
        var versionDataPath = $"packages/{packageId}/{entry.HashHex[..8].ToLowerInvariant()}/versionData.mszyml";
        var versionDataBytes = await WingetSourceClient
            .GetVerifiedAsync(versionDataPath, entry.HashHex, ct).ConfigureAwait(false);

        var versionData = ParseYaml<WingetVersionDataManifest>(MszipCompression.Decompress(versionDataBytes),
            "版本数据");
        var versions = versionData.Versions
            .Where(v => !string.IsNullOrEmpty(v.RelativePath) && !string.IsNullOrEmpty(v.Sha256))
            .ToList();
        if (versions.Count == 0)
            throw new InvalidOperationException($"WinGet 源的版本数据中没有 {packageId} 的可用清单。");

        var versionEntry = versions.FirstOrDefault(v =>
                                string.Equals(v.Version, entry.LatestVersion, StringComparison.OrdinalIgnoreCase))
                            ?? versions[0];

        status?.Report("正在获取官方安装包信息…");
        var manifestBytes = await WingetSourceClient
            .GetVerifiedAsync(versionEntry.RelativePath!, versionEntry.Sha256!, ct).ConfigureAwait(false);
        var manifest = ParseYaml<WingetMergedManifest>(manifestBytes, "安装清单");

        var installer = WingetInstallerSelector.Select(manifest.Installers, RuntimeInformation.ProcessArchitecture)
            ?? throw new InvalidOperationException(
                $"{manifest.PackageName ?? packageId} 官方源只提供 zip/Portable/MSIX 等格式，没有可直接下载运行的安装程序。");

        var version = versionEntry.Version ?? manifest.PackageVersion ?? entry.LatestVersion;
        var installerType = string.IsNullOrEmpty(installer.InstallerType)
            ? manifest.InstallerType ?? "exe"
            : installer.InstallerType;

        return new WingetInstallPlan(
            packageId,
            version,
            installer.InstallerUrl!,
            installer.InstallerSha256 ?? "",
            WingetInstallerSelector.BuildFileName(installer.InstallerUrl!, packageId, installerType),
            installerType,
            string.IsNullOrEmpty(installer.Architecture) ? "neutral" : installer.Architecture);
    }

    /// <summary>删除本地索引缓存（存储清理页使用），下次搜索时重新下载。</summary>
    public static void ClearCache()
    {
        try
        {
            if (Directory.Exists(WingetIndexStore.CacheDir))
                Directory.Delete(WingetIndexStore.CacheDir, recursive: true);
        }
        catch (Exception ex)
        {
            LogService.Warn(LogService.App, $"清理 WinGet 源索引缓存失败：{ex.Message}");
        }
    }

    private static T ParseYaml<T>(byte[] bytes, string what) where T : class
    {
        try
        {
            return Yaml.Deserialize<T>(Encoding.UTF8.GetString(bytes))
                   ?? throw new InvalidDataException($"WinGet 源{what}为空。");
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new InvalidDataException($"WinGet 源{what}解析失败：{ex.Message}", ex);
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        InvalidDataException => ex.Message,
        InvalidOperationException => ex.Message,
        System.Net.Http.HttpRequestException => $"连接 WinGet 官方源失败（cdn.winget.microsoft.com）：{ex.Message}",
        TaskCanceledException => "连接 WinGet 官方源超时，请检查网络或代理设置。",
        _ => $"读取 WinGet 官方源失败：{ex.Message}"
    };
}
