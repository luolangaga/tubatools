using TubaWinUi3.PluginSdk;

namespace Tubatools.CapybaraLulu;

public sealed class CapybaraPlugin : ITubaPlugin, IPluginStartup
{
    /// <summary>
    /// 美化包不注册成内置工具：它只有一个设置页入口，由启动钩子注入。
    /// 这样「全部工具」「内置工具」和搜索里都不会多出一张卡片。
    /// </summary>
    public void Register(IPluginHost host) { }

    public ValueTask StartupAsync(IPluginStartupContext context, CancellationToken ct = default)
        => CapybaraRuntime.StartAsync(context, ct);

    public ValueTask ShutdownAsync(CancellationToken ct = default)
        => CapybaraRuntime.StopAsync(ct);
}
