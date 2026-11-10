using System.Diagnostics;
using System.Drawing;
using System.Text.Json.Nodes;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace ComputerUse.Windows.Runtime;

// A full read of a LibreOffice Calc window costs about two seconds, most of
// it one hit test per cell in view. After an action that can change only
// the cell under the cursor (a click or value on a cell, or text or an edit
// or cursor key while the sheet has focus) the window is read again in part:
//
// - the window roots, by element;
// - the formula bar, which holds the Name Box, and the sheet's parent, which
//   holds the sheet and the editor of a cell being edited, walked again;
// - the cells that showed a value or the cursor, the acted-on cell and the
//   cell the cursor moved to, by hit test. Hits at the first and last cell
//   in view confirm that no cell moved.
//
// The other records, empty cells included, are copied from the full read,
// and the response says so. Anything unexpected (another window, a moved
// cell, a cursor in an unread cell, a failed read, a changed observation
// budget, other input since the read) makes the caller read the whole
// window instead.
internal sealed partial class UiaProvider
{
    private sealed record SpreadsheetRead(
        CachedUiaNode Table,
        int[] TablePath,
        int TableDepth,
        SpreadsheetLayout Layout,
        IReadOnlySet<(int Row, int Column)> Listed,
        bool Complete);

    // The latest read of a window with one sheet, kept for one window at a
    // time like the element leases. Records are copies: the published ones
    // belong to the response. Cursors are the cells that may hold the
    // cursor; Cursor is the one known to hold it, if any.
    private sealed record SpreadsheetWindowRead(
        int ProcessId,
        string ProcessGeneration,
        long WindowHandle,
        string WindowSignature,
        long InputGeneration,
        int MaxNodes,
        int MaxDepth,
        IReadOnlyList<(CachedUiaNode Node, JsonObject Record, int[] Path, int Depth)> Nodes,
        SpreadsheetRead Sheet,
        int[] FormulaBarPath,
        IReadOnlyCollection<(int Row, int Column)> Cursors,
        (int Row, int Column)? Cursor,
        bool AfterCellAction)
    {
        internal int[] SheetParentPath => Sheet.TablePath[..^1];

        // Whether the sheet, a cell or a cell's editor had the keyboard
        // focus. LibreOffice 24.8 focuses the cursor cell, 25.2 the sheet.
        internal bool SheetFocused => Nodes.Any(node =>
            IsUnder(node.Path, SheetParentPath) && node.Path.Length > SheetParentPath.Length &&
            WindowsKeyboardFocus.IsFocused(node.Record));
    }

    private SpreadsheetWindowRead? _spreadsheetRead;

    internal const string SpreadsheetReadbackNote =
        "Read again after this action: the window, the formula bar and the " +
        "sheet cells that hold a value or the cursor. Other controls and the " +
        "empty cells are as last observed; observe reads them again.";

    private static readonly TimeSpan SpreadsheetCursorSettleTime = TimeSpan.FromMilliseconds(300);

    private void ForgetSpreadsheetRead() => _spreadsheetRead = null;

    // Keeps a full read for TryReadSpreadsheetBack when it read exactly one
    // sheet completely, nothing else is missing, and the window shows one
    // Name Box: a combo box outside the sheet named by a cell reference.
    private void RememberSpreadsheetRead(
        Process process,
        NativeDesktop.WindowRecord selectedWindow,
        NativeWindowProjection projection,
        IReadOnlyList<(CachedUiaNode Node, JsonObject Record, int[] Path, int Depth)> sampled,
        JsonArray annotations,
        SpreadsheetRead? sheet,
        int maxNodes,
        int maxDepth)
    {
        _spreadsheetRead = null;
        if (sheet is not { Complete: true } || sheet.TablePath.Length < 3) { return; }
        var tableId = PathJson(sheet.TablePath).ToJsonString();
        if (annotations.Any(annotation =>
                annotation?["kind"]?.GetValue<string>() != "focus_evidence" &&
                (annotation?["kind"]?.GetValue<string>() != "cell_summary" ||
                 annotation["parentRuntimeId"]?.ToJsonString() != tableId)))
        {
            return;
        }
        var nameBoxes = sampled
            .Where(node => !IsUnder(node.Path, sheet.TablePath) &&
                node.Node.Facts is { ControlType: ControlType.ComboBox } facts &&
                SpreadsheetReadback.CursorCell(facts.Name) is not null)
            .ToArray();
        if (nameBoxes.Length != 1) { return; }
        var formulaBar = nameBoxes[0].Path[..^1];
        var sheetParent = sheet.TablePath[..^1];
        // Both are walked again, so neither may be a root or hold the other.
        if (formulaBar.Length < 2 ||
            IsUnder(formulaBar, sheetParent) || IsUnder(sheetParent, formulaBar) ||
            !sampled.Any(node => node.Path.SequenceEqual(formulaBar)) ||
            !sampled.Any(node => node.Path.SequenceEqual(sheetParent)))
        {
            return;
        }
        var named = SpreadsheetReadback.CursorCell(nameBoxes[0].Node.Facts!.Name)!.Value;
        var focused = FocusedCells(sampled, sheet.TablePath);
        _spreadsheetRead = new SpreadsheetWindowRead(
            process.Id,
            ProcessGeneration(process),
            selectedWindow.Handle.ToInt64(),
            WindowSignature(projection),
            NativeDesktop.InputGeneration,
            maxNodes,
            maxDepth,
            CopyNodes(sampled),
            sheet,
            formulaBar,
            focused.Append(named).ToHashSet(),
            focused.Length == 1 ? focused[0] : named,
            AfterCellAction: false);
    }

    // Whether an action can change at most the cell under the cursor of the
    // sheet that the latest read of its window holds, which cell it targets,
    // and which cursor key it presses. Checked before the input, so that no
    // other input came between the read and the action.
    private bool SpreadsheetCellAction(
        Process process,
        NativeDesktop.WindowRecord selectedWindow,
        string intent,
        JsonObject input,
        JsonObject? target,
        JsonObject? elementRecord,
        AutomationElement? element,
        out (int Row, int Column)? cell,
        out string? cursorKey)
    {
        cell = null;
        cursorKey = null;
        var read = _spreadsheetRead;
        if (read is null ||
            read.ProcessId != process.Id ||
            read.ProcessGeneration != ProcessGeneration(process) ||
            read.InputGeneration != NativeDesktop.InputGeneration ||
            ProjectNativeWindows(process, NativeDesktop.ListWindows(process.Id))
                .LogicalRecord(selectedWindow).Handle.ToInt64() != read.WindowHandle)
        {
            return false;
        }
        var key = OptionalString(input, "key", string.Empty);
        var allowed = intent switch
        {
            "keypress" => SpreadsheetReadback.IsCellLocalKey(key),
            "type" => true,
            "click" or "set_value" => element is not null,
            _ => false,
        };
        if (!allowed) { return false; }
        if (element is null)
        {
            // Keys and text go to the focus, which the latest read saw in
            // the sheet; no input came since.
            cursorKey = intent == "keypress" && SpreadsheetReadback.IsCursorKey(key) ? key : null;
            return read.SheetFocused;
        }
        var path = OptionalString(
            target, "providerObjectPath",
            OptionalString(elementRecord, "providerObjectPath", string.Empty));
        var scope = SpreadsheetCellScope + ":";
        if (!path.StartsWith(scope, StringComparison.Ordinal)) { return false; }
        var parts = path[scope.Length..].Split(':', 2);
        if (parts.Length != 2 ||
            parts[1] != read.Sheet.Table.Facts?.Name ||
            !SpreadsheetGrid.TryParseA1(parts[0], out var row, out var column))
        {
            return false;
        }
        cell = (row, column);
        return true;
    }

    // The window read again after a cell action, or null when only a full
    // read can describe it. A recheck (the core's second read when the first
    // showed no change) applies only right after such a read, with no input
    // since.
    private JsonObject? TryReadSpreadsheetBack(
        SpreadsheetWindowRead? read,
        Process process,
        NativeDesktop.WindowRecord window,
        JsonObject? observation,
        (int Row, int Column)? target,
        string? cursorKey = null,
        bool recheck = false)
    {
        _spreadsheetRead = null;
        if (read is null ||
            read.ProcessId != process.Id ||
            read.ProcessGeneration != ProcessGeneration(process) ||
            (recheck && (!read.AfterCellAction || read.InputGeneration != NativeDesktop.InputGeneration)) ||
            OptionalInt(observation, "max_tree_nodes", DefaultNodeLimit) != read.MaxNodes ||
            OptionalInt(observation, "max_tree_depth", DefaultDepthLimit) != read.MaxDepth)
        {
            return null;
        }
        var projection = ProjectNativeWindows(process, NativeDesktop.ListWindows(process.Id));
        var selected = projection.LogicalRecord(window);
        if (selected.Handle.ToInt64() != read.WindowHandle ||
            WindowSignature(projection) != read.WindowSignature)
        {
            return null;
        }
        var sheet = read.Sheet;
        var tableName = sheet.Table.Facts?.Name ?? string.Empty;
        var sheetParent = read.SheetParentPath;
        var processGeneration = ProcessGeneration(process);
        var selectedHandle = OpaqueWindowHandle(process, selected.Handle);
        var cache = new UiaSnapshotCache(_automation);
        var readClock = Stopwatch.StartNew();
        var readBudget = new OwnershipProbeBudget(read.MaxNodes, SnapshotSampleTimeout, () => readClock.Elapsed);
        var annotations = new JsonArray();
        var nodes = new List<(CachedUiaNode Node, JsonObject Record, int[] Path, int Depth)>();
        var listed = new HashSet<(int Row, int Column)>();
        var cursors = new HashSet<(int Row, int Column)>();
        var focus = FocusSample(readBudget.CheckTime);
        try
        {
            using (WindowsUiaAutomationFactory.SnapshotReadTimeout(_automation))
            {
                // Where the cursor should be: the acted-on cell, or where a
                // cursor key moves it.
                var expected = target ??
                    (cursorKey is not null && read.Cursor is { } from
                        ? SpreadsheetReadback.PredictCursor(from, cursorKey)
                        : null);
                if (expected is not null || cursorKey is not null)
                {
                    SettleSpreadsheetCursor(read, cache, expected);
                }
                foreach (var node in read.Nodes)
                {
                    var walked = IsUnder(node.Path, read.FormulaBarPath) || IsUnder(node.Path, sheetParent);
                    if (walked && !node.Path.SequenceEqual(read.FormulaBarPath) &&
                        !node.Path.SequenceEqual(sheetParent))
                    {
                        continue;
                    }
                    if (walked)
                    {
                        var fresh = cache.ReadRoot(node.Node.Element);
                        if (!Walk(fresh, node.Path, node.Depth)) { return null; }
                        continue;
                    }
                    if (node.Path.Length == 1)
                    {
                        var root = cache.ReadRoot(node.Node.Element);
                        if (root.Facts is not { } rootFacts ||
                            rootFacts.ControlType != node.Node.Facts?.ControlType)
                        {
                            return null;
                        }
                        // The first record names the window by its title.
                        nodes.Add((root, ElementJson(
                            root.Element, rootFacts,
                            node.Record["index"]?.GetValue<int>() == 0 ? 0 : 1,
                            processGeneration, process, selected, selectedHandle, node.Path,
                            RequireCompleteRecord, readBudget.CheckTime,
                            () => UiaDocumentText.ProbeContent(cache, root, readBudget.CheckTime)),
                            node.Path, node.Depth));
                        continue;
                    }
                    // A copied control that held the focus may have lost it
                    // to the sheet: only a full read can say. The sheet's
                    // ancestors keep it while the sheet has it.
                    if (WindowsKeyboardFocus.IsFocused(node.Record) &&
                        !IsUnder(sheet.TablePath, node.Path))
                    {
                        return null;
                    }
                    nodes.Add((node.Node, (JsonObject)node.Record.DeepClone(), node.Path, node.Depth));
                }
                if (!nodes.Any(node => node.Path.SequenceEqual(sheet.TablePath))) { return null; }

                // LibreOffice 25.2 names the cursor cell in the Name Box;
                // 24.8 leaves the name as it was and focuses the cell. A
                // selected range acts on more than one cell.
                foreach (var node in nodes.Where(node =>
                             IsUnder(node.Path, read.FormulaBarPath) &&
                             node.Node.Facts?.ControlType == ControlType.ComboBox))
                {
                    var name = node.Node.Facts!.Name;
                    if (SpreadsheetReadback.CursorCell(name) is { } named) { cursors.Add(named); }
                    else if (name.Contains(':')) { return null; }
                }
                if (expected is { } expectedCell) { cursors.Add(expectedCell); }
                var probes = SpreadsheetReadback.Plan(
                    sheet.Layout, sheet.Listed.Concat(read.Cursors).Concat(cursors));
                if (probes is null) { return null; }
                foreach (var probe in probes)
                {
                    if (!SpreadsheetGrid.PointShowsWindow(probe.Point, selected.Handle)) { return null; }
                    var hitNode = cache.ReadAtPoint(probe.Point, sheet.Table);
                    var hit = CellAt(hitNode);
                    if (!SpreadsheetReadback.Confirms(probe, hit))
                    {
                        // The editor of a cell being edited covers the cell.
                        // Read the cell itself if a read listed it, or keep
                        // it empty; anything else means the sheet changed.
                        if (hit is not null || hitNode?.Facts is not { } covering ||
                            !Rectangle.Inflate(probe.Bounds, 1, 1).Contains(covering.Bounds))
                        {
                            return null;
                        }
                        var previous = read.Nodes.FirstOrDefault(node =>
                            node.Path.Length == sheet.TablePath.Length + 2 &&
                            IsUnder(node.Path, sheet.TablePath) &&
                            node.Path[^2] == probe.Row && node.Path[^1] == probe.Column);
                        if (previous.Node is null) { continue; }
                        hit = CellAt(cache.ReadRoot(previous.Node.Element));
                        if (!SpreadsheetReadback.Confirms(probe, hit)) { return null; }
                    }
                    int[] cellPath = [.. sheet.TablePath, probe.Row, probe.Column];
                    var record = SpreadsheetCellRecord(
                        hit!, tableName, cellPath, 1, processGeneration,
                        process, selected, selectedHandle, RequireCompleteRecord, readBudget.CheckTime, focus);
                    if (record is null) { continue; }
                    nodes.Add((hit!.Cell, record, cellPath, sheet.TableDepth + 1));
                    listed.Add((probe.Row, probe.Column));
                }
                var ownershipBudget = new OwnershipProbeBudget(
                    checked(read.MaxNodes * 4 + 256), SnapshotSampleTimeout, () => readClock.Elapsed);
                var boundary = CreateLogicalBoundary(process, projection, ownershipBudget);
                var focusIncomplete = false;
                BindCurrentFocus(nodes, focus, annotations, _ => focusIncomplete = true,
                    readBudget.CheckTime, boundary, selected.Handle.ToInt64());
                if (focusIncomplete) { return null; }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(
                "[provider] spreadsheet read-back failed; reading the whole window: " +
                $"{error.GetType().Name} HRESULT=0x{error.HResult:x8}");
            return null;
        }
        var focused = FocusedCells(nodes, sheet.TablePath);
        var updated = read with
        {
            InputGeneration = NativeDesktop.InputGeneration,
            Nodes = CopyNodes(nodes),
            Sheet = sheet with { Listed = listed },
            Cursors = cursors.Concat(focused).ToHashSet(),
            Cursor = focused.Length == 1 ? focused[0] : cursors.Count == 1 ? cursors.First() : null,
            AfterCellAction = true,
        };
        // Where the sheet had the focus, it must still have it: on a cell
        // that was read (24.8), on the editor of a cell, or on the sheet
        // (25.2). Otherwise the cursor moved to a cell that was not read.
        if (nodes.Count > read.MaxNodes || (read.SheetFocused && !updated.SheetFocused)) { return null; }
        var empty = sheet.Layout.Cells.Count - listed.Count;
        if (empty > 0)
        {
            annotations.Add(CellSummaryAnnotation(sheet.TablePath, empty, listed.Count, sheet.Layout));
        }
        var snapshot = PublishSnapshot(process, selected, projection, nodes, annotations);
        _spreadsheetRead = updated;
        return snapshot;

        // Reads a node and its subtree again, as the full read walked it.
        // The sheet's cells are read by hit test above, not walked. A node
        // whose ownership is not inherited from its parent (a native child
        // window) needs the full read's ownership probe.
        bool Walk(CachedUiaNode node, int[] path, int depth)
        {
            if (node.Facts is not { } facts || nodes.Count >= read.MaxNodes || depth >= read.MaxDepth)
            {
                return false;
            }
            nodes.Add((node, ElementJson(
                node.Element, facts, 1, processGeneration, process, selected, selectedHandle, path,
                RequireCompleteRecord, readBudget.CheckTime,
                () => UiaDocumentText.ProbeContent(cache, node, readBudget.CheckTime)),
                path, depth));
            if (path.SequenceEqual(sheet.TablePath))
            {
                return facts.ControlType == sheet.Table.Facts?.ControlType &&
                    facts.Name == tableName &&
                    facts.Bounds == sheet.Table.Facts?.Bounds;
            }
            // Never walk a table's children: a sheet lists every cell of the
            // sheet, and a moved or second sheet needs the full read.
            if (facts.ControlType is ControlType.Table or ControlType.DataGrid) { return false; }
            var index = 0;
            for (var child = cache.ReadFirstChild(node); child is not null; child = cache.ReadNextSibling(child))
            {
                if (child.Facts is not { } childFacts ||
                    InheritedWindowOwnership.FromParent(
                        true,
                        childFacts.NativeWindowHandle,
                        childFacts.Window || childFacts.ControlType == ControlType.Window) != true ||
                    !Walk(child, [.. path, index], depth + 1))
                {
                    return false;
                }
                index++;
            }
            return true;
        }

        // Partial readback reuses a complete earlier observation. If a new
        // record has an acquisition gap, the full read must publish that
        // gap instead of caching the incomplete record as complete.
        static void RequireCompleteRecord(string reason) =>
            throw new WindowOwnershipUnavailableException(
                "Spreadsheet read-back record is incomplete: " + reason);

        static SpreadsheetCellHit<CachedUiaNode>? CellAt(CachedUiaNode? cell) =>
            cell?.Facts is { ControlType: ControlType.DataItem } facts &&
                SpreadsheetGrid.TryParseA1(facts.Name, out var row, out var column)
                ? new SpreadsheetCellHit<CachedUiaNode>(cell, row, column, facts.Bounds)
                : null;
    }

    // LibreOffice can answer an accessibility call before it handles input
    // that is still queued, so a read right after a click on a cell or a
    // cursor key may see the cursor where it was. Waits, within a bound,
    // until the Name Box names the expected cell (25.2) or that cell has the
    // focus (24.8); without an expected cell, until the Name Box names a
    // cell that did not hold the cursor. When the bound ends first (the
    // cursor stayed at the edge of the sheet, or moved elsewhere), the read
    // goes on and its focus check decides.
    private void SettleSpreadsheetCursor(
        SpreadsheetWindowRead read,
        UiaSnapshotCache cache,
        (int Row, int Column)? expected)
    {
        var nameBox = read.Nodes.FirstOrDefault(node =>
            IsUnder(node.Path, read.FormulaBarPath) &&
            node.Node.Facts?.ControlType == ControlType.ComboBox).Node;
        var probe = expected is { } cell && read.Sheet.Layout.Cells.TryGetValue(cell, out var bounds)
            ? SpreadsheetReadback.ProbePoint(bounds, read.Sheet.Layout.Area)
            : (Point?)null;
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var named = nameBox is null
                ? null
                : SpreadsheetReadback.CursorCell(
                    Safe<string?>(() => cache.ReadRoot(nameBox.Element).Facts?.Name, null));
            if (expected is null
                    ? named is { } moved && !read.Cursors.Contains(moved)
                    : named == expected ||
                        probe is { } point &&
                        Safe<UiaNodeFacts?>(() => cache.ReadAtPoint(point, read.Sheet.Table)?.Facts, null) is
                            { HasKeyboardFocus: true } facts &&
                        SpreadsheetReadback.CursorCell(facts.Name) == expected)
            {
                return;
            }
            if (clock.Elapsed >= SpreadsheetCursorSettleTime) { return; }
            Thread.Sleep(20);
        }
    }

    private static (int Row, int Column)[] FocusedCells(
        IEnumerable<(CachedUiaNode Node, JsonObject Record, int[] Path, int Depth)> nodes,
        int[] tablePath) =>
        nodes
            .Where(node => node.Path.Length == tablePath.Length + 2 &&
                IsUnder(node.Path, tablePath) &&
                WindowsKeyboardFocus.IsFocused(node.Record))
            .Select(node => (node.Path[^2], node.Path[^1]))
            .ToArray();

    private static (CachedUiaNode Node, JsonObject Record, int[] Path, int Depth)[] CopyNodes(
        IEnumerable<(CachedUiaNode Node, JsonObject Record, int[] Path, int Depth)> nodes) =>
        nodes
            .Select(node => (node.Node, (JsonObject)node.Record.DeepClone(), node.Path, node.Depth))
            .ToArray();

    // The windows of the process as a full read lists them. Any change (a
    // dialog, a popup, a tooltip, a moved window) needs a full read.
    private static string WindowSignature(NativeWindowProjection projection) =>
        string.Join('\u001e', projection.NativeWindows.Select(window =>
            string.Join('\u001f',
                window.Handle.ToInt64(), window.OwnerHandle.ToInt64(), window.Title,
                window.Bounds, window.Active, window.Cloaked)));

    private static bool IsUnder(int[] path, int[] prefix) =>
        path.Length >= prefix.Length && path.AsSpan(0, prefix.Length).SequenceEqual(prefix);
}
