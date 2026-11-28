using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Tubatools.MinecraftLauncher.Services;

internal sealed record McModpackInstallResult(
    string VersionId,
    string Name,
    int Files,
    int Overrides);

internal sealed record McModpackFile(
    string Path,
    string Url,
    string? Sha1,
    long Size,
    bool Unsupported);

internal sealed record McModpackIndex(
    string Name,
    string VersionId,
    string Minecraft,
    McLoaderKind Loader,
    string? LoaderVersion,
    IReadOnlyList<McModpackFile> Files);

/// <summary>Modrinth .mrpack 整合包安装：解析 index、安装加载器、下载内容、覆盖 overrides。</summary>
internal static class McModpackInstaller
{
    public static async Task<McModpackInstallResult> InstallAsync(
        HttpClient http,
        McResourceFile pack,
        string displayName,
        string slug,
        string gameDirectory,
        string cacheDirectory,
        string mirror,
        IReadOnlyList<JavaRuntime> javas,
        string? javaPath,
        string language,
        IProgress<McInstallProgress>? progress,
        Action<string> log,
        CancellationToken ct)
    {
        gameDirectory = Path.GetFullPath(gameDirectory);
        var packDirectory = Path.Combine(cacheDirectory, "modpacks");
        Directory.CreateDirectory(packDirectory);
        var packPath = Path.Combine(packDirectory, SanitizeFileName(pack.FileName, "pack.mrpack"));

        progress?.Report(new McInstallProgress("下载整合包", 0, 1));
        var download = await McDownloader.DownloadAsync(http,
            [new McDownloadItem(pack.Url, packPath, pack.Sha1, pack.Size, pack.FileName)],
            1, null, log, ct);
        if (download.Failures.Count > 0)
            throw new IOException("整合包下载失败：" + download.Failures[0].Error);

        var index = ReadIndex(packPath);
        var loaderVersion = index.Loader == McLoaderKind.Vanilla ? null : index.LoaderVersion;
        if (index.Loader != McLoaderKind.Vanilla && string.IsNullOrWhiteSpace(loaderVersion))
            throw new InvalidDataException("整合包缺少加载器版本，无法安装。");

        var requiredJava = RequiredJava(index.Minecraft);
        var (java, _) = McJava.Select(javas, requiredJava, javaPath);
        var loader = await McLoaderInstaller.InstallAsync(http, index.Loader, index.Minecraft, loaderVersion,
            gameDirectory, mirror, true, cacheDirectory, java, progress, log, ct);
        var parent = Path.GetFileNameWithoutExtension(loader.VersionJsonPath ?? "");
        if (string.IsNullOrWhiteSpace(parent))
            parent = index.Minecraft;

        var versionsRoot = Path.Combine(gameDirectory, "versions");
        var versionId = UniqueVersionId(versionsRoot, SanitizeVersionId(slug));
        var versionDirectory = Path.Combine(versionsRoot, versionId);
        Directory.CreateDirectory(versionDirectory);

        var versionJson = new JsonObject
        {
            ["id"] = versionId,
            ["type"] = "release",
            ["releaseTime"] = DateTime.UtcNow.ToString("O"),
            ["inheritsFrom"] = parent
        };
        var versionJsonPath = Path.Combine(versionDirectory, versionId + ".json");
        await File.WriteAllTextAsync(versionJsonPath, versionJson.ToJsonString(), ct);

        progress?.Report(new McInstallProgress("下载整合包内容", 0, index.Files.Count));
        var files = index.Files
            .Where(file => !file.Unsupported && !string.IsNullOrWhiteSpace(file.Url))
            .Select(file => new McDownloadItem(file.Url, SafeTarget(versionDirectory, file.Path),
                file.Sha1, file.Size, file.Path))
            .ToList();
        if (files.Count > 0)
        {
            var fileProgress = new Progress<McDownloadProgress>(value =>
                progress?.Report(new McInstallProgress("下载整合包内容", value.Done, value.Total)));
            var result = await McDownloader.DownloadAsync(http, files, 24, fileProgress, log, ct);
            if (result.Failures.Count > 0)
                throw new IOException($"整合包有 {result.Failures.Count} 个文件下载失败，请重试。\n" +
                                      result.Failures[0].Error);
        }

        var overrides = ExtractOverrides(packPath, versionDirectory, log);
        McGameLauncher.ApplyGameLanguage(versionDirectory, language, log);
        log($"[整合包] {index.Name} 安装完成：{versionId}，内容 {files.Count} 个，覆盖 {overrides} 个");

        return new McModpackInstallResult(versionId, index.Name, files.Count, overrides);
    }

    internal static McModpackIndex ReadIndex(string packPath)
    {
        using var archive = ZipFile.OpenRead(packPath);
        var entry = archive.GetEntry("modrinth.index.json")
                    ?? throw new InvalidDataException("整合包缺少 modrinth.index.json。");
        using var stream = entry.Open();
        var json = JsonNode.Parse(stream) as JsonObject
                   ?? throw new InvalidDataException("整合包 modrinth.index.json 无法解析。");
        var name = json.Str("name") ?? "Minecraft 整合包";
        var versionId = json.Str("versionId") ?? "1.0.0";
        var dependencies = json["dependencies"] as JsonObject;
        var minecraft = dependencies?.Str("minecraft");
        if (string.IsNullOrWhiteSpace(minecraft))
            throw new InvalidDataException("整合包没有声明 Minecraft 版本。");

        var loader = McLoaderKind.Vanilla;
        string? loaderVersion = null;
        foreach (var (key, kind) in new[]
                 {
                     ("fabric-loader", McLoaderKind.Fabric),
                     ("quilt-loader", McLoaderKind.Quilt),
                     ("forge", McLoaderKind.Forge),
                     ("neoforge", McLoaderKind.NeoForge)
                 })
        {
            if (dependencies?[key] is not { } value) continue;
            loader = kind;
            loaderVersion = value.ToString();
            break;
        }

        if (loader is McLoaderKind.Forge or McLoaderKind.NeoForge &&
            loaderVersion?.StartsWith(minecraft + "-", StringComparison.OrdinalIgnoreCase) == true)
            loaderVersion = loaderVersion[(minecraft.Length + 1)..];

        var files = new List<McModpackFile>();
        if (json["files"] is JsonArray array)
            foreach (var node in array)
            {
                var path = node?["path"]?.ToString();
                var downloads = node?["downloads"] as JsonArray;
                var url = downloads?.Select(item => item?.ToString())
                    .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(url)) continue;
                var unsupported = string.Equals(node?["env"]?["client"]?.ToString(), "unsupported",
                    StringComparison.OrdinalIgnoreCase);
                files.Add(new McModpackFile(
                    path,
                    url,
                    node?["hashes"]?["sha1"]?.ToString(),
                    node?.Long("fileSize", -1) ?? -1,
                    unsupported));
            }

        return new McModpackIndex(name, versionId, minecraft, loader, loaderVersion, files);
    }

    private static string UniqueVersionId(string versionsRoot, string preferred)
    {
        var id = preferred;
        var index = 2;
        while (Directory.Exists(Path.Combine(versionsRoot, id))) id = $"{preferred}-{index++}";
        return id;
    }

    private static string SanitizeVersionId(string value)
    {
        var chars = value.Trim().Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-')
            .ToArray();
        var result = new string(chars).Trim('-', '.', '_');
        if (result.Length == 0) result = "modpack";
        return result.Length > 48 ? result[..48].TrimEnd('-', '.', '_') : result;
    }

    private static string SanitizeFileName(string value, string fallback)
    {
        var name = Path.GetFileName(value);
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private static string SafeTarget(string root, string relative)
    {
        var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("整合包文件路径越界：" + relative);
        return target;
    }

    private static int ExtractOverrides(string packPath, string destination, Action<string> log)
    {
        var count = 0;
        using var archive = ZipFile.OpenRead(packPath);
        foreach (var entry in archive.Entries)
        {
            if (entry.Name.Length == 0) continue;
            var name = entry.FullName.Replace('\\', '/');
            var prefix = name.StartsWith("client-overrides/", StringComparison.Ordinal)
                ? "client-overrides/"
                : name.StartsWith("overrides/", StringComparison.Ordinal) ? "overrides/" : null;
            if (prefix is null) continue;
            var relative = name[prefix.Length..];
            try
            {
                var target = SafeTarget(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
                count++;
            }
            catch (Exception ex)
            {
                log($"[整合包] 跳过覆盖文件 {relative}：{ex.Message}");
            }
        }

        return count;
    }

    private static int RequiredJava(string minecraftVersion)
    {
        var parts = minecraftVersion.Split('.');
        if (parts.Length == 1)
        {
            return int.TryParse(parts[0], out var major) && major >= 26 ? 25 : 8;
        }

        if (!int.TryParse(parts[0], out var first) || !int.TryParse(parts[1], out var second))
            return 17;
        if (first > 1) return 25;
        if (second >= 21) return 21;
        if (second >= 18) return 17;
        return 8;
    }
}
