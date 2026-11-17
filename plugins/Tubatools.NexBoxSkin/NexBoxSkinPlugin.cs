using TubaWinUi3.PluginSdk;
using Tubatools.Onboarding;

namespace Tubatools.NexBoxSkin;

/// <summary>
/// Skin-only plugin: installs the NexBox shell and leaves the host tool registry untouched.
/// All game/utility pages live in the web sidebar; the only host integration is the
/// injected Settings card and the startup hint.
/// </summary>
public sealed class NexBoxSkinPlugin : ITubaPlugin, IPluginStartup
{
    public void Register(IPluginHost host)
    {
        bool chinese = host.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        PluginOpenCompat.RegisterDestinationEntry(host, PluginDestination.Settings);
        PluginOnboardingDialog.ScheduleInstallGuide(PluginOnboardingDialog.Guide(
            chinese ? "NexBox 皮肤" : "NexBox Skin",
            chinese ? "NexBox 皮肤已启用，主界面切换为 NexBox 风格。" : "The NexBox skin is active and the main window now uses the NexBox layout.",
            [
                new OnboardingEntry(
                    chinese ? "外观" : "Appearance",
                    chinese ? "NexBox 主界面" : "NexBox main window",
                    chinese ? "设置页「工具和系统集成」里可以随时关闭皮肤。" : "Turn the skin off anytime under Settings → Tools and system integration.",
                    "\uE790",
                    chinese ? "打开设置" : "Open settings",
                    PluginOnboardingDialog.OpenSettings())
            ], chinese));
    }

    public ValueTask StartupAsync(IPluginStartupContext context, CancellationToken cancellationToken = default) =>
        new(NexBoxSkinRuntime.StartAsync(context, cancellationToken));

    public async ValueTask ShutdownAsync(CancellationToken cancellationToken = default) =>
        await NexBoxSkinRuntime.StopAsync();
}
