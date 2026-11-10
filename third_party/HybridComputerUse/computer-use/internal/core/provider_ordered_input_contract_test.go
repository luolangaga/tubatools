package core

import (
	"encoding/json"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func boolPointer(value bool) *bool {
	return &value
}

func intPointer(value int) *int {
	return &value
}

func testProviderOrderedInputRequest(
	keys ...string,
) ProviderOrderedInputRequest {
	handle := "w2:" + strings.Repeat("a", 32)
	steps := make([]ProviderOrderedInputStep, 0, len(keys))
	for index, key := range keys {
		steps = append(steps, ProviderOrderedInputStep{
			ActionIndex: 4 + index,
			Key:         key,
		})
	}
	return ProviderOrderedInputRequest{
		ContractVersion: ProviderOrderedInputContractVersion,
		App:             "a1",
		AppIdentity: &ProviderAppIdentity{
			ProviderBusName:   "pid:42",
			ProcessGeneration: "pid:42:start:100",
		},
		WindowHandle: handle,
		ExpectedWindow: &ProviderWindowExpectation{
			WindowHandle:      handle,
			ProviderBusName:   "pid:42",
			ProcessGeneration: "pid:42:start:100",
			Role:              "window",
			Name:              "Document",
		},
		Steps: steps,
		FinalObservation: ProviderObservationOptions{
			CaptureSnapshot: boolPointer(true),
		},
	}
}

func testProviderOrderedInputSuccess(
	request ProviderOrderedInputRequest,
) ProviderOrderedInputResult {
	steps := make([]ProviderOrderedInputStepResult, 0, len(request.Steps))
	for _, step := range request.Steps {
		steps = append(steps, ProviderOrderedInputStepResult{
			ActionIndex:       step.ActionIndex,
			Status:            ProviderOrderedInputCompleted,
			DispatchAttempted: true,
			Emission:          ProviderOrderedInputEmissionComplete,
			WindowContinuity:  ProviderOrderedInputWindowSame,
		})
	}
	return ProviderOrderedInputResult{
		ContractVersion: ProviderOrderedInputContractVersion,
		OK:              true,
		Steps:           steps,
		Snapshot: &ProviderSnapshot{
			WindowHandle: request.WindowHandle,
		},
		Mutation: ProviderMutationFacts{
			InputEventsEmitted:  true,
			DesktopStateMutated: true,
		},
	}
}

func TestProviderOrderedInputContractAcceptsCompleteSuccess(t *testing.T) {
	request := testProviderOrderedInputRequest("DOWN", "ENTER")
	result := testProviderOrderedInputSuccess(request)
	if err := request.Validate(); err != nil {
		t.Fatal(err)
	}
	if err := result.ValidateFor(request); err != nil {
		t.Fatal(err)
	}
	encoded, err := json.Marshal(result)
	if err != nil {
		t.Fatal(err)
	}
	var decoded ProviderOrderedInputResult
	if err := DecodeProviderWireJSON(encoded, &decoded); err != nil {
		t.Fatalf("strict result round trip failed: %v", err)
	}
	if err := decoded.ValidateFor(request); err != nil {
		t.Fatalf("round-tripped result failed validation: %v", err)
	}
}

func TestProviderOrderedInputContractAcceptsTypedStops(t *testing.T) {
	request := testProviderOrderedInputRequest("A", "B", "C")
	tests := []struct {
		name   string
		result ProviderOrderedInputResult
	}{
		{
			name: "window changed before first dispatch",
			result: ProviderOrderedInputResult{
				ContractVersion: ProviderOrderedInputContractVersion,
				Steps: []ProviderOrderedInputStepResult{
					{
						ActionIndex:      4,
						Status:           ProviderOrderedInputFailed,
						Emission:         ProviderOrderedInputEmissionNone,
						WindowContinuity: ProviderOrderedInputWindowChanged,
					},
					{
						ActionIndex: 5,
						Status:      ProviderOrderedInputNotAttempted,
						Emission:    ProviderOrderedInputEmissionNone,
					},
					{
						ActionIndex: 6,
						Status:      ProviderOrderedInputNotAttempted,
						Emission:    ProviderOrderedInputEmissionNone,
					},
				},
				StoppedActionIndex:          intPointer(4),
				FirstUnattemptedActionIndex: intPointer(4),
				Failure: &ProviderFailure{
					Code:          "stale_window_ref",
					Phase:         "precondition",
					FactsComplete: true,
				},
			},
		},
		{
			name: "partial emission stops later actions",
			result: ProviderOrderedInputResult{
				ContractVersion: ProviderOrderedInputContractVersion,
				Steps: []ProviderOrderedInputStepResult{
					{
						ActionIndex:       4,
						Status:            ProviderOrderedInputCompleted,
						DispatchAttempted: true,
						Emission:          ProviderOrderedInputEmissionComplete,
						WindowContinuity:  ProviderOrderedInputWindowSame,
					},
					{
						ActionIndex:       5,
						Status:            ProviderOrderedInputFailed,
						DispatchAttempted: true,
						Emission:          ProviderOrderedInputEmissionPartial,
						WindowContinuity:  ProviderOrderedInputWindowSame,
					},
					{
						ActionIndex: 6,
						Status:      ProviderOrderedInputNotAttempted,
						Emission:    ProviderOrderedInputEmissionNone,
					},
				},
				StoppedActionIndex:          intPointer(5),
				FirstUnattemptedActionIndex: intPointer(6),
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
			},
		},
		{
			name: "same-window pre-dispatch transient can retry",
			result: ProviderOrderedInputResult{
				ContractVersion: ProviderOrderedInputContractVersion,
				Steps: []ProviderOrderedInputStepResult{
					{
						ActionIndex:      4,
						Status:           ProviderOrderedInputFailed,
						Emission:         ProviderOrderedInputEmissionNone,
						WindowContinuity: ProviderOrderedInputWindowSame,
					},
					{
						ActionIndex: 5,
						Status:      ProviderOrderedInputNotAttempted,
						Emission:    ProviderOrderedInputEmissionNone,
					},
					{
						ActionIndex: 6,
						Status:      ProviderOrderedInputNotAttempted,
						Emission:    ProviderOrderedInputEmissionNone,
					},
				},
				StoppedActionIndex:          intPointer(4),
				FirstUnattemptedActionIndex: intPointer(4),
				Failure: &ProviderFailure{
					Code:          "provider_busy",
					Phase:         "precondition",
					FactsComplete: true,
				},
				RetrySameRequest: true,
			},
		},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			if err := test.result.ValidateFor(request); err != nil {
				t.Fatal(err)
			}
		})
	}
}

func TestProviderOrderedInputContractAcceptsFinalObservationFailure(t *testing.T) {
	request := testProviderOrderedInputRequest("A", "B")
	result := testProviderOrderedInputSuccess(request)
	result.OK = false
	result.Snapshot = nil
	result.Failure = &ProviderFailure{
		Code:          "state_unavailable",
		Phase:         "observation",
		FactsComplete: true,
	}
	result.Mutation.MutationUnknown = true
	if err := result.ValidateFor(request); err != nil {
		t.Fatal(err)
	}

	result.RetrySameRequest = true
	if err := result.ValidateFor(request); err == nil ||
		!strings.Contains(err.Error(), "invalid stop, snapshot, or retry") {
		t.Fatalf("retryable observation failure error=%v", err)
	}
}

func TestProviderOrderedInputRequestRejectsNonBatchableShapes(t *testing.T) {
	tests := []struct {
		name   string
		mutate func(*ProviderOrderedInputRequest)
		want   string
	}{
		{
			name: "missing window expectation",
			mutate: func(request *ProviderOrderedInputRequest) {
				request.ExpectedWindow = nil
			},
			want: "exact expected_window",
		},
		{
			name: "noncontiguous indices",
			mutate: func(request *ProviderOrderedInputRequest) {
				request.Steps[1].ActionIndex++
			},
			want: "strictly contiguous",
		},
		{
			name: "missing final canonical boundary",
			mutate: func(request *ProviderOrderedInputRequest) {
				request.FinalObservation.CaptureSnapshot = boolPointer(false)
			},
			want: "final canonical snapshot",
		},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			request := testProviderOrderedInputRequest("A", "B")
			test.mutate(&request)
			err := request.Validate()
			if err == nil || !strings.Contains(err.Error(), test.want) {
				t.Fatalf("error=%v, want substring %q", err, test.want)
			}
		})
	}
}

func TestProviderOrderedInputRequestAcceptsMaximumNormalizedChord(t *testing.T) {
	keys := make([]string, contract.GUIMaxKeys)
	for index := range keys {
		keys[index] = strings.Repeat("K", contract.GUIMaxKeyLength)
	}
	request := testProviderOrderedInputRequest(strings.Join(keys, "+"))
	if err := request.Validate(); err != nil {
		t.Fatalf("maximum normalized chord was rejected: %v", err)
	}
	request.Steps[0].Key += "X"
	if err := request.Validate(); err == nil ||
		!strings.Contains(err.Error(), "too long") {
		t.Fatalf("overlong normalized chord error=%v", err)
	}
}

func TestProviderOrderedInputResultRejectsContradictoryEvidence(t *testing.T) {
	request := testProviderOrderedInputRequest("A", "B", "C")
	tests := []struct {
		name   string
		result ProviderOrderedInputResult
		want   string
	}{
		{
			name: "missing result entry",
			result: func() ProviderOrderedInputResult {
				result := testProviderOrderedInputSuccess(request)
				result.Steps = result.Steps[:2]
				return result
			}(),
			want: "2 steps, want 3",
		},
		{
			name: "continued after failure",
			result: ProviderOrderedInputResult{
				ContractVersion: ProviderOrderedInputContractVersion,
				Steps: []ProviderOrderedInputStepResult{
					{
						ActionIndex:      4,
						Status:           ProviderOrderedInputFailed,
						Emission:         ProviderOrderedInputEmissionNone,
						WindowContinuity: ProviderOrderedInputWindowChanged,
					},
					{
						ActionIndex:       5,
						Status:            ProviderOrderedInputCompleted,
						DispatchAttempted: true,
						Emission:          ProviderOrderedInputEmissionComplete,
						WindowContinuity:  ProviderOrderedInputWindowSame,
					},
					{
						ActionIndex: 6,
						Status:      ProviderOrderedInputNotAttempted,
						Emission:    ProviderOrderedInputEmissionNone,
					},
				},
			},
			want: "continued after",
		},
		{
			name: "window changed but input emitted",
			result: ProviderOrderedInputResult{
				ContractVersion: ProviderOrderedInputContractVersion,
				Steps: []ProviderOrderedInputStepResult{
					{
						ActionIndex:       4,
						Status:            ProviderOrderedInputFailed,
						DispatchAttempted: true,
						Emission:          ProviderOrderedInputEmissionComplete,
						WindowContinuity:  ProviderOrderedInputWindowChanged,
					},
					{
						ActionIndex: 5,
						Status:      ProviderOrderedInputNotAttempted,
						Emission:    ProviderOrderedInputEmissionNone,
					},
					{
						ActionIndex: 6,
						Status:      ProviderOrderedInputNotAttempted,
						Emission:    ProviderOrderedInputEmissionNone,
					},
				},
			},
			want: "dispatched without proving",
		},
		{
			name: "partial emission omitted aggregate",
			result: ProviderOrderedInputResult{
				ContractVersion: ProviderOrderedInputContractVersion,
				Steps: []ProviderOrderedInputStepResult{
					{
						ActionIndex:       4,
						Status:            ProviderOrderedInputFailed,
						DispatchAttempted: true,
						Emission:          ProviderOrderedInputEmissionPartial,
						WindowContinuity:  ProviderOrderedInputWindowSame,
					},
					{
						ActionIndex: 5,
						Status:      ProviderOrderedInputNotAttempted,
						Emission:    ProviderOrderedInputEmissionNone,
					},
					{
						ActionIndex: 6,
						Status:      ProviderOrderedInputNotAttempted,
						Emission:    ProviderOrderedInputEmissionNone,
					},
				},
			},
			want: "aggregate input emission",
		},
		{
			name: "retry after prior emission",
			result: func() ProviderOrderedInputResult {
				result := ProviderOrderedInputResult{
					ContractVersion: ProviderOrderedInputContractVersion,
					Steps: []ProviderOrderedInputStepResult{
						{
							ActionIndex:       4,
							Status:            ProviderOrderedInputCompleted,
							DispatchAttempted: true,
							Emission:          ProviderOrderedInputEmissionComplete,
							WindowContinuity:  ProviderOrderedInputWindowSame,
						},
						{
							ActionIndex:      5,
							Status:           ProviderOrderedInputFailed,
							Emission:         ProviderOrderedInputEmissionNone,
							WindowContinuity: ProviderOrderedInputWindowSame,
						},
						{
							ActionIndex: 6,
							Status:      ProviderOrderedInputNotAttempted,
							Emission:    ProviderOrderedInputEmissionNone,
						},
					},
					StoppedActionIndex:          intPointer(5),
					FirstUnattemptedActionIndex: intPointer(5),
					Failure: &ProviderFailure{
						Code:          "provider_busy",
						Phase:         "precondition",
						FactsComplete: true,
					},
					Mutation: ProviderMutationFacts{
						InputEventsEmitted: true,
						MutationUnknown:    true,
					},
					RetrySameRequest: true,
				}
				return result
			}(),
			want: "same-request retry",
		},
		{
			name: "retry with nontransient failure",
			result: ProviderOrderedInputResult{
				ContractVersion: ProviderOrderedInputContractVersion,
				Steps: []ProviderOrderedInputStepResult{
					{
						ActionIndex:      4,
						Status:           ProviderOrderedInputFailed,
						Emission:         ProviderOrderedInputEmissionNone,
						WindowContinuity: ProviderOrderedInputWindowSame,
					},
					{
						ActionIndex: 5,
						Status:      ProviderOrderedInputNotAttempted,
						Emission:    ProviderOrderedInputEmissionNone,
					},
					{
						ActionIndex: 6,
						Status:      ProviderOrderedInputNotAttempted,
						Emission:    ProviderOrderedInputEmissionNone,
					},
				},
				StoppedActionIndex:          intPointer(4),
				FirstUnattemptedActionIndex: intPointer(4),
				Failure: &ProviderFailure{
					Code:          "permission_denied",
					Phase:         "precondition",
					FactsComplete: true,
				},
				RetrySameRequest: true,
			},
			want: "complete transient",
		},
		{
			name: "wrong first unattempted index",
			result: func() ProviderOrderedInputResult {
				result := testProviderOrderedInputSuccess(request)
				result.OK = false
				result.Steps[1] = ProviderOrderedInputStepResult{
					ActionIndex:      5,
					Status:           ProviderOrderedInputFailed,
					Emission:         ProviderOrderedInputEmissionNone,
					WindowContinuity: ProviderOrderedInputWindowChanged,
				}
				result.Steps[2] = ProviderOrderedInputStepResult{
					ActionIndex: 6,
					Status:      ProviderOrderedInputNotAttempted,
					Emission:    ProviderOrderedInputEmissionNone,
				}
				result.StoppedActionIndex = intPointer(5)
				result.FirstUnattemptedActionIndex = intPointer(6)
				result.Failure = &ProviderFailure{
					Code:          "stale_window_ref",
					Phase:         "precondition",
					FactsComplete: true,
				}
				result.Snapshot = nil
				result.Mutation = ProviderMutationFacts{
					InputEventsEmitted: true,
					MutationUnknown:    true,
				}
				return result
			}(),
			want: "first_unattempted",
		},
		{
			name: "success without final snapshot",
			result: func() ProviderOrderedInputResult {
				result := testProviderOrderedInputSuccess(request)
				result.Snapshot = nil
				return result
			}(),
			want: "final canonical snapshot",
		},
		{
			name: "success with absent expected window",
			result: func() ProviderOrderedInputResult {
				result := testProviderOrderedInputSuccess(request)
				result.Snapshot.WindowAbsent = true
				return result
			}(),
			want: "final canonical snapshot",
		},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			err := test.result.ValidateFor(request)
			if err == nil || !strings.Contains(err.Error(), test.want) {
				t.Fatalf("error=%v, want substring %q", err, test.want)
			}
		})
	}
}

func TestProviderOrderedInputStrictDecodeRejectsUnknownFields(t *testing.T) {
	request := testProviderOrderedInputRequest("ENTER")
	encoded, err := json.Marshal(request)
	if err != nil {
		t.Fatal(err)
	}
	encoded = []byte(strings.Replace(
		string(encoded),
		`"contract_version":1`,
		`"contract_version":1,"fast":true`,
		1,
	))
	var decoded ProviderOrderedInputRequest
	if err := DecodeProviderWireJSON(encoded, &decoded); err == nil ||
		!strings.Contains(err.Error(), "unknown field") {
		t.Fatalf("strict decoder error=%v, want unknown field", err)
	}
}

type orderedInputRecordingBackend struct {
	*recordingPlatformBackend
	requests []ProviderOrderedInputRequest
	execute  func(ProviderOrderedInputRequest) ProviderOrderedInputResult
	err      error
}

func (backend *orderedInputRecordingBackend) ExecuteOrderedInput(
	request ProviderOrderedInputRequest,
) (ProviderOrderedInputResult, error) {
	backend.requests = append(backend.requests, request)
	if backend.err != nil {
		return ProviderOrderedInputResult{}, backend.err
	}
	if backend.execute != nil {
		return backend.execute(request), nil
	}
	return ProviderOrderedInputResult{}, nil
}

func TestProviderOrderedInputBackendIsOptional(t *testing.T) {
	legacy := &recordingPlatformBackend{}
	if _, ok := orderedInputBackend(legacy); ok {
		t.Fatal("legacy backend unexpectedly advertises ordered input")
	}
	ordered := &orderedInputRecordingBackend{
		recordingPlatformBackend: legacy,
	}
	if _, ok := orderedInputBackend(ordered); !ok {
		t.Fatal("optional ordered-input backend was not detected")
	}
}

func TestProviderOrderedInputRequestsCompileOnlyPlannedRuns(t *testing.T) {
	handle := "w2:" + strings.Repeat("b", 32)
	context := &AXBatchContext{
		AppToken:     "a1",
		WindowRef:    "w1",
		WindowHandle: handle,
		expectedWindow: &ProviderWindowExpectation{
			WindowHandle:      handle,
			ProviderBusName:   "pid:84",
			ProcessGeneration: "pid:84:start:200",
			Role:              "window",
			Name:              "Document",
		},
	}
	actions := []axBatchAction{
		{actionType: "click", targetRef: "r1"},
		{actionType: "keypress", key: "DOWN"},
		{actionType: "keypress", key: "ENTER"},
		{actionType: "set_value", targetRef: "r2", value: "6"},
		{actionType: "keypress", key: "TAB"},
	}
	plan, err := compileAXBatchExecutionPlan(
		context,
		actions,
		observation{mode: contract.ObserveDiff},
	)
	if err != nil {
		t.Fatal(err)
	}
	requests, err := providerOrderedInputRequestsFromPlan(
		context,
		plan,
		actions,
	)
	if err != nil {
		t.Fatal(err)
	}
	if len(requests) != 2 {
		t.Fatalf("ordered input requests=%d, want 2: %#v",
			len(requests), requests)
	}
	if len(requests[0].Steps) != 2 ||
		requests[0].Steps[0].ActionIndex != 1 ||
		requests[0].Steps[0].Key != "DOWN" ||
		requests[0].Steps[1].ActionIndex != 2 ||
		requests[0].Steps[1].Key != "ENTER" {
		t.Fatalf("first ordered run is wrong: %#v", requests[0])
	}
	if len(requests[1].Steps) != 1 ||
		requests[1].Steps[0].ActionIndex != 4 ||
		requests[1].Steps[0].Key != "TAB" {
		t.Fatalf("second ordered run is wrong: %#v", requests[1])
	}
	for _, request := range requests {
		if request.FinalObservation.CaptureSnapshot == nil ||
			!*request.FinalObservation.CaptureSnapshot {
			t.Fatalf("ordered run lacks canonical boundary: %#v", request)
		}
	}
}

func TestProviderOrderedInputRequestsFallBackWithoutWindowIdentity(t *testing.T) {
	context := testAXBatchPlanContext()
	context.AppToken = "a1"
	actions := []axBatchAction{{
		actionType: "keypress",
		key:        "ENTER",
	}}
	plan, err := compileAXBatchExecutionPlan(
		context,
		actions,
		observation{},
	)
	if err != nil {
		t.Fatal(err)
	}
	requests, err := providerOrderedInputRequestsFromPlan(
		context,
		plan,
		actions,
	)
	if err != nil {
		t.Fatal(err)
	}
	if len(requests) != 0 {
		t.Fatalf("missing provider window identity did not fall back: %#v",
			requests)
	}
}
