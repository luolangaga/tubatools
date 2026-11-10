package core

import (
	"encoding/json"
	"strings"
	"testing"
)

func TestDecodeAXSelectShapes(t *testing.T) {
	actions, err := decodeAXActions([]any{
		map[string]any{"type": "select", "ref": "r3"},
		map[string]any{"type": "select", "ref": "r3", "to_ref": "r5"},
		map[string]any{"type": "select", "ref": "r3", "text": "beta"},
	})
	if err != nil || len(actions) != 3 {
		t.Fatalf("valid select steps rejected: %#v %v", actions, err)
	}
	if actions[1].toRef != "r5" || actions[2].text != "beta" || actions[0].targetRef != "r3" {
		t.Fatalf("decoded select steps: %#v", actions)
	}
	for name, test := range map[string]struct {
		action  map[string]any
		path    string
		message string
	}{
		"needs ref":      {map[string]any{"type": "select", "to_ref": "r5"}, "actions[0].ref", "select needs ref"},
		"window to_ref":  {map[string]any{"type": "select", "ref": "r3", "to_ref": "w2"}, "actions[0].to_ref", "rN"},
		"empty text":     {map[string]any{"type": "select", "ref": "r3", "text": ""}, "actions[0].text", "non-empty"},
		"range and text": {map[string]any{"type": "select", "ref": "r3", "to_ref": "r5", "text": "x"}, "actions[0]", "not both"},
		"unknown field":  {map[string]any{"type": "select", "ref": "r3", "value": "x"}, "actions[0].value", ""},
	} {
		_, err := decodeAXActions([]any{test.action})
		if err == nil || err.path != test.path || !strings.Contains(err.Message, test.message) {
			t.Errorf("%s: got %#v, want path %q containing %q", name, err, test.path, test.message)
		}
	}
}

func TestSelectRequestCarriesRangeEndAcrossProviderWire(t *testing.T) {
	request := ProviderActionRequest{
		ProtocolVersion: ProviderProtocolVersion,
		Intent:          AccessibilitySelect,
		App:             "a1",
		Target:          &ProviderSemanticTarget{Selector: "paragraph", Role: "paragraph"},
	}
	axSelectCommand{To: &semanticTarget{Selector: "paragraph", Role: "paragraph"}}.
		applyProviderAction(&request)
	if err := ValidateProviderActionRequest(request); err != nil {
		t.Fatalf("select request rejected: %v", err)
	}
	encoded, err := json.Marshal(request)
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(encoded), `"input":{"to_target":{"selector":"paragraph"`) {
		t.Fatalf("select input missing to_target: %s", encoded)
	}
	var decoded ProviderActionRequest
	if err := json.Unmarshal(encoded, &decoded); err != nil {
		t.Fatal(err)
	}
	if decoded.Select == nil || decoded.Select.ToTarget == nil {
		t.Fatalf("select input lost in round trip: %#v", decoded)
	}
	request.Select.Text = "beta"
	if err := ValidateProviderActionRequest(request); err == nil {
		t.Fatal("select with both text and to_target was accepted")
	}
	request.Target = nil
	request.Select = &ProviderSelectInput{}
	if err := ValidateProviderActionRequest(request); err == nil {
		t.Fatal("select without a target was accepted")
	}
}
