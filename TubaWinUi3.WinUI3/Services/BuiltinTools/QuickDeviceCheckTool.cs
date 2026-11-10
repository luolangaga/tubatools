using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class QuickDeviceCheckTool : IBuiltinTool
{
    public string Id => "quick-device-check";
    public string Name => LocalizationService.L("Builtin_quick-device-check_Name", "快速验机");
    public string Description => LocalizationService.L("Builtin_quick-device-check_Desc", "新电脑验机向导：外观检查、硬件信息、硬盘通电、屏幕坏点、外设测试、摄像头、音频录放、CPU 和 GPU 烤机，一站式完成");
    public string Glyph => "\uE962";
    public string Category => "硬件工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(QuickDeviceCheckPage));
        return Task.CompletedTask;
    }
}
