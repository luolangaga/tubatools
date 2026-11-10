using System.Text.Json.Nodes;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class UiaProvider
{
    private static Func<bool> PrepareSelection(
        AutomationElement? element,
        JsonObject input,
        out Action emit,
        out string route)
    {
        if (element is null)
        {
            throw new ProviderFailureException("semantic_target_not_found", "precondition",
                "select requires an exact target.");
        }
        var requested = OptionalString(input, "text", string.Empty);
        var text = element.Patterns.Text.PatternOrDefault;
        var item = element.Patterns.SelectionItem.PatternOrDefault;
        if (text is not null && (requested.Length > 0 || item is null))
        {
            if (text.SupportedTextSelection == SupportedTextSelection.None)
            {
                throw new ProviderFailureException("capability_unsupported", "precondition",
                    "The UIA text control does not support selection.");
            }
            var document = text.DocumentRange;
            var expected = UiaSelectionPolicy.RequireUniqueText(document.GetText(-1), requested);
            // UIA supplies the range; never turn UTF-16 offsets into provider
            // character offsets, which can split emoji or grapheme clusters.
            var range = requested.Length == 0
                ? document
                : document.FindText(requested, false, false);
            if (range is null || range.GetText(-1) != expected)
            {
                throw new ProviderFailureException("capability_unsupported", "precondition",
                    "UIA could not resolve the exact requested text range; no input was emitted.");
            }
            _ = text.GetSelection();
            route = "uia.text.select";
            emit = range.Select;
            return () =>
            {
                var selected = text.GetSelection();
                return selected.Length == 1 &&
                    SameTextRange(selected[0], range) &&
                    selected[0].GetText(-1) == expected;
            };
        }
        if (requested.Length > 0 || item is null)
        {
            throw new ProviderFailureException("capability_unsupported", "precondition",
                "The target does not expose the required UIA selection pattern.");
        }
        var container = item.SelectionContainer.Value;
        var selection = container?.Patterns.Selection.PatternOrDefault;
        if (selection is null)
        {
            throw new ProviderFailureException("capability_unsupported", "precondition",
                "The UIA selection container cannot report the complete selection.");
        }
        // Probe readback before changing selection. A provider that cannot
        // enumerate it cannot prove that selecting one item cleared others.
        _ = selection.Selection.Value;
        route = "uia.selection_item.select";
        emit = item.Select;
        return () =>
        {
            var selected = selection.Selection.Value;
            return selected.Length == 1 && selected[0].Equals(element) &&
                item.IsSelected.Value;
        };
    }

    private static bool SameTextRange(ITextRange left, ITextRange right)
    {
        return left.CompareEndpoints(TextPatternRangeEndpoint.Start,
                   right, TextPatternRangeEndpoint.Start) == 0 &&
            left.CompareEndpoints(TextPatternRangeEndpoint.End,
                right, TextPatternRangeEndpoint.End) == 0;
    }
}
