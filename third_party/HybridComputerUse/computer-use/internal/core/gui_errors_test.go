package core

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"regexp"
	"sort"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// guiLine is the first text line a model receives for a GUI result.
func guiLine(t *testing.T, result ToolCallResult) map[string]any {
	t.Helper()
	visible := FinalizeForModel(contract.PublicToolGUI, result, false)
	if len(visible.Content) == 0 || visible.Content[0].Type != "text" {
		t.Fatalf("GUI result has no evidence line: %#v", visible.Content)
	}
	line := visible.Content[0].Text
	var evidence map[string]any
	if err := json.Unmarshal([]byte(line), &evidence); err != nil {
		t.Fatalf("evidence line is not one JSON object: %q", line)
	}
	if !strings.HasPrefix(line, `{"ok":`) {
		t.Fatalf("evidence line does not start with ok: %q", line)
	}
	return evidence
}

func guiFailedResponse(t *testing.T, code string, emitted bool, dispatched int) ProviderDesktopResult {
	count := dispatched
	return ProviderDesktopResult{
		OK:    false,
		Error: "provider says " + code,
		Snapshot: &AppSnapshot{
			ScreenshotPNGBase64: testPNGBase64(t, 40, 30),
			CoordinateSpace:     "screen_image_px", FrameID: "f2", Width: 40, Height: 30,
		},
		GUIResult: ProviderGUIResult{Dispatched: &count, InputEventsEmitted: &emitted},
		Failure:   &ProviderFailure{Code: code, Phase: "precondition"},
	}
}

// Every GUI code is produced by a real GUI path, and every GUI error line
// carries a corrected GUI call or instructions for external recovery.
func TestEveryGUIErrorCodeHasAHint(t *testing.T) {
	seen := map[string]bool{}
	check := func(name string, result ToolCallResult, want string) {
		t.Helper()
		evidence := guiLine(t, result)
		if evidence["ok"] != false || evidence["code"] != want {
			t.Fatalf("%s: want code %s, got %#v", name, want, evidence)
		}
		hint, _ := evidence["hint"].(string)
		if message, _ := evidence["message"].(string); message == "" {
			t.Fatalf("%s: no message: %#v", name, evidence)
		}
		if want == "permission_denied" {
			for _, step := range []string{"permission", "restart", "screenshot"} {
				if !strings.Contains(hint, step) {
					t.Fatalf("%s: hint lacks %s: %q", name, step, hint)
				}
			}
			seen[want] = true
			return
		}
		var decoded map[string]any
		if json.Unmarshal([]byte(hint), &decoded) != nil {
			t.Fatalf("%s: hint is not a JSON object: %q", name, hint)
		}
		call := decoded
		if _, whole := decoded["actions"]; !whole {
			call = map[string]any{"actions": []any{decoded}}
		}
		if err := contract.ValidateToolArguments(contract.PublicToolGUI, guiJSONRoundTrip(t, call)); err != nil {
			t.Fatalf("%s: hint %s is not a valid GUI call: %s", name, hint, err.Message)
		}
		seen[want] = true
	}

	s := NewService()
	check("schema", s.callTool(contract.PublicToolGUI, map[string]any{
		"actions": []any{map[string]any{"type": "click", "x": 1.0, "y": 1.0, "button": "middle-ish"}},
	}), contract.AXCodeInvalidArgument)

	s.guiSnapshot = &AppSnapshot{FrameID: "f1"}
	check("stale before input", s.guiResponse(guiFailedResponse(t, "stale_gui_frame", false, 0)), contract.AXCodeGUIFrameStale)
	check("stale windows provider", s.guiResponse(guiFailedResponse(t, "stale_layout", false, 0)), contract.AXCodeGUIFrameStale)
	check("stale mid batch", s.guiResponse(guiFailedResponse(t, "stale_gui_frame", true, 1)), contract.AXCodeWindowChanged)
	check("input failure", s.guiResponse(guiFailedResponse(t, "", true, 1)), contract.AXCodeEffectUnknown)
	check("capture failure", s.guiResponse(guiFailedResponse(t, "", false, 0)), contract.AXCodeRuntimeError)
	check("permission refusal", s.guiResponse(guiFailedResponse(t, "permission_denied", false, 0)), "permission_denied")

	have := append([]string{}, guiPublicErrorCodes...)
	got := make([]string, 0, len(seen))
	for code := range seen {
		got = append(got, code)
	}
	sort.Strings(have)
	sort.Strings(got)
	if strings.Join(have, ",") != strings.Join(got, ",") {
		t.Fatalf("GUI codes %v, produced %v", have, got)
	}
	for _, code := range guiPublicErrorCodes {
		if _, ok := contract.PublicErrorCodes[code]; !ok {
			t.Fatalf("GUI code %s is not in the error table", code)
		}
	}
}

func TestGUICapturePermissionRecoveryPreservesInputEvidence(t *testing.T) {
	for _, emitted := range []bool{false, true} {
		t.Run(fmt.Sprintf("input_emitted_%t", emitted), func(t *testing.T) {
			count := 0
			if emitted {
				count = 1
			}
			response := guiFailedResponse(t, "permission_denied", emitted, count)
			response.Snapshot = nil
			response.GUIResult.InputActionsDispatched = &count
			response.GUIResult.PartialInputPossible = &emitted
			response.GUIResult.MutationUnknown = &emitted
			backend := &recordingPlatformBackend{desktopResponse: &response}
			s := NewServiceWithBackend(backend)
			s.guiSnapshot = &AppSnapshot{FrameID: "f1"}
			result := s.CallForModel(contract.PublicToolGUI, map[string]any{
				"actions": []any{map[string]any{"type": "keypress", "keys": []any{"ENTER"}}},
			}, false)
			if len(backend.desktopRequests) != 1 {
				t.Fatalf("permission denial retried the provider: %d requests", len(backend.desktopRequests))
			}
			var evidence map[string]any
			if err := json.Unmarshal([]byte(result.Content[0].Text), &evidence); err != nil {
				t.Fatal(err)
			}
			wantCode := "permission_denied"
			if emitted {
				wantCode = contract.AXCodeEffectUnknown
			}
			if evidence["code"] != wantCode || evidence["input_emitted"] != emitted ||
				evidence["completed"] != float64(count) || evidence["requested"] != float64(1) {
				t.Fatalf("permission error lost delivery evidence: %#v", evidence)
			}
			hint, _ := evidence["hint"].(string)
			for _, step := range []string{"Screen Recording", "Accessibility", "restart", "screenshot"} {
				if !strings.Contains(hint, step) {
					t.Fatalf("permission recovery lacks %s: %q", step, hint)
				}
			}
			if emitted {
				actions, _ := evidence["actions"].(map[string]any)
				if actions["partial_input_possible"] != true || actions["mutation_unknown"] != true {
					t.Fatalf("permission error lost partial-input warning: %#v", evidence)
				}
			}
		})
	}
}

func TestGUIIncompletePointerActionPreservesEvidenceAndInvalidatesRefs(t *testing.T) {
	for _, tc := range []struct {
		name    string
		emitted bool
		partial bool
	}{
		{"first event refused", false, false},
		{"first click completed", true, true},
		{"modifier pressed before pointer refusal", true, true},
		{"first event transport unknown", false, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			zero := 0
			response := guiFailedResponse(t, "", tc.emitted, 0)
			response.GUIResult.InputActionsDispatched = &zero
			response.GUIResult.PartialInputPossible = &tc.partial
			response.GUIResult.MutationUnknown = &tc.partial
			response.GUIResult.EffectUnknown = &tc.partial
			backend := &recordingPlatformBackend{desktopResponse: &response}
			s := NewServiceWithBackend(backend)
			s.guiSnapshot = &AppSnapshot{FrameID: "f1"}
			s.rememberSnapshot("Editor", expectSnapshot())
			result := s.callTool(contract.PublicToolGUI, map[string]any{
				"actions": []any{map[string]any{"type": "double_click", "x": 1.0, "y": 2.0}},
			})
			evidence := guiLine(t, result)
			wantCode := contract.AXCodeRuntimeError
			if tc.emitted || tc.partial {
				wantCode = contract.AXCodeEffectUnknown
			}
			if evidence["code"] != wantCode || evidence["input_emitted"] != tc.emitted ||
				evidence["completed"] != float64(0) || evidence["requested"] != float64(1) {
				t.Fatalf("incomplete action lost delivery facts: %#v", evidence)
			}
			if (s.currentSnapshot("Editor") == nil) != (tc.emitted || tc.partial) {
				t.Fatalf("snapshot invalidation disagrees with emission facts: %#v", evidence)
			}
			if len(backend.desktopRequests) != 1 {
				t.Fatalf("failed pointer action was retried: %d requests", len(backend.desktopRequests))
			}
			if evidence["hint"] != GUIScreenshotCall {
				t.Fatalf("failure must recover by observation, not repeat input: %#v", evidence)
			}
		})
	}
}

// The error table is exactly the AX codes plus the GUI codes, and the
// recovery list in hybrid.md names exactly the table's codes.
func TestErrorTableEqualsDocumentedCodes(t *testing.T) {
	union := map[string]bool{}
	for _, code := range append(append([]string{}, axPublicErrorCodes...), guiPublicErrorCodes...) {
		union[code] = true
	}
	table := make([]string, 0, len(contract.PublicErrorCodes))
	for code := range contract.PublicErrorCodes {
		table = append(table, code)
		if !union[code] {
			t.Fatalf("error table code %s is used by neither AX nor GUI", code)
		}
	}
	if len(union) != len(contract.PublicErrorCodes) {
		t.Fatalf("error table has %d codes, AX and GUI use %d", len(contract.PublicErrorCodes), len(union))
	}
	raw, err := os.ReadFile("../../../instructions/hybrid.md")
	if err != nil {
		t.Fatal(err)
	}
	section := string(raw)
	section = section[strings.Index(section, "## When Something Fails"):]
	bullet := regexp.MustCompile("(?m)^- ((?:`[a-z_]+`(?:, )?)+):")
	name := regexp.MustCompile("`([a-z_]+)`")
	var documented []string
	for _, match := range bullet.FindAllStringSubmatch(section, -1) {
		for _, code := range name.FindAllStringSubmatch(match[1], -1) {
			documented = append(documented, code[1])
		}
	}
	sort.Strings(table)
	sort.Strings(documented)
	if strings.Join(table, ",") != strings.Join(documented, ",") {
		t.Fatalf("hybrid.md lists %v; the error table is %v", documented, table)
	}
	for path, codes := range map[string][]string{
		"../../../docs/MCP.md":              table,
		"../../../instructions/gui-only.md": guiPublicErrorCodes,
	} {
		raw, err := os.ReadFile(path)
		if err != nil {
			t.Fatal(err)
		}
		for _, code := range codes {
			if !strings.Contains(string(raw), "`"+code+"`") {
				t.Fatalf("%s does not name %s", path, code)
			}
		}
	}
}

// A GUI success and a GUI error have the same leading fields as AX, and
// records read the same values.
func TestGUIRecordMatchesEvidenceLine(t *testing.T) {
	t.Setenv("COMPUTER_USE_OUTPUT_DIR", t.TempDir())
	s := NewServiceWithBackend(&recordingPlatformBackend{desktopResponse: func() *ProviderDesktopResult {
		response := guiFailedResponse(t, "stale_gui_frame", false, 0)
		return &response
	}()})
	s.guiSnapshot = &AppSnapshot{FrameID: "f1"}
	args := map[string]any{"actions": []any{map[string]any{"type": "click", "x": 5.0, "y": 5.0}}}
	text := s.CallForModel(contract.PublicToolGUI, args, false)
	line := text.Content[0].Text
	if !strings.HasPrefix(line, `{"ok":false,"code":"gui_frame_stale","message":`) ||
		!strings.Contains(line, `"input_emitted":false,"completed":0,"requested":1,"screen":{`) {
		t.Fatalf("stale GUI evidence line: %s", line)
	}
	if last := text.Content[len(text.Content)-1]; last.Type != "image" {
		t.Fatalf("stale GUI result lost its fresh screenshot: %#v", text.Content)
	}
	s.guiSnapshot = &AppSnapshot{FrameID: "f1"}
	record := s.CallRecord(contract.PublicToolGUI, args, CallOptions{})
	if record.OK || record.Code != contract.AXCodeGUIFrameStale || record.Hint != GUIScreenshotCall ||
		record.Screen == nil || record.Screen.Width != 40 || record.InputEmitted {
		encoded, _ := json.Marshal(record)
		t.Fatalf("stale GUI record: %s", encoded)
	}
}

func guiJSONRoundTrip(t *testing.T, value map[string]any) map[string]any {
	t.Helper()
	raw, err := json.Marshal(value)
	if err != nil {
		t.Fatal(err)
	}
	var out map[string]any
	if err := json.Unmarshal(raw, &out); err != nil {
		t.Fatal(err)
	}
	return out
}

// Every GUI rejection before input reports completed:0 and requested, like
// an AX preflight rejection, whichever check rejected it.
func TestGUIRejectionsReportCounts(t *testing.T) {
	s := NewService()
	for _, tc := range []struct {
		args      map[string]any
		requested float64
	}{
		{map[string]any{}, 0},
		{map[string]any{"actions": []any{}}, 0},
		{map[string]any{"actions": []any{map[string]any{"type": "screenshot"}, map[string]any{"type": "wait"}}}, 2},
		{map[string]any{"actions": []any{map[string]any{"type": "wait"}}, "settle_ms": "800"}, 1},
		{map[string]any{"actions": []any{map[string]any{"type": "wait"}}, "bogus": 1}, 1},
	} {
		// Only the schema gate rejects these shapes; dispatchValidated
		// bypasses it and is not a public path.
		for name, result := range map[string]ToolCallResult{
			"callTool": s.callTool(contract.PublicToolGUI, tc.args),
		} {
			evidence := guiLine(t, result)
			if evidence["code"] != contract.AXCodeInvalidArgument || evidence["completed"] != 0.0 ||
				evidence["requested"] != tc.requested {
				t.Fatalf("%s %v: %#v", name, tc.args, evidence)
			}
		}
	}
}

// A single unknown field is named in the path, so the hint (the action
// without it) and the path agree.
func TestGUIUnknownFieldPathNamesTheField(t *testing.T) {
	for _, err := range []error{
		errors.New(`GUI actions[0]: unknown field "ms"`),
		errors.New(`actions[0] wait does not accept fields: "ms"; accepted fields: type`),
	} {
		if got := guiErrorPath("actions[0]", err.Error()); got != "actions[0].ms" {
			t.Fatalf("%q: path %q", err, got)
		}
	}
	if got := guiErrorPath("actions[0]", `actions[0] click does not accept fields: "a", "b"`); got != "actions[0]" {
		t.Fatalf("two unknown fields: path %q", got)
	}
	result := guiArgumentError(errors.New(`GUI actions[0]: unknown field "ms"`),
		[]any{map[string]any{"type": "wait", "ms": 5.0}})
	evidence := guiLine(t, result)
	if evidence["path"] != "actions[0].ms" || evidence["hint"] != `{"type":"wait"}` {
		t.Fatalf("decoder unknown field: %#v", evidence)
	}
}

// A provider error that is neither a timeout, a transport failure nor a
// request rejection is an explicit runtime_error with a screenshot hint.
func TestGUIProviderErrorIsExplicitRuntimeError(t *testing.T) {
	s := NewServiceWithBackend(&recordingPlatformBackend{err: errors.New("provider exploded")})
	result := s.callTool(contract.PublicToolGUI, map[string]any{"actions": []any{map[string]any{"type": "screenshot"}}})
	if result.StructuredContent[contract.AXPublicErrorCodeKey] != contract.AXCodeRuntimeError {
		t.Fatalf("provider error code is implicit: %#v", result.StructuredContent)
	}
	evidence := guiLine(t, result)
	if evidence["code"] != contract.AXCodeRuntimeError || evidence["hint"] != GUIScreenshotCall ||
		evidence["message"] != "provider exploded" {
		t.Fatalf("provider error evidence: %#v", evidence)
	}
}
