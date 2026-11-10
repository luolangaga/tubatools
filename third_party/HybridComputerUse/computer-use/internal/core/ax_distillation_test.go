package core

import (
	"fmt"
	"strings"
	"testing"
)

func TestWorkingProjectionHidesUnaddressableControlLeaf(t *testing.T) {
	plugin := removeUnaddressableControlsPlugin()
	hidden := &AXNode{
		ID: "button", Role: "button", SemanticClass: "control",
		AddressabilityEvaluated: true,
		AddressabilityReason:    "not_currently_visible",
	}
	visible := &AXNode{
		ID: "button-ref", Role: "button", SemanticClass: "control",
		Name: "Open", Ref: "r1",
		AddressabilityEvaluated: true,
	}
	stateful := &AXNode{
		ID: "checkbox", Role: "checkbox", SemanticClass: "control",
		Name:                    "Enabled",
		States:                  AXStates{Checked: CheckedTrue},
		AddressabilityEvaluated: true,
	}
	named := &AXNode{
		ID: "named-button", Role: "button", SemanticClass: "control",
		Name:                    "Save",
		AddressabilityEvaluated: true,
		AddressabilityReason:    "stable_identity_unavailable",
	}
	roots, removed, _, err := plugin.Apply(
		&DistillationContext{Snapshot: &AXSnapshot{
			AppRef: "a1", WindowRef: "w1",
		}}, []*AXNode{hidden, visible, stateful, named},
	)
	if err != nil {
		t.Fatal(err)
	}
	if removed != 1 || len(roots) != 3 ||
		roots[0].Ref != "r1" || roots[1].ID != "checkbox" ||
		roots[2].ID != named.ID {
		t.Fatalf("projection roots=%#v removed=%d", roots, removed)
	}
	if label := renderAXNodeLabel(stateful); !strings.Contains(
		label, "[addressable=false]",
	) {
		t.Fatalf("stateful label=%q", label)
	}
	if label := renderAXNodeLabel(named); !strings.Contains(
		label, "[addressable=false]",
	) {
		t.Fatalf("visible identity-incomplete control lacked marker: %q", label)
	}
}

func TestWorkingProjectionKeepsUnknownAddressability(t *testing.T) {
	plugin := removeUnaddressableControlsPlugin()
	unknown := &AXNode{
		ID:                      "unknown-button",
		Role:                    "button",
		SemanticClass:           "control",
		Name:                    "Continue",
		AddressabilityEvaluated: true,
	}
	roots, removed, _, err := plugin.Apply(
		&DistillationContext{Snapshot: &AXSnapshot{
			AppRef: "a1", WindowRef: "w1",
		}},
		[]*AXNode{unknown},
	)
	if err != nil {
		t.Fatal(err)
	}
	if removed != 0 || len(roots) != 1 || roots[0] != unknown {
		t.Fatalf("unknown addressability was discarded: roots=%#v removed=%d", roots, removed)
	}
}

func TestStructuralBudgetTreatsAllKnownStatesAsCritical(t *testing.T) {
	level := 2
	for name, node := range map[string]*AXNode{
		"disabled":        {States: AXStates{Disabled: TriTrue}},
		"collapsed":       {States: AXStates{Expanded: TriFalse}},
		"hierarchy level": {States: AXStates{Level: &level}},
	} {
		if !axCriticalState(node) {
			t.Fatalf("%s state was not protected by structural budget", name)
		}
	}
}

func TestCollectionSummaryPreservesForceKeptDisambiguator(t *testing.T) {
	parent := &AXNode{ID: "list", Role: "list"}
	for index := 0; index < 12; index++ {
		child := &AXNode{
			ID:     fmt.Sprintf("item-%d", index),
			Role:   "listitem",
			Name:   fmt.Sprintf("Item %d", index),
			Parent: parent,
		}
		if index == 5 {
			child.ForceKeep = true
		}
		parent.Children = append(parent.Children, child)
	}
	plugin := summarizeCollectionsPlugin()
	roots, _, _, err := plugin.Apply(nil, []*AXNode{parent})
	if err != nil {
		t.Fatal(err)
	}
	found := false
	for _, child := range roots[0].Children {
		if child.ID == "item-5" {
			found = true
			break
		}
	}
	if !found {
		t.Fatal("collection summary removed a force-kept disambiguator")
	}
}
