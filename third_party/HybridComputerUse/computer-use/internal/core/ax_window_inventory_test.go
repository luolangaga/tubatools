package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// windowScriptBackend answers apps listings from a script, one inventory per
// call (the last repeats), and records every other provider call.
type windowScriptBackend struct {
	*recordingPlatformBackend
	inventories [][]AppDescriptor
	listings    int
}

func (b *windowScriptBackend) ObserveApps(ProviderObserveAppsRequest) (ProviderAccessibilityResult, error) {
	index := b.listings
	if index >= len(b.inventories) {
		index = len(b.inventories) - 1
	}
	b.listings++
	return ProviderAccessibilityResult{OK: true, Apps: b.inventories[index]}, nil
}

const (
	testHandleReport = "w2:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
	testHandleNotes  = "w2:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
	testHandleMail   = "w2:cccccccccccccccccccccccccccccccc"
)

type testWindow struct {
	handle, title string
	active        bool
}

func editorApps(windows ...testWindow) []AppDescriptor {
	editor := AppDescriptor{
		Name: "editor", BundleIdentifier: "editor", PID: 10, InputCapable: true,
		ProviderBusName: ":1.10", ProcessGeneration: "pid:10:start:1",
	}
	mail := AppDescriptor{
		Name: "mail", BundleIdentifier: "mail", PID: 20, InputCapable: true,
		ProviderBusName: ":1.20", ProcessGeneration: "pid:20:start:1",
	}
	panel := AppDescriptor{Name: "panel", PID: 30, InputCapable: false}
	for _, window := range windows {
		entry := ProviderAppWindow{
			Title: window.title, Role: "frame", Handle: window.handle,
			ProviderBusName: ":1.10", ProviderObjectPath: "/w/" + window.title,
			ProcessGeneration: "pid:10:start:1", Active: window.active, InputTarget: true,
		}
		if window.handle == testHandleMail {
			entry.ProviderBusName = ":1.20"
			entry.ProcessGeneration = "pid:20:start:1"
			mail.Windows = append(mail.Windows, entry)
			continue
		}
		editor.Windows = append(editor.Windows, entry)
	}
	return []AppDescriptor{editor, mail, panel}
}

func windowScriptService(inventories ...[]AppDescriptor) (*Service, *windowScriptBackend) {
	backend := &windowScriptBackend{
		recordingPlatformBackend: &recordingPlatformBackend{},
		inventories:              inventories,
	}
	return NewServiceWithBackend(backend), backend
}

func TestAppsListsEveryWindowWithRefTitleAppAndActive(t *testing.T) {
	service, _ := windowScriptService(editorApps(
		testWindow{testHandleReport, "Quarterly report - Editor", true},
		testWindow{testHandleNotes, "Notes - Editor", false},
		testWindow{testHandleMail, "Inbox - Mail", false},
	))
	text := ResultText(service.listApps())
	for _, want := range []string{
		`w1 "Quarterly report - Editor"  app=editor  active`,
		`w2 "Notes - Editor"  app=editor`,
		`w3 "Inbox - Mail"  app=mail`,
		"(not input targets: panel)",
	} {
		if !strings.Contains(text, want) {
			t.Fatalf("apps output lacks %q:\n%s", want, text)
		}
	}
	// Windows are addressed by wN; the aN is not offered as an argument.
	if strings.Contains(text, " a1") || strings.Contains(text, " a2") {
		t.Fatalf("apps output presents an aN:\n%s", text)
	}
	// The same window keeps its ref on the next listing.
	if again := ResultText(service.listApps()); !strings.Contains(again, `w1 "Quarterly report - Editor"`) {
		t.Fatalf("window ref changed between listings:\n%s", again)
	}
}

type notedWindowBackend struct {
	*windowScriptBackend
	notes []string
}

func (b *notedWindowBackend) ObserveApps(request ProviderObserveAppsRequest) (ProviderAccessibilityResult, error) {
	result, err := b.windowScriptBackend.ObserveApps(request)
	result.Notes = b.notes
	return result, err
}

func TestAppsSaysWhenTheProviderListingIsIncomplete(t *testing.T) {
	_, script := windowScriptService(editorApps(
		testWindow{testHandleReport, "Quarterly report - Editor", true},
	))
	note := "Incomplete: 1 application(s) did not answer the accessibility bus; their windows may be missing above."
	service := NewServiceWithBackend(&notedWindowBackend{script, []string{note}})
	text := ResultText(service.listApps())
	if !strings.HasSuffix(strings.TrimSpace(text), note) ||
		!strings.Contains(text, `w1 "Quarterly report - Editor"`) {
		t.Fatalf("apps output lacks the incompleteness note after the windows:\n%s", text)
	}
}

func TestAppsSkipsWindowsWithoutStrongHandle(t *testing.T) {
	service, _ := windowScriptService(editorApps(
		testWindow{"", "Untracked - Editor", true},
	))
	text := ResultText(service.listApps())
	if !strings.Contains(text, `"Untracked - Editor"  app=editor  (not addressable)`) ||
		strings.Contains(text, "w1 ") {
		t.Fatalf("unaddressable window got a ref:\n%s", text)
	}
}

func TestActivateByTitleResolvesExactlyOneWindowBeforeInput(t *testing.T) {
	inventory := editorApps(
		testWindow{testHandleReport, "Quarterly report - Editor", false},
		testWindow{testHandleNotes, "Notes - Editor", true},
		testWindow{testHandleMail, "Inbox - Mail", false},
	)
	for name, test := range map[string]struct {
		title string
		code  string
		want  []string
	}{
		"none": {"Budget", contract.AXCodeWindowNotFound, []string{
			`no visible window title contains "Budget"`, `w3 "Inbox - Mail"`}},
		"many": {"editor", contract.AXCodeAmbiguousWindow, []string{
			`2 windows match title "editor"`, `w1 "Quarterly report - Editor"`, `w2 "Notes - Editor"`}},
	} {
		service, backend := windowScriptService(inventory)
		result := service.callTool(contract.PublicToolAX, map[string]any{
			"mode": "act", "actions": []any{
				map[string]any{"type": "activate", "title": test.title},
			},
		})
		if !result.IsError || result.StructuredContent[contract.AXPublicErrorCodeKey] != test.code ||
			result.StructuredContent[contract.AXPublicErrorPathKey] != "actions[0].title" {
			t.Fatalf("%s: %#v", name, result.StructuredContent)
		}
		for _, want := range test.want {
			if !strings.Contains(ResultText(result), want) {
				t.Fatalf("%s: message lacks %q: %s", name, want, ResultText(result))
			}
		}
		if len(backend.calls) != 0 || len(backend.actionRequests) != 0 {
			t.Fatalf("%s: input reached the provider: %v", name, backend.calls)
		}
		prepared := WithAXTextEvidence(PrepareAXEvidence(result))
		if !strings.HasPrefix(prepared.Content[0].Text, `{"ok":false,"code":"`+test.code+`","path":"actions[0].title"`) ||
			!strings.Contains(prepared.Content[0].Text, `"hint":`) {
			t.Fatalf("%s: evidence line %s", name, prepared.Content[0].Text)
		}
	}

	service, _ := windowScriptService(inventory)
	window, err := service.resolveWindowTitle("QUARTERLY")
	if err != nil || window.ref != "w1" || window.handle != testHandleReport {
		t.Fatalf("one case-insensitive match must resolve to w1: %#v %#v", window, err)
	}
}

func TestTitleResolvesOnceAndNeverRebinds(t *testing.T) {
	// The window list is read once, before input. A later listing in which
	// the resolved window is gone and another window has the same title
	// must not move the batch to that other window.
	before := editorApps(testWindow{testHandleReport, "Report - Editor", true})
	after := editorApps(testWindow{testHandleNotes, "Report - Editor", true})
	service, backend := windowScriptService(before, after)
	actions, argErr := decodeAXActions([]any{
		map[string]any{"type": "activate", "title": "Report"},
	})
	if argErr != nil {
		t.Fatal(argErr)
	}
	backend.err = errNoWindow
	result := service.axBatchDecoded(actions, observation{mode: contract.ObserveNone}, "")
	if backend.listings != 1 {
		t.Fatalf("title was resolved %d times, want once", backend.listings)
	}
	if !strings.Contains(ResultText(result), `title "Report" -> w1 "Report - Editor" (editor)`) {
		t.Fatalf("resolution note missing: %s", ResultText(result))
	}
	if actions[0].targetRef != "w1" {
		t.Fatalf("title resolved to %q", actions[0].targetRef)
	}
	if len(backend.actionRequests) != 0 {
		t.Fatalf("window action used a semantic action request: %#v", backend.actionRequests)
	}
}

func TestClosedWindowAfterTitleResolutionFailsAsStale(t *testing.T) {
	service, backend := windowScriptService(editorApps(
		testWindow{testHandleReport, "Report - Editor", true},
	))
	window, argErr := service.resolveWindowTitle("Report")
	if argErr != nil {
		t.Fatal(argErr)
	}
	// The window closes after resolution; its ref becomes stale. The batch
	// must fail on the ref, not search titles again.
	service.WindowRefRegistry.MarkLifecycle(window.ref, AXRefStale, "window_closed")
	result := service.axBatchBound([]axBatchAction{{actionType: "close", targetRef: window.ref}},
		observation{mode: contract.ObserveNone}, "", nil, 1)
	if !result.IsError || backend.listings != 1 || len(backend.calls) != 0 {
		t.Fatalf("closed window was not reported stale before input: %#v listings=%d calls=%v",
			result.StructuredContent, backend.listings, backend.calls)
	}
	if code := axPublicErrorCode(result.StructuredContent); code != contract.AXCodeStaleRef {
		t.Fatalf("code=%q want stale_ref: %#v", code, result.StructuredContent)
	}
}

func TestRefLessKeysBindToTheFocusedWindow(t *testing.T) {
	service, _ := windowScriptService(editorApps(
		testWindow{testHandleReport, "Report - Editor", false},
		testWindow{testHandleNotes, "Notes - Editor", true},
	))
	focus, argErr := service.resolveFocusedWindow()
	if argErr != nil || focus.ref != "w2" {
		t.Fatalf("focused window = %#v %#v", focus, argErr)
	}
	context, err := service.deriveAXBatchContext("", []axBatchAction{
		{actionType: "keypress", key: "CTRL+S"},
		{actionType: "type", text: "x"},
	}, focus)
	if err != nil || context.WindowRef != "w2" || context.WindowHandle != testHandleNotes ||
		!context.boundByFocus || context.expectedWindow == nil {
		t.Fatalf("focus binding = %#v %v", context, err)
	}
}

func TestRefLessKeysWithoutFocusedWindowFailBeforeInput(t *testing.T) {
	service, backend := windowScriptService(editorApps(
		testWindow{testHandleReport, "Report - Editor", false},
	))
	result := service.callTool(contract.PublicToolAX, map[string]any{
		"mode": "act", "actions": []any{
			map[string]any{"type": "keypress", "keys": []any{"ENTER"}},
		},
	})
	if !result.IsError || result.StructuredContent[contract.AXPublicErrorCodeKey] != contract.AXCodeNoFocusedWindow ||
		len(backend.calls) != 0 {
		t.Fatalf("no focused window: %#v calls=%v", result.StructuredContent, backend.calls)
	}
	if !strings.Contains(ResultText(result), `w1 "Report - Editor"`) ||
		result.StructuredContent[contract.AXPublicErrorHintKey] == "" {
		t.Fatalf("no_focused_window must list windows and hint: %s", ResultText(result))
	}
}

func TestFocusChangeBeforeFirstActionStopsWithoutInput(t *testing.T) {
	// Preflight sees w2 focused; by the time the first key is due, focus has
	// moved to w1.
	service, backend := windowScriptService(
		editorApps(
			testWindow{testHandleReport, "Report - Editor", false},
			testWindow{testHandleNotes, "Notes - Editor", true},
		),
		editorApps(
			testWindow{testHandleReport, "Report - Editor", true},
			testWindow{testHandleNotes, "Notes - Editor", false},
		),
	)
	actions, _ := decodeAXActions([]any{
		map[string]any{"type": "keypress", "keys": []any{"ENTER"}},
	})
	result := service.axBatchDecoded(actions, observation{mode: contract.ObserveNone}, "")
	if !result.IsError || result.StructuredContent[contract.AXPublicErrorCodeKey] != contract.AXCodeWindowChanged ||
		len(backend.actionRequests) != 0 {
		t.Fatalf("focus change before input: %#v requests=%d", result.StructuredContent, len(backend.actionRequests))
	}
	public := axPublicEvidence(result.StructuredContent, true, result.Content)
	if public["input_emitted"] != false || public["completed"] != 0 || public["requested"] != 1 {
		t.Fatalf("public counts: %#v", public)
	}
	if !strings.Contains(ResultText(result), `focus is now on w1 "Report - Editor"`) {
		t.Fatalf("message must name the new focus: %s", ResultText(result))
	}
}

func TestFocusChangeAfterActionKStopsAtKPlusOne(t *testing.T) {
	focused := editorApps(
		testWindow{testHandleReport, "Report - Editor", true},
	)
	moved := editorApps(
		testWindow{testHandleReport, "Report - Editor", false},
		testWindow{testHandleMail, "Inbox - Mail", true},
	)
	// Listings: preflight, check before action 1, check before action 2
	// (still focused), check before action 3 (moved).
	service, backend := windowScriptService(focused, focused, focused, moved)
	backend.accessibilityResponse = &ProviderAccessibilityResult{
		OK: true, Snapshot: &AppSnapshot{
			App: AppDescriptor{Name: "editor", BundleIdentifier: "editor", PID: 10,
				ProviderBusName: ":1.10", ProcessGeneration: "pid:10:start:1"},
			WindowTitle:  "Report - Editor",
			WindowHandle: testHandleReport,
			SnapshotID:   "report",
			TreeLines:    []string{"\t0 frame \"Report - Editor\""},
			Elements: []ElementRecord{{
				Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Report - Editor",
				ProviderWindowBusName: ":1.10", ProviderWindowObjectPath: "/w/Report - Editor",
				ProviderWindowProcessGeneration: "pid:10:start:1", ProviderWindowRole: "frame",
				ProviderWindowName: "Report - Editor",
			}},
		},
		Mutation:       &ProviderMutationFacts{InputEventsEmitted: true},
		ActionEvidence: &ProviderActionEvidence{InputEmitted: true, Route: "keyboard_synthesis"},
	}
	actions, _ := decodeAXActions([]any{
		map[string]any{"type": "keypress", "keys": []any{"A"}},
		map[string]any{"type": "keypress", "keys": []any{"B"}},
		map[string]any{"type": "keypress", "keys": []any{"C"}},
	})
	result := service.axBatchDecoded(actions, observation{mode: contract.ObserveNone}, "")
	if !result.IsError || result.StructuredContent[contract.AXPublicErrorCodeKey] != contract.AXCodeWindowChanged {
		t.Fatalf("focus change after action 2: %#v", result.StructuredContent)
	}
	if len(backend.actionRequests) != 2 {
		t.Fatalf("sent %d keys, want 2 before the stop", len(backend.actionRequests))
	}
	for _, request := range backend.actionRequests {
		if request.WindowHandle != testHandleReport || request.ExpectedWindow == nil {
			t.Fatalf("key not bound to the focused window: %#v", request)
		}
	}
	public := axPublicEvidence(result.StructuredContent, true, result.Content)
	if public["completed"] != 2 || public["requested"] != 3 || public["input_emitted"] != true {
		t.Fatalf("public counts: %#v", public)
	}
}

var errNoWindow = errorString("window action not modeled in this test")

type errorString string

func (e errorString) Error() string { return string(e) }

func TestPublicCountsReadTopLevelStopFacts(t *testing.T) {
	public := axPublicEvidence(map[string]any{
		"failure_code": "stale_target", "failure_phase": "between_actions",
		"actions_requested": 2, "actions_completed_without_call_error": 1,
		"input_events_emitted": true,
	}, true, []ContentItem{
		{Type: "text", Text: `bound_window: w2 "Doc" (app, focused at preflight)`},
		{Type: "text", Text: "batch_window_changed: action 1/2 completed"},
	})
	if public["code"] != contract.AXCodeWindowChanged || public["completed"] != 1 ||
		public["requested"] != 2 || public["message"] != "batch_window_changed: action 1/2 completed" {
		t.Fatalf("public evidence = %#v", public)
	}
}
