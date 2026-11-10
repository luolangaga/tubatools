package core

import (
	"fmt"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"strings"
)

func axBatchHasInputBefore(actions []axBatchAction) bool {
	for _, action := range actions {
		if action.actionType != "wait" {
			return true
		}
	}
	return false
}

func axBatchOnlyWaits(actions []axBatchAction) bool {
	for _, action := range actions {
		if action.actionType != "wait" {
			return false
		}
	}
	return len(actions) > 0
}

// preflightAXWaitRef checks that a wait ref still names an element of the
// batch window before any input.
func (s *Service) preflightAXWaitRef(context *AXBatchContext, ref string) error {
	entry, err := s.resolveShortAXRefOwned(ref)
	if err != nil {
		return err
	}
	if context != nil && entry.WindowHandle != context.WindowHandle {
		return fmt.Errorf("%s belongs to window %s, not the batch window %s; wait for it in its own act call",
			ref, entry.LogicalWindowRef, context.WindowRef)
	}
	return nil
}

func axWaitEvidenceEntry(index int, result ToolCallResult) map[string]any {
	entry := map[string]any{
		"action_index":  index,
		"action_number": index + 1,
		"action_type":   "wait",
		"call_error":    !axPostconditionPassed(result),
	}
	if status, _ := result.StructuredContent["assertion_status"].(string); status != "" {
		entry["wait_status"] = status
	}
	return entry
}

// rebindAfterWindowWait moves the rest of the batch into the window a wait
// found: after {"type":"wait","window":"Export"}, keys and ref-less text go
// to that window, as when the call had named it. A settle wait that saw
// focus move to another window moves the batch there in the same way.
// Element refs from the earlier window still fail on window identity.
func (s *Service) rebindAfterWindowWait(
	context *AXBatchContext,
	wait *axWait,
	result ToolCallResult,
) {
	if wait == nil || (wait.window == "" && !wait.settle) || wait.gone {
		return
	}
	ref, _ := result.StructuredContent["wait_window_ref"].(string)
	if ref == "" || ref == context.WindowRef {
		return
	}
	entry, err := s.WindowRefRegistry.Resolve(ref, "")
	if err != nil || !validInternalWindowHandle(entry.WindowHandle) ||
		entry.NoAccessibilityTree {
		// A window without an accessibility tree has nothing to read; the
		// batch stays bound where it was.
		return
	}
	context.WindowRef = entry.Ref
	context.WindowHandle = entry.WindowHandle
	context.AppRef = FirstNonEmpty(entry.AppRef, context.AppRef)
	context.AppToken = FirstNonEmpty(entry.AppRef, entry.AppIdentity, context.AppToken)
	context.boundByFocus = false
	context.expectedWindow = &ProviderWindowExpectation{
		WindowHandle:       entry.WindowHandle,
		ProviderBusName:    entry.ProviderWindowBusName,
		ProviderObjectPath: entry.ProviderWindowObjectPath,
		ProcessGeneration:  entry.ProviderWindowProcessGeneration,
		Role:               entry.ProviderWindowRole,
		Name:               entry.ProviderWindowName,
	}
	// One read of the new window makes it the current state of its app, so
	// the next step's window check and its diff start from that window.
	noScreenshot := false
	_, _, _ = s.refreshSnapshot(context.AppToken, AXServiceRequest{
		Operation:          axServiceObserve,
		App:                context.AppToken,
		IncludeScreenshot:  &noScreenshot,
		TargetWindowHandle: context.WindowHandle,
		SourceRef:          context.WindowRef,
		Scope:              contract.AXScopeWindow,
		TreeView:           contract.TreeViewWorking,
	})
}

// stopAXBatchAtWait ends a batch at a wait that did not hold. The steps
// before it ran; none after it were sent. The current state follows.
func (s *Service) stopAXBatchAtWait(
	context *AXBatchContext,
	actions []axBatchAction,
	index int,
	waitResult ToolCallResult,
	actionEvidence []map[string]any,
	waitNotes []string,
	obs observation,
) ToolCallResult {
	anyInputEmitted, anyPartialPossible, anyMutationUnknown := batchMutationSummary(actionEvidence)
	message := firstLine(ResultText(waitResult))
	if !strings.HasPrefix(message, "wait_timeout") {
		message = "wait_timeout: " + message
	}
	detail := strings.TrimSpace(strings.TrimPrefix(ResultText(waitResult), firstLine(ResultText(waitResult))))
	unreadable := waitResult.IsError
	if unreadable && !strings.Contains(message, "could not be read") {
		message += " (the state could not be read completely while waiting)"
	}
	stopped := TextResult(message, true)
	if len(waitNotes) > 0 {
		stopped.Content = append([]ContentItem{{Type: "text", Text: strings.Join(waitNotes, "\n")}}, stopped.Content...)
	}
	if detail != "" {
		stopped.Content = append(stopped.Content, ContentItem{Type: "text", Text: detail})
	}
	hint := `{"mode":"observe","window_ref":"` + FirstNonEmpty(context.WindowRef, "wN") + `"}`
	if ref, _ := waitResult.StructuredContent["until_window_ref"].(string); isShortAXWindowRef(ref) {
		hint = `{"mode":"observe","window_ref":"` + ref + `"}`
	}
	if wait := actions[index].wait; wait != nil && wait.window != "" {
		// The message lists the windows; the focused one is where the
		// application went instead.
		hint = `{"mode":"apps"}`
		if ref, _ := waitResult.StructuredContent["focused_window_ref"].(string); ref != "" {
			hint = `{"mode":"observe","window_ref":"` + ref + `"}`
		}
	}
	if corrected, _ := waitResult.StructuredContent["wait_hint"].(string); corrected != "" {
		hint = corrected
	}
	stopped.StructuredContent = map[string]any{
		contract.AXPublicErrorCodeKey:   contract.AXCodeWaitTimeout,
		contract.AXPublicErrorPathKey:   fmt.Sprintf("actions[%d]", index),
		contract.AXPublicErrorDetailKey: message,
		contract.AXPublicErrorHintKey:   hint,
		"failure_code":                  "wait_timeout",
		"failure_phase":                 "wait",
		"preflight_only":                false,
		"poll_attempted":                waitResult.StructuredContent["poll_attempted"] == true,
		"input_events_emitted":          anyInputEmitted,
		"partial_input_possible":        anyPartialPossible,
		"mutation_unknown":              anyMutationUnknown,
		"retry_same_request":            false,
		"a11y_batch_result": map[string]any{
			"actions_requested":                    len(actions),
			"actions_completed_without_call_error": index,
			"failed_action_index":                  index,
			"failed_action_type":                   "wait",
			"rejected_before_first_action":         false,
			"partial_sequence":                     index > 0,
			"actions":                              actionEvidence,
		},
	}
	if obs.resolvedMode() == contract.ObserveNone {
		return stopped
	}
	current := s.axBatchFinalObservation(context, obs, false)
	if current.IsError {
		stopped.Content = append(stopped.Content, ContentItem{
			Type: "text",
			Text: "current state unavailable: " + firstLine(ResultText(current)),
		})
		stopped.StructuredContent["observation_required"] = true
		return stopped
	}
	stopped.Content = append(stopped.Content, current.Content...)
	stopped.StructuredContent["final_observation_returned"] = true
	replaceAXObservationCoverage(stopped.StructuredContent, current.StructuredContent)
	if kind, present := current.StructuredContent["final_observation_kind"]; present {
		stopped.StructuredContent["final_observation_kind"] = kind
	}
	return stopped
}

// axBatchContinuityStop stops before the next input when the batch window
// changed or cannot be confirmed.
func (s *Service) axBatchContinuityStop(
	context *AXBatchContext,
	actions []axBatchAction,
	index int,
	actionEvidence []map[string]any,
	obs observation,
) (ToolCallResult, bool) {
	continuity := s.axBatchWindowContinuity(context)
	if continuity == axWindowSame {
		return ToolCallResult{}, false
	}
	anyInputEmitted, anyPartialPossible, anyMutationUnknown := batchMutationSummary(actionEvidence)
	failureCode := "stale_target"
	messageCode := "batch_window_changed"
	if continuity == axWindowUnknown {
		failureCode = "observation_incomplete"
		messageCode = "batch_window_unknown"
	}
	recovery, hint, currentRef := s.axBatchContinuityRecovery(context)
	stopped := TextResult(
		fmt.Sprintf(
			"%s: step %d/%d completed, but the logical window is %s "+
				"before step %d. The remaining steps were not attempted. %s",
			messageCode, index+1, len(actions), continuity, index+2, recovery,
		),
		true,
	)
	stopped.StructuredContent = map[string]any{
		contract.AXPublicErrorHintKey:          hint,
		"failure_code":                         failureCode,
		"failure_phase":                        "between_actions",
		"window_continuity":                    string(continuity),
		"actions_requested":                    len(actions),
		"actions_completed_without_call_error": index + 1,
		"stopped_before_action_index":          index + 1,
		"rejected_before_first_action":         false,
		"partial_sequence":                     true,
		"final_observation_returned":           false,
		"actions":                              actionEvidence,
		"any_input_emitted":                    anyInputEmitted,
		"input_events_emitted":                 anyInputEmitted,
		"partial_input_possible":               anyPartialPossible,
		"mutation_unknown":                     anyMutationUnknown || anyInputEmitted || anyPartialPossible,
		"observation_required":                 true,
		"retry_same_request":                   false,
	}
	if currentRef != "" {
		// The recovery read must follow the window that stopped the batch,
		// just as the hint does, rather than rereading the original window.
		stopped.StructuredContent["current_window_ref"] = currentRef
	}
	return s.recoverAXBatchObservation(context, stopped, obs), true
}

// Use the read that detected the change. A popup need not have a title or
// an addressable window, and closing a dialog can reveal an existing window.
func (s *Service) axBatchContinuityRecovery(context *AXBatchContext) (string, string, string) {
	var snapshot *AppSnapshot
	if s != nil && context != nil {
		snapshot = s.currentSnapshot(context.AppToken)
	}
	if snapshot == nil || snapshot.WindowAbsent {
		return "List current windows, inspect the intended window, then send only the remaining actions.", `{"mode":"apps"}`, ""
	}
	if ref := snapshot.publicWindowRef; ref != "" {
		if entry, err := s.WindowRefRegistry.Resolve(ref, ""); err == nil && entry.WindowHandle == snapshot.WindowHandle {
			return "The latest read shows " + ref + ". Observe that window before sending only the remaining actions.",
				axHintJSON(map[string]any{"mode": "observe", "window_ref": ref}), ref
		}
	}
	return "The latest window has no usable wN. Use GUI screenshot to inspect the window or popup before replanning the remaining actions.", GUIScreenshotCall, ""
}

// axWaitOnly runs an act call whose steps are all waits. It sends no
// input, so it needs no focused window: the batch window comes from a ref
// or window_ref, or from the focused window when one exists. A window wait
// needs neither.
func (s *Service) axWaitOnly(
	actions []axBatchAction,
	obs observation,
	windowRef string,
) ToolCallResult {
	context := &AXBatchContext{}
	needsWindow := false
	for _, action := range actions {
		if action.wait.window == "" {
			needsWindow = true
		}
	}
	if needsWindow {
		var focus *axDesktopWindow
		if windowRef == "" && !axBatchHasRef(actions) {
			var argErr *axArgError
			focus, argErr = s.resolveFocusedWindow()
			if argErr != nil {
				argErr.Message = "a wait without ref or window_ref reads the focused window, and no window has focus; " +
					"give window_ref, or wait for a window by title. Windows:" + strings.TrimPrefix(argErr.Message[strings.Index(argErr.Message, "Windows:"):], "Windows:")
				argErr.Hint = `{"mode":"act","window_ref":"w1","actions":[{"type":"wait","text":"Saved"}]}`
				return axArgErrorResult(argErr, len(actions))
			}
		}
		bound, err := s.deriveAXBatchContext(windowRef, actions, focus)
		if err != nil {
			return axBatchPreflightError(err, len(actions))
		}
		context = bound
		for index, action := range actions {
			if action.wait.ref == "" {
				continue
			}
			if err := s.preflightAXWaitRef(context, action.wait.ref); err != nil {
				return axBatchPreflightError(fmt.Errorf("actions[%d].ref: %w", index, err), len(actions))
			}
		}
	}
	evidence := []map[string]any{}
	notes := []string{}
	for index, action := range actions {
		result := s.runAXWait(context, action.wait)
		evidence = append(evidence, axWaitEvidenceEntry(index, result))
		if !axPostconditionPassed(result) {
			if context.WindowHandle == "" {
				obs = observation{mode: contract.ObserveNone}
			}
			return s.stopAXBatchAtWait(context, actions, index, result, evidence, notes, obs)
		}
		notes = append(notes, firstLine(ResultText(result)))
		s.rebindAfterWindowWait(context, action.wait, result)
	}
	result := TextResult(strings.Join(notes, "\n"), false)
	result.StructuredContent = map[string]any{
		"input_events_emitted": false,
		"a11y_batch_result": map[string]any{
			"actions_requested":                    len(actions),
			"actions_completed_without_call_error": len(actions),
			"actions":                              evidence,
		},
	}
	if context.WindowHandle != "" && obs.resolvedMode() != contract.ObserveNone {
		current := s.axBatchFinalObservation(context, obs, false)
		if !current.IsError {
			result.Content = append(result.Content, current.Content...)
			for _, key := range []string{"final_observation_kind", "final_observation_returned"} {
				if value, present := current.StructuredContent[key]; present {
					result.StructuredContent[key] = value
				}
			}
		}
	}
	return result
}

// axReadbackOnlyFailure reports an action whose input was sent and whose
// only failure is the read after it.
func axReadbackOnlyFailure(result ToolCallResult) bool {
	code, _ := result.StructuredContent["failure_code"].(string)
	phase, _ := result.StructuredContent["failure_phase"].(string)
	return code == "effect_unknown" && phase == "post_action_observation" &&
		axEvidenceReportsInputEmission(result.StructuredContent)
}

// A successful wait can replace an action's failed readback. It cannot
// replace the separately requested final observation or resolve uncertain
// input delivery. Those failures retain their own evidence and recovery gate.
func axClearReadbackFailure(structured map[string]any) {
	code, _ := structured["failure_code"].(string)
	phase, _ := structured["failure_phase"].(string)
	observationCode, _ := structured["observation_failure_code"].(string)
	if observationCode != "" || code != "effect_unknown" || phase != "post_action_observation" {
		return
	}
	for _, key := range []string{"failure_code", "failure_phase", "failure_subject",
		"underlying_failure_code", "observation_failure_code", "post_action_observation_status",
		"continuation", "effect_unknown", "mutation_unknown", "recovery"} {
		delete(structured, key)
	}
	structured["observation_required"] = false
	structured["next_identical_observation_required"] = false
}
