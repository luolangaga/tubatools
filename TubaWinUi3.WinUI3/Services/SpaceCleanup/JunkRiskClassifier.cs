using FluentCleaner.Models;
using FluentCleaner.Services;

namespace TubaWinUi3.Services.SpaceCleanup;

/// <summary>清理项目的风险级别。高风险项目永远不会被默认勾选，删除前必须额外确认。</summary>
public enum JunkRiskLevel
{
    Normal = 0,
    Caution = 1,
    Dangerous = 2,
}

public sealed record JunkRisk(JunkRiskLevel Level, string Reason)
{
    public static readonly JunkRisk None = new(JunkRiskLevel.Normal, "");

    public bool IsRisky => Level != JunkRiskLevel.Normal;
}

public sealed record SensitiveFolder(string Name, string Path);

/* 规则库只描述"删什么"，不描述"删的东西值不值钱"。
   这里按展开后的目标路径判断风险：个人文件夹（下载/桌面/文档/图片/音乐/视频）根目录、
   用户目录、Windows 目录和磁盘根目录都不该在用户没注意时被清空。 */
public static class JunkRiskClassifier
{
    private static readonly Lazy<IReadOnlyList<SensitiveFolder>> PersonalFolders = new(DefaultPersonalFolders);
    private static readonly Lazy<IReadOnlyList<SensitiveFolder>> ProtectedRoots = new(DefaultProtectedRoots);

    public static JunkRisk Evaluate(CleanerEntry entry, ScanResult? result = null)
        => EvaluateCore(entry, result, PersonalFolders.Value, ProtectedRoots.Value);

    /// <summary>只有显式 Default=True 且没有风险的规则才允许默认勾选。</summary>
    public static bool IsDefaultSelected(bool entryDefault, JunkRisk risk) => entryDefault && !risk.IsRisky;

    public static JunkRisk EvaluateCore(CleanerEntry entry, ScanResult? result,
        IReadOnlyList<SensitiveFolder> personalFolders, IReadOnlyList<SensitiveFolder> protectedRoots)
    {
        var level = JunkRiskLevel.Normal;
        var reasons = new List<string>();

        void Raise(JunkRiskLevel candidate, string reason)
        {
            if (candidate == JunkRiskLevel.Normal) return;
            if (candidate > level) level = candidate;
            if (!reasons.Contains(reason, StringComparer.OrdinalIgnoreCase)) reasons.Add(reason);
        }

        var expander = new PathExpander();
        foreach (var key in entry.FileKeys)
        {
            string expanded;
            try { expanded = expander.ExpandVariables(key.Path); }
            catch { continue; }

            // 只取第一个通配符之前的字面路径：%UserProfile%\Downloads\* 仍然算"下载目录"。
            string target = LiteralPrefix(expanded);
            if (target.Length == 0) continue;

            bool allFiles = IsAllFiles(key.Pattern);
            bool deep = key.Flag != FileKeyFlag.None;

            foreach (var root in protectedRoots)
                if (PathEquals(target, root.Path))
                    Raise(JunkRiskLevel.Dangerous, $"目标是{root.Name}");

            foreach (var folder in personalFolders)
            {
                if (PathEquals(target, folder.Path))
                {
                    if (allFiles)
                        Raise(JunkRiskLevel.Dangerous,
                            deep ? $"会清空「{folder.Name}」及其所有子文件夹中的全部文件" : $"会清空「{folder.Name}」中的全部文件");
                    else
                        Raise(JunkRiskLevel.Caution, $"会删除「{folder.Name}」中的 {ShortenPattern(key.Pattern)} 文件");
                }
                else if (deep && allFiles && IsStrictlyInside(target, folder.Path))
                {
                    Raise(JunkRiskLevel.Caution, $"会删除「{folder.Name}」下的完整目录");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.Warning))
            Raise(JunkRiskLevel.Caution, entry.Warning.Trim());

        if (result is not null && result.RegistryToDelete.Count > 0)
            Raise(JunkRiskLevel.Caution, "包含注册表项目");

        return reasons.Count == 0 ? JunkRisk.None : new JunkRisk(level, string.Join(" · ", reasons));
    }

    private static IReadOnlyList<SensitiveFolder> DefaultPersonalFolders()
    {
        var folders = new List<SensitiveFolder>();
        void Add(string name, string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string normalized = path.TrimEnd('\\', '/');
            if (normalized.Length == 0) return;
            if (!folders.Any(f => string.Equals(f.Path, normalized, StringComparison.OrdinalIgnoreCase)))
                folders.Add(new SensitiveFolder(name, normalized));
        }

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add("桌面", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add("文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add("下载", DownloadsFolder.Resolve());
        Add("下载", Path.Combine(profile, "Downloads"));
        Add("图片", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Add("音乐", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        Add("视频", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        Add("OneDrive", Environment.GetEnvironmentVariable("OneDrive"));
        return folders;
    }

    private static IReadOnlyList<SensitiveFolder> DefaultProtectedRoots()
    {
        var roots = new List<SensitiveFolder>();
        void Add(string name, string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string normalized = path.TrimEnd('\\', '/');
            if (normalized.Length == 0) return;
            roots.Add(new SensitiveFolder(name, normalized));
        }

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add("用户目录", profile);
        Add("Windows 目录", Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        Add("磁盘根目录", Path.GetPathRoot(profile));
        return roots;
    }

    private static string LiteralPrefix(string path)
    {
        int wildcard = path.IndexOfAny(['*', '?']);
        return (wildcard < 0 ? path : path[..wildcard]).TrimEnd('\\', '/');
    }

    private static bool IsAllFiles(string pattern)
        => pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Any(p => p is "*" or "*.*");

    private static string ShortenPattern(string pattern)
    {
        string trimmed = pattern.Trim();
        return trimmed.Length <= 40 ? trimmed : trimmed[..37] + "…";
    }

    private static bool PathEquals(string left, string right)
        => string.Equals(left.TrimEnd('\\', '/'), right.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static bool IsStrictlyInside(string child, string parent)
        => child.StartsWith(parent.TrimEnd('\\', '/') + "\\", StringComparison.OrdinalIgnoreCase);
}
