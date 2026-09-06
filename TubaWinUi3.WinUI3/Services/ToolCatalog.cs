using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class ToolCatalog
{
    private static readonly string[] LaunchableExtensions =
    [
        ".exe",
        ".bat",
        ".cmd",
        ".lnk",
        ".msc",
        ".ps1",
        ".vbs"
    ];

    public static bool IsCacheReady => _cachedAllTools is not null;

    public static string AppDirectory
    {
        get
        {
            try
            {
                var path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path))
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        return dir;
                }
            }
            catch { }
            return AppContext.BaseDirectory;
        }
    }

    private static string? _cachedToolsRoot;

    /// <summary>Tools 根是否处于「构建工具缓存 / 测试」覆盖模式（与正常解析缓存的根区分）。</summary>
    private static bool _toolsRootOverridden;

    public static string ToolsRoot
    {
        get
        {
            if (_cachedToolsRoot is not null)
                return _cachedToolsRoot;
            _cachedToolsRoot = FindToolsRoot();
            return _cachedToolsRoot;
        }
    }

    /// <summary>读取用户自定义的分类顺序（AppSettings CategoryOrder），无记录或损坏时返回空列表。</summary>
    private static List<string> LoadCategoryOrder()
    {
        var orderJson = AppSettings.Get("CategoryOrder");
        if (string.IsNullOrWhiteSpace(orderJson))
            return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(orderJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>扫描当前没有任何工具的空白分类名列表。纯查询，可后台线程调用。</summary>
    public static List<string> FindEmptyCategories()
    {
        if (!Directory.Exists(ToolsRoot))
            return [];
        return GetCategories()
            .Where(name => GetTools(name).Count == 0)
            .ToList();
    }

    /// <summary>
    /// 分类下没有任何工具时删除其目录（含图标与排序记录），返回是否实际删除。
    /// 涉及 AppSettings 写入，调用方应在 UI 线程调用。
    /// </summary>
    public static bool PruneCategoryIfEmpty(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return false;

        var dir = Path.Combine(ToolsRoot, category);
        if (!Directory.Exists(dir))
            return false;

        try
        {
            if (GetTools(category).Count > 0)
                return false;

            Directory.Delete(dir, recursive: true);

            AppSettings.Remove($"CategoryGlyph_{category}");

            var order = LoadCategoryOrder();
            var changed = order.RemoveAll(name => name.Equals(category, StringComparison.CurrentCultureIgnoreCase));
            if (changed > 0)
                AppSettings.Set("CategoryOrder", System.Text.Json.JsonSerializer.Serialize(order));

            return true;
        }
        catch
        {
            // 目录被占用或只读（如打包安装目录）：跳过，不阻断主流程
            return false;
        }
    }

    /// <summary>把新建的分类追加到自定义顺序末尾（先固化当前全部分类顺序，再追加新分类）。</summary>
    public static void AppendCategoryOrder(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return;

        var order = LoadCategoryOrder();
        if (order.Any(name => name.Equals(category, StringComparison.CurrentCultureIgnoreCase)))
            return;

        var knownSet = new HashSet<string>(order, StringComparer.CurrentCultureIgnoreCase);
        order.AddRange(GetCategories().Where(name => !knownSet.Contains(name)));
        order.Add(category);
        AppSettings.Set("CategoryOrder", System.Text.Json.JsonSerializer.Serialize(order));
    }

    public static IReadOnlyList<string> GetCategories()
    {
        if (!Directory.Exists(ToolsRoot))
        {
            return [];
        }

        var dirs = Directory.GetDirectories(ToolsRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToList();

        var ordered = LoadCategoryOrder();

        if (ordered.Count > 0)
        {
            var orderedSet = new HashSet<string>(ordered, StringComparer.CurrentCultureIgnoreCase);
            var result = ordered.Where(name => dirs.Contains(name!)).ToList();
            foreach (var d in dirs.OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!orderedSet.Contains(d))
                    result.Add(d);
            }
            return result;
        }

        return dirs.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static IReadOnlyList<ToolItem> GetTools(string? category)
    {
        if (string.IsNullOrWhiteSpace(category) || !Directory.Exists(ToolsRoot))
        {
            return [];
        }

        lock (_cacheLock)
        {
            if (_toolsCache.TryGetValue(category, out var cached) && RootsMatch(cached.Root, ToolsRoot))
                return cached.Items;
        }

        var categoryRoot = Path.Combine(ToolsRoot, category);
        var items = new ConcurrentBag<ToolItem>();

        // tools.json 副本/内置挂载声明：物理扫描需避让同名目录（构建残留的空壳目录
        // 否则会经 HasDownloadUrl 生成无图标占位条目，与下方合成条目重复）
        var placements = ToolMetadataService.GetCategoryPlacements(category).ToList();
        var declaredDirKeys = placements
            .Where(p => !string.IsNullOrWhiteSpace(p.Match) && (
                !string.IsNullOrWhiteSpace(p.BuiltinId) ||
                (!string.IsNullOrWhiteSpace(p.PrimaryCategory) &&
                 !p.PrimaryCategory.Equals(category, StringComparison.OrdinalIgnoreCase))))
            .Select(p => p.Match.Replace(" ", "").Replace("-", "").Replace("_", ""))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 物理目录扫描（分类目录可能不存在：纯 tools.json 副本的分类也要能出列表）
        if (Directory.Exists(categoryRoot))
        {
            var toolDirs = Directory.GetDirectories(categoryRoot).ToList();
            var merged = MergeArchDirectories(toolDirs);

            // 并行扫描各工具目录（递归枚举 + FileVersionInfo 是主要 I/O 开销）
            Parallel.ForEach(merged, toolDir =>
            {
                var dirKey = Path.GetFileName(toolDir).Replace(" ", "").Replace("-", "").Replace("_", "");
                if (declaredDirKeys.Contains(dirKey))
                    return; // 已由 tools.json 副本/内置挂载声明，物理占位跳过避免重复

                var launchable = FindPrimaryLaunchable(toolDir);
                if (launchable is not null || ToolMetadataService.HasDownloadUrl(category, toolDir))
                    items.Add(CreateToolItemWithVariants(category, categoryRoot, launchable ?? CreatePlaceholderPath(toolDir), toolDir));
            });
        }

        // tools.json 多分类副本与内置挂载：由 category+categories / builtin 字段声明（无 link.json）
        foreach (var placement in placements)
        {
            if (!string.IsNullOrWhiteSpace(placement.BuiltinId))
            {
                var builtinItem = CreateBuiltinPlacedItem(placement, category);
                if (builtinItem is not null)
                    items.Add(builtinItem);
            }
            else if (!string.IsNullOrWhiteSpace(placement.PrimaryCategory) &&
                     !placement.PrimaryCategory.Equals(category, StringComparison.OrdinalIgnoreCase))
            {
                var copyItem = CreateCategoryCopyItem(placement, category);
                if (copyItem is not null)
                    items.Add(copyItem);
            }
        }

        // 排序：tools.json 的 order 字段为主序（收录工具按编辑顺序）；
        // 未收录的自定义工具退回 AppSettings ToolOrder_（旧数据兼容）→ 名称字典序
        var toolOrderJson = AppSettings.Get($"ToolOrder_{category}");
        List<string>? toolOrder = null;
        if (!string.IsNullOrWhiteSpace(toolOrderJson))
        {
            try
            {
                toolOrder = System.Text.Json.JsonSerializer.Deserialize<List<string>>(toolOrderJson);
            }
            catch { }
        }

        var orderedItems = items
            .Where(item => item.SortOrder.HasValue)
            .OrderBy(item => item.SortOrder!.Value)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var unorderedItems = items.Where(item => !item.SortOrder.HasValue).ToList();
        if (toolOrder is not null && toolOrder.Count > 0)
            unorderedItems = ReorderByName(unorderedItems, toolOrder).ToList();
        else
            unorderedItems = unorderedItems
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.RelativePath, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        IReadOnlyList<ToolItem> result = orderedItems.Concat(unorderedItems).ToList();

        lock (_cacheLock) { _toolsCache[category] = new CategoryCacheEntry(ToolsRoot, result); }
        return result;
    }

    /// <summary>按给定工具名顺序重排列表;未列出的项按名称追加。
    /// 顺序表里同名出现多次（历史配置残留）时不重复返回同一工具。</summary>
    internal static IReadOnlyList<ToolItem> ReorderByName(IReadOnlyList<ToolItem> items, IReadOnlyList<string> orderedNames)
    {
        var emitted = new HashSet<ToolItem>(ReferenceEqualityComparer.Instance);
        var ordered = new List<ToolItem>();

        foreach (var name in orderedNames)
        {
            var match = items.FirstOrDefault(it =>
                !emitted.Contains(it) && it.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
            if (match is not null)
            {
                emitted.Add(match);
                ordered.Add(match);
            }
        }

        foreach (var item in items.OrderBy(it => it.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!emitted.Contains(item))
                ordered.Add(item);
        }
        return ordered;
    }

    private static List<string> MergeArchDirectories(List<string> toolDirs)
    {
        var dirNames = toolDirs.Select(d => Path.GetFileName(d)!).ToList();
        var consumed = new HashSet<int>();
        var result = new List<string>();

        for (var i = 0; i < toolDirs.Count; i++)
        {
            if (consumed.Contains(i))
                continue;

            var strippedI = StripArchSuffix(dirNames[i]);
            result.Add(toolDirs[i]);

            for (var j = i + 1; j < toolDirs.Count; j++)
            {
                if (consumed.Contains(j))
                    continue;

                var strippedJ = StripArchSuffix(dirNames[j]);
                if (strippedI.Equals(strippedJ, StringComparison.OrdinalIgnoreCase))
                {
                    consumed.Add(j);
                }
            }
        }

        return result;
    }

    public static IReadOnlyList<ToolItem> GetAllToolsLazy(int skip, int take)
    {
        return GetAllToolsCached().Skip(skip).Take(take).ToList();
    }

    public static int GetAllToolsCount()
    {
        return GetAllToolsCached().Count;
    }

    private static IReadOnlyList<string>? _cachedTags;
    private static volatile IReadOnlyList<ToolItem>? _cachedAllTools;

    /// <summary>「全部工具」缓存对应的 Tools 根（根切换后旧缓存视为未命中）。</summary>
    private static string? _cachedAllToolsRoot;

    /// <summary>根一致时才返回已缓存的全部工具一览，否则返回 null（强制重扫）。</summary>
    private static IReadOnlyList<ToolItem>? PeekAllToolsCache()
    {
        lock (_cacheLock)
        {
            return _cachedAllToolsRoot == ToolsRoot ? _cachedAllTools : null;
        }
    }

    private static void SetAllToolsCache(IReadOnlyList<ToolItem> tools)
    {
        lock (_cacheLock)
        {
            _cachedAllTools = tools;
            _cachedAllToolsRoot = ToolsRoot;
        }
    }
    private static readonly object _cacheLock = new();
    private static readonly object _scanLock = new();
    private static readonly Dictionary<string, CategoryCacheEntry> _toolsCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>分类缓存条目：与 Tools 根绑定，根切换后旧条目视为未命中（并行扫描互不污染）。</summary>
    private sealed record CategoryCacheEntry(string Root, IReadOnlyList<ToolItem> Items);

    private static bool RootsMatch(string? a, string? b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static int _cacheVersion = 6; // v6: 多分类副本/内置挂载来自 tools.json（category/categories/builtin 字段），link.json 链路删除

    public static int CacheVersion => _cacheVersion;

    public static event Action? ToolsChanged;

    /// <summary>
    /// Single-flight：并发调用（MainWindow 预热 / 首页 / 标签栏 / 图标清理）
    /// 共享同一次扫描，避免冷启动时 3~4 路重复全量扫描。
    /// </summary>
    public static IReadOnlyList<ToolItem> GetAllToolsCached()
    {
        var cached = PeekAllToolsCache();
        if (cached is not null)
            return cached;

        lock (_scanLock)
        {
            cached = PeekAllToolsCache();
            if (cached is not null)
                return cached;

            var tools = ScanAllTools();
            SetAllToolsCache(tools);
            return tools;
        }
    }

    /// <summary>真实扫描整个 Tools 树（各分类并行），供 single-flight 使用。</summary>
    private static IReadOnlyList<ToolItem> ScanAllTools()
    {
        if (!Directory.Exists(ToolsRoot))
            return [];

        var categories = GetCategories();
        var perCategory = new List<ToolItem>[categories.Count];
        Parallel.For(0, categories.Count, i => perCategory[i] = GetTools(categories[i]).ToList());
        return DeduplicateAllTools([.. perCategory.SelectMany(c => c)]);
    }

    /// <summary>
    /// 对各分类的原始扫描结果做跨分类去重，生成「全部工具」一览。
    /// 同名工具（含 tools.json 跨分类副本）只保留一份，并把该名称出现的所有分类合并到 Categories 上。
    /// </summary>
    private static IReadOnlyList<ToolItem> DeduplicateAllTools(List<ToolItem> allItems)
    {
        var nameToCategories = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in allItems)
        {
            if (!nameToCategories.TryGetValue(item.Name, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                nameToCategories[item.Name] = set;
            }
            set.Add(item.Category);
            if (item.PrimaryCategory is not null)
                set.Add(item.PrimaryCategory);
            foreach (var c in item.Categories)
                set.Add(c);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<ToolItem>();
        foreach (var item in allItems)
        {
            var key = (item.PrimaryCategory ?? item.Category) + "|" + item.Name;
            if (seen.Add(key))
            {
                if (nameToCategories.TryGetValue(item.Name, out var cats) && cats.Count > 1)
                    item.SetCategories(cats.ToList());
                deduped.Add(item);
            }
        }

        SetAllToolsCache(deduped);
        return deduped;
    }

    /// <summary>
    /// 直读 tools.json + 并行全量扫描（single-flight 合并并发调用，结果进内存缓存）。
    /// 无磁盘缓存层：tools.json 是唯一元数据事实来源，扫描本身并行化保证启动速度。
    /// </summary>
    public static Task<IReadOnlyList<ToolItem>> GetAllToolsAsync()
    {
        return Task.Run(GetAllToolsCached);
    }

    /// <summary>仅供测试/工具模式使用，覆盖 Tools 根路径。传 null 恢复自动查找。</summary>
    public static void SetToolsRootForBuild(string? toolsRoot)
    {
        _cachedToolsRoot = toolsRoot;
        _toolsRootOverridden = toolsRoot is not null;
    }

    public static IReadOnlyList<string> GetAllTags()
    {
        if (_cachedTags is not null)
            return _cachedTags;

        var allTools = GetAllToolsCached();
        _cachedTags = allTools
            .SelectMany(t => t.Tags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .DistinctBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return _cachedTags;
    }

    /// <summary>工具集变化（增删/排序）后调用：清空内存缓存，下次访问按需重扫。不主动重扫。</summary>
    public static void OnToolsChanged()
    {
        lock (_cacheLock) { _toolsCache.Clear(); _cachedAllTools = null; _cachedAllToolsRoot = null; }
        Interlocked.Increment(ref _cacheVersion);
    }

    public static void InvalidateTagsCache()
    {
        _cachedTags = null;
        OnToolsChanged();
    }

    public static void RefreshToolsRoot()
    {
        _cachedToolsRoot = null;
        _toolsRootOverridden = false;
        InvalidateTagsCache();
        ToolsChanged?.Invoke();
    }

    public static IReadOnlyList<ToolItem> Search(string query, string? tag = null)
    {
        if (!Directory.Exists(ToolsRoot))
            return [];

        var normalizedQuery = query.Trim();
        if (normalizedQuery.Length == 0 && string.IsNullOrEmpty(tag))
            return [];

        var allTools = GetAllToolsCached();

        return allTools
            .Where(item =>
            {
                var matchesQuery = normalizedQuery.Length == 0 ||
                    item.Name.Contains(normalizedQuery, StringComparison.CurrentCultureIgnoreCase) ||
                    item.RelativePath.Contains(normalizedQuery, StringComparison.CurrentCultureIgnoreCase) ||
                    (item.Tags?.Any(t => t.Contains(normalizedQuery, StringComparison.CurrentCultureIgnoreCase)) ?? false);

                var matchesTag = string.IsNullOrEmpty(tag) ||
                    (item.Tags?.Any(t => t.Equals(tag, StringComparison.CurrentCultureIgnoreCase)) ?? false);

                return matchesQuery && matchesTag;
            })
            .ToList();
    }

    private static ToolItem CreateToolItemWithVariants(string category, string categoryRoot, string path, string toolDir)
    {
        var extension = Path.GetExtension(path);
        var rawFileName = GetDisplayName(path);
        var relativePath = Path.GetRelativePath(categoryRoot, path);
        var metadata = ToolMetadataService.GetMetadata(category, path);
        var isPlaceholder = !File.Exists(path) && (!string.IsNullOrWhiteSpace(metadata.DownloadUrl) || !string.IsNullOrWhiteSpace(metadata.WingetId));

        var primaryArch = DetectArch(Path.GetFileNameWithoutExtension(path));
        var archDisplay = FormatArchDisplay(primaryArch);

        var alternates = FindAllArchVariants(toolDir, path);

        var dirName = Path.GetFileName(toolDir);
        var hasArchVariants = alternates.Count > 0 || primaryArch is not null;
        var name = hasArchVariants ? dirName : rawFileName;

        var categoryRootDir = Path.Combine(ToolsRoot, category);
        if (Directory.Exists(categoryRootDir))
        {
            var strippedDir = StripArchSuffix(dirName);
            foreach (var otherDir in Directory.GetDirectories(categoryRootDir))
            {
                var otherName = Path.GetFileName(otherDir)!;
                if (otherName.Equals(dirName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var strippedOther = StripArchSuffix(otherName);
                if (!strippedOther.Equals(strippedDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                var otherLaunchable = FindPrimaryLaunchable(otherDir);
                if (otherLaunchable is null)
                    continue;

                var otherFileName = Path.GetFileNameWithoutExtension(otherLaunchable);
                var otherArch = DetectArch(otherFileName);
                if (otherArch is null)
                    continue;

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
            string? variantPath = null;

            if (!string.IsNullOrWhiteSpace(jv.File))
            {
                var candidate = System.IO.Path.Combine(toolDir, jv.File);
                if (File.Exists(candidate))
                    variantPath = candidate;
            }

            if (variantPath is null && !string.IsNullOrWhiteSpace(jv.Dir))
            {
                var altDir = System.IO.Path.Combine(categoryRootDir, jv.Dir);
                if (Directory.Exists(altDir))
                {
                    var altLaunchable = FindPrimaryLaunchable(altDir);
                    if (altLaunchable is not null)
                        variantPath = altLaunchable;
                }
            }

            if (variantPath is null)
                continue;

            if (variantPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                continue;

            if (alternates.Any(a => a.Path.Equals(variantPath, StringComparison.OrdinalIgnoreCase)))
                continue;

            var vName = System.IO.Path.GetFileNameWithoutExtension(variantPath);
            alternates.Add(new ArchVariant
            {
                Name = CleanupName(StripArchSuffix(vName)),
                Path = variantPath,
                Arch = jv.Arch ?? FormatArchDisplay(DetectArch(vName)) ?? "x86"
            });
        }

        var cleanName = CleanupName(StripArchSuffix(name));
        if (string.IsNullOrWhiteSpace(cleanName) || cleanName.Length < 3)
            cleanName = CleanupName(dirName);

        var remoteUrl = DetectRemoteUrl(path);

        var item = new ToolItem
        {
            Name = cleanName,
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
            RemoteUrl = remoteUrl,
            TutorialUrl = metadata.TutorialUrl,
            Tags = metadata.Tags ?? [],
            IsFavorite = isPlaceholder ? false : FavoritesService.IsFavorite(path),
            PrimaryArch = archDisplay.Length > 0 ? archDisplay : null,
            AlternateVersions = alternates,
            SortOrder = metadata.Order
        };
        item.InitArchOptions();
        return item;
    }

    private static ToolItem CreateToolItem(string category, string categoryRoot, string path)
    {
        var extension = Path.GetExtension(path);
        var rawFileName = GetDisplayName(path);
        var relativePath = Path.GetRelativePath(categoryRoot, path);
        var metadata = ToolMetadataService.GetMetadata(category, path);
        var isPlaceholder = !File.Exists(path) && (!string.IsNullOrWhiteSpace(metadata.DownloadUrl) || !string.IsNullOrWhiteSpace(metadata.WingetId));

        var primaryArch = DetectArch(Path.GetFileNameWithoutExtension(path));
        var toolDir = Path.GetDirectoryName(path);
        var dirName = toolDir is not null ? Path.GetFileName(toolDir) : rawFileName;
        var hasArchVariants = primaryArch is not null;
        var name = hasArchVariants ? dirName : rawFileName;

        var cleanName = CleanupName(StripArchSuffix(name));
        if (string.IsNullOrWhiteSpace(cleanName) || cleanName.Length < 3)
            cleanName = CleanupName(dirName);

        var item = new ToolItem
        {
            Name = cleanName,
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
            TutorialUrl = metadata.TutorialUrl,
            Tags = metadata.Tags ?? [],
            IsFavorite = isPlaceholder ? false : FavoritesService.IsFavorite(path),
            SortOrder = metadata.Order
        };
        item.InitArchOptions();
        return item;
    }

    private static bool IsLaunchable(string path)
    {
        var extension = Path.GetExtension(path);
        return LaunchableExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string[] ArchSuffixes =
    [
        "64", "32", "x64", "x86", "_x64", "_x86", "_64", "_32",
        "w64", "w32", "_Win64", "_Win32", "ARM64", "_ARM64"
    ];

    private static readonly string[] ArchX64Patterns =
    [
        "x64", "_x64", "64", "_64", "w64", "_Win64"
    ];

    private static readonly string[] ArchArm64Patterns =
    [
        "ARM64", "_ARM64", "arm64", "_arm64"
    ];

    private static readonly string[] Arch32Patterns =
    [
        "x86", "_x86", "32", "_32", "w32", "_Win32"
    ];

    /// <summary>已知多架构工具的文件名模式映射（用于跨目录关联）。</summary>
    private static readonly Dictionary<string, string[]> KnownMultiArchTools = new(StringComparer.OrdinalIgnoreCase)
    {
        // CPUZ: cpuz_x64.exe, cpuz_x32.exe, cpuz_arm64.exe
        ["cpuz"] = ["cpuz_x64.exe", "cpuz_x32.exe", "cpuz_arm64.exe", "cpuz64.exe", "cpuz32.exe"],
        // hwinfo: HWiNFO64.exe, HWiNFO32.exe, HWiNFO_ARM64.exe
        ["hwinfo"] = ["HWiNFO64.exe", "HWiNFO32.exe", "HWiNFO_ARM64.exe", "HWiNFO.exe"],
        // HWMonitor: HWMonitor_x64.exe, HWMonitor_x32.exe, hwmonitor_arm64.exe
        ["hwmonitor"] = ["HWMonitor_x64.exe", "HWMonitor_x32.exe", "hwmonitor_arm64.exe", "HWMonitor.exe"],
        // Dism++: Dism++x64.exe, Dism++x86.exe, Dism++ARM64.exe
        ["dism++"] = ["Dism++x64.exe", "Dism++x86.exe", "Dism++ARM64.exe", "Dism++.exe"],
        // CoreTemp: Core Temp x64.exe, Core Temp x86.exe
        ["coretemp"] = ["Core Temp x64.exe", "Core Temp x86.exe", "Core Temp.exe"],
        // CrystalDiskInfo: DiskInfo64S.exe, DiskInfo32S.exe
        ["crystaldiskinfo"] = ["DiskInfo64S.exe", "DiskInfo32S.exe", "DiskInfo.exe", "DiskInfo64.exe", "DiskInfo32.exe"],
        // BOOTICE: BOOTICEx64.exe, BOOTICEx86.exe
        ["bootice"] = ["BOOTICEx64.exe", "BOOTICEx86.exe", "BOOTICE.exe"],
        // bluescreenview: BlueScreenViewx64.exe, BlueScreenViewx86.exe
        ["bluescreenview"] = ["BlueScreenViewx64.exe", "BlueScreenViewx86.exe", "BlueScreenView.exe"],
        // AIDA64: aida64.exe (通常只有一个版本，但可能有bench64.dll等)
        ["aida64"] = ["aida64.exe", "aida64.exe.manifest"],
        // LinX: linpack_xeon64.exe, linpack_xeon32.exe
        ["linx"] = ["linpack_xeon64.exe", "linpack_xeon32.exe", "linpack_xeon.exe"]
    };

    /// <summary>
    /// Architecture of the host OS (not the running process). Using <c>OSArchitecture</c>
    /// instead of <c>ProcessArchitecture</c> ensures correct detection even when the app
    /// runs as x86/x64 under WOW64 or ARM64 emulation.
    /// </summary>
    private static Architecture OSArch => RuntimeInformation.OSArchitecture;

    /// <summary>
    /// Priority-ordered architecture names preferred on the current OS.
    /// ARM64 OS runs ARM64 natively and x64/x86 via emulation.
    /// x64 OS runs x64 natively and x86 via WOW64.
    /// x86 OS only runs x86.
    /// </summary>
    public static IReadOnlyList<string> PreferredArchPriority => OSArch switch
    {
        Architecture.Arm64 => ["ARM64", "x64", "x86"],
        Architecture.X64 => ["x64", "x86"],
        Architecture.X86 => ["x86"],
        _ => ["x64", "x86"]
    };

    /// <summary>
    /// Picks the <see cref="ArchOption"/> that best matches the current OS architecture.
    /// Falls back to <paramref name="fallback"/> (or the first option) when no known-arch
    /// option is compatible, so a tool with only an unknown/empty arch still resolves.
    /// </summary>
    public static ArchOption? PickPreferredArchOption(IReadOnlyList<ArchOption> options, ArchOption? fallback = null)
    {
        if (options.Count == 0) return fallback;

        foreach (var arch in PreferredArchPriority)
        {
            var match = options.FirstOrDefault(o => o.Arch.Equals(arch, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return fallback ?? options[0];
    }

    internal static string? DetectArch(string name)
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

    internal static string FormatArchDisplay(string? arch)
    {
        return arch switch
        {
            "ARM64" => "ARM64",
            "x64" or "Win64" => "x64",
            "x86" or "Win32" => "x86",
            _ => arch ?? ""
        };
    }

    private static List<ArchVariant> FindAllArchVariants(string toolDir, string? primaryPath)
    {
        var variants = new List<ArchVariant>();
        var dirName = Path.GetFileName(toolDir);
        var primaryExt = primaryPath is not null ? Path.GetExtension(primaryPath) : null;

        // 1. 同目录内的架构变体
        var allLaunchables = Directory.EnumerateFiles(toolDir, "*", SearchOption.AllDirectories)
            .Where(IsLaunchable)
            .ToList();

        foreach (var filePath in allLaunchables)
        {
            if (filePath.Equals(primaryPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (primaryExt is not null && !Path.GetExtension(filePath).Equals(primaryExt, StringComparison.OrdinalIgnoreCase))
                continue;

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var arch = DetectArch(fileName);
            if (arch is null)
                continue;

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

        // 2. 使用已知多架构工具映射表查找跨目录变体
        if (primaryPath is not null)
        {
            var primaryFileName = Path.GetFileName(primaryPath);
            var primaryBaseName = Path.GetFileNameWithoutExtension(primaryPath);

            // 查找当前工具在映射表中的条目
            foreach (var kvp in KnownMultiArchTools)
            {
                // 检查主文件是否匹配映射表中的任何模式
                if (kvp.Value.Any(pattern => primaryFileName.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
                {
                    // 在同分类目录下查找其他架构版本
                    var categoryRoot = Path.GetDirectoryName(toolDir);
                    if (categoryRoot is not null && Directory.Exists(categoryRoot))
                    {
                        foreach (var otherDir in Directory.GetDirectories(categoryRoot))
                        {
                            if (otherDir.Equals(toolDir, StringComparison.OrdinalIgnoreCase))
                                continue;

                            var otherDirName = Path.GetFileName(otherDir);
                            // 检查目录名是否匹配（去除架构后缀后）
                            var strippedOther = StripArchSuffix(otherDirName);
                            var strippedCurrent = StripArchSuffix(dirName);
                            if (!strippedOther.Equals(strippedCurrent, StringComparison.OrdinalIgnoreCase))
                                continue;

                            // 在该目录中查找匹配的架构版本
                            foreach (var pattern in kvp.Value)
                            {
                                var candidatePath = Path.Combine(otherDir, pattern);
                                if (File.Exists(candidatePath) && IsLaunchable(candidatePath))
                                {
                                    var candidateFileName = Path.GetFileNameWithoutExtension(candidatePath);
                                    var candidateArch = DetectArch(candidateFileName);
                                    if (candidateArch is not null && !variants.Any(v => v.Path.Equals(candidatePath, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        variants.Add(new ArchVariant
                                        {
                                            Name = CleanupName(StripArchSuffix(candidateFileName)),
                                            Path = candidatePath,
                                            Arch = FormatArchDisplay(candidateArch)
                                        });
                                    }
                                }
                            }
                        }
                    }
                    break;
                }
            }
        }

        return variants;
    }

    private static string? FindPrimaryLaunchable(string toolDir)
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
            if (deepTarget is not null)
                return deepTarget;
        }

        var allLaunchables = Directory.EnumerateFiles(toolDir, "*", SearchOption.AllDirectories)
            .Where(IsLaunchable)
            .ToList();

        if (allLaunchables.Count == 0)
            return null;

        if (allLaunchables.Count == 1)
            return allLaunchables[0];

        var directLaunchables = Directory.EnumerateFiles(toolDir)
            .Where(IsLaunchable)
            .ToList();

        var match = directLaunchables.FirstOrDefault(f =>
            Path.GetFileNameWithoutExtension(f).Equals(dirName, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
            return match;

        var archCandidates = directLaunchables
            .Where(f => StripArchSuffix(Path.GetFileNameWithoutExtension(f))
                .Equals(StripArchSuffix(dirName), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (archCandidates.Count > 0)
            return PickPreferredArch(archCandidates);

        match = allLaunchables.FirstOrDefault(f =>
            Path.GetFileNameWithoutExtension(f).Equals(dirName, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
            return match;

        archCandidates = allLaunchables
            .Where(f => StripArchSuffix(Path.GetFileNameWithoutExtension(f))
                .Equals(StripArchSuffix(dirName), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (archCandidates.Count > 0)
            return PickPreferredArch(archCandidates);

        if (directLaunchables.Count > 0)
            return directLaunchables[0];

        return allLaunchables[0];
    }

    private static string PickPreferredArch(List<string> candidates)
    {
        foreach (var arch in PreferredArchPriority)
        {
            var patterns = arch switch
            {
                "ARM64" => ArchArm64Patterns,
                "x64" => ArchX64Patterns,
                _ => Arch32Patterns
            };
            var match = candidates.FirstOrDefault(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                return patterns.Any(p => name.EndsWith(p, StringComparison.OrdinalIgnoreCase));
            });
            if (match is not null) return match;
        }

        return candidates[0];
    }

    internal static string StripArchSuffix(string name)
    {
        foreach (var suffix in ArchSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^suffix.Length];
            }
        }

        return name;
    }

    internal static string CleanupName(string name)
    {
        return name
            .Replace("_x64", " x64", StringComparison.OrdinalIgnoreCase)
            .Replace("_x86", " x86", StringComparison.OrdinalIgnoreCase)
            .Replace("_ARM64", " ARM64", StringComparison.OrdinalIgnoreCase)
            .Replace("_arm64", " ARM64", StringComparison.OrdinalIgnoreCase)
            .Replace("_", " ");
    }

    private static string GetDisplayName(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (!fileName.Equals("start", StringComparison.OrdinalIgnoreCase))
        {
            return fileName;
        }

        var parentName = Directory.GetParent(path)?.Name;
        return string.IsNullOrWhiteSpace(parentName) ? fileName : parentName;
    }

    private static string CreatePlaceholderPath(string toolDir)
    {
        var dirName = Path.GetFileName(toolDir);
        return Path.Combine(toolDir, dirName + ".exe");
    }

     private static string FindToolsRoot()
     {
         if (RuntimeHelper.IsMsixPackaged)
         {
             return Path.Combine(
                 RuntimeHelper.GetLocalAppDataRoot(),
                 "TubaWinUi3", "Tools");
         }

         var outputTools = Path.Combine(AppDirectory, "Tools");
         if (Directory.Exists(outputTools))
         {
             return outputTools;
         }

         var directory = new DirectoryInfo(AppDirectory);
         while (directory is not null)
         {
             var candidate = Path.Combine(directory.FullName, "Tools");
             if (Directory.Exists(candidate))
             {
                 return candidate;
             }

             directory = directory.Parent;
         }

         return outputTools;
     }

    internal static string? DetectRemoteUrl(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!File.Exists(filePath))
            return null;

        try
        {
            var lines = File.ReadAllLines(filePath);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("::", StringComparison.Ordinal) ||
                    line.StartsWith("@", StringComparison.Ordinal))
                    line = line.StartsWith("@", StringComparison.Ordinal) ? line[1..].Trim() : line[3..].Trim();

                if (!line.StartsWith("start ", StringComparison.OrdinalIgnoreCase))
                    continue;

                var argPart = line[6..].Trim();
                if (argPart.Length == 0) continue;

                if (argPart.StartsWith('"'))
                {
                    var closing = argPart.IndexOf('"', 1);
                    if (closing > 0)
                        argPart = argPart[(closing + 1)..].Trim();
                }

                if (argPart.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    argPart.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return argPart.Split(' ', 2)[0];
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 内置工具挂载（tools.json builtin 字段）：Path 用虚拟目录 ToolsRoot/分类/目录名，
    /// 与旧 link.json 目录键完全一致，收藏与拖拽排序的持久化键不受迁移影响。
    /// </summary>
    private static ToolItem? CreateBuiltinPlacedItem(ToolMetadataService.CategoryPlacement placement, string category)
    {
        var builtinTool = BuiltinToolRegistry.GetById(placement.BuiltinId!);
        if (builtinTool is null) return null;

        var dirName = !string.IsNullOrWhiteSpace(placement.Match) ? placement.Match : builtinTool.Name;
        var placedDir = Path.Combine(ToolsRoot, category, dirName);
        var kindText = builtinTool.Kind switch
        {
            BuiltinToolKind.Dialog => "弹窗",
            BuiltinToolKind.BackgroundTask => "后台任务",
            BuiltinToolKind.ProgressTask => "进度任务",
            BuiltinToolKind.InstantAction => "即时操作",
            _ => "内置"
        };

        return new ToolItem
        {
            Name = builtinTool.Name,
            Category = category,
            Path = placedDir,
            RelativePath = Path.GetRelativePath(ToolsRoot, placedDir),
            Extension = "内置",
            IconGlyph = builtinTool.Glyph,
            Description = builtinTool.Description,
            IsFavorite = FavoritesService.IsFavorite(placedDir),
            IsBuiltinLink = true,
            BuiltinToolId = builtinTool.Id,
            BuiltinKindText = kindText,
            Tags = [],
            SortOrder = placement.Order
        };
    }

    /// <summary>
    /// 跨分类副本（tools.json category+categories 字段）：以主分类的物理目录生成完整工具项，
    /// 标记 IsLinked 并组成多分类（替代旧 link.json 的 target 链接）。
    /// </summary>
    private static ToolItem? CreateCategoryCopyItem(ToolMetadataService.CategoryPlacement placement, string category)
    {
        var primaryCategory = placement.PrimaryCategory!;
        var categoryRoot = Path.Combine(ToolsRoot, primaryCategory);
        if (!Directory.Exists(categoryRoot)) return null;

        // 主分类下定位物理目录：相对路径含 match 或目录名灵活匹配（与 FindJsonMetadataByDir 同规则）。
        // 多候选时评分择优：目录名精确等于 match > 灵活匹配相等 > 相对路径包含，
        // 避免 memtest / memtest64 / memtestpro 这类前缀重叠目录误配。
        var toolDir = Directory.GetDirectories(categoryRoot)
            .Select(d => new
            {
                Dir = d,
                Score =
                    string.Equals(Path.GetFileName(d), placement.Match, StringComparison.OrdinalIgnoreCase) ? 3 :
                    string.Equals(
                        Path.GetFileName(d).Replace(" ", "").Replace("-", "").Replace("_", ""),
                        placement.Match.Replace(" ", "").Replace("-", "").Replace("_", ""),
                        StringComparison.OrdinalIgnoreCase) ? 2 :
                    Path.GetRelativePath(ToolsRoot, d).Contains(placement.Match, StringComparison.CurrentCultureIgnoreCase) ||
                    ToolMetadataService.MatchesFlexible(Path.GetFileName(d), placement.Match) ? 1 : 0
            })
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            .ThenBy(c => Path.GetFileName(c.Dir), StringComparer.OrdinalIgnoreCase)
            .Select(c => c.Dir)
            .FirstOrDefault();
        if (toolDir is null) return null;

        var launchable = FindPrimaryLaunchable(toolDir);
        if (launchable is null && !ToolMetadataService.HasDownloadUrl(primaryCategory, toolDir))
            return null;

        var baseItem = CreateToolItemWithVariants(
            primaryCategory,
            categoryRoot,
            launchable ?? CreatePlaceholderPath(toolDir),
            toolDir);

        var categories = placement.Categories
            .Concat([primaryCategory])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ToolItem
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
            RemoteUrl = baseItem.RemoteUrl,
            TutorialUrl = baseItem.TutorialUrl,
            Tags = baseItem.Tags,
            IsFavorite = baseItem.IsFavorite,
            PrimaryArch = baseItem.PrimaryArch,
            AlternateVersions = baseItem.AlternateVersions,
            SortOrder = placement.Order ?? baseItem.SortOrder
        };
    }
}
