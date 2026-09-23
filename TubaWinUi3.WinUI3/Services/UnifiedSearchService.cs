using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class UnifiedSearchService
{
    // 标题/副标题以资源键为准，中文原文作为缺键回退（渐进迁移期保持原文）。
    private sealed record SettingEntry(string TitleKey, string Title, string SubtitleKey, string Subtitle, string Glyph, string SettingKey);

    private sealed record QuickActionEntry(string TitleKey, string Title, string SubtitleKey, string Subtitle, string Glyph, string Action);

    private static readonly SettingEntry[] SettingsEntries =
    [
        new("Search_SettingTheme", "应用主题", "Search_SettingThemeDesc", "选择浅色、深色或跟随系统", "\uE790", "Theme"),
        new("Search_SettingCompact", "简洁列表模式", "Search_SettingCompactDesc", "原版图吧工具箱的样式", "\uE8FD", "CompactMode"),
        new("Search_SettingBrandLogo", "显示品牌 Logo", "Search_SettingBrandLogoDesc", "在硬件信息页面显示品牌图标", "\uE8F1", "BrandLogo"),
        new("Search_SettingDefaultPage", "默认启动页面", "Search_SettingDefaultPageDesc", "选择应用启动后默认打开的页面", "\uE8A5", "DefaultPage"),
        new("Search_SettingShowFrequent", "显示常用推荐", "Search_SettingShowFrequentDesc", "在常用页面显示按使用频率排序的推荐工具", "\uE734", "ShowFrequentRecommendations"),
        new("Search_SettingFastMode", "快速模式", "Search_SettingFastModeDesc", "禁用所有动画，提升响应速度", "\uEB3F", "FastMode"),
        new("Search_SettingWatermark", "截图水印", "Search_SettingWatermarkDesc", "硬件信息截图时添加水印", "\uE8B9", "Watermark"),
        new("Search_SettingRememberWindow", "记住窗口位置和大小", "Search_SettingRememberWindowDesc", "关闭后下次启动恢复位置", "\uE784", "RememberWindow"),
        new("Search_SettingBackground", "背景图片", "Search_SettingBackgroundDesc", "导入图片作为主页面背景", "\uE91B", "Background"),
        new("Search_SettingUpdate", "检查更新", "Search_SettingUpdateDesc", "检查是否有新版本", "\uE895", "Update"),
        new("Search_SettingConfigManager", "配置管理", "Search_SettingConfigManagerDesc", "管理配置文件的存储位置、导出和导入", "\uE8B7", "ConfigManager"),
        new("Search_SettingCustomToolManager", "自定义工具管理", "Search_SettingCustomToolManagerDesc", "管理工具分类、导入自定义工具", "\uE8B7", "CustomToolManager"),
        new("Search_SettingExportApp", "导出当前软件", "Search_SettingExportAppDesc", "打包成可分发压缩包", "\uE896", "ExportApp"),
    ];

    private static readonly QuickActionEntry[] QuickActions =
    [
        new("Search_QuickHardware", "硬件信息", "Search_QuickHardwareDesc", "查看处理器、显卡、内存等硬件信息", "\uE977", "navigate:hardware"),
        new("Search_QuickFavorites", "常用工具", "Search_QuickFavoritesDesc", "查看收藏的工具", "\uE735", "navigate:favorites"),
        new("Search_QuickBuiltin", "内置工具", "Search_QuickBuiltinDesc", "无需外部文件的系统工具", "\uE90F", "navigate:builtin"),
        new("Search_QuickSettings", "设置", "Search_QuickSettingsDesc", "应用外观和功能设置", "\uE713", "navigate:settings"),
    ];

    public static IReadOnlyList<SearchResult> Search(string query)
    {
        var normalized = query.Trim();
        if (normalized.Length == 0)
            return [];

        var results = new List<SearchResult>();

        SearchExternalTools(normalized, results);
        SearchBuiltinTools(normalized, results);
        if (!RuntimeHelper.IsMsixPackaged)
            SearchCommunityTools(normalized, results);
        SearchSettings(normalized, results);

        DeduplicateResults(results);

        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(20)
            .ToList();
    }

    public static IReadOnlyList<SearchResult> GetQuickPanelItems()
    {
        var items = new List<SearchResult>();

        var recentPaths = LaunchHistoryService.GetHistory();
        foreach (var toolPath in recentPaths.Take(3))
        {
            var tool = FindToolByPath(toolPath);
            if (tool is not null)
            {
                var iconPath = tool.IconPath ?? ToolIconService.GetCachedIconPath(tool.Path);
                items.Add(new SearchResult
                {
                    Title = tool.Name,
                    Subtitle = LocalizationService.GetCategoryDisplayName(tool.Category),
                    Glyph = tool.IconGlyph ?? "\uE8B7",
                    Kind = tool.DatabaseSource?.Equals("custom", StringComparison.OrdinalIgnoreCase) == true
                        ? SearchItemKind.CustomTool
                        : SearchItemKind.ExternalTool,
                    MatchKey = tool.Path,
                    IconPath = iconPath,
                    BuiltinToolId = tool.IsBuiltinLink ? tool.BuiltinToolId : null,
                    Category = tool.Category,
                    Score = 100
                });
            }
        }

        foreach (var qa in QuickActions)
        {
            items.Add(new SearchResult
            {
                Title = LocalizationService.L(qa.TitleKey, qa.Title),
                Subtitle = LocalizationService.L(qa.SubtitleKey, qa.Subtitle),
                Glyph = qa.Glyph,
                Kind = SearchItemKind.QuickAction,
                MatchKey = qa.Action,
                Score = 50
            });
        }

        return items;
    }

    private static void SearchExternalTools(string query, List<SearchResult> results)
    {
        try
        {
            var tools = ToolCatalog.Search(query);
            foreach (var tool in tools)
            {
                var isCustom = tool.DatabaseSource?.Equals("custom", StringComparison.OrdinalIgnoreCase) == true;
                var score = CalcScore(query, tool.Name, tool.Tags);
                var iconPath = tool.IconPath ?? ToolIconService.GetCachedIconPath(tool.Path);
                results.Add(new SearchResult
                {
                    Title = tool.Name,
                    Subtitle = isCustom
                        ? string.Format(LocalizationService.L("Search_CustomSubtitle", "自定义 · {0}"), LocalizationService.GetCategoryDisplayName(tool.Category))
                        : LocalizationService.GetCategoryDisplayName(tool.Category),
                    Glyph = tool.IconGlyph ?? "\uE8B7",
                    Kind = isCustom ? SearchItemKind.CustomTool : SearchItemKind.ExternalTool,
                    MatchKey = tool.Path,
                    IconPath = iconPath,
                    BuiltinToolId = tool.IsBuiltinLink ? tool.BuiltinToolId : null,
                    Category = tool.Category,
                    Score = isCustom ? score + 1 : score
                });
            }
        }
        catch { }
    }

    private static void SearchBuiltinTools(string query, List<SearchResult> results)
    {
        try
        {
            foreach (var tool in BuiltinToolRegistry.Tools)
            {
                var score = CalcScore(query, tool.Name, [tool.Description, tool.Category]);
                if (score > 0)
                {
                    results.Add(new SearchResult
                    {
                        Title = tool.Name,
                        Subtitle = LocalizationService.GetBuiltinCategoryDisplayName(tool.Category),
                        Glyph = tool.Glyph,
                        Kind = SearchItemKind.BuiltinTool,
                        MatchKey = tool.Id,
                        BuiltinToolId = tool.Id,
                        Category = tool.Category,
                        Score = score
                    });
                }
            }
        }
        catch { }
    }

    private static void SearchSettings(string query, List<SearchResult> results)
    {
        foreach (var s in SettingsEntries)
        {
            var title = LocalizationService.L(s.TitleKey, s.Title);
            var subtitle = LocalizationService.L(s.SubtitleKey, s.Subtitle);
            var score = CalcScore(query, title, [subtitle]);
            if (score > 0)
            {
                results.Add(new SearchResult
                {
                    Title = title,
                    Subtitle = subtitle,
                    Glyph = s.Glyph,
                    Kind = SearchItemKind.Setting,
                    MatchKey = s.SettingKey,
                    Score = score
                });
            }
        }
    }

    private static void SearchCommunityTools(string query, List<SearchResult> results)
    {
    }

    private static void DeduplicateResults(List<SearchResult> results)
    {
        var builtinIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in results)
        {
            if (r.Kind == SearchItemKind.ExternalTool)
            {
                var tool = ToolCatalog.GetAllToolsCached()
                    .FirstOrDefault(t => t.Path.Equals(r.MatchKey, StringComparison.OrdinalIgnoreCase));
                if (tool?.IsBuiltinLink == true && !string.IsNullOrWhiteSpace(tool.BuiltinToolId))
                    builtinIds.Add(tool.BuiltinToolId);
            }
        }

        var seen = new HashSet<(SearchItemKind, string)>();
        for (var i = results.Count - 1; i >= 0; i--)
        {
            var r = results[i];
            if (r.Kind == SearchItemKind.BuiltinTool && builtinIds.Contains(r.MatchKey))
            {
                results.RemoveAt(i);
                continue;
            }
            var key = (r.Kind, r.MatchKey);
            if (!seen.Add(key))
                results.RemoveAt(i);
        }
    }

    private static ToolItem? FindToolByPath(string toolPath)
    {
        try
        {
            return ToolCatalog.GetAllToolsCached()
                .FirstOrDefault(t => t.Path.Equals(toolPath, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    internal static double CalcScore(string query, string primary, IReadOnlyList<string>? secondary)
    {
        var score = 0.0;

        if (primary.Equals(query, StringComparison.CurrentCultureIgnoreCase))
            score += 100;
        else if (primary.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
            score += 80;
        else if (primary.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            score += 60;

        if (secondary is not null)
        {
            foreach (var text in secondary)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    score += 20;
            }
        }

        return score;
    }
}
