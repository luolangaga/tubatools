namespace TubaWinUi3.PluginSdk;

/// <summary>每个插件一个入口类型，必须有公共无参构造函数。</summary>
public interface ITubaPlugin
{
    void Register(IPluginHost host);
}

/// <summary>「打开」入口的回调：宿主点插件页的「打开」按钮时在 UI 线程执行。</summary>
public delegate Task PluginOpenHandler(PluginOpenContext context);

public interface IPluginHost
{
    int ApiVersion => 2;
    /// <summary>宿主界面语言（zh-CN / en-US），供插件本地化工具名与说明。</summary>
    string Language => "zh-CN";
    /// <summary>API v2: one tool can host a complete multi-function XAML page.</summary>
    void RegisterTool(PluginToolDefinition tool);
    /// <summary>
    /// 注册插件「打开」入口（可选）：注册后插件页显示绿色「打开」按钮，点击时执行这段代码；
    /// 不注册的插件继续显示「已安装」。只能在 Register 期间调用，且每个插件只能注册一次。
    /// </summary>
    void RegisterOpen(PluginOpenHandler open) =>
        throw new NotSupportedException("此宿主没有提供插件「打开」入口，请更新工具箱。");
}

public sealed class PluginExecutionContext
{
    public required CancellationToken CancellationToken { get; init; }
    public required Func<string, string, Task> ShowMessageAsync { get; init; }
    public required IPluginServices Services { get; init; }
    public Func<string, string, Task<bool>> ConfirmAsync { get; init; } = (_, _) => throw new NotSupportedException();
    /// <summary>Shows inline progress. Empty or whitespace closes the progress banner.</summary>
    public Action<string> ReportProgress { get; init; } = _ => { };
}
