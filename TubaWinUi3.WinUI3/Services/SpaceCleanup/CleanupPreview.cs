using System.Text;
using FluentCleaner.Models;
using FluentCleaner.Services;

namespace TubaWinUi3.Services.SpaceCleanup;

/// <summary>一条清理规则要删除的目标位置（FileKey 展开后的实际目录）。</summary>
public sealed record CleanupTargetInfo(string Path, string Pattern, string Scope, int FileCount);

/// <summary>按目录分组的待删除文件，供预览对话框展开查看。</summary>
public sealed record CleanupDirectoryGroup(string Directory, IReadOnlyList<string> Files, int TotalFiles);

/// <summary>
/// 把清理扫描结果整理成「目标目录 + 文件明细」的展示数据。
/// 纯数据处理，便于测试；对话框只负责排版。
/// </summary>
public static class CleanupPreview
{
    public const int MaxTargets = 50;
    public const int MaxGroups = 40;
    public const int MaxFilesPerGroup = 100;
    public const int MaxRegistry = 100;

    public static IReadOnlyList<CleanupTargetInfo> GetTargets(ScanResult result)
    {
        var expander = new PathExpander();
        var files = result.FilesToDelete;
        var targets = new List<CleanupTargetInfo>();
        foreach (var key in result.Entry.FileKeys)
        {
            string expanded;
            try { expanded = expander.ExpandVariables(key.Path); }
            catch { continue; }
            if (string.IsNullOrWhiteSpace(expanded)) continue;

            var literal = LiteralPrefix(expanded);
            if (literal.Length == 0) continue;

            var scope = key.Flag switch
            {
                FileKeyFlag.Recurse => "包含子目录",
                FileKeyFlag.RemoveSelf => "包含子目录并删除空目录",
                _ => "仅当前目录",
            };
            targets.Add(new CleanupTargetInfo(literal, key.Pattern, scope,
                files.Count(f => IsUnder(f, literal))));
        }

        return targets
            .GroupBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(t => t.FileCount).First())
            .OrderByDescending(t => t.FileCount)
            .ThenBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxTargets)
            .ToList();
    }

    public static IReadOnlyList<CleanupDirectoryGroup> GetDirectoryGroups(ScanResult result)
        => result.FilesToDelete
            .GroupBy(f => Path.GetDirectoryName(f) ?? f, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CleanupDirectoryGroup(
                g.Key,
                g.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxFilesPerGroup)
                    .Select(f => Path.GetFileName(f) ?? f)
                    .ToList(),
                g.Count()))
            .OrderByDescending(g => g.TotalFiles)
            .ThenBy(g => g.Directory, StringComparer.OrdinalIgnoreCase)
            .Take(MaxGroups)
            .ToList();

    /// <summary>「复制全部」用的纯文本版本。</summary>
    public static string BuildText(IReadOnlyList<CleanupRow> rows)
    {
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            text.AppendLine($"条目：{row.Name} · {row.SizeText}");
            text.AppendLine($"分类：{row.Category} · {row.Detail}");
            if (!string.IsNullOrWhiteSpace(row.Warning))
                text.AppendLine($"提示：{row.Warning}");

            if (row.Junk is { } result)
            {
                var targets = GetTargets(result);
                if (targets.Count > 0)
                {
                    text.AppendLine("将删除以下位置：");
                    foreach (var target in targets)
                        text.AppendLine($"  - {target.Path}（{target.FileCount:N0} 个文件，{target.Scope}；匹配 {target.Pattern}）");
                }

                text.AppendLine($"文件（共 {result.FilesToDelete.Count:N0} 个）：");
                var groups = GetDirectoryGroups(result);
                foreach (var group in groups)
                {
                    text.AppendLine($"  [{group.Directory}]（{group.TotalFiles:N0} 个文件）");
                    foreach (var file in group.Files)
                        text.AppendLine("    " + file);
                    if (group.TotalFiles > group.Files.Count)
                        text.AppendLine($"    … 其余 {group.TotalFiles - group.Files.Count:N0} 个文件未展开");
                }
                var grouped = groups.Sum(g => g.TotalFiles);
                if (grouped < result.FilesToDelete.Count)
                    text.AppendLine($"  … 其余 {result.FilesToDelete.Count - grouped:N0} 个文件分布在更多目录中，未展开。");

                if (result.RegistryToDelete.Count > 0)
                {
                    text.AppendLine($"注册表（{result.RegistryToDelete.Count:N0} 项）：");
                    foreach (var registry in result.RegistryToDelete.Take(MaxRegistry))
                        text.AppendLine("  " + registry);
                }
            }
            else if (row.Windows is { } windows)
            {
                text.AppendLine(windows.Description);
            }
            else
            {
                text.AppendLine("该条目没有可预览的文件明细。");
            }

            text.AppendLine();
        }
        return text.ToString();
    }

    private static bool IsUnder(string file, string directory)
    {
        var root = directory.TrimEnd('\\', '/');
        return file.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file, root, StringComparison.OrdinalIgnoreCase);
    }

    private static string LiteralPrefix(string path)
    {
        int wildcard = path.IndexOfAny(['*', '?']);
        return (wildcard < 0 ? path : path[..wildcard]).TrimEnd('\\', '/');
    }
}
