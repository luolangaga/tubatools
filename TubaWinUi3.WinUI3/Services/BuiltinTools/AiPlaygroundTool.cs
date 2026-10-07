using TubaWinUi3.Services;

namespace TubaWinUi3.Services;

/// <summary>本地 AI 试炼场：在设备本地部署并运行 LLM / 图像分类 / 目标检测 / 图像特征模型（NPU / GPU / CPU）。</summary>
public sealed class AiPlaygroundTool : IBuiltinTool
{
    public string Id => "ai-playground";
    public string Name => LocalizationService.L("Builtin_ai-playground_Name", "本地AI试炼场");
    public string Description => LocalizationService.L("Builtin_ai-playground_Desc",
        "本地部署并运行 AI 模型：LLM 对话、图像分类、目标检测、图像特征对比，支持 NPU / GPU / CPU 加速。");
    public string Glyph => "\uE950";
    public string Category => "实用工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.AiPlaygroundPage));
        return Task.CompletedTask;
    }
}
