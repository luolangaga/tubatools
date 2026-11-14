using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public sealed class MarkdownTableTests
{
    [Fact]
    public void Parse_GfmTable_HeaderBodyAndAlignment()
    {
        var lines = """
        | Name | Value | Note |
        | :-- | --: | :--: |
        | Steam | 1 | a \| b |
        | ATK | 2 | `code` |
        """.Replace("\r\n", "\n").Split('\n');

        Assert.True(MarkdownTables.IsStart(lines, 0));
        var table = MarkdownTables.Parse(lines, 0, out var next);

        Assert.NotNull(table);
        Assert.Equal(4, next);
        Assert.Equal(3, table!.ColumnCount);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("Name", table.Rows[0][0]);
        Assert.Equal("a | b", table.Rows[1][2]);
        Assert.Equal(MarkdownTableAlignment.Left, table.Alignments[0]);
        Assert.Equal(MarkdownTableAlignment.Right, table.Alignments[1]);
        Assert.Equal(MarkdownTableAlignment.Center, table.Alignments[2]);
    }

    [Fact]
    public void Parse_NonTable_ReturnsFalse()
    {
        var lines = new[] { "Name | Value", "---- | ----" };
        Assert.False(MarkdownTables.IsStart(lines, 0));
    }
}
