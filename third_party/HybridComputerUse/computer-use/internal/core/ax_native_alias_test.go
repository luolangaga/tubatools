package core

import (
	"errors"
	"strings"
	"testing"
)

func nativeAliasFixture(scope string) *AppSnapshot {
	snapshot, node := nativeIdentityFixture()
	first := *nativeIdentityRecord(node)
	first.ProviderIdentityScope = scope
	second := first
	second.Index, second.RuntimeID = 2, []int{0, 2}
	return &AppSnapshot{
		WindowHandle: snapshot.WindowHandle, publicWindowRef: "w1", publicAppRef: "a1",
		SnapshotID: "c1", App: AppDescriptor{Name: "fixture"},
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "window", Name: "Document"},
			first, second,
		},
	}
}

func TestNativeAliasConflicts(t *testing.T) {
	for _, scope := range []string{"", "process", "process_instance"} {
		for _, conflict := range []string{"role", "id", "owner", "later_owner", "label_hides_role"} {
			t.Run(scope+"/"+conflict, func(t *testing.T) {
				snapshot := nativeAliasFixture(scope)
				switch conflict {
				case "role":
					snapshot.Elements[2].ControlType = "textbox"
				case "id":
					snapshot.Elements[2].AutomationID = "other"
				case "owner":
					snapshot.Elements[2].ProviderWindowObjectPath = "window-two"
				case "label_hides_role":
					snapshot.Elements[2].Name, snapshot.Elements[2].ControlType = "Different", "textbox"
				case "later_owner":
					snapshot.Elements[1].ProviderWindowObjectPath = ""
					third := snapshot.Elements[2]
					third.Index, third.RuntimeID, third.ProviderWindowObjectPath = 3, []int{0, 3}, "window-two"
					snapshot.Elements = append(snapshot.Elements, third)
				}
				tree, err := buildAXSnapshot(snapshot)
				var typed *axProviderIdentityConflict
				if tree != nil || !errors.As(err, &typed) {
					t.Fatalf("conflict accepted: %v", err)
				}
				if strings.Contains(err.Error(), "backend:42") || strings.Contains(err.Error(), "window-two") {
					t.Fatal("error leaked private identity")
				}
			})
		}
	}
}

func TestNativeAliasCompatibleAndDistinct(t *testing.T) {
	snapshot := nativeAliasFixture("process_instance")
	snapshot.Elements[1].AutomationID = ""
	snapshot.Elements[1].States, snapshot.Elements[2].States = "[focused]", "[selected]"
	tree, err := buildAXSnapshot(snapshot)
	if err != nil {
		t.Fatal(err)
	}
	first := tree.ByIndex[1]
	if first != tree.ByIndex[2] || first.AutomationID != "save" ||
		len(first.RuntimeIDAliases) != 2 || first.States.Active != TriUnknown {
		t.Fatalf("compatible aliases lost identity or consensus: %#v", first)
	}
	snapshot.Elements[2].ProviderObjectPath = "different-object"
	tree, err = buildAXSnapshot(snapshot)
	if err != nil || tree.ByIndex[1] == tree.ByIndex[2] {
		t.Fatalf("distinct objects merged: %v", err)
	}
	snapshot = nativeAliasFixture("table_owner")
	snapshot.Elements[1].Name, snapshot.Elements[2].Name = "A1", "B1"
	snapshot.Elements[1].ControlType, snapshot.Elements[2].ControlType = "cell", "cell"
	tree, err = buildAXSnapshot(snapshot)
	if err != nil || tree.ByIndex[1] == tree.ByIndex[2] {
		t.Fatalf("virtual targets merged: %v", err)
	}
}
