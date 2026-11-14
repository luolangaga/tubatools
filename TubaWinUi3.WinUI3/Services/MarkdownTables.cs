using System.Text.RegularExpressions;

namespace TubaWinUi3.Services;

internal enum MarkdownTableAlignment
{
    Left,
    Center,
    Right
}

internal sealed record MarkdownTable(
    IReadOnlyList<string[]> Rows,
    IReadOnlyList<MarkdownTableAlignment> Alignments)
{
    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(row => row.Length);
}

/// <summary>
/// GFM 管道表格的最小解析器。只负责切行、切单元格和对齐信息；
/// 单元格里的行内样式仍交给 <see cref="MarkdownTextService"/>。
/// </summary>
internal static partial class MarkdownTables
{
    internal static bool IsStart(string[] lines, int index)
    {
        if (index < 0 || index + 1 >= lines.Length) return false;
        if (!LooksLikeRow(lines[index])) return false;
        return IsSeparatorRow(lines[index + 1]);
    }

    internal static MarkdownTable? Parse(string[] lines, int start, out int next)
    {
        next = start;
        if (!IsStart(lines, start)) return null;

        var alignments = ParseAlignments(lines[start + 1]);
        var rows = new List<string[]>();
        var index = start;
        while (index < lines.Length && LooksLikeRow(lines[index]))
        {
            if (index == start + 1 && IsSeparatorRow(lines[index]))
            {
                index++;
                continue;
            }

            var cells = SplitCells(lines[index]);
            if (cells.Length > 0) rows.Add(cells);
            index++;
        }

        if (rows.Count == 0 || alignments.Count == 0)
        {
            next = start;
            return null;
        }

        var columns = Math.Max(rows.Max(row => row.Length), alignments.Count);
        for (var r = 0; r < rows.Count; r++)
        {
            if (rows[r].Length == columns) continue;
            var padded = new string[columns];
            Array.Fill(padded, "");
            Array.Copy(rows[r], padded, rows[r].Length);
            rows[r] = padded;
        }
        for (var c = alignments.Count; c < columns; c++) alignments.Add(MarkdownTableAlignment.Left);

        next = index;
        return new MarkdownTable(rows, alignments);
    }

    private static bool LooksLikeRow(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith('|') && trimmed.LastIndexOf('|') > 0;
    }

    private static bool IsSeparatorRow(string line)
    {
        var trimmed = line.Trim();
        if (!LooksLikeRow(trimmed)) return false;
        var cells = SplitCells(trimmed);
        return cells.Length > 0 && cells.All(cell => SeparatorCellRegex().IsMatch(cell));
    }

    private static List<MarkdownTableAlignment> ParseAlignments(string line)
    {
        var result = new List<MarkdownTableAlignment>();
        foreach (var cell in SplitCells(line))
        {
            var value = cell.Trim();
            result.Add(value.StartsWith(':')
                ? value.EndsWith(':') ? MarkdownTableAlignment.Center : MarkdownTableAlignment.Left
                : value.EndsWith(':') ? MarkdownTableAlignment.Right : MarkdownTableAlignment.Left);
        }
        return result;
    }

    private static string[] SplitCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];

        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (c == '\\' && i + 1 < trimmed.Length && trimmed[i + 1] == '|')
            {
                current.Append('|');
                i++;
                continue;
            }

            if (c == '|')
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        cells.Add(current.ToString().Trim());
        return cells.ToArray();
    }

    [GeneratedRegex(@"^:?-+:?$")]
    private static partial Regex SeparatorCellRegex();
}
