namespace TubaWinUi3.Services;

/// <summary>天梯墙上的一个刻度：相对榜首的百分比，以及它在墙上的绝对纵向位置。</summary>
public sealed record LadderTick(int Percent, double Y);

/// <summary>
/// 天梯墙的排版规则（纯逻辑，可单测）：
/// 型号按性能降序，从左到右、从上到下密排；左侧刻度轴按“相对榜首”的百分比定位，
/// 这样整面墙共用一把尺子，任何一个型号的高低都能和榜首直接对照。
/// </summary>
public static class RankingLadderLayout
{
    /// <summary>墙上单行高度（型号条 + 间隙）。</summary>
    public const double RowHeight = 26;

    /// <summary>左侧刻度轴宽度。</summary>
    public const double RailWidth = 62;

    /// <summary>刻度间距上限：相邻两个百分比刻度靠得太近时只保留更靠上的那个。</summary>
    public const double MinimumTickGap = 22;

    /// <summary>参与标注的百分比刻度（降序）。</summary>
    public static readonly int[] PercentTicks = [100, 75, 50, 25, 10, 5, 1];

    /// <summary>按可用宽度决定列数：窄窗口单列（等同列表），宽屏最多四列。</summary>
    public static int ColumnsFor(double width)
    {
        if (width >= 1560) return 4;
        if (width >= 1120) return 3;
        if (width >= 760) return 2;
        return 1;
    }

    /// <summary>
    /// 计算刻度位置。metrics 必须按性能降序；max 为整条榜单（未筛选）的最高分，
    /// 因此筛选后刻度仍指向同一个绝对性能位置，不会随筛选漂移。
    /// </summary>
    public static List<LadderTick> BuildTicks(IReadOnlyList<long> metrics, long max, int columns)
    {
        var ticks = new List<LadderTick>();
        if (metrics.Count == 0 || max <= 0) return ticks;

        columns = Math.Max(1, columns);
        var lastY = double.NegativeInfinity;

        foreach (var percent in PercentTicks)
        {
            var threshold = max * percent / 100.0;
            var index = -1;
            for (var i = 0; i < metrics.Count; i++)
            {
                if (metrics[i] <= threshold)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0) continue; // 整榜都高于该阈值（例如筛选后的小样本）

            var y = Math.Floor(index / (double)columns) * RowHeight;
            if (y - lastY < MinimumTickGap) continue;

            ticks.Add(new LadderTick(percent, y));
            lastY = y;
        }

        return ticks;
    }

    /// <summary>按型号数量算出墙的总高度。</summary>
    public static double HeightFor(int count, int columns)
    {
        if (count <= 0) return 0;
        var rows = (int)Math.Ceiling(count / (double)Math.Max(1, columns));
        return rows * RowHeight;
    }
}
