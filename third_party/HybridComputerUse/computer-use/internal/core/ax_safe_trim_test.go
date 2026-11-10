package core

import (
	"fmt"
	"reflect"
	"strconv"
	"strings"
	"testing"
	"unicode/utf8"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestFindTextExcerptBoundaries(t *testing.T) {
	for _, tc := range []struct {
		name, value, label, description string
		want                            bool
	}{
		{"empty", " \n\t", "", "", false},
		{"same name", "Hello world", "hello  world", "", false},
		{"visible description", "Hello world", "", "Contents: Hello world", false},
		{"suppressed description", "Hello world", "", "Hello world", true},
		{"clipped description", "Hello world", "", strings.Repeat("a", 300) + "Hello world" + strings.Repeat("z", 300), true},
		{"unicode", strings.Repeat("界🙂", 60), "", "", true},
		{"invalid utf8", "before\xffafter", "", "", true},
		{"visible long prefix", strings.Repeat("abcd", 100), strings.Repeat("abcd", 100), "", false},
	} {
		t.Run(tc.name, func(t *testing.T) {
			node := &AXNode{Role: "paragraph", Name: tc.label,
				Properties: AXProperties{Value: tc.value, Description: tc.description}}
			text, clipped, ok := findMatchText(node)
			if ok != tc.want || !utf8.ValidString(text) {
				t.Fatalf("excerpt=%q clipped=%v ok=%v want=%v", text, clipped, ok, tc.want)
			}
			if utf8.RuneCountInString(text) > maxFindTextRunes+1 {
				t.Fatalf("excerpt exceeds 80 content runes plus ellipsis: %q", text)
			}
			if tc.name == "unicode" && (text != strings.Repeat("界🙂", 40)+"…" || !clipped) {
				t.Fatalf("unicode prefix was not preserved: %q", text)
			}
			if node.Properties.Value != tc.value || node.Properties.Description != tc.description {
				t.Fatal("display excerpt mutated canonical properties")
			}
		})
	}
	for _, role := range []CanonicalRole{"textbox", "combobox", "slider", "spinbutton", "progressbar", "scrollbar"} {
		if _, _, ok := findMatchText(&AXNode{Role: role, Properties: AXProperties{Value: "already visible"}}); ok {
			t.Fatalf("duplicated working value for %s", role)
		}
	}
}

func TestAbsentRefRangesPreserveEveryRefAndGap(t *testing.T) {
	// r8 remains current; nested removed nodes include a duplicate r3.
	root := &AXNode{Ref: "r3", Children: []*AXNode{{Ref: "r4"}, {Ref: "r5"}, {Ref: "r7"}, {Ref: "r8"}, {Ref: "r9"}, {Ref: "r10"}, {Ref: "r11"}, {Ref: "r3"}}}
	refs := compactAbsentRefs([]AXDiffChange{{Kind: AXDiffRemoved, Before: root}}, &AXSnapshot{CanonicalRoots: []*AXNode{{Ref: "r8"}}})
	rendered := joinAXRefRuns(refs)
	expanded := []string{}
	for _, part := range strings.Split(rendered, ", ") {
		bounds := strings.Split(part, "..")
		if len(bounds) == 1 {
			expanded = append(expanded, part)
			continue
		}
		start, e1 := strconv.Atoi(strings.TrimPrefix(bounds[0], "r"))
		end, e2 := strconv.Atoi(strings.TrimPrefix(bounds[1], "r"))
		if e1 != nil || e2 != nil || end < start {
			t.Fatalf("invalid range %q", part)
		}
		for n := start; n <= end; n++ {
			expanded = append(expanded, fmt.Sprintf("r%d", n))
		}
	}
	want := []string{"r3", "r4", "r5", "r7", "r9", "r10", "r11"}
	if !reflect.DeepEqual(expanded, want) {
		t.Fatalf("%q expanded to %v, want %v", rendered, expanded, want)
	}
}

func TestSafeTrimPreservesRepeatedExplicitFullTrees(t *testing.T) {
	svc, _ := reportService(t, reportMain())
	window, _ := reportObserve(t, svc)
	for i := 0; i < 2; i++ {
		result := svc.callTool(contract.PublicToolAX, map[string]any{
			"mode": "observe", "window_ref": window, "observe": "full",
		})
		text := ResultText(result)
		if result.IsError || !strings.Contains(text, `button "OK"`) || strings.Contains(text, "Tree unchanged:") {
			t.Fatalf("explicit full read %d omitted its tree: %s", i+1, text)
		}
	}
}

// Consecutive absent refs are written as a run; isolated refs and pairs are
// listed one by one.
func TestAbsentRefsWriteConsecutiveRunsAsRanges(t *testing.T) {
	for _, test := range []struct {
		refs []string
		want string
	}{
		{[]string{"r5"}, "r5"},
		{[]string{"r5", "r6"}, "r5, r6"},
		{[]string{"r5", "r6", "r7"}, "r5..r7"},
		{[]string{"r1", "r3", "r4", "r5", "r6", "r9", "r10"}, "r1, r3..r6, r9, r10"},
		{[]string{"r14", "r1074", "r1075", "r1076", "r1497"}, "r14, r1074..r1076, r1497"},
	} {
		if got := joinAXRefRuns(test.refs); got != test.want {
			t.Fatalf("joinAXRefRuns(%v) = %q, want %q", test.refs, got, test.want)
		}
	}
}

// A removed node cannot be a target: its line names the node without an
// addressability marker, and its refs appear once in absent_refs.
func TestRemovedNodesCarryNoAddressabilityMarker(t *testing.T) {
	menu := &AXNode{
		ID: "menu", Role: "menu", Name: "Sheet",
		SemanticFingerprint: "f1:sheet",
	}
	items := []*AXNode{}
	for index := 0; index < 4; index++ {
		item := &AXNode{
			ID:   fmt.Sprintf("item-%d", index),
			Role: "menuitem", Name: fmt.Sprintf("Item %d", index),
			Ref:                     fmt.Sprintf("r%d", 40+index),
			SemanticFingerprint:     fmt.Sprintf("f1:item-%d", index),
			AddressabilityEvaluated: true,
			Parent:                  menu,
		}
		items = append(items, item)
	}
	menu.Children = items
	closed := *menu
	closed.Children = nil
	before := &AppSnapshot{WindowHandle: "window-1", AXSnapshot: &AXSnapshot{
		CanonicalRoots: []*AXNode{menu}, Roots: []*AXNode{menu},
	}}
	after := &AppSnapshot{
		WindowHandle: "window-1",
		TreeLines: []string{
			"ax:", ` - menu "Sheet"`,
			`  - text "` + strings.Repeat("unchanged context ", 30) + `"`,
		},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{&closed}, Roots: []*AXNode{&closed},
		},
	}
	diff, ok := testIncrementalAXSnapshot(before, after)
	if !ok {
		t.Fatal("closing a menu should produce a diff")
	}
	text := strings.Join(diff, "\n")
	if !strings.Contains(text, `<removed> menuitem "Item 0"`) ||
		strings.Contains(text, "addressable=") ||
		!strings.Contains(text, "absent_refs: [r40..r43]") {
		t.Fatalf("removed nodes rendered with markers or listed refs one by one:\n%s", text)
	}
}

// find on paragraphs shows the start of each paragraph's text, which the
// tree does not render, so the model can pick one without a rooted observe.
func TestFindShowsTextOfTextBearingMatches(t *testing.T) {
	long := "The committee reviewed item four and agreed to revisit the budget " +
		"allocation for the regional offices next quarter."
	svc := NewServiceWithBackend(&recordingPlatformBackend{})
	snapshot := &AppSnapshot{
		App:          AppDescriptor{Name: "Writer", PID: 42, ProviderBusName: ":1.42", ProcessGeneration: "42"},
		WindowTitle:  "report.docx",
		WindowHandle: "w2:" + strings.Repeat("a", 32),
		SnapshotID:   "find-text",
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "report.docx"},
			{Index: 1, RuntimeID: []int{0, 0}, ControlType: "document text", Name: "report"},
			{Index: 2, RuntimeID: []int{0, 0, 0}, ControlType: "paragraph", Value: "Short line.",
				States: "[focusable]", ProviderBusName: ":1.42", ProviderObjectPath: "/p/2", ProcessGeneration: "42"},
			{Index: 3, RuntimeID: []int{0, 0, 1}, ControlType: "paragraph", Value: long,
				States: "[focusable]", ProviderBusName: ":1.42", ProviderObjectPath: "/p/3", ProcessGeneration: "42"},
			{Index: 4, RuntimeID: []int{0, 0, 2}, ControlType: "paragraph",
				States: "[focusable]", ProviderBusName: ":1.42", ProviderObjectPath: "/p/4", ProcessGeneration: "42"},
		},
		Scope: contract.AXScopeWindow,
	}
	if err := svc.BuildAndRenderAXSnapshot(snapshot, contract.TreeViewWorking); err != nil {
		t.Fatal(err)
	}
	result := svc.RenderFindPage(snapshot, snapshot.Elements[2:], 0, 10,
		`role~"paragraph"`, nil, false)
	text := ResultText(result)
	if !strings.Contains(text, `text="Short line."`) ||
		!strings.Contains(text, `text="The committee reviewed item four and agreed to revisit the budget allocation…" text_truncated=true`) {
		t.Fatalf("find units lack paragraph text:\n%s", text)
	}
	if strings.Count(text, " text=") != 2 {
		t.Fatalf("an empty paragraph or the context got a text line:\n%s", text)
	}
}
