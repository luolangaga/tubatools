using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services.AgentCli;

/// <summary>无窗口、无单实例转发的外部 Agent 协议。WinExe 通过文件返回结果。</summary>
internal static class AgentCliService
{
    private sealed record ToolExecutionResult(string Tool, int ExitCode, bool TimedOut, string Stdout, string Stderr,
        int OutputTruncatedAtChars, bool Success);
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal static readonly IReadOnlyDictionary<string, string[]> Tools = new Dictionary<string, string[]>
    {
        ["cpuz"] = ["cpuz_arm64.exe", "cpuz_x64.exe", "cpuz_x32.exe"],
        ["furmark"] = ["furmark.exe"],
        ["diskspd"] = ["DiskSpdA64.exe", "DiskSpd64.exe", "DiskSpd32.exe"],
        ["crystaldiskinfo"] = ["DiskInfoA64.exe", "DiskInfo64S.exe", "DiskInfo64.exe", "DiskInfo32.exe"],
        ["prime95"] = ["prime95.exe"],
        ["wiztree"] = ["WizTree64.exe", "WizTree.exe"],
        ["aida64"] = ["aida64.exe"],
        ["hwinfo"] = ["HWiNFO64.exe", "HWiNFO32.exe"],
        ["autoruns"] = ["autorunsc64a.exe", "autorunsc64.exe", "autorunsc.exe"],
        ["bluescreenview"] = ["BlueScreenViewx64.exe", "BlueScreenView.exe"],
        ["batteryinfoview"] = ["BatteryInfoView.exe"],
        ["usbdeview"] = ["USBDeview.exe"],
        ["defraggler"] = ["df64.exe", "df.exe"],
        ["diskgenius"] = ["DiskGenius.exe"],
        ["nvidiainspector"] = ["nvidiaInspector.exe"],
        ["nvidiaprofileinspector"] = ["nvidiaProfileInspector.exe"],
        ["everything"] = ["Everything64.exe", "Everything.exe"],
        ["windbg"] = ["windbg.exe"]
    };

    internal static bool IsInvocation(string[] args) => args.Length > 1 && (args[1].Equals(AgentCliRequest.Switch, StringComparison.OrdinalIgnoreCase)
        || args[1].Equals(AgentQueryBroker.WorkerSwitch, StringComparison.OrdinalIgnoreCase));

    internal static async Task<int> ExecuteAsync(string[] args)
    {
        AgentCliRequest? request = null;
        object result;
        int exitCode = 0;
        try
        {
            request = AgentCliRequest.Parse(args.Skip(1).ToArray());
            AppSettings.Load();
            bool brokered = AgentQueryBroker.IsReadOnlyCommand(request.Command) && !IsAdmin();
            object? data = brokered ? await AgentQueryBroker.QueryAsync(request.Command) : request.Command switch
            {
                "help" => new { commands = new[] { "help", "hardware", "sensors", "disks", "cpuz", "tools", "run" },
                    run = "run --output <absolute.json> <tool-id> --allow-execution --timeout <1..300> -- <args>",
                    sensorUnits = "Temp: °C; Power: W; Clock: MHz; Load: %; -1 means unavailable; read-only queries reuse the elevated toolbox or request Windows consent; Agent stays unelevated" },
                "hardware" => await HardwareInfoService.LoadAsync(),
                "sensors" => await ReadSensorsAsync(),
                "disks" => await DiskHealthService.GetHealthAsync(),
                "cpuz" => await QueryLocalAsync("cpuz", true),
                "tools" => Tools.Select(t => new { id = t.Key, path = ResolveTool(t.Key), executionRequires = "--allow-execution; user authorization; bounded timeout" }).ToArray(),
                "run" => await RunToolAsync(request.Arguments),
                _ => throw new ArgumentException("未知命令")
            };
            var ok = data is not ToolExecutionResult { Success: false };
            if (!ok) exitCode = 1;
            result = new { schemaVersion = 1, ok, command = request.Command, capturedAt = DateTimeOffset.UtcNow,
                isAdmin = IsAdmin() || brokered, callerIsAdmin = IsAdmin(), executionMode = brokered ? "elevated-readonly-broker" : "local", data };
        }
        catch (Exception ex)
        {
            exitCode = 1;
            result = new { schemaVersion = 1, ok = false, command = request?.Command, error = new { code = ex.GetType().Name, message = ex.Message } };
        }
        try
        {
            // Even a malformed command should return a machine-readable error to a valid --output.
            var index = Array.IndexOf(args, "--output");
            var output = request?.Output ?? (index >= 0 && index + 1 < args.Length ? args[index + 1] : null);
            if (output is null || !Path.IsPathFullyQualified(output) || output.StartsWith("\\\\")) return 2;
            var temp = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(result, Json), new UTF8Encoding(false));
                File.Move(temp, output, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        catch { return 2; }
        return exitCode;
    }

    private static bool IsAdmin() => AgentQueryBroker.IsAdmin();

    internal static async Task<object> QueryLocalAsync(string command, bool disposeSensors) => command switch
    {
        "hardware" => await HardwareInfoService.LoadAsync(),
        "sensors" => await ReadSensorsAsync(disposeSensors),
        "disks" => await DiskHealthService.GetHealthAsync(),
        "cpuz" => await CpuzInfoService.FetchAsync() ?? throw new InvalidOperationException("CPU-Z 不可用或报告生成失败，请确认已安装 CPU-Z。"),
        _ => throw new ArgumentException("不支持的查询。")
    };

    private static async Task<object> ReadSensorsAsync(bool disposeSensors = true)
    {
        return await Task.Run(async () =>
        {
            try
            {
                LiteMonitorService.Instance.EnsureInit();
                _ = LiteMonitorService.Instance.Read();
                await Task.Delay(1100);
                return (object)LiteMonitorService.Instance.Read();
            }
            finally { if (disposeSensors) LiteMonitorService.Instance.Dispose(); }
        });
    }

    internal static string? ResolveTool(string id)
    {
        if (!Tools.TryGetValue(id, out var names)) throw new ArgumentException("未知 CLI 工具 ID。先调用 tools。");
        if (id == "cpuz") return CpuzInfoService.FindCpuzExe();
        if (id == "furmark") return PerformanceBenchmarkService.FindFurMarkExe();
        if (id == "diskspd") return PerformanceBenchmarkService.FindDiskSpdExe();
        if (!Directory.Exists(ToolCatalog.ToolsRoot)) return null;
        var files = Directory.EnumerateFiles(ToolCatalog.ToolsRoot, "*.exe", SearchOption.AllDirectories).ToArray();
        var architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
        // Native ARM first on ARM; x64 first on x64; avoid unsupported binaries on x86.
        static bool IsArm(string name) => name.Contains("a64", StringComparison.OrdinalIgnoreCase)
            || name.Contains("arm64", StringComparison.OrdinalIgnoreCase) || name.EndsWith("64a.exe", StringComparison.OrdinalIgnoreCase);
        var ranked = names.Where(n => architecture == System.Runtime.InteropServices.Architecture.Arm64 || !IsArm(n))
            .Where(n => architecture != System.Runtime.InteropServices.Architecture.X86 || !n.Contains("64", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => IsArm(n) ? 0 : 1);
        return ranked.Select(n => files.FirstOrDefault(p => Path.GetFileName(p).Equals(n, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(p => p != null);
    }

    private static async Task<object> RunToolAsync(string[] args)
    {
        var (tool, timeout, toolArgs) = AgentCliRequest.ValidateRun(args);
        var exe = ResolveTool(tool) ?? throw new FileNotFoundException("工具未安装，请先在工具箱中下载对应工具。");
        exe = FurMarkLaunchPath.Prepare(exe, Path.GetDirectoryName(exe)).ExePath;
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)!, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in toolArgs) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("启动工具失败。");
        ChildProcessJob.Assign(process);
        // Drain continuously, retaining a bounded prefix so verbose tools cannot exhaust memory.
        static async Task<string> Drain(StreamReader reader)
        {
            var text = new StringBuilder();
            var buffer = new char[4096];
            int read;
            while ((read = await reader.ReadAsync(buffer)) > 0)
                if (text.Length < 65536) text.Append(buffer, 0, Math.Min(read, 65536 - text.Length));
            return text.ToString();
        }
        var stdout = Drain(process.StandardOutput);
        var stderr = Drain(process.StandardError);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        bool timedOut = false;
        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            timedOut = true;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        var output = await stdout.WaitAsync(TimeSpan.FromSeconds(5));
        var error = await stderr.WaitAsync(TimeSpan.FromSeconds(5));
        return new ToolExecutionResult(tool, process.ExitCode, timedOut, output, error, 65536,
            !timedOut && process.ExitCode == 0);
    }
}
