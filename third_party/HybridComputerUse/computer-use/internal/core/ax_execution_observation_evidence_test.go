package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

type closedDialogObservationBackend struct {
	*reportDesktop
	closed bool
}

func (b *closedDialogObservationBackend) ActOnWindow(request ProviderWindowActionRequest) (ProviderWindowResult, error) {
	b.calls = append(b.calls, "window_"+string(request.Action))
	b.closed = true
	b.windows = b.windows[:1]
	b.windows[0].active = true
	return ProviderWindowResult{
		OK: true, Snapshot: b.snapshot(b.windows[0]),
		Mutation:       &ProviderMutationFacts{InputEventsEmitted: true, DesktopStateMutated: true},
		ActionEvidence: &ProviderActionEvidence{InputEmitted: true, Route: "native_window_command"},
	}, nil
}

func (b *closedDialogObservationBackend) Observe(request ProviderObserveRequest) (ProviderAccessibilityResult, error) {
	if !b.closed || request.WindowHandle != reportDialogHandle {
		return b.reportBackend.Observe(request)
	}
	b.observeRequests = append(b.observeRequests, request)
	result := ProviderAccessibilityResult{
		OK: false, Error: "The exact window reference is stale; no input was emitted.",
		Failure:         &ProviderFailure{Code: "stale_window_ref", Phase: "precondition", Subject: "window", FactsComplete: false},
		RecoveryContext: &ProviderRecoveryContext{SourceAppStatus: "alive", SourceWindowStatus: "unknown", InventoryComplete: true},
	}
	result.PrepareForService()
	return result, nil
}

func TestCloseAndTitleWaitPreserveUnavailableFinalObservation(t *testing.T) {
	main := reportMain()
	main.active = false
	dialog := reportWindow{handle: reportDialogHandle, role: "dialog", title: "Search", path: "/window/2", active: true}
	s, original := reportService(t, main, dialog)
	backend := &closedDialogObservationBackend{reportDesktop: &reportDesktop{reportBackend: original}}
	s.Backend = backend
	reportObserve(t, s)
	dialogRef := ""
	for _, window := range s.currentSnapshot("Editor").Windows {
		if window.Handle == reportDialogHandle {
			dialogRef = window.publicRef
		}
	}
	result := s.callTool(contract.PublicToolAX, map[string]any{
		"mode": "act", "observe": "diff", "actions": []any{
			map[string]any{"type": "close", "ref": dialogRef},
			map[string]any{"type": "wait", "window": "Search", "gone": true, "timeout_ms": 1.0},
		},
	})
	public := WithAXTextEvidence(PrepareAXEvidence(result))
	fields := public.StructuredContent
	if public.IsError || fields["input_emitted"] != true || fields["completed"] != 2 || fields["requested"] != 2 {
		t.Fatalf("successful action and wait were lost: %#v\n%s", fields, ResultText(public))
	}
	if fields["observation_failure_code"] != "stale_window_ref" || fields["observation_required"] != true ||
		fields["final_observation_returned"] != false || fields["final_observation_kind"] != "unavailable" ||
		fields["source_window_status"] != "unknown" {
		t.Fatalf("title wait erased the failed exact-window observation: %#v\n%s", fields, ResultText(public))
	}
	if fields["target_window_absent"] == true || fields["close_effect_verified"] == true {
		t.Fatalf("title wait was promoted to exact-window proof: %#v", fields)
	}
	if text := ResultText(public); strings.Contains(text, "no input was emitted") ||
		!strings.Contains(text, "final_observation_unavailable: the exact source window reference is stale.") {
		t.Fatalf("stale follow-up repeated input evidence or lost its reason: %s", text)
	}
	if len(backend.calls) != 1 || backend.calls[0] != "window_close" {
		t.Fatalf("close was replayed: %v", backend.calls)
	}
}

func TestFirstWaitTimeoutIsExecutionWithoutInput(t *testing.T) {
	desktop := &waitDesktop{recordingPlatformBackend: &recordingPlatformBackend{}, windowsAt: func(int) []testWindow {
		return []testWindow{{handle: testHandleReport, title: "Document", active: true}}
	}}
	s := NewServiceWithBackend(desktop)
	result := s.callTool(contract.PublicToolAX, map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "wait", "window": "Missing", "timeout_ms": 1.0},
	}})
	public := WithAXTextEvidence(PrepareAXEvidence(result))
	fields := public.StructuredContent
	batch, _ := fields["batch"].(map[string]any)
	if !public.IsError || fields["failure_phase"] != "wait" || fields["input_emitted"] != false ||
		fields["completed"] != 0 || fields["requested"] != 1 || fields["preflight_only"] != false ||
		batch["rejected_before_first_action"] == true {
		t.Fatalf("executed wait was labelled preflight: %#v", fields)
	}
	if desktop.listings == 0 || len(desktop.calls) != 0 || len(desktop.actionRequests) != 0 {
		t.Fatalf("wait did not poll without input: listings=%d calls=%v", desktop.listings, desktop.calls)
	}
}

func TestWaitDoesNotClearIndependentObservationOrInputFailure(t *testing.T) {
	for _, fields := range []map[string]any{
		{"observation_failure_code": "stale_window_ref", "observation_required": true, "source_window_status": "unknown"},
		{"failure_code": "effect_unknown", "failure_phase": "execution", "mutation_unknown": true},
	} {
		axClearReadbackFailure(fields)
		if fields["observation_failure_code"] == nil && fields["failure_code"] == nil {
			t.Fatalf("wait erased unrelated failure: %#v", fields)
		}
	}
}

func TestInvalidWaitRemainsPreflight(t *testing.T) {
	desktop := &waitDesktop{recordingPlatformBackend: &recordingPlatformBackend{}, windowsAt: func(int) []testWindow {
		t.Fatal("invalid wait reached the provider")
		return nil
	}}
	s := NewServiceWithBackend(desktop)
	result := WithAXTextEvidence(PrepareAXEvidence(s.callTool(contract.PublicToolAX, map[string]any{
		"mode": "act", "actions": []any{map[string]any{"type": "wait", "window": "Search", "timeout_ms": 0.0}},
	})))
	if !result.IsError || result.StructuredContent["preflight_only"] != true || result.StructuredContent["input_emitted"] != false {
		t.Fatalf("invalid wait lost its preflight classification: %#v", result.StructuredContent)
	}
}

func TestVerifiedCloseRemovesScopedObservationFailureText(t *testing.T) {
	action := TextResult("close sent", false)
	action.StructuredContent = map[string]any{"input_events_emitted": true}
	failure := TextResult("The exact window reference is stale; no input was emitted.\nMore read failure context.", true)
	failure.StructuredContent = map[string]any{"failure_code": "stale_window_ref", "final_observation_returned": false}
	merged := mergeAXBatchFinalObservation(action, failure)
	proof := closedWindowFinalObservation(&AXBatchContext{WindowRef: "w1"}, observation{mode: contract.ObserveDiff})
	verified := finalizeVerifiedCloseLifecycle(mergeAXBatchFinalObservation(merged, proof))
	if text := ResultText(verified); strings.Contains(text, "stale") || strings.Contains(text, "More read failure") {
		t.Fatalf("verified lifecycle retained superseded read failure text: %s", text)
	}
	if verified.StructuredContent["close_effect_verified"] != true || verified.StructuredContent["observation_required"] != false {
		t.Fatalf("verified lifecycle evidence lost: %#v", verified.StructuredContent)
	}
}

func TestFinalObservationPreservesOtherErrorsInTheirReadOnlyScope(t *testing.T) {
	action := TextResult("input sent", false)
	action.StructuredContent = map[string]any{"input_events_emitted": true, "mutation_unknown": true}
	failure := TextResult("Provider connection failed; no input was emitted.", true)
	failure.StructuredContent = map[string]any{"failure_code": "runtime_transport_error", "final_observation_returned": false}
	merged := mergeAXBatchFinalObservation(action, failure)
	if text := ResultText(merged); !strings.Contains(text, "final_observation_unavailable: read-only follow-up after the action:\nProvider connection failed") {
		t.Fatalf("read failure detail or scope lost: %s", text)
	}
	if merged.StructuredContent["input_events_emitted"] != true || merged.StructuredContent["mutation_unknown"] != true ||
		merged.StructuredContent["observation_failure_code"] != "runtime_transport_error" {
		t.Fatalf("input or observation error was masked: %#v", merged.StructuredContent)
	}
	if ResultText(failure) != "Provider connection failed; no input was emitted." {
		t.Fatal("merging rewrote the original provider result")
	}
}
