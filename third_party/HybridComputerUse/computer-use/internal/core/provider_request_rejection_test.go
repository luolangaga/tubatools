package core

import (
	"encoding/json"
	"errors"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func testRequestRejection() *RuntimeRequestRejectionError {
	dispatched := false
	return &RuntimeRequestRejectionError{
		Operation: ProviderWireOrderedInput,
		Rejection: ProviderRequestRejection{
			Code: "invalid_arguments", Phase: "request_validation",
			DispatchAttempted: &dispatched, Message: "unsupported native key",
		},
	}
}

func TestProviderRequestRejectionWireIsExclusiveAndComplete(t *testing.T) {
	for _, operation := range []ProviderWireOperation{
		ProviderWireObserveApps, ProviderWireObserve, ProviderWireResolveAndAct,
		ProviderWireOrderedInput, ProviderWireDesktopBatch, ProviderWireWindowList, ProviderWireWindowAction,
	} {
		response := ProviderWireResponse{
			Protocol: ProviderProtocolName, SchemaVersion: ProviderProtocolVersion,
			RequestID: "bound-request", Operation: operation,
			RequestRejection: &testRequestRejection().Rejection,
		}
		data, err := json.Marshal(response)
		if err != nil {
			t.Fatal(err)
		}
		if _, err := DecodeProviderWireResponse(data); err != nil {
			t.Fatalf("%s rejection rejected: %v", operation, err)
		}
	}
	base := `{"protocol":"computer-use-provider","schema_version":2,"request_id":"r","operation":"ordered_input","request_rejection":{"code":"invalid_arguments","phase":"request_validation","dispatch_attempted":false,"message":"bad key"}}`
	if _, err := DecodeProviderWireResponse([]byte(base)); err != nil {
		t.Fatalf("valid rejection fixture rejected: %v", err)
	}
	for _, invalid := range []string{
		strings.Replace(base, `"dispatch_attempted":false,`, "", 1),
		strings.Replace(base, `"dispatch_attempted":false`, `"dispatch_attempted":null`, 1),
		strings.Replace(base, `"dispatch_attempted":false`, `"dispatch_attempted":true`, 1),
		strings.Replace(base, `"request_validation"`, `"after_input"`, 1),
		strings.Replace(base, `"invalid_arguments"`, `"transport_lost"`, 1),
		strings.Replace(base, `"message":"bad key"`, `"message":""`, 1),
		strings.Replace(base, `"operation":"ordered_input"`, `"operation":"unknown"`, 1),
		strings.Replace(base, `"request_rejection":`, `"provider_reset_required":true,"request_rejection":`, 1),
		strings.Replace(base, `"request_rejection":`, `"ordered_input":{},"request_rejection":`, 1),
		strings.Replace(base, `"request_rejection":`, `"accessibility":{"ok":false},"request_rejection":`, 1),
		strings.Replace(base, `"message":"bad key"`, `"message":"bad key","input_events_emitted":true`, 1),
	} {
		if _, err := DecodeProviderWireResponse([]byte(invalid)); err == nil {
			t.Fatalf("incomplete/contradictory rejection accepted: %s", invalid)
		}
	}
}

type requestRejectionBackend struct {
	*recordingPlatformBackend
	requests []ProviderOrderedInputRequest
	rejectAt int
	err      error
}

func (b *requestRejectionBackend) ExecuteOrderedInput(r ProviderOrderedInputRequest) (ProviderOrderedInputResult, error) {
	b.requests = append(b.requests, r)
	if len(b.requests) == b.rejectAt {
		return ProviderOrderedInputResult{}, b.err
	}
	result := testProviderOrderedInputSuccess(r)
	result.Snapshot = orderedInputBatchTestSnapshot(r)
	return result, nil
}

func TestProviderRequestRejectionOrderedBatchPreservesPrefix(t *testing.T) {
	for _, mixed := range []bool{false, true} {
		for _, rejection := range []bool{false, true} {
			for _, observe := range []string{contract.ObserveNone, contract.ObserveDiff} {
				s, _, window, ref := orderedInputTestService(t)
				backend := &requestRejectionBackend{
					recordingPlatformBackend: &recordingPlatformBackend{
						accessibilityResponse: &ProviderAccessibilityResult{
							OK: true, Snapshot: mixedOrderedInputBatchTestSnapshot(),
							Mutation: &ProviderMutationFacts{InputEventsEmitted: true},
						},
					},
					rejectAt: 1, err: testRequestRejection(),
				}
				if !rejection {
					backend.err = &RuntimeTransportError{
						Operation: ProviderWireOrderedInput, Phase: "read_response",
						RequestSent: true, Cause: errors.New("EOF"),
					}
				}
				actions := []any{
					map[string]any{"type": "keypress", "keys": []any{"RIGHT"}},
					map[string]any{"type": "keypress", "keys": []any{"ESC"}},
				}
				args := map[string]any{"mode": "act", "actions": actions, "window_ref": window, "observe": observe}
				if mixed {
					backend.rejectAt = 2
					args["actions"] = append([]any{
						map[string]any{"type": "click", "ref": ref},
						map[string]any{"type": "keypress", "keys": []any{"SUPER", "A"}},
						map[string]any{"type": "type", "text": "prefix"},
					}, actions...)
					delete(args, "window_ref")
				}
				s.Backend = backend
				result := s.callTool("AX", args)
				if !result.IsError || len(backend.requests) != backend.rejectAt {
					t.Fatalf("unexpected execution: %#v requests=%d", result, len(backend.requests))
				}
				facts := publicBatchEvidence(t, result)
				requested, completed := 2, 0
				if mixed {
					requested, completed = 5, 3
				}
				batch := assertPublicBatchCounts(t, facts, requested, completed)
				if rejection {
					if facts["failure_code"] != "provider_request_rejected" ||
						facts["partial_input_possible"] == true || facts["preflight_only"] == true ||
						facts["input_emitted"] != mixed {
						t.Fatalf("rejection mislabeled mixed=%v: %#v", mixed, facts)
					}
					internal := result.StructuredContent["a11y_batch_result"].(map[string]any)
					if internal["first_unattempted_action_index"] != completed {
						t.Fatalf("missing exact unattempted run: %#v", internal)
					}
					if !mixed && batch["rejected_before_first_action"] != true {
						t.Fatalf("whole native request not marked undispatched: %#v", batch)
					}
					if strings.Contains(ResultText(result), "transport_failed") {
						t.Fatal("native rejection mislabeled as transport loss")
					}
				} else if facts["failure_code"] != "runtime_transport_lost" ||
					facts["partial_input_possible"] != true {
					t.Fatalf("true transport uncertainty weakened: %#v", facts)
				}
			}
		}
	}
}

func TestProviderRequestRejectionServicePathsDoNotInvalidate(t *testing.T) {
	s, app, window, _ := orderedInputTestService(t)
	s.rememberFailedObservation(app, nil)
	result := s.RuntimeFailureResult(AXServiceRequest{App: app}, nil, testRequestRejection())
	if !result.IsError || result.StructuredContent["input_events_emitted"] != false ||
		result.StructuredContent["preflight_only"] != false {
		t.Fatalf("action rejection lost facts: %#v", result)
	}
	if !s.hasPendingFailedObservation(app) {
		t.Fatal("native request rejection consumed pending observation")
	}
	if _, err := s.resolveShortAXWindowRefOwned(window); err != nil {
		t.Fatalf("undispatched rejection invalidated window: %v", err)
	}
}

func TestProviderRequestRejectionGUIKeepsPendingState(t *testing.T) {
	for _, coordinate := range []bool{false, true} {
		s, app, window, _ := orderedInputTestService(t)
		backend := &recordingPlatformBackend{err: testRequestRejection()}
		s.Backend = backend
		s.rememberFailedObservation(app, nil)
		action := map[string]any{"type": "keypress", "keys": []any{"RIGHT"}}
		if coordinate {
			action = map[string]any{"type": "click", "x": 10, "y": 10}
		}
		result := s.callTool("GUI", map[string]any{"actions": []any{action}})
		if !result.IsError || result.StructuredContent["input_events_emitted"] != false ||
			result.StructuredContent["partial_input_possible"] != false ||
			result.StructuredContent["mutation_unknown"] != false ||
			result.StructuredContent["preflight_only"] != false ||
			len(backend.desktopRequests) != 1 {
			t.Fatalf("GUI rejection lost evidence: %#v", result)
		}
		for _, item := range result.Content {
			if item.Type == "image" {
				t.Fatal("native argument rejection fabricated screenshot")
			}
		}
		if !s.hasPendingFailedObservation(app) {
			t.Fatal("GUI native rejection consumed pending AX gate")
		}
		if _, err := s.resolveShortAXWindowRefOwned(window); err != nil {
			t.Fatalf("GUI native rejection invalidated AX window: %v", err)
		}
	}
}

func TestProviderRequestRejectionPublicReadsAreNotLocalPreflight(t *testing.T) {
	for _, mode := range []string{"observe", "find"} {
		s, app, _, _ := orderedInputTestService(t)
		backend := &recordingPlatformBackend{err: testRequestRejection()}
		s.Backend = backend
		args := map[string]any{"mode": mode, "app": app}
		if mode == "find" {
			args["query"] = map[string]any{"role": "textbox"}
		}
		result := s.callTool("AX", args)
		if !result.IsError ||
			result.StructuredContent["provider_request_rejected"] != true ||
			result.StructuredContent["preflight_only"] != false ||
			result.StructuredContent["input_events_emitted"] != false {
			t.Fatalf("%s rejection lost runtime facts: %#v", mode, result)
		}
		if len(backend.observeRequests) != 1 {
			t.Fatalf("%s attempted hidden recovery: %d", mode, len(backend.observeRequests))
		}
	}
}
