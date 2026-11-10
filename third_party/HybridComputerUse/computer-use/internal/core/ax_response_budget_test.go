package core

import (
	"strings"
	"testing"
)

func TestOversizedResponsesAreCappedInlineNotSpilled(t *testing.T) {
	// MCP clients may replace an oversized result wholesale with a pointer:
	//     <persisted-output> Output too large (118.1KB). Full output saved to: …
	// An agent without file access cannot read it and receives nothing.
	// --output-mode file fails for the same reason; the only remedy is output
	// that fits inline in the first place.
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "")
	small := strings.Repeat("line\n", 100)
	if capInline(small) != small {
		t.Fatal("small output must be left untouched")
	}
	big := strings.Repeat("a fairly long tree line that costs bytes\n", 4000)
	capped := capInline(big)
	if len(capped) >= len(big) {
		t.Fatal("output over the limit must be capped")
	}
	if len(capped) > MaxInlineChars()+400 {
		t.Fatalf("capped output is still too long: %d", len(capped))
	}
	// The notice must say how much was cut. Silent truncation makes the agent
	// believe it saw the whole tree, which is worse than losing the data.
	for _, fragment := range []string{"TRUNCATED", "of", "lines", "`find`"} {
		if !strings.Contains(capped, fragment) {
			t.Fatalf("truncation notice is missing %q", fragment)
		}
	}
	// The threshold is configurable because clients have different limits.
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "500")
	if MaxInlineChars() != 500 {
		t.Fatal("the limit must be overridable by an environment variable")
	}
}

func TestAXFinalTextBudgetCoversAllTextItems(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "240")
	result := ToolCallResult{
		Content: []ContentItem{
			{Type: "text", Text: strings.Repeat("a", 160)},
			{Type: "text", Text: strings.Repeat("b", 160)},
		},
		StructuredContent: map[string]any{"tree_view": "working"},
	}
	result = EnforceAXResponseBudget(result)
	if chars := textContentChars(result); chars > 240 {
		t.Fatalf("final text has %d chars, exceeds advertised 240", chars)
	}
	if !strings.Contains(ResultText(result), "[TRUNCATED:") {
		t.Fatalf("truncation was not reported: %q", ResultText(result))
	}
	if result.StructuredContent["tree_complete_for_model"] != false ||
		result.StructuredContent["truncation_reason"] != "response_budget" {
		t.Fatalf("structured budget metadata is dishonest: %#v", result.StructuredContent)
	}
	if got := result.StructuredContent["response_chars"]; got != textContentChars(result) {
		t.Fatalf("response_chars=%v, actual=%d", got, textContentChars(result))
	}
}

func TestAXFinalTextBudgetReportsStructuralOmissionWithoutTextTruncation(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "800")
	result := ToolCallResult{
		Content: []ContentItem{
			{Type: "text", Text: "ax:\n - \"window \\\"Editor\\\"\""},
		},
		StructuredContent: map[string]any{
			"tree_view":                 "working",
			"structural_budget_applied": true,
		},
	}
	result = EnforceAXResponseBudget(result)
	if strings.Contains(ResultText(result), "[response-budget]") {
		t.Fatalf("routine omission returned operator telemetry: %q",
			ResultText(result))
	}
	if result.StructuredContent["tree_complete_for_model"] != false ||
		result.StructuredContent["truncation_reason"] != "structural_budget" {
		t.Fatalf(
			"structural budget metadata is dishonest: %#v",
			result.StructuredContent,
		)
	}
}

func TestProviderOmissionRecoveryDistinguishesProjectionFromAcquisition(t *testing.T) {
	coverage := providerCoverageSummary(
		[]axProviderAnnotation{
			{Kind: "provider_omission", Count: 3, Reason: "filtered"},
			{Kind: "provider_omission", Count: 2, Reason: "pressure"},
			{Kind: "hidden_children", Count: 7, Reason: "collapsed_menu"},
			{
				Kind: "hidden_children", Count: 11,
				Reason: "provider_children_not_rendered",
			},
			{Kind: "provider_omission", Count: 4, Reason: "max_tree_depth"},
			{Kind: "provider_omission", Count: 5, Reason: "time_budget"},
			{
				Kind: "provider_omission", Count: 6,
				Reason: "filtered,max_tree_depth",
			},
		},
	)
	if coverage.ProjectionHidden != 5 ||
		coverage.ExpandableHidden != 18 ||
		coverage.AcquisitionIncomplete != 15 {
		t.Fatalf(
			"coverage classification: %#v",
			coverage,
		)
	}

	result := PrepareAXEvidence(ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: "ax: []"}},
		StructuredContent: map[string]any{
			"ax_mode":                   "observe",
			"projection_hidden_nodes":   5,
			"expandable_hidden_nodes":   18,
			"coverage_incomplete_nodes": 9,
			"tree_complete_for_model":   false,
		},
	})
	recover, ok := result.StructuredContent["recover"].(map[string]any)
	if !ok || recover["acquisition"] == nil || recover["structural_budget"] != nil {
		t.Fatalf("dishonest omission recovery: %#v", result.StructuredContent)
	}
	if result.StructuredContent["coverage"] != "partial" ||
		result.StructuredContent["tree_complete"] != false {
		t.Fatalf("coverage category was lost: %#v", result.StructuredContent)
	}
}
