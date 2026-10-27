using FluentCleaner.Models;
using FluentCleaner.Services;

namespace TubaWinUi3.Services.SpaceCleanup;

public sealed class CleanupRow
{
    public override string ToString() => $"{Name}，{SizeText}，{Detail} {Warning}";
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Detail { get; init; }
    public string Warning { get; init; } = "";
    public long Bytes { get; init; }
    public string SizeText => ScanResult.FormatBytes(Bytes);
    public bool DefaultSelected { get; init; }
    public ScanResult? Junk { get; init; }
    public WindowsCleanupItem? Windows { get; init; }
}

public static class JunkCleanupFacade
{
    public static Task<List<CleanupRow>> ScanAsync(IProgress<string>? progress, CancellationToken token)
        => Task.Run(async () =>
        {
            var parser = new Winapp2Parser(); var detection = new DetectionService(); var cleaner = new CleaningService();
            var entries = await parser.ParseFileAsync(JunkCleanerDatabase.GetEffectivePath(JunkDatabaseKind.Winapp2));
            if (Directory.Exists(JunkCleanerDatabase.CustomDir))
                foreach (var file in Directory.EnumerateFiles(JunkCleanerDatabase.CustomDir, "*.ini"))
                {
                    token.ThrowIfCancellationRequested();
                    foreach (var entry in await parser.ParseFileAsync(file))
                    { entry.IsCustom = true; entries.RemoveAll(e => e.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)); entries.Add(entry); }
                }
            var results = new List<CleanupRow>(); int index = 0;
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested(); progress?.Report($"正在分析规则 {++index:N0}/{entries.Count:N0}：{entry.Name}");
                bool hasDetection = entry.DetectKeys.Count > 0 || entry.DetectFiles.Count > 0 || entry.SpecialDetect is not null;
                if (!(entry.IsCustom && !hasDetection) && !detection.IsInstalled(entry)) continue;
                var result = await cleaner.AnalyzeAsync(entry, null, token);
                if (result.FilesToDelete.Count == 0 && result.RegistryToDelete.Count == 0) continue;
                string warning = string.Join(" · ", new[] { entry.Warning, result.RegistryToDelete.Count > 0 ? "包含注册表项目" : null }.Where(s => !string.IsNullOrWhiteSpace(s)));
                results.Add(new CleanupRow { Name = entry.Name, Category = CategoryResolver.TryMapLangSecRef(entry).Name,
                    Detail = $"{result.FilesToDelete.Count:N0} 个文件 · {result.RegistryToDelete.Count:N0} 个注册表项目",
                    Bytes = result.TotalBytes, Junk = result, Warning = warning,
                    DefaultSelected = entry.Default && string.IsNullOrWhiteSpace(warning) });
            }
            return results.OrderBy(r => r.Category).ThenByDescending(r => r.Bytes).ToList();
        }, token);
}
