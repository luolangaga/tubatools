using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TubaWinUi3.Services.Telemetry;

/// <summary>
/// 将既有 SDK/离线队列的 gzip JSON-stream 转为 Axiom 事件。
/// SDK 仅用于在本地构造已脱敏的数据；所有网络请求直接发到 Axiom。
/// </summary>
internal static class AxiomTelemetryPayload
{
    internal static byte[] ConvertBatch(byte[] payload, string contentEncoding)
    {
        using var input = new MemoryStream(payload);
        using Stream decoded = contentEncoding.Equals("gzip", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(input, CompressionMode.Decompress) : input;
        using var reader = new StreamReader(decoded, Encoding.UTF8);
        var events = new JsonArray();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var envelope = JsonNode.Parse(line) as JsonObject
                ?? throw new JsonException("遥测条目必须是对象");
            events.Add(ConvertEnvelope(envelope));
        }
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            JsonSerializer.Serialize(gzip, events);
        return output.ToArray();
    }

    internal static JsonObject ConvertEnvelope(JsonObject envelope)
    {
        var data = envelope["data"]?["baseData"] as JsonObject
            ?? throw new JsonException("缺少遥测数据");
        var baseType = envelope["data"]?["baseType"]?.GetValue<string>();
        var tags = envelope["tags"];
        var details = data["exceptions"] as JsonArray;
        var timestamp = envelope["time"]?.GetValue<string>()
            ?? throw new JsonException("缺少原始事件时间");
        if (!DateTimeOffset.TryParse(timestamp, out _)) throw new JsonException("无效事件时间");
        var properties = data["properties"]?.DeepClone() ?? new JsonObject();
        // 老批次重传也使用相同 event_id，看板按它去重，避免网络重试扩大统计。
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.ToJsonString())));
        return new JsonObject
        {
            ["_time"] = timestamp,
            ["event_id"] = "client:" + id,
            ["kind"] = baseType == "ExceptionData" ? "exception" : "event",
            ["name"] = data["name"]?.DeepClone() ?? JsonValue.Create("exception"),
            ["user_Id"] = tags?["ai.user.id"]?.DeepClone() ?? JsonValue.Create(""),
            ["session_Id"] = tags?["ai.session.id"]?.DeepClone() ?? JsonValue.Create(""),
            ["application_Version"] = tags?["ai.application.ver"]?.DeepClone() ?? JsonValue.Create(""),
            ["customDimensions"] = properties,
            ["version"] = properties["version"]?.DeepClone() ?? tags?["ai.application.ver"]?.DeepClone() ?? JsonValue.Create(""),
            ["tool"] = properties["tool"]?.DeepClone() ?? JsonValue.Create(""),
            ["frames"] = details?.FirstOrDefault()?["parsedStack"]?.ToJsonString() ?? "[]",
            ["type"] = details?.FirstOrDefault()?["typeName"]?.DeepClone() ?? JsonValue.Create(""),
            ["outerMessage"] = details?.FirstOrDefault()?["message"]?.DeepClone() ?? JsonValue.Create(""),
            ["innermostMessage"] = details?.LastOrDefault()?["message"]?.DeepClone() ?? JsonValue.Create(""),
            ["details"] = details?.DeepClone() ?? new JsonArray(),
            ["origin"] = "desktop",
        };
    }

    // Axiom 可返回 HTTP 200 但只接收部分条目；不能仅按状态码判定成功。
    internal static bool IsFullyAccepted(string response)
    {
        try
        {
            using var json = JsonDocument.Parse(response);
            return json.RootElement.TryGetProperty("failed", out var failed)
                && failed.TryGetInt32(out var count) && count == 0
                && json.RootElement.TryGetProperty("ingested", out var ingested)
                && ingested.TryGetInt32(out var accepted) && accepted > 0;
        }
        catch (JsonException) { return false; }
    }
}
