using TubaWinUi3.Pages;
namespace TubaWinUi3.Services;

public sealed class WifiPasswordTool : IBuiltinTool
{
    public string Id => "wifi-password";
    public string Name => LocalizationService.L("Builtin_wifi-password_Name", "WiFi 密码");
    public string Description => LocalizationService.L("Builtin_wifi-password_Desc", "查看已保存的 WiFi 密码，扫描局域网设备并在路由器中踢下线。");
    public string Glyph => "\uE701";
    public string Category => "网络工具";
    public BuiltinToolKind Kind => BuiltinToolKind.BackgroundTask;
    public Task ExecuteAsync(BuiltinToolContext context)
    {
        var view = new WifiPasswordView();
        App.MainWindow?.NavigateToToolPage(typeof(ToolContentPage), new ToolContentPageParam
        {
            ToolId = Id, Title = "WiFi 密码与网络", Glyph = Glyph,
            Description = "管理已保存的网络、查看连接信息、扫码分享，扫描局域网设备并在路由器中踢下线。",
            Content = view, OnClose = view.ClearSensitiveData
        });
        return Task.CompletedTask;
    }
}
