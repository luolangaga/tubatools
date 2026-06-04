namespace TubaWinUi3.Services;

public static class BuiltinToolRegistry
{
    private static readonly List<IBuiltinTool> _tools = [];

    public static IReadOnlyList<IBuiltinTool> Tools => _tools;

    public static void Register(IBuiltinTool tool)
    {
        if (_tools.Any(t => t.Id == tool.Id))
        {
            throw new InvalidOperationException($"内置工具 '{tool.Id}' 已注册。");
        }
        _tools.Add(tool);
    }

    public static void RegisterDefaults()
    {
        Register(new CertBlockTool());
        Register(new PortViewerTool());
        Register(new HostsEditorTool());
        Register(new KeyboardTestTool());
        Register(new JunkCleanerTool());
        Register(new BsodAnalysisTool());
        Register(new WingetInstallerTool());
        Register(new BatteryReportTool());
        Register(new SpeedTestTool());
        Register(new WifiPasswordTool());
        Register(new DiskSpaceAnalyzerTool());
        Register(new LiteMonitorTool());
        Register(new WindowsActivationTool());
        Register(new DefenderTool());
        Register(new CpuRankingTool());
        Register(new GpuRankingTool());
        Register(new ContextMenuMgrTool());
    }

    public static IReadOnlyList<string> GetCategories()
    {
        return _tools
            .Select(t => t.Category)
            .Distinct()
            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<IBuiltinTool> GetByCategory(string category)
    {
        return _tools
            .Where(t => t.Category == category)
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IBuiltinTool? GetById(string id)
    {
        return _tools.FirstOrDefault(t => t.Id == id);
    }
}