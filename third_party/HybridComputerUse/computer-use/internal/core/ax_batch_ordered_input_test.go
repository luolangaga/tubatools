package core

import (
	"encoding/json"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// Inspect the delivered text evidence, including its JSON round trip.
func publicBatchEvidence(t *testing.T, result ToolCallResult) map[string]any {
	t.Helper()
	result.StructuredContent["ax_mode"] = contract.AXModeAct
	visible := WithAXTextEvidence(PrepareAXEvidence(result))
	wire, err := json.Marshal(visible)
	if err != nil {
		t.Fatal(err)
	}
	var decoded ToolCallResult
	if err := json.Unmarshal(wire, &decoded); err != nil {
		t.Fatal(err)
	}
	for _, item := range decoded.Content {
		if item.Type == "text" && strings.HasPrefix(item.Text, `{"ok":`) {
			var facts map[string]any
			if err := json.Unmarshal([]byte(item.Text), &facts); err != nil {
				t.Fatal(err)
			}
			return facts
		}
	}
	t.Fatalf("missing public AX evidence: %s", wire)
	return nil
}

func assertPublicBatchCounts(t *testing.T, facts map[string]any, requested, completed int) map[string]any {
	t.Helper()
	if facts["requested"] != float64(requested) ||
		facts["completed"] != float64(completed) {
		t.Fatalf("public whole-request counts lost: %#v", facts)
	}
	batch, _ := facts["batch"].(map[string]any)
	if batch == nil {
		batch = map[string]any{}
	}
	return batch
}

func orderedInputTestService(t *testing.T) (*Service, string, string, string) {
	t.Helper()
	s := NewService()
	app := s.AppRefRegistry.Acquire("LibreOffice", ":1.84", "pid:10:start:100", "c1")
	handle := "w2:0123456789abcdef0123456789abcdef"
	window := s.WindowRefRegistry.Acquire("libreoffice", handle, `frame "Sheet"`, "c1",
		":1.84", "/window/1", "pid:10:start:100", "frame", "Sheet", app)
	ref := s.RefRegistry.Acquire("libreoffice", handle, `button "Save"`, CanonicalRole("button"), "Save", "c1",
		":1.84", "/window/1/save", "pid:10:start:100",
		":1.84", "/window/1", "pid:10:start:100", "frame", "Sheet", app, window)
	return s, app, window, ref
}

func orderedInputTestSnapshot() *AppSnapshot {
	return &AppSnapshot{
		App:         AppDescriptor{Name: "Editor", PID: 42},
		WindowTitle: "Document", WindowHandle: "0x42", SnapshotID: "ordered-input-test",
		TreeLines: []string{
			"\t0 frame \"Document\"", "\t\t1 panel \"Form\"",
			"\t\t\t2 entry \"Name\" [focused]", "\t\t\t3 push button \"Save\" [click]",
			"\t\t4 status bar \"Ready\"",
		},
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Document"},
			{Index: 1, RuntimeID: []int{0, 0}, ControlType: "panel", Name: "Form"},
			{Index: 2, RuntimeID: []int{0, 0, 0}, ControlType: "entry", Name: "Name", States: "[focused]"},
			{Index: 3, RuntimeID: []int{0, 0, 1}, ControlType: "push button", Name: "Save", States: "[has-click-action]"},
			{Index: 4, RuntimeID: []int{0, 1}, ControlType: "status bar", Name: "Ready"},
		},
		Scope: contract.AXScopeWindow,
	}
}

func TestOrderedBatchPreflightsEntireRequestBeforeNativeDispatch(t *testing.T) {
	for _, invalid := range []map[string]any{
		{"type": "keypress", "keys": []any{}},
		{"type": "increment", "ref": "r1"},
		{"type": "decrement", "ref": "r1"},
		{"type": "keypress", "keys": []any{"ENTER"}, "unsupported": true},
		{"type": "keypress", "keys": []any{"ENTER"}, "until": map[string]any{"type": "invalid"}},
	} {
		s, _, window, _ := orderedInputTestService(t)
		legacy := &recordingPlatformBackend{}
		backend := &orderedInputRecordingBackend{recordingPlatformBackend: legacy}
		s.Backend = backend
		result := s.axBatch([]any{
			map[string]any{"type": "keypress", "keys": []any{"A"}}, invalid,
		}, observation{mode: contract.ObserveNone}, window)

		if !result.IsError || len(backend.requests) != 0 || len(legacy.calls) != 0 {
			t.Fatalf("invalid tail reached provider: invalid=%#v result=%#v", invalid, result)
		}
	}
}

func TestOrderedBatchCrossWindowTailRejectedBeforeInput(t *testing.T) {
	s, app, _, ref := orderedInputTestService(t)
	otherWindow := s.WindowRefRegistry.Acquire("libreoffice", "w2:11111111111111111111111111111111", `frame "Other"`, "c1",
		":1.84", "/window/2", "pid:10:start:100", "frame", "Other", app)
	legacy := &recordingPlatformBackend{}
	backend := &orderedInputRecordingBackend{recordingPlatformBackend: legacy}
	s.Backend = backend
	result := s.axBatch([]any{
		map[string]any{"type": "click", "ref": ref},
		map[string]any{"type": "keypress", "keys": []any{"ENTER"}},
		map[string]any{"type": "close", "ref": otherWindow},
	}, observation{mode: contract.ObserveNone}, "")
	if !result.IsError || len(backend.requests) != 0 || len(legacy.calls) != 0 {
		t.Fatalf("cross-window batch reached provider: %#v", result)
	}
}

// A batch with a wait step runs action by action, so the wait polls between
// actions and the final observation shows the awaited state.
func TestOrderedBatchWithAWaitKeepsThePerActionPath(t *testing.T) {
	for _, between := range []bool{false, true} {
		s, _, window, _ := orderedInputTestService(t)
		legacy := &recordingPlatformBackend{accessibilityResponse: &ProviderAccessibilityResult{
			OK: true, Snapshot: mixedOrderedInputBatchTestSnapshot(),
			Mutation: &ProviderMutationFacts{InputEventsEmitted: true},
		}}
		backend := &orderedInputRecordingBackend{recordingPlatformBackend: legacy}
		s.Backend = backend
		actions := []any{
			map[string]any{"type": "keypress", "keys": []any{"ENTER"}},
			map[string]any{"type": "wait", "query": map[string]any{"role": "button", "name_contains": "Sav"}, "timeout_ms": 1.0},
		}
		if between {
			actions = append(actions, map[string]any{"type": "keypress", "keys": []any{"TAB"}})
		}
		result := s.axBatch(actions, observation{mode: contract.ObserveDiff}, window)
		if result.IsError || len(backend.requests) != 0 || len(legacy.actionRequests) != len(actions)-1 {
			t.Fatalf("batch with a wait left the per-action path: between=%t result=%s calls=%#v",
				between, ResultText(result), legacy.calls)
		}
		facts := result.StructuredContent["a11y_batch_result"].(map[string]any)
		if !between && (facts["final_observation_phase"] != "after_wait" || facts["final_observation_returned"] != true) {
			t.Fatalf("a trailing wait did not come before the final observation: %#v", facts)
		}
	}
}

func TestOrderedBatchOptionalFinalObservationFailure(t *testing.T) {
	for _, tail := range []bool{false, true} {
		s, _, window, _ := orderedInputTestService(t)
		legacy := &recordingPlatformBackend{}
		backend := &orderedInputRecordingBackend{recordingPlatformBackend: legacy,
			execute: completedOrderedInputObservationFailure,
		}
		s.Backend = backend
		actions := []any{map[string]any{"type": "keypress", "keys": []any{"ENTER"}}}
		if tail {
			actions = append(actions, map[string]any{"type": "type", "text": "must not run"})
		}
		result := s.axBatch(actions, observation{mode: contract.ObserveNone}, window)
		facts := result.StructuredContent["a11y_batch_result"].(map[string]any)
		if result.IsError != tail || len(backend.requests) != 1 || len(legacy.calls) != 0 ||
			facts["actions_completed_without_call_error"] != 1 || facts["final_observation_returned"] != false ||
			result.StructuredContent["observation_required"] != true || result.StructuredContent["retry_same_request"] != false {
			t.Fatalf("optional observation hid unattempted tail or replayed input: tail=%t result=%#v", tail, result)
		}
		if tail && facts["first_unattempted_action_index"] != 1 {
			t.Fatalf("missing unattempted suffix: %#v", facts)
		}
	}
}
