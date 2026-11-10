package core

import (
	"fmt"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func axBatchActionObservation(
	index,
	count int,
	final observation,
	waitsFollow bool,
) observation {
	if index == count-1 && !waitsFollow {
		// Capture the requested final state in the last action's provider call.
		return final
	}
	captureSnapshot := index < count-1
	return observation{
		mode:            contract.ObserveNone,
		captureSnapshot: &captureSnapshot,
	}
}

// axBatchLightReadback reports whether the read after action index may cover
// the window root only instead of the whole tree.
//
// A step between actions needs its read for three things: the window
// continuity check before the next action (window handle and wN), the
// before-state of the next action, and the latest read a wait step consults.
// A root-only read still answers the first: the provider selects the window
// the same way, so a dialog that opened is still detected, and it still waits
// for the UI to settle. The other two need the tree, so the read stays full
// when the next step is a wait, names a ref (its effect line compares the
// element before and after), or is the last action (its notes compare the
// trees around it). Only keys, text and scrolls without a ref qualify,
// because only they need no element from the read before them.
//
// A batch whose input ends with waits for windows only is the exception: a
// window wait reads the window list, not the tree, and the final observation
// reads the tree after the waits. The last action's own read and the read
// before it are then root-only, and its notes compare root facts only.
func axBatchLightReadback(actions []axBatchAction, index, lastInput int) bool {
	if index < 0 || index > lastInput || lastInput >= len(actions) {
		return false
	}
	if index+1 >= lastInput && !axBatchEndsWithWindowWaits(actions, lastInput) {
		return false
	}
	end := index + 2
	if end > lastInput+1 {
		end = lastInput + 1
	}
	for _, step := range actions[index:end] {
		switch step.actionType {
		case "keypress", "type", "scroll":
		default:
			return false
		}
		if step.targetRef != "" {
			return false
		}
	}
	return true
}

// axBatchEndsWithWindowWaits reports whether at least one step follows the
// last input and every such step waits for a window title only.
func axBatchEndsWithWindowWaits(actions []axBatchAction, lastInput int) bool {
	if lastInput < 0 || lastInput >= len(actions)-1 {
		return false
	}
	for _, step := range actions[lastInput+1:] {
		wait := step.wait
		if step.actionType != "wait" || wait == nil || wait.window == "" ||
			wait.text != "" || wait.ref != "" || wait.query != nil ||
			wait.state != "" || wait.value != "" || wait.settle {
			return false
		}
	}
	return true
}

func (s *Service) axBatchFinalObservation(
	context *AXBatchContext,
	obs observation,
	allowClosedWindowLifecycle bool,
) ToolCallResult {
	if context == nil || obs.resolvedMode() == contract.ObserveNone {
		return ToolCallResult{}
	}
	request := AXServiceRequest{
		Operation:          axServiceObserve,
		App:                context.AppToken,
		IncludeScreenshot:  obs.resolvedScreenshot(),
		TargetWindowHandle: context.WindowHandle,
		SourceRef:          context.WindowRef,
		Scope:              contract.AXScopeWindow,
		TreeView:           contract.TreeViewWorking,
	}
	snapshot, notes, result := s.refreshSnapshot(context.AppToken, request)
	if result.IsError {
		if current, ok := s.finalObservationContinuation(context, result); ok {
			request.TargetWindowHandle = current.WindowHandle
			request.SourceRef = current.Ref
			snapshot, notes, result = s.refreshSnapshot(context.AppToken, request)
			if !result.IsError {
				rendered := s.renderAXBatchFinalObservation(snapshot, notes, obs)
				if rendered.StructuredContent == nil {
					rendered.StructuredContent = map[string]any{}
				}
				rendered.StructuredContent["source_window_status"] = "gone"
				rendered.StructuredContent["final_observation_window_ref"] =
					current.Ref
				rendered.StructuredContent["final_observation_followed_window_transition"] =
					true
				return rendered
			}
		}
		if allowClosedWindowLifecycle &&
			closedWindowLifecycleProven(context, result) {
			return closedWindowFinalObservation(context, obs)
		}
		if result.StructuredContent == nil {
			result.StructuredContent = map[string]any{}
		}
		result.StructuredContent["failure_phase"] = "final_observation"
		result.StructuredContent["final_observation_kind"] = "unavailable"
		result.StructuredContent["final_observation_returned"] = false
		result.StructuredContent["observation_required"] = true
		return result
	}
	return s.renderAXBatchFinalObservation(snapshot, notes, obs)
}

func (s *Service) recoverAXBatchObservation(
	context *AXBatchContext,
	failed ToolCallResult,
	obs observation,
) ToolCallResult {
	if s == nil || context == nil || obs.resolvedMode() == contract.ObserveNone {
		return failed
	}
	if failed.StructuredContent == nil {
		return failed
	}
	if failed.StructuredContent["focus_preparation"] != nil || failed.StructuredContent["pointer_preparation"] != nil {
		return failed
	}
	inputEmitted := axEvidenceReportsInputEmission(failed.StructuredContent)
	partial, _ := failed.StructuredContent["partial_input_possible"].(bool)
	unknown, _ := failed.StructuredContent["mutation_unknown"].(bool)
	if !inputEmitted && !partial && !unknown {
		return failed
	}

	targetWindowHandle := context.WindowHandle
	sourceRef := context.WindowRef
	if currentRef, _ :=
		failed.StructuredContent["current_window_ref"].(string); isShortAXWindowRef(currentRef) {
		if current, err := s.WindowRefRegistry.Resolve(currentRef, ""); err == nil &&
			current.AppRef == context.AppRef &&
			validInternalWindowHandle(current.WindowHandle) {
			targetWindowHandle = current.WindowHandle
			sourceRef = current.Ref
		}
	}
	if sourceStatus, _ :=
		failed.StructuredContent["source_window_status"].(string); sourceStatus == "gone" &&
		sourceRef == context.WindowRef {
		targetWindowHandle = ""
		sourceRef = ""
	}

	request := AXServiceRequest{
		Operation:          axServiceObserve,
		App:                context.AppToken,
		IncludeScreenshot:  obs.resolvedScreenshot(),
		TargetWindowHandle: targetWindowHandle,
		SourceRef:          sourceRef,
		Scope:              contract.AXScopeWindow,
		TreeView:           contract.TreeViewWorking,
	}
	snapshot, notes, observationResult := s.refreshSnapshot(
		context.AppToken,
		request,
	)
	appRootRetryAttempted := false
	targetedFailureCode := ""
	if (targetWindowHandle != "" || sourceRef != "") &&
		axRecoveryObservationCanRetryAtAppRoot(observationResult) {
		appRootRetryAttempted = true
		targetedFailureCode, _ =
			observationResult.StructuredContent["failure_code"].(string)
		request.TargetWindowHandle = ""
		request.SourceRef = ""
		snapshot, notes, observationResult = s.refreshSnapshot(
			context.AppToken,
			request,
		)
		if !observationResult.IsError {
			notes = append(
				[]string{
					"[recovery] The window-targeted read became stale; " +
						"the current application window was read instead.",
				},
				notes...,
			)
		}
	}
	if !observationResult.IsError {
		observationResult = s.renderAXBatchFinalObservation(
			snapshot,
			append(
				[]string{
					"[recovery] Fresh current state after an uncertain mutation. " +
						"This observation does not attribute the state to the failed action.",
				},
				notes...,
			),
			obs,
		)
	}
	merged := mergeAXBatchRecoveryObservation(failed, observationResult)
	if appRootRetryAttempted && merged.StructuredContent != nil {
		merged.StructuredContent["recovery_app_root_retry_attempted"] = true
		if targetedFailureCode != "" {
			merged.StructuredContent["recovery_targeted_failure_code"] =
				targetedFailureCode
		}
	}
	return merged
}

func axRecoveryObservationCanRetryAtAppRoot(result ToolCallResult) bool {
	if !result.IsError || result.StructuredContent == nil {
		return false
	}
	code, _ := result.StructuredContent["failure_code"].(string)
	switch code {
	case "stale_window_ref", "stale_target", "observation_incomplete", "state_unavailable":
		return true
	default:
		return false
	}
}

func mergeAXBatchRecoveryObservation(
	failed,
	observationResult ToolCallResult,
) ToolCallResult {
	originalFailureCode, _ := failed.StructuredContent["failure_code"].(string)
	originalFailurePhase, _ := failed.StructuredContent["failure_phase"].(string)
	merged := mergeAXBatchFinalObservation(failed, observationResult)
	if merged.StructuredContent == nil {
		merged.StructuredContent = map[string]any{}
	}
	merged.StructuredContent["recovery_observation_attempted"] = true
	returned, _ := merged.StructuredContent["final_observation_returned"].(bool)
	merged.StructuredContent["recovery_observation_returned"] = returned
	merged.StructuredContent["cause_unattributed"] = true
	merged.StructuredContent["task_effect_verified"] = false
	if originalFailureCode != "" {
		merged.StructuredContent["failure_code"] = originalFailureCode
	}
	if originalFailurePhase != "" {
		merged.StructuredContent["failure_phase"] = originalFailurePhase
	}
	if returned {
		merged.StructuredContent["observation_required"] = false
		merged.StructuredContent["next_identical_observation_required"] = false
		merged.StructuredContent["post_action_observation_status"] =
			"recovered_current_state"
		if continuation, ok :=
			merged.StructuredContent["continuation"].(map[string]any); ok &&
			strings.TrimSpace(fmt.Sprint(continuation["state"])) ==
				"observe_required" {
			delete(merged.StructuredContent, "continuation")
		}
	} else if code, _ :=
		observationResult.StructuredContent["failure_code"].(string); code != "" {
		merged.StructuredContent["recovery_observation_failure_code"] = code
	}
	return merged
}

func (s *Service) finalObservationContinuation(
	context *AXBatchContext,
	result ToolCallResult,
) (*AXWindowRefEntry, bool) {
	if s == nil || context == nil || !result.IsError ||
		result.StructuredContent == nil {
		return nil, false
	}
	code, _ := result.StructuredContent["failure_code"].(string)
	sourceStatus, _ := result.StructuredContent["source_window_status"].(string)
	inventoryComplete, _ :=
		result.StructuredContent["recovery_inventory_complete"].(bool)
	currentRef, _ := result.StructuredContent["current_window_ref"].(string)
	// Following a replacement window needs complete lifecycle facts.
	factsComplete, _ := result.StructuredContent["failure_facts_complete"].(bool)
	if code != "stale_window_ref" ||
		sourceStatus != "gone" ||
		!inventoryComplete ||
		!factsComplete ||
		!isShortAXWindowRef(currentRef) ||
		currentRef == context.WindowRef {
		return nil, false
	}
	current, err := s.WindowRefRegistry.Resolve(currentRef, "")
	if err != nil ||
		current.AppRef == "" ||
		current.AppRef != context.AppRef ||
		!validInternalWindowHandle(current.WindowHandle) {
		return nil, false
	}
	return current, true
}

func (s *Service) renderAXBatchFinalObservation(
	snapshot *AppSnapshot,
	notes []string,
	obs observation,
) ToolCallResult {
	if snapshot == nil {
		result := TextResult(
			"final_observation_unavailable: AX returned no snapshot after the wait",
			true,
		)
		result.StructuredContent = map[string]any{
			"failure_code":               "observation_incomplete",
			"failure_phase":              "final_observation",
			"final_observation_returned": false,
			"final_observation_kind":     "unavailable",
			"observation_required":       true,
		}
		return result
	}
	if obs.resolvedMode() == contract.ObserveScreenshot {
		result, _ := renderAXScreenshotObservation(snapshot, notes)
		result.axProofSnapshot = snapshot
		result.axProofNotes = append([]string(nil), notes...)
		return result
	}

	var result ToolCallResult
	var deliveredLines []string
	var treeReturned, compact, fullTree bool
	if mode := obs.resolvedMode(); mode == contract.ObserveTree || mode == contract.ObserveFull {
		// Explicit evidence requests include the whole current working tree,
		// even when focus or a previously delivered baseline could shorten it.
		if mode == contract.ObserveTree {
			result = snapshot.withoutScreenshot().resultWithNotes(notes)
		} else {
			result = snapshot.resultWithNotes(notes)
		}
		treeReturned, fullTree = true, true
		if result.StructuredContent == nil {
			result.StructuredContent = map[string]any{}
		}
		result.StructuredContent["final_observation_kind"] = mode
	} else {
		result, deliveredLines, treeReturned, compact, fullTree =
			s.renderAXDiffOrCompactObservation(snapshot, notes)
	}
	if !treeReturned {
		return result
	}
	advanced := s.rememberDeliveredActionObservation(
		snapshot,
		result,
		deliveredLines,
		true,
		fullTree,
		compact,
	)
	if result.StructuredContent == nil {
		result.StructuredContent = map[string]any{}
	}
	result.StructuredContent["full_tree_returned"] = fullTree
	result.StructuredContent["tree_reference_only"] = false
	result.StructuredContent["final_observation_returned"] = true
	result.StructuredContent["next_identical_observation_required"] = false
	result.StructuredContent["compact_action_observation"] = compact
	result.StructuredContent["delivered_snapshot_baseline_advanced"] = advanced
	addTreeProjectionMetadata(&result, snapshot)
	result.axProofSnapshot = snapshot
	result.axProofNotes = append([]string(nil), notes...)
	return result
}

func renderAXScreenshotObservation(
	snapshot *AppSnapshot,
	notes []string,
) (ToolCallResult, bool) {
	result := snapshot.screenshotOnlyResult(notes)
	if result.StructuredContent == nil {
		result.StructuredContent = map[string]any{}
	}
	returned := snapshot != nil && snapshot.ScreenshotPNGBase64 != ""
	result.StructuredContent["final_observation_returned"] = returned
	result.StructuredContent["screenshot_returned"] = returned
	if returned {
		result.StructuredContent["final_observation_kind"] = "screenshot"
	} else {
		result.StructuredContent["final_observation_kind"] = "unavailable"
	}
	result.StructuredContent["observation_required"] = !returned
	result.StructuredContent["next_identical_observation_required"] = !returned
	return result, returned
}

func (s *Service) renderAXDiffOrCompactObservation(
	snapshot *AppSnapshot,
	notes []string,
) (ToolCallResult, []string, bool, bool, bool) {
	baseline := s.deliveredSnapshotFor(snapshot)
	if diff, ok := incrementalTree(baseline, snapshot); ok {
		result := snapshot.resultWithTreeOverride(notes, diff)
		if result.StructuredContent == nil {
			result.StructuredContent = map[string]any{}
		}
		result.StructuredContent["final_observation_kind"] = "diff"
		return result, diff, true, false, false
	}
	if contentRevisionChanged(baseline, snapshot) {
		if lines := boundedWorkingObservationLines(snapshot); len(lines) > 0 {
			result := snapshot.resultWithTreeOverride(notes, lines)
			if result.StructuredContent == nil {
				result.StructuredContent = map[string]any{}
			}
			result.StructuredContent["final_observation_kind"] = "working_tree"
			result.StructuredContent["content_transition_working_tree"] = true
			return result, lines, true, false, true
		}
	}
	lines := compactActionObservationLines(snapshot, baseline)
	if len(lines) > 0 {
		result := snapshot.resultWithTreeOverride(notes, lines)
		if result.StructuredContent == nil {
			result.StructuredContent = map[string]any{}
		}
		kind := "focused"
		switch lines[0] {
		case "content:":
			kind = "content"
		case "popup:":
			kind = "popup"
		}
		result.StructuredContent["final_observation_kind"] = kind
		return result, lines, true, true, false
	}
	lines = boundedWorkingObservationLines(snapshot)
	if len(lines) > 0 {
		result := snapshot.resultWithTreeOverride(notes, lines)
		if result.StructuredContent == nil {
			result.StructuredContent = map[string]any{}
		}
		result.StructuredContent["working_tree_fallback"] = true
		result.StructuredContent["final_observation_kind"] = "working_tree"
		return result, lines, true, false, true
	}
	result := snapshot.resultWithTreeOverride(notes, []string{
		"final_observation_unavailable: no bounded current AX subtree was available",
	})
	if result.StructuredContent == nil {
		result.StructuredContent = map[string]any{}
	}
	result.StructuredContent["full_tree_returned"] = false
	result.StructuredContent["tree_reference_only"] = false
	result.StructuredContent["compact_action_observation"] = false
	result.StructuredContent["delivered_snapshot_baseline_advanced"] = false
	result.StructuredContent["final_observation_returned"] = false
	result.StructuredContent["final_observation_kind"] = "unavailable"
	result.StructuredContent["observation_required"] = true
	result.StructuredContent["next_identical_observation_required"] = true
	return result, nil, false, false, false
}

func contentRevisionChanged(before, after *AppSnapshot) bool {
	return before != nil &&
		after != nil &&
		validContentRevision(before.contentRevision) &&
		validContentRevision(after.contentRevision) &&
		before.contentRevision != after.contentRevision
}

func (s *Service) renderAXWorkingObservation(
	snapshot *AppSnapshot,
	notes []string,
) (ToolCallResult, []string, bool, bool, bool) {
	if lines := boundedWorkingObservationLines(snapshot); len(lines) > 0 {
		result := snapshot.resultWithTreeOverride(notes, lines)
		if result.StructuredContent == nil {
			result.StructuredContent = map[string]any{}
		}
		result.StructuredContent["final_observation_kind"] = "working_tree"
		return result, lines, true, false, true
	}
	return s.renderAXDiffOrCompactObservation(snapshot, notes)
}

func boundedWorkingObservationLines(snapshot *AppSnapshot) []string {
	if snapshot == nil {
		return nil
	}
	for _, line := range snapshot.TreeLines {
		trimmed := strings.TrimSpace(line)
		if trimmed != "" && trimmed != "ax:" {
			return append([]string(nil), snapshot.TreeLines...)
		}
	}
	return nil
}

func mergeAXBatchFinalObservation(
	actionResult,
	observationResult ToolCallResult,
) ToolCallResult {
	hadPriorError := actionResult.IsError
	inputEmitted := axEvidenceReportsInputEmission(
		actionResult.StructuredContent,
	)
	observationContent := observationResult.Content
	if observationResult.IsError {
		observationContent = append([]ContentItem(nil), observationContent...)
		for index := range observationContent {
			if observationContent[index].Type == "text" {
				if index == 0 && observationResult.StructuredContent["failure_code"] == "stale_window_ref" {
					observationContent[index].Text = "final_observation_unavailable: the exact source window reference is stale."
					continue
				}
				// A provider's no-input statement describes this read, not
				// the actions whose results precede it in the same tool call.
				observationContent[index].Text = "final_observation_unavailable: read-only follow-up after the action:\n" +
					observationContent[index].Text
			}
		}
	}
	actionResult.Content = append(
		stripSupersededAXObservationPlaceholder(actionResult.Content),
		observationContent...,
	)
	if actionResult.StructuredContent == nil {
		actionResult.StructuredContent = map[string]any{}
	}
	if inputEmitted {
		actionResult.StructuredContent["input_events_emitted"] = true
	}
	if _, present := observationResult.StructuredContent["final_observation_returned"]; present {
		replaceAXObservationCoverage(actionResult.StructuredContent, observationResult.StructuredContent)
	}
	for key, value := range observationResult.StructuredContent {
		switch key {
		case "failure_code", "failure_phase":
			if actionResult.IsError {
				continue
			}
		case "preflight_only", "evidence_scope":
			if inputEmitted {
				continue
			}
		case "input_events_emitted", "partial_input_possible", "mutation_unknown":
			// These facts are monotonic across one call. A final observation
			// cannot erase input or uncertainty already recorded by the action.
			if existing, _ := actionResult.StructuredContent[key].(bool); existing {
				continue
			}
		}
		actionResult.StructuredContent[key] = value
	}
	if inputEmitted {
		actionResult.StructuredContent["preflight_only"] = false
		if unknown, _ := actionResult.StructuredContent["mutation_unknown"].(bool); unknown {
			actionResult.StructuredContent["evidence_scope"] =
				"post_action_observation"
		}
	}
	observationReturned, hasObservationStatus :=
		observationResult.StructuredContent["final_observation_returned"].(bool)
	observationUnavailable := observationResult.IsError ||
		(hasObservationStatus && !observationReturned)
	verifiedTargetAbsent, _ :=
		observationResult.StructuredContent["target_window_absent"].(bool)
	postconditionVerified, _ :=
		actionResult.StructuredContent["postcondition_verified"].(bool)
	if verifiedTargetAbsent && postconditionVerified {
		if _, present :=
			actionResult.StructuredContent["observation_required"]; !present {
			actionResult.StructuredContent["observation_required"] =
				!observationReturned
		}
		actionResult.StructuredContent["next_identical_observation_required"] = false
		clearVerifiedCloseUncertainty(actionResult.StructuredContent, nil)
		return actionResult
	}
	if observationUnavailable {
		actionResult.StructuredContent["final_observation_returned"] = false
		actionResult.StructuredContent["observation_required"] = true
		if inputEmitted && !hadPriorError {
			actionResult.IsError = false
			actionResult.StructuredContent["effect_unknown"] = true
			actionResult.StructuredContent["mutation_unknown"] = true
			actionResult.StructuredContent["post_action_observation_status"] =
				"unknown"
			if code, ok :=
				observationResult.StructuredContent["failure_code"].(string); ok {
				actionResult.StructuredContent["observation_failure_code"] = code
				delete(actionResult.StructuredContent, "failure_code")
			} else {
				actionResult.StructuredContent["observation_failure_code"] =
					"final_observation_unavailable"
			}
			delete(actionResult.StructuredContent, "failure_phase")
		} else if !hadPriorError {
			actionResult.IsError = true
		}
	}
	return actionResult
}

// replaceAXObservationCoverage makes the coverage facts describe the final
// observation, not the action's earlier read. A key the final observation
// does not set is removed, so a complete read cannot inherit an earlier gap.
func replaceAXObservationCoverage(destination, current map[string]any) {
	for _, key := range []string{
		"tree_complete_for_model", "coverage_incomplete_nodes", "coverage_incomplete_reasons",
		"structural_budget_applied", "structural_budget_omitted_nodes", "structural_budget_omitted_targets",
	} {
		delete(destination, key)
		if value, present := current[key]; present {
			destination[key] = value
		}
	}
}
