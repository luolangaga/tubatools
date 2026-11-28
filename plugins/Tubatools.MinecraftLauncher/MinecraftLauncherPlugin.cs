using TubaWinUi3.PluginSdk;
using Tubatools.MinecraftLauncher.Ui;
using Tubatools.Onboarding;

namespace Tubatools.MinecraftLauncher;

public sealed class MinecraftLauncherPlugin : ITubaPlugin
{
    public void Register(IPluginHost host)
    {
        bool chinese = host.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        host.RegisterTool(new PluginToolDefinition
        {
            Id = "minecraft-launcher",
            Name = chinese ? "我的世界启动器" : "Minecraft Launcher",
            Category = "游戏工具",
            Description = chinese
                ? "现代 Minecraft 风格：一键安装原版 / Fabric / Quilt / Forge / NeoForge，Modrinth 模组、光影与资源包一键下载，微软正版与离线账户"
                : "Modern Minecraft style launcher: one-click Vanilla/Fabric/Quilt/Forge/NeoForge install, Modrinth mods/shaders/resource packs, Microsoft and offline accounts",
            IconPath = "assets/icon.svg",
            Glyph = "\uE7FC",
            Tags = ["Minecraft", "我的世界", "启动器", "PCL", "Forge", "Fabric", "NeoForge", "Quilt", "Modrinth", "模组", "光影", "微软登录"],
            CreateContent = McLauncherView.Create
        });
        PluginOpenCompat.RegisterToolEntry(host, "minecraft-launcher");
        PluginOnboardingDialog.ScheduleInstallGuide(PluginOnboardingDialog.Guide(
            chinese ? "我的世界启动器" : "Minecraft Launcher",
            chinese
                ? "选择游戏目录、登录微软或添加离线账户后即可选择版本启动；还没有版本时可在「下载」页安装。"
                : "Pick a game folder and sign in with Microsoft or an offline account, then launch any installed version. Install new versions from the Download tab.",
            [
                new OnboardingEntry(
                    chinese ? "内置工具" : "Built-in tool",
                    chinese ? "我的世界启动器" : "Minecraft Launcher",
                    chinese
                        ? "已添加到「游戏工具」分类，界面布局参考 PCL CE。"
                        : "Added under Gaming tools with a PCL CE style layout.",
                    "\uE7FC",
                    chinese ? "打开" : "Open",
                    PluginOnboardingDialog.OpenTool("minecraft-launcher"))
            ], chinese));
    }
}
