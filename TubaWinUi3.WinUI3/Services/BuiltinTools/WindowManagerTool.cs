namespace TubaWinUi3.Services;

public sealed class WindowManagerTool : IBuiltinTool
{
    public string Id => "window-manager";
    public string Name => "窗口管理";
    public string Description => "拖动十字选择窗口，查看信息，设置无边框、全屏、置顶或恢复原状。";
    public string Glyph => "\uE737";
    public string Category => "游戏工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;
    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(Pages.WindowManagerPage));
        return Task.CompletedTask;
    }
}
