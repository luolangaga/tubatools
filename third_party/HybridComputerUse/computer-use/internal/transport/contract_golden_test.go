package transport

import (
	"bytes"
	"encoding/json"
	"errors"
	"flag"
	"os"
	"path/filepath"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

var updateContract = flag.Bool(
	"update-contract",
	false,
	"rewrite testdata/contract/public_contract.json from the current build",
)

// contractEnvironment lists every environment variable that can change the
// model-visible contract. The golden test clears them so it always observes
// the compiled defaults.
var contractEnvironment = []string{
	"COMPUTER_USE_CHANNELS",
	"COMPUTER_USE_STRUCTURED_CONTENT",
	"COMPUTER_USE_ERROR_CONTEXT",
	"COMPUTER_USE_OUTPUT_MODE",
	"COMPUTER_USE_OUTPUT_DIR",
	"COMPUTER_USE_OUTPUT_THRESHOLD",
	"COMPUTER_USE_MAX_INLINE_CHARS",
	"COMPUTER_USE_TRACE_FILE",
}

// contractCalls are tool calls that are rejected before any desktop input.
// Their responses are model-visible and must stay stable.
var contractCalls = []struct {
	Tool      string
	Arguments map[string]any
}{
	{"AX", map[string]any{}},
	{"AX", map[string]any{"mode": "bogus"}},
	{"AX", map[string]any{"mode": "assert_ax"}},
	{"AX", map[string]any{"mode": "observe", "bogus": 1}},
	{"AX", map[string]any{"mode": "observe", "observation_target": "apps"}},
	{"AX", map[string]any{"mode": "observe", "app_ref": "a1"}},
	{"AX", map[string]any{"mode": "observe"}},
	{"AX", map[string]any{"mode": "observe", "app": "gedit", "window_ref": "w1"}},
	{"AX", map[string]any{"mode": "act"}},
	{"AX", map[string]any{"mode": "act", "actions": []any{}}},
	{"AX", map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "click"},
	}}},
	{"AX", map[string]any{"mode": "act", "app": "x", "actions": []any{
		map[string]any{"type": "click", "ref": "r1"},
	}}},
	{"AX", map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "bogus", "ref": "r1"},
	}}},
	{"AX", map[string]any{"mode": "act", "actions": []any{
		map[string]any{"ref": "w1", "action": "close"},
	}}},
	{"AX", map[string]any{"mode": "act", "actions": []any{
		map[string]any{"ref": "r1", "action": "press"},
	}}},
	{"AX", map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "click", "target": map[string]any{"ref": "r1"}},
	}}},
	{"AX", map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "close", "ref": "w1"},
		map[string]any{"type": "click", "ref": "r1"},
	}}},
	{"AX", map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "activate", "ref": "w1", "title": "Report"},
	}}},
	{"AX", map[string]any{"mode": "act", "window_ref": "w1", "actions": []any{
		map[string]any{"type": "keypress", "keys": []any{}},
	}}},
	{"AX", map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "click", "ref": "r1"},
	}}},
	{"AX", map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "keypress", "keys": []any{"CTRL", "S"}},
	}}},
	{"AX", map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "activate", "title": "Report"},
	}}},
	{"AX", map[string]any{"mode": "find"}},
	{"AX", map[string]any{"mode": "find", "app": "gedit", "query": map[string]any{}}},
	{"AX", map[string]any{"mode": "find", "app": "gedit", "query": map[string]any{"name": "Save"}}},
	{"AX", map[string]any{"mode": "find", "app": "gedit", "limit": -1,
		"query": map[string]any{"role": "button"}}},
	{"AX", map[string]any{"mode": "find", "cursor": "f1"}},
	{"AX", map[string]any{"mode": "observe", "app": "gedit",
		"until": map[string]any{"type": "value_equals", "ref": "r1"}}},
	{"AX", map[string]any{"mode": "observe", "app": "gedit",
		"until": map[string]any{"type": "title", "text": "Saved", "timeout_ms": 1}}},
	{"AX", map[string]any{"mode": "observe", "app": "gedit"}},
	{"AX", map[string]any{"mode": "apps"}},
	{"GUI", map[string]any{}},
	{"GUI", map[string]any{"actions": []any{}}},
	{"GUI", map[string]any{"actions": []any{
		map[string]any{"type": "screenshot"},
		map[string]any{"type": "click", "x": 1, "y": 1},
	}}},
	{"GUI", map[string]any{"actions": []any{map[string]any{"type": "click"}}}},
	{"GUI", map[string]any{"actions": []any{
		map[string]any{"type": "click", "x": 1, "y": 1, "button": "middle-ish"},
	}}},
	{"GUI", map[string]any{"actions": []any{map[string]any{"type": "wait", "ms": 5}}}},
	{"GUI", map[string]any{"actions": []any{
		map[string]any{"type": "scroll", "x": 1, "y": 1},
	}}},
	{"GUI", map[string]any{"actions": []any{
		map[string]any{"type": "keypress", "keys": []any{}},
	}}},
	{"GUI", map[string]any{"actions": []any{map[string]any{"type": "type"}}}},
	{"GUI", map[string]any{"actions": []any{map[string]any{"type": "bogus"}}}},
	{"GUI", map[string]any{"actions": []any{
		map[string]any{"type": "drag", "path": []any{map[string]any{"x": 1, "y": 1}}},
	}}},
	{"GUI", map[string]any{"actions": []any{map[string]any{"type": "screenshot"}}}},
	{"Nope", map[string]any{}},
	{" ax ", map[string]any{"mode": "observe", "observation_target": "apps"}},
}

type contractExchange struct {
	Request  any `json:"request"`
	Response any `json:"response"`
}

type publicContract struct {
	Initialize map[string]any     `json:"initialize"`
	ToolsList  any                `json:"tools_list"`
	Protocol   []contractExchange `json:"protocol"`
	Calls      []contractExchange `json:"calls"`
	CLI        map[string]string  `json:"cli"`
}

func TestPublicContractMatchesGolden(t *testing.T) {
	for _, name := range contractEnvironment {
		t.Setenv(name, "")
	}
	got := capturePublicContract(t)
	encoded, err := json.MarshalIndent(got, "", "  ")
	if err != nil {
		t.Fatalf("encode contract: %v", err)
	}
	encoded = append(encoded, '\n')
	path := filepath.Join("..", "..", "testdata", "contract", "public_contract.json")
	if *updateContract {
		if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
			t.Fatal(err)
		}
		if err := os.WriteFile(path, encoded, 0o644); err != nil {
			t.Fatal(err)
		}
		return
	}
	want, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("read golden contract (regenerate with -update-contract): %v", err)
	}
	if !bytes.Equal(encoded, want) {
		t.Fatalf(
			"model-visible MCP contract changed; review the diff and rerun with " +
				"-update-contract only for an intentional contract revision",
		)
	}
}

func capturePublicContract(t *testing.T) publicContract {
	t.Helper()
	contract := publicContract{
		Initialize: map[string]any{},
		CLI:        map[string]string{},
	}
	for _, protocol := range []string{core.MCPProtocolLegacy, core.MCPProtocolStructured} {
		response := handleMCPRequest(map[string]any{
			"jsonrpc": "2.0",
			"id":      float64(1),
			"method":  "initialize",
			"params":  map[string]any{"protocolVersion": protocol},
		}, core.NewService())
		contract.Initialize[protocol] = withoutServerVersion(t, response)
	}

	svc := core.NewServiceWithBackend(&transportRecordingPlatformBackend{
		err: errors.New("desktop unavailable in contract test"),
	})
	handleMCPRequest(map[string]any{
		"jsonrpc": "2.0",
		"id":      float64(1),
		"method":  "initialize",
		"params":  map[string]any{"protocolVersion": core.MCPProtocolLegacy},
	}, svc)
	contract.ToolsList = roundTripJSON(t, handleMCPRequest(map[string]any{
		"jsonrpc": "2.0",
		"id":      float64(2),
		"method":  "tools/list",
		"params":  map[string]any{},
	}, svc))
	for _, method := range []string{"ping", "bogus/method"} {
		request := map[string]any{
			"jsonrpc": "2.0",
			"id":      float64(3),
			"method":  method,
			"params":  map[string]any{},
		}
		contract.Protocol = append(contract.Protocol, contractExchange{
			Request:  method,
			Response: roundTripJSON(t, handleMCPRequest(request, svc)),
		})
	}
	for index, call := range contractCalls {
		request := map[string]any{
			"jsonrpc": "2.0",
			"id":      float64(10 + index),
			"method":  "tools/call",
			"params": map[string]any{
				"name":      call.Tool,
				"arguments": roundTripJSON(t, call.Arguments),
			},
		}
		contract.Calls = append(contract.Calls, contractExchange{
			Request:  map[string]any{"tool": call.Tool, "arguments": call.Arguments},
			Response: roundTripJSON(t, handleMCPRequest(request, svc)),
		})
	}

	for _, args := range [][]string{
		{"--help"},
		{"help", "mcp"},
		{"help", "call"},
		{"help", "doctor"},
		{"help", "version"},
	} {
		var out bytes.Buffer
		if err := RunCLI(args, &out); err != nil {
			t.Fatalf("runCLI(%q): %v", args, err)
		}
		contract.CLI[joinArgs(args)] = out.String()
	}
	return contract
}

func withoutServerVersion(t *testing.T, response map[string]any) map[string]any {
	t.Helper()
	decoded := roundTripJSON(t, response).(map[string]any)
	result := decoded["result"].(map[string]any)
	serverInfo := result["serverInfo"].(map[string]any)
	if serverInfo["version"] != contract.ServerVersion {
		t.Fatalf("serverInfo.version = %#v, want %q", serverInfo["version"], contract.ServerVersion)
	}
	serverInfo["version"] = "<version>"
	return decoded
}

// roundTripJSON converts typed responses into the JSON shape a client sees.
func roundTripJSON(t *testing.T, value any) any {
	t.Helper()
	encoded, err := json.Marshal(value)
	if err != nil {
		t.Fatalf("encode %T: %v", value, err)
	}
	var decoded any
	if err := json.Unmarshal(encoded, &decoded); err != nil {
		t.Fatalf("decode %T: %v", value, err)
	}
	return decoded
}

func joinArgs(args []string) string {
	var out bytes.Buffer
	for index, arg := range args {
		if index > 0 {
			out.WriteByte(' ')
		}
		out.WriteString(arg)
	}
	return out.String()
}
