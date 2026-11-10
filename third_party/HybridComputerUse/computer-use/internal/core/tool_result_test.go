package core

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestStructuredEvidenceScopeDoesNotClaimTaskSuccess(t *testing.T) {
	result := TextResult("ok", false).withEvidenceScope("delivery_or_local_ui")
	if result.StructuredContent["evidence_scope"] != "delivery_or_local_ui" {
		t.Fatalf("missing evidence scope: %#v", result.StructuredContent)
	}
	if verified, ok := result.StructuredContent["task_effect_verified"].(bool); !ok || verified {
		t.Fatalf("mutating tool result must not claim task effect: %#v", result.StructuredContent)
	}
	if _, exists := result.StructuredContent["tool_execution_succeeded"]; exists {
		t.Fatalf("generic success bits invite task-effect overclaim: %#v", result.StructuredContent)
	}
	for _, ambiguous := range []string{"operation_completed", "tool_call_completed"} {
		if _, exists := result.StructuredContent[ambiguous]; exists {
			t.Fatalf("ambiguous completion field %q must not be returned: %#v",
				ambiguous, result.StructuredContent)
		}
	}
	encoded, err := json.Marshal(result)
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(encoded), `"structuredContent"`) {
		t.Fatalf("structured evidence must be serialized: %s", encoded)
	}

	failed := TextResult("missing app", true).withEvidenceScope("delivery_or_local_ui")
	if failed.StructuredContent["evidence_scope"] != "error_only" {
		t.Fatalf("precondition errors must not claim observation or delivery: %#v", failed.StructuredContent)
	}
	if _, exists := failed.StructuredContent["tool_execution_succeeded"]; exists {
		t.Fatalf("isError already carries failure; no generic success bit should remain")
	}
	for _, ambiguous := range []string{"operation_completed", "tool_call_completed"} {
		if _, exists := failed.StructuredContent[ambiguous]; exists {
			t.Fatalf("failed results must not revive ambiguous completion field %q: %#v",
				ambiguous, failed.StructuredContent)
		}
	}

	stale := TextResult("stale", true)
	stale.StructuredContent = map[string]any{"observation_required": true}
	stale = stale.withEvidenceScope("delivery_or_local_ui")
	if stale.StructuredContent["evidence_scope"] != "error_only" {
		t.Fatalf("a failed action must not claim fresh UI evidence: %#v",
			stale.StructuredContent)
	}
}

func TestTextEvidencePreservesTheOriginalToolContent(t *testing.T) {
	result := ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: "full accessibility tree"}},
		StructuredContent: map[string]any{
			"task_effect_verified": false,
		},
	}
	visible := withEvidenceLine(result, nil)
	if len(visible.Content) != 2 ||
		!strings.HasPrefix(visible.Content[0].Text, "{") ||
		visible.Content[1].Text != "full accessibility tree" {
		t.Fatalf("text evidence hid or rewrote the original tool content: %#v", visible.Content)
	}
}

func TestActionOutcomeSeparatesAttemptFromPredicateAndTaskEffect(t *testing.T) {
	withoutExpect := withActionOutcome(
		TextResult("ok", false),
	).withEvidenceScope("delivery_or_local_ui")
	if withoutExpect.StructuredContent["action_attempted"] != true ||
		withoutExpect.StructuredContent["predicate_checked"] != false {
		t.Fatalf("an unverified action must report attempt separately from predicate state: %#v",
			withoutExpect.StructuredContent)
	}
	if _, exists := withoutExpect.StructuredContent["predicate_verified"]; exists {
		t.Fatalf("no expect means no predicate verdict, not predicate_verified=false: %#v",
			withoutExpect.StructuredContent)
	}
}

// TestOutputSpillsToFileOnlyWhenAskedAndOnlyWhenBig checks that large output
// can be written to a file and replaced by its path. Rather than compressing the content, the agent
// reads the file when it needs detail: a LibreOffice or VS Code tree runs to
// tens of thousands of characters while the agent usually needs one or two
// elements.
func TestOutputSpillsToFileOnlyWhenAskedAndOnlyWhenBig(t *testing.T) {
	dir := t.TempDir()
	t.Setenv("COMPUTER_USE_OUTPUT_DIR", dir)

	big := strings.Repeat("a very long tree line\n", 500)

	// The default stdout mode writes nothing to disk.
	t.Setenv("COMPUTER_USE_OUTPUT_MODE", "")
	if got := spillToFile("snapshot", big); got != "" {
		t.Fatalf("default mode must not spill to disk: %s", got[:60])
	}

	// In file mode, output below the threshold stays inline: spilling turns one
	// call into two, a net loss for small trees.
	t.Setenv("COMPUTER_USE_OUTPUT_MODE", "file")
	if got := spillToFile("snapshot", "short tree"); got != "" {
		t.Fatalf("small output must not spill to disk: %s", got)
	}

	replacement := spillToFile("snapshot", big)
	if replacement == "" {
		t.Fatal("large output should spill to disk")
	}
	for _, fragment := range []string{dir, "characters", "lines", "Read that file"} {
		if !strings.Contains(replacement, fragment) {
			t.Fatalf("replacement must say where the output went, how large it is and how to read it, missing %q: %s", fragment, replacement)
		}
	}
	// The replacement must be far smaller than the original, or the mechanism
	// is pointless.
	if len(replacement) >= len(big)/4 {
		t.Fatalf("replacement saves too little: %d vs %d", len(replacement), len(big))
	}

	// The file must hold the original byte for byte, since the agent reads it
	// to get the full output.
	entries, err := os.ReadDir(dir)
	if err != nil || len(entries) != 1 {
		t.Fatalf("exactly one file should be written: %v %v", entries, err)
	}
	written, err := os.ReadFile(filepath.Join(dir, entries[0].Name()))
	if err != nil || string(written) != big {
		t.Fatalf("spilled file differs from the original output")
	}
}

// TestSpillFailureFallsBackToInlineText checks that a failed spill never fails
// the tool; the text is returned inline instead.
func TestSpillFailureFallsBackToInlineText(t *testing.T) {
	t.Setenv("COMPUTER_USE_OUTPUT_MODE", "file")
	t.Setenv("COMPUTER_USE_OUTPUT_DIR", "/proc/definitely-not-writable")
	if got := spillToFile("snapshot", strings.Repeat("x", 9000)); got != "" {
		t.Fatalf("a failed spill should fall back to inline text, not return a path that does not exist: %s", got[:60])
	}
}

// A GUI success starts with the same evidence line as AX: ok and
// input_emitted first, then the screen and the action facts.
func TestGUISuccessStartsWithTheEvidenceLine(t *testing.T) {
	service := NewService()
	emitted := true
	effectUnknown := true
	quiescent := true
	dispatched := 1
	result := service.guiResponse(ProviderDesktopResult{
		OK: true,
		Snapshot: &AppSnapshot{
			ScreenshotPNGBase64: testPNGBase64(t, 1920, 1080),
			CoordinateSpace:     "screen_image_px",
			Width:               1920,
			Height:              1080,
		},
		GUIResult: ProviderGUIResult{
			Dispatched:         &dispatched,
			InputEventsEmitted: &emitted,
			EffectUnknown:      &effectUnknown,
			Quiescent:          &quiescent,
		},
	})
	visible := FinalizeForModel(contract.PublicToolGUI, result, false)
	line := visible.Content[0].Text
	if !strings.HasPrefix(line, `{"ok":true,"input_emitted":true,"screen":{`) {
		t.Fatalf("GUI success evidence line: %q", line)
	}
	if last := visible.Content[len(visible.Content)-1]; last.Type != "image" {
		t.Fatalf("GUI success lost its screenshot: %#v", visible.Content)
	}
}

func TestLegacyTextEvidenceIncludesFinalStructuralBudgetMetadata(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "800")
	result := ToolCallResult{
		Content: []ContentItem{
			{Type: "text", Text: "ax:\n - \"window \\\"Editor\\\"\""},
		},
		StructuredContent: map[string]any{
			"tree_view":                         "working",
			"structural_budget_applied":         true,
			"structural_budget_omitted_nodes":   17,
			"structural_budget_omitted_targets": 4,
		},
	}
	result = EnforceAXResponseBudget(result)
	result = withEvidenceLine(result, nil)
	if len(result.Content) == 0 ||
		!strings.HasPrefix(result.Content[0].Text, "{") {
		t.Fatalf("text evidence missing: %#v", result.Content)
	}
	line := result.Content[0].Text
	var evidence map[string]any
	if err := json.Unmarshal([]byte(line), &evidence); err != nil {
		t.Fatal(err)
	}
	if evidence["truncation_reason"] != "structural_budget" ||
		evidence["tree_complete_for_model"] != false ||
		evidence["structural_budget_omitted_nodes"] != float64(17) ||
		evidence["structural_budget_omitted_targets"] != float64(4) {
		t.Fatalf("legacy evidence serialized before final budget: %#v", evidence)
	}
}

func TestPrepareAXEvidenceCompactsSuccessWithoutReturningTelemetry(t *testing.T) {
	result := ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: "ax:\n - window \"Editor\""}},
		StructuredContent: map[string]any{
			"ax_mode":                 "observe",
			"canonical_element_count": 157,
			"tree_complete_for_model": true,
			"truncation_reason":       "none",
			"task_effect_verified":    false,
			"distillation": []any{
				map[string]any{"name": "normalize_whitespace"},
			},
			"shadow_ast": map[string]any{
				"projected_nodes": 128,
				"target_recall":   1,
			},
		},
	}
	result = PrepareAXEvidence(result)
	if len(result.StructuredContent) != 2 {
		t.Fatalf("routine success should return only ok and input_emitted: %#v",
			result.StructuredContent)
	}
	for _, noisy := range []string{
		"distillation",
		"shadow_ast",
		"task_effect_verified",
		"truncation_reason",
		"tree_complete_for_model",
	} {
		if _, present := result.StructuredContent[noisy]; present {
			t.Fatalf("compact success leaked %s: %#v", noisy,
				result.StructuredContent)
		}
	}
	visible := WithAXTextEvidence(result)
	if len(visible.Content) != 2 ||
		visible.Content[0].Text != `{"ok":true,"input_emitted":false}` {
		t.Fatalf("routine success must carry only the short evidence line: %#v",
			visible.Content)
	}
}

func TestModelVisibleEvidenceKeepsInternalHashesPrivate(t *testing.T) {
	internal := map[string]any{
		"snapshot_id":                             "abc123",
		"immediate_snapshot_id":                   "before123",
		"final_snapshot_id":                       "after123",
		"observed_snapshot_id":                    "observed123",
		"window_identity_stabilization_attempted": true,
		"window_identity_stabilization_trigger":   "semantic_window_change",
		"content_revision":                        "c1:deadbeef",
		"content_revision_before":                 "c1:before",
		"content_revision_after":                  "c1:after",
		"failure_code":                            "stale_element_ref",
		"argument_error_code":                     "invalid_argument",
		"argument_error_path":                     "/actions/0",
		contract.AXPublicErrorCodeKey:             "invalid_argument",
		contract.AXPublicErrorPathKey:             "actions[0]",
		contract.AXPublicErrorHintKey:             "{}",
		contract.AXPublicErrorDetailKey:           "message",
	}
	visible := modelVisibleEvidence(internal)
	if visible["failure_code"] != "stale_element_ref" {
		t.Fatalf("public failure fact was lost: %#v", visible)
	}
	for key := range modelPrivateEvidenceKeys {
		if _, present := visible[key]; present {
			t.Fatalf("private identity %s leaked: %#v", key, visible)
		}
		if _, retained := internal[key]; !retained {
			t.Fatalf("internal identity %s was deleted", key)
		}
	}
}

func TestAssertAXCanonicalFallbackDoesNotBypassStability(t *testing.T) {
	service := NewService()
	target := &semanticTarget{
		Selector:           `checkbox "Automatic quotes"`,
		LocatorKind:        AXLocatorProviderBound,
		WindowHandle:       "private-window",
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/quote",
		ProcessGeneration:  "99123",
		Role:               "checkbox",
		Name:               "Automatic quotes",
		HasName:            true,
	}
	record := &ElementRecord{
		Index:              7,
		ControlType:        "check box",
		Name:               "Automatic quotes",
		States:             "[active]",
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/quote",
		ProcessGeneration:  "99123",
	}
	fallbackCalls := 0
	result := service.verifyResolvedAXRefWithProbes(
		"editor",
		"r17",
		target,
		verifyGoal{state: "!checked"},
		10,
		1,
		func(ProviderActionRequest) (*ProviderAccessibilityResult, error) {
			return &ProviderAccessibilityResult{
				OK:    false,
				Error: "AX resolver read was incomplete",
				Failure: &ProviderFailure{
					Code:          "observation_incomplete",
					FactsComplete: false,
				},
			}, nil
		},
		func() (*axAssertionSnapshotProof, ToolCallResult) {
			fallbackCalls++
			if fallbackCalls == 1 {
				return &axAssertionSnapshotProof{
					snapshot: &AppSnapshot{WindowHandle: "private-window"},
					record:   record,
				}, ToolCallResult{}
			}
			return nil, TextResult("fallback unavailable", true).
				withEvidenceScope("error_only")
		},
	)
	if !result.IsError ||
		result.StructuredContent["assertion_status"] == "pass" ||
		result.axProofSnapshot != nil {
		t.Fatalf("one fallback sample bypassed stable_ms: %#v", result)
	}
}

func TestModelVisibleEvidenceReportsOnlySuccessfulWindowStabilization(t *testing.T) {
	visible := modelVisibleEvidence(map[string]any{
		"window_identity_stabilization_attempted": true,
		"window_identity_stabilization_trigger":   "semantic_window_change",
		"window_identity_stabilized":              true,
	})
	if visible["window_identity_stabilized"] != true {
		t.Fatalf("successful stabilization was hidden: %#v", visible)
	}
	if _, present := visible["window_identity_stabilization_attempted"]; present {
		t.Fatalf("internal stabilization attempt leaked: %#v", visible)
	}
	if _, present := visible["window_identity_stabilization_trigger"]; present {
		t.Fatalf("internal stabilization trigger leaked: %#v", visible)
	}
}
