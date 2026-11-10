using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services.AiAgent;

internal static class AgentActivityStore
{
    private static string PathFor(string session) => Path.Combine(AiComponentService.Root, "Activity",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(session))) + ".json");
    internal static IReadOnlyList<AgentOperation> Read(string session)
    {
        var path = PathFor(session);
        if (!File.Exists(path)) return [];
        try { return JsonSerializer.Deserialize<List<AgentOperation>>(File.ReadAllText(path)) ?? []; }
        catch (JsonException) { return []; }
    }
    internal static void Save(string session, IReadOnlyList<AgentOperation> operations)
    {
        var path = PathFor(session);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(operations.TakeLast(50)));
        File.Move(path + ".tmp", path, true);
    }
    internal static void Delete(string session)
    {
        var path = PathFor(session);
        if (File.Exists(path)) File.Delete(path);
    }
}
