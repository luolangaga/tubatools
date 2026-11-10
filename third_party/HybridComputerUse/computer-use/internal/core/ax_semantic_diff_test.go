package core

import "testing"

func TestWeakDuplicateCohortDoesNotInventObjectContinuity(t *testing.T) {
	beforeParent := &AXNode{
		ID: "list-before", Role: "list", Name: "Options",
		SemanticFingerprint: "f1:list",
	}
	afterParent := &AXNode{
		ID: "list-after", Role: "list", Name: "Options",
		SemanticFingerprint: "f1:list",
	}
	makeOption := func(id, value string, parent *AXNode) *AXNode {
		return &AXNode{
			ID: id, Role: "textbox", Name: "Option",
			Properties:          AXProperties{Value: value},
			SemanticFingerprint: "f1:duplicate-option",
			Parent:              parent,
		}
	}
	beforeParent.Children = []*AXNode{
		makeOption("before-a", "A", beforeParent),
		makeOption("before-b", "B", beforeParent),
	}
	afterParent.Children = []*AXNode{
		makeOption("after-b", "B", afterParent),
		makeOption("after-c", "C", afterParent),
	}

	changes := diffAXSnapshots(
		&AXSnapshot{CanonicalRoots: []*AXNode{beforeParent}},
		&AXSnapshot{CanonicalRoots: []*AXNode{afterParent}},
	)
	added, removed, propertyChanged := 0, 0, 0
	for _, change := range changes {
		switch change.Kind {
		case AXDiffAdded:
			added++
		case AXDiffRemoved:
			removed++
		case AXDiffPropertyChanged, AXDiffStateChanged, AXDiffChanged:
			if change.After != nil && change.After.Name == "Option" {
				propertyChanged++
			}
		}
	}
	if added != 1 || removed != 1 || propertyChanged != 0 {
		t.Fatalf(
			"weak duplicate continuity was invented: added=%d removed=%d changed=%d changes=%#v",
			added,
			removed,
			propertyChanged,
			changes,
		)
	}
}

func TestWeakDuplicateContainersRequireMatchingSubtrees(t *testing.T) {
	beforeParent := &AXNode{
		ID: "before-root", Role: "list", Name: "Options",
		SemanticFingerprint: "f1:list",
	}
	afterParent := &AXNode{
		ID: "after-root", Role: "list", Name: "Options",
		SemanticFingerprint: "f1:list",
	}
	makeGroup := func(id, childName string, parent *AXNode) *AXNode {
		group := &AXNode{
			ID: id, Role: "group", Name: "Option",
			SemanticFingerprint: "f1:duplicate-group",
			Parent:              parent,
		}
		group.Children = []*AXNode{{
			ID: id + "-child", Role: "text", Name: childName,
			SemanticFingerprint: "f1:text:" + childName,
			Parent:              group,
		}}
		return group
	}
	beforeParent.Children = []*AXNode{
		makeGroup("before-a", "A", beforeParent),
		makeGroup("before-b", "B", beforeParent),
	}
	afterParent.Children = []*AXNode{
		makeGroup("after-b", "B", afterParent),
		makeGroup("after-c", "C", afterParent),
	}
	changes := diffAXSnapshots(
		&AXSnapshot{CanonicalRoots: []*AXNode{beforeParent}},
		&AXSnapshot{CanonicalRoots: []*AXNode{afterParent}},
	)
	added, removed := 0, 0
	for _, change := range changes {
		if change.After != nil && change.After.Role == "group" &&
			change.Kind == AXDiffAdded {
			added++
		}
		if change.Before != nil && change.Before.Role == "group" &&
			change.Kind == AXDiffRemoved {
			removed++
		}
	}
	if added != 1 || removed != 1 {
		t.Fatalf(
			"weak duplicate containers invented continuity: added=%d removed=%d changes=%#v",
			added,
			removed,
			changes,
		)
	}
}

func TestWeakDuplicateContainersUseDescendantStrongIdentity(t *testing.T) {
	beforeParent := &AXNode{
		ID: "before-root", Role: "list", Name: "Rows",
		SemanticFingerprint: "f1:list",
	}
	afterParent := &AXNode{
		ID: "after-root", Role: "list", Name: "Rows",
		SemanticFingerprint: "f1:list",
	}
	makeGroup := func(id, childRef string, parent *AXNode) *AXNode {
		group := &AXNode{
			ID: id, Role: "group", Name: "Row",
			SemanticFingerprint: "f1:duplicate-row",
			Parent:              parent,
		}
		group.Children = []*AXNode{{
			ID: id + "-child", Role: "button", Name: "Delete", Ref: childRef,
			SemanticFingerprint: "f1:delete",
			Parent:              group,
		}}
		return group
	}
	beforeParent.Children = []*AXNode{
		makeGroup("before-r2", "r2", beforeParent),
		makeGroup("before-r3", "r3", beforeParent),
	}
	afterParent.Children = []*AXNode{
		makeGroup("after-r3", "r3", afterParent),
		makeGroup("after-r2", "r2", afterParent),
	}
	changes := diffAXSnapshots(
		&AXSnapshot{CanonicalRoots: []*AXNode{beforeParent}},
		&AXSnapshot{CanonicalRoots: []*AXNode{afterParent}},
	)
	moved, added, removed := 0, 0, 0
	for _, change := range changes {
		switch change.Kind {
		case AXDiffMoved:
			moved++
		case AXDiffAdded:
			added++
		case AXDiffRemoved:
			removed++
		}
	}
	if moved == 0 || added != 0 || removed != 0 {
		t.Fatalf(
			"descendant strong identity was not preserved across reorder: moved=%d added=%d removed=%d changes=%#v",
			moved,
			added,
			removed,
			changes,
		)
	}
}

func TestIncrementalAXSnapshotRequiresLogicalWindowIdentity(t *testing.T) {
	beforeNode := &AXNode{
		ID: "save", Role: "button", Name: "Save", Ref: "r1",
		SemanticFingerprint: "f1:save",
	}
	afterNode := &AXNode{
		ID: "delete", Role: "button", Name: "Delete", Ref: "r2",
		SemanticFingerprint: "f1:delete",
	}
	before := &AppSnapshot{
		App: AppDescriptor{Name: "Editor", PID: 7},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{beforeNode},
			Roots:          []*AXNode{beforeNode},
		},
	}
	after := &AppSnapshot{
		App: AppDescriptor{Name: "Editor", PID: 7},
		AXSnapshot: &AXSnapshot{
			CanonicalRoots: []*AXNode{afterNode},
			Roots:          []*AXNode{afterNode},
		},
	}
	if diff, ok := incrementalAXSnapshot(before, after); ok {
		t.Fatalf(
			"snapshots without a reliable logical window ref must not be diffed: %#v",
			diff,
		)
	}

	before.publicWindowRef = "w1"
	after.publicWindowRef = "w2"
	if diff, ok := incrementalAXSnapshot(before, after); ok {
		t.Fatalf("different logical windows must not be diffed: %#v", diff)
	}
}

func TestSemanticDuplicatesAreMatchedOnlyWithinTheirParent(t *testing.T) {
	beforeLeft := &AXNode{
		ID: "left", Role: "group", Name: "Left",
		SemanticFingerprint: "f1:left",
	}
	beforeRight := &AXNode{
		ID: "right", Role: "group", Name: "Right",
		SemanticFingerprint: "f1:right",
	}
	oldItem := &AXNode{
		ID: "old-item", Role: "menuitem", Name: "Format",
		SemanticFingerprint: "f1:format",
		Parent:              beforeLeft,
	}
	beforeLeft.Children = []*AXNode{oldItem}
	afterLeft := *beforeLeft
	afterLeft.Children = nil
	afterRight := *beforeRight
	newItem := *oldItem
	newItem.ID = "new-item"
	newItem.Parent = &afterRight
	afterRight.Children = []*AXNode{&newItem}
	changes := diffAXSnapshots(
		&AXSnapshot{CanonicalRoots: []*AXNode{beforeLeft, beforeRight}},
		&AXSnapshot{CanonicalRoots: []*AXNode{&afterLeft, &afterRight}},
	)
	var added, removed int
	for _, change := range changes {
		switch change.Kind {
		case AXDiffAdded:
			added++
		case AXDiffRemoved:
			removed++
		}
	}
	if added != 1 || removed != 1 {
		t.Fatalf("cross-parent semantic duplicates were paired: %#v", changes)
	}
}
