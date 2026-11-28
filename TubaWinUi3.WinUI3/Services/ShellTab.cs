using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TubaWinUi3.Services;

/// <summary>
/// 主窗口的一个标签页：独立的导航栈（<see cref="Frame"/>）+ 标题栏里的标签头。
///
/// 页面实例由 <see cref="Frame"/> 的导航缓存持有，所以切走再切回来会回到原来的页面状态，
/// 而不是重建一遍（缓存深度与改动前的单个 NavFrame 一致）。
/// </summary>
internal sealed class ShellTab
{
    public ShellTab(Frame frame, TabViewItem item)
    {
        Frame = frame;
        Item = item;
    }

    public Frame Frame { get; }

    public TabViewItem Item { get; }

    /// <summary>当前页面类型；标签尚未导航时为 null。</summary>
    public Type? PageType { get; set; }

    /// <summary>当前页面的导航参数，语言切换后据此重新计算标题。</summary>
    public object? Parameter { get; set; }

    /// <summary>标签文本（同时用作悬停提示）。</summary>
    public string Title { get; set; } = "";

    /// <summary>标题是否由页面里的 <c>ToolPageHeader</c> 提供（而不是 <see cref="ShellTabTitle"/> 的映射）。</summary>
    public bool TitleFromPage { get; set; }
}
