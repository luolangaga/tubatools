using System.Text.Json.Nodes;

namespace Tubatools.MinecraftLauncher.Services;

internal sealed class McInstallPlan
{
    public List<McDownloadItem> Files { get; } = [];
    public string? AssetIndexId { get; set; }
    public string? AssetIndexPath { get; set; }
    public string? AssetIndexUrl { get; set; }
    public string? LoggingConfigPath { get; set; }
}

/// <summary>按 PCL 的规则把版本 JSON 展开为下载清单（客户端、支持库、natives、资源索引）。</summary>
internal static class McInstallPlanner
{
    public static McInstallPlan Plan(JsonObject versionJson, string versionId, string gameDirectory,
        string versionDirectory, string mirror, McOsInfo os)
    {
        var plan = new McInstallPlan();
        var versionIdSafe = versionId;

        var client = versionJson.Obj("downloads")?.Obj("client");
        var clientUrl = client?.Str("url");
        if (!string.IsNullOrWhiteSpace(clientUrl))
        {
            var primary = McMirror.ClientUrl(versionIdSafe, clientUrl, mirror) ?? clientUrl;
            var fallback = primary == clientUrl ? null : clientUrl;
            plan.Files.Add(new McDownloadItem(
                primary,
                Path.Combine(versionDirectory, versionIdSafe + ".jar"),
                client?.Str("sha1"),
                client.Long("size", -1),
                $"客户端 {versionIdSafe}.jar",
                fallback));
        }

        foreach (var library in McLibraryResolver.Resolve(versionJson, gameDirectory, os))
        {
            if (string.IsNullOrWhiteSpace(library.Url)) continue;
            var (primary, fallback) = McMirror.Resolve(library.Url, mirror);
            plan.Files.Add(new McDownloadItem(
                primary,
                library.LocalPath,
                library.Sha1,
                library.Size,
                library.Name,
                fallback));
        }

        var assetIndex = versionJson.Obj("assetIndex");
        var assetIndexId = assetIndex?.Str("id");
        var assetIndexUrl = assetIndex?.Str("url");
        if (!string.IsNullOrWhiteSpace(assetIndexId) && !string.IsNullOrWhiteSpace(assetIndexUrl))
        {
            var (primary, fallback) = McMirror.Resolve(assetIndexUrl, mirror);
            var target = Path.Combine(gameDirectory, "assets", "indexes", assetIndexId + ".json");
            plan.AssetIndexId = assetIndexId;
            plan.AssetIndexPath = target;
            plan.AssetIndexUrl = assetIndexUrl;
            plan.Files.Add(new McDownloadItem(
                primary,
                target,
                assetIndex?.Str("sha1"),
                assetIndex.Long("size", -1),
                $"资源索引 {assetIndexId}.json",
                fallback));
        }

        var loggingFile = versionJson.Obj("logging")?.Obj("client")?.Obj("file");
        var loggingUrl = loggingFile?.Str("url");
        if (!string.IsNullOrWhiteSpace(loggingUrl))
        {
            var fileId = loggingFile?.Str("id") ?? "client.xml";
            var target = Path.Combine(gameDirectory, "assets", "log_configs", fileId);
            plan.LoggingConfigPath = target;
            var (primary, fallback) = McMirror.Resolve(loggingUrl, mirror);
            plan.Files.Add(new McDownloadItem(primary, target, loggingFile?.Str("sha1"),
                loggingFile.Long("size", -1), $"日志配置 {fileId}", fallback));
        }

        return plan;
    }

    /// <summary>从资源索引生成资源对象下载清单。</summary>
    public static List<McDownloadItem> PlanAssets(JsonObject assetIndex, string gameDirectory, string mirror)
    {
        var items = new List<McDownloadItem>();
        if (assetIndex["objects"] is not JsonObject objects) return items;

        var objectsDirectory = Path.Combine(gameDirectory, "assets", "objects");
        foreach (var property in objects)
        {
            var hash = property.Value?.Str("hash");
            if (string.IsNullOrWhiteSpace(hash) || hash.Length < 2) continue;
            var size = property.Value.Long("size", -1);
            var relative = $"{hash[..2]}/{hash}";
            var target = Path.Combine(objectsDirectory, hash[..2], hash);
            // 不在这里跳过已存在文件：由下载器按大小/SHA-1 判断，损坏文件也会被重新下载。
            var official = $"https://resources.download.minecraft.net/{relative}";
            var (primary, fallback) = McMirror.ResolveAsset(official, mirror);
            items.Add(new McDownloadItem(primary, target, hash, size, $"资源 {property.Key}", fallback));
        }

        return items;
    }

    /// <summary>旧版资源索引需要把对象复制到 virtual/legacy（或 resources）。</summary>
    public static (bool Virtual, bool MapToResources) GetLegacyMode(JsonObject assetIndex)
    {
        return (assetIndex.Bool("virtual"), assetIndex.Bool("map_to_resources"));
    }
}

internal sealed record McInstallProgress(string Stage, int Done, int Total);

internal sealed record McInstallResult(
    bool Success,
    IReadOnlyList<McDownloadFailure> Failures,
    IReadOnlyList<string> Notes,
    string? VersionJsonPath);

internal static class McInstaller
{
    /// <summary>安装一个 Mojang 清单版本：版本 JSON → 客户端 / 支持库 / 资源索引 → 全部资源对象。</summary>
    public static async Task<McInstallResult> InstallAsync(
        HttpClient http,
        string versionId,
        string jsonUrl,
        string gameDirectory,
        string mirror,
        McOsInfo os,
        IProgress<McInstallProgress>? progress,
        Action<string>? log,
        CancellationToken ct,
        bool versionIsolation = true)
    {
        gameDirectory = Path.GetFullPath(gameDirectory);
        var notes = new List<string>();
        var failures = new List<McDownloadFailure>();
        var versionsDirectory = Path.Combine(gameDirectory, "versions");
        var versionDirectory = Path.Combine(versionsDirectory, versionId);
        Directory.CreateDirectory(versionDirectory);

        progress?.Report(new McInstallProgress("获取版本信息", 0, 1));
        var jsonText = await FetchVersionJsonAsync(http, versionId, jsonUrl, mirror, ct);
        var versionJson = McJson.ParseObject(jsonText);
        var jsonPath = Path.Combine(versionDirectory, versionId + ".json");
        WriteAtomic(jsonPath, jsonText);
        log?.Invoke($"已保存版本 JSON：{jsonPath}");

        if (versionJson["inheritsFrom"]?.ToString() is { Length: > 0 } parent)
            notes.Add($"该版本继承自 {parent}，请确认前置版本已安装。");

        var plan = McInstallPlanner.Plan(versionJson, versionId, gameDirectory, versionDirectory, mirror, os);

        progress?.Report(new McInstallProgress("下载依赖与客户端", 0, plan.Files.Count));
        var stageProgress = new Progress<McDownloadProgress>(value =>
            progress?.Report(new McInstallProgress("下载依赖与客户端", value.Done, value.Total)));
        var result = await McDownloader.DownloadAsync(http, plan.Files, 8, stageProgress, log, ct);
        failures.AddRange(result.Failures);

        if (plan.AssetIndexPath is not null && File.Exists(plan.AssetIndexPath))
        {
            JsonObject assetIndex;
            try
            {
                assetIndex = McJson.ParseObject(await File.ReadAllTextAsync(plan.AssetIndexPath, ct));
            }
            catch (Exception ex)
            {
                failures.Add(new McDownloadFailure("资源索引", ex.Message));
                assetIndex = new JsonObject();
            }

            var assets = McInstallPlanner.PlanAssets(assetIndex, gameDirectory, mirror);
            progress?.Report(new McInstallProgress("下载游戏资源", 0, assets.Count));
            var assetProgress = new Progress<McDownloadProgress>(value =>
                progress?.Report(new McInstallProgress("下载游戏资源", value.Done, value.Total)));
            var assetResult = await McDownloader.DownloadAsync(http, assets, 24, assetProgress, log, ct);
            failures.AddRange(assetResult.Failures);

            var (virtualAssets, mapToResources) = McInstallPlanner.GetLegacyMode(assetIndex);
            if (virtualAssets || mapToResources)
            {
                progress?.Report(new McInstallProgress("整理旧版资源", 0, 1));
                var indieDirectory = versionIsolation ? versionDirectory : gameDirectory;
                var copied = await Task.Run(
                    () => PrepareLegacyAssets(assetIndex, gameDirectory, indieDirectory, mapToResources, ct), ct);
                notes.Add(mapToResources
                    ? $"旧版资源已整理 {copied} 个文件（resources）。"
                    : $"旧版资源已整理 {copied} 个文件（virtual/legacy）。");
            }
        }
        else
        {
            notes.Add("版本 JSON 中没有资源索引，可能无法启动。");
        }

        progress?.Report(new McInstallProgress("安装完成", 1, 1));
        return new McInstallResult(failures.Count == 0, failures, notes, jsonPath);
    }

    private static async Task<string> FetchVersionJsonAsync(HttpClient http, string versionId, string officialUrl,
        string mirror, CancellationToken ct)
    {
        var urls = new List<string>();
        var mirrorUrl = McMirror.VersionJsonUrl(versionId, mirror);
        if (mirrorUrl is not null) urls.Add(mirrorUrl);
        var rewritten = McMirror.Rewrite(officialUrl, mirror);
        if (rewritten is not null && !urls.Contains(rewritten)) urls.Add(rewritten);
        if (!urls.Contains(officialUrl)) urls.Add(officialUrl);

        var errors = new List<string>();
        foreach (var url in urls)
        {
            try
            {
                return await McHttp.GetStringAsync(http, url, TimeSpan.FromSeconds(30), ct);
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

        throw new HttpRequestException("无法下载版本 JSON。\n" + string.Join("\n", errors));
    }

    private static int PrepareLegacyAssets(JsonObject assetIndex, string gameDirectory, string indieDirectory,
        bool mapToResources, CancellationToken ct)
    {
        if (assetIndex["objects"] is not JsonObject objects) return 0;
        // 与 PCL 一致：map_to_resources 落在实例工作目录的 resources，virtual 落在 assets/virtual/legacy。
        var targetRoot = mapToResources
            ? Path.Combine(indieDirectory, "resources")
            : Path.Combine(gameDirectory, "assets", "virtual", "legacy");
        var objectsRoot = Path.Combine(gameDirectory, "assets", "objects");
        var copied = 0;

        foreach (var property in objects)
        {
            ct.ThrowIfCancellationRequested();
            var hash = property.Value?.Str("hash");
            if (string.IsNullOrWhiteSpace(hash) || hash.Length < 2) continue;
            var source = Path.Combine(objectsRoot, hash[..2], hash);
            if (!File.Exists(source)) continue;
            var target = Path.Combine(targetRoot, property.Key.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(source).Length) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try
            {
                File.Copy(source, target, true);
                copied++;
            }
            catch
            {
                // 单个资源复制失败不影响其它文件。
            }
        }

        return copied;
    }

    private static void WriteAtomic(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, true);
    }
}
