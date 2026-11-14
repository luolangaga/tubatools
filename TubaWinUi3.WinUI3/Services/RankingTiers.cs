namespace TubaWinUi3.Services;

/// <summary>
/// 天梯图中的一段梯队。Start / End 是“当前排序结果”里的 0 基位次区间（含两端）。
/// </summary>
public sealed record RankingTier(int Level, string Title, string Hint, int Start, int End)
{
    public int Count => End - Start + 1;
}

/// <summary>
/// 天梯图分档规则：按“当前列表位次的百分比”切分，而不是按绝对分数。
/// 这样桌面 / 笔记本 / 多核 / 单核 / 全部 GPU 这些量级完全不同的榜单，
/// 都能切出大小合理、彼此可对照的梯队。纯逻辑，便于单元测试；界面只负责画档头。
/// </summary>
public static class RankingTiers
{
    /// <summary>各梯队上界（占当前列表的比例，含右端）。</summary>
    public static readonly double[] UpperBounds = [0.10, 0.30, 0.60, 0.85, 1.00];

    public static readonly string[] Titles = ["顶尖性能", "高端性能", "主流性能", "入门性能", "基础性能"];

    public static readonly string[] Hints = ["前 10%", "10–30%", "30–60%", "60–85%", "后 15%"];

    /// <summary>少于该数量时不显示档头：每档只剩一两行，反而把列表割裂。</summary>
    public const int MinimumCount = 10;

    /// <summary>按列表长度生成梯队；数量不足时返回空集合（调用方直接平铺）。</summary>
    public static List<RankingTier> Build(int count)
    {
        var tiers = new List<RankingTier>();
        if (count < MinimumCount) return tiers;

        var start = 0;
        for (var level = 0; level < UpperBounds.Length && start < count; level++)
        {
            var isLast = level == UpperBounds.Length - 1;
            var end = isLast
                ? count - 1
                : Math.Min(count - 1, (int)Math.Floor(count * UpperBounds[level]) - 1);
            if (end < start) continue;

            tiers.Add(new RankingTier(level + 1, Titles[level], Hints[level], start, end));
            start = end + 1;
        }
        return tiers;
    }
}

/// <summary>天梯图条形比例与百分位换算（纯逻辑，可单测）。</summary>
public static class RankingScale
{
    /// <summary>条形最小可见比例：分数极低的型号仍留一丝填充，避免整行空白。</summary>
    public const double MinimumFraction = 0.015;

    /// <summary>条形填充比例（0–1）。max 为当前榜单最高分，超出范围的输入被钳制。</summary>
    public static double Fraction(double value, double max)
    {
        if (max <= 0 || value <= 0) return 0;
        return Math.Clamp(value / max, MinimumFraction, 1.0);
    }

    /// <summary>相对榜首的百分比（四舍五入到整数）。</summary>
    public static int Percent(double value, double max) =>
        max <= 0 || value <= 0
            ? 0
            : (int)Math.Round(value / max * 100, MidpointRounding.AwayFromZero);

    /// <summary>0 基位次换算成“超过多少比例的机型”。</summary>
    public static int BeatPercent(int position, int count)
    {
        if (count <= 1) return 0;
        var clamped = Math.Clamp(position, 0, count - 1);
        var beaten = count - clamped - 1;
        return (int)Math.Round(beaten * 100.0 / (count - 1), MidpointRounding.AwayFromZero);
    }
}
