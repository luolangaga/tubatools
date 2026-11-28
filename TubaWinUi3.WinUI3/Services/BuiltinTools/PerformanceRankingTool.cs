using TubaWinUi3.Pages;
using TubaWinUi3.Controls;

namespace TubaWinUi3.Services;

/// <summary>「性能排行」入口：CPU / GPU 天梯图、社区跑分排行与核间延迟查询的合并入口。</summary>
public sealed class PerformanceRankingTool : IBuiltinTool
{
    public string Id => "performance-ranking";
    public string Name => LocalizationService.L("Builtin_performance-ranking_Name", "性能排行");
    public string Description => LocalizationService.L(
        "Builtin_performance-ranking_Desc",
        "CPU / GPU 天梯图、社区跑分排行与核间延迟查询集中在一个页面，按标签切换。");
    public string Glyph => "\uE9D5";
    public string Category => "硬件工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        var tab = context.PreferredTab ?? "benchmark";

        // 当前主窗口就是性能排行页时只切换标签，避免重复导航把它重新创建成默认标签；
        // 若用户设置了内置工具独立窗口，仍交给 NavigateToToolPage 开新窗口。
        if (!BuiltinToolWindow.ForceWindowMode
            && !AppSettings.GetBool("BuiltinToolsOpenInWindow", false)
            && App.MainWindow?.PluginNavigationFrame.Content is PerformanceRankingPage current)
        {
            current.SwitchTab(tab);
            return Task.CompletedTask;
        }

        PerformanceRankingPage.PrepareTab(tab);
        App.MainWindow?.NavigateToToolPage(typeof(PerformanceRankingPage));
        return Task.CompletedTask;
    }
}
