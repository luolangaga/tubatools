using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class BenchmarkCloudTool : IBuiltinTool
{
	public string Id => "benchmark-cloud";
	public string Name => LocalizationService.L("Builtin_benchmark-cloud_Name", "跑分排行");
	public string Description => LocalizationService.L("Builtin_benchmark-cloud_Desc", "查看全球排行榜，与同硬件用户对比性能。");
	public string Glyph => "\ue9d5";
	public string Category => "硬件工具";
	public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

	public Task ExecuteAsync(BuiltinToolContext context)
	{
		App.MainWindow?.NavigateToToolPage(typeof(BenchmarkCloudPage));
		return Task.CompletedTask;
	}
}
