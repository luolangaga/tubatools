package transport

import (
	"bytes"
	"encoding/json"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

// TestMain lets the test binary act as a daemon process for the auto-start
// tests: with HCU_TEST_DAEMON set it serves a fake desktop and exits.
func TestMain(m *testing.M) {
	if socket := os.Getenv("HCU_TEST_DAEMON"); socket != "" {
		os.Exit(runTestDaemon(socket))
	}
	os.Exit(m.Run())
}

// fakeDesktopBackend is a two-window application for daemon tests. It
// counts input calls and can hold one input call open, to show that a
// second client waits for it.
type fakeDesktopBackend struct {
	transportAppWideBackend
	mu         sync.Mutex
	inputs     int
	hold       chan struct{}
	holding    chan struct{}
	inFlight   int
	maxInFlght int
}

func newFakeDesktopBackend() *fakeDesktopBackend {
	return &fakeDesktopBackend{transportAppWideBackend: transportAppWideBackend{open: []transportUntilFakeWindow{transportUntilMainWindow()}}}
}

func (b *fakeDesktopBackend) ObserveApps(core.ProviderObserveAppsRequest) (core.ProviderAccessibilityResult, error) {
	return core.ProviderAccessibilityResult{OK: true, Apps: []core.AppDescriptor{{
		Name: "LibreOffice", BundleIdentifier: "libreoffice", PID: 10, InputCapable: true,
		ProviderBusName: ":1.84", ProcessGeneration: "pid:10:start:100",
		Windows: []core.ProviderAppWindow{{
			Title: "Sheet", Role: "frame", Handle: transportUntilMainHandle, ProviderBusName: ":1.84",
			ProviderObjectPath: "/window/1", ProcessGeneration: "pid:10:start:100", Active: true, InputTarget: true,
		}},
	}}}, nil
}

func (b *fakeDesktopBackend) ResolveAndAct(request core.ProviderActionRequest) (core.ProviderAccessibilityResult, error) {
	if request.Intent == core.AccessibilityResolveTarget {
		return core.ProviderAccessibilityResult{OK: true, ResolvedElement: &core.ElementRecord{
			ControlType: "push button", Name: "Save", States: "[has-click-action]",
		}}, nil
	}
	b.mu.Lock()
	b.inputs++
	b.inFlight++
	if b.inFlight > b.maxInFlght {
		b.maxInFlght = b.inFlight
	}
	hold, holding := b.hold, b.holding
	b.hold = nil
	b.mu.Unlock()
	if hold != nil {
		close(holding)
		<-hold
	}
	b.mu.Lock()
	b.inFlight--
	b.mu.Unlock()
	snapshot := b.snapshotFor(transportUntilMainWindow())
	return core.ProviderAccessibilityResult{
		OK: true, Snapshot: snapshot,
		Mutation:       &core.ProviderMutationFacts{InputEventsEmitted: true},
		ActionEvidence: &core.ProviderActionEvidence{InputEmitted: true},
	}, nil
}

func (b *fakeDesktopBackend) inputCount() int {
	b.mu.Lock()
	defer b.mu.Unlock()
	return b.inputs
}

func runTestDaemon(socket string) int {
	idle := 30 * time.Second
	if raw := os.Getenv("HCU_TEST_IDLE"); raw != "" {
		idle, _ = time.ParseDuration(raw)
	}
	// The fake desktop answers for this process's real session environment,
	// so clients connect through the same connectDaemon path as the CLI.
	server := newDaemon(socket, core.NewDesktopState(newFakeDesktopBackend()),
		core.DefaultRuntimeBackend().RuntimeEnvironment(os.Environ()), idle)
	if err := server.listen(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	if err := server.serve(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	return 0
}

// startInProcessDaemon serves a fake desktop from this test process.
func startInProcessDaemon(t *testing.T, idle time.Duration) (*daemon, *fakeDesktopBackend, string) {
	t.Helper()
	t.Setenv("COMPUTER_USE_ERROR_CONTEXT", "off")
	socket := shortSocket(t)
	backend := newFakeDesktopBackend()
	server := newDaemon(socket, core.NewDesktopState(backend), []string{"DISPLAY=:9"}, idle)
	if err := server.listen(); err != nil {
		t.Fatal(err)
	}
	done := make(chan struct{})
	go func() {
		_ = server.serve()
		close(done)
	}()
	t.Cleanup(func() {
		server.stop()
		<-done
	})
	return server, backend, socket
}

func testClient(t *testing.T, socket, session string) *daemonClient {
	t.Helper()
	client, err := dialDaemon(socket)
	if err != nil {
		t.Fatal(err)
	}
	params := map[string]any{"display": ":9"}
	if session != "" {
		params["session"] = session
	}
	var hello map[string]any
	if err := client.request("hello", params, &hello); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(client.close)
	return client
}

func callRecordOver(t *testing.T, client *daemonClient, tool string, args map[string]any, extra map[string]any) core.Record {
	t.Helper()
	params := map[string]any{"tool": tool, "args": args}
	for key, value := range extra {
		params[key] = value
	}
	var record core.Record
	if err := client.request("call", params, &record); err != nil {
		t.Fatal(err)
	}
	return record
}

// observeMain lists apps (issuing w1) and observes the main window.
func observeMain(t *testing.T, client *daemonClient) core.Record {
	t.Helper()
	callRecordOver(t, client, contract.PublicToolAX, map[string]any{"mode": "apps"}, nil)
	return callRecordOver(t, client, contract.PublicToolAX, map[string]any{"mode": "observe", "window_ref": "w1"}, nil)
}

func firstRef(t *testing.T, record core.Record) string {
	t.Helper()
	for _, element := range record.Elements {
		if element.Ref != "" && element.Role == "button" {
			return element.Ref
		}
	}
	t.Fatalf("no button ref in %+v", record)
	return ""
}

// A ref issued to one client acts for another client on another session.
func TestDaemonClientsShareRefs(t *testing.T) {
	_, backend, socket := startInProcessDaemon(t, time.Minute)
	first := testClient(t, socket, "")
	second := testClient(t, socket, "")
	apps := callRecordOver(t, first, contract.PublicToolAX, map[string]any{"mode": "apps"}, nil)
	if !apps.OK || len(apps.Windows) != 1 || apps.Windows[0].Ref != "w1" {
		t.Fatalf("apps: %+v", apps)
	}
	observed := callRecordOver(t, first, contract.PublicToolAX, map[string]any{"mode": "observe", "window_ref": "w1"}, nil)
	ref := firstRef(t, observed)
	acted := callRecordOver(t, second, contract.PublicToolAX, map[string]any{
		"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": ref}},
	}, nil)
	if !acted.OK || !acted.InputEmitted || backend.inputCount() != 1 {
		t.Fatalf("second client could not use the first client's ref: %+v", acted)
	}
	read := callRecordOver(t, second, contract.PublicToolAX, map[string]any{"mode": "read", "refs": []any{ref}}, nil)
	if !read.OK || len(read.Read) != 1 || !read.Read[0].OK {
		t.Fatalf("read over the socket: %+v", read)
	}
}

// Two clients acting at once never run input concurrently: the second
// waits until the first call returns.
func TestDaemonInputLockSerializesClients(t *testing.T) {
	_, backend, socket := startInProcessDaemon(t, time.Minute)
	first := testClient(t, socket, "")
	second := testClient(t, socket, "")
	ref := firstRef(t, observeMain(t, first))
	release := make(chan struct{})
	holding := make(chan struct{})
	backend.mu.Lock()
	backend.hold, backend.holding = release, holding
	backend.mu.Unlock()
	click := map[string]any{"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": ref}}}
	var wg sync.WaitGroup
	wg.Add(1)
	go func() {
		defer wg.Done()
		callRecordOver(t, first, contract.PublicToolAX, click, nil)
	}()
	<-holding
	secondDone := make(chan core.Record, 1)
	go func() { secondDone <- callRecordOver(t, second, contract.PublicToolAX, click, nil) }()
	select {
	case <-secondDone:
		t.Fatal("second client's input ran while the first held the input lock")
	case <-time.After(150 * time.Millisecond):
	}
	close(release)
	wg.Wait()
	if record := <-secondDone; !record.OK {
		t.Fatalf("second click after the lock: %+v", record)
	}
	backend.mu.Lock()
	defer backend.mu.Unlock()
	if backend.maxInFlght != 1 || backend.inputs != 2 {
		t.Fatalf("max concurrent inputs=%d inputs=%d", backend.maxInFlght, backend.inputs)
	}
}

// A retried act with the same request_id, even on a new connection, gets
// the stored result and sends no second input.
func TestDaemonRequestIDSurvivesReconnect(t *testing.T) {
	_, backend, socket := startInProcessDaemon(t, time.Minute)
	first := testClient(t, socket, "")
	ref := firstRef(t, observeMain(t, first))
	click := map[string]any{"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": ref}}}
	original := callRecordOver(t, first, contract.PublicToolAX, click, map[string]any{"request_id": "req-1"})
	first.close()
	retry := callRecordOver(t, testClient(t, socket, ""), contract.PublicToolAX, click, map[string]any{"request_id": "req-1"})
	if !original.OK || original.Replayed || !retry.Replayed || retry.RequestID != "req-1" || backend.inputCount() != 1 {
		t.Fatalf("original=%+v retry=%+v inputs=%d", original, retry, backend.inputCount())
	}
}

// Sessions keep separate find cursors and diff baselines; a named session
// is shared by connections that name it.
func TestDaemonSessionsAreSeparate(t *testing.T) {
	server, _, socket := startInProcessDaemon(t, time.Minute)
	testClient(t, socket, "alpha")
	testClient(t, socket, "alpha")
	testClient(t, socket, "beta")
	server.mu.Lock()
	defer server.mu.Unlock()
	if len(server.sessions) != 2 || server.sessions["alpha"] == server.sessions["beta"] {
		t.Fatalf("sessions: %v", server.sessions)
	}
	if server.sessions["alpha"].DesktopState != server.sessions["beta"].DesktopState {
		t.Fatal("sessions must share the desktop")
	}
}

// A client on another desktop is refused; the daemon also refuses to run a
// call once its own desktop environment changes.
func TestDaemonRefusesAnotherDesktop(t *testing.T) {
	server, backend, socket := startInProcessDaemon(t, time.Minute)
	client, err := dialDaemon(socket)
	if err != nil {
		t.Fatal(err)
	}
	defer client.close()
	err = client.request("hello", map[string]any{"display": ":1"}, &map[string]any{})
	if err == nil || !strings.Contains(err.Error(), "use another socket") {
		t.Fatalf("hello from another desktop: %v", err)
	}
	server.currentEnvironment = func() string { return "DISPLAY=:1;WAYLAND_DISPLAY=;XDG_RUNTIME_DIR=" }
	record := callRecordOver(t, testClient(t, socket, ""), contract.PublicToolAX, map[string]any{"mode": "apps"}, nil)
	if record.OK || record.Code != contract.AXCodeRuntimeError || !strings.Contains(record.Message, "environment changed") ||
		backend.inputCount() != 0 {
		t.Fatalf("changed environment: %+v", record)
	}
}

// With no client connected for the idle timeout, the daemon exits and
// removes its socket.
func TestDaemonExitsWhenIdle(t *testing.T) {
	server, _, socket := startInProcessDaemon(t, 80*time.Millisecond)
	client := testClient(t, socket, "")
	time.Sleep(200 * time.Millisecond)
	select {
	case <-server.stopped:
		t.Fatal("daemon exited while a client was connected")
	default:
	}
	client.close()
	select {
	case <-server.stopped:
	case <-time.After(3 * time.Second):
		t.Fatal("idle daemon did not exit")
	}
	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) {
		if _, err := os.Stat(socket); os.IsNotExist(err) {
			return
		}
		time.Sleep(10 * time.Millisecond)
	}
	t.Fatal("idle daemon left its socket behind")
}

// The socket is private to the user, and a stale socket from a crashed
// daemon is replaced while a live one is not.
func TestDaemonSocketPermissionsAndStaleSocket(t *testing.T) {
	_, _, socket := startInProcessDaemon(t, time.Minute)
	checkDaemonSocketPermissions(t, socket)
	if err := prepareSocketPath(socket); err == nil || !strings.Contains(err.Error(), "already serving") {
		t.Fatalf("a live daemon's socket must not be replaced: %v", err)
	}

	stale := shortSocket(t)
	crashed := newDaemon(stale, core.NewDesktopState(newFakeDesktopBackend()), nil, time.Minute)
	if err := crashed.listen(); err != nil {
		t.Fatal(err)
	}
	// Close the listener without removing the file, as a crash would.
	crashed.listener.(interface{ SetUnlinkOnClose(bool) }).SetUnlinkOnClose(false)
	crashed.listener.Close()
	if _, err := os.Stat(stale); err != nil {
		t.Fatalf("stale socket file should remain: %v", err)
	}
	restarted := newDaemon(stale, core.NewDesktopState(newFakeDesktopBackend()), nil, time.Minute)
	if err := restarted.listen(); err != nil {
		t.Fatalf("a stale socket must be replaced: %v", err)
	}
	restarted.listener.Close()

	notSocket := filepath.Join(filepath.Dir(stale), "plain")
	if err := os.WriteFile(notSocket, nil, 0o600); err != nil {
		t.Fatal(err)
	}
	if err := prepareSocketPath(notSocket); err == nil {
		t.Fatal("a regular file at the socket path must not be removed")
	}
}

// A restarted daemon continues ref numbering, so a ref held by a client of
// the old daemon fails as stale instead of naming another element.
func TestDaemonRestartNeverReusesRefNumbers(t *testing.T) {
	socket := shortSocket(t)
	first := newDaemon(socket, core.NewDesktopState(newFakeDesktopBackend()), nil, time.Minute)
	first.desktop.RefRegistry.Next = 42
	first.desktop.WindowRefRegistry.Next = 7
	first.saveRefCounters()
	second := newDaemon(socket, core.NewDesktopState(newFakeDesktopBackend()), nil, time.Minute)
	if second.desktop.RefRegistry.Next != 42 || second.desktop.WindowRefRegistry.Next != 7 {
		t.Fatalf("counters not restored: r=%d w=%d", second.desktop.RefRegistry.Next, second.desktop.WindowRefRegistry.Next)
	}
	if _, err := second.desktop.RefRegistry.Resolve("r12", ""); err == nil ||
		!strings.Contains(err.Error(), "expired") {
		t.Fatalf("an old ref must fail as expired after restart: %v", err)
	}
}

// The first CLI call starts the daemon; concurrent first calls start one;
// a crashed daemon is restarted on the next call.
func TestCLIAutoStartsOneDaemonAndRecoversFromCrash(t *testing.T) {
	socket := shortSocket(t)
	executable, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	restore := daemonServeCommand
	defer func() { daemonServeCommand = restore }()
	daemonServeCommand = func(socket string) (*exec.Cmd, error) {
		command := exec.Command(executable, "-test.run=^$")
		command.Env = append(os.Environ(), "HCU_TEST_DAEMON="+socket, "COMPUTER_USE_ERROR_CONTEXT=off")
		return command, nil
	}
	var wg sync.WaitGroup
	pids := make([]string, 4)
	for index := range pids {
		wg.Add(1)
		go func(index int) {
			defer wg.Done()
			client, err := connectDaemon(socket, "", true)
			if err != nil {
				t.Error(err)
				return
			}
			pids[index] = fmt.Sprint(client.hello["pid"])
			client.close()
		}(index)
	}
	wg.Wait()
	for _, pid := range pids[1:] {
		if pid != pids[0] {
			t.Fatalf("concurrent first calls started several daemons: %v", pids)
		}
	}
	var stdout, stderr bytes.Buffer
	status := runDaemonCall(cliOutput{records: true, socket: socket, session: "default"},
		contract.PublicToolAX, map[string]any{"mode": "apps"}, core.CallOptions{}, &stdout, &stderr)
	var record core.Record
	if status != 0 || json.Unmarshal(stdout.Bytes(), &record) != nil || !record.OK {
		t.Fatalf("status=%d stdout=%s stderr=%s", status, stdout.String(), stderr.String())
	}

	// Crash: kill the daemon without cleanup; the socket file stays.
	pid := 0
	fmt.Sscan(pids[0], &pid)
	process, _ := os.FindProcess(pid)
	if err := process.Kill(); err != nil {
		t.Fatal(err)
	}
	waitUntil(t, func() bool { return !daemonLive(socket) })
	client, err := connectDaemon(socket, "", true)
	if err != nil {
		t.Fatalf("restart after crash: %v", err)
	}
	defer client.close()
	if fmt.Sprint(client.hello["pid"]) == pids[0] {
		t.Fatal("expected a new daemon after the crash")
	}
	var stop map[string]any
	_ = client.request("stop", map[string]any{}, &stop)
	waitUntil(t, func() bool { return !daemonLive(socket) })
}

func waitUntil(t *testing.T, done func() bool) {
	t.Helper()
	deadline := time.Now().Add(5 * time.Second)
	for time.Now().Before(deadline) {
		if done() {
			return
		}
		time.Sleep(20 * time.Millisecond)
	}
	t.Fatal("condition not reached within 5s")
}

// MCP attached to the daemon returns the same bytes as an in-process MCP
// server, and its refs are usable from another client.
func TestAttachedMCPMatchesInProcessAndSharesRefs(t *testing.T) {
	_, backend, socket := startInProcessDaemon(t, time.Minute)
	attached := testClient(t, socket, "mcp")
	local := core.NewServiceWithBackend(newFakeDesktopBackend())
	requests := []map[string]any{
		{"jsonrpc": "2.0", "id": json.Number("1"), "method": "initialize", "params": map[string]any{"protocolVersion": core.MCPProtocolLegacy}},
		{"jsonrpc": "2.0", "id": json.Number("2"), "method": "tools/list", "params": map[string]any{}},
		{"jsonrpc": "2.0", "id": json.Number("3"), "method": "tools/call", "params": map[string]any{
			"name": "AX", "arguments": map[string]any{"mode": "apps"}}},
		{"jsonrpc": "2.0", "id": json.Number("4"), "method": "tools/call", "params": map[string]any{
			"name": "AX", "arguments": map[string]any{"mode": "observe", "window_ref": "w1"}}},
	}
	for _, request := range requests {
		want, _ := json.Marshal(handleMCPRequest(request, local))
		var response struct {
			Message json.RawMessage `json:"message"`
		}
		if err := attached.request("mcp", map[string]any{"message": request}, &response); err != nil {
			t.Fatal(err)
		}
		if !bytes.Equal(compactJSONBytes(t, want), compactJSONBytes(t, response.Message)) {
			t.Fatalf("attached MCP differs for %v:\nlocal:    %s\nattached: %s", request["method"], want, response.Message)
		}
	}
	cli := testClient(t, socket, "")
	observed := callRecordOver(t, cli, contract.PublicToolAX, map[string]any{"mode": "observe", "window_ref": "w1"}, nil)
	ref := firstRef(t, observed)
	var response struct {
		Message map[string]any `json:"message"`
	}
	click := map[string]any{"jsonrpc": "2.0", "id": json.Number("5"), "method": "tools/call", "params": map[string]any{
		"name": "AX", "arguments": map[string]any{"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": ref}}}}}
	if err := attached.request("mcp", map[string]any{"message": click}, &response); err != nil {
		t.Fatal(err)
	}
	if backend.inputCount() != 1 || strings.Contains(contract.CompactJSON(response.Message), `"ok":false`) {
		t.Fatalf("MCP could not act on a CLI ref: %v", response.Message)
	}
}

func compactJSONBytes(t *testing.T, data []byte) []byte {
	t.Helper()
	var value any
	if err := json.Unmarshal(data, &value); err != nil {
		t.Fatal(err)
	}
	out, _ := json.Marshal(value)
	return out
}

// An in-process MCP server refuses input while a daemon serves the
// desktop, allows reads, and is unchanged when no daemon runs.
func TestInProcessMCPRefusesInputWhileADaemonRuns(t *testing.T) {
	guard := &daemonInputGuard{socket: "/nonexistent", live: func(string) bool { return true }}
	act := map[string]any{"jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": map[string]any{
		"name": "AX", "arguments": map[string]any{"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": "r1"}}}}}
	refusal := guard.refuse(act)
	if refusal == nil || !strings.Contains(contract.CompactJSON(refusal), "--attach") ||
		!strings.Contains(contract.CompactJSON(refusal), `\"input_emitted\":false`) {
		t.Fatalf("act must be refused: %v", refusal)
	}
	for _, read := range []map[string]any{
		{"name": "AX", "arguments": map[string]any{"mode": "observe", "window_ref": "w1"}},
		{"name": "GUI", "arguments": map[string]any{"actions": []any{map[string]any{"type": "screenshot"}}}},
		{"name": "GUI", "arguments": map[string]any{"actions": []any{map[string]any{"type": "zoom", "region": []any{0, 0, 80, 80}}}}},
		// An act of only wait steps sends no input.
		{"name": "AX", "arguments": map[string]any{"mode": "act", "actions": []any{
			map[string]any{"type": "wait", "window": "Saved"}}}},
	} {
		if guard.refuse(map[string]any{"method": "tools/call", "params": read}) != nil {
			t.Fatalf("reads stay allowed: %v", read)
		}
	}
	clickThenWait := map[string]any{"method": "tools/call", "params": map[string]any{
		"name": "AX", "arguments": map[string]any{"mode": "act", "actions": []any{
			map[string]any{"type": "click", "ref": "r1"}, map[string]any{"type": "wait", "window": "Saved"}}}}}
	if guard.refuse(clickThenWait) == nil {
		t.Fatal("an act with an action and a wait sends input and must be refused")
	}
	guard.live = func(string) bool { return false }
	if guard.refuse(act) != nil {
		t.Fatal("without a daemon nothing changes")
	}
}
