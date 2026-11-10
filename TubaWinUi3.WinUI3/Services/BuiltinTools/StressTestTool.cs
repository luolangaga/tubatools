using Microsoft.UI.Xaml;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;

namespace TubaWinUi3.Services;

public sealed class StressTestTool : IBuiltinTool
{
    public string Id => "stress-test";
    public string Name => LocalizationService.L("Builtin_stress-test_Name", "CPU / GPU 烤机");
    public string Description => LocalizationService.L("Builtin_stress-test_Desc", "CPU 和 GPU 压力测试，支持单烤与双烤，实时监控温度、频率和功耗。");
    public string Glyph => "\uECAD";
    public string Category => "硬件工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(StressTestPage));
        return Task.CompletedTask;
    }
}
