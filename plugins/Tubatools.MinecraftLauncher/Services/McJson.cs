using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tubatools.MinecraftLauncher.Services;

/// <summary>JSON 读取与合并。合并语义与 PCL 一致：对象递归合并、数组追加、标量由子项覆盖。</summary>
internal static class McJson
{
    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static JsonObject ParseObject(string text)
    {
        try
        {
            return JsonNode.Parse(text, NodeOptions, DocumentOptions) as JsonObject
                   ?? throw new InvalidDataException("JSON 根节点不是对象。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"JSON 解析失败：{ex.Message}", ex);
        }
    }

    public static void Merge(JsonObject target, JsonNode? source)
    {
        if (source is not JsonObject sourceObject) return;
        foreach (var property in sourceObject.ToArray())
        {
            if (target[property.Key] is JsonObject targetChild && property.Value is JsonObject sourceChild)
                Merge(targetChild, sourceChild);
            else if (target[property.Key] is JsonArray targetArray && property.Value is JsonArray sourceArray)
                foreach (var item in sourceArray)
                    targetArray.Add(item?.DeepClone());
            else
                target[property.Key] = property.Value?.DeepClone();
        }
    }

    public static string? Str(this JsonNode? node, string name) =>
        node is JsonObject obj ? obj[name]?.ToString() : null;

    public static JsonObject? Obj(this JsonNode? node, string name) =>
        node is JsonObject obj ? obj[name] as JsonObject : null;

    public static JsonArray? Arr(this JsonNode? node, string name) =>
        node is JsonObject obj ? obj[name] as JsonArray : null;

    public static JsonArray? Arr(this JsonNode? node, string first, string second) =>
        node is JsonObject obj && obj[first] is JsonObject child ? child[second] as JsonArray : null;

    public static long Long(this JsonNode? node, string name, long fallback = 0)
    {
        var text = node.Str(name);
        return long.TryParse(text, out var value) ? value : fallback;
    }

    public static int Int(this JsonNode? node, string name, int fallback = 0) =>
        (int)Math.Clamp(node.Long(name, fallback), int.MinValue, int.MaxValue);

    public static bool Bool(this JsonNode? node, string name, bool fallback = false)
    {
        var value = node is JsonObject obj ? obj[name] : null;
        if (value is null) return fallback;
        if (value.GetValueKind() == JsonValueKind.True) return true;
        if (value.GetValueKind() == JsonValueKind.False) return false;
        return bool.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    public static DateTime Date(this JsonNode? node, string name, DateTime fallback)
    {
        var text = node.Str(name);
        return DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var value)
            ? value.ToLocalTime()
            : fallback;
    }
}
