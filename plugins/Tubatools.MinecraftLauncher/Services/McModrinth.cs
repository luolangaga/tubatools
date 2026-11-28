using System.Globalization;
using System.Text.Json.Nodes;

namespace Tubatools.MinecraftLauncher.Services;

internal sealed record McResourceHit(
    string ProjectId,
    string Slug,
    string Title,
    string Description,
    string? IconUrl,
    int Downloads,
    IReadOnlyList<string> Categories);

internal sealed record McResourceSearchResult(IReadOnlyList<McResourceHit> Hits, int TotalHits);

internal sealed record McResourceFile(
    string Url,
    string FileName,
    string? Sha1,
    long Size,
    string VersionNumber,
    string VersionType,
    IReadOnlyList<string> RequiredProjectIds);

/// <summary>Modrinth API：搜索模组 / 光影 / 资源包，解析兼容文件并一键安装。</summary>
internal static class McModrinth
{
    private const string Base = "https://api.modrinth.com/v2";

    public static async Task<McResourceSearchResult> SearchAsync(HttpClient http, string projectType,
        string query, string? gameVersion, string? loader, string index, int offset, int limit,
        CancellationToken ct)
    {
        var facets = new List<string> { $"""["project_type:{projectType}"]""" };
        if (!string.IsNullOrWhiteSpace(gameVersion))
            facets.Add($"""["versions:{gameVersion}"]""");
        if (!string.IsNullOrWhiteSpace(loader))
            facets.Add($"""["categories:{loader}"]""");
        var facetsJson = "[" + string.Join(",", facets) + "]";
        var url = $"{Base}/search?query={Uri.EscapeDataString(query)}" +
                  $"&facets={Uri.EscapeDataString(facetsJson)}" +
                  $"&index={Uri.EscapeDataString(index)}&offset={offset}&limit={limit}";
        var json = await McHttp.GetStringAsync(http, url, TimeSpan.FromSeconds(30), ct);
        var root = McJson.ParseObject(json);
        var hits = new List<McResourceHit>();
        if (root["hits"] is JsonArray array)
            foreach (var node in array)
            {
                var projectId = node.Str("project_id");
                var title = node.Str("title");
                if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(title)) continue;
                var categories = (node["categories"] as JsonArray)?
                    .Select(item => item?.ToString() ?? "")
                    .Where(item => item.Length > 0)
                    .ToArray() ?? [];
                hits.Add(new McResourceHit(
                    projectId,
                    node.Str("slug") ?? projectId,
                    title,
                    node.Str("description") ?? "",
                    node.Str("icon_url"),
                    node.Int("downloads", 0),
                    categories));
            }

        var total = root.Int("total_hits", hits.Count);
        return new McResourceSearchResult(hits, total);
    }

    public static async Task<McResourceFile?> ResolveFileAsync(HttpClient http, string projectId,
        string projectType, string? gameVersion, string? loader, CancellationToken ct)
    {
        var arguments = new List<string>();
        if (!string.IsNullOrWhiteSpace(gameVersion))
            arguments.Add($"game_versions={Uri.EscapeDataString($"[\"{gameVersion}\"]")}");
        if (projectType == "mod" && !string.IsNullOrWhiteSpace(loader))
            arguments.Add($"loaders={Uri.EscapeDataString($"[\"{loader}\"]")}");
        var url = $"{Base}/project/{Uri.EscapeDataString(projectId)}/version" +
                  (arguments.Count > 0 ? "?" + string.Join("&", arguments) : "");
        var json = await McHttp.GetStringAsync(http, url, TimeSpan.FromSeconds(30), ct);
        if (JsonNode.Parse(json) is not JsonArray versions) return null;

        McResourceFile? fallback = null;
        foreach (var version in versions)
        {
            if (version is not JsonObject item) continue;
            var files = item["files"] as JsonArray;
            if (files is null) continue;
            var primary = files.OfType<JsonObject>().FirstOrDefault(file => file.Bool("primary"))
                          ?? files.OfType<JsonObject>().FirstOrDefault();
            if (primary is null) continue;
            var fileUrl = primary.Str("url");
            var fileName = primary.Str("filename");
            if (string.IsNullOrWhiteSpace(fileUrl) || string.IsNullOrWhiteSpace(fileName)) continue;
            var required = new List<string>();
            if (item["dependencies"] is JsonArray dependencies)
                foreach (var dependency in dependencies)
                {
                    if (!string.Equals(dependency?["dependency_type"]?.ToString(), "required",
                            StringComparison.OrdinalIgnoreCase)) continue;
                    var dependencyProjectId = dependency?["project_id"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(dependencyProjectId)) required.Add(dependencyProjectId);
                }

            var resource = new McResourceFile(
                fileUrl,
                fileName,
                primary.Obj("hashes")?.Str("sha1"),
                primary.Long("size", -1),
                item.Str("version_number") ?? "",
                item.Str("version_type") ?? "release",
                required);
            if (string.Equals(resource.VersionType, "release", StringComparison.OrdinalIgnoreCase))
                return resource;
            fallback ??= resource;
        }

        return fallback;
    }

    public static string FormatDownloads(int downloads) => downloads switch
    {
        >= 100_000_000 => (downloads / 100_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "亿",
        >= 10_000 => (downloads / 10_000d).ToString("0.#", CultureInfo.InvariantCulture) + "万",
        _ => downloads.ToString("N0", CultureInfo.InvariantCulture)
    };
}
