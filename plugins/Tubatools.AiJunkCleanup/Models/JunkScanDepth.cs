namespace Tubatools.AiJunkCleanup.Models;

/// <summary>垃圾清理的扫描深度。默认标准；深度只扩大只读检查范围，不会放宽删除限制。</summary>
internal enum JunkScanDepth
{
    Quick = 0,
    Standard = 1,
    Deep = 2
}

internal static class JunkScanDepths
{
    internal static JunkScanDepth Parse(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "quick" or "fast" or "快速" => JunkScanDepth.Quick,
        "deep" or "full" or "深度" => JunkScanDepth.Deep,
        _ => JunkScanDepth.Standard
    };

    internal static string Key(JunkScanDepth depth) => depth switch
    {
        JunkScanDepth.Quick => "quick",
        JunkScanDepth.Deep => "deep",
        _ => "standard"
    };

    internal static string Label(JunkScanDepth depth) => depth switch
    {
        JunkScanDepth.Quick => "快速",
        JunkScanDepth.Deep => "深度",
        _ => "标准"
    };

    internal static string Tip(JunkScanDepth depth) => depth switch
    {
        JunkScanDepth.Quick => "优先看最常见、最容易确认的缓存和临时位置，速度快。",
        JunkScanDepth.Deep => "大范围只读浏览指定盘符/目录，专门找意外的大文件、旧安装包和长期不用的缓存；耗时更长。",
        _ => "在快速基础上扩大浏览范围，顺着占用大的目录继续找可清理内容。"
    };

    /// <summary>同一次 MFT 扫描导出深度索引的参数：越深越快照旧只读，不会二次扫盘。</summary>
    internal readonly record struct ScanIndexProfile(
        int Depth,
        long MinDirectoryBytes,
        long MinFileBytes,
        int MaxDirectories,
        int MaxFiles);

    internal static ScanIndexProfile Index(JunkScanDepth depth) => depth switch
    {
        JunkScanDepth.Quick => new(4, 64L << 20, 512L << 20, 5_000, 300),
        JunkScanDepth.Deep => new(16, 4L << 20, 32L << 20, 60_000, 3_000),
        _ => new(8, 16L << 20, 128L << 20, 20_000, 1_000)
    };

    /// <summary>随用户消息一起交给 AI 的深度说明；技能文件里有完整定义。</summary>
    internal static string Prompt(JunkScanDepth depth) => depth switch
    {
        JunkScanDepth.Quick =>
            "扫描深度：快速。优先看最常见、最容易确认的缓存/临时位置，并只对用户点名的目录做一次浅层浏览；目标是快速给出少量高置信度项目，不追求覆盖所有位置。",
        JunkScanDepth.Deep =>
            "扫描深度：深度。尽可能广泛地只读浏览用户指定的盘符/目录和常见数据位置，主动寻找意外的大文件、旧安装包、长期未使用的缓存、日志和开发产物；允许更多只读命令和更深的递归。不要扫描系统保护目录（Windows/Program Files/ProgramData 根、$Recycle.Bin、System Volume Information），不要读取大文件内容；仍然禁止任何删除、移动或重命名。",
        _ =>
            "扫描深度：标准。在快速基础上扩大浏览范围：查看用户目录和常见应用缓存，顺着占用大的目录继续往下看，允许对具体目录做有限深度的只读递归；目标是找出明显可清理的缓存、日志和意外的大文件。"
    };
}
