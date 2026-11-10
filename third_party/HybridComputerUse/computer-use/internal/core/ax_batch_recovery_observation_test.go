package core

import (
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"strings"
	"testing"
)

func TestRecoveryObservationPreservesOriginalUncertainty(t *testing.T) {
	failed := ToolCallResult{
		IsError: true,
		Content: []ContentItem{{Type: "text", Text: "effect unknown"}},
		StructuredContent: map[string]any{
			"failure_code":         "effect_unknown",
			"failure_phase":        "action",
			"input_events_emitted": true,
			"mutation_unknown":     true,
			"observation_required": true,
			"continuation": map[string]any{
				"state":     "observe_required",
				"next_mode": "observe",
			},
		},
	}
	observation := ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: "fresh current state"}},
		StructuredContent: map[string]any{
			"final_observation_returned": true,
			"observation_required":       false,
		},
	}
	merged := mergeAXBatchRecoveryObservation(failed, observation)
	if !merged.IsError ||
		merged.StructuredContent["failure_code"] != "effect_unknown" ||
		merged.StructuredContent["failure_phase"] != "action" ||
		merged.StructuredContent["input_events_emitted"] != true ||
		merged.StructuredContent["mutation_unknown"] != true ||
		merged.StructuredContent["final_observation_returned"] != true ||
		merged.StructuredContent["observation_required"] != false ||
		merged.StructuredContent["recovery_observation_attempted"] != true ||
		merged.StructuredContent["recovery_observation_returned"] != true ||
		merged.StructuredContent["cause_unattributed"] != true {
		t.Fatalf("recovery observation corrupted action evidence: %#v", merged)
	}
	if _, present := merged.StructuredContent["continuation"]; present {
		t.Fatalf("successful recovery retained observe-required continuation: %#v",
			merged.StructuredContent)
	}
}

func TestRecoveryObservationFailureStillRequiresObservation(t *testing.T) {
	failed := ToolCallResult{
		IsError: true,
		StructuredContent: map[string]any{
			"failure_code":         "effect_unknown",
			"failure_phase":        "action",
			"input_events_emitted": true,
			"mutation_unknown":     true,
			"observation_required": true,
		},
	}
	observation := ToolCallResult{
		IsError: true,
		StructuredContent: map[string]any{
			"failure_code":               "observation_incomplete",
			"final_observation_returned": false,
			"observation_required":       true,
		},
	}
	merged := mergeAXBatchRecoveryObservation(failed, observation)
	if merged.StructuredContent["failure_code"] != "effect_unknown" ||
		merged.StructuredContent["observation_required"] != true ||
		merged.StructuredContent["recovery_observation_returned"] != false ||
		merged.StructuredContent["recovery_observation_failure_code"] !=
			"observation_incomplete" {
		t.Fatalf("failed recovery lost continuation evidence: %#v", merged)
	}
}

func TestRecoveryObservationRetriesAtAppRootWhenReplacementRefStales(t *testing.T) {
	service, appRef, sourceWindowRef, _ := sheetBatchService(t)
	replacementHandle := "w2:11111111111111111111111111111111"
	replacementRef := service.WindowRefRegistry.Acquire(
		"editor",
		replacementHandle,
		`window "Document"`,
		"c2",
		":1.84",
		"/app/window/document",
		"pid:10:start:100",
		"window",
		"Document",
		appRef,
	)
	current := editorFormSnapshot()
	current.App = AppDescriptor{
		Name:              "Editor",
		ProviderBusName:   ":1.84",
		ProcessGeneration: "pid:10:start:100",
	}
	current.WindowTitle = "Document"
	current.WindowHandle = replacementHandle
	current.SnapshotID = "current"
	backend := &recordingPlatformBackend{
		accessibilityResponses: []ProviderAccessibilityResult{
			{
				OK:    false,
				Error: "stale_window_ref: replacement changed again",
				Failure: &ProviderFailure{
					Code:          "stale_window_ref",
					Phase:         "precondition",
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
	service.Backend = backend
	failed := ToolCallResult{
		IsError: true,
		StructuredContent: map[string]any{
			"failure_code":         "stale_target",
			"failure_phase":        "between_actions",
			"input_events_emitted": true,
			"mutation_unknown":     true,
			"observation_required": true,
			"current_window_ref":   replacementRef,
		},
	}
	context := &AXBatchContext{
		AppRef:       appRef,
		AppToken:     appRef,
		WindowRef:    sourceWindowRef,
		WindowHandle: "w2:0123456789abcdef0123456789abcdef",
	}

	result := service.recoverAXBatchObservation(
		context,
		failed,
		observation{mode: contract.ObserveDiff},
	)
	if !result.IsError {
		t.Fatalf("recovered observation must preserve the stopped batch error")
	}
	for key, expected := range map[string]any{
		"failure_code":                        "stale_target",
		"failure_phase":                       "between_actions",
		"final_observation_returned":          true,
		"observation_required":                false,
		"next_identical_observation_required": false,
		"recovery_observation_returned":       true,
		"recovery_app_root_retry_attempted":   true,
		"recovery_targeted_failure_code":      "stale_window_ref",
		"cause_unattributed":                  true,
	} {
		if result.StructuredContent[key] != expected {
			t.Fatalf("%s=%#v want %#v in %#v",
				key, result.StructuredContent[key], expected,
				result.StructuredContent)
		}
	}
	if len(backend.observeRequests) != 2 {
		t.Fatalf("observe calls=%d, want 2", len(backend.observeRequests))
	}
	if backend.observeRequests[0].WindowHandle != replacementHandle {
		t.Fatalf("first recovery was not replacement-targeted: %#v",
			backend.observeRequests[0])
	}
	if backend.observeRequests[1].WindowHandle != "" {
		t.Fatalf("second recovery was not app-rooted: %#v",
			backend.observeRequests[1])
	}
	if text := ResultText(result); !strings.Contains(text, `button "Save"`) ||
		!strings.Contains(text, "window-targeted read became stale") {
		t.Fatalf("app-root recovery observation missing: %q", text)
	}
}

func TestRecoveryObservationDoesNotRetryUnrelatedFailureAtAppRoot(t *testing.T) {
	for _, code := range []string{"permission_denied", "invalid_argument"} {
		result := ToolCallResult{
			IsError: true,
			StructuredContent: map[string]any{
				"failure_code": code,
			},
		}
		if axRecoveryObservationCanRetryAtAppRoot(result) {
			t.Fatalf("%s unexpectedly allowed app-root retry", code)
		}
	}
}

func TestRecoveryObservationRejectsForeignOrStaleCurrentWindow(t *testing.T) {
	for _, invalid := range []string{"foreign", "stale"} {
		t.Run(invalid, func(t *testing.T) {
			service, backend := reportService(t, reportMain())
			window, _ := reportObserve(t, service)
			context, err := service.deriveAXBatchContext(window, nil, nil)
			if err != nil {
				t.Fatal(err)
			}
			currentRef := service.WindowRefRegistry.Acquire(
				"editor", reportDialogHandle, `dialog "Confirm changes"`, "current",
				":1.84", "/window/dialog", "pid:10:start:100", "dialog", "Confirm changes", context.AppRef,
			)
			if invalid == "foreign" {
				service.WindowRefRegistry.entries[currentRef].AppRef = "a999"
			} else {
				delete(service.WindowRefRegistry.entries, currentRef)
			}
			failed := ToolCallResult{IsError: true, StructuredContent: map[string]any{
				"failure_code": "stale_target", "failure_phase": "between_actions",
				"current_window_ref": currentRef, "input_events_emitted": true,
				"mutation_unknown": true, "retry_same_request": false,
			}}
			result := service.recoverAXBatchObservation(context, failed, observation{mode: contract.ObserveDiff})
			if !result.IsError || len(backend.actionRequests) != 0 {
				t.Fatalf("recovery changed the failure or emitted input: %#v", result)
			}
			request := backend.observeRequests[len(backend.observeRequests)-1]
			if request.WindowHandle != context.WindowHandle {
				t.Fatalf("%s current ref changed observation ownership: %#v", invalid, request)
			}
		})
	}
}

func TestRecoveryObservationPreservesNonObserveContinuation(t *testing.T) {
	failed := ToolCallResult{
		IsError: true,
		StructuredContent: map[string]any{
			"failure_code":         "effect_unknown",
			"observation_required": true,
		},
	}
	observation := ToolCallResult{
		StructuredContent: map[string]any{
			"final_observation_returned": true,
			"continuation": map[string]any{
				"state":     "ready",
				"route":     "GUI",
				"next_mode": "screenshot",
			},
		},
	}
	merged := mergeAXBatchRecoveryObservation(failed, observation)
	continuation, ok := merged.StructuredContent["continuation"].(map[string]any)
	if !ok || continuation["route"] != "GUI" {
		t.Fatalf("successful recovery dropped unrelated continuation: %#v",
			merged.StructuredContent)
	}
}
