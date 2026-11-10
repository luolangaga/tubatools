package transport

import (
	"bytes"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"image"
	"image/png"
	"reflect"
	"regexp"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

// Copies of lower-package test helpers for tests in this package.

func transportOrderedInputTestSnapshot() *core.AppSnapshot {
	return &core.AppSnapshot{
		App:         core.AppDescriptor{Name: "Editor", PID: 42},
		WindowTitle: "Document", WindowHandle: "0x42", SnapshotID: "ordered-input-test",
		TreeLines: []string{
			"\t0 frame \"Document\"", "\t\t1 panel \"Form\"",
			"\t\t\t2 entry \"Name\" [focused]", "\t\t\t3 push button \"Save\" [click]",
			"\t\t4 status bar \"Ready\"",
		},
		Elements: []core.ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Document"},
			{Index: 1, RuntimeID: []int{0, 0}, ControlType: "panel", Name: "Form"},
			{Index: 2, RuntimeID: []int{0, 0, 0}, ControlType: "entry", Name: "Name", States: "[focused]"},
			{Index: 3, RuntimeID: []int{0, 0, 1}, ControlType: "push button", Name: "Save", States: "[has-click-action]"},
			{Index: 4, RuntimeID: []int{0, 1}, ControlType: "status bar", Name: "Ready"},
		},
		Scope: contract.AXScopeWindow,
	}
}

func transportDecodeJSONForTest(t *testing.T, text string) map[string]any {
	t.Helper()
	var value map[string]any
	if err := json.Unmarshal([]byte(text), &value); err != nil {
		t.Fatalf("decode %s: %v", text, err)
	}
	return value
}

func transportFindDeliverySnapshot(t testing.TB, value string) (*core.Service, *core.AppSnapshot) {
	t.Helper()
	svc := core.NewServiceWithBackend(&transportRecordingPlatformBackend{})
	snapshot := &core.AppSnapshot{
		App:          core.AppDescriptor{Name: "Independent", PID: 42, ProviderBusName: ":1.42", ProcessGeneration: "42"},
		WindowTitle:  "Selection",
		WindowHandle: "w2:" + strings.Repeat("a", 32),
		SnapshotID:   "find-delivery",
		Elements: []core.ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Selection"},
			{Index: 1, RuntimeID: []int{0, 0}, ControlType: "entry", Name: "Container", Value: value},
			{Index: 2, RuntimeID: []int{0, 0, 0}, ControlType: "list item", Name: "Target", States: "[has-click-action]"},
		},
		Scope: contract.AXScopeWindow,
	}
	if err := svc.BuildAndRenderAXSnapshot(snapshot, contract.TreeViewWorking); err != nil {
		t.Fatal(err)
	}
	return svc, snapshot
}

func transportFindDeliveryMany(t testing.TB, count int) (*core.Service, *core.AppSnapshot, []core.ElementRecord) {
	t.Helper()
	svc, snapshot := transportFindDeliverySnapshot(t, "")
	snapshot.Elements = snapshot.Elements[:2]
	for i := 0; i < count; i++ {
		snapshot.Elements = append(snapshot.Elements, core.ElementRecord{
			Index: i + 2, RuntimeID: []int{0, 0, i}, ControlType: "list item",
			Name: fmt.Sprintf("Target %02d", i), States: "[has-click-action]",
			Description: strings.Repeat("d", 240),
		})
	}
	if err := svc.BuildAndRenderAXSnapshot(snapshot, contract.TreeViewWorking); err != nil {
		t.Fatal(err)
	}
	return svc, snapshot, snapshot.Elements[2:]
}

var transportFindDeliveredTargets = regexp.MustCompile(`(?m)^ - match=(\d+) ref=("r\d+")$`)

func transportFindDeliveryModelEvidence(t *testing.T, result core.ToolCallResult) map[string]any {
	t.Helper()
	if result.StructuredContent != nil {
		return result.StructuredContent
	}
	for _, item := range result.Content {
		if item.Type == "text" && strings.HasPrefix(item.Text, `{"ok":`) {
			var evidence map[string]any
			if err := json.Unmarshal([]byte(item.Text), &evidence); err != nil {
				t.Fatal(err)
			}
			return evidence
		}
	}
	t.Fatal("missing model evidence")
	return nil
}

func transportFindDeliveryFinal(result core.ToolCallResult) core.ToolCallResult {
	result = core.WithAXModeEvidence(result, contract.AXModeFind)
	result = core.EnforceAXResponseBudget(result)
	result = core.NormalizeAXEvidenceInvariants(result)
	return core.WithAXTextEvidence(core.PrepareAXEvidence(result))
}

// Keep complete canonical records but hide them from the working projection.
func transportFindExposureFixture(t *testing.T, count int) (*core.Service, *core.AppSnapshot, []core.ElementRecord) {
	t.Helper()
	svc, snapshot, matches := transportFindDeliveryMany(t, count)
	root := *snapshot.AXSnapshot.Roots[0]
	root.Children = nil
	snapshot.AXSnapshot.Roots = []*core.AXNode{&root}
	if core.AXProjectedIndices(snapshot.AXSnapshot.Roots)[matches[0].Index] {
		t.Fatal("fixture must hide canonical matches")
	}
	return svc, snapshot, matches
}

var transportExposureRefLabels = regexp.MustCompile(`\[ref=(r[0-9]+)\]`)

func transportAssertFindExposureDelivered(t *testing.T, svc *core.Service, snapshot *core.AppSnapshot, result core.ToolCallResult) {
	t.Helper()
	want := map[int]core.ElementRecord{}
	projected := core.AXProjectedIndices(snapshot.AXSnapshot.Roots)
	for _, label := range transportExposureRefLabels.FindAllStringSubmatch(core.ResultText(result), -1) {
		entry := svc.RefRegistry.Entries[label[1]]
		if entry == nil {
			t.Fatalf("delivered ref missing registry entry: %s", label[1])
		}
		for _, record := range snapshot.Elements {
			if !projected[record.Index] && reflect.DeepEqual(record.RuntimeID, entry.RuntimeID) {
				want[record.Index] = record
			}
		}
	}
	got := svc.ExposedHiddenRefs[core.DeliveredSnapshotKey(snapshot)]
	if len(got) != len(want) {
		t.Fatalf("exposure count=%d, actually delivered hidden refs=%d\n%s", len(got), len(want), core.ResultText(result))
	}
	for index, record := range want {
		if saved, ok := got[index]; !ok || !core.SameCanonicalAddress(record, saved) {
			t.Errorf("delivered hidden index %d lost canonical identity", index)
		}
	}
}

const (
	transportUntilMainHandle   = "w2:0123456789abcdef0123456789abcdef"
	transportUntilDialogHandle = "w2:fedcba9876543210fedcba9876543210"
)

type transportUntilFakeWindow struct {
	handle, role, title, path string
	modal                     bool
}

// appWideBackend is one application with several top-level windows. Like
// the Linux provider, an observe reads one window (the main one when no
// handle is given) and lists them all; a window list filtered by handle
// returns only that window's title.
type transportAppWideBackend struct {
	transportRecordingPlatformBackend
	open          []transportUntilFakeWindow
	appGone       bool
	appGoneFacts  bool
	listRequests  []core.ProviderWindowListRequest
	observedHands []string
}

func transportUntilMainWindow() transportUntilFakeWindow {
	return transportUntilFakeWindow{handle: transportUntilMainHandle, role: "frame", title: "Sheet", path: "/window/1"}
}

func transportUntilDialogWindow() transportUntilFakeWindow {
	return transportUntilFakeWindow{handle: transportUntilDialogHandle, role: "dialog", title: "Insert Sheet", path: "/window/2", modal: true}
}

func (b *transportAppWideBackend) goneFailure() *core.ProviderFailure {
	return &core.ProviderFailure{Code: "stale_app_ref", Phase: "precondition", Subject: "app", FactsComplete: b.appGoneFacts}
}

func (b *transportAppWideBackend) snapshotFor(selected transportUntilFakeWindow) *core.ProviderSnapshot {
	snapshot := transportMixedOrderedInputBatchTestSnapshot()
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
		snapshot.Windows = append(snapshot.Windows, core.TopLevelWindow{
			Title: window.title, Role: window.role, Handle: window.handle,
			ProviderBusName: ":1.84", ProviderObjectPath: window.path,
			ProviderProcessGeneration: "pid:10:start:100",
			Selected:                  window.handle == selected.handle,
			Modal:                     window.modal, Active: window.modal, Showing: true,
		})
	}
	return snapshot
}

func (b *transportAppWideBackend) Observe(request core.ProviderObserveRequest) (core.ProviderAccessibilityResult, error) {
	b.observeRequests = append(b.observeRequests, request)
	b.observedHands = append(b.observedHands, request.WindowHandle)
	if b.appGone {
		result := core.ProviderAccessibilityResult{OK: false, Error: "stale_app_ref: gone", Failure: b.goneFailure()}
		result.PrepareForService()
		return result, nil
	}
	for _, window := range b.open {
		if request.WindowHandle == window.handle || (request.WindowHandle == "" && window.role != "dialog") {
			return core.ProviderAccessibilityResult{OK: true, Snapshot: b.snapshotFor(window)}, nil
		}
	}
	result := core.ProviderAccessibilityResult{OK: false, Error: "stale_window_ref: the referenced logical window no longer exists.",
		Failure: &core.ProviderFailure{Code: "stale_window_ref", Phase: "precondition", Subject: "window", FactsComplete: true}}
	result.PrepareForService()
	return result, nil
}

func (b *transportAppWideBackend) ListWindows(request core.ProviderWindowListRequest) (core.ProviderWindowResult, error) {
	b.listRequests = append(b.listRequests, request)
	if b.appGone {
		return core.ProviderWindowResult{OK: false, Error: "stale_app_ref: gone", Failure: b.goneFailure()}, nil
	}
	titles := []string{}
	for _, window := range b.open {
		if request.WindowHandle == "" || request.WindowHandle == window.handle {
			titles = append(titles, window.title)
		}
	}
	if request.WindowHandle != "" && len(titles) == 0 {
		return core.ProviderWindowResult{OK: false, Error: "stale_window_ref: the referenced logical window no longer exists.",
			Failure: &core.ProviderFailure{Code: "stale_window_ref", Phase: "precondition", Subject: "window", FactsComplete: true}}, nil
	}
	return core.ProviderWindowResult{OK: true, AppName: "LibreOffice", AppPID: 10, WindowTitles: titles}, nil
}

func transportUntilScopeService(t *testing.T, open ...transportUntilFakeWindow) (*core.Service, *transportAppWideBackend, *core.AXBatchContext) {
	t.Helper()
	s, appRef, windowRef, _ := transportSheetBatchService(t)
	backend := &transportAppWideBackend{open: open}
	s.Backend = backend
	return s, backend, &core.AXBatchContext{
		AppRef: appRef, AppToken: appRef, WindowRef: windowRef, WindowHandle: transportUntilMainHandle,
	}
}

// windowScriptBackend answers apps listings from a script, one inventory per
// call (the last repeats), and records every other provider call.
type transportWindowScriptBackend struct {
	*transportRecordingPlatformBackend
	inventories [][]core.AppDescriptor
	listings    int
}

func (b *transportWindowScriptBackend) ObserveApps(core.ProviderObserveAppsRequest) (core.ProviderAccessibilityResult, error) {
	index := b.listings
	if index >= len(b.inventories) {
		index = len(b.inventories) - 1
	}
	b.listings++
	return core.ProviderAccessibilityResult{OK: true, Apps: b.inventories[index]}, nil
}

const (
	transportTestHandleReport = "w2:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
	transportTestHandleNotes  = "w2:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
	transportTestHandleMail   = "w2:cccccccccccccccccccccccccccccccc"
)

type transportTestWindow struct {
	handle, title string
	active        bool
}

func transportEditorApps(windows ...transportTestWindow) []core.AppDescriptor {
	editor := core.AppDescriptor{
		Name: "editor", BundleIdentifier: "editor", PID: 10, InputCapable: true,
		ProviderBusName: ":1.10", ProcessGeneration: "pid:10:start:1",
	}
	mail := core.AppDescriptor{
		Name: "mail", BundleIdentifier: "mail", PID: 20, InputCapable: true,
		ProviderBusName: ":1.20", ProcessGeneration: "pid:20:start:1",
	}
	panel := core.AppDescriptor{Name: "panel", PID: 30, InputCapable: false}
	for _, window := range windows {
		entry := core.ProviderAppWindow{
			Title: window.title, Role: "frame", Handle: window.handle,
			ProviderBusName: ":1.10", ProviderObjectPath: "/w/" + window.title,
			ProcessGeneration: "pid:10:start:1", Active: window.active, InputTarget: true,
		}
		if window.handle == transportTestHandleMail {
			entry.ProviderBusName = ":1.20"
			entry.ProcessGeneration = "pid:20:start:1"
			mail.Windows = append(mail.Windows, entry)
			continue
		}
		editor.Windows = append(editor.Windows, entry)
	}
	return []core.AppDescriptor{editor, mail, panel}
}

func transportWindowScriptService(inventories ...[]core.AppDescriptor) (*core.Service, *transportWindowScriptBackend) {
	backend := &transportWindowScriptBackend{
		transportRecordingPlatformBackend: &transportRecordingPlatformBackend{},
		inventories:                       inventories,
	}
	return core.NewServiceWithBackend(backend), backend
}

func transportTestPNGBase64(t *testing.T, width, height int) string {
	t.Helper()
	var encoded bytes.Buffer
	if err := png.Encode(&encoded, image.NewRGBA(image.Rect(0, 0, width, height))); err != nil {
		t.Fatalf("encode test PNG: %v", err)
	}
	return base64.StdEncoding.EncodeToString(encoded.Bytes())
}

type transportRecordingPlatformBackend struct {
	calls                  []string
	actionRequests         []core.ProviderActionRequest
	desktopRequests        []core.ProviderDesktopRequest
	observeRequests        []core.ProviderObserveRequest
	accessibilityResponse  *core.ProviderAccessibilityResult
	accessibilityResponses []core.ProviderAccessibilityResult
	desktopResponse        *core.ProviderDesktopResult
	windowResponse         *core.ProviderWindowResult
	err                    error
	processState           string
	windowKnown            bool
	windowAlive            bool
}

func (b *transportRecordingPlatformBackend) accessibilityResult(
	call string,
) (core.ProviderAccessibilityResult, error) {
	b.calls = append(b.calls, call)
	if len(b.accessibilityResponses) > 0 {
		result := b.accessibilityResponses[0]
		b.accessibilityResponses = b.accessibilityResponses[1:]
		result.PrepareForService()
		return result, b.err
	}
	if b.accessibilityResponse == nil {
		return core.ProviderAccessibilityResult{}, b.err
	}
	result := *b.accessibilityResponse
	result.PrepareForService()
	return result, b.err
}

func (b *transportRecordingPlatformBackend) ObserveApps(
	core.ProviderObserveAppsRequest,
) (core.ProviderAccessibilityResult, error) {
	return b.accessibilityResult("observe_apps")
}

func (b *transportRecordingPlatformBackend) Observe(
	request core.ProviderObserveRequest,
) (core.ProviderAccessibilityResult, error) {
	b.observeRequests = append(b.observeRequests, request)
	return b.accessibilityResult("observe")
}

func (b *transportRecordingPlatformBackend) ResolveAndAct(
	request core.ProviderActionRequest,
) (core.ProviderAccessibilityResult, error) {
	b.actionRequests = append(b.actionRequests, request)
	return b.accessibilityResult("resolve_and_act")
}

func (b *transportRecordingPlatformBackend) ExecuteBatch(
	request core.ProviderDesktopRequest,
) (core.ProviderDesktopResult, error) {
	b.desktopRequests = append(b.desktopRequests, request)
	b.calls = append(b.calls, "desktop_execute_batch")
	if b.desktopResponse == nil {
		return core.ProviderDesktopResult{}, b.err
	}
	return *b.desktopResponse, b.err
}

func (b *transportRecordingPlatformBackend) ListWindows(
	core.ProviderWindowListRequest,
) (core.ProviderWindowResult, error) {
	b.calls = append(b.calls, "window_list")
	if b.windowResponse == nil {
		return core.ProviderWindowResult{}, b.err
	}
	return *b.windowResponse, b.err
}

func (b *transportRecordingPlatformBackend) ActOnWindow(
	request core.ProviderWindowActionRequest,
) (core.ProviderWindowResult, error) {
	b.calls = append(b.calls, "window_"+string(request.Action))
	if b.windowResponse == nil {
		return core.ProviderWindowResult{}, b.err
	}
	return *b.windowResponse, b.err
}

func (b *transportRecordingPlatformBackend) WindowForPID(int) (bool, bool) {
	return b.windowKnown, b.windowAlive
}

func (*transportRecordingPlatformBackend) HostInfo() core.HostBackendInfo {
	return core.HostBackendInfo{Platform: "test"}
}

func (*transportRecordingPlatformBackend) RuntimeEnvironment(base []string) []string {
	return base
}

func (b *transportRecordingPlatformBackend) ProcessState(int) string {
	return b.processState
}

func transportFindToolDefinition(t *testing.T, name string) contract.ToolDefinition {
	t.Helper()
	for _, tool := range contract.AllToolDefinitions() {
		if tool.Name == name {
			return tool
		}
	}
	t.Fatalf("missing tool definition %q", name)
	return contract.ToolDefinition{}
}

func transportSheetBatchService(t *testing.T) (*core.Service, string, string, string) {
	t.Helper()
	service := core.NewService()
	appRef := service.AppRefRegistry.Acquire(
		"LibreOffice", ":1.84", "pid:10:start:100", "c1",
	)
	handle := "w2:0123456789abcdef0123456789abcdef"
	windowRef := service.WindowRefRegistry.Acquire(
		"libreoffice", handle, `frame "Sheet"`, "c1",
		":1.84", "/window/1", "pid:10:start:100", "frame", "Sheet",
		appRef,
	)
	elementRef := service.RefRegistry.AcquireTyped(core.AXLocatorSemantic,
		"libreoffice", handle, `button "Save"`, core.CanonicalRole("button"),
		"Save", "c1",
		":1.84", "/window/1/save", "pid:10:start:100",
		":1.84", "/window/1", "pid:10:start:100", "frame", "Sheet",
		appRef, windowRef,
	)
	return service, appRef, windowRef, elementRef
}

func transportBoolPointer(value bool) *bool {
	return &value
}

func transportTestProviderOrderedInputRequest(
	keys ...string,
) core.ProviderOrderedInputRequest {
	handle := "w2:" + strings.Repeat("a", 32)
	steps := make([]core.ProviderOrderedInputStep, 0, len(keys))
	for index, key := range keys {
		steps = append(steps, core.ProviderOrderedInputStep{
			ActionIndex: 4 + index,
			Key:         key,
		})
	}
	return core.ProviderOrderedInputRequest{
		ContractVersion: core.ProviderOrderedInputContractVersion,
		App:             "a1",
		AppIdentity: &core.ProviderAppIdentity{
			ProviderBusName:   "pid:42",
			ProcessGeneration: "pid:42:start:100",
		},
		WindowHandle: handle,
		ExpectedWindow: &core.ProviderWindowExpectation{
			WindowHandle:      handle,
			ProviderBusName:   "pid:42",
			ProcessGeneration: "pid:42:start:100",
			Role:              "window",
			Name:              "Document",
		},
		Steps: steps,
		FinalObservation: core.ProviderObservationOptions{
			CaptureSnapshot: transportBoolPointer(true),
		},
	}
}

func transportOrderedInputBatchTestSnapshot(
	request core.ProviderOrderedInputRequest,
) *core.ProviderSnapshot {
	snapshot := transportOrderedInputTestSnapshot()
	snapshot.App = core.AppDescriptor{
		Name:              request.App,
		PID:               10,
		ProviderBusName:   request.AppIdentity.ProviderBusName,
		ProcessGeneration: request.AppIdentity.ProcessGeneration,
		InputCapable:      true,
	}
	snapshot.WindowHandle = request.WindowHandle
	for index := range snapshot.Elements {
		snapshot.Elements[index].ProviderWindowBusName =
			request.ExpectedWindow.ProviderBusName
		snapshot.Elements[index].ProviderWindowObjectPath =
			request.ExpectedWindow.ProviderObjectPath
		snapshot.Elements[index].ProviderWindowProcessGeneration =
			request.ExpectedWindow.ProcessGeneration
		snapshot.Elements[index].ProviderWindowRole =
			request.ExpectedWindow.Role
		snapshot.Elements[index].ProviderWindowName =
			request.ExpectedWindow.Name
	}
	return snapshot
}

func transportMixedOrderedInputBatchTestSnapshot() *core.ProviderSnapshot {
	request := transportTestProviderOrderedInputRequest("ENTER")
	request.App = "LibreOffice"
	request.AppIdentity = &core.ProviderAppIdentity{
		ProviderBusName:   ":1.84",
		ProcessGeneration: "pid:10:start:100",
	}
	request.WindowHandle = "w2:0123456789abcdef0123456789abcdef"
	request.ExpectedWindow = &core.ProviderWindowExpectation{
		WindowHandle:       request.WindowHandle,
		ProviderBusName:    ":1.84",
		ProcessGeneration:  "pid:10:start:100",
		ProviderObjectPath: "/window/1",
		Role:               "frame",
		Name:               "Sheet",
	}
	snapshot := transportOrderedInputBatchTestSnapshot(request)
	for index := range snapshot.Elements {
		snapshot.Elements[index].ProviderBusName = ":1.84"
		snapshot.Elements[index].ProcessGeneration = "pid:10:start:100"
	}
	snapshot.Elements[3].ProviderObjectPath = "/window/1/save"
	return snapshot
}
