package core

import (
	"strings"
	"testing"
)

func TestRemovedChildPrefersStrongParentIdentityOverReusedRuntimePath(t *testing.T) {
	beforeDocument := &AXNode{
		ID: "0", Role: "document", Name: "Old title",
		Ref: "r1", SemanticFingerprint: "f1:document",
	}
	beforeText := &AXNode{
		ID: "0/0", Role: "text", Name: "Old warning",
		SemanticFingerprint: "f1:warning",
		Parent:              beforeDocument,
	}
	beforeDocument.Children = []*AXNode{beforeText}

	unrelated := &AXNode{
		ID: "0", Role: "document", Name: "Unrelated document",
		SemanticFingerprint: "f1:unrelated",
	}
	currentDocument := &AXNode{
		ID: "1", Role: "document", Name: "New title",
		Ref: "r1", SemanticFingerprint: "f1:document",
	}
	before := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{beforeDocument},
			Roots:          []*AXNode{beforeDocument},
		},
	}
	after := &AppSnapshot{
		WindowHandle: "window-1",
		TreeLines: []string{
			"ax:",
			` - document "Unrelated document"`,
			` - document "New title" [ref=r1]`,
			`  - text "` + strings.Repeat("current content ", 20) + `"`,
		},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{unrelated, currentDocument},
			Roots:          []*AXNode{unrelated, currentDocument},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("runtime-path reuse should still produce a bounded changed chunk")
	}
	text := strings.Join(diff, "\n")
	if strings.Contains(text, `document "Unrelated document":`+"\n  - <removed>") {
		t.Fatalf("removed child was attached through a reused runtime path:\n%s", text)
	}
	if !strings.Contains(text, `document "New title"`) ||
		!strings.Contains(text, `<removed> text "Old warning"`) {
		t.Fatalf("removed child did not follow the strong parent identity:\n%s", text)
	}
}

func TestRemovedChildDoesNotTrustWeakRuntimePathReuse(t *testing.T) {
	beforeGroup := &AXNode{
		ID: "0", Role: "group", Name: "Old panel",
		SemanticFingerprint: "f1:old-panel",
	}
	beforeText := &AXNode{
		ID: "0/0", Role: "text", Name: "Warning",
		SemanticFingerprint: "f1:warning", Parent: beforeGroup,
	}
	beforeGroup.Children = []*AXNode{beforeText}
	unrelated := &AXNode{
		ID: "0", Role: "group", Name: "Unrelated panel",
		SemanticFingerprint: "f1:unrelated-panel",
	}
	before := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{beforeGroup},
			Roots:          []*AXNode{beforeGroup},
		},
	}
	after := &AppSnapshot{
		WindowHandle: "window-1",
		TreeLines: []string{
			"ax:",
			` - group "Unrelated panel"`,
			`  - text "` + strings.Repeat("current content ", 20) + `"`,
		},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{unrelated},
			Roots:          []*AXNode{unrelated},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("weak runtime-path reuse should produce bounded historical evidence")
	}
	text := strings.Join(diff, "\n")
	if strings.Contains(text, `group "Unrelated panel":`+"\n  - <removed>") {
		t.Fatalf("removed child was attached to an unrelated reused path:\n%s", text)
	}
	if !strings.Contains(text, `<removed> group "Old panel"`) {
		t.Fatalf("unproven parent continuity was not kept in removed history:\n%s", text)
	}
}

func TestChangedChunkUsesCanonicalTruthNotProjectionMembership(t *testing.T) {
	canonical := &AXNode{
		ID: "0", Role: "text", Name: "Stable",
		SemanticFingerprint: "f1:stable",
	}
	before := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{canonical},
			Roots:          []*AXNode{canonical},
		},
	}
	after := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{canonical},
			Roots:          nil,
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("projection-only change should produce compact no-change evidence")
	}
	if text := strings.Join(diff, "\n"); text != "ax_diff:\n effect: no_ax_change" {
		t.Fatalf("projection churn fabricated semantic changes:\n%s", text)
	}
}

func TestChangedChunkFallsBackWhenItCostsMoreThanCurrentTree(t *testing.T) {
	beforeNode := &AXNode{
		ID: "0", Role: "document", Name: "Old",
		SemanticFingerprint: "f1:old",
	}
	afterNode := &AXNode{
		ID: "1", Role: "document", Name: strings.Repeat("New", 100),
		SemanticFingerprint: "f1:new",
	}
	before := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{beforeNode},
			Roots:          []*AXNode{beforeNode},
		},
	}
	after := &AppSnapshot{
		WindowHandle: "window-1",
		TreeLines:    []string{"ax:", ` - document "New"`},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{afterNode},
			Roots:          []*AXNode{afterNode},
		},
	}
	if diff, ok := testIncrementalAXSnapshot(before, after); ok {
		t.Fatalf("oversized diff should fall back to current tree: %#v", diff)
	}
}

func TestTypedSnapshotNeverFallsBackToLegacyLineDiff(t *testing.T) {
	beforeNode := &AXNode{
		ID: "0", Role: "document", Name: "Old",
		SemanticFingerprint: "f1:old",
	}
	afterNode := &AXNode{
		ID: "1", Role: "document", Name: strings.Repeat("New", 100),
		SemanticFingerprint: "f1:new",
	}
	before := &AppSnapshot{
		WindowHandle: "window-1",
		TreeLines:    []string{"old rendered line"},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{beforeNode},
			Roots:          []*AXNode{beforeNode},
		},
	}
	after := &AppSnapshot{
		WindowHandle: "window-1",
		TreeLines:    []string{"new rendered line"},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{afterNode},
			Roots:          []*AXNode{afterNode},
		},
	}
	if diff, ok := incrementalTree(before, after); ok {
		t.Fatalf("typed oversized diff must request the current tree, not legacy text diff: %#v", diff)
	}
}

func TestChangedChunkKeepsSeparatedChangesInTreeOrder(t *testing.T) {
	beforeFirst := &AXNode{
		ID: "first", Role: "checkbox", Name: "First", Ref: "r2",
		SemanticFingerprint: "f1:first",
		States:              AXStates{Checked: CheckedFalse},
	}
	beforeSecond := &AXNode{
		ID: "second", Role: "checkbox", Name: "Second", Ref: "r9",
		SemanticFingerprint: "f1:second",
		States:              AXStates{Checked: CheckedFalse},
	}
	afterFirst := *beforeFirst
	afterFirst.States.Checked = CheckedTrue
	afterSecond := *beforeSecond
	afterSecond.States.Checked = CheckedTrue
	before := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{beforeFirst, beforeSecond},
			Roots:          []*AXNode{beforeFirst, beforeSecond},
		},
	}
	after := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{&afterFirst, &afterSecond},
			Roots:          []*AXNode{&afterFirst, &afterSecond},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("separated state changes should produce changed chunks")
	}
	text := strings.Join(diff, "\n")
	first := strings.Index(text, `checkbox "First"`)
	second := strings.Index(text, `checkbox "Second"`)
	if first < 0 || second < 0 || first >= second {
		t.Fatalf("changed chunks lost tree order:\n%s", text)
	}
}

func TestChangedChunkRemovedOnlySubtreeDoesNotRenderOldTarget(t *testing.T) {
	oldDialog := &AXNode{
		ID: "dialog", Role: "dialog", Name: "Confirm",
		Ref: "r30", SemanticFingerprint: "f1:confirm",
	}
	oldButton := &AXNode{
		ID: "dialog/remove", Role: "button", Name: "Remove",
		Ref: "r31", SemanticFingerprint: "f1:remove",
		Parent: oldDialog,
	}
	oldDialog.Children = []*AXNode{oldButton}
	stable := &AXNode{
		ID: "stable", Role: "document", Name: "Inbox",
		SemanticFingerprint: "f1:inbox",
	}
	before := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{stable, oldDialog},
			Roots:          []*AXNode{stable, oldDialog},
		},
	}
	after := &AppSnapshot{
		WindowHandle: "window-1",
		TreeLines: []string{
			"ax:",
			` - document "Inbox":`,
			`  - text "` + strings.Repeat("Current inbox context ", 20) + `"`,
		},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{stable},
			Roots:          []*AXNode{stable},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("removed-only subtree should produce lifecycle evidence")
	}
	text := strings.Join(diff, "\n")
	if strings.Contains(text, `<changed> dialog "Confirm"`) ||
		strings.Contains(text, `<changed> button "Remove"`) {
		t.Fatalf("removed target was rendered as currently actionable:\n%s", text)
	}
	if !strings.Contains(text, "absent_refs: [r30, r31]") {
		t.Fatalf("removed refs were not reported:\n%s", text)
	}
}

func TestChangedChunkMovedRefIsNotReportedAbsent(t *testing.T) {
	beforeLeft := &AXNode{
		ID: "left", Role: "group", Name: "Left",
		SemanticFingerprint: "f1:left",
	}
	beforeRight := &AXNode{
		ID: "right", Role: "group", Name: "Right",
		SemanticFingerprint: "f1:right",
	}
	beforeButton := &AXNode{
		ID: "save-old", Role: "button", Name: "Save", Ref: "r5",
		SemanticFingerprint: "f1:save",
		Parent:              beforeLeft,
	}
	beforeLeft.Children = []*AXNode{beforeButton}
	afterLeft := &AXNode{
		ID: "left", Role: "group", Name: "Left",
		SemanticFingerprint: "f1:left",
	}
	afterRight := &AXNode{
		ID: "right", Role: "group", Name: "Right",
		SemanticFingerprint: "f1:right",
	}
	afterButton := &AXNode{
		ID: "save-new", Role: "button", Name: "Save", Ref: "r5",
		SemanticFingerprint: "f1:save",
		Parent:              afterRight,
	}
	afterRight.Children = []*AXNode{afterButton}
	before := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{beforeLeft, beforeRight},
			Roots:          []*AXNode{beforeLeft, beforeRight},
		},
	}
	after := &AppSnapshot{
		WindowHandle: "window-1",
		TreeLines: []string{
			"ax:",
			` - group "Left"`,
			` - group "Right":`,
			`  - button "Save" [ref=r5]`,
			`  - text "` + strings.Repeat("context ", 30) + `"`,
		},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{afterLeft, afterRight},
			Roots:          []*AXNode{afterLeft, afterRight},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("moved current ref should produce a current changed chunk")
	}
	text := strings.Join(diff, "\n")
	if !strings.Contains(text, `button "Save" [ref=r5]`) {
		t.Fatalf("moved current ref was not rendered:\n%s", text)
	}
	if strings.Contains(text, "absent_refs: [r5]") {
		t.Fatalf("current ref was incorrectly reported absent:\n%s", text)
	}
}

func TestChangedChunkReportsRemovedVisibleTextWithParentContext(t *testing.T) {
	beforeDocument := &AXNode{
		ID: "doc", Role: "document", Name: "Checkout",
		SemanticFingerprint: "f1:checkout",
	}
	removedText := &AXNode{
		ID: "failure", Role: "text", Name: "Payment failed",
		SemanticFingerprint: "f1:payment-failed",
		Parent:              beforeDocument,
	}
	beforeDocument.Children = []*AXNode{removedText}
	afterDocument := &AXNode{
		ID: "doc", Role: "document", Name: "Checkout",
		SemanticFingerprint: "f1:checkout",
	}
	before := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{beforeDocument},
			Roots:          []*AXNode{beforeDocument},
		},
	}
	after := &AppSnapshot{
		WindowHandle: "window-1",
		TreeLines: []string{
			"ax:",
			` - document "Checkout":`,
			`  - text "` + strings.Repeat("current content ", 20) + `"`,
		},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{afterDocument},
			Roots:          []*AXNode{afterDocument},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("removed visible text should produce a semantic removal chunk")
	}
	text := strings.Join(diff, "\n")
	if !strings.Contains(text, `document "Checkout":`) ||
		!strings.Contains(text, `<removed> text "Payment failed"`) {
		t.Fatalf("removed text lost semantic context:\n%s", text)
	}
}

func TestChangedParentAndChildRenderOneGroupedChunk(t *testing.T) {
	beforeGroup := &AXNode{
		ID: "group", Role: "group", Name: "Preferences",
		SemanticFingerprint: "f1:preferences",
		States:              AXStates{Expanded: TriFalse},
	}
	beforeChild := &AXNode{
		ID: "alerts", Role: "checkbox", Name: "Alerts", Ref: "r2",
		SemanticFingerprint: "f1:alerts",
		States:              AXStates{Checked: CheckedFalse},
		Parent:              beforeGroup,
	}
	beforeGroup.Children = []*AXNode{beforeChild}
	afterGroup := *beforeGroup
	afterGroup.States.Expanded = TriTrue
	afterChild := *beforeChild
	afterChild.States.Checked = CheckedTrue
	afterChild.Parent = &afterGroup
	afterGroup.Children = []*AXNode{&afterChild}
	before := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{beforeGroup},
			Roots:          []*AXNode{beforeGroup},
		},
	}
	after := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{&afterGroup},
			Roots:          []*AXNode{&afterGroup},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("parent and child state changes should produce a grouped chunk")
	}
	text := strings.Join(diff, "\n")
	if strings.Count(text, `group "Preferences"`) != 1 ||
		strings.Count(text, `checkbox "Alerts"`) != 1 {
		t.Fatalf("parent/child changes were duplicated:\n%s", text)
	}
	if !strings.Contains(text, ` - <changed> group "Preferences"`) ||
		!strings.Contains(text, `  - <changed> checkbox "Alerts"`) {
		t.Fatalf("parent/child changes lost hierarchy:\n%s", text)
	}
}
