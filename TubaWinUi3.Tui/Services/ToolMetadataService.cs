using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using TubaWinUi3.Tui.Models;

namespace TubaWinUi3.Tui.Services
{
    public sealed class ToolMetadata
    {
        public string Name { get; set; }
        public int? Order { get; set; }
        public string Description { get; set; }
        public string Publisher { get; set; }
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string WingetId { get; set; }
        public IReadOnlyList<string> Tags { get; set; }
    }

    public sealed class JsonArchVariantResult
    {
        public string File { get; set; }
        public string Dir { get; set; }
        public string Arch { get; set; }
    }

    public static class ToolMetadataService
    {
        private static IReadOnlyList<JsonToolMetadata> _metadata;

        public static void InvalidateCache()
        {
            _metadata = null;
        }

        public static bool HasDownloadUrl(string toolDir)
        {
            var dirName = Path.GetFileName(toolDir);
            var metadata = LoadMetadata();

            foreach (var item in metadata)
            {
                if (!string.IsNullOrWhiteSpace(item.Match) &&
                    (!string.IsNullOrWhiteSpace(item.DownloadUrl) || !string.IsNullOrWhiteSpace(item.WingetId)) &&
                    dirName.IndexOf(item.Match, StringComparison.CurrentCultureIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        public static ToolMetadata GetMetadata(string toolPath)
        {
            FileVersionInfo versionInfo = null;
            try
            {
                if (File.Exists(toolPath))
                    versionInfo = FileVersionInfo.GetVersionInfo(toolPath);
            }
            catch { }

            var jsonMetadata = FindJsonMetadata(toolPath);
            var description = FirstUseful(
                jsonMetadata != null ? jsonMetadata.Description : null,
                versionInfo != null ? versionInfo.FileDescription : null,
                versionInfo != null ? versionInfo.ProductName : null,
                ReadFolderDescription(toolPath));

            return new ToolMetadata
            {
                Name = jsonMetadata != null ? jsonMetadata.Name : null,
                Order = jsonMetadata != null ? jsonMetadata.Order : null,
                Description = description,
                Publisher = FirstUseful(
                    jsonMetadata != null ? jsonMetadata.Publisher : null,
                    versionInfo != null ? versionInfo.CompanyName : null,
                    versionInfo != null ? versionInfo.LegalCopyright : null),
                Version = FirstUseful(
                    versionInfo != null ? versionInfo.ProductVersion : null,
                    versionInfo != null ? versionInfo.FileVersion : null),
                DownloadUrl = jsonMetadata != null ? jsonMetadata.DownloadUrl : null,
                WingetId = jsonMetadata != null ? jsonMetadata.WingetId : null,
                Tags = jsonMetadata != null ? jsonMetadata.Tags : null
            };
        }

        public static IReadOnlyList<JsonArchVariantResult> GetArchVariants(string toolPath, string toolDir = null)
        {
            var jsonMetadata = FindJsonMetadata(toolPath);
            if (jsonMetadata == null && toolDir != null)
                jsonMetadata = FindJsonMetadataByDir(toolDir);

            if (jsonMetadata == null || jsonMetadata.ArchVariants == null || jsonMetadata.ArchVariants.Count == 0)
                return new List<JsonArchVariantResult>();

            var result = new List<JsonArchVariantResult>();
            foreach (var v in jsonMetadata.ArchVariants)
            {
                result.Add(new JsonArchVariantResult { File = v.File, Dir = v.Dir, Arch = v.Arch });
            }
            return result;
        }

        public static string GetLaunchTarget(string toolDir)
        {
            var jsonMetadata = FindJsonMetadataByDir(toolDir);
            return jsonMetadata != null ? jsonMetadata.LaunchTarget : null;
        }

        private static JsonToolMetadata FindJsonMetadata(string toolPath)
        {
            var metadata = LoadMetadata();
            var registered = metadata.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Directory) && Path.GetFullPath(toolPath).StartsWith(Path.GetFullPath(ResolveUserDirectory(t.Directory)).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
            if (registered != null) return registered;

            var fileName = Path.GetFileNameWithoutExtension(toolPath);
            var relativePath = Path.GetRelativePath(ToolCatalog.ToolsRoot, toolPath);
            var dirName = Path.GetFileName(Path.GetDirectoryName(toolPath));

            JsonToolMetadata best = null;
            int bestLen = 0;

            foreach (var item in metadata)
            {
                if (!string.IsNullOrWhiteSpace(item.Directory) || string.IsNullOrWhiteSpace(item.Match)) continue;
                if (fileName.IndexOf(item.Match, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    relativePath.IndexOf(item.Match, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    MatchesFlexible(dirName, item.Match))
                {
                    if (item.Match.Length > bestLen)
                    {
                        best = item;
                        bestLen = item.Match.Length;
                    }
                }
            }
            return best;
        }

        internal static bool DirectoryBelongsToMatch(string toolDir, string match)
        {
            var metadata = FindJsonMetadataByDir(toolDir);
            return metadata != null && string.IsNullOrWhiteSpace(metadata.Directory) &&
                string.Equals(metadata.Match, match, StringComparison.OrdinalIgnoreCase);
        }

        private static JsonToolMetadata FindJsonMetadataByDir(string toolDir)
        {
            var metadata = LoadMetadata();
            var registered = metadata.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Directory) && Path.GetFullPath(toolDir).Equals(Path.GetFullPath(ResolveUserDirectory(t.Directory)), StringComparison.OrdinalIgnoreCase));
            if (registered != null) return registered;

            var dirName = Path.GetFileName(toolDir);
            var relativePath = Path.GetRelativePath(ToolCatalog.ToolsRoot, toolDir);

            JsonToolMetadata best = null;
            int bestLen = 0;

            foreach (var item in metadata)
            {
                if (!string.IsNullOrWhiteSpace(item.Directory) || string.IsNullOrWhiteSpace(item.Match)) continue;
                if (relativePath.IndexOf(item.Match, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    MatchesFlexible(dirName, item.Match))
                {
                    if (item.Match.Length > bestLen)
                    {
                        best = item;
                        bestLen = item.Match.Length;
                    }
                }
            }
            return best;
        }

        /// <summary>目录名/路径与 tools.json match 字段的灵活匹配规则（去空格-下划线-连字符后子串匹配）。</summary>
        public static bool MatchesFlexible(string source, string match)
        {
            if (string.IsNullOrWhiteSpace(source))
                return false;

            if (source.IndexOf(match, StringComparison.CurrentCultureIgnoreCase) >= 0)
                return true;

            var normalizedSource = source.Replace(" ", "").Replace("-", "").Replace("_", "");
            var normalizedMatch = match.Replace(" ", "").Replace("-", "").Replace("_", "");

            return normalizedSource.IndexOf(normalizedMatch, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        /// <summary>
        /// 多分类副本声明：tools.json 条目的 categories 含指定分类时返回该条目。
        /// （内置挂载条目由调用方识别 BuiltinId 后跳过——兼容版无内置功能实现。）
        /// </summary>
        public static IReadOnlyList<CategoryPlacement> GetCategoryPlacements(string category)
        {
            try
            {
                var result = new List<CategoryPlacement>();
                foreach (var item in LoadMetadata())
                {
                    if (item.Hidden || item.Categories == null) continue;
                    bool hit = false;
                    foreach (var c in item.Categories)
                    {
                        if (string.Equals(c, category, StringComparison.OrdinalIgnoreCase))
                        {
                            hit = true;
                            break;
                        }
                    }
                    if (!hit) continue;

                    var cats = new List<string>();
                    foreach (var c in item.Categories)
                    {
                        if (!string.IsNullOrWhiteSpace(c) && !cats.Contains(c))
                            cats.Add(c);
                    }
                    result.Add(new CategoryPlacement
                    {
                        Match = item.Match ?? "",
                        PrimaryCategory = item.Category,
                        Categories = cats,
                        BuiltinId = string.IsNullOrWhiteSpace(item.Builtin) ? null : item.Builtin
                    });
                }
                return result;
            }
            catch
            {
                return new List<CategoryPlacement>();
            }
        }

        public sealed class CategoryPlacement
        {
            public string Match { get; set; }
            public string PrimaryCategory { get; set; }
            public IReadOnlyList<string> Categories { get; set; }
            public string BuiltinId { get; set; }
        }

        private static IReadOnlyList<JsonToolMetadata> LoadMetadata()
        {
            if (_metadata != null)
                return _metadata;

            var metadataDirectory = Path.Combine(Path.GetDirectoryName(ToolCatalog.ToolsRoot), "Metadata");
            var path = Path.Combine(metadataDirectory, "tools.default.json");
            if (!File.Exists(path)) path = Path.Combine(metadataDirectory, "tools.json");
            if (!File.Exists(path))
            {
                _metadata = new List<JsonToolMetadata>();
                return _metadata;
            }

            try
            {
                var json = File.ReadAllText(path);
                var root = JsonNode.Parse(json);
                var toolsArray = root["tools"] as JsonArray;
                if (toolsArray == null)
                {
                    _metadata = new List<JsonToolMetadata>();
                    return _metadata;
                }

                // 与主应用共享用户库协议；TUI 只读工具库数据，不改官方发布文件。
                try
                {
                    var users = Path.Combine(ConfigManager.GetDataDir(), "user-tools.json");
                    if (File.Exists(users) && JsonNode.Parse(File.ReadAllText(users))["tools"] is JsonArray userTools)
                        foreach (var tool in userTools) toolsArray.Add(tool.DeepClone());
                    var statesPath = Path.Combine(ConfigManager.GetDataDir(), "tool-state.json");
                    if (File.Exists(statesPath) && JsonNode.Parse(File.ReadAllText(statesPath))["tools"] is JsonArray states)
                    {
                        foreach (var tool in toolsArray.OfType<JsonObject>())
                        {
                            var id = ReadValue<string>(tool, "id") ?? "official:" + ReadValue<string>(tool, "match");
                            var state = states.OfType<JsonObject>().FirstOrDefault(t => string.Equals(ReadValue<string>(t, "id"), id, StringComparison.OrdinalIgnoreCase));
                            if (state == null) continue;
                            foreach (var field in new[] { "order", "version", "hidden" })
                                if (state[field] != null) tool[field] = state[field].DeepClone();
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine("[ToolMetadata] 用户工具数据读取失败：" + ex.Message); }

                var list = new List<JsonToolMetadata>();
                foreach (var item in toolsArray.OfType<JsonObject>())
                {
                    var meta = new JsonToolMetadata
                    {
                        Match = ReadValue<string>(item, "match"),
                        Name = ReadValue<string>(item, "name"),
                        Id = ReadValue<string>(item, "id"),
                        Directory = ReadValue<string>(item, "directory"),
                        Order = ReadValue<int>(item, "order", out var order) ? order : null,
                        Hidden = ReadValue<bool>(item, "hidden"),
                        Description = ReadValue<string>(item, "description"),
                        Publisher = ReadValue<string>(item, "publisher"),
                        DownloadUrl = ReadValue<string>(item, "downloadUrl"),
                        WingetId = ReadValue<string>(item, "wingetId"),
                        LaunchTarget = ReadValue<string>(item, "launchTarget"),
                        Category = ReadValue<string>(item, "category"),
                        Builtin = ReadValue<string>(item, "builtin")
                    };

                    var tagsToken = item["tags"];
                    if (tagsToken is JsonArray tags)
                    {
                        meta.Tags = tags.Select(ReadString).OfType<string>().ToList();
                    }

                    var categoriesToken = item["categories"];
                    if (categoriesToken is JsonArray categories)
                    {
                        meta.Categories = categories
                            .Select(t => ReadString(t))
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();
                    }

                    var variantsToken = item["archVariants"];
                    if (variantsToken is JsonArray variants)
                    {
                        meta.ArchVariants = new List<JsonArchVariant>();
                        foreach (var v in variants.OfType<JsonObject>())
                        {
                            meta.ArchVariants.Add(new JsonArchVariant
                            {
                                File = ReadValue<string>(v, "file"),
                                Dir = ReadValue<string>(v, "dir"),
                                Arch = ReadValue<string>(v, "arch")
                            });
                        }
                    }

                    list.Add(meta);
                }
                _metadata = list;
            }
            catch
            {
                _metadata = new List<JsonToolMetadata>();
            }

            return _metadata;
        }

        private static string ReadString(JsonNode node) =>
            node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

        private static T ReadValue<T>(JsonNode node, string key) =>
            ReadValue<T>(node, key, out var value) ? value : default;

        private static bool ReadValue<T>(JsonNode node, string key, out T value)
        {
            value = default;
            return node is JsonObject obj && obj[key] is JsonValue json && json.TryGetValue(out value);
        }

        private static string ReadFolderDescription(string toolPath)
        {
            var directory = Path.GetDirectoryName(toolPath);
            if (directory == null) return null;

            string textFile = null;
            try
            {
                foreach (var f in Directory.EnumerateFiles(directory, "*.txt", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(f);
                    if (name.IndexOf("readme", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("说明", StringComparison.CurrentCultureIgnoreCase) >= 0)
                    {
                        textFile = f;
                        break;
                    }
                }
            }
            catch { return null; }

            if (textFile == null) return null;

            try
            {
                foreach (var line in File.ReadLines(textFile))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        return line.Length > 160 ? line.Substring(0, 160) : line;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string FirstUseful(params string[] values)
        {
            if (values == null) return null;
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }
            return null;
        }

        internal static bool IsHiddenDirectory(string directory) => FindJsonMetadataByDir(directory)?.Hidden == true;
        internal static bool IsUserDirectory(string directory) => FindJsonMetadataByDir(directory)?.Directory != null;

        internal static string ResolveUserDirectory(string stored) => Path.GetFullPath(stored
            .Replace("{UserToolsRoot}", Path.Combine(ConfigManager.GetDataDir(), "UserTools"))
            .Replace("{ToolsRoot}", ToolCatalog.ToolsRoot)
            .Replace("{DataDir}", ConfigManager.GetDataDir())
            .Replace("{AppDir}", Path.GetDirectoryName(ToolCatalog.ToolsRoot))
            .Replace("{ParentDir}", Path.GetDirectoryName(Path.GetDirectoryName(ToolCatalog.ToolsRoot)))
            .Replace("{AppDataDir}", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TubaWinUi3")));

        internal static IEnumerable<string> GetAdditionalCategories()
        {
            foreach (var tool in LoadMetadata())
            {
                if (tool.Hidden || !string.IsNullOrWhiteSpace(tool.Builtin) || string.IsNullOrWhiteSpace(tool.Category))
                    continue;

                if (!string.IsNullOrWhiteSpace(tool.Directory))
                    yield return tool.Category;

                if (tool.Categories != null)
                    foreach (var category in tool.Categories)
                        yield return category;
            }
        }

        internal static IReadOnlyList<string> GetUserDirectories(string category) => LoadMetadata()
            .Where(t => !t.Hidden && !string.IsNullOrWhiteSpace(t.Directory) && string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase))
            .Select(t => ResolveUserDirectory(t.Directory)).ToList();

        private sealed class JsonToolMetadata
        {
            public string Match { get; set; }
            public string Name { get; set; }
            public string Id { get; set; }
            public string Directory { get; set; }
            public int? Order { get; set; }
            public bool Hidden { get; set; }
            public string Description { get; set; }
            public string Publisher { get; set; }
            public string DownloadUrl { get; set; }
            public string WingetId { get; set; }
            public string LaunchTarget { get; set; }
            public IReadOnlyList<string> Tags { get; set; }
            public List<JsonArchVariant> ArchVariants { get; set; }

            /// <summary>主分类：物理目录所在的分类（多分类副本条目声明用）。</summary>
            public string Category { get; set; }

            /// <summary>副本分类：工具额外出现的分类列表。</summary>
            public List<string> Categories { get; set; }

            /// <summary>内置工具挂载 id（兼容版无内置功能实现，仅用于识别后跳过）。</summary>
            public string Builtin { get; set; }
        }

        private sealed class JsonArchVariant
        {
            public string File { get; set; }
            public string Dir { get; set; }
            public string Arch { get; set; }
        }
    }
}
