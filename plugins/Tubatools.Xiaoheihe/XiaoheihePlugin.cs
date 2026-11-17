using TubaWinUi3.PluginSdk;
using Tubatools.Onboarding;

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
        PluginOpenCompat.RegisterToolEntry(host, "xiaoheihe");
        PluginOnboardingDialog.ScheduleInstallGuide(PluginOnboardingDialog.Guide(
            chinese ? "小黑盒" : "Xiaoheihe",
            chinese ? "小黑盒客户端已就绪，打开后扫码登录即可。" : "The Xiaoheihe client is ready; open it and scan the QR code to sign in.",
            [
                new OnboardingEntry(
                    chinese ? "内置工具" : "Built-in tool",
                    chinese ? "小黑盒" : "Xiaoheihe",
                    chinese ? "已添加到「游戏工具」分类，打开后扫码登录。" : "Added under Gaming tools. Open it and scan the QR code to sign in.",
                    "\uE7FC",
                    chinese ? "打开" : "Open",
                    PluginOnboardingDialog.OpenTool("xiaoheihe"))
            ], chinese));
    }
}
