using System.Text.Json;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>按稳定 ID 保存内置工具启用状态与开始菜单排除项，默认保留所有工具。</summary>
public static class ToolVisibilityService
{
    public const string DisabledBuiltinsKey = "DisabledBuiltinTools";
    public const string SearchExcludedKey = "SearchExcludedTools";

    internal static HashSet<string> Parse(string? value)
    {
        try { return new(JsonSerializer.Deserialize<string[]>(value ?? "[]") ?? [], StringComparer.OrdinalIgnoreCase); }
        catch (JsonException) { return new(StringComparer.OrdinalIgnoreCase); }
    }

    public static bool IsBuiltinEnabled(string id) => !Parse(AppSettings.Get(DisabledBuiltinsKey)).Contains(id);
    public static bool IsSearchEnabled(string key) => !Parse(AppSettings.Get(SearchExcludedKey)).Contains(key);
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
