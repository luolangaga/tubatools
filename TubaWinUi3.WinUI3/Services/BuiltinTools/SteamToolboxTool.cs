namespace TubaWinUi3.Services;
public sealed class SteamToolboxTool : IBuiltinTool
{
    public string Id => "steam-toolbox";
    public string Name => LocalizationService.L("Builtin_steam-toolbox_Name", "Steam 工具箱");
    public string Description => LocalizationService.L("Builtin_steam-toolbox_Desc", "Steam 本机账号、多存储库游戏管理与内置挂时长");
    public string Glyph => "\uE7FC";
    public string Category => "游戏工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;
    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(TubaWinUi3.Pages.SteamToolboxPage));
        return Task.CompletedTask;
    }
}
