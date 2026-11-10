package core

import (
	"testing"
)

func TestFindCanIssueProviderBoundRefWithoutKnownCapability(t *testing.T) {
	showing := true
	node := &AXNode{
		ID: "0/1", Role: "cell", Name: "C2",
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/cell/2/3",
		ProcessGeneration:  "99123",
		Showing:            &showing,
		Visible:            &showing,
	}
	snapshot := &AXSnapshot{
		AppRef:       "a1",
		AppIdentity:  "calc",
		WindowHandle: "window-1",
		WindowRef:    "w1",
		Revision:     "c1",
		Roots:        []*AXNode{node},
		ByID:         map[string]*AXNode{"0/1": node},
	}
	registry := NewRefRegistry(16)
	registry.AssignSnapshotRefs(snapshot)
	if node.Ref != "" {
		t.Fatalf("passive default node received ref %q", node.Ref)
	}
	node.FindMatch = true
	registry.AssignSnapshotRefs(snapshot)
	if node.Ref == "" {
		t.Fatal("explicit find match with complete provider identity did not receive an on-demand ref")
	}
	entry, err := registry.Resolve(node.Ref, "")
	if err != nil {
		t.Fatal(err)
	}
	if entry.LocatorKind != AXLocatorProviderBound ||
		entry.AppRef != "a1" || entry.LogicalWindowRef != "w1" {
		t.Fatalf("find ref lost provider-bound ownership: %#v", entry)
	}
}

func TestFindPrefersSemanticRefWhenProviderIdentityIsIncomplete(t *testing.T) {
	showing := true
	node := &AXNode{
		ID:             "0/1",
		Role:           "cell",
		Name:           "C2",
		FindMatch:      true,
		LocatorKind:    AXLocatorSemantic,
		SemanticTarget: encodeScopedSemanticRef(`cell "C2"`, "private-window"),
		Showing:        &showing,
		Visible:        &showing,
	}
	snapshot := &AXSnapshot{
		AppRef:       "a1",
		AppIdentity:  "calc",
		WindowHandle: "private-window",
		WindowRef:    "w1",
		Revision:     "c1",
		Roots:        []*AXNode{node},
		ByID:         map[string]*AXNode{"0/1": node},
	}
	registry := NewRefRegistry(16)
	registry.AssignSnapshotRefs(snapshot)
	if node.Ref == "" {
		t.Fatal("unique semantic find match did not receive a ref")
	}
	entry, err := registry.Resolve(node.Ref, "")
	if err != nil {
		t.Fatal(err)
	}
	if entry.LocatorKind != AXLocatorSemantic ||
		entry.AppRef != "a1" || entry.LogicalWindowRef != "w1" {
		t.Fatalf("semantic find ref lost ownership: %#v", entry)
	}
}

func TestFindCanIssueRefsAcrossPassiveCanonicalRoles(t *testing.T) {
	for _, role := range []CanonicalRole{
		"menu", "cell", "gridcell", "tab", "group", "listitem",
	} {
		t.Run(string(role), func(t *testing.T) {
			showing := true
			node := &AXNode{
				ID:                 "0/1",
				Role:               role,
				Name:               "Target",
				FindMatch:          true,
				ProviderBusName:    ":1.42",
				ProviderObjectPath: "/org/a11y/target/" + string(role),
				ProcessGeneration:  "99123",
				Showing:            &showing,
				Visible:            &showing,
			}
			snapshot := &AXSnapshot{
				AppRef:       "a1",
				AppIdentity:  "app",
				WindowHandle: "private-window",
				WindowRef:    "w1",
				Revision:     "c1",
				Roots:        []*AXNode{node},
				ByID:         map[string]*AXNode{"0/1": node},
			}
			registry := NewRefRegistry(16)
			registry.AssignSnapshotRefs(snapshot)
			if node.Ref == "" {
				t.Fatalf("find match role %q did not receive a ref", role)
			}
			entry, err := registry.Resolve(node.Ref, "")
			if err != nil {
				t.Fatal(err)
			}
			if entry.LocatorKind != AXLocatorProviderBound {
				t.Fatalf("find match role %q locator=%q, want provider_bound",
					role, entry.LocatorKind)
			}
		})
	}
}

func TestFindAddressabilityReportsUnavailableStableIdentity(t *testing.T) {
	node := &AXNode{
		ID:        "0/1",
		Role:      "cell",
		Name:      "C2",
		FindMatch: true,
	}
	snapshot := &AXSnapshot{
		AppRef:       "a1",
		AppIdentity:  "calc",
		WindowHandle: "private-window",
		WindowRef:    "w1",
		Revision:     "c1",
	}
	eligible, reason, _ := axNodeRefEligibility(snapshot, node)
	if eligible || reason != "stable_identity_unavailable" {
		t.Fatalf("eligibility=%v reason=%q, want stable_identity_unavailable",
			eligible, reason)
	}
}

func TestDefaultObserveCanIssueRefFromGeometryAndPointerEvidence(t *testing.T) {
	showing := true
	node := &AXNode{
		ID:             "0/1",
		Role:           "cell",
		Name:           "C2",
		LocatorKind:    AXLocatorSemantic,
		SemanticTarget: encodeScopedSemanticRef(`cell "C2"`, "private-window"),
		Showing:        &showing,
		Visible:        &showing,
		SourceRecord: &ElementRecord{
			Frame:  &frame{X: 40, Y: 50, Width: 80, Height: 24},
			States: "[has-click-action]",
		},
	}
	snapshot := &AXSnapshot{
		AppRef:       "a1",
		AppIdentity:  "calc",
		WindowHandle: "private-window",
		WindowRef:    "w1",
		Revision:     "c1",
		Roots:        []*AXNode{node},
		ByID:         map[string]*AXNode{"0/1": node},
	}
	registry := NewRefRegistry(16)
	registry.AssignSnapshotRefs(snapshot)
	if node.Ref == "" {
		t.Fatal("visible target with stable identity, geometry, and pointer evidence did not receive a ref")
	}
}

func TestDefaultObserveSeparatesControlIdentityFromActionability(t *testing.T) {
	showing := true
	node := &AXNode{
		ID:             "0/1",
		Role:           "button",
		SemanticClass:  "control",
		Name:           "Save",
		LocatorKind:    AXLocatorSemantic,
		SemanticTarget: encodeScopedSemanticRef(`button "Save"`, "private-window"),
		Showing:        &showing,
		Visible:        &showing,
	}
	snapshot := &AXSnapshot{
		AppRef:       "a1",
		AppIdentity:  "app",
		WindowHandle: "private-window",
		WindowRef:    "w1",
		Revision:     "c1",
		Roots:        []*AXNode{node},
		ByID:         map[string]*AXNode{"0/1": node},
	}
	NewRefRegistry(16).AssignSnapshotRefs(snapshot)
	if node.Ref == "" {
		t.Fatal("stable visible control identity received no ref")
	}
	if axNodeHasCapability(node) {
		t.Fatal("test control unexpectedly has an executable capability")
	}
}

func TestDefaultObserveCanIssueRefToVisibleSelectableItem(t *testing.T) {
	showing := true
	node := &AXNode{
		ID:             "0/1",
		Role:           "treeitem",
		SemanticClass:  "item",
		Name:           "Inbox",
		LocatorKind:    AXLocatorSemantic,
		SemanticTarget: encodeScopedSemanticRef(`treeitem "Inbox"`, "private-window"),
		Showing:        &showing,
		Visible:        &showing,
		SourceRecord: &ElementRecord{
			Frame: &frame{X: 40, Y: 50, Width: 80, Height: 24},
		},
	}
	snapshot := &AXSnapshot{
		AppRef:       "a1",
		AppIdentity:  "app",
		WindowHandle: "private-window",
		WindowRef:    "w1",
		Revision:     "c1",
		Roots:        []*AXNode{node},
		ByID:         map[string]*AXNode{"0/1": node},
	}
	NewRefRegistry(16).AssignSnapshotRefs(snapshot)
	if node.Ref == "" {
		t.Fatal("stable visible selectable item received no ref")
	}
}

func TestDefaultObserveDoesNotInferActionabilityFromRoleAndGeometry(t *testing.T) {
	showing := true
	node := &AXNode{
		ID:             "0/1",
		Role:           "group",
		SemanticClass:  "structure",
		Name:           "Static Group",
		LocatorKind:    AXLocatorSemantic,
		SemanticTarget: encodeScopedSemanticRef(`group "Static Group"`, "private-window"),
		Showing:        &showing,
		Visible:        &showing,
		SourceRecord: &ElementRecord{
			Frame: &frame{X: 40, Y: 50, Width: 80, Height: 24},
		},
	}
	snapshot := &AXSnapshot{
		AppRef:       "a1",
		AppIdentity:  "app",
		WindowHandle: "private-window",
		WindowRef:    "w1",
		Revision:     "c1",
		Roots:        []*AXNode{node},
		ByID:         map[string]*AXNode{"0/1": node},
	}
	registry := NewRefRegistry(16)
	registry.AssignSnapshotRefs(snapshot)
	if node.Ref != "" {
		t.Fatalf("role and geometry alone incorrectly implied actionability: %q", node.Ref)
	}
}

func TestExpandableProviderOmissionBlocksSemanticRefIssuance(t *testing.T) {
	showing := true
	node := &AXNode{
		ID: "0/0", Role: "button", Name: "Save",
		SemanticClass:      "control",
		LocatorKind:        AXLocatorSemantic,
		SemanticTarget:     encodeScopedSemanticRef(`button "Save"`, "window-1"),
		RuntimeID:          []int{0, 0},
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/save",
		ProcessGeneration:  "99123",
		Showing:            &showing,
		Visible:            &showing,
	}
	snapshot := &AXSnapshot{
		AppRef: "a1", AppIdentity: "editor",
		WindowHandle: "window-1", WindowRef: "w1", Revision: "c1",
		IdentityCoverageIncomplete: true,
		Roots:                      []*AXNode{node},
		ByID:                       map[string]*AXNode{"0/0": node},
	}
	registry := NewRefRegistry(16)
	registry.AssignSnapshotRefs(snapshot)
	if node.Ref == "" {
		t.Fatal("complete provider identity should permit a provider-bound ref")
	}
	entry, err := registry.Resolve(node.Ref, "")
	if err != nil {
		t.Fatal(err)
	}
	if entry.LocatorKind != AXLocatorProviderBound {
		t.Fatalf("locator=%q want provider_bound", entry.LocatorKind)
	}
}

func TestFindAddressabilityExplainsHiddenTarget(t *testing.T) {
	hidden := false
	collapsed := AXNode{
		ID: "0", Role: "group", Name: "Collapsed",
		States: AXStates{Expanded: TriFalse},
	}
	node := &AXNode{
		ID: "0/1", Role: "button", Name: "Hidden",
		SemanticTarget:     `button "Hidden"`,
		Capabilities:       AXCapabilities{Activate: ActivationSemantic},
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/hidden",
		ProcessGeneration:  "99123",
		Showing:            &hidden,
		Visible:            &hidden,
		Parent:             &collapsed,
		FindMatch:          true,
	}
	collapsed.Children = []*AXNode{node}
	snapshot := &AXSnapshot{
		AppRef:       "a1",
		AppIdentity:  "editor",
		WindowHandle: "window-1",
		WindowRef:    "w1",
		Revision:     "c1",
		Roots:        []*AXNode{&collapsed},
		ByID:         map[string]*AXNode{"0": &collapsed, "0/1": node},
	}
	registry := NewRefRegistry(16)
	registry.AssignSnapshotRefs(snapshot)
	if node.Ref != "" {
		t.Fatalf("collapsed hidden find target received ref %q", node.Ref)
	}
	eligible, reason, _ := axNodeRefEligibility(snapshot, node)
	if eligible || reason != "hidden_under_collapsed_ancestor" {
		t.Fatalf("eligibility=%v reason=%q, want hidden_under_collapsed_ancestor",
			eligible, reason)
	}
}

func TestDefaultObserveIssuesRefToUnnamedEditorWithProviderIdentity(t *testing.T) {
	// GTK and Qt document editors (gedit, Bluefish, Kate) are unnamed text
	// areas. They are the main target of their window; without a ref the
	// model saw the text but could only type through the focused window.
	showing := true
	editor := &AXNode{
		ID:                 "0/4",
		Role:               "textbox",
		SemanticClass:      "control",
		Capabilities:       AXCapabilities{EditText: true},
		LocatorKind:        AXLocatorProviderBound,
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/atspi/accessible/77",
		ProcessGeneration:  "4242:1",
		Showing:            &showing,
		Visible:            &showing,
		SourceRecord: &ElementRecord{
			Frame: &frame{X: 0, Y: 70, Width: 900, Height: 670},
		},
	}
	unnamedButton := &AXNode{
		ID:                 "0/5",
		Role:               "button",
		SemanticClass:      "control",
		Capabilities:       AXCapabilities{Activate: ActivationSemantic},
		LocatorKind:        AXLocatorProviderBound,
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/atspi/accessible/78",
		ProcessGeneration:  "4242:1",
		Showing:            &showing,
		Visible:            &showing,
	}
	snapshot := &AXSnapshot{
		AppRef:       "a1",
		AppIdentity:  "gedit",
		WindowHandle: "private-window",
		WindowRef:    "w1",
		Revision:     "c1",
		Roots:        []*AXNode{editor, unnamedButton},
		ByID:         map[string]*AXNode{"0/4": editor, "0/5": unnamedButton},
	}
	NewRefRegistry(16).AssignSnapshotRefs(snapshot)
	if editor.Ref == "" {
		t.Fatalf("unnamed editor with provider identity got no ref (reason %q)", editor.AddressabilityReason)
	}
	if unnamedButton.Ref != "" {
		t.Fatalf("unnamed button without a description must stay unaddressed, got %q", unnamedButton.Ref)
	}
}
