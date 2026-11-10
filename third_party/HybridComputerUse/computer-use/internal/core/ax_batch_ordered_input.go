package core

import (
	"errors"
	"fmt"
	"strings"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func wholeBatchOrderedInputRequest(
	requests []ProviderOrderedInputRequest,
	actions []axBatchAction,
) (*ProviderOrderedInputRequest, bool) {
	if len(requests) != 1 ||
		len(requests[0].Steps) != len(actions) ||
		len(actions) == 0 {
		return nil, false
	}
	for index, step := range requests[0].Steps {
		if step.ActionIndex != index ||
			actions[index].actionType != "keypress" ||
			actions[index].targetRef != "" {
			return nil, false
		}
	}
	request := requests[0]
	return &request, true
}

func (s *Service) bindOrderedInputRuntimeApp(
	request *ProviderOrderedInputRequest,
) error {
	if request == nil {
		return fmt.Errorf("ordered-input request is missing")
	}
	operation := AXServiceRequest{App: request.App}
	if err := s.bindRuntimeAppIdentity(&operation); err != nil {
		return err
	}
	if operation.AppIdentity != nil {
		if request.AppIdentity == nil ||
			*operation.AppIdentity != *request.AppIdentity {
			return fmt.Errorf(
				"ordered-input app ref identity does not match its window owner",
			)
		}
		request.App = operation.App
	}
	// The shared service binder preserves its ref for lifecycle accounting;
	// the native wire carries the display name plus exact process identity.
	if isShortAXAppRef(request.App) {
		entry, err := s.resolveShortAXAppRef(request.App)
		if err != nil {
			return err
		}
		request.App = entry.DisplayName
	}
	return request.Validate()
}

func orderedInputActionEvidence(
	actions []axBatchAction,
	result ProviderOrderedInputResult,
) []map[string]any {
	evidence := make([]map[string]any, 0, len(result.Steps))
	for position, step := range result.Steps {
		actionType := "keypress"
		if position < len(actions) {
			actionType = actions[position].actionType
		}
		entry := map[string]any{
			"action_index":       step.ActionIndex,
			"action_number":      step.ActionIndex + 1,
			"action_type":        actionType,
			"execution_path":     "provider_ordered_input",
			"status":             string(step.Status),
			"call_error":         step.Status == ProviderOrderedInputFailed,
			"dispatch_attempted": step.DispatchAttempted,
			"emission":           string(step.Emission),
			"input_events_emitted": step.Emission ==
				ProviderOrderedInputEmissionComplete ||
				step.Emission == ProviderOrderedInputEmissionPartial,
			"partial_input_possible": step.Emission ==
				ProviderOrderedInputEmissionPartial,
			"mutation_unknown": step.Status == ProviderOrderedInputFailed &&
				(step.Emission == ProviderOrderedInputEmissionComplete ||
					step.Emission == ProviderOrderedInputEmissionPartial),
		}
		if step.WindowContinuity != ProviderOrderedInputWindowUnchecked {
			entry["window_continuity"] = string(step.WindowContinuity)
		}
		evidence = append(evidence, entry)
	}
	return evidence
}

func orderedInputCompletedCount(result ProviderOrderedInputResult) int {
	count := 0
	for _, step := range result.Steps {
		if step.Status == ProviderOrderedInputCompleted {
			count++
		}
	}
	return count
}

func orderedInputBatchFacts(
	actions []axBatchAction,
	result ProviderOrderedInputResult,
	finalObservationReturned bool,
	finalObservationRequested bool,
) map[string]any {
	evidence := orderedInputActionEvidence(actions, result)
	var failedActionIndex any
	if result.StoppedActionIndex != nil {
		failedActionIndex = *result.StoppedActionIndex
	}
	rejectedBeforeFirstAction := false
	if result.StoppedActionIndex != nil &&
		*result.StoppedActionIndex == 0 &&
		len(result.Steps) > 0 {
		rejectedBeforeFirstAction = !result.Steps[0].DispatchAttempted
	}
	facts := map[string]any{
		"actions_requested":                    len(actions),
		"actions_completed_without_call_error": orderedInputCompletedCount(result),
		"failed_action_index":                  failedActionIndex,
		"rejected_before_first_action":         rejectedBeforeFirstAction,
		"partial_sequence": orderedInputCompletedCount(result) > 0 &&
			orderedInputCompletedCount(result) < len(actions),
		"final_observation_returned": finalObservationReturned,
		"observation_required":       finalObservationRequested && !finalObservationReturned,
		"actions":                    evidence,
		"any_input_emitted":          result.Mutation.InputEventsEmitted,
		"partial_input_possible":     result.Mutation.PartialInputPossible,
		"mutation_unknown":           result.Mutation.MutationUnknown,
		"execution_path":             "provider_ordered_input",
	}
	if result.FirstUnattemptedActionIndex != nil {
		facts["first_unattempted_action_index"] =
			*result.FirstUnattemptedActionIndex
	}
	return facts
}

func orderedInputCompletedBeforeObservationFailure(
	result ProviderOrderedInputResult,
	actionCount int,
) bool {
	return !result.OK &&
		result.Failure != nil &&
		result.Failure.FactsComplete &&
		result.Failure.Phase == "observation" &&
		result.StoppedActionIndex == nil &&
		result.FirstUnattemptedActionIndex == nil &&
		actionCount > 0 &&
		orderedInputCompletedCount(result) == actionCount &&
		result.Mutation.InputEventsEmitted &&
		!result.Mutation.PartialInputPossible
}

// Callers prove the entire batch completed and no predicate or final observation
// is owed. Keep the observation warning and lifecycle gate; never imply replay.
func completedBatchOptionalObservationWarning(
	result ToolCallResult,
	facts map[string]any,
) ToolCallResult {
	result.IsError = false
	result.Content = []ContentItem{{
		Type: "text",
		Text: fmt.Sprintf(
			"All %d action calls completed; the native final observation became "+
				"unavailable after input. No final observation was requested. "+
				"Task effect is unverified; observe current state before further input.",
			facts["actions_requested"],
		),
	}}
	result.StructuredContent["ordered_input_actions_completed_before_observation_failure"] = true
	result.StructuredContent["observation_required"] = true
	result.StructuredContent["final_observation_returned"] = false
	result.StructuredContent["retry_same_request"] = false
	facts["observation_required"] = true
	facts["final_observation_returned"] = false
	result.StructuredContent["a11y_batch_result"] = facts
	return result
}

func completeRecoveredOrderedInputObservation(
	result ToolCallResult,
	facts map[string]any,
) ToolCallResult {
	if result.StructuredContent == nil ||
		result.StructuredContent["recovery_observation_returned"] != true {
		return result
	}
	result.IsError = false
	for _, key := range []string{
		"failure_code",
		"failure_phase",
		"failure_facts_complete",
		"effect_unknown",
		"mutation_unknown",
		"observation_failure_code",
		"recovery_observation_failure_code",
	} {
		delete(result.StructuredContent, key)
	}
	result.StructuredContent["ordered_input_observation_recovered"] = true
	result.StructuredContent["observation_required"] = false
	result.StructuredContent["next_identical_observation_required"] = false
	result.StructuredContent["retry_same_request"] = false
	result.StructuredContent["continuation"] = map[string]any{
		"state":              "ready",
		"retry_same_request": false,
	}
	facts["failed_action_index"] = nil
	facts["partial_sequence"] = false
	facts["final_observation_returned"] = true
	facts["observation_required"] = false
	facts["mutation_unknown"] = false
	if actions, ok := facts["actions"].([]map[string]any); ok {
		for _, action := range actions {
			delete(action, "mutation_unknown")
		}
	}
	result.StructuredContent["a11y_batch_result"] = facts
	return result
}

func orderedInputFirstUnattemptedAction(
	result ProviderOrderedInputResult,
	runEnd int,
	actionCount int,
) (int, bool) {
	if result.FirstUnattemptedActionIndex != nil {
		return *result.FirstUnattemptedActionIndex, true
	}
	if len(result.Steps) == 0 {
		return 0, false
	}
	for _, step := range result.Steps {
		if step.Status == ProviderOrderedInputNotAttempted {
			return step.ActionIndex, true
		}
	}
	next := runEnd + 1
	if next < actionCount {
		return next, true
	}
	return 0, false
}

func (s *Service) orderedInputTransportFailure(
	context *AXBatchContext,
	actions []axBatchAction,
	err error,
	obs observation,
) ToolCallResult {
	var transport *RuntimeTransportError
	requestUnsent := errors.As(err, &transport) && !transport.RequestSent
	uncertain := !requestUnsent
	detail := "; the request may have reached the native provider, so do not retry " +
		"the batch until current state is observed."
	if requestUnsent {
		detail = "; no bytes of this ordered-input request reached the native provider."
	}
	result := TextResult(
		"ordered_input_transport_failed: "+err.Error()+detail,
		true,
	)
	result.StructuredContent = map[string]any{
		"failure_code":               "runtime_transport_lost",
		"failure_phase":              "ordered_input_transport",
		"input_events_emitted":       false,
		"partial_input_possible":     uncertain,
		"mutation_unknown":           uncertain,
		"retry_same_request":         false,
		"observation_required":       true,
		"final_observation_returned": false,
		"a11y_batch_result": map[string]any{
			"actions_requested":                    len(actions),
			"actions_completed_without_call_error": 0,
			"failed_action_index":                  nil,
			"rejected_before_first_action":         requestUnsent,
			"partial_sequence":                     uncertain,
			"final_observation_returned":           false,
			"observation_required":                 true,
			"actions":                              []map[string]any{},
			"any_input_emitted":                    false,
			"partial_input_possible":               uncertain,
			"mutation_unknown":                     uncertain,
			"execution_path":                       "provider_ordered_input",
		},
	}
	if obs.resolvedMode() != contract.ObserveNone {
		result = s.recoverAXBatchObservation(context, result, obs)
	}
	return result
}

type orderedInputRunExecution struct {
	nativeResult ProviderOrderedInputResult
	snapshot     *AppSnapshot
	notes        []string
	result       ToolCallResult
}

func (s *Service) stopAXOrderedInputRun(
	context *AXBatchContext,
	actions []axBatchAction,
	start, end int,
	execution orderedInputRunExecution,
	evidence []map[string]any,
	obs observation,
) ToolCallResult {
	result := execution.result
	if result.StructuredContent == nil {
		result.StructuredContent = map[string]any{}
	}
	emitted, partial, unknown := batchMutationSummary(evidence)
	emitted = emitted || result.StructuredContent["input_events_emitted"] == true
	partial = partial || result.StructuredContent["partial_input_possible"] == true
	unknown = unknown || result.StructuredContent["mutation_unknown"] == true
	completed := start + orderedInputCompletedCount(execution.nativeResult)
	var failedIndex any
	if execution.nativeResult.StoppedActionIndex != nil {
		failedIndex = *execution.nativeResult.StoppedActionIndex
	}
	facts := map[string]any{
		"actions_requested":                    len(actions),
		"actions_completed_without_call_error": completed,
		"failed_action_index":                  failedIndex,
		"rejected_before_first_action":         start == 0 && completed == 0 && !emitted && !partial && !unknown,
		"partial_sequence":                     completed > 0 && completed < len(actions),
		"final_observation_returned":           false,
		"observation_required":                 true,
		"actions":                              evidence,
		"any_input_emitted":                    emitted,
		"partial_input_possible":               partial,
		"mutation_unknown":                     unknown,
	}
	if first, known := orderedInputFirstUnattemptedAction(execution.nativeResult, end, len(actions)); known {
		facts["first_unattempted_action_index"] = first
	}
	if result.StructuredContent["provider_request_rejected"] == true {
		facts["first_unattempted_action_index"] = start
	}
	result.StructuredContent["input_events_emitted"] = emitted
	result.StructuredContent["partial_input_possible"] = partial
	result.StructuredContent["mutation_unknown"] = unknown || emitted || partial
	result.StructuredContent["final_observation_returned"] = false
	result.StructuredContent["observation_required"] = true
	// Retrying a failed segment would replay earlier actions in this request.
	result.StructuredContent["retry_same_request"] = false
	if end == len(actions)-1 && obs.resolvedMode() == contract.ObserveNone &&
		orderedInputCompletedBeforeObservationFailure(execution.nativeResult, end-start+1) {
		return completedBatchOptionalObservationWarning(result, facts)
	}
	if obs.resolvedMode() != contract.ObserveNone {
		result = s.recoverAXBatchObservation(context, result, obs)
	}
	for _, key := range []string{"final_observation_returned", "observation_required", "next_identical_observation_required"} {
		if value, present := result.StructuredContent[key]; present {
			facts[key] = value
		}
	}
	return prependAXBatchSummary(result, "", facts)
}

func (s *Service) executePlannedOrderedInputRun(
	context *AXBatchContext,
	request ProviderOrderedInputRequest,
	includeScreenshot bool,
) orderedInputRunExecution {
	backend, available := orderedInputBackend(s.platformBackend())
	if !available {
		return orderedInputRunExecution{
			result: TextResult("ordered-input backend is unavailable", true),
		}
	}
	if err := s.bindOrderedInputRuntimeApp(&request); err != nil {
		return orderedInputRunExecution{
			result: axBatchPreflightError(
				fmt.Errorf("ordered-input application binding: %w", err),
				len(request.Steps),
			),
		}
	}
	// The provider reads the tree only; the image is a desktop frame
	// captured after the run (attachDesktopFrame).
	noImage := false
	request.FinalObservation.IncludeScreenshot = &noImage
	providerStarted := time.Now()
	nativeResult, err := backend.ExecuteOrderedInput(request)
	s.countProviderTime(providerStarted)
	if err != nil {
		if result, rejected := runtimeRequestRejectionResult(err); rejected {
			return orderedInputRunExecution{result: result}
		}
		return orderedInputRunExecution{
			result: s.orderedInputTransportFailure(
				context,
				nil,
				err,
				observation{mode: contract.ObserveNone},
			),
		}
	}
	if err := nativeResult.ValidateFor(request); err != nil {
		return orderedInputRunExecution{
			result: s.orderedInputTransportFailure(
				context,
				nil,
				fmt.Errorf("invalid native result: %w", err),
				observation{mode: contract.ObserveNone},
			),
		}
	}
	if !nativeResult.OK {
		providerResponse := ProviderAccessibilityResult{
			Failure:  nativeResult.Failure,
			Mutation: &nativeResult.Mutation,
		}
		providerResponse.PrepareForService()
		s.syncRuntimeFailureLifecycle(AXServiceRequest{
			App:       context.AppToken,
			SourceRef: context.WindowRef,
		}, providerResponse)
		failureCode := "provider_error"
		failurePhase := "ordered_input"
		if nativeResult.Failure != nil {
			if strings.TrimSpace(nativeResult.Failure.Code) != "" {
				failureCode = nativeResult.Failure.Code
			}
			if strings.TrimSpace(nativeResult.Failure.Phase) != "" {
				failurePhase = nativeResult.Failure.Phase
			}
		}
		result := TextResult(
			fmt.Sprintf(
				"Stopped during ordered keyboard input: %s during %s.",
				failureCode,
				failurePhase,
			),
			true,
		)
		result.StructuredContent = map[string]any{
			"failure_code":           failureCode,
			"failure_phase":          failurePhase,
			"input_events_emitted":   nativeResult.Mutation.InputEventsEmitted,
			"partial_input_possible": nativeResult.Mutation.PartialInputPossible,
			"mutation_unknown":       nativeResult.Mutation.MutationUnknown,
			"retry_same_request":     nativeResult.RetrySameRequest,
		}
		if failure := nativeResult.Failure; failure != nil {
			result.StructuredContent["failure_facts_complete"] = failure.FactsComplete
			if failure.Subject == "app" || failure.Subject == "window" {
				result.StructuredContent["failure_subject"] = failure.Subject
			}
			if failure.UnderlyingCode != "" {
				result.StructuredContent["underlying_failure_code"] = failure.UnderlyingCode
			}
		}
		return orderedInputRunExecution{
			nativeResult: nativeResult,
			result:       result,
		}
	}

	providerResponse := &ProviderAccessibilityResult{
		OK:       true,
		Snapshot: nativeResult.Snapshot,
		Mutation: &nativeResult.Mutation,
	}
	snapshotRequest := AXServiceRequest{
		Operation:         axServiceObserve,
		App:               context.AppToken,
		IncludeScreenshot: &includeScreenshot,
		Scope:             contract.AXScopeWindow,
		TreeView:          contract.TreeViewWorking,
	}
	snapshot, preparationResult := s.prepareProviderSnapshot(
		snapshotRequest,
		providerResponse,
	)
	if preparationResult.IsError {
		if preparationResult.StructuredContent == nil {
			preparationResult.StructuredContent = map[string]any{}
		}
		preparationResult.StructuredContent["input_events_emitted"] = true
		preparationResult.StructuredContent["mutation_unknown"] = true
		return orderedInputRunExecution{
			nativeResult: nativeResult,
			result:       preparationResult,
		}
	}
	var frameNotes []string
	if includeScreenshot {
		frameNotes = s.attachDesktopFrame(snapshot)
	}
	snapshot, notes, commitResult := s.commitPreparedSnapshot(
		context.AppToken,
		snapshotRequest,
		snapshot,
		frameNotes,
	)
	if commitResult.IsError {
		return orderedInputRunExecution{
			nativeResult: nativeResult,
			result:       commitResult,
		}
	}
	result := ToolCallResult{}
	result.StructuredContent = map[string]any{
		"action_attempted":       true,
		"input_events_emitted":   nativeResult.Mutation.InputEventsEmitted,
		"partial_input_possible": nativeResult.Mutation.PartialInputPossible,
		"mutation_unknown":       nativeResult.Mutation.MutationUnknown,
		"retry_same_request":     false,
	}
	return orderedInputRunExecution{
		nativeResult: nativeResult,
		snapshot:     snapshot,
		notes:        notes,
		result:       result,
	}
}

func (s *Service) executeWholeBatchOrderedInput(
	context *AXBatchContext,
	actions []axBatchAction,
	executionPlan axBatchExecutionPlan,
	obs observation,
) (ToolCallResult, bool) {
	_, available := orderedInputBackend(s.platformBackend())
	if !available {
		return ToolCallResult{}, false
	}
	requests, err := providerOrderedInputRequestsFromPlan(
		context,
		executionPlan,
		actions,
	)
	if err != nil {
		return axBatchValidationError(
			"ordered-input compilation: "+err.Error(),
			len(actions),
		), true
	}
	request, eligible := wholeBatchOrderedInputRequest(requests, actions)
	if !eligible {
		return ToolCallResult{}, false
	}
	execution := s.executePlannedOrderedInputRun(
		context,
		*request,
		*obs.resolvedScreenshot(),
	)
	nativeResult := execution.nativeResult

	if execution.result.IsError {
		result := execution.result
		if result.StructuredContent == nil {
			result.StructuredContent = map[string]any{}
		}
		result.StructuredContent["observation_required"] = true
		result.StructuredContent["final_observation_returned"] = false
		facts := orderedInputBatchFacts(
			actions,
			nativeResult,
			false,
			obs.resolvedMode() != contract.ObserveNone,
		)
		// Transport loss has no validated per-step result. Preserve the
		// uncertain sequence boundary instead of claiming an untouched batch.
		if len(nativeResult.Steps) == 0 && result.StructuredContent["partial_input_possible"] == true {
			facts["partial_sequence"] = true
		}
		original, _ := result.StructuredContent["a11y_batch_result"].(map[string]any)
		if result.StructuredContent["provider_request_rejected"] == true ||
			(original["rejected_before_first_action"] == true && len(nativeResult.Steps) == 0) {
			facts["rejected_before_first_action"] = true
			facts["first_unattempted_action_index"] = 0
		}
		for _, field := range []string{
			"input_events_emitted",
			"partial_input_possible",
			"mutation_unknown",
		} {
			if value, present := result.StructuredContent[field].(bool); present {
				factField := field
				if field == "input_events_emitted" {
					factField = "any_input_emitted"
				}
				facts[factField] = value
			}
		}
		if orderedInputCompletedBeforeObservationFailure(
			nativeResult,
			len(actions),
		) {
			if obs.resolvedMode() == contract.ObserveNone {
				return completedBatchOptionalObservationWarning(result, facts), true
			}
			result.IsError = false
			result.Content = []ContentItem{{
				Type: "text",
				Text: fmt.Sprintf(
					"Executed %d ordered keypress action(s) in the bound AX window; "+
						"the native final observation became unavailable after input.",
					len(actions),
				),
			}}
			result.StructuredContent["ordered_input_actions_completed_before_observation_failure"] = true
			result.StructuredContent["a11y_batch_result"] = facts
			result = s.recoverAXBatchObservation(context, result, obs)
			result = completeRecoveredOrderedInputObservation(result, facts)
			if result.StructuredContent["recovery_observation_returned"] != true {
				result.IsError = true
			}
			return result, true
		}
		result.StructuredContent["a11y_batch_result"] = facts
		if obs.resolvedMode() != contract.ObserveNone {
			result = s.recoverAXBatchObservation(context, result, obs)
		}
		return result, true
	}

	result := execution.result
	result.StructuredContent["final_observation_returned"] = false
	finalObservationReturned := false
	if obs.resolvedMode() != contract.ObserveNone {
		finalObservation := s.renderAXBatchFinalObservation(
			execution.snapshot,
			execution.notes,
			obs,
		)
		result = mergeAXBatchFinalObservation(result, finalObservation)
		finalObservationReturned, _ =
			result.StructuredContent["final_observation_returned"].(bool)
	}
	result.StructuredContent["a11y_batch_result"] =
		orderedInputBatchFacts(
			actions,
			nativeResult,
			finalObservationReturned,
			obs.resolvedMode() != contract.ObserveNone,
		)
	result.StructuredContent["observation_required"] =
		obs.resolvedMode() != contract.ObserveNone && !finalObservationReturned
	result.StructuredContent["next_identical_observation_required"] =
		result.StructuredContent["observation_required"]
	return result, true
}
