package core

import (
	"fmt"
	"strings"
	"testing"
)

func TestFindContextReturnsMergedCanonicalPathAndNearbySiblings(t *testing.T) {
	window := &AXNode{ID: "window", Role: "window", Name: "Editor"}
	dialog := &AXNode{
		ID: "dialog", Role: "dialog", Name: "Preferences", Parent: window,
	}
	group := &AXNode{
		ID: "group", Role: "group", Name: "Document", Parent: dialog,
	}
	window.Children = []*AXNode{dialog}
	dialog.Children = []*AXNode{group}
	for index, name := range []string{
		"Before 1", "Before 2", "Save", "After 1", "After 2", "Unrelated",
	} {
		node := &AXNode{
			ID:            fmt.Sprintf("button-%d", index),
			Role:          "button",
			Name:          name,
			ProviderIndex: index + 1,
			Parent:        group,
			Capabilities: AXCapabilities{
				Activate: ActivationSemantic,
			},
		}
		if name == "Save" {
			node.Ref = "r7"
		}
		group.Children = append(group.Children, node)
	}
	snapshot := &AppSnapshot{
		AXSnapshot: &AXSnapshot{
			SchemaVersion:  "AXSnapshot",
			AppIdentity:    "editor",
			WindowHandle:   "window-1",
			Revision:       "revision",
			CanonicalRoots: []*AXNode{window},
			Roots:          cloneAXForest([]*AXNode{window}, nil),
			ByIndex: map[int]*AXNode{
				3: group.Children[2],
			},
		},
	}
	context := buildAXFindContext(
		snapshot,
		[]ElementRecord{{Index: 3}},
		map[int]bool{},
	)
	findSnapshot := &AXSnapshot{
		SchemaVersion: "AXSnapshot",
		AppIdentity:   "editor",
		WindowHandle:  "window-1",
		Revision:      "revision",
		Roots:         context,
	}
	lines, err := RenderAXSnapshotYAML(
		findSnapshot,
		AXRenderOptions{RootKey: "matches"},
	)
	if err != nil {
		t.Fatal(err)
	}
	text := strings.Join(lines, "\n")
	for _, expected := range []string{
		`window "Editor"`,
		`dialog "Preferences"`,
		`group "Document"`,
		`button "Before 1"`,
		`button "Before 2"`,
		`button "Save" [ref=r7]`,
		`/match: "true"`,
		`button "After 1"`,
		`button "After 2"`,
	} {
		if !strings.Contains(text, expected) {
			t.Fatalf("find context omitted %q:\n%s", expected, text)
		}
	}
	for _, forbidden := range []string{
		"working_projection",
		"serializer_hidden",
		"provider_omission",
		"reason=",
	} {
		if strings.Contains(text, forbidden) {
			t.Fatalf("find context leaked internal annotation %q:\n%s", forbidden, text)
		}
	}
	if strings.Contains(text, `button "Unrelated"`) {
		t.Fatalf("find context included distant sibling:\n%s", text)
	}
}

func TestStructuralBudgetPreservesCriticalStateAndFindRecovery(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "900")
	window := &AXNode{ID: "window", Role: "window", Name: "Editor"}
	toolbar := &AXNode{
		ID: "toolbar", Role: "toolbar", Name: "Commands", Parent: window,
	}
	window.Children = []*AXNode{toolbar}
	byIndex := map[int]*AXNode{}
	for index := 0; index < 40; index++ {
		node := &AXNode{
			ID:            fmt.Sprintf("command-%d", index),
			Role:          "button",
			Name:          fmt.Sprintf("Command %02d", index),
			Ref:           fmt.Sprintf("r%d", index+1),
			ProviderIndex: index + 1,
			Parent:        toolbar,
			Capabilities: AXCapabilities{
				Activate: ActivationSemantic,
			},
		}
		if index == 20 {
			node.States.Selected = TriTrue
		}
		toolbar.Children = append(toolbar.Children, node)
		byIndex[index+1] = node
	}
	snapshot := &AXSnapshot{
		SchemaVersion:  "AXSnapshot",
		AppIdentity:    "editor",
		WindowHandle:   "window-1",
		Revision:       "revision",
		CanonicalRoots: []*AXNode{window},
		Roots:          cloneAXForest([]*AXNode{window}, nil),
		ByID:           map[string]*AXNode{},
		ByIndex:        byIndex,
	}
	if err := distillAXSnapshot(snapshot); err != nil {
		t.Fatal(err)
	}
	lines, err := RenderAXSnapshotYAML(snapshot, AXRenderOptions{})
	if err != nil {
		t.Fatal(err)
	}
	text := strings.Join(lines, "\n")
	if !strings.Contains(text, `button "Command 20" [selected=true] [ref=r21]`) {
		t.Fatalf("critical selected target was omitted:\n%s", text)
	}
	if !strings.Contains(text, "recover=find|root") {
		t.Fatalf("budget omission lacks recovery path:\n%s", text)
	}
	var budget DistillationStageReport
	for _, report := range snapshot.Distillation {
		if report.Name == "apply_output_budget" {
			budget = report
			break
		}
	}
	if !budget.BudgetApplied ||
		budget.BudgetOmittedNodes == 0 ||
		budget.BudgetOmittedTargets == 0 ||
		budget.TargetRecall >= budget.TargetCount ||
		budget.InvariantFailure != "" {
		t.Fatalf("structural budget report is invalid: %#v", budget)
	}
	if budget.BudgetCharsAfter > axStructuralTreeBudget() {
		t.Fatalf(
			"structural budget remained oversized: %d > %d",
			budget.BudgetCharsAfter,
			axStructuralTreeBudget(),
		)
	}
	app := &AppSnapshot{AXSnapshot: snapshot}
	context := buildAXFindContext(
		app,
		[]ElementRecord{{Index: 2}},
		AXProjectedIndices(snapshot.Roots),
	)
	findSnapshot := &AXSnapshot{
		SchemaVersion: "AXSnapshot",
		AppIdentity:   "editor",
		WindowHandle:  "window-1",
		Revision:      "revision",
		Roots:         context,
	}
	findLines, err := RenderAXSnapshotYAML(
		findSnapshot,
		AXRenderOptions{RootKey: "matches"},
	)
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(
		strings.Join(findLines, "\n"),
		`button "Command 01" [ref=r2]`,
	) {
		t.Fatalf("budget-hidden target was not recoverable by find")
	}
}

func TestAmbiguousFindCandidatesReceiveProviderBoundRefs(t *testing.T) {
	snapshot := &AXSnapshot{
		AppIdentity:  "Editor",
		AppRef:       "a1",
		WindowHandle: "pid7/win0/frame",
		WindowRef:    "w1",
		Revision:     "c1",
		Roots: []*AXNode{
			{
				ID: "left", Role: CanonicalRole("text"), Name: "Repeated",
				FindMatch: true, LocatorKind: AXLocatorSemantic,
				SemanticTarget:     `text "Repeated" [focused]`,
				ProviderBusName:    ":1.42",
				ProviderObjectPath: "/org/a11y/label/1",
				ProcessGeneration:  "pid:7:start:10",
				RuntimeID:          []int{0, 1},
			},
			{
				ID: "right", Role: CanonicalRole("text"), Name: "Repeated",
				FindMatch: true, LocatorKind: AXLocatorSemantic,
				SemanticTarget:     `text "Repeated"`,
				ProviderBusName:    ":1.42",
				ProviderObjectPath: "/org/a11y/label/2",
				ProcessGeneration:  "pid:7:start:10",
				RuntimeID:          []int{0, 2},
			},
		},
		ByID: map[string]*AXNode{},
	}
	for _, node := range snapshot.Roots {
		snapshot.ByID[node.ID] = node
	}
	registry := NewRefRegistry(32)
	registry.AssignSnapshotRefs(snapshot)
	for _, node := range snapshot.Roots {
		if node.Ref == "" {
			t.Fatalf("ambiguous find candidate %q received no ref", node.ID)
		}
		entry := registry.Entries[node.Ref]
		if entry == nil || entry.LocatorKind != AXLocatorProviderBound {
			t.Fatalf(
				"candidate %q locator=%v, want provider_bound",
				node.ID,
				entry,
			)
		}
	}
	if snapshot.Roots[0].Ref == snapshot.Roots[1].Ref {
		t.Fatal("distinct provider objects shared one ref")
	}
}

func TestFindCandidateDoesNotKeepInheritedUnsafeRef(t *testing.T) {
	left := &AXNode{
		ID: "left", Role: CanonicalRole("text"), Name: "Repeated",
		FindMatch: true, LocatorKind: AXLocatorSemantic,
		SemanticTarget: `text "Repeated"`, Ref: "r98",
	}
	right := &AXNode{
		ID: "right", Role: CanonicalRole("text"), Name: "Repeated",
		FindMatch: true, LocatorKind: AXLocatorSemantic,
		SemanticTarget: `text "Repeated"`, Ref: "r99",
	}
	snapshot := &AXSnapshot{
		AppIdentity: "Editor", AppRef: "a1",
		WindowHandle: "pid7/win0/frame", WindowRef: "w1",
		Revision: "c1", Roots: []*AXNode{left, right},
		ByID: map[string]*AXNode{"left": left, "right": right},
	}
	NewRefRegistry(32).AssignSnapshotRefs(snapshot)
	if left.Ref != "" || right.Ref != "" {
		t.Fatalf(
			"unsafe inherited refs remained visible: left=%q right=%q",
			left.Ref,
			right.Ref,
		)
	}
}

func TestStabilizeFindRefsUsesRootResolverTruth(t *testing.T) {
	service := NewService()
	left := &AXNode{
		ID: "left", Role: CanonicalRole("button"), Name: "Repeated",
		FindMatch: true, LocatorKind: AXLocatorSemantic,
		SemanticTarget: `button "Repeated"`, Ref: "r1",
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/button/1",
		ProcessGeneration:  "pid:7:start:10",
		RuntimeID:          []int{0, 1},
	}
	service.RefRegistry.Entries["r1"] = &AXRefEntry{
		Ref: "r1", LocatorKind: AXLocatorSemantic,
		SemanticTarget: `button "Repeated"`,
		WindowHandle:   "pid7/win0/frame",
		Lifecycle:      AXRefActive,
	}
	source := &AppSnapshot{
		WindowHandle: "pid7/win0/frame",
		Elements: []ElementRecord{
			{
				Index: 1, ControlType: "push button", Name: "Repeated",
				RuntimeID: []int{0, 1},
			},
			{
				Index: 2, ControlType: "push button", Name: "Repeated",
				RuntimeID: []int{0, 2},
			},
		},
	}
	findSnapshot := &AXSnapshot{
		AppIdentity: "Editor", AppRef: "a1",
		WindowHandle: "pid7/win0/frame", WindowRef: "w1",
		Revision: "c1", Roots: []*AXNode{left},
		ByID: map[string]*AXNode{"left": left},
	}
	service.stabilizeFindRefs(source, findSnapshot)
	if left.Ref == "" {
		t.Fatal("ambiguous semantic ref was not replaced")
	}
	entry := service.RefRegistry.Entries[left.Ref]
	if entry == nil || entry.LocatorKind != AXLocatorProviderBound {
		t.Fatalf("replacement entry=%#v, want provider_bound", entry)
	}
}
