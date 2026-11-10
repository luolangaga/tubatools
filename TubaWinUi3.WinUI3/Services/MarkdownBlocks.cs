using System.Text;

namespace TubaWinUi3.Services;

internal sealed record MarkdownBlock(bool IsCode, string Text, string Language);
internal static class MarkdownBlocks
{
    internal static IReadOnlyList<MarkdownBlock> Parse(string markdown)
    {
        var result = new List<MarkdownBlock>();
        var buffer = new StringBuilder();
        char marker = '\0';
        int fenceLength = 0;
        string language = "";
        void Flush(bool code)
        {
            if (buffer.Length > 0) result.Add(new(code, buffer.ToString().TrimEnd('\r', '\n'), language));
            buffer.Clear();
        }
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.TrimStart();
            var c = trimmed.Length > 0 ? trimmed[0] : '\0';
            var length = c is '`' or '~' ? trimmed.TakeWhile(ch => ch == c).Count() : 0;
            if (marker == '\0' && length >= 3)
            {
                Flush(false); marker = c; fenceLength = length; language = trimmed[length..].Trim();
            }
            else if (marker != '\0' && marker == c && length >= fenceLength && trimmed[length..].Trim().Length == 0)
            {
                Flush(true); marker = '\0'; language = "";
            }
            else buffer.AppendLine(line);
        }
        Flush(marker != '\0');
        return result;
    }
}
