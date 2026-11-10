namespace TubaWinUi3.Services;

/// <summary>
/// 分类显示名映射：物理分类名（中文，永远是数据键）→ 资源键。
/// 仅用于显示层；Tag、CategoryOrder、CategoryGlyph_* 设置键一律保持原始中文名。
/// </summary>
internal static class CategoryDisplayNames
{
    /// <summary>Tools/ 目录下的外部工具分类。</summary>
    private static readonly Dictionary<string, string> ToolCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["处理器工具"] = "Category_Processor",
        ["显卡工具"] = "Category_Graphics",
        ["显示器工具"] = "Category_Display",
        ["硬盘工具"] = "Category_Storage",
        ["内存工具"] = "Category_Memory",
        ["外设工具"] = "Category_Peripherals",
        ["游戏工具"] = "Category_Gaming",
        ["声卡工具"] = "Category_Audio",
        ["网卡工具"] = "Category_Network",
        ["烤鸡工具"] = "Category_StressTest",
        ["综合检测"] = "Category_GeneralCheck",
        ["综合工具"] = "Category_General",
        ["其他工具"] = "Category_Other",
        // 内置工具分类名也可能以物理目录出现（旧版/社区工具目录）；没有映射时会被
        // 回退逻辑去掉「工具」后缀，显示成莫名其妙的「系统」「硬件」等分类。
        ["系统工具"] = "Category_Builtin_System",
        ["硬件工具"] = "Category_Builtin_Hardware",
        ["网络工具"] = "Category_Builtin_Network",
        ["实用工具"] = "Category_Builtin_Utility",
    };

    /// <summary>内置工具分类（IBuiltinTool.Category）。</summary>
    private static readonly Dictionary<string, string> BuiltinCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["系统工具"] = "Category_Builtin_System",
        ["硬件工具"] = "Category_Builtin_Hardware",
        ["网络工具"] = "Category_Builtin_Network",
        ["游戏工具"] = "Category_Builtin_Gaming",
        ["实用工具"] = "Category_Builtin_Utility",
    };

    public static bool TryGetToolCategoryKey(string category, out string key)
        => ToolCategories.TryGetValue(category, out key!);

    public static bool TryGetBuiltinCategoryKey(string category, out string key)
        => BuiltinCategories.TryGetValue(category, out key!);
}
