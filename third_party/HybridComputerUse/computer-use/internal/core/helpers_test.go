package core

// Fixtures, test doubles and shorthands shared by several test files.

import (
	"bytes"
	"encoding/base64"
	"fmt"
	"image"
	"image/png"
	"io/fs"
	"os"
	"path/filepath"
	"slices"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func (s *Service) verifyResolvedAXRefWithProbe(
	app,
	displayRef string,
	target *semanticTarget,
	goal verifyGoal,
	timeoutMS,
	stableMS int,
	probe func(ProviderActionRequest) (*ProviderAccessibilityResult, error),
) ToolCallResult {
	return s.verifyResolvedAXRefWithProbes(
		app,
		displayRef,
		target,
		goal,
		timeoutMS,
		stableMS,
		probe,
		nil,
	)
}

// withActionOutcome applies an outcome without request facts, so no
// actionability evidence is attached.
func withActionOutcome(response ToolCallResult) ToolCallResult {
	return withActionOutcomeForRequest(response, AXServiceRequest{}, nil)
}

func lookupElement(snapshot *AppSnapshot, elementIndex string) (*ElementRecord, error) {
	return lookupElementFor(snapshot, elementIndex, "", "")
}

func (registry *RefRegistry) Acquire(
	appIdentity,
	windowHandle,
	semanticTarget string,
	role CanonicalRole,
	name,
	revision string,
	providerIdentity ...string,
) string {
	return registry.AcquireTyped(
		AXLocatorSemantic,
		appIdentity,
		windowHandle,
		semanticTarget,
		role,
		name,
		revision,
		providerIdentity...,
	)
}

func runtimeReportedFailure(response ProviderAccessibilityResult) ToolCallResult {
	return (*Service)(nil).runtimeReportedFailure("", AXServiceRequest{}, response)
}

func (s *Service) expandShortAXRef(app, address string) (string, error) {
	address = strings.TrimSpace(address)
	if !isShortAXRef(address) {
		return address, nil
	}
	entry, err := s.resolveShortAXRef(app, address)
	if err != nil {
		return "", err
	}
	return entry.SemanticTarget, nil
}

func testPNGBase64(t *testing.T, width, height int) string {
	t.Helper()
	var encoded bytes.Buffer
	if err := png.Encode(&encoded, image.NewRGBA(image.Rect(0, 0, width, height))); err != nil {
		t.Fatalf("encode test PNG: %v", err)
	}
	return base64.StdEncoding.EncodeToString(encoded.Bytes())
}

// testDesktopFrame is a GUI screenshot response: the full-desktop frame that
// every AX image is captured as.
func testDesktopFrame(t *testing.T, frameID string, width, height int) *ProviderDesktopResult {
	t.Helper()
	return &ProviderDesktopResult{OK: true, Snapshot: &AppSnapshot{
		ScreenshotPNGBase64: testPNGBase64(t, width, height),
		ScreenshotMimeType:  "image/png",
		CoordinateSpace:     "screen_image_px",
		FrameID:             frameID,
		Width:               width,
		Height:              height,
	}}
}

type recordingPlatformBackend struct {
	calls                  []string
	actionRequests         []ProviderActionRequest
	desktopRequests        []ProviderDesktopRequest
	observeRequests        []ProviderObserveRequest
	accessibilityResponse  *ProviderAccessibilityResult
	accessibilityResponses []ProviderAccessibilityResult
	desktopResponse        *ProviderDesktopResult
	windowResponse         *ProviderWindowResult
	err                    error
	processState           string
	windowKnown            bool
	windowAlive            bool
}

func (b *recordingPlatformBackend) accessibilityResult(
	call string,
) (ProviderAccessibilityResult, error) {
	b.calls = append(b.calls, call)
	if len(b.accessibilityResponses) > 0 {
		result := b.accessibilityResponses[0]
		b.accessibilityResponses = b.accessibilityResponses[1:]
		result.PrepareForService()
		return result, b.err
	}
	if b.accessibilityResponse == nil {
		return ProviderAccessibilityResult{}, b.err
	}
	result := *b.accessibilityResponse
	result.PrepareForService()
	return result, b.err
}

func (b *recordingPlatformBackend) ObserveApps(
	ProviderObserveAppsRequest,
) (ProviderAccessibilityResult, error) {
	return b.accessibilityResult("observe_apps")
}

func (b *recordingPlatformBackend) Observe(
	request ProviderObserveRequest,
) (ProviderAccessibilityResult, error) {
	b.observeRequests = append(b.observeRequests, request)
	return b.accessibilityResult("observe")
}

func (b *recordingPlatformBackend) ResolveAndAct(
	request ProviderActionRequest,
) (ProviderAccessibilityResult, error) {
	b.actionRequests = append(b.actionRequests, request)
	return b.accessibilityResult("resolve_and_act")
}

func (b *recordingPlatformBackend) ExecuteBatch(
	request ProviderDesktopRequest,
) (ProviderDesktopResult, error) {
	b.desktopRequests = append(b.desktopRequests, request)
	b.calls = append(b.calls, "desktop_execute_batch")
	if b.desktopResponse == nil {
		return ProviderDesktopResult{}, b.err
	}
	return *b.desktopResponse, b.err
}

func (b *recordingPlatformBackend) ListWindows(
	ProviderWindowListRequest,
) (ProviderWindowResult, error) {
	b.calls = append(b.calls, "window_list")
	if b.windowResponse == nil {
		return ProviderWindowResult{}, b.err
	}
	return *b.windowResponse, b.err
}

func (b *recordingPlatformBackend) ActOnWindow(
	request ProviderWindowActionRequest,
) (ProviderWindowResult, error) {
	b.calls = append(b.calls, "window_"+string(request.Action))
	if b.windowResponse == nil {
		return ProviderWindowResult{}, b.err
	}
	return *b.windowResponse, b.err
}

func (b *recordingPlatformBackend) WindowForPID(int) (bool, bool) {
	return b.windowKnown, b.windowAlive
}

func (*recordingPlatformBackend) HostInfo() HostBackendInfo {
	return HostBackendInfo{Platform: "test"}
}

func (*recordingPlatformBackend) RuntimeEnvironment(base []string) []string {
	return base
}

func (b *recordingPlatformBackend) ProcessState(int) string {
	return b.processState
}

func findToolDefinition(t *testing.T, name string) contract.ToolDefinition {
	t.Helper()
	for _, tool := range contract.AllToolDefinitions() {
		if tool.Name == name {
			return tool
		}
	}
	t.Fatalf("missing tool definition %q", name)
	return contract.ToolDefinition{}
}

func mainGoSource(t *testing.T) string {
	t.Helper()
	// The module root is two directories up (computer-use/internal/<pkg>).
	root := filepath.Join("..", "..")
	names := make([]string, 0)
	err := filepath.WalkDir(root, func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if entry.IsDir() {
			switch entry.Name() {
			case "vendor", "testdata", "native":
				return filepath.SkipDir
			}
			return nil
		}
		name := entry.Name()
		if strings.HasSuffix(name, ".go") && !strings.HasSuffix(name, "_test.go") {
			names = append(names, path)
		}
		return nil
	})
	if err != nil {
		t.Fatalf("read Go source directory: %v", err)
	}
	slices.Sort(names)
	var source strings.Builder
	for _, name := range names {
		data, readErr := os.ReadFile(name)
		if readErr != nil {
			t.Fatalf("read %s: %v", name, readErr)
		}
		source.Write(data)
		source.WriteByte('\n')
	}
	return source.String()
}

// ---------------------------------------------------------------------------
// expect: act -> settle -> verify
// ---------------------------------------------------------------------------

func expectSnapshot(elements ...ElementRecord) *AppSnapshot {
	return &AppSnapshot{
		App:       AppDescriptor{Name: "gedit", PID: 7},
		TreeLines: []string{"0 frame Doc"},
		Elements:  elements,
	}
}

func editorFormSnapshot() *AppSnapshot {
	return &AppSnapshot{
		App:          AppDescriptor{Name: "Editor", PID: 42},
		WindowTitle:  "Document",
		WindowHandle: "0x42",
		SnapshotID:   "editor-form",
		TreeLines: []string{
			"\t0 frame \"Document\"",
			"\t\t1 panel \"Form\"",
			"\t\t\t2 entry \"Name\" [focused]",
			"\t\t\t3 push button \"Save\" [click]",
			"\t\t4 status bar \"Ready\"",
		},
		Elements: []ElementRecord{
			{Index: 0, RuntimeID: []int{0}, ControlType: "frame", Name: "Document"},
			{Index: 1, RuntimeID: []int{0, 0}, ControlType: "panel", Name: "Form"},
			{Index: 2, RuntimeID: []int{0, 0, 0}, ControlType: "entry", Name: "Name", States: "[focused]"},
			{Index: 3, RuntimeID: []int{0, 0, 1}, ControlType: "push button", Name: "Save", States: "[has-click-action]"},
			{Index: 4, RuntimeID: []int{0, 1}, ControlType: "status bar", Name: "Ready"},
		},
		Scope: contract.AXScopeWindow,
	}
}

func typedTreeTextForTest(t *testing.T, snapshot *AppSnapshot) string {
	t.Helper()
	typed, err := buildAXSnapshot(snapshot)
	if err != nil {
		t.Fatal(err)
	}
	NewRefRegistry(128).AssignSnapshotRefs(typed)
	if err := distillAXSnapshot(typed); err != nil {
		t.Fatal(err)
	}
	lines, err := RenderAXSnapshotYAML(typed, AXRenderOptions{RootKey: "ax"})
	if err != nil {
		t.Fatal(err)
	}
	snapshot.AXSnapshot = typed
	snapshot.TreeLines = lines
	return strings.Join(lines, "\n")
}

func windowTransitionSnapshot(
	handle,
	title string,
	withProviderIdentity bool,
) *AppSnapshot {
	snapshot := editorFormSnapshot()
	snapshot.App = AppDescriptor{
		Name:              "Editor",
		PID:               42,
		ProviderBusName:   ":1.42",
		ProcessGeneration: "pid:42:start:100",
	}
	snapshot.WindowTitle = title
	snapshot.WindowHandle = handle
	snapshot.SnapshotID = title
	snapshot.Elements[0].ControlType = "dialog"
	snapshot.Elements[0].Name = title
	snapshot.Elements[3].Name = "OK"
	if withProviderIdentity {
		for index := range snapshot.Elements {
			record := &snapshot.Elements[index]
			record.ProviderBusName = ":1.42"
			record.ProviderObjectPath = fmt.Sprintf("/dialog/%d", index)
			record.ProcessGeneration = "pid:42:start:100"
			record.ProviderWindowBusName = ":1.42"
			record.ProviderWindowObjectPath = "/dialog"
			record.ProviderWindowProcessGeneration = "pid:42:start:100"
			record.ProviderWindowRole = "dialog"
			record.ProviderWindowName = title
		}
	}
	return snapshot
}

func sheetBatchService(t *testing.T) (*Service, string, string, string) {
	t.Helper()
	service := NewService()
	appRef := service.AppRefRegistry.Acquire(
		"LibreOffice", ":1.84", "pid:10:start:100", "c1",
	)
	handle := "w2:0123456789abcdef0123456789abcdef"
	windowRef := service.WindowRefRegistry.Acquire(
		"libreoffice", handle, `frame "Sheet"`, "c1",
		":1.84", "/window/1", "pid:10:start:100", "frame", "Sheet",
		appRef,
	)
	elementRef := service.RefRegistry.Acquire(
		"libreoffice", handle, `button "Save"`, CanonicalRole("button"),
		"Save", "c1",
		":1.84", "/window/1/save", "pid:10:start:100",
		":1.84", "/window/1", "pid:10:start:100", "frame", "Sheet",
		appRef, windowRef,
	)
	return service, appRef, windowRef, elementRef
}
