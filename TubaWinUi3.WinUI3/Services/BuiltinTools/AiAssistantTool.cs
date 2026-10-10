using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

/// <summary>
/// 内置工具入口「AI 助手」：承载新的智能代理页面（AiAgentPage）。
/// 完整版 Agent 架构：多轮工具调用循环 + 步骤链可视化 + 确认卡片。
/// </summary>
public sealed class AiAssistantTool : IBuiltinTool
{
    public string Id => "ai-assistant";
    public string Name => LocalizationService.L("Builtin_ai-assistant_Name", "AI 助手");
    public string Description => LocalizationService.L("Builtin_ai-assistant_Desc", "智能系统代理，可诊断问题、优化配置、读写文件、执行命令、联网搜索并执行操作。");
    public string Glyph => "\uE946";
    public string Category => "实用工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        var page = new AiAgentPage();

        App.MainWindow?.NavigateToToolPage(typeof(ToolContentPage), new ToolContentPageParam
        {
            ToolId = Id,
            Title = "AI 助手",
            Description = "智能系统代理，可诊断问题、优化配置、执行操作并联网搜索",
            Glyph = Glyph,
            Content = page,
            OnClose = () => page.Unload()
        });

        return Task.CompletedTask;
    }
}
