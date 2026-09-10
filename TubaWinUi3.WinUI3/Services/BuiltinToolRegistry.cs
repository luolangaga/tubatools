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
        Register(new RogueCleanerTool());
        Register(new SandboxieTool());
        Register(new PortViewerTool());
        Register(new HostsEditorTool());
        Register(new KeyboardTestTool());
        Register(new JunkCleanerTool());
        Register(new BatteryAnalyzerTool());
        Register(new SpeedTestTool());
        Register(new WifiPasswordTool());

        Register(new CpuRankingTool());
        Register(new GpuRankingTool());
        Register(new ContextMenuMgrTool());
        Register(new HardwareSpooferTool());
        Register(new NetworkAdapterProxyTool());
        Register(new UniGetUITool());
        Register(new OptimizerDuckTool());
        Register(new AiAssistantTool());
        Register(new PerformanceBenchmarkTool());
        Register(new BenchmarkCloudTool());
        Register(new LatencyImageQueryTool());
        Register(new WindowsImageTool());
        Register(new PcTutorialTool());
        Register(new AntiMotionSicknessTool());
        Register(new GameMonitorTool());
        Register(new GameMonitorRecordsTool());
        if (!RuntimeHelper.IsMsixPackaged)
            Register(new CommunityToolBuiltinTool());
        Register(new ScreenTestTool());
        Register(new ServiceCenterTool());
        Register(new OfficialWebsitesTool());
        Register(new DotnetCompletionTool());
        Register(new FormatConvertTool());
        Register(new RuntimeRepairTool());
        Register(new DiskHealthTool());
        Register(new LanFileShareTool());
        Register(new QuickDeviceCheckTool());
        Register(new NetworkOptimizeTool());
        Register(new EnergyStarTool());
        Register(new RatingSystemTool());
        Register(new VolumeShaderTool());
        Register(new StressTestTool());
        Register(new TrafficMonitorTool());
        Register(new WingetStoreTool());
        Register(new WindowsFeatureTool());

        Register(new StartupManagerTool());
        Register(new DigitalLiteracyTestTool());
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