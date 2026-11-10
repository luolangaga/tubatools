namespace TubaWinUi3.PluginSdk;

/// <summary>Prefer one meaningful tool page containing related actions, inputs and results.</summary>
public sealed class PluginToolDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Category { get; init; } = "其他工具";
    public string Description { get; init; } = "";
    public string Glyph { get; init; } = "\uE7F4";
    /// <summary>Package-relative SVG/PNG/JPEG/BMP path. Falls back to the manifest icon, then Glyph.</summary>
    public string IconPath { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    /// <summary>Runtime WinUI XAML fragment. One FrameworkElement root, no x:Class/event attributes/x:Bind.</summary>
    public string? Xaml { get; init; }
    /// <summary>Alternative to inline Xaml: package-relative .xaml file, loaded by the host.</summary>
    public string? XamlPath { get; init; }
    /// <summary>Alternative for advanced plugins referencing WinUI: return a fresh FrameworkElement.</summary>
    public Func<PluginExecutionContext, object>? CreateContent { get; init; }
    /// <summary>For the rare tool that only needs a single immediate operation; mutually exclusive with UI.</summary>
    public Func<PluginExecutionContext, Task>? ExecuteAsync { get; init; }
    public Func<PluginViewContext, Task>? InitializeAsync { get; init; }
    public bool ShowInSidebar { get; init; }
    public bool ShowInSettings { get; init; }
}

public sealed class PluginViewContext
{
    public required PluginExecutionContext Host { get; init; }
    public IPluginServices Services => Host.Services;
    public CancellationToken CancellationToken => Host.CancellationToken;
    public required object Root { get; init; }
    public required Func<string, object?> FindElement { get; init; }
    /// <summary>Button activation or ToggleSwitch state changes. Callbacks start on the UI thread and are serialized per page.</summary>
    public required Action<string, Func<PluginExecutionContext, Task>> BindClick { get; init; }
    /// <summary>ToggleSwitch/ToggleButton, TextBox, ComboBox, NumberBox or Slider changes. SetValue and initialization do not invoke callbacks.</summary>
    public Action<string, Func<PluginExecutionContext, Task>> BindValueChanged { get; init; } = (_, _) => throw new NotSupportedException("此宿主不支持 BindValueChanged，请更新工具箱。");
    public required Func<string, string> GetValue { get; init; }
    public required Action<string, string> SetValue { get; init; }
    public required Action<string, bool> SetEnabled { get; init; }
    public required Action<string, bool> SetVisible { get; init; }
    public required Action<string, string> SetImage { get; init; }
    public required Func<Func<Task>, Task> RunOnUiThreadAsync { get; init; }
    /// <summary>Detach custom events/timers/resources when the page closes or navigation leaves it.</summary>
    public required Action<Action> OnClosed { get; init; }
}

public enum PluginTheme { System, Light, Dark }
public enum PluginNavigationLayout { Sidebar, Tabs }
public enum PluginDestination { Home, Hardware, BuiltinTools, Settings, Plugins, AiAssistant }
public sealed record PluginAppearance(PluginTheme Theme, PluginNavigationLayout Navigation, bool Compact);
public sealed record PluginToolInfo(string Id, string Name, string Category, string Description, bool IsBuiltin);

/// <summary>Stable host capabilities; no access to arbitrary internal app setting keys.</summary>
public interface IPluginServices
{
    /// <summary>Returns a documented SDK host service, or null if unavailable. Does not expose internal implementation types.</summary>
    T? GetService<T>() where T : class => null;
    string PluginId { get; }
    string PackageDirectory { get; }
    string DataDirectory { get; }
    string GetAssetPath(string relativePath);
    Task<string?> GetSettingAsync(string key, CancellationToken ct = default);
    Task SetSettingAsync(string key, string? value, CancellationToken ct = default);
    Task<IReadOnlyList<PluginToolInfo>> GetToolsAsync(CancellationToken ct = default);
    Task RunToolAsync(string id, CancellationToken ct = default);
    Task NavigateAsync(PluginDestination destination, CancellationToken ct = default);
    PluginAppearance GetAppearance();
    Task ApplyAppearanceAsync(PluginAppearance appearance, CancellationToken ct = default);
    Task OpenUriAsync(Uri uri, CancellationToken ct = default);
    Task CopyTextAsync(string text, CancellationToken ct = default);
    Task<string?> PickOpenFileAsync(string title, CancellationToken ct = default);
    /// <summary>原生文件夹选择；取消或宿主不支持时返回 null。</summary>
    Task<string?> PickFolderAsync(string title, CancellationToken ct = default) => Task.FromResult<string?>(null);
}
