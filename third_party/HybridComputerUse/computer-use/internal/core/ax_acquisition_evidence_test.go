package core

import (
	"encoding/json"
	"reflect"
	"strings"
	"testing"
)

func TestAcquisitionEvidenceSurvivesMixedProviderGaps(t *testing.T) {
	for _, structuralCut := range []bool{false, true} {
		snapshot := editorFormSnapshot()
		snapshot.Annotations = []axProviderAnnotation{
			{Kind: "provider_omission", Count: 3, Reason: "filtered"},
			{Kind: "provider_omission", Count: 2, Reason: "pressure"},
			{Kind: "hidden_children", Count: 7, Reason: "collapsed_menu"},
			{Kind: "hidden_children", Count: 11, Reason: "provider_children_not_rendered"},
			{Kind: axCellSummaryAnnotation, Count: 294, Reason: "empty_cells"},
			{Kind: "provider_omission", Count: 4, Reason: "max_tree_depth"},
			{Kind: "provider_omission", Count: 5, Reason: "time_budget"},
			{Kind: "provider_omission", Count: 6, Reason: "filtered,max_tree_depth,time_budget"},
			{Kind: "provider_omission", Reason: "time_budget", Detail: "private traversal detail"},
			{Kind: "provider_omission"},
		}
		result := TextResult(typedTreeTextForTest(t, snapshot), false)
		addTreeProjectionMetadata(&result, snapshot)
		result.StructuredContent["ax_mode"] = "observe"
		if structuralCut {
			result.StructuredContent["structural_budget_applied"] = true
			result.StructuredContent["structural_budget_omitted_nodes"] = 20
		}
		result = PrepareAXEvidence(EnforceAXResponseBudget(result))
		// Check the evidence line delivered to text-only MCP clients, not
		// just the private metadata that compaction used to discard.
		visible := withEvidenceLine(result, nil)
		var facts map[string]any
		if err := json.Unmarshal([]byte(visible.Content[0].Text), &facts); err != nil {
			t.Fatal(err)
		}
		acquisition, ok := facts["acquisition"].(map[string]any)
		if !ok || acquisition["reported_missing_nodes"] != float64(17) ||
			!reflect.DeepEqual(acquisition["reasons"], []any{"max_tree_depth", "time_budget", "unknown"}) {
			t.Fatalf("mixed annotations lost or inflated acquisition evidence: %#v", facts)
		}
		if facts["coverage"] != "partial" || facts["tree_complete"] != false {
			t.Fatalf("acquisition gap was presented as complete: %#v", facts)
		}
		recovery := facts["recover"].(map[string]any)
		if (recovery["structural_budget"] != nil) != structuralCut ||
			!strings.Contains(recovery["acquisition"].(string), "do not guarantee complete acquisition") {
			t.Fatalf("recovery confused acquisition and output cuts: %#v", recovery)
		}
		for _, private := range []string{"private traversal detail", "provider_omission", "parentRuntimeId"} {
			if strings.Contains(ResultText(visible), private) {
				t.Fatalf("private provider diagnostics leaked: %s", ResultText(visible))
			}
		}
	}
}

func TestAcquisitionEvidenceFollowsReturnedObservationScope(t *testing.T) {
	for _, test := range []struct {
		name, mode, finalKind   string
		findPage, isError, want bool
	}{
		{name: "observation", mode: "observe", want: true},
		{name: "final tree", mode: "act", finalKind: "tree", want: true},
		{name: "no final observation", mode: "act"},
		{name: "failed target resolution", mode: "act", isError: true, want: true},
		{name: "find page", mode: "find", findPage: true, want: true},
		{name: "legacy targeted match", mode: "find"},
	} {
		t.Run(test.name, func(t *testing.T) {
			snapshot := editorFormSnapshot()
			snapshot.Annotations = []axProviderAnnotation{{Kind: "provider_omission", Count: 4, Reason: "time_budget"}}
			result := TextResult("ax: []", test.isError)
			addTreeProjectionMetadata(&result, snapshot)
			result.StructuredContent["ax_mode"] = test.mode
			result.StructuredContent["final_observation_kind"] = test.finalKind
			result.StructuredContent["final_observation_returned"] = test.finalKind != ""
			if test.isError {
				result.StructuredContent["failure_phase"] = "resolve"
			}
			result.StructuredContent["find_match_count"] = 1
			result.StructuredContent["find_delivery_complete"] = test.findPage
			result = PrepareAXEvidence(EnforceAXResponseBudget(result))
			if (result.StructuredContent["acquisition"] != nil) != test.want ||
				(result.StructuredContent["coverage"] == "partial") != test.want {
				t.Fatalf("unrelated gap or hidden returned-tree gap: %#v", result.StructuredContent)
			}
		})
	}
}

func TestProjectionAndExpandableOmissionsDoNotClaimAcquisitionFailure(t *testing.T) {
	snapshot := editorFormSnapshot()
	snapshot.Annotations = []axProviderAnnotation{
		{Kind: "provider_omission", Count: 9, Reason: "filtered,pressure"},
		{Kind: "hidden_children", Count: 7, Reason: "collapsed_menu"},
		{Kind: axCellSummaryAnnotation, Count: 100, Reason: "empty_cells"},
	}
	result := TextResult("ax: []", false)
	addTreeProjectionMetadata(&result, snapshot)
	result.StructuredContent["ax_mode"] = "observe"
	result.StructuredContent["structural_budget_applied"] = true
	result.StructuredContent["structural_budget_omitted_nodes"] = 20
	result = PrepareAXEvidence(EnforceAXResponseBudget(result))
	recovery, _ := result.StructuredContent["recover"].(map[string]any)
	if result.StructuredContent["acquisition"] != nil || result.StructuredContent["coverage"] != nil ||
		recovery["acquisition"] != nil || recovery["structural_budget"] != "find|root" {
		t.Fatalf("output-only omission became an acquisition failure: %#v", result.StructuredContent)
	}
}
