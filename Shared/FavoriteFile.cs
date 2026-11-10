using System.Text.Json.Nodes;

namespace TubaWinUi3.Storage;

/// <summary>Both frontends participate in the same read/modify/write protocol.</summary>
internal static class FavoriteFile
{
    internal static List<string> Read(string path)
    {
        using var lease = AtomicFile.Lock(path);
        return ReadLocked(path);
    }

    private static List<string> ReadLocked(string path)
    {
        var text = AtomicFile.ReadText(path, text => _ = Parse(text));
        return text is null ? [] : Parse(text);
    }

    private static List<string> Parse(string text)
    {
        if (JsonNode.Parse(text) is not JsonArray array) throw new InvalidDataException("收藏文件格式无效。");
        return array.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s
            : throw new InvalidDataException("收藏文件包含无效路径。")).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    internal static List<string> Update(string path, Func<List<string>, List<string>> update)
    {
        using var lease = AtomicFile.Lock(path);
        var entries = update(ReadLocked(path));
        var array = new JsonArray();
        foreach (var entry in entries.Distinct(StringComparer.OrdinalIgnoreCase)) array.Add((JsonNode?)JsonValue.Create(entry));
        AtomicFile.WriteText(path, array.ToJsonString());
        return entries;
    }
}
