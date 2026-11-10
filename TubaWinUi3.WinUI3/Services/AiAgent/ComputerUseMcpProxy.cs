using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>
/// stdio MCP proxy in front of the official Microsoft Hybrid Computer Use server.
/// JSON-RPC lines are forwarded untouched; every AX/GUI tool call first publishes a
/// control lease and waits until the host shows the topmost control banner (or the user
/// stops). This keeps the product handshake while the actual desktop control stays in the
/// upstream <c>computer-use.exe mcp</c> implementation.
/// </summary>
internal static class ComputerUseMcpProxy
{
    internal const string Argument = "--computer-use-mcp";
    internal const string ControlRootVariable = "TUBA_COMPUTER_CONTROL_ROOT";
    internal const string SessionVariable = "TUBA_COMPUTER_CONTROL_SESSION";
    internal const string ExecutableVariable = "TUBA_COMPUTER_USE_EXE";
    internal const string TimeoutVariable = "TUBA_COMPUTER_CONTROL_TIMEOUT_MS";
    internal const string TraceVariable = "TUBA_COMPUTER_MCP_TRACE";
    internal const string DefaultSession = "computer-use";

    private const int PollMilliseconds = 80;
    private static readonly object WriteLock = new();
    private static readonly object PendingLock = new();
    private static readonly Dictionary<string, PendingCall> PendingCalls = new(StringComparer.Ordinal);
    private static string _lastWindowHint = "";
    private static volatile bool _shuttingDown;

    private sealed record PendingCall(string LeasePath, bool Capture, string Window, string ControlRoot);

    internal static bool IsInvocation(IReadOnlyList<string> args) =>
        args.Any(a => string.Equals(a, Argument, StringComparison.OrdinalIgnoreCase));

    internal static int Run()
    {
        try { return RunCore(); }
        catch (Exception ex)
        {
            Trace("fatal: " + ex);
            try { Console.Error.WriteLine(ex); } catch (IOException) { }
            return 1;
        }
    }

    private static int RunCore()
    {
        Trace("start");
        try
        {
            Console.InputEncoding = new UTF8Encoding(false);
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        catch (IOException) { }

        var controlRoot = Environment.GetEnvironmentVariable(ControlRootVariable);
        if (string.IsNullOrWhiteSpace(controlRoot)) controlRoot = ComputerControlService.Root;
        var session = Environment.GetEnvironmentVariable(SessionVariable);
        if (string.IsNullOrWhiteSpace(session)) session = DefaultSession;
        var timeout = TimeSpan.FromSeconds(15);
        if (int.TryParse(Environment.GetEnvironmentVariable(TimeoutVariable), out var configured) && configured > 0)
            timeout = TimeSpan.FromMilliseconds(configured);

        var executable = ResolveServer();
        Trace("server=" + (executable ?? "(null)") + " control=" + controlRoot);
        if (executable is null)
        {
            Console.Error.WriteLine("Hybrid Computer Use 组件缺失：请重新安装图吧工具箱。");
            return 1;
        }

        // A fresh MCP connection is the user's explicit restart after a stop.
        TryDelete(Path.Combine(controlRoot, "stop-" + ComputerControlService.SessionKey(session) + ".flag"));

        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        start.ArgumentList.Add("mcp");
        // The provider is a hidden mode of this executable, so it shares the app runtime
        // instead of shipping a separate self-contained .NET process.
        if (Environment.ProcessPath is { Length: > 0 } host)
            start.Environment[ComputerUseProviderHost.ProviderPathVariable] = host;
        start.Environment[ComputerUseProviderHost.InvocationVariable] = "1";
        using var child = Process.Start(start);
        Trace("child=" + (child?.Id.ToString() ?? "(null)"));
        if (child is null) return 1;
        ChildProcessJob.Assign(child);

        new Thread(() => PumpOutput(child)) { IsBackground = true, Name = "ComputerUse stdout" }.Start();
        new Thread(() => PumpError(child)) { IsBackground = true, Name = "ComputerUse stderr" }.Start();

        string? line;
        while ((line = Console.In.ReadLine()) is not null)
        {
            // Some clients emit a UTF-8 BOM at the start of the first line; JSON-RPC rejects it.
            if (line.Length > 0 && line[0] == '\uFEFF') line = line[1..];
            if (line.Length == 0) continue;
            try
            {
                line = NormalizeRequest(line);
                var rejection = Authorize(line, controlRoot, session, timeout, out var forward);
                if (rejection is not null) { WriteLine(rejection); continue; }
                if (!forward) continue;
                child.StandardInput.WriteLine(line);
                child.StandardInput.Flush();
            }
            catch (IOException) { break; }
            catch (ObjectDisposedException) { break; }
        }

        Trace("stdin closed, shutting down");
        _shuttingDown = true;
        try { child.StandardInput.Close(); } catch (IOException) { }
        try
        {
            if (!child.WaitForExit(5000)) child.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return 0;
    }

    private static string? ResolveServer()
    {
        var configured = Environment.GetEnvironmentVariable(ExecutableVariable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        // The app ships the official bundle beside the executable; read-only installs
        // (MSIX/Program Files) can run it in place without staging a 170 MB copy.
        var architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
            System.Runtime.InteropServices.Architecture.Arm64 ? "win-arm64" : "win-x64";
        var bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "HybridComputerUse", architecture, "computer-use.exe");
        if (File.Exists(bundled)) return bundled;
        try { HybridComputerUseService.EnsureInstalledAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException) { }
        return File.Exists(HybridComputerUseService.Executable) ? HybridComputerUseService.Executable : null;
    }

    /// <summary>
    /// Some MCP clients/models serialize array fields as JSON strings (or comma/plus separated
    /// text). The official server validates strictly, so normalize AX/GUI arguments here:
    /// actions/region/keys/path arrays and numeric coordinates must arrive as JSON values.
    /// </summary>
    internal static string NormalizeRequest(string line)
    {
        if (!line.Contains("\"tools/call\"", StringComparison.Ordinal)) return line;
        JsonNode? node;
        try { node = JsonNode.Parse(line); }
        catch (JsonException) { return line; }
        if (node is not JsonObject request || request["params"] is not JsonObject parameters) return line;
        if (parameters["name"] is not JsonValue nameValue || !nameValue.TryGetValue<string>(out var name) ||
            !IsComputerTool(name)) return line;

        if (parameters["arguments"] is JsonValue argumentValue && argumentValue.TryGetValue<string>(out var argumentText))
        {
            try
            {
                if (JsonNode.Parse(argumentText) is JsonObject parsed) parameters["arguments"] = parsed;
            }
            catch (JsonException) { return line; }
        }
        if (parameters["arguments"] is not JsonObject arguments) return line;
        var gui = AgentProtocol.ComputerTool(name) == "GUI";
        return NormalizeArguments(arguments, gui) ? request.ToJsonString() : line;
    }

    private static bool NormalizeArguments(JsonObject arguments, bool gui)
    {
        var changed = false;
        if (arguments["actions"] is JsonValue actionsValue && actionsValue.TryGetValue<string>(out var actionsText))
        {
            var parsed = TryParse(actionsText);
            if (parsed is JsonArray array) { arguments["actions"] = array; changed = true; }
            else if (parsed is JsonObject single) { arguments["actions"] = new JsonArray(single); changed = true; }
        }
        if (arguments["actions"] is JsonArray actions)
        {
            var observationOnly = actions.Count > 0;
            foreach (var action in actions.OfType<JsonObject>())
            {
                changed |= CoerceArray(action, "region", numbers: true);
                changed |= CoerceArray(action, "keys", keys: true);
                changed |= CoerceNumber(action, "x");
                changed |= CoerceNumber(action, "y");
                changed |= CoerceNumber(action, "scroll_x");
                changed |= CoerceNumber(action, "scroll_y");
                changed |= CoerceNumber(action, "timeout_ms");
                changed |= CoerceNumber(action, "ms");
                var type = action["type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out var typeName) ? typeName : "";
                if (type is "screenshot" or "zoom")
                {
                    // settle_ms belongs to input batches, not to a screenshot/zoom action.
                    if (action.Remove("settle_ms")) changed = true;
                }
                else observationOnly = false;
            }
            if (observationOnly && arguments.Remove("settle_ms")) changed = true;
            // An explicit screenshot must be the only action. For an input batch the final
            // screenshot is returned anyway, so drop the redundant screenshot step.
            if (gui && actions.Count > 1 && actions.OfType<JsonObject>().Any(a =>
                    a["type"] is JsonValue v && v.TryGetValue<string>(out var t) && t == "screenshot"))
            {
                for (var i = actions.Count - 1; i >= 0; i--)
                    if (actions[i] is JsonObject action && action["type"] is JsonValue value &&
                        value.TryGetValue<string>(out var type) && type == "screenshot")
                        actions.RemoveAt(i);
                if (actions.Count == 0) actions.Add(new JsonObject { ["type"] = "screenshot" });
                changed = true;
            }
        }
        changed |= CoerceArray(arguments, "region", numbers: true);
        changed |= CoerceArray(arguments, "keys", keys: true);
        changed |= CoerceArray(arguments, "path");
        changed |= CoerceNumber(arguments, "x");
        changed |= CoerceNumber(arguments, "y");
        changed |= CoerceNumber(arguments, "scroll_x");
        changed |= CoerceNumber(arguments, "scroll_y");
        changed |= CoerceNumber(arguments, "settle_ms");
        return changed;
    }

    private static JsonNode? TryParse(string text)
    {
        try { return JsonNode.Parse(text); }
        catch (JsonException) { return null; }
    }

    private static bool CoerceArray(JsonObject arguments, string key, bool numbers = false, bool keys = false)
    {
        if (arguments[key] is not JsonValue value || !value.TryGetValue<string>(out var text)) return false;
        text = text.Trim();
        if (text.Length == 0) return false;
        if (text[0] == '[')
        {
            if (TryParse(text) is JsonArray parsed) { arguments[key] = parsed; return true; }
            return false;
        }
        var separators = keys ? new[] { ',', '+', ' ', ';' } : new[] { ',' };
        var parts = text.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        var array = new JsonArray();
        foreach (var part in parts)
        {
            if (numbers)
            {
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return false;
                array.Add(number);
            }
            else array.Add(part);
        }
        arguments[key] = array;
        return true;
    }

    private static bool CoerceNumber(JsonObject arguments, string key)
    {
        if (arguments[key] is not JsonValue value || !value.TryGetValue<string>(out var text)) return false;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return false;
        arguments[key] = number;
        return true;
    }
    internal static string? Authorize(string line, string controlRoot, string session, TimeSpan timeout, out bool forward)
    {
        forward = true;
        JsonDocument document;
        try { document = JsonDocument.Parse(line); }
        catch (JsonException) { return null; }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var method = AgentProtocol.Text(root, "method");
            var id = root.TryGetProperty("id", out var identifier) ? identifier : default;

            if (method == "notifications/cancelled")
            {
                ReleaseCancelled(root);
                return null;
            }
            if (method != "tools/call") return null;
            if (!root.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object) return null;
            var toolName = AgentProtocol.Text(parameters, "name");
            if (!IsComputerTool(toolName)) return null;

            var tool = AgentProtocol.ComputerTool(toolName);
            var arguments = parameters.TryGetProperty("arguments", out var argumentValue) && argumentValue.ValueKind == JsonValueKind.Object
                ? argumentValue : default;
            var capture = IsCaptureCall(tool, arguments);
            var window = WindowHint(arguments);

            var key = ComputerControlService.SessionKey(session);
            var lease = Path.Combine(controlRoot, "lease-" + key + ".json");
            var shown = Path.Combine(controlRoot, "shown-" + key + ".flag");
            var stop = Path.Combine(controlRoot, "stop-" + key + ".flag");

            if (File.Exists(stop))
            {
                forward = false;
                return ErrorResult(id, "USER_STOPPED_COMPUTER_CONTROL",
                    "用户已停止电脑控制。不要重新获取控制；只有用户明确要求继续后才能再次操作电脑。");
            }

            Directory.CreateDirectory(controlRoot);
            TryDelete(shown);
            WriteLease(lease, key, session, tool, capture, window);
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(stop))
                {
                    TryDelete(lease);
                    forward = false;
                    return ErrorResult(id, "USER_STOPPED_COMPUTER_CONTROL",
                        "用户已停止电脑控制。不要重新获取控制；只有用户明确要求继续后才能再次操作电脑。");
                }
                if (File.Exists(shown))
                {
                    if (id.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
                        lock (PendingLock) PendingCalls[id.GetRawText()] = new(lease, capture, window, controlRoot);
                    return null;
                }
                Thread.Sleep(PollMilliseconds);
            }

            TryDelete(lease);
            forward = false;
            return ErrorResult(id, "USER_CONTROL_BANNER_NOT_SHOWN",
                "图吧工具箱没有显示电脑控制提示（应用未运行或已被关闭）。请打开工具箱后重试；不要绕过控制提示。");
        }
    }

    /// <summary>Tool names arrive as AX, GUI or with a client/server prefix such as computer_use_AX.</summary>
    internal static bool IsComputerTool(string name)
    {
        var tool = name.Trim();
        var index = tool.LastIndexOfAny(['.', '_']);
        if (index >= 0 && index + 1 < tool.Length) tool = tool[(index + 1)..];
        return tool is "AX" or "GUI";
    }

    /// <summary>GUI always returns a final full-desktop screenshot; AX only when it asks for one.</summary>
    private static bool IsCaptureCall(string tool, JsonElement arguments)
    {
        if (tool == "GUI") return true;
        if (tool != "AX" || arguments.ValueKind != JsonValueKind.Object) return false;
        var observe = AgentProtocol.Text(arguments, "observe");
        return observe is "full" or "screenshot";
    }

    /// <summary>
    /// Keeps the last window the agent named so a following GUI screenshot can flash the same
    /// window even though GUI arguments carry no window identity.
    /// </summary>
    private static string WindowHint(JsonElement arguments)
    {
        var hint = ReadWindowHint(arguments);
        if (hint.Length == 0) return _lastWindowHint;
        _lastWindowHint = hint;
        return hint;
    }

    private static string ReadWindowHint(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object) return "";
        foreach (var key in new[] { "title", "window", "app" })
        {
            var value = AgentProtocol.Text(arguments, key);
            if (value.Length > 0) return value;
        }
        if (arguments.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
            foreach (var action in actions.EnumerateArray())
            {
                var title = AgentProtocol.Text(action, "title");
                if (title.Length > 0) return title;
            }
        return "";
    }

    private static void WriteLease(string path, string key, string session, string tool, bool capture, string window)
    {
        var payload = JsonSerializer.Serialize(new
        {
            Key = key,
            SessionId = session,
            ProcessId = Environment.ProcessId,
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            RequestId = Guid.NewGuid().ToString("N"),
            Tool = tool,
            Capture = capture,
            Window = window,
            Foreground = ForegroundTitle()
        });
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, payload, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    /// <summary>The window the user is looking at when the agent starts the call.</summary>
    private static string ForegroundTitle()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return "";
        var title = new StringBuilder(512);
        return GetWindowText(window, title, title.Capacity) > 0 ? title.ToString().Trim() : "";
    }

    private static void ReleaseCancelled(JsonElement root)
    {
        if (!root.TryGetProperty("params", out var parameters) || parameters.ValueKind != JsonValueKind.Object) return;
        if (!parameters.TryGetProperty("requestId", out var requestId)) return;
        PendingCall? pending;
        lock (PendingLock) { if (!PendingCalls.Remove(requestId.GetRawText(), out pending)) return; }
        TryDelete(pending.LeasePath);
    }

    private static void PumpOutput(Process child)
    {
        try
        {
            string? line;
            while ((line = child.StandardOutput.ReadLine()) is not null)
            {
                WriteLine(line);
                ReleaseCompleted(line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
        finally
        {
            if (!_shuttingDown) Environment.Exit(1);
        }
    }

    private static void PumpError(Process child)
    {
        try
        {
            string? line;
            while ((line = child.StandardError.ReadLine()) is not null) Console.Error.WriteLine(line);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    private static void ReleaseCompleted(string line)
    {
        lock (PendingLock) { if (PendingCalls.Count == 0) return; }
        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return;
            if (!document.RootElement.TryGetProperty("id", out var id)) return;
            PendingCall? pending;
            lock (PendingLock) { if (!PendingCalls.Remove(id.GetRawText(), out pending)) return; }
            TryDelete(pending.LeasePath);
            // The capture indicator must appear only after the server has returned the image;
            // creating a topmost window during capture makes HCU reject the layout.
            if (pending.Capture) WriteCaptureSignal(pending);
        }
        catch (JsonException) { }
    }

    private static void WriteCaptureSignal(PendingCall pending)
    {
        try
        {
            Directory.CreateDirectory(pending.ControlRoot);
            var path = Path.Combine(pending.ControlRoot, "capture-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { Window = pending.Window }), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string ErrorResult(JsonElement id, string code, string message)
    {
        var identifier = id.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? "null" : id.GetRawText();
        var text = JsonSerializer.Serialize(code + ": " + message);
        return "{\"jsonrpc\":\"2.0\",\"id\":" + identifier +
               ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":" + text + "}],\"isError\":true}}";
    }

    private static void WriteLine(string line)
    {
        lock (WriteLock)
        {
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void Trace(string message)
    {
        var path = Environment.GetEnvironmentVariable(TraceVariable);
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.AppendAllText(path, DateTime.Now.ToString("O") + " " + message + Environment.NewLine); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
}
