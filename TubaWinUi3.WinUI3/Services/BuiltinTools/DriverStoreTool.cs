namespace TubaWinUi3.Services;

public sealed class DriverStoreTool : IBuiltinTool
{
    public string Id => "driver-store";
    public string Name => LocalizationService.L("Builtin_driver-store_Name", "驱动管理");
    public string Description => LocalizationService.L("Builtin_driver-store_Desc", "查看驱动库中每个驱动的全部版本与磁盘占用，安全清理没有设备在用的旧版本残留，支持导出驱动备份与从备份还原");
    public string Glyph => "\uE9D5";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.DriverStorePage));
        return Task.CompletedTask;
    }
}
