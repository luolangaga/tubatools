package core

import (
	"os"
	"path/filepath"
	"sort"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// axBadCalls has at least one call per rejection site in the AX decoder.
// TestEveryAXArgumentErrorHintDecodes checks that the table reaches every
// axInvalid call in ax_public_request.go.
var axBadCalls = []string{
	// mode
	`{}`,
	`{"mode":"assert_ax"}`,
	`{"mode":"OBSERVE","app":"x"}`,
	// fields per mode, renamed and removed fields
	`{"mode":"apps","observe":"none"}`,
	`{"mode":"apps","app":"soffice"}`,
	`{"mode":"observe","app_ref":"a1"}`,
	`{"mode":"observe","observation_target":"apps"}`,
	`{"mode":"act","app":"vlc","window_ref":"w3","actions":[{"type":"keypress","keys":["CTRL","Q"]}]}`,
	// identity
	`{"mode":"observe","app":1234}`,
	`{"mode":"observe","app":"  "}`,
	`{"mode":"observe","app":"x","window_ref":"w1"}`,
	`{"mode":"observe","window_ref":"W1"}`,
	`{"mode":"observe"}`,
	// observe
	`{"mode":"observe","window_ref":"w2","root":"w2","observe":"tree"}`,
	`{"mode":"observe","app":"x","root":"w2"}`,
	`{"mode":"observe","window_ref":"w1","root":"w2"}`,
	`{"mode":"observe","app":"x","observe":"Diff"}`,
	`{"mode":"observe","app":"x","observe":"notes"}`,
	`{"mode":"observe","app":"x","scope":"desktop"}`,
	`{"mode":"observe","app":"x","observe":"none"}`,
	// find
	`{"mode":"find","window_ref":"w3","query":{"name_contains":"record"},"scope":"window","limit":100}`,
	`{"mode":"find","app":"x","query":{"role":"button"},"limit":0}`,
	`{"mode":"find","app":"x","query":{"role":"button"},"limit":"5"}`,
	`{"mode":"find","cursor":"f1","query":{"role":"button"}}`,
	`{"mode":"find","cursor":"page2"}`,
	`{"mode":"find","query":{"role":"button"}}`,
	`{"mode":"find","app":"x"}`,
	`{"mode":"find","app":"x","query":"Save"}`,
	`{"mode":"find","app":"x","query":{}}`,
	`{"mode":"find","app":"x","query":{"name":"Save"}}`,
	`{"mode":"find","app":"x","query":{"role":"button","limit":5}}`,
	`{"mode":"find","app":"x","query":{"role":" "}}`,
	`{"mode":"find","app":"x","query":{"name_equals":"Save","name_contains":"Sa"}}`,
	// actions
	`{"mode":"act"}`,
	`{"mode":"act","actions":{"type":"click","ref":"r1"}}`,
	`{"mode":"act","actions":[]}`,
	`{"mode":"act","actions":[` + strings.TrimSuffix(strings.Repeat(`{"type":"keypress","keys":["TAB"]},`, contract.AXBatchMaxActions+1), ",") + `]}`,
	`{"mode":"act","actions":[{"type":"wait","window":"A","timeout_ms":40000},{"type":"wait","window":"B","timeout_ms":30000}]}`,
	`{"mode":"act","actions":["click r1"]}`,
	`{"mode":"act","window_ref":"w1","actions":[{"type":"wait","settle":true},{"type":"keypress","keys":["TAB"]}]}`,
	`{"mode":"act","actions":[{"type":"click","ref":"r1"},{"type":"wait","settle":"yes"}]}`,
	`{"mode":"act","actions":[{"type":"click","ref":"r1"},{"type":"wait","settle":true,"gone":true}]}`,
	`{"mode":"act","actions":[{"type":"close","ref":"w3"},{"type":"close","ref":"w2"}]}`,
	`{"mode":"act","actions":[{"type":"type","text":"` + strings.Repeat("a", 8000) + `"},{"type":"type","text":"` +
		strings.Repeat("b", 8000) + `"},{"type":"type","text":"` + strings.Repeat("c", 8000) + `"}]}`,
	`{"mode":"act","actions":[{"ref":"r1","action":"press"}]}`,
	`{"mode":"act","actions":[{"action":"zoom"}]}`,
	`{"mode":"act","actions":[{"type":"press","ref":"r1"}]}`,
	`{"mode":"act","actions":[{}]}`,
	`{"mode":"act","actions":[{"type":"click","target":{"ref":"r1"}}]}`,
	`{"mode":"act","actions":[{"type":"keypress","key":"CTRL+S"}]}`,
	`{"mode":"act","actions":[{"type":"click","ref":"r1","text":"x"}]}`,
	`{"mode":"act","actions":[{"type":"close","window_ref":"w2"}],"observe":"diff"}`,
	`{"mode":"act","actions":[{"type":"activate","window_ref":"w3"}],"observe":"none"}`,
	`{"mode":"act","actions":[{"type":"activate","ref":"w3","window_ref":"w3"}]}`,
	`{"mode":"act","actions":[{"type":"click","ref":"r??"}]}`,
	`{"mode":"act","actions":[{"type":"click","ref":"w1"}]}`,
	`{"mode":"act","actions":[{"type":"close","ref":"r1"}]}`,
	`{"mode":"act","actions":[{"type":"click"}]}`,
	`{"mode":"act","actions":[{"type":"set_value","ref":"r1"}]}`,
	`{"mode":"act","actions":[{"type":"set_value","ref":"r1","value":"` + strings.Repeat("v", 10001) + `"}]}`,
	`{"mode":"act","actions":[{"type":"select","to_ref":"r2"}]}`,
	`{"mode":"act","actions":[{"type":"select","ref":"r1","text":""}]}`,
	`{"mode":"act","actions":[{"type":"select","ref":"r1","text":"` + strings.Repeat("t", 10001) + `"}]}`,
	`{"mode":"act","actions":[{"type":"select","ref":"r1","to_ref":"r2","text":"x"}]}`,
	`{"mode":"act","actions":[{"type":"type","text":""}]}`,
	`{"mode":"act","actions":[{"type":"type","text":"` + strings.Repeat("t", 10001) + `"}]}`,
	`{"mode":"act","actions":[{"type":"scroll"}]}`,
	`{"mode":"act","actions":[{"type":"scroll","direction":"down","pages":0}]}`,
	`{"mode":"act","actions":[{"type":"scroll","direction":"down","pages":500}]}`,
	`{"mode":"act","actions":[{"type":"activate"}]}`,
	`{"mode":"act","actions":[{"type":"activate","ref":"w1","title":"Report"}]}`,
	`{"mode":"act","actions":[{"type":"activate","title":" "}]}`,
	`{"mode":"act","actions":[{"type":"keypress"}]}`,
	`{"mode":"act","actions":[{"type":"keypress","keys":"CTRL+S"}]}`,
	`{"mode":"act","actions":[{"type":"keypress","keys":[]}]}`,
	`{"mode":"act","actions":[{"type":"keypress","keys":[" CTRL"]}]}`,
	`{"mode":"act","actions":[{"type":"keypress","keys":["` + strings.Repeat("K", 65) + `"]}]}`,
	// until
	`{"mode":"observe","app":"x","until":"saved"}`,
	`{"mode":"observe","app":"x","until":{"type":"value_equals","ref":"r1"}}`,
	`{"mode":"observe","app":"x","until":{"type":"exists","ref":"r1","window":"w1"}}`,
	`{"mode":"observe","app":"x","until":{"type":"title","text":"x","ref":"r1"}}`,
	`{"mode":"observe","app":"x","until":{"type":"exists","ref":"r1","negate":"yes"}}`,
	`{"mode":"observe","app":"x","until":{"type":"title","text":"x","timeout_ms":60001}}`,
	`{"mode":"observe","app":"x","until":{"type":"title","text":"x","timeout_ms":-5}}`,
	`{"mode":"observe","app":"x","until":{"type":"title","text":"x","stable_ms":5001}}`,
	`{"mode":"observe","app":"x","until":{"type":"title"}}`,
	`{"mode":"observe","app":"x","until":{"type":"title","text":7}}`,
	`{"mode":"observe","app":"x","until":{"type":"state","ref":"r1"}}`,
	`{"mode":"observe","app":"x","until":{"type":"value","ref":"r1","value":"  "}}`,
	`{"mode":"observe","app":"x","until":{"type":"value","ref":"r1","value":"1","match":"regex"}}`,
	`{"mode":"observe","app":"x","until":{"type":"value","ref":"r1","value":3,"match":"contains"}}`,
	`{"mode":"observe","app":"x","until":{"type":"text","ref":"r1"}}`,
	`{"mode":"observe","app":"x","until":{"type":"exists"}}`,
	`{"mode":"observe","app":"x","until":{"type":"exists","ref":"r1","selector":{"role":"dialog"}}}`,
	`{"mode":"observe","app":"x","until":{"type":"exists","ref":"w1"}}`,
	`{"mode":"observe","app":"x","until":{"type":"exists","ref":"button"}}`,
	`{"mode":"observe","app":"x","until":{"type":"exists","selector":"dialog"}}`,
	`{"mode":"observe","app":"x","until":{"type":"exists","selector":{"role":"dialog","title":"Print"}}}`,
	`{"mode":"observe","app":"x","until":{"type":"exists","selector":{"role":"dialog","name":" "}}}`,
	`{"mode":"observe","app":"x","until":{"type":"exists","selector":{"role":"dialog","state":"active"}}}`,
	`{"mode":"observe","app":"x","until":{"type":"exists","selector":{"role":"a[b]"}}}`,
	`{"mode":"act","window_ref":"w3","actions":[{"type":"click","ref":"r22","until":{"type":"text","text":"Input & Codecs Settings","timeout_ms":5000}}],"observe":"tree"}`,
	`{"mode":"act","actions":[{"type":"click","ref":"r1","postcondition":{"type":"exists","ref":"r2"}}]}`,
}

// Every invalid_argument from the decoder has a hint that decodes: a whole
// call (starting with "mode") decodes as is; a fragment decodes when it
// replaces the object at the error path.
func TestEveryAXArgumentErrorHintDecodes(t *testing.T) {
	reached := map[int]bool{}
	first := false
	// Only the first rejection of a call is its error site; later ones come
	// from the decodes that derive the hint.
	axInvalidHook = func(file string, line int) {
		if first && filepath.Base(file) == "ax_public_request.go" {
			reached[line] = true
		}
		first = false
	}
	defer func() { axInvalidHook = nil }()
	for _, raw := range axBadCalls {
		args := decodeTestArguments(t, raw)
		first = true
		_, err := DecodeAXRequest(args)
		label := raw
		if len(label) > 160 {
			label = label[:160] + "..."
		}
		if err == nil || err.Code != contract.AXCodeInvalidArgument {
			t.Fatalf("%s: want invalid_argument, got %#v", label, err)
		}
		if err.Hint == "" || len(err.Hint) > contract.MaxArgumentHintBytes {
			t.Fatalf("%s: hint %q (%d bytes)", label, err.Hint, len(err.Hint))
		}
		if hintErr := axHintDecodeError(args, err); hintErr != nil {
			t.Fatalf("%s: hint %s for %s does not decode: %s (%s)", label, err.Hint, err.path, hintErr.Message, hintErr.path)
		}
	}
	source, readErr := os.ReadFile("ax_public_request.go")
	if readErr != nil {
		t.Fatal(readErr)
	}
	var missed []string
	for index, line := range strings.Split(string(source), "\n") {
		if strings.Contains(line, "axInvalid(") && !strings.Contains(line, "func axInvalid(") &&
			!strings.HasPrefix(strings.TrimSpace(line), "//") && !reached[index+1] {
			missed = append(missed, strings.TrimSpace(line))
		}
	}
	sort.Strings(missed)
	if len(missed) > 0 {
		t.Fatalf("no bad call reaches these rejection sites:\n%s", strings.Join(missed, "\n"))
	}
}

func axHintDecodeError(args map[string]any, err *axArgError) *axArgError {
	call, ok := axSpliceHint(args, err.path, err.Hint)
	if !ok {
		return &axArgError{path: err.path, Message: "hint is not a whole call and has no anchor at the error path"}
	}
	_, hintErr := decodeAXCall(call)
	return hintErr
}

// Six calls models wrote whose field errors had no hint, and the corrected
// call the model sent next.
func TestRecordedFieldErrorsHintTheNextCall(t *testing.T) {
	for raw, hint := range map[string]string{
		`{"mode":"apps","app":"soffice"}`:                                                                   `{"mode":"apps"}`,
		`{"mode":"apps","observe":"none"}`:                                                                  `{"mode":"apps"}`,
		`{"mode":"apps","observe":"tree"}`:                                                                  `{"mode":"apps"}`,
		`{"mode":"observe","window_ref":"w2","root":"w2","observe":"tree"}`:                                 `{"mode":"observe","window_ref":"w2","observe":"tree"}`,
		`{"mode":"act","actions":[{"type":"close","window_ref":"w2"}],"observe":"diff"}`:                    `{"type":"close","ref":"w2"}`,
		`{"mode":"act","actions":[{"type":"activate","window_ref":"w3"}],"observe":"none"}`:                 `{"type":"activate","ref":"w3"}`,
		`{"mode":"act","app":"vlc","actions":[{"type":"keypress","keys":["TAB"]}],"observe":"none"}`:        `{"mode":"act","actions":[{"type":"keypress","keys":["TAB"]}],"observe":"none"}`,
		`{"mode":"find","window_ref":"w3","query":{"name_contains":"record"},"scope":"window","limit":100}`: `{"mode":"find","window_ref":"w3","limit":50,"query":{"name_contains":"record"},"scope":"window"}`,
	} {
		_, err := DecodeAXRequest(decodeTestArguments(t, raw))
		if err == nil || err.Hint != hint {
			t.Fatalf("%s: got %#v, want hint %s", raw, err, hint)
		}
	}
}
