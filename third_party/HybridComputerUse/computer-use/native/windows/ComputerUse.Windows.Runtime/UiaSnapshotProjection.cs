using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class UiaProvider
{
    private static JsonObject BuildWindowlessSnapshot(
        JsonObject app,
        string closedWindowHandle)
    {
        return new JsonObject
        {
            ["app"] = app.DeepClone(),
            ["windowAbsent"] = true,
            ["closedWindowHandle"] = closedWindowHandle,
            ["windows"] = new JsonArray(),
            ["snapshotId"] = Guid.NewGuid().ToString("N"),
            ["sampledAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["treeLines"] = new JsonArray(
                "No top-level AX windows remain for this application after close."),
            ["elements"] = new JsonArray(),
        };
    }

    private JsonObject BuildSnapshot(
        Process process,
        NativeDesktop.WindowRecord selectedWindow,
        JsonObject? observation)
    {
        // One clock covers ownership, traversal and the single ownership
        // retry, so a retry cannot restart the sampling deadline.
        var elapsed = Stopwatch.StartNew();
        try
        {
            var attempt = 0;
            return WindowOwnershipObservation.Read(() =>
            {
                if (attempt++ > 0) { NativeDesktop.RefreshReadOnlyWindowScope(); }
                return BuildSnapshotSample(
                    process, selectedWindow, observation, () => elapsed.Elapsed);
            });
        }
        catch (WindowOwnershipUnavailableException error)
        {
            throw new ProviderFailureException(
                "window_ownership_unavailable", "observation",
                $"A complete logical-window sample could not be acquired: {error.Message}");
        }
    }

    private JsonObject BuildSnapshotSample(
        Process process,
        NativeDesktop.WindowRecord selectedWindow,
        JsonObject? observation,
        Func<TimeSpan> elapsed)
    {
        var annotations = new JsonArray();
        var projection = ProjectNativeWindows(
            process,
            NativeDesktop.ListWindows(process.Id));
        selectedWindow = projection.LogicalRecord(selectedWindow);
        // Root and popup discovery share one read of the desktop's children.
        var discovery = new UiaDesktopDiscovery(_automation);
        var maxNodes = OptionalInt(observation, "max_tree_nodes", DefaultNodeLimit);
        var roots = LogicalAutomationRoots(
            process,
            projection,
            selectedWindow,
            discoverTransients: false,
            annotation: Gap,
            elapsed: elapsed,
            discovery: discovery,
            // A one-node read returns the window root, which the window's own
            // native handles give; the desktop scan finds only further roots.
            scanDesktop: maxNodes > 1);
        var initialRootIdentities = roots.Select(RuntimeObjectPath).ToArray();
        var maxDepth = OptionalInt(observation, "max_tree_depth", DefaultDepthLimit);
        var visits = new OwnershipProbeBudget(
            maxNodes, SnapshotSampleTimeout, elapsed);
        var ownershipBudget = new OwnershipProbeBudget(
            checked(maxNodes * 4 + 256), SnapshotSampleTimeout, elapsed);
        var boundary = CreateLogicalBoundary(process, projection, ownershipBudget);
        var index = 0;
        var selectedHandle = OpaqueWindowHandle(process, selectedWindow.Handle);
        var sampled = new List<(CachedUiaNode Node, JsonObject Record, int[] Path, int Depth)>();
        var identities = new CanonicalNodeIdentitySet<CachedUiaNode>((left, right) =>
        {
            visits.CheckTime();
            return _automation.Compare(left.Element, right.Element);
        });
        var processGeneration = ProcessGeneration(process);
        var cache = new UiaSnapshotCache(_automation);
        var transientRoots = 0;
        var grids = new List<SpreadsheetRead?>();
        var focus = FocusSample(visits.CheckTime);

        // No ForceNoCache scope here: the snapshot cache activates its own
        // requests, and every other call in this block runs uncached.
        using (WindowsUiaAutomationFactory.SnapshotReadTimeout(_automation))
        {
            try
            {
                Visit(ReadRoots(roots));
                // Known window roots provide the main canonical tree. Searching
                // the desktop again for raw-only popups must not delay it.
                if (visits.Remaining > 0 && elapsed() < SnapshotSampleTimeout)
                {
                    var additionalRoots = LogicalAutomationRoots(
                        process, projection, selectedWindow,
                        transientsOnly: true,
                        annotation: Gap, elapsed: elapsed,
                        precedingRuntimeIdentities: initialRootIdentities,
                        discovery: discovery);
                    // Keep the first root table frozen. New roots append to it;
                    // duplicate records skip emission, not their index slots.
                    transientRoots = additionalRoots.Count;
                    Visit(ReadRoots(additionalRoots), roots.Count);
                }
                else
                {
                    Gap("transient_discovery_incomplete");
                }
            }
            catch (ObservationDeadlineExceededException)
            {
                // Keep the sampled canonical records. An exhausted read budget
                // is not evidence that the window was replaced and cannot retry.
                Gap("time_budget");
                Gap("transient_discovery_incomplete");
            }
            BindCurrentFocus(sampled, focus, annotations, Gap, visits.CheckTime,
                boundary, selectedWindow.Handle.ToInt64());
        }

        var snapshot = PublishSnapshot(process, selectedWindow, projection, sampled, annotations);
        RememberSpreadsheetRead(
            process, selectedWindow, projection, sampled, annotations,
            transientRoots == 0 && grids.Count == 1 ? grids[0] : null,
            maxNodes, maxDepth);
        return snapshot;


        bool Emit(CachedUiaNode node, int[] treePath, int depth)
        {
            if (node.Facts is null)
            {
                Gap("node_read_failed");
                return false;
            }
            JsonObject record;
            try
            {
                record = ElementJson(
                    node.Element,
                    node.Facts,
                    index,
                    processGeneration,
                    process,
                    selectedWindow,
                    selectedHandle,
                    treePath,
                    Gap,
                    visits.CheckTime,
                    () => UiaDocumentText.ProbeContent(cache, node, visits.CheckTime));
            }
            catch
            {
                Gap("node_read_failed");
                return false;
            }
            var providerObjectPath =
                record["providerObjectPath"]?.GetValue<string>() ?? string.Empty;
            try
            {
                var propertyKey = string.Join('\u001f',
                    record["controlType"]?.GetValue<string>() ?? string.Empty,
                    record["name"]?.GetValue<string>() ?? string.Empty,
                    record["className"]?.GetValue<string>() ?? string.Empty,
                    record["frame"]?.ToJsonString() ?? string.Empty);
                if (!identities.TryAdd(node, providerObjectPath, propertyKey, treePath[0])) { return false; }
            }
            catch (ObservationDeadlineExceededException) { throw; }
            catch
            {
                Gap("node_identity_unavailable");
                return false;
            }
            sampled.Add((node, record, treePath, depth));
            index++;
            if (node.Facts.ControlType is ControlType.Table or ControlType.DataGrid)
            {
                ReadSpreadsheetCells(node, treePath, depth);
            }
            return true;
        }

        // Identify a spreadsheet through a visible cell owned by this table.
        // Navigating to the first child can enumerate the full virtual sheet
        // inside the accessibility bridge, even without a cache request.
        void ReadSpreadsheetCells(CachedUiaNode table, int[] tablePath, int depth)
        {
            var area = Rectangle.Intersect(table.Facts!.Bounds, selectedWindow.Bounds);
            var started = elapsed();
            var identificationUnavailable = area.Width < 4 || area.Height < 4;
            var viewport = SpreadsheetGrid.ReadViewport(
                area,
                point =>
                {
                    if (!SpreadsheetGrid.PointShowsWindow(point, selectedWindow.Handle))
                    {
                        identificationUnavailable = true;
                        return null;
                    }
                    CachedUiaNode? cell;
                    try
                    {
                        cell = cache.ReadAtPoint(point, table);
                        if (cell?.Facts is not { } facts)
                        {
                            identificationUnavailable = true;
                            return null;
                        }
                        if (facts.ControlType != ControlType.DataItem ||
                            !SpreadsheetGrid.TryParseA1(facts.Name, out var row, out var column))
                        {
                            identificationUnavailable = !SpreadsheetGrid.CanEnumerateAfterHit(
                                facts.ControlType, facts.Name, facts.Bounds.Contains(point),
                                () => cache.IsSameElement(cell, table),
                                () => cache.IsImmediateChildOf(cell, table),
                                () => cache.IsOrdinaryRowChildOf(cell, table));
                            return null;
                        }
                        if (!facts.Bounds.Contains(point) || !cache.IsImmediateChildOf(cell, table))
                        {
                            identificationUnavailable = true;
                            return null;
                        }
                        return new SpreadsheetCellHit<CachedUiaNode>(cell, row, column, facts.Bounds,
                            !string.IsNullOrEmpty(facts.ValueText));
                    }
                    catch
                    {
                        identificationUnavailable = true;
                        return null;
                    }
                },
                MaxSpreadsheetProbes,
                () => elapsed() - started < SpreadsheetReadTime && elapsed() < SnapshotSampleTimeout);
            if (viewport is null)
            {
                if (identificationUnavailable || elapsed() - started >= SpreadsheetReadTime ||
                    elapsed() >= SnapshotSampleTimeout)
                {
                    table.CellsRead = true;
                    Omit(tablePath, "visible_cell_range_unavailable", 1);
                    grids.Add(null);
                }
                return;
            }
            table.CellsRead = true;
            var layout = new SpreadsheetLayout(
                area, viewport.Row0, viewport.Row1, viewport.Column0, viewport.Column1,
                viewport.Cells.ToDictionary(cell => (cell.Row, cell.Column), cell => cell.Bounds));
            var listedCells = new HashSet<(int Row, int Column)>();
            var empty = 0;
            var overBudget = 0;
            var failed = 0;
            foreach (var cell in viewport.Cells)
            {
                int[] cellPath = [.. tablePath, cell.Row, cell.Column];
                JsonObject? record;
                try
                {
                    record = SpreadsheetCellRecord(
                        cell, table.Facts.Name, cellPath, index, processGeneration,
                        process, selectedWindow, selectedHandle, Gap, visits.CheckTime, focus);
                }
                catch
                {
                    Gap("node_read_failed");
                    failed++;
                    continue;
                }
                if (record is null)
                {
                    empty++;
                    continue;
                }
                if (visits.Remaining == 0)
                {
                    overBudget++;
                    continue;
                }
                visits.Take();
                sampled.Add((cell.Cell, record, cellPath, depth + 1));
                index++;
                listedCells.Add((cell.Row, cell.Column));
            }
            if (empty > 0)
            {
                annotations.Add(CellSummaryAnnotation(tablePath, empty, listedCells.Count, layout));
            }
            grids.Add(new SpreadsheetRead(
                table, tablePath, depth, layout, listedCells,
                Complete: failed == 0 && overBudget == 0 && viewport.Missing == 0 &&
                    viewport.Unprobed == 0 && viewport.ExtentComplete));
            if (viewport.Missing > 0)
            {
                Omit(tablePath, "visible_cell_lookup_unavailable", viewport.Missing);
            }
            if (viewport.Unprobed > 0 || !viewport.ExtentComplete)
            {
                Omit(tablePath, "visible_cell_budget", Math.Max(1, viewport.Unprobed));
            }
            if (overBudget > 0)
            {
                Omit(tablePath, "node_budget", overBudget);
            }
        }

        void Omit(int[] parentPath, string reason, int count)
        {
            annotations.Add(new JsonObject
            {
                ["parentRuntimeId"] = PathJson(parentPath),
                ["kind"] = "omitted",
                ["count"] = count,
                ["reason"] = reason,
                ["detail"] = "Visible spreadsheet cells were not all read.",
            });
        }

        void Visit(IReadOnlyList<CachedUiaNode> selectedRoots, int rootIndexOffset = 0)
        {
            LogicalWindowTraversal.Visit(
                selectedRoots, visits, maxDepth,
                Owned,
                Emit,
                node => node.CellsRead ? null : ReadLink(() => cache.ReadFirstChild(node)),
                node => ReadLink(() => cache.ReadNextSibling(node)),
                Gap,
                rootIndexOffset);
        }

        // Each read returns one parent's control-view children with every
        // fact a record needs, so a level costs one cross-process call per
        // parent instead of about twenty per node. A root that cannot be read
        // keeps its slot, so later roots keep their indices.
        IReadOnlyList<CachedUiaNode> ReadRoots(IReadOnlyList<AutomationElement> selectedRoots)
        {
            var nodes = new CachedUiaNode[selectedRoots.Count];
            for (var rootIndex = 0; rootIndex < selectedRoots.Count; rootIndex++)
            {
                visits.CheckTime();
                var root = selectedRoots[rootIndex];
                CachedUiaNode? node;
                try { node = cache.ReadRoot(root); }
                catch { node = null; }
                nodes[rootIndex] = node ?? new CachedUiaNode(root, null, null, 0);
            }
            return nodes;
        }

        CachedUiaNode? ReadLink(Func<CachedUiaNode?> read)
        {
            try
            {
                return read();
            }
            catch
            {
                Gap("children_read_failed");
                return null;
            }
        }

        bool Owned(CachedUiaNode node)
        {
            if (node.Owned is bool known) { return known; }
            var facts = node.Facts;
            var owned = (facts is null
                    ? null
                    : InheritedWindowOwnership.FromParent(
                        node.Parent?.Owned,
                        facts.NativeWindowHandle,
                        facts.Window || facts.ControlType == ControlType.Window)) ??
                boundary.Owner(node.Element) == selectedWindow.Handle.ToInt64();
            node.Owned = owned;
            return owned;
        }


        void Gap(string reason)
        {
            if (annotations.OfType<JsonObject>().Any(item =>
                item["reason"]?.GetValue<string>() == reason)) { return; }
            annotations.Add(new JsonObject
            {
                ["kind"] = "omitted",
                ["count"] = 1,
                ["reason"] = reason,
                ["detail"] = "Logical-window canonical acquisition is incomplete.",
            });
        }
    }

    // Publishes sampled records in tree-path order with dense indices,
    // records the window's element leases and adds the window inventory.
    private JsonObject PublishSnapshot(
        Process process,
        NativeDesktop.WindowRecord selectedWindow,
        NativeWindowProjection projection,
        IEnumerable<(CachedUiaNode Node, JsonObject Record, int[] Path, int Depth)> sampled,
        JsonArray annotations)
    {
        // Traversal alternates parent branches, so records arrive breadth
        // first. Publish them in tree-path order with dense indices.
        var pathOrder = Comparer<int[]>.Create((left, right) =>
        {
            for (var offset = 0; offset < Math.Min(left.Length, right.Length); offset++)
            {
                var order = left[offset].CompareTo(right[offset]);
                if (order != 0) { return order; }
            }
            return left.Length.CompareTo(right.Length);
        });
        var records = new JsonArray();
        var lines = new JsonArray();
        var observedElementLeases =
            new Dictionary<string, ObservedElementLease<AutomationElement>>(
                StringComparer.Ordinal);
        var index = 0;
        foreach (var sample in sampled.OrderBy(sample => sample.Path, pathOrder))
        {
            sample.Record["index"] = index;
            records.Add(sample.Record);
            lines.Add(new string(' ', sample.Depth * 2) +
                $"{index} {sample.Record["controlType"]?.GetValue<string>() ?? "unknown"} " +
                $"{sample.Record["name"]?.GetValue<string>() ?? string.Empty}");
            var providerPath = sample.Record["providerObjectPath"]?.GetValue<string>() ?? string.Empty;
            if (providerPath.StartsWith("runtime:", StringComparison.Ordinal))
            {
                observedElementLeases[providerPath] =
                    new ObservedElementLease<AutomationElement>(sample.Node.Element, sample.Record);
            }
            else if (providerPath.StartsWith(SpreadsheetCellScope + ":", StringComparison.Ordinal))
            {
                observedElementLeases[providerPath] =
                    new ObservedElementLease<AutomationElement>(sample.Node.Element, sample.Record);
            }
            index++;
        }
        _observedElementLeases.Replace(
            process.Id,
            ProcessGeneration(process),
            selectedWindow.Handle.ToInt64(),
            observedElementLeases);

        var windows = projection.LogicalWindows();
        var windowInventory = new JsonArray();
        foreach (var window in windows)
        {
            var handle = OpaqueWindowHandle(process, window.Handle);
            windowInventory.Add(
                new JsonObject
                {
                    ["title"] = window.Title,
                    ["role"] = "window",
                    ["handle"] = handle,
                    ["providerBusName"] = $"win32:pid:{process.Id}",
                    ["providerObjectPath"] = handle,
                    ["providerProcessGeneration"] = ProcessGeneration(process),
                    ["selected"] = window.Handle == selectedWindow.Handle,
                    ["active"] = window.Active,
                    ["showing"] = true,
                });
        }
        var snapshot = new JsonObject
        {
            ["app"] = AppJson(process, selectedWindow.HostedProcessId),
            ["windowTitle"] = selectedWindow.Title,
            ["windowHandle"] = OpaqueWindowHandle(process, selectedWindow.Handle),
            ["windows"] = windowInventory,
            ["snapshotId"] = Guid.NewGuid().ToString("N"),
            ["sampledAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["windowBounds"] = RectangleJson(selectedWindow.Bounds),
            ["treeLines"] = lines,
            ["elements"] = records,
            ["annotations"] = annotations,
        };
        return snapshot;
    }

    // The record of a cell read by hit test, or null when the cell is empty
    // and does not hold the cursor: such a cell is summarized, not listed.
    // The cell has no UIA runtime id, and its position among the table's
    // children is too far to walk: an action finds it again by hit test at
    // its observed rectangle. The identity names the table and sheet, so a
    // ref to C2 of one sheet never resolves to C2 of another.
    private JsonObject? SpreadsheetCellRecord(
        SpreadsheetCellHit<CachedUiaNode> cell,
        string tableName,
        int[] cellPath,
        int index,
        string processGeneration,
        Process process,
        NativeDesktop.WindowRecord window,
        string windowHandle,
        Action<string> gap,
        Action checkTime,
        WindowsKeyboardFocusSample<AutomationElement> focus)
    {
        var facts = cell.Cell.Facts!;
        var record = ElementJson(
            cell.Cell.Element, facts, index, processGeneration,
            process, window, windowHandle, cellPath, gap, checkTime);
        if (!SpreadsheetGrid.RetainCell(record, facts.HasKeyboardFocus,
                () => focus.IsFocusedObject(cell.Cell.Element,
                    facts.RuntimeId.Length > 0 ? RuntimeObjectPath(facts.RuntimeId) : string.Empty)))
        {
            return null;
        }
        record["providerIdentityScope"] = SpreadsheetCellScope;
        record["providerObjectPath"] = SpreadsheetCellObjectPath(
            tableName, cell.Row, cell.Column);
        return record;
    }

    // Same annotation as the Linux provider: the core renders it on the
    // table as /empty-cells.
    private static JsonObject CellSummaryAnnotation(
        int[] tablePath, int empty, int listed, SpreadsheetLayout layout) => new()
    {
        ["parentRuntimeId"] = PathJson(tablePath),
        ["kind"] = "cell_summary",
        ["count"] = empty,
        ["reason"] = "empty_cells",
        ["detail"] =
            $"{empty} empty cells in view " +
            $"{SpreadsheetGrid.A1(layout.Row0, layout.Column0)}:" +
            $"{SpreadsheetGrid.A1(layout.Row1, layout.Column1)} omitted " +
            $"({listed} listed); they have no ref: move the cell cursor there " +
            "with keypress, then type without a ref",
    };

    // Identity scope of a spreadsheet cell record (see ResolveSpreadsheetCell).
    private const string SpreadsheetCellScope = "table_cell";
    // Each probe is one cross-process hit test; together the two limits keep
    // a large window's grid within about two seconds.
    private const int MaxSpreadsheetProbes = 1200;
    private static readonly TimeSpan SpreadsheetReadTime = TimeSpan.FromSeconds(2);

    // The window is part of every ref identity, so the sheet name and the
    // cell coordinate identify a cell. The table's tree path is left out:
    // it shifts when toolbar controls before it appear or disappear.
    private static string SpreadsheetCellObjectPath(string tableName, int row, int column) =>
        $"{SpreadsheetCellScope}:{SpreadsheetGrid.A1(row, column)}:{tableName}";

    private static JsonArray PathJson(IEnumerable<int> path) =>
        new(path.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    // Builds one record from facts read through the snapshot cache. Only a
    // text control's selection support and document text need a live call.
    private JsonObject ElementJson(
        AutomationElement element,
        UiaNodeFacts facts,
        int index,
        string processGeneration,
        Process process,
        NativeDesktop.WindowRecord window,
        string windowHandle,
        IReadOnlyList<int> treePath,
        Action<string> gap,
        Action checkTime,
        Func<DocumentContentProbe>? probeContent = null)
    {
        var role = facts.ControlType is { } controlType
            ? UiaSnapshotNodeReads.RoleOrUnknown(() => CanonicalRole(controlType))
            : "unknown";
        var name = index == 0 ? window.Title : facts.Name;
        var rectangle = facts.Bounds;
        var hasKeyboardFocus = facts.HasKeyboardFocus;
        if (facts.KeyboardFocus.Gap is { } focusGap) { gap?.Invoke(focusGap); }
        var visibility = facts.Visibility;
        var isEffectivelyVisible = visibility.EffectiveVisibility(
            hasKeyboardFocus,
            rectangle,
            window.Bounds);
        JsonObject? visibilityEvidence = null;
        if (visibility.IsOffscreen is null)
        {
            visibilityEvidence = new JsonObject
            {
                ["state"] = "unknown",
                ["reason"] = visibility.Reason,
            };
            if (visibility.ExceptionType is not null)
            {
                visibilityEvidence["exceptionType"] = visibility.ExceptionType;
                visibilityEvidence["hresult"] = visibility.HResult;
            }
        }
        else if (visibility.IsOffscreen == true && isEffectivelyVisible == true)
        {
            visibilityEvidence = new JsonObject
            {
                ["state"] = "offscreen",
                ["reason"] = "focus_geometry_override",
            };
        }
        var textPattern = facts.Text
            ? UiaSnapshotNodeReads.OptionalPattern(() => element.Patterns.Text.PatternOrDefault)
            : null;
        var valueWritable = facts.Value && !facts.ValueReadOnly;
        var rangeValueWritable = facts.RangeValue && !facts.RangeValueReadOnly;
        var actions = new JsonArray();
        AddAction(actions, facts.Invoke, "invoke");
        AddAction(actions, facts.Toggle, "toggle");
        AddAction(
            actions,
            facts.SelectionItem || textPattern is not null &&
                Safe(() => textPattern.SupportedTextSelection !=
                    FlaUI.Core.Definitions.SupportedTextSelection.None, false),
            "select");
        AddAction(actions, facts.ExpandCollapse, "expand");
        AddAction(actions, facts.ScrollItem, "scrollIntoView");
        AddAction(
            actions,
            valueWritable ||
                UiaSetValueRouting.PrefersRange(role, rangeValueWritable),
            "setValue");
        var states = new List<string>();
        if (facts.IsEnabled == true)
        {
            states.Add("enabled");
        }
        if (hasKeyboardFocus)
        {
            states.Add("focused");
        }
        if (facts.Toggle && facts.ToggleState is { } toggleState)
        {
            var canonical = ProviderValueReads.OptionalString(
                () => WindowsToggleState.Canonicalize(toggleState.ToString()));
            if (!string.IsNullOrEmpty(canonical))
            {
                states.Add(canonical);
            }
        }
        if (facts.SelectionItem)
        {
            states.Add(facts.IsSelected == true ? "selected" : "unselected");
        }
        var value = string.Empty;
        if (facts.Text || facts.ControlType == ControlType.Document)
        {
            var textRead = DocumentTextRead.Read(limit =>
            {
                checkTime?.Invoke();
                return textPattern?.DocumentRange.GetText(limit);
            }, MaxDocumentTextLength);
            if (textRead.Text is null && facts.ControlType == ControlType.Document)
            {
                textRead = textRead.WithContentFallback(
                    probeContent ?? (() => throw new InvalidOperationException("Document content probe is unavailable.")),
                    () => UiaDocumentText.ReadLegacy(
                        element, MaxDocumentTextLength, checkTime ?? (() => { })));
            }
            if (textRead.IncompleteReason is { } reason) { gap?.Invoke(reason); }
            value = facts.Value && facts.ControlType != ControlType.Document
                ? LiveValueReadback.CanonicalTextValue(facts.ValueText ?? string.Empty, textRead.Text)
                : textRead.Text ?? facts.ValueText ?? string.Empty;
        }
        else if (facts.Value)
        {
            value = facts.ValueText ?? string.Empty;
        }
        else if (facts.RangeValue && facts.RangeValueNumber is { } number)
        {
            value = number.ToString(
                "R",
                System.Globalization.CultureInfo.InvariantCulture);
        }
        var nativeRuntimeId = facts.RuntimeId;
        var providerBusName = $"uia3:pid:{process.Id}";
        var providerObjectPath = nativeRuntimeId.Length == 0
            ? string.Empty
            : RuntimeObjectPath(nativeRuntimeId);
        var record = new JsonObject
        {
            ["index"] = index,
            ["runtimeId"] = new JsonArray(
                treePath.Select(value => JsonValue.Create(value)).ToArray()),
            ["automationId"] = facts.AutomationId,
            ["providerBusName"] = providerBusName,
            ["providerObjectPath"] = providerObjectPath,
            ["processGeneration"] = processGeneration,
            ["providerIdentityScope"] = nativeRuntimeId.Length == 0
                ? "snapshot_only"
                : "self",
            ["providerWindowBusName"] = $"win32:pid:{process.Id}",
            ["providerWindowObjectPath"] = windowHandle,
            ["providerWindowProcessGeneration"] = processGeneration,
            ["providerWindowRole"] = "window",
            ["providerWindowName"] = window.Title,
            ["showing"] = isEffectivelyVisible,
            ["visible"] = isEffectivelyVisible,
            ["name"] = name,
            ["controlType"] = role,
            ["localizedControlType"] = facts.LocalizedControlType,
            ["className"] = facts.ClassName,
            ["value"] = value,
            ["frame"] = RectangleJson(rectangle),
            ["actions"] = actions,
            ["states"] = string.Join(' ', states),
            ["description"] = facts.HelpText,
        };
        if (visibilityEvidence is not null)
        {
            record["visibilityEvidence"] = visibilityEvidence;
        }
        return record;
    }

}
