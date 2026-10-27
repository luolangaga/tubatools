using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>按稳定 ID 保存内置工具启用状态与开始菜单排除项，默认保留所有工具。</summary>
public static class ToolVisibilityService
{
    public const string DisabledBuiltinsKey = "DisabledBuiltinTools";
    public const string SearchExcludedKey = "SearchExcludedTools";
    public const string SearchSelectedKey = "SearchSelectedTools";

    // 旧开关只用于迁移历史配置；新配置始终以明确勾选的稳定键为准。
    public static bool HasSearchSelection => AppSettings.Get(SearchSelectedKey) is string value
        ? Parse(value).Count > 0 : AppSettings.GetBool("WindowsSearchIndex", false);

    internal static HashSet<string> LoadSearchSelection(IEnumerable<string> availableKeys)
    {
        if (AppSettings.Get(SearchSelectedKey) is string value) return Parse(value);
        if (!AppSettings.GetBool("WindowsSearchIndex", false)) return new(StringComparer.OrdinalIgnoreCase);
        var excluded = Parse(AppSettings.Get(SearchExcludedKey));
        return new(availableKeys.Where(key => !excluded.Contains(key)), StringComparer.OrdinalIgnoreCase);
    }

    internal static void SaveSearchSelection(IEnumerable<string> selectedKeys)
    {
        var selected = selectedKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        AppSettings.Set(SearchSelectedKey, JsonSerializer.Serialize(selected));
        AppSettings.Set("WindowsSearchIndex", selected.Length > 0);
    }

    internal static HashSet<string> Parse(string? value)
    {
        try { return new(JsonSerializer.Deserialize<string[]>(value ?? "[]") ?? [], StringComparer.OrdinalIgnoreCase); }
        catch (JsonException) { return new(StringComparer.OrdinalIgnoreCase); }
    }

    public static bool IsBuiltinEnabled(string id) => !Parse(AppSettings.Get(DisabledBuiltinsKey)).Contains(id);
    public static bool IsSearchEnabled(string key) => AppSettings.Get(SearchSelectedKey) is string value
        ? Parse(value).Contains(key)
        : AppSettings.GetBool("WindowsSearchIndex", false) && !Parse(AppSettings.Get(SearchExcludedKey)).Contains(key);
    public static string SearchKey(ToolItem tool) => tool.IsBuiltinLink
        ? "builtin:" + tool.BuiltinToolId
        : "tool:" + (tool.LibraryId ?? Path.GetRelativePath(ToolCatalog.ToolsRoot, tool.Path).Replace('\\', '/'));

    public static void Save(IEnumerable<string> disabledBuiltins, IEnumerable<string> excludedSearch)
    {
        AppSettings.Set(DisabledBuiltinsKey, JsonSerializer.Serialize(disabledBuiltins.Distinct(StringComparer.OrdinalIgnoreCase)));
        AppSettings.Set(SearchExcludedKey, JsonSerializer.Serialize(excludedSearch.Distinct(StringComparer.OrdinalIgnoreCase)));
        ToolCatalog.InvalidateTagsCache();
    }
}
