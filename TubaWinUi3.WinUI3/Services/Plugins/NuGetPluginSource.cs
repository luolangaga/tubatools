using System.Net.Http;
using System.Text.Json;

namespace TubaWinUi3.Services.Plugins;

public static class NuGetPluginSource
{
    public const string IndexUrl = "https://api.nuget.org/v3/index.json";
    public const string UploadUrl = "https://www.nuget.org/packages/manage/upload";
    private static (string Search, string Content)? _endpoints;

    private static async Task<(string Search, string Content)> EndpointsAsync(CancellationToken ct)
    {
        if (_endpoints is { } cached) return cached;
        using var http = ProxyService.CreateClient(TimeSpan.FromSeconds(30));
        using var json = JsonDocument.Parse(await http.GetStringAsync(IndexUrl, ct));
        var resources = json.RootElement.GetProperty("resources").EnumerateArray().ToArray();
        string Find(string type) => resources.First(r => r.GetProperty("@type").GetString()!.StartsWith(type, StringComparison.Ordinal))
            .GetProperty("@id").GetString()!;
        return (_endpoints = (Find("SearchQueryService"), Find("PackageBaseAddress/"))).Value;
    }

    public static async Task<(IReadOnlyList<PluginPackage> Packages, bool HasMore)> SearchAsync(string query, int page, CancellationToken ct)
    {
        var endpoints = await EndpointsAsync(ct);
        var url = $"{endpoints.Search}?q={Uri.EscapeDataString("tags:Tubatools " + query)}&skip={page * 30}&take=30&prerelease=false&semVerLevel=2.0.0";
        using var http = ProxyService.CreateClient(TimeSpan.FromSeconds(30));
        var json = await http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);
        return (ParseSearch(json), (long)(page + 1) * 30 < doc.RootElement.GetProperty("totalHits").GetInt64());
    }

    internal static IReadOnlyList<PluginPackage> ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").EnumerateArray()
            .Where(p => p.TryGetProperty("tags", out var tags) && tags.EnumerateArray()
                .Any(t => string.Equals(t.GetString(), "Tubatools", StringComparison.OrdinalIgnoreCase)))
            .Select(p => new PluginPackage(p.GetProperty("id").GetString()!, p.GetProperty("version").GetString()!,
                Text(p, "description"),
                p.TryGetProperty("authors", out var a) ? JoinAuthors(a) : "",
                p.TryGetProperty("totalDownloads", out var n) && n.TryGetInt64(out var downloads) ? downloads : 0,
                Text(p, "iconUrl"))).ToList();
    }

    /// <summary>NuGet 包详情页：插件列表/安装弹窗的“主页”统一指向发行页面。</summary>
    public static Uri PackagePage(string id, string version)
    {
        var versionSegment = string.IsNullOrWhiteSpace(version) ? "" : "/" + Uri.EscapeDataString(version);
        return new Uri($"https://www.nuget.org/packages/{Uri.EscapeDataString(id)}{versionSegment}");
    }

    private static string Text(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Null or JsonValueKind.Undefined => "",
            _ => value.ToString()
        };
    }

    private static string JoinAuthors(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
            return string.Join("、", value.EnumerateArray()
                .Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
                .Where(v => !string.IsNullOrWhiteSpace(v)));
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }

    internal static async Task DownloadAsync(PluginPackage package, string destination, CancellationToken ct)
    {
        var endpoints = await EndpointsAsync(ct);
        var id = PluginPackageRules.Segment(package.Id);
        var version = PluginPackageRules.Segment(package.Version);
        using var http = ProxyService.CreateClient(TimeSpan.FromMinutes(3));
        using var response = await http.GetAsync($"{endpoints.Content.TrimEnd('/')}/{id}/{version}/{id}.{version}.nupkg", HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 100 * 1024 * 1024) throw new IOException("插件包超过 100 MB。");
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(destination);
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += count;
            if (total > 100 * 1024 * 1024) throw new IOException("插件包超过 100 MB。");
            await target.WriteAsync(buffer.AsMemory(0, count), ct);
        }
    }
}
