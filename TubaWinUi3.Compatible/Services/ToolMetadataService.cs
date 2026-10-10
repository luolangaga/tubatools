using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TubaWinUi3.Compatible.Models;

namespace TubaWinUi3.Compatible.Services
{
    public sealed class ToolMetadata
    {
        public string Name { get; set; }
        public string LibraryId { get; set; }
        public int? Order { get; set; }
        public string Description { get; set; }
        public string Publisher { get; set; }
        public string Version { get; set; }
        public string DatabaseSource { get; set; }
        public string DownloadUrl { get; set; }
        public string DownloadFilter { get; set; }
        public string WingetId { get; set; }
        public string LaunchTarget { get; set; }
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

        public static bool HasDownloadUrl(string category, string toolDir)
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

        public static ToolMetadata GetMetadata(string category, string toolPath)
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
                LibraryId = jsonMetadata != null ? jsonMetadata.Id : null,
                Order = jsonMetadata != null ? jsonMetadata.Order : null,
                Description = description,
                Publisher = FirstUseful(
                    jsonMetadata != null ? jsonMetadata.Publisher : null,
                    versionInfo != null ? versionInfo.CompanyName : null,
                    versionInfo != null ? versionInfo.LegalCopyright : null),
                Version = FirstUseful(
                    versionInfo != null ? versionInfo.ProductVersion : null,
                    versionInfo != null ? versionInfo.FileVersion : null),
                DatabaseSource = jsonMetadata == null ? null : "JSON",
                DownloadUrl = jsonMetadata != null ? jsonMetadata.DownloadUrl : null,
                DownloadFilter = jsonMetadata != null ? jsonMetadata.DownloadFilter : null,
                WingetId = jsonMetadata != null ? jsonMetadata.WingetId : null,
                LaunchTarget = jsonMetadata != null ? jsonMetadata.LaunchTarget : null,
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
            var relativePath = PathHelper.GetRelativePath(ToolCatalog.ToolsRoot, toolPath);
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

        private static JsonToolMetadata FindJsonMetadataByDir(string toolDir)
        {
            var metadata = LoadMetadata();
            var registered = metadata.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Directory) && Path.GetFullPath(toolDir).Equals(Path.GetFullPath(ResolveUserDirectory(t.Directory)), StringComparison.OrdinalIgnoreCase));
            if (registered != null) return registered;

            var dirName = Path.GetFileName(toolDir);
            var relativePath = PathHelper.GetRelativePath(ToolCatalog.ToolsRoot, toolDir);

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

            var metadataDirectory = FindRoot("Metadata");
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
                var root = JObject.Parse(json);
                var toolsArray = root["tools"] as JArray;
                if (toolsArray == null)
                {
                    _metadata = new List<JsonToolMetadata>();
                    return _metadata;
                }

                // 与主应用共享用户库协议；旧/PE 版本只读用户数据，不改官方发布文件。
                try
                {
                    var users = Path.Combine(ConfigManager.GetDataDir(), "user-tools.json");
                    if (File.Exists(users) && JObject.Parse(File.ReadAllText(users))["tools"] is JArray userTools)
                        foreach (var tool in userTools) toolsArray.Add(tool.DeepClone());
                    var statesPath = Path.Combine(ConfigManager.GetDataDir(), "tool-state.json");
                    if (File.Exists(statesPath) && JObject.Parse(File.ReadAllText(statesPath))["tools"] is JArray states)
                    {
                        foreach (var tool in toolsArray)
                        {
                            var id = tool.Value<string>("id") ?? "official:" + tool.Value<string>("match");
                            var state = states.FirstOrDefault(t => string.Equals(t.Value<string>("id"), id, StringComparison.OrdinalIgnoreCase));
                            if (state == null) continue;
                            foreach (var field in new[] { "order", "version", "hidden" })
                                if (state[field] != null) tool[field] = state[field].DeepClone();
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine("[ToolMetadata] 用户工具数据读取失败：" + ex.Message); }

                var result = new List<JsonArchVariant>();
                var list = new List<JsonToolMetadata>();
                foreach (var item in toolsArray)
                {
                    var meta = new JsonToolMetadata
                    {
                        Match = item.Value<string>("match"),
                        Name = item.Value<string>("name"),
                        Id = item.Value<string>("id"),
                        Directory = item.Value<string>("directory"),
                        Order = item.Value<int?>("order"),
                        Hidden = item.Value<bool?>("hidden") == true,
                        Description = item.Value<string>("description"),
                        Publisher = item.Value<string>("publisher"),
                        DownloadUrl = item.Value<string>("downloadUrl"),
                        DownloadFilter = item.Value<string>("downloadFilter"),
                        WingetId = item.Value<string>("wingetId"),
                        LaunchTarget = item.Value<string>("launchTarget"),
                        Category = item.Value<string>("category"),
                        Builtin = item.Value<string>("builtin")
                    };

                    var tagsToken = item["tags"];
                    if (tagsToken != null)
                    {
                        meta.Tags = tagsToken.Select(t => t.Value<string>()).ToList();
                    }

                    var categoriesToken = item["categories"];
                    if (categoriesToken != null)
                    {
                        meta.Categories = categoriesToken
                            .Select(t => t.Value<string>())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();
                    }

                    var variantsToken = item["archVariants"];
                    if (variantsToken != null)
                    {
                        meta.ArchVariants = new List<JsonArchVariant>();
                        foreach (var v in variantsToken)
                        {
                            meta.ArchVariants.Add(new JsonArchVariant
                            {
                                File = v.Value<string>("file"),
                                Dir = v.Value<string>("dir"),
                                Arch = v.Value<string>("arch")
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

        private static string FindRoot(string folderName)
        {
            var appDir = ToolCatalog.AppDirectory;
            var outputRoot = Path.Combine(appDir, folderName);
            if (Directory.Exists(outputRoot))
                return outputRoot;

            var srcRoot = Path.Combine(appDir, "src", folderName);
            if (Directory.Exists(srcRoot))
                return srcRoot;

            var directory = new DirectoryInfo(appDir);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, folderName);
                if (Directory.Exists(candidate))
                    return candidate;

                var srcCandidate = Path.Combine(directory.FullName, "src", folderName);
                if (Directory.Exists(srcCandidate))
                    return srcCandidate;

                directory = directory.Parent;
            }

            return outputRoot;
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

        internal static IReadOnlyList<string> GetUserCategories() => LoadMetadata()
            .Where(t => !t.Hidden && !string.IsNullOrWhiteSpace(t.Directory) && !string.IsNullOrWhiteSpace(t.Category))
            .Select(t => t.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

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
            public string DownloadFilter { get; set; }
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
