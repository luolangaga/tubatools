package core

import (
	"regexp"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// The tests in this file come from AX errors models made in recorded tasks.
// Each case is a call that was rejected, or whose error left the model
// without the next call.

const (
	recoveryMainHandle   = "w2:11111111111111111111111111111111"
	recoveryDialogHandle = "w2:22222222222222222222222222222222"
)

// recoveryWindow is one top-level window of the fake Writer.
type recoveryWindow struct {
	handle, role, title string
	elements            []ElementRecord
}

// recoveryDesktop is one application, soffice, with a document window and
// optionally a dialog. focus names the handle with keyboard focus.
type recoveryDesktop struct {
	recordingPlatformBackend
	windows []recoveryWindow
	focus   string
	// onAction changes the desktop when input arrives.
	onAction func(d *recoveryDesktop, request ProviderActionRequest)
}

func newRecoveryDesktop() *recoveryDesktop {
	return &recoveryDesktop{
		windows: []recoveryWindow{{handle: recoveryMainHandle, role: "frame", title: "notes.odt - LibreOffice Writer",
			elements: []ElementRecord{
				{ControlType: "menu", Name: "Format", States: "[has-click-action]", Actions: []string{"click"}},
				{ControlType: "document text", Name: "notes - LibreOffice Document"},
				{ControlType: "paragraph", Name: "", Value: "Second paragraph with Single word"},
			}}},
		focus: recoveryMainHandle,
	}
}

func (d *recoveryDesktop) openParagraphDialog() {
	d.windows = append(d.windows, recoveryWindow{handle: recoveryDialogHandle, role: "dialog", title: "Paragraph",
		elements: []ElementRecord{
			{ControlType: "page tab", Name: "Indents & Spacing", States: "[selected]"},
			{ControlType: "panel", Name: "Line Spacing"},
			{ControlType: "spin button", Name: "Above paragraph", Value: "0.00\""},
			{ControlType: "combo box", Name: "Line spacing", Value: "Single"},
			{ControlType: "check box", Name: "Activate page line-spacing", States: "[focused]"},
			{ControlType: "drawing area", Name: "Example"},
			{ControlType: "push button", Name: "OK"},
		}})
	d.focus = recoveryDialogHandle
}

func (d *recoveryDesktop) window(handle string) *recoveryWindow {
	for index := range d.windows {
		if d.windows[index].handle == handle {
			return &d.windows[index]
		}
	}
	return nil
}

func (d *recoveryDesktop) snapshot(window recoveryWindow) *ProviderSnapshot {
	path := "/w/" + window.handle[3:7]
	snapshot := &AppSnapshot{
		App: AppDescriptor{Name: "soffice", PID: 10, ProviderBusName: ":1.84",
			ProcessGeneration: "pid:10:start:100", InputCapable: true},
		WindowTitle: window.title, WindowHandle: window.handle,
		SnapshotID: "recovery-" + path + "-" + itoa(len(window.elements)),
		TreeLines:  []string{"\t0 " + window.role + " \"" + window.title + "\""},
		Elements: []ElementRecord{{Index: 0, RuntimeID: []int{0}, ControlType: window.role, Name: window.title,
			States: map[bool]string{true: "[focused]"}[d.focus == window.handle && window.role == "dialog"]}},
		Scope: contract.AXScopeWindow,
	}
	for index, element := range window.elements {
		element.Index = index + 1
		element.RuntimeID = []int{0, index}
		if element.States == "" && element.ControlType == "push button" {
			element.States = "[has-click-action]"
			element.Actions = []string{"click"}
		}
		snapshot.Elements = append(snapshot.Elements, element)
		snapshot.TreeLines = append(snapshot.TreeLines,
			"\t\t"+itoa(index+1)+" "+element.ControlType+" \""+element.Name+"\" "+element.States)
	}
	for index := range snapshot.Elements {
		element := &snapshot.Elements[index]
		element.ProviderBusName = ":1.84"
		element.ProcessGeneration = "pid:10:start:100"
		element.ProviderObjectPath = path + "/" + itoa(index)
		element.ProviderWindowBusName = ":1.84"
		element.ProviderWindowObjectPath = path
		element.ProviderWindowProcessGeneration = "pid:10:start:100"
		element.ProviderWindowRole = window.role
		element.ProviderWindowName = window.title
	}
	for _, other := range d.windows {
		snapshot.Windows = append(snapshot.Windows, TopLevelWindow{
			Title: other.title, Role: other.role, Handle: other.handle,
			ProviderBusName: ":1.84", ProviderObjectPath: "/w/" + other.handle[3:7],
			ProviderProcessGeneration: "pid:10:start:100",
			Selected:                  other.handle == window.handle,
			Active:                    other.handle == d.focus, Showing: true,
			Modal: other.role == "dialog",
		})
	}
	return snapshot
}

func (d *recoveryDesktop) read(handle string) ProviderAccessibilityResult {
	if handle == "" {
		handle = d.focus
	}
	if window := d.window(handle); window != nil {
		return ProviderAccessibilityResult{OK: true, Snapshot: d.snapshot(*window)}
	}
	result := ProviderAccessibilityResult{OK: false, Error: "stale_window_ref: gone",
		Failure: &ProviderFailure{Code: "stale_window_ref", Phase: "precondition", Subject: "window", FactsComplete: true}}
	result.PrepareForService()
	return result
}

func (d *recoveryDesktop) ObserveApps(ProviderObserveAppsRequest) (ProviderAccessibilityResult, error) {
	app := AppDescriptor{Name: "soffice", BundleIdentifier: "soffice", PID: 10, InputCapable: true,
		ProviderBusName: ":1.84", ProcessGeneration: "pid:10:start:100"}
	for _, window := range d.windows {
		app.Windows = append(app.Windows, ProviderAppWindow{Title: window.title, Role: window.role,
			Handle: window.handle, ProviderBusName: ":1.84", ProviderObjectPath: "/w/" + window.handle[3:7],
			ProcessGeneration: "pid:10:start:100", Active: window.handle == d.focus, InputTarget: true})
	}
	panel := AppDescriptor{Name: "xfce4-panel", PID: 30, InputCapable: false}
	return ProviderAccessibilityResult{OK: true, Apps: []AppDescriptor{app, panel}}, nil
}

func (d *recoveryDesktop) Observe(request ProviderObserveRequest) (ProviderAccessibilityResult, error) {
	d.observeRequests = append(d.observeRequests, request)
	return d.read(request.WindowHandle), nil
}

func (d *recoveryDesktop) ResolveAndAct(request ProviderActionRequest) (ProviderAccessibilityResult, error) {
	d.actionRequests = append(d.actionRequests, request)
	if d.onAction != nil {
		d.onAction(d, request)
	}
	result := d.read(request.WindowHandle)
	result.Mutation = &ProviderMutationFacts{InputEventsEmitted: true, DesktopStateMutated: true}
	result.ActionEvidence = &ProviderActionEvidence{InputEmitted: true, Route: "semantic_action"}
	return result, nil
}

func recoveryService(t *testing.T, desktop *recoveryDesktop) *Service {
	t.Helper()
	return NewServiceWithBackend(desktop)
}

// recoveryCall runs a call through the public path and returns the
// evidence line and the text after it.
func recoveryCall(t *testing.T, s *Service, args map[string]any) (string, string) {
	t.Helper()
	visible := WithAXTextEvidence(PrepareAXEvidence(s.callTool(contract.PublicToolAX, args)))
	if len(visible.Content) == 0 {
		t.Fatal("no content")
	}
	text := []string{}
	for _, item := range visible.Content {
		if item.Type == "text" {
			text = append(text, item.Text)
		}
	}
	all := strings.Join(text, "\n")
	line, rest, _ := strings.Cut(all, "\n")
	return line, rest
}

var recoveryWindowRef = regexp.MustCompile(`(w[0-9]+) "([^"]*)"`)

// windowRefs lists the windows and returns title -> wN.
func windowRefs(t *testing.T, s *Service) map[string]string {
	t.Helper()
	_, text := recoveryCall(t, s, map[string]any{"mode": "apps"})
	refs := map[string]string{}
	for _, match := range recoveryWindowRef.FindAllStringSubmatch(text, -1) {
		refs[match[2]] = match[1]
	}
	return refs
}

func TestContentFindInADialogSearchesTheDialog(t *testing.T) {
	desktop := newRecoveryDesktop()
	desktop.openParagraphDialog()
	s := recoveryService(t, desktop)
	refs := windowRefs(t, s)
	line, text := recoveryCall(t, s, map[string]any{"mode": "find", "window_ref": refs["Paragraph"],
		"scope": "content", "query": map[string]any{"name_contains": "Above"}})
	if !strings.HasPrefix(line, `{"ok":true`) || !strings.Contains(text, "scope: window (a dialog has no separate content area)") ||
		!strings.Contains(text, `spinbutton "Above paragraph"`) {
		t.Fatalf("content find in a dialog: %s\n%s", line, text)
	}
	// The document window still narrows to its document.
	line, text = recoveryCall(t, s, map[string]any{"mode": "observe", "window_ref": refs["notes.odt - LibreOffice Writer"],
		"scope": "content"})
	if !strings.HasPrefix(line, `{"ok":true`) || !strings.Contains(text, `scope: content root=document text`) ||
		strings.Contains(text, `menu "Format"`) {
		t.Fatalf("content observe of the document: %s\n%s", line, text)
	}
}

// Item 3: a wait that times out says what is there instead.

// The window list leads with wN, and a close title becomes the hint.
// Recorded: wait "Find & Replace" while "Find and Replace" was open.
func TestWindowWaitTimeoutNamesWindowsAndANearTitle(t *testing.T) {
	desktop := newRecoveryDesktop()
	desktop.openParagraphDialog()
	desktop.windows[1].title = "Find and Replace"
	s := recoveryService(t, desktop)
	line, _ := recoveryCall(t, s, map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "wait", "window": "Find & Replace", "timeout_ms": 1.0}}})
	message := regexp.MustCompile(`"message":"((?:[^"\\]|\\.)*)"`).FindStringSubmatch(line)
	if message == nil || !strings.Contains(message[1], `windows: w`) ||
		!strings.Contains(message[1], `\"Find and Replace\" (focused)`) ||
		!strings.Contains(line, `"hint":"{\"type\":\"wait\",\"timeout_ms\":1,\"window\":\"Find and Replace\"}"`) {
		t.Fatalf("window wait timeout: %s", line)
	}
	if len([]rune(message[1])) > axPublicMessageRunes {
		t.Fatalf("message is %d runes", len([]rune(message[1])))
	}
}

// A query wait names the closest elements; a role spelled another way
// becomes the corrected wait.
func TestQueryWaitTimeoutNamesTheClosestElements(t *testing.T) {
	desktop := newRecoveryDesktop()
	desktop.openParagraphDialog()
	s := recoveryService(t, desktop)
	refs := windowRefs(t, s)
	line, _ := recoveryCall(t, s, map[string]any{"mode": "act", "window_ref": refs["Paragraph"], "actions": []any{
		map[string]any{"type": "wait", "query": map[string]any{"role": "list box", "name_contains": "Line spacing"},
			"timeout_ms": 1.0}}})
	if !strings.Contains(line, `closest: combobox \"Line spacing\" value=\"Single\"`) ||
		!strings.Contains(line, `"hint":"{\"type\":\"wait\",\"query\":{\"name_contains\":\"Line spacing\",\"role\":\"combobox\"}`) {
		t.Fatalf("query wait timeout: %s", line)
	}
	// A name the candidate only nearly has is replaced, so the corrected
	// wait matches the candidate it names (recorded: "Below paragraphs").
	line, _ = recoveryCall(t, s, map[string]any{"mode": "act", "window_ref": refs["Paragraph"], "actions": []any{
		map[string]any{"type": "wait", "query": map[string]any{"role": "spin button", "name_contains": "Above paragraphs"},
			"timeout_ms": 1.0}}})
	if !strings.Contains(line, `"hint":"{\"type\":\"wait\",\"query\":{\"name_contains\":\"Above paragraph\",\"role\":\"spinbutton\"}`) {
		t.Fatalf("corrected wait does not match its candidate: %s", line)
	}
}

// Item 5: a find that matches nothing lists the closest elements with refs.

// Recorded: {"role":"combo box"} in the Paragraph dialog. A role written
// with or without a space finds the same element; a role of the same
// family is offered as a candidate with its ref.
func TestFindRoleSpellingsAndCandidates(t *testing.T) {
	desktop := newRecoveryDesktop()
	desktop.openParagraphDialog()
	s := recoveryService(t, desktop)
	refs := windowRefs(t, s)
	for _, role := range []string{"combo box", "combobox", "combo_box"} {
		line, text := recoveryCall(t, s, map[string]any{"mode": "find", "window_ref": refs["Paragraph"],
			"query": map[string]any{"role": role}})
		if !strings.Contains(line, `"find_match_count":1`) || !strings.Contains(text, `combobox "Line spacing"`) {
			t.Fatalf("role %q: %s\n%s", role, line, text)
		}
	}
	line, text := recoveryCall(t, s, map[string]any{"mode": "find", "window_ref": refs["Paragraph"],
		"query": map[string]any{"role": "list box"}})
	if !strings.Contains(line, `"find_match_count":0`) ||
		!regexp.MustCompile(`closest \(not matches\):\n - combobox "Line spacing" value="Single" \[ref=r[0-9]+\]`).MatchString(text) {
		t.Fatalf("list box candidates: %s\n%s", line, text)
	}
	candidate := regexp.MustCompile(`combobox "Line spacing" value="Single" \[ref=(r[0-9]+)\]`).FindStringSubmatch(text)[1]
	if _, err := s.RefRegistry.Resolve(candidate, ""); err != nil {
		t.Fatalf("candidate ref %s does not resolve: %v", candidate, err)
	}
	// Text that matches a word of a name, but not the whole query.
	_, text = recoveryCall(t, s, map[string]any{"mode": "find", "window_ref": refs["Paragraph"],
		"query": map[string]any{"name_contains": "Spacing line"}})
	if !strings.Contains(text, `combobox "Line spacing"`) || !strings.Contains(text, `group "Line Spacing"`) {
		t.Fatalf("name candidates:\n%s", text)
	}
}

// A name match ranks above the same words in a value: a browser address
// bar whose URL contains the page text must not come first.
func TestNearCandidatesPreferNameMatches(t *testing.T) {
	snapshot := &AppSnapshot{Elements: []ElementRecord{
		{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Smoke Page"},
		{Index: 1, RuntimeID: []int{0, 0}, ControlType: "entry", Name: "Address and search bar",
			Value: "data:text/html,<title>Smoke Page</title><h1>Smoke heading</h1>"},
		{Index: 2, RuntimeID: []int{0, 1}, ControlType: "heading", Name: "Smoke heading"},
	}}
	near := axNearElements(snapshot, newAXNearQuery("", "", "Smoke headings", ""), 3)
	if len(near) < 2 || near[0].record.Name != "Smoke heading" {
		t.Fatalf("ranking: %v", near)
	}
}

// The tree renders a table cell inside a grid as gridcell, and find
// matches that rendered role. Recorded: 10 zero-result finds for gridcell.
func TestFindMatchesTheRenderedRole(t *testing.T) {
	snapshot := &AppSnapshot{Elements: []ElementRecord{
		{Index: 0, RuntimeID: []int{}, ControlType: "tree table", Name: "Insert before"},
		{Index: 1, RuntimeID: []int{0}, ControlType: "table cell", Name: "Sheet1"},
	}}
	if err := buildAXSnapshotForTest(snapshot); err != nil {
		t.Fatal(err)
	}
	if !queryRecordMatches(snapshot, &snapshot.Elements[1], "gridcell", "", "", "", false, false) {
		t.Fatalf("gridcell did not match the rendered role %q", axRenderedRole(snapshot, &snapshot.Elements[1]))
	}
	if !queryRecordMatches(snapshot, &snapshot.Elements[1], "table cell", "", "", "", false, false) {
		t.Fatal("the provider role no longer matches")
	}
}

// States as the tree renders them: active is keyboard focus, unchecked is
// the absent checked state.
func TestFindStatesMatchTheirRenderedNames(t *testing.T) {
	focused := &ElementRecord{States: "[focused]"}
	checked := &ElementRecord{States: "[checked]"}
	plain := &ElementRecord{}
	for _, test := range []struct {
		record *ElementRecord
		state  string
		want   bool
	}{
		{focused, "active", true}, {plain, "active", false},
		{checked, "unchecked", false}, {plain, "unchecked", true},
		{checked, "checked", true}, {checked, "checked=true", true}, {plain, "checked=false", true},
	} {
		if got := axQueryStateMatches(test.record, test.state); got != test.want {
			t.Fatalf("state %q on %q: %v", test.state, test.record.States, got)
		}
	}
}

func buildAXSnapshotForTest(snapshot *AppSnapshot) error {
	return NewService().BuildAndRenderAXSnapshot(snapshot, contract.TreeViewWorking)
}

// A successful earlier sample cannot prove the current state after the
// final inventory read fails, even if it supplied a close title and ref.
func TestWindowWaitTimeoutRejectsStaleInventoryAfterReadFailure(t *testing.T) {
	inventory := &axWindowInventory{windows: []axDesktopWindow{{
		ref: "w7", title: "Find and Replace", active: true,
	}}}
	for _, gone := range []bool{false, true} {
		wait := &axWait{window: "Find & Replace", gone: gone, timeoutMS: 1000}
		result := NewService().windowWaitTimeout(nil, wait, inventory, "provider disconnected", 2)
		if !result.IsError || result.StructuredContent["assertion_status"] != "observation_incomplete" {
			t.Fatalf("unreadable final sample was treated as a predicate result: %#v", result)
		}
		if !strings.Contains(ResultText(result), "provider disconnected") || strings.Contains(ResultText(result), "w7") {
			t.Fatalf("stale windows presented as current: %s", ResultText(result))
		}
		for _, key := range []string{"wait_hint", "focused_window_ref"} {
			if _, present := result.StructuredContent[key]; present {
				t.Fatalf("stale sample supplied %s: %#v", key, result.StructuredContent)
			}
		}
	}
}

func TestWindowWaitTimeoutHandlesUnboundDesktopWait(t *testing.T) {
	inventory := &axWindowInventory{windows: []axDesktopWindow{
		{ref: "w8", title: "Document", active: false},
		{ref: "w9", title: "Move/Copy Sheet", active: true},
	}}
	result := NewService().windowWaitTimeout(nil,
		&axWait{window: "Move or Copy Sheet", timeoutMS: 1000}, inventory, "", 1)
	if result.IsError || result.StructuredContent["assertion_status"] != "predicate_false" {
		t.Fatalf("complete inventory should disprove the exact predicate: %#v", result)
	}
	text := ResultText(result)
	if strings.Index(text, "w9") < 0 || strings.Index(text, "w9") > strings.Index(text, "w8") {
		t.Fatalf("focused window must be first: %s", text)
	}
	if hint, _ := result.StructuredContent["wait_hint"].(string); !strings.Contains(hint, "Move/Copy Sheet") {
		t.Fatalf("missing corrected wait: %#v", result.StructuredContent)
	}
}
