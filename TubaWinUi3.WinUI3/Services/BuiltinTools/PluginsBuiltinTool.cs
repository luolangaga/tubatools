using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class PluginsBuiltinTool : IBuiltinTool
{
	public string Id => "plugins";
	public string Name => LocalizationService.L("Builtin_plugins_Name", "插件");
	public string Description => LocalizationService.L("Builtin_plugins_Desc", "从 NuGet 发现、安装和管理图吧工具箱插件。");
	public string Glyph => "\ue774";
	public string Category => "实用工具";
	public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

	public Task ExecuteAsync(BuiltinToolContext context)
	{
		App.MainWindow?.NavigateToToolPage(typeof(PluginsPage));
		return Task.CompletedTask;
	}
}
