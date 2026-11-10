package core

import (
	"fmt"
	"strconv"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

type axBatchAction struct {
	actionType string
	targetRef  string
	// toRef is the range end of a select action.
	toRef     string
	direction string
	pages     float64
	text      string
	key       string
	value     string
	title     string
	// wait is set for a wait step, which sends no input.
	wait *axWait
}

func (s *Service) executeAXBatchAction(
	context *AXBatchContext,
	action axBatchAction,
	obs observation,
) ToolCallResult {
	const noDeclaredTarget = ""
	switch action.actionType {
	case "activate", "close":
		return s.windowAction(
			context.AppToken,
			action.targetRef,
			noDeclaredTarget,
			action.actionType,
			obs,
		).withEvidenceScope("delivery_or_local_ui")
	case "click":
		return s.click(
			context.AppToken,
			action.targetRef,
			noDeclaredTarget,
			nil,
			nil,
			1,
			"left",
			"auto",
			obs,
		).withEvidenceScope("delivery_or_local_ui")
	case "keypress":
		return s.pressKey(
			context.AppToken,
			action.targetRef,
			noDeclaredTarget,
			context.WindowHandle,
			context.expectedWindow,
			action.key,
			obs,
		).withEvidenceScope("delivery_or_local_ui")
	case "scroll":
		return s.scroll(
			context.AppToken,
			action.direction,
			action.targetRef,
			noDeclaredTarget,
			context.WindowHandle,
			context.expectedWindow,
			action.pages,
			obs,
		).withEvidenceScope("delivery_or_local_ui")
	case "set_value":
		return s.setValue(
			context.AppToken,
			action.targetRef,
			noDeclaredTarget,
			action.value,
			obs,
			context.windowsQueuedInputPending,
		).withEvidenceScope("delivery_or_local_ui")
	case "select":
		return s.selectRange(
			context.AppToken,
			action.targetRef,
			action.toRef,
			action.text,
			obs,
		).withEvidenceScope("delivery_or_local_ui")
	case "type":
		return s.typeText(
			context.AppToken,
			action.targetRef,
			noDeclaredTarget,
			context.WindowHandle,
			context.expectedWindow,
			action.text,
			obs,
		).withEvidenceScope("delivery_or_local_ui")
	default:
		return TextResult(
			fmt.Sprintf("unsupported AX action %q", action.actionType),
			true,
		).withEvidenceScope("error_only")
	}
}

// axBatchDecoded runs decoded actions in one window. Window titles and
// the focused window are resolved here, once, before any input; after that
// the batch uses only the resolved wN.
func (s *Service) axBatchDecoded(
	actions []axBatchAction,
	obs observation,
	windowRef string,
) ToolCallResult {
	requested := len(actions)
	if axBatchOnlyWaits(actions) {
		return s.axWaitOnly(actions, obs, windowRef)
	}
	bindingNotes := []string{}
	for index := range actions {
		if actions[index].title == "" {
			continue
		}
		window, argErr := s.resolveWindowTitle(actions[index].title)
		if argErr != nil {
			argErr.path = fmt.Sprintf("actions[%d].title", index)
			return axArgErrorResult(argErr, requested)
		}
		actions[index].targetRef = window.ref
		bindingNotes = append(bindingNotes, fmt.Sprintf(
			"title %s -> %s %s (%s)", strconv.Quote(actions[index].title),
			window.ref, strconv.Quote(window.title), window.appName))
	}
	if window := s.axNoTreeBatchWindow(actions, windowRef); window != nil {
		return s.axNoTreeWindowBatch(actions, window, bindingNotes, requested)
	}
	var focus *axDesktopWindow
	if windowRef == "" && !axBatchHasRef(actions) {
		var argErr *axArgError
		focus, argErr = s.resolveFocusedWindow()
		if argErr != nil {
			return axArgErrorResult(argErr, requested)
		}
		bindingNotes = append(bindingNotes, fmt.Sprintf(
			"bound_window: %s %s (%s, focused at preflight)",
			focus.ref, strconv.Quote(focus.title), focus.appName))
	}
	result := s.axBatchBound(actions, obs, windowRef, focus, requested)
	if len(bindingNotes) > 0 {
		at := 0
		if len(result.Content) > 0 && isActReportText(result.Content[0].Text) {
			at = 1
		}
		notes := ContentItem{Type: "text", Text: strings.Join(bindingNotes, "\n")}
		result.Content = append(result.Content[:at:at],
			append([]ContentItem{notes}, result.Content[at:]...)...)
	}
	return result
}

func axBatchHasRef(actions []axBatchAction) bool {
	for _, action := range actions {
		if action.targetRef != "" {
			return true
		}
	}
	return false
}

// axBatchBound runs the batch and puts the act report first.
func (s *Service) axBatchBound(
	actions []axBatchAction,
	obs observation,
	windowRef string,
	focus *axDesktopWindow,
	requested int,
) ToolCallResult {
	trace := newActReportTrace(obs)
	result := s.axBatchRun(actions, obs, windowRef, focus, requested, trace)
	return s.withActReport(result, actions, trace)
}

func (s *Service) axBatchRun(
	actions []axBatchAction,
	obs observation,
	windowRef string,
	focus *axDesktopWindow,
	requested int,
	trace *actReportTrace,
) ToolCallResult {
	if err := s.preflightPlatformAXKeys(actions); err != nil {
		return axBatchPreflightError(err, requested)
	}
	if s.platformBackend().HostInfo().Platform == "windows" {
		if err := preflightWindowsAXKeys(actions); err != nil {
			return axBatchPreflightError(err, requested)
		}
	}
	context, err := s.deriveAXBatchContext(windowRef, actions, focus)
	if err != nil {
		return axBatchPreflightError(err, requested)
	}
	context.report = trace
	trace.bind(s, context)
	lastInput := -1
	for index, action := range actions {
		if action.actionType != "wait" {
			lastInput = index
			continue
		}
		if action.wait.ref == "" {
			continue
		}
		if err := s.preflightAXWaitRef(context, action.wait.ref); err != nil {
			return axBatchPreflightError(fmt.Errorf("actions[%d].ref: %w", index, err), requested)
		}
	}

	executionPlan, err := compileAXBatchExecutionPlan(context, actions, obs)
	if err != nil {
		return axBatchPreflightError(err, requested)
	}
	orderedRuns := map[int]ProviderOrderedInputRequest{}
	// Wait steps poll between actions on the per-action path; the ordered
	// provider path runs keys back to back.
	guarded := context.boundByFocus || lastInput < len(actions)-1
	for _, action := range actions {
		guarded = guarded || action.actionType == "wait"
	}
	if _, available := orderedInputBackend(s.platformBackend()); available && !guarded {
		requests, err := providerOrderedInputRequestsFromPlan(context, executionPlan, actions)
		if err != nil {
			return axBatchPreflightError(err, requested)
		}
		if result, used := s.executeWholeBatchOrderedInput(context, actions, executionPlan, obs); used {
			return result
		}
		for _, request := range requests {
			orderedRuns[request.Steps[0].ActionIndex] = request
		}
	}
	actionEvidence := make([]map[string]any, 0, len(actions))
	waitNotes := []string{}
	for index := 0; index < len(actions); index++ {
		action := actions[index]
		if action.actionType == "wait" {
			// A wait step between actions: it sends no input.
			trace.recordBefore(s, index)
			waitResult := s.runAXWait(context, action.wait)
			actionEvidence = append(actionEvidence, axWaitEvidenceEntry(index, waitResult))
			if !axPostconditionPassed(waitResult) {
				return s.stopAXBatchAtWait(context, actions, index, waitResult, actionEvidence, waitNotes, obs)
			}
			waitNotes = append(waitNotes, firstLine(ResultText(waitResult)))
			s.rebindAfterWindowWait(context, action.wait, waitResult)
			if index < len(actions)-1 && actions[index+1].actionType != "wait" {
				if stopped, changed := s.axBatchContinuityStop(context, actions, index, actionEvidence, obs); changed {
					return stopped
				}
			}
			continue
		}
		actionObservation := axBatchActionObservation(
			index,
			len(actions),
			obs,
			index < len(actions)-1 && index == lastInput,
		)
		actionObservation.lightReadback = axBatchLightReadback(actions, index, lastInput)
		windowAction := action.actionType == "activate" ||
			action.actionType == "close"
		if context.boundByFocus {
			// Keys and text without a ref go to the focused window. If focus
			// left the bound window, stop instead of sending input elsewhere.
			if stopped, changed := s.axBatchFocusChanged(context, index, len(actions), actionEvidence); changed {
				return s.recoverAXBatchObservation(context, stopped, obs)
			}
		}
		context.settleBaseline = nil
		if axSettleWaitFollows(actions, index) {
			context.settleBaseline = s.readAXSettleState(context, nil)
		}
		trace.recordBefore(s, index)
		result := ToolCallResult{}
		actionEntry := map[string]any{}
		request, orderedRun := orderedRuns[index]
		if orderedRun {
			runEnd := index + len(request.Steps) - 1
			execution := s.executePlannedOrderedInputRun(context, request,
				runEnd == len(actions)-1 && *obs.resolvedScreenshot())
			result = execution.result
			runEvidence := orderedInputActionEvidence(actions[index:runEnd+1], execution.nativeResult)
			actionEvidence = append(actionEvidence, runEvidence...)
			if result.IsError {
				return s.stopAXOrderedInputRun(context, actions, index, runEnd,
					execution, actionEvidence, obs)
			}
			actionEntry = runEvidence[len(runEvidence)-1]
			index = runEnd
			action = actions[index]
			if index == len(actions)-1 && obs.resolvedMode() != contract.ObserveNone {
				result = mergeAXBatchFinalObservation(result,
					s.renderAXBatchFinalObservation(execution.snapshot, execution.notes, obs))
			}
		} else {
			if context.windowsQueuedInputPending && action.actionType == "type" && action.text != "" {
				result = TextResult("Earlier queued input has not been confirmed processed; no text was sent. Observe and confirm focus and selection before sending text separately; do not repeat the earlier input.", true)
				result.StructuredContent = map[string]any{
					"failure_code": "desktop_input_failed", "failure_phase": "preparation",
					"input_events_emitted": false, "mutation_unknown": true,
					"observation_required": true,
				}
			} else {
				result = s.executeAXBatchAction(context, action, actionObservation)
			}
			if s.platformBackend().HostInfo().Platform == "windows" &&
				(action.actionType == "keypress" || action.actionType == "click" || action.actionType == "scroll") {
				context.windowsQueuedInputPending = true
			}
			if s.platformBackend().HostInfo().Platform == "windows" {
				if evidence, ok := result.StructuredContent["runtime_action_evidence"].(RuntimeActionEvidence); ok &&
					evidence.Route == "uia.text_select_and_send_input.document_replace" {
					context.windowsQueuedInputPending = true
				}
			}
			if !result.IsError ||
				(action.actionType == "close" &&
					axEvidenceReportsInputEmission(result.StructuredContent)) {
				result = s.recoverClosedWindowActionObservation(
					context,
					action,
					result,
					obs,
				)
			}
			actionEntry = batchActionEvidenceEntry(index, action.actionType, result)
			context.settleBaseline.notePixelChange(result)
		}
		if result.IsError && index+1 < len(actions) && actions[index+1].actionType == "wait" &&
			axReadbackOnlyFailure(result) {
			// The input was sent and only the read after it failed, for
			// example because the window closed or the application exited.
			// The wait that follows is the check the caller asked for, so it
			// decides instead of this read.
			result.IsError = false
			actionEntry["call_error"] = false
		}
		if result.IsError {
			actionEvidence = append(actionEvidence, actionEntry)
			anyInputEmitted, anyPartialPossible, anyMutationUnknown :=
				batchMutationSummary(
					actionEvidence,
				)
			facts := map[string]any{
				"actions_requested":                    len(actions),
				"actions_completed_without_call_error": index,
				"failed_action_index":                  index,
				"failed_action_number":                 index + 1,
				"failed_action_type":                   action.actionType,
				"rejected_before_first_action":         false,
				"partial_sequence":                     index > 0,
				"final_observation_returned":           false,
				"actions":                              actionEvidence,
				"any_input_emitted":                    anyInputEmitted,
				"partial_input_possible":               anyPartialPossible,
			}
			if result.StructuredContent == nil {
				result.StructuredContent = map[string]any{}
			}
			finalObservationReturned, _ :=
				result.StructuredContent["final_observation_returned"].(bool)
			preparationFailed := result.StructuredContent["focus_preparation"] != nil ||
				result.StructuredContent["pointer_preparation"] != nil
			if !finalObservationReturned && !preparationFailed {
				result.StructuredContent["observation_required"] = true
			}
			result.StructuredContent["partial_sequence_possible"] = index > 0
			result.StructuredContent["input_events_emitted"] = anyInputEmitted
			result.StructuredContent["partial_input_possible"] =
				anyPartialPossible
			result.StructuredContent["mutation_unknown"] =
				anyMutationUnknown || anyInputEmitted || anyPartialPossible
			result = s.recoverAXBatchObservation(context, result, obs)
			finalObservationReturned, _ =
				result.StructuredContent["final_observation_returned"].(bool)
			facts["final_observation_returned"] = finalObservationReturned
			facts["observation_required"] =
				result.StructuredContent["observation_required"]
			observationSummary := "No final batch observation was returned; " +
				"observe fresh state before recovery."
			if preparationFailed {
				observationSummary = "Preparation may have changed state or may complete later. " +
					"Stop; no recovery observation, wait or later action was attempted."
			}
			if finalObservationReturned {
				observationSummary = "A fresh current-state observation follows. " +
					"It does not prove which action caused that state."
				if result.StructuredContent["action_outcome"] == "not_executed" {
					observationSummary = "A fresh independent current-state " +
						"observation follows. The failed action emitted no input " +
						"and did not cause that state."
				}
			}
			return prependAXBatchSummary(
				result,
				fmt.Sprintf(
					"Stopped at action %d/%d (%s). "+
						"%d earlier action(s) returned without error; "+
						"that does not verify their effect. %s",
					index+1, len(actions), action.actionType, index,
					observationSummary,
				),
				facts,
			)
		}
		if !orderedRun {
			actionEvidence = append(actionEvidence, actionEntry)
		}
		if index < lastInput && actions[index+1].actionType != "wait" {
			if stopped, changed := s.axBatchContinuityStop(context, actions, index, actionEvidence, obs); changed {
				return stopped
			}
			continue
		}
		if index < lastInput {
			continue
		}
		if index == lastInput {
			result = compactAXActionContent(result)
			// Waits after the last action run before the final observation,
			// which then shows the state they waited for.
			for next := index + 1; next < len(actions); next++ {
				trace.recordBefore(s, next)
				waitResult := s.runAXWait(context, actions[next].wait)
				actionEvidence = append(actionEvidence, axWaitEvidenceEntry(next, waitResult))
				if !axPostconditionPassed(waitResult) {
					return s.stopAXBatchAtWait(context, actions, next, waitResult, actionEvidence, waitNotes, obs)
				}
				waitNotes = append(waitNotes, firstLine(ResultText(waitResult)))
				s.rebindAfterWindowWait(context, actions[next].wait, waitResult)
			}
			if index < len(actions)-1 && obs.resolvedMode() != contract.ObserveNone &&
				action.actionType != "close" {
				result = mergeAXBatchFinalObservation(result,
					s.axBatchFinalObservation(context, obs, true))
			}
			if index < len(actions)-1 {
				// The waits after the last input held: the awaited state
				// was read live, which answers a read that failed after the
				// input (for example, the window closed or the app exited).
				result.StructuredContent["wait_verified"] = true
				axClearReadbackFailure(result.StructuredContent)
			}
			if len(waitNotes) > 0 {
				result.Content = append(
					[]ContentItem{{Type: "text", Text: strings.Join(waitNotes, "\n")}},
					result.Content...,
				)
			}
			if obs.resolvedMode() != contract.ObserveNone && !windowAction {
				if required, _ :=
					result.StructuredContent["observation_required"].(bool); required {
					result = s.recoverAXBatchObservation(
						context,
						result,
						obs,
					)
				}
			}
			finalObservationReturned := obs.resolvedMode() != contract.ObserveNone
			if value, ok := result.StructuredContent["final_observation_returned"].(bool); ok {
				finalObservationReturned = value
			}
			observationRequired := !finalObservationReturned &&
				obs.resolvedMode() != contract.ObserveNone
			if value, present :=
				result.StructuredContent["observation_required"].(bool); present {
				observationRequired = value
			} else {
				result.StructuredContent["observation_required"] =
					observationRequired
			}
			nextIdenticalObservationRequired := observationRequired
			if value, present :=
				result.StructuredContent["next_identical_observation_required"].(bool); present {
				nextIdenticalObservationRequired = value
			} else {
				result.StructuredContent["next_identical_observation_required"] =
					nextIdenticalObservationRequired
			}
			facts := map[string]any{
				"actions_requested":                    len(actions),
				"actions_completed_without_call_error": len(actions),
				"failed_action_index":                  nil,
				"rejected_before_first_action":         false,
				"partial_sequence":                     false,
				"final_observation_returned":           finalObservationReturned,
				"observation_required":                 observationRequired,
				"next_identical_observation_required":  nextIdenticalObservationRequired,
				"actions":                              actionEvidence,
			}
			if index < len(actions)-1 {
				facts["final_observation_phase"] = "after_wait"
			} else {
				facts["final_observation_action_index"] = index
			}
			return prependAXBatchSummary(
				result,
				"",
				facts,
			)
		}
	}
	panic("decodeAXActions returned an empty action sequence")
}
