using System.Text.Json;
using System.Text.Json.Nodes;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>
/// Stores the OpenCode Zen API key in the AI component's own auth file
/// (<c>&lt;DataDir&gt;\AiAssistant\data\opencode\auth.json</c>, i.e. the component's XDG data dir),
/// in the same format <c>opencode auth login</c> writes. A component restart is required afterwards.
/// </summary>
internal static class ZenCredentialStore
{
    internal static string AuthFile => Path.Combine(AiComponentService.Root, "data", "opencode", "auth.json");

    internal static async Task SaveAsync(string apiKey, CancellationToken ct = default) => await SaveAsync(apiKey, AuthFile, ct);

    internal static async Task SaveAsync(string apiKey, string path, CancellationToken ct = default)
    {
        var key = apiKey.Trim();
        if (key.Length == 0) throw new ArgumentException("OpenCode Zen API Key 为空。");
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("无效的登录信息路径。");
        Directory.CreateDirectory(directory);
        var root = new JsonObject();
        if (File.Exists(path))
        {
            try { if (JsonNode.Parse(await File.ReadAllTextAsync(path, ct)) is JsonObject existing) root = existing; }
            catch (JsonException) { /* A broken store is replaced rather than blocking sign-in. */ }
        }
        // Keep other providers' credentials; only the Zen entry is replaced.
        root[AgentErrorParser.ZenProviderId] = new JsonObject { ["type"] = "api", ["key"] = key };
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
