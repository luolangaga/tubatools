package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestAXModelOutputDropsProviderImagesUnlessExplicitlyRequested(t *testing.T) {
	imageResult := ToolCallResult{Content: []ContentItem{
		{Type: "text", Text: "semantic evidence"},
		{Type: "image", Data: "provider-overreturned-image", MimeType: "image/png"},
	}}
	tests := []struct {
		name      string
		tool      string
		args      map[string]any
		wantImage bool
	}{
		{
			name: "act default diff",
			tool: "AX",
			args: map[string]any{"mode": "act"},
		},
		{
			name: "act explicit diff",
			tool: "AX",
			args: map[string]any{"mode": "act", "observe": "diff"},
		},
		{
			name: "act none",
			tool: "AX",
			args: map[string]any{"mode": "act", "observe": "none"},
		},
		{
			name:      "act screenshot",
			tool:      "AX",
			args:      map[string]any{"mode": "act", "observe": "screenshot"},
			wantImage: true,
		},
		{
			name: "observe default diff",
			tool: "AX",
			args: map[string]any{"mode": "observe"},
		},
		{
			name: "observe explicit diff",
			tool: "AX",
			args: map[string]any{"mode": "observe", "observe": "diff"},
		},
		{
			name: "observe tree",
			tool: "AX",
			args: map[string]any{"mode": "observe", "observe": "tree"},
		},
		{
			name:      "observe screenshot",
			tool:      "AX",
			args:      map[string]any{"mode": "observe", "observe": "screenshot"},
			wantImage: true,
		},
		{
			name:      "observe full",
			tool:      "AX",
			args:      map[string]any{"mode": "observe", "observe": "full"},
			wantImage: true,
		},
		{
			name: "apps",
			tool: "AX",
			args: map[string]any{"mode": "apps"},
		},
		{
			name: "find",
			tool: "AX",
			args: map[string]any{"mode": "find"},
		},
		{
			name: "observe until without image",
			tool: "AX",
			args: map[string]any{"mode": "observe", "until": map[string]any{"type": "title", "text": "x"}},
		},
		{
			name:      "gui is unaffected",
			tool:      "GUI",
			args:      map[string]any{},
			wantImage: true,
		},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			got := enforceAXImageOutputPolicy(test.tool, test.args, imageResult)
			hasImage := false
			for _, item := range got.Content {
				if item.Type == "image" {
					hasImage = true
				}
			}
			if hasImage != test.wantImage {
				t.Fatalf(
					"image visibility = %t, want %t; content=%#v",
					hasImage,
					test.wantImage,
					got.Content,
				)
			}
			if len(got.Content) == 0 || got.Content[0].Text != "semantic evidence" {
				t.Fatalf("image policy changed semantic content: %#v", got.Content)
			}
		})
	}
}

func TestPrepareAXEvidenceKeepsUnderlyingFailureCode(t *testing.T) {
	result := ToolCallResult{
		IsError: true,
		Content: []ContentItem{{
			Type: "text",
			Text: "effect unknown",
		}},
		StructuredContent: map[string]any{
			"failure_code":            "effect_unknown",
			"underlying_failure_code": "missing_post_action_snapshot",
			"failure_phase":           "final_observation",
			"input_events_emitted":    true,
			"mutation_unknown":        true,
		},
	}
	visible := PrepareAXEvidence(result)
	if visible.StructuredContent["underlying_failure_code"] !=
		"missing_post_action_snapshot" {
		t.Fatalf(
			"model output lost the underlying failure code: %#v",
			visible.StructuredContent,
		)
	}
}

func TestPrepareAXEvidenceReportsStructuralOmission(t *testing.T) {
	result := ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: "ax:\n - /omitted: budget"}},
		StructuredContent: map[string]any{
			"ax_mode":                           "observe",
			"canonical_element_count":           430,
			"tree_complete_for_model":           false,
			"truncation_reason":                 "structural_budget",
			"structural_budget_applied":         true,
			"structural_budget_omitted_nodes":   310,
			"structural_budget_omitted_targets": 40,
			"shadow_ast": map[string]any{
				"projected_nodes": 120,
			},
		},
	}
	result = PrepareAXEvidence(result)
	if result.StructuredContent["tree_complete"] != false ||
		result.StructuredContent["structural_budget_omitted_nodes"] != nil ||
		result.StructuredContent["structural_budget_omitted_targets"] != nil ||
		result.StructuredContent["truncation_reason"] != nil {
		t.Fatalf("structural omission facts were lost: %#v",
			result.StructuredContent)
	}
	recover, ok := result.StructuredContent["recover"].(map[string]any)
	if !ok || recover["structural_budget"] != "find|root" {
		t.Fatalf("structural omission recovery was lost: %#v", result.StructuredContent)
	}
}

func TestTargetScopedCoverageRecoveryOnlyAfterTargetedFailure(t *testing.T) {
	tests := []struct {
		name        string
		full        map[string]any
		isError     bool
		wantPartial bool
		wantRecover bool
	}{
		{
			name: "find match suppresses unrelated coverage warning",
			full: map[string]any{
				"ax_mode":                   "find",
				"find_match_count":          1,
				"coverage_incomplete_nodes": 12,
				"tree_complete_for_model":   false,
			},
		},
		{
			name: "find miss exposes targeted recovery",
			full: map[string]any{
				"ax_mode":                   "find",
				"find_match_count":          0,
				"coverage_incomplete_nodes": 12,
				"tree_complete_for_model":   false,
			},
			wantPartial: true,
			wantRecover: true,
		},
		{
			name: "successful action suppresses unrelated coverage warning",
			full: map[string]any{
				"ax_mode":                   "act",
				"coverage_incomplete_nodes": 12,
				"tree_complete_for_model":   false,
				"input_events_emitted":      true,
			},
		},
		{
			name: "failed resolve exposes targeted recovery",
			full: map[string]any{
				"ax_mode":                   "act",
				"coverage_incomplete_nodes": 12,
				"tree_complete_for_model":   false,
				"failure_phase":             "resolve",
				"input_events_emitted":      false,
			},
			isError:     true,
			wantPartial: true,
			wantRecover: true,
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			result := compactAXModelEvidence(test.full, test.isError)
			if got := result["coverage"] == "partial"; got != test.wantPartial {
				t.Fatalf("coverage partial=%t, want %t: %#v",
					got, test.wantPartial, result)
			}
			recover, _ := result["recover"].(map[string]any)
			got := recover["target"] == "GUI" ||
				recover["target"] == "find|GUI"
			if got != test.wantRecover {
				t.Fatalf("target recovery=%t, want %t: %#v",
					got, test.wantRecover, result)
			}
		})
	}
}

func TestPrepareAXEvidenceKeepsPreflightFailureFacts(t *testing.T) {
	result := ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: "stale ref"}},
		IsError: true,
		StructuredContent: map[string]any{
			"ax_mode":                 "act",
			"failure_code":            "stale_element_ref",
			"failure_phase":           "precondition",
			"input_events_emitted":    false,
			"partial_input_possible":  false,
			"mutation_unknown":        false,
			"failure_facts_complete":  true,
			"task_effect_verified":    false,
			"response_budget_chars":   24000,
			"tree_complete_for_model": true,
		},
	}
	result = PrepareAXEvidence(result)
	for key, expected := range map[string]any{
		"code":                   contract.AXCodeStaleRef,
		"failure_code":           "stale_element_ref",
		"failure_phase":          "precondition",
		"input_emitted":          false,
		"partial_input_possible": false,
		"mutation_unknown":       false,
		"failure_facts_complete": true,
	} {
		if result.StructuredContent[key] != expected {
			t.Fatalf("preflight fact %s lost: %#v", key,
				result.StructuredContent)
		}
	}
	if _, present := result.StructuredContent["response_budget_chars"]; present {
		t.Fatalf("preflight evidence leaked normal telemetry: %#v",
			result.StructuredContent)
	}
}

func TestPrepareAXEvidenceKeepsPartialBatchFailure(t *testing.T) {
	result := ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: "batch stopped"}},
		IsError: true,
		StructuredContent: map[string]any{
			"ax_mode": "act",
			"a11y_batch_result": map[string]any{
				"actions_requested":                    2,
				"actions_completed_without_call_error": 1,
				"failed_action_index":                  1,
				"failed_action_type":                   "click",
				"partial_sequence":                     true,
				"actions": []map[string]any{
					{
						"action_index": 0,
						"action_type":  "set_value",
						"call_error":   false,
						"actionability": map[string]any{
							"route":         "editable_text",
							"input_emitted": "true",
						},
					},
					{
						"action_index":  1,
						"action_type":   "click",
						"call_error":    true,
						"failure_code":  "stale_element_ref",
						"failure_phase": "precondition",
						"actionability": map[string]any{
							"resolution":    "failed",
							"route":         "unknown",
							"input_emitted": "false",
						},
					},
				},
			},
			"observation_required": true,
		},
	}
	result = PrepareAXEvidence(result)
	batch, ok := result.StructuredContent["batch"].(map[string]any)
	if !ok || batch["partial_sequence"] != true ||
		batch["failed_action_index"] != 1 {
		t.Fatalf("partial batch summary is incomplete: %#v",
			result.StructuredContent)
	}
	actions, ok := batch["actions"].([]map[string]any)
	if !ok || len(actions) != 1 ||
		actions[0]["index"] != 1 ||
		actions[0]["failure_code"] != "stale_element_ref" ||
		actions[0]["failure_phase"] != "precondition" {
		t.Fatalf("ordered partial action evidence was lost: %#v", batch)
	}
	if _, present := actions[0]["route"]; present {
		t.Fatalf("provider route leaked into model evidence: %#v", actions[0])
	}
}

func TestPrepareAXEvidenceOmitsRoutineBatchDefaults(t *testing.T) {
	result := PrepareAXEvidence(ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: "batch result"}},
		StructuredContent: map[string]any{
			"ax_mode": "act",
			"a11y_batch_result": map[string]any{
				"actions_requested":                    1,
				"actions_completed_without_call_error": 1,
				"partial_sequence":                     false,
				"actions": []map[string]any{{
					"action_index": 0,
					"action_type":  "click",
					"call_error":   false,
					"actionability": map[string]any{
						"route":                  "semantic_action",
						"input_emitted":          true,
						"readback_sampled":       false,
						"delivery_verified":      "unknown",
						"postcondition_verified": "unknown",
					},
				}},
			},
		},
	})
	if _, present := result.StructuredContent["batch"]; present {
		t.Fatalf("routine successful batch leaked internal action details: %#v",
			result.StructuredContent)
	}
}

func TestPrepareAXEvidenceKeepsContinuationSeparated(t *testing.T) {
	result := ToolCallResult{
		Content: []ContentItem{
			{Type: "text", Text: "stale_element_ref"},
			{Type: "text", Text: "recovery detail"},
		},
		StructuredContent: map[string]any{
			"continuation": map[string]any{
				"state":              "ready",
				"retry_same_request": false,
				"next_mode":          "find",
				"fresh_window_ref":   "w3",
			},
		},
	}
	result = PrepareAXEvidence(result)
	joined := ResultText(result)
	if strings.Contains(joined, "w3recovery") {
		t.Fatalf("continuation and following text were concatenated: %q", joined)
	}
}

func TestPrepareAXEvidenceCannotExposeEmittedInputAsPreflight(t *testing.T) {
	result := ToolCallResult{
		IsError: true,
		StructuredContent: map[string]any{
			"preflight_only":       true,
			"mutation_unknown":     true,
			"input_events_emitted": true,
			"a11y_batch_result": map[string]any{
				"actions": []any{
					map[string]any{"input_emitted": true},
				},
			},
		},
	}
	result = PrepareAXEvidence(result)
	if preflight, _ := result.StructuredContent["preflight_only"].(bool); preflight {
		t.Fatalf("post-emission evidence remained preflight-only: %#v", result.StructuredContent)
	}
	if scope := result.StructuredContent["evidence_scope"]; scope != "post_action_observation" {
		t.Fatalf("post-emission unknown evidence scope = %#v", scope)
	}
}

func TestPrepareAXEvidenceKeepsContinuationVisibleWithoutStructuredContent(t *testing.T) {
	result := ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: "stale_element_ref"}},
		IsError: true,
		StructuredContent: map[string]any{
			"failure_code": "stale_element_ref",
			"continuation": map[string]any{
				"state":              "ready",
				"retry_same_request": false,
				"next_mode":          "find",
				"fresh_window_ref":   "w3",
			},
		},
	}
	prepared := PrepareAXEvidence(result)
	text := ResultText(prepared)
	for _, expected := range []string{
		"continuation:",
		"state=ready",
		"retry_same_request=false",
		"next_mode=find",
		"fresh_window_ref=w3",
	} {
		if !strings.Contains(text, expected) {
			t.Fatalf("model-visible continuation lost %q: %q", expected, text)
		}
	}
	if _, ok := prepared.StructuredContent["continuation"]; !ok {
		t.Fatalf("compact structured continuation missing: %#v",
			prepared.StructuredContent)
	}
}

func TestPrepareAXEvidenceKeepsAssertionTimeoutFacts(t *testing.T) {
	result := ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: "assertion timed out"}},
		IsError: true,
		StructuredContent: map[string]any{
			"ax_mode":                   "assert_ax",
			"failure_code":              "assertion_timeout",
			"failure_phase":             "assertion",
			"assertion_sampled_live_ui": true,
			"preflight_only":            false,
			"predicate_verified":        false,
			"task_completion_verified":  false,
			"distillation":              []any{"debug-only"},
		},
	}
	result = PrepareAXEvidence(result)
	if result.StructuredContent["failure_code"] != "assertion_timeout" ||
		result.StructuredContent["failure_phase"] != "assertion" ||
		result.StructuredContent["assertion_sampled_live_ui"] != true ||
		result.StructuredContent["predicate_verified"] != false {
		t.Fatalf("assertion timeout evidence was lost: %#v",
			result.StructuredContent)
	}
	if _, present := result.StructuredContent["distillation"]; present {
		t.Fatalf("assertion timeout leaked debug telemetry")
	}
}

// Internal predicate evidence reaches the model under the public name wait.
func TestPrepareAXEvidenceNamesPredicateEvidenceWait(t *testing.T) {
	result := ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: "batch result"}},
		StructuredContent: map[string]any{
			"ax_mode": "act",
			"a11y_batch_result": map[string]any{
				"actions_requested":                    1,
				"actions_completed_without_call_error": 1,
				"actions": []map[string]any{
					{
						"action_index": 0,
						"action_type":  "click",
						"call_error":   false,
						"actionability": map[string]any{
							"route":                  "semantic_action",
							"input_emitted":          "true",
							"readback_sampled":       "true",
							"delivery_verified":      "true",
							"postcondition_verified": "true",
						},
					},
				},
				"postcondition": map[string]any{
					"status": "pass",
				},
			},
			"actionability": map[string]any{
				"stages": []any{"full telemetry only"},
			},
		},
	}
	result = PrepareAXEvidence(result)
	batch := result.StructuredContent["batch"].(map[string]any)
	postcondition, ok := batch["wait"].(map[string]any)
	if !ok || postcondition["status"] != "pass" {
		t.Fatalf("requested postcondition evidence was lost: %#v", batch)
	}
	if _, present := batch["actions"]; present {
		t.Fatalf("routine per-action routing leaked with postcondition: %#v", batch)
	}
	if _, present := result.StructuredContent["actionability"]; present {
		t.Fatalf("top-level duplicate actionability leaked into compact evidence")
	}
}

func TestPrepareAXEvidenceKeepsUnknownFinalObservation(t *testing.T) {
	result := PrepareAXEvidence(ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: "action delivered"}},
		StructuredContent: map[string]any{
			"ax_mode":                         "act",
			"input_events_emitted":            true,
			"effect_unknown":                  true,
			"mutation_unknown":                true,
			"final_observation_returned":      false,
			"observation_required":            true,
			"observation_failure_code":        "runtime_timeout",
			"post_action_observation_status":  "unknown",
			"postcondition_verified":          false,
			"task_completion_verified":        false,
			"provider_internal_debug_payload": "hidden",
		},
	})
	for key, expected := range map[string]any{
		"input_emitted":                  true,
		"effect_unknown":                 true,
		"mutation_unknown":               true,
		"final_observation_returned":     false,
		"observation_required":           true,
		"observation_failure_code":       "runtime_timeout",
		"post_action_observation_status": "unknown",
		"wait_verified":                  false,
	} {
		if result.StructuredContent[key] != expected {
			t.Fatalf("%s=%#v want %#v in %#v",
				key, result.StructuredContent[key], expected,
				result.StructuredContent)
		}
	}
	if _, present := result.StructuredContent["provider_internal_debug_payload"]; present {
		t.Fatalf("provider debug payload leaked: %#v", result.StructuredContent)
	}
}

func TestPrepareAXEvidenceKeepsFinalObservationKind(t *testing.T) {
	result := PrepareAXEvidence(ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: "fresh current tree"}},
		StructuredContent: map[string]any{
			"ax_mode":                    "act",
			"final_observation_returned": true,
			"final_observation_kind":     "working_tree",
			"working_tree_fallback":      true,
		},
	})
	if result.StructuredContent["final_observation_kind"] != "working_tree" {
		t.Fatalf("final observation kind was hidden: %#v",
			result.StructuredContent)
	}
	if _, present := result.StructuredContent["working_tree_fallback"]; present {
		t.Fatalf("redundant fallback telemetry leaked: %#v",
			result.StructuredContent)
	}
}

func TestPrepareAXEvidenceKeepsFinalWindowTransition(t *testing.T) {
	result := PrepareAXEvidence(ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: "current window"}},
		StructuredContent: map[string]any{
			"ax_mode":                                           "act",
			"final_observation_returned":                        true,
			"source_window_status":                              "gone",
			"final_observation_window_ref":                      "w9",
			"final_observation_followed_window_transition":      true,
			"observation_required":                              false,
			"next_identical_observation_required":               false,
			"provider_internal_final_observation_window_handle": "hidden",
		},
	})
	for key, expected := range map[string]any{
		"final_observation_returned":                   true,
		"source_window_status":                         "gone",
		"final_observation_window_ref":                 "w9",
		"final_observation_followed_window_transition": true,
		"observation_required":                         false,
		"next_identical_observation_required":          false,
	} {
		if result.StructuredContent[key] != expected {
			t.Fatalf("%s=%#v want %#v in %#v",
				key, result.StructuredContent[key], expected,
				result.StructuredContent)
		}
	}
	if _, present :=
		result.StructuredContent["provider_internal_final_observation_window_handle"]; present {
		t.Fatalf("private window identity leaked: %#v", result.StructuredContent)
	}
}

func TestPrepareAXEvidenceFullViewStillHidesDebugTelemetry(t *testing.T) {
	full := map[string]any{
		"ax_mode":      "observe",
		"tree_view":    "full",
		"distillation": []any{"complete-stage-report"},
		"shadow_ast":   map[string]any{"canonical_nodes": 10},
	}
	result := PrepareAXEvidence(ToolCallResult{
		Content:           []ContentItem{{Type: "text", Text: "full tree"}},
		StructuredContent: full,
	})
	if result.StructuredContent["distillation"] != nil ||
		result.StructuredContent["shadow_ast"] != nil {
		t.Fatalf("full view leaked debug telemetry: %#v",
			result.StructuredContent)
	}
	if len(result.StructuredContent) != 2 ||
		result.StructuredContent["ok"] != true ||
		result.StructuredContent["input_emitted"] != false {
		t.Fatalf("full view returned internal evidence: %#v",
			result.StructuredContent)
	}
}

func TestDelimitAXTextContentPreservesItemBoundaries(t *testing.T) {
	result := ToolCallResult{
		Content: []ContentItem{
			{Type: "text", Text: "observe: none"},
			{Type: "text", Text: "native transition"},
			{Type: "image", Data: "ignored"},
		},
	}
	result = delimitAXTextContent(result)
	if got := result.Content[0].Text; got != "observe: none\n" {
		t.Fatalf("first text item was not delimited: %q", got)
	}
	if got := result.Content[1].Text; got != "native transition\n" {
		t.Fatalf("second text item was not delimited: %q", got)
	}
}

func TestContinuationTextIncludesRoute(t *testing.T) {
	result := appendAXContinuationText(ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: "observe: none\n"}},
	}, map[string]any{
		"state":              "ready",
		"retry_same_request": false,
		"route":              "GUI",
		"next_mode":          "screenshot",
	})
	text := result.Content[0].Text
	if !strings.Contains(text, "route=GUI") {
		t.Fatalf("continuation omitted route: %q", text)
	}
}
