namespace TubaWinUi3.Services;

public static class BuiltinToolRegistry
{
    private static readonly List<IBuiltinTool> _tools = [];

    public static IReadOnlyList<IBuiltinTool> AllTools { get { lock (_tools) return _tools.ToList(); } }
    public static IReadOnlyList<IBuiltinTool> Tools => AllTools
        .Where(t => ToolVisibilityService.IsBuiltinEnabled(t.Id))
        // 停用/卸载后立即隐藏插件工具；注册表与已加载程序集保留到重启。
        .Where(t => t is not Plugins.PluginXamlTool plugin || Plugins.PluginManager.IsPackageVisible(plugin.PackageId))
        .ToList();

    public static void Register(IBuiltinTool tool)
    {
        if (_tools.Any(t => t.Id == tool.Id))
        {
            throw new InvalidOperationException($"内置工具 '{tool.Id}' 已注册。");
        }
        _tools.Add(tool);
    }

    internal static void RegisterPluginTools(IReadOnlyList<IBuiltinTool> tools)
    {
        lock (_tools)
        {
            var ids = new HashSet<string>(_tools.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var tool in tools)
                if (!ids.Add(tool.Id)) throw new InvalidOperationException($"工具 ID 重复：{tool.Id}");
            _tools.AddRange(tools);
        }
    }

    internal static void ReplacePluginTools(string packageId, IReadOnlyList<IBuiltinTool> tools)
    {
        var prefix = "plugin:" + packageId.ToLowerInvariant() + ":";
        lock (_tools)
        {
            var ids = _tools.Where(t => !t.Id.StartsWith(prefix, StringComparison.Ordinal)).Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var tool in tools)
                if (!tool.Id.StartsWith(prefix, StringComparison.Ordinal) || !ids.Add(tool.Id))
                    throw new InvalidOperationException($"工具 ID 重复或不属于插件：{tool.Id}");
            _tools.RemoveAll(t => t.Id.StartsWith(prefix, StringComparison.Ordinal));
            _tools.AddRange(tools);
        }
    }

    public static void RegisterDefaults()
    {
        Register(new CertBlockTool());
        Register(new RogueCleanerTool());
        Register(new SandboxieTool());
        Register(new PortViewerTool());
        Register(new HostsEditorTool());
        Register(new KeyboardTestTool());
        Register(new SpaceCleanupTool());
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
        Register(new PerformanceBenchmarkTool());
        Register(new BenchmarkCloudTool());
        Register(new LatencyImageQueryTool());
        Register(new WindowsImageTool());
        Register(new PcTutorialTool());
        Register(new AntiMotionSicknessTool());
        Register(new GameMonitorTool());
        Register(new GameMonitorRecordsTool());
        Register(new GameTunnelTool());
        Register(new WebsiteAccelerationTool());
        Register(new PluginsBuiltinTool());
        Register(new ScreenTestTool());
        Register(new DisplayToolsTool());
        Register(new ServiceCenterTool());
        Register(new OfficialWebsitesTool());
        Register(new DotnetCompletionTool());
        Register(new FormatConvertTool());
        Register(new RuntimeRepairTool());
        Register(new DiskHealthTool());
        Register(new LanFileShareTool());
        Register(new PhoneLinkTool());
        Register(new QuickDeviceCheckTool());
        Register(new NetworkOptimizeTool());
        Register(new EnergyStarTool());
        Register(new VolumeShaderTool());
        Register(new StressTestTool());
        Register(new TrafficMonitorTool());
        Register(new WingetStoreTool());
        Register(new WindowsFeatureTool());

        Register(new StartupManagerTool());
        Register(new DigitalLiteracyTestTool());
        Register(new TimeSyncTool());
        Register(new FileLocksmithTool());
        Register(new HandleCleanerTool());
        Register(new EnvironmentVariablesTool());
        Register(new AiPlaygroundTool());
        Register(new AiAssistantTool());
        Register(new MemoryManagerTool());
        Register(new DriverStoreTool());
        Register(new LogViewerTool());
        Register(new WindowManagerTool());
    }

    public static IReadOnlyList<string> GetCategories()
    {
        return Tools
            .Select(t => t.Category)
            .Distinct()
            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<IBuiltinTool> GetByCategory(string category)
    {
        return Tools
            .Where(t => t.Category == category)
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static IBuiltinTool? GetById(string id)
    {
        // Existing desktop shortcuts / history continue to open the merged tool.
        if (id == "junk-cleaner") id = "space-cleanup";
        return Tools.FirstOrDefault(t => t.Id == id);
    }
}
