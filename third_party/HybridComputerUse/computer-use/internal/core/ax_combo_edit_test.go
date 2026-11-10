package core

import (
	"encoding/json"
	"testing"
)

func TestComboEditCapabilitiesRequireProviderEvidence(t *testing.T) {
	for _, supported := range []bool{false, true} {
		record := &ElementRecord{ControlType: "combo box", SetValueSupported: &supported}
		if got := typedAXCapabilities(record, "combobox").SetValue; got != supported {
			t.Fatalf("explicit setValue=%v produced %v", supported, got)
		}
	}
	if typedAXCapabilities(&ElementRecord{ControlType: "combo box"}, "combobox").SetValue {
		t.Fatal("combobox role alone must not advertise an editable value")
	}
}

func TestComboEditRefBindsPartnerThroughProviderWire(t *testing.T) {
	registry := NewRefRegistry(32)
	identity := &axRelationTarget{ProviderBusName: ":1.2", ProviderObjectPath: "/editor", ProcessGeneration: "4:5"}
	node := &AXNode{
		Role: "combobox", ProviderRole: "combo box", Name: "Find",
		ProviderBusName: ":1.2", ProviderObjectPath: "/toggle", ProcessGeneration: "4:5",
		SemanticTarget: `combobox "Find"`, RuntimeID: []int{0, 1},
		SourceRecord: &ElementRecord{EditTarget: identity},
	}
	snapshot := &AXSnapshot{AppIdentity: "Editor", AppRef: "a1", WindowRef: "w1", WindowHandle: "private-window", Revision: "c1"}
	ref := registry.AcquireNode(snapshot, node, AXLocatorSemantic)
	entry, err := registry.Resolve(ref, "")
	if err != nil {
		t.Fatal(err)
	}
	target, err := semanticTargetFromRefEntry(entry)
	if err != nil {
		t.Fatal(err)
	}
	encoded, err := json.Marshal(providerTargetFromOperation(target))
	if err != nil {
		t.Fatal(err)
	}
	var decoded ProviderSemanticTarget
	if err := json.Unmarshal(encoded, &decoded); err != nil {
		t.Fatal(err)
	}
	if !sameComboEditTarget(decoded.EditTarget, identity) {
		t.Fatalf("lost editor binding: %s", encoded)
	}
	if same := registry.AcquireNode(snapshot, node, AXLocatorSemantic); same != ref {
		t.Fatalf("unchanged editor changed ref: %s -> %s", ref, same)
	}
	replacement := *identity
	replacement.ProviderObjectPath = "/replacement"
	node.SourceRecord.EditTarget = &replacement
	newRef := registry.AcquireNode(snapshot, node, AXLocatorSemantic)
	if newRef == ref {
		t.Fatal("replacement editor reused old ref")
	}
	if _, err := registry.Resolve(ref, ""); err == nil {
		t.Fatal("replaced editor ref remained actionable")
	}
	node.SourceRecord.EditTarget = nil
	if readonlyRef := registry.AcquireNode(snapshot, node, AXLocatorSemantic); readonlyRef == newRef {
		t.Fatal("missing editor reused editable ref")
	}
}

func TestComboEditPrivateWireFixture(t *testing.T) {
	request, err := DecodeProviderWireRequest(readProviderFixture(t, "combo-edit.request.json"))
	if err != nil {
		t.Fatal(err)
	}
	if request.Action.Target.EditTarget == nil {
		t.Fatal("fixture lost editor identity")
	}
	request.Action.Target.EditTarget.ProcessGeneration = ""
	if err := request.Validate(); err == nil {
		t.Fatal("incomplete editor identity accepted")
	}
}
