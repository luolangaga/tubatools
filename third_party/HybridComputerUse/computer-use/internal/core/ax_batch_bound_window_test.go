package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestBackgroundRefBatchKeepsWindowWhenSiblingActive(t *testing.T) {
	main := reportMain()
	main.active = false
	s, backend := rootOnlyService(t, false, main, reportWindow{
		handle: reportOtherHandle, role: "frame", title: "Other document",
		path: "/window/other", active: true,
	})
	window, refs := reportObserve(t, s, "Size")
	backend.onAction = func(b *reportBackend) {
		b.value = "changed"
	}
	result := s.callTool(contract.PublicToolAX, map[string]any{
		"mode": "act", "actions": []any{
			map[string]any{"type": "set_value", "ref": refs["Size"], "value": "14"},
			map[string]any{"type": "set_value", "ref": refs["Size"], "value": "16"},
		},
	})
	if result.IsError {
		t.Fatalf("background ref batch failed: %s", ResultText(result))
	}
	if len(backend.actionRequests) != 2 {
		t.Fatalf("wanted both actions, got %d", len(backend.actionRequests))
	}
	for _, request := range backend.actionRequests {
		if request.Target == nil || request.Target.WindowHandle != main.handle {
			t.Fatalf("action reached sibling: %#v", request)
		}
	}
	snapshot := s.currentSnapshot("Editor")
	if snapshot == nil || snapshot.WindowHandle != main.handle || snapshot.publicWindowRef != window {
		t.Fatalf("final observation left the bound window: %#v", snapshot)
	}
	if backend.windows[0].active || !backend.windows[1].active {
		t.Fatal("background semantic writes moved focus")
	}
}

func TestModalBatchRecoveryObservesCurrentWindowWithoutReplayingInput(t *testing.T) {
	s, backend := rootOnlyService(t, false, reportMain())
	_, refs := reportObserve(t, s, "OK", "Size")
	backend.onAction = func(b *reportBackend) {
		b.windows[0].active = false
		b.windows = append(b.windows, reportWindow{
			handle: reportDialogHandle, role: "dialog", title: "Confirm changes",
			path: "/window/dialog", active: true,
		})
	}
	result := s.callTool(contract.PublicToolAX, map[string]any{
		"mode": "act", "observe": "diff", "actions": []any{
			map[string]any{"type": "click", "ref": refs["OK"]},
			map[string]any{"type": "set_value", "ref": refs["Size"], "value": "80"},
		},
	})
	public := axPublicEvidence(result.StructuredContent, result.IsError, result.Content)
	if !result.IsError || public["code"] != contract.AXCodeWindowChanged ||
		public["completed"] != 1 || public["input_emitted"] != true {
		t.Fatalf("lost stopped-prefix evidence: %#v", public)
	}
	if len(backend.actionRequests) != 1 || backend.value != "12" {
		t.Fatalf("later input or replay reached the provider: %#v", backend.actionRequests)
	}
	snapshot := s.currentSnapshot("Editor")
	if snapshot == nil || snapshot.WindowHandle != reportDialogHandle {
		t.Fatalf("recovery replaced the current modal with the original window: %#v", snapshot)
	}
	request := backend.observeRequests[len(backend.observeRequests)-1]
	if request.WindowHandle != reportDialogHandle {
		t.Fatalf("recovery did not target the current modal: %#v", request)
	}
	if !strings.Contains(ResultText(result), `window: "Confirm changes"`) ||
		result.StructuredContent["final_observation_returned"] != true ||
		result.StructuredContent["retry_same_request"] != false {
		t.Fatalf("missing current modal observation or unsafe retry guidance: %s", ResultText(result))
	}
	hint := decodeJSONForTest(t, public["hint"].(string))
	if hint["window_ref"] != snapshot.publicWindowRef {
		t.Fatalf("hint and recovery disagree: %#v, window %s", hint, snapshot.publicWindowRef)
	}
}
