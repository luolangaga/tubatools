package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func deliveredDiffBaselineSnapshot(id string, lines ...string) *AppSnapshot {
	return &AppSnapshot{
		sourceApp:       "editor",
		WindowTitle:     "Doc",
		WindowHandle:    "pid7/win0/frame",
		publicWindowRef: "w1",
		SnapshotID:      id,
		TreeLines:       append([]string{"ax:"}, lines...),
		projection:      snapshotProjection{View: contract.TreeViewWorking, Scope: contract.AXScopeWindow},
	}
}

// An act that returned a diff advances only the delivered-state baseline, not
// the full-tree ledger. The model still holds that state, so an observe of the
// unchanged window must reference it instead of resending the whole tree.
func TestObserveAfterDiffOnUnchangedWindowReferencesDeliveredState(t *testing.T) {
	service := NewService()
	afterAct := deliveredDiffBaselineSnapshot(
		"after-act", ` - window "Doc":`, `  - button "Save" [ref=r2]`,
	)
	service.rememberDeliveredSnapshot(afterAct)
	if len(service.observations.lastFullTree) != 0 {
		t.Fatal("test setup: a diff delivery must not mark a full tree delivered")
	}

	observed := *afterAct
	observed.comparedToPrevious = true
	observed.identicalToPrevious = true
	result := service.getAppStateResult(&observed, nil, false)
	text := ResultText(result)
	if strings.Contains(text, `button "Save"`) ||
		!strings.Contains(text, "Tree unchanged:") ||
		!strings.Contains(text, `{"mode":"observe","observe":"full"}`) {
		t.Fatalf("unchanged window after a diff resent the tree:\n%s", text)
	}
	if result.StructuredContent["tree_reference_only"] != true ||
		result.StructuredContent["full_tree_returned"] != false {
		t.Fatalf("reference lacks delivery facts: %#v", result.StructuredContent)
	}

	forced := service.getAppStateResult(&observed, nil, true)
	if !strings.Contains(ResultText(forced), `button "Save"`) {
		t.Fatalf("forceFull must return the whole tree: %s", ResultText(forced))
	}
}

func TestObserveAfterDiffOnChangedWindowReturnsFullTree(t *testing.T) {
	service := NewService()
	service.rememberDeliveredSnapshot(deliveredDiffBaselineSnapshot(
		"after-act", ` - window "Doc":`, `  - button "Save" [ref=r2]`,
	))
	changed := deliveredDiffBaselineSnapshot(
		"later", ` - window "Doc":`, `  - button "Saved" [ref=r2]`,
	)
	text := ResultText(service.getAppStateResult(changed, nil, false))
	if !strings.Contains(text, `button "Saved"`) ||
		strings.Contains(text, "Tree unchanged:") {
		t.Fatalf("changed window must return the full tree:\n%s", text)
	}
}

func TestObserveWithIncompleteAcquisitionReturnsFullTree(t *testing.T) {
	service := NewService()
	service.rememberDeliveredSnapshot(deliveredDiffBaselineSnapshot(
		"same", ` - window "Doc":`, `  - button "Save" [ref=r2]`,
	))
	incomplete := deliveredDiffBaselineSnapshot(
		"same", ` - window "Doc":`, `  - button "Save" [ref=r2]`,
	)
	incomplete.Annotations = []axProviderAnnotation{{
		Kind: "omitted", Count: 3, Reason: "read_error",
	}}
	text := ResultText(service.getAppStateResult(incomplete, nil, false))
	if !strings.Contains(text, `button "Save"`) ||
		strings.Contains(text, "Tree unchanged:") {
		t.Fatalf("incomplete acquisition must return the full tree:\n%s", text)
	}
}

func TestObserveFullReturnsWholeTreeAfterIdenticalDelivery(t *testing.T) {
	snapshot := windowTransitionSnapshot(
		"w2:0123456789abcdef0123456789abcdef", "Document", true,
	)
	service := NewServiceWithBackend(&recordingPlatformBackend{
		accessibilityResponse: &ProviderAccessibilityResult{OK: true, Snapshot: snapshot},
	})
	texts := map[int]string{}
	for index, observe := range []string{"tree", "tree", "full"} {
		result := service.callTool("AX", map[string]any{
			"app": "Editor", "mode": "observe", "observe": observe,
		})
		if result.IsError {
			t.Fatalf("observe %s failed: %#v", observe, result)
		}
		texts[index] = ResultText(result)
	}
	if !strings.Contains(texts[0], `button "OK"`) {
		t.Fatalf("first observe must return the tree:\n%s", texts[0])
	}
	if !strings.Contains(texts[1], "Tree unchanged:") ||
		strings.Contains(texts[1], `button "OK"`) {
		t.Fatalf("repeated observe tree should reference the delivered tree:\n%s", texts[1])
	}
	if !strings.Contains(texts[2], `button "OK"`) ||
		strings.Contains(texts[2], "Tree unchanged:") {
		t.Fatalf("observe full must return the whole tree:\n%s", texts[2])
	}
}

// An act with observe "tree" on a window whose state the model already holds
// (delivered as a diff) gets the same one-line reference an observe gets.
func TestActTreeOnUnchangedDeliveredWindowReferencesIt(t *testing.T) {
	service := NewService()
	afterAct := deliveredDiffBaselineSnapshot(
		"after-act", ` - window "Doc":`, `  - button "Save" [ref=r2]`,
	)
	service.rememberDeliveredSnapshot(afterAct)
	if !service.deliveredStateMatches(afterAct) {
		t.Fatal("identical delivered state did not match")
	}
	changed := deliveredDiffBaselineSnapshot(
		"changed", ` - window "Doc":`, `  - button "Save As" [ref=r3]`,
	)
	if service.deliveredStateMatches(changed) {
		t.Fatal("a changed window matched the delivered state")
	}
}
