using System.Runtime.InteropServices;
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

        var orderJson = AppSettings.Get("CategoryOrder");
        List<string>? ordered = null;
        if (!string.IsNullOrWhiteSpace(orderJson))
        {
            try
            {
                ordered = System.Text.Json.JsonSerializer.Deserialize(orderJson, TubaDefaultContext.Default.ListString);
            }
            catch { }
        }

        if (ordered is not null && ordered.Count > 0)
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
            if (_toolsCache.TryGetValue(category, out var cached))
                return cached;
        }

        var categoryRoot = Path.Combine(ToolsRoot, category);
        if (!Directory.Exists(categoryRoot))
        {
            lock (_cacheLock) { _toolsCache[category] = []; }
            return [];
        }

        var toolDirs = Directory.GetDirectories(categoryRoot).ToList();
        var merged = MergeArchDirectories(toolDirs);

        var items = new List<ToolItem>();
        foreach (var toolDir in merged)
        {
            var linkInfo = TryResolveLink(toolDir);
            if (linkInfo is not null)
            {
                var linkedItem = CreateLinkedToolItem(category, categoryRoot, toolDir, linkInfo);
                if (linkedItem is not null)
                    items.Add(linkedItem);
            }
            else
            {
                var launchable = FindPrimaryLaunchable(toolDir);
                if (launchable is not null || ToolMetadataService.HasDownloadUrl(category, toolDir))
                    items.Add(CreateToolItemWithVariants(category, categoryRoot, launchable ?? CreatePlaceholderPath(toolDir), toolDir));
            }
        }

        var toolOrderJson = AppSettings.Get($"ToolOrder_{category}");
        List<string>? toolOrder = null;
        if (!string.IsNullOrWhiteSpace(toolOrderJson))
        {
            try
            {
                toolOrder = System.Text.Json.JsonSerializer.Deserialize(toolOrderJson, TubaDefaultContext.Default.ListString);
            }
            catch { }
        }

        IReadOnlyList<ToolItem> result;
        if (toolOrder is not null && toolOrder.Count > 0)
        {
            var orderedSet = new HashSet<string>(toolOrder, StringComparer.CurrentCultureIgnoreCase);
            var ordered = toolOrder
                .Where(name => items.Any(it => it.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
                .Select(name => items.First(it => it.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
                .ToList();
            foreach (var item in items.OrderBy(it => it.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!orderedSet.Contains(item.Name))
                    ordered.Add(item);
            }
            result = ordered;
        }
        else
        {
            result = items
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.RelativePath, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        lock (_cacheLock) { _toolsCache[category] = result; }
        return result;
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
    private static IReadOnlyList<ToolItem>? _cachedAllTools;
    private static readonly object _cacheLock = new();
    private static readonly Dictionary<string, IReadOnlyList<ToolItem>> _toolsCache = new(StringComparer.OrdinalIgnoreCase);
    private static int _cacheVersion;

    public static int CacheVersion => _cacheVersion;

    public static IReadOnlyList<ToolItem> GetAllToolsCached()
    {
        if (_cachedAllTools is not null)
            return _cachedAllTools;

        if (!Directory.Exists(ToolsRoot))
            return _cachedAllTools = [];

        var allItems = GetCategories().SelectMany(GetTools).ToList();

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

        _cachedAllTools = deduped;
        return _cachedAllTools;
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

    public static void InvalidateTagsCache()
    {
        _cachedTags = null;
        _cachedAllTools = null;
        lock (_cacheLock) { _toolsCache.Clear(); }
        Interlocked.Increment(ref _cacheVersion);
    }

    public static void RefreshToolsRoot()
    {
        _cachedToolsRoot = null;
        InvalidateTagsCache();
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
        var name = GetDisplayName(path);
        var relativePath = Path.GetRelativePath(categoryRoot, path);
        var metadata = ToolMetadataService.GetMetadata(category, path);
        var isPlaceholder = !File.Exists(path) && (!string.IsNullOrWhiteSpace(metadata.DownloadUrl) || !string.IsNullOrWhiteSpace(metadata.WingetId));

        var primaryArch = DetectArch(Path.GetFileNameWithoutExtension(path));
        var archDisplay = FormatArchDisplay(primaryArch);

        var alternates = FindAllArchVariants(toolDir, path);

        var categoryRootDir = Path.Combine(ToolsRoot, category);
        if (Directory.Exists(categoryRootDir))
        {
            var dirName = Path.GetFileName(toolDir);
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
        if (string.IsNullOrWhiteSpace(cleanName))
            cleanName = CleanupName(name);

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
            Tags = metadata.Tags ?? [],
            IsFavorite = isPlaceholder ? false : FavoritesService.IsFavorite(path),
            PrimaryArch = archDisplay.Length > 0 ? archDisplay : null,
            AlternateVersions = alternates
        };
        item.InitArchOptions();
        return item;
    }

    private static ToolItem CreateToolItem(string category, string categoryRoot, string path)
    {
        var extension = Path.GetExtension(path);
        var name = GetDisplayName(path);
        var relativePath = Path.GetRelativePath(categoryRoot, path);
        var metadata = ToolMetadataService.GetMetadata(category, path);
        var isPlaceholder = !File.Exists(path) && (!string.IsNullOrWhiteSpace(metadata.DownloadUrl) || !string.IsNullOrWhiteSpace(metadata.WingetId));

        var item = new ToolItem
        {
            Name = CleanupName(StripArchSuffix(name)),
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
            Tags = metadata.Tags ?? [],
            IsFavorite = isPlaceholder ? false : FavoritesService.IsFavorite(path)
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
        "x64", "_x64", "w64", "_Win64"
    ];

    private static readonly string[] ArchArm64Patterns =
    [
        "ARM64", "_ARM64", "arm64", "_arm64"
    ];

    private static readonly string[] Arch32Patterns =
    [
        "x86", "_x86", "32", "_32", "w32", "_Win32"
    ];

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

    private static string? DetectArch(string name)
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

    private static string FormatArchDisplay(string? arch)
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

    private static string StripArchSuffix(string name)
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

    private static string CleanupName(string name)
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
                 Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
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

    private sealed class LinkInfo
    {
        public required string TargetRelativePath { get; init; }
        public required string TargetFullPath { get; init; }
    }

    private static LinkInfo? TryResolveLink(string toolDir)
    {
        var linkPath = Path.Combine(toolDir, "link.json");
        if (!File.Exists(linkPath)) return null;

        var files = Directory.GetFiles(toolDir);
        var dirs = Directory.GetDirectories(toolDir);
        if (files.Length != 1 || dirs.Length != 0) return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(linkPath));
            if (doc.RootElement.TryGetProperty("target", out var targetVal))
            {
                var target = targetVal.GetString();
                if (string.IsNullOrWhiteSpace(target)) return null;
                var targetFull = Path.Combine(ToolsRoot, target);
                if (!Directory.Exists(targetFull)) return null;
                return new LinkInfo { TargetRelativePath = target, TargetFullPath = targetFull };
            }
        }
        catch { }

        return null;
    }

    private static ToolItem? CreateLinkedToolItem(string category, string categoryRoot, string linkDir, LinkInfo linkInfo)
    {
        var targetLaunchable = FindPrimaryLaunchable(linkInfo.TargetFullPath);
        if (targetLaunchable is null && !ToolMetadataService.HasDownloadUrl(category, linkInfo.TargetFullPath))
            return null;

        var primaryCategory = Path.GetFileName(Path.GetDirectoryName(linkInfo.TargetRelativePath)) ?? category;
        var baseItem = CreateToolItemWithVariants(
            primaryCategory,
            Path.GetDirectoryName(linkInfo.TargetFullPath) ?? linkInfo.TargetFullPath,
            targetLaunchable ?? CreatePlaceholderPath(linkInfo.TargetFullPath),
            linkInfo.TargetFullPath);

        var categories = new List<string> { primaryCategory, category }
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

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
            Tags = baseItem.Tags,
            IsFavorite = baseItem.IsFavorite,
            PrimaryArch = baseItem.PrimaryArch,
            AlternateVersions = baseItem.AlternateVersions
        };
    }
}
