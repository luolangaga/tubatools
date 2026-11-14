using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tubatools.AiXamlCard;

/// <summary>一张待渲染的卡片内容。</summary>
public sealed record CardContent(string Title, string Xaml, string Code)
{
    public bool HasCode => !string.IsNullOrWhiteSpace(Code);
}

/// <summary>
/// AI 交来的一批卡片。一次提交可以包含多张，窗口用标签页并排展示——
/// 这样「一次回答给出多个指标面板」不会互相覆盖。
/// </summary>
public sealed record CardRequest(string RequestId, IReadOnlyList<CardContent> Cards)
{
    public int Count => Cards.Count;
}

/// <summary>
/// 请求的磁盘契约：<c>&lt;pluginData&gt;/requests/&lt;id&gt;.json</c>。
/// 同时接受单张（<c>xaml</c>）与多张（<c>cards: [...]</c>）两种写法，
/// 后者让助手一次提交整套面板。
/// </summary>
public static class CardRequestFile
{
    public const string FolderName = "requests";
    public const int MaxXamlCharacters = 192 * 1024;
    public const int MaxCodeCharacters = 96 * 1024;
    public const int MaxCards = 8;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string DirectoryFor(string pluginData) => Path.Combine(pluginData, FolderName);

    public static CardRequest? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > (MaxXamlCharacters + MaxCodeCharacters) * MaxCards) return null;
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException) { return null; }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var id = Text(root, "id");
            var cards = new List<CardContent>();

            if (root.TryGetProperty("cards", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    if (cards.Count >= MaxCards) break;
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (Content(Text(item, "title"), Text(item, "xaml"), Text(item, "code")) is { } card) cards.Add(card);
                }
            }
            else if (Content(Text(root, "title"), Text(root, "xaml"), Text(root, "code")) is { } single)
            {
                cards.Add(single);
            }

            return cards.Count == 0 ? null : new CardRequest(id, cards);
        }
    }

    private static CardContent? Content(string title, string xaml, string code)
    {
        if (string.IsNullOrWhiteSpace(xaml)) return null;
        var text = xaml.Length > MaxXamlCharacters ? xaml[..MaxXamlCharacters] : xaml;
        var source = code.Length > MaxCodeCharacters ? code[..MaxCodeCharacters] : code;
        var label = title.Trim();
        if (label.Length > 60) label = label[..60];
        return new CardContent(label, text, source);
    }

    public static CardRequest? Read(string path)
    {
        try { return Parse(File.ReadAllText(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static string Text(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
