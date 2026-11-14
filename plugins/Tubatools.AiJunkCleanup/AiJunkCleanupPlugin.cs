using Tubatools.AiJunkCleanup.Runtime;
using TubaWinUi3.PluginSdk;

namespace Tubatools.AiJunkCleanup;

/// <summary>
/// 不注册任何内置工具/工具卡片。插件只实现启动钩子，在 AI 助手页面运行时注入「垃圾清理」模式，
/// 因此不会占用工具箱的工具入口资源，也不需要修改宿主代码。
/// </summary>
public sealed class AiJunkCleanupPlugin : ITubaPlugin, IPluginStartup
{
    public void Register(IPluginHost host)
    {
        // 故意不调用 host.RegisterTool：本插件只增加 AI 模式，不新增工具入口。
    }

    public ValueTask StartupAsync(IPluginStartupContext context, CancellationToken ct = default) =>
        AiJunkCleanupRuntime.Current.StartupAsync(context, ct);

    public ValueTask ShutdownAsync(CancellationToken ct = default) =>
        AiJunkCleanupRuntime.Current.ShutdownAsync();
}
