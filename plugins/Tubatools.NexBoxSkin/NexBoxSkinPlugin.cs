using TubaWinUi3.PluginSdk;

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
    }

    public ValueTask StartupAsync(IPluginStartupContext context, CancellationToken cancellationToken = default) =>
        new(NexBoxSkinRuntime.StartAsync(context, cancellationToken));

    public async ValueTask ShutdownAsync(CancellationToken cancellationToken = default) =>
        await NexBoxSkinRuntime.StopAsync();
}
