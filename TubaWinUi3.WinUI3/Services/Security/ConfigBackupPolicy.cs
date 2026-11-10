using System.Text.Json;
using System.Text.Json.Nodes;

namespace TubaWinUi3.Services;

internal static class ConfigBackupPolicy
{
    internal static bool IsCredential(string key) => AppSettings.IsSecretKey(key)
        || key.Equals("ApiKey", StringComparison.OrdinalIgnoreCase)
        || key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("token", StringComparison.OrdinalIgnoreCase)
        || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || key.Equals("PhoneLinkApprovedDevices", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("PhoneLinkPair", StringComparison.OrdinalIgnoreCase)
        || key.Equals("PhoneLinkSecureTransportVersion", StringComparison.OrdinalIgnoreCase);

    internal static bool Exclude(string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        return parts.Any(p => p.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || p.Contains(".corrupt-", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(".config-import-", StringComparison.OrdinalIgnoreCase)
            || new[] { "instance", "Logs", "IconCache", "Metadata" }.Contains(p, StringComparer.OrdinalIgnoreCase)
            || p.Equals(".config_location", StringComparison.OrdinalIgnoreCase));
    }

    internal static string Sanitize(string text)
    {
        var root = JsonNode.Parse(text) ?? throw new JsonException("配置内容为空。");
        Clean(root);
        return root.ToJsonString();
    }

    private static void Clean(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToArray())
                if (IsCredential(key)) obj.Remove(key);
                else if (obj[key] is { } child) Clean(child);
        }
        else if (node is JsonArray array)
            foreach (var child in array) if (child is not null) Clean(child);
    }
}
