namespace TubaWinUi3.Services;

/// <summary>
/// 主窗口自定义标题栏内容的宽度预算（Issue #262）。
///
/// <c>Microsoft.UI.Xaml.Controls.TitleBar</c> 会把 Content 里每个交互控件（AutoSuggestBox、
/// Button、TabView）的布局矩形原样登记为「直通区域」，这些矩形不做裁剪。内容一旦宽于标题栏实际
/// 留给 Content 的列宽，就会盖住右侧的 48 DIP 最小拖拽区、系统最小化/最大化/关闭按钮 ——
/// 表现为窗口越窄、系统缩放越大，标题栏能被点/拖的地方越少。
///
/// 标题栏是两列：标签条（Auto）｜右侧面板（贴右）。标签条与右侧面板之间的空白是标题栏的
/// 拖拽区，窗口变窄时按顺序收窄搜索框、收起搜索框、收起动作按钮。
/// 标签条自己的宽度上限 <see cref="TabStripMaxWidth"/> 只是「设计宽度」：标签多到超过它时，
/// 先把标签条到右侧面板之间的空白用掉（那里本来只是拖拽区），而不是立刻把标签挤进滚动区；
/// 但始终保留 <see cref="MinTitleBarDragWidth"/> 的拖拽宽度，右侧面板的设计宽度也不让。
/// 计算都是纯数值逻辑，由 <c>TitleBarLayoutTests</c> 覆盖。
/// </summary>
public static class TitleBarLayout
{
    /// <summary>搜索框的设计宽度上限（与 MainWindow.xaml 的 TitleSearchArea 一致）。</summary>
    public const double SearchMaxWidth = 240;

    /// <summary>搜索框与动作按钮之间的间距。</summary>
    public const double SearchGap = 8;

    /// <summary>标题栏右侧两个动作按钮（新手教程 34 + 间距 6 + 下载队列 34）。</summary>
    public const double ActionButtonsWidth = 34 + 6 + 34;

    /// <summary>再窄就没有使用价值的搜索框宽度，低于它直接收起。</summary>
    public const double MinSearchWidth = 96;

    /// <summary>标签条至少要占用的宽度；连它都放不下就整条让给系统标题按钮。</summary>
    public const double TabStripMinWidth = 140;

    /// <summary>标签条的设计宽度上限（没有更多标签时停在这里）。</summary>
    public const double TabStripMaxWidth = 520;

    /// <summary>标签条自身的左右外边距（XAML 里 TabView 的 Margin 12,0,8,0）。</summary>
    public const double TabStripMargin = 20;

    /// <summary>
    /// 标签条再宽也必须给标题栏留下的拖拽宽度。标签多的时候可以借用中间的空白，
    /// 但不能一路铺到系统标题按钮上 —— 否则窗口就没地方拖了（微软标题栏示例同样建议
    /// 留出约 188 DIP 的拖拽区，这里取 160 是它与标签可读性之间的折中）。
    /// </summary>
    public const double MinTitleBarDragWidth = 160;

    /// <summary>标签条与右侧面板之间的间距（XAML 里 TabView 的右边距 8）。</summary>
    public const double TabGap = 8;

    /// <summary>右侧面板自身的右内边距（XAML 里的 Margin 4）。</summary>
    public const double RightInset = 4;

    /// <summary>某个 Content 列宽下标题栏内容应该呈现的样子（宽度单位 DIP）。</summary>
    public readonly record struct Presentation(
        bool ShowTabStrip,
        double TabStripWidth,
        bool ShowSearchBox,
        double SearchBoxWidth,
        bool ShowActionButtons);

    private static readonly Presentation Hidden = new(false, 0, false, 0, false);

    /// <summary>
    /// 按标题栏 Content 列的实际可用宽度决定内容布局。标签条优先级最高（它是唯一的主导航），
    /// 先给它 <see cref="TabStripMinWidth"/>；右侧再按剩余空间依次收窄搜索框、收起搜索框、收起动作按钮。
    /// 保证 <c>TabStripWidth + TabStripMargin + TabGap + SearchBoxWidth + SearchGap + ActionButtonsWidth
    /// + RightInset &lt;= contentWidth</c>，任何宽度下都不会溢出。
    /// </summary>
    public static Presentation ForContentWidth(double contentWidth)
    {
        if (double.IsNaN(contentWidth) || contentWidth <= 0)
            return Hidden;

        var budget = contentWidth - RightInset;
        if (budget < TabStripMinWidth + TabStripMargin) return Hidden;

        // 右侧面板：能放搜索框就放（在 Min..Max 之间伸缩），否则只留动作按钮。
        // 标签条至少要有 MinWidth + Margin，这部分先从预算里扣掉再算右侧。
        var showSearch = budget >= TabStripMinWidth + TabStripMargin + TabGap
                                        + ActionButtonsWidth + SearchGap + MinSearchWidth;
        var showButtons = budget >= TabStripMinWidth + TabStripMargin + TabGap + ActionButtonsWidth;

        var searchWidth = 0.0;
        if (showSearch)
        {
            var room = budget - TabStripMinWidth - TabStripMargin - TabGap - ActionButtonsWidth - SearchGap;
            searchWidth = Math.Clamp(room, MinSearchWidth, SearchMaxWidth);
        }

        var right = (showSearch ? searchWidth + SearchGap : 0)
                  + (showButtons ? ActionButtonsWidth : 0);

        // 右侧什么都没有时不需要间距，标签条可以一路用掉剩余空间
        var tabRoom = right > 0 ? budget - right - TabGap : budget;
        if (tabRoom < TabStripMinWidth + TabStripMargin)
            return new Presentation(false, 0, showSearch, searchWidth, showButtons);

        // 标签条的外框宽度：设计上限 → 借用中间的空白（本来只是拖拽区）。
        // 标签再多也不吃掉 MinTitleBarDragWidth，窗口始终拖得动。
        var stripOuter = Math.Min(tabRoom,
            Math.Max(TabStripMaxWidth + TabStripMargin, tabRoom - MinTitleBarDragWidth));
        var stripWidth = Math.Max(TabStripMinWidth, stripOuter - TabStripMargin);

        return new Presentation(true, stripWidth, showSearch, searchWidth, showButtons);
    }
}