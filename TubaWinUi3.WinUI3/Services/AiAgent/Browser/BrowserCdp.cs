using System.Text.Json;
using System.Text.Json.Nodes;

namespace TubaWinUi3.Services.AiAgent;

internal sealed class BrowserToolException : Exception
{
    internal string Code { get; }
    internal BrowserToolException(string code, string message) : base(message) => Code = code;
}

internal sealed record BrowserKeyPress(string Key, string Code, int VirtualKey, string? Text, int Modifiers);

internal static class BrowserCdp
{
    // Bitmask used by Input.dispatchKeyEvent: Alt=1, Ctrl=2, Meta=4, Shift=8.
    internal const int AltModifier = 1;
    internal const int CtrlModifier = 2;
    internal const int MetaModifier = 4;
    internal const int ShiftModifier = 8;

    internal static string Json(string value) => JsonSerializer.Serialize(value);

    internal static string NormalizeUrl(string input)
    {
        var text = (input ?? "").Trim().Trim('"');
        if (text.Length == 0) throw new BrowserToolException("INVALID_ARGUMENT", "url 不能为空。");
        if (text.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            throw new BrowserToolException("INVALID_ARGUMENT", "不支持 javascript: 网址；需要执行脚本请用 browser_eval。");
        if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            throw new BrowserToolException("INVALID_ARGUMENT", "AI 浏览器不打开本机 file:// 地址。");
        if (text.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out _)) throw new BrowserToolException("INVALID_ARGUMENT", "网址格式无效：" + text);
            return text;
        }
        if (text.StartsWith("//", StringComparison.Ordinal)) text = "https:" + text;
        else if (!text.Contains("://", StringComparison.Ordinal))
        {
            var lower = text.ToLowerInvariant();
            var local = lower.StartsWith("localhost") || lower.StartsWith("127.0.0.1") ||
                        lower.StartsWith("[::1]") || lower.StartsWith("0.0.0.0");
            text = (local ? "http://" : "https://") + text;
        }
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new BrowserToolException("INVALID_ARGUMENT", "网址格式无效：" + input);
        return text;
    }

    internal static JsonObject EvaluateParams(string expression) => new()
    {
        ["expression"] = expression,
        ["returnByValue"] = true,
        ["awaitPromise"] = true,
        ["userGesture"] = true
    };

    internal static JsonElement EvaluateValue(JsonElement result, out string type)
    {
        type = "";
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("exceptionDetails", out var exception))
        {
            var text = exception.TryGetProperty("text", out var t) ? t.GetString() : "";
            var description = exception.TryGetProperty("exception", out var inner) && inner.ValueKind == JsonValueKind.Object &&
                              inner.TryGetProperty("description", out var d) ? d.GetString() : "";
            throw new BrowserToolException("SCRIPT_ERROR", string.IsNullOrWhiteSpace(description) ? text ?? "页面脚本执行失败。" : description!);
        }
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("result", out var value))
            throw new BrowserToolException("SCRIPT_ERROR", "页面没有返回结果。");
        if (value.TryGetProperty("type", out var typeElement)) type = typeElement.GetString() ?? "";
        return value.TryGetProperty("value", out var actual) ? actual : default;
    }

    internal static bool TryParseKey(string input, out BrowserKeyPress press)
    {
        press = new("", "", 0, null, 0);
        var text = (input ?? "").Trim();
        if (text.Length == 0) return false;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        var modifiers = 0;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= CtrlModifier; break;
                case "alt": modifiers |= AltModifier; break;
                case "shift": modifiers |= ShiftModifier; break;
                case "win" or "meta" or "cmd" or "command": modifiers |= MetaModifier; break;
                default: return false;
            }
        }
        var key = parts[^1];
        var special = SpecialKey(key);
        if (special is not null)
        {
            press = special with { Modifiers = modifiers };
            return true;
        }
        if (key.Length == 1)
        {
            var ch = key[0];
            if (char.IsAsciiLetter(ch))
            {
                var upper = char.ToUpperInvariant(ch);
                var vk = 0x41 + (upper - 'A');
                press = new(upper.ToString(), "Key" + upper, vk,
                    modifiers is AltModifier or CtrlModifier or MetaModifier ? null : (modifiers & ShiftModifier) != 0 ? upper.ToString() : char.ToLowerInvariant(ch).ToString(),
                    modifiers);
                return true;
            }
            if (char.IsAsciiDigit(ch))
            {
                press = new(ch.ToString(), "Digit" + ch, 0x30 + (ch - '0'), ch.ToString(), modifiers);
                return true;
            }
            press = new(ch.ToString(), "", 0, ch.ToString(), modifiers);
            return true;
        }
        return false;
    }

    private static BrowserKeyPress? SpecialKey(string key) => key.ToLowerInvariant() switch
    {
        "enter" or "return" => new("Enter", "Enter", 13, "\r", 0),
        "tab" => new("Tab", "Tab", 9, null, 0),
        "escape" or "esc" => new("Escape", "Escape", 27, null, 0),
        "backspace" => new("Backspace", "Backspace", 8, null, 0),
        "delete" or "del" => new("Delete", "Delete", 46, null, 0),
        "space" => new(" ", "Space", 32, " ", 0),
        "arrowup" or "up" => new("ArrowUp", "ArrowUp", 38, null, 0),
        "arrowdown" or "down" => new("ArrowDown", "ArrowDown", 40, null, 0),
        "arrowleft" or "left" => new("ArrowLeft", "ArrowLeft", 37, null, 0),
        "arrowright" or "right" => new("ArrowRight", "ArrowRight", 39, null, 0),
        "pageup" => new("PageUp", "PageUp", 33, null, 0),
        "pagedown" => new("PageDown", "PageDown", 34, null, 0),
        "home" => new("Home", "Home", 36, null, 0),
        "end" => new("End", "End", 35, null, 0),
        "insert" => new("Insert", "Insert", 45, null, 0),
        _ when key.Length is 2 or 3 && (key[0] is 'f' or 'F') && int.TryParse(key[1..], out var number) && number is >= 1 and <= 12
            => new("F" + number, "F" + number, 111 + number, null, 0),
        _ => null
    };

    internal static JsonObject KeyEvent(string type, BrowserKeyPress press)
    {
        var value = new JsonObject
        {
            ["type"] = type,
            ["key"] = press.Key,
            ["code"] = press.Code,
            ["windowsVirtualKeyCode"] = press.VirtualKey,
            ["nativeVirtualKeyCode"] = press.VirtualKey,
            ["modifiers"] = press.Modifiers
        };
        if (press.Text is not null)
        {
            value["text"] = press.Text;
            value["unmodifiedText"] = press.Text;
        }
        return value;
    }
}
