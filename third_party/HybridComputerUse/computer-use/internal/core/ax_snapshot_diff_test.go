package core

import (
	"fmt"
	"strings"
	"testing"
)

func TestSemanticDiffDoesNotCascadeAfterListInsertion(t *testing.T) {
	node := func(id, ref, fingerprint, name string) *AXNode {
		return &AXNode{
			ID: id, Ref: ref, Role: "listitem", Name: name,
			SemanticFingerprint: fingerprint,
		}
	}
	before := &AXSnapshot{Roots: []*AXNode{
		node("a", "r1", "f1:a", "A"),
		node("b", "r2", "f1:b", "B"),
	}}
	after := &AXSnapshot{Roots: []*AXNode{
		node("x", "r3", "f1:x", "X"),
		node("a2", "r1", "f1:a", "A"),
		node("b2", "r2", "f1:b", "B"),
	}}
	changes := diffAXSnapshots(before, after)
	if len(changes) != 1 || changes[0].Kind != AXDiffAdded ||
		changes[0].Key != "r3" {
		t.Fatalf("list insertion cascaded across stable items: %#v", changes)
	}
	if err := assertStableDiffIdentity(before, after); err != nil {
		t.Fatal(err)
	}
}

func TestSemanticDiffUsesSequenceAlignmentForRepeatedIdentitylessItems(t *testing.T) {
	item := func(id string) *AXNode {
		return &AXNode{
			ID: id, Role: "listitem", Name: "Repeated",
			SemanticFingerprint: "f1:repeated",
		}
	}
	before := &AXSnapshot{Roots: []*AXNode{
		item("old-1"),
		item("old-2"),
		item("old-3"),
	}}
	after := &AXSnapshot{Roots: []*AXNode{
		item("new-item"),
		item("old-1-after"),
		item("old-2-after"),
		item("old-3-after"),
	}}
	changes := diffAXSnapshots(before, after)
	if len(changes) != 1 || changes[0].Kind != AXDiffAdded {
		t.Fatalf("repeated cohort insertion cascaded: %#v", changes)
	}
}

func TestSemanticDiffIgnoresWrapperMovesAndKeepsPropertyChange(t *testing.T) {
	parent := func(id, ref, fingerprint string, child *AXNode) *AXNode {
		node := &AXNode{
			ID: id, Ref: ref, Role: "group",
			SemanticFingerprint: fingerprint,
			Children:            []*AXNode{child},
		}
		child.Parent = node
		return node
	}
	beforeChild := &AXNode{
		ID: "item-before", Ref: "r3", Role: "button", Name: "Save",
		SemanticFingerprint: "f1:save",
	}
	afterChild := &AXNode{
		ID: "item-after", Ref: "r3", Role: "button", Name: "Save",
		SemanticFingerprint: "f1:save",
	}
	before := &AXSnapshot{Roots: []*AXNode{
		parent("left", "r1", "f1:left", beforeChild),
		{ID: "right", Ref: "r2", Role: "group", SemanticFingerprint: "f1:right"},
	}}
	after := &AXSnapshot{Roots: []*AXNode{
		{ID: "left2", Ref: "r1", Role: "group", SemanticFingerprint: "f1:left"},
		parent("right2", "r2", "f1:right", afterChild),
	}}
	changes := diffAXSnapshots(before, after)
	if len(changes) != 0 {
		t.Fatalf("anonymous wrapper churn leaked into semantic diff: %#v", changes)
	}

	propertyAfter := *afterChild
	propertyAfter.Properties.Description = "Document saved"
	propertyAfter.Parent = before.Roots[0]
	propertySnapshot := &AXSnapshot{Roots: []*AXNode{
		parent("left3", "r1", "f1:left", &propertyAfter),
		before.Roots[1],
	}}
	changes = diffAXSnapshots(before, propertySnapshot)
	foundProperty := false
	for _, change := range changes {
		if change.Key == "r3" && change.Kind == AXDiffPropertyChanged {
			foundProperty = true
		}
	}
	if !foundProperty {
		t.Fatalf("property change was not classified: %#v", changes)
	}
}

func assertStableDiffIdentity(before, after *AXSnapshot) error {
	changes := diffAXSnapshots(before, after)
	for _, change := range changes {
		if strings.Contains(change.Key, "/0:") ||
			strings.Contains(change.Key, "/1:") {
			return fmt.Errorf("positional diff identity leaked into key %q", change.Key)
		}
	}
	return nil
}

func testIncrementalAXSnapshot(
	before,
	after *AppSnapshot,
) ([]string, bool) {
	const windowRef = "w1"
	before.publicWindowRef = windowRef
	after.publicWindowRef = windowRef
	if before.AXSnapshot != nil {
		before.AXSnapshot.WindowRef = windowRef
	}
	if after.AXSnapshot != nil {
		after.AXSnapshot.WindowRef = windowRef
	}
	return incrementalAXSnapshot(before, after)
}

func TestStructuredAXDiffUsesYAMLAndStableRefs(t *testing.T) {
	beforeNode := &AXNode{
		ID: "0", Role: "checkbox", Name: "Alerts", Ref: "r4",
		SemanticFingerprint: "f1:alerts",
		States:              AXStates{Checked: CheckedTrue},
	}
	afterNode := &AXNode{
		ID: "0", Role: "checkbox", Name: "Alerts", Ref: "r4",
		SemanticFingerprint: "f1:alerts",
		States:              AXStates{Checked: CheckedFalse},
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
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{afterNode},
			Roots:          []*AXNode{afterNode},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("one-node state change should produce a structured diff")
	}
	text := strings.Join(diff, "\n")
	if !strings.Contains(text, "ax_diff:") ||
		!strings.Contains(text, `<changed> checkbox "Alerts"`) ||
		!strings.Contains(text, `[ref=r4]`) ||
		strings.Contains(text, "\t") {
		t.Fatalf("unexpected structured diff:\n%s", text)
	}
	for _, forbidden := range []string{"/before:", "/after:", "state_changed", "unchanged:"} {
		if strings.Contains(text, forbidden) {
			t.Fatalf("legacy flat diff field %q leaked:\n%s", forbidden, text)
		}
	}
}

func TestChangedChunkDoesNotExposeProviderIdentity(t *testing.T) {
	beforeNode := &AXNode{
		ID: "0", Role: "button", Name: "Save",
		ProviderIdentity:    ":1.42\x00/org/a11y/private/save\x0099123",
		SemanticFingerprint: "f1:save",
		States:              AXStates{Active: TriFalse},
	}
	afterNode := *beforeNode
	afterNode.States.Active = TriTrue
	before := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{beforeNode},
			Roots:          []*AXNode{beforeNode},
		},
	}
	after := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{&afterNode},
			Roots:          []*AXNode{&afterNode},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("provider-backed state change should produce a diff")
	}
	text := strings.Join(diff, "\n")
	for _, forbidden := range []string{":1.42", "/org/a11y/", "provider:"} {
		if strings.Contains(text, forbidden) {
			t.Fatalf("private provider identity %q leaked:\n%s", forbidden, text)
		}
	}
}

func TestChangedChunkGroupsPageReplacementAndReportsAbsentRefs(t *testing.T) {
	oldDocument := &AXNode{
		ID: "old", Role: "document", Name: "General Settings",
		Ref: "r100", SemanticFingerprint: "f1:general",
	}
	oldButton := &AXNode{
		ID: "old/button", Role: "button", Name: "Apply",
		Ref: "r101", SemanticFingerprint: "f1:apply",
		Parent: oldDocument,
	}
	oldDocument.Children = []*AXNode{oldButton}
	newDocument := &AXNode{
		ID: "new", Role: "document", Name: "Privacy Settings",
		SemanticFingerprint: "f1:privacy",
	}
	newButton := &AXNode{
		ID: "new/button", Role: "checkbox", Name: "Do Not Track",
		Ref: "r115", SemanticFingerprint: "f1:dnt",
		Parent: newDocument,
	}
	newDocument.Children = []*AXNode{newButton}
	before := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{oldDocument},
			Roots:          []*AXNode{oldDocument},
		},
	}
	after := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{newDocument},
			Roots:          []*AXNode{newDocument},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("page replacement should produce a changed chunk")
	}
	text := strings.Join(diff, "\n")
	if strings.Count(text, "<changed>") != 1 ||
		!strings.Contains(text, `document "Privacy Settings"`) ||
		!strings.Contains(text, `checkbox "Do Not Track" [ref=r115]`) ||
		!strings.Contains(text, "absent_refs: [r100, r101]") ||
		!strings.Contains(text, "absent_ref_status: absent_from_current_snapshot") {
		t.Fatalf("page replacement was not grouped:\n%s", text)
	}
}

func TestIncompleteAcquisitionNeverClaimsRemovalOrAbsentRef(t *testing.T) {
	beforeDocument := &AXNode{
		ID: "doc", Role: "document", Name: "Settings",
		Ref: "r1", SemanticFingerprint: "f1:settings",
	}
	beforeButton := &AXNode{
		ID: "delete", Role: "button", Name: "Delete",
		Ref: "r2", SemanticFingerprint: "f1:delete",
		Parent: beforeDocument,
	}
	beforeDocument.Children = []*AXNode{beforeButton}
	afterDocument := *beforeDocument
	afterDocument.Children = nil
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
			` - document "Settings" [ref=r1]:`,
			`  - text "` + strings.Repeat("current partial content ", 20) + `"`,
		},
		Annotations: []axProviderAnnotation{{
			Kind:   "provider_omission",
			Count:  1,
			Reason: "time_budget",
		}},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{&afterDocument},
			Roots:          []*AXNode{&afterDocument},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("incomplete acquisition should return bounded positive evidence")
	}
	text := strings.Join(diff, "\n")
	for _, forbidden := range []string{
		"<removed>",
		"absent_refs:",
		"effect: no_ax_change",
	} {
		if strings.Contains(text, forbidden) {
			t.Fatalf("incomplete acquisition fabricated %q:\n%s", forbidden, text)
		}
	}
	if !strings.Contains(text, "observation_incomplete: true") ||
		!strings.Contains(text, "effect: no_confirmed_ax_change") {
		t.Fatalf("incomplete acquisition was not labelled honestly:\n%s", text)
	}
}

func TestIncompleteBaselineCannotCreatePositiveDiff(t *testing.T) {
	beforeDocument := &AXNode{
		ID: "doc", Role: "document", Name: "Settings",
		Ref: "r1", SemanticFingerprint: "f1:settings",
	}
	afterDocument := *beforeDocument
	afterButton := &AXNode{
		ID: "save", Role: "button", Name: "Save",
		Ref: "r2", SemanticFingerprint: "f1:save",
		Parent: &afterDocument,
	}
	afterDocument.Children = []*AXNode{afterButton}
	before := &AppSnapshot{
		WindowHandle: "window-1",
		Annotations: []axProviderAnnotation{{
			Kind: "provider_omission", Count: 1, Reason: "provider_probe_error",
		}},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{beforeDocument},
			Roots:          []*AXNode{beforeDocument},
		},
	}
	after := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{&afterDocument},
			Roots:          []*AXNode{&afterDocument},
		},
	}
	if diff, ok := testIncrementalAXSnapshot(before, after); ok {
		t.Fatalf("incomplete baseline fabricated a positive diff: %#v", diff)
	}
}

func TestRemovedChildUsesCurrentParentNameAfterRename(t *testing.T) {
	beforeDocument := &AXNode{
		ID: "doc", Role: "document", Name: "Old title",
		Ref: "r1", SemanticFingerprint: "f1:document",
	}
	beforeText := &AXNode{
		ID: "warning", Role: "text", Name: "Old warning",
		SemanticFingerprint: "f1:warning",
		Parent:              beforeDocument,
	}
	beforeDocument.Children = []*AXNode{beforeText}
	afterDocument := *beforeDocument
	afterDocument.Name = "New title"
	afterDocument.Children = nil
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
			` - document "New title" [ref=r1]:`,
			`  - text "` + strings.Repeat("current content ", 20) + `"`,
		},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{&afterDocument},
			Roots:          []*AXNode{&afterDocument},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("parent rename plus child removal should produce a changed chunk")
	}
	text := strings.Join(diff, "\n")
	if strings.Contains(text, `document "Old title"`) {
		t.Fatalf("removed child was attached to the obsolete parent name:\n%s", text)
	}
	if !strings.Contains(text, `document "New title"`) ||
		!strings.Contains(text, `<removed> text "Old warning"`) {
		t.Fatalf("removed child lost current parent context:\n%s", text)
	}
}
