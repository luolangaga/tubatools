namespace TubaWinUi3.Services;

public sealed class WebsiteAccelerationTool : IBuiltinTool
{
    public string Id => "website-acceleration";
    public string Name => LocalizationService.L("Builtin_website-acceleration_Name", "网站加速");
    public string Description => LocalizationService.L("Builtin_website-acceleration_Desc", "内置网站加速，支持服务全选、实时流量图表与连接详情");
    public string Glyph => "\uE774";
    public string Category => "网络工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;
    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.WebsiteAccelerationPage));
        return Task.CompletedTask;
    }
}
