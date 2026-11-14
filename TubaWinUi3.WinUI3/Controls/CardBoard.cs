using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace TubaWinUi3.Controls;

/// <summary>
/// 信息卡瀑布流面板（纯展示，不提供手动排布）：
/// 按可用宽度自适应列数，卡片自上而下流入当前最矮的列（分区默认跨度可跨列），
/// 内容比 <see cref="FillHeight"/> 矮时把每列缺口平均拉伸回卡片并重排一次，
/// 让各列底部对齐、铺满窗口；内容更高时照常滚动。
/// 每次测量都按自然高度重新分列——没有「钉住的列」，任何内容变化后都保持均衡。
/// </summary>
public sealed class CardBoard : Panel
{
    /// <summary>卡片跨几列（≥1，受当前列数钳制）；由页面按分区默认值设置。</summary>
    public static readonly DependencyProperty ColumnSpanProperty = DependencyProperty.RegisterAttached(
        "ColumnSpan", typeof(int), typeof(CardBoard), new PropertyMetadata(1, OnColumnSpanChanged));

    public static int GetColumnSpan(DependencyObject element) => (int)element.GetValue(ColumnSpanProperty);
    public static void SetColumnSpan(DependencyObject element, int value) => element.SetValue(ColumnSpanProperty, value);

    /// <summary>每列最小宽度（决定列数）。</summary>
    public double MinColumnWidth { get; set; } = 336;

    public double ColumnGap { get; set; } = 14;
    public double RowGap { get; set; } = 14;

    /// <summary>列数上限（宽屏铺满但不无限铺开，保持卡片可读宽度）。</summary>
    public int MaxColumns { get; set; } = 3;

    /// <summary>
    /// 「铺满」目标高度（由页面按视口可用高度设置）：内容比它矮时把缺口平均拉伸回卡片，
    /// 让各列底部对齐并填满窗口；内容更高时正常滚动，不做任何压缩。
    /// </summary>
    public double FillHeight
    {
        get => _fillHeight;
        set
        {
            if (Math.Abs(_fillHeight - value) < 0.5) return;
            _fillHeight = Math.Max(0, value);
            InvalidateMeasure();
        }
    }

    private double _fillHeight;

    /// <summary>拉伸前的最小缺口，避免几分之一像素的抖动。</summary>
    private const double FillThreshold = 8;

    private readonly Dictionary<UIElement, CardPlacement> _placements = [];
    private int _columns = 1;
    private double _columnWidth;

    // 刻意不设置 ChildrenTransitions：这个面板每次测量都会按自然高度重新分列，
    // 再叠加「铺满高度」的一次重排；RepositionThemeTransition 会让每次重排的卡片位置
    // 都跑一遍动画，动画帧又反过来触发重排，形成布局/动画循环（数秒后报错、界面卡死）。
    // 瀑布流保持静态定位，Issue #266。

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width;
        if (!double.IsFinite(width) || width <= 0)
            width = (MinColumnWidth + ColumnGap) * Math.Max(1, MaxColumns);

        _columns = Math.Min(CardLayoutMath.ResolveColumnCount(width, MinColumnWidth, ColumnGap), Math.Max(1, MaxColumns));
        _columnWidth = Math.Max(0, (width - (_columns - 1) * ColumnGap) / _columns);

        var items = new List<BoardItem>(Children.Count);
        var ordered = new List<UIElement>(Children.Count);

        foreach (var child in Children)
        {
            if (child is not FrameworkElement element) continue;

            var span = Math.Clamp(GetColumnSpan(element), 1, _columns);
            var cardWidth = span * _columnWidth + (span - 1) * ColumnGap;
            element.Measure(new Size(Math.Max(0, cardWidth), double.PositiveInfinity));

            items.Add(new BoardItem(-1, span, element.DesiredSize.Height));
            ordered.Add(element);
        }

        var placements = CardLayoutMath.Arrange(items, _columnWidth, _columns, ColumnGap, RowGap);
        var naturalBottom = 0d;
        foreach (var placement in placements)
            naturalBottom = Math.Max(naturalBottom, placement.Rect.Bottom);

        var totalHeight = naturalBottom;

        // 铺满：内容比目标高度矮时，把每列缺口平均补给单列卡片并重排一次
        // （位置随高度下移，保证不重叠、各列底部对齐到目标高度）。
        if (_fillHeight > naturalBottom + FillThreshold && placements.Count > 0)
        {
            var resolved = new List<BoardItem>(placements.Count);
            for (var i = 0; i < placements.Count; i++)
                resolved.Add(new BoardItem(placements[i].Column, placements[i].ColumnSpan, items[i].Height));

            var extras = CardLayoutMath.DistributeFill(resolved, placements, _columns, _fillHeight);
            for (var i = 0; i < resolved.Count; i++)
                resolved[i] = resolved[i] with { Height = resolved[i].Height + extras[i] };

            placements = CardLayoutMath.Arrange(resolved, _columnWidth, _columns, ColumnGap, RowGap);
            totalHeight = _fillHeight;
        }

        _placements.Clear();
        for (var i = 0; i < ordered.Count; i++)
            _placements[ordered[i]] = placements[i];

        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var rect = child is not null && _placements.TryGetValue(child, out var placement)
                ? placement.Rect
                : new Rect(0, 0, 0, 0);
            child?.Arrange(rect);
        }

        return finalSize;
    }

    private static void OnColumnSpanChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && element.Parent is CardBoard board)
            board.InvalidateMeasure();
    }
}
