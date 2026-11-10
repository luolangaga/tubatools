namespace TubaWinUi3.PluginSdk;

/// <summary>
/// 可选的插件启动钩子。宿主在主窗口创建后调用 <see cref="StartupAsync"/>；
/// 插件被停用/卸载时调用 <see cref="ShutdownAsync"/>。实现该接口的插件不需要先打开工具页。
/// </summary>
public interface IPluginStartup
{
    ValueTask StartupAsync(IPluginStartupContext context, CancellationToken ct = default);
    ValueTask ShutdownAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
}

/// <summary>启动钩子的宿主上下文。HostAccess 仅在插件声明 fullTrust 时可用。</summary>
public interface IPluginStartupContext
{
    string PluginId { get; }
    string DataDirectory { get; }
    IPluginHostAccess? HostAccess { get; }
    Task<string?> GetSettingAsync(string key, CancellationToken ct = default);
    Task SetSettingAsync(string key, string? value, CancellationToken ct = default);
}
