package core

import (
	"encoding/json"
	"reflect"
	"strings"
	"testing"
)

func TestNativeVisibilityUnknownAliases(t *testing.T) {
	yes := true
	known := ElementRecord{Showing: &yes, Visible: &yes}
	unsupported := ElementRecord{VisibilityEvidence: &AXVisibilityEvidence{State: "unknown", Reason: "unsupported"}}
	readError := ElementRecord{VisibilityEvidence: &AXVisibilityEvidence{
		State: "unknown", Reason: "read_error", ExceptionType: "ProviderException", HResult: "0x80004005",
	}}
	override := ElementRecord{Showing: &yes, Visible: &yes,
		VisibilityEvidence: &AXVisibilityEvidence{State: "offscreen", Reason: "focus_geometry_override"}}
	for _, records := range [][]ElementRecord{
		{known, unsupported, readError, override},
		{override, readError, unsupported, known},
		{unsupported, known, override, readError},
	} {
		snapshot := nativeAliasFixture("process")
		base := snapshot.Elements[1]
		snapshot.Elements = snapshot.Elements[:1]
		for i, evidence := range records {
			record := base
			record.Index, record.RuntimeID = i+1, []int{0, i}
			record.Showing, record.Visible, record.VisibilityEvidence = evidence.Showing, evidence.Visible, evidence.VisibilityEvidence
			snapshot.Elements = append(snapshot.Elements, record)
		}
		tree, err := buildAXSnapshot(snapshot)
		if err != nil {
			t.Fatal(err)
		}
		node := tree.ByIndex[1]
		if !reflect.DeepEqual(node.VisibilityEvidence, readError.VisibilityEvidence) || node.Showing != nil || node.Visible != nil {
			t.Fatalf("alias ordering erased unknown evidence: %#v", node)
		}
		if node.VisibilityEvidence == readError.VisibilityEvidence {
			t.Fatal("source evidence aliased")
		}
	}
}

func TestNativeVisibilityCloneAndWire(t *testing.T) {
	snapshot := nativeAliasFixture("process")
	snapshot.Elements = snapshot.Elements[:2]
	yes := true
	snapshot.Elements[1].Showing, snapshot.Elements[1].Visible = &yes, &yes
	snapshot.Elements[1].VisibilityEvidence = &AXVisibilityEvidence{
		State: "unknown", Reason: "read_error", ExceptionType: strings.Repeat("E", 200), HResult: "0x80004005extra",
	}
	data, err := json.Marshal(snapshot)
	if err != nil {
		t.Fatal(err)
	}
	var decoded AppSnapshot
	if err := json.Unmarshal(data, &decoded); err != nil {
		t.Fatal(err)
	}
	decoded.publicAppRef, decoded.publicWindowRef = "a1", "w1"
	tree, err := buildAXSnapshot(&decoded)
	if err != nil {
		t.Fatal(err)
	}
	canonical := tree.ByIndex[1]
	if canonical.Showing != nil || canonical.Visible != nil {
		t.Fatal("unknown became visible")
	}
	if len(canonical.VisibilityEvidence.ExceptionType) != 128 || canonical.VisibilityEvidence.HResult != "0x80004005" {
		t.Fatal("unbounded diagnostic evidence")
	}
	tree.Roots[0].Children[0].VisibilityEvidence.Reason = "unsupported"
	if canonical.VisibilityEvidence.Reason != "read_error" || len(decoded.Elements[1].VisibilityEvidence.ExceptionType) != 200 {
		t.Fatal("clone mutated canonical or wire evidence")
	}
}

func TestNativeVisibilityDiagnosticConflict(t *testing.T) {
	a := &AXVisibilityEvidence{State: "unknown", Reason: "read_error", ExceptionType: "First", HResult: "0x80004005"}
	b := &AXVisibilityEvidence{State: "unknown", Reason: "read_error", ExceptionType: "Second", HResult: "0x80070005"}
	for _, pair := range [][2]*AXVisibilityEvidence{{a, b}, {b, a}} {
		got := mergeVisibilityEvidence(pair[0], pair[1])
		if got.State != "unknown" || got.ExceptionType != "" || got.HResult != "" {
			t.Fatalf("invented exception: %#v", got)
		}
	}
	if a.ExceptionType != "First" || b.ExceptionType != "Second" {
		t.Fatal("source diagnostics mutated")
	}
	if mergeVisibilityEvidence(nil, nil) != nil {
		t.Fatal("absent evidence invented visibility")
	}
}

func TestNativeVisibilityExplicitSetValueCapability(t *testing.T) {
	for _, bus := range []string{":1.42", "mac:42", "uia3:pid:42"} {
		for _, supported := range []bool{false, true} {
			record := &ElementRecord{ProviderBusName: bus, ControlType: "slider", SetValueSupported: &supported, Actions: []string{"set_value"}}
			if got := typedAXCapabilities(record, "slider").SetValue; got != supported {
				t.Fatalf("explicit capability ignored for %s: %v", bus, got)
			}
		}
		record := &ElementRecord{ProviderBusName: bus, ControlType: "slider"}
		if got := typedAXCapabilities(record, "slider").SetValue; got != (bus != "uia3:pid:42") {
			t.Fatalf("legacy capability fallback changed for %s", bus)
		}
		record.Actions = []string{"set_value"}
		if !typedAXCapabilities(record, "slider").SetValue {
			t.Fatal("native set_value action lost")
		}
	}
}
