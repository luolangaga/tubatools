using Tubatools.AiJunkCleanup.Runtime;
using TubaWinUi3.PluginSdk;
using Tubatools.Onboarding;

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
        PluginOpenCompat.RegisterDestinationEntry(host, PluginDestination.AiAssistant);
        // 安装/更新完成立刻展示已有的教程；启动钩子里的首次教程会被抑制，避免弹两遍。
        AiJunkCleanupRuntime.ScheduleInstallGuide();
    }

    public ValueTask StartupAsync(IPluginStartupContext context, CancellationToken ct = default) =>
        AiJunkCleanupRuntime.Current.StartupAsync(context, ct);

    public ValueTask ShutdownAsync(CancellationToken ct = default) =>
        AiJunkCleanupRuntime.Current.ShutdownAsync();
}
