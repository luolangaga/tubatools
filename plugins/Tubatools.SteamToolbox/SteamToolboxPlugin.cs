using TubaWinUi3.PluginSdk;

namespace Tubatools.SteamToolbox;

public sealed class SteamToolboxPlugin : ITubaPlugin
{
    private static int _exitHooked;

    public void Register(IPluginHost host)
    {
        // 插件页面关闭后挂卡会话仍在后台运行；进程退出时礼貌地结束会话并释放 Steam 连接。
        if (Interlocked.Exchange(ref _exitHooked, 1) == 0)
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { SteamIdleService.Disconnect(); } catch { }
            };

        bool chinese = host.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        host.RegisterTool(new PluginToolDefinition
        {
            Id = "steam-toolbox",
            Name = chinese ? "Steam 工具箱" : "Steam Toolbox",
            Category = "游戏工具",
            Description = chinese ? "Steam 本机账号、多存储库游戏管理与内置挂时长" : "Local Steam accounts, game libraries and built-in idling",
            IconPath = "assets/icon.svg",
            Glyph = "\uE7FC",
            Tags = ["Steam", "游戏", "挂卡", "时长"],
            CreateContent = SteamToolboxView.Create
        });
    }
}
