package core

import (
	"errors"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestOrderedInputTransportUsesRequestDeliveryEvidence(t *testing.T) {
	for _, test := range []struct {
		name    string
		err     error
		unknown bool
	}{
		{"zero bytes", &RuntimeTransportError{Platform: "macOS", Phase: "write_request", RequestSent: false, Cause: errors.New("broken pipe")}, false},
		{"partial write", &RuntimeTransportError{Platform: "macOS", Phase: "write_request", RequestSent: true, Cause: errors.New("broken pipe")}, true},
		{"lost response", &RuntimeTransportError{Platform: "macOS", Phase: "read_response", RequestSent: true, Cause: errors.New("EOF")}, true},
		{"timeout", &RuntimeTimeoutError{Platform: "macOS", Phase: "write_request"}, true},
		{"unclassified", errors.New("connection lost"), true},
	} {
		t.Run(test.name, func(t *testing.T) {
			s, _, window, _ := orderedInputTestService(t)
			backend := &orderedInputRecordingBackend{
				recordingPlatformBackend: &recordingPlatformBackend{}, err: test.err,
			}
			s.Backend = backend
			result := s.axBatch([]any{
				map[string]any{"type": "keypress", "keys": []any{"A"}},
				map[string]any{"type": "keypress", "keys": []any{"B"}},
			}, observation{mode: contract.ObserveNone}, window)
			if !result.IsError || len(backend.requests) != 1 {
				t.Fatalf("transport failure succeeded or replayed: %#v", result)
			}
			facts := result.StructuredContent["a11y_batch_result"].(map[string]any)
			if result.StructuredContent["mutation_unknown"] != test.unknown ||
				result.StructuredContent["partial_input_possible"] != test.unknown ||
				facts["rejected_before_first_action"] != !test.unknown {
				t.Fatalf("delivery evidence discarded: %#v", result.StructuredContent)
			}
			public := publicBatchEvidence(t, result)
			assertPublicBatchCounts(t, public, 2, 0)
			if !test.unknown && (public["code"] != contract.AXCodeRuntimeError || public["input_emitted"] != false) {
				t.Fatalf("unsent request reported input uncertainty: %#v", public)
			}
			if !test.unknown && facts["first_unattempted_action_index"] != 0 {
				t.Fatalf("unsent request lost its unattempted boundary: %#v", facts)
			}
		})
	}
}

func TestUnsentOrderedInputKeepsEarlierBatchInputEvidence(t *testing.T) {
	s, _, _, ref := orderedInputTestService(t)
	backend := &orderedInputRecordingBackend{
		recordingPlatformBackend: &recordingPlatformBackend{
			accessibilityResponse: &ProviderAccessibilityResult{
				OK: true, Snapshot: mixedOrderedInputBatchTestSnapshot(),
				Mutation:       &ProviderMutationFacts{InputEventsEmitted: true},
				ActionEvidence: &RuntimeActionEvidence{InputEmitted: true},
			},
		},
		err: &RuntimeTransportError{Platform: "macOS", Phase: "write_request", RequestSent: false, Cause: errors.New("broken pipe")},
	}
	s.Backend = backend
	result := s.axBatch([]any{
		map[string]any{"type": "click", "ref": ref},
		map[string]any{"type": "keypress", "keys": []any{"A"}},
		map[string]any{"type": "keypress", "keys": []any{"B"}},
	}, observation{mode: contract.ObserveNone}, "")
	public := publicBatchEvidence(t, result)
	batch := assertPublicBatchCounts(t, public, 3, 1)
	if !result.IsError || public["input_emitted"] != true || batch["rejected_before_first_action"] == true {
		t.Fatalf("earlier click evidence discarded: %#v", public)
	}
	if len(backend.requests) != 1 || len(backend.actionRequests) != 1 {
		t.Fatal("failed ordered input was replayed")
	}
}
