package core

import (
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"strings"
	"testing"
)

func TestVerifiedCloseClearsTopLevelAndNestedUncertainty(t *testing.T) {
	structured := map[string]any{
		"effect_unknown":     true,
		"mutation_unknown":   true,
		"cause_unattributed": true,
	}
	action := map[string]any{
		"mutation_unknown":   true,
		"cause_unattributed": true,
	}
	clearVerifiedCloseUncertainty(structured, action)
	for _, key := range []string{
		"effect_unknown",
		"mutation_unknown",
		"cause_unattributed",
	} {
		if _, present := structured[key]; present {
			t.Fatalf("verified close retained top-level %s: %#v",
				key, structured)
		}
		if _, present := action[key]; present {
			t.Fatalf("verified close retained action-level %s: %#v",
				key, action)
		}
	}
}

func TestFinalObservationFollowsVerifiedTransientWindowClosure(t *testing.T) {
	for _, mode := range []string{contract.ObserveScreenshot, contract.ObserveTree, contract.ObserveFull} {
		t.Run(mode, func(t *testing.T) {
			testFinalObservationFollowsVerifiedTransientWindowClosure(t, mode)
		})
	}
}

func testFinalObservationFollowsVerifiedTransientWindowClosure(t *testing.T, mode string) {
	service, appRef, sourceWindowRef, _ := sheetBatchService(t)
	backend := &recordingPlatformBackend{
		accessibilityResponses: []ProviderAccessibilityResult{
			{
				OK:    false,
				Error: "stale_window_ref: source dialog is gone",
				Failure: &ProviderFailure{
					Code:          "stale_window_ref",
					Phase:         "precondition",
					FactsComplete: true,
					Subject:       "window",
				},
				RecoveryContext: &ProviderRecoveryContext{
					SourceAppStatus:    "alive",
					SourceWindowStatus: "gone",
					InventoryComplete:  true,
					CurrentWindow: &ProviderReplacementWindow{
						WindowHandle:       "w2:11111111111111111111111111111111",
						Title:              "Document",
						Role:               "frame",
						ProviderBusName:    ":1.84",
						ProviderObjectPath: "/window/current",
						ProcessGeneration:  "pid:10:start:100",
					},
				},
			},
			{
				OK: true,
				Snapshot: &ProviderSnapshot{
					App:                 AppDescriptor{Name: "LibreOffice"},
					WindowTitle:         "Document",
					WindowHandle:        "w2:11111111111111111111111111111111",
					ScreenshotPNGBase64: "current-image",
					ScreenshotMimeType:  "image/png",
					TreeLines:           []string{`0 frame "Current document"`, ` 1 push button "Current action"`},
					Elements: []ElementRecord{
						{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Current document"},
						{Index: 1, RuntimeID: []int{0, 0}, ControlType: "push button", Name: "Current action"},
					},
				},
			},
		},
	}
	backend.desktopResponse = testDesktopFrame(t, "desktop-frame", 64, 36)
	service.Backend = backend
	context := &AXBatchContext{
		AppRef:       appRef,
		WindowRef:    sourceWindowRef,
		AppToken:     appRef,
		WindowHandle: "w2:0123456789abcdef0123456789abcdef",
	}
	result := service.axBatchFinalObservation(
		context,
		observation{mode: mode},
		false,
	)
	if result.IsError ||
		result.StructuredContent["final_observation_returned"] != true ||
		result.StructuredContent["final_observation_followed_window_transition"] !=
			true {
		t.Fatalf("verified window transition did not return current observation: %#v",
			result)
	}
	if len(backend.observeRequests) != 2 {
		t.Fatalf("observe calls=%d, want 2", len(backend.observeRequests))
	}
	if mode == contract.ObserveTree || mode == contract.ObserveFull {
		if !strings.Contains(ResultText(result), `button "Current action"`) {
			t.Fatalf("replacement window tree missing: %s", ResultText(result))
		}
	}
	if mode == contract.ObserveFull || mode == contract.ObserveScreenshot {
		foundImage := false
		for _, item := range result.Content {
			foundImage = foundImage || item.Type == "image" &&
				item.Data == backend.desktopResponse.Snapshot.ScreenshotPNGBase64
		}
		// The provider read returns no image; the image is one desktop frame
		// captured after the replacement window was read.
		if !foundImage || len(backend.desktopRequests) != 1 {
			t.Fatalf("replacement window final screenshot not captured or returned: %d captures",
				len(backend.desktopRequests))
		}
	}
	if backend.observeRequests[0].WindowHandle != context.WindowHandle ||
		backend.observeRequests[1].WindowHandle !=
			"w2:11111111111111111111111111111111" {
		t.Fatalf("unexpected observation targets: %#v", backend.observeRequests)
	}
	if _, err := service.WindowRefRegistry.Resolve(sourceWindowRef, ""); err == nil ||
		!strings.Contains(err.Error(), string(AXRefStale)) {
		t.Fatalf("gone source window ref was not tombstoned: %v", err)
	}
}

func TestFinalObservationReturnsLifecycleWhenClosedAppRefIsStale(
	t *testing.T,
) {
	service, appRef, sourceWindowRef, _ := sheetBatchService(t)
	backend := &recordingPlatformBackend{
		accessibilityResponses: []ProviderAccessibilityResult{{
			OK:                   false,
			Error:                "stale_app_ref: process generation no longer exists",
			FailureCode:          "stale_app_ref",
			FailurePhase:         "precondition",
			FailureSubject:       "app",
			FailureFactsComplete: true,
		}},
	}
	service.Backend = backend
	context := &AXBatchContext{
		AppRef:       appRef,
		WindowRef:    sourceWindowRef,
		AppToken:     appRef,
		WindowHandle: "w2:0123456789abcdef0123456789abcdef",
	}
	result := service.axBatchFinalObservation(
		context,
		observation{mode: contract.ObserveDiff},
		true,
	)
	if result.IsError ||
		result.StructuredContent["target_window_absent"] != true ||
		result.StructuredContent["final_observation_returned"] != true ||
		result.StructuredContent["observation_required"] == true {
		t.Fatalf("closed app ref did not produce lifecycle observation: %#v",
			result)
	}
}

func TestClosedWindowLifecycleRejectsIncompleteStaleAppFacts(t *testing.T) {
	context := &AXBatchContext{WindowRef: "w7"}
	for name, fields := range map[string]map[string]any{
		"incomplete facts": {
			"failure_code":           "stale_app_ref",
			"failure_subject":        "app",
			"failure_facts_complete": false,
		},
		"wrong subject": {
			"failure_code":           "stale_app_ref",
			"failure_subject":        "window",
			"failure_facts_complete": true,
		},
	} {
		t.Run(name, func(t *testing.T) {
			if closedWindowLifecycleProven(
				context,
				ToolCallResult{IsError: true, StructuredContent: fields},
			) {
				t.Fatalf("unsafe stale-app lifecycle accepted: %#v", fields)
			}
		})
	}
}

func TestSuccessfulCloseRecoversLifecycleObservation(t *testing.T) {
	service, appRef, sourceWindowRef, _ := sheetBatchService(t)
	backend := &recordingPlatformBackend{
		accessibilityResponses: []ProviderAccessibilityResult{{
			OK:    false,
			Error: "stale_window_ref: source window is gone",
			Failure: &ProviderFailure{
				Code:          "stale_window_ref",
				Phase:         "precondition",
				FactsComplete: true,
				Subject:       "window",
			},
			RecoveryContext: &ProviderRecoveryContext{
				SourceAppStatus:    "absent",
				SourceWindowStatus: "gone",
				InventoryComplete:  true,
			},
		}},
	}
	service.Backend = backend
	context := &AXBatchContext{
		AppRef:       appRef,
		WindowRef:    sourceWindowRef,
		AppToken:     appRef,
		WindowHandle: "w2:0123456789abcdef0123456789abcdef",
	}
	actionResult := ToolCallResult{
		StructuredContent: map[string]any{
			"final_observation_returned": false,
			"observation_required":       true,
			"input_events_emitted":       true,
			"effect_unknown":             true,
			"mutation_unknown":           true,
		},
	}
	result := service.recoverClosedWindowActionObservation(
		context,
		axBatchAction{actionType: "close"},
		actionResult,
		observation{mode: contract.ObserveDiff},
	)
	if result.IsError ||
		result.StructuredContent["target_window_absent"] != true ||
		result.StructuredContent["final_observation_returned"] != true ||
		result.StructuredContent["observation_required"] != false {
		t.Fatalf("successful close did not recover lifecycle evidence: %#v", result)
	}
	if result.StructuredContent["effect_unknown"] == true ||
		result.StructuredContent["mutation_unknown"] == true {
		t.Fatalf("verified close retained superseded uncertainty: %#v", result)
	}
	if len(backend.observeRequests) != 1 {
		t.Fatalf("close recovery observations=%d, want 1", len(backend.observeRequests))
	}
}

func TestVerifiedCloseScreenshotDoesNotBecomeEffectUnknown(t *testing.T) {
	action := ToolCallResult{
		StructuredContent: map[string]any{
			"input_events_emitted":   true,
			"postcondition_verified": true,
		},
	}
	observation := closedWindowFinalObservation(
		&AXBatchContext{WindowRef: "w7"},
		observation{mode: contract.ObserveScreenshot},
	)
	merged := mergeAXBatchFinalObservation(action, observation)
	if merged.IsError ||
		merged.StructuredContent["target_window_absent"] != true ||
		merged.StructuredContent["observation_required"] != true ||
		merged.StructuredContent["next_identical_observation_required"] != false {
		t.Fatalf("verified close lifecycle evidence was lost: %#v", merged)
	}
	if _, ok := merged.StructuredContent["effect_unknown"]; ok {
		t.Fatalf("verified close was marked effect_unknown: %#v",
			merged.StructuredContent)
	}
	if _, ok := merged.StructuredContent["mutation_unknown"]; ok {
		t.Fatalf("verified close was marked mutation_unknown: %#v",
			merged.StructuredContent)
	}
}

func TestVerifiedCloseUsesLifecycleObservationInsteadOfClosedWindowRead(t *testing.T) {
	result := closedWindowFinalObservation(
		&AXBatchContext{WindowRef: "w7"},
		observation{mode: contract.ObserveDiff},
	)
	if result.IsError {
		t.Fatalf("verified close lifecycle observation must not be an error: %#v", result)
	}
	text := ResultText(result)
	if !strings.Contains(text, "effect: target_window_absent") ||
		!strings.Contains(text, "absent_refs: [w7]") {
		t.Fatalf("close lifecycle evidence is incomplete: %q", text)
	}
	if result.StructuredContent["target_window_absent"] != true ||
		result.StructuredContent["final_observation_returned"] != true ||
		result.StructuredContent["final_observation_kind"] != "lifecycle" {
		t.Fatalf("close lifecycle fields are incomplete: %#v", result.StructuredContent)
	}
}

func TestExistingCloseLifecycleGetsVerifiedOutcomeFields(t *testing.T) {
	service := NewService()
	result := service.recoverClosedWindowActionObservation(
		&AXBatchContext{WindowRef: "w7"},
		axBatchAction{actionType: "close"},
		ToolCallResult{
			IsError: true,
			Content: []ContentItem{
				{Type: "text", Text: "final_observation_unavailable: stale app"},
				{Type: "text", Text: "ax_diff:\n effect: target_window_absent"},
			},
			StructuredContent: map[string]any{
				"target_window_absent":                true,
				"final_observation_returned":          true,
				"observation_required":                false,
				"next_identical_observation_required": false,
				"input_events_emitted":                true,
				"effect_unknown":                      true,
				"mutation_unknown":                    true,
				"observation_failure_code":            "stale_app_ref",
				"post_action_observation_status":      "unknown",
				"continuation":                        map[string]any{"state": "observe_required"},
			},
		},
		observation{mode: contract.ObserveDiff},
	)
	if result.IsError ||
		result.StructuredContent["close_effect_verified"] != true ||
		result.StructuredContent["observation_required"] != false {
		t.Fatalf("verified close lifecycle was not finalized: %#v",
			result.StructuredContent)
	}
	if _, present := result.StructuredContent["task_effect_verified"]; present {
		t.Fatalf("close proof claimed whole-task success: %#v",
			result.StructuredContent)
	}
	for _, key := range []string{
		"effect_unknown",
		"mutation_unknown",
		"observation_failure_code",
		"post_action_observation_status",
		"continuation",
	} {
		if _, present := result.StructuredContent[key]; present {
			t.Fatalf("verified close retained superseded %s: %#v",
				key, result.StructuredContent)
		}
	}
	if text := ResultText(result); strings.Contains(
		text,
		"final_observation_unavailable",
	) || !strings.Contains(text, "target_window_absent") {
		t.Fatalf("verified close retained superseded observation text: %q", text)
	}
}

func TestVerifiedCloseDoesNotClaimTargetScreenshot(t *testing.T) {
	result := finalizeVerifiedCloseLifecycle(closedWindowFinalObservation(
		&AXBatchContext{WindowRef: "w7"},
		observation{mode: contract.ObserveScreenshot},
	))
	if result.IsError {
		t.Fatalf("missing screenshot after verified close must not reverse success: %#v", result)
	}
	if result.StructuredContent["final_observation_returned"] != false ||
		result.StructuredContent["screenshot_returned"] != false ||
		result.StructuredContent["target_window_absent"] != true ||
		result.StructuredContent["observation_required"] != true ||
		result.StructuredContent["next_identical_observation_required"] != false {
		t.Fatalf("closed-window screenshot evidence is dishonest: %#v", result.StructuredContent)
	}
	if result.StructuredContent["close_effect_verified"] != true {
		t.Fatalf("verified close effect was hidden: %#v", result.StructuredContent)
	}
}

func TestCloseObserveNoneStillVerifiesLifecycleWithoutReturningObservation(
	t *testing.T,
) {
	service, appRef, sourceWindowRef, _ := sheetBatchService(t)
	backend := &recordingPlatformBackend{
		accessibilityResponses: []ProviderAccessibilityResult{{
			OK:    false,
			Error: "stale_window_ref: source window is gone",
			Failure: &ProviderFailure{
				Code:          "stale_window_ref",
				Phase:         "precondition",
				FactsComplete: true,
				Subject:       "window",
			},
			RecoveryContext: &ProviderRecoveryContext{
				SourceAppStatus:    "absent",
				SourceWindowStatus: "gone",
				InventoryComplete:  true,
			},
		}},
	}
	service.Backend = backend
	context := &AXBatchContext{
		AppRef:       appRef,
		WindowRef:    sourceWindowRef,
		AppToken:     appRef,
		WindowHandle: "w2:0123456789abcdef0123456789abcdef",
	}
	actionResult := ToolCallResult{
		IsError: true,
		Content: []ContentItem{{
			Type: "text",
			Text: "close input emitted; final observation unavailable",
		}},
		StructuredContent: map[string]any{
			"failure_code":                   "stale_window_ref",
			"failure_phase":                  "post_action_observation",
			"final_observation_returned":     false,
			"observation_required":           true,
			"input_events_emitted":           true,
			"effect_unknown":                 true,
			"mutation_unknown":               true,
			"cause_unattributed":             true,
			"partial_input_possible":         false,
			"post_action_observation_status": "unknown",
		},
	}

	result := service.recoverClosedWindowActionObservation(
		context,
		axBatchAction{actionType: "close"},
		actionResult,
		observation{mode: contract.ObserveNone},
	)
	if result.IsError ||
		result.StructuredContent["target_window_absent"] != true ||
		result.StructuredContent["close_effect_verified"] != true ||
		result.StructuredContent["final_observation_kind"] != "none" ||
		result.StructuredContent["final_observation_returned"] != false ||
		result.StructuredContent["observation_required"] != false ||
		result.StructuredContent["next_identical_observation_required"] != false {
		t.Fatalf("observe=none close lifecycle evidence is incomplete: %#v", result)
	}
	for _, key := range []string{
		"effect_unknown",
		"mutation_unknown",
		"cause_unattributed",
		"continuation",
		"observation_failure_code",
		"post_action_observation_status",
	} {
		if _, present := result.StructuredContent[key]; present {
			t.Fatalf("verified close retained superseded %s: %#v",
				key, result.StructuredContent)
		}
	}
	text := ResultText(result)
	if strings.Contains(text, "ax_diff:") ||
		!strings.Contains(text, `observe="none" returned no final tree or screenshot`) {
		t.Fatalf("observe=none close output is misleading: %q", text)
	}
	if len(backend.observeRequests) != 1 {
		t.Fatalf("close lifecycle probes=%d, want 1", len(backend.observeRequests))
	}
}

func TestVerifiedClosePreservesIndependentActionUncertainty(t *testing.T) {
	result := finalizeVerifiedCloseLifecycle(ToolCallResult{
		IsError: true,
		Content: []ContentItem{{
			Type: "text",
			Text: "input emitted\nfinal_observation_unavailable: stale app\n" +
				"partial input remains possible",
		}},
		StructuredContent: map[string]any{
			"target_window_absent":       true,
			"final_observation_returned": true,
			"failure_code":               "effect_unknown",
			"failure_phase":              "action",
			"input_events_emitted":       true,
			"partial_input_possible":     true,
			"mutation_unknown":           true,
			"effect_unknown":             true,
			"observation_required":       false,
			"task_effect_verified":       true,
		},
	})
	if !result.IsError ||
		result.StructuredContent["close_effect_verified"] != true ||
		result.StructuredContent["failure_code"] != "effect_unknown" ||
		result.StructuredContent["partial_input_possible"] != true ||
		result.StructuredContent["mutation_unknown"] != true ||
		result.StructuredContent["effect_unknown"] != true {
		t.Fatalf("close proof erased independent action uncertainty: %#v", result)
	}
	if _, present := result.StructuredContent["task_effect_verified"]; present {
		t.Fatalf("close proof claimed whole-task success: %#v",
			result.StructuredContent)
	}
	text := ResultText(result)
	if strings.Contains(text, "final_observation_unavailable:") ||
		!strings.Contains(text, "input emitted") ||
		!strings.Contains(text, "partial input remains possible") {
		t.Fatalf("close cleanup removed unrelated evidence: %q", text)
	}
}
