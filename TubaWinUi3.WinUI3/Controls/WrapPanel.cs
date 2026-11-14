using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace TubaWinUi3.Controls;

/// <summary>
/// 轻量流式面板：按可用宽度从左到右排列子元素，放不下就换行。
/// 只用于预设指令这类少量、定宽的内容，不做虚拟化。
/// </summary>
public sealed class WrapPanel : Panel
{
    private const double Gap = 8;

    protected override Size MeasureOverride(Size availableSize)
    {
        var maxWidth = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width);
        double x = 0, y = 0, rowHeight = 0, width = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(maxWidth, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (x > 0 && x + Gap + size.Width > maxWidth) { x = 0; y += rowHeight + Gap; rowHeight = 0; }
            if (x > 0) x += Gap;
            x += size.Width;
            rowHeight = Math.Max(rowHeight, size.Height);
            width = Math.Max(width, x);
        }
        return new Size(Math.Min(width, maxWidth), y + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var maxWidth = Math.Max(0, finalSize.Width);
        double x = 0, y = 0, rowHeight = 0;
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + Gap + size.Width > maxWidth) { x = 0; y += rowHeight + Gap; rowHeight = 0; }
            if (x > 0) x += Gap;
            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width;
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        return new Size(finalSize.Width, y + rowHeight);
    }
}
