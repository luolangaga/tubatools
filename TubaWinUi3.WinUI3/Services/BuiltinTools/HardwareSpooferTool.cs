using Microsoft.UI.Xaml;

namespace TubaWinUi3.Services;

public sealed class HardwareSpooferTool : IBuiltinTool
{
    public string Id => "hardware-spoofer";
    public string Name => LocalizationService.L("Builtin_hardware-spoofer_Name", "配置修改器");
    public string Description => LocalizationService.L("Builtin_hardware-spoofer_Desc", "修改注册表中的 CPU、GPU、系统等硬件信息显示，支持一键恢复原始配置。");
    public string Glyph => "\uEEA1";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.HardwareSpooferPage));
        return Task.CompletedTask;
    }
}
