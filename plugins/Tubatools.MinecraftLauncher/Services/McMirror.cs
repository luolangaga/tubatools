namespace Tubatools.MinecraftLauncher.Services;

/// <summary>官方 / BMCLAPI 下载源。BMCLAPI 的 `/v1/packages/...`、`/maven/...`、`/assets/...` 均按官方路径镜像。</summary>
internal static class McMirror
{
    public const string Auto = "auto";
    public const string Official = "official";
    public const string Bmclapi = "bmclapi";

    public const string OfficialManifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    public const string BmclapiManifest = "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json";
    public const string BmclapiHost = "https://bmclapi2.bangbang93.com";

    public static string Normalize(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        Official => Official,
        Bmclapi => Bmclapi,
        _ => Auto
    };

    public static string DisplayName(string mode) => Normalize(mode) switch
    {
        Official => "官方源",
        Bmclapi => "BMCLAPI 镜像",
        _ => "自动（镜像优先）"
    };

    /// <summary>按优先级返回清单地址（自动模式优先 BMCLAPI）。</summary>
    public static IReadOnlyList<string> ManifestUrls(string mode) => Normalize(mode) switch
    {
        Official => [OfficialManifest],
        Bmclapi => [BmclapiManifest, OfficialManifest],
        _ => [BmclapiManifest, OfficialManifest]
    };

    /// <summary>把官方 URL 改写为镜像 URL；官方模式或无法改写时返回 null。</summary>
    public static string? Rewrite(string? url, string mode)
    {
        if (string.IsNullOrWhiteSpace(url) || Normalize(mode) == Official) return null;

        if (url.StartsWith("https://libraries.minecraft.net/", StringComparison.OrdinalIgnoreCase))
            return BmclapiHost + "/maven/" + url["https://libraries.minecraft.net/".Length..];
        if (url.StartsWith("https://resources.download.minecraft.net/", StringComparison.OrdinalIgnoreCase))
            return BmclapiHost + "/assets/" + url["https://resources.download.minecraft.net/".Length..];
        if (url.StartsWith("https://piston-meta.mojang.com/", StringComparison.OrdinalIgnoreCase))
            return BmclapiHost + "/" + url["https://piston-meta.mojang.com/".Length..];
        if (url.StartsWith("https://launchermeta.mojang.com/", StringComparison.OrdinalIgnoreCase))
            return BmclapiHost + "/" + url["https://launchermeta.mojang.com/".Length..];
        if (url.StartsWith("https://launcher.mojang.com/", StringComparison.OrdinalIgnoreCase))
            return BmclapiHost + "/" + url["https://launcher.mojang.com/".Length..];
        if (url.StartsWith("https://piston-data.mojang.com/", StringComparison.OrdinalIgnoreCase))
            return BmclapiHost + "/" + url["https://piston-data.mojang.com/".Length..];
        return null;
    }

    public static string? ClientUrl(string versionId, string? officialUrl, string mode)
    {
        if (Normalize(mode) == Official) return null;
        return $"{BmclapiHost}/version/{Uri.EscapeDataString(versionId)}/client";
    }

    public static string? VersionJsonUrl(string versionId, string mode) =>
        Normalize(mode) == Official ? null : $"{BmclapiHost}/version/{Uri.EscapeDataString(versionId)}/json";

    /// <summary>官方 URL 与镜像 URL 组成下载顺序（先镜像、后官方）。</summary>
    public static (string Primary, string? Fallback) Resolve(string officialUrl, string mode)
    {
        var mirror = Rewrite(officialUrl, mode);
        return mirror is null ? (officialUrl, null) : (mirror, officialUrl);
    }

    /// <summary>资源对象优先官方 CDN（实测通常比 BMCLAPI 快很多），镜像作为回退。</summary>
    public static (string Primary, string? Fallback) ResolveAsset(string officialUrl, string mode)
    {
        var mirror = Rewrite(officialUrl, mode);
        if (mirror is null) return (officialUrl, null);
        return Normalize(mode) == Bmclapi ? (mirror, officialUrl) : (officialUrl, mirror);
    }
}

/// <summary>镜像健康状态：BMCLAPI 连续失败后，本进程内优先走官方源，避免每个文件都等一次超时。</summary>
internal static class McMirrorHealth
{
    private static long _bmclapiDownUntilTicks;
    private static int _consecutiveFailures;

    public static bool MirrorDown => DateTime.UtcNow.Ticks < Interlocked.Read(ref _bmclapiDownUntilTicks);

    public static void ReportSuccess(string url)
    {
        if (!IsMirror(url)) return;
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        Interlocked.Exchange(ref _bmclapiDownUntilTicks, 0);
    }

    public static void ReportFailure(string url)
    {
        if (!IsMirror(url)) return;
        if (Interlocked.Increment(ref _consecutiveFailures) < 3) return;
        Interlocked.Exchange(ref _bmclapiDownUntilTicks, DateTime.UtcNow.AddMinutes(5).Ticks);
    }

    public static bool IsMirror(string url) =>
        url.StartsWith(McMirror.BmclapiHost, StringComparison.OrdinalIgnoreCase);
}
