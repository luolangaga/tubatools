using TubaWinUi3.PluginSdk;
using Tubatools.Onboarding;

namespace Tubatools.CapybaraLulu;

public sealed class CapybaraPlugin : ITubaPlugin, IPluginStartup
{
    /// <summary>
    /// 美化包不注册成内置工具：它只有一个设置页入口，由启动钩子注入。
    /// 这样「全部工具」「内置工具」和搜索里都不会多出一张卡片。
    /// </summary>
    public void Register(IPluginHost host)
    {
        bool chinese = host.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        PluginOpenCompat.RegisterDestinationEntry(host, PluginDestination.Settings);
        PluginOnboardingDialog.ScheduleInstallGuide(PluginOnboardingDialog.Guide(
            chinese ? "水豚噜噜美化包" : "Capybara Lulu",
            chinese ? "水豚噜噜美化包已启用：柔和动态背景、侧栏页脚伙伴与硬件页卡片。" : "Capybara Lulu is active: soft animated background, sidebar companion and hardware page cards.",
            [
                new OnboardingEntry(
                    chinese ? "外观" : "Appearance",
                    chinese ? "水豚噜噜美化" : "Capybara Lulu",
                    chinese ? "已自动生效；设置页「工具和系统集成」里可以调整或关闭。" : "It is already applied. Adjust or disable it under Settings → Tools and system integration.",
                    "\uE790",
                    chinese ? "打开设置" : "Open settings",
                    PluginOnboardingDialog.OpenSettings())
            ], chinese));
    }

    public ValueTask StartupAsync(IPluginStartupContext context, CancellationToken ct = default)
        => CapybaraRuntime.StartAsync(context, ct);

    public ValueTask ShutdownAsync(CancellationToken ct = default)
        => CapybaraRuntime.StopAsync(ct);
}
