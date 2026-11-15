using System.Security.Cryptography;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TubaWinUi3.Pages;
using TubaWinUi3.PluginSdk;

namespace TubaWinUi3.Services.Plugins;

internal sealed partial class PluginHostServices(string pluginId, string toolId, string packageDirectory, DispatcherQueue dispatcher,
    Func<XamlRoot?> getRoot, Action<string> report, bool fullTrust = false, IReadOnlyList<string>? permissions = null) : IPluginServices
{
    /// <summary>
    /// 兼容 1.9.0 及更早版本的 fullTrust 插件：像 MacSkin 这类插件会用反射按
    /// 「7 个参数」的构造函数重建一份宿主服务（SDK 没有暴露 IPluginServices，它只能这么做）。
    /// 反射构造按参数个数精确匹配，可选参数不算数——所以一旦给主构造函数加参数，
    /// 这些插件就会在启动时抛 MissingMethodException。保留这个旧签名，新增参数请只加在后面。
    /// </summary>
    internal PluginHostServices(string pluginId, string toolId, string packageDirectory, DispatcherQueue dispatcher,
        Func<XamlRoot?> getRoot, Action<string> report, bool fullTrust)
        : this(pluginId, toolId, packageDirectory, dispatcher, getRoot, report, fullTrust, null)
    {
    }

    private readonly PluginSettingsStore _store = new(pluginId);
    public string PluginId => pluginId;
    public string PackageDirectory => packageDirectory;

    /// <summary>本插件是否声明了某项能力（fullTrust 恒为 true；无清单时兼容放行）。</summary>
    internal bool Allowed(string capability) => PluginCapabilities.IsAllowed(permissions, fullTrust, capability);

    /// <summary>能力检查：未声明时抛出可直接展示给插件作者的错误。</summary>
    private void Require(string capability)
    {
        if (!Allowed(capability)) throw PluginCapabilities.Denied(capability);
    }

    public string GetAssetPath(string relativePath)
    {
        var path = PluginPackageRules.ResolveFile(packageDirectory, relativePath);
        return File.Exists(path) ? path : throw new FileNotFoundException("插件资源不存在。", path);
    }
    public string DataDirectory { get { Directory.CreateDirectory(_store.DirectoryPath); return _store.DirectoryPath; } }
    public Task<string?> GetSettingAsync(string key, CancellationToken ct = default) => _store.GetAsync(key, ct);
    public Task SetSettingAsync(string key, string? value, CancellationToken ct = default) => _store.SetAsync(key, value, ct);
    private static string CatalogId(string path) => "catalog:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
    public async Task<IReadOnlyList<PluginToolInfo>> GetToolsAsync(CancellationToken ct = default)
    {
        Require(PluginCapabilities.Tools);
        ct.ThrowIfCancellationRequested();
        var tools = await ToolCatalog.GetAllToolsAsync(); ct.ThrowIfCancellationRequested();
        return BuiltinToolRegistry.Tools.Select(t => new PluginToolInfo(t.Id, t.Name, t.Category, t.Description, true))
            .Concat(tools.Where(t => !t.IsBuiltinLink).Select(t => new PluginToolInfo(CatalogId(t.Path), t.Name, t.Category, t.Description ?? "", false))).ToArray();
    }
    public Task RunToolAsync(string id, CancellationToken ct = default) => OnUi(async () =>
    {
        // 启动外部工具＝执行任意程序，能力要求比「枚举工具列表」更高。
        Require(PluginCapabilities.Process);
        if (id.Equals(toolId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("工具不能递归启动自身。请选择页面中的具体功能。");
        if (BuiltinToolRegistry.Tools.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } builtin)
        {
            await builtin.ExecuteTrackedAsync(new BuiltinToolContext { XamlRoot = getRoot() ?? throw new InvalidOperationException("工具页面已关闭。"), CancellationToken = ct, OnProgress = report });
            return;
        }
        var tool = (await ToolCatalog.GetAllToolsAsync()).FirstOrDefault(t => !t.IsBuiltinLink && CatalogId(t.Path) == id)
            ?? throw new KeyNotFoundException("工具不存在或已不可用，请重新获取工具列表。");
        await CatalogToolLauncher.LaunchAsync(tool, dispatcher, getRoot, (_, message, _) => report(message));
    }, ct);
    public Task NavigateAsync(PluginDestination destination, CancellationToken ct = default) => OnUi(() =>
    {
        var window = App.MainWindow ?? throw new InvalidOperationException("主窗口不可用。");
        if (destination == PluginDestination.Settings) window.NavigateToSettings();
        else window.NavigateToToolPage(destination switch
        {
            PluginDestination.Home => typeof(DashboardPage), PluginDestination.Hardware => typeof(HardwarePage),
            PluginDestination.BuiltinTools => typeof(BuiltinToolsPage), PluginDestination.Plugins => typeof(PluginsPage),
            PluginDestination.AiAssistant => typeof(AiAssistantPage), _ => throw new ArgumentOutOfRangeException(nameof(destination))
        });
        return Task.CompletedTask;
    }, ct);
    public PluginAppearance GetAppearance() => new(ThemeService.CurrentTheme switch
    { AppTheme.Light => PluginTheme.Light, AppTheme.Dark => PluginTheme.Dark, _ => PluginTheme.System },
        NavLayoutModeService.IsTabMode() ? PluginNavigationLayout.Tabs : PluginNavigationLayout.Sidebar, CompactModeService.IsCompactModeEnabled());
    public Task ApplyAppearanceAsync(PluginAppearance appearance, CancellationToken ct = default)
    {
        Require(PluginCapabilities.Appearance);
        ArgumentNullException.ThrowIfNull(appearance);
        if (!Enum.IsDefined(appearance.Theme) || !Enum.IsDefined(appearance.Navigation)) throw new ArgumentOutOfRangeException(nameof(appearance));
        return OnUi(() =>
        {
            ThemeService.SetTheme(appearance.Theme switch { PluginTheme.Light => AppTheme.Light, PluginTheme.Dark => AppTheme.Dark, _ => AppTheme.Default });
            var layout = appearance.Navigation == PluginNavigationLayout.Tabs ? "tabs" : "sidebar";
            if (NavLayoutModeService.GetNavLayoutMode() != layout) NavLayoutModeService.SetNavLayoutMode(layout);
            if (CompactModeService.IsCompactModeEnabled() != appearance.Compact) CompactModeService.SetCompactModeEnabled(appearance.Compact);
            return Task.CompletedTask;
        }, ct);
    }
    public Task OpenUriAsync(Uri uri, CancellationToken ct = default)
    {
        Require(PluginCapabilities.Uri);
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("https" or "http" or "ms-settings")) throw new ArgumentException("只支持 HTTP、HTTPS 和 ms-settings 链接。");
        return OnUi(async () => { if (!await Windows.System.Launcher.LaunchUriAsync(uri)) throw new IOException("无法打开链接。"); }, ct);
    }
    public Task CopyTextAsync(string text, CancellationToken ct = default) => OnUi(() =>
    {
        Require(PluginCapabilities.Clipboard);
        var result = ClipboardService.TrySetText(text, flush: true);
        if (!result.Success) throw result.Error ?? new InvalidOperationException("剪贴板正被占用，无法复制。");
        return Task.CompletedTask;
    }, ct);
    public async Task<string?> PickOpenFileAsync(string title, CancellationToken ct = default)
    {
        Require(PluginCapabilities.Files);
        string? path = null;
        await OnUi(() => { path = Win32Dialogs.PickOpen("所有文件\0*.*\0\0", title); return Task.CompletedTask; }, ct);
        return path;
    }
    public async Task<string?> PickFolderAsync(string title, CancellationToken ct = default)
    {
        Require(PluginCapabilities.Files);
        string? path = null;
        await OnUi(async () => { path = await Win32Dialogs.PickFolderAsync(title); }, ct);
        return path;
    }
    private Task OnUi(Func<Task> action, CancellationToken ct) => PluginUiThread.RunAsync(dispatcher, action, ct);
}
