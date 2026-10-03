using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class GpuDriverDownloadTool : IBuiltinTool
{
    public string Id => "gpu-driver-download";
    public string Name => LocalizationService.L("Builtin_gpu-driver-download_Name", "显卡驱动下载");
    public string Description => LocalizationService.L("Builtin_gpu-driver-download_Desc", "在 NVIDIA、AMD、Intel 官方页面选择显卡型号和驱动版本，并直接下载官方安装包。");
    public string Glyph => "\uE950";
    public string Category => "硬件工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(GpuDriverDownloadPage));
        return Task.CompletedTask;
    }
}
