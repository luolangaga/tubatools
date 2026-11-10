package transport

import (
	"bytes"
	"encoding/json"
	"strings"
	"testing"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

func TestCallSequenceStopsAfterFirstToolError(t *testing.T) {
	output, hasError, err := runCallCommand([]string{
		"--calls",
		`[{"tool":"AX","args":{"mode":"invalid"}},{"tool":"GUI","args":{"actions":[]}}]`,
	}, core.NewService())
	if err != nil {
		t.Fatal(err)
	}
	if !hasError {
		t.Fatal("expected hasError")
	}
	items, ok := output.([]map[string]any)
	if !ok {
		t.Fatalf("output type = %T", output)
	}
	if len(items) != 1 {
		t.Fatalf("sequence output count = %d, want 1", len(items))
	}
}

func TestStandaloneCallRespectsDaemonInputOwner(t *testing.T) {
	_, _, socket := startInProcessDaemon(t, time.Minute)
	t.Setenv("HCU_SOCKET", socket)
	backend := newFakeDesktopBackend()
	svc := core.NewServiceWithBackend(backend)
	observe := `{"mode":"observe","app":"LibreOffice","observe":"tree"}`
	if _, hasError, err := runCallCommand([]string{"AX", "--args", observe}, svc); err != nil || hasError {
		t.Fatalf("standalone read failed: hasError=%v err=%v", hasError, err)
	}
	for _, args := range [][]string{
		{"AX", "--args", `{"mode":"act","actions":[{"type":"click","ref":"r1"}],"observe":"none"}`},
		{"GUI", "--args", `{"actions":[{"type":"type","text":"must not type"}]}`},
		{"--calls", `[{"tool":"AX","args":` + observe + `},{"tool":"AX","args":{"mode":"act","actions":[{"type":"click","ref":"r1"}],"observe":"none"}}]`},
	} {
		output, hasError, err := runCallCommand(args, svc)
		if err != nil || !hasError {
			t.Fatalf("input not refused: hasError=%v err=%v output=%v", hasError, err, output)
		}
		encoded, _ := json.Marshal(output)
		if !strings.Contains(string(encoded), "--attach") || !strings.Contains(string(encoded), `\"input_emitted\":false`) {
			t.Fatalf("refusal lost recovery or no-input evidence: %s", encoded)
		}
		if backend.inputCount() != 0 {
			t.Fatalf("standalone call emitted %d inputs before refusal", backend.inputCount())
		}
	}
	if _, hasError, err := runCallCommand([]string{"AX", "--args", `{"mode":"find","app":"LibreOffice","query":{"name_equals":"Save"}}`}, svc); err != nil || hasError {
		t.Fatalf("standalone find failed: hasError=%v err=%v", hasError, err)
	}
}

func TestStandaloneCallInputWithoutDaemon(t *testing.T) {
	t.Setenv("HCU_SOCKET", shortSocket(t))
	backend := newFakeDesktopBackend()
	svc := core.NewServiceWithBackend(backend)
	_, hasError, err := runCallCommand([]string{"--calls", `[
		{"tool":"AX","args":{"mode":"observe","app":"LibreOffice","observe":"tree"}},
		{"tool":"AX","args":{"mode":"act","actions":[{"type":"click","ref":"r1"}],"observe":"none"}}
	]`}, svc)
	if err != nil || hasError || backend.inputCount() != 1 {
		t.Fatalf("one-shot input changed: hasError=%v err=%v inputs=%d", hasError, err, backend.inputCount())
	}
}

func TestReadArgumentsAcceptsJSONObject(t *testing.T) {
	args, err := readArguments(`{"app":"Text Editor","pages":2}`, "")
	if err != nil {
		t.Fatal(err)
	}
	if args["app"] != "Text Editor" {
		t.Fatalf("app = %v", args["app"])
	}
	if args["pages"].(json.Number).String() != "2" {
		t.Fatalf("pages = %v", args["pages"])
	}
}

func TestRemovedCLICompatibilityCommandsStayRemoved(t *testing.T) {
	for _, command := range []string{"list-apps", "snapshot"} {
		var output bytes.Buffer
		err := RunCLI([]string{command}, &output)
		if err == nil || !strings.Contains(err.Error(), "unknown command") {
			t.Fatalf("removed CLI command %q was accepted: err=%v output=%q",
				command, err, output.String())
		}
	}
}

// computer-use call prints the model-facing result: no structuredContent,
// and images as file paths rather than base64.
func TestCallPrintsModelFacingResultWithImageFiles(t *testing.T) {
	t.Setenv("COMPUTER_USE_OUTPUT_DIR", t.TempDir())
	dispatched := 1
	svc := core.NewServiceWithBackend(&transportRecordingPlatformBackend{desktopResponse: &core.ProviderDesktopResult{
		OK:        true,
		GUIResult: core.ProviderGUIResult{Dispatched: &dispatched},
		Snapshot: &core.AppSnapshot{
			ScreenshotPNGBase64: transportTestPNGBase64(t, 40, 30), CoordinateSpace: "screen_image_px",
			FrameID: "f", Width: 40, Height: 30,
		},
	}})
	output, _, err := runCallCommand([]string{"GUI", "--args", `{"actions":[{"type":"screenshot"}]}`}, svc)
	if err != nil {
		t.Fatal(err)
	}
	encoded, err := json.Marshal(output)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(string(encoded), "structuredContent") ||
		strings.Contains(string(encoded), `"data"`) ||
		!strings.Contains(string(encoded), `"path"`) {
		t.Fatalf("call output is not the model-facing result with image files: %s", encoded)
	}
}
