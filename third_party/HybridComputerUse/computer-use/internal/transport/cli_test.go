package transport

import (
	"bytes"
	"encoding/json"
	"os"
	"reflect"
	"sort"
	"strings"
	"testing"
	"time"

	contractpkg "github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

// cliGolden is the hand-maintained shorthand table, one case per entry:
// the command line and the exact tool arguments it must produce.
var cliGolden = []struct {
	line string
	tool string
	args string
}{
	{"apps", "AX", `{"mode":"apps"}`},
	{"observe w1", "AX", `{"mode":"observe","window_ref":"w1"}`},
	{"observe soffice --root r5 --scope content --as diff", "AX", `{"mode":"observe","app":"soffice","root":"r5","scope":"content","observe":"diff"}`},
	{`wait {"window":"Save"}`, "AX", `{"mode":"act","actions":[{"type":"wait","window":"Save"}],"observe":"none"}`},
	{`wait w1 {"ref":"r3","gone":true,"timeout_ms":900} --as diff`, "AX", `{"mode":"act","window_ref":"w1","actions":[{"type":"wait","ref":"r3","gone":true,"timeout_ms":900}],"observe":"diff"}`},
	{`wait {"query":{"role":"dialog"}} --as tree`, "AX", `{"mode":"act","actions":[{"type":"wait","query":{"role":"dialog"}}],"observe":"tree"}`},
	{`wait w1 {"text":"Saved"}`, "AX", `{"mode":"act","window_ref":"w1","actions":[{"type":"wait","text":"Saved"}],"observe":"none"}`},
	{"observe w1 --as full", "AX", `{"mode":"observe","window_ref":"w1","observe":"full"}`},
	{"observe w1 --as screenshot", "AX", `{"mode":"observe","window_ref":"w1","observe":"screenshot"}`},
	{"find w1 --role button --name-has Sort --limit 5", "AX", `{"mode":"find","window_ref":"w1","query":{"role":"button","name_contains":"Sort"},"limit":5}`},
	{"find soffice --name OK --text-has total --state checked", "AX", `{"mode":"find","app":"soffice","query":{"name_equals":"OK","text_contains":"total","state":"checked"}}`},
	{"find --cursor f2", "AX", `{"mode":"find","cursor":"f2"}`},
	{"read r1 r2", "AX", `{"mode":"read","refs":["r1","r2"]}`},
	{"click r41", "AX", `{"mode":"act","actions":[{"type":"click","ref":"r41"}]}`},
	{`click r41 --wait {"ref":"r41","state":"checked"}`, "AX", `{"mode":"act","actions":[{"type":"click","ref":"r41"},{"type":"wait","ref":"r41","state":"checked"}]}`},
	{`click r41 --wait {"settle":true}`, "AX", `{"mode":"act","actions":[{"type":"click","ref":"r41"},{"type":"wait","settle":true}]}`},
	{`act --json {"actions":[{"type":"keypress","keys":["CTRL","O"]}]} --wait {"window":"Open"}`, "AX", `{"mode":"act","actions":[{"type":"keypress","keys":["CTRL","O"]},{"type":"wait","window":"Open"}]}`},
	{"type r2 hello", "AX", `{"mode":"act","actions":[{"type":"type","ref":"r2","text":"hello"}]}`},
	{"type hello --window w1", "AX", `{"mode":"act","actions":[{"type":"type","text":"hello"}],"window_ref":"w1"}`},
	{"key ctrl+s", "AX", `{"mode":"act","actions":[{"type":"keypress","keys":["CTRL","S"]}]}`},
	{"key r2 enter --as diff", "AX", `{"mode":"act","actions":[{"type":"keypress","ref":"r2","keys":["ENTER"]}],"observe":"diff"}`},
	{"set r7 42", "AX", `{"mode":"act","actions":[{"type":"set_value","ref":"r7","value":"42"}]}`},
	{"scroll r9 down --pages 2", "AX", `{"mode":"act","actions":[{"type":"scroll","ref":"r9","direction":"down","pages":2}]}`},
	{"activate w2", "AX", `{"mode":"act","actions":[{"type":"activate","ref":"w2"}]}`},
	{"activate Calc", "AX", `{"mode":"act","actions":[{"type":"activate","title":"Calc"}]}`},
	{"close w3", "AX", `{"mode":"act","actions":[{"type":"close","ref":"w3"}]}`},
	{`act --json {"actions":[{"type":"click","ref":"r1"},{"type":"keypress","keys":["ENTER"]}]} --window w1`, "AX", `{"mode":"act","actions":[{"type":"click","ref":"r1"},{"type":"keypress","keys":["ENTER"]}],"window_ref":"w1"}`},
	{"select r4", "AX", `{"mode":"act","actions":[{"type":"select","ref":"r4"}]}`},
	{"select r4 total --as diff", "AX", `{"mode":"act","actions":[{"type":"select","ref":"r4","text":"total"}],"observe":"diff"}`},
	{"select r4 --to r9", "AX", `{"mode":"act","actions":[{"type":"select","ref":"r4","to_ref":"r9"}]}`},
	{"gui screenshot", "GUI", `{"actions":[{"type":"screenshot"}]}`},
	{"gui zoom 600 60 680 100", "GUI", `{"actions":[{"type":"zoom","region":[600,60,680,100]}]}`},
	{"gui click 120 40", "GUI", `{"actions":[{"type":"click","x":120,"y":40}]}`},
	{"gui double_click 120 40", "GUI", `{"actions":[{"type":"double_click","x":120,"y":40}]}`},
	{"gui move 120 40", "GUI", `{"actions":[{"type":"move","x":120,"y":40}]}`},
	{"gui type hello", "GUI", `{"actions":[{"type":"type","text":"hello"}]}`},
	{"gui key ctrl+s", "GUI", `{"actions":[{"type":"keypress","keys":["CTRL","S"]}]}`},
	{`gui --json {"actions":[{"type":"drag","path":[{"x":1,"y":2},{"x":3,"y":4}]}]}`, "GUI", `{"actions":[{"type":"drag","path":[{"x":1,"y":2},{"x":3,"y":4}]}]}`},
}

// splitCLILine splits on spaces, keeping a JSON value (starting with { or [)
// as one argument.
func splitCLILine(line string) []string {
	out := []string{}
	for len(line) > 0 {
		line = strings.TrimLeft(line, " ")
		if line == "" {
			break
		}
		if line[0] == '{' || line[0] == '[' {
			decoder := json.NewDecoder(strings.NewReader(line))
			var value any
			_ = decoder.Decode(&value)
			end := int(decoder.InputOffset())
			out = append(out, line[:end])
			line = line[end:]
			continue
		}
		field, rest, _ := strings.Cut(line, " ")
		out = append(out, field)
		line = rest
	}
	return out
}

// Every shorthand produces exactly its golden arguments, and those pass
// the server's own decoder and published schema (read is records-only and
// is checked by the read handler instead).
func TestCLIShorthandsProduceSchemaValidCalls(t *testing.T) {
	covered := map[string]bool{}
	for _, golden := range cliGolden {
		words := splitCLILine(golden.line)
		command := words[0]
		covered[command] = true
		positional, flags, err := parseCLIArgs(command, words[1:])
		if err != nil {
			t.Fatalf("%s: %v", golden.line, err)
		}
		for _, name := range []string{"session", "socket", "request-id", "since", "records"} {
			delete(flags, name)
		}
		tool, args, err := cliShorthands[command].build(positional, flags)
		if err != nil {
			t.Fatalf("%s: %v", golden.line, err)
		}
		var want map[string]any
		if err := json.Unmarshal([]byte(golden.args), &want); err != nil {
			t.Fatal(err)
		}
		if tool != golden.tool || !reflect.DeepEqual(roundTripJSON(t, args), any(want)) {
			t.Fatalf("%s:\n got %s %s\nwant %s %s", golden.line, tool, contractpkg.CompactJSON(args), golden.tool, golden.args)
		}
		if isRecordsOnly(tool, args) {
			continue
		}
		instance := roundTripJSON(t, args).(map[string]any)
		if tool == contractpkg.PublicToolAX {
			if _, argErr := core.DecodeAXRequest(instance); argErr != nil {
				t.Fatalf("%s: AX decoder rejects %s: %s", golden.line, golden.args, argErr.Message)
			}
		}
		if argErr := contractpkg.ValidateToolArguments(tool, instance); argErr != nil {
			t.Fatalf("%s: published schema rejects %s: %s", golden.line, golden.args, argErr.Message)
		}
	}
	names := []string{}
	for name := range cliShorthands {
		if !covered[name] {
			names = append(names, name)
		}
	}
	sort.Strings(names)
	if len(names) > 0 {
		t.Fatalf("shorthands without a golden case: %v", names)
	}
}

// The removed --until flag reaches the core as the until field, which
// rejects it before any input with the same call written as a wait step.
func TestCLIUntilIsRejectedWithAWaitHint(t *testing.T) {
	for _, line := range []string{
		`click r41 --until {"type":"state","ref":"r41","state":"checked"}`,
		`observe w1 --until {"type":"title","text":"Saved"}`,
	} {
		words := splitCLILine(line)
		positional, flags, err := parseCLIArgs(words[0], words[1:])
		if err != nil {
			t.Fatal(err)
		}
		_, args, err := cliShorthands[words[0]].build(positional, flags)
		if err != nil {
			t.Fatalf("%s: %v", line, err)
		}
		_, argErr := core.DecodeAXRequest(roundTripJSON(t, args).(map[string]any))
		if argErr == nil || argErr.Code != contractpkg.AXCodeInvalidArgument ||
			!strings.Contains(argErr.Hint, `"type":"wait"`) {
			t.Fatalf("%s: want invalid_argument with a wait hint, got %#v", line, argErr)
		}
		if _, hintErr := core.DecodeAXRequest(transportDecodeJSONForTest(t, argErr.Hint)); hintErr != nil {
			t.Fatalf("%s: hint %s does not decode: %s", line, argErr.Hint, hintErr.Message)
		}
	}
}

func TestCLIUsageErrorsExit64(t *testing.T) {
	for _, line := range []string{"click", "click w1", "observe", "find w1 --limit x", "gui click 1", "wait w1",
		"gui zoom 1 2 3", "gui zoom a 0 80 80", "select", "select w1", "select r1 a b"} {
		var stdout, stderr bytes.Buffer
		words := splitCLILine(line)
		if status := runShorthand(words[0], words[1:], &stdout, &stderr); status != exitUsage {
			t.Fatalf("%q: status %d, want %d (%s)", line, status, exitUsage, stderr.String())
		}
	}
}

// Each public code maps to a documented exit status; exit statuses
// separate "no input was sent" from "input may have been sent".
func TestExitCodesFollowInputEmission(t *testing.T) {
	for code, entry := range contractpkg.PublicErrorCodes {
		if exitCodeFor(code) != entry.ExitCode || entry.ExitCode == 0 || entry.ExitCode == exitUsage {
			t.Fatalf("%s: exit %d", code, entry.ExitCode)
		}
	}
	byExit := map[int]map[string]bool{}
	for _, entry := range contractpkg.PublicErrorCodes {
		if byExit[entry.ExitCode] == nil {
			byExit[entry.ExitCode] = map[string]bool{}
		}
		byExit[entry.ExitCode][entry.InputEmitted] = true
	}
	for exit, emitted := range byExit {
		if emitted["no"] && emitted["maybe"] {
			t.Fatalf("exit %d mixes codes that sent no input with codes that may have", exit)
		}
	}
	if exitCodeFor("") != 0 {
		t.Fatal("success must exit 0")
	}
}

// The CLI default output is the MCP text, and errors exit with the mapped
// status; --json prints a record.
func TestCLITextMatchesMCPOverTheDaemon(t *testing.T) {
	t.Setenv("COMPUTER_USE_ERROR_CONTEXT", "off")
	socket := shortSocket(t)
	server := newDaemon(socket, core.NewDesktopState(newFakeDesktopBackend()),
		core.DefaultRuntimeBackend().RuntimeEnvironment(os.Environ()), time.Minute)
	if err := server.listen(); err != nil {
		t.Fatal(err)
	}
	go server.serve()
	defer server.stop()

	local := core.NewServiceWithBackend(newFakeDesktopBackend())
	for _, step := range []struct {
		line   string
		args   map[string]any
		status int
	}{
		{"apps", map[string]any{"mode": "apps"}, 0},
		{"observe w1", map[string]any{"mode": "observe", "window_ref": "w1"}, 0},
		{"observe w9", map[string]any{"mode": "observe", "window_ref": "w9"}, 4},
		{"click r999", map[string]any{"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": "r999"}}}, 4},
	} {
		var stdout, stderr bytes.Buffer
		words := append(splitCLILine(step.line), "--socket", socket)
		status := runShorthand(words[0], words[1:], &stdout, &stderr)
		var want bytes.Buffer
		printTextContent(&want, local.CallForModel(contractpkg.PublicToolAX, step.args, false).Content)
		if status != step.status || stdout.String() != want.String() {
			t.Fatalf("%s: status %d (want %d)\nCLI:\n%s\nMCP:\n%s\nstderr: %s",
				step.line, status, step.status, stdout.String(), want.String(), stderr.String())
		}
	}
	var stdout, stderr bytes.Buffer
	if status := runShorthand("apps", []string{"--json", "--socket", socket}, &stdout, &stderr); status != 0 {
		t.Fatalf("apps --json: %d %s", status, stderr.String())
	}
	var record core.Record
	if err := json.Unmarshal(stdout.Bytes(), &record); err != nil || !record.OK || record.Op != "ax.apps" {
		t.Fatalf("apps --json: %s", stdout.String())
	}
}

// batch prints one record per call and stops at the first error.
func TestCLIBatchStopsAtFirstError(t *testing.T) {
	t.Setenv("COMPUTER_USE_ERROR_CONTEXT", "off")
	socket := shortSocket(t)
	server := newDaemon(socket, core.NewDesktopState(newFakeDesktopBackend()),
		core.DefaultRuntimeBackend().RuntimeEnvironment(os.Environ()), time.Minute)
	if err := server.listen(); err != nil {
		t.Fatal(err)
	}
	go server.serve()
	defer server.stop()
	input := `[{"tool":"AX","args":{"mode":"apps"}},{"tool":"AX","args":{"mode":"observe","window_ref":"w9"}},{"tool":"AX","args":{"mode":"apps"}}]`
	var stdout, stderr bytes.Buffer
	status := runBatch([]string{"--socket", socket}, strings.NewReader(input), &stdout, &stderr)
	lines := strings.Split(strings.TrimSpace(stdout.String()), "\n")
	if status != 4 || len(lines) != 2 {
		t.Fatalf("status=%d lines=%d\n%s%s", status, len(lines), stdout.String(), stderr.String())
	}
	stdout.Reset()
	status = runBatch([]string{"--socket", socket, "--keep-going"}, strings.NewReader(input), &stdout, &stderr)
	if lines := strings.Split(strings.TrimSpace(stdout.String()), "\n"); status != 4 || len(lines) != 3 {
		t.Fatalf("keep-going: status=%d lines=%d", status, len(lines))
	}
}

// schema prints the published tool schemas and the error table.
func TestSchemaCommandPrintsPublishedContract(t *testing.T) {
	t.Setenv("HCU_SOCKET", shortSocket(t))
	var out bytes.Buffer
	if err := runSchema(nil, &out); err != nil {
		t.Fatal(err)
	}
	var contract struct {
		ContractVersion string `json:"contract_version"`
		Tools           []struct {
			Name        string         `json:"name"`
			InputSchema map[string]any `json:"input_schema"`
		} `json:"tools"`
		Errors map[string]contractpkg.PublicError `json:"errors"`
	}
	if err := json.Unmarshal(out.Bytes(), &contract); err != nil {
		t.Fatal(err)
	}
	if contract.ContractVersion != contractpkg.ContractVersion || len(contract.Tools) != 2 || len(contract.Errors) != len(contractpkg.PublicErrorCodes) {
		t.Fatalf("schema: %s", out.String())
	}
	for _, tool := range contract.Tools {
		published := transportFindToolDefinition(t, tool.Name).InputSchema
		if !reflect.DeepEqual(roundTripJSON(t, published), any(tool.InputSchema)) {
			t.Fatalf("%s schema differs from tools/list", tool.Name)
		}
	}
}
