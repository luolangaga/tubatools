using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TubaWinUi3.Services;

public sealed record ToolMetadata(
    string? Name,
    string? Description,
    string? Publisher,
    string? Version,
    string? DatabaseSource,
    string? DownloadUrl,
    string? DownloadFilter,
    string? WingetId,
    string? LaunchTarget,
    string? TutorialUrl,
    IReadOnlyList<string>? Tags,
    int? ToolVersion,
    int? Order = null);

public sealed record JsonArchVariantResult(string? File, string? Dir, string? Arch);

public sealed record RemoteToolVersion(string Match, int Version, string? DownloadUrl);

public static class ToolMetadataService
{
    private static IReadOnlyList<JsonToolMetadata>? _metadata;

    public static void InvalidateCache()
    {
        lock (MetadataGate) _metadata = null;
    }

    public static Task RemoveMetadataAsync(string toolPath)
    {
        var entry = FindJsonMetadata(toolPath);
        var directory = entry?.Directory is { Length: > 0 } stored
            ? UserToolLibrary.ResolveDirectory(stored) : Path.GetDirectoryName(toolPath);
        if (directory is not null && !UserToolLibrary.Remove(directory) && entry?.Match is { } match)
            UserToolLibrary.SetStates([("official:" + match, "hidden", JsonValue.Create(true))]);
        InvalidateCache();
        return Task.CompletedTask;
    }

    public static bool HasDownloadUrl(string category, string toolDir)
    {
        var metadata = FindJsonMetadataByDir(toolDir);
        return !string.IsNullOrWhiteSpace(metadata?.DownloadUrl) || !string.IsNullOrWhiteSpace(metadata?.WingetId);
    }

    public static ToolMetadata GetMetadata(string category, string toolPath)
    {
        var jsonMetadata = FindJsonMetadata(toolPath);

        string? description = jsonMetadata?.Description;
        string? publisher = jsonMetadata?.Publisher;
        string? version = null;

        if (File.Exists(toolPath))
        {
            try
            {
                var ext = Path.GetExtension(toolPath);
                if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(toolPath);
                    description ??= FirstUseful(versionInfo.FileDescription, versionInfo.ProductName);
                    publisher ??= FirstUseful(versionInfo.CompanyName, versionInfo.LegalCopyright);
                    version = FirstUseful(versionInfo.ProductVersion, versionInfo.FileVersion);
                }
            }
            catch { }
        }

        if (description is null)
        {
            description = ReadFolderDescription(toolPath);
        }

        return new ToolMetadata(
            jsonMetadata?.Name,
            description,
            publisher,
            version,
            jsonMetadata is null ? null : "JSON",
            jsonMetadata?.DownloadUrl,
            jsonMetadata?.DownloadFilter,
            jsonMetadata?.WingetId,
            jsonMetadata?.LaunchTarget,
            jsonMetadata?.TutorialUrl,
            jsonMetadata?.Tags,
            jsonMetadata?.ToolVersion,
            jsonMetadata?.Order);
    }

    public static IReadOnlyList<JsonArchVariantResult> GetArchVariants(string toolPath, string? toolDir = null)
    {
        var jsonMetadata = FindJsonMetadata(toolPath);
        if (jsonMetadata is null && toolDir is not null)
            jsonMetadata = FindJsonMetadataByDir(toolDir);

        if (jsonMetadata?.ArchVariants is null || jsonMetadata.ArchVariants.Count == 0)
            return [];

        return jsonMetadata.ArchVariants
            .Select(v => new JsonArchVariantResult(v.File, v.Dir, v.Arch))
            .ToList();
    }

    internal static JsonToolMetadata? FindJsonMetadata(string toolPath)
    {
        var metadata = LoadMetadata();
        var fileName = Path.GetFileNameWithoutExtension(toolPath);
        var relativePath = Path.GetRelativePath(ToolCatalog.ToolsRoot, toolPath);
        var dirName = Path.GetFileName(Path.GetDirectoryName(toolPath));

        var registered = metadata.FirstOrDefault(item => item.Directory is { Length: > 0 } stored &&
            UserToolLibrary.IsWithin(toolPath, UserToolLibrary.ResolveDirectory(stored)));
        if (registered is not null) return registered;
        if (UserToolLibrary.IsWithin(toolPath, UserToolLibrary.ToolsRoot)) return null;
        return metadata
            .Where(item => item.Directory is null &&
                !string.IsNullOrWhiteSpace(item.Match) &&
                (fileName.Contains(item.Match, StringComparison.CurrentCultureIgnoreCase) ||
                 relativePath.Contains(item.Match, StringComparison.CurrentCultureIgnoreCase) ||
                 MatchesFlexible(dirName, item.Match)))
            .OrderByDescending(item => item.Match!.Length)
            .FirstOrDefault();
    }

    public static string? GetLaunchTarget(string toolDir)
    {
        var jsonMetadata = FindJsonMetadataByDir(toolDir);
        return jsonMetadata?.LaunchTarget;
    }

    public static int? GetToolVersion(string toolPath)
    {
        var jsonMetadata = FindJsonMetadata(toolPath);
        return jsonMetadata?.ToolVersion;
    }

    public static int? GetToolVersionByDir(string toolDir)
    {
        var jsonMetadata = FindJsonMetadataByDir(toolDir);
        return jsonMetadata?.ToolVersion;
    }

    public static string? GetDownloadUrlByDir(string toolDir)
    {
        var jsonMetadata = FindJsonMetadataByDir(toolDir);
        return jsonMetadata?.DownloadUrl;
    }

    public static void UpdateToolVersion(string match, int newVersion)
    {
        UserToolLibrary.SetStates([("official:" + match, "version", JsonValue.Create(newVersion))]);
        InvalidateCache();
    }

    /// <summary>排序只写用户状态，发布的工具定义始终只读。</summary>
    public static void SaveToolOrder(IReadOnlyList<string> orderedToolDirs)
    {
        var changes = new List<(string Id, string Field, JsonNode? Value)>();
        var order = 0;
        foreach (var directory in orderedToolDirs)
        {
            var entry = FindJsonMetadataByDir(directory);
            if (entry?.Match is null) continue;
            changes.Add((entry.Id ?? "official:" + entry.Match, "order", JsonValue.Create(order++)));
        }
        if (changes.Count > 0) UserToolLibrary.SetStates(changes);
        InvalidateCache();
    }

    /// <summary>
    /// 写入/更新用户工具库中的一个工具条目（按真实目录定位，保留未知字段）。
    /// 社区工具安装与自定义工具导入共用；写入失败抛 IOException（调用方决定如何呈现）。
    /// 注意：不要写 "version" 字段（int，驱动远端工具库版本比较），社区工具的字符串版本号
    /// 记录在 CommunityToolRegistry，不混进这里。
    /// </summary>
    internal static void UpsertToolMetadataEntry(
        string match,
        string? name = null,
        string? description = null,
        string? publisher = null,
        IReadOnlyList<string>? tags = null,
        string? launchTarget = null,
        IReadOnlyList<JsonArchVariant>? archVariants = null,
        string? toolDirectory = null,
        string? category = null,
        string? communityId = null)
    {
        if (string.IsNullOrWhiteSpace(match))
            throw new ArgumentException("match 不能为空", nameof(match));

        // 先迁移旧库，再登记新工具；旧路径保留以保护已有快捷方式。
        _ = LoadMetadata();
        toolDirectory ??= Directory.Exists(ToolCatalog.ToolsRoot)
            ? Directory.GetDirectories(ToolCatalog.ToolsRoot)
                .SelectMany(Directory.GetDirectories)
                .FirstOrDefault(d => Path.GetFileName(d).Equals(match.Trim(), StringComparison.OrdinalIgnoreCase))
            : null;
        if (toolDirectory is null) throw new IOException("找不到工具目录，无法登记。");
        category ??= Path.GetFileName(Path.GetDirectoryName(toolDirectory));
        var entry = new JsonObject { ["match"] = match.Trim() };

        if (!string.IsNullOrWhiteSpace(name)) entry["name"] = name.Trim();
        if (!string.IsNullOrWhiteSpace(description)) entry["description"] = description.Trim();
        if (!string.IsNullOrWhiteSpace(publisher)) entry["publisher"] = publisher.Trim();
        if (!string.IsNullOrWhiteSpace(launchTarget)) entry["launchTarget"] = launchTarget.Trim();

        if (tags is { Count: > 0 })
        {
            var tagArray = new JsonArray(tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => JsonValue.Create(tag.Trim()))
                .ToArray<JsonNode?>());
            if (tagArray.Count > 0) entry["tags"] = tagArray;
        }

        if (archVariants is { Count: > 0 })
        {
            var variantArray = new JsonArray(archVariants
                .Where(v => !string.IsNullOrWhiteSpace(v.File) || !string.IsNullOrWhiteSpace(v.Dir))
                .Select(v =>
                {
                    var item = new JsonObject();
                    if (!string.IsNullOrWhiteSpace(v.File)) item["file"] = v.File!.Trim();
                    if (!string.IsNullOrWhiteSpace(v.Dir)) item["dir"] = v.Dir!.Trim();
                    if (!string.IsNullOrWhiteSpace(v.Arch)) item["arch"] = v.Arch!.Trim();
                    return (JsonNode?)item;
                })
                .ToArray());
            if (variantArray.Count > 0) entry["archVariants"] = variantArray;
        }

        entry["category"] = category;
        UserToolLibrary.Upsert(entry, toolDirectory, communityId);
        InvalidateCache();
    }

    public static async Task<IReadOnlyList<RemoteToolVersion>?> FetchRemoteToolsJsonAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await TryFetchGitCodeToolsJsonAsync(ct);
            if (result is not null) return result;
        }
        catch { }

        try
        {
            var result = await TryFetchGitHubToolsJsonAsync(ct);
            if (result is not null) return result;
        }
        catch { }

        return null;
    }

    private static async Task<IReadOnlyList<RemoteToolVersion>?> TryFetchGitCodeToolsJsonAsync(CancellationToken ct)
    {
        const string url = "https://raw.gitcode.com/luolangaga/tubatool/raw/master/TubaWinUi3.WinUI3/Metadata/tools.json";

        using var client = ProxyService.CreateClient(TimeSpan.FromSeconds(15));
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolUpdate");

        var toolsJsonText = await client.GetStringAsync(url, ct);
        return ParseRemoteToolsJson(toolsJsonText);
    }

    private static async Task<IReadOnlyList<RemoteToolVersion>?> TryFetchGitHubToolsJsonAsync(CancellationToken ct)
    {
        const string url = "https://raw.githubusercontent.com/luolangaga/tubatool/master/TubaWinUi3.WinUI3/Metadata/tools.json";

        using var client = ProxyService.CreateClient(TimeSpan.FromSeconds(15));
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolUpdate");

        var toolsJsonText = await client.GetStringAsync(url, ct);
        return ParseRemoteToolsJson(toolsJsonText);
    }

    private static IReadOnlyList<RemoteToolVersion>? ParseRemoteToolsJson(string jsonText)
    {
        try
        {
            var database = JsonSerializer.Deserialize<JsonToolDatabase>(jsonText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (database?.Tools is null) return null;

            return database.Tools
                .Where(t => !string.IsNullOrWhiteSpace(t.Match) && t.ToolVersion.HasValue)
                .Select(t => new RemoteToolVersion(t.Match!, t.ToolVersion!.Value, t.DownloadUrl))
                .ToList();
        }
        catch { return null; }
    }

    internal static JsonToolMetadata? FindJsonMetadataByDir(string toolDir)
    {
        var metadata = LoadMetadata();
        var dirName = Path.GetFileName(toolDir);
        var relativePath = Path.GetRelativePath(ToolCatalog.ToolsRoot, toolDir);

        var registered = metadata.FirstOrDefault(item => item.Directory is { Length: > 0 } stored &&
            Path.GetFullPath(UserToolLibrary.ResolveDirectory(stored)).Equals(Path.GetFullPath(toolDir), StringComparison.OrdinalIgnoreCase));
        if (registered is not null) return registered;
        if (UserToolLibrary.IsWithin(toolDir, UserToolLibrary.ToolsRoot)) return null;
        return metadata
            .Where(item => item.Directory is null &&
                !string.IsNullOrWhiteSpace(item.Match) &&
                (relativePath.Contains(item.Match, StringComparison.CurrentCultureIgnoreCase) ||
                 MatchesFlexible(dirName, item.Match)))
            .OrderByDescending(item => item.Match!.Length)
            .FirstOrDefault();
    }

    /// <summary>目录名/路径与 tools.json match 字段的灵活匹配规则（去空格-下划线-连字符后子串匹配），供目录定位复用。</summary>
    public static bool MatchesFlexible(string? source, string match)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        if (source.Contains(match, StringComparison.CurrentCultureIgnoreCase))
            return true;

        var normalizedSource = source.Replace(" ", "", StringComparison.Ordinal)
                                      .Replace("-", "", StringComparison.Ordinal)
                                      .Replace("_", "", StringComparison.Ordinal);
        var normalizedMatch = match.Replace(" ", "", StringComparison.Ordinal)
                                   .Replace("-", "", StringComparison.Ordinal)
                                   .Replace("_", "", StringComparison.Ordinal);

        return normalizedSource.Contains(normalizedMatch, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// 多分类副本/内置挂载声明：tools.json 条目的 categories 含指定分类时返回该条目。
    /// 真实工具副本用 category(主分类)+categories(副本分类)；内置挂载用 builtin+categories(挂载位置)。
    /// </summary>
    public static IReadOnlyList<CategoryPlacement> GetCategoryPlacements(string category)
    {
        try
        {
            return LoadMetadata()
                .Where(item => item.Categories is not null &&
                               item.Categories.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase)))
                .Select(item => new CategoryPlacement(
                    item.Match ?? "",
                    item.Category,
                    item.Categories!.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    string.IsNullOrWhiteSpace(item.Builtin) ? null : item.Builtin,
                    item.Order))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public sealed record CategoryPlacement(
        string Match,
        string? PrimaryCategory,
        IReadOnlyList<string> Categories,
        string? BuiltinId,
        int? Order);

    private static readonly object MetadataGate = new();
    private static string? _loadedDataDirectory;

    internal static IReadOnlyList<JsonToolMetadata> GetUserTools(string? category = null) =>
        LoadMetadata().Where(t => t.Directory is not null &&
            (category is null || string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase))).ToList();

    private static IReadOnlyList<JsonToolMetadata> LoadMetadata()
    {
        lock (MetadataGate)
        {
            if (_metadata is not null && _loadedDataDirectory == UserToolLibrary.DataDirectory) return _metadata;
            var metadataDirectory = FindRoot("Metadata");
            var baseline = Path.Combine(metadataDirectory, "tools.default.json");
            var current = Path.Combine(metadataDirectory, "tools.json");
            var official = UserToolLibrary.ReadObject(File.Exists(baseline) ? baseline : current);
            var legacy = new List<string> { Path.Combine(UserToolLibrary.DataDirectory, "Metadata", "tools.json") };
            if (File.Exists(baseline)) legacy.Add(current);
            if (RuntimeHelper.IsPackagedContext)
                legacy.Add(Path.Combine(RuntimeHelper.GetLocalAppDataRoot(), "TubaWinUi3", "Metadata", "tools.json"));
            var backups = Path.Combine(ToolCatalog.AppDirectory, "Data", "LegacyToolMetadata");
            if (Directory.Exists(backups)) legacy.AddRange(Directory.GetFiles(backups, "*.json").OrderBy(File.GetLastWriteTimeUtc));
            var migratedBackups = Path.Combine(UserToolLibrary.DataDirectory, "LegacyToolMetadata");
            if (Directory.Exists(migratedBackups)) legacy.AddRange(Directory.GetFiles(migratedBackups, "*.json").OrderBy(File.GetLastWriteTimeUtc));
            try
            {
                var merged = UserToolLibrary.Load(official, legacy);
                // 即使旧 tools.json 已被覆盖，社区安装记录仍能恢复启动目标与分类。
                foreach (var record in CommunityToolRegistry.GetInstalledRecords())
                {
                    if (merged.Any(t => t["directory"] is not null &&
                        (t["source"]?.GetValue<string>() is "community" or "legacy") &&
                        string.Equals(t["match"]?.GetValue<string>(), record.ToolId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(t["category"]?.GetValue<string>(), record.Category, StringComparison.OrdinalIgnoreCase))) continue;
                    var directory = Path.Combine(ToolCatalog.ToolsRoot, record.Category, record.ToolId);
                    if (!System.IO.Directory.Exists(directory)) continue;
                    UserToolLibrary.Upsert(new JsonObject
                    {
                        ["match"] = record.ToolId, ["name"] = record.ToolId,
                        ["category"] = record.Category, ["launchTarget"] = record.LaunchTarget,
                        ["publisher"] = record.Author
                    }, directory, record.ToolId);
                }
                _metadata = UserToolLibrary.Load(official, legacy)
                    .Select(t => t.Deserialize<JsonToolMetadata>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!)
                    .ToList();
            }
            catch (Exception ex)
            {
                // 用户库损坏不覆盖原文件、不阻断官方工具；刷新时仍会重试。
                Debug.WriteLine($"[ToolMetadata] 用户工具库读取失败，原文件保留: {ex.Message}");
                return official["tools"]?.Deserialize<List<JsonToolMetadata>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            }
            _loadedDataDirectory = UserToolLibrary.DataDirectory;
            ApplyLanguageOverlay(metadataDirectory);
            return _metadata;
        }
    }

    /// <summary>
    /// 语言覆盖：Tools_&lt;语言&gt;.json 按 match 合并显示字段（description / publisher / tags）。
    /// 逻辑字段（order / category / categories / downloadUrl 等）始终以 tools.json 为唯一来源；
    /// 覆盖文件缺失、条目或字段缺省时回退中文原文。
    /// </summary>
    private static void ApplyLanguageOverlay(string metadataDir)
    {
        try
        {
            string overlayPath = Path.Combine(metadataDir, $"Tools_{LocalizationService.CurrentLanguage}.json");
            if (!File.Exists(overlayPath))
            {
                return;
            }

            using var stream = File.OpenRead(overlayPath);
            var overlay = JsonSerializer.Deserialize<JsonToolDatabase>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (overlay?.Tools is not { Count: > 0 })
            {
                return;
            }

            foreach (var over in overlay.Tools)
            {
                if (string.IsNullOrWhiteSpace(over.Match))
                {
                    continue;
                }

                var target = _metadata!.FirstOrDefault(t => t.Directory is null &&
                    string.Equals(t.Match, over.Match, StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(over.Description))
                {
                    target.Description = over.Description;
                }

                if (!string.IsNullOrWhiteSpace(over.Publisher))
                {
                    target.Publisher = over.Publisher;
                }

                if (over.Tags is { Count: > 0 })
                {
                    target.Tags = over.Tags;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ToolMetadata] 语言覆盖加载失败，保持中文元数据: {ex.Message}");
        }
    }

    private static string? ReadFolderDescription(string toolPath)
    {
        var directory = Path.GetDirectoryName(toolPath);
        if (directory is null)
        {
            return null;
        }

        var textFile = Directory.EnumerateFiles(directory, "*.txt", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => Path.GetFileName(path).Contains("readme", StringComparison.OrdinalIgnoreCase) ||
                                    Path.GetFileName(path).Contains("说明", StringComparison.CurrentCultureIgnoreCase) ||
                                    Path.GetFileName(path).Contains("What's New", StringComparison.OrdinalIgnoreCase));
        if (textFile is null)
        {
            return null;
        }

        try
        {
            var text = File.ReadLines(textFile).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
            return text is { Length: > 160 } ? text[..160] : text;
        }
        catch
        {
            return null;
        }
    }

    private static string? FirstUseful(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    /// <summary>仅供测试使用：直接替代 FindRoot 的返回值（即 Metadata 目录本身，null = 恢复自动查找）。</summary>
    internal static string? MetadataRootOverride;

    internal static void SetMetadataRootForTests(string? metadataDir)
    {
        MetadataRootOverride = metadataDir;
        _metadata = null;
    }

    private static string FindRoot(string folderName)
    {
        if (MetadataRootOverride is not null)
            return MetadataRootOverride;

        var appDir = ToolCatalog.AppDirectory;
        var outputRoot = Path.Combine(appDir, folderName);
        if (Directory.Exists(outputRoot))
        {
            return outputRoot;
        }

        var directory = new DirectoryInfo(appDir);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, folderName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return outputRoot;
    }

    /// <summary>用户数据目录。官方 Metadata 从 FindRoot 读取，永不作为运行时写入目标。</summary>
    public static string GetWritableMetadataDir() => UserToolLibrary.DataDirectory;

    private sealed class JsonToolDatabase
    {
        public List<JsonToolMetadata> Tools { get; set; } = [];
    }

    internal sealed class JsonToolMetadata
    {
        public string? Match { get; set; }
        public string? Id { get; set; }
        public string? Directory { get; set; }
        public string? Source { get; set; }

        /// <summary>显示名覆盖：卡片/搜索按此取名（为空时沿用目录/文件名）。社区工具与自定义工具写入。</summary>
        public string? Name { get; set; }

        public string? Description { get; set; }
        public string? Publisher { get; set; }
        public string? DownloadUrl { get; set; }
        public string? DownloadFilter { get; set; }
        public string? WingetId { get; set; }
        public string? LaunchTarget { get; set; }
        public string? TutorialUrl { get; set; }
        public List<string>? Tags { get; set; }
        public List<JsonArchVariant>? ArchVariants { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("version")]
        public int? ToolVersion { get; set; }
        public int? Order { get; set; }

        /// <summary>主分类：物理目录所在的分类（多分类副本条目声明用）。</summary>
        public string? Category { get; set; }

        /// <summary>副本分类：工具额外出现的分类列表；内置挂载条目 = 挂载位置列表。</summary>
        public List<string>? Categories { get; set; }

        /// <summary>内置工具挂载：BuiltinToolRegistry 的工具 id（替代旧 link.json 的 builtin 链接）。</summary>
        public string? Builtin { get; set; }
    }

    internal sealed class JsonArchVariant
    {
        public string? File { get; set; }

        public string? Dir { get; set; }

        public string? Arch { get; set; }
    }
}
