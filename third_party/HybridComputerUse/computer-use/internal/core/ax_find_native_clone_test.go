package core

import (
	"reflect"
	"strings"
	"testing"
)

func TestFindNativeCursorDeepCopy(t *testing.T) {
	svc, snapshot, matches := findDeliveryMany(t, 2)
	yes := true
	record := &matches[0]
	record.RuntimeIDAliases = [][]int{{1, 2}}
	record.Actions = []string{"set_value"}
	record.Showing, record.Visible, record.SetValueSupported = &yes, &yes, &yes
	record.Frame = &frame{X: 10}
	record.VisibilityEvidence = &AXVisibilityEvidence{State: "unknown", Reason: "read_error"}
	record.TextAttributes = map[string]string{"weight": "normal"}
	record.Relations = map[string][]axRelationTarget{"labelled_by": {{Name: "Label"}}}
	want := cloneFindRecord(*record)
	snapshot.findHiddenRecords = map[int]ElementRecord{record.Index: *record}
	snapshot.AXSnapshot.ByIndex[record.Index].VisibilityEvidence = cloneVisibilityEvidence(record.VisibilityEvidence)
	cursor := svc.FindCursorRegistry.Create(snapshot, matches, 0, "target")
	resolution, err := svc.FindCursorRegistry.Resolve(cursor)
	if err != nil {
		t.Fatal(err)
	}
	record.RuntimeID[0] = 99
	record.RuntimeIDAliases[0][0] = 99
	record.Actions[0] = "changed"
	yes = false
	record.Frame.X = 99
	record.VisibilityEvidence.Reason = "unsupported"
	record.TextAttributes["weight"] = "bold"
	record.Relations["labelled_by"][0].Name = "Changed"
	snapshot.AXSnapshot.ByIndex[record.Index].VisibilityEvidence.Reason = "unsupported"
	set := resolution.ResultSet
	if !reflect.DeepEqual(set.Matches[0], want) || !reflect.DeepEqual(set.Snapshot.findHiddenRecords[record.Index], want) {
		t.Fatal("live mutable record state contaminated cached match or hidden provenance")
	}
	if set.Snapshot.AXSnapshot.ByIndex[record.Index].VisibilityEvidence.Reason != "read_error" {
		t.Fatal("live visibility contaminated cached canonical node")
	}
	set.Matches[0].VisibilityEvidence.Reason = "changed"
	if set.Snapshot.findHiddenRecords[record.Index].VisibilityEvidence.Reason != "read_error" {
		t.Fatal("cached matches alias the hidden provenance ledger")
	}
}

func TestFindNativeVisibilityRendererAndClones(t *testing.T) {
	for _, test := range []struct {
		state, reason, token string
	}{
		{"unknown", "unsupported", "[visibility=unknown reason=unsupported]"},
		{"unknown", "read_error", "[visibility=unknown reason=read_error]"},
		{"offscreen", "focus_geometry_override", "[visibility=offscreen reason=focus_geometry_override]"},
		{"unknown", "untrusted]\n - injected", ""},
	} {
		t.Run(test.reason, func(t *testing.T) {
			node := &AXNode{ID: "target", Role: "button", Name: "Target", VisibilityEvidence: &AXVisibilityEvidence{
				State: test.state, Reason: test.reason,
			}}
			context := cloneAXFindContextForest([]*AXNode{node}, map[string]bool{node.ID: true}, nil, nil, nil)
			display := findDisplayNode(node)
			for _, clone := range []*AXNode{context[0], display} {
				label := renderAXNodeLabel(clone)
				if test.token != "" && !strings.Contains(label, test.token) {
					t.Fatalf("visibility evidence missing from label: %s", label)
				}
				if test.token == "" && strings.Contains(label, "visibility=") {
					t.Fatalf("untrusted visibility reason entered renderer: %s", label)
				}
				clone.VisibilityEvidence.Reason = "changed"
				if node.VisibilityEvidence.Reason != test.reason {
					t.Fatal("find clone mutated canonical visibility evidence")
				}
			}
		})
	}
}
