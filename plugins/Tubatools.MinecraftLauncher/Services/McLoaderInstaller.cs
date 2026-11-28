using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Tubatools.MinecraftLauncher.Services;

internal enum McLoaderKind
{
    Vanilla,
    Fabric,
    Quilt,
    Forge,
    NeoForge
}

internal sealed record McLoaderVersion(string Version, bool Recommended);

/// <summary>
/// 一键安装：原版 + Fabric / Quilt / Forge / NeoForge。
/// Forge / NeoForge 使用官方 installer（--installClient），Fabric / Quilt 使用 Meta profile。
/// </summary>
internal static class McLoaderInstaller
{
    private const string ForgeMaven = "https://maven.minecraftforge.net/net/minecraftforge/forge";
    private const string NeoForgeMaven = "https://maven.neoforged.net/releases/net/neoforged/neoforge";
    private const string FabricMeta = "https://meta.fabricmc.net/v2";
    private const string QuiltMeta = "https://meta.quiltmc.org/v3";
    private static readonly Comparer<string> VersionComparer =
        Comparer<string>.Create(McLibraryResolver.CompareVersion);

    public static string DisplayName(McLoaderKind kind) => kind switch
    {
        McLoaderKind.Fabric => "Fabric",
        McLoaderKind.Quilt => "Quilt",
        McLoaderKind.Forge => "Forge",
        McLoaderKind.NeoForge => "NeoForge",
        _ => "原版"
    };

    public static async Task<IReadOnlyList<McLoaderVersion>> GetVersionsAsync(HttpClient http,
        McLoaderKind kind, string gameVersion, CancellationToken ct)
    {
        return kind switch
        {
            McLoaderKind.Fabric => await GetFabricLikeVersionsAsync(http, FabricMeta, gameVersion, ct),
            McLoaderKind.Quilt => await GetFabricLikeVersionsAsync(http, QuiltMeta, gameVersion, ct),
            McLoaderKind.Forge => await GetForgeVersionsAsync(http, gameVersion, ct),
            McLoaderKind.NeoForge => await GetNeoForgeVersionsAsync(http, gameVersion, ct),
            _ => []
        };
    }

    public static async Task<McInstallResult> InstallAsync(
        HttpClient http,
        McLoaderKind kind,
        string gameVersion,
        string? loaderVersion,
        string gameDirectory,
        string mirror,
        bool versionIsolation,
        string cacheDirectory,
        JavaRuntime? java,
        IProgress<McInstallProgress>? progress,
        Action<string>? log,
        CancellationToken ct)
    {
        gameDirectory = Path.GetFullPath(gameDirectory);
        if (kind == McLoaderKind.Vanilla)
        {
            var manifest = await McVersionManifestClient.FetchAsync(http, mirror, cacheDirectory, false, ct);
            var target = manifest.Versions.FirstOrDefault(version =>
                             string.Equals(version.Id, gameVersion, StringComparison.OrdinalIgnoreCase))
                         ?? throw new InvalidDataException($"版本清单里找不到 Minecraft {gameVersion}。");
            return await McInstaller.InstallAsync(http, target.Id, target.Url, gameDirectory, mirror,
                McOsInfo.Current, progress, log, ct, versionIsolation);
        }

        progress?.Report(new McInstallProgress("检查原版文件", 0, 1));
        var notes = new List<string>();
        var failures = new List<McDownloadFailure>();
        var parentResult = await EnsureVanillaAsync(http, gameVersion, gameDirectory, mirror, versionIsolation,
            cacheDirectory, progress, log, ct);
        if (parentResult is not null)
        {
            failures.AddRange(parentResult.Failures);
            notes.AddRange(parentResult.Notes);
        }

        if (kind is McLoaderKind.Fabric or McLoaderKind.Quilt)
        {
            var metaBase = kind == McLoaderKind.Fabric ? FabricMeta : QuiltMeta;
            var profileResult = await InstallProfileAsync(http, metaBase, DisplayName(kind), gameVersion,
                loaderVersion, gameDirectory, mirror, progress, log, ct);
            failures.AddRange(profileResult.Failures);
            notes.AddRange(profileResult.Notes);
            return new McInstallResult(failures.Count == 0 && profileResult.Success, failures, notes,
                profileResult.VersionJsonPath);
        }

        var installerResult = await InstallWithInstallerAsync(http, kind, gameVersion, loaderVersion,
            gameDirectory, cacheDirectory, java, progress, log, ct);
        failures.AddRange(installerResult.Failures);
        notes.AddRange(installerResult.Notes);
        return new McInstallResult(failures.Count == 0 && installerResult.Success, failures, notes,
            installerResult.VersionJsonPath);
    }

    private static async Task<McInstallResult?> EnsureVanillaAsync(HttpClient http, string gameVersion,
        string gameDirectory, string mirror, bool versionIsolation, string cacheDirectory,
        IProgress<McInstallProgress>? progress, Action<string>? log, CancellationToken ct)
    {
        var version = McVersionStore.Load(gameDirectory, gameVersion);
        var ready = version is { IsComplete: true } &&
                    version.AssetIndexId is { Length: > 0 } indexId &&
                    File.Exists(Path.Combine(gameDirectory, "assets", "indexes", indexId + ".json")) &&
                    AssetsComplete(gameDirectory, indexId) &&
                    McLibraryResolver.FindMissing(
                        McLibraryResolver.Resolve(version.Json, gameDirectory, McOsInfo.Current)).Count == 0;
        if (ready) return null;

        progress?.Report(new McInstallProgress("安装原版 " + gameVersion, 0, 1));
        log?.Invoke($"原版 {gameVersion} 不完整，先自动安装原版。");
        var manifest = await McVersionManifestClient.FetchAsync(http, mirror, cacheDirectory, false, ct);
        var target = manifest.Versions.FirstOrDefault(candidate =>
                         string.Equals(candidate.Id, gameVersion, StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidDataException($"版本清单里找不到 Minecraft {gameVersion}。");
        var result = await McInstaller.InstallAsync(http, target.Id, target.Url, gameDirectory, mirror,
            McOsInfo.Current, progress, log, ct, versionIsolation);
        return result;
    }

    private static bool AssetsComplete(string gameDirectory, string indexId)
    {
        try
        {
            var path = Path.Combine(gameDirectory, "assets", "indexes", indexId + ".json");
            var index = McJson.ParseObject(File.ReadAllText(path));
            return McInstallPlanner.PlanAssets(index, gameDirectory, "official")
                .All(asset => File.Exists(asset.TargetPath));
        }
        catch
        {
            return false;
        }
    }

    private static async Task<IReadOnlyList<McLoaderVersion>> GetFabricLikeVersionsAsync(HttpClient http,
        string metaBase, string gameVersion, CancellationToken ct)
    {
        var json = await McHttp.GetStringAsync(http,
            $"{metaBase}/versions/loader/{Uri.EscapeDataString(gameVersion)}",
            TimeSpan.FromSeconds(30), ct);
        if (JsonNode.Parse(json) is not JsonArray array) return [];
        var result = new List<McLoaderVersion>();
        foreach (var node in array)
        {
            var version = node?["loader"]?["version"]?.ToString();
            if (string.IsNullOrWhiteSpace(version)) continue;
            var stable = node?["loader"]?["stable"] is JsonValue value &&
                         value.GetValueKind() == System.Text.Json.JsonValueKind.True;
            if (!stable && result.Count < 120) stable = version.StartsWith("0.") && !version.Contains("beta", StringComparison.OrdinalIgnoreCase);
            result.Add(new McLoaderVersion(version, result.Count == 0 || stable));
        }

        for (var i = 0; i < result.Count; i++)
            if (result[i].Recommended)
            {
                result[0] = result[0] with { Recommended = false };
                result[i] = result[i] with { Recommended = true };
                break;
            }

        return result;
    }

    private static async Task<IReadOnlyList<McLoaderVersion>> GetForgeVersionsAsync(HttpClient http,
        string gameVersion, CancellationToken ct)
    {
        var metadata = await McHttp.GetStringAsync(http, $"{ForgeMaven}/maven-metadata.xml",
            TimeSpan.FromSeconds(30), ct);
        var versions = ParseMavenVersions(metadata, gameVersion + "-");
        var recommended = "";
        try
        {
            var promosJson = await McHttp.GetStringAsync(http,
                "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json",
                TimeSpan.FromSeconds(20), ct);
            recommended = McJson.ParseObject(promosJson).Obj("promos")?.Str(gameVersion + "-recommended") ?? "";
        }
        catch
        {
            // 推荐版本获取失败时使用最新稳定版。
        }

        var result = versions
            .OrderByDescending(version => version, VersionComparer)
            .Select(version => new McLoaderVersion(version,
                string.Equals(version, recommended, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (result.Count > 0 && result.All(version => !version.Recommended))
            result[0] = result[0] with { Recommended = true };
        return result.Take(120).ToList();
    }

    private static async Task<IReadOnlyList<McLoaderVersion>> GetNeoForgeVersionsAsync(HttpClient http,
        string gameVersion, CancellationToken ct)
    {
        var prefix = NeoForgePrefix(gameVersion);
        if (prefix is null) return [];

        var versions = new List<string>();
        try
        {
            var json = await McHttp.GetStringAsync(http,
                "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge",
                TimeSpan.FromSeconds(30), ct);
            if (McJson.ParseObject(json)["versions"] is JsonArray array)
                versions.AddRange(array.Select(node => node?.ToString() ?? ""));
        }
        catch
        {
            var metadata = await McHttp.GetStringAsync(http, $"{NeoForgeMaven}/maven-metadata.xml",
                TimeSpan.FromSeconds(30), ct);
            versions.AddRange(ParseMavenVersions(metadata, prefix));
        }

        var filtered = versions
            .Where(version => version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                              !version.Contains("beta", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(version => version, VersionComparer)
            .ToList();
        if (filtered.Count == 0)
            filtered = versions
                .Where(version => version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(version => version, VersionComparer)
                .ToList();
        return filtered.Take(120).Select((version, index) =>
            new McLoaderVersion(version, index == 0)).ToList();
    }

    internal static string? NeoForgePrefix(string gameVersion)
    {
        var parts = gameVersion.Split('.');
        if (parts.Length < 2 || parts[0] != "1") return null;
        if (parts[1] == "20" && (parts.Length < 3 || parts[2] is "0" or "1")) return null;
        var minor = parts[1];
        var patch = parts.Length >= 3 ? parts[2] : "0";
        return $"{minor}.{patch}.";
    }

    internal static List<string> ParseMavenVersions(string xml, string prefix)
    {
        try
        {
            var document = XDocument.Parse(xml);
            return document.Descendants()
                .Where(element => element.Name.LocalName == "version")
                .Select(element => element.Value.Trim())
                .Where(version => version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(version => version[prefix.Length..])
                .Where(version => version.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static async Task<McInstallResult> InstallProfileAsync(HttpClient http, string metaBase,
        string loaderName, string gameVersion, string? loaderVersion, string gameDirectory, string mirror,
        IProgress<McInstallProgress>? progress, Action<string>? log, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(loaderVersion))
        {
            var versions = await GetFabricLikeVersionsAsync(http, metaBase, gameVersion, ct);
            loaderVersion = versions.FirstOrDefault(version => version.Recommended)?.Version
                            ?? versions.FirstOrDefault()?.Version
                            ?? throw new InvalidDataException($"{loaderName} 没有支持 Minecraft {gameVersion} 的版本。");
        }

        progress?.Report(new McInstallProgress($"获取 {loaderName} 配置", 0, 1));
        var profileJson = await McHttp.GetStringAsync(http,
            $"{metaBase}/versions/loader/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json",
            TimeSpan.FromSeconds(30), ct);
        var profile = McJson.ParseObject(profileJson);
        var id = profile["id"]?.ToString() is { Length: > 0 } profileId
            ? profileId
            : $"{loaderName.ToLowerInvariant()}-loader-{loaderVersion}-{gameVersion}";
        var versionDirectory = Path.Combine(gameDirectory, "versions", id);
        Directory.CreateDirectory(versionDirectory);
        var jsonPath = Path.Combine(versionDirectory, id + ".json");
        File.WriteAllText(jsonPath, profile.ToJsonString());
        log?.Invoke($"已写入 {loaderName} 配置：{jsonPath}");

        var plan = McInstallPlanner.Plan(profile, id, gameDirectory, versionDirectory, mirror, McOsInfo.Current);
        progress?.Report(new McInstallProgress($"下载 {loaderName} 依赖", 0, plan.Files.Count));
        var stageProgress = new Progress<McDownloadProgress>(value =>
            progress?.Report(new McInstallProgress($"下载 {loaderName} 依赖", value.Done, value.Total)));
        var result = await McDownloader.DownloadAsync(http, plan.Files, 8, stageProgress, log, ct);
        return new McInstallResult(result.Failures.Count == 0, result.Failures,
            [$"{loaderName} {loaderVersion} 已安装。"], jsonPath);
    }

    private static async Task<McInstallResult> InstallWithInstallerAsync(HttpClient http, McLoaderKind kind,
        string gameVersion, string? loaderVersion, string gameDirectory, string cacheDirectory,
        JavaRuntime? java, IProgress<McInstallProgress>? progress, Action<string>? log, CancellationToken ct)
    {
        var display = DisplayName(kind);
        if (string.IsNullOrWhiteSpace(loaderVersion))
        {
            var versions = await GetVersionsAsync(http, kind, gameVersion, ct);
            loaderVersion = versions.FirstOrDefault(version => version.Recommended)?.Version
                            ?? versions.FirstOrDefault()?.Version
                            ?? throw new InvalidDataException($"{display} 没有支持 Minecraft {gameVersion} 的版本。");
        }

        var installerUrl = kind == McLoaderKind.Forge
            ? $"{ForgeMaven}/{gameVersion}-{loaderVersion}/forge-{gameVersion}-{loaderVersion}-installer.jar"
            : $"{NeoForgeMaven}/{loaderVersion}/neoforge-{loaderVersion}-installer.jar";
        var expectedId = kind == McLoaderKind.Forge
            ? $"{gameVersion}-forge-{loaderVersion}"
            : $"neoforge-{loaderVersion}";
        var expectedJson = Path.Combine(gameDirectory, "versions", expectedId, expectedId + ".json");
        if (File.Exists(expectedJson))
            return new McInstallResult(true, [], [$"{display} {loaderVersion} 已安装。"], expectedJson);
        var installerDirectory = Path.Combine(Path.GetTempPath(), "TubaWinUi3", "minecraft-installers");
        Directory.CreateDirectory(installerDirectory);
        var installerPath = Path.Combine(installerDirectory,
            $"{display.ToLowerInvariant()}-{gameVersion}-{loaderVersion}-installer.jar");
        if (!File.Exists(installerPath) || new FileInfo(installerPath).Length == 0)
        {
            progress?.Report(new McInstallProgress($"下载 {display} 安装器", 0, 1));
            log?.Invoke($"下载 {display} 安装器：{installerUrl}");
            var download = await McDownloader.DownloadAsync(http,
                [new McDownloadItem(installerUrl, installerPath, null, -1, $"{display} 安装器")],
                1, null, log, ct);
            if (download.Failures.Count > 0)
                throw new IOException($"下载 {display} 安装器失败：{download.Failures[0].Error}");
        }

        java ??= McJava.Detect(null, gameDirectory).FirstOrDefault();
        if (java is null)
            throw new InvalidOperationException($"安装 {display} 需要 Java，请先在设置页指定 java.exe。");

        var profilePath = Path.Combine(gameDirectory, "launcher_profiles.json");
        if (!File.Exists(profilePath))
            File.WriteAllText(profilePath,
                """{"profiles":{},"settings":{"enableSnapshots":false,"keepLauncherOpen":false,"showMenu":false},"version":3}""");

        progress?.Report(new McInstallProgress($"运行 {display} 安装器", 0, 1));
        log?.Invoke($"运行 {display} 安装器：{installerPath}");
        var startTime = DateTime.Now;
        var startInfo = new ProcessStartInfo(java.JavaExe)
        {
            Arguments = $"-jar \"{installerPath}\" --installClient \"{gameDirectory}\"",
            WorkingDirectory = gameDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = java.Major > 8 ? System.Text.Encoding.UTF8 : System.Text.Encoding.Default,
            StandardErrorEncoding = java.Major > 8 ? System.Text.Encoding.UTF8 : System.Text.Encoding.Default
        };
        var tail = new Queue<string>();
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => Append(args.Data);
        process.ErrorDataReceived += (_, args) => Append(args.Data);
        void Append(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (tail)
            {
                tail.Enqueue(line);
                while (tail.Count > 40) tail.Dequeue();
            }

            log?.Invoke($"[{display}] {line}");
        }

        if (!process.Start()) throw new InvalidOperationException($"无法启动 {display} 安装器。");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException($"{display} 安装器超过 20 分钟未完成。");
        }

        if (process.ExitCode != 0)
        {
            string detail;
            lock (tail) detail = string.Join("\n", tail);
            throw new InvalidOperationException(
                $"{display} 安装器退出代码 {process.ExitCode}。\n{detail}");
        }

        if (File.Exists(expectedJson))
            return new McInstallResult(true, [], [$"{display} {loaderVersion} 已安装。"], expectedJson);
        var versionsDirectory = Path.Combine(gameDirectory, "versions");
        var candidate = Directory.Exists(versionsDirectory)
            ? Directory.EnumerateDirectories(versionsDirectory)
                .Select(directory => (Directory: directory, Time: Directory.GetLastWriteTime(directory)))
                .Where(item => item.Time >= startTime.AddSeconds(-5) &&
                               Path.GetFileName(item.Directory)
                                   .Contains(kind == McLoaderKind.Forge ? "forge" : "neoforge",
                                       StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Time)
                .Select(item => Path.Combine(item.Directory, Path.GetFileName(item.Directory) + ".json"))
                .FirstOrDefault(File.Exists)
            : null;
        if (candidate is not null)
            return new McInstallResult(true, [], [$"{display} {loaderVersion} 已安装（{Path.GetFileNameWithoutExtension(candidate)}）。"],
                candidate);

        throw new InvalidOperationException(
            $"{display} 安装器已结束，但没有找到生成的版本配置。请查看日志确认是否安装成功。");
    }
}
