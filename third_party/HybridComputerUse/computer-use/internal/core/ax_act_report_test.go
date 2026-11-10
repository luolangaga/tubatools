package core

import (
	"regexp"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

const (
	reportMainHandle   = "w2:0123456789abcdef0123456789abcdef"
	reportDialogHandle = "w2:fedcba9876543210fedcba9876543210"
	reportOtherHandle  = "w2:00112233445566778899aabbccddeeff"
)

// reportWindow is one top-level window of the fake application.
type reportWindow struct {
	handle, role, title, path string
	active                    bool
}

// reportBackend serves one application. Each read returns the current
// state: which windows are open and which control values they show. A
// click or set_value applies the next queued change before the provider
// reads the window back, as the real provider does.
type reportBackend struct {
	recordingPlatformBackend
	windows []reportWindow
	checked bool
	value   string
	// onAction mutates the state when input arrives.
	onAction func(b *reportBackend)
	// skipReadback makes an action return no snapshot (observe "none").
	skipReadback bool
	// loseReadback makes the read after an action fail after input.
	loseReadback bool
	// page is the title of the settings page the window shows.
	page string
	// menuOpen exposes the items of the "Edit" menu.
	menuOpen bool
}

func (b *reportBackend) snapshot(selected reportWindow) *ProviderSnapshot {
	checked := "[unchecked]"
	if b.checked {
		checked = "[checked]"
	}
	setValue := true
	snapshot := &AppSnapshot{
		App: AppDescriptor{Name: "Editor", PID: 10, ProviderBusName: ":1.84",
			ProcessGeneration: "pid:10:start:100", InputCapable: true},
		WindowTitle: selected.title, WindowHandle: selected.handle,
		SnapshotID: "report-" + selected.path + "-" + b.value + checked,
		TreeLines: []string{
			"\t0 " + selected.role + " \"" + selected.title + "\"",
			"\t\t1 check box \"Interface\" " + checked,
			"\t\t2 entry \"Size\" [editable]",
			"\t\t3 push button \"OK\" [click]",
		},
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: selected.role, Name: selected.title},
			{Index: 1, RuntimeID: []int{0, 0}, ControlType: "check box", Name: "Interface", States: checked},
			{Index: 2, RuntimeID: []int{0, 1}, ControlType: "entry", Name: "Size", Value: b.value,
				States: "[editable focusable]", SetValueSupported: &setValue},
			{Index: 3, RuntimeID: []int{0, 2}, ControlType: "push button", Name: "OK", States: "[has-click-action]"},
			{Index: 4, RuntimeID: []int{0, 3}, ControlType: "label", Name: FirstNonEmpty(b.page, "General")},
			{Index: 5, RuntimeID: []int{0, 4}, ControlType: "menu item", Name: "Edit", States: "[has-click-action]"},
		},
		Scope: contract.AXScopeWindow,
	}
	snapshot.TreeLines = append(snapshot.TreeLines,
		"\t\t4 label \""+FirstNonEmpty(b.page, "General")+"\"",
		"\t\t5 menu item \"Edit\" [has-click-action]")
	if b.menuOpen {
		snapshot.Elements = append(snapshot.Elements,
			ElementRecord{Index: 6, RuntimeID: []int{0, 4, 0}, ControlType: "menu item",
				Name: "Preferences", States: "[has-click-action]"},
			ElementRecord{Index: 7, RuntimeID: []int{0, 4, 1}, ControlType: "menu item",
				Name: "Undo", States: "[has-click-action]"})
		snapshot.TreeLines = append(snapshot.TreeLines,
			"\t\t\t6 menu item \"Preferences\" [has-click-action]",
			"\t\t\t7 menu item \"Undo\" [has-click-action]")
	}
	for index := range snapshot.Elements {
		element := &snapshot.Elements[index]
		element.ProviderBusName = ":1.84"
		element.ProcessGeneration = "pid:10:start:100"
		element.ProviderObjectPath = selected.path + "/" + itoa(index)
		element.ProviderWindowBusName = ":1.84"
		element.ProviderWindowObjectPath = selected.path
		element.ProviderWindowProcessGeneration = "pid:10:start:100"
		element.ProviderWindowRole = selected.role
		element.ProviderWindowName = selected.title
	}
	for _, window := range b.windows {
		snapshot.Windows = append(snapshot.Windows, TopLevelWindow{
			Title: window.title, Role: window.role, Handle: window.handle,
			ProviderBusName: ":1.84", ProviderObjectPath: window.path,
			ProviderProcessGeneration: "pid:10:start:100",
			Selected:                  window.handle == selected.handle,
			Active:                    window.active, Showing: true,
		})
	}
	return snapshot
}

func (b *reportBackend) read(handle string) ProviderAccessibilityResult {
	for _, window := range b.windows {
		if window.handle == handle || (handle == "" && window.role != "dialog") {
			return ProviderAccessibilityResult{OK: true, Snapshot: b.snapshot(window)}
		}
	}
	result := ProviderAccessibilityResult{OK: false, Error: "stale_window_ref: gone",
		Failure: &ProviderFailure{Code: "stale_window_ref", Phase: "precondition", Subject: "window", FactsComplete: true}}
	result.PrepareForService()
	return result
}

func (b *reportBackend) Observe(request ProviderObserveRequest) (ProviderAccessibilityResult, error) {
	b.observeRequests = append(b.observeRequests, request)
	return b.read(request.WindowHandle), nil
}

func (b *reportBackend) ResolveAndAct(request ProviderActionRequest) (ProviderAccessibilityResult, error) {
	b.actionRequests = append(b.actionRequests, request)
	if b.onAction != nil {
		b.onAction(b)
	}
	result := b.read(request.WindowHandle)
	if b.skipReadback {
		result = ProviderAccessibilityResult{OK: true, ObservationSkipped: true}
	}
	if b.loseReadback {
		result = ProviderAccessibilityResult{OK: true}
	}
	result.Mutation = &ProviderMutationFacts{InputEventsEmitted: true, DesktopStateMutated: true}
	result.ActionEvidence = &ProviderActionEvidence{InputEmitted: true, Route: "semantic_action"}
	return result, nil
}

func (b *reportBackend) ListWindows(ProviderWindowListRequest) (ProviderWindowResult, error) {
	titles := []string{}
	for _, window := range b.windows {
		titles = append(titles, window.title)
	}
	return ProviderWindowResult{OK: true, AppName: "Editor", AppPID: 10, WindowTitles: titles}, nil
}

func reportService(t *testing.T, windows ...reportWindow) (*Service, *reportBackend) {
	t.Helper()
	s := NewService()
	backend := &reportBackend{windows: windows, value: "12"}
	s.Backend = backend
	return s, backend
}

func reportMain() reportWindow {
	return reportWindow{handle: reportMainHandle, role: "frame", title: "Report", path: "/window/1", active: true}
}

// reportObserve reads the main window and returns its wN and the rN of a
// named element from the tree.
func reportObserve(t *testing.T, s *Service, names ...string) (string, map[string]string) {
	t.Helper()
	result := s.callTool(contract.PublicToolAX, map[string]any{"mode": "observe", "app": "Editor", "observe": "tree"})
	text := ResultText(result)
	if result.IsError {
		t.Fatalf("observe: %s", text)
	}
	window := regexp.MustCompile(`window: "[^"]*" \[ref=(w[0-9]+)\]`).FindStringSubmatch(text)
	if window == nil {
		t.Fatalf("no window ref in %s", text)
	}
	refs := map[string]string{}
	for _, name := range names {
		match := regexp.MustCompile(`"` + regexp.QuoteMeta(name) + `"[^\n]*\[ref=(r[0-9]+)\]`).FindStringSubmatch(text)
		if match == nil {
			t.Fatalf("no ref for %q in %s", name, text)
		}
		refs[name] = match[1]
	}
	return window[1], refs
}

// visibleAct runs an act call through the public result path and returns
// the evidence line and the text lines after it.
func visibleAct(t *testing.T, s *Service, args map[string]any) (string, []string) {
	t.Helper()
	result := WithAXTextEvidence(PrepareAXEvidence(s.callTool(contract.PublicToolAX, args)))
	lines := []string{}
	for _, item := range result.Content {
		if item.Type == "text" {
			lines = append(lines, strings.Split(strings.TrimSpace(item.Text), "\n")...)
		}
	}
	return lines[0], lines[1:]
}

func TestActReportShowsTheTargetStateChange(t *testing.T) {
	s, backend := reportService(t, reportMain())
	_, refs := reportObserve(t, s, "Interface")
	backend.onAction = func(b *reportBackend) { b.checked = true }
	line, rest := visibleAct(t, s, map[string]any{
		"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": refs["Interface"]}},
	})
	want := "effect: " + refs["Interface"] + ` checkbox "Interface" checked=false→true` +
		" (only state flags changed in this window)"
	if rest[0] != want || rest[1] != "windows: no change" {
		t.Fatalf("report:\n%s\nwant first line %q", strings.Join(rest, "\n"), want)
	}
	if !strings.HasPrefix(line, `{"ok":true,"input_emitted":true`) || strings.Contains(line, "effect_unknown") {
		t.Fatalf("evidence line %s", line)
	}
}

// A checkbox that switches a settings page reports the page change by not
// carrying the state-only remark.
func TestActReportStateChangeWithAPageSwitch(t *testing.T) {
	s, backend := reportService(t, reportMain())
	_, refs := reportObserve(t, s, "Interface")
	backend.onAction = func(b *reportBackend) { b.checked, b.page = true, "Input & Codecs" }
	_, rest := visibleAct(t, s, map[string]any{
		"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": refs["Interface"]}},
	})
	if want := "effect: " + refs["Interface"] + ` checkbox "Interface" checked=false→true`; rest[0] != want {
		t.Fatalf("report %q want %q", rest[0], want)
	}
}

// A menu whose own states do not change when it opens (GTK2 menus report
// none) still shows its response: items appeared under it.
func TestActReportCountsItemsThatAppearUnderAnUnchangedElement(t *testing.T) {
	s, backend := reportService(t, reportMain())
	_, refs := reportObserve(t, s, "Edit")
	backend.onAction = func(b *reportBackend) { b.menuOpen = true }
	line, rest := visibleAct(t, s, map[string]any{
		"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": refs["Edit"]}},
	})
	if want := "effect: " + refs["Edit"] + ` menuitem "Edit" itself unchanged; 2 nodes appeared under it`; rest[0] != want {
		t.Fatalf("report %q want %q", rest[0], want)
	}
	if strings.Contains(line, `"effect_unknown":true`) {
		t.Fatalf("items that appeared under the element are its observed effect: %s", line)
	}
}

// A change elsewhere in the window is reported but not attributed to the
// element: the element's own effect stays unconfirmed.
func TestActReportNamesChangesElsewhereWithoutClaimingThem(t *testing.T) {
	s, backend := reportService(t, reportMain())
	_, refs := reportObserve(t, s, "OK")
	backend.onAction = func(b *reportBackend) { b.page = "Advanced" }
	result := s.callTool(contract.PublicToolAX, map[string]any{
		"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": refs["OK"]}},
	})
	if want := "effect: " + refs["OK"] + ` button "OK" itself unchanged; 1 node changed elsewhere`; !strings.Contains(ResultText(result), want) {
		t.Fatalf("report %q want %q", ResultText(result), want)
	}
	if result.StructuredContent["target_effect_observed"] == true {
		t.Fatalf("a change elsewhere must not confirm the element's effect")
	}
}

func TestActReportShowsAValueChange(t *testing.T) {
	s, backend := reportService(t, reportMain())
	_, refs := reportObserve(t, s, "Size")
	backend.onAction = func(b *reportBackend) { b.value = "14" }
	_, rest := visibleAct(t, s, map[string]any{
		"mode": "act", "actions": []any{map[string]any{"type": "set_value", "ref": refs["Size"], "value": "14"}},
	})
	if want := "effect: " + refs["Size"] + ` textbox "Size" value="12"→"14"`; rest[0] != want {
		t.Fatalf("report %q want %q", rest[0], want)
	}
}

// An action whose element did not change says so; the evidence line then
// keeps effect_unknown, because nothing confirms the effect.
func TestActReportSaysNoVisibleChange(t *testing.T) {
	s, _ := reportService(t, reportMain())
	_, refs := reportObserve(t, s, "OK")
	line, rest := visibleAct(t, s, map[string]any{
		"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": refs["OK"]}},
	})
	if want := "effect: " + refs["OK"] + ` button "OK" no visible change`; rest[0] != want {
		t.Fatalf("report %q want %q", rest[0], want)
	}
	if !strings.Contains(line, `"effect_unknown":true`) {
		t.Fatalf("an unconfirmed effect must stay unknown: %s", line)
	}
}

func TestActReportIsUnknownWithoutAReadback(t *testing.T) {
	s, backend := reportService(t, reportMain())
	_, refs := reportObserve(t, s, "Interface")
	backend.skipReadback = true
	_, rest := visibleAct(t, s, map[string]any{
		"mode": "act", "observe": "none",
		"actions": []any{map[string]any{"type": "click", "ref": refs["Interface"]}},
	})
	if !strings.HasPrefix(rest[0], "effect: "+refs["Interface"]+` checkbox "Interface" unknown (observe "none"`) ||
		!strings.HasPrefix(rest[1], "windows: unknown (") {
		t.Fatalf("report:\n%s", strings.Join(rest, "\n"))
	}
}

func TestActReportNamesAnOpenedDialogWithItsRef(t *testing.T) {
	main := reportMain()
	s, backend := reportService(t, main)
	_, refs := reportObserve(t, s, "OK")
	backend.onAction = func(b *reportBackend) {
		b.windows[0].active = false
		b.windows = append(b.windows, reportWindow{
			handle: reportDialogHandle, role: "dialog", title: "Insert Sheet", path: "/window/2", active: true,
		})
	}
	_, rest := visibleAct(t, s, map[string]any{
		"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": refs["OK"]}},
	})
	opened := regexp.MustCompile(`^windows: opened (w[0-9]+) "Insert Sheet" \(dialog, focused\)$`).FindStringSubmatch(rest[1])
	if opened == nil {
		t.Fatalf("windows line %q", rest[1])
	}
	// The new wN is usable at once.
	if entry, err := s.WindowRefRegistry.Resolve(opened[1], ""); err != nil || entry.WindowHandle != reportDialogHandle {
		t.Fatalf("%s does not resolve to the dialog: %v", opened[1], err)
	}
}

func TestActReportNamesAClosedWindowAndTheFocus(t *testing.T) {
	main := reportMain()
	main.active = false
	dialog := reportWindow{handle: reportDialogHandle, role: "dialog", title: "Print", path: "/window/2", active: true}
	s, backend := reportService(t, main, dialog)
	// Read the dialog's sibling list through the main window, then act in it.
	window, refs := reportObserve(t, s, "OK")
	snapshot := s.currentSnapshot("Editor")
	dialogRef := ""
	for _, item := range snapshot.Windows {
		if item.Handle == reportDialogHandle {
			dialogRef = item.publicRef
		}
	}
	backend.onAction = func(b *reportBackend) {
		b.windows = []reportWindow{main}
		b.windows[0].active = true
	}
	_, rest := visibleAct(t, s, map[string]any{
		"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": refs["OK"]}},
	})
	want := `windows: closed ` + dialogRef + ` "Print"; focus ` + window
	if rest[1] != want {
		t.Fatalf("windows line %q want %q", rest[1], want)
	}
}

// A call rejected before input has nothing to report.
func TestActReportIsAbsentWhenNothingWasSent(t *testing.T) {
	s, _ := reportService(t, reportMain())
	reportObserve(t, s)
	_, rest := visibleAct(t, s, map[string]any{
		"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": "r999"}},
	})
	for _, line := range rest {
		if isActReportText(line) {
			t.Fatalf("report on a rejected call: %q", line)
		}
	}
}

// A verified effect never says effect_unknown on the same line: neither
// when the action until held, nor when the target visibly changed.
func TestVerifiedEffectIsNotAlsoUnknown(t *testing.T) {
	for _, full := range []map[string]any{
		{"input_events_emitted": true, "postcondition_verified": true, "effect_unknown": true,
			"mutation_unknown": true, "cause_unattributed": true},
		{"input_events_emitted": true, "target_effect_observed": true, "effect_unknown": true,
			"mutation_unknown": true},
	} {
		result := PrepareAXEvidence(ToolCallResult{StructuredContent: full})
		for _, key := range []string{"effect_unknown", "mutation_unknown", "cause_unattributed"} {
			if _, present := result.StructuredContent[key]; present {
				t.Fatalf("%s next to a verified effect: %#v", key, result.StructuredContent)
			}
		}
	}
	// An error keeps its uncertainty.
	failed := PrepareAXEvidence(ToolCallResult{IsError: true, StructuredContent: map[string]any{
		"input_events_emitted": true, "target_effect_observed": true, "effect_unknown": true,
	}})
	if failed.StructuredContent["effect_unknown"] != true {
		t.Fatalf("a failed call lost effect_unknown: %#v", failed.StructuredContent)
	}
}

// One element that changed does not vouch for a keypress in the same call.
func TestActReportKeepsUncertaintyOfAnUnreportedAction(t *testing.T) {
	s, backend := reportService(t, reportMain())
	_, refs := reportObserve(t, s, "Interface")
	backend.onAction = func(b *reportBackend) { b.checked = true }
	result := s.callTool(contract.PublicToolAX, map[string]any{
		"mode": "act", "actions": []any{
			map[string]any{"type": "click", "ref": refs["Interface"]},
			map[string]any{"type": "keypress", "keys": []any{"TAB"}},
		},
	})
	if result.StructuredContent["target_effect_observed"] == true {
		t.Fatalf("a keypress effect was counted as observed: %s", ResultText(result))
	}
}

// Closing a window listed by apps, before any read of its application,
// still names the closed window instead of reporting unknown.
func TestActReportNamesAClosedWindowWithoutAPriorRead(t *testing.T) {
	service, backend := windowScriptService(editorApps(
		testWindow{testHandleReport, "Report - Editor", true},
	))
	window, argErr := service.resolveWindowTitle("Report")
	if argErr != nil {
		t.Fatal(argErr)
	}
	backend.windowResponse = &ProviderWindowResult{
		OK: true, Snapshot: &ProviderSnapshot{WindowAbsent: true, ClosedWindowHandle: testHandleReport},
		Mutation:       &ProviderMutationFacts{InputEventsEmitted: true, DesktopStateMutated: true},
		ActionEvidence: &ProviderActionEvidence{InputEmitted: true, Route: "native_window_command"},
	}
	// The read after the close proves the window is gone.
	backend.accessibilityResponses = []ProviderAccessibilityResult{{
		OK: false, Error: "stale_window_ref: source window is gone",
		Failure:         &ProviderFailure{Code: "stale_window_ref", Phase: "precondition", FactsComplete: true, Subject: "window"},
		RecoveryContext: &ProviderRecoveryContext{SourceAppStatus: "absent", SourceWindowStatus: "gone", InventoryComplete: true},
	}}
	result := service.callTool(contract.PublicToolAX, map[string]any{"mode": "act",
		"actions": []any{map[string]any{"type": "close", "ref": window.ref}}})
	text := ResultText(WithAXTextEvidence(PrepareAXEvidence(result)))
	if !strings.Contains(text, `windows: closed `+window.ref+` "Report - Editor"`) {
		t.Fatalf("closed window not named:\n%s", text)
	}
}
