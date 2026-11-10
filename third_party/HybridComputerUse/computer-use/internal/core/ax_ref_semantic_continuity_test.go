package core

import (
	"slices"
	"strings"
	"testing"
)

func TestSemanticRefSurvivesProviderObjectRebuildInSameLogicalWindow(t *testing.T) {
	target := encodeScopedSemanticRef(`cell "C2"`, "private-window")
	registry := NewRefRegistry(16)
	first := registry.AcquireTyped(
		AXLocatorSemantic,
		"calc",
		"private-window",
		target,
		"cell",
		"C2",
		"c1",
		":1.42",
		"/org/a11y/cell/old",
		"99123",
		"",
		"",
		"",
		"",
		"",
		"a1",
		"w1",
	)
	second := registry.AcquireTyped(
		AXLocatorSemantic,
		"calc",
		"private-window",
		target,
		"cell",
		"C2",
		"c2",
		":1.42",
		"/org/a11y/cell/rebuilt",
		"99123",
		"",
		"",
		"",
		"",
		"",
		"a1",
		"w1",
	)
	if first == "" || second != first {
		t.Fatalf("provider rebuild changed semantic ref: %q -> %q", first, second)
	}
	entry, err := registry.Resolve(first, "")
	if err != nil {
		t.Fatalf("semantic ref became stale: %v", err)
	}
	if entry.ProviderObjectPath != "/org/a11y/cell/old" {
		t.Fatalf(
			"unverified provider binding changed to %q",
			entry.ProviderObjectPath,
		)
	}
}

func TestSemanticRefKeepsLogicalIdentityUntilLiveRebind(t *testing.T) {
	target := encodeScopedSemanticRef(`button "OK"`, "private-window")
	registry := NewRefRegistry(16)
	first := registry.AcquireTyped(
		AXLocatorSemantic,
		"editor",
		"private-window",
		target,
		"button",
		"OK",
		"c1",
		":1.42",
		"/org/a11y/dialog/old-ok",
		"99123",
		"",
		"",
		"",
		"",
		"",
		"a1",
		"w1",
	)
	second := registry.AcquireTyped(
		AXLocatorSemantic,
		"editor",
		"private-window",
		target,
		"button",
		"OK",
		"c2",
		":1.42",
		"/org/a11y/dialog/new-ok",
		"99123",
		"",
		"",
		"",
		"",
		"",
		"a1",
		"w1",
	)
	if first == "" || second != first {
		t.Fatalf("same logical target changed ref: %q -> %q", first, second)
	}
	entry, err := registry.Resolve(first, "")
	if err != nil {
		t.Fatalf("semantic ref became stale: %v", err)
	}
	if entry.ProviderObjectPath != "/org/a11y/dialog/old-ok" {
		t.Fatalf(
			"unverified provider binding changed to %q",
			entry.ProviderObjectPath,
		)
	}
}

func TestSemanticRefReusesLogicalIdentityAcrossSamePathProviderRebuild(t *testing.T) {
	showing := true
	oldOwner := &AXNode{
		ID: "0", Role: "dialog", Name: "Question", RuntimeID: []int{0},
	}
	oldNode := &AXNode{
		ID: "0/0", Role: "button", Name: "OK",
		SemanticClass:      "control",
		LocatorKind:        AXLocatorSemantic,
		SemanticTarget:     encodeScopedSemanticRef(`button "OK"`, "window-1"),
		RuntimeID:          []int{0, 0},
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/old-ok",
		ProcessGeneration:  "99123",
		Showing:            &showing,
		Visible:            &showing,
		Parent:             oldOwner,
	}
	oldOwner.Children = []*AXNode{oldNode}
	firstSnapshot := &AXSnapshot{
		AppRef:       "a1",
		AppIdentity:  "editor",
		WindowHandle: "window-1",
		WindowRef:    "w1",
		Revision:     "c1",
		Roots:        []*AXNode{oldOwner},
	}
	registry := NewRefRegistry(16)
	first := registry.AcquireNode(
		firstSnapshot, oldNode, AXLocatorSemantic,
	)
	if first == "" {
		t.Fatal("initial semantic ref was not issued")
	}

	newOwner := &AXNode{
		ID: "0", Role: "dialog", Name: "Question", RuntimeID: []int{0},
	}
	newNode := &AXNode{
		ID: "0/0", Role: "button", Name: "OK",
		LocatorKind:        AXLocatorSemantic,
		SemanticTarget:     oldNode.SemanticTarget,
		RuntimeID:          []int{0, 0},
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/rebuilt-ok",
		ProcessGeneration:  "99123",
		Showing:            &showing,
		Visible:            &showing,
		Parent:             newOwner,
	}
	newOwner.Children = []*AXNode{newNode}
	secondSnapshot := &AXSnapshot{
		AppRef:       "a1",
		AppIdentity:  "editor",
		WindowHandle: "window-1",
		WindowRef:    "w1",
		Revision:     "c2",
		Roots:        []*AXNode{newOwner},
	}
	second := registry.AcquireNode(
		secondSnapshot, newNode, AXLocatorSemantic,
	)
	if second != first {
		t.Fatalf("same logical target changed ref: %q -> %q", first, second)
	}
	entry, err := registry.Resolve(first, "")
	if err != nil {
		t.Fatalf("semantic ref became stale: %v", err)
	}
	if entry.ProviderObjectPath != "/org/a11y/old-ok" {
		t.Fatalf(
			"unverified provider binding changed to %q",
			entry.ProviderObjectPath,
		)
	}
}

func TestCompleteSnapshotReplacesSemanticRefOutsideRuntimeLineage(t *testing.T) {
	showing := true
	owner := &AXNode{
		ID: "0", Role: "dialog", Name: "Question", RuntimeID: []int{0},
	}
	oldNode := &AXNode{
		ID: "0/0", Role: "button", Name: "OK",
		SemanticClass:      "control",
		LocatorKind:        AXLocatorSemantic,
		SemanticTarget:     encodeScopedSemanticRef(`button "OK"`, "window-1"),
		RuntimeID:          []int{0, 0},
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/old-ok",
		ProcessGeneration:  "99123",
		Showing:            &showing,
		Visible:            &showing,
		Parent:             owner,
	}
	owner.Children = []*AXNode{oldNode}
	registry := NewRefRegistry(16)
	first := registry.AcquireNode(&AXSnapshot{
		AppRef: "a1", AppIdentity: "editor",
		WindowHandle: "window-1", WindowRef: "w1", Revision: "c1",
		Roots: []*AXNode{owner},
	}, oldNode, AXLocatorSemantic)

	replacementOwner := &AXNode{
		ID: "0", Role: "dialog", Name: "Question", RuntimeID: []int{0},
	}
	replacement := &AXNode{
		ID: "0/1", Role: "button", Name: "OK",
		LocatorKind:        AXLocatorSemantic,
		SemanticTarget:     oldNode.SemanticTarget,
		RuntimeID:          []int{0, 1},
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/new-ok",
		ProcessGeneration:  "99123",
		Showing:            &showing,
		Visible:            &showing,
		Parent:             replacementOwner,
	}
	replacementOwner.Children = []*AXNode{replacement}
	second := registry.AcquireNode(&AXSnapshot{
		AppRef: "a1", AppIdentity: "editor",
		WindowHandle: "window-1", WindowRef: "w1", Revision: "c2",
		Roots: []*AXNode{replacementOwner},
	}, replacement, AXLocatorSemantic)
	if second == first || second == "" {
		t.Fatalf("replacement provider lineage reused stale ref: %q -> %q", first, second)
	}
	if _, err := registry.Resolve(first, ""); err == nil ||
		!strings.Contains(err.Error(), "superseded") {
		t.Fatalf("old ref was not superseded: %v", err)
	}
	entry, err := registry.Resolve(second, "")
	if err != nil {
		t.Fatalf("fresh replacement ref is not actionable: %v", err)
	}
	if !slices.Equal(entry.RuntimeID, []int{0, 1}) {
		t.Fatalf("fresh runtime path=%v, want [0 1]", entry.RuntimeID)
	}
}

func TestIncompleteSnapshotDoesNotReplaceSemanticRefOutsideRuntimeLineage(t *testing.T) {
	showing := true
	oldNode := &AXNode{
		ID: "0/0", Role: "button", Name: "OK",
		LocatorKind:        AXLocatorSemantic,
		SemanticTarget:     encodeScopedSemanticRef(`button "OK"`, "window-1"),
		RuntimeID:          []int{0, 0},
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/old-ok",
		ProcessGeneration:  "99123",
		Showing:            &showing,
		Visible:            &showing,
	}
	registry := NewRefRegistry(16)
	first := registry.AcquireNode(&AXSnapshot{
		AppRef: "a1", AppIdentity: "editor",
		WindowHandle: "window-1", WindowRef: "w1", Revision: "c1",
	}, oldNode, AXLocatorSemantic)
	replacement := &AXNode{
		ID: "0/1", Role: "button", Name: "OK",
		LocatorKind:        AXLocatorSemantic,
		SemanticTarget:     oldNode.SemanticTarget,
		RuntimeID:          []int{0, 1},
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/new-ok",
		ProcessGeneration:  "99123",
		Showing:            &showing,
		Visible:            &showing,
	}
	second := registry.AcquireNode(&AXSnapshot{
		AppRef: "a1", AppIdentity: "editor",
		WindowHandle: "window-1", WindowRef: "w1", Revision: "c2",
		AcquisitionIncomplete:      true,
		IdentityCoverageIncomplete: true,
	}, replacement, AXLocatorSemantic)
	if second == first || second == "" {
		t.Fatalf("partial snapshot reused semantic ref: %q -> %q", first, second)
	}
	entry, err := registry.Resolve(second, "")
	if err != nil {
		t.Fatalf("provider-bound replacement ref is not usable: %v", err)
	}
	if entry.LocatorKind != AXLocatorProviderBound {
		t.Fatalf("partial snapshot locator=%q, want provider_bound", entry.LocatorKind)
	}
	oldEntry, err := registry.Resolve(first, "")
	if err != nil {
		t.Fatalf("partial evidence invalidated old ref: %v", err)
	}
	if oldEntry.LastSeenRevision != "c1" {
		t.Fatalf("partial evidence refreshed old ref to %q", oldEntry.LastSeenRevision)
	}
}

func TestCompleteInventoryTombstonesAbsentSemanticRef(t *testing.T) {
	showing := true
	oldNode := &AXNode{
		ID: "0/0", Role: "button", Name: "OK",
		SemanticClass:      "control",
		LocatorKind:        AXLocatorSemantic,
		SemanticTarget:     encodeScopedSemanticRef(`button "OK"`, "window-1"),
		RuntimeID:          []int{0, 0},
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/old-ok",
		ProcessGeneration:  "99123",
		Showing:            &showing,
		Visible:            &showing,
	}
	registry := NewRefRegistry(16)
	firstSnapshot := &AXSnapshot{
		AppRef: "a1", AppIdentity: "editor",
		WindowHandle: "window-1", WindowRef: "w1", Revision: "c1",
		RefInventoryComplete: true,
		Roots:                []*AXNode{oldNode},
		ByID:                 map[string]*AXNode{"0/0": oldNode},
	}
	registry.AssignSnapshotRefs(firstSnapshot)
	first := oldNode.Ref
	if first == "" {
		t.Fatal("initial semantic ref was not issued")
	}

	registry.AssignSnapshotRefs(&AXSnapshot{
		AppRef: "a1", AppIdentity: "editor",
		WindowHandle: "window-1", WindowRef: "w1", Revision: "c2",
		RefInventoryComplete: true,
		Roots:                []*AXNode{},
		ByID:                 map[string]*AXNode{},
	})
	if _, err := registry.Resolve(first, ""); err == nil ||
		!strings.Contains(err.Error(), "superseded") {
		t.Fatalf("absent ref was not tombstoned: %v", err)
	}

	newNode := *oldNode
	newNode.Ref = ""
	newNode.ProviderObjectPath = "/org/a11y/later-ok"
	thirdSnapshot := &AXSnapshot{
		AppRef: "a1", AppIdentity: "editor",
		WindowHandle: "window-1", WindowRef: "w1", Revision: "c3",
		RefInventoryComplete: true,
		Roots:                []*AXNode{&newNode},
		ByID:                 map[string]*AXNode{"0/0": &newNode},
	}
	registry.AssignSnapshotRefs(thirdSnapshot)
	if newNode.Ref == "" || newNode.Ref == first {
		t.Fatalf("later same-name object revived old ref: %q -> %q",
			first, newNode.Ref)
	}
}

func TestSemanticProviderRefreshRejectsDifferentRuntimeLineage(t *testing.T) {
	registry := NewRefRegistry(16)
	target := encodeScopedSemanticRef(`button "OK"`, "window-1")
	ref := registry.AcquireTyped(
		AXLocatorSemantic,
		"editor",
		"window-1",
		target,
		"button",
		"OK",
		"c1",
		":1.42",
		"/org/a11y/old-ok",
		"99123",
		"",
		"",
		"",
		"",
		"",
		"a1",
		"w1",
	)
	registry.BindRuntimePaths(ref, []int{0, 0}, [][]int{{0, 0}})
	record := &ElementRecord{
		ControlType:        "push button",
		Name:               "OK",
		RuntimeID:          []int{0, 1},
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/new-ok",
		ProcessGeneration:  "99123",
	}
	if registry.RefreshProviderBinding(ref, record, true) {
		t.Fatal("semantic ref rebound outside its original runtime lineage")
	}
}
