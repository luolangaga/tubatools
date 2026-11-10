package core

import (
	"encoding/json"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func decodeTestArguments(t *testing.T, raw string) map[string]any {
	t.Helper()
	decoder := json.NewDecoder(strings.NewReader(raw))
	decoder.UseNumber()
	var args map[string]any
	if err := decoder.Decode(&args); err != nil {
		t.Fatalf("decode %s: %v", raw, err)
	}
	return args
}

func TestPublishedInputSchemasCompile(t *testing.T) {
	for _, tool := range contract.AllToolDefinitions() {
		schema, err := contract.CompiledInputSchema(tool.Name)
		if err != nil || schema == nil {
			t.Fatalf("%s input schema did not compile: %v", tool.Name, err)
		}
		again, _ := contract.CompiledInputSchema(tool.Name)
		if again != schema {
			t.Fatalf("%s input schema was compiled twice", tool.Name)
		}
	}
}

// Each rejected call names one problem and, when the call can be repaired
// mechanically, the corrected call itself. AX arguments are checked by the
// AX decoder first; the schema gate remains the backstop.
func TestArgumentErrorsSelectTheIntendedBranch(t *testing.T) {
	for _, tc := range []struct {
		tool, args, path string
		want             []string
	}{
		{"GUI", `{"actions":[{"type":"wait","ms":5}]}`, "/actions/0",
			[]string{`actions[0] wait does not accept fields: "ms"`}},
		{"GUI", `{"actions":[{"type":"click","x":"10","y":1}]}`, "/actions/0/x",
			[]string{"actions[0].x must be a number, not a string"}},
		{"GUI", `{"actions":[{"type":"wait"}],"settle_ms":"800"}`, "/settle_ms",
			[]string{"settle_ms must be an integer, not a string"}},
		{"GUI", `{"actions":[{"type":"click","x":1,"y":1,"button":"LEFT"}]}`, "/actions/0/button",
			[]string{`button "LEFT" is unsupported`}},
		{"GUI", `{"actions":[{"type":"scroll","x":1,"y":1,"scroll_y":0}]}`, "/actions/0/scroll_y",
			[]string{"scroll_y must not be 0"}},
		{"AX", `{"mode":"ACT"}`, "/mode", []string{`mode "ACT" is unsupported`}},
		{"AX", `{"mode":"observe","app":"x","actions":[]}`, "/actions",
			[]string{"actions must contain 1 to 64 items"}},
		{"AX", `{"mode":"observe","window_ref":7}`, "/window_ref",
			[]string{"window_ref must be a string, not a number"}},
		{"AX", `{"mode":"act","actions":[{"type":"click","ref":"r1","target":"r1"}]}`, "/actions/0",
			[]string{`does not accept fields: "target"`}},
	} {
		argErr := contract.ValidateToolArguments(tc.tool, decodeTestArguments(t, tc.args))
		if argErr == nil {
			t.Errorf("%s %s was accepted", tc.tool, tc.args)
			continue
		}
		if argErr.Path != tc.path {
			t.Errorf("%s %s: path %q, want %q (%s)", tc.tool, tc.args, argErr.Path, tc.path, argErr.Message)
		}
		for _, fragment := range tc.want {
			if !strings.Contains(argErr.Message, fragment) {
				t.Errorf("%s %s: message %q lacks %q", tc.tool, tc.args, argErr.Message, fragment)
			}
		}
		for _, noise := range []string{"oneOf", "anyOf", "allOf", "$defs", "schema://", "jsonschema"} {
			if strings.Contains(argErr.Message, noise) {
				t.Errorf("%s %s: message leaks schema internals: %q", tc.tool, tc.args, argErr.Message)
			}
		}
	}
}

// The schema and the AX decoder must agree: every call the decoder accepts
// passes the published schema.
func TestAXDecoderAcceptedCallsPassTheSchema(t *testing.T) {
	for _, raw := range []string{
		`{"mode":"apps"}`,
		`{"mode":"observe","app":"a1"}`,
		`{"mode":"observe","window_ref":"w1","root":"focused","observe":"full","scope":"content"}`,
		`{"mode":"act","actions":[{"type":"wait","window":"Saved","gone":true,"timeout_ms":100}]}`,
		`{"mode":"act","actions":[{"type":"wait","ref":"r3","value":"5"}]}`,
		`{"mode":"act","window_ref":"w1","actions":[{"type":"wait","query":{"role":"dialog","name_contains":"Print","state":"focused"}}]}`,
		`{"mode":"find","window_ref":"w1","limit":50,"query":{"role":"button","name_contains":"Save"}}`,
		`{"mode":"find","cursor":"f2","limit":10}`,
		`{"mode":"act","observe":"tree","window_ref":"w1","actions":[{"type":"click","ref":"r1"},{"type":"wait","ref":"r1","state":"checked"},{"type":"wait","text":"Done","gone":true}]}`,
		`{"mode":"act","actions":[{"type":"activate","title":"Report"},{"type":"keypress","keys":["CTRL","S"]},{"type":"type","text":"x"},{"type":"scroll","direction":"down","pages":0.5},{"type":"set_value","ref":"r2","value":""},{"type":"close","ref":"w2"}]}`,
		`{"mode":"act","actions":[{"ref":"r103","type":"click"}]}`,
	} {
		args := decodeTestArguments(t, raw)
		if _, err := DecodeAXRequest(args); err != nil {
			t.Fatalf("decoder rejected %s: %#v", raw, err)
		}
		if argErr := contract.ValidateToolArguments(contract.PublicToolAX, args); argErr != nil {
			t.Fatalf("schema rejected decoder-accepted %s: %s", raw, argErr.Message)
		}
	}
}

// Rejected arguments must not reach a provider or emit input.
func TestSchemaRejectionHappensBeforeAnyProviderCall(t *testing.T) {
	for _, tc := range []struct{ tool, args string }{
		{"AX", `{"mode":"act","actions":[{"ref":"r1","action":"press"}]}`},
		{"AX", `{"mode":"observe","app":"x","observe":"Diff"}`},
		{"AX", `{"mode":"act","actions":[{"type":"wait","ref":"r1","gone":"yes"}]}`},
		{"GUI", `{"actions":[{"type":"click","x":1,"y":1,"delay":3}]}`},
		{"GUI", `{"actions":[{"type":"wait"}],"settle_ms":"800"}`},
	} {
		backend := &recordingPlatformBackend{}
		result := NewServiceWithBackend(backend).callTool(tc.tool, decodeTestArguments(t, tc.args))
		if !result.IsError {
			t.Fatalf("%s %s was accepted", tc.tool, tc.args)
		}
		if len(backend.calls) != 0 || len(backend.desktopRequests) != 0 || len(backend.actionRequests) != 0 {
			t.Fatalf("%s %s reached the provider: %v", tc.tool, tc.args, backend.calls)
		}
		code := result.StructuredContent["argument_error_code"]
		if tc.tool == contract.PublicToolAX {
			code = result.StructuredContent[contract.AXPublicErrorCodeKey]
		}
		if code != contract.ArgumentErrorInvalid {
			t.Fatalf("%s %s: missing argument error code: %#v", tc.tool, tc.args, result.StructuredContent)
		}
		visible := withEvidenceLine(result, nil)
		if strings.Contains(ResultText(visible), "argument_error") ||
			strings.Contains(ResultText(visible), "public_error") {
			t.Fatalf("internal argument evidence reached the model: %q", ResultText(visible))
		}
	}
}

func TestArgumentHintsStayShort(t *testing.T) {
	long := strings.Repeat("x", 2000)
	raw := `{"mode":"act","actions":[{"ref":"r1","action":"set","value":"` + long + `"}]}`
	_, argErr := DecodeAXRequest(decodeTestArguments(t, raw))
	if argErr == nil {
		t.Fatal("misplaced action was accepted")
	}
	if len(argErr.Message)+len(argErr.Hint) > 400 {
		t.Fatalf("argument error repeats the long value: %d bytes", len(argErr.Message)+len(argErr.Hint))
	}
}

// Recorded runs often name the action verb under "action" with a synonym
// ({"ref":"r1","action":"press"}). The hint maps it to the published action;
// the synonym itself stays invalid.
func TestArgumentHintMapsRecordedVerbSynonyms(t *testing.T) {
	for raw, hint := range map[string]string{
		`{"mode":"act","actions":[{"ref":"r1","action":"press"}]}`:               `{"type":"click","ref":"r1"}`,
		`{"mode":"act","actions":[{"action":"close_window","target":"w1"}]}`:     `{"type":"close","ref":"w1"}`,
		`{"mode":"act","actions":[{"ref":"r8","action":"set_text","text":"a"}]}`: `{"type":"set_value","ref":"r8","value":"a"}`,
	} {
		_, argErr := DecodeAXRequest(decodeTestArguments(t, raw))
		if argErr == nil || argErr.Hint != hint {
			t.Fatalf("%s: got %#v, want hint %s", raw, argErr, hint)
		}
	}
	if _, argErr := DecodeAXRequest(decodeTestArguments(t,
		`{"mode":"act","actions":[{"type":"press","ref":"r1"}]}`)); argErr == nil {
		t.Fatal("a hint synonym became an accepted action type")
	}
}
