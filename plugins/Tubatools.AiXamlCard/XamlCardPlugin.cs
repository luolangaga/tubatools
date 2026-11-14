using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.PluginSdk;

namespace Tubatools.AiXamlCard;

public sealed class XamlCardPlugin : ITubaPlugin, IPluginStartup
{
    public const string ToolId = "ai-xaml-card";

    private static PluginExecutionContext? _running;
    private static CardPage? _page;
    private static CardStore? _store;
    private static CardRequestWatcher? _watcher;
    private static CardWorkspaceWatcher? _rules;

    public void Register(IPluginHost host) => host.RegisterTool(new PluginToolDefinition
    {
        Id = ToolId,
        Name = host.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "AI UI Cards" : "AI 界面卡片",
        Category = "其他工具",
        Description = host.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? "Render native WinUI cards the assistant writes, with dynamically compiled C# code-behind."
            : "渲染助手写的原生 WinUI 卡片：XAML 界面 + 动态编译的 C# 代码后置。",
        IconPath = "assets/icon.svg",
        Tags = ["AI", "XAML", "界面", "动态编译"],
        ShowInSettings = true,
        CreateContent = CreateCardPage,
        InitializeAsync = InitializeCardPage,
    });

    public ValueTask StartupAsync(IPluginStartupContext context, CancellationToken ct = default)
    {
        try { CardSkill.Install(context.DataDirectory, ResolveScript()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        // 「多用卡片」不只靠技能描述：把偏好写进 AGENTS.md 并持续保住，
        // 因为宿主每次连接都会整份重写那份文件。
        CardWorkspaceRules.Install(context.DataDirectory);
        _rules = new CardWorkspaceWatcher(context.DataDirectory);

        _store = new CardStore(ResolveWorkspace(context.DataDirectory));
        // 「直接展示」的主通道：助手提交请求，插件立刻开窗渲染。
        // 必须用宿主 UI 线程的 DispatcherQueue，窗口只能在那里创建。
        if (TubaWinUi3.Plugins.PluginHostBridge.Dispatcher is { } dispatcher)
            _watcher = new CardRequestWatcher(context.DataDirectory, dispatcher);

        _watcher?.Start();
        return ValueTask.CompletedTask;
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default)
    {
        _watcher?.Dispose();
        _watcher = null;
        _rules?.Dispose();
        _rules = null;
        _page?.Deactivate();
        _page = null;
        _store = null;
        return ValueTask.CompletedTask;
    }

    private static object CreateCardPage(PluginExecutionContext context)
    {
        _running = context;
        _store ??= new CardStore(ResolveWorkspace(context.Services.DataDirectory));
        _page = new CardPage(_store);
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _page.Build()
        };
    }

    private static Task InitializeCardPage(PluginViewContext view)
    {
        _page?.Activate();
        view.OnClosed(() => _page?.Deactivate());
        return Task.CompletedTask;
    }

    /// <summary>安装包里带一份提交脚本，复制到技能目录供助手调用。</summary>
    private static string? ResolveScript()
    {
        var package = TryPackageDirectory();
        if (package is null) return null;
        var path = Path.Combine(package, "scripts", CardSkill.ScriptName);
        return File.Exists(path) ? path : null;
    }

    private static string? TryPackageDirectory()
    {
        var assembly = typeof(XamlCardPlugin).Assembly.Location;
        return assembly.Length == 0 ? null : Path.GetDirectoryName(assembly);
    }

    /// <summary>
    /// AI 工作区位于数据目录的 AiAssistant/Workspace；插件不假设宿主内部路径，
    /// 找不到时退回自己的数据目录，仍然可用。
    /// </summary>
    private static string ResolveWorkspace(string pluginData)
    {
        foreach (var candidate in Candidates(pluginData))
            if (Directory.Exists(candidate)) return candidate;
        var fallback = Path.Combine(pluginData, "Workspace");
        try { Directory.CreateDirectory(fallback); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return fallback;
    }

    /// <summary>pluginData = &lt;DataDir&gt;/PluginData/&lt;id&gt;，上溯几层即可遇到 AiAssistant。</summary>
    private static IEnumerable<string> Candidates(string pluginData)
    {
        var directory = new DirectoryInfo(pluginData);
        for (var i = 0; i < 4 && directory is not null; i++, directory = directory.Parent)
            yield return Path.Combine(directory.FullName, "AiAssistant", "Workspace");
    }
}
