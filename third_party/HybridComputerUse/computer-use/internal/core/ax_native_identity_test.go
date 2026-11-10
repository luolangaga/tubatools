package core

import (
	"encoding/json"
	"fmt"
	"reflect"
	"strings"
	"testing"
)

func nativeIdentityFixture() (*AXSnapshot, *AXNode) {
	window := "w2:" + strings.Repeat("a", 32)
	node := &AXNode{
		ID: "save", Role: "button", ProviderRole: "button", Name: "Save",
		SemanticTarget: encodeScopedSemanticRef(`button "Save"`, window),
		LocatorKind:    AXLocatorProviderBound, FindMatch: true,
		ProviderBusName: "backend:42", ProviderObjectPath: "retained-object",
		ProcessGeneration: "epoch1", ProviderIdentityScope: "process_instance",
		ProviderWindowBusName: "backend:42", ProviderWindowObjectPath: "window-one",
		ProviderWindowProcessGeneration: "epoch1", AutomationID: "save",
		RuntimeID: []int{0, 1}, RuntimeIDAliases: [][]int{{0, 1}, {1, 1}},
	}
	return &AXSnapshot{
		AppIdentity: "fixture", AppRef: "a1", WindowRef: "w1", WindowHandle: window,
		Revision: "c1", RefInventoryComplete: true,
		Roots: []*AXNode{node}, ByID: map[string]*AXNode{node.ID: node},
		ByIndex: map[int]*AXNode{1: node},
	}, node
}

func nativeIdentityRecord(node *AXNode) *ElementRecord {
	return &ElementRecord{
		Index: 1, RuntimeID: append([]int(nil), node.RuntimeID...),
		ControlType: node.ProviderRole, Name: node.Name, AutomationID: node.AutomationID,
		ProviderBusName: node.ProviderBusName, ProviderObjectPath: node.ProviderObjectPath,
		ProcessGeneration: node.ProcessGeneration, ProviderIdentityScope: node.ProviderIdentityScope,
		ProviderWindowBusName:           node.ProviderWindowBusName,
		ProviderWindowObjectPath:        node.ProviderWindowObjectPath,
		ProviderWindowProcessGeneration: node.ProviderWindowProcessGeneration,
	}
}

func nativeIdentityAcquire(r *RefRegistry, snapshot *AXSnapshot, node *AXNode, mode string) string {
	switch mode {
	case "typed":
		return r.AcquireTyped(node.LocatorKind, snapshot.AppIdentity, snapshot.WindowHandle,
			node.SemanticTarget, node.Role, node.Name, snapshot.Revision,
			node.ProviderBusName, node.ProviderObjectPath, node.ProcessGeneration,
			node.ProviderWindowBusName, node.ProviderWindowObjectPath,
			node.ProviderWindowProcessGeneration, "", "", snapshot.AppRef, snapshot.WindowRef,
			node.ProviderIdentityScope, node.AutomationID, node.ProviderRole)
	case "node":
		return r.AcquireNode(snapshot, node, node.LocatorKind)
	default:
		r.AssignSnapshotRefs(snapshot)
		return node.Ref
	}
}

func TestNativeIdentityRetainedProjection(t *testing.T) {
	for _, mode := range []string{"typed", "node", "snapshot"} {
		for _, id := range []string{"", "save"} {
			t.Run(mode+"/"+id, func(t *testing.T) {
				r := NewRefRegistry(32)
				snapshot, node := nativeIdentityFixture()
				node.AutomationID = id
				ref := nativeIdentityAcquire(r, snapshot, node, mode)
				if ref == "" {
					t.Fatal("no initial ref")
				}
				r.BindRuntimePaths(ref, node.RuntimeID, node.RuntimeIDAliases)
				for step := 2; step < 5; step++ {
					node.RuntimeID, node.RuntimeIDAliases = []int{0, step}, [][]int{{0, step}, {1, step}}
					node.SemanticTarget = fmt.Sprintf("diagnostic projection %d", step)
					snapshot.Revision = fmt.Sprintf("c%d", step)
					if next := nativeIdentityAcquire(r, snapshot, node, mode); next != ref {
						t.Fatalf("same retained object changed ref: %s -> %s", ref, next)
					}
					r.AssignSnapshotRefs(snapshot)
					entry, err := r.Resolve(ref, "")
					if err != nil || node.Ref != ref {
						t.Fatalf("find lost retained ref: %v", err)
					}
					if !reflect.DeepEqual(entry.RuntimeIDAliases, node.RuntimeIDAliases) || runtimePathMatchesEntry(entry, []int{0, step - 1}) {
						t.Fatal("stale runtime path survived projection refresh")
					}
					target, err := semanticTargetFromRefEntry(entry)
					if err != nil {
						t.Fatal(err)
					}
					target.AllowProviderRebind = true
					NewService().authorizeProviderRebind("fixture", entry, target)
					if target.AllowProviderRebind {
						t.Fatal("retained token permitted semantic rebind")
					}
				}
			})
		}
	}
}

func TestNativeIdentityProjectionRejectsContradictions(t *testing.T) {
	changes := map[string]func(*AXSnapshot, *AXNode){
		"scope":         func(_ *AXSnapshot, n *AXNode) { n.ProviderIdentityScope = "process" },
		"token":         func(_ *AXSnapshot, n *AXNode) { n.ProviderObjectPath = "replacement" },
		"epoch":         func(_ *AXSnapshot, n *AXNode) { n.ProcessGeneration = "epoch2" },
		"bus":           func(_ *AXSnapshot, n *AXNode) { n.ProviderBusName = "backend:43" },
		"window_object": func(_ *AXSnapshot, n *AXNode) { n.ProviderWindowObjectPath = "window-two" },
		"window_bus":    func(_ *AXSnapshot, n *AXNode) { n.ProviderWindowBusName = "backend:43" },
		"window_epoch":  func(_ *AXSnapshot, n *AXNode) { n.ProviderWindowProcessGeneration = "epoch2" },
		"missing_owner": func(_ *AXSnapshot, n *AXNode) { n.ProviderWindowObjectPath = "" },
		"window_handle": func(s *AXSnapshot, _ *AXNode) { s.WindowHandle += "other" },
		"window_ref":    func(s *AXSnapshot, _ *AXNode) { s.WindowRef = "w2" },
		"app_ref":       func(s *AXSnapshot, _ *AXNode) { s.AppRef = "a2" },
		"role":          func(_ *AXSnapshot, n *AXNode) { n.Role = "textbox" },
		"provider_role": func(_ *AXSnapshot, n *AXNode) { n.ProviderRole = "entry" },
		"name":          func(_ *AXSnapshot, n *AXNode) { n.Name = "Replacement" },
		"automation_id": func(_ *AXSnapshot, n *AXNode) { n.AutomationID = "replacement" },
		"missing_id":    func(_ *AXSnapshot, n *AXNode) { n.AutomationID = "" },
	}
	for _, mode := range []string{"typed", "node", "snapshot"} {
		for name, change := range changes {
			t.Run(mode+"/"+name, func(t *testing.T) {
				r := NewRefRegistry(32)
				snapshot, node := nativeIdentityFixture()
				old := nativeIdentityAcquire(r, snapshot, node, mode)
				before := *r.Entries[old]
				node.RuntimeID, node.SemanticTarget = []int{0, 12}, "changed diagnostic"
				change(snapshot, node)
				if next := nativeIdentityAcquire(r, snapshot, node, mode); next == old {
					t.Fatal("contradictory identity reused old ref")
				}
				entry := r.Entries[old]
				if entry.ProviderObjectPath != before.ProviderObjectPath || entry.Name != before.Name ||
					entry.ProviderWindowObjectPath != before.ProviderWindowObjectPath || entry.AutomationID != before.AutomationID {
					t.Fatal("replacement contaminated frozen identity")
				}
			})
		}
	}
}

func TestNativeIdentityIncompleteOwnerCannotMigrate(t *testing.T) {
	for _, mode := range []string{"typed", "node", "snapshot"} {
		for _, field := range []string{"app", "window", "owner", "owner_bus", "owner_epoch"} {
			t.Run(mode+"/"+field, func(t *testing.T) {
				r := NewRefRegistry(32)
				snapshot, node := nativeIdentityFixture()
				switch field {
				case "app":
					snapshot.AppRef = ""
				case "window":
					snapshot.WindowRef = ""
				case "owner":
					node.ProviderWindowObjectPath = ""
				case "owner_bus":
					node.ProviderWindowBusName = ""
				case "owner_epoch":
					node.ProviderWindowProcessGeneration = ""
				}
				old := nativeIdentityAcquire(r, snapshot, node, mode)
				node.RuntimeID, node.SemanticTarget = []int{0, 3}, "different projection"
				if next := nativeIdentityAcquire(r, snapshot, node, mode); old != "" && next == old {
					t.Fatal("incomplete ownership authorized projection migration")
				}
			})
		}
	}
}

func TestNativeIdentityRefreshBoundaries(t *testing.T) {
	for _, scope := range []string{"process", "process_instance"} {
		for _, change := range []string{"same", "token", "runtime", "owner", "missing_owner", "scope"} {
			t.Run(scope+"/"+change, func(t *testing.T) {
				r := NewRefRegistry(32)
				snapshot, node := nativeIdentityFixture()
				node.LocatorKind, node.ProviderIdentityScope = AXLocatorSemantic, scope
				ref := r.AcquireNode(snapshot, node, AXLocatorSemantic)
				before := *r.Entries[ref]
				record := nativeIdentityRecord(node)
				switch change {
				case "token":
					record.ProviderObjectPath = "replacement"
				case "runtime":
					record.RuntimeID = []int{0, 8}
				case "owner":
					record.ProviderWindowObjectPath = "window-two"
				case "missing_owner":
					record.ProviderWindowObjectPath = ""
				case "scope":
					record.ProviderIdentityScope = "other"
				}
				want := change == "same" || scope == "process" && (change == "token" || change == "scope" || change == "missing_owner")
				if got := r.RefreshProviderBinding(ref, record, true); got != want {
					t.Fatalf("refresh=%v want=%v", got, want)
				}
				if !want && (r.Entries[ref].ProviderObjectPath != before.ProviderObjectPath ||
					!reflect.DeepEqual(r.Entries[ref].RuntimeID, before.RuntimeID)) {
					t.Fatal("rejected refresh changed original binding")
				}
				if r.Entries[ref].ProviderWindowObjectPath != before.ProviderWindowObjectPath {
					t.Fatal("refresh discarded or changed the issuing window")
				}
			})
		}
	}
}

func TestNativeIdentityRawLabelsReachWire(t *testing.T) {
	for _, whitespace := range []string{"\u202f", "\u00a0", "\t", "\n"} {
		for _, kind := range []AXLocatorKind{AXLocatorSemantic, AXLocatorProviderBound} {
			t.Run(fmt.Sprintf("%q/%s", whitespace, kind), func(t *testing.T) {
				r := NewRefRegistry(32)
				snapshot, node := nativeIdentityFixture()
				record := nativeIdentityRecord(node)
				record.Name = "Meeting" + whitespace + "PM"
				node.SourceRecord, node.Name = record, "Meeting PM"
				node.SemanticTarget = encodeScopedSemanticRef(`button "Meeting PM"`, snapshot.WindowHandle)
				ref := r.AcquireNode(snapshot, node, kind)
				target, err := semanticTargetFromRefEntry(r.Entries[ref])
				if err != nil {
					t.Fatal(err)
				}
				encoded, err := json.Marshal(providerTargetFromOperation(target))
				if err != nil {
					t.Fatal(err)
				}
				var wire ProviderSemanticTarget
				if err := json.Unmarshal(encoded, &wire); err != nil {
					t.Fatal(err)
				}
				if wire.Name != record.Name || !wire.HasName || wire.ProviderObjectPath != record.ProviderObjectPath ||
					!reflect.DeepEqual(wire.RuntimeID, record.RuntimeID) {
					t.Fatalf("raw label or identity lost: %#v", wire)
				}
				if node.Name != "Meeting PM" {
					t.Fatal("display name changed")
				}
			})
		}
	}
}

func TestNativeIdentityDescriptionDoesNotBecomeName(t *testing.T) {
	for _, kind := range []AXLocatorKind{AXLocatorSemantic, AXLocatorProviderBound} {
		r := NewRefRegistry(16)
		snapshot, node := nativeIdentityFixture()
		node.Name, node.SemanticTarget = "", `button [desc="Details"]`
		node.SourceRecord = nativeIdentityRecord(node)
		ref := r.AcquireNode(snapshot, node, kind)
		target, err := semanticTargetFromRefEntry(r.Entries[ref])
		if err != nil || target.HasName || target.Name != "" {
			t.Fatalf("description became name: %#v %v", target, err)
		}
	}
}

func TestNativeIdentityOldLifecycleCannotEraseReplacement(t *testing.T) {
	r := NewRefRegistry(32)
	snapshot, node := nativeIdentityFixture()
	node.LocatorKind = AXLocatorSemantic
	old := r.AcquireNode(snapshot, node, node.LocatorKind)
	node.ProviderObjectPath = "replacement"
	fresh := r.AcquireNode(snapshot, node, node.LocatorKind)
	r.MarkSuperseded(old, "late stale observation")
	if next := r.AcquireNode(snapshot, node, node.LocatorKind); next != fresh || next == old {
		t.Fatalf("old lifecycle erased replacement: %s %s %s", old, fresh, next)
	}
}

func TestNativeIdentityRejectsSelectorWindowMismatch(t *testing.T) {
	r := NewRefRegistry(16)
	snapshot, node := nativeIdentityFixture()
	node.SemanticTarget = encodeScopedSemanticRef(`button "Save"`, "another-window")
	ref := r.AcquireNode(snapshot, node, AXLocatorSemantic)
	if _, err := semanticTargetFromRefEntry(r.Entries[ref]); err == nil {
		t.Fatal("selector overrode ref-owned window")
	}
}

func TestNativeIdentityEvictionPreservesReplacementIndex(t *testing.T) {
	r := NewRefRegistry(1)
	snapshot, node := nativeIdentityFixture()
	old := r.AcquireNode(snapshot, node, AXLocatorSemantic)
	node.ProviderObjectPath = "replacement"
	fresh := r.AcquireNode(snapshot, node, AXLocatorSemantic)
	if old == fresh || r.Entries[old] != nil {
		t.Fatal("old fixture ref was not replaced and evicted")
	}
	if next := r.AcquireNode(snapshot, node, AXLocatorSemantic); next != fresh {
		t.Fatal("evicting old ref erased the replacement identity index")
	}
}

func TestNativeIdentityFindRefreshAndWindowGuard(t *testing.T) {
	for _, change := range []string{"raw_label", "token", "other_window"} {
		t.Run(change, func(t *testing.T) {
			s := NewService()
			snapshot, node := nativeIdentityFixture()
			node.LocatorKind = AXLocatorSemantic
			node.Name, node.AutomationID = "Meeting PM", ""
			node.SemanticTarget = encodeScopedSemanticRef(`button "Meeting PM"`, snapshot.WindowHandle)
			node.Ref = s.RefRegistry.AcquireNode(snapshot, node, AXLocatorSemantic)
			ref := node.Ref
			record := nativeIdentityRecord(node)
			record.Name = "Meeting\u202fPM"
			source := &AppSnapshot{WindowHandle: snapshot.WindowHandle, Elements: []ElementRecord{*record}}
			switch change {
			case "token":
				source.Elements[0].ProviderObjectPath = "replacement"
			case "other_window":
				source.WindowHandle = "another-window"
			}
			s.stabilizeFindRefs(source, snapshot)
			entry := s.RefRegistry.Entries[ref]
			if change == "raw_label" {
				if node.Ref != ref || entry.Name != record.Name {
					t.Fatal("fresh find lost exact raw label")
				}
			} else if node.Ref != "" {
				t.Fatal("find advertised a ref contradicted by the current binding")
			}
			if change != "raw_label" && entry.Name != "Meeting PM" {
				t.Fatal("rejected refresh contaminated label")
			}
		})
	}
}

func TestNativeIdentityWrapperRebindNeedsStrongOwner(t *testing.T) {
	for _, change := range []string{"complete", "no_id", "no_runtime", "no_owner", "retained"} {
		t.Run(change, func(t *testing.T) {
			s := NewService()
			snapshot, node := nativeIdentityFixture()
			node.ProviderIdentityScope = "process"
			switch change {
			case "no_id":
				node.AutomationID = ""
			case "no_runtime":
				node.RuntimeID = nil
			case "no_owner":
				node.ProviderWindowObjectPath = ""
			case "retained":
				node.ProviderIdentityScope = "process_instance"
			}
			ref := s.RefRegistry.AcquireNode(snapshot, node, AXLocatorProviderBound)
			entry := s.RefRegistry.Entries[ref]
			target, err := semanticTargetFromRefEntry(entry)
			if err != nil {
				t.Fatal(err)
			}
			s.authorizeProviderRebind("fixture", entry, target)
			if target.AllowProviderRebind != (change == "complete") {
				t.Fatalf("unsafe or missing wrapper rebind for %s", change)
			}
			if target.AutomationID != entry.AutomationID {
				t.Fatal("automation anchor lost")
			}
		})
	}
}

func TestNativeIdentityCanonicalRawLabelAndAutomationWire(t *testing.T) {
	for _, kind := range []AXLocatorKind{AXLocatorSemantic, AXLocatorProviderBound} {
		t.Run(string(kind), func(t *testing.T) {
			source := nativeAliasFixture("process_instance")
			source.Elements = source.Elements[:2]
			source.Elements[1].Name = "Meeting\u202fPM"
			tree, err := buildAXSnapshot(source)
			if err != nil {
				t.Fatal(err)
			}
			node := tree.ByIndex[1]
			node.LocatorKind, node.FindMatch = kind, true
			tree.Roots = tree.CanonicalRoots
			registry := NewRefRegistry(16)
			registry.AssignSnapshotRefs(tree)
			if node.Ref == "" || node.Name != "Meeting PM" {
				t.Fatal("canonical fixture did not issue expected target")
			}
			target, err := semanticTargetFromRefEntry(registry.Entries[node.Ref])
			if err != nil {
				t.Fatal(err)
			}
			wire := providerTargetFromOperation(target)
			if wire.Name != source.Elements[1].Name || wire.AutomationID != source.Elements[1].AutomationID {
				t.Fatalf("canonical identity lost on provider wire: %#v", wire)
			}
		})
	}
}
