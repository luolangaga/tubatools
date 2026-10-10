using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TubaWinUi3.Compatible.Models;

namespace TubaWinUi3.Compatible.Services
{
    internal static class PathHelper
    {
        public static string GetRelativePath(string relativeTo, string path)
        {
            if (string.IsNullOrWhiteSpace(relativeTo)) return path;
            if (string.IsNullOrWhiteSpace(path)) return path;

            var fromUri = new Uri(relativeTo.TrimEnd('\\') + "\\");
            var toUri = new Uri(path.TrimEnd('\\') + "\\");

            if (fromUri.Scheme != toUri.Scheme || fromUri.Host != toUri.Host)
                return path;

            var relativeUri = fromUri.MakeRelativeUri(toUri);
            var relative = Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', '\\');
            if (relative.EndsWith("\\")) relative = relative.Substring(0, relative.Length - 1);
            return relative;
        }
    }

    public static class ToolCatalog
    {
        private static readonly string[] LaunchableExtensions = new[]
        {
            ".exe", ".bat", ".cmd", ".lnk", ".msc", ".ps1", ".vbs"
        };

        public static string AppDirectory
        {
            get
            {
                try
                {
                    var path = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                    if (!string.IsNullOrEmpty(path))
                    {
                        var dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                            return dir;
                    }
                }
                catch { }
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        public static string ToolsRoot { get { return FindToolsRoot(); } }

        public static IReadOnlyList<string> GetCategories()
        {
            var dirs = (Directory.Exists(ToolsRoot) ? Directory.GetDirectories(ToolsRoot).Select(Path.GetFileName) : Enumerable.Empty<string>())
                .Concat(ToolMetadataService.GetUserCategories())
                .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var orderJson = AppSettings.Get("CategoryOrder");
            List<string> ordered = null;
            if (!string.IsNullOrWhiteSpace(orderJson))
            {
                try
                {
                    ordered = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(orderJson);
                }
                catch { }
            }

            if (ordered != null && ordered.Count > 0)
            {
                var orderedSet = new HashSet<string>(ordered, StringComparer.CurrentCultureIgnoreCase);
                var result = ordered.Where(name => dirs.Contains(name)).ToList();
                foreach (var d in dirs.OrderBy(d2 => d2, StringComparer.CurrentCultureIgnoreCase))
                {
                    if (!orderedSet.Contains(d))
                        result.Add(d);
                }
                return result;
            }

            return dirs.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public static IReadOnlyList<ToolItem> GetTools(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return new List<ToolItem>();

            var categoryRoot = Path.Combine(ToolsRoot, category);

            var items = new List<ToolItem>();
            var userDirectories = ToolMetadataService.GetUserDirectories(category);


            // tools.json 副本声明：物理扫描需避让同名目录（构建残留的空壳目录
            // 否则会经 HasDownloadUrl 生成无图标占位条目，与下方合成条目重复）。
            // 注意：builtin 挂载兼容版不合成条目，物理同名目录需保留扫描，否则工具消失。
            var placements = ToolMetadataService.GetCategoryPlacements(category);
            var declaredDirKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in placements)
            {
                if (string.IsNullOrWhiteSpace(p.Match) ||
                    !string.IsNullOrEmpty(p.BuiltinId) ||
                    string.IsNullOrEmpty(p.PrimaryCategory) ||
                    p.PrimaryCategory.Equals(category, StringComparison.OrdinalIgnoreCase))
                    continue;
                declaredDirKeys.Add(p.Match.Replace(" ", "").Replace("-", "").Replace("_", ""));
            }

            // 物理目录扫描（分类目录可能不存在：纯 tools.json 副本的分类也要能出列表）
            if (Directory.Exists(categoryRoot))
            {
                var toolDirs = Directory.GetDirectories(categoryRoot).Where(d => !userDirectories.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();
                var merged = MergeArchDirectories(toolDirs);

                foreach (var toolDir in merged)
                {
                    if (userDirectories.Contains(toolDir, StringComparer.OrdinalIgnoreCase) || ToolMetadataService.IsHiddenDirectory(toolDir)) continue;
                    var dirKey = Path.GetFileName(toolDir).Replace(" ", "").Replace("-", "").Replace("_", "");
                    if (declaredDirKeys.Contains(dirKey))
                        continue; // 已由 tools.json 副本/内置挂载声明，物理占位跳过避免重复

                    var launchable = FindPrimaryLaunchable(toolDir);
                    if (launchable != null || ToolMetadataService.HasDownloadUrl(category, toolDir))
                        items.AddRange(CreateToolItems(category, categoryRoot, launchable ?? CreatePlaceholderPath(toolDir), toolDir));
                }
            }

            foreach (var directory in userDirectories)
            {
                if (!Directory.Exists(directory)) continue;
                var launchable = FindPrimaryLaunchable(directory);
                if (launchable != null) items.AddRange(CreateToolItems(category, Path.GetDirectoryName(directory), launchable, directory));
            }

            // tools.json 多分类副本：由 category+categories 字段声明（旧 link.json 链路已删除）。
            // 内置挂载条目（builtin）兼容版无内置功能实现，跳过。
            foreach (var placement in placements)
            {
                if (!string.IsNullOrEmpty(placement.BuiltinId))
                    continue;
                if (string.IsNullOrEmpty(placement.PrimaryCategory) ||
                    placement.PrimaryCategory.Equals(category, StringComparison.OrdinalIgnoreCase))
                    continue;

                items.AddRange(CreateCategoryCopyItems(placement, category));
            }

            var toolOrderJson = AppSettings.Get("ToolOrder_" + category);
            List<string> toolOrder = null;
            if (!string.IsNullOrWhiteSpace(toolOrderJson))
            {
                try
                {
                    toolOrder = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(toolOrderJson);
                }
                catch { }
            }

            if (toolOrder != null && toolOrder.Count > 0)
            {
                var orderedSet = new HashSet<string>(toolOrder, StringComparer.CurrentCultureIgnoreCase);
                var result = new List<ToolItem>();
                foreach (var name in toolOrder)
                {
                    var match = items.FirstOrDefault(it => it.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
                    if (match != null) result.Add(match);
                }
                foreach (var item in items.OrderBy(it => it.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    if (!orderedSet.Contains(item.Name))
                        result.Add(item);
                }
                return result;
            }

            return items
                .OrderBy(item => item.SortOrder ?? int.MaxValue)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.RelativePath, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static List<string> MergeArchDirectories(List<string> toolDirs)
        {
            var dirNames = toolDirs.Select(d => Path.GetFileName(d)).ToList();
            var consumed = new HashSet<int>();
            var result = new List<string>();

            for (var i = 0; i < toolDirs.Count; i++)
            {
                if (consumed.Contains(i)) continue;

                var strippedI = StripArchSuffix(dirNames[i]);
                result.Add(toolDirs[i]);

                for (var j = i + 1; j < toolDirs.Count; j++)
                {
                    if (consumed.Contains(j)) continue;
                    var strippedJ = StripArchSuffix(dirNames[j]);
                    if (strippedI.Equals(strippedJ, StringComparison.OrdinalIgnoreCase))
                        consumed.Add(j);
                }
            }

            return result;
        }

        /// <summary>
        /// 多架构模式：目录下发现多个架构变体（x64/x86/ARM64）时，每个架构解析为一个独立工具卡片，
        /// 名称带架构后缀（如 "CPU-Z x64" / "CPU-Z ARM64"）；无变体时保持单个工具。
        /// </summary>
        private static List<ToolItem> CreateToolItems(string category, string categoryRoot, string path, string toolDir)
        {
            var primary = CreateToolItemWithVariants(category, categoryRoot, path, toolDir);
            var result = new List<ToolItem>();

            if (primary.AlternateVersions != null && primary.AlternateVersions.Count > 0)
            {
                var baseName = primary.Name;
                primary.Name = string.IsNullOrEmpty(primary.PrimaryArch)
                    ? baseName
                    : baseName + " " + primary.PrimaryArch;
                foreach (var v in primary.AlternateVersions)
                    result.Add(CloneArchItem(primary, baseName, v));
                primary.AlternateVersions = new List<ArchVariant>();
                result.Insert(0, primary);
            }
            else
            {
                result.Add(primary);
            }
            return result;
        }

        /// <summary>按架构变体复制一个独立工具项（每个架构一个卡片）。</summary>
        private static ToolItem CloneArchItem(ToolItem baseItem, string baseName, ArchVariant variant)
        {
            return new ToolItem
            {
                Name = baseName + " " + variant.Arch,
                LibraryId = baseItem.LibraryId,
                SortOrder = baseItem.SortOrder,
                Category = baseItem.Category,
                PrimaryCategory = baseItem.PrimaryCategory,
                Categories = baseItem.Categories,
                IsLinked = baseItem.IsLinked,
                Path = variant.Path,
                RelativePath = PathHelper.GetRelativePath(ToolCatalog.ToolsRoot, variant.Path),
                Extension = Path.GetExtension(variant.Path).TrimStart('.').ToUpperInvariant(),
                IconPath = null,
                IconGlyph = ToolIconService.GetIconGlyph(variant.Path),
                Description = baseItem.Description,
                Publisher = baseItem.Publisher,
                Version = baseItem.Version,
                DatabaseSource = baseItem.DatabaseSource,
                DownloadUrl = baseItem.DownloadUrl,
                DownloadFilter = baseItem.DownloadFilter,
                WingetId = baseItem.WingetId,
                Tags = baseItem.Tags,
                IsFavorite = FavoritesService.IsFavorite(variant.Path),
                PrimaryArch = variant.Arch,
                AlternateVersions = new List<ArchVariant>()
            };
        }

        /// <summary>
        /// 跨分类副本（tools.json category+categories 字段）：以主分类的物理目录生成完整工具项，
        /// 标记 IsLinked 并组成多分类（替代旧 link.json 的 target 链接；多架构拆卡逻辑与主扫描一致）。
        /// </summary>
        private static List<ToolItem> CreateCategoryCopyItems(ToolMetadataService.CategoryPlacement placement, string category)
        {
            var primaryCategory = placement.PrimaryCategory;
            var categoryRoot = Path.Combine(ToolsRoot, primaryCategory);
            if (!Directory.Exists(categoryRoot))
                return new List<ToolItem>();

            // 主分类下定位物理目录：相对路径含 match 或目录名灵活匹配（与 FindJsonMetadataByDir 同规则）。
            // 多候选时评分择优：目录名精确等于 match > 灵活匹配相等 > 相对路径包含，
            // 避免 memtest / memtest64 / memtestpro 这类前缀重叠目录误配。
            string toolDir = null;
            int bestScore = 0;
            foreach (var d in Directory.GetDirectories(categoryRoot))
            {
                var name = Path.GetFileName(d);
                int score;
                if (string.Equals(name, placement.Match, StringComparison.OrdinalIgnoreCase))
                    score = 3;
                else if (string.Equals(
                    name.Replace(" ", "").Replace("-", "").Replace("_", ""),
                    placement.Match.Replace(" ", "").Replace("-", "").Replace("_", ""),
                    StringComparison.OrdinalIgnoreCase))
                    score = 2;
                else if (PathHelper.GetRelativePath(ToolsRoot, d).IndexOf(placement.Match, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                         ToolMetadataService.MatchesFlexible(name, placement.Match))
                    score = 1;
                else
                    continue;

                if (score > bestScore)
                {
                    bestScore = score;
                    toolDir = d;
                }
            }
            if (toolDir == null)
                return new List<ToolItem>();

            var launchable = FindPrimaryLaunchable(toolDir);
            if (launchable == null && !ToolMetadataService.HasDownloadUrl(primaryCategory, toolDir))
                return new List<ToolItem>();

            var bases = CreateToolItems(
                primaryCategory,
                categoryRoot,
                launchable ?? CreatePlaceholderPath(toolDir),
                toolDir);

            var categories = new List<string>(placement.Categories);
            if (!categories.Contains(primaryCategory))
                categories.Add(primaryCategory);

            var result = new List<ToolItem>();
            foreach (var baseItem in bases)
            {
                result.Add(new ToolItem
                {
                    Name = baseItem.Name,
                    Category = category,
                    PrimaryCategory = primaryCategory,
                    Categories = categories,
                    IsLinked = true,
                    Path = baseItem.Path,
                    RelativePath = baseItem.RelativePath,
                    Extension = baseItem.Extension,
                    IconPath = baseItem.IconPath,
                    IconGlyph = baseItem.IconGlyph,
                    Description = baseItem.Description,
                    Publisher = baseItem.Publisher,
                    Version = baseItem.Version,
                    DatabaseSource = baseItem.DatabaseSource,
                    DownloadUrl = baseItem.DownloadUrl,
                    DownloadFilter = baseItem.DownloadFilter,
                    WingetId = baseItem.WingetId,
                    Tags = baseItem.Tags,
                    IsFavorite = baseItem.IsFavorite,
                    PrimaryArch = baseItem.PrimaryArch,
                    AlternateVersions = baseItem.AlternateVersions
                });
            }
            return result;
        }

        /// <summary>
        /// 「全部工具」一览：同名工具（含 tools.json 跨分类副本）只保留一份，
        /// 并把该名称出现的所有分类合并到 Categories 上（与主应用算法一致）。
        /// </summary>
        private static IReadOnlyList<ToolItem> DeduplicateAllTools(IReadOnlyList<ToolItem> allItems)
        {
            var nameToCategories = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in allItems)
            {
                HashSet<string> set;
                var identity = item.LibraryId ?? item.Name;
                if (!nameToCategories.TryGetValue(identity, out set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    nameToCategories[identity] = set;
                }
                set.Add(item.Category);
                if (!string.IsNullOrEmpty(item.PrimaryCategory))
                    set.Add(item.PrimaryCategory);
                foreach (var c in item.Categories)
                    set.Add(c);
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = new List<ToolItem>();
            foreach (var item in allItems)
            {
                var key = item.LibraryId is null ? (item.PrimaryCategory ?? item.Category) + "|" + item.Name
                    : item.LibraryId + "|" + item.PrimaryArch;
                if (seen.Add(key))
                {
                    if (nameToCategories.TryGetValue(item.LibraryId ?? item.Name, out var cats) && cats.Count > 1)
                        item.SetCategories(cats.ToList());
                    deduped.Add(item);
                }
            }
            return deduped;
        }

        /// <summary>全部工具（跨分类去重、合并多分类）。</summary>
        public static IReadOnlyList<ToolItem> GetAllToolsDeduped()
        {

            return DeduplicateAllTools(GetCategories().SelectMany(GetTools).ToList());
        }

        public static IReadOnlyList<ToolItem> GetAllToolsLazy(int skip, int take)
        {

            return GetCategories()
                .SelectMany(GetTools)
                .Skip(skip)
                .Take(take)
                .ToList();
        }

        public static int GetAllToolsCount()
        {
            return GetCategories().Sum(c => GetTools(c).Count);
        }

        private static IReadOnlyList<string> _cachedTags;
        private static IReadOnlyList<ToolItem> _cachedAllTools;

        private static IReadOnlyList<ToolItem> GetAllToolsCached()
        {
            if (_cachedAllTools != null) return _cachedAllTools;
            _cachedAllTools = DeduplicateAllTools(GetCategories().SelectMany(GetTools).ToList());
            return _cachedAllTools;
        }

        public static IReadOnlyList<string> GetAllTags()
        {
            if (_cachedTags != null) return _cachedTags;

            var allTools = GetAllToolsCached();
            _cachedTags = allTools
                .SelectMany(t => t.Tags ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .GroupBy(t => t, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => g.Key)
                .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return _cachedTags;
        }

        public static void InvalidateTagsCache()
        {
            _cachedTags = null;
            _cachedAllTools = null;
        }

        public static IReadOnlyList<ToolItem> Search(string query, string tag = null)
        {

            var normalizedQuery = (query ?? "").Trim();
            if (normalizedQuery.Length == 0 && string.IsNullOrEmpty(tag))
                return new List<ToolItem>();

            var allTools = GetAllToolsCached();
            var result = new List<ToolItem>();

            foreach (var item in allTools)
            {
                var matchesQuery = normalizedQuery.Length == 0 ||
                    item.Name.IndexOf(normalizedQuery, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    item.RelativePath.IndexOf(normalizedQuery, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    (item.Tags != null && item.Tags.Any(t => t.IndexOf(normalizedQuery, StringComparison.CurrentCultureIgnoreCase) >= 0));

                var matchesTag = string.IsNullOrEmpty(tag) ||
                    (item.Tags != null && item.Tags.Any(t => t.Equals(tag, StringComparison.CurrentCultureIgnoreCase)));

                if (matchesQuery && matchesTag)
                    result.Add(item);
            }

            return result;
        }

        private static ToolItem CreateToolItemWithVariants(string category, string categoryRoot, string path, string toolDir)
        {
            var extension = Path.GetExtension(path);
            var rawName = GetDisplayName(path);
            var relativePath = PathHelper.GetRelativePath(categoryRoot, path);
            var metadata = ToolMetadataService.GetMetadata(category, path);
            var isPlaceholder = !File.Exists(path) && (!string.IsNullOrWhiteSpace(metadata.DownloadUrl) || !string.IsNullOrWhiteSpace(metadata.WingetId));

            var primaryArch = DetectArch(Path.GetFileNameWithoutExtension(path));
            var archDisplay = FormatArchDisplay(primaryArch);

            var alternates = FindAllArchVariants(toolDir, path);

            var categoryRootDir = categoryRoot;
            if (!ToolMetadataService.IsUserDirectory(toolDir) && Directory.Exists(categoryRootDir))
            {
                var dirName = Path.GetFileName(toolDir);
                var strippedDir = StripArchSuffix(dirName);
                foreach (var otherDir in Directory.GetDirectories(categoryRootDir))
                {
                    var otherName = Path.GetFileName(otherDir);
                    if (otherName.Equals(dirName, StringComparison.OrdinalIgnoreCase)) continue;
                    var strippedOther = StripArchSuffix(otherName);
                    if (!strippedOther.Equals(strippedDir, StringComparison.OrdinalIgnoreCase)) continue;

                    var otherLaunchable = FindPrimaryLaunchable(otherDir);
                    if (otherLaunchable == null) continue;

                    var otherFileName = Path.GetFileNameWithoutExtension(otherLaunchable);
                    var otherArch = DetectArch(otherFileName);
                    if (otherArch == null) continue;

                    alternates.Add(new ArchVariant
                    {
                        Name = CleanupName(StripArchSuffix(otherFileName)),
                        Path = otherLaunchable,
                        Arch = FormatArchDisplay(otherArch)
                    });
                }
            }

            var jsonVariants = ToolMetadataService.GetArchVariants(path, toolDir);
            foreach (var jv in jsonVariants)
            {
                string variantPath = null;

                if (!string.IsNullOrWhiteSpace(jv.File))
                {
                    var candidate = Path.Combine(toolDir, jv.File);
                    if (File.Exists(candidate))
                        variantPath = candidate;
                }

                if (variantPath == null && !string.IsNullOrWhiteSpace(jv.Dir))
                {
                    var altDir = Path.Combine(categoryRootDir, jv.Dir);
                    if (Directory.Exists(altDir))
                    {
                        var altLaunchable = FindPrimaryLaunchable(altDir);
                        if (altLaunchable != null)
                            variantPath = altLaunchable;
                    }
                }

                if (variantPath == null) continue;
                if (variantPath.Equals(path, StringComparison.OrdinalIgnoreCase)) continue;
                if (alternates.Any(a => a.Path.Equals(variantPath, StringComparison.OrdinalIgnoreCase))) continue;

                var vName = Path.GetFileNameWithoutExtension(variantPath);
                alternates.Add(new ArchVariant
                {
                    Name = CleanupName(StripArchSuffix(vName)),
                    Path = variantPath,
                    Arch = jv.Arch ?? FormatArchDisplay(DetectArch(vName)) ?? "x86"
                });
            }

            // 命名与主应用一致：存在架构变体（或文件名带架构）时优先用目录名，避免文件名被架构后缀破坏
            var toolDirName = Path.GetFileName(toolDir);
            var hasArchVariants = alternates.Count > 0 || primaryArch != null;
            var name = hasArchVariants ? toolDirName : rawName;

            var cleanName = metadata.Name ?? CleanupName(StripArchSuffix(name));
            if (string.IsNullOrWhiteSpace(cleanName) || cleanName.Length < 3)
                cleanName = CleanupName(toolDirName);

            var item = new ToolItem
            {
                Name = cleanName,
                LibraryId = metadata.LibraryId,
                SortOrder = metadata.Order,
                Category = category,
                Path = path,
                RelativePath = relativePath,
                Extension = isPlaceholder ? "待下载" : extension.TrimStart('.').ToUpperInvariant(),
                IconPath = null,
                IconGlyph = isPlaceholder ? null : ToolIconService.GetIconGlyph(path),
                Description = metadata.Description,
                Publisher = metadata.Publisher,
                Version = metadata.Version,
                DatabaseSource = metadata.DatabaseSource,
                DownloadUrl = metadata.DownloadUrl,
                DownloadFilter = metadata.DownloadFilter,
                WingetId = metadata.WingetId,
                Tags = metadata.Tags ?? new List<string>(),
                IsFavorite = isPlaceholder ? false : FavoritesService.IsFavorite(path),
                PrimaryArch = archDisplay.Length > 0 ? archDisplay : null,
                AlternateVersions = alternates
            };
            item.InitArchOptions();
            return item;
        }

        private static bool IsLaunchable(string path)
        {
            var extension = Path.GetExtension(path);
            for (int i = 0; i < LaunchableExtensions.Length; i++)
            {
                if (extension.Equals(LaunchableExtensions[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // 顺序很重要：带下划线/长的形式必须排在前，且 "arm64" 类必须优先于裸 "64"，
        // 否则 "cpuz_arm64" 会被剥成 "cpuz_"（残留下划线）或 "cpuz_arm"，导致跨架构匹配失败。
        private static readonly string[] ArchSuffixes = new[]
        {
            "_ARM64", "ARM64", "_arm64", "arm64",
            "_Win64", "_Win32", "w64", "w32",
            "_x64", "x64", "_x86", "x86",
            "_64", "_32", "64", "32"
        };

        private static readonly string[] ArchX64Patterns = new[]
        {
            "x64", "_x64", "64", "_64", "w64", "_Win64"
        };

        private static readonly string[] ArchArm64Patterns = new[]
        {
            "ARM64", "_ARM64", "arm64", "_arm64"
        };

        private static readonly string[] Arch32Patterns = new[]
        {
            "x86", "_x86", "32", "_32", "w32", "_Win32"
        };

        private static bool IsX64OS { get { return Environment.Is64BitOperatingSystem; } }

        /// <summary>主机 OS 架构优先序（与主应用 PreferredArchPriority 一致）：ARM64 系统 ARM64 > x64 > x86。</summary>
        private static IReadOnlyList<string> PreferredArchPriority
        {
            get
            {
                try
                {
                    switch (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture)
                    {
                        case System.Runtime.InteropServices.Architecture.Arm64: return new[] { "ARM64", "x64", "x86" };
                        case System.Runtime.InteropServices.Architecture.X64: return new[] { "x64", "x86" };
                        case System.Runtime.InteropServices.Architecture.X86: return new[] { "x86" };
                    }
                }
                catch { }
                return new[] { "x64", "x86" };
            }
        }

        private static string DetectArch(string name)
        {
            foreach (var p in ArchArm64Patterns)
            {
                if (name.EndsWith(p, StringComparison.OrdinalIgnoreCase))
                    return "ARM64";
            }
            foreach (var p in ArchX64Patterns)
            {
                if (name.EndsWith(p, StringComparison.OrdinalIgnoreCase))
                    return "x64";
            }
            foreach (var p in Arch32Patterns)
            {
                if (name.EndsWith(p, StringComparison.OrdinalIgnoreCase))
                    return "x86";
            }
            return null;
        }

        private static string FormatArchDisplay(string arch)
        {
            if (arch == "ARM64") return "ARM64";
            if (arch == "x64" || arch == "Win64") return "x64";
            if (arch == "x86" || arch == "Win32") return "x86";
            return arch ?? "";
        }

        private static List<ArchVariant> FindAllArchVariants(string toolDir, string primaryPath)
        {
            var variants = new List<ArchVariant>();
            var dirName = Path.GetFileName(toolDir);
            var primaryExt = primaryPath != null ? Path.GetExtension(primaryPath) : null;

            var allLaunchables = Directory.EnumerateFiles(toolDir, "*", SearchOption.AllDirectories)
                .Where(IsLaunchable)
                .ToList();

            foreach (var filePath in allLaunchables)
            {
                if (filePath.Equals(primaryPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (primaryExt != null && !Path.GetExtension(filePath).Equals(primaryExt, StringComparison.OrdinalIgnoreCase)) continue;

                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var arch = DetectArch(fileName);
                if (arch == null) continue;

                var stripped = StripArchSuffix(fileName);
                var dirStripped = StripArchSuffix(dirName);
                if (!stripped.Equals(dirStripped, StringComparison.OrdinalIgnoreCase) &&
                    !stripped.Equals(dirName, StringComparison.OrdinalIgnoreCase))
                    continue;

                variants.Add(new ArchVariant
                {
                    Name = CleanupName(StripArchSuffix(fileName)),
                    Path = filePath,
                    Arch = FormatArchDisplay(arch)
                });
            }

            return variants;
        }

        private static string FindPrimaryLaunchable(string toolDir)
        {
            var dirName = Path.GetFileName(toolDir);

            var launchTarget = ToolMetadataService.GetLaunchTarget(toolDir);
            if (!string.IsNullOrWhiteSpace(launchTarget))
            {
                var targetPath = Path.Combine(toolDir, launchTarget);
                if (File.Exists(targetPath) && IsLaunchable(targetPath))
                    return targetPath;

                var deepTarget = Directory.EnumerateFiles(toolDir, launchTarget, SearchOption.AllDirectories)
                    .FirstOrDefault(f => IsLaunchable(f));
                if (deepTarget != null)
                    return deepTarget;
            }

            var allLaunchables = Directory.EnumerateFiles(toolDir, "*", SearchOption.AllDirectories)
                .Where(IsLaunchable)
                .ToList();

            if (allLaunchables.Count == 0) return null;
            if (allLaunchables.Count == 1) return allLaunchables[0];

            var directLaunchables = Directory.EnumerateFiles(toolDir)
                .Where(IsLaunchable)
                .ToList();

            var match = directLaunchables.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Equals(dirName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            var archCandidates = directLaunchables
                .Where(f => StripArchSuffix(Path.GetFileNameWithoutExtension(f))
                    .Equals(StripArchSuffix(dirName), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (archCandidates.Count > 0) return PickPreferredArch(archCandidates);

            match = allLaunchables.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Equals(dirName, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            archCandidates = allLaunchables
                .Where(f => StripArchSuffix(Path.GetFileNameWithoutExtension(f))
                    .Equals(StripArchSuffix(dirName), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (archCandidates.Count > 0) return PickPreferredArch(archCandidates);
            if (directLaunchables.Count > 0) return directLaunchables[0];

            return allLaunchables[0];
        }

        private static string PickPreferredArch(List<string> candidates)
        {
            // 用 DetectArch 分类（ARM64 模式优先于 x64 的裸 "64" 后缀），再按 OS 优先序选择
            var byArch = candidates
                .Select(f => new { f, arch = DetectArch(Path.GetFileNameWithoutExtension(f)) })
                .ToList();

            foreach (var pref in PreferredArchPriority)
            {
                var match = byArch.FirstOrDefault(c => c.arch != null &&
                    c.arch.Equals(pref, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match.f;
            }

            return candidates[0];
        }

        private static string StripArchSuffix(string name)
        {
            foreach (var suffix in ArchSuffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return name.Substring(0, name.Length - suffix.Length);
            }
            return name;
        }

        private static string CleanupName(string name)
        {
            return name
                .Replace("_x64", " x64")
                .Replace("_x86", " x86")
                .Replace("_ARM64", " ARM64")
                .Replace("_arm64", " ARM64")
                .Replace("_", " ");
        }

        private static string GetDisplayName(string path)
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!fileName.Equals("start", StringComparison.OrdinalIgnoreCase))
                return fileName;

            var parentName = Directory.GetParent(path) != null ? Directory.GetParent(path).Name : null;
            return string.IsNullOrWhiteSpace(parentName) ? fileName : parentName;
        }

        private static string CreatePlaceholderPath(string toolDir)
        {
            var dirName = Path.GetFileName(toolDir);
            return Path.Combine(toolDir, dirName + ".exe");
        }

        private static string FindToolsRoot()
        {
            var outputTools = Path.Combine(AppDirectory, "Tools");
            if (Directory.Exists(outputTools))
                return outputTools;

            var srcTools = Path.Combine(AppDirectory, "src", "Tools");
            if (Directory.Exists(srcTools))
                return srcTools;

            var directory = new DirectoryInfo(AppDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "Tools");
                if (Directory.Exists(candidate))
                    return candidate;

                var srcCandidate = Path.Combine(directory.FullName, "src", "Tools");
                if (Directory.Exists(srcCandidate))
                    return srcCandidate;

                directory = directory.Parent;
            }

            return outputTools;
        }
    }
}
