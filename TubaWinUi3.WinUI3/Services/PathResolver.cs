namespace TubaWinUi3.Services;

public static class PathResolver
{
    private static readonly (string Placeholder, Func<string> Resolver)[] PlaceholderResolvers =
    [
        ("{AppDir}", () => ToolCatalog.AppDirectory),
        ("{ParentDir}", () => Path.GetDirectoryName(ToolCatalog.AppDirectory) ?? ToolCatalog.AppDirectory),
        ("{ToolsRoot}", () => ToolCatalog.ToolsRoot),
        ("{UserToolsRoot}", () => ToolCatalog.UserToolsRoot),
        ("{DataDir}", () => ConfigManager.GetDataDir()),
        ("{AppDataDir}", () => Path.Combine(
            RuntimeHelper.GetLocalAppDataRoot(),
            "TubaWinUi3")),
    ];

    public static string ExpandPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path ?? "";
        if (!path.Contains('{')) return path;

        var result = path;
        foreach (var (placeholder, resolver) in PlaceholderResolvers)
        {
            if (result.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var resolved = resolver();
                    result = result.Replace(placeholder, resolved, StringComparison.OrdinalIgnoreCase);
                }
                catch { }
            }
        }

        return result;
    }

    public static string MakeRelative(string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath)) return absolutePath ?? "";
        if (absolutePath.Contains('{')) return absolutePath;

        var bestPlaceholder = "";
        var bestRelative = absolutePath;
        var bestLength = int.MaxValue;

        foreach (var (placeholder, resolver) in PlaceholderResolvers)
        {
            try
            {
                var basePath = resolver();
                if (string.IsNullOrEmpty(basePath)) continue;

                if (!absolutePath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                var relative = absolutePath[basePath.Length..];
                if (relative.Length > 0 && (relative[0] == Path.DirectorySeparatorChar || relative[0] == Path.AltDirectorySeparatorChar))
                    relative = relative[1..];
                else if (relative.Length > 0)
                    continue; // 无路径分隔符边界（如 ToolsExtra），不视为该基准之下

                if (relative.Length < bestLength)
                {
                    bestLength = relative.Length;
                    bestPlaceholder = placeholder;
                    bestRelative = placeholder + Path.DirectorySeparatorChar + relative;
                }
            }
            catch { }
        }

        return bestRelative;
    }

    public static string MakeAbsolute(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return storedPath ?? "";
        if (!storedPath.Contains('{')) return storedPath;
        return ExpandPath(storedPath);
    }

    public static string MakeConfigDirRelative(string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath)) return absolutePath ?? "";
        if (absolutePath.Contains('{')) return absolutePath;

        try
        {
            var dataDir = ConfigManager.GetDataDir();
            if (!string.IsNullOrEmpty(dataDir) &&
                absolutePath.StartsWith(dataDir, StringComparison.OrdinalIgnoreCase))
            {
                var relative = absolutePath[dataDir.Length..];
                if (relative.Length > 0 && (relative[0] == Path.DirectorySeparatorChar || relative[0] == Path.AltDirectorySeparatorChar))
                    relative = relative[1..];
                else if (relative.Length > 0)
                    return MakeRelative(absolutePath); // 无路径分隔符边界，不属于数据目录
                return "{DataDir}" + Path.DirectorySeparatorChar + relative;
            }
        }
        catch { }

        return MakeRelative(absolutePath);
    }

    public static List<string> MigratePaths(List<string> paths)
    {
        return paths.Select(p => MakeRelative(p)).ToList();
    }

    public static List<string> ExpandPaths(List<string> paths)
    {
        return paths.Select(p => MakeAbsolute(p)).ToList();
    }

    public static string[] GetAvailablePlaceholders()
    {
        return PlaceholderResolvers.Select(p => p.Placeholder).ToArray();
    }

    public static string GetPlaceholderDescription(string placeholder) => placeholder switch
    {
        "{AppDir}" => "程序所在目录",
        "{ParentDir}" => "程序上一级目录",
        "{ToolsRoot}" => "工具目录 (Tools/)",
        "{UserToolsRoot}" => "用户工具目录 (UserTools/)",
        "{DataDir}" => "当前配置数据目录",
        "{AppDataDir}" => "AppData 目录 (%LocalAppData%\\TubaWinUi3\\)",
        _ => placeholder
    };

    public static string GetPlaceholderExample(string placeholder) => placeholder switch
    {
        "{AppDir}" => "{AppDir}\\Data",
        "{ParentDir}" => "{ParentDir}\\TubaConfig",
        "{ToolsRoot}" => "{ToolsRoot}\\..\\Config",
        "{UserToolsRoot}" => "{UserToolsRoot}\\其他工具",
        "{DataDir}" => "{DataDir}\\Backgrounds",
        "{AppDataDir}" => "{AppDataDir}\\settings.json",
        _ => placeholder
    };
}
