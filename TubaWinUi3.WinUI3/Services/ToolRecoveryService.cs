namespace TubaWinUi3.Services;

public sealed record RecoverableTool(string Directory, string Category, IReadOnlyList<string> Executables)
{
    public override string ToString() => Category + " / " + Path.GetFileName(Directory);
}

public static class ToolRecoveryService
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".bat", ".cmd", ".lnk", ".msc", ".ps1", ".vbs" };

    /// <summary>只把分类下的工具目录作为候选；不把依赖/辅助 exe 自动登记成多张卡片。</summary>
    public static IReadOnlyList<RecoverableTool> FindUnregistered()
    {
        var result = new List<RecoverableTool>();
        var roots = new[] { ToolCatalog.ToolsRoot,
            Path.Combine(ToolCatalog.UserToolsRoot, "Custom"),
            Path.Combine(ToolCatalog.UserToolsRoot, "Community") };
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var category in Directory.GetDirectories(root))
            foreach (var directory in Directory.GetDirectories(category))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                if (ToolMetadataService.FindJsonMetadataByDir(directory) is not null) continue;
                var executables = Directory.EnumerateFiles(directory, "*", new EnumerationOptions
                    { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true })
                    .Where(p => Extensions.Contains(Path.GetExtension(p)))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
                if (executables.Count == 0 || executables.Any(p => ToolMetadataService.FindJsonMetadata(p) is not null)) continue;
                result.Add(new RecoverableTool(directory, Path.GetFileName(category), executables));
            }
        }
        return result;
    }

    public static void Register(RecoverableTool tool, string name, string executable)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("工具名称不能为空。");
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || !tool.Executables.Any(p => Path.GetFullPath(p).Equals(executable, StringComparison.OrdinalIgnoreCase)) ||
            !UserToolLibrary.IsWithin(executable, tool.Directory))
            throw new ArgumentException("请选择工具目录中的主程序。");
        ToolMetadataService.UpsertToolMetadataEntry(Path.GetFileName(tool.Directory), name: name,
            launchTarget: Path.GetRelativePath(tool.Directory, executable),
            toolDirectory: tool.Directory, category: tool.Category);
        ToolCatalog.InvalidateTagsCache();
    }
}
