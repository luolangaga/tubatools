using System.Text.Json.Nodes;

namespace Tubatools.MinecraftLauncher.Services;

internal sealed record McFabricLoader(string Version, bool Stable);

/// <summary>Fabric Meta：加载器列表、安装配置 JSON 与安装流程（原版父版本 + Fabric 库）。</summary>
internal static class McFabric
{
    public const string MetaBase = "https://meta.fabricmc.net";

    public static McFabricLoader[] ParseLoaders(string json)
    {
        if (JsonNode.Parse(json) is not JsonArray array) return [];
        var result = new List<McFabricLoader>();
        foreach (var node in array)
        {
            var version = node?["loader"]?["version"]?.ToString();
            if (string.IsNullOrWhiteSpace(version)) continue;
            var stable = node?["loader"]?["stable"] is JsonValue value &&
                         value.GetValueKind() == System.Text.Json.JsonValueKind.True;
            result.Add(new McFabricLoader(version, stable));
        }

        return [.. result];
    }

    public static async Task<IReadOnlyList<McFabricLoader>> GetLoadersAsync(HttpClient http, string gameVersion,
        CancellationToken ct)
    {
        var json = await McHttp.GetStringAsync(http,
            $"{MetaBase}/v2/versions/loader/{Uri.EscapeDataString(gameVersion)}",
            TimeSpan.FromSeconds(30), ct);
        return ParseLoaders(json);
    }

    public static async Task<JsonObject> GetProfileAsync(HttpClient http, string gameVersion, string loaderVersion,
        CancellationToken ct)
    {
        var json = await McHttp.GetStringAsync(http,
            $"{MetaBase}/v2/versions/loader/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json",
            TimeSpan.FromSeconds(30), ct);
        return McJson.ParseObject(json);
    }

    public static string GetProfileId(JsonObject profile, string gameVersion, string loaderVersion) =>
        profile["id"]?.ToString() is { Length: > 0 } id
            ? id
            : $"fabric-loader-{loaderVersion}-{gameVersion}";

    /// <summary>安装 Fabric：确保原版完整（JSON / JAR / 支持库 / 资源），再写入 Fabric 配置并下载其依赖。</summary>
    public static async Task<McInstallResult> InstallAsync(
        HttpClient http,
        string gameVersion,
        string? loaderVersion,
        string gameDirectory,
        string mirror,
        bool versionIsolation,
        IProgress<McInstallProgress>? progress,
        Action<string>? log,
        CancellationToken ct)
    {
        var failures = new List<McDownloadFailure>();
        var notes = new List<string>();
        var versionsDirectory = Path.Combine(gameDirectory, "versions");
        Directory.CreateDirectory(versionsDirectory);

        var parentVersion = McVersionStore.Load(gameDirectory, gameVersion);
        var parentReady = parentVersion is { IsComplete: true } &&
                          parentVersion.AssetIndexId is { Length: > 0 } indexId &&
                          File.Exists(Path.Combine(gameDirectory, "assets", "indexes", indexId + ".json")) &&
                          McLibraryResolver.FindMissing(
                              McLibraryResolver.Resolve(parentVersion.Json, gameDirectory, McOsInfo.Current)).Count == 0;
        if (!parentReady)
        {
            progress?.Report(new McInstallProgress("安装原版 " + gameVersion, 0, 1));
            notes.Add($"先安装原版 {gameVersion}。");
            var manifest = await McVersionManifestClient.FetchAsync(http, mirror,
                Path.Combine(Path.GetTempPath(), "TubaWinUi3", "minecraft-cache"), false, ct);
            var parent = manifest.Versions.FirstOrDefault(version =>
                             string.Equals(version.Id, gameVersion, StringComparison.OrdinalIgnoreCase))
                         ?? throw new InvalidDataException($"版本清单里找不到原版 {gameVersion}。");
            var parentResult = await McInstaller.InstallAsync(http, parent.Id, parent.Url, gameDirectory, mirror,
                McOsInfo.Current, progress, log, ct, versionIsolation);
            failures.AddRange(parentResult.Failures);
            notes.AddRange(parentResult.Notes);
        }

        if (string.IsNullOrWhiteSpace(loaderVersion))
        {
            progress?.Report(new McInstallProgress("获取 Fabric 加载器列表", 0, 1));
            var loaders = await GetLoadersAsync(http, gameVersion, ct);
            loaderVersion = loaders.FirstOrDefault(loader => loader.Stable)?.Version
                            ?? loaders.FirstOrDefault()?.Version
                            ?? throw new InvalidDataException($"Fabric 没有支持 Minecraft {gameVersion} 的加载器。");
        }

        progress?.Report(new McInstallProgress("获取 Fabric 配置", 0, 1));
        var profile = await GetProfileAsync(http, gameVersion, loaderVersion, ct);
        var id = GetProfileId(profile, gameVersion, loaderVersion);
        var versionDirectory = Path.Combine(versionsDirectory, id);
        Directory.CreateDirectory(versionDirectory);
        var jsonPath = Path.Combine(versionDirectory, id + ".json");
        File.WriteAllText(jsonPath, profile.ToJsonString());
        log?.Invoke($"已写入 Fabric 配置：{jsonPath}");

        var plan = McInstallPlanner.Plan(profile, id, gameDirectory, versionDirectory, mirror, McOsInfo.Current);
        progress?.Report(new McInstallProgress("下载 Fabric 依赖", 0, plan.Files.Count));
        var stageProgress = new Progress<McDownloadProgress>(value =>
            progress?.Report(new McInstallProgress("下载 Fabric 依赖", value.Done, value.Total)));
        var result = await McDownloader.DownloadAsync(http, plan.Files, 8, stageProgress, log, ct);
        failures.AddRange(result.Failures);
        notes.Add($"Fabric Loader {loaderVersion} 已安装。");
        return new McInstallResult(failures.Count == 0, failures, notes, jsonPath);
    }
}
