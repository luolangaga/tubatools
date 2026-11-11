using Microsoft.UI.Xaml.Controls;

namespace TubaWinUi3.Services;

/// <summary>
/// 工具卡片网格的列宽计算（首页 / 常用 / 内置 / 社区四个页面共用，保证卡片尺寸行为一致）。
/// </summary>
public static class ToolCardLayout
{
    public const double NormalMinItemWidth = 280;
    public const double NormalSpacing = 12;
    public const double CompactMinItemWidth = 100;
    public const double CompactSpacing = 10;

    /// <summary>按可用宽度计算列数与每列宽度（纯函数，可单测）。</summary>
    public static (int Columns, double ItemWidth) Compute(double availableWidth, double minItemWidth, double spacing)
    {
        if (availableWidth <= 0 || minItemWidth <= 0)
            return (1, Math.Max(0, availableWidth));

        var columns = Math.Max(1, (int)((availableWidth + spacing) / (minItemWidth + spacing)));
        var itemWidth = (availableWidth - (columns - 1) * spacing) / columns;
        return (columns, Math.Max(minItemWidth, itemWidth));
    }

    /// <summary>
    /// 按列均分铺满整行的槽宽（槽宽含卡片右侧外边距）。
    /// 行宽里预留 <paramref name="slack"/> 像素：除不尽时 columns×槽宽 会比行宽大出几个 ULP，
    /// ItemsWrapGrid 只要发现放不下就会把最后一整列换到下一行（右侧空出一列），
    /// 所以不能贴着行宽取整。2px 摊到每列不足 1px，肉眼不可见。
    /// </summary>
    public static double FitItemWidth(double rowWidth, int columns, double slack = 2)
    {
        if (columns < 1) columns = 1;
        return Math.Max(1, (rowWidth - Math.Max(0, slack)) / columns);
    }

    /// <summary>用网格实际宽度（扣掉内边距）更新 ItemsWrapGrid.ItemWidth。</summary>
    public static void Apply(GridView grid, bool compact = false)
    {
        if (grid.ItemsPanelRoot is not ItemsWrapGrid panel) return;

        var minItemWidth = compact ? CompactMinItemWidth : NormalMinItemWidth;
        var spacing = compact ? CompactSpacing : NormalSpacing;
        var availableWidth = grid.ActualWidth - grid.Padding.Left - grid.Padding.Right;
        if (availableWidth <= 0) return;

        var (_, itemWidth) = Compute(availableWidth, minItemWidth, spacing);
        panel.ItemWidth = itemWidth;
    }
}
