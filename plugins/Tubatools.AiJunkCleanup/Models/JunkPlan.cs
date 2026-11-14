namespace Tubatools.AiJunkCleanup.Models;

/// <summary>最终展示的风险等级。AI 给出的等级只会被安全规则提高，不会被降低。</summary>
internal enum JunkRisk
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>AI 清单中的一项。路径与理由都直接来自模型，展示前还要经过 <see cref="JunkSafety"/>。</summary>
internal sealed record JunkPlanItem(
    string Path,
    bool IsDirectory,
    long SizeBytes,
    JunkRisk Risk,
    string Category,
    string Reason,
    string RiskDetail,
    string Suggestion);

internal sealed record JunkPlan(
    string Summary,
    IReadOnlyList<JunkPlanItem> Items)
{
    public long TotalBytes => Items.Where(i => i.SizeBytes > 0).Sum(i => i.SizeBytes);
}

internal sealed record JunkCleanupResult(
    int Deleted,
    int Failed,
    int Skipped,
    long FreedBytes,
    IReadOnlyList<string> Errors)
{
    public bool AnySuccess => Deleted > 0;
}
