package core

import "testing"

func TestSuccessfulActionFinalObservationExposesPartialCoverage(t *testing.T) {
	for _, kind := range []string{"content", "tree", "diff", "working_tree", "focused", "full", "none", "screenshot", "lifecycle", "unavailable"} {
		t.Run(kind, func(t *testing.T) {
			returned := kind != "none" && kind != "unavailable"
			result := ToolCallResult{StructuredContent: map[string]any{
				"input_events_emitted": true, "final_observation_kind": kind,
				"final_observation_returned": returned,
				"coverage_incomplete_nodes":  3, "tree_complete_for_model": false,
			}}
			facts := publicBatchEvidence(t, result)
			wantPartial := returned && kind != "screenshot" && kind != "lifecycle"
			if (facts["coverage"] == "partial") != wantPartial ||
				wantPartial && facts["tree_complete"] != false {
				t.Fatalf("kind %s coverage mismatch: %#v", kind, facts)
			}
			if facts["ok"] != true || facts["input_emitted"] != true {
				t.Fatalf("coverage changed successful action evidence: %#v", facts)
			}
		})
	}
}

func TestFinalObservationCoverageReplacesEarlierCoverage(t *testing.T) {
	for _, partial := range []bool{false, true} {
		for _, failed := range []bool{false, true} {
			action := ToolCallResult{IsError: failed, StructuredContent: map[string]any{
				"input_events_emitted": true, "mutation_unknown": failed,
				"coverage_incomplete_nodes":       99,
				"coverage_incomplete_reasons":     []string{"old_timeout"},
				"tree_complete_for_model":         false,
				"structural_budget_applied":       true,
				"structural_budget_omitted_nodes": 88,
			}}
			if failed {
				action.StructuredContent["failure_code"] = "effect_unknown"
				action.StructuredContent["failure_phase"] = "mutation"
			}
			current := ToolCallResult{StructuredContent: map[string]any{
				"final_observation_kind": "content", "final_observation_returned": true,
				"tree_complete_for_model": !partial,
			}}
			if partial {
				current.StructuredContent["coverage_incomplete_nodes"] = 1
				current.StructuredContent["coverage_incomplete_reasons"] = []string{"new_timeout"}
			}
			merged := mergeAXBatchFinalObservation(action, current)
			facts := publicBatchEvidence(t, merged)
			if merged.IsError != failed || facts["input_emitted"] != true ||
				merged.StructuredContent["mutation_unknown"] != failed ||
				(facts["coverage"] == "partial") != partial {
				t.Fatalf("new observation changed action or used old coverage: %#v", facts)
			}
			if merged.StructuredContent["structural_budget_omitted_nodes"] != nil ||
				merged.StructuredContent["structural_budget_applied"] != nil {
				t.Fatalf("old structural coverage survived: %#v", merged.StructuredContent)
			}
			if !partial && (merged.StructuredContent["coverage_incomplete_nodes"] != nil ||
				merged.StructuredContent["coverage_incomplete_reasons"] != nil || facts["tree_complete"] == false) {
				t.Fatalf("complete current observation inherited partial coverage: %#v", merged.StructuredContent)
			}
			if failed && merged.StructuredContent["failure_phase"] != "mutation" {
				t.Fatalf("observation replaced action failure: %#v", merged.StructuredContent)
			}
		}
	}
}
