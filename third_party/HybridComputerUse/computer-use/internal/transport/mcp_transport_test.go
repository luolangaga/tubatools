package transport

import (
	"bytes"
	"encoding/json"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

func TestMCPInitializeResponseContainsToolsCapability(t *testing.T) {
	request := map[string]any{
		"jsonrpc": "2.0",
		"id":      float64(1),
		"method":  "initialize",
		"params":  map[string]any{},
	}
	response := handleMCPRequest(request, core.NewService())
	result, ok := response["result"].(map[string]any)
	if !ok {
		t.Fatalf("missing result: %#v", response)
	}
	capabilities := result["capabilities"].(map[string]any)
	if _, ok := capabilities["tools"]; !ok {
		t.Fatalf("missing tools capability: %#v", capabilities)
	}
	if result["instructions"] != contract.ServerInstructions {
		t.Fatal("initialize must return the self-contained static serverInstructions")
	}
}

func TestMCPDefaultsToModelVisibleTextEvidence(t *testing.T) {
	for _, tc := range []struct {
		requested      string
		wantNegotiated string
	}{
		{core.MCPProtocolLegacy, core.MCPProtocolLegacy},
		{core.MCPProtocolStructured, core.MCPProtocolStructured},
		{"2025-11-25", core.MCPProtocolStructured},
	} {
		svc := core.NewService()
		init := handleMCPRequest(map[string]any{
			"jsonrpc": "2.0",
			"id":      float64(1),
			"method":  "initialize",
			"params": map[string]any{
				"protocolVersion": tc.requested,
			},
		}, svc)
		result := init["result"].(map[string]any)
		if result["protocolVersion"] != tc.wantNegotiated {
			t.Fatalf("requested %s negotiated %#v, want %s",
				tc.requested, result["protocolVersion"], tc.wantNegotiated)
		}

		call := handleMCPRequest(map[string]any{
			"jsonrpc": "2.0",
			"id":      float64(2),
			"method":  "tools/call",
			"params": map[string]any{
				"name": "AX",
				"arguments": map[string]any{
					"mode": "invalid",
				},
			},
		}, svc)
		callResult := call["result"].(core.ToolCallResult)
		if callResult.StructuredContent != nil ||
			len(callResult.Content) == 0 {
			t.Fatalf("requested %s must receive text evidence by default: %#v",
				tc.requested, callResult)
		}
	}
}

func TestMCPStructuredContentIsExplicitOptIn(t *testing.T) {
	t.Setenv("COMPUTER_USE_STRUCTURED_CONTENT", "1")
	svc := core.NewService()
	handleMCPRequest(map[string]any{
		"jsonrpc": "2.0",
		"id":      float64(1),
		"method":  "initialize",
		"params": map[string]any{
			"protocolVersion": core.MCPProtocolStructured,
		},
	}, svc)
	call := handleMCPRequest(map[string]any{
		"jsonrpc": "2.0",
		"id":      float64(2),
		"method":  "tools/call",
		"params": map[string]any{
			"name":      "AX",
			"arguments": map[string]any{"mode": "invalid"},
		},
	}, svc)
	result := call["result"].(core.ToolCallResult)
	if result.StructuredContent == nil {
		t.Fatalf("2025-06-18 opt-in must preserve structuredContent: %#v", result)
	}
	for _, item := range result.Content {
		if strings.HasPrefix(item.Text, `{"ok":`) {
			t.Fatalf("opt-in structured clients should not receive duplicate text evidence: %#v", result)
		}
	}
}

func TestMCPFramingRecoversFromMalformedAndOversizedMessages(t *testing.T) {
	ping := `{"jsonrpc":"2.0","id":1,"method":"ping"}` + "\n"
	oversized := `{"x":"` + strings.Repeat("x", contract.MaxMCPMessageBytes) + `"}` + "\n"
	input := strings.NewReader("{not-json}\n" + oversized + ping)
	var output bytes.Buffer
	if err := runMCP(input, &output); err != nil {
		t.Fatal(err)
	}
	lines := strings.Split(strings.TrimSpace(output.String()), "\n")
	if len(lines) != 3 || !strings.Contains(lines[0], `"code":-32700`) ||
		!strings.Contains(lines[1], `"code":-32600`) ||
		!strings.Contains(lines[2], `"id":1`) {
		t.Fatalf("framing did not recover after bad messages: %q", output.String())
	}
}

func TestMCPAnswersEveryRequestAndNoNotification(t *testing.T) {
	cases := []struct {
		name  string
		input string
		want  []string // substrings, one per expected output line; nil = no output
	}{
		{"initialized notification", `{"jsonrpc":"2.0","method":"notifications/initialized"}`, nil},
		{"cancelled notification", `{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":3}}`, nil},
		{"request without id is not executed", `{"jsonrpc":"2.0","method":"tools/list"}`, nil},
		{"client response", `{"jsonrpc":"2.0","id":7,"result":{}}`, nil},
		{"non-object message", `5`, []string{`"code":-32600`}},
		{"wrong jsonrpc version", `{"jsonrpc":2,"id":3,"method":"ping"}`, []string{`"id":3`, `"code":-32600`}},
		{"null id", `{"jsonrpc":"2.0","id":null,"method":"ping"}`, []string{`"id":null`, `"code":-32600`}},
		{"method not a string", `{"jsonrpc":"2.0","id":4,"method":5}`, []string{`"code":-32600`}},
		{"params not an object", `{"jsonrpc":"2.0","id":5,"method":"tools/list","params":[]}`, []string{`"code":-32602`}},
		{"arguments not an object", `{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"GUI","arguments":[]}}`, []string{`"code":-32602`}},
		{"missing tool name", `{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{}}`, []string{`"code":-32602`}},
		{"empty batch", `[]`, []string{`"code":-32600`}},
		{"batch", `[{"jsonrpc":"2.0","id":"a","method":"ping"},{"jsonrpc":"2.0","method":"notifications/initialized"}]`, []string{`"id":"a"`}},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			var output bytes.Buffer
			if err := runMCP(strings.NewReader(tc.input+"\n"), &output); err != nil {
				t.Fatal(err)
			}
			got := strings.TrimSpace(output.String())
			if tc.want == nil {
				if got != "" {
					t.Fatalf("expected no response, got %q", got)
				}
				return
			}
			for _, want := range tc.want {
				if !strings.Contains(got, want) {
					t.Fatalf("response %q does not contain %q", got, want)
				}
			}
		})
	}
}

func TestMCPRequestDecodePreservesExactJSONNumbers(t *testing.T) {
	request, err := decodeMCPRequest([]byte(
		`{"jsonrpc":"2.0","id":1,"method":"tools/call","params":` +
			`{"name":"AX","arguments":{"mode":"observe","until":` +
			`{"type":"value","ref":"r1",` +
			`"value":9007199254740993}}}}`,
	))
	if err != nil {
		t.Fatal(err)
	}
	params := request["params"].(map[string]any)
	arguments := params["arguments"].(map[string]any)
	assertion := arguments["until"].(map[string]any)
	value, ok := assertion["value"].(json.Number)
	if !ok || value.String() != "9007199254740993" {
		t.Fatalf("MCP transport changed exact JSON number: %#v",
			assertion["value"])
	}
	if _, err := decodeMCPRequest([]byte(`{} {}`)); err == nil {
		t.Fatal("MCP transport accepted multiple JSON values")
	}
}
