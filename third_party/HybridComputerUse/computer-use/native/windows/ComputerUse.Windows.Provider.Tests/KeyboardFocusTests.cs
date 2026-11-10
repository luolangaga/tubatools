using ComputerUse.Windows.Runtime;
using System.Text.Json.Nodes;

internal static class KeyboardFocusTests
{
    internal static void Run()
    {
        Check(WindowsKeyboardFocus.IsFocused(new JsonObject { ["states"] = "enabled focused selected" }),
            "Spreadsheet reuse reads the canonical focus state after exact object binding.");
        Check(!WindowsKeyboardFocus.IsFocused(new JsonObject { ["states"] = "enabled unfocused" }) &&
            !WindowsKeyboardFocus.IsFocused(new JsonObject()),
            "Missing canonical focus or an unrelated state cannot restore an earlier cached focus flag.");
        Check(WindowsKeyboardFocusRead.Read(() => (true, false)) == new WindowsKeyboardFocusRead(false),
            "A supported false property remains a known false.");
        Check(WindowsKeyboardFocusRead.Read(() => (true, true)) == new WindowsKeyboardFocusRead(true),
            "A supported true property remains a known true.");
        Check(WindowsKeyboardFocusRead.Read(() => (false, true)) ==
            new WindowsKeyboardFocusRead(null, "keyboard_focus_unsupported"),
            "An unsupported out-value is not a focus fact.");
        Check(WindowsKeyboardFocusRead.Read(() => throw new InvalidOperationException()) ==
            new WindowsKeyboardFocusRead(null, "keyboard_focus_read_failed"),
            "A failed property read differs from unsupported and false.");

        var comparisons = new List<string>();
        var rows = new[] { "other-window", "focused", "sibling" };
        CanonicalFocusMatch Match(string focus, Func<string, bool> equal) =>
            WindowsKeyboardFocus.Match(rows, focus, node => node,
                node => { comparisons.Add(node); return equal(node); }, () => true, () => { });
        var result = Match("focused", node => node == "focused");
        Check(result.Index == 1 && result.Gap is null && comparisons.SequenceEqual(new[] { "focused" }),
            "Stable identities narrow work but the focused object still requires an exact comparison.");
        comparisons.Clear();
        Check(Match("focused", _ => false).Index is null && comparisons.Count == 1,
            "A matching runtime identity alone does not prove focus.");
        comparisons.Clear();
        Check(Match("foreign-window", _ => throw new InvalidOperationException()).Index is null && comparisons.Count == 0,
            "Focus outside the sampled window does not select a foreground or sibling node.");

        var unidentified = new[] { new object(), new object() };
        result = WindowsKeyboardFocus.Match(unidentified, "", _ => "",
            node => ReferenceEquals(node, unidentified[1]), () => true, () => { });
        Check(result.Index == 1 && result.Gap is null, "An object without a runtime ID needs an exact comparison.");
        result = WindowsKeyboardFocus.Match(unidentified, "", _ => "", _ => true,
            () => throw new InvalidOperationException("Ambiguous focus must not run confirmation."), () => { });
        Check(result.Index is null && result.Gap == "focused_element_ambiguous", "Multiple canonical matches must not publish arbitrary focus.");
        foreach (var laterFocus in new object?[] { null, new object() })
        {
            result = WindowsKeyboardFocus.Match(unidentified, "", _ => "",
                node => ReferenceEquals(node, unidentified[1]),
                () => laterFocus is not null && ReferenceEquals(unidentified[1], laterFocus), () => { });
            Check(result.Index is null && result.Gap == "keyboard_focus_changed",
                "Missing or changed focus during confirmation does not bind the earlier object.");
        }
        var readFailure = new InvalidOperationException("focus read failed");
        try
        {
            WindowsKeyboardFocus.Match(unidentified, "", _ => "",
                node => ReferenceEquals(node, unidentified[1]), () => throw readFailure, () => { });
            throw new InvalidOperationException("Focus read failure became a successful match.");
        }
        catch (InvalidOperationException error) when (ReferenceEquals(error, readFailure)) { }
        var deadline = new InvalidOperationException("deadline");
        try
        {
            WindowsKeyboardFocus.Match(unidentified, "", _ => "", _ => true, () => true, () => throw deadline);
            throw new InvalidOperationException("Focus ignored its sampling deadline.");
        }
        catch (InvalidOperationException error) when (ReferenceEquals(error, deadline)) { }
        CheckCellEligibility();
        Console.WriteLine("Keyboard focus identity and property tests passed.");
    }

    private static void CheckCellEligibility()
    {
        var blank = new JsonObject { ["value"] = "" };
        var valued = new JsonObject { ["value"] = "Data" };
        foreach (var property in new[]
                 {
                     new WindowsKeyboardFocusRead(false),
                     WindowsKeyboardFocusRead.Read(() => (false, false)),
                     WindowsKeyboardFocusRead.Read(() => throw new InvalidOperationException()),
                 })
        {
            var cells = new[] { new object(), new object(), new object() };
            var focusReads = 0;
            var comparisons = 0;
            var focus = new WindowsKeyboardFocusSample<object>(
                () => { focusReads++; return cells[1]; }, _ => "",
                (left, right) => { comparisons++; return ReferenceEquals(left, right); }, () => { });
            var retained = cells.Where(cell => SpreadsheetGrid.RetainCell(blank, property.Value == true,
                () => focus.IsFocusedObject(cell, ""))).ToArray();
            Check(retained.SequenceEqual(new[] { cells[1] }) && cells.Length - retained.Length == 2,
                "The actual focused blank is eligible despite false or unknown flags; only other blanks are summarized.");
            var match = focus.Match(retained, cell => cell, _ => "");
            Check(match.Index == 0 && match.Gap is null && focusReads == 2 && comparisons == 4,
                "Eligibility and binding share one focus read and cell comparisons, followed by one live confirmation.");
            Check(property.Value is not null || property.Gap is not null,
                "Exact focus does not discard unsupported or failed property evidence.");
        }
        Check(SpreadsheetGrid.RetainCell(valued, false,
                () => throw new InvalidOperationException("Valued cells need no focus eligibility call.")),
            "A valued cell remains eligible without any live focus lookup.");
        Check(SpreadsheetGrid.RetainCell(blank, true,
                () => throw new InvalidOperationException("A cached focus candidate is already eligible.")),
            "A cached focus candidate stays available for final exact binding.");

        var cellObject = new object();
        var otherWindow = new object();
        var foreignFocus = new WindowsKeyboardFocusSample<object>(() => otherWindow, _ => "foreign",
            (_, _) => throw new InvalidOperationException("Distinct identities must not be compared."), () => { });
        Check(!SpreadsheetGrid.RetainCell(blank, false, () => foreignFocus.IsFocusedObject(cellObject, "cell")),
            "Focus in another window does not retain a blank cell by geometry or a cached flag.");
        var collision = new WindowsKeyboardFocusSample<object>(() => otherWindow, _ => "same",
            ReferenceEquals, () => { });
        Check(!SpreadsheetGrid.RetainCell(blank, false, () => collision.IsFocusedObject(cellObject, "same")),
            "Equal runtime identities alone cannot retain a blank cell.");
        var missing = new WindowsKeyboardFocusSample<object>(() => null, _ => "", ReferenceEquals, () => { });
        Check(!SpreadsheetGrid.RetainCell(blank, false, () => missing.IsFocusedObject(cellObject, "")),
            "An absent focused object does not invent a focused cell.");

        foreach (var later in new object?[] { null, otherWindow })
        {
            var calls = 0;
            var changing = new WindowsKeyboardFocusSample<object>(() => calls++ == 0 ? cellObject : later,
                _ => "", ReferenceEquals, () => { });
            Check(SpreadsheetGrid.RetainCell(blank, false, () => changing.IsFocusedObject(cellObject, "")),
                "The earlier focused cell remains available for stability checking.");
            var match = changing.Match(new[] { cellObject }, node => node, _ => "");
            Check(match.Index is null && match.Gap == "keyboard_focus_changed",
                "Changed or lost focus leaves partial evidence and never binds the earlier blank cell.");
        }
        var ambiguous = new WindowsKeyboardFocusSample<object>(() => cellObject, _ => "", (_, _) => true, () => { });
        var duplicates = new[] { cellObject, otherWindow }.Where(node =>
            SpreadsheetGrid.RetainCell(blank, false, () => ambiguous.IsFocusedObject(node, ""))).ToArray();
        Check(duplicates.Length == 2 && ambiguous.Match(duplicates, node => node, _ => "").Gap == "focused_element_ambiguous",
            "Ambiguous retained blanks never receive an arbitrary focus binding.");
        var compareFailure = new WindowsKeyboardFocusSample<object>(() => cellObject, _ => "",
            (left, right) => ReferenceEquals(left, cellObject) ? true : throw new InvalidOperationException(), () => { });
        Check(SpreadsheetGrid.RetainCell(blank, false, () => compareFailure.IsFocusedObject(cellObject, "")) &&
            !SpreadsheetGrid.RetainCell(blank, false, () => compareFailure.IsFocusedObject(otherWindow, "")) &&
            compareFailure.Match(new[] { cellObject }, node => node, _ => "").Gap == "keyboard_focus_read_failed",
            "A later failed comparison keeps the earlier retained candidate from being published as certain focus.");
        var failed = new WindowsKeyboardFocusSample<object>(() => throw new InvalidOperationException(),
            _ => "", ReferenceEquals, () => { });
        Check(!SpreadsheetGrid.RetainCell(blank, false, () => failed.IsFocusedObject(cellObject, "")) &&
            failed.Match(Array.Empty<object>(), node => node, _ => "").Gap == "keyboard_focus_read_failed",
            "A failed focus read survives blank filtering as partial evidence.");
        var expired = new WindowsKeyboardFocusSample<object>(() => cellObject, _ => "", ReferenceEquals,
            () => throw new ObservationDeadlineExceededException());
        Check(!SpreadsheetGrid.RetainCell(blank, false, () => expired.IsFocusedObject(cellObject, "")) &&
            expired.Match(Array.Empty<object>(), node => node, _ => "").Gap == "keyboard_focus_time_budget",
            "An expired focus sample remains partial after blank filtering.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
