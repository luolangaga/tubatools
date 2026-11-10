package core

import (
	"testing"
)

const (
	untilMainHandle   = "w2:0123456789abcdef0123456789abcdef"
	untilDialogHandle = "w2:fedcba9876543210fedcba9876543210"
)

type untilFakeWindow struct {
	handle, role, title, path string
	modal                     bool
}

// appWideBackend is one application with several top-level windows. Like
// the Linux provider, an observe reads one window (the main one when no
// handle is given) and lists them all; a window list filtered by handle
// returns only that window's title.
type appWideBackend struct {
	recordingPlatformBackend
	open          []untilFakeWindow
	appGone       bool
	appGoneFacts  bool
	listRequests  []ProviderWindowListRequest
	observedHands []string
}

func untilMainWindow() untilFakeWindow {
	return untilFakeWindow{handle: untilMainHandle, role: "frame", title: "Sheet", path: "/window/1"}
}

func untilDialogWindow() untilFakeWindow {
	return untilFakeWindow{handle: untilDialogHandle, role: "dialog", title: "Insert Sheet", path: "/window/2", modal: true}
}

func (b *appWideBackend) goneFailure() *ProviderFailure {
	return &ProviderFailure{Code: "stale_app_ref", Phase: "precondition", Subject: "app", FactsComplete: b.appGoneFacts}
}

func (b *appWideBackend) snapshotFor(selected untilFakeWindow) *ProviderSnapshot {
	snapshot := mixedOrderedInputBatchTestSnapshot()
	snapshot.WindowHandle = selected.handle
	snapshot.WindowTitle = selected.title
	snapshot.SnapshotID = "until-" + selected.path
	for index := range snapshot.Elements {
		element := &snapshot.Elements[index]
		element.ProviderWindowObjectPath = selected.path
		element.ProviderWindowRole = selected.role
		element.ProviderWindowName = selected.title
	}
	snapshot.Elements[3].ProviderObjectPath = selected.path + "/save"
	// The root element is the window itself.
	snapshot.Elements[0].ProviderBusName = ":1.84"
	snapshot.Elements[0].ProviderObjectPath = selected.path
	if selected.role == "dialog" {
		snapshot.Elements[0].ControlType = "dialog"
		snapshot.Elements[0].Name = selected.title
		snapshot.Elements[3].Name = "Insert"
		snapshot.TreeLines[0] = "\t0 dialog \"Insert Sheet\""
		snapshot.TreeLines[3] = "\t\t\t3 push button \"Insert\" [click]"
	}
	for _, window := range b.open {
		snapshot.Windows = append(snapshot.Windows, TopLevelWindow{
			Title: window.title, Role: window.role, Handle: window.handle,
			ProviderBusName: ":1.84", ProviderObjectPath: window.path,
			ProviderProcessGeneration: "pid:10:start:100",
			Selected:                  window.handle == selected.handle,
			Modal:                     window.modal, Active: window.modal, Showing: true,
		})
	}
	return snapshot
}

func (b *appWideBackend) Observe(request ProviderObserveRequest) (ProviderAccessibilityResult, error) {
	b.observeRequests = append(b.observeRequests, request)
	b.observedHands = append(b.observedHands, request.WindowHandle)
	if b.appGone {
		result := ProviderAccessibilityResult{OK: false, Error: "stale_app_ref: gone", Failure: b.goneFailure()}
		result.PrepareForService()
		return result, nil
	}
	for _, window := range b.open {
		if request.WindowHandle == window.handle || (request.WindowHandle == "" && window.role != "dialog") {
			return ProviderAccessibilityResult{OK: true, Snapshot: b.snapshotFor(window)}, nil
		}
	}
	result := ProviderAccessibilityResult{OK: false, Error: "stale_window_ref: the referenced logical window no longer exists.",
		Failure: &ProviderFailure{Code: "stale_window_ref", Phase: "precondition", Subject: "window", FactsComplete: true}}
	result.PrepareForService()
	return result, nil
}

func (b *appWideBackend) ListWindows(request ProviderWindowListRequest) (ProviderWindowResult, error) {
	b.listRequests = append(b.listRequests, request)
	if b.appGone {
		return ProviderWindowResult{OK: false, Error: "stale_app_ref: gone", Failure: b.goneFailure()}, nil
	}
	titles := []string{}
	for _, window := range b.open {
		if request.WindowHandle == "" || request.WindowHandle == window.handle {
			titles = append(titles, window.title)
		}
	}
	if request.WindowHandle != "" && len(titles) == 0 {
		return ProviderWindowResult{OK: false, Error: "stale_window_ref: the referenced logical window no longer exists.",
			Failure: &ProviderFailure{Code: "stale_window_ref", Phase: "precondition", Subject: "window", FactsComplete: true}}, nil
	}
	return ProviderWindowResult{OK: true, AppName: "LibreOffice", AppPID: 10, WindowTitles: titles}, nil
}

func untilScopeService(t *testing.T, open ...untilFakeWindow) (*Service, *appWideBackend, *AXBatchContext) {
	t.Helper()
	s, appRef, windowRef, _ := sheetBatchService(t)
	backend := &appWideBackend{open: open}
	s.Backend = backend
	return s, backend, &AXBatchContext{
		AppRef: appRef, AppToken: appRef, WindowRef: windowRef, WindowHandle: untilMainHandle,
	}
}

func untilStatus(result ToolCallResult) string {
	status, _ := result.StructuredContent["assertion_status"].(string)
	return status
}

// queryWait polls a query wait step in the batch window, as an act call
// with {"type":"wait","query":query,"gone":gone} does.
func queryWait(s *Service, context *AXBatchContext, query map[string]any, gone bool) ToolCallResult {
	return s.waitForQuery(context, &axWait{query: query, gone: gone, timeoutMS: 1})
}

// Shape 1: the batch acts in the document window and the awaited dialog is
// another top-level window of the same application, as LibreOffice opens
// its dialogs.
func TestQueryWaitSeesDialogInAnotherWindowOfTheApp(t *testing.T) {
	s, backend, context := untilScopeService(t, untilMainWindow(), untilDialogWindow())
	for _, query := range []map[string]any{
		{"role": "dialog"},
		{"role": "button", "name_contains": "Insert"},
	} {
		result := queryWait(s, context, query, false)
		if result.IsError || untilStatus(result) != "pass" {
			t.Fatalf("%v in the dialog window did not satisfy the wait: %s", query, ResultText(result))
		}
	}
	for _, request := range backend.actionRequests {
		t.Fatalf("wait sent input: %#v", request)
	}
}

// A gone query is true only when no window of the app matches.
func TestGoneQueryWaitChecksEveryWindowOfTheApp(t *testing.T) {
	s, _, context := untilScopeService(t, untilMainWindow(), untilDialogWindow())
	result := queryWait(s, context, map[string]any{"role": "dialog"}, true)
	if untilStatus(result) == "pass" {
		t.Fatalf("a dialog still open in another window must keep gone false: %s", ResultText(result))
	}
}

// A query found in the bound window costs one read.
func TestQueryWaitMatchedInBoundWindowReadsOneWindow(t *testing.T) {
	s, backend, context := untilScopeService(t, untilMainWindow(), untilDialogWindow())
	result := queryWait(s, context, map[string]any{"role": "button", "name_contains": "Save"}, false)
	if untilStatus(result) != "pass" || len(backend.observedHands) != 1 {
		t.Fatalf("bound-window match: status=%s reads=%v", untilStatus(result), backend.observedHands)
	}
}

// A false query wait names the dialog that has focus.
func TestFalseQueryWaitNamesTheFocusedDialog(t *testing.T) {
	s, _, context := untilScopeService(t, untilMainWindow(), untilDialogWindow())
	result := queryWait(s, context, map[string]any{"role": "button", "name_contains": "Never"}, false)
	ref, _ := result.StructuredContent["until_window_ref"].(string)
	if untilStatus(result) != "predicate_false" || !isShortAXWindowRef(ref) || ref == context.WindowRef {
		t.Fatalf("false wait: status=%s window=%q", untilStatus(result), ref)
	}
}

// A query wait bound to a window that closed reads the app's remaining
// windows instead of returning unknown.
func TestQueryWaitAfterTheBatchDialogClosed(t *testing.T) {
	s, _, context := untilScopeService(t, untilMainWindow())
	context.WindowHandle = untilDialogHandle
	result := queryWait(s, context, map[string]any{"role": "dialog"}, true)
	if result.IsError || untilStatus(result) != "pass" {
		t.Fatalf("closed dialog query must be absent: %s", ResultText(result))
	}
}

func TestGoneQueryWaitAfterTheAppExited(t *testing.T) {
	s, backend, context := untilScopeService(t)
	backend.appGone, backend.appGoneFacts = true, true
	s.markAppIdentityLifecycle(context.AppRef, AXRefStale, "stale_app_ref")
	result := queryWait(s, context, map[string]any{"role": "dialog"}, true)
	if result.IsError || untilStatus(result) != "pass" {
		t.Fatalf("an exited app has no matching element: %s", ResultText(result))
	}
}
