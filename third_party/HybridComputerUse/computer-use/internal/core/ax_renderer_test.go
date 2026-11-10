package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestStructuredAXYAMLSupportsValueAndChildren(t *testing.T) {
	root := &AXNode{
		ID:   "0",
		Role: "group",
		Name: "Status: current",
		Properties: AXProperties{
			Value: "ready\nnow",
		},
		Children: []*AXNode{{
			ID:   "0.0",
			Role: "button",
			Name: "Save #1",
			Ref:  "r7",
		}},
	}
	root.Children[0].Parent = root
	snapshot := &AXSnapshot{
		SchemaVersion: "AXSnapshot",
		AppIdentity:   "test",
		WindowHandle:  "window",
		Revision:      "rev",
		Roots:         []*AXNode{root},
	}
	lines, err := RenderAXSnapshotYAML(snapshot, AXRenderOptions{})
	if err != nil {
		t.Fatal(err)
	}
	text := strings.Join(lines, "\n")
	for _, fragment := range []string{
		"ax:",
		`group "Status: current":`,
		`button "Save #1" [ref=r7]`,
	} {
		if !strings.Contains(text, fragment) {
			t.Fatalf("typed YAML missing %q:\n%s", fragment, text)
		}
	}
	if strings.Contains(text, "\t") {
		t.Fatalf("YAML indentation must not contain tabs:\n%s", text)
	}
	if strings.Contains(text, `/value: "ready\nnow"`) {
		t.Fatalf("working group leaked provider default value:\n%s", text)
	}
	rendered, err := RenderAXSnapshotYAML(snapshot, AXRenderOptions{})
	if err != nil {
		t.Fatal(err)
	}
	for _, forbidden := range []string{
		`/value: "ready\nnow"`,
		"/provider-role",
		"/provider-actions",
	} {
		if strings.Contains(strings.Join(rendered, "\n"), forbidden) {
			t.Fatalf("model renderer leaked %q:\n%s",
				forbidden, strings.Join(rendered, "\n"))
		}
	}
}

func TestRelationPruningRequiresUniqueLabelTarget(t *testing.T) {
	snapshot := &AppSnapshot{
		Elements: []ElementRecord{
			{Index: 1, RuntimeID: []int{0}, ControlType: "frame", Name: "Dialog"},
			{
				Index: 2, RuntimeID: []int{0, 0}, ControlType: "label", Name: "Width",
				ProviderBusName: ":1.10", ProviderObjectPath: "/label/width",
				ProcessGeneration: "p1",
			},
			{
				Index: 3, RuntimeID: []int{0, 1}, ControlType: "spin button", Name: "Width",
				RelationReadComplete: true,
				NameSource:           "labelled_by",
				Relations: map[string][]axRelationTarget{
					"labelled-by": {{
						Role: "label", Name: "Width",
						ProviderBusName:    ":1.10",
						ProviderObjectPath: "/label/width",
						ProcessGeneration:  "p1",
					}},
				},
			},
		},
	}
	tree, err := buildAXSnapshot(snapshot)
	if err != nil {
		t.Fatal(err)
	}
	registry := NewRefRegistry(32)
	registry.AssignSnapshotRefs(tree)
	if err := distillAXSnapshot(tree); err != nil {
		t.Fatal(err)
	}
	if AXProjectedIndices(tree.Roots)[2] {
		t.Fatal("unique relation-linked duplicate label was not pruned")
	}
	snapshot.Elements = append(snapshot.Elements, ElementRecord{
		Index: 4, RuntimeID: []int{0, 2}, ControlType: "label", Name: "Width",
		ProviderBusName: ":1.10", ProviderObjectPath: "/label/other",
		ProcessGeneration: "p1",
	})
	tree, err = buildAXSnapshot(snapshot)
	if err != nil {
		t.Fatal(err)
	}
	registry.AssignSnapshotRefs(tree)
	if err := distillAXSnapshot(tree); err != nil {
		t.Fatal(err)
	}
	if AXProjectedIndices(tree.Roots)[2] {
		t.Fatal("exact provider-linked duplicate label was not pruned")
	}
	snapshot.Elements[2].RelationReadComplete = false
	tree, err = buildAXSnapshot(snapshot)
	if err != nil {
		t.Fatal(err)
	}
	registry.AssignSnapshotRefs(tree)
	if err := distillAXSnapshot(tree); err != nil {
		t.Fatal(err)
	}
	if !AXProjectedIndices(tree.Roots)[2] {
		t.Fatal("incomplete relation evidence must retain the label")
	}
}

func TestAllModelRenderersHideProviderDiagnostics(t *testing.T) {
	snapshot := editorFormSnapshot()
	snapshot.Annotations = []axProviderAnnotation{{
		Kind:   "provider_omission",
		Count:  12,
		Reason: "time_budget",
		Detail: "internal traversal stopped",
	}}
	for _, view := range []string{contract.TreeViewWorking, contract.TreeViewFull} {
		typed, err := buildAXSnapshot(snapshot)
		if err != nil {
			t.Fatal(err)
		}
		if err := distillAXSnapshot(typed); err != nil {
			t.Fatal(err)
		}
		lines, err := RenderAXSnapshotYAML(
			typed,
			AXRenderOptions{
				RootKey: "ax",
			},
		)
		if err != nil {
			t.Fatal(err)
		}
		text := strings.Join(lines, "\n")
		for _, forbidden := range []string{
			"provider_omission",
			"time_budget",
			"internal traversal stopped",
			"/provider-role",
			"/provider-actions",
		} {
			if strings.Contains(text, forbidden) {
				t.Fatalf("%s renderer leaked %q:\n%s", view, forbidden, text)
			}
		}
	}
}

// A spreadsheet leaves its empty visible cells out of the tree. The
// provider's summary of them arrives as an annotation on the table; it must
// reach the rendered tree, and it is not an acquisition gap.
func TestEmptyCellSummaryIsRenderedOnTheTable(t *testing.T) {
	detail := "294 empty cells in view A1:J30 omitted (6 listed; table is " +
		"1048576x16384); they have no ref: move the cell cursor there with " +
		"keypress, then type without a ref"
	snapshot := &AppSnapshot{
		App:          AppDescriptor{Name: "Sheets", PID: 42},
		WindowTitle:  "Book",
		WindowHandle: "0x42",
		SnapshotID:   "sheet",
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Book"},
			{Index: 1, RuntimeID: []int{0, 0}, ControlType: "table", Name: "Sheet1"},
			{Index: 2, RuntimeID: []int{0, 0, 0, 1}, ControlType: "table cell", Name: "B1"},
		},
		Annotations: []axProviderAnnotation{{
			ParentRuntimeID: []int{0, 0},
			Kind:            axCellSummaryAnnotation,
			Count:           294,
			Reason:          "empty_cells",
			Detail:          detail,
		}},
		Scope: contract.AXScopeWindow,
	}
	text := typedTreeTextForTest(t, snapshot)
	if !strings.Contains(text, `table "Sheet1":`) ||
		!strings.Contains(text, "/empty-cells: ") ||
		!strings.Contains(text, "move the cell cursor there with keypress") {
		t.Fatalf("empty-cell summary missing from the rendered table:\n%s", text)
	}
	coverage := providerCoverageSummary(snapshot.Annotations)
	if coverage.AcquisitionIncomplete != 0 || coverage.ProjectionHidden != 0 ||
		coverage.ExpandableHidden != 0 {
		t.Fatalf("empty-cell summary was counted as a coverage gap: %#v", coverage)
	}
}

// MuseScore's own accessibility layer answers most child slots of its Qt
// Quick windows with null references. The window then looks empty; the
// count must reach the rendered tree on the parent, and it stays a coverage
// gap because the toolkit reports content that AX cannot read.
func TestNullChildReferencesAreRenderedOnTheirParent(t *testing.T) {
	detail := "76 of 78 child references returned no object; AX cannot read " +
		"or address that part of the window. Use GUI there."
	snapshot := &AppSnapshot{
		App:          AppDescriptor{Name: "MuseScore4", PID: 42},
		WindowTitle:  "MuseScore Studio",
		WindowHandle: "0x42",
		SnapshotID:   "null-children",
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "MuseScore Studio"},
		},
		Annotations: []axProviderAnnotation{{
			ParentRuntimeID: []int{0},
			Kind:            "provider_omission",
			Count:           76,
			Reason:          axNullReferenceReason,
			Detail:          detail,
		}},
		Scope: contract.AXScopeWindow,
	}
	text := typedTreeTextForTest(t, snapshot)
	if !strings.Contains(text, "/null-children: ") || !strings.Contains(text, "76 of 78 child references returned no object") {
		t.Fatalf("null-children summary missing from the rendered window:\n%s", text)
	}
	if coverage := providerCoverageSummary(snapshot.Annotations); coverage.AcquisitionIncomplete != 76 {
		t.Fatalf("null children are not a coverage gap: %#v", coverage)
	}
}
