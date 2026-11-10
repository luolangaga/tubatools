using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal sealed record WindowsKeyboardFocusRead(bool? Value, string? Gap = null)
{
    internal static WindowsKeyboardFocusRead Read(Func<(bool Supported, bool Value)> read)
    {
        try
        {
            var (supported, value) = read();
            return supported ? new(value) : new(null, "keyboard_focus_unsupported");
        }
        catch { return new(null, "keyboard_focus_read_failed"); }
    }
}

internal sealed record CanonicalFocusMatch(int? Index, string? Gap = null);

// One focused object per sample supplies both cell eligibility and final
// binding. Reuse comparisons so blank cells need no second live comparison.
internal sealed class WindowsKeyboardFocusSample<T>(
    Func<T?> readFocus, Func<T, string> identity, Func<T, T, bool> compare, Action checkTime)
    where T : class
{
    private bool _read;
    private T? _focused;
    private string _identity = string.Empty;
    private readonly Dictionary<T, bool> _matches = new(ReferenceEqualityComparer.Instance);
    internal string? Gap { get; private set; }
    internal T? Focused { get { Capture(); return _focused; } }

    internal bool IsFocusedObject(T node, string nodeIdentity)
    {
        Capture();
        if (Gap is not null || _focused is null) { return false; }
        if (_matches.TryGetValue(node, out var match)) { return match; }
        try
        {
            checkTime();
            match = (_identity.Length == 0 || nodeIdentity.Length == 0 || _identity == nodeIdentity) &&
                compare(node, _focused);
            _matches.Add(node, match);
            return match;
        }
        catch (ObservationDeadlineExceededException) { Gap = "keyboard_focus_time_budget"; }
        catch { Gap = "keyboard_focus_read_failed"; }
        return false;
    }

    internal CanonicalFocusMatch Match<TNode>(
        IReadOnlyList<TNode> nodes, Func<TNode, T> element, Func<TNode, string> nodeIdentity)
    {
        Capture();
        if (Gap is not null || _focused is null) { return new(null, Gap); }
        var match = WindowsKeyboardFocus.Match(nodes, _identity, nodeIdentity,
            node => IsFocusedObject(element(node), nodeIdentity(node)), StillCurrent, checkTime);
        return Gap is null ? match : new(null, Gap);
    }

    private bool StillCurrent()
    {
        if (Gap is not null) { return false; }
        checkTime();
        var current = readFocus();
        return Gap is null && _focused is not null && current is not null && compare(_focused, current);
    }

    private void Capture()
    {
        if (_read) { return; }
        _read = true;
        try
        {
            checkTime();
            _focused = readFocus();
            if (_focused is not null) { _identity = identity(_focused); }
        }
        catch (ObservationDeadlineExceededException) { Gap = "keyboard_focus_time_budget"; }
        catch { Gap = "keyboard_focus_read_failed"; }
    }
}

internal static class WindowsKeyboardFocus
{
    internal static bool IsFocused(JsonObject record) =>
        (record["states"]?.GetValue<string>() ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("focused");

    // A native runtime identity narrows comparisons; it never substitutes
    // for an exact comparison with the object returned by FocusedElement.
    internal static CanonicalFocusMatch Match<T>(
        IReadOnlyList<T> nodes, string focusedIdentity, Func<T, string> identity,
        Func<T, bool> isFocusedObject, Func<bool> focusStillCurrent, Action checkTime)
    {
        int? match = null;
        for (var index = 0; index < nodes.Count; index++)
        {
            checkTime();
            var candidateIdentity = identity(nodes[index]);
            if (focusedIdentity.Length > 0 && candidateIdentity.Length > 0 &&
                focusedIdentity != candidateIdentity) { continue; }
            if (!isFocusedObject(nodes[index])) { continue; }
            if (match is not null) { return new(null, "focused_element_ambiguous"); }
            match = index;
        }
        if (match is not null)
        {
            checkTime();
            if (!focusStillCurrent()) { return new(null, "keyboard_focus_changed"); }
        }
        return new(match);
    }
}
