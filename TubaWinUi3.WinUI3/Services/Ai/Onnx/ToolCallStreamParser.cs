using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services.Ai.Onnx;

/// <summary>解析一段增量后的产出：可见文本增量 + 解析出的工具调用。</summary>
public sealed class ParseResult
{
    public string Text { get; init; } = "";
    public IReadOnlyList<LocalToolCall> Tools { get; init; } = [];
    public bool IsEmpty => Text.Length == 0 && Tools.Count == 0;
}

/// <summary>
/// 本地模型的流式工具调用解析器（纯逻辑，可单测）。
/// 模型按约定输出 &lt;tool_call&gt;{"name":...,"arguments":{...}}&lt;/tool_call&gt;，
/// 本解析器把这类块从可见文本中剥离、解析成 <see cref="LocalToolCall"/>，
/// 其余文本原样作为文本增量返回。增量输入下会保留可能构成标记前缀的尾部，
/// 避免把半个 "&lt;tool_call&gt;" 当普通文本泄漏出去。
/// </summary>
public sealed class ToolCallStreamParser
{
    private const string Open = LocalChatPrompt.ToolCallOpen;
    private const string Close = LocalChatPrompt.ToolCallClose;

    private readonly StringBuilder _buffer = new();
    private bool _inToolCall;

    /// <summary>喂入一段文本增量，返回本次可安全产出的文本与已完成的工具调用。</summary>
    public ParseResult Append(string? delta)
    {
        if (!string.IsNullOrEmpty(delta)) _buffer.Append(delta);

        var text = new StringBuilder();
        var tools = new List<LocalToolCall>();

        while (true)
        {
            var current = _buffer.ToString();

            if (!_inToolCall)
            {
                var openIdx = current.IndexOf(Open, StringComparison.Ordinal);
                if (openIdx >= 0)
                {
                    text.Append(current, 0, openIdx);
                    _buffer.Remove(0, openIdx + Open.Length);
                    _inToolCall = true;
                    continue;
                }

                // 没有完整标记：保留「可能是标记前缀」的尾部，其余作为文本产出
                var hold = LongestMarkerPrefixSuffix(current, Open);
                var emitLen = current.Length - hold;
                if (emitLen > 0)
                {
                    text.Append(current, 0, emitLen);
                    _buffer.Remove(0, emitLen);
                }
                break;
            }
            else
            {
                var closeIdx = current.IndexOf(Close, StringComparison.Ordinal);
                if (closeIdx >= 0)
                {
                    var payload = current[..closeIdx];
                    _buffer.Remove(0, closeIdx + Close.Length);
                    _inToolCall = false;
                    if (TryParseToolCall(payload, out var call)) tools.Add(call);
                    continue;
                }
                break;
            }
        }

        return new ParseResult { Text = text.ToString(), Tools = tools };
    }

    /// <summary>流结束：把残留内容全部产出（不完整的工具块尽力解析）。</summary>
    public ParseResult Flush()
    {
        var current = _buffer.ToString();
        _buffer.Clear();
        var text = new StringBuilder();
        var tools = new List<LocalToolCall>();

        if (_inToolCall)
        {
            _inToolCall = false;
            if (TryParseToolCall(current, out var call)) tools.Add(call);
        }
        else
        {
            text.Append(current);
        }

        return new ParseResult { Text = text.ToString(), Tools = tools };
    }

    private static bool TryParseToolCall(string payload, out LocalToolCall call)
    {
        call = null!;
        payload = payload.Trim();
        if (payload.Length == 0) return false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("name", out var nameProp)) return false;
            var name = nameProp.GetString() ?? "";
            if (name.Length == 0) return false;

            var args = new Dictionary<string, object?>();
            if (root.TryGetProperty("arguments", out var argProp) && argProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in argProp.EnumerateObject())
                    args[p.Name] = p.Value.Clone();
            }

            call = new LocalToolCall
            {
                Id = $"lc_{Guid.NewGuid():N}"[..10],
                Name = name,
                Arguments = args,
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>返回 text 末尾中「是 marker 前缀」的最长后缀长度。</summary>
    internal static int LongestMarkerPrefixSuffix(string text, string marker)
    {
        var max = Math.Min(text.Length, marker.Length - 1);
        for (var len = max; len > 0; len--)
        {
            if (string.CompareOrdinal(text, text.Length - len, marker, 0, len) == 0)
                return len;
        }
        return 0;
    }
}
