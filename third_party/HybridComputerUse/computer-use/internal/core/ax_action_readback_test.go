package core

import (
	"fmt"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestIncrementalTreeOnlyWhenSafeAndCheaper(t *testing.T) {
	// Requiring an identical line count is too strong a proxy for "no
	// structural change": opening a menu, showing a dialog or adding a list row
	// all change the count, and those are the most common actions.
	//
	// The real safety invariant is finer. The element index is printed in each
	// line, and StableIndexer derives it from the tree path, role and name, so
	//
	//     byte-identical line  =>  same path, role and name  =>  same index
	//
	// Conversely, inserting a node changes the paths of later siblings, which
	// receive new indices and therefore new line text, so they always appear in
	// the diff. A large reorder exceeds the size thresholds and falls back to
	// the full tree, so the mechanism corrects itself when indices shift.
	base := func(lines ...string) *AppSnapshot { return &AppSnapshot{TreeLines: lines} }

	unchanged, ok := incrementalTree(base("a", "b", "c"), base("a", "b", "c"))
	if !ok || len(unchanged) != 1 || !strings.Contains(unchanged[0], "0 changes") {
		t.Fatalf("identical trees must yield only a zero-change summary, got ok=%v diff=%v", ok, unchanged)
	}

	diff, ok := incrementalTree(base("a", "b", "c", "d", "e", "f"),
		base("a", "b", "X", "d", "e", "f"))
	if !ok {
		t.Fatal("a single changed line should use the incremental diff")
	}
	if len(diff) != 3 || diff[1] != "-c" || diff[2] != "+X" {
		t.Fatalf("a replaced line should yield -old +new, got %v", diff)
	}
	if !strings.Contains(diff[0], "Existing rN refs remain bound") {
		t.Fatal("diff header must state that unaffected short refs remain bound")
	}

	// Pure insertion: the most common change after an action, which a
	// line-count criterion would reject.
	diff, ok = incrementalTree(base("a", "b", "c", "d", "e", "f", "g"),
		base("a", "b", "NEW", "c", "d", "e", "f", "g"))
	if !ok {
		t.Fatal("pure insertion should use the incremental diff, not fall back to the full tree")
	}
	if len(diff) != 2 || diff[1] != "+NEW" {
		t.Fatalf("pure insertion should yield only +new, got %v", diff)
	}

	// Pure deletion.
	diff, ok = incrementalTree(base("a", "b", "GONE", "c", "d", "e", "f"),
		base("a", "b", "c", "d", "e", "f"))
	if !ok {
		t.Fatal("pure deletion should use the incremental diff")
	}
	if len(diff) != 2 || diff[1] != "-GONE" {
		t.Fatalf("pure deletion should yield only -old, got %v", diff)
	}

	if _, ok := incrementalTree(base("a", "b", "c"), base("X", "Y", "c")); ok {
		t.Fatal("when most lines change, the diff should fall back to the full tree")
	}
	if _, ok := incrementalTree(nil, base("a")); ok {
		t.Fatal("without a previous snapshot there must be no incremental diff")
	}
}

func TestIncrementalTreeRejectsRootOrSerializerViewChanges(t *testing.T) {
	before := &AppSnapshot{
		WindowHandle: "pid1/win0/frame",
		TreeLines:    []string{"a", "b"},
		projection:   snapshotProjection{View: contract.TreeViewWorking},
	}
	otherRoot := &AppSnapshot{
		WindowHandle: "pid1/win1/frame",
		TreeLines:    []string{"a", "b"},
		projection:   snapshotProjection{View: contract.TreeViewWorking},
	}
	if _, ok := incrementalTree(before, otherRoot); ok {
		t.Fatal("different root windows must never share an incremental address-space claim")
	}

	fullView := &AppSnapshot{
		WindowHandle: "pid1/win0/frame",
		TreeLines:    []string{"a", "b"},
		projection:   snapshotProjection{View: contract.TreeViewFull},
	}
	if _, ok := incrementalTree(before, fullView); ok {
		t.Fatal("working and full serializer views must not be diffed as one tree")
	}
}

// TestIncrementalTreeKeepsLineDiffForShortLines checks that short lines keep
// the plain -/+ diff form.
func TestIncrementalTreeKeepsLineDiffForShortLines(t *testing.T) {
	base := func(lines ...string) *AppSnapshot { return &AppSnapshot{TreeLines: lines} }
	diff, ok := incrementalTree(base("a", "b", "c", "d", "e", "f"),
		base("a", "b", "X", "d", "e", "f"))
	if !ok {
		t.Fatal("replacing one short line should still use the incremental diff")
	}
	if len(diff) != 3 || diff[1] != "-c" || diff[2] != "+X" {
		t.Fatalf("short lines must not be summarized and should stay -old +new, got %v", diff)
	}
}

func TestIncrementalTreeRejectsNearFullDynamicPageDiff(t *testing.T) {
	before := &AppSnapshot{TreeLines: make([]string, 0, 120)}
	after := &AppSnapshot{TreeLines: make([]string, 0, 120)}
	for index := 0; index < 120; index++ {
		before.TreeLines = append(
			before.TreeLines,
			fmt.Sprintf("%d list item \"Old vehicle %03d\"", index, index),
		)
		after.TreeLines = append(
			after.TreeLines,
			fmt.Sprintf("%d list item \"New vehicle %03d\"", index, index),
		)
	}
	if _, ok := incrementalTree(before, after); ok {
		t.Fatal("a near-full dynamic-page diff must use compact action context")
	}
}

func TestActionFallbackReturnsFocusedContextNotWholeTree(t *testing.T) {
	snapshot := editorFormSnapshot()
	typedTreeTextForTest(t, snapshot)
	lines := compactActionObservationLines(snapshot)
	text := strings.Join(lines, "\n")
	if !strings.Contains(text, "focus:") ||
		!strings.Contains(text, `textbox "Name"`) {
		t.Fatalf("compact action observation lacks focused context:\n%s", text)
	}
	if strings.Contains(text, `status "Ready"`) {
		t.Fatalf("compact action observation replayed unrelated window state:\n%s", text)
	}
}

func TestActionFallbackPrefersChangedContentWhenFocusIsNavigation(t *testing.T) {
	snapshot := &AppSnapshot{
		TreeLines: []string{
			"\t0 frame \"Settings\"",
			"\t\t1 tree \"Navigation\"",
			"\t\t\t2 tree item \"Composition\" [focused selected]",
			"\t\t3 document web \"Composition Settings\"",
			"\t\t\t4 checkbox \"Automatically quote\"",
		},
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Settings"},
			{Index: 1, RuntimeID: []int{0, 0}, ControlType: "tree", Name: "Navigation"},
			{Index: 2, RuntimeID: []int{0, 0, 0}, ControlType: "tree item", Name: "Composition", States: "[focused selected]"},
			{Index: 3, RuntimeID: []int{0, 1}, ControlType: "document web", Name: "Composition Settings"},
			{Index: 4, RuntimeID: []int{0, 1, 0}, ControlType: "check box", Name: "Automatically quote"},
		},
		Scope:           contract.AXScopeWindow,
		contentRevision: "c1:changed",
	}
	typedTreeTextForTest(t, snapshot)
	snapshot.contentRoot = &snapshot.Elements[3]

	lines := compactActionObservationLines(snapshot, nil)
	text := strings.Join(lines, "\n")
	if !strings.Contains(text, "content:") ||
		!strings.Contains(text, `checkbox "Automatically quote"`) {
		t.Fatalf("navigation fallback omitted changed content:\n%s", text)
	}
	if strings.Contains(text, `tree item "Composition"`) {
		t.Fatalf("navigation fallback returned only the old focused node:\n%s", text)
	}
	service := NewService()
	result, _, returned, compact, fullTree :=
		service.renderAXDiffOrCompactObservation(snapshot, nil)
	if !returned || !compact || fullTree ||
		result.StructuredContent["final_observation_kind"] != "content" {
		t.Fatalf("content fallback kind is inconsistent: %#v",
			result.StructuredContent)
	}
}

func popupMenuSnapshot(t *testing.T, menuOpen bool) *AppSnapshot {
	t.Helper()
	snapshot := &AppSnapshot{
		App:          AppDescriptor{Name: "Paint", PID: 42},
		WindowTitle:  "Paint",
		WindowHandle: "0x42",
		SnapshotID:   "paint-menu",
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Paint"},
			{Index: 1, RuntimeID: []int{0, 0}, ControlType: "menu bar"},
			{Index: 2, RuntimeID: []int{0, 0, 0}, ControlType: "menu item", Name: "Settings", States: "[has-click-action]"},
			{Index: 3, RuntimeID: []int{0, 1}, ControlType: "panel", Name: "Canvas", States: "[focused]"},
			{Index: 4, RuntimeID: []int{0, 1, 0}, ControlType: "scroll bar", Name: "Horizontal"},
		},
		Scope: contract.AXScopeWindow,
	}
	if menuOpen {
		snapshot.Elements = append(snapshot.Elements,
			ElementRecord{Index: 5, RuntimeID: []int{0, 0, 0, 0}, ControlType: "menu item", Name: "Configure Paint...", States: "[has-click-action]"},
			ElementRecord{Index: 6, RuntimeID: []int{0, 0, 0, 1}, ControlType: "check menu item", Name: "Show Rulers", States: "[has-click-action]"},
		)
	}
	typedTreeTextForTest(t, snapshot)
	for _, node := range snapshot.AXSnapshot.ByIndex {
		if node != nil && node.Name == "Settings" {
			if node.Ref == "" {
				// The fixture only needs the acted element to be addressable.
				node.Ref = "r90"
			}
			snapshot.actedRef = node.Ref
		}
	}
	if snapshot.actedRef == "" {
		t.Fatal("fixture has no Settings menu item")
	}
	return snapshot
}

// A click that opens a menu leaves focus on the canvas or the opener; the
// compact result must list the opened items instead, so the caller does not
// need another find to see them.
func TestCompactActionObservationShowsOpenedPopupItems(t *testing.T) {
	snapshot := popupMenuSnapshot(t, true)
	lines := compactActionObservationLines(snapshot, nil)
	text := strings.Join(lines, "\n")
	if len(lines) == 0 || lines[0] != "popup:" ||
		!strings.Contains(text, `menuitem "Configure Paint..."`) ||
		!strings.Contains(text, `"Show Rulers"`) {
		t.Fatalf("opened menu items missing from compact result:\n%s", text)
	}
	if strings.Contains(text, "Canvas") {
		t.Fatalf("compact result kept the focused canvas instead of the popup:\n%s", text)
	}
	service := NewService()
	result, _, returned, compact, _ :=
		service.renderAXDiffOrCompactObservation(snapshot, nil)
	if !returned || !compact ||
		result.StructuredContent["final_observation_kind"] != "popup" {
		t.Fatalf("popup observation kind is inconsistent: %#v", result.StructuredContent)
	}
}

func TestCompactActionObservationKeepsFocusWhenNoPopupOpened(t *testing.T) {
	snapshot := popupMenuSnapshot(t, false)
	lines := compactActionObservationLines(snapshot, nil)
	if len(lines) == 0 || lines[0] != "focus:" {
		t.Fatalf("an action that opened nothing must keep the focus view, got %v", lines)
	}
}
