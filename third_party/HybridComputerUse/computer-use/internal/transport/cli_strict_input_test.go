package transport

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

// Malformed local arguments must not reach the daemon, even when a valid
// reference and a live desktop make the requested mutation executable.
func TestCLIStrictUnknownFlagsRejectBeforeDaemon(t *testing.T) {
	t.Setenv("DISPLAY", ":9")
	t.Setenv("WAYLAND_DISPLAY", "")
	server, backend, socket := startInProcessDaemon(t, time.Minute)
	ref := firstRef(t, observeMain(t, testClient(t, socket, "strict-setup")))
	for _, tc := range []struct {
		name, command string
		args          []string
	}{
		{"click_wait_typo", "click", []string{ref, "--wiat", `{"window":"Save"}`}},
		{"click_inline_wait_typo", "click", []string{ref, `--wiat={"window":"Save"}`}},
		{"click_unknown_without_value", "click", []string{ref, "--wiat"}},
		{"apps_wrong_flag", "apps", []string{"--root", "focused"}},
		{"observe_wrong_flag", "observe", []string{"w1", "--pages", "2"}},
		{"find_wrong_flag", "find", []string{"w1", "--wait", `{"window":"Save"}`}},
		{"read_wrong_flag", "read", []string{ref, "--pages", "2"}},
		{"key_wrong_flag", "key", []string{"ENTER", "--pages", "2"}},
		{"gui_wrong_flag", "gui", []string{"screenshot", "--wait", `{"window":"Save"}`}},
	} {
		t.Run(tc.name, func(t *testing.T) {
			before := strictConnectionCount(server)
			var stdout, stderr bytes.Buffer
			args := append([]string{"--socket", socket}, tc.args...)
			status := runShorthand(tc.command, args, &stdout, &stderr)
			if status != exitUsage || backend.inputCount() != 0 || strictConnectionCount(server) != before {
				t.Fatalf("status=%d inputs=%d connections=%d->%d stdout=%q stderr=%q", status,
					backend.inputCount(), before, strictConnectionCount(server), stdout.String(), stderr.String())
			}
		})
	}
}

func strictConnectionCount(server *daemon) int {
	server.mu.Lock()
	defer server.mu.Unlock()
	return server.nextConn
}

func TestCLIStrictJSONRejectsTrailingValuesBeforeDaemon(t *testing.T) {
	t.Setenv("DISPLAY", ":9")
	t.Setenv("WAYLAND_DISPLAY", "")
	server, backend, socket := startInProcessDaemon(t, time.Minute)
	ref := firstRef(t, observeMain(t, testClient(t, socket, "strict-json-setup")))
	for _, suffix := range []string{" trailing garbage", ` {"extra":true}`, " null"} {
		for _, command := range []string{"act", "gui", "wait", "click"} {
			t.Run(command+suffix, func(t *testing.T) {
				var args []string
				switch command {
				case "act":
					args = []string{"--json", fmt.Sprintf(`{"actions":[{"type":"click","ref":%q}]}`, ref) + suffix}
				case "gui":
					args = []string{"--json", `{"actions":[{"type":"screenshot"}]}` + suffix}
				case "wait":
					args = []string{`{"window":"Sheet"}` + suffix}
				case "click":
					args = []string{ref, "--wait", `{"window":"Sheet"}` + suffix}
				}
				before := strictConnectionCount(server)
				var stdout, stderr bytes.Buffer
				status := runShorthand(command, append([]string{"--socket", socket}, args...), &stdout, &stderr)
				if status != exitUsage || backend.inputCount() != 0 || strictConnectionCount(server) != before {
					t.Fatalf("status=%d inputs=%d connections=%d->%d stdout=%q stderr=%q", status,
						backend.inputCount(), before, strictConnectionCount(server), stdout.String(), stderr.String())
				}
			})
		}
	}
}

type strictUnreadInput struct{ reads int }

func (r *strictUnreadInput) Read([]byte) (int, error) {
	r.reads++
	return 0, io.EOF
}

func TestCLIStrictManagementFlagsRejectBeforeDaemon(t *testing.T) {
	server, _, socket := startInProcessDaemon(t, time.Minute)
	for _, command := range []string{"batch", "schema", "daemon"} {
		for _, flag := range [][]string{{"--typo", "value"}, {"--typo=value"}, {"--typo"}} {
			t.Run(command+strings.Join(flag, "_"), func(t *testing.T) {
				before := strictConnectionCount(server)
				args := append([]string{"--socket", socket}, flag...)
				var stdout, stderr bytes.Buffer
				var err error
				switch command {
				case "batch":
					input := &strictUnreadInput{}
					status := runBatch(args, input, &stdout, &stderr)
					if status != exitUsage || input.reads != 0 {
						t.Fatalf("status=%d stdin reads=%d", status, input.reads)
					}
				case "schema":
					err = runSchema(args, &stdout)
				case "daemon":
					err = runDaemonCommand(append([]string{"status"}, args...), &stdout)
				}
				if command != "batch" && (err == nil || ExitStatusOf(err) != exitUsage) {
					t.Fatalf("expected usage error, got %v", err)
				}
				if strictConnectionCount(server) != before {
					t.Fatalf("invalid command connected to daemon: %d -> %d", before, strictConnectionCount(server))
				}
			})
		}
	}
}

// Valid whitespace and shared transport/output flags still run one mutation,
// and retrying the same call returns its original result without another input.
func TestCLIStrictValidJSONWhitespaceAndSharedFlags(t *testing.T) {
	t.Setenv("DISPLAY", ":9")
	t.Setenv("WAYLAND_DISPLAY", "")
	_, backend, socket := startInProcessDaemon(t, time.Minute)
	ref := firstRef(t, observeMain(t, testClient(t, socket, "strict-valid-setup")))
	args := []string{"--socket", socket, "--session", "strict-valid", "--request-id", "strict-valid-id", "--records",
		"--json", fmt.Sprintf(`{"actions":[{"type":"click","ref":%q}],"observe":"none"}`, ref) + " \n\t\r"}
	for attempt := 0; attempt < 2; attempt++ {
		var stdout, stderr bytes.Buffer
		if status := runShorthand("act", args, &stdout, &stderr); status != 0 {
			t.Fatalf("attempt %d status=%d stderr=%q stdout=%q", attempt, status, stderr.String(), stdout.String())
		}
		var record core.Record
		if err := json.Unmarshal(stdout.Bytes(), &record); err != nil {
			t.Fatal(err)
		}
		if !record.OK || record.Replayed != (attempt == 1) || backend.inputCount() != 1 {
			t.Fatalf("attempt=%d record=%+v inputs=%d", attempt, record, backend.inputCount())
		}
	}
	for _, tc := range []struct {
		command string
		args    []string
	}{
		{"wait", []string{`{"window":"Sheet"}` + " \n\t"}},
		{"click", []string{ref, "--wait", `{"window":"Sheet"}` + " \n\t", "--as", "none"}},
	} {
		var stdout, stderr bytes.Buffer
		if status := runShorthand(tc.command, append([]string{"--socket", socket}, tc.args...), &stdout, &stderr); status != 0 {
			t.Fatalf("%s status=%d stderr=%q stdout=%q", tc.command, status, stderr.String(), stdout.String())
		}
	}
	// GUI parsing uses the same JSON path, without requiring a pixel provider.
	p, f, err := parseCLIArgs("gui", []string{"--json", `{"actions":[{"type":"screenshot"}]}` + " \n\t"})
	if err != nil {
		t.Fatal(err)
	}
	if _, _, err := cliShorthands["gui"].build(p, f); err != nil {
		t.Fatal(err)
	}
}

func TestCLIStrictTextFlagDependsOnCommand(t *testing.T) {
	p, f, err := parseCLIArgs("find", []string{"w1", "--text", "exact text"})
	if err != nil {
		t.Fatal(err)
	}
	tool, args, err := cliShorthands["find"].build(p, f)
	if err != nil {
		t.Fatal(err)
	}
	query, _ := args["query"].(map[string]any)
	if tool != contract.PublicToolAX || args["window_ref"] != "w1" || query["text_equals"] != "exact text" {
		t.Fatalf("find lost its text filter: tool=%s args=%v", tool, args)
	}
	if _, _, err := parseCLIArgs("find", []string{"w1", "--text"}); err == nil {
		t.Fatal("find --text without its value must fail")
	}
	p, f, err = parseCLIArgs("batch", []string{"--text", "calls.json"})
	if err != nil || len(p) != 1 || p[0] != "calls.json" || !f.has("text") {
		t.Fatalf("batch --text consumed its filename: positional=%v flags=%v err=%v", p, f, err)
	}
}

func TestCLIStrictCallJSONSources(t *testing.T) {
	const exactNumber = "9007199254740993"
	for _, sequence := range []bool{false, true} {
		for _, file := range []bool{false, true} {
			for _, suffix := range []string{" trailing garbage", " {}", " null", " \n\t\r"} {
				t.Run(fmt.Sprintf("sequence=%v/file=%v/suffix=%q", sequence, file, suffix), func(t *testing.T) {
					body := `{"number":` + exactNumber + `}`
					if sequence {
						body = `[{"tool":"AX","args":` + body + `}]`
					}
					inline, path := body+suffix, ""
					if file {
						path = filepath.Join(t.TempDir(), "input.json")
						if err := os.WriteFile(path, []byte(inline), 0o600); err != nil {
							t.Fatal(err)
						}
						inline = ""
					}
					var args map[string]any
					var err error
					if sequence {
						var calls []callSpec
						calls, err = readCallSequence(inline, path)
						if err == nil {
							if len(calls) != 1 || calls[0].Tool != contract.PublicToolAX {
								t.Fatalf("unexpected calls: %v", calls)
							}
							args = calls[0].Args
						}
					} else {
						args, err = readArguments(inline, path)
					}
					if strings.TrimSpace(suffix) != "" {
						if err == nil {
							t.Fatalf("accepted trailing content: %q", suffix)
						}
						return
					}
					if err != nil {
						t.Fatalf("rejected whitespace: %v", err)
					}
					number, ok := args["number"].(json.Number)
					if !ok || number.String() != exactNumber {
						t.Fatalf("JSON number lost precision: %#v", args["number"])
					}
				})
			}
		}
	}
}

func TestCLIStrictBatchJSONSourcesRejectBeforeDaemon(t *testing.T) {
	t.Setenv("DISPLAY", ":9")
	t.Setenv("WAYLAND_DISPLAY", "")
	server, backend, socket := startInProcessDaemon(t, time.Minute)
	ref := firstRef(t, observeMain(t, testClient(t, socket, "strict-batch-setup")))
	for _, file := range []bool{false, true} {
		for _, suffix := range []string{" trailing garbage", " []", " null", " \n\t\r"} {
			t.Run(fmt.Sprintf("file=%v/suffix=%q", file, suffix), func(t *testing.T) {
				body := fmt.Sprintf(`[{"tool":"AX","args":{"mode":"act","observe":"none","actions":[{"type":"click","ref":%q}]}}]`, ref) + suffix
				args := []string{"--socket", socket}
				if file {
					path := filepath.Join(t.TempDir(), "batch.json")
					if err := os.WriteFile(path, []byte(body), 0o600); err != nil {
						t.Fatal(err)
					}
					args = append(args, path)
				} else {
					args = append(args, "-")
				}
				before, inputs := strictConnectionCount(server), backend.inputCount()
				var stdout, stderr bytes.Buffer
				status := runBatch(args, strings.NewReader(body), &stdout, &stderr)
				if strings.TrimSpace(suffix) != "" {
					if status != exitUsage || backend.inputCount() != inputs || strictConnectionCount(server) != before {
						t.Fatalf("status=%d inputs=%d->%d connections=%d->%d stderr=%q", status, inputs,
							backend.inputCount(), before, strictConnectionCount(server), stderr.String())
					}
				} else if status != 0 || backend.inputCount() != inputs+1 {
					t.Fatalf("valid batch failed: status=%d inputs=%d->%d stdout=%q stderr=%q", status,
						inputs, backend.inputCount(), stdout.String(), stderr.String())
				}
			})
		}
	}
}

// act and gui read a --json body from standard input (-) or a file (@PATH)
// exactly as they read it inline, so code that builds a command needs no
// shell quoting. An unreadable source is a usage error before the daemon.
func TestCLIJSONBodyFromStdinOrFile(t *testing.T) {
	t.Setenv("DISPLAY", ":9")
	t.Setenv("WAYLAND_DISPLAY", "")
	server, backend, socket := startInProcessDaemon(t, time.Minute)
	ref := firstRef(t, observeMain(t, testClient(t, socket, "json-source-setup")))
	body := fmt.Sprintf(`{"actions":[{"type":"click","ref":%q}],"observe":"none"}`, ref)
	file := filepath.Join(t.TempDir(), "act.json")
	if err := os.WriteFile(file, []byte(body+"\n"), 0o600); err != nil {
		t.Fatal(err)
	}
	defer func(previous io.Reader) { cliStdin = previous }(cliStdin)
	for index, source := range []string{"-", "@" + file} {
		cliStdin = strings.NewReader(body)
		var stdout, stderr bytes.Buffer
		args := []string{"--socket", socket, "--records", "--json", source}
		if status := runShorthand("act", args, &stdout, &stderr); status != 0 {
			t.Fatalf("%s: status=%d stderr=%q", source, status, stderr.String())
		}
		var record core.Record
		if err := json.Unmarshal(stdout.Bytes(), &record); err != nil || !record.OK || backend.inputCount() != index+1 {
			t.Fatalf("%s: record=%+v err=%v inputs=%d", source, record, err, backend.inputCount())
		}
	}
	for _, tc := range []struct {
		command, source, stdin string
	}{
		{"act", "@" + filepath.Join(t.TempDir(), "missing.json"), ""},
		{"gui", "@" + filepath.Join(t.TempDir(), "missing.json"), ""},
		{"act", "-", body + " trailing"},
		{"gui", "-", ""},
	} {
		cliStdin = strings.NewReader(tc.stdin)
		before := strictConnectionCount(server)
		var stdout, stderr bytes.Buffer
		status := runShorthand(tc.command, []string{"--socket", socket, "--json", tc.source}, &stdout, &stderr)
		if status != exitUsage || strictConnectionCount(server) != before {
			t.Fatalf("%s %s: status=%d connections=%d->%d stderr=%q", tc.command, tc.source, status,
				before, strictConnectionCount(server), stderr.String())
		}
	}
	cliStdin = strings.NewReader(`{"actions":[{"type":"screenshot"}]}`)
	p, f, err := parseCLIArgs("gui", []string{"--json", "-"})
	if err != nil {
		t.Fatal(err)
	}
	if f["json"], err = jsonFlagSource(f["json"]); err != nil {
		t.Fatal(err)
	}
	if _, args, err := cliShorthands["gui"].build(p, f); err != nil || len(args["actions"].([]any)) != 1 {
		t.Fatalf("gui --json - built %v, %v", args, err)
	}
}
