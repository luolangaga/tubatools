using System.Diagnostics;
using System.Text.Json.Nodes;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class UiaProvider
{
    // A spreadsheet cell is found again by a hit test at its rectangle in
    // the latest observation of the window. If the sheet scrolled or a
    // window covers the cell, the hit returns another element and the
    // reference is stale.
    private AutomationElement ResolveSpreadsheetCell(
        Process process,
        NativeDesktop.WindowRecord selectedWindow,
        string providerObjectPath,
        JsonObject? target,
        JsonObject? record)
    {
        var logicalWindow = ProjectNativeWindows(
            process,
            NativeDesktop.ListWindows(process.Id))
            .LogicalRecord(selectedWindow);
        if (!_observedElementLeases.TryGet(
                process.Id,
                ProcessGeneration(process),
                logicalWindow.Handle.ToInt64(),
                providerObjectPath,
                out var lease) ||
            lease?.CanonicalRecord["frame"] is not JsonObject frame ||
            !SpreadsheetGrid.TryParseA1(
                lease.CanonicalRecord["name"]?.GetValue<string>(), out var row, out var column))
        {
            throw new ProviderFailureException(
                "stale_element_ref",
                "precondition",
                "The cell is not in the latest observation of this window; no input was emitted.");
        }
        var center = new System.Drawing.Point(
            Safe(() => frame["x"]!.GetValue<int>() + frame["width"]!.GetValue<int>() / 2, int.MinValue),
            Safe(() => frame["y"]!.GetValue<int>() + frame["height"]!.GetValue<int>() / 2, int.MinValue));
        AutomationElement? hit = null;
        if (center.X != int.MinValue && center.Y != int.MinValue &&
            SpreadsheetGrid.PointShowsWindow(center, logicalWindow.Handle))
        {
            using (FlaUI.Core.CacheRequest.ForceNoCache())
            {
                hit = Safe<AutomationElement?>(() => _automation.FromPoint(center), null);
            }
        }
        if (hit is null ||
            !SpreadsheetGrid.TryParseA1(Safe(() => hit.Name, string.Empty), out var hitRow, out var hitColumn) ||
            hitRow != row ||
            hitColumn != column)
        {
            throw new ProviderFailureException(
                "stale_element_ref",
                "precondition",
                "The cell is no longer at its observed position; no input was emitted.");
        }
        EnsureTargetStillMatches(hit, target, record);
        return hit;
    }

    private AutomationElement ResolveSnapshotPath(
        IReadOnlyList<AutomationElement> roots,
        JsonObject? target,
        JsonObject? record)
    {
        var source = target?["runtimeId"] ?? record?["runtimeId"];
        if (source is not JsonArray path || path.Count == 0)
        {
            throw new ProviderFailureException(
                "stale_element_ref",
                "precondition",
                "The snapshot-only target has no tree path; no input was emitted.");
        }
        var indices = new List<int>(path.Count);
        foreach (var node in path)
        {
            if (node is not JsonValue value ||
                !value.TryGetValue<int>(out var index) ||
                index < 0)
            {
                throw new ProviderFailureException(
                    "stale_element_ref",
                    "precondition",
                    "The snapshot-only target tree path is invalid; no input was emitted.");
            }
            indices.Add(index);
        }
        if (indices[0] < 0 || indices[0] >= roots.Count)
        {
            throw new ProviderFailureException(
                "stale_element_ref",
                "precondition",
                "The snapshot-only target tree path is not rooted; no input was emitted.");
        }
        var current = roots[indices[0]];
        var controlWalker =
            _automation.TreeWalkerFactory.GetControlViewWalker();
        for (var depth = 1; depth < indices.Count; depth++)
        {
            var childIndex = indices[depth];
            if (childIndex >= DefaultNodeLimit)
            {
                throw new ProviderFailureException(
                    "stale_element_ref",
                    "precondition",
                    "The snapshot-only target tree path exceeds the bounded child budget; no input was emitted.");
            }
            AutomationElement[] children;
            try
            {
                children = ReadControlChildren(
                    current,
                    controlWalker,
                    childIndex + 1);
            }
            catch
            {
                throw new ProviderFailureException(
                    "stale_element_ref",
                    "precondition",
                    "The snapshot-only target tree path is no longer readable; no input was emitted.");
            }
            if (childIndex >= children.Length)
            {
                throw new ProviderFailureException(
                    "stale_element_ref",
                    "precondition",
                    "The snapshot-only target tree path no longer exists; no input was emitted.");
            }
            current = children[childIndex];
        }
        EnsureTargetStillMatches(current, target, record);
        return current;
    }

    private JsonObject ResolvedElementJson(
        AutomationElement root,
        AutomationElement resolved,
        Process process,
        NativeDesktop.WindowRecord window)
    {
        var requestedObjectPath = RuntimeObjectPath(resolved);
        if (string.IsNullOrEmpty(requestedObjectPath))
        {
            throw new ProviderFailureException(
                "semantic_target_not_found",
                "observation",
                "The resolved target has no provider-native identity.");
        }
        var snapshot = BuildSnapshot(process, window, null);
        var elements = snapshot["elements"] as JsonArray ?? new JsonArray();
        var matches = elements
            .OfType<JsonObject>()
            .Where(record => string.Equals(
                record["providerObjectPath"]?.GetValue<string>(),
                requestedObjectPath,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            throw new ProviderFailureException(
                "semantic_target_not_found",
                "observation",
                "The resolved target disappeared before it could be sampled.");
        }
        if (matches.Length != 1)
        {
            throw new ProviderFailureException(
                "semantic_target_ambiguous",
                "observation",
                "The resolved provider-native target identity is not unique.");
        }
        return (JsonObject)matches[0].DeepClone();
    }

    private AutomationElement ResolveProviderObjectPath(
        IReadOnlyList<AutomationElement> roots,
        string requestedProviderObjectPath,
        Action<AutomationElement>? validate = null)
    {
        var matches = new List<AutomationElement>();
        _ = VisitResolutionCandidates(
            roots,
            element =>
            {
                var objectPath = RuntimeObjectPath(element);
                if (!string.IsNullOrEmpty(objectPath) &&
                    string.Equals(
                        objectPath,
                        requestedProviderObjectPath,
                        StringComparison.Ordinal))
                {
                    validate?.Invoke(element);
                    matches.Add(element);
                }
                return matches.Count < 2;
            });
        if (matches.Count == 0)
        {
            throw new ProviderFailureException(
                "stale_element_ref",
                "precondition",
                "The provider-native target identity is stale; no input was emitted.");
        }
        if (matches.Count != 1)
        {
            throw new ProviderFailureException(
                "semantic_target_ambiguous",
                "precondition",
                "The provider-native target identity is not unique; no input was emitted.");
        }
        return matches[0];
    }

    private static string RuntimeObjectPath(AutomationElement element)
    {
        var runtimeId = Safe(
            () => element.Properties.RuntimeId.ValueOrDefault ?? Array.Empty<int>(),
            Array.Empty<int>());
        return runtimeId.Length == 0 ? string.Empty : RuntimeObjectPath(runtimeId);
    }

    private static string RuntimeObjectPath(IReadOnlyList<int> runtimeId)
    {
        return "runtime:" + string.Join('.', runtimeId);
    }
}
