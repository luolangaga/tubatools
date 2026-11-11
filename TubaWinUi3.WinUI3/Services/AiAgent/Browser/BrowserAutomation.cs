using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>Browser operations expressed purely through DevTools Protocol on the built-in WebView2.</summary>
internal static class BrowserAutomation
{
    internal static async Task<string> GetStatusAsync(BuiltInBrowserRuntime runtime, CancellationToken ct)
    {
        var status = await ReadStatusAsync(runtime, ct);
        var builder = new StringBuilder();
        builder.AppendLine("URL: " + (status.Url.Length > 0 ? status.Url : "(空白页)"));
        builder.AppendLine("标题: " + (status.Title.Length > 0 ? status.Title : "(无标题)"));
        builder.AppendLine("加载状态: " + status.Ready);
        return builder.ToString().TrimEnd();
    }

    internal static async Task<string> NavigateAsync(BuiltInBrowserRuntime runtime, string address, bool observe, CancellationToken ct)
    {
        var url = BrowserCdp.NormalizeUrl(address);
        BrowserPageStatus before;
        try { before = await ReadStatusAsync(runtime, ct); }
        catch (BrowserToolException) { before = new("", "", ""); }
        try { await runtime.SendCdpAsync("Page.enable", null, ct); } catch (BrowserToolException) { }
        var result = await runtime.SendCdpAsync("Page.navigate", new JsonObject { ["url"] = url }, ct);
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("errorText", out var error) &&
            error.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(error.GetString()))
            throw new BrowserToolException("NAVIGATION_FAILED", "打开网页失败：" + error.GetString());

        var status = await WaitForNavigationAsync(runtime, before.Url, TimeSpan.FromSeconds(25), ct);
        var builder = new StringBuilder();
        builder.AppendLine("已打开: " + (status.Url.Length > 0 ? status.Url : url));
        builder.AppendLine("标题: " + (status.Title.Length > 0 ? status.Title : "(无标题)"));
        builder.AppendLine("加载状态: " + status.Ready);
        if (observe)
        {
            await Task.Delay(250, ct);
            builder.AppendLine();
            builder.Append(await SnapshotAsync(runtime, includeText: true, ct));
        }
        else
        {
            builder.AppendLine("需要元素 ref 时调用 browser_snapshot。");
        }
        return builder.ToString().TrimEnd();
    }

    internal static async Task<string> SnapshotAsync(BuiltInBrowserRuntime runtime, bool includeText, CancellationToken ct)
    {
        var data = await ReadSnapshotAsync(runtime, ct);
        return data.Format(includeText);
    }

    internal static async Task<string> ClickAsync(BuiltInBrowserRuntime runtime, string? reference, string? selector, string? text, CancellationToken ct)
    {
        var resolved = await ResolveAsync(runtime, reference, selector, text, ct);
        if (!resolved.ok)
        {
            if (resolved.error == "ambiguous")
                throw new BrowserToolException("ELEMENT_AMBIGUOUS",
                    "匹配到多个元素，请改用 ref：\n" + string.Join("\n", resolved.matches.Select(m => $"{m.Ref} \"{m.Label}\"")));
            throw new BrowserToolException("ELEMENT_NOT_FOUND",
                "没有找到目标元素。请重新调用 browser_snapshot，再用返回的 ref 操作。");
        }

        await MouseAsync(runtime, "mouseMoved", resolved.x, resolved.y, 0, ct);
        await MouseAsync(runtime, "mousePressed", resolved.x, resolved.y, 1, ct);
        await MouseAsync(runtime, "mouseReleased", resolved.x, resolved.y, 0, ct);
        await Task.Delay(250, ct);
        var status = await ReadStatusAsync(runtime, ct);
        return $"已点击 {resolved.reference} <{resolved.tag}> \"{resolved.label}\"\nURL: {status.Url}\n标题: {status.Title}\n页面变化后请重新调用 browser_snapshot 获取新的 ref。";
    }

    internal static async Task<string> TypeAsync(BuiltInBrowserRuntime runtime, string? reference, string? selector,
        string text, bool clear, bool submit, CancellationToken ct)
    {
        var result = await runtime.SendCdpAsync("Runtime.evaluate",
            BrowserCdp.EvaluateParams(BrowserScripts.FocusElement(reference, selector, clear)), ct);
        var value = BrowserCdp.EvaluateValue(result, out _);
        if (!IsOk(value)) throw new BrowserToolException("ELEMENT_NOT_FOUND",
            "没有找到输入框。请重新调用 browser_snapshot，再用返回的 ref 操作。");
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("editable", out var editable) &&
            editable.ValueKind == JsonValueKind.False)
            throw new BrowserToolException("NOT_EDITABLE", "目标元素不可编辑（已禁用或只读）。");

        if (text.Length > 0)
            await runtime.SendCdpAsync("Input.insertText", new JsonObject { ["text"] = text }, ct);
        if (submit)
        {
            await Task.Delay(120, ct);
            await SendKeyAsync(runtime, "Enter", ct);
        }
        var status = await ReadStatusAsync(runtime, ct);
        return (text.Length > 0 ? "已输入文本" : "已聚焦输入框") + (submit ? "并提交" : "") +
               $"\nURL: {status.Url}\n标题: {status.Title}";
    }

    internal static async Task<string> PressAsync(BuiltInBrowserRuntime runtime, string key, CancellationToken ct)
    {
        if (!BrowserCdp.TryParseKey(key, out var press))
            throw new BrowserToolException("INVALID_ARGUMENT", "无法识别的按键：" + key + "。可用 Enter、Tab、Escape、ArrowDown、PageDown、Control+A 等。");
        await SendKeyAsync(runtime, press, ct);
        await Task.Delay(150, ct);
        var status = await ReadStatusAsync(runtime, ct);
        return $"已发送按键 {key}\nURL: {status.Url}\n标题: {status.Title}";
    }

    internal static async Task<string> ScrollAsync(BuiltInBrowserRuntime runtime, string? reference, double dx, double dy, CancellationToken ct)
    {
        var result = await runtime.SendCdpAsync("Runtime.evaluate",
            BrowserCdp.EvaluateParams(BrowserScripts.Scroll(reference, dx, dy)), ct);
        var value = BrowserCdp.EvaluateValue(result, out _);
        if (!IsOk(value)) throw new BrowserToolException("ELEMENT_NOT_FOUND", "没有找到要滚动的元素。");
        var y = value.TryGetProperty("y", out var yElement) && yElement.TryGetDouble(out var number) ? number : 0;
        return $"已滚动，页面纵向位置 y={y}。需要查看新内容时调用 browser_snapshot。";
    }

    internal static async Task<string> SelectAsync(BuiltInBrowserRuntime runtime, string? reference, string? selector,
        string? value, string? label, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(value) && string.IsNullOrEmpty(label))
            throw new BrowserToolException("INVALID_ARGUMENT", "value 和 label 至少填一个。");
        var result = await runtime.SendCdpAsync("Runtime.evaluate",
            BrowserCdp.EvaluateParams(BrowserScripts.SelectOption(reference, selector, value, label)), ct);
        var actual = BrowserCdp.EvaluateValue(result, out _);
        if (IsOk(actual))
            return $"已选择 \"{String(actual, "label")}\"（值：{String(actual, "value")}）";
        if (String(actual, "error") == "option_not_found")
            return "没有找到该选项。可选值：\n" + string.Join("\n", EnumerateOptions(actual));
        throw new BrowserToolException("NOT_SELECT", "目标元素不是下拉框。请用 browser_click 操作普通元素。");
    }

    internal static async Task<string> WaitAsync(BuiltInBrowserRuntime runtime, string? selector, string? text,
        TimeSpan timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(selector) && string.IsNullOrWhiteSpace(text))
        {
            await Task.Delay(timeout, ct);
            return $"已等待 {timeout.TotalSeconds:0.#} 秒。";
        }
        var started = DateTime.UtcNow;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        while (!deadline.IsCancellationRequested)
        {
            try
            {
                var result = await runtime.SendCdpAsync("Runtime.evaluate",
                    BrowserCdp.EvaluateParams(BrowserScripts.WaitFor(selector, text)), deadline.Token);
                var value = BrowserCdp.EvaluateValue(result, out _);
                if (IsOk(value))
                {
                    var status = await ReadStatusAsync(runtime, deadline.Token);
                    return $"等待完成（{Math.Round((DateTime.UtcNow - started).TotalSeconds, 1)} 秒）\nURL: {status.Url}\n标题: {status.Title}";
                }
            }
            catch (OperationCanceledException) { break; }
            catch (BrowserToolException) { }
            try { await Task.Delay(250, deadline.Token); }
            catch (OperationCanceledException) { break; }
        }
        throw new BrowserToolException("WAIT_TIMEOUT", $"等待超时（{timeout.TotalSeconds:0.#} 秒）。请检查页面状态或换一个条件。");
    }

    internal static async Task<string> EvaluateAsync(BuiltInBrowserRuntime runtime, string expression, CancellationToken ct)
    {
        var result = await runtime.SendCdpAsync("Runtime.evaluate", BrowserCdp.EvaluateParams(expression), ct);
        var value = BrowserCdp.EvaluateValue(result, out var type);
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Null => "null",
            JsonValueKind.Undefined => "undefined",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
            _ => value.GetRawText()
        };
        if (text.Length > 20000) text = text[..20000] + "\n…（已截断）";
        return string.IsNullOrEmpty(text) ? $"({type})" : text;
    }

    internal static async Task<string> HistoryAsync(BuiltInBrowserRuntime runtime, string action, CancellationToken ct)
    {
        switch (action.ToLowerInvariant())
        {
            case "back":
                await runtime.SendCdpAsync("Runtime.evaluate", BrowserCdp.EvaluateParams("history.back(); 'ok'"), ct);
                break;
            case "forward":
                await runtime.SendCdpAsync("Runtime.evaluate", BrowserCdp.EvaluateParams("history.forward(); 'ok'"), ct);
                break;
            case "reload":
                await runtime.SendCdpAsync("Page.reload", new JsonObject { ["ignoreCache"] = false }, ct);
                break;
            default:
                throw new BrowserToolException("INVALID_ARGUMENT", "action 只能是 back、forward 或 reload。");
        }
        await Task.Delay(300, ct);
        var status = await WaitForReadyAsync(runtime, TimeSpan.FromSeconds(20), ct);
        return $"{action}\nURL: {status.Url}\n标题: {status.Title}\n加载状态: {status.Ready}";
    }

    internal static async Task<BrowserPageStatus> ReadStatusAsync(BuiltInBrowserRuntime runtime, CancellationToken ct)
    {
        var result = await runtime.SendCdpAsync("Runtime.evaluate", BrowserCdp.EvaluateParams(BrowserScripts.PageStatus), ct);
        var value = BrowserCdp.EvaluateValue(result, out _);
        if (value.ValueKind != JsonValueKind.Object) return new("", "", "");
        return new(String(value, "url"), String(value, "title"), String(value, "ready"));
    }

    private static async Task<BrowserSnapshotData> ReadSnapshotAsync(BuiltInBrowserRuntime runtime, CancellationToken ct)
    {
        var result = await runtime.SendCdpAsync("Runtime.evaluate", BrowserCdp.EvaluateParams(BrowserScripts.Snapshot), ct);
        var value = BrowserCdp.EvaluateValue(result, out _);
        return BrowserSnapshotData.Parse(value);
    }

    private static async Task<BrowserPageStatus> WaitForNavigationAsync(BuiltInBrowserRuntime runtime, string previousUrl,
        TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        var last = new BrowserPageStatus("", "", "");
        while (true)
        {
            try
            {
                last = await ReadStatusAsync(runtime, deadline.Token);
                var changed = !string.Equals(last.Url, previousUrl, StringComparison.OrdinalIgnoreCase) || previousUrl.Length == 0;
                if (changed && last.Ready == "complete") return last;
            }
            catch (OperationCanceledException) { return last; }
            catch (BrowserToolException) { }
            try { await Task.Delay(200, deadline.Token); }
            catch (OperationCanceledException) { return last; }
        }
    }

    private static async Task<BrowserPageStatus> WaitForReadyAsync(BuiltInBrowserRuntime runtime, TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        var last = new BrowserPageStatus("", "", "");
        while (true)
        {
            try
            {
                last = await ReadStatusAsync(runtime, deadline.Token);
                if (last.Ready == "complete") return last;
            }
            catch (OperationCanceledException) { return last; }
            catch (BrowserToolException) { }
            try { await Task.Delay(200, deadline.Token); }
            catch (OperationCanceledException) { return last; }
        }
    }

    private static async Task<ResolvedElement> ResolveAsync(BuiltInBrowserRuntime runtime, string? reference, string? selector,
        string? text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reference) && string.IsNullOrWhiteSpace(selector) && string.IsNullOrWhiteSpace(text))
            throw new BrowserToolException("INVALID_ARGUMENT", "ref、selector、text 至少提供一个。");
        var result = await runtime.SendCdpAsync("Runtime.evaluate",
            BrowserCdp.EvaluateParams(BrowserScripts.ResolveElement(reference, selector, text)), ct);
        var value = BrowserCdp.EvaluateValue(result, out _);
        if (IsOk(value))
            return new ResolvedElement(true, "", String(value, "ref"), String(value, "tag"), String(value, "label"),
                Number(value, "x"), Number(value, "y"), []);
        var matches = new List<(string Ref, string Label)>();
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("matches", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var item in list.EnumerateArray()) matches.Add((String(item, "ref"), String(item, "label")));
        return new ResolvedElement(false, String(value, "error"), "", "", "", 0, 0, matches);
    }

    private static async Task MouseAsync(BuiltInBrowserRuntime runtime, string type, double x, double y, int buttons, CancellationToken ct)
    {
        var parameters = new JsonObject
        {
            ["type"] = type,
            ["x"] = x,
            ["y"] = y,
            ["button"] = "left",
            ["buttons"] = buttons,
            ["clickCount"] = type == "mouseMoved" ? 0 : 1
        };
        await runtime.SendCdpAsync("Input.dispatchMouseEvent", parameters, ct);
    }

    private static Task SendKeyAsync(BuiltInBrowserRuntime runtime, string key, CancellationToken ct)
    {
        if (!BrowserCdp.TryParseKey(key, out var press))
            throw new BrowserToolException("INVALID_ARGUMENT", "无法识别的按键：" + key);
        return SendKeyAsync(runtime, press, ct);
    }

    private static async Task SendKeyAsync(BuiltInBrowserRuntime runtime, BrowserKeyPress press, CancellationToken ct)
    {
        var downType = press.Text is null ? "rawKeyDown" : "keyDown";
        await runtime.SendCdpAsync("Input.dispatchKeyEvent", BrowserCdp.KeyEvent(downType, press), ct);
        await runtime.SendCdpAsync("Input.dispatchKeyEvent", BrowserCdp.KeyEvent("keyUp", press with { Text = null }), ct);
    }

    private static bool IsOk(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;

    private static string String(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static double Number(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.TryGetDouble(out var number) ? number : 0;

    private static IEnumerable<string> EnumerateOptions(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("options", out var options) ||
            options.ValueKind != JsonValueKind.Array) yield break;
        foreach (var option in options.EnumerateArray())
            yield return $"- {String(option, "label")}（值：{String(option, "value")}）";
    }

    private sealed record ResolvedElement(bool ok, string error, string reference, string tag, string label,
        double x, double y, IReadOnlyList<(string Ref, string Label)> matches);
}
