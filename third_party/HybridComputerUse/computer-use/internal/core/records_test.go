package core

import (
	"encoding/json"
	"strings"
	"sync"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func recordsService(t *testing.T) (*Service, *appWideBackend) {
	t.Helper()
	t.Setenv("COMPUTER_USE_ERROR_CONTEXT", "off")
	s, backend, _ := untilScopeService(t, untilMainWindow(), untilDialogWindow())
	return s, backend
}

// observe in records returns every canonical element, and the same refs the
// text tree shows.
func TestObserveRecordsReturnElementsWithRefs(t *testing.T) {
	s, _ := recordsService(t)
	text := ResultText(s.CallForModel(contract.PublicToolAX, map[string]any{"mode": "observe", "window_ref": "w1"}, false))
	record := s.CallRecord(contract.PublicToolAX, map[string]any{"mode": "observe", "window_ref": "w1"}, CallOptions{})
	if !record.OK || record.Op != "ax.observe" || record.Window == nil || record.Window.Ref != "w1" {
		t.Fatalf("observe record: %+v", record)
	}
	refs := 0
	for _, element := range record.Elements {
		if element.Ref != "" {
			refs++
			if !strings.Contains(text, "[ref="+element.Ref+"]") {
				t.Fatalf("records ref %s is not in the text tree:\n%s", element.Ref, text)
			}
		}
	}
	if refs == 0 || record.SnapshotID == "" || record.TimingMS == nil {
		t.Fatalf("observe record has no refs, snapshot id or timing: %+v", record)
	}
}

// since reports changes against an earlier records snapshot; an unknown
// since is an argument error, not a silent full tree.
func TestObserveRecordsSinceDiff(t *testing.T) {
	s, _ := recordsService(t)
	first := s.CallRecord(contract.PublicToolAX, map[string]any{"mode": "observe", "window_ref": "w1"}, CallOptions{})
	second := s.CallRecord(contract.PublicToolAX, map[string]any{"mode": "observe", "window_ref": "w1"}, CallOptions{Since: first.SnapshotID})
	if !second.OK || second.Diff == nil || !second.Diff.Unchanged || second.Elements != nil {
		t.Fatalf("unchanged window must diff as unchanged: %+v", second)
	}
	before := []RecordElement{{Ref: "r1", Role: "button", Name: "Save"}, {Ref: "r2", Role: "button", Name: "Old"}}
	after := []RecordElement{{Ref: "r1", Role: "button", Name: "Save", States: map[string]string{"pressed": "true"}}, {Ref: "r3", Role: "button", Name: "New"}}
	diff := diffRecordElements("s1", before, after)
	if len(diff.Changed) != 1 || diff.Changed[0].Ref != "r1" || len(diff.Added) != 1 || len(diff.Removed) != 1 || diff.Unchanged {
		t.Fatalf("diff: %+v", diff)
	}
	missing := s.CallRecord(contract.PublicToolAX, map[string]any{"mode": "observe", "window_ref": "w1"}, CallOptions{Since: "s999"})
	if missing.OK || missing.Code != contract.AXCodeInvalidArgument || missing.Path != "since" {
		t.Fatalf("unknown since: %+v", missing)
	}
}

// find in records pages by limit alone and follows the cursor; the text
// budget never cuts records.
func TestFindRecordsPageWithCursor(t *testing.T) {
	t.Setenv("COMPUTER_USE_MAX_INLINE_CHARS", "600")
	svc, snapshot, matches := findDeliveryMany(t, 30)
	svc.rememberSnapshot("Independent", snapshot)
	_ = matches
	svc.WindowRefRegistry = NewWindowRefRegistry(8)
	args := map[string]any{"mode": "find", "app": "Independent", "query": map[string]any{"name_contains": "Target"}, "limit": 25}
	backend := &recordingPlatformBackend{accessibilityResponse: &ProviderAccessibilityResult{OK: true, Snapshot: snapshot}}
	svc.Backend = backend
	first := svc.CallRecord(contract.PublicToolAX, args, CallOptions{})
	if !first.OK || first.Page == nil || first.Page.Total != 30 || first.Page.Returned != 25 || first.Page.NextCursor == "" {
		t.Fatalf("first page: ok=%t code=%s msg=%s page=%+v", first.OK, first.Code, first.Message, first.Page)
	}
	second := svc.CallRecord(contract.PublicToolAX, map[string]any{"mode": "find", "cursor": first.Page.NextCursor}, CallOptions{})
	if !second.OK || second.Page.Returned != 5 || second.Page.Offset != 25 || second.Page.NextCursor != "" {
		t.Fatalf("second page: %+v", second.Page)
	}
	seen := map[string]bool{}
	for _, element := range append(first.Matches, second.Matches...) {
		if element.Ref == "" || seen[element.Ref] {
			t.Fatalf("every match needs a distinct ref: %+v", element)
		}
		seen[element.Ref] = true
	}
}

// Records report the same public error as the text contract.
func TestRecordErrorsMatchTextEvidence(t *testing.T) {
	for _, args := range []map[string]any{
		{"mode": "act", "actions": []any{map[string]any{"ref": "w1", "action": "close"}}},
		{"mode": "observe", "window_ref": "w9"},
		{"mode": "find", "cursor": "f9"},
	} {
		s := NewServiceWithBackend(&recordingPlatformBackend{})
		textLine := firstLine(ResultText(NewServiceWithBackend(&recordingPlatformBackend{}).CallForModel(contract.PublicToolAX, args, false)))
		var evidence map[string]any
		if err := json.Unmarshal([]byte(textLine), &evidence); err != nil {
			t.Fatalf("evidence line: %v %q", err, textLine)
		}
		record := s.CallRecord(contract.PublicToolAX, args, CallOptions{})
		if record.OK || record.Code != evidence["code"] || record.Hint != evidence["hint"] {
			t.Fatalf("%v: record code=%s hint=%s, text %v", args, record.Code, record.Hint, evidence)
		}
	}
}

// read resolves each ref live and reports a stale ref without rebinding.
func TestReadRecordsResolveEachRef(t *testing.T) {
	s, _, _, ref := sheetBatchService(t)
	value := "42"
	backend := &recordingPlatformBackend{accessibilityResponse: &ProviderAccessibilityResult{
		OK: true, ResolvedElement: &ElementRecord{ControlType: "push button", Name: "Save", Value: value, States: "[checked]"},
	}}
	s.Backend = backend
	record := s.CallRecord(contract.PublicToolAX, map[string]any{"mode": "read", "refs": []any{ref, "r999"}}, CallOptions{})
	if !record.OK || len(record.Read) != 2 {
		t.Fatalf("read: %+v", record)
	}
	if !record.Read[0].OK || record.Read[0].Element.Name != "Save" || record.Read[0].Element.Value != value ||
		record.Read[0].Element.States["checked"] != "true" {
		t.Fatalf("live read: %+v", record.Read[0])
	}
	if record.Read[1].OK || record.Read[1].Code != contract.AXCodeStaleRef {
		t.Fatalf("never-issued ref must be stale_ref: %+v", record.Read[1])
	}
	if len(backend.actionRequests) != 1 || backend.actionRequests[0].Intent != AccessibilityResolveTarget {
		t.Fatalf("read must only resolve, never act: %+v", backend.actionRequests)
	}
	bad := s.CallRecord(contract.PublicToolAX, map[string]any{"mode": "read", "refs": []any{}}, CallOptions{})
	if bad.OK || bad.Code != contract.AXCodeInvalidArgument {
		t.Fatalf("empty refs: %+v", bad)
	}
}

// read has no text form: the MCP contract rejects it like any unknown mode.
func TestReadIsNotPartOfTheModelContract(t *testing.T) {
	result := NewServiceWithBackend(&recordingPlatformBackend{}).CallForModel(contract.PublicToolAX,
		map[string]any{"mode": "read", "refs": []any{"r1"}}, false)
	if !result.IsError || !strings.Contains(ResultText(result), "invalid_argument") {
		t.Fatalf("text read must be rejected: %s", ResultText(result))
	}
}

// A repeated request_id returns the first result and never runs the call
// again, even when the retry arrives while the first call is running.
func TestRequestIDRunsACallOnce(t *testing.T) {
	shared := &DesktopShared{RequestResults: map[string]StoredRequest{}}
	runs := 0
	release := make(chan struct{})
	started := make(chan struct{})
	run := func() any {
		runs++
		close(started)
		<-release
		return Record{OK: true, Op: "ax.act", InputEmitted: true}
	}
	var wg sync.WaitGroup
	var first, second any
	var replayed bool
	wg.Add(1)
	go func() {
		defer wg.Done()
		first, _, _ = shared.ReplayOnce("id-1", "key", false, run)
	}()
	<-started
	wg.Add(1)
	go func() {
		defer wg.Done()
		second, replayed, _ = shared.ReplayOnce("id-1", "key", true, func() any { runs++; return nil })
	}()
	close(release)
	wg.Wait()
	if runs != 1 || !replayed || first.(Record).InputEmitted != second.(Record).InputEmitted {
		t.Fatalf("runs=%d replayed=%t", runs, replayed)
	}
	if _, _, err := shared.ReplayOnce("id-1", "other", true, run); err == nil {
		t.Fatal("the same request_id with another call must be rejected")
	}
}

// Every public code the core can return is in the published error table.
func TestEveryPublicErrorCodeIsPublished(t *testing.T) {
	core := []string{
		contract.AXCodeInvalidArgument, contract.AXCodeStaleRef, contract.AXCodeWindowChanged, contract.AXCodeWaitTimeout,
		contract.AXCodeEffectUnknown, contract.AXCodeAppUnavailable, contract.AXCodeRuntimeError,
		contract.AXCodeAmbiguousWindow, contract.AXCodeWindowNotFound, contract.AXCodeNoFocusedWindow, contract.AXCodeGUIFrameStale,
	}
	for _, code := range core {
		if _, ok := contract.PublicErrorCodes[code]; !ok {
			t.Fatalf("public code %s is missing from the schema error table", code)
		}
	}
	for code := range axPublicHints {
		if _, ok := contract.PublicErrorCodes[code]; !ok {
			t.Fatalf("hinted code %s is missing from the schema error table", code)
		}
	}
}

// An AX full or screenshot record names its image as the GUI frame, in the
// same screen field a GUI screenshot record uses, so code reads one shape.
func TestAXImageRecordCarriesTheGUIFrameScreen(t *testing.T) {
	t.Setenv("COMPUTER_USE_OUTPUT_DIR", t.TempDir())
	for _, observe := range []string{contract.ObserveFull, contract.ObserveScreenshot} {
		s := NewServiceWithBackend(desktopFrameBackend(t))
		record := s.CallRecord(contract.PublicToolAX, map[string]any{
			"mode": "observe", "app": "Editor", "observe": observe,
		}, CallOptions{})
		if !record.OK || record.Screen == nil || len(record.Images) != 1 {
			t.Fatalf("%s record: %+v", observe, record)
		}
		if record.Screen.Width != 136 || record.Screen.Height != 77 ||
			record.Screen.CoordinateSpace != "screen_image_px" || record.Screen.Image != record.Images[0] {
			t.Fatalf("%s screen is not the delivered frame: %+v", observe, record.Screen)
		}
	}
}

// A zoom record gives code the mapping as data: evidence.zoom and the zoom
// image path, and no screen, because the zoom image is not a GUI frame.
func TestGUIZoomRecordCarriesTheMapping(t *testing.T) {
	t.Setenv("COMPUTER_USE_OUTPUT_DIR", t.TempDir())
	s := NewServiceWithBackend(zoomBackend(t))
	s.guiSnapshot = &AppSnapshot{FrameID: "layout-v1-0123456789abcdef", Width: 1365, Height: 768}
	record := s.CallRecord(contract.PublicToolGUI, map[string]any{
		"actions": []any{map[string]any{"type": "zoom", "region": []any{100.0, 0.0, 108.0, 768.0}}},
	}, CallOptions{})
	zoom, _ := record.Evidence["zoom"].(map[string]any)
	if !record.OK || record.InputEmitted || record.Screen != nil || len(record.Images) != 1 || zoom == nil {
		t.Fatalf("zoom record: %+v", record)
	}
	if zoom["scale_x"] != 5.6264 || zoom["frame"] != "89abcdef" || !strings.Contains(record.Text, "x = 99.5313 + u/5.6264") {
		t.Fatalf("zoom record mapping differs from its text: %+v", record)
	}
}

// GUI records carry the image path and screen size, never base64.
func TestGUIRecordWritesImageFile(t *testing.T) {
	t.Setenv("COMPUTER_USE_OUTPUT_DIR", t.TempDir())
	dispatched := 1
	s := NewServiceWithBackend(&recordingPlatformBackend{desktopResponse: &ProviderDesktopResult{
		OK: true, GUIResult: ProviderGUIResult{Dispatched: &dispatched},
		Snapshot: &AppSnapshot{ScreenshotPNGBase64: testPNGBase64(t, 40, 30), CoordinateSpace: "screen_image_px",
			FrameID: "f", Width: 40, Height: 30},
	}})
	record := s.CallRecord(contract.PublicToolGUI, map[string]any{"actions": []any{map[string]any{"type": "screenshot"}}}, CallOptions{})
	encoded, _ := json.Marshal(record)
	if !record.OK || record.Screen == nil || record.Screen.Width != 40 || record.Screen.Image == "" ||
		strings.Contains(string(encoded), "iVBOR") {
		t.Fatalf("gui record: %s", encoded)
	}
}
