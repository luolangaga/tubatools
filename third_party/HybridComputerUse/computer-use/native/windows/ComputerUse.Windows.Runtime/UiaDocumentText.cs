using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3.Patterns;
using Interop.UIAutomationClient;

namespace ComputerUse.Windows.Runtime;

internal static class UiaDocumentText
{
    internal static DocumentContentProbe ProbeContent(
        UiaSnapshotCache cache, CachedUiaNode document, Action checkSnapshotTime)
    {
        var clock = Stopwatch.StartNew();
        void CheckTime()
        {
            checkSnapshotTime();
            if (clock.Elapsed >= TimeSpan.FromMilliseconds(200))
            {
                throw new ObservationDeadlineExceededException();
            }
        }
        return DocumentTextRead.ProbeContent(document,
            cache.ReadFirstChild, cache.ReadNextSibling, node =>
            {
                var facts = node.Facts ?? throw new InvalidOperationException("Document child facts are unavailable.");
                return facts.ControlType is ControlType.Table or ControlType.DataGrid ||
                    facts.ControlType == ControlType.Text &&
                        (!string.IsNullOrEmpty(facts.Name) || !string.IsNullOrEmpty(facts.ValueText)) ||
                    facts.ControlType is ControlType.Edit or ControlType.Document or ControlType.DataItem &&
                        !string.IsNullOrEmpty(facts.ValueText);
            }, CheckTime);
    }

    internal static DocumentTextRead ReadLegacy(
        AutomationElement document, int maxCharacters, Action checkSnapshotTime)
    {
        var clock = Stopwatch.StartNew();
        void CheckTime()
        {
            checkSnapshotTime();
            if (clock.Elapsed >= TimeSpan.FromSeconds(2))
            {
                throw new ObservationDeadlineExceededException();
            }
        }
        try
        {
            CheckTime();
            var pattern = document.Patterns.LegacyIAccessible.PatternOrDefault as LegacyIAccessiblePattern;
            if (pattern is null) { return new(null, "document_text_unavailable"); }
            CheckTime();
            var accessible = pattern.NativePattern.GetIAccessible();
            if (accessible is null) { return new(null, "document_text_unavailable"); }
            return DocumentTextRead.Collect(new Node(accessible, 0), ReadText,
                node => node.ChildId == 0 ? node.Accessible.accChildCount : 0,
                ReadChild, CheckTime, maxCharacters);
        }
        catch (ObservationDeadlineExceededException) { return new(null, "document_text_time_budget"); }
        catch { return new(null, "document_text_read_failed"); }

        DocumentTextContent ReadText(Node node)
        {
            CheckTime();
            if (node.Accessible.get_accRole(node.ChildId) is not int role)
            {
                throw new InvalidOperationException("Accessible content role is unavailable.");
            }
            // CLIENT and DOCUMENT may expose paragraph content as a value.
            // Only STATICTEXT and TEXT names also denote textual content.
            if (role is not (10 or 15 or 41 or 42)) { return new(null, IsTextBearing: false); }
            CheckTime();
            var value = node.Accessible.get_accValue(node.ChildId);
            if (!string.IsNullOrEmpty(value) || role is not (41 or 42)) { return new(value); }
            CheckTime();
            return new(node.Accessible.get_accName(node.ChildId) ?? value);
        }

        Node ReadChild(Node parent, int offset)
        {
            var children = new object[1];
            CheckTime();
            var result = AccessibleChildren(parent.Accessible, offset, 1, children, out var obtained);
            if (result < 0) { Marshal.ThrowExceptionForHR(result); }
            if (obtained != 1) { throw new InvalidOperationException("Accessible child disappeared while reading."); }
            return children[0] switch
            {
                IAccessible accessible => new(accessible, 0),
                int childId when childId > 0 => new(parent.Accessible, childId),
                _ => throw new InvalidOperationException("Accessible child identity is unavailable."),
            };
        }
    }

    private sealed record Node(IAccessible Accessible, int ChildId);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleChildren(IAccessible container, int start,
        int count, [Out] object[] children, out int obtained);
}
