using TubaWinUi3.PluginSdk;

namespace Tubatools.MacOSInstaller;

public sealed class MacOSInstallerPlugin : ITubaPlugin
{
    public void Register(IPluginHost host)
    {
        bool chinese = host.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        host.RegisterTool(new PluginToolDefinition
        {
            Id = "macos-installer",
            Name = chinese ? "macOS 安装助手" : "macOS Installer",
            Category = chinese ? "系统工具" : "System tools",
            Description = chinese
                ? "新手向导：四步完成硬件检查、EFI 生成、BIOS 设置与 macOS 安装 U 盘制作"
                : "Beginner wizard: hardware check, EFI build, BIOS setup and macOS installer USB in four steps",
            IconPath = "assets/icon.svg",
            Glyph = "\uE7F4",
            Tags = ["macOS", "OpenCore", "黑苹果", "U盘", "引导", "新手向导"],
            CreateContent = ctx => InstallerPage.Create(ctx)
        });
    }
}
