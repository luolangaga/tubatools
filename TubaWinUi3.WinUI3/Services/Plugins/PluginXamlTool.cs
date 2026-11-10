using System.Xml;
using System.Xml.Linq;
using Microsoft.UI.Xaml;
using TubaWinUi3.Controls;
using TubaWinUi3.Pages;
using TubaWinUi3.PluginSdk;

namespace TubaWinUi3.Services.Plugins;

internal sealed class PluginXamlTool(string packageId, PluginToolDefinition definition, string? iconPath, string? xaml = null, string packageDirectory = "", bool fullTrust = false) : IBuiltinTool
{
    internal string PackageId => packageId;
    internal PluginToolDefinition Definition => definition;
    internal string? IconPath => iconPath;
    internal string? Xaml => xaml ?? definition.Xaml;
    internal string PackageDirectory => packageDirectory;
    internal bool FullTrust => fullTrust;
    public string Id => $"plugin:{packageId.ToLowerInvariant()}:{definition.Id.ToLowerInvariant()}";
    public string Name => definition.Name;
    public string Category => definition.Category;
    public string Description => definition.Description;
    public string Glyph => definition.Glyph;
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;
    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        if (definition.ExecuteAsync is { } action)
        {
            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread() ?? throw new InvalidOperationException("请从界面线程启动插件工具。");
            await action(PluginXamlView.CreateExecution(this, dispatcher, () => context.XamlRoot, context.OnProgress ?? (_ => { }), context.CancellationToken));
            return;
        }
        var window = App.MainWindow ?? throw new InvalidOperationException("主窗口不可用。");
        window.NavigateToToolPage(typeof(PluginToolPage), Id);
    }
}

internal static class PluginToolRules
{
    internal static void Validate(PluginToolDefinition tool, string? loadedXaml = null)
    {
        ArgumentNullException.ThrowIfNull(tool);
        PluginPackageRules.Segment(tool.Id);
        if (string.IsNullOrWhiteSpace(tool.Name) || string.IsNullOrWhiteSpace(tool.Category) ||
            tool.Category.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || tool.Category is "." or "..") throw new ArgumentException("工具名称和分类无效。");
        var modes = (tool.Xaml is not null ? 1 : 0) + (tool.XamlPath is not null ? 1 : 0) + (tool.CreateContent is not null ? 1 : 0) + (tool.ExecuteAsync is not null ? 1 : 0);
        if (modes != 1) throw new ArgumentException("每个工具必须且只能提供 Xaml、XamlPath、CreateContent 或 ExecuteAsync 之一。");
        if ((loadedXaml ?? tool.Xaml) is { } xaml)
        {
            using var reader = XmlReader.Create(new StringReader(xaml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader);
            if (document.Root is null || document.Root.Name.NamespaceName != "http://schemas.microsoft.com/winfx/2006/xaml/presentation")
                throw new InvalidDataException("自定义 XAML 需要一个 WinUI 根元素与默认 xmlns。");
            if (document.Descendants().Attributes().Any(a => a.Name == XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml") || a.Value.Contains("{x:Bind", StringComparison.Ordinal)))
                throw new InvalidDataException("运行时 XAML 不支持 x:Class 或 x:Bind，请在 InitializeAsync 中使用 BindClick/BindValueChanged 绑定事件。");
        }
    }
}
