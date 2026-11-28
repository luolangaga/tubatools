using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tubatools.MinecraftLauncher.Services;

internal sealed record McLibrary(
    string Name,
    string LocalPath,
    string? Url,
    string? Sha1,
    long Size,
    bool IsNatives)
{
    /// <summary>去掉版本号后的名称，用于去重与识别加载器。</summary>
    public string GroupArtifact
    {
        get
        {
            var parts = Name.Split(':');
            return parts.Length >= 2 ? $"{parts[0]}:{parts[1]}" : Name;
        }
    }

    public string Version
    {
        get
        {
            var parts = Name.Split(':');
            return parts.Length >= 3 ? parts[2] : "";
        }
    }
}

/// <summary>PCL 的 McLibListGet：按 rules 解析支持库、natives 分类器与去重。</summary>
internal static class McLibraryResolver
{
    public static List<McLibrary> Resolve(JsonObject merged, string gameDirectory, McOsInfo os, bool keepSameNameDifferentVersion = false)
    {
        var result = new List<McLibrary>();
        var librariesDirectory = Path.Combine(gameDirectory, "libraries");
        if (merged["libraries"] is not JsonArray libraries) return result;
        var classpathNatives = UsesClasspathNatives(merged);

        foreach (var node in libraries)
        {
            if (node is not JsonObject library) continue;
            if (!McRules.Check(library["rules"], os)) continue;

            var name = library["name"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(name)) continue;

            var rootUrl = library["url"]?.ToString();
            var relativePath = MavenRelativePath(name);
            if (!string.IsNullOrWhiteSpace(rootUrl) && relativePath.Length > 0)
                rootUrl = rootUrl.TrimEnd('/') + "/" + relativePath.Replace('\\', '/');

            if (library["natives"] is not JsonObject natives)
            {
                var nativeClassifier = ClassifierOf(name);
                if (IsNativeClassifier(nativeClassifier))
                {
                    if (classpathNatives)
                    {
                        // 26.x：native jar 本身在 classpath，由 NativeLibrariesBootstrap/LWJGL 自行解压。
                        var classpathEntry = BuildArtifact(library, name, rootUrl, librariesDirectory);
                        if (classpathEntry is not null) result.Add(classpathEntry);
                    }
                    else
                    {
                        var nativeEntry = BuildNativeArtifact(library, name, rootUrl, librariesDirectory);
                        if (nativeEntry is not null) result.Add(nativeEntry);
                    }

                    continue;
                }

                var token = BuildArtifact(library, name, rootUrl, librariesDirectory);
                if (token is not null) result.Add(token);
                continue;
            }

            var classifier = natives["windows"]?.ToString();
            if (string.IsNullOrWhiteSpace(classifier)) continue;
            classifier = classifier.Replace("${arch}", os.Is32Bit ? "32" : "64");
            var nativeToken = BuildNative(library, name, rootUrl, librariesDirectory, classifier);
            if (nativeToken is not null) result.Add(nativeToken);
        }

        return Deduplicate(result, keepSameNameDifferentVersion);
    }

    private static McLibrary? BuildArtifact(JsonObject library, string name, string? rootUrl, string librariesDirectory)
    {
        var artifact = library["downloads"]?["artifact"] as JsonObject;
        var artifactPath = artifact?["path"]?.ToString();
        var localPath = !string.IsNullOrWhiteSpace(artifactPath)
            ? Path.Combine(librariesDirectory, artifactPath.Replace('/', Path.DirectorySeparatorChar))
            : Path.Combine(librariesDirectory, MavenRelativePath(name).Replace('/', Path.DirectorySeparatorChar));

        return new McLibrary(
            name,
            localPath,
            string.IsNullOrWhiteSpace(rootUrl) ? artifact?["url"]?.ToString() : rootUrl,
            artifact?["sha1"]?.ToString(),
            artifact.Long("size", 0),
            false);
    }

    private static McLibrary? BuildNative(JsonObject library, string name, string? rootUrl, string librariesDirectory, string classifier)
    {
        var classifiers = library["downloads"]?["classifiers"] as JsonObject;
        var download = classifiers?[classifier] as JsonObject
                       ?? classifiers?["natives-windows"] as JsonObject;
        var path = download?["path"]?.ToString();

        string localPath;
        if (!string.IsNullOrWhiteSpace(path))
            localPath = Path.Combine(librariesDirectory, path.Replace('/', Path.DirectorySeparatorChar));
        else
        {
            var fallback = MavenRelativePath(name);
            if (fallback.Length == 0) return null;
            fallback = Regex.Replace(fallback, @"\.jar$", $"-{classifier}.jar");
            localPath = Path.Combine(librariesDirectory, fallback.Replace('/', Path.DirectorySeparatorChar));
        }

        return new McLibrary(
            name,
            localPath,
            string.IsNullOrWhiteSpace(rootUrl) ? download?["url"]?.ToString() : rootUrl,
            download?["sha1"]?.ToString(),
            download.Long("size", 0),
            true);
    }

    private static string? ClassifierOf(string name)
    {
        var parts = name.Split(':');
        return parts.Length > 3 ? parts[3] : null;
    }

    private static bool UsesClasspathNatives(JsonObject merged)
    {
        if (merged["arguments"]?["jvm"] is not JsonArray jvm) return false;
        foreach (var node in jvm)
        {
            var value = node is JsonObject obj ? obj["value"]?.ToString() : node?.ToString();
            if (value is not null &&
                (value.Contains("natives_directory}/lwjgl", StringComparison.Ordinal) ||
                 value.Contains("SharedLibraryExtractPath", StringComparison.Ordinal)))
                return true;
        }

        return false;
    }

    private static bool IsNativeClassifier(string? classifier) =>
        !string.IsNullOrWhiteSpace(classifier) &&
        (classifier.StartsWith("natives-", StringComparison.OrdinalIgnoreCase) ||
         Regex.IsMatch(classifier, @"^(windows|linux|macos|osx)(-|$)", RegexOptions.IgnoreCase));

    /// <summary>现代版本把 natives 作为独立 Maven 分类器条目，artifact 指向 native jar。</summary>
    private static McLibrary? BuildNativeArtifact(JsonObject library, string name, string? rootUrl,
        string librariesDirectory)
    {
        var artifact = library["downloads"]?["artifact"] as JsonObject;
        var path = artifact?["path"]?.ToString();
        var localPath = !string.IsNullOrWhiteSpace(path)
            ? Path.Combine(librariesDirectory, path.Replace('/', Path.DirectorySeparatorChar))
            : Path.Combine(librariesDirectory, MavenRelativePath(name).Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(MavenRelativePath(name))) return null;

        return new McLibrary(
            name,
            localPath,
            string.IsNullOrWhiteSpace(rootUrl) ? artifact?["url"]?.ToString() : rootUrl,
            artifact?["sha1"]?.ToString(),
            artifact.Long("size", 0),
            true);
    }

    /// <summary>group:artifact:version[:classifier] → group/artifact/version/artifact-version[-classifier].jar</summary>
    public static string MavenRelativePath(string name)
    {
        var parts = name.Split(':');
        if (parts.Length < 3) return "";
        var group = parts[0];
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? parts[3] : null;
        var file = classifier is null
            ? $"{artifact}-{version}.jar"
            : $"{artifact}-{version}-{classifier}.jar";
        return string.Join('/', group.Split('.').Append(artifact).Append(version).Append(file));
    }

    public static string MavenDirectory(string name)
    {
        var parts = name.Split(':');
        if (parts.Length < 3) return "";
        return string.Join(Path.DirectorySeparatorChar, parts[0].Split('.').Append(parts[1]).Append(parts[2]));
    }

    private static List<McLibrary> Deduplicate(List<McLibrary> tokens, bool keepSameNameDifferentVersion)
    {
        var result = new Dictionary<string, McLibrary>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            var key = token.GroupArtifact + "|" + token.IsNatives + "|" + ClassifierOf(token.Name);
            if (result.TryGetValue(key, out var existing))
            {
                if (keepSameNameDifferentVersion && CompareVersion(token.Version, existing.Version) != 0)
                {
                    result[key + "|" + Guid.NewGuid().ToString("N")] = token;
                    continue;
                }

                if (CompareVersion(token.Version, existing.Version) >= 0)
                    result[key] = token;
            }
            else
            {
                result[key] = token;
            }
        }

        return [.. result.Values];
    }

    /// <summary>近似版本比较：按 . - _ + 分段，数字按数值比较、非数字按字符串比较。</summary>
    public static int CompareVersion(string? left, string? right)
    {
        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.IsNullOrEmpty(left)) return -1;
        if (string.IsNullOrEmpty(right)) return 1;

        var a = Regex.Split(left, @"[.\-_+]");
        var b = Regex.Split(right, @"[.\-_+]");
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : "0";
            var y = i < b.Length ? b[i] : "0";
            if (long.TryParse(x, out var xn) && long.TryParse(y, out var yn))
            {
                if (xn != yn) return xn.CompareTo(yn);
                continue;
            }

            var compare = string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            if (compare != 0) return compare;
        }

        return 0;
    }

    /// <summary>检查支持库是否齐全，返回缺口的描述（最多 5 条）。</summary>
    public static List<string> FindMissing(IEnumerable<McLibrary> libraries)
    {
        var missing = new List<string>();
        foreach (var library in libraries)
        {
            if (File.Exists(library.LocalPath)) continue;
            missing.Add(library.Name);
            if (missing.Count >= 5) break;
        }

        return missing;
    }
}
