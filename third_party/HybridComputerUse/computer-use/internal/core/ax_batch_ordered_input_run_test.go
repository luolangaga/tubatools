package core

import (
	"errors"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"testing"
)

func orderedInputBatchTestSnapshot(
	request ProviderOrderedInputRequest,
) *ProviderSnapshot {
	snapshot := orderedInputTestSnapshot()
	snapshot.App = AppDescriptor{
		Name:              request.App,
		PID:               10,
		ProviderBusName:   request.AppIdentity.ProviderBusName,
		ProcessGeneration: request.AppIdentity.ProcessGeneration,
		InputCapable:      true,
	}
	snapshot.WindowHandle = request.WindowHandle
	for index := range snapshot.Elements {
		snapshot.Elements[index].ProviderWindowBusName =
			request.ExpectedWindow.ProviderBusName
		snapshot.Elements[index].ProviderWindowObjectPath =
			request.ExpectedWindow.ProviderObjectPath
		snapshot.Elements[index].ProviderWindowProcessGeneration =
			request.ExpectedWindow.ProcessGeneration
		snapshot.Elements[index].ProviderWindowRole =
			request.ExpectedWindow.Role
		snapshot.Elements[index].ProviderWindowName =
			request.ExpectedWindow.Name
	}
	return snapshot
}

func mixedOrderedInputBatchTestSnapshot() *ProviderSnapshot {
	request := testProviderOrderedInputRequest("ENTER")
	request.App = "LibreOffice"
	request.AppIdentity = &ProviderAppIdentity{
		ProviderBusName:   ":1.84",
		ProcessGeneration: "pid:10:start:100",
	}
	request.WindowHandle = "w2:0123456789abcdef0123456789abcdef"
	request.ExpectedWindow = &ProviderWindowExpectation{
		WindowHandle:       request.WindowHandle,
		ProviderBusName:    ":1.84",
		ProcessGeneration:  "pid:10:start:100",
		ProviderObjectPath: "/window/1",
		Role:               "frame",
		Name:               "Sheet",
	}
	snapshot := orderedInputBatchTestSnapshot(request)
	for index := range snapshot.Elements {
		snapshot.Elements[index].ProviderBusName = ":1.84"
		snapshot.Elements[index].ProcessGeneration = "pid:10:start:100"
	}
	snapshot.Elements[3].ProviderObjectPath = "/window/1/save"
	return snapshot
}

func completedOrderedInputObservationFailure(
	request ProviderOrderedInputRequest,
) ProviderOrderedInputResult {
	result := testProviderOrderedInputSuccess(request)
	result.OK = false
	result.Snapshot = nil
	result.Failure = &ProviderFailure{
		Code:          "stale_window_ref",
		Phase:         "observation",
		FactsComplete: true,
		Subject:       "window",
	}
	result.Mutation.MutationUnknown = true
	return result
}

func TestAXMixedBatchExecutesEveryPlannedOrderedInputRun(t *testing.T) {
	service, _, _, elementRef := orderedInputTestService(t)
	legacy := &recordingPlatformBackend{
		accessibilityResponse: &ProviderAccessibilityResult{
			OK:       true,
			Snapshot: mixedOrderedInputBatchTestSnapshot(),
			Mutation: &ProviderMutationFacts{InputEventsEmitted: true},
		},
	}
	backend := &orderedInputRecordingBackend{
		recordingPlatformBackend: legacy,
		execute: func(request ProviderOrderedInputRequest) ProviderOrderedInputResult {
			result := testProviderOrderedInputSuccess(request)
			result.Snapshot = orderedInputBatchTestSnapshot(request)
			return result
		},
	}
	service.Backend = backend

	result := service.axBatch([]any{
		map[string]any{
			"type": "click",
			"ref":  elementRef,
		},
		map[string]any{"type": "keypress", "keys": []any{"DOWN"}},
		map[string]any{"type": "keypress", "keys": []any{"ENTER"}},
		map[string]any{"type": "type", "text": "not-run"},
		map[string]any{"type": "keypress", "keys": []any{"TAB"}},
	},
		observation{mode: contract.ObserveNone},
		"",
	)
	if result.IsError {
		t.Fatalf("mixed ordered batch failed: %s\n%#v", ResultText(result), result)
	}
	if len(backend.requests) != 2 {
		t.Fatalf("ordered requests=%d, want 2: %#v",
			len(backend.requests), backend.requests)
	}
	if len(legacy.actionRequests) != 2 {
		t.Fatalf("legacy semantic actions=%d, want 2: %#v",
			len(legacy.actionRequests), legacy.actionRequests)
	}
	if backend.requests[0].Steps[0].ActionIndex != 1 ||
		backend.requests[0].Steps[1].ActionIndex != 2 ||
		backend.requests[1].Steps[0].ActionIndex != 4 {
		t.Fatalf("ordered runs lost global indices: %#v", backend.requests)
	}
	facts, _ := result.StructuredContent["a11y_batch_result"].(map[string]any)
	entries, _ := facts["actions"].([]map[string]any)
	if len(entries) != 5 ||
		entries[1]["execution_path"] != "provider_ordered_input" ||
		entries[2]["execution_path"] != "provider_ordered_input" ||
		entries[4]["execution_path"] != "provider_ordered_input" ||
		facts["actions_completed_without_call_error"] != 5 {
		t.Fatalf("mixed batch evidence is incomplete: %#v", facts)
	}
}

func TestAXMixedBatchStopsOnOrderedPartialEmissionWithoutRetry(t *testing.T) {
	service, _, _, elementRef := orderedInputTestService(t)
	legacy := &recordingPlatformBackend{
		accessibilityResponse: &ProviderAccessibilityResult{
			OK:       true,
			Snapshot: mixedOrderedInputBatchTestSnapshot(),
			Mutation: &ProviderMutationFacts{InputEventsEmitted: true},
		},
	}
	backend := &orderedInputRecordingBackend{
		recordingPlatformBackend: legacy,
		execute: func(request ProviderOrderedInputRequest) ProviderOrderedInputResult {
			stopped := request.Steps[1].ActionIndex
			return ProviderOrderedInputResult{
				ContractVersion: ProviderOrderedInputContractVersion,
				Steps: []ProviderOrderedInputStepResult{
					{
						ActionIndex:       request.Steps[0].ActionIndex,
						Status:            ProviderOrderedInputCompleted,
						DispatchAttempted: true,
						Emission:          ProviderOrderedInputEmissionComplete,
						WindowContinuity:  ProviderOrderedInputWindowSame,
					},
					{
						ActionIndex:       stopped,
						Status:            ProviderOrderedInputFailed,
						DispatchAttempted: true,
						Emission:          ProviderOrderedInputEmissionPartial,
						WindowContinuity:  ProviderOrderedInputWindowSame,
					},
				},
				StoppedActionIndex: &stopped,
				Failure: &ProviderFailure{
					Code:          "input_emission_failed",
					Phase:         "emission",
					FactsComplete: true,
				},
				Mutation: ProviderMutationFacts{
					InputEventsEmitted:   true,
					PartialInputPossible: true,
					DesktopStateMutated:  true,
					MutationUnknown:      true,
				},
			}
		},
	}
	service.Backend = backend

	result := service.axBatch([]any{
		map[string]any{
			"type": "click",
			"ref":  elementRef,
		},
		map[string]any{"type": "keypress", "keys": []any{"A"}},
		map[string]any{"type": "keypress", "keys": []any{"B"}},
		map[string]any{"type": "type", "text": "not-run"},
	},
		observation{mode: contract.ObserveNone},
		"",
	)
	if !result.IsError {
		t.Fatalf("partial ordered emission was reported as success: %#v", result)
	}
	if len(backend.requests) != 1 || len(legacy.actionRequests) != 1 {
		t.Fatalf("failed ordered run was retried or later action ran: ordered=%d legacy=%d",
			len(backend.requests), len(legacy.actionRequests))
	}
	facts, _ := result.StructuredContent["a11y_batch_result"].(map[string]any)
	entries, _ := facts["actions"].([]map[string]any)
	if facts["failed_action_index"] != 2 ||
		facts["actions_completed_without_call_error"] != 2 ||
		facts["first_unattempted_action_index"] != 3 ||
		len(entries) != 3 ||
		entries[2]["emission"] != "partial" ||
		result.StructuredContent["partial_input_possible"] != true {
		t.Fatalf("partial mixed evidence is incomplete: %#v / %#v",
			facts, result.StructuredContent)
	}
	public := publicBatchEvidence(t, result)
	batch := assertPublicBatchCounts(t, public, 4, 2)
	if batch["failed_action_index"] != float64(2) ||
		public["partial_input_possible"] != true ||
		public["mutation_unknown"] != true {
		t.Fatalf("public partial boundary lost: %#v", public)
	}
}

func TestAXWholeOrderedTransportFailurePreservesUncertainty(t *testing.T) {
	service, _, windowRef, _ := orderedInputTestService(t)
	service.Backend = &orderedInputRecordingBackend{
		recordingPlatformBackend: &recordingPlatformBackend{},
		err:                      errors.New("connection closed after request write"),
	}

	result := service.axBatch([]any{
		map[string]any{"type": "keypress", "keys": []any{"A"}},
		map[string]any{"type": "keypress", "keys": []any{"B"}},
	},
		observation{mode: contract.ObserveNone},
		windowRef,
	)
	if !result.IsError {
		t.Fatalf("ordered transport failure was reported as success: %#v", result)
	}
	facts, _ := result.StructuredContent["a11y_batch_result"].(map[string]any)
	if facts["actions_requested"] != 2 ||
		facts["rejected_before_first_action"] != false ||
		facts["partial_input_possible"] != true ||
		facts["mutation_unknown"] != true ||
		result.StructuredContent["partial_input_possible"] != true ||
		result.StructuredContent["mutation_unknown"] != true {
		t.Fatalf("whole ordered transport uncertainty was lost: %#v / %#v",
			facts, result.StructuredContent)
	}
}

func TestAXMixedOrderedTransportFailurePreservesUncertainty(t *testing.T) {
	service, _, _, elementRef := orderedInputTestService(t)
	legacy := &recordingPlatformBackend{
		accessibilityResponse: &ProviderAccessibilityResult{
			OK:       true,
			Snapshot: mixedOrderedInputBatchTestSnapshot(),
			Mutation: &ProviderMutationFacts{InputEventsEmitted: true},
		},
	}
	backend := &orderedInputRecordingBackend{
		recordingPlatformBackend: legacy,
		err:                      errors.New("connection closed after request write"),
	}
	service.Backend = backend

	result := service.axBatch([]any{
		map[string]any{
			"type": "click",
			"ref":  elementRef,
		},
		map[string]any{"type": "keypress", "keys": []any{"A"}},
		map[string]any{"type": "keypress", "keys": []any{"B"}},
		map[string]any{"type": "type", "text": "not-run"},
	},
		observation{mode: contract.ObserveNone},
		"",
	)
	if !result.IsError {
		t.Fatalf("mixed ordered transport failure was reported as success: %#v", result)
	}
	facts, _ := result.StructuredContent["a11y_batch_result"].(map[string]any)
	if facts["actions_requested"] != 4 ||
		facts["actions_completed_without_call_error"] != 1 ||
		facts["rejected_before_first_action"] != false ||
		facts["first_unattempted_action_index"] != nil ||
		result.StructuredContent["partial_input_possible"] != true ||
		result.StructuredContent["mutation_unknown"] != true {
		t.Fatalf("mixed ordered transport uncertainty was lost: %#v / %#v",
			facts, result.StructuredContent)
	}
	if len(backend.requests) != 1 || len(legacy.actionRequests) != 1 {
		t.Fatalf("transport-lost ordered run was retried: ordered=%d legacy=%d",
			len(backend.requests), len(legacy.actionRequests))
	}
}

func TestAXBatchUsesOrderedBackendForWholeUntargetedKeypressRun(t *testing.T) {
	service, _, windowRef, _ := orderedInputTestService(t)
	legacy := &recordingPlatformBackend{}
	backend := &orderedInputRecordingBackend{
		recordingPlatformBackend: legacy,
		execute: func(request ProviderOrderedInputRequest) ProviderOrderedInputResult {
			result := testProviderOrderedInputSuccess(request)
			result.Snapshot = orderedInputBatchTestSnapshot(request)
			return result
		},
	}
	service.Backend = backend

	result := service.axBatch([]any{
		map[string]any{"type": "keypress", "keys": []any{"DOWN"}},
		map[string]any{"type": "keypress", "keys": []any{"ENTER"}},
	},
		observation{mode: contract.ObserveNone},
		windowRef,
	)
	if result.IsError {
		t.Fatalf("ordered batch failed: %s\n%#v", ResultText(result), result)
	}
	if len(backend.requests) != 1 {
		t.Fatalf("ordered backend requests=%d, want 1", len(backend.requests))
	}
	request := backend.requests[0]
	if request.App != "LibreOffice" ||
		request.AppIdentity == nil ||
		request.AppIdentity.ProviderBusName != ":1.84" {
		t.Fatalf("ordered request did not bind runtime app identity: %#v", request)
	}
	if len(legacy.calls) != 0 {
		t.Fatalf("ordered batch also used the legacy path: %#v", legacy.calls)
	}
	facts, _ := result.StructuredContent["a11y_batch_result"].(map[string]any)
	if facts["execution_path"] != "provider_ordered_input" ||
		facts["actions_completed_without_call_error"] != 2 {
		t.Fatalf("ordered batch evidence is incomplete: %#v", facts)
	}
}

func TestAXWholeOrderedInputRecoversCurrentWindowAfterCompletedInput(t *testing.T) {
	service, _, windowRef, _ := orderedInputTestService(t)
	current := mixedOrderedInputBatchTestSnapshot()
	current.WindowHandle = "w2:11111111111111111111111111111111"
	current.WindowTitle = "Find & Replace"
	legacy := &recordingPlatformBackend{
		accessibilityResponses: []ProviderAccessibilityResult{
			{
				OK:    false,
				Error: "state_unavailable: source window changed",
				Failure: &ProviderFailure{
					Code:          "state_unavailable",
					Phase:         "observation",
					FactsComplete: true,
					Subject:       "window",
				},
			},
			{
				OK:       true,
				Snapshot: current,
			},
		},
	}
	backend := &orderedInputRecordingBackend{
		recordingPlatformBackend: legacy,
		execute:                  completedOrderedInputObservationFailure,
	}
	service.Backend = backend

	result := service.axBatch([]any{
		map[string]any{"type": "keypress", "keys": []any{"SUPER", "F"}},
	},
		observation{mode: contract.ObserveDiff},
		windowRef,
	)
	if result.IsError {
		t.Fatalf("completed input with recovered current window failed: %#v", result)
	}
	for key, expected := range map[string]any{
		"final_observation_returned":                                 true,
		"observation_required":                                       false,
		"recovery_observation_returned":                              true,
		"recovery_app_root_retry_attempted":                          true,
		"recovery_targeted_failure_code":                             "state_unavailable",
		"ordered_input_actions_completed_before_observation_failure": true,
		"ordered_input_observation_recovered":                        true,
		"task_effect_verified":                                       false,
	} {
		if result.StructuredContent[key] != expected {
			t.Fatalf("%s=%#v want %#v in %#v",
				key, result.StructuredContent[key], expected,
				result.StructuredContent)
		}
	}
	for _, key := range []string{
		"failure_code", "failure_phase", "effect_unknown", "mutation_unknown",
	} {
		if _, present := result.StructuredContent[key]; present {
			t.Fatalf("%s survived successful recovery: %#v",
				key, result.StructuredContent)
		}
	}
	facts := result.StructuredContent["a11y_batch_result"].(map[string]any)
	if facts["actions_completed_without_call_error"] != 1 ||
		facts["partial_sequence"] != false ||
		facts["mutation_unknown"] != false {
		t.Fatalf("recovered ordered-input facts are inconsistent: %#v", facts)
	}
	if len(legacy.observeRequests) != 2 ||
		legacy.observeRequests[0].WindowHandle == "" ||
		legacy.observeRequests[1].WindowHandle != "" {
		t.Fatalf("recovery did not fall back from window to app root: %#v",
			legacy.observeRequests)
	}

	result.StructuredContent["ax_mode"] = contract.AXModeAct
	prepared := PrepareAXEvidence(result)
	if prepared.StructuredContent["input_emitted"] != true ||
		prepared.StructuredContent["task_effect_verified"] == true {
		t.Fatalf("recovered action overstated causal evidence: %#v", prepared.StructuredContent)
	}
	if _, present := prepared.StructuredContent["mutation_unknown"]; present {
		t.Fatalf("compact result retained resolved mutation uncertainty: %#v",
			prepared.StructuredContent)
	}
}

func TestAXWholeOrderedInputKeepsUncertaintyWhenRecoveryFails(t *testing.T) {
	service, _, windowRef, _ := orderedInputTestService(t)
	recoveryFailure := ProviderAccessibilityResult{
		OK:    false,
		Error: "state_unavailable: no current window",
		Failure: &ProviderFailure{
			Code:          "state_unavailable",
			Phase:         "observation",
			FactsComplete: true,
			Subject:       "window",
		},
	}
	legacy := &recordingPlatformBackend{
		accessibilityResponses: []ProviderAccessibilityResult{
			recoveryFailure,
			recoveryFailure,
		},
	}
	backend := &orderedInputRecordingBackend{
		recordingPlatformBackend: legacy,
		execute:                  completedOrderedInputObservationFailure,
	}
	service.Backend = backend

	result := service.axBatch([]any{
		map[string]any{"type": "keypress", "keys": []any{"SUPER", "F"}},
	},
		observation{mode: contract.ObserveDiff},
		windowRef,
	)
	if !result.IsError ||
		result.StructuredContent["failure_code"] != "stale_window_ref" ||
		result.StructuredContent["mutation_unknown"] != true ||
		result.StructuredContent["observation_required"] != true ||
		result.StructuredContent["recovery_observation_returned"] != false ||
		result.StructuredContent["recovery_observation_failure_code"] !=
			"state_unavailable" {
		t.Fatalf("failed recovery cleared uncertainty: %#v", result)
	}
	facts := result.StructuredContent["a11y_batch_result"].(map[string]any)
	if facts["partial_sequence"] != false ||
		facts["mutation_unknown"] != true {
		t.Fatalf("failed recovery corrupted ordered-input facts: %#v", facts)
	}
	if len(legacy.observeRequests) != 2 {
		t.Fatalf("recovery observe calls=%d, want 2",
			len(legacy.observeRequests))
	}
}

func TestAXBatchPreservesOrderedPartialEmissionWithoutLegacyRetry(t *testing.T) {
	service, _, windowRef, _ := orderedInputTestService(t)
	legacy := &recordingPlatformBackend{}
	backend := &orderedInputRecordingBackend{
		recordingPlatformBackend: legacy,
		execute: func(request ProviderOrderedInputRequest) ProviderOrderedInputResult {
			stopped := request.Steps[1].ActionIndex
			unattempted := request.Steps[2].ActionIndex
			return ProviderOrderedInputResult{
				ContractVersion: ProviderOrderedInputContractVersion,
				Steps: []ProviderOrderedInputStepResult{
					{
						ActionIndex:       request.Steps[0].ActionIndex,
						Status:            ProviderOrderedInputCompleted,
						DispatchAttempted: true,
						Emission:          ProviderOrderedInputEmissionComplete,
						WindowContinuity:  ProviderOrderedInputWindowSame,
					},
					{
						ActionIndex:       stopped,
						Status:            ProviderOrderedInputFailed,
						DispatchAttempted: true,
						Emission:          ProviderOrderedInputEmissionPartial,
						WindowContinuity:  ProviderOrderedInputWindowSame,
					},
					{
						ActionIndex: request.Steps[2].ActionIndex,
						Status:      ProviderOrderedInputNotAttempted,
						Emission:    ProviderOrderedInputEmissionNone,
					},
				},
				StoppedActionIndex:          &stopped,
				FirstUnattemptedActionIndex: &unattempted,
				Failure: &ProviderFailure{
					Code:          "input_emission_failed",
					Phase:         "emission",
					FactsComplete: true,
				},
				Mutation: ProviderMutationFacts{
					InputEventsEmitted:   true,
					PartialInputPossible: true,
					DesktopStateMutated:  true,
					MutationUnknown:      true,
				},
			}
		},
	}
	service.Backend = backend

	result := service.axBatch([]any{
		map[string]any{"type": "keypress", "keys": []any{"A"}},
		map[string]any{"type": "keypress", "keys": []any{"B"}},
		map[string]any{"type": "keypress", "keys": []any{"C"}},
	},
		observation{mode: contract.ObserveNone},
		windowRef,
	)
	if !result.IsError {
		t.Fatalf("partial ordered emission was reported as success: %#v", result)
	}
	if len(backend.requests) != 1 || len(legacy.calls) != 0 {
		t.Fatalf("partial ordered batch was retried: ordered=%d legacy=%#v",
			len(backend.requests), legacy.calls)
	}
	facts, _ := result.StructuredContent["a11y_batch_result"].(map[string]any)
	actions, _ := facts["actions"].([]map[string]any)
	if len(actions) != 3 ||
		actions[1]["emission"] != "partial" ||
		actions[2]["status"] != "not_attempted" ||
		facts["first_unattempted_action_index"] != 2 {
		t.Fatalf("partial ordered evidence is incomplete: %#v", facts)
	}
}
