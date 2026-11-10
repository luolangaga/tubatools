using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using FlaUI.Core.Definitions;

namespace ComputerUse.Windows.Runtime;

// A cell returned by a hit test, with the zero-based row and column that its
// A1 name gives.
internal sealed record SpreadsheetCellHit<T>(T Cell, int Row, int Column, Rectangle Bounds, bool HasValue = false);

// The cells a spreadsheet grid shows. Missing counts probes that did not
// return a cell of the grid (another window covers it); Unprobed counts
// viewport cells that the probe budget left unread. ExtentComplete is false
// when the budget ended the edge scans, so the range may be too small.
internal sealed record SpreadsheetViewport<T>(
    int Row0,
    int Row1,
    int Column0,
    int Column1,
    IReadOnlyList<SpreadsheetCellHit<T>> Cells,
    int Missing,
    int Unprobed,
    bool ExtentComplete);

// A spreadsheet grid (LibreOffice Calc through IAccessible2) is a table
// whose cells are named by A1 reference. LibreOffice 24.8 lists them as its
// children in row-major order over the whole sheet: the second row starts
// after all 16384 cells of the first. LibreOffice 25.2 lists none. Walking
// the children cannot reach the visible rows either way, and the table
// supports neither the Grid nor the Table pattern. The cells on screen are
// reached by hit tests instead, one per cell.
internal static class SpreadsheetGrid
{
    internal static bool RetainCell(JsonObject record, bool cachedFocus, Func<bool> isCurrentFocus) =>
        !string.IsNullOrEmpty(record["value"]?.GetValue<string>()) || cachedFocus || isCurrentFocus();

    // An ordinary direct row/header, or a header inside an exact MSAA row
    // of this table, permits traversal. Editors and unknown parents do not.
    internal static bool CanEnumerateAfterHit(
        ControlType? type, string name, bool containsPoint,
        Func<bool> isTable, Func<bool> isImmediateChild,
        Func<bool>? isOrdinaryRowChild = null)
    {
        if (!containsPoint || type is not (ControlType.DataItem or ControlType.Header or ControlType.HeaderItem) ||
            type == ControlType.DataItem && TryParseA1(name, out _, out _)) { return false; }
        try { return !isTable() && (isImmediateChild() ||
            type is ControlType.Header or ControlType.HeaderItem && isOrdinaryRowChild?.Invoke() == true); }
        catch { return false; }
    }

    // Reads the cells shown in area, one hit test per cell. A hit at the
    // top-left corner finds the first visible cell; hits one pixel past each
    // cell's far edge find the visible rows (down its column) and columns
    // (along its row); then one hit per remaining cell reads the rest.
    // Sibling steps would be cheaper per cell, but a cell found by a hit test
    // has no position among its siblings: LibreOffice answered the first
    // GetNextSibling only after seconds per step and then stopped responding.
    // Returns null when the top-left hit finds no cell.
    internal static SpreadsheetViewport<T>? ReadViewport<T>(
        Rectangle area,
        Func<Point, SpreadsheetCellHit<T>?> hit,
        int probeBudget,
        Func<bool> mayProbe)
    {
        if (area.Width < 4 || area.Height < 4) { return null; }
        var probes = 0;
        var stopped = false;
        var missing = 0;
        var origin = Probe(new Point(area.X + 2, area.Y + 2));
        if (origin is null) { return null; }
        var cells = new Dictionary<(int Row, int Column), SpreadsheetCellHit<T>>();
        cells[(origin.Row, origin.Column)] = origin;

        // Rows: down the first visible column.
        var originX = Middle(origin.Bounds.Left, origin.Bounds.Right, area.Left, area.Right);
        var rows = new List<(int Index, int Center)>
        {
            (origin.Row, Middle(origin.Bounds.Top, origin.Bounds.Bottom, area.Top, area.Bottom)),
        };
        var rowsComplete = true;
        for (var edge = origin.Bounds.Bottom; edge + 1 < area.Bottom;)
        {
            var next = Probe(new Point(originX, edge + 1));
            if (next is null)
            {
                // Covered or out of budget: the lower rows are unknown.
                rowsComplete = false;
                if (!stopped) { missing++; }
                break;
            }
            if (next.Row > rows[^1].Index)
            {
                rows.Add((next.Row, Middle(next.Bounds.Top, next.Bounds.Bottom, area.Top, area.Bottom)));
                cells.TryAdd((next.Row, next.Column), next);
            }
            // Never step backwards, even on an inconsistent rectangle.
            edge = Math.Max(next.Bounds.Bottom, edge + 2);
        }

        // Columns: along the first visible row.
        var columns = new List<(int Index, int Center)>
        {
            (origin.Column, originX),
        };
        var columnsComplete = true;
        var hitEdge = origin.Bounds.Right;
        while (hitEdge + 1 < area.Right)
        {
            var next = Probe(new Point(hitEdge + 1, rows[0].Center));
            if (next is null)
            {
                columnsComplete = false;
                if (!stopped) { missing++; }
                break;
            }
            if (next.Row == origin.Row && next.Column > columns[^1].Index &&
                next.Bounds.Width > 0 && next.Bounds.Left < area.Right)
            {
                columns.Add((next.Column, Middle(next.Bounds.Left, next.Bounds.Right, area.Left, area.Right)));
                cells.TryAdd((next.Row, next.Column), next);
            }
            hitEdge = Math.Max(next.Bounds.Right, hitEdge + 2);
        }

        // Values already read in the first row hint which columns contain
        // data. Read those columns before spending the same bounded work on
        // blank-looking columns. A blank first cell is not proof of an empty
        // column: the second pass still reads it and counts any unread cells.
        var remainingColumns = columns.Skip(1).ToArray();
        var valuedColumns = remainingColumns.Where(column =>
            cells.TryGetValue((origin.Row, column.Index), out var cell) && cell.HasValue).ToArray();
        var otherColumns = remainingColumns.Where(column =>
            !cells.TryGetValue((origin.Row, column.Index), out var cell) || !cell.HasValue).ToArray();
        var unprobed = 0;
        foreach (var group in new[] { valuedColumns, otherColumns })
        {
            foreach (var (_, y) in rows.Skip(1))
            {
                foreach (var (_, x) in group)
                {
                    var cell = Probe(new Point(x, y));
                    if (cell is null)
                    {
                        if (stopped) { unprobed++; } else { missing++; }
                        continue;
                    }
                    // A merged cell answers for every cell it covers: list it once.
                    cells.TryAdd((cell.Row, cell.Column), cell);
                }
            }
        }
        return new SpreadsheetViewport<T>(
            rows[0].Index,
            rows[^1].Index,
            columns[0].Index,
            columns[^1].Index,
            cells.Values.OrderBy(cell => cell.Row).ThenBy(cell => cell.Column).ToArray(),
            missing,
            unprobed,
            columnsComplete && rowsComplete);

        SpreadsheetCellHit<T>? Probe(Point point)
        {
            if (stopped || probes >= probeBudget || !mayProbe())
            {
                stopped = true;
                return null;
            }
            probes++;
            return hit(point);
        }
    }

    // The middle of the part of [start, end) that lies inside [low, high).
    private static int Middle(int start, int end, int low, int high)
    {
        var from = Math.Max(start, low);
        var to = Math.Min(end, high);
        return to > from ? from + (to - from) / 2 : Math.Clamp(start, low, high - 1);
    }

    // Parses an A1 reference ("C10") into zero-based row and column.
    internal static bool TryParseA1(string? name, out int row, out int column)
    {
        row = column = -1;
        if (string.IsNullOrEmpty(name)) { return false; }
        var position = 0;
        long letters = 0;
        while (position < name.Length && name[position] is >= 'A' and <= 'Z')
        {
            letters = letters * 26 + (name[position] - 'A' + 1);
            if (letters > int.MaxValue) { return false; }
            position++;
        }
        if (position == 0 || position > 3 || position == name.Length || name[position] == '0') { return false; }
        long number = 0;
        for (; position < name.Length; position++)
        {
            if (name[position] is < '0' or > '9') { return false; }
            number = number * 10 + (name[position] - '0');
            if (number > int.MaxValue) { return false; }
        }
        row = (int)number - 1;
        column = (int)letters - 1;
        return true;
    }

    internal static string A1(int row, int column)
    {
        var letters = string.Empty;
        for (var number = column + 1; number > 0; number = (number - 1) / 26)
        {
            letters = (char)('A' + (number - 1) % 26) + letters;
        }
        return letters + (row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // True when the top-level window under point is window: a hit test there
    // reads this window, not one that covers it.
    internal static bool PointShowsWindow(Point point, IntPtr window)
    {
        var under = WindowFromPoint(new NativePoint { X = point.X, Y = point.Y });
        return under != IntPtr.Zero &&
            GetAncestor(under, GaRoot) == GetAncestor(window, GaRoot);
    }

    private const uint GaRoot = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);
}
