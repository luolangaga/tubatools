namespace TubaWinUi3.Services;

public sealed class AiAssistantTool : IBuiltinTool
{
    public string Id => "ai-assistant";
    public string Name => LocalizationService.L("Builtin_ai-assistant_Name", "AI 助手");
    public string Description => LocalizationService.L("Builtin_ai-assistant_Desc", "对话解决问题，使用 AI 开发、编译并加载工具箱插件。");
    public string Glyph => "\uE99A";
    public string Category => "实用工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;
    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(Pages.AiAssistantPage));
        return Task.CompletedTask;
    }
}
