package core

import (
	"encoding/json"
	"fmt"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestRefRegistryIsMonotonicPersistentAndExplicitlyExpires(t *testing.T) {
	registry := NewRefRegistry(2)
	makeRef := func(name string) string {
		return registry.Acquire(
			"editor", "window-1",
			encodeScopedSemanticRef(
				`push button "`+name+`"`, "window-1",
			),
			"button", name, "rev",
		)
	}
	first := makeRef("One")
	second := makeRef("Two")
	if first != "r1" || second != "r2" {
		t.Fatalf("unexpected initial refs: %q %q", first, second)
	}
	if again := makeRef("One"); again != first {
		t.Fatalf("stable identity was reissued: %q -> %q", first, again)
	}
	third := makeRef("Three")
	if third != "r3" {
		t.Fatalf("registry reused an old id: %q", third)
	}
	if _, err := registry.Resolve(second, "editor"); err == nil ||
		!strings.Contains(err.Error(), "expired") {
		t.Fatalf("evicted ref did not report expired: %v", err)
	}
	if replacement := makeRef("Two"); replacement != "r4" {
		t.Fatalf("expired identity reused its old ref: %q", replacement)
	}
}

func TestRoleOntologyAndTypedStatesDoNotInventFacts(t *testing.T) {
	record := &ElementRecord{
		ControlType: "internal frame",
		States:      "[focused checked]",
		Actions:     []string{"click", "set value"},
	}
	descriptor := canonicalRoleDescriptor(record.ControlType, nil)
	if descriptor.ModelRole != RoleGeneric {
		t.Fatalf("unknown provider role became %q", descriptor.ModelRole)
	}
	states := typedAXStates(record)
	if states.Active != TriTrue || states.Checked != CheckedTrue {
		t.Fatalf("typed states lost explicit facts: %#v", states)
	}
	if states.Disabled != TriUnknown {
		t.Fatalf("missing enabled/disabled was inferred as %q", states.Disabled)
	}
	capabilities := typedAXCapabilities(record, descriptor.ModelRole)
	if capabilities.Activate != ActivationSemantic || !capabilities.SetValue {
		t.Fatalf("actions were not separated into capabilities: %#v", capabilities)
	}
}

func TestDistillationPipelineReportsEveryStageAndPreservesTargets(t *testing.T) {
	snapshot := &AppSnapshot{
		App:          AppDescriptor{Name: "Editor"},
		WindowHandle: "window-1",
		SnapshotID:   "rev1",
		Elements: []ElementRecord{
			{Index: 1, RuntimeID: []int{0}, ControlType: "frame", Name: "Editor"},
			{Index: 2, RuntimeID: []int{0, 0}, ControlType: "panel"},
			{Index: 3, RuntimeID: []int{0, 0, 0}, ControlType: "push button", Name: "Save", Actions: []string{"click"}},
		},
	}
	typed, err := buildAXSnapshot(snapshot)
	if err != nil {
		t.Fatal(err)
	}
	NewRefRegistry(32).AssignSnapshotRefs(typed)
	if err := distillAXSnapshot(typed); err != nil {
		t.Fatal(err)
	}
	want := []string{
		"normalize_whitespace",
		"remove_empty_decorative_leaves",
		"remove_unaddressable_controls",
		"coalesce_text",
		"unwrap_neutral_single_child",
		"remove_relation_proven_duplicate_labels",
		"suppress_redundant_cohort_properties",
		"summarize_homogeneous_collections",
		"shorten_named_descriptions",
		"apply_output_budget",
	}
	if len(typed.Distillation) != len(want) {
		t.Fatalf("stage reports=%d want=%d", len(typed.Distillation), len(want))
	}
	for index, report := range typed.Distillation {
		if report.Name != want[index] {
			t.Fatalf("stage %d=%q want %q", index, report.Name, want[index])
		}
		if report.TargetRecall != report.TargetCount || report.InvariantFailure != "" {
			t.Fatalf("stage invariant failed: %#v", report)
		}
	}
}

func TestBuildAndRenderAssignsRefsBeforeDistillation(t *testing.T) {
	service := NewService()
	snapshot := &AppSnapshot{
		App: AppDescriptor{
			Name:              "Editor",
			ProviderBusName:   ":1.42",
			ProcessGeneration: "42",
		},
		WindowHandle: "w2:" + strings.Repeat("a", 32),
		SnapshotID:   "revision-1",
		Elements: []ElementRecord{
			{
				Index: 1, RuntimeID: []int{0},
				ControlType: "frame", Name: "Editor",
			},
			{
				Index: 2, RuntimeID: []int{0, 0},
				ControlType: "push button", Name: "Save",
				Actions: []string{"click"},
			},
		},
	}
	if err := service.BuildAndRenderAXSnapshot(
		snapshot,
		contract.TreeViewWorking,
	); err != nil {
		t.Fatal(err)
	}
	if snapshot.AXSnapshot == nil {
		t.Fatal("working projection did not retain its typed snapshot")
	}
	button := snapshot.AXSnapshot.ByIndex[2]
	if button == nil || button.Ref == "" || !button.AddressabilityEvaluated {
		t.Fatalf(
			"canonical button was not addressed before projection: app_ref=%q window_ref=%q button=%#v",
			snapshot.AXSnapshot.AppRef,
			snapshot.AXSnapshot.WindowRef,
			button,
		)
	}
	for _, report := range snapshot.AXSnapshot.Distillation {
		if report.TargetCount == 0 || report.TargetRecall != report.TargetCount {
			t.Fatalf("distillation ran without assigned target truth: %#v", report)
		}
	}
	if !strings.Contains(
		strings.Join(snapshot.TreeLines, "\n"),
		`button "Save" [ref=`,
	) {
		t.Fatalf("working projection lost the assigned ref:\n%s",
			strings.Join(snapshot.TreeLines, "\n"))
	}
}

func TestCollectionSummaryPreservesItemsWithAddressableDescendants(t *testing.T) {
	cases := []struct {
		name       string
		parentRole CanonicalRole
		itemRole   CanonicalRole
	}{
		{name: "list item", parentRole: "list", itemRole: "listitem"},
		{name: "grid cell", parentRole: "grid", itemRole: "gridcell"},
		{name: "tree item", parentRole: "tree", itemRole: "treeitem"},
		{name: "article", parentRole: "document", itemRole: "article"},
		{name: "option", parentRole: "list", itemRole: "option"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			parent := &AXNode{ID: "0", Role: testCase.parentRole}
			for index := 0; index < 12; index++ {
				item := &AXNode{
					ID:     fmt.Sprintf("0/%d", index),
					Role:   testCase.itemRole,
					Parent: parent,
				}
				if index == 6 {
					group := &AXNode{
						ID:     "0/6/0",
						Role:   "group",
						Parent: item,
					}
					target := &AXNode{
						ID:     "0/6/0/0",
						Role:   "link",
						Name:   "Open result",
						Ref:    "r7",
						Parent: group,
					}
					group.Children = []*AXNode{target}
					item.Children = []*AXNode{group}
				}
				parent.Children = append(parent.Children, item)
			}
			canonical := cloneAXForest([]*AXNode{parent}, nil)
			snapshot := &AXSnapshot{
				SchemaVersion:  "AXSnapshot",
				CanonicalRoots: canonical,
				Roots:          []*AXNode{parent},
			}
			before := countAXNodes(snapshot.Roots)
			if err := distillAXSnapshot(snapshot); err != nil {
				t.Fatalf("collection summarization lost a descendant target: %v", err)
			}
			if got := targetRecall(
				collectAXTargetIDs(snapshot.CanonicalRoots),
				snapshot.Roots,
			); got != 1 {
				t.Fatalf("descendant target recall=%d want 1", got)
			}
			foundTarget := false
			foundSummary := false
			var visit func([]*AXNode)
			visit = func(nodes []*AXNode) {
				for _, node := range nodes {
					foundTarget = foundTarget || node.ID == "0/6/0/0"
					foundSummary = foundSummary ||
						(node.Synthetic &&
							strings.Contains(node.Properties.Omitted, "collection role="))
					visit(node.Children)
				}
			}
			visit(snapshot.Roots)
			if !foundTarget {
				t.Fatal("addressable descendant was removed from the projection")
			}
			if !foundSummary || countAXNodes(snapshot.Roots) >= before {
				t.Fatalf(
					"target-free middle items were not summarized: before=%d after=%d",
					before,
					countAXNodes(snapshot.Roots),
				)
			}
		})
	}
}

func TestRendererMarksRetainedUnaddressableInteractiveRoles(t *testing.T) {
	cases := []*AXNode{
		{
			ID: "menu", Role: "menu", SemanticClass: "boundary",
			Name: "File", States: AXStates{Expanded: TriFalse},
			AddressabilityEvaluated: true,
		},
		{
			ID: "control", Role: "button", SemanticClass: "control",
			Name: "Save", AddressabilityEvaluated: true,
		},
		{
			ID: "item", Role: "listitem", SemanticClass: "item",
			Name: "Inbox", AddressabilityEvaluated: true,
		},
	}
	for _, node := range cases {
		if label := renderAXNodeLabel(node); !strings.Contains(
			label,
			"[addressable=false]",
		) {
			t.Fatalf("%s without ref lacked addressability marker: %q",
				node.Role, label)
		}
	}
	passive := &AXNode{
		ID: "group", Role: "group", SemanticClass: "structure",
		Name: "Layout", AddressabilityEvaluated: true,
	}
	if label := renderAXNodeLabel(passive); strings.Contains(
		label,
		"[addressable=false]",
	) {
		t.Fatalf("passive structure received an interaction marker: %q", label)
	}
	unknown := &AXNode{
		ID: "unknown", Role: "button", SemanticClass: "control",
		Name: "Pending",
	}
	if label := renderAXNodeLabel(unknown); !strings.Contains(
		label,
		"[addressable=unknown]",
	) {
		t.Fatalf("unassessed interaction lacked unknown marker: %q", label)
	}
}

func TestCoalesceTextPreservesNameContent(t *testing.T) {
	parent := &AXNode{ID: "parent", Role: "group"}
	first := &AXNode{ID: "first", Role: RoleText, Name: "Hello"}
	second := &AXNode{ID: "second", Role: RoleText, Name: "World"}
	parent.Children = []*AXNode{first, second}
	snapshot := &AXSnapshot{
		SchemaVersion:  "AXSnapshot",
		CanonicalRoots: []*AXNode{parent},
		Roots:          cloneAXForest([]*AXNode{parent}, nil),
		ByID:           map[string]*AXNode{},
		ByIndex:        map[int]*AXNode{},
	}
	if err := distillAXSnapshot(snapshot); err != nil {
		t.Fatal(err)
	}
	if len(snapshot.Roots) != 1 ||
		snapshot.Roots[0].Name != "Hello World" {
		t.Fatalf("coalesced text lost accessible names: %#v", snapshot.Roots)
	}
}

func TestTypedActionabilityEvidenceDoesNotClaimDelivery(t *testing.T) {
	evidence := initialActionabilityEvidence(
		"click",
		&ElementRecord{
			ControlType: "push button",
			Actions:     []string{"click"},
		},
		nil,
	)
	result := ToolCallResult{StructuredContent: map[string]any{
		"input_events_emitted": true,
	}}
	final := finalizeActionabilityEvidence(
		evidence,
		AXServiceRequest{Action: AXClickCommand{}},
		result,
		true,
		nil,
	)
	if final.InputEmitted != TriTrue ||
		final.DeliveryVerified != TriUnknown ||
		final.PostconditionVerified != TriUnknown {
		t.Fatalf("actionability overclaimed evidence: %#v", final)
	}
}

func TestStaticContentDoesNotReceiveRefsOrBlockCollectionSummary(t *testing.T) {
	list := &AXNode{
		ID:   "list",
		Role: "list",
	}
	for index := 0; index < 20; index++ {
		child := &AXNode{
			ID:             fmt.Sprintf("item-%d", index),
			Role:           "listitem",
			Name:           fmt.Sprintf("Static item %d", index),
			SemanticTarget: fmt.Sprintf("s2:static-%d", index),
			Parent:         list,
		}
		list.Children = append(list.Children, child)
	}
	button := &AXNode{
		ID:             "button",
		Role:           "button",
		Name:           "Apply",
		SemanticTarget: "s2:apply",
		Capabilities: AXCapabilities{
			Activate: ActivationSemantic,
		},
		Parent: list,
	}
	list.Children = append(list.Children, button)
	snapshot := &AXSnapshot{
		SchemaVersion:  "AXSnapshot",
		AppRef:         "a1",
		AppIdentity:    "test",
		WindowHandle:   "window",
		WindowRef:      "w1",
		Revision:       "revision",
		CanonicalRoots: []*AXNode{list},
		Roots:          cloneAXForest([]*AXNode{list}, nil),
		ByID:           map[string]*AXNode{},
		ByIndex:        map[int]*AXNode{},
	}
	NewRefRegistry(64).AssignSnapshotRefs(snapshot)
	projectedButton := snapshot.Roots[0].Children[len(snapshot.Roots[0].Children)-1]
	if projectedButton.Ref == "" {
		t.Fatal("interactive button did not receive a ref")
	}
	for _, child := range list.Children[:20] {
		if child.Ref != "" {
			t.Fatalf("static item received address cost: %#v", child)
		}
	}
	snapshot.Roots = cloneAXForest(snapshot.CanonicalRoots, nil)
	if err := distillAXSnapshot(snapshot); err != nil {
		t.Fatal(err)
	}
	if got := countAXNodes(snapshot.Roots); got >= 22 {
		t.Fatalf("static collection was not compressed: %d nodes", got)
	}
}

func TestRefsSkipAnonymousAndCollapsedDescendants(t *testing.T) {
	root := &AXNode{ID: "root", Role: "window", Name: "App"}
	anonymous := &AXNode{
		ID:             "anonymous",
		Role:           "button",
		SemanticTarget: "s2:anonymous",
		Capabilities:   AXCapabilities{Activate: ActivationSemantic},
		Parent:         root,
	}
	menu := &AXNode{
		ID:     "menu",
		Role:   "menu",
		Name:   "File",
		States: AXStates{Expanded: TriFalse},
		Parent: root,
	}
	hidden := &AXNode{
		ID:             "hidden",
		Role:           "menuitem",
		Name:           "Save",
		SemanticTarget: "s2:hidden",
		Capabilities:   AXCapabilities{Activate: ActivationSemantic},
		Parent:         menu,
	}
	visible := &AXNode{
		ID:             "visible",
		Role:           "button",
		Name:           "Apply",
		SemanticTarget: "s2:visible",
		Capabilities:   AXCapabilities{Activate: ActivationSemantic},
		Parent:         root,
	}
	menu.Children = []*AXNode{hidden}
	root.Children = []*AXNode{anonymous, menu, visible}
	snapshot := &AXSnapshot{
		SchemaVersion: "AXSnapshot",
		AppRef:        "a1",
		AppIdentity:   "app",
		WindowHandle:  "window",
		WindowRef:     "w1",
		Revision:      "r1",
		Roots:         []*AXNode{root},
	}
	NewRefRegistry(16).AssignSnapshotRefs(snapshot)
	if anonymous.Ref != "" || hidden.Ref != "" {
		t.Fatalf("non-operable nodes received refs: anonymous=%q hidden=%q",
			anonymous.Ref, hidden.Ref)
	}
	if visible.Ref == "" {
		t.Fatal("visible identifiable control did not receive a ref")
	}
}

func TestExactProviderAliasesCollapseToOneCanonicalTarget(t *testing.T) {
	snapshot := &AppSnapshot{
		WindowHandle:    "w2:" + strings.Repeat("a", 32),
		publicWindowRef: "w1",
		publicAppRef:    "a1",
		SnapshotID:      "c1",
		App: AppDescriptor{
			Name:              "Editor",
			ProviderBusName:   ":1.42",
			ProcessGeneration: "99123",
		},
		Elements: []ElementRecord{
			{
				Index: 0, RuntimeID: []int{0},
				ControlType: "frame", Name: "Editor",
			},
			{
				Index: 1, RuntimeID: []int{0, 0},
				ControlType: "menu", Name: "Format",
				Actions:            []string{"click"},
				States:             "[focused]",
				ProviderBusName:    ":1.42",
				ProviderObjectPath: "/org/a11y/format",
				ProcessGeneration:  "99123",
			},
			{
				Index: 2, RuntimeID: []int{0, 1},
				ControlType: "menu", Name: "Format",
				Actions:            []string{"click"},
				States:             "[selected]",
				ProviderBusName:    ":1.42",
				ProviderObjectPath: "/org/a11y/format",
				ProcessGeneration:  "99123",
			},
		},
	}
	tree, err := buildAXSnapshot(snapshot)
	if err != nil {
		t.Fatal(err)
	}
	if got := len(tree.CanonicalRoots[0].Children); got != 1 {
		t.Fatalf("exact provider aliases produced %d canonical children", got)
	}
	node := tree.CanonicalRoots[0].Children[0]
	if tree.ByIndex[1] != node || tree.ByIndex[2] != node {
		t.Fatal("provider index aliases did not resolve to one canonical node")
	}
	if len(node.RuntimeIDAliases) != 2 {
		t.Fatalf("runtime aliases=%v, want 2", node.RuntimeIDAliases)
	}
	if node.States.Active != TriUnknown ||
		node.States.Selected != TriUnknown {
		t.Fatalf("alias-only states were promoted into canonical facts: %#v", node.States)
	}
	if node.LocatorKind != AXLocatorProviderBound {
		t.Fatalf("ambiguous semantic target kind=%q, want provider_bound", node.LocatorKind)
	}
	registry := NewRefRegistry(16)
	registry.AssignSnapshotRefs(tree)
	if node.Ref == "" {
		t.Fatal("provider-bound ambiguous target did not receive a short ref")
	}
}

func TestSameNameDifferentProviderObjectsRemainDistinct(t *testing.T) {
	snapshot := &AppSnapshot{
		WindowHandle:    "w2:" + strings.Repeat("b", 32),
		publicWindowRef: "w1",
		SnapshotID:      "c1",
		App:             AppDescriptor{Name: "Editor"},
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Editor"},
			{
				Index: 1, RuntimeID: []int{0, 0},
				ControlType: "menu", Name: "Format", Actions: []string{"click"},
				ProviderBusName: ":1.42", ProviderObjectPath: "/format/1",
				ProcessGeneration: "99123",
			},
			{
				Index: 2, RuntimeID: []int{0, 1},
				ControlType: "menu", Name: "Format", Actions: []string{"click"},
				ProviderBusName: ":1.42", ProviderObjectPath: "/format/2",
				ProcessGeneration: "99123",
			},
		},
	}
	tree, err := buildAXSnapshot(snapshot)
	if err != nil {
		t.Fatal(err)
	}
	if got := len(tree.CanonicalRoots[0].Children); got != 2 {
		t.Fatalf("different provider objects collapsed to %d nodes", got)
	}
	for _, node := range tree.CanonicalRoots[0].Children {
		if node.LocatorKind != AXLocatorProviderBound {
			t.Fatalf("ambiguous node kind=%q, want provider_bound", node.LocatorKind)
		}
	}
}

func TestExactAliasStateConflictBecomesUnknown(t *testing.T) {
	node := &AXNode{States: AXStates{Selected: TriTrue}}
	mergeExactProviderAlias(node, &ElementRecord{States: "[unselected]"})
	if node.States.Selected != TriUnknown {
		t.Fatalf("conflicting selected state=%q, want unknown", node.States.Selected)
	}
}

func TestRoleOntologyHasUniqueProviderAliases(t *testing.T) {
	seen := map[string]CanonicalRole{}
	for _, descriptor := range roleOntology {
		for _, alias := range descriptor.ProviderAliases {
			key := strings.ToLower(strings.TrimSpace(alias))
			if previous, exists := seen[key]; exists {
				t.Fatalf(
					"provider alias %q maps to both %q and %q",
					key, previous, descriptor.ModelRole,
				)
			}
			seen[key] = descriptor.ModelRole
		}
	}
	if seen["tree table"] != "treegrid" {
		t.Fatalf("tree table role = %q, want treegrid", seen["tree table"])
	}
}

func TestActionabilityUsesRuntimeRouteInsteadOfActionTypeGuess(t *testing.T) {
	evidence := initialActionabilityEvidence("type", nil, nil)
	readback := true
	focus := true
	editable := true
	actual := &RuntimeActionEvidence{
		Route: "editable_text_api", FocusVerified: &focus,
		EditableVerified: &editable, ReadbackSampled: true,
		ReadbackVerified: &readback,
		InputEmitted:     true,
	}
	final := finalizeActionabilityEvidence(
		evidence,
		AXServiceRequest{Action: axTypeCommand{}},
		ToolCallResult{},
		true,
		actual,
	)
	if final.Route != "editable_text_api" ||
		final.DeliveryVerified != TriTrue ||
		final.FocusVerified != TriTrue ||
		final.EditableVerified != TriTrue {
		t.Fatalf("runtime route was not preserved: %#v", final)
	}
	for _, stage := range final.Stages {
		if stage.Stage == ActionSampleGeometry &&
			stage.Status != ActionStageNotRequired {
			t.Fatalf("semantic text write falsely claimed geometry: %#v", final)
		}
	}
}

func TestBatchEvidenceKeepsEveryOrderedAction(t *testing.T) {
	entries := make([]map[string]any, 0, contract.AXBatchMaxActions)
	for index := 0; index < contract.AXBatchMaxActions; index++ {
		result := ToolCallResult{StructuredContent: map[string]any{
			"actionability": ActionabilityEvidence{
				Route:        "keyboard_synthesis",
				InputEmitted: TriTrue,
			},
		}}
		entries = append(
			entries,
			batchActionEvidenceEntry(index, "keypress", result),
		)
	}
	if len(entries) != contract.AXBatchMaxActions {
		t.Fatalf("batch evidence count=%d", len(entries))
	}
	for index, entry := range entries {
		if entry["action_index"] != index ||
			entry["action_number"] != index+1 ||
			entry["actionability"] == nil {
			t.Fatalf("batch evidence lost action %d: %#v", index, entry)
		}
	}
	payload, err := json.Marshal(entries)
	if err != nil {
		t.Fatal(err)
	}
	if len(payload) >= MaxInlineChars() {
		t.Fatalf(
			"32-action evidence exceeds final text budget: %d bytes",
			len(payload),
		)
	}
}

func TestFailedActionPreservesPartialRuntimeRoute(t *testing.T) {
	evidence := initialActionabilityEvidence("click", nil, nil)
	focus := true
	actual := &RuntimeActionEvidence{
		Route: "coordinate_synthesis", GeometrySampled: true,
		FocusVerified: &focus, InputEmitted: true,
	}
	final := finalizeActionabilityEvidence(
		evidence,
		AXServiceRequest{Action: AXClickCommand{}},
		ToolCallResult{StructuredContent: map[string]any{
			"failure_code":         "post_emit_failure",
			"input_events_emitted": true,
		}},
		false,
		actual,
	)
	if final.Route != "coordinate_synthesis" ||
		final.InputEmitted != TriTrue ||
		final.ReadbackSampled != TriUnknown {
		t.Fatalf("partial failure evidence was lost: %#v", final)
	}
}

func TestProviderBoundDiagnosticSelectorPreservesFallbackSource(t *testing.T) {
	for _, test := range []struct {
		name   string
		record *ElementRecord
		want   string
	}{
		{
			name: "description",
			record: &ElementRecord{
				ControlType: "list item",
				Description: "Open the desktop folder",
			},
			want: `listitem [desc="Open the desktop folder"]`,
		},
		{
			name: "placeholder",
			record: &ElementRecord{
				ControlType: "text",
				Placeholder: "Search files",
			},
			want: `text [placeholder="Search files"]`,
		},
	} {
		t.Run(test.name, func(t *testing.T) {
			role := canonicalRoleDescriptor(
				test.record.ControlType, nil,
			).ModelRole
			if got := semanticDiagnosticSelector(role, test.record); got != test.want {
				t.Fatalf("selector=%q, want %q", got, test.want)
			}
		})
	}
}

func TestStructuredAXTreeReconstructsNearestRetainedAncestor(t *testing.T) {
	snapshot := &AppSnapshot{
		Elements: []ElementRecord{
			{Index: 1, RuntimeID: []int{0}, ControlType: "frame", Name: "Editor"},
			// Provider pruning omitted path 0.2, so this record must attach to 0.
			{Index: 2, RuntimeID: []int{0, 2, 4}, ControlType: "push button", Name: "Save"},
		},
	}
	tree, err := buildAXSnapshot(snapshot)
	if err != nil {
		t.Fatal(err)
	}
	if len(tree.CanonicalRoots) != 1 || len(tree.CanonicalRoots[0].Children) != 1 {
		t.Fatalf("nearest retained ancestor was not reconstructed: %#v", tree.CanonicalRoots)
	}
	if tree.CanonicalRoots[0].Children[0].ProviderIndex != 2 {
		t.Fatalf("wrong child attached: %#v", tree.CanonicalRoots[0].Children[0])
	}
}

func TestModelAXRoleAliasesAreStandardAndProviderIndependent(t *testing.T) {
	for provider, want := range map[string]string{
		"push button": "button",
		"entry":       "textbox",
		"check box":   "checkbox",
		"list item":   "listitem",
		"page tab":    "tab",
	} {
		if got := canonicalRoleDescriptor(provider, nil).ModelRole; string(got) != want {
			t.Fatalf("model role for %q = %q, want %q", provider, got, want)
		}
	}
	if got := canonicalRoleDescriptor("internal frame", nil).ModelRole; got != RoleGeneric {
		t.Fatalf("unknown provider role fabricated canonical role %q", got)
	}
}

func TestA1LikeCellDoesNotReceiveOwnerlessTypedSemanticIdentity(t *testing.T) {
	snapshot := &AppSnapshot{
		App:             AppDescriptor{Name: "Calc"},
		WindowHandle:    "private-window",
		publicAppRef:    "a1",
		publicWindowRef: "w1",
		SnapshotID:      "c1",
		Elements: []ElementRecord{
			{
				Index: 1, RuntimeID: []int{0}, ControlType: "frame",
				Name: "Book.ods - LibreOffice Calc",
			},
			{
				Index: 2, RuntimeID: []int{0, 0}, ControlType: "table cell",
				Name: "C2", ProviderBusName: ":1.42",
				ProviderObjectPath: "/org/a11y/cell/2/3",
				ProcessGeneration:  "99123",
			},
		},
	}
	typed, err := buildAXSnapshot(snapshot)
	if err != nil {
		t.Fatal(err)
	}
	node := typed.ByIndex[2]
	if node == nil {
		t.Fatal("C2 node missing")
	}
	if node.LocatorKind != AXLocatorProviderBound {
		t.Fatalf(
			"ownerless C2 locator=%q, want provider_bound",
			node.LocatorKind,
		)
	}
	if node.SemanticTarget == "" {
		t.Fatal("provider-bound C2 lost its diagnostic semantic selector")
	}
}

func TestAssignSnapshotRefsRecordsAddressabilityReason(t *testing.T) {
	button := &AXNode{
		ID: "button", Role: "button", SemanticClass: "control",
		Name: "Save",
	}
	snapshot := &AXSnapshot{
		SchemaVersion:  "AXSnapshot",
		AppRef:         "a1",
		WindowRef:      "w1",
		CanonicalRoots: []*AXNode{button},
		Roots:          cloneAXForest([]*AXNode{button}, nil),
		ByID:           map[string]*AXNode{"button": button},
	}
	NewRefRegistry(8).AssignSnapshotRefs(snapshot)
	projected := snapshot.Roots[0]
	if projected.Ref != "" || projected.AddressabilityReason == "" {
		t.Fatalf("projected addressability=%#v", projected)
	}
	if button.AddressabilityReason != projected.AddressabilityReason {
		t.Fatalf("canonical reason=%q projected=%q",
			button.AddressabilityReason, projected.AddressabilityReason)
	}
}

func TestProjectedSubtreePreservesOwnerRefsForAddressability(t *testing.T) {
	button := &AXNode{
		ID: "button", Role: "button", SemanticClass: "control",
		Name:                    "Save",
		AddressabilityEvaluated: true,
		AddressabilityReason:    "stable_identity_unavailable",
	}
	typed := &AXSnapshot{
		SchemaVersion:  "AXSnapshot",
		AppRef:         "a7",
		WindowRef:      "w9",
		CanonicalRoots: []*AXNode{button},
		Roots:          cloneAXForest([]*AXNode{button}, nil),
		ByID:           map[string]*AXNode{"button": button},
		ByIndex:        map[int]*AXNode{1: button},
	}
	record := &ElementRecord{Index: 1}
	snapshot := &AppSnapshot{AXSnapshot: typed}
	lines, err := projectedSubtreeLines(snapshot, record)
	if err != nil {
		t.Fatal(err)
	}
	text := strings.Join(lines, "\n")
	if !strings.Contains(text, `button "Save" [addressable=false]`) {
		t.Fatalf("subtree lost owner-scoped addressability:\n%s", text)
	}
}

func TestAXLocatorForRecordBindsA1NamedCellsToTheProviderObject(t *testing.T) {
	cell := &ElementRecord{Name: "C2"}
	for _, tc := range []struct {
		name             string
		role             CanonicalRole
		semanticTarget   string
		providerIdentity string
		wantKind         AXLocatorKind
		wantTarget       string
	}{
		{"semantic button", "button", "sem", "", AXLocatorSemantic, "sem"},
		{"A1 cell with provider object", "cell", "sem", "pid", AXLocatorProviderBound, "sem"},
		{"A1 cell without provider object", "gridcell", "sem", "", "", ""},
		{"no semantic target, provider object", "cell", "", "pid", AXLocatorProviderBound, `cell "C2"`},
		{"no target, no provider object", "cell", "", "", "", ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			kind, target := axLocatorForRecord(cell, tc.role, tc.semanticTarget, tc.providerIdentity)
			if kind != tc.wantKind || target != tc.wantTarget {
				t.Fatalf("axLocatorForRecord = (%q, %q), want (%q, %q)", kind, target, tc.wantKind, tc.wantTarget)
			}
		})
	}
}
