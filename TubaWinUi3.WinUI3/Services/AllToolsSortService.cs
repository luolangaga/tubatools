using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>全部工具的派生顺序，不改写分类页的自定义顺序或目录缓存。</summary>
internal static class AllToolsSortService
{
    internal static IReadOnlyList<ToolItem> Sort(IEnumerable<ToolItem> tools,
        IReadOnlyList<LaunchRecord> history)
    {
        var lastUsed = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in history)
            if (!lastUsed.TryGetValue(record.Path, out var previous) || record.LastLaunched > previous)
                lastUsed[record.Path] = record.LastLaunched;

        // 未使用过或最后使用时间相同的工具沿用原顺序：内置在前，组内保持目录顺序。
        return tools.OrderByDescending(LastUsed)
            .ThenByDescending(t => t.IsBuiltinLink)
            .ToList();

        DateTime LastUsed(ToolItem tool)
        {
            var latest = lastUsed.GetValueOrDefault(tool.Path);
            // 内置工具可挂载到多个分类，历史可能来自另一分类的虚拟目录。
            if (tool.IsBuiltinLink)
            {
                var categoryDir = Path.GetDirectoryName(tool.Path);
                var root = categoryDir is null ? null : Path.GetDirectoryName(categoryDir);
                if (root is not null)
                    foreach (var category in tool.Categories)
                    {
                        var alias = Path.Combine(root, category, Path.GetFileName(tool.Path));
                        var used = lastUsed.GetValueOrDefault(alias);
                        if (used > latest) latest = used;
                    }
            }
            return latest;
        }
    }
}
