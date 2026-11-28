namespace TubaWinUi3.PluginSdk;

/// <summary>
/// 插件「打开」入口。可选实现：只有调用 <see cref="IPluginHost.RegisterOpen"/> 注册过的插件才会在
/// 插件页（发现 / 已安装 / 详情）显示绿色「打开」按钮，点击时执行这段插件自己的代码；
/// 没有注册的插件保持原来的「已安装」状态，旧插件无需任何改动。
/// </summary>
public sealed class PluginOpenContext
{
    /// <summary>本次打开动作的取消令牌。</summary>
    public required CancellationToken CancellationToken { get; init; }

    /// <summary>与工具页相同的宿主服务（插件设置、包内资源路径与公开宿主能力）。</summary>
    public required IPluginServices Services { get; init; }

    /// <summary>仅在清单声明 <c>fullTrust: true</c> 时提供，否则为 null。</summary>
    public IPluginHostAccess? HostAccess { get; init; }

    /// <summary>打开本插件注册的工具页；省略 <c>toolId</c> 时打开第一个工具。没有注册工具时抛错。</summary>
    public required Func<string?, Task> OpenToolAsync { get; init; }

    /// <summary>原生可滚动消息对话框。</summary>
    public required Func<string, string, Task> ShowMessageAsync { get; init; }

    /// <summary>原生确认对话框，默认不选确认。</summary>
    public required Func<string, string, Task<bool>> ConfirmAsync { get; init; }
}
