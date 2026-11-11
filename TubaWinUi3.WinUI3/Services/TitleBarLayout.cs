namespace TubaWinUi3.Services;

/// <summary>
/// 主窗口自定义标题栏内容的宽度预算（Issue #262）。
///
/// <c>Microsoft.UI.Xaml.Controls.TitleBar</c> 会把 Content 里每个交互控件（AutoSuggestBox、
/// Button）的布局矩形原样登记为「直通区域」，这些矩形不做裁剪。内容一旦宽于标题栏实际
/// 留给 Content 的列宽，就会盖住右侧的 48 DIP 最小拖拽区、系统最小化/最大化/关闭按钮 ——
/// 表现为窗口越窄、系统缩放越大，标题栏能被点/拖的地方越少，最后只剩最左边两个按钮。
///
/// 所以标题栏内容必须可以收缩：搜索框先按剩余宽度变窄，再不够就依次收起右侧按钮和搜索框，
/// 任何宽度下内容都不会向右侧溢出。这里的计算都是纯数值逻辑，由 <c>TitleBarLayoutTests</c> 覆盖。
/// </summary>
public static class TitleBarLayout
{
    /// <summary>搜索框的设计宽度上限（与 MainWindow.xaml 一致）。</summary>
    public const double SearchMaxWidth = 320;

    /// <summary>搜索框右侧与动作按钮之间的间距。</summary>
    public const double SearchGap = 8;

    /// <summary>标题栏右侧两个动作按钮（新手教程 34 + 间距 6 + 下载队列 34）。</summary>
    public const double ActionButtonsWidth = 34 + 6 + 34;

    /// <summary>再窄就没有使用价值的搜索框宽度，低于它直接收起。</summary>
    public const double MinSearchWidth = 120;

    /// <summary>某个 Content 列宽下标题栏内容应该呈现的样子（宽度单位 DIP）。</summary>
    public readonly record struct Presentation(bool ShowSearchBox, double SearchBoxWidth, bool ShowActionButtons);

    /// <summary>
    /// 按标题栏 Content 列的实际可用宽度决定内容布局。优先级：搜索框 &gt; 右侧动作按钮 ——
    /// 极窄时宁可收起按钮，也不让控件压到系统标题按钮上。返回值保证
    /// <c>SearchBoxWidth + SearchGap + (ShowActionButtons ? ActionButtonsWidth : 0) &lt;= contentWidth</c>。
    /// </summary>
    public static Presentation ForContentWidth(double contentWidth)
    {
        if (double.IsNaN(contentWidth) || contentWidth <= 0)
            return new Presentation(false, 0, false);

        if (contentWidth >= MinSearchWidth + SearchGap + ActionButtonsWidth)
        {
            var searchBoxWidth = Math.Min(SearchMaxWidth, contentWidth - SearchGap - ActionButtonsWidth);
            return new Presentation(true, searchBoxWidth, true);
        }

        if (contentWidth >= MinSearchWidth + SearchGap)
        {
            var searchBoxWidth = Math.Min(SearchMaxWidth, contentWidth - SearchGap);
            return new Presentation(true, searchBoxWidth, false);
        }

        return new Presentation(false, 0, false);
    }
}
