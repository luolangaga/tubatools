using System.Drawing;

namespace ComputerUse.Windows.Runtime;

// The cells in view of a sheet that a full read probed, each with the
// rectangle its hit test returned. A merged cell appears once.
internal sealed record SpreadsheetLayout(
    Rectangle Area,
    int Row0,
    int Row1,
    int Column0,
    int Column1,
    IReadOnlyDictionary<(int Row, int Column), Rectangle> Cells)
{
    // The first and last cell in row-major order: probing both again
    // detects a scroll, a resize or a moved window.
    internal (int Row, int Column) First => Cells.Keys.MinBy(Order);

    internal (int Row, int Column) Last => Cells.Keys.MaxBy(Order);

    private static long Order((int Row, int Column) cell) =>
        (long)cell.Row * 16384 * 4 + cell.Column;
}

// One hit test of a read after an action, and the cell it must return.
internal sealed record SpreadsheetProbe(int Row, int Column, Point Point, Rectangle Bounds);

// After an action on a cell, a sheet is read again at the cells that can
// have changed instead of at every cell in view: the cells that showed a
// value or the cursor, the acted-on cell and the cursor cell. Two more hits,
// at the first and last cell in view, confirm that no cell moved. Empty
// cells are not read again. See UiaProvider.TryReadSpreadsheetBack.
internal static class SpreadsheetReadback
{
    // The hit tests for cells, first and last cell first. Null when a cell
    // is not in view: the sheet must have scrolled to show it, so only a
    // full read can say what the view shows.
    internal static IReadOnlyList<SpreadsheetProbe>? Plan(
        SpreadsheetLayout layout,
        IEnumerable<(int Row, int Column)> cells)
    {
        var order = new List<(int Row, int Column)> { layout.First, layout.Last };
        foreach (var cell in cells)
        {
            if (!layout.Cells.ContainsKey(cell)) { return null; }
            if (!order.Contains(cell)) { order.Add(cell); }
        }
        return order
            .Distinct()
            .Select(cell =>
            {
                var bounds = layout.Cells[cell];
                return new SpreadsheetProbe(
                    cell.Row, cell.Column, ProbePoint(bounds, layout.Area), bounds);
            })
            .ToArray();
    }

    // True when the hit returned the planned cell at its earlier rectangle.
    // Any other answer means the sheet scrolled, a row or column changed
    // size, or a window covers the cell.
    internal static bool Confirms<T>(SpreadsheetProbe probe, SpreadsheetCellHit<T>? hit) =>
        hit is not null &&
        hit.Row == probe.Row &&
        hit.Column == probe.Column &&
        hit.Bounds == probe.Bounds;

    // The middle of the part of bounds inside area.
    internal static Point ProbePoint(Rectangle bounds, Rectangle area)
    {
        var visible = Rectangle.Intersect(bounds, area);
        if (visible.IsEmpty) { visible = bounds; }
        return new Point(visible.Left + visible.Width / 2, visible.Top + visible.Height / 2);
    }

    // The cell under the cursor that a Name Box shows ("C4"). A range
    // ("A1:B3") or a named range is not one cell: keys and text then act on
    // more than the cursor cell, so the caller reads the whole sheet.
    internal static (int Row, int Column)? CursorCell(string? nameBox) =>
        SpreadsheetGrid.TryParseA1(nameBox, out var row, out var column)
            ? (row, column)
            : null;

    // Whether a key changes at most the cell under the cursor: it edits the
    // cell, commits or cancels the edit, or moves the cursor. Shift is the
    // only modifier, because Ctrl and Alt chords paste, fill, delete rows or
    // open dialogs. A key that scrolls the sheet is allowed: the probes of
    // the read after it see the scroll and fall back to a full read.
    internal static bool IsCellLocalKey(string key)
    {
        var tokens = key.Split('+', StringSplitOptions.TrimEntries);
        if (tokens.Length == 0 || tokens.Any(string.IsNullOrEmpty)) { return false; }
        try
        {
            if (tokens[..^1].Any(token => WindowsKeyMapping.KeyCode(token) != ShiftKey))
            {
                return false;
            }
            return WindowsKeyMapping.KeyCode(tokens[^1]) is
                0x08 or 0x09 or 0x0D or 0x1B or 0x20 or 0x2E or // Backspace, Tab, Enter, Esc, Space, Delete
                >= 0x21 and <= 0x28 or                           // Page Up/Down, End, Home, arrows
                0x71 or                                          // F2 edits the cell
                >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or    // digits and letters
                >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDE;      // punctuation
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    // Whether a cell-local key moves the cursor to another cell (unless the
    // cursor is at the edge of the sheet): Enter, Tab, arrows, Home, End and
    // Page Up/Down.
    internal static bool IsCursorKey(string key)
    {
        if (!IsCellLocalKey(key)) { return false; }
        var code = WindowsKeyMapping.KeyCode(key.Split('+', StringSplitOptions.TrimEntries)[^1]);
        return code is 0x09 or 0x0D or >= 0x21 and <= 0x28;
    }

    // Where a cursor key moves the cursor from a cell, with LibreOffice's
    // default that Enter moves down. Null when the destination depends on
    // the sheet's content or scroll position (End, Page Up and Down), when
    // Shift extends a selection with an arrow, Home or End, or when the move
    // leaves the sheet.
    internal static (int Row, int Column)? PredictCursor((int Row, int Column) from, string key)
    {
        if (!IsCursorKey(key)) { return null; }
        var tokens = key.Split('+', StringSplitOptions.TrimEntries);
        var shift = tokens.Length > 1;
        (int Row, int Column)? to = WindowsKeyMapping.KeyCode(tokens[^1]) switch
        {
            0x0D => shift ? (from.Row - 1, from.Column) : (from.Row + 1, from.Column),
            0x09 => shift ? (from.Row, from.Column - 1) : (from.Row, from.Column + 1),
            _ when shift => null,
            0x24 => (from.Row, 0),
            0x25 => (from.Row, from.Column - 1),
            0x26 => (from.Row - 1, from.Column),
            0x27 => (from.Row, from.Column + 1),
            0x28 => (from.Row + 1, from.Column),
            _ => null,
        };
        return to is { Row: >= 0, Column: >= 0 } ? to : null;
    }

    private const ushort ShiftKey = 0x10;
}
