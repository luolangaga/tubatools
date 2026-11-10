package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func partialInventoryService() (*Service, *recordingPlatformBackend) {
	backend := &recordingPlatformBackend{accessibilityResponse: &ProviderAccessibilityResult{
		OK:              true,
		Apps:            editorApps(testWindow{testHandleReport, "Report - Editor", true}),
		InventoryIssues: []ProviderInventoryIssue{{App: "Busy app", Reason: "AXWindows unavailable (AX -25204)"}},
	}}
	return NewServiceWithBackend(backend), backend
}

func TestPartialAppsKeepConfirmedWindowsAndUnknownEvidence(t *testing.T) {
	s, _ := partialInventoryService()
	result := PrepareAXEvidence(s.callTool(contract.PublicToolAX, map[string]any{"mode": "apps"}))
	if result.IsError || result.StructuredContent["inventory_complete"] != false ||
		!strings.Contains(ResultText(result), `w1 "Report - Editor"`) ||
		!strings.Contains(ResultText(result), `Incomplete window inventory for "Busy app"`) {
		t.Fatalf("partial apps lost confirmed windows or uncertainty: %#v\n%s", result.StructuredContent, ResultText(result))
	}
	issues, ok := result.StructuredContent["inventory_issues"].([]ProviderInventoryIssue)
	if !ok || len(issues) != 1 || issues[0].App != "Busy app" || !strings.Contains(issues[0].Reason, "-25204") {
		t.Fatalf("structured issue lost: %#v", result.StructuredContent)
	}
	record := s.CallRecord(contract.PublicToolAX, map[string]any{"mode": "apps"}, CallOptions{})
	if !record.OK || record.InputEmitted || len(record.Windows) != 1 ||
		record.Windows[0].Ref != "w1" || record.Evidence["inventory_complete"] != false ||
		record.Evidence["inventory_issues"] == nil {
		t.Fatalf("records disagree with partial text discovery: %+v", record)
	}
}

func TestPartialAppsKeepConfirmedCloseRefUsable(t *testing.T) {
	s, backend := partialInventoryService()
	s.listApps()
	backend.windowResponse = &ProviderWindowResult{
		OK:             true,
		Snapshot:       &ProviderSnapshot{WindowAbsent: true, ClosedWindowHandle: testHandleReport},
		Mutation:       &ProviderMutationFacts{InputEventsEmitted: true, DesktopStateMutated: true},
		ActionEvidence: &ProviderActionEvidence{InputEmitted: true, Route: "native_window_command"},
	}
	result := s.callTool(contract.PublicToolAX, map[string]any{
		"mode": "act", "actions": []any{map[string]any{"type": "close", "ref": "w1"}},
	})
	if result.IsError || result.StructuredContent["close_effect_verified"] != true {
		t.Fatalf("unrelated inventory failure blocked confirmed window: %#v\n%s", result.StructuredContent, ResultText(result))
	}
	if strings.Join(backend.calls, ",") != "observe_apps,window_close" {
		t.Fatalf("ref close should validate its target without global enumeration: %v", backend.calls)
	}
}

func TestPartialInventoryCannotEstablishUniqueTitleFocusOrAbsence(t *testing.T) {
	for _, action := range []map[string]any{
		{"type": "close", "title": "Report"},
		{"type": "keypress", "keys": []any{"ENTER"}},
	} {
		s, backend := partialInventoryService()
		result := PrepareAXEvidence(s.callTool(contract.PublicToolAX, map[string]any{
			"mode": "act", "actions": []any{action},
		}))
		if !result.IsError || result.StructuredContent["input_emitted"] != false ||
			len(backend.actionRequests) != 0 || len(backend.desktopRequests) != 0 ||
			strings.Contains(strings.Join(backend.calls, ","), "window_close") {
			t.Fatalf("incomplete inventory allowed %v: %#v calls=%v", action, result.StructuredContent, backend.calls)
		}
	}
	for _, gone := range []bool{false, true} {
		s, _ := partialInventoryService()
		result := s.waitForDesktopWindow(nil, &axWait{window: "Missing", gone: gone})
		if !result.IsError || result.StructuredContent["assertion_status"] != "observation_incomplete" {
			t.Fatalf("unknown inventory became a predicate value (gone=%v): %#v", gone, result.StructuredContent)
		}
	}
}

func TestInventoryCompletenessRecoveryPreservesRefs(t *testing.T) {
	s, backend := partialInventoryService()
	s.listApps()
	backend.accessibilityResponse.InventoryIssues = nil
	result := s.listApps()
	if result.IsError || strings.Contains(ResultText(result), "Incomplete") ||
		!strings.Contains(ResultText(result), `w1 "Report - Editor"`) {
		t.Fatalf("complete inventory did not recover cleanly: %s", ResultText(result))
	}
	window, err := s.resolveWindowTitle("Report")
	if err != nil || window.ref != "w1" {
		t.Fatalf("unique title did not recover: window=%#v err=%v", window, err)
	}
	wait := s.waitForDesktopWindow(nil, &axWait{window: "Missing", gone: true})
	if wait.IsError || wait.StructuredContent["assertion_status"] != "pass" {
		t.Fatalf("complete absence did not pass: %#v", wait.StructuredContent)
	}
}

func TestAppsGlobalProviderFailureCannotBecomePartialSuccess(t *testing.T) {
	s, backend := partialInventoryService()
	backend.accessibilityResponse.OK = false
	backend.accessibilityResponse.Error = "Accessibility permission denied"
	backend.accessibilityResponse.Failure = &ProviderFailure{Code: "permission_denied", Phase: "precondition"}
	result := PrepareAXEvidence(s.callTool(contract.PublicToolAX, map[string]any{"mode": "apps"}))
	if !result.IsError || result.StructuredContent["input_emitted"] != false ||
		result.StructuredContent["message"] != "Accessibility permission denied" || s.lastInventory != nil {
		t.Fatalf("permission failure was hidden: %#v\n%s", result.StructuredContent, ResultText(result))
	}
}
