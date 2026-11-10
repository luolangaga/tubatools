package core

import (
	"encoding/json"
	"os"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// Every until call models wrote in recorded OSWorld tasks (94 distinct
// calls) is rejected before input, and its hint is the same call
// written with wait steps: it decodes, keeps every input step in order, and
// puts each wait where the until was.
func TestPanelUntilCallsAreRejectedWithAnEquivalentWait(t *testing.T) {
	raw, err := os.ReadFile("../../testdata/until/panel_until_calls.json")
	if err != nil {
		t.Fatal(err)
	}
	var calls []map[string]any
	if err := json.Unmarshal(raw, &calls); err != nil {
		t.Fatal(err)
	}
	if len(calls) < 90 {
		t.Fatalf("panel corpus has %d calls", len(calls))
	}
	shapes := map[string]int{}
	for _, call := range calls {
		original, _ := json.Marshal(call)
		_, argErr := DecodeAXRequest(call)
		if argErr == nil || argErr.Code != contract.AXCodeInvalidArgument ||
			!strings.Contains(argErr.Message, `type \"wait\"`) && !strings.Contains(argErr.Message, `type "wait"`) {
			t.Fatalf("%s: not rejected with a wait answer: %#v", original, argErr)
		}
		hint := decodeJSONForTest(t, argErr.Hint)
		request, hintErr := DecodeAXRequest(hint)
		if hintErr != nil {
			t.Fatalf("%s: hint %s does not decode: %#v", original, argErr.Hint, hintErr)
		}
		inputs, waits := []string{}, 0
		for _, action := range request.actions {
			if action.actionType == "wait" {
				waits++
				shapes[action.wait.describeShape()]++
				continue
			}
			inputs = append(inputs, action.actionType+" "+action.targetRef+action.title)
		}
		want := []string{}
		items, _ := call["actions"].([]any)
		for _, item := range items {
			action := item.(map[string]any)
			want = append(want, action["type"].(string)+" "+OptionalString(action, "ref")+OptionalString(action, "title"))
		}
		if strings.Join(inputs, ",") != strings.Join(want, ",") || waits == 0 {
			t.Fatalf("%s: hint %s changed the input steps: %v want %v", original, argErr.Hint, inputs, want)
		}
		if call["mode"] == contract.AXModeAct {
			if ref, ok := call["window_ref"]; ok && hint["window_ref"] != ref {
				t.Fatalf("%s: hint dropped window_ref: %s", original, argErr.Hint)
			}
		}
	}
	t.Logf("wait shapes in converted panel calls: %v", shapes)
}

func (w *axWait) describeShape() string {
	shape := "text"
	switch {
	case w.window != "":
		shape = "window"
	case w.query != nil:
		shape = "query"
	case w.ref != "":
		shape = "ref"
		for _, field := range []struct{ name, value string }{{"value", w.value}, {"state", w.state}, {"text", w.text}} {
			if field.value != "" {
				shape += "+" + field.name
			}
		}
	}
	if w.gone {
		shape += " gone"
	}
	return shape
}

// The converter covers each until form. An action until follows its action;
// a call-level until follows the last action; observe until becomes a
// wait-only act.
func TestUntilConverterMapsEachForm(t *testing.T) {
	for name, test := range map[string]struct {
		call map[string]any
		want string
	}{
		"title on action": {map[string]any{"mode": "act", "window_ref": "w2", "actions": []any{
			map[string]any{"type": "keypress", "keys": []any{"ENTER"},
				"until": map[string]any{"type": "title", "text": "Preferences", "timeout_ms": 10000.0}},
			map[string]any{"type": "click", "ref": "r9"},
		}}, `{"mode":"act","window_ref":"w2","actions":[{"type":"keypress","keys":["ENTER"]},{"type":"wait","window":"Preferences","timeout_ms":10000},{"type":"click","ref":"r9"}]}`},
		"title gone on call": {map[string]any{"mode": "act", "observe": "none", "actions": []any{
			map[string]any{"type": "close", "ref": "w1"}},
			"until": map[string]any{"type": "title", "text": "Print", "negate": true}},
			`{"mode":"act","actions":[{"type":"close","ref":"w1"},{"type":"wait","window":"Print","gone":true}],"observe":"none"}`},
		"selector on observe": {map[string]any{"mode": "observe", "app": "a1",
			"until": map[string]any{"type": "exists", "selector": map[string]any{"role": "menu item", "name": "Paragraph"}}},
			`{"mode":"act","actions":[{"type":"wait","query":{"name_contains":"Paragraph","role":"menu item"}}]}`},
		"ref state": {map[string]any{"mode": "act", "actions": []any{
			map[string]any{"type": "click", "ref": "r5", "until": map[string]any{"type": "state", "ref": "r5", "state": "checked"}}}},
			`{"mode":"act","actions":[{"type":"click","ref":"r5"},{"type":"wait","ref":"r5","state":"checked"}]}`},
		"ref value contains": {map[string]any{"mode": "act", "actions": []any{
			map[string]any{"type": "set_value", "ref": "r5", "value": "auto save",
				"until": map[string]any{"type": "value", "ref": "r5", "value": "auto", "match": "contains"}}}},
			`{"mode":"act","actions":[{"type":"set_value","ref":"r5","value":"auto save"},{"type":"wait","ref":"r5","text":"auto"}]}`},
		"bare text": {map[string]any{"mode": "act", "actions": []any{
			map[string]any{"type": "click", "ref": "r31", "until": map[string]any{"type": "text", "text": "Account Settings"}}}},
			`{"mode":"act","actions":[{"type":"click","ref":"r31"},{"type":"wait","text":"Account Settings"}]}`},
		"selector gone": {map[string]any{"mode": "act", "actions": []any{
			map[string]any{"type": "keypress", "keys": []any{"ESC"}}},
			"until": map[string]any{"type": "exists", "selector": map[string]any{"role": "menu"}, "negate": true}},
			`{"mode":"act","actions":[{"type":"keypress","keys":["ESC"]},{"type":"wait","query":{"role":"menu"},"gone":true}]}`},
	} {
		_, argErr := DecodeAXRequest(test.call)
		if argErr == nil || argErr.Hint != test.want {
			t.Fatalf("%s: hint\n%s\nwant\n%s", name, hintOf(argErr), test.want)
		}
		if _, err := DecodeAXRequest(decodeJSONForTest(t, argErr.Hint)); err != nil {
			t.Fatalf("%s: hint does not decode: %#v", name, err)
		}
	}
}

func hintOf(err *axArgError) string {
	if err == nil {
		return "<accepted>"
	}
	return err.Hint
}

// waitDesktop is a desktop whose windows the test changes between polls.
type waitDesktop struct {
	*recordingPlatformBackend
	listings int
	// windowsAt returns the windows for the nth listing.
	windowsAt func(n int) []testWindow
}

func (b *waitDesktop) ObserveApps(ProviderObserveAppsRequest) (ProviderAccessibilityResult, error) {
	b.listings++
	return ProviderAccessibilityResult{OK: true, Apps: editorApps(b.windowsAt(b.listings)...)}, nil
}

func TestWaitWindowSeesAWindowOfAnyApplication(t *testing.T) {
	desktop := &waitDesktop{recordingPlatformBackend: &recordingPlatformBackend{},
		windowsAt: func(n int) []testWindow {
			if n < 3 {
				return []testWindow{{handle: testHandleReport, title: "Report", active: true}}
			}
			// The awaited window belongs to another application.
			return []testWindow{{handle: testHandleReport, title: "Report"},
				{handle: testHandleMail, title: "Export As", active: true}}
		}}
	s := NewServiceWithBackend(desktop)
	result := s.callTool(contract.PublicToolAX, map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "wait", "window": "export", "timeout_ms": 3000.0},
	}})
	line := publicLine(t, result)
	if result.IsError || !strings.HasPrefix(line, `{"ok":true,"input_emitted":false,"completed":1,"requested":1`) {
		t.Fatalf("wait window: %s\n%s", line, ResultText(result))
	}
	if !strings.Contains(ResultText(result), `wait: window w`) || !strings.Contains(ResultText(result), `"Export As"`) {
		t.Fatalf("the found window is not named with its wN: %s", ResultText(result))
	}
	if len(desktop.actionRequests) != 0 || desktop.listings < 3 {
		t.Fatalf("a wait-only act sent input or did not poll: actions=%d listings=%d", len(desktop.actionRequests), desktop.listings)
	}
}

// A window that closes, or an application that exits with it, satisfies
// gone; a window that stays fails with wait_timeout and says which window.
func TestWaitWindowGone(t *testing.T) {
	for _, closes := range []bool{true, false} {
		desktop := &waitDesktop{recordingPlatformBackend: &recordingPlatformBackend{},
			windowsAt: func(n int) []testWindow {
				if closes && n > 1 {
					return nil
				}
				return []testWindow{{handle: testHandleReport, title: "Print", active: true}}
			}}
		s := NewServiceWithBackend(desktop)
		result := s.callTool(contract.PublicToolAX, map[string]any{"mode": "act", "actions": []any{
			map[string]any{"type": "wait", "window": "Print", "gone": true, "timeout_ms": 600.0},
		}})
		line := publicLine(t, result)
		if closes && (result.IsError || !strings.HasPrefix(line, `{"ok":true`)) {
			t.Fatalf("closed window: %s", line)
		}
		if !closes && (!strings.HasPrefix(line, `{"ok":false,"code":"wait_timeout","path":"actions[0]"`) ||
			!strings.Contains(line, `still open: w1 \"Print\"`) ||
			!strings.Contains(line, `"hint":"{\"mode\":\"observe\",\"window_ref\":\"w1\"}"`)) {
			t.Fatalf("open window: %s", line)
		}
	}
}

// A wait that does not hold stops the batch like a failed action: earlier
// steps ran, later steps were not sent.
func TestWaitTimeoutStopsTheBatch(t *testing.T) {
	s, backend := reportService(t, reportMain())
	_, refs := reportObserve(t, s, "OK")
	result := s.callTool(contract.PublicToolAX, map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "click", "ref": refs["OK"]},
		map[string]any{"type": "wait", "query": map[string]any{"role": "dialog"}, "timeout_ms": 1.0},
		map[string]any{"type": "keypress", "keys": []any{"ENTER"}},
	}})
	line := publicLine(t, result)
	if !strings.HasPrefix(line, `{"ok":false,"code":"wait_timeout","path":"actions[1]"`) ||
		!strings.Contains(line, `"input_emitted":true,"completed":1,"requested":3`) {
		t.Fatalf("evidence line %s", line)
	}
	if len(backend.actionRequests) != 1 {
		t.Fatalf("steps after the failed wait were sent: %d", len(backend.actionRequests))
	}
	if text := ResultText(result); !strings.Contains(text, "ax_diff") && !strings.Contains(text, "ax:") {
		t.Fatalf("a failed wait must return the current state:\n%s", text)
	}
}

// A query wait uses find's matching: a substring of the name and the role
// as find prints it.
func TestWaitQueryMatchesLikeFind(t *testing.T) {
	s, backend := reportService(t, reportMain())
	_, refs := reportObserve(t, s, "OK")
	backend.onAction = func(b *reportBackend) {
		b.windows[0].active = false
		b.windows = append(b.windows, reportWindow{
			handle: reportDialogHandle, role: "dialog", title: "Paragraph...", path: "/window/2", active: true,
		})
	}
	result := s.callTool(contract.PublicToolAX, map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "click", "ref": refs["OK"]},
		map[string]any{"type": "wait", "query": map[string]any{"role": "dialog", "name_contains": "paragraph"}, "timeout_ms": 1.0},
	}})
	if line := publicLine(t, result); result.IsError || !strings.HasPrefix(line, `{"ok":true`) {
		t.Fatalf("query wait missed a dialog in another window of the app: %s\n%s", line, ResultText(result))
	}
}

// A ref wait checks the element's value, state, or text.
func TestWaitRefValueAndState(t *testing.T) {
	s, backend := reportService(t, reportMain())
	_, refs := reportObserve(t, s, "Size", "Interface")
	backend.onAction = func(b *reportBackend) { b.value, b.checked = "14", true }
	backend.accessibilityResponse = nil
	result := s.callTool(contract.PublicToolAX, map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "set_value", "ref": refs["Size"], "value": "14"},
		map[string]any{"type": "wait", "ref": refs["Size"], "value": "14", "timeout_ms": 1.0},
		map[string]any{"type": "wait", "ref": refs["Interface"], "state": "checked", "timeout_ms": 1.0},
	}})
	if line := publicLine(t, result); result.IsError || !strings.HasPrefix(line, `{"ok":true`) ||
		strings.Contains(line, "effect_unknown") {
		t.Fatalf("ref waits: %s\n%s", line, ResultText(result))
	}
}

// After a wait for a window, keys without ref go to that window.
func TestKeysAfterAWindowWaitGoToThatWindow(t *testing.T) {
	s, backend := reportService(t, reportMain())
	_, refs := reportObserve(t, s, "OK")
	backend.onAction = func(b *reportBackend) {
		if len(b.windows) == 1 {
			b.windows[0].active = false
			b.windows = append(b.windows, reportWindow{
				handle: reportDialogHandle, role: "dialog", title: "Export", path: "/window/2", active: true,
			})
		}
	}
	desktop := &reportDesktop{reportBackend: backend}
	s.Backend = desktop
	result := s.callTool(contract.PublicToolAX, map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "click", "ref": refs["OK"]},
		map[string]any{"type": "wait", "window": "Export", "timeout_ms": 1.0},
		map[string]any{"type": "keypress", "keys": []any{"ENTER"}},
	}})
	if result.IsError || len(backend.actionRequests) != 2 {
		t.Fatalf("batch after a window wait: %s", ResultText(result))
	}
	if got := backend.actionRequests[1].WindowHandle; got != reportDialogHandle {
		t.Fatalf("keys after the wait went to %q, not the awaited window", got)
	}
}

// reportDesktop lists the fake application's windows for window waits.
type reportDesktop struct {
	*reportBackend
}

func (b *reportDesktop) ObserveApps(ProviderObserveAppsRequest) (ProviderAccessibilityResult, error) {
	app := AppDescriptor{Name: "Editor", PID: 10, InputCapable: true,
		ProviderBusName: ":1.84", ProcessGeneration: "pid:10:start:100"}
	for _, window := range b.windows {
		app.Windows = append(app.Windows, ProviderAppWindow{
			Title: window.title, Role: window.role, Handle: window.handle,
			ProviderBusName: ":1.84", ProviderObjectPath: window.path,
			ProcessGeneration: "pid:10:start:100", Active: window.active, InputTarget: true,
		})
	}
	return ProviderAccessibilityResult{OK: true, Apps: []AppDescriptor{app}}, nil
}

// A wait-only act with no window and no focus says so before reading.
func TestWaitOnlyActWithoutAWindowNeedsOne(t *testing.T) {
	desktop := &waitDesktop{recordingPlatformBackend: &recordingPlatformBackend{},
		windowsAt: func(int) []testWindow { return []testWindow{{handle: testHandleReport, title: "Report"}} }}
	s := NewServiceWithBackend(desktop)
	result := s.callTool(contract.PublicToolAX, map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "wait", "text": "Saved"},
	}})
	line := publicLine(t, result)
	if !strings.HasPrefix(line, `{"ok":false,"code":"no_focused_window"`) || !strings.Contains(line, `\"window_ref\":\"w1\"`) {
		t.Fatalf("evidence line %s", line)
	}
}

// The public error codes are one list: every code maps from its internal
// facts with a hint (TestEveryPublicErrorCodeHasAHint), and the docs and the
// instructions name exactly these codes and no removed one.
// TestErrorTableEqualsDocumentedCodes checks that the docs name every
// current code; this keeps the removed until codes out of them.
func TestRemovedUntilCodesStayUndocumented(t *testing.T) {
	for _, path := range []string{"../../../docs/MCP.md", "../../../instructions/hybrid.md"} {
		raw, err := os.ReadFile(path)
		if err != nil {
			t.Fatal(err)
		}
		text := string(raw)
		for _, removed := range []string{"until_failed", "until_unknown", `"until":`} {
			if strings.Contains(text, removed) {
				t.Fatalf("%s still names %s", path, removed)
			}
		}
	}
}

// A ref wait that the read after the action already answers passes without
// polling the live resolver.
func TestRefWaitAnsweredByTheLatestReadDoesNotPoll(t *testing.T) {
	s, backend := reportService(t, reportMain())
	_, refs := reportObserve(t, s, "Interface")
	backend.onAction = func(b *reportBackend) { b.checked = true }
	result := s.callTool(contract.PublicToolAX, map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "click", "ref": refs["Interface"]},
		map[string]any{"type": "wait", "ref": refs["Interface"], "state": "checked", "timeout_ms": 5000.0},
	}})
	if result.IsError || len(backend.actionRequests) != 1 {
		t.Fatalf("wait: %s", ResultText(result))
	}
	if !strings.Contains(ResultText(result), "wait: "+refs["Interface"]+" to be checked") {
		t.Fatalf("wait line missing: %s", ResultText(result))
	}
}

// When only the read after an action failed (input was sent), a following
// wait decides: here the awaited window is gone, so the batch succeeds.
// Without a following wait, the same action stops the batch as before. An
// input step after such a wait still stops on the unconfirmed window.
func TestWaitDecidesWhenOnlyTheReadbackFailed(t *testing.T) {
	for _, withWait := range []bool{true, false} {
		s, backend := reportService(t, reportMain())
		_, refs := reportObserve(t, s, "OK")
		backend.loseReadback = true
		desktop := &reportDesktop{reportBackend: backend}
		s.Backend = desktop
		actions := []any{map[string]any{"type": "click", "ref": refs["OK"]}}
		if withWait {
			actions = append(actions, map[string]any{"type": "wait", "window": "Print", "gone": true, "timeout_ms": 1.0})
		} else {
			actions = append(actions, map[string]any{"type": "keypress", "keys": []any{"TAB"}})
		}
		line := publicLine(t, s.callTool(contract.PublicToolAX, map[string]any{"mode": "act", "actions": actions}))
		if withWait && (!strings.HasPrefix(line, `{"ok":true,"input_emitted":true,"completed":2,"requested":2`) ||
			strings.Contains(line, "effect_unknown") || strings.Contains(line, `"observation_required":true`)) {
			t.Fatalf("the wait did not decide: %s", line)
		}
		if !withWait && !strings.HasPrefix(line, `{"ok":false,"code":"effect_unknown"`) {
			t.Fatalf("without a wait the lost readback must stop the batch: %s", line)
		}
	}
}
