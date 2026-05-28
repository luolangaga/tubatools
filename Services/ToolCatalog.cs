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

    public static string ToolsRoot => FindToolsRoot();

    public static IReadOnlyList<string> GetCategories()
    {
        if (!Directory.Exists(ToolsRoot))
        {
            return [];
        }

        return Directory.GetDirectories(ToolsRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Cast<string>()
            .ToList();
    }

    public static IReadOnlyList<ToolItem> GetTools(string? category)
    {
        if (string.IsNullOrWhiteSpace(category) || !Directory.Exists(ToolsRoot))
        {
            return [];
        }

        var categoryRoot = Path.Combine(ToolsRoot, category);
        if (!Directory.Exists(categoryRoot))
        {
            return [];
        }

        var toolDirs = Directory.GetDirectories(categoryRoot).ToList();
        var merged = MergeArchDirectories(toolDirs);

        return merged
            .Select(toolDir => (toolDir, launchable: FindPrimaryLaunchable(toolDir)))
            .Where(x => x.launchable is not null || ToolMetadataService.HasDownloadUrl(category, x.toolDir))
            .Select(x => CreateToolItemWithVariants(category, categoryRoot, x.launchable ?? CreatePlaceholderPath(x.toolDir), x.toolDir))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.RelativePath, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
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
        if (!Directory.Exists(ToolsRoot))
            return [];

        return GetCategories()
            .SelectMany(GetTools)
            .Skip(skip)
            .Take(take)
            .ToList();
    }

    public static int GetAllToolsCount()
    {
        if (!Directory.Exists(ToolsRoot))
            return 0;

        return GetCategories()
            .Sum(c => GetTools(c).Count);
    }

    public static IReadOnlyList<ToolItem> Search(string query)
    {
        if (!Directory.Exists(ToolsRoot))
        {
            return [];
        }

        var normalizedQuery = query.Trim();
        if (normalizedQuery.Length == 0)
        {
            return [];
        }

        return GetCategories()
            .SelectMany(GetTools)
            .Where(item =>
                item.Name.Contains(normalizedQuery, StringComparison.CurrentCultureIgnoreCase) ||
                item.RelativePath.Contains(normalizedQuery, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    private static ToolItem CreateToolItemWithVariants(string category, string categoryRoot, string path, string toolDir)
    {
        var extension = Path.GetExtension(path);
        var name = GetDisplayName(path);
        var relativePath = Path.GetRelativePath(categoryRoot, path);
        var metadata = ToolMetadataService.GetMetadata(category, path);
        var isPlaceholder = !File.Exists(path) && !string.IsNullOrWhiteSpace(metadata.DownloadUrl);

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
                    Name = CleanupName(otherFileName),
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
                Name = CleanupName(vName),
                Path = variantPath,
                Arch = jv.Arch ?? FormatArchDisplay(DetectArch(vName)) ?? "x86"
            });
        }

        var cleanName = CleanupName(StripArchSuffix(name));
        if (string.IsNullOrWhiteSpace(cleanName))
            cleanName = CleanupName(name);

        return new ToolItem
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
            IsFavorite = isPlaceholder ? false : FavoritesService.IsFavorite(path),
            PrimaryArch = archDisplay.Length > 0 ? archDisplay : null,
            AlternateVersions = alternates
        };
    }

    private static ToolItem CreateToolItem(string category, string categoryRoot, string path)
    {
        var extension = Path.GetExtension(path);
        var name = GetDisplayName(path);
        var relativePath = Path.GetRelativePath(categoryRoot, path);
        var metadata = ToolMetadataService.GetMetadata(category, path);
        var isPlaceholder = !File.Exists(path) && !string.IsNullOrWhiteSpace(metadata.DownloadUrl);

        return new ToolItem
        {
            Name = CleanupName(name),
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
            IsFavorite = isPlaceholder ? false : FavoritesService.IsFavorite(path)
        };
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

    private static readonly string[] Arch64Patterns =
    [
        "x64", "_x64", "64", "_64", "w64", "_Win64", "ARM64", "_ARM64"
    ];

    private static readonly string[] Arch32Patterns =
    [
        "x86", "_x86", "32", "_32", "w32", "_Win32"
    ];

    private static bool Is64BitOS => Environment.Is64BitOperatingSystem;

    private static string? DetectArch(string name)
    {
        foreach (var p in Arch64Patterns)
        {
            if (name.EndsWith(p, StringComparison.OrdinalIgnoreCase))
                return p.TrimStart('_') is "Win64" or "ARM64" ? p.TrimStart('_') : "x64";
        }
        foreach (var p in Arch32Patterns)
        {
            if (name.EndsWith(p, StringComparison.OrdinalIgnoreCase))
                return p.TrimStart('_') is "Win32" ? "x86" : "x86";
        }
        return null;
    }

    private static string FormatArchDisplay(string? arch)
    {
        return arch switch
        {
            "x64" or "Win64" or "ARM64" => "x64",
            "x86" or "Win32" => "x86",
            _ => arch ?? ""
        };
    }

    private static List<ArchVariant> FindAllArchVariants(string toolDir, string? primaryPath)
    {
        var variants = new List<ArchVariant>();
        var dirName = Path.GetFileName(toolDir);

        var allLaunchables = Directory.EnumerateFiles(toolDir, "*", SearchOption.AllDirectories)
            .Where(IsLaunchable)
            .ToList();

        foreach (var filePath in allLaunchables)
        {
            if (filePath.Equals(primaryPath, StringComparison.OrdinalIgnoreCase))
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
                Name = CleanupName(fileName),
                Path = filePath,
                Arch = FormatArchDisplay(arch)
            });
        }

        return variants;
    }

    private static string? FindPrimaryLaunchable(string toolDir)
    {
        var dirName = Path.GetFileName(toolDir);

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
        if (Is64BitOS)
        {
            var x64 = candidates.FirstOrDefault(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                return Arch64Patterns.Any(p => name.EndsWith(p, StringComparison.OrdinalIgnoreCase));
            });
            if (x64 is not null)
                return x64;
        }
        else
        {
            var x86 = candidates.FirstOrDefault(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                return Arch32Patterns.Any(p => name.EndsWith(p, StringComparison.OrdinalIgnoreCase));
            });
            if (x86 is not null)
                return x86;
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
        var outputTools = Path.Combine(AppContext.BaseDirectory, "Tools");
        if (Directory.Exists(outputTools))
        {
            return outputTools;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
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
}
