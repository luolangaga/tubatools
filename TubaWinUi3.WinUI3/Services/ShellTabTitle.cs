namespace TubaWinUi3.Services;

/// <summary>
/// 标签页标题的纯逻辑映射：页面类型 + 导航参数 → 标题 / 图标。
///
/// 标签条在标题栏里，标题必须一眼看出这个标签在干什么，所以这里覆盖固定页面、
/// 工具分类与内置工具页；其余页面（几十个工具页）先给一个兜底标题，
/// 随后由 <c>MainWindow</c> 从页面里的 <c>ToolPageHeader</c> 读取真实标题补正 ——
/// 这样新增工具页不必回来改这张表。
/// </summary>
internal readonly record struct ShellTabTitle(
    string Fallback,
    string? ResourceKey,
    string Glyph,
    string? BuiltinToolId = null,
    string? Category = null)
{
    /// <summary>首页标签（新建标签默认打开它）。</summary>
    internal static ShellTabTitle Home { get; } = new("首页", "MainWindow_NavHome.Content", "\uE80F");

    /// <summary>已知页面类型的标题；未收录的类型返回 null，交给调用方兜底。</summary>
    internal static ShellTabTitle? Describe(Type pageType, object? parameter)
    {
        // 首页与「常用」是同一个 DashboardPage，靠导航参数区分
        if (pageType == typeof(Pages.DashboardPage))
            return parameter as string == "favorites"
                ? new("常用", "MainWindow_NavFavorites.Content", "\uE734")
                : Home;
        if (pageType == typeof(Pages.FavoritesPage)) return new("常用", "MainWindow_NavFavorites.Content", "\uE734");
        if (pageType == typeof(Pages.HardwarePage)) return new("硬件信息", "MainWindow_NavHardware.Content", "\uE977");
        if (pageType == typeof(Pages.BuiltinToolsPage)) return new("内置", "MainWindow_NavBuiltin.Content", "\uE90F");
        if (pageType == typeof(Pages.PerformanceBenchmarkPage)) return new("性能测试", "MainWindow_NavBenchmark.Content", "\uE9D9");
        if (pageType == typeof(Pages.PluginsPage)) return new("插件", "MainWindow_NavCommunity.Content", "\uE779");
        if (pageType == typeof(Pages.AiAssistantPage)) return new("AI 助手", "MainWindow_NavAiAssistant.Content", "\uE99A");
        if (pageType == typeof(Pages.SettingsPage)) return new("设置", "MainWindow_NavSettings", "\uE713");

        // 工具分类页（Tools/ 下的目录）：显示名走分类本地化，图标由调用方按分类取彩色图。
        // 无参数时就是首页（外部页面/教程会这样导航）。
        if (pageType == typeof(Pages.HomePage))
            return parameter is string category && !string.IsNullOrWhiteSpace(category)
                ? new(category, null, "\uE8B7", null, category)
                : Home;

        // 代码构建的工具页：标题/图标/工具 id 都在参数里。
        if (parameter is Pages.ToolContentPageParam content && !string.IsNullOrWhiteSpace(content.Title))
            return new(content.Title, null, string.IsNullOrWhiteSpace(content.Glyph) ? "\uE8A5" : content.Glyph, content.ToolId);

        // 插件工具页：id 对应注册表里的插件工具。
        if (pageType == typeof(Pages.PluginToolPage) && parameter is string pluginId &&
            BuiltinToolRegistry.GetById(pluginId) is { } plugin)
            return new(plugin.Name, null, plugin.Glyph, plugin.Id);

        return null;
    }
}
