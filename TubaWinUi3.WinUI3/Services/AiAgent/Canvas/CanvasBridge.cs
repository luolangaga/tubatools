using System.Text.Json;
using System.Text.Json.Serialization;

namespace TubaWinUi3.Services.AiAgent;

public enum CanvasRequestKind { Unknown, Prompt, Insert, Title, Open, Ready, Height }

public sealed record CanvasRequest(CanvasRequestKind Kind, string Text, double Height, bool Silent)
{
    public static CanvasRequest Unknown => new(CanvasRequestKind.Unknown, "", 0, false);
}

/// <summary>
/// JSON contract between a canvas page and the host. Parsing lives here (pure, unit tested)
/// so a hostile or buggy page can only reach the host through this narrow, bounded surface.
/// </summary>
public static class CanvasBridge
{
    public const int MaxMessageBytes = 64 * 1024;
    public const int MaxTextLength = 8000;
    public const int MaxUrlLength = 2048;
    public const double MaxReportedHeight = 20000;

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Returns <see cref="CanvasRequestKind.Unknown"/> for anything not whitelisted.</summary>
    public static CanvasRequest Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxMessageBytes) return CanvasRequest.Unknown;
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException) { return CanvasRequest.Unknown; }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return CanvasRequest.Unknown;
            var type = Text(root, "type");
            if (type == "ready") return new(CanvasRequestKind.Ready, "", 0, false);
            if (type == "height")
            {
                if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var height))
                    return CanvasRequest.Unknown;
                if (!double.IsFinite(height) || height <= 0) return CanvasRequest.Unknown;
                return new(CanvasRequestKind.Height, "", Math.Clamp(height, 0, MaxReportedHeight), false);
            }
            var text = Text(root, "text");
            if (type is not ("prompt" or "insert" or "title" or "open") || text.Length == 0) return CanvasRequest.Unknown;
            var limit = type == "open" ? MaxUrlLength : MaxTextLength;
            if (text.Length > limit) text = text[..limit];
            var kind = type switch
            {
                "prompt" => CanvasRequestKind.Prompt,
                "insert" => CanvasRequestKind.Insert,
                "title" => CanvasRequestKind.Title,
                _ => CanvasRequestKind.Open
            };
            if (kind == CanvasRequestKind.Open && !IsAllowedUrl(text)) return CanvasRequest.Unknown;
            var silent = root.TryGetProperty("silent", out var s) && s.ValueKind == JsonValueKind.True;
            return new(kind, text, 0, silent);
        }
    }

    /// <summary>Only public http(s) reach the shell; the page itself can never leave its virtual host.</summary>
    public static bool IsAllowedUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxUrlLength) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        if (uri.UserInfo.Length > 0) return false;
        var host = uri.Host.Trim('[', ']');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return false;
        if (host is "127.0.0.1" or "::1" or "0.0.0.0") return false;
        if (System.Net.IPAddress.TryParse(host, out var address))
        {
            var bytes = address.GetAddressBytes();
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                if (bytes[0] is 0 or 10 or 127) return false;
                if (bytes[0] == 192 && bytes[1] == 168) return false;
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
                if (bytes[0] == 169 && bytes[1] == 254) return false;
            }
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal) return false;
        }
        return true;
    }

    public static bool IsAllowedSource(string? source) =>
        source is not null && source.StartsWith(CanvasShim.Origin, StringComparison.OrdinalIgnoreCase);

    private static string Text(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    public static string BuildState(bool running, string phase, string detail) =>
        JsonSerializer.Serialize(new { type = "state", running, phase, detail = detail.Length > 240 ? detail[..240] : detail }, Options);

    public static string BuildMessage(string role, string text) =>
        JsonSerializer.Serialize(new { type = "message", role, text = text.Length > MaxTextLength ? text[..MaxTextLength] : text }, Options);

    public static string BuildNotice(string text, bool error) =>
        JsonSerializer.Serialize(new { type = "notice", level = error ? "error" : "info", text }, Options);

    public static string BuildImages(IReadOnlyList<string> names) =>
        JsonSerializer.Serialize(new
        {
            type = "images",
            items = names.Select(name => new { name, url = "assets/" + Uri.EscapeDataString(name) }).ToArray()
        }, Options);
}
