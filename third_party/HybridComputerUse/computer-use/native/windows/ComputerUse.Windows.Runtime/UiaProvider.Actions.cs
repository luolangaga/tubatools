using System.Diagnostics;
using System.Drawing;
using System.Text.Json.Nodes;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class UiaProvider
{
    internal JsonObject ResolveAndAct(JsonObject request)
    {
        var intent = RequiredString(request, "intent");
        var input = request["input"] as JsonObject ?? new JsonObject();
        // Text validation cannot follow focus preparation or document
        // selection: malformed UTF-16 must reject before either can mutate.
        if (intent is "type" or "set_value")
        {
            NativeDesktop.ValidateTypedText(OptionalString(
                input, intent == "type" ? "text" : "value", string.Empty));
        }
        var query = RequiredString(request, "app");
        var (process, scope) = ResolveApp(request, query);
        var target = request["target"] as JsonObject;
        var elementRecord = request["element"] as JsonObject;
        var selectedWindow = ResolveWindow(
            process,
            target,
            OptionalString(request, "window_handle", string.Empty),
            "stale_element_ref",
            scope);
        var requestWindowHandle = OptionalString(
            request,
            "window_handle",
            string.Empty);
        var currentWindowHandle = OptionalString(
            target,
            "windowHandle",
            requestWindowHandle);
        WindowsWindowExpectation.ValidateCurrent(
            request["expected_window"] as JsonObject,
            requestWindowHandle,
            currentWindowHandle,
            $"win32:pid:{process.Id}",
            currentWindowHandle,
            ProcessGeneration(process),
            "window");
        var root = _automation.FromHandle(selectedWindow.Handle);
        var element = ResolveElement(
            root,
            target,
            elementRecord,
            process,
            selectedWindow,
            () => _automation.FromHandle(selectedWindow.Handle));
        EnsureTargetLogicalWindow(element, process, selectedWindow);
        var ownerIdentity = CapturePostActionOwnerIdentity(selectedWindow);
        if (intent == "resolve_target")
        {
            if (element is null)
            {
                throw new ProviderFailureException(
                    "semantic_target_not_found",
                    "precondition",
                    "resolve_target requires an exact target.");
            }
            return new JsonObject
            {
                ["ok"] = true,
                ["resolved_element"] = ResolvedElementJson(
                    root,
                    element,
                    process,
                    selectedWindow),
            };
        }
        // Decided before any input: the read after the action may then read
        // the sheet in part (see TryReadSpreadsheetBack). Any other action
        // drops the kept read, so a later cell action reads in full.
        var spreadsheetRead = SpreadsheetCellAction(
                process, selectedWindow, intent, input, target, elementRecord, element,
                out var spreadsheetCell, out var spreadsheetCursorKey)
            ? _spreadsheetRead
            : null;
        ForgetSpreadsheetRead();
        var route = "unknown";
        var geometrySampled = false;
        bool? pointOwnerVerified = null;
        bool? focusVerified = null;
        var readbackSampled = false;
        bool? readbackVerified = null;
        bool? desktopStateMutated = null;
        var inputEmitted = false;
        var mutationAttempted = false;
        var pointerPreparationAttempted = false;

        using var changed = new ManualResetEventSlim(false);
        using var eventHandler = new FailurePreservingDisposable(
            root.RegisterStructureChangedEvent(
                TreeScope.Subtree,
                (_, _, _) => changed.Set()),
            error => Console.Error.WriteLine(
                "[provider] AX structure event cleanup failed without " +
                "replacing the primary action result: " +
                $"{error.GetType().Name} HRESULT=0x{error.HResult:x8}: " +
                error.Message));

        try
        {
            switch (intent)
            {
                case "select":
                    var verifySelection = PrepareSelection(element, input,
                        out var emitSelection, out route);
                    EnsureTargetLogicalWindow(element, process, selectedWindow);
                    mutationAttempted = true;
                    emitSelection();
                    inputEmitted = true;
                    readbackSampled = true;
                    try
                    {
                        readbackVerified = verifySelection();
                    }
                    catch
                    {
                        readbackVerified = false;
                    }
                    if (readbackVerified != true)
                    {
                        throw new ProviderFailureException("readback_mismatch", "readback",
                            "UIA accepted select but the complete selection did not match; selection may have changed.",
                            inputEventsEmitted: true);
                    }
                    break;
                case "click":
                var clickMethod = OptionalString(input, "click_method", "auto");
                if (TryInvoke(
                        element,
                        (selectedRoute, _) =>
                        {
                            route = selectedRoute;
                            mutationAttempted = true;
                        }))
                {
                    inputEmitted = true;
                }
                else
                {
                    if (clickMethod == "accessibility")
                    {
                        throw new ProviderFailureException(
                            "capability_unsupported",
                            "precondition",
                            "The target exposes no semantically equivalent UIA pattern.");
                    }
                    if (element is null)
                    {
                        throw new ProviderFailureException(
                            "semantic_target_not_found", "precondition",
                            "Pointer fallback requires an exact target.");
                    }
                    var point = VerifiedPointerPoint(element, process, selectedWindow);
                    geometrySampled = true;
                    pointOwnerVerified = true;
                    route = "input.send_input.pointer";
                    mutationAttempted = true;
                    NativeDesktop.Click(
                        point.X,
                        point.Y,
                        OptionalString(input, "mouse_button", "left"),
                        OptionalInt(input, "click_count", 1));
                    inputEmitted = true;
                }
                    break;
                case "set_value":
                if (element is null)
                {
                    throw new ProviderFailureException(
                        "semantic_target_not_found",
                        "precondition",
                        "set_value requires an exact target.");
                }
                var requestedValue = OptionalString(input, "value", string.Empty);
                var rangePattern =
                    element.Patterns.RangeValue.PatternOrDefault;
                var rangeWritable = rangePattern is not null &&
                    !rangePattern.IsReadOnly.Value;
                if (UiaSetValueRouting.PrefersRange(
                        CanonicalRole(element.ControlType),
                        rangeWritable))
                {
                    if (!UiaSetValueRouting.TryParseFiniteRangeValue(
                            requestedValue,
                            out var requestedRangeValue))
                    {
                        throw new ProviderFailureException(
                            "invalid_request",
                            "precondition",
                            "set_value requires a finite invariant number for a UIA range target.");
                    }
                    if (!UiaSetValueRouting.IsWithinRange(
                            requestedRangeValue,
                            rangePattern!.Minimum.Value,
                            rangePattern.Maximum.Value))
                    {
                        throw new ProviderFailureException(
                            "invalid_request",
                            "precondition",
                            "set_value is outside the UIA range target bounds.");
                    }
                    route = "uia.range_value.set";
                    var rangeBefore = Safe<double?>(
                        () => rangePattern!.Value.Value, null);
                    mutationAttempted = true;
                    rangePattern.SetValue(requestedRangeValue);
                    inputEmitted = true;
                    readbackSampled = true;
                    readbackVerified = LiveValueReadback.VerifyRange(
                        requestedRangeValue,
                        () =>
                        {
                            var readbackRoot =
                                _automation.FromHandle(selectedWindow.Handle);
                            var readbackElement = ResolveSetValueReadbackElement(
                                element,
                                readbackRoot,
                                target,
                                elementRecord,
                                process,
                                selectedWindow);
                            var readbackRange =
                                readbackElement.Patterns.RangeValue.PatternOrDefault;
                            if (readbackRange is null ||
                                readbackRange.IsReadOnly.Value)
                            {
                                return null;
                            }
                            return readbackRange.Value.Value;
                        });
                    if (readbackVerified != true)
                    {
                        throw new ProviderFailureException(
                            "readback_mismatch",
                            "readback",
                            "UIA RangeValue accepted the write but readback did not match.",
                            inputEventsEmitted: true);
                    }
                    desktopStateMutated = ActionEvidencePolicy.VerifiedChange(
                        rangeBefore is double before &&
                            !LiveValueReadback.RangeEquals(before, requestedRangeValue));
                    break;
                }
                var valuePattern = element.Patterns.Value.PatternOrDefault;
                if (valuePattern is null || valuePattern.IsReadOnly.Value)
                {
                    throw new ProviderFailureException(
                        "capability_unsupported",
                        "precondition",
                        "The target does not expose a writable UIA Value pattern.");
                }
                var textPattern = element.Patterns.Text.PatternOrDefault;
                var valueBefore = Safe<string?>(
                    () => LiveValueReadback.CanonicalTextValue(
                        valuePattern.Value.Value,
                        textPattern?.DocumentRange.GetText(-1)),
                    null);
                if (UiaSetValueRouting.PrefersDocumentInput(
                        element.ControlType == ControlType.Document,
                        !valuePattern.IsReadOnly.Value,
                        textPattern is not null))
                {
                    UiaSetValueRouting.RequireDocumentInputAllowed(
                        input["require_semantic_write"]?.GetValue<bool>() == true);
                    route = requestedValue.Length == 0
                        ? "uia.text_select_and_send_input.document_replace"
                        : "uia.text_select_and_window_messages.document_replace";
                    EnsureExactFocus(element,
                        () => EnsureTargetLogicalWindow(element, process, selectedWindow));
                    mutationAttempted = true;
                    // Select the document through its semantic contract; a
                    // queued Ctrl+A could be overtaken by synchronous text.
                    textPattern!.DocumentRange.Select();
                    inputEmitted = true;
                    if (requestedValue.Length == 0)
                    {
                        NativeDesktop.PressKeys(new[] { "DELETE" });
                    }
                    else
                    {
                        TypeInSelectedWindow(requestedValue);
                    }
                    focusVerified = ForegroundOwnedBy(process.Id);
                }
                else
                {
                    route = "uia.value.set";
                    mutationAttempted = true;
                    valuePattern.SetValue(requestedValue);
                    inputEmitted = true;
                }
                readbackSampled = true;
                readbackVerified = LiveValueReadback.VerifyTextValue(
                    requestedValue,
                    () =>
                    {
                        var readbackRoot =
                            _automation.FromHandle(selectedWindow.Handle);
                        var readbackElement = ResolveSetValueReadbackElement(
                            element,
                            readbackRoot,
                            target,
                            elementRecord,
                            process,
                            selectedWindow);
                        var readbackPattern =
                            readbackElement.Patterns.Value.PatternOrDefault;
                        if (readbackPattern is null ||
                            readbackPattern.IsReadOnly.Value)
                        {
                            return null;
                        }
                        return readbackPattern.Value.Value;
                    },
                    () =>
                    {
                        var readbackRoot =
                            _automation.FromHandle(selectedWindow.Handle);
                        var readbackElement = ResolveSetValueReadbackElement(
                            element,
                            readbackRoot,
                            target,
                            elementRecord,
                            process,
                            selectedWindow);
                        var readbackPattern =
                            readbackElement.Patterns.Text.PatternOrDefault;
                        if (readbackPattern is null)
                        {
                            return null;
                        }
                        return readbackPattern.DocumentRange.GetText(-1);
                    });
                if (readbackVerified != true)
                {
                    throw new ProviderFailureException(
                        "readback_mismatch",
                        "readback",
                        "UIA Value accepted the write but readback did not match.",
                        inputEventsEmitted: true);
                }
                desktopStateMutated = ActionEvidencePolicy.VerifiedChange(
                    valueBefore is not null &&
                        !LiveValueReadback.TextEquals(valueBefore, requestedValue));
                    break;
                case "type":
                    route = "input.window_messages";
                    if (element is not null)
                    {
                        EnsureExactFocus(element,
                            () => EnsureTargetLogicalWindow(element, process, selectedWindow));
                    }
                    else
                    {
                        NativeDesktop.ActivateWindow(selectedWindow.Handle);
                    }
                    mutationAttempted = true;
                    TypeInSelectedWindow(OptionalString(input, "text", string.Empty));
                    inputEmitted = OptionalString(input, "text", string.Empty).Length > 0;
                    focusVerified = ForegroundOwnedBy(process.Id);
                    break;
                case "keypress":
                    route = "input.send_input.keyboard";
                    if (element is not null)
                    {
                        EnsureExactFocus(element,
                            () => EnsureTargetLogicalWindow(element, process, selectedWindow));
                    }
                    else
                    {
                        NativeDesktop.ActivateWindow(selectedWindow.Handle);
                    }
                    mutationAttempted = true;
                    NativeDesktop.PressKeys(
                        new[] { RequiredString(input, "key") });
                    inputEmitted = true;
                    focusVerified = ForegroundOwnedBy(process.Id);
                    break;
                case "scroll":
                    if (TryScroll(
                            element,
                            OptionalString(input, "direction", "down"),
                            () =>
                            {
                                route = "uia.scroll";
                                mutationAttempted = true;
                            }))
                    {
                        inputEmitted = true;
                    }
                    else
                    {
                        route = "input.send_input.wheel";
                        // Window-scoped scrolling owns a logical window, not an
                        // arbitrary element. Element-scoped scrolling requires
                        // the exact element at the point.
                        RequirePointerEligibility(element ?? root);
                        pointerPreparationAttempted = true;
                        NativeDesktop.ActivateWindow(selectedWindow.Handle);
                        var point = VerifiedPointerPoint(
                            element ?? root, process, selectedWindow,
                            exactElementRequired: element is not null);
                        geometrySampled = true;
                        pointOwnerVerified = true;
                        mutationAttempted = true;
                        NativeDesktop.Scroll(
                            point.X,
                            point.Y,
                            0,
                            (OptionalString(input, "direction", "down") == "up"
                                ? -1
                                : 1) *
                            Math.Max(1, OptionalDouble(input, "pages", 1)));
                        inputEmitted = true;
                    }
                    break;
                default:
                    throw new ProviderFailureException(
                        "unsupported_intent",
                        "precondition",
                        $"Unsupported accessibility intent {intent}.");
            }

            _ = changed.Wait(TimeSpan.FromMilliseconds(120));
            var observation = request["observation"] as JsonObject;
            if (observation?["capture_snapshot"]?.GetValue<bool>() == false)
            {
                return new JsonObject
                {
                    ["ok"] = true,
                    ["observation_skipped"] = true,
                    ["mutation"] = ActionEvidencePolicy.MutationFacts(
                        inputEmitted, desktopStateMutated),
                    ["action_evidence"] = ActionEvidence(
                        route,
                        geometrySampled,
                        focusVerified,
                        readbackSampled,
                        readbackVerified,
                        inputEmitted, pointOwnerVerified),
                };
            }
            var followedExternalOwner = false;
            var readBack = false;
            var snapshot = PostActionObservationRetry.Execute(
                () => PostActionObservationWindows(
                    process.Id,
                    ownerIdentity),
                window => window.Handle.ToInt64(),
                window => window.Active,
                selectedWindow.Handle.ToInt64(),
                (window, _) =>
                {
                    readBack = false;
                    if (window.ProcessId == process.Id)
                    {
                        var partial = TryReadSpreadsheetBack(
                            spreadsheetRead, process, window, observation,
                            spreadsheetCell, spreadsheetCursorKey);
                        // A retry reads the whole window.
                        spreadsheetRead = null;
                        readBack = partial is not null;
                        return partial ?? BuildSnapshot(
                            process,
                            window,
                            observation);
                    }
                    if (!MatchesPostActionOwner(ownerIdentity, window))
                    {
                        throw new ProviderFailureException(
                            "window_target_stale",
                            "post_action_observation",
                            "The captured native owner identity is no longer current.");
                    }
                    using var ownerProcess = Process.GetProcessById(
                        window.ProcessId);
                    followedExternalOwner = true;
                    return BuildSnapshot(
                        ownerProcess,
                        window,
                        observation);
                },
                () => new JsonObject
                {
                    ["app"] = AppJson(process),
                    ["windowAbsent"] = true,
                    ["treeLines"] = new JsonArray(),
                    ["elements"] = new JsonArray(),
                },
                onFailure: (attempt, window, error) =>
                    Console.Error.WriteLine(
                        "[provider] post-action snapshot attempt " +
                        $"{attempt} failed for HWND " +
                        (window is null
                            ? "none"
                            : $"0x{window.Handle.ToInt64():x}") +
                        $": {error.GetType().Name} " +
                        $"HRESULT=0x{error.HResult:x8}"),
                windowOwnerHandle:
                    window => window.OwnerHandle.ToInt64(),
                originalOwnerHandle:
                    ownerIdentity?.Handle.ToInt64() ?? 0);
            var response = new JsonObject
            {
                ["ok"] = true,
                ["snapshot"] = snapshot,
                ["mutation"] = ActionEvidencePolicy.MutationFacts(
                        inputEmitted, desktopStateMutated),
                ["action_evidence"] = ActionEvidence(
                    route,
                    geometrySampled,
                    focusVerified,
                    readbackSampled,
                    readbackVerified,
                    inputEmitted, pointOwnerVerified),
            };
            if (followedExternalOwner)
            {
                response["external_desktop_transition"] = true;
                response["notes"] = new JsonArray(
                    "The acted-on transient window retired; the final AX " +
                    "observation follows its exact pre-action native owner.");
            }
            else if (readBack)
            {
                response["notes"] = new JsonArray(SpreadsheetReadbackNote);
            }
            return response;
        }
        catch (Exception error) when (
            pointerPreparationAttempted &&
            PointerPreparationFailureAttribution.HasKnownZeroRequestedInput(
                error, mutationAttempted, inputEmitted))
        {
            throw PointerPreparationFailureAttribution.FromZeroInputError(
                error, ActionEvidence(
                    route, geometrySampled, focusVerified,
                    readbackSampled, readbackVerified, false, pointOwnerVerified));
        }
        catch (ProviderFailureException error) when (
            mutationAttempted || error.FocusPreparation.HasValue)
        {
            throw UiaMutationFailureAttribution.Preserve(
                error, inputEmitted, ActionEvidence(
                    route,
                    geometrySampled,
                    error.FocusPreparation.HasValue ? false : focusVerified,
                    readbackSampled,
                    readbackVerified,
                    inputEmitted, pointOwnerVerified));
        }
        catch (Exception error) when (mutationAttempted)
        {
            var failureStage = inputEmitted
                ? "post_action_observation"
                : "action_dispatch";
            Console.Error.WriteLine(
                $"[provider] AX {failureStage} failed via {route}: " +
                $"{error.GetType().Name} HRESULT=0x{error.HResult:x8}: " +
                error.Message);
            throw UiaMutationFailureAttribution.FromUnexpected(
                error,
                inputEmitted,
                ActionEvidence(
                    route,
                    geometrySampled,
                    focusVerified,
                    readbackSampled,
                    readbackVerified,
                    inputEmitted, pointOwnerVerified));
        }

        void TypeInSelectedWindow(string text)
        {
            var expectedRoot = NativeDesktop.RootWindow(selectedWindow.Handle);
            if (expectedRoot == 0)
            {
                throw new ProviderFailureException(
                    "window_target_stale", "precondition",
                    "The selected input window disappeared; no text was sent.");
            }
            NativeDesktop.TypeText(text, expectedRoot);
        }
    }

    private static bool ForegroundOwnedBy(int processId) =>
        ActionEvidencePolicy.KeyboardFocusVerified(
            processId,
            NativeDesktop.TryGetWindowProcessId(
                NativeDesktop.ForegroundWindow(),
                out var foregroundProcess)
                ? foregroundProcess
                : null);

    private PostActionOwnerIdentity? CapturePostActionOwnerIdentity(
        NativeDesktop.WindowRecord selectedWindow)
    {
        return PostActionOwnerTransitionPolicy.Capture(
            selectedWindow.OwnerHandle,
            handle =>
                NativeDesktop.TryGetWindowProcessId(handle, out var processId)
                    ? processId
                    : null,
            processId =>
            {
                using var process = Process.GetProcessById(processId);
                return ProcessGeneration(process);
            },
            handle => _windowLifecycle.Generation(handle));
    }

    private IReadOnlyList<NativeDesktop.WindowRecord>
        PostActionObservationWindows(
            int sourceProcessId,
            PostActionOwnerIdentity? ownerIdentity)
    {
        return NativeDesktop.ListWindows()
            .Where(
                window =>
                    window.ProcessId == sourceProcessId ||
                    MatchesPostActionOwner(ownerIdentity, window))
            .ToArray();
    }

    private bool MatchesPostActionOwner(
        PostActionOwnerIdentity? expected,
        NativeDesktop.WindowRecord current)
    {
        if (expected is null ||
            current.Handle != expected.Handle ||
            current.ProcessId != expected.ProcessId)
        {
            return false;
        }
        try
        {
            using var process = Process.GetProcessById(current.ProcessId);
            return PostActionOwnerTransitionPolicy.Matches(
                expected,
                current.Handle,
                current.ProcessId,
                ProcessGeneration(process),
                _windowLifecycle.Generation(current.Handle));
        }
        catch
        {
            return false;
        }
    }

    private AutomationElement ResolveSetValueReadbackElement(
        AutomationElement? written,
        AutomationElement root,
        JsonObject? target,
        JsonObject? record,
        Process process,
        NativeDesktop.WindowRecord selectedWindow)
    {
        // Read back the element that was written when it still has the
        // target's provider identity; resolving the target again walks the
        // window's roots for every sample.
        var providerObjectPath = OptionalString(
            target,
            "providerObjectPath",
            OptionalString(record, "providerObjectPath", string.Empty));
        AutomationElement? resolved = null;
        if (written is not null &&
            providerObjectPath.StartsWith("runtime:", StringComparison.Ordinal))
        {
            try
            {
                if (RuntimeObjectPath(written) == providerObjectPath) { resolved = written; }
            }
            catch
            {
                // A retired element falls back to resolution.
            }
        }
        resolved ??= ResolveSetValueReadbackElementCore(
            root, target, record, process, selectedWindow);
        try
        {
            EnsureTargetLogicalWindow(resolved, process, selectedWindow);
        }
        catch (ProviderFailureException)
        {
            throw new ProviderFailureException(
                "stale_element_ref", "readback",
                "The written target's window ownership could not be verified during readback.",
                inputEventsEmitted: true);
        }
        return resolved;
    }

    private AutomationElement ResolveSetValueReadbackElementCore(
        AutomationElement root,
        JsonObject? target,
        JsonObject? record,
        Process process,
        NativeDesktop.WindowRecord selectedWindow)
    {
        var providerObjectPath = OptionalString(
            target,
            "providerObjectPath",
            OptionalString(record, "providerObjectPath", string.Empty));
        if (!providerObjectPath.StartsWith(
                "runtime:",
                StringComparison.Ordinal))
        {
            return ResolveElement(
                    root,
                    target,
                    record,
                    process,
                    selectedWindow) ??
                throw new ProviderFailureException(
                    "stale_element_ref",
                    "readback",
                    "The set_value target disappeared before live readback.",
                    inputEventsEmitted: true);
        }

        return ResolveProviderObjectPath(
            LogicalAutomationRoots(process, selectedWindow),
            providerObjectPath,
            resolved =>
            {
                if (!RoleMatchesTarget(resolved, target, record))
                {
                    throw new ProviderFailureException(
                        "stale_element_ref",
                        "readback",
                        "The set_value target role changed before live readback.",
                        inputEventsEmitted: true);
                }
            });
    }
}
