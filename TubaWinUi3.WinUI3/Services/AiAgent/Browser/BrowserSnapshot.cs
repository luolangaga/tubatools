using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services.AiAgent;

internal sealed record BrowserSnapshotItem(string Ref, string Tag, string Type, string Label, bool Disabled,
    bool? Checked, string Value, string Href, double X, double Y);

internal sealed record BrowserSnapshotData(string Url, string Title, string Ready, string Text,
    IReadOnlyList<BrowserSnapshotItem> Items, int ScrollY, int ScrollHeight, int ViewportHeight)
{
    internal static BrowserSnapshotData Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new BrowserToolException("SNAPSHOT_FAILED", "页面没有返回可读取的 DOM 快照。");
        var items = new List<BrowserSnapshotItem>();
        if (value.TryGetProperty("items", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var item in list.EnumerateArray())
                items.Add(new BrowserSnapshotItem(
                    ReadText(item, "ref"), ReadText(item, "tag"), ReadText(item, "type"), ReadText(item, "label"),
                    Bool(item, "disabled"), item.TryGetProperty("checked", out var checkedValue) ? checkedValue.ValueKind == JsonValueKind.True : null,
                    ReadText(item, "value"), ReadText(item, "href"),
                    Number(item, "x"), Number(item, "y")));
        return new BrowserSnapshotData(
            ReadText(value, "url"), ReadText(value, "title"), ReadText(value, "ready"), ReadText(value, "text"), items,
            (int)Number(value, "scrollY"), (int)Number(value, "scrollHeight"), (int)Number(value, "viewportHeight"));
    }

    private static string ReadText(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static bool Bool(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;

    private static double Number(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.TryGetDouble(out var number) ? number : 0;

    internal string Format(bool includeText = true)
    {
        var builder = new StringBuilder();
        builder.AppendLine("URL: " + (Url.Length > 0 ? Url : "(空白页)"));
        builder.AppendLine("标题: " + (Title.Length > 0 ? Title : "(无标题)"));
        builder.AppendLine($"加载状态: {Ready} · 滚动 {ScrollY}/{Math.Max(0, ScrollHeight - ViewportHeight)}（视口 {ViewportHeight}）");
        if (includeText)
        {
            builder.AppendLine();
            builder.AppendLine("页面文本:");
            builder.AppendLine(Text.Length > 0 ? Text : "(无可见文本)");
        }
        builder.AppendLine();
        builder.AppendLine("可交互元素（ref 仅在本次页面状态有效；页面变化后重新 snapshot）：");
        if (Items.Count == 0)
        {
            builder.AppendLine("(没有可交互元素)");
        }
        else
        {
            foreach (var item in Items)
            {
                builder.Append(item.Ref).Append(" <").Append(item.Tag);
                if (item.Type.Length > 0) builder.Append('[').Append(item.Type).Append(']');
                builder.Append("> ");
                if (item.Label.Length > 0) builder.Append('"').Append(item.Label).Append('"');
                if (item.Value.Length > 0) builder.Append(" value=\"").Append(item.Value).Append('"');
                if (item.Checked is not null) builder.Append(item.Checked.Value ? " [已勾选]" : " [未勾选]");
                if (item.Disabled) builder.Append(" [禁用]");
                if (item.Href.Length > 0) builder.Append(' ').Append(item.Href);
                builder.AppendLine();
            }
        }
        return builder.ToString().TrimEnd();
    }
}
