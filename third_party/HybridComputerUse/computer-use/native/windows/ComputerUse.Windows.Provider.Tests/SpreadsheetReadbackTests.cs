using System.Drawing;
using ComputerUse.Windows.Runtime;

internal static class SpreadsheetReadbackTests
{
    internal static void Run()
    {
        CheckPlan();
        CheckConfirms();
        CheckCursorCell();
        CheckCellLocalKeys();
    }

    // A 3x3 view at B2:D4 with 50x20 cells. A read after an action probes
    // the first and last cell in view, then each requested cell once.
    private static void CheckPlan()
    {
        var cells = new Dictionary<(int Row, int Column), Rectangle>();
        for (var row = 1; row <= 3; row++)
        {
            for (var column = 1; column <= 3; column++)
            {
                cells[(row, column)] = new Rectangle(100 + (column - 1) * 50, 200 + (row - 1) * 20, 50, 20);
            }
        }
        // The last column is cut by the area edge.
        var layout = new SpreadsheetLayout(new Rectangle(100, 200, 130, 60), 1, 3, 1, 3, cells);
        var plan = SpreadsheetReadback.Plan(layout, new[] { (2, 2), (1, 1), (2, 2), (3, 1) })
            ?? throw new InvalidOperationException("plan");
        Check(plan.Select(probe => (probe.Row, probe.Column)).SequenceEqual(new[] { (1, 1), (3, 3), (2, 2), (3, 1) }),
            "First and last cell come first and each cell is probed once: " +
            string.Join(' ', plan.Select(probe => SpreadsheetGrid.A1(probe.Row, probe.Column))));
        Check(plan.All(probe => probe.Bounds.Contains(probe.Point) && layout.Area.Contains(probe.Point)),
            "Each probe hits its cell inside the area.");
        Check(plan.Single(probe => probe.Row == 3 && probe.Column == 3).Point == new Point(215, 250),
            "A cut cell is probed in its visible part.");
        Check(SpreadsheetReadback.Plan(layout, new[] { (2, 2), (9, 1) }) is null,
            "A cell out of view needs a full read.");
    }

    private static void CheckConfirms()
    {
        var probe = new SpreadsheetProbe(1, 2, new Point(5, 5), new Rectangle(0, 0, 10, 10));
        Check(SpreadsheetReadback.Confirms(probe, new SpreadsheetCellHit<string>("C2", 1, 2, new Rectangle(0, 0, 10, 10))),
            "The same cell at the same rectangle confirms.");
        Check(!SpreadsheetReadback.Confirms(probe, new SpreadsheetCellHit<string>("C3", 2, 2, new Rectangle(0, 0, 10, 10))),
            "Another cell under the point (scrolled) does not confirm.");
        Check(!SpreadsheetReadback.Confirms(probe, new SpreadsheetCellHit<string>("C2", 1, 2, new Rectangle(0, 0, 12, 10))),
            "A resized cell does not confirm.");
        Check(!SpreadsheetReadback.Confirms<string>(probe, null), "No cell does not confirm.");
    }

    private static void CheckCursorCell()
    {
        Check(SpreadsheetReadback.CursorCell("C4") == (3, 2), "The Name Box names the cursor cell.");
        foreach (var name in new[] { "A1:B3", "Sales", "", null })
        {
            Check(SpreadsheetReadback.CursorCell(name) is null, $"'{name}' is not one cell.");
        }
    }

    private static void CheckCellLocalKeys()
    {
        foreach (var key in new[] { "ENTER", "Return", "TAB", "SHIFT+TAB", "SHIFT+ENTER", "ESCAPE", "BACKSPACE",
                     "DELETE", "F2", "DOWN", "ArrowLeft", "HOME", "END", "PAGE_DOWN", "a", "7", "SHIFT+a", "SPACE", "-" })
        {
            Check(SpreadsheetReadback.IsCellLocalKey(key), $"{key} changes at most the cursor cell.");
        }
        foreach (var key in new[] { "CTRL+V", "CTRL+Z", "ALT+DOWN", "CTRL+HOME", "SHIFT+CTRL+DOWN", "F5", "F12",
                     "SHIFT", "WIN", "", "CTRL+", "NOPE" })
        {
            Check(!SpreadsheetReadback.IsCellLocalKey(key), $"{key} may change more than the cursor cell.");
        }
        foreach (var key in new[] { "ENTER", "SHIFT+TAB", "UP", "PAGEDOWN", "HOME" })
        {
            Check(SpreadsheetReadback.IsCursorKey(key), $"{key} moves the cursor.");
        }
        foreach (var key in new[] { "ESCAPE", "F2", "DELETE", "a", "CTRL+DOWN" })
        {
            Check(!SpreadsheetReadback.IsCursorKey(key), $"{key} does not move the cursor.");
        }
        var from = (Row: 2, Column: 2);
        foreach (var (key, to) in new (string, (int, int)?)[]
                 {
                     ("ENTER", (3, 2)), ("SHIFT+ENTER", (1, 2)), ("TAB", (2, 3)), ("SHIFT+TAB", (2, 1)),
                     ("DOWN", (3, 2)), ("UP", (1, 2)), ("LEFT", (2, 1)), ("RIGHT", (2, 3)), ("HOME", (2, 0)),
                     ("END", null), ("PAGEDOWN", null), ("SHIFT+DOWN", null), ("F2", null), ("a", null),
                 })
        {
            Check(SpreadsheetReadback.PredictCursor(from, key) == to, $"{key} from C3 goes to {to}.");
        }
        Check(SpreadsheetReadback.PredictCursor((0, 0), "UP") is null, "UP from A1 leaves the sheet.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("SpreadsheetReadbackTests: " + message); }
    }
}
