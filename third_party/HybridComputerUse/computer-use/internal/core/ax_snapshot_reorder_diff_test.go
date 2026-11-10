package core

import (
	"fmt"
	"strconv"
	"strings"
	"testing"
)

func TestSiblingReorderProducesChangedChunk(t *testing.T) {
	parentBefore := &AXNode{
		ID: "list", Role: "list", Name: "Results",
		Ref: "r1", SemanticFingerprint: "f1:list",
	}
	firstBefore := &AXNode{
		ID: "first", Role: "listitem", Name: "First",
		Ref: "r2", SemanticFingerprint: "f1:first", Parent: parentBefore,
	}
	secondBefore := &AXNode{
		ID: "second", Role: "listitem", Name: "Second",
		Ref: "r3", SemanticFingerprint: "f1:second", Parent: parentBefore,
	}
	parentBefore.Children = []*AXNode{firstBefore, secondBefore}

	parentAfter := *parentBefore
	firstAfter := *firstBefore
	secondAfter := *secondBefore
	firstAfter.Parent = &parentAfter
	secondAfter.Parent = &parentAfter
	parentAfter.Children = []*AXNode{&secondAfter, &firstAfter}

	before := &AppSnapshot{
		WindowHandle: "window-1",
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{parentBefore},
			Roots:          []*AXNode{parentBefore},
		},
	}
	after := &AppSnapshot{
		WindowHandle: "window-1",
		TreeLines: withUnchangedWindowContext([]string{
			"ax:",
			` - list "Results" [ref=r1]:`,
			`  - listitem "Second" [ref=r3]`,
			`  - listitem "First" [ref=r2]`,
		}),
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{&parentAfter},
			Roots:          []*AXNode{&parentAfter},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("sibling reorder should produce a bounded changed chunk")
	}
	text := strings.Join(diff, "\n")
	if strings.Contains(text, "effect: no_ax_change") ||
		!strings.Contains(text, `<changed> list "Results"`) ||
		!strings.Contains(text, "/order_changed: true") ||
		!strings.Contains(text, `listitem "Second" [ref=r3] [position=1]`) ||
		!strings.Contains(text, `listitem "First" [ref=r2] [position=2]`) {
		t.Fatalf("sibling reorder was reported as unchanged:\n%s", text)
	}
}

func TestSiblingReorderPreservesChangedChildProperties(t *testing.T) {
	parentBefore := &AXNode{
		ID: "list", Role: "list", Name: "Results",
		Ref: "r1", SemanticFingerprint: "f1:list",
	}
	textboxBefore := &AXNode{
		ID: "query", Role: "textbox", Name: "Query",
		Ref: "r2", SemanticFingerprint: "f1:query",
		Properties: AXProperties{Value: "old"}, Parent: parentBefore,
	}
	buttonBefore := &AXNode{
		ID: "save", Role: "button", Name: "Save",
		Ref: "r3", SemanticFingerprint: "f1:save", Parent: parentBefore,
	}
	parentBefore.Children = []*AXNode{textboxBefore, buttonBefore}

	parentAfter := *parentBefore
	textboxAfter := *textboxBefore
	textboxAfter.Properties.Value = "new"
	buttonAfter := *buttonBefore
	textboxAfter.Parent = &parentAfter
	buttonAfter.Parent = &parentAfter
	parentAfter.Children = []*AXNode{&buttonAfter, &textboxAfter}

	before := &AppSnapshot{
		WindowHandle:    "window-1",
		publicWindowRef: "w1",
		AXSnapshot: &AXSnapshot{
			WindowRef:      "w1",
			CanonicalRoots: []*AXNode{parentBefore},
			Roots:          []*AXNode{parentBefore},
		},
	}
	after := &AppSnapshot{
		WindowHandle:    "window-1",
		publicWindowRef: "w1",
		TreeLines: withUnchangedWindowContext([]string{
			"ax:",
			` - list "Results" [ref=r1]:`,
			`  - button "Save" [ref=r3]`,
			`  - textbox "Query" [ref=r2]:`,
			`   - /value: new`,
		}),
		AXSnapshot: &AXSnapshot{
			WindowRef:      "w1",
			CanonicalRoots: []*AXNode{&parentAfter},
			Roots:          []*AXNode{&parentAfter},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("reorder plus property change should produce a changed chunk")
	}
	text := strings.Join(diff, "\n")
	if !strings.Contains(text, "/order_changed: true") ||
		!strings.Contains(text, `textbox "Query" [ref=r2] [position=2]`) ||
		!strings.Contains(text, "/value: new") {
		t.Fatalf("reorder swallowed the changed child property:\n%s", text)
	}
}

func TestInsertionDoesNotHideSurvivorReorder(t *testing.T) {
	beforeParent := &AXNode{
		ID: "list", Role: "list", Name: "Results",
		Ref: "r1", SemanticFingerprint: "f1:list",
	}
	afterParent := *beforeParent
	makeItem := func(id, name, ref string, parent *AXNode) *AXNode {
		return &AXNode{
			ID: id, Role: "listitem", Name: name, Ref: ref,
			SemanticFingerprint: "f1:" + id, Parent: parent,
		}
	}
	aBefore := makeItem("a", "A", "r2", beforeParent)
	bBefore := makeItem("b", "B", "r3", beforeParent)
	cBefore := makeItem("c", "C", "r4", beforeParent)
	beforeParent.Children = []*AXNode{aBefore, bBefore, cBefore}
	xAfter := makeItem("x", "X", "r5", &afterParent)
	cAfter := makeItem("c", "C", "r4", &afterParent)
	aAfter := makeItem("a", "A", "r2", &afterParent)
	bAfter := makeItem("b", "B", "r3", &afterParent)
	afterParent.Children = []*AXNode{xAfter, cAfter, aAfter, bAfter}

	before := &AppSnapshot{
		publicWindowRef: "w1",
		AXSnapshot: &AXSnapshot{
			WindowRef: "w1", CanonicalRoots: []*AXNode{beforeParent},
			Roots: []*AXNode{beforeParent},
		},
	}
	after := &AppSnapshot{
		publicWindowRef: "w1",
		TreeLines:       withUnchangedWindowContext([]string{"ax:", ` - list "Results" [ref=r1]`}),
		AXSnapshot: &AXSnapshot{
			WindowRef: "w1", CanonicalRoots: []*AXNode{&afterParent},
			Roots: []*AXNode{&afterParent},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("insertion plus survivor reorder should produce a changed chunk")
	}
	text := strings.Join(diff, "\n")
	if !strings.Contains(text, "/order_changed: true") ||
		!strings.Contains(text, `<changed> listitem "X" [ref=r5] [position=1]`) ||
		!strings.Contains(text, `listitem "C" [ref=r4] [position=2]`) {
		t.Fatalf("insertion hid survivor reorder:\n%s", text)
	}
}

func TestLargeSiblingReorderUsesBoundedSummary(t *testing.T) {
	beforeParent := &AXNode{
		ID: "list", Role: "list", Name: "Results",
		Ref: "r1", SemanticFingerprint: "f1:list",
	}
	afterParent := *beforeParent
	for index := 0; index < 200; index++ {
		id := fmt.Sprintf("item-%03d", index)
		beforeParent.Children = append(beforeParent.Children, &AXNode{
			ID: id, Role: "textbox", Name: id,
			Ref:                 "r" + strconv.Itoa(index+2),
			SemanticFingerprint: "f1:" + id, Parent: beforeParent,
		})
	}
	for index := len(beforeParent.Children) - 1; index >= 0; index-- {
		clone := *beforeParent.Children[index]
		clone.Parent = &afterParent
		afterParent.Children = append(afterParent.Children, &clone)
	}
	before := &AppSnapshot{
		publicWindowRef: "w1",
		AXSnapshot: &AXSnapshot{
			WindowRef: "w1", CanonicalRoots: []*AXNode{beforeParent},
			Roots: []*AXNode{beforeParent},
		},
	}
	after := &AppSnapshot{
		publicWindowRef: "w1",
		TreeLines:       withUnchangedWindowContext([]string{"ax:", ` - list "Results" [ref=r1]`}),
		AXSnapshot: &AXSnapshot{
			WindowRef: "w1", CanonicalRoots: []*AXNode{&afterParent},
			Roots: []*AXNode{&afterParent},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("large reorder should remain a bounded changed chunk")
	}
	text := strings.Join(diff, "\n")
	if len(text) > 6000 ||
		!strings.Contains(text, "/order_changed: moved=") ||
		!strings.Contains(text, "/positions_omitted:") {
		t.Fatalf("large reorder was not compacted:\n%s", text)
	}
}

func TestLargeSiblingReorderWithManyPropertyChangesStaysBounded(t *testing.T) {
	beforeParent := &AXNode{
		ID: "list", Role: "list", Name: "Results",
		Ref: "r1", SemanticFingerprint: "f1:list",
	}
	afterParent := *beforeParent
	for index := 0; index < 200; index++ {
		id := fmt.Sprintf("item-%03d", index)
		beforeParent.Children = append(beforeParent.Children, &AXNode{
			ID: id, Role: "textbox", Name: id,
			Ref:                 "r" + strconv.Itoa(index+2),
			SemanticFingerprint: "f1:" + id,
			Properties:          AXProperties{Value: "old"},
			Parent:              beforeParent,
		})
	}
	for index := len(beforeParent.Children) - 1; index >= 0; index-- {
		clone := *beforeParent.Children[index]
		if index < 80 {
			clone.Properties.Value = "new"
		}
		clone.Parent = &afterParent
		afterParent.Children = append(afterParent.Children, &clone)
	}
	before := &AppSnapshot{
		publicWindowRef: "w1",
		AXSnapshot: &AXSnapshot{
			WindowRef: "w1", CanonicalRoots: []*AXNode{beforeParent},
			Roots: []*AXNode{beforeParent},
		},
	}
	after := &AppSnapshot{
		publicWindowRef: "w1",
		TreeLines:       withUnchangedWindowContext([]string{"ax:", ` - list "Results" [ref=r1]`}),
		AXSnapshot: &AXSnapshot{
			WindowRef: "w1", CanonicalRoots: []*AXNode{&afterParent},
			Roots: []*AXNode{&afterParent},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("large reorder plus property changes should stay incremental")
	}
	text := strings.Join(diff, "\n")
	if len(text) > 6000 ||
		!strings.Contains(text, "/order_changed: moved=") ||
		!strings.Contains(text, "/value: new") ||
		!strings.Contains(text, "/positions_omitted:") ||
		!strings.Contains(text, "/semantic_changes_omitted:") {
		t.Fatalf("large changed reorder was not compacted honestly:\n%s", text)
	}
}

func TestLargeSiblingReorderPrioritizesMiddlePropertyChange(t *testing.T) {
	beforeParent := &AXNode{
		ID: "list", Role: "list", Name: "Results",
		Ref: "r1", SemanticFingerprint: "f1:list",
	}
	afterParent := *beforeParent
	for index := 0; index < 200; index++ {
		id := fmt.Sprintf("item-%03d", index)
		beforeParent.Children = append(beforeParent.Children, &AXNode{
			ID: id, Role: "textbox", Name: id,
			Ref:                 "r" + strconv.Itoa(index+2),
			SemanticFingerprint: "f1:" + id,
			Properties:          AXProperties{Value: "old"},
			Parent:              beforeParent,
		})
	}
	for index := len(beforeParent.Children) - 1; index >= 0; index-- {
		clone := *beforeParent.Children[index]
		if index == 100 {
			clone.Properties.Value = "middle-change"
		}
		clone.Parent = &afterParent
		afterParent.Children = append(afterParent.Children, &clone)
	}
	before := &AppSnapshot{
		publicWindowRef: "w1",
		AXSnapshot: &AXSnapshot{
			WindowRef: "w1", CanonicalRoots: []*AXNode{beforeParent},
			Roots: []*AXNode{beforeParent},
		},
	}
	after := &AppSnapshot{
		publicWindowRef: "w1",
		TreeLines:       withUnchangedWindowContext([]string{"ax:", ` - list "Results" [ref=r1]`}),
		AXSnapshot: &AXSnapshot{
			WindowRef: "w1", CanonicalRoots: []*AXNode{&afterParent},
			Roots: []*AXNode{&afterParent},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("large reorder plus middle property change should stay incremental")
	}
	text := strings.Join(diff, "\n")
	if !strings.Contains(text, `textbox "item-100"`) ||
		!strings.Contains(text, "/value: middle-change") {
		t.Fatalf("order sampling hid the only semantic change:\n%s", text)
	}
}

func TestCanonicalRootReorderProducesChangedChunk(t *testing.T) {
	firstBefore := &AXNode{
		ID: "first", Role: "document", Name: "First",
		Ref: "r1", SemanticFingerprint: "f1:first",
	}
	secondBefore := &AXNode{
		ID: "second", Role: "document", Name: "Second",
		Ref: "r2", SemanticFingerprint: "f1:second",
	}
	firstAfter := *firstBefore
	secondAfter := *secondBefore
	before := &AppSnapshot{
		publicWindowRef: "w1",
		AXSnapshot: &AXSnapshot{
			WindowRef:      "w1",
			CanonicalRoots: []*AXNode{firstBefore, secondBefore},
			Roots:          []*AXNode{firstBefore, secondBefore},
		},
	}
	after := &AppSnapshot{
		publicWindowRef: "w1",
		TreeLines: withUnchangedWindowContext([]string{
			"ax:",
			` - document "Second" [ref=r2]`,
			` - document "First" [ref=r1]`,
		}),
		AXSnapshot: &AXSnapshot{
			WindowRef:      "w1",
			CanonicalRoots: []*AXNode{&secondAfter, &firstAfter},
			Roots:          []*AXNode{&secondAfter, &firstAfter},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("canonical root reorder should produce a changed chunk")
	}
	text := strings.Join(diff, "\n")
	if !strings.Contains(text, "<changed> root") ||
		!strings.Contains(text, "/order_changed: true") ||
		!strings.Contains(text, `document "Second" [ref=r2] [position=1]`) {
		t.Fatalf("root reorder was reported incorrectly:\n%s", text)
	}
}

func TestRootReorderDoesNotDuplicateChangedDescendant(t *testing.T) {
	firstBefore := &AXNode{
		ID: "first", Role: "textbox", Name: "First",
		Ref: "r1", SemanticFingerprint: "f1:first",
		Properties: AXProperties{Value: "old"},
	}
	secondBefore := &AXNode{
		ID: "second", Role: "button", Name: "Second",
		Ref: "r2", SemanticFingerprint: "f1:second",
	}
	firstAfter := *firstBefore
	firstAfter.Properties.Value = "new"
	secondAfter := *secondBefore
	before := &AppSnapshot{
		publicWindowRef: "w1",
		AXSnapshot: &AXSnapshot{
			WindowRef:      "w1",
			CanonicalRoots: []*AXNode{firstBefore, secondBefore},
			Roots:          []*AXNode{firstBefore, secondBefore},
		},
	}
	after := &AppSnapshot{
		publicWindowRef: "w1",
		TreeLines: withUnchangedWindowContext([]string{
			"ax:",
			` - button "Second" [ref=r2]`,
			` - textbox "First" [ref=r1]:`,
			`  - /value: new`,
		}),
		AXSnapshot: &AXSnapshot{
			WindowRef:      "w1",
			CanonicalRoots: []*AXNode{&secondAfter, &firstAfter},
			Roots:          []*AXNode{&secondAfter, &firstAfter},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("root reorder plus property change should produce a changed chunk")
	}
	text := strings.Join(diff, "\n")
	if count := strings.Count(text, `textbox "First" [ref=r1]`); count != 1 {
		t.Fatalf("root order chunk duplicated the changed node %d times:\n%s", count, text)
	}
	if !strings.Contains(text, "/value: new") {
		t.Fatalf("root order chunk lost the changed value:\n%s", text)
	}
}

// withUnchangedWindowContext appends the unchanged rest of a window to a
// fixture tree. A diff replaces the current tree only when it is shorter, and
// real windows carry far more unchanged nodes than these focused fixtures.
func withUnchangedWindowContext(lines []string) []string {
	out := append([]string(nil), lines...)
	for index := 0; index < 300; index++ {
		out = append(out, fmt.Sprintf(` - text "Unchanged context line %03d"`, index))
	}
	return out
}

func TestReorderDiffLargerThanTreeFallsBackToTree(t *testing.T) {
	beforeParent := &AXNode{
		ID: "list", Role: "list", Name: "Results",
		Ref: "r1", SemanticFingerprint: "f1:list",
	}
	first := &AXNode{ID: "a", Role: "listitem", Name: "A", Ref: "r2",
		SemanticFingerprint: "f1:a", Parent: beforeParent}
	second := &AXNode{ID: "b", Role: "listitem", Name: "B", Ref: "r3",
		SemanticFingerprint: "f1:b", Parent: beforeParent}
	beforeParent.Children = []*AXNode{first, second}
	afterParent := *beforeParent
	firstAfter, secondAfter := *first, *second
	firstAfter.Parent, secondAfter.Parent = &afterParent, &afterParent
	afterParent.Children = []*AXNode{&secondAfter, &firstAfter}
	before := &AppSnapshot{AXSnapshot: &AXSnapshot{
		CanonicalRoots: []*AXNode{beforeParent}, Roots: []*AXNode{beforeParent},
	}}
	after := &AppSnapshot{
		TreeLines: []string{
			"ax:",
			` - list "Results" [ref=r1]:`,
			`  - listitem "B" [ref=r3]`,
			`  - listitem "A" [ref=r2]`,
		},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{&afterParent}, Roots: []*AXNode{&afterParent},
		},
	}
	if diff, ok := testIncrementalAXSnapshot(before, after); ok {
		t.Fatalf("a reorder diff longer than the tree must fall back to the tree:\n%s",
			strings.Join(diff, "\n"))
	}
}
