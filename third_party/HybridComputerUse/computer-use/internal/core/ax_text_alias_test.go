package core

import (
	"reflect"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestAXTextAliasesKeepValidationAndInput(t *testing.T) {
	for _, tc := range []struct {
		name, raw   string
		wantError   bool
		text, value string
	}{
		{"type alias", `{"type":"type","value":"literal 字"}`, false, "literal 字", ""},
		{"value alias", `{"type":"set_value","ref":"r1","text":"字"}`, false, "", "字"},
		{"both same", `{"type":"type","text":"a","value":"a"}`, true, "", ""},
		{"both different", `{"type":"set_value","ref":"r1","text":"a","value":"b"}`, true, "", ""},
		{"empty alias", `{"type":"set_value","ref":"r1","text":""}`, true, "", ""},
		{"canonical clear", `{"type":"set_value","ref":"r1","value":""}`, false, "", ""},
		{"wrong type", `{"type":"type","value":1}`, true, "", ""},
		{"wrong value type", `{"type":"set_value","ref":"r1","text":[]}`, true, "", ""},
		{"missing ref", `{"type":"set_value","text":"a"}`, true, "", ""},
		{"select retains fields", `{"type":"select","ref":"r1","value":"a"}`, true, "", ""},
		{"select conflict", `{"type":"select","ref":"r1","text":"a","to_ref":"r2"}`, true, "", ""},
	} {
		t.Run(tc.name, func(t *testing.T) {
			object := decodeTestArguments(t, tc.raw)
			before := decodeTestArguments(t, tc.raw)
			action, err := decodeAXAction(object, "actions[0]")
			if (err != nil) != tc.wantError {
				t.Fatalf("error %v, wantError %v", err, tc.wantError)
			}
			if !tc.wantError && (action.text != tc.text || action.value != tc.value) {
				t.Fatalf("wrong literal input: %+v", action)
			}
			if !reflect.DeepEqual(object, before) {
				t.Fatalf("caller arguments mutated: %v", object)
			}
		})
	}
}

func TestAXTextAliasStillCountsTextBudget(t *testing.T) {
	_, err := decodeAXActions([]any{map[string]any{"type": "type", "value": strings.Repeat("字", contract.MaxActionText+1)}})
	if err == nil {
		t.Fatal("alias bypassed text size limit")
	}
}

func TestAXTypeAliasDeliversLiteralThroughPublicCall(t *testing.T) {
	d := newRecoveryDesktop()
	s := NewServiceWithBackend(d)
	r := s.callTool("AX", decodeTestArguments(t, `{"mode":"act","observe":"none","actions":[{"type":"type","value":"literal 字"}]}`))
	if r.IsError || len(d.actionRequests) != 1 {
		t.Fatalf("public alias delivery failed: result=%+v actions=%v", r, d.actionRequests)
	}
	request := d.actionRequests[0]
	if request.Type == nil || request.Type.Text != "literal 字" || request.WindowHandle != recoveryMainHandle {
		t.Fatalf("wrong text or window delivered: %+v", request)
	}
}

func TestAXConflictingAliasRejectsWholeBatchBeforeInput(t *testing.T) {
	d := newRecoveryDesktop()
	s := NewServiceWithBackend(d)
	r := s.callTool("AX", decodeTestArguments(t, `{"mode":"act","actions":[{"type":"keypress","keys":["TAB"]},{"type":"type","text":"a","value":"b"}]}`))
	if !r.IsError || len(d.actionRequests) != 0 {
		t.Fatalf("rejection did not precede input: result=%+v actions=%v", r, d.actionRequests)
	}
}
