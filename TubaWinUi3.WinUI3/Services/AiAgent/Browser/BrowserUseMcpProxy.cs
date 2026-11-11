using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TubaWinUi3.Services.AiAgent;

internal sealed record BrowserToolResult(string Text, byte[]? Image = null, string MimeType = "image/png", bool IsError = false);

/// <summary>
/// stdio MCP server for browser control. It is a hidden mode of TubaWinUi3.exe, so it shares
/// the app's runtime without starting XAML. Built-in WebView2 calls are relayed to the app;
/// external Chromium browsers are driven directly over Chrome DevTools Protocol.
/// </summary>
internal static class BrowserUseMcpProxy
{
    internal const string Argument = "--browser-use-mcp";
    private static readonly object WriteLock = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    internal static bool IsInvocation(IReadOnlyList<string> args) =>
        args.Any(a => string.Equals(a, Argument, StringComparison.OrdinalIgnoreCase));

    internal static int Run()
    {
        try
        {
            Console.InputEncoding = new UTF8Encoding(false);
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        catch (IOException) { }
        var session = new BrowserMcpSession();
        try
        {
            RunLoopAsync(session).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine(ex); } catch (IOException) { }
            return 1;
        }
        finally
        {
            try { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { }
        }
    }

    private static async Task RunLoopAsync(BrowserMcpSession session)
    {
        using var lifetime = new CancellationTokenSource();
        var calls = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        var tasks = new List<Task>();
        try
        {
            string? line;
            while ((line = await Console.In.ReadLineAsync()) is not null)
            {
                if (line.Length == 0) continue;
                if (line[0] == '\uFEFF') line = line[1..];
                JsonNode? parsed;
                try { parsed = JsonNode.Parse(line); }
                catch (JsonException)
                {
                    WriteError(null, -32700, "JSON 解析失败。");
                    continue;
                }
                if (parsed is not JsonObject request) continue;
                var method = request["method"]?.GetValue<string>() ?? "";
                var id = request["id"];
                var parameters = request["params"] as JsonObject;

                switch (method)
                {
                    case "initialize":
                        WriteResult(id, new JsonObject
                        {
                            ["protocolVersion"] = parameters?["protocolVersion"]?.GetValue<string>() ?? "2024-11-05",
                            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                            ["serverInfo"] = new JsonObject { ["name"] = "tuba-browser", ["version"] = "1.0.0" },
                            ["instructions"] = "浏览器控制。使用工具箱内置 WebView2：它的用户数据在工具箱专属目录，不是用户日常 Chrome/Edge 的配置，通常没有用户已登录的账号；需要登录时让用户在弹出的浏览器窗口里完成。每次工具结果首行都会标注内置 WebView2 与专属用户数据目录。优先 browser_snapshot 获取 ref，再用 ref 点击/输入；不要把 GUI 坐标点击浏览器窗口当作首选方案。"
                        });
                        break;

                    case "notifications/initialized":
                        break;

                    case "notifications/cancelled":
                    {
                        var requestId = parameters?["requestId"];
                        var key = requestId?.ToJsonString() ?? "";
                        lock (calls)
                        {
                            if (calls.TryGetValue(key, out var source))
                            {
                                try { source.Cancel(); }
                                catch (ObjectDisposedException) { }
                            }
                        }
                        break;
                    }

                    case "ping":
                        WriteResult(id, new JsonObject());
                        break;

                    case "tools/list":
                        WriteResult(id, new JsonObject { ["tools"] = Definitions() });
                        break;

                    case "tools/call":
                    {
                        if (parameters is null) { WriteError(id, -32602, "缺少参数。"); break; }
                        var name = parameters["name"]?.GetValue<string>() ?? "";
                        var arguments = parameters["arguments"] as JsonObject ?? new JsonObject();
                        if (parameters["arguments"] is JsonValue argumentText && argumentText.TryGetValue<string>(out var rawArguments))
                        {
                            try { if (JsonNode.Parse(rawArguments) is JsonObject parsedArguments) arguments = parsedArguments; }
                            catch (JsonException) { }
                        }
                        var key = id?.ToJsonString() ?? Guid.NewGuid().ToString("N");
                        var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        source.CancelAfter(TimeSpan.FromMinutes(3));
                        lock (calls) calls[key] = source;
                        var task = Task.Run(async () =>
                        {
                            BrowserToolResult result;
                            try
                            {
                                result = await BrowserMcpTools.CallAsync(session, name, arguments, source.Token);
                            }
                            catch (BrowserToolException ex)
                            {
                                result = new($"{ex.Code}: {ex.Message}", IsError: true);
                            }
                            catch (OperationCanceledException)
                            {
                                result = new("CANCELLED: 浏览器操作已取消。", IsError: true);
                            }
                            catch (Exception ex)
                            {
                                result = new("BROWSER_ERROR: " + ex.Message, IsError: true);
                            }
                            finally
                            {
                                lock (calls) calls.Remove(key);
                                source.Dispose();
                            }
                            if (!lifetime.IsCancellationRequested) WriteToolResult(id, result);
                        }, CancellationToken.None);
                        tasks.Add(task);
                        break;
                    }

                    default:
                        if (id is not null) WriteError(id, -32601, "不支持的方法：" + method);
                        break;
                }
                tasks.RemoveAll(t => t.IsCompleted);
            }
        }
        finally
        {
            lifetime.Cancel();
            try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { }
        }
    }

    private static void WriteToolResult(JsonNode? id, BrowserToolResult result)
    {
        var content = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = result.Text } };
        if (result.Image is { Length: > 0 })
            content.Add(new JsonObject
            {
                ["type"] = "image",
                ["data"] = Convert.ToBase64String(result.Image),
                ["mimeType"] = result.MimeType
            });
        WriteResult(id, new JsonObject { ["content"] = content, ["isError"] = result.IsError });
    }

    internal static JsonArray Definitions() =>
    [
        Tool(BrowserToolNames.Status,
            "查看 AI 浏览器状态：是否已启动、页面 URL/标题、用户是否已停止控制。引擎固定为内置 WebView2（工具箱专属用户数据，不含用户日常浏览器登录状态）。",
            new JsonObject()),
        Tool(BrowserToolNames.Navigate,
            "在 AI 浏览器中打开网址，自动补全 https。打开后默认返回页面文本和可交互元素 ref。",
            new JsonObject
            {
                ["url"] = Text("完整网址或域名，例如 https://example.com 或 example.com。"),
                ["observe"] = Bool("是否在打开后返回页面快照，默认 true。")
            }, "url"),
        Tool(BrowserToolNames.Snapshot,
            "读取当前页面的可见文本和可交互元素（按钮、链接、输入框等），分配 e1、e2… ref。页面变化后必须重新调用；ref 只对本次页面状态有效。",
            new JsonObject { ["include_text"] = Bool("是否包含页面文本，默认 true。") }),
        Tool(BrowserToolNames.Click,
            "点击页面元素。优先使用 snapshot 返回的 ref，也可用 CSS selector 或可见文字定位。ref/selector/text 至少提供一个。",
            new JsonObject
            {
                ["ref"] = Text("snapshot 返回的元素 ref，例如 e3。"),
                ["selector"] = Text("CSS 选择器，例如 button.submit。"),
                ["text"] = Text("按可见文字查找元素；匹配多个时会返回候选 ref。")
            }),
        Tool(BrowserToolNames.Type,
            "向输入框输入文本。用 ref/selector 定位；clear 先清空，submit 输入后按 Enter 提交。",
            new JsonObject
            {
                ["ref"] = Text("输入框的 ref。"),
                ["selector"] = Text("输入框的 CSS 选择器。"),
                ["text"] = Text("要输入的文本；留空只聚焦或清空。"),
                ["clear"] = Bool("输入前清空原内容，默认 false。"),
                ["submit"] = Bool("输入后按 Enter，默认 false。")
            }),
        Tool(BrowserToolNames.Press,
            "发送键盘按键，例如 Enter、Tab、Escape、ArrowDown、PageDown、Control+A。",
            new JsonObject { ["key"] = Text("按键，支持组合键如 Control+A。") }, "key"),
        Tool(BrowserToolNames.Scroll,
            "滚动页面或指定元素。dy 为正数表示向下滚动。",
            new JsonObject
            {
                ["dy"] = Number("纵向滚动像素，默认 600。"),
                ["dx"] = Number("横向滚动像素，默认 0。"),
                ["ref"] = Text("可选：滚动这个元素而不是整个页面。")
            }),
        Tool(BrowserToolNames.Select,
            "选择原生 <select> 下拉框的选项，按 value 或可见文字 label。",
            new JsonObject
            {
                ["ref"] = Text("下拉框的 ref。"),
                ["selector"] = Text("下拉框的 CSS 选择器。"),
                ["value"] = Text("选项的 value。"),
                ["label"] = Text("选项的可见文字。")
            }),
        Tool(BrowserToolNames.Screenshot,
            "截取当前网页视口截图（PNG），用于视觉确认。截图只包含网页内容，不包括工具箱窗口。",
            new JsonObject()),
        Tool(BrowserToolNames.Eval,
            "在页面中执行 JavaScript 并返回结果。只在语义操作（snapshot/click/type）无法完成时使用；不要用它绕过用户停止或读取本机任意文件。",
            new JsonObject { ["expression"] = Text("要执行的 JavaScript 表达式。") }, "expression"),
        Tool(BrowserToolNames.Wait,
            "等待页面出现指定 CSS selector 或可见文字，最多 60 秒。",
            new JsonObject
            {
                ["selector"] = Text("等待出现的 CSS 选择器。"),
                ["text"] = Text("等待出现的页面文字。"),
                ["timeout_ms"] = Number("超时毫秒数，默认 10000，最大 60000。")
            }),
        Tool(BrowserToolNames.History,
            "页面后退、前进或刷新。",
            new JsonObject
            {
                ["action"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "back", "forward", "reload" },
                    ["description"] = "back、forward 或 reload。"
                }
            }, "action")
    ];

    private static JsonObject Tool(string name, string description, JsonObject properties, params string[] required)
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false };
        if (required.Length > 0)
        {
            var list = new JsonArray();
            foreach (var item in required) list.Add(item);
            schema["required"] = list;
        }
        return new JsonObject { ["name"] = name, ["description"] = description, ["inputSchema"] = schema };
    }

    private static JsonObject Text(string description) => new() { ["type"] = "string", ["description"] = description };
    private static JsonObject Bool(string description) => new() { ["type"] = "boolean", ["description"] = description };
    private static JsonObject Number(string description) => new() { ["type"] = "number", ["description"] = description };

    private static void WriteResult(JsonNode? id, JsonObject result)
    {
        var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone() ?? JsonValue.Create((string?)null), ["result"] = result };
        Write(response);
    }

    private static void WriteError(JsonNode? id, int code, string message)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone() ?? JsonValue.Create((string?)null),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        };
        Write(response);
    }

    private static void Write(JsonObject message)
    {
        var line = message.ToJsonString(Json);
        lock (WriteLock)
        {
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
    }
}

internal sealed class BrowserMcpSession : IAsyncDisposable
{
    private BuiltInBrowserRuntime? _runtime;

    internal string Header => AiBrowserInfo.ResultHeader;

    internal async Task<BuiltInBrowserRuntime> EnsureRuntimeAsync(CancellationToken ct)
    {
        _runtime ??= new BuiltInBrowserRuntime();
        await _runtime.EnsureAsync(ct);
        return _runtime;
    }

    public async ValueTask DisposeAsync()
    {
        if (_runtime is null) return;
        try { await _runtime.DisposeAsync(); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException) { }
        _runtime = null;
    }
}

internal static class BrowserMcpTools
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal static async Task<BrowserToolResult> CallAsync(BrowserMcpSession session, string name, JsonObject arguments, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try { return await CallCoreAsync(session, name, arguments, ct); }
        finally { Gate.Release(); }
    }

    private static async Task<BrowserToolResult> CallCoreAsync(BrowserMcpSession session, string name, JsonObject arguments, CancellationToken ct)
    {
        var tool = BrowserToolNames.Normalize(name);
        if (tool.Length == 0)
            return new($"UNKNOWN_TOOL: 未知的浏览器工具：{name}", IsError: true);

        var header = session.Header;
        if (tool != BrowserToolNames.Status && AiBrowserControl.IsStopped)
            return new(header + "\nUSER_STOPPED_BROWSER_CONTROL: 用户已停止浏览器控制。不要重新获取控制；只有用户明确要求继续后才能再次操作浏览器。", IsError: true);

        try
        {
            var runtime = await session.EnsureRuntimeAsync(ct);
            switch (tool)
            {
                case BrowserToolNames.Status:
                {
                    var status = await BrowserAutomation.GetStatusAsync(runtime, ct);
                    var control = AiBrowserControl.IsStopped ? "用户已停止（只有用户明确要求继续后才能操作）" : "允许";
                    return new(header + "\n" + status + "\n控制状态：" + control);
                }
                case BrowserToolNames.Navigate:
                {
                    var url = Str(arguments, "url");
                    if (url.Length == 0) throw new BrowserToolException("INVALID_ARGUMENT", "url 不能为空。");
                    var text = await BrowserAutomation.NavigateAsync(runtime, url, Bool(arguments, "observe", true), ct);
                    return new(header + "\n" + text);
                }
                case BrowserToolNames.Snapshot:
                {
                    var text = await BrowserAutomation.SnapshotAsync(runtime, Bool(arguments, "include_text", true), ct);
                    return new(header + "\n" + text);
                }
                case BrowserToolNames.Click:
                {
                    var text = await BrowserAutomation.ClickAsync(runtime, Str(arguments, "ref"), Str(arguments, "selector"), Str(arguments, "text"), ct);
                    return new(header + "\n" + text);
                }
                case BrowserToolNames.Type:
                {
                    var text = await BrowserAutomation.TypeAsync(runtime, Str(arguments, "ref"), Str(arguments, "selector"),
                        Str(arguments, "text"), Bool(arguments, "clear"), Bool(arguments, "submit"), ct);
                    return new(header + "\n" + text);
                }
                case BrowserToolNames.Press:
                    return new(header + "\n" + await BrowserAutomation.PressAsync(runtime, Str(arguments, "key"), ct));
                case BrowserToolNames.Scroll:
                {
                    var dy = Num(arguments, "dy", 600);
                    var dx = Num(arguments, "dx", 0);
                    return new(header + "\n" + await BrowserAutomation.ScrollAsync(runtime, Str(arguments, "ref"), dx, dy, ct));
                }
                case BrowserToolNames.Select:
                    return new(header + "\n" + await BrowserAutomation.SelectAsync(runtime, Str(arguments, "ref"),
                        Str(arguments, "selector"), Str(arguments, "value"), Str(arguments, "label"), ct));
                case BrowserToolNames.Screenshot:
                {
                    var image = await runtime.CaptureAsync(ct);
                    return new(header + "\n已截取当前网页视口截图（仅网页内容）。", image);
                }
                case BrowserToolNames.Eval:
                {
                    var expression = Str(arguments, "expression");
                    if (expression.Length == 0) throw new BrowserToolException("INVALID_ARGUMENT", "expression 不能为空。");
                    return new(header + "\n" + await BrowserAutomation.EvaluateAsync(runtime, expression, ct));
                }
                case BrowserToolNames.Wait:
                {
                    var timeout = Math.Clamp(Num(arguments, "timeout_ms", 10000), 100, 60000);
                    return new(header + "\n" + await BrowserAutomation.WaitAsync(runtime, Str(arguments, "selector"),
                        Str(arguments, "text"), TimeSpan.FromMilliseconds(timeout), ct));
                }
                case BrowserToolNames.History:
                    return new(header + "\n" + await BrowserAutomation.HistoryAsync(runtime, Str(arguments, "action"), ct));
                default:
                    return new($"UNKNOWN_TOOL: 未知的浏览器工具：{name}", IsError: true);
            }
        }
        catch (BrowserToolException ex)
        {
            return new(header + "\n" + ex.Code + ": " + ex.Message, IsError: true);
        }
        catch (OperationCanceledException)
        {
            return new("CANCELLED: 浏览器操作已取消。", IsError: true);
        }
        catch (Exception ex)
        {
            return new(header + "\nBROWSER_ERROR: " + ex.Message, IsError: true);
        }
    }

    private static string Str(JsonObject arguments, string key)
    {
        if (!arguments.TryGetPropertyValue(key, out var node) || node is null) return "";
        try { return node.GetValue<string>(); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException) { return node.ToJsonString(); }
    }

    private static bool Bool(JsonObject arguments, string key, bool fallback = false)
    {
        if (!arguments.TryGetPropertyValue(key, out var node) || node is null) return fallback;
        try { return node.GetValue<bool>(); }
        catch (InvalidOperationException)
        {
            return bool.TryParse(Str(arguments, key), out var value) ? value : fallback;
        }
    }

    private static double Num(JsonObject arguments, string key, double fallback = 0)
    {
        if (!arguments.TryGetPropertyValue(key, out var node) || node is null) return fallback;
        try { return node.GetValue<double>(); }
        catch (InvalidOperationException)
        {
            return double.TryParse(Str(arguments, key), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
        }
    }
}
