package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestPostconditionFinalObservationUsesLatestSnapshot(t *testing.T) {
	service := NewService()
	current := &AppSnapshot{
		App:                 AppDescriptor{Name: "Editor"},
		WindowTitle:         "Document",
		WindowHandle:        "window-1",
		SnapshotID:          "after",
		ScreenshotPNGBase64: "latest-image",
		ScreenshotMimeType:  "image/png",
	}
	result := service.renderAXBatchFinalObservation(
		current,
		nil,
		observation{mode: contract.ObserveScreenshot},
	)
	foundLatest := false
	for _, item := range result.Content {
		if item.Type == "image" && item.Data == "latest-image" {
			foundLatest = true
			break
		}
	}
	if !foundLatest {
		t.Fatalf("postcondition final screenshot did not use the latest snapshot: %#v",
			result.Content)
	}
	if result.StructuredContent["final_observation_returned"] != true {
		t.Fatalf("final observation evidence missing: %#v", result.StructuredContent)
	}
}

func TestFinalObservationContinuationRequiresCompleteGoneSourceAndSameApp(t *testing.T) {
	service, appRef, sourceWindowRef, _ := sheetBatchService(t)
	currentWindowRef := service.WindowRefRegistry.Acquire(
		"Editor",
		"w2:11111111111111111111111111111111",
		`window "Document"`,
		"",
		":1.8",
		"/org/a11y/atspi/accessible/8",
		"pid:44:start:100",
		"frame",
		"Document",
		appRef,
	)
	context := &AXBatchContext{
		AppRef:       appRef,
		WindowRef:    sourceWindowRef,
		AppToken:     appRef,
		WindowHandle: "w2:0123456789abcdef0123456789abcdef",
	}
	valid := ToolCallResult{
		IsError: true,
		StructuredContent: map[string]any{
			"failure_code":                "stale_window_ref",
			"source_window_status":        "gone",
			"recovery_inventory_complete": true,
			"failure_facts_complete":      true,
			"current_window_ref":          currentWindowRef,
		},
	}
	entry, ok := service.finalObservationContinuation(context, valid)
	if !ok || entry == nil || entry.Ref != currentWindowRef {
		t.Fatalf("valid final observation continuation rejected: %#v, %t", entry, ok)
	}

	for name, mutate := range map[string]func(map[string]any){
		"incomplete inventory": func(fields map[string]any) {
			fields["recovery_inventory_complete"] = false
		},
		"source not gone": func(fields map[string]any) {
			fields["source_window_status"] = "present"
		},
		"unknown lifecycle facts": func(fields map[string]any) {
			fields["failure_facts_complete"] = false
		},
		"wrong failure": func(fields map[string]any) {
			fields["failure_code"] = "observation_incomplete"
		},
		"same window": func(fields map[string]any) {
			fields["current_window_ref"] = sourceWindowRef
		},
	} {
		t.Run(name, func(t *testing.T) {
			fields := map[string]any{}
			for key, value := range valid.StructuredContent {
				fields[key] = value
			}
			mutate(fields)
			if entry, ok := service.finalObservationContinuation(
				context,
				ToolCallResult{IsError: true, StructuredContent: fields},
			); ok || entry != nil {
				t.Fatalf("unsafe continuation accepted: %#v", entry)
			}
		})
	}
}

func TestFinalObservationDoesNotInferCloseFromNonCloseWindowDisappearance(
	t *testing.T,
) {
	service, appRef, sourceWindowRef, _ := sheetBatchService(t)
	service.Backend = &recordingPlatformBackend{
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
				SourceAppStatus:    "alive",
				SourceWindowStatus: "gone",
				InventoryComplete:  true,
			},
		}},
	}
	context := &AXBatchContext{
		AppRef:       appRef,
		WindowRef:    sourceWindowRef,
		AppToken:     appRef,
		WindowHandle: "w2:0123456789abcdef0123456789abcdef",
	}
	result := service.axBatchFinalObservation(
		context,
		observation{mode: contract.ObserveDiff},
		false,
	)
	if !result.IsError ||
		result.StructuredContent["target_window_absent"] == true ||
		result.StructuredContent["observation_required"] != true {
		t.Fatalf("non-close disappearance became lifecycle evidence: %#v", result)
	}
}

func TestEmptyScreenshotDoesNotClaimFinalObservation(t *testing.T) {
	snapshot := &AppSnapshot{
		App:          AppDescriptor{Name: "Editor"},
		WindowTitle:  "Document",
		WindowHandle: "window-1",
	}
	result, returned := renderAXScreenshotObservation(snapshot, nil)
	if returned ||
		result.StructuredContent["final_observation_returned"] != false ||
		result.StructuredContent["screenshot_returned"] != false ||
		result.StructuredContent["final_observation_kind"] != "unavailable" ||
		result.StructuredContent["observation_required"] != true {
		t.Fatalf("empty screenshot overstated final observation: %#v",
			result.StructuredContent)
	}
}

func TestEmptyCompactFallbackDoesNotClaimFinalObservation(t *testing.T) {
	service := NewService()
	snapshot := &AppSnapshot{
		App:          AppDescriptor{Name: "Editor"},
		WindowTitle:  "Document",
		WindowHandle: "window-1",
		TreeLines:    []string{"ax:"},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: nil,
			Roots:          nil,
		},
	}
	result := service.renderAXBatchFinalObservation(
		snapshot,
		nil,
		observation{mode: contract.ObserveDiff},
	)
	if result.IsError {
		t.Fatalf("missing compact observation must not reverse an emitted action: %#v",
			result)
	}
	if result.StructuredContent["final_observation_returned"] != false ||
		result.StructuredContent["final_observation_kind"] != "unavailable" ||
		result.StructuredContent["observation_required"] != true ||
		result.StructuredContent["delivered_snapshot_baseline_advanced"] != false {
		t.Fatalf("empty compact fallback overstated final observation: %#v",
			result.StructuredContent)
	}
	if !strings.Contains(ResultText(result), "final_observation_unavailable") {
		t.Fatalf("missing compact observation lacked recovery text: %q",
			ResultText(result))
	}
}

func TestSharedDiffFallbackDoesNotClaimEmptyObservation(t *testing.T) {
	service := NewService()
	snapshot := &AppSnapshot{
		App:          AppDescriptor{Name: "Editor"},
		WindowTitle:  "Document",
		WindowHandle: "window-1",
		TreeLines:    []string{"ax:"},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: nil,
			Roots:          nil,
		},
	}
	result, lines, returned, compact, fullTree :=
		service.renderAXDiffOrCompactObservation(snapshot, nil)
	if returned || compact || fullTree || len(lines) != 0 {
		t.Fatalf("empty fallback was reported as an observation: returned=%t compact=%t full=%t lines=%#v",
			returned, compact, fullTree, lines)
	}
	if result.StructuredContent["final_observation_returned"] != false ||
		result.StructuredContent["observation_required"] != true ||
		result.StructuredContent["next_identical_observation_required"] != true {
		t.Fatalf("shared action fallback overstated observation evidence: %#v",
			result.StructuredContent)
	}
}

func TestSharedDiffFallbackIdentifiesFocusedObservation(t *testing.T) {
	service := NewService()
	snapshot := editorFormSnapshot()
	typedTreeTextForTest(t, snapshot)
	result, lines, returned, compact, fullTree :=
		service.renderAXDiffOrCompactObservation(snapshot, nil)
	if !returned || !compact || fullTree || len(lines) == 0 ||
		result.StructuredContent["final_observation_kind"] != "focused" {
		t.Fatalf("focused observation kind is incomplete: result=%#v lines=%#v",
			result.StructuredContent, lines)
	}
}

func TestSharedDiffFallbackPrefersWorkingTreeForStabilizedWindow(t *testing.T) {
	service := NewService()
	snapshot := editorFormSnapshot()
	typedTreeTextForTest(t, snapshot)

	result, lines, returned, compact, fullTree :=
		service.renderAXWorkingObservation(snapshot, nil)
	if !returned || compact || !fullTree || len(lines) <= 1 {
		t.Fatalf("stabilized working tree missing: returned=%t compact=%t full=%t lines=%#v",
			returned, compact, fullTree, lines)
	}
	if result.StructuredContent["final_observation_kind"] != "working_tree" ||
		!strings.Contains(ResultText(result), `button "Save"`) {
		t.Fatalf("stabilized working tree is incomplete: %#v\n%s",
			result.StructuredContent, ResultText(result))
	}
}

func TestBatchFinalObservationAdvancesFreshWorkingTreeBaseline(t *testing.T) {
	service := NewService()
	snapshot := editorFormSnapshot()
	for index := range snapshot.Elements {
		snapshot.Elements[index].States = strings.ReplaceAll(
			snapshot.Elements[index].States,
			"[focused]",
			"",
		)
	}
	typedTreeTextForTest(t, snapshot)
	snapshot.sourceApp = "editor"
	snapshot.publicWindowRef = "w2"
	snapshot.projection = snapshotProjection{
		View:  contract.TreeViewWorking,
		Scope: contract.AXScopeWindow,
	}

	result := service.renderAXBatchFinalObservation(
		snapshot,
		nil,
		observation{mode: contract.ObserveDiff},
	)
	if result.StructuredContent["final_observation_returned"] != true ||
		result.StructuredContent["observation_required"] == true ||
		result.StructuredContent["full_tree_returned"] != true ||
		result.StructuredContent["delivered_snapshot_baseline_advanced"] != true {
		t.Fatalf("fresh working tree did not close the observation loop: %#v",
			result.StructuredContent)
	}
	if got := service.deliveredSnapshotFor(snapshot); got != snapshot {
		t.Fatalf("fresh working tree did not become the new diff baseline: got=%#v want=%#v",
			got, snapshot)
	}
}

func TestStripSupersededObservationPlaceholderPreservesOtherLines(t *testing.T) {
	content := []ContentItem{{
		Type: "text",
		Text: "Note: input emitted\n\n" +
			"(observe=\"none\": no final tree or screenshot was returned.)\n" +
			"failure detail",
	}}
	filtered := stripSupersededAXObservationPlaceholder(content)
	if len(filtered) != 1 {
		t.Fatalf("filtered content=%#v", filtered)
	}
	text := filtered[0].Text
	if strings.Contains(text, `observe="none"`) {
		t.Fatalf("placeholder survived: %q", text)
	}
	for _, want := range []string{"Note: input emitted", "failure detail"} {
		if !strings.Contains(text, want) {
			t.Fatalf("non-placeholder evidence %q was removed: %q", want, text)
		}
	}
}

func TestFinalObservationFailureDoesNotOverwritePostconditionFailure(t *testing.T) {
	action := ToolCallResult{
		IsError: true,
		StructuredContent: map[string]any{
			"failure_code":         "postcondition_unknown",
			"failure_phase":        "postcondition",
			"input_events_emitted": true,
		},
	}
	observation := ToolCallResult{
		IsError: true,
		StructuredContent: map[string]any{
			"failure_code":               "observation_incomplete",
			"failure_phase":              "final_observation",
			"final_observation_returned": false,
		},
	}
	merged := mergeAXBatchFinalObservation(action, observation)
	if merged.StructuredContent["failure_code"] != "postcondition_unknown" ||
		merged.StructuredContent["failure_phase"] != "postcondition" ||
		!merged.IsError {
		t.Fatalf("final observation masked postcondition failure: %#v",
			merged.StructuredContent)
	}
}

func TestMissingFinalObservationMarksEmittedMutationUnknown(t *testing.T) {
	action := ToolCallResult{
		StructuredContent: map[string]any{
			"input_events_emitted": true,
			"mutation_unknown":     false,
		},
	}
	observation := ToolCallResult{
		StructuredContent: map[string]any{
			"final_observation_returned": false,
			"observation_required":       true,
		},
	}
	merged := mergeAXBatchFinalObservation(action, observation)
	if merged.IsError ||
		merged.StructuredContent["effect_unknown"] != true ||
		merged.StructuredContent["mutation_unknown"] != true ||
		merged.StructuredContent["observation_failure_code"] !=
			"final_observation_unavailable" {
		t.Fatalf("missing final observation lost uncertainty: %#v", merged)
	}
}

func TestMissingFinalObservationUsesNestedRuntimeEmissionEvidence(t *testing.T) {
	action := ToolCallResult{
		StructuredContent: map[string]any{
			"runtime_action_evidence": RuntimeActionEvidence{
				Route:        "semantic_action",
				InputEmitted: true,
			},
			"actionability": ActionabilityEvidence{
				Route:        "semantic_action",
				InputEmitted: TriTrue,
			},
		},
	}
	observation := ToolCallResult{
		StructuredContent: map[string]any{
			"final_observation_returned": false,
			"observation_required":       true,
		},
	}
	merged := mergeAXBatchFinalObservation(action, observation)
	if merged.IsError ||
		merged.StructuredContent["input_events_emitted"] != true ||
		merged.StructuredContent["effect_unknown"] != true ||
		merged.StructuredContent["mutation_unknown"] != true {
		t.Fatalf("nested emission evidence was ignored: %#v", merged)
	}
}

func TestFinalObservationFailurePreservesSuccessfulAction(t *testing.T) {
	action := ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: "action delivered"}},
		StructuredContent: map[string]any{
			"input_events_emitted": true,
			"mutation_unknown":     false,
		},
	}
	observation := ToolCallResult{
		IsError: true,
		Content: []ContentItem{{
			Type: "text",
			Text: "runtime_timeout: final AX observation timed out",
		}},
		StructuredContent: map[string]any{
			"failure_code":               "runtime_timeout",
			"failure_phase":              "final_observation",
			"final_observation_returned": false,
			"observation_required":       true,
		},
	}
	merged := mergeAXBatchFinalObservation(action, observation)
	if merged.IsError {
		t.Fatalf("post-action observation failure reversed successful delivery: %#v",
			merged)
	}
	if merged.StructuredContent["input_events_emitted"] != true ||
		merged.StructuredContent["effect_unknown"] != true ||
		merged.StructuredContent["mutation_unknown"] != true ||
		merged.StructuredContent["observation_failure_code"] != "runtime_timeout" ||
		merged.StructuredContent["final_observation_returned"] != false ||
		merged.StructuredContent["observation_required"] != true {
		t.Fatalf("post-action observation evidence is incomplete: %#v",
			merged.StructuredContent)
	}
	if _, present := merged.StructuredContent["failure_code"]; present {
		t.Fatalf("observation failure was exposed as action failure: %#v",
			merged.StructuredContent)
	}
}

func TestBatchFinalActionCapturesRequestedObservationDirectly(t *testing.T) {
	final := observation{mode: contract.ObserveDiff}
	intermediate := axBatchActionObservation(0, 2, final, false)
	if intermediate.resolvedMode() != contract.ObserveNone ||
		intermediate.captureSnapshot == nil ||
		!*intermediate.captureSnapshot {
		t.Fatalf("intermediate action observation=%#v", intermediate)
	}
	last := axBatchActionObservation(1, 2, final, false)
	if last.resolvedMode() != contract.ObserveDiff || last.captureSnapshot != nil {
		t.Fatalf("final action did not own requested observation: %#v", last)
	}
	postconditionLast := axBatchActionObservation(1, 2, final, true)
	if postconditionLast.resolvedMode() != contract.ObserveNone ||
		postconditionLast.captureSnapshot == nil ||
		*postconditionLast.captureSnapshot {
		t.Fatalf("postcondition final action captured the wrong time point: %#v",
			postconditionLast)
	}
}

func TestFinalObservationCannotRewriteEmittedActionAsPreflight(t *testing.T) {
	action := ToolCallResult{
		IsError: true,
		StructuredContent: map[string]any{
			"failure_code":         "postcondition_unknown",
			"failure_phase":        "postcondition",
			"input_events_emitted": true,
			"mutation_unknown":     true,
			"preflight_only":       false,
			"evidence_scope":       "post_action_observation",
		},
	}
	observation := ToolCallResult{
		IsError: true,
		StructuredContent: map[string]any{
			"failure_code":               "observation_incomplete",
			"failure_phase":              "final_observation",
			"final_observation_returned": false,
			"preflight_only":             true,
			"evidence_scope":             "error_only",
		},
	}
	merged := mergeAXBatchFinalObservation(action, observation)
	if merged.StructuredContent["preflight_only"] != false {
		t.Fatalf("emitted action was rewritten as preflight: %#v",
			merged.StructuredContent)
	}
	if merged.StructuredContent["evidence_scope"] != "post_action_observation" {
		t.Fatalf("post-action evidence scope was lost: %#v",
			merged.StructuredContent)
	}
}

func TestFinalObservationCannotClearMonotonicActionEvidence(t *testing.T) {
	action := ToolCallResult{
		IsError: true,
		StructuredContent: map[string]any{
			"failure_code":           "postcondition_unknown",
			"failure_phase":          "postcondition",
			"input_events_emitted":   true,
			"partial_input_possible": true,
			"mutation_unknown":       true,
		},
	}
	observation := ToolCallResult{
		IsError: true,
		StructuredContent: map[string]any{
			"input_events_emitted":   false,
			"partial_input_possible": false,
			"mutation_unknown":       false,
		},
	}
	merged := mergeAXBatchFinalObservation(action, observation)
	for _, key := range []string{
		"input_events_emitted",
		"partial_input_possible",
		"mutation_unknown",
	} {
		if merged.StructuredContent[key] != true {
			t.Fatalf("%s was cleared by final observation: %#v",
				key, merged.StructuredContent)
		}
	}
}
