using System.Text;

namespace TubaWinUi3.Services.AiAgent;

internal static class AgentHistory
{
    internal static IReadOnlyList<AgentMessage> Merge(IReadOnlyList<AgentMessage> previous, IReadOnlyList<AgentMessage> latest)
    {
        var merged = previous.ToList();
        var indexes = merged.Select((m, i) => (m.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal);
        foreach (var message in latest)
            if (indexes.TryGetValue(message.Id, out var index)) merged[index] = message;
            else { indexes.Add(message.Id, merged.Count); merged.Add(message); }
        return merged;
    }
    internal static string ToMarkdown(string title, IReadOnlyList<AgentMessage> messages)
    {
        var output = new StringBuilder("# " + title.Replace('\r', ' ').Replace('\n', ' ') + "\n\n");
        foreach (var message in messages)
        {
            if (string.IsNullOrWhiteSpace(message.Text) && !(message.Content?.Any(p => p.Text.Length > 0 || p.Operation is not null) ?? false)) continue;
            output.AppendLine("## " + (message.Role == "user" ? AgentText.L("You", "你") : AgentText.L("Title", "AI 助手"))).AppendLine();
            if (message.Content is { Count: > 0 })
                foreach (var part in message.Content)
                {
                    if (part.Text.Length > 0) output.AppendLine(part.Text).AppendLine();
                    if (part.Operation is { } op)
                    {
                        output.AppendLine($"### {op.Label} · {op.Target}").AppendLine();
                        foreach (var text in new[] { op.Command, op.Diff, op.Output }.Where(s => s.Length > 0))
                        {
                            var fence = new string('`', Math.Max(3, LongestBackticks(text) + 1));
                            output.AppendLine(fence).AppendLine(text).AppendLine(fence).AppendLine();
                        }
                    }
                }
            else output.AppendLine(message.Text).AppendLine();
        }
        return output.ToString().ReplaceLineEndings("\n");
    }
    private static int LongestBackticks(string text)
    {
        var longest = 0; var current = 0;
        foreach (var ch in text) { current = ch == '`' ? current + 1 : 0; longest = Math.Max(current, longest); }
        return longest;
    }
}
