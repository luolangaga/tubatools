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
