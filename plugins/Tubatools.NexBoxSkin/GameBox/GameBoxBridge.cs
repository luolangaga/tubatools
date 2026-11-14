using System.Net;
using System.Text.Json;

namespace Tubatools.NexBoxSkin.GameBox;

internal static class GameBoxBridge
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        IncludeFields = true,
    };

    public static bool TryParse(string? json, out int id, out string command, out JsonElement args)
    {
        id = 0;
        command = "";
        args = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("type", out var type) || !string.Equals(type.GetString(), "invoke", StringComparison.Ordinal))
            {
                return false;
            }

            if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out id))
            {
                return false;
            }

            if (!root.TryGetProperty("cmd", out var commandElement) || string.IsNullOrWhiteSpace(command = commandElement.GetString() ?? ""))
            {
                return false;
            }

            args = root.TryGetProperty("args", out var argsElement) ? argsElement.Clone() : default;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string Result(int id, object? data) =>
        JsonSerializer.Serialize(new { type = "result", id, ok = true, data }, JsonOptions);

    public static string Error(int id, string message) =>
        JsonSerializer.Serialize(new { type = "result", id, ok = false, error = message }, JsonOptions);

    public static string Event(string name, object? data) =>
        JsonSerializer.Serialize(new { type = "event", @event = name, data }, JsonOptions);

    public static bool IsAllowedHttp(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var address))
        {
            if (IPAddress.IsLoopback(address) || IsPrivate(address))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsAllowedProxy(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return uri.Host.Equals("df.nexbox.top", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 169 && bytes[1] == 254);
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        }

        return false;
    }
}
