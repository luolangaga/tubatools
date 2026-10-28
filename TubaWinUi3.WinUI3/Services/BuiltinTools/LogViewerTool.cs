namespace TubaWinUi3.Services;

public sealed class LogViewerTool : IBuiltinTool
{
    public string Id => "log-viewer";
    public string Name => LocalizationService.L("Builtin_log-viewer_Name", "运行日志");
    public string Description => LocalizationService.L("Builtin_log-viewer_Desc", "统一查看启动轨迹、崩溃、AI 助手、联机、压力测试等全部日志，支持实时跟随、级别与关键字过滤、复制、清空与打开日志目录");
    public string Glyph => "\uE7C3";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.LogViewerPage));
        return Task.CompletedTask;
    }
}
