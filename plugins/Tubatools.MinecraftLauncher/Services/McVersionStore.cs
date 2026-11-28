using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tubatools.MinecraftLauncher.Services;

/// <summary>一个已安装版本（已合并 inheritsFrom 继承链）。</summary>
internal sealed class McVersion
{
    public required string Id { get; init; }
    public required string Directory { get; init; }
    public required JsonObject Json { get; init; }
    public string? InheritsFrom { get; init; }
    public string VanillaName { get; init; } = "未知";
    public string Type { get; init; } = "release";
    public DateTime ReleaseTime { get; init; }
    public string MainClass { get; init; } = "";
    public string ClientJarPath { get; init; } = "";
    public bool HasClientJar { get; init; }
    public string Loaders { get; init; } = "";
    public string? AssetIndexId { get; init; }
    public string? AssetIndexUrl { get; init; }
    public string? Assets { get; init; }
    public string? Error { get; init; }
    public bool HasForge { get; init; }
    public bool HasNeoForge { get; init; }
    public bool HasFabric { get; init; }
    public bool HasOptiFine { get; init; }
    public bool HasLiteLoader { get; init; }

    public string VersionLabel => Loaders.Length > 0 ? $"Minecraft {VanillaName} · {Loaders}" : $"Minecraft {VanillaName}";
    public string TypeLabel => Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        "old_beta" => "远古 Beta",
        "old_alpha" => "远古 Alpha",
        _ => Type
    };

    public JsonObject? AssetIndex => Json["assetIndex"] as JsonObject;
    public string JsonPath => Path.Combine(Directory, Id + ".json");
    public bool IsComplete => Error is null && HasClientJar;
}

internal sealed class McVersionScanResult
{
    public List<McVersion> Versions { get; } = [];
    public List<string> Problems { get; } = [];
    public string GameDirectory { get; init; } = "";
}

/// <summary>扫描 versions 目录、合并继承链、识别加载器与客户端 JAR（对齐 PCL 的 McInstance）。</summary>
internal static class McVersionStore
{
    public static McVersionScanResult Scan(string gameDirectory)
    {
        gameDirectory = Path.GetFullPath(gameDirectory);
        var result = new McVersionScanResult { GameDirectory = gameDirectory };
        var versionsDirectory = Path.Combine(gameDirectory, "versions");
        if (!Directory.Exists(versionsDirectory))
            return result;

        var raw = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in Directory.EnumerateDirectories(versionsDirectory))
        {
            var id = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(id)) continue;
            var jsonPath = Path.Combine(directory, id + ".json");
            if (!File.Exists(jsonPath))
            {
                var candidates = Directory.GetFiles(directory, "*.json");
                if (candidates.Length == 0)
                {
                    result.Problems.Add($"{id}：缺少版本 JSON");
                    continue;
                }

                jsonPath = candidates[0];
            }

            try
            {
                raw[id] = McJson.ParseObject(File.ReadAllText(jsonPath));
                files[id] = jsonPath;
            }
            catch (Exception ex)
            {
                result.Problems.Add($"{id}：{ex.Message}");
            }
        }

        foreach (var (id, json) in raw)
        {
            try
            {
                result.Versions.Add(Build(id, raw[id], raw, files[id]));
            }
            catch (Exception ex)
            {
                result.Problems.Add($"{id}：{ex.Message}");
            }
        }

        result.Versions.Sort((left, right) =>
        {
            var compare = right.ReleaseTime.CompareTo(left.ReleaseTime);
            return compare != 0 ? compare : string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    public static McVersion? Load(string gameDirectory, string id)
    {
        var directory = Path.Combine(gameDirectory, "versions", id);
        var jsonPath = Path.Combine(directory, id + ".json");
        if (!File.Exists(jsonPath))
        {
            var candidates = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json") : [];
            if (candidates.Length == 0) return null;
            jsonPath = candidates[0];
        }

        var json = McJson.ParseObject(File.ReadAllText(jsonPath));
        var raw = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase) { [id] = json };
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [id] = jsonPath };
        return Build(id, json, raw, jsonPath);
    }

    private static McVersion Build(string id, JsonObject own, Dictionary<string, JsonObject> rawById, string jsonPath)
    {
        var gameDirectory = GetGameDirectoryFromJsonPath(jsonPath);
        var inheritsFrom = own["inheritsFrom"]?.ToString();
        if (string.Equals(inheritsFrom, id, StringComparison.OrdinalIgnoreCase))
            inheritsFrom = null;

        var chain = new List<JsonObject> { own };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { id };
        var parent = inheritsFrom;
        while (!string.IsNullOrWhiteSpace(parent) && seen.Add(parent))
        {
            if (!rawById.TryGetValue(parent, out var parentJson))
                break;
            chain.Add(parentJson);
            parent = parentJson["inheritsFrom"]?.ToString();
        }

        var merged = (JsonObject)chain[^1].DeepClone();
        for (var i = chain.Count - 2; i >= 0; i--)
            McJson.Merge(merged, chain[i]);

        var libraries = merged["libraries"] as JsonArray;
        var loaders = DetectLoaders(libraries, out var flags);
        var vanilla = ResolveVanillaName(id, merged, inheritsFrom, rawById);
        var type = merged["type"]?.ToString() ?? "release";
        var releaseTime = merged.Date("releaseTime", DateTime.MinValue);
        if (releaseTime == DateTime.MinValue && File.Exists(jsonPath))
            releaseTime = File.GetLastWriteTime(jsonPath);

        var jarName = merged["jar"]?.ToString();
        string jarPath;
        if (!string.IsNullOrWhiteSpace(jarName))
        {
            jarPath = Path.Combine(gameDirectory, "versions", jarName, jarName + ".jar");
            if (!File.Exists(jarPath))
            {
                var ownJar = Path.Combine(gameDirectory, "versions", id, id + ".jar");
                if (File.Exists(ownJar)) jarPath = ownJar;
            }
        }
        else
        {
            var top = id;
            var cursor = inheritsFrom;
            var guard = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { id };
            while (!string.IsNullOrWhiteSpace(cursor) && guard.Add(cursor))
            {
                top = cursor;
                cursor = rawById.TryGetValue(cursor, out var parentJson)
                    ? parentJson["inheritsFrom"]?.ToString()
                    : null;
            }

            jarPath = Path.Combine(gameDirectory, "versions", top, top + ".jar");
            if (!File.Exists(jarPath))
            {
                var ownJar = Path.Combine(gameDirectory, "versions", id, id + ".jar");
                if (File.Exists(ownJar)) jarPath = ownJar;
            }
        }

        var assetIndexId = merged.Obj("assetIndex")?.Str("id");
        if (string.IsNullOrWhiteSpace(assetIndexId))
        {
            var assets = merged["assets"]?.ToString();
            if (!string.IsNullOrWhiteSpace(assets) && assets is not ("legacy" or "pre-1.6"))
                assetIndexId = assets;
            else if (assets is "legacy" or "pre-1.6")
                assetIndexId = assets;
        }

        return new McVersion
        {
            Id = id,
            Directory = Path.Combine(gameDirectory, "versions", id),
            Json = merged,
            InheritsFrom = inheritsFrom,
            VanillaName = vanilla,
            Type = type,
            ReleaseTime = releaseTime,
            MainClass = merged["mainClass"]?.ToString() ?? "",
            ClientJarPath = jarPath,
            HasClientJar = File.Exists(jarPath),
            Loaders = string.Join(" · ", loaders),
            AssetIndexId = assetIndexId,
            AssetIndexUrl = merged.Obj("assetIndex")?.Str("url"),
            Assets = merged["assets"]?.ToString(),
            HasForge = flags.HasForge,
            HasNeoForge = flags.HasNeoForge,
            HasFabric = flags.HasFabric,
            HasOptiFine = flags.HasOptiFine,
            HasLiteLoader = flags.HasLiteLoader
        };
    }

    private static string GetGameDirectoryFromJsonPath(string jsonPath)
    {
        var versionDirectory = Path.GetDirectoryName(jsonPath) ?? "";
        var versionsDirectory = Path.GetDirectoryName(versionDirectory) ?? "";
        return Path.GetDirectoryName(versionsDirectory) ?? "";
    }

    private sealed record LoaderFlags(bool HasForge, bool HasNeoForge, bool HasFabric, bool HasOptiFine, bool HasLiteLoader);

    private static List<string> DetectLoaders(JsonArray? libraries, out LoaderFlags flags)
    {
        bool forge = false, neoForge = false, fabric = false, optiFine = false, liteLoader = false;
        string? forgeVersion = null, neoVersion = null, fabricVersion = null, optiVersion = null, liteVersion = null;
        bool quilt = false;
        string? quiltVersion = null;

        foreach (var node in libraries ?? [])
        {
            var name = (node as JsonObject)?["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            var parts = name.Split(':');
            if (parts.Length < 3) continue;
            var groupArtifact = parts[0] + ":" + parts[1];
            switch (groupArtifact)
            {
                case "net.minecraftforge:forge":
                    forge = true;
                    forgeVersion ??= parts[2];
                    break;
                case "net.neoforged:neoforge":
                    neoForge = true;
                    neoVersion ??= parts[2];
                    break;
                case "net.fabricmc:fabric-loader":
                    fabric = true;
                    fabricVersion ??= parts[2];
                    break;
                case "org.quiltmc:quilt-loader":
                    quilt = true;
                    quiltVersion ??= parts[2];
                    fabric = true;
                    fabricVersion ??= parts[2];
                    break;
                case "optifine:OptiFine":
                    optiFine = true;
                    optiVersion ??= parts[2];
                    break;
                case "com.mumfrey:liteloader":
                    liteLoader = true;
                    liteVersion ??= parts[2];
                    break;
            }
        }

        var display = new List<string>();
        if (forge) display.Add("Forge " + (forgeVersion ?? ""));
        if (neoForge) display.Add("NeoForge " + (neoVersion ?? ""));
        if (quilt) display.Add("Quilt " + (quiltVersion ?? ""));
        else if (fabric) display.Add("Fabric " + (fabricVersion ?? ""));
        if (optiFine) display.Add("OptiFine " + (optiVersion ?? ""));
        if (liteLoader) display.Add("LiteLoader " + (liteVersion ?? ""));
        flags = new LoaderFlags(forge, neoForge, fabric, optiFine, liteLoader);
        return display;
    }

    private static string ResolveVanillaName(string id, JsonObject merged, string? inheritsFrom,
        Dictionary<string, JsonObject> rawById)
    {
        var clientVersion = merged["clientVersion"]?.ToString();
        if (!string.IsNullOrWhiteSpace(clientVersion)) return clientVersion;

        if (merged.Arr("arguments", "game") is { } gameArguments)
        {
            for (var i = 0; i < gameArguments.Count - 1; i++)
                if (string.Equals(gameArguments[i]?.ToString(), "--fml.mcVersion", StringComparison.Ordinal))
                    return gameArguments[i + 1]?.ToString() ?? id;
        }

        var jar = merged["jar"]?.ToString();
        if (!string.IsNullOrWhiteSpace(jar) && !string.Equals(jar, id, StringComparison.OrdinalIgnoreCase))
            return jar;

        // 继承版本（Fabric / Quilt / Forge）的 MC 版本以父版本为准；它们的 id 常以加载器版本开头。
        if (!string.IsNullOrWhiteSpace(inheritsFrom))
        {
            if (rawById.TryGetValue(inheritsFrom, out var parent))
                return ResolveVanillaName(inheritsFrom, parent, parent["inheritsFrom"]?.ToString(), rawById);
            return inheritsFrom;
        }

        var match = Regex.Match(id, @"\d+\.\d+(?:\.\d+)?(?:-[a-z0-9]+)?", RegexOptions.IgnoreCase);
        if (match.Success) return match.Value;

        var libraries = merged["libraries"]?.ToString() ?? "";
        match = Regex.Match(libraries, @"net\.minecraft:minecraft:(\d+\.\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value;

        return id;
    }

    /// <summary>版本隔离：开启时工作目录为版本自身文件夹（PCL 默认）。</summary>
    public static string ResolveGameDirectory(McVersion version, LauncherSettings settings) =>
        settings.VersionIsolation ? version.Directory : settings.GameDirectory;

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString(unit == 0 ? "N0" : "N1", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
