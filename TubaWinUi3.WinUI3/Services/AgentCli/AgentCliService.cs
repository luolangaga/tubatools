using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services.AgentCli;

/// <summary>无窗口、无单实例转发的外部 Agent 协议。WinExe 通过文件返回结果。</summary>
internal static class AgentCliService
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

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
                "help" => new { commands = new[] { "help", "hardware" }, note = "Other diagnostics and tools are operated through computer use in the toolbox UI." },
                "hardware" => await HardwareInfoService.LoadForDisplayAsync(),
                _ => throw new ArgumentException("未知命令")
            };
            const bool ok = true;
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
        "hardware" => await HardwareInfoService.LoadForDisplayAsync(),
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

}
