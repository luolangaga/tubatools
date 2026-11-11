using TubaWinUi3.PluginSdk;

namespace Tubatools.Xiaoheihe;

public sealed class XiaoheihePlugin : ITubaPlugin
{
    public void Register(IPluginHost host)
    {
        bool chinese = host.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        host.RegisterTool(new PluginToolDefinition
        {
            Id = "xiaoheihe",
            Name = chinese ? "小黑盒" : "Xiaoheihe",
            Category = "游戏工具",
            Description = chinese
                ? "小黑盒社区客户端：扫码登录、推荐流、社区、搜索、帖子评论、签到、收藏、历史与游戏库"
                : "Xiaoheihe community client: QR login, feeds, topics, search, posts, comments, check-in, favourites and game library",
            IconPath = "assets/icon.svg",
            Glyph = "\uE7FC",
            Tags = ["小黑盒", "Heybox", "社区", "游戏", "论坛"],
            CreateContent = XiaoheiheView.Create
        });
    }
}
