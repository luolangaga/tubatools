namespace TubaWinUi3.Services;

public sealed class SpaceCleanupTool : IBuiltinTool
{
    public string Id => "space-cleanup";
    public string Name => LocalizationService.L("Builtin_space-cleanup_Name", "空间清理");
    public string Description => LocalizationService.L("Builtin_space-cleanup_Desc", "TreeMap 可视化磁盘占用、应用垃圾清理与 Windows 磁盘清理。");
    public string Glyph => "\uEDA8";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;
    public Task ExecuteAsync(BuiltinToolContext context)
    { App.MainWindow?.NavigateToToolPage(typeof(Pages.SpaceCleanupPage)); return Task.CompletedTask; }
}
