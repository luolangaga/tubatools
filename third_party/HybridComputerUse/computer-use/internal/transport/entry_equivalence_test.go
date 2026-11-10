package transport

import (
	"encoding/base64"
	"encoding/json"
	"os"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

// equivalenceCall is one public tool call in a cross-entry scenario.
type equivalenceCall struct {
	tool string
	args string
}

// equivalenceScenario builds two identical services: one is driven through
// MCP tools/call, the other through computer-use call.
type equivalenceScenario struct {
	name  string
	build func(t *testing.T) *core.Service
	calls []equivalenceCall
}

func equivalenceScenarios() []equivalenceScenario {
	return []equivalenceScenario{
		{
			name: "apps then observe a window",
			build: func(t *testing.T) *core.Service {
				s, _, _ := transportUntilScopeService(t, transportUntilMainWindow(), transportUntilDialogWindow())
				return s
			},
			calls: []equivalenceCall{
				{contract.PublicToolAX, `{"mode":"observe","window_ref":"w1"}`},
				{contract.PublicToolAX, `{"mode":"observe","window_ref":"w1","observe":"diff"}`},
				{contract.PublicToolAX, `{"mode":"find","window_ref":"w1","query":{"role":"button"}}`},
			},
		},
		{
			name: "apps listing",
			build: func(t *testing.T) *core.Service {
				s, _ := transportWindowScriptService(transportEditorApps(
					transportTestWindow{transportTestHandleReport, "Quarterly report - Editor", true},
					transportTestWindow{transportTestHandleMail, "Inbox - Mail", false},
				))
				return s
			},
			calls: []equivalenceCall{{contract.PublicToolAX, `{"mode":"apps"}`}},
		},
		{
			name: "argument errors",
			build: func(t *testing.T) *core.Service {
				return core.NewServiceWithBackend(&transportRecordingPlatformBackend{})
			},
			calls: []equivalenceCall{
				{contract.PublicToolAX, `{"mode":"act","actions":[{"ref":"w1","action":"close"}]}`},
				{contract.PublicToolAX, `{"mode":"observe","window_ref":"w9"}`},
				{contract.PublicToolGUI, `{"actions":[]}`},
			},
		},
		{
			name: "GUI screenshot",
			build: func(t *testing.T) *core.Service {
				dispatched := 1
				return core.NewServiceWithBackend(&transportRecordingPlatformBackend{desktopResponse: &core.ProviderDesktopResult{
					OK:        true,
					GUIResult: core.ProviderGUIResult{Dispatched: &dispatched},
					Snapshot: &core.AppSnapshot{
						ScreenshotPNGBase64: transportTestPNGBase64(t, 100, 80),
						CoordinateSpace:     "screen_image_px",
						FrameID:             "frame-1",
						Width:               100,
						Height:              80,
					},
				}})
			},
			calls: []equivalenceCall{{contract.PublicToolGUI, `{"actions":[{"type":"screenshot"}]}`}},
		},
	}
}

// The MCP result and the computer-use call output carry the same text
// content byte for byte; call replaces each image's base64 with a file
// that holds the same bytes.
func TestCallOutputMatchesMCPToolResult(t *testing.T) {
	t.Setenv("COMPUTER_USE_ERROR_CONTEXT", "off")
	t.Setenv("COMPUTER_USE_STRUCTURED_CONTENT", "")
	t.Setenv("COMPUTER_USE_OUTPUT_DIR", t.TempDir())
	for _, scenario := range equivalenceScenarios() {
		t.Run(scenario.name, func(t *testing.T) {
			viaMCP := scenario.build(t)
			viaCall := scenario.build(t)
			for index, call := range scenario.calls {
				wire := handleMCPRequest(map[string]any{
					"jsonrpc": "2.0", "id": float64(index + 1), "method": "tools/call",
					"params": map[string]any{"name": call.tool, "arguments": decodeTestArgs(t, call.args)},
				}, viaMCP)
				mcpResult := wire["result"].(core.ToolCallResult)

				output, _, err := runCallCommand([]string{call.tool, "--args", call.args}, viaCall)
				if err != nil {
					t.Fatalf("call %d: %v", index, err)
				}
				cliResult := output.(core.ToolCallResult)
				compareEntryResults(t, index, mcpResult, cliResult)
			}
		})
	}
}

func compareEntryResults(t *testing.T, index int, mcp, cli core.ToolCallResult) {
	t.Helper()
	if mcp.IsError != cli.IsError || cli.StructuredContent != nil {
		t.Fatalf("call %d: isError %t/%t structured=%v", index, mcp.IsError, cli.IsError, cli.StructuredContent)
	}
	if len(mcp.Content) != len(cli.Content) {
		t.Fatalf("call %d: %d MCP items, %d call items", index, len(mcp.Content), len(cli.Content))
	}
	for item := range mcp.Content {
		want, got := mcp.Content[item], cli.Content[item]
		if want.Type != got.Type {
			t.Fatalf("call %d item %d: type %s vs %s", index, item, want.Type, got.Type)
		}
		if want.Type != "image" {
			if want.Text != got.Text {
				t.Fatalf("call %d item %d differs:\nMCP:  %q\ncall: %q", index, item, want.Text, got.Text)
			}
			continue
		}
		if got.Data != "" || got.Path == "" || want.Path != "" {
			t.Fatalf("call %d item %d: call must return an image path, MCP inline data", index, item)
		}
		onDisk, err := os.ReadFile(got.Path)
		if err != nil {
			t.Fatal(err)
		}
		if base64.StdEncoding.EncodeToString(onDisk) != want.Data {
			t.Fatalf("call %d item %d: image file differs from the MCP image", index, item)
		}
	}
	encoded, err := json.Marshal(cli)
	if err != nil {
		t.Fatal(err)
	}
	if len(encoded) > 200000 {
		t.Fatalf("call %d: call output unexpectedly large (%d bytes)", index, len(encoded))
	}
}

func decodeTestArgs(t *testing.T, raw string) map[string]any {
	t.Helper()
	args, err := readArguments(raw, "")
	if err != nil {
		t.Fatal(err)
	}
	return args
}
