using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using TubaWinUi3.Services;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

[Collection("GlobalConfigTests")]
public sealed class ComputerUseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tuba-computer-test-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previous = ConfigManager.DataDirectoryOverride;
    public ComputerUseTests() { ConfigManager.DataDirectoryOverride = _root; Directory.CreateDirectory(_root); }
    public void Dispose() { ConfigManager.DataDirectoryOverride = _previous; Directory.Delete(_root, true); }

    private static string BundledServer()
    {
        var architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        return Path.Combine(AppContext.BaseDirectory, "Assets", "HybridComputerUse", architecture, "computer-use.exe");
    }

    [Fact]
    public async Task AppHost_RunsEmbeddedComputerUseProvider()
    {
        var app = LocateAppHost();
        Assert.True(File.Exists(app), app);
        var start = new ProcessStartInfo(app)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
        };
        start.Environment[ComputerUseProviderHost.InvocationVariable] = "1";
        using var process = Process.Start(start)!;
        try
        {
            await process.StandardInput.WriteLineAsync("""{"request_id":"t1","operation":"bogus"}""");
            await process.StandardInput.FlushAsync();
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.NotNull(line);
            Assert.Contains("\"protocol\":\"computer-use-provider\"", line);
            Assert.Contains("invalid_request", line);
            Assert.False(process.HasExited, "embedded provider exited after one request");
        }
        finally
        {
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    [Fact]
    public void Workspace_RegistersOfficialHybridComputerUseBridge()
    {
        AgentWorkspace.Prepare();
        var skill = Path.Combine(AgentWorkspace.DirectoryPath, ".opencode", "skills", "tuba-computer-use", "SKILL.md");
        Assert.True(File.Exists(skill));
        var text = File.ReadAllText(skill);
        Assert.Contains("AX", text);
        Assert.Contains("GUI", text);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(skill)!, "scripts", "server.ps1")));
        var launch = Path.Combine(AgentWorkspace.DirectoryPath, ".opencode", "skills", "tuba-toolbox", "scripts", "launch-tool.ps1");
        Assert.True(File.Exists(launch), launch);

        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(AgentWorkspace.DirectoryPath, "opencode.json")));
        var server = config.RootElement.GetProperty("mcp").GetProperty("servers").GetProperty("computer_use");
        Assert.Equal("local", server.GetProperty("type").GetString());
        Assert.False(server.GetProperty("codemode").GetBoolean());
        var command = server.GetProperty("command").EnumerateArray().Select(c => c.GetString()!).ToArray();
        Assert.EndsWith("TubaWinUi3.exe", command[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ComputerUseMcpProxy.Argument, command[1]);
        var environment = server.GetProperty("environment");
        Assert.Contains("ComputerControl", environment.GetProperty(ComputerUseMcpProxy.ControlRootVariable).GetString());
        Assert.EndsWith("computer-use.exe", environment.GetProperty(ComputerUseMcpProxy.ExecutableVariable).GetString(), StringComparison.OrdinalIgnoreCase);

        var hostTools = File.ReadAllText(Path.Combine(AgentWorkspace.DirectoryPath, "host-tools.md"));
        Assert.Contains("AX", hostTools);
        Assert.Contains("GUI", hostTools);
    }

    [Fact]
    public async Task OfficialServer_Initializes_WithAxAndGui()
    {
        var executable = BundledServer();
        Assert.True(File.Exists(executable), executable);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)!,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
        };
        start.ArgumentList.Add("mcp");
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        async Task<JsonElement> Request(object request)
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
            await process.StandardInput.FlushAsync();
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60));
            Assert.False(line is null, "MCP exited: " + (process.HasExited ? await errors : "no response"));
            using var json = JsonDocument.Parse(line!);
            return json.RootElement.Clone();
        }
        try
        {
            var init = await Request(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "local-test", version = "1" } } });
            Assert.True(init.TryGetProperty("result", out _));
            var list = await Request(new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } });
            var names = list.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).Order().ToArray();
            Assert.Equal(new[] { "AX", "GUI" }, names);
            // A malformed mode is rejected before any desktop access.
            var invalid = await Request(new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "AX", arguments = new { mode = "bogus" } } });
            Assert.True(invalid.GetProperty("result").GetProperty("isError").GetBoolean());
        }
        finally
        {
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    [Theory]
    [InlineData("AX", true)]
    [InlineData("GUI", true)]
    [InlineData("computer_use_AX", true)]
    [InlineData("computer_use.GUI", true)]
    [InlineData("mcp__computer_use__AX", true)]
    [InlineData("computer_windows", false)]
    [InlineData("AXIS", false)]
    public void ToolNames_AreRecognizedAcrossClientPrefixes(string name, bool expected) =>
        Assert.Equal(expected, ComputerUseMcpProxy.IsComputerTool(name));

    [Theory]
    [InlineData("computer_use_AX", "{\"mode\":\"act\",\"actions\":[{\"type\":\"click\",\"ref\":\"r12\"}]}", "界面操作", "act · click")]
    [InlineData("computer_use_AX", "{\"mode\":\"observe\",\"window\":\"w1\"}", "界面操作", "observe")]
    [InlineData("computer_use.GUI", "{\"actions\":[{\"type\":\"screenshot\"}]}", "屏幕操作", "screenshot")]
    [InlineData("GUI", "{\"actions\":[{\"type\":\"click\",\"x\":10,\"y\":20},{\"type\":\"click\",\"x\":30,\"y\":40},{\"type\":\"type\",\"text\":\"ok\"}]}", "屏幕操作", "click ×2, type")]
    public void DesktopCalls_HaveExplicitTimelineLabels(string name, string input, string label, string target)
    {
        using var parsed = JsonDocument.Parse(input);
        var part = JsonSerializer.SerializeToElement(new { name, state = new { status = "completed", input = parsed.RootElement } });
        var operation = AgentOperations.Parse(part, "step");
        Assert.Equal("computer", operation.Kind);
        Assert.Equal(label, operation.Label);
        Assert.Equal(target, operation.Target);
    }

    [Fact]
    public void Bridge_NormalizesStringifiedGuiArguments()
    {
        var line = """
        {"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"GUI","arguments":{"actions":"[{\"type\":\"click\",\"x\":\"10\",\"y\":\"20\"},{\"type\":\"keypress\",\"keys\":\"CTRL+A\"}]"}}}
        """;
        using var document = JsonDocument.Parse(ComputerUseMcpProxy.NormalizeRequest(line));
        var arguments = document.RootElement.GetProperty("params").GetProperty("arguments");
        var actions = arguments.GetProperty("actions");
        Assert.Equal(JsonValueKind.Array, actions.ValueKind);
        Assert.Equal(2, actions.GetArrayLength());
        Assert.Equal(10, actions[0].GetProperty("x").GetDouble());
        Assert.Equal(20, actions[0].GetProperty("y").GetDouble());
        Assert.Equal(new[] { "CTRL", "A" }, actions[1].GetProperty("keys").EnumerateArray().Select(k => k.GetString()).ToArray());
    }

    [Fact]
    public void Bridge_WrapsSingleActionObjectAndStringifiedArguments()
    {
        var line = """
        {"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"GUI","arguments":"{\"actions\":\"{\\\"type\\\":\\\"screenshot\\\"}\"}"}}
        """;
        using var document = JsonDocument.Parse(ComputerUseMcpProxy.NormalizeRequest(line));
        var actions = document.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("actions");
        Assert.Equal(JsonValueKind.Array, actions.ValueKind);
        Assert.Equal(1, actions.GetArrayLength());
        Assert.Equal("screenshot", actions[0].GetProperty("type").GetString());
    }

    [Fact]
    public void Bridge_NormalizesAxKeysAndRegion()
    {
        var line = """
        {"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"computer_use_AX","arguments":{"mode":"act","actions":"[{\"type\":\"keypress\",\"keys\":\"CTRL+SHIFT+S\"},{\"type\":\"scroll\",\"region\":\"0,0,100,100\"}]"}}}
        """;
        using var document = JsonDocument.Parse(ComputerUseMcpProxy.NormalizeRequest(line));
        var actions = document.RootElement.GetProperty("params").GetProperty("arguments").GetProperty("actions");
        Assert.Equal(new[] { "CTRL", "SHIFT", "S" }, actions[0].GetProperty("keys").EnumerateArray().Select(k => k.GetString()).ToArray());
        Assert.Equal(new double[] { 0, 0, 100, 100 }, actions[1].GetProperty("region").EnumerateArray().Select(v => v.GetDouble()).ToArray());
    }

    [Fact]
    public void Bridge_LeavesNonComputerToolsUntouched()
    {
        var line = """{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"bash","arguments":{"actions":"[1,2]"}}}""";
        Assert.Equal(line, ComputerUseMcpProxy.NormalizeRequest(line));
    }

    private static string LocateAppHost()
    {
        // The test output lacks the Windows App SDK bootstrapper, so run the real app output.
        var overrideHost = Environment.GetEnvironmentVariable("TUBA_TEST_APP_HOST");
        if (!string.IsNullOrWhiteSpace(overrideHost) && File.Exists(overrideHost)) return overrideHost;
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var framework = new DirectoryInfo(AppContext.BaseDirectory).Name;
        var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        var app = Path.Combine(repository, "TubaWinUi3.WinUI3", "bin", configuration, framework, architecture, "TubaWinUi3.exe");
        return File.Exists(app) ? app : Path.Combine(AppContext.BaseDirectory, "TubaWinUi3.exe");
    }

    [Fact]
    public async Task Bridge_NormalizesStringifiedActions_BeforeReachingOfficialServer()
    {
        var app = LocateAppHost();
        var server = BundledServer();
        Assert.True(File.Exists(app), app);
        Assert.True(File.Exists(server), server);
        var control = Path.Combine(_root, "bridge-control");
        Directory.CreateDirectory(control);
        var start = new ProcessStartInfo(app)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
        };
        start.ArgumentList.Add("--computer-use-mcp");
        start.Environment[ComputerUseMcpProxy.ExecutableVariable] = server;
        start.Environment[ComputerUseMcpProxy.ControlRootVariable] = control;
        start.Environment[ComputerUseMcpProxy.SessionVariable] = ComputerUseMcpProxy.DefaultSession;
        start.Environment[ComputerUseMcpProxy.TimeoutVariable] = "10000";
        var trace = Path.Combine(_root, "bridge-trace.log");
        start.Environment[ComputerUseMcpProxy.TraceVariable] = trace;
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        using var hostStop = new CancellationTokenSource();
        var host = Task.Run(async () =>
        {
            while (!hostStop.IsCancellationRequested)
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(control, "lease-*.json"))
                    {
                        var key = Path.GetFileNameWithoutExtension(file)["lease-".Length..];
                        await File.WriteAllTextAsync(Path.Combine(control, "shown-" + key + ".flag"), "{}");
                    }
                }
                catch (IOException) { }
                try { await Task.Delay(80, hostStop.Token); } catch (OperationCanceledException) { break; }
            }
        });
        async Task<JsonElement> Request(object request)
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
            await process.StandardInput.FlushAsync();
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60));
            Assert.False(line is null,
                "MCP exited: code=" + (process.HasExited ? process.ExitCode.ToString() : "?") +
                " stderr=" + (process.HasExited ? await errors : "no response") +
                " trace=" + (File.Exists(trace) ? await File.ReadAllTextAsync(trace) : "(none)"));
            using var json = JsonDocument.Parse(line!);
            return json.RootElement.Clone();
        }
        try
        {
            var init = await Request(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "bridge-test", version = "1" } } });
            Assert.True(init.TryGetProperty("result", out _));
            // The model wrote actions as a JSON string; the bridge must hand the server an array.
            var call = await Request(new
            {
                jsonrpc = "2.0", id = 2, method = "tools/call",
                @params = new { name = "GUI", arguments = new { actions = "[{\"type\":\"bogus\"}]" } }
            });
            var result = call.GetProperty("result");
            Assert.True(result.GetProperty("isError").GetBoolean());
            var text = result.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
            Assert.Contains("actions[0].type", text);
            Assert.Contains("unsupported", text);
            Assert.DoesNotContain("must be an array", text);
            // The capture indicator is published only after the response; a topmost window
            // created during capture would be rejected by the server as a layout change.
            string[] signals = [];
            for (var attempt = 0; attempt < 40 && signals.Length == 0; attempt++)
            {
                signals = Directory.GetFiles(control, "capture-*.json");
                if (signals.Length == 0) await Task.Delay(50);
            }
            Assert.NotEmpty(signals);
        }
        finally
        {
            hostStop.Cancel();
            try { await host; } catch (OperationCanceledException) { }
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    [Fact]
    public async Task ToolLaunchScript_ResolvesInstalledTool_WithoutStartingIt()
    {
        var root = Path.Combine(_root, "tool-launch");
        Directory.CreateDirectory(Path.Combine(root, "Tools", "DemoTool"));
        var host = Path.Combine(root, "TubaWinUi3.exe");
        await File.WriteAllTextAsync(host, "");
        var tool = Path.Combine(root, "Tools", "DemoTool", "DemoTool.exe");
        await File.WriteAllTextAsync(tool, "");
        var script = Path.Combine(AppContext.BaseDirectory, "Assets", "AgentSkills", "tuba-toolbox", "scripts", "launch-tool.ps1");
        Assert.True(File.Exists(script), script);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-Name", "DemoTool", "-ResolveOnly" })
            start.ArgumentList.Add(argument);
        start.Environment["TUBA_TOOLBOX_EXE"] = host;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var output = await stdout;
        Assert.True(process.ExitCode == 0, output + await stderr);
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();
        using var json = JsonDocument.Parse(line);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(tool, json.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public void Bridge_DropsRedundantScreenshotAndCoercesInputNumbers()
    {
        var line = """
        {"jsonrpc":"2.0","id":9,"method":"tools/call","params":{"name":"GUI","arguments":{"actions":[{"type":"screenshot"},{"type":"scroll","x":"100","y":"200","scroll_y":"-3"}],"settle_ms":"800"}}}
        """;
        using var document = JsonDocument.Parse(ComputerUseMcpProxy.NormalizeRequest(line));
        var arguments = document.RootElement.GetProperty("params").GetProperty("arguments");
        var actions = arguments.GetProperty("actions");
        Assert.Equal(1, actions.GetArrayLength());
        Assert.Equal("scroll", actions[0].GetProperty("type").GetString());
        Assert.Equal(100, actions[0].GetProperty("x").GetDouble());
        Assert.Equal(200, actions[0].GetProperty("y").GetDouble());
        Assert.Equal(-3, actions[0].GetProperty("scroll_y").GetDouble());
        Assert.Equal(JsonValueKind.Number, arguments.GetProperty("settle_ms").ValueKind);
        Assert.Equal(800, arguments.GetProperty("settle_ms").GetInt32());
    }

    [Fact]
    public void BridgeGate_ForwardsOnlyAfterHostShowsBanner()
    {
        var control = Path.Combine(_root, "control");
        var session = ComputerUseMcpProxy.DefaultSession;
        var key = ComputerControlService.SessionKey(session);
        var shown = Path.Combine(control, "shown-" + key + ".flag");
        var lease = Path.Combine(control, "lease-" + key + ".json");
        var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 7, method = "tools/call", @params = new { name = "AX", arguments = new { mode = "apps" } } });

        // No host: the call is rejected locally and no lease is left behind.
        var missing = ComputerUseMcpProxy.Authorize(request, control, session, TimeSpan.FromMilliseconds(150), out var forwarded);
        Assert.False(forwarded);
        Assert.Contains("USER_CONTROL_BANNER_NOT_SHOWN", missing);
        Assert.False(File.Exists(lease));

        // The host acknowledges the banner while the call is waiting: it is forwarded once.
        var acknowledge = Task.Run(async () => { await Task.Delay(120); File.WriteAllText(shown, "{}"); });
        var allowed = ComputerUseMcpProxy.Authorize(request, control, session, TimeSpan.FromSeconds(5), out forwarded);
        acknowledge.Wait();
        Assert.True(forwarded);
        Assert.Null(allowed);
        Assert.True(File.Exists(lease));

        // A stop flag denies the call even when a banner flag is present.
        File.WriteAllText(Path.Combine(control, "stop-" + key + ".flag"), "stopped");
        var stopped = ComputerUseMcpProxy.Authorize(request, control, session, TimeSpan.FromSeconds(5), out forwarded);
        Assert.False(forwarded);
        Assert.Contains("USER_STOPPED_COMPUTER_CONTROL", stopped);
        File.Delete(Path.Combine(control, "stop-" + key + ".flag"));
        File.Delete(shown);
        File.Delete(lease);
    }
}
