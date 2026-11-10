namespace TubaWinUi3.PluginSdk;

/// <summary>每个插件一个入口类型，必须有公共无参构造函数。</summary>
public interface ITubaPlugin
{
    void Register(IPluginHost host);
}

public interface IPluginHost
{
    int ApiVersion => 2;
    /// <summary>宿主界面语言（zh-CN / en-US），供插件本地化工具名与说明。</summary>
    string Language => "zh-CN";
    /// <summary>API v2: one tool can host a complete multi-function XAML page.</summary>
    void RegisterTool(PluginToolDefinition tool);
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
