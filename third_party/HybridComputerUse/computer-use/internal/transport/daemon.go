package transport

import (
	"bufio"
	"bytes"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

// The daemon serves one desktop to many local clients over a Unix socket.
// It is core plus a socket transport: every method maps to a public tool
// call through the same callTool, records and text rendering as MCP.
//
// Wire format: one JSON-RPC 2.0 message per line, in both directions.
//
//	hello  {client_version?, session?}          -> {version, contract_version, session, desktop, instance_id}
//	call   {tool, args, format?, request_id?, since?} -> Record (format records)
//	                                            or {content, isError} (format text)
//	schema {}                                   -> describeContract()
//	mcp    {message}                            -> MCP response for an attached MCP server
//	stop   {}                                   -> {} then the daemon exits
//
// Sessions: each connection gets its own session state unless hello names
// a session; connections that name the same session share it. Desktop
// state (refs, provider, request_id results) is shared by all.

const daemonProtocolVersion = 1

const defaultDaemonIdleTimeout = 30 * time.Minute

// daemon is one running server.
type daemon struct {
	socket      string
	instanceID  string
	desktop     *core.DesktopState
	environment string
	// display is the DISPLAY (or WAYLAND_DISPLAY) the daemon serves.
	display     string
	idleTimeout time.Duration
	// currentEnvironment re-reads the desktop environment before each call;
	// nil skips the check (tests).
	currentEnvironment func() string

	mu       sync.Mutex
	sessions map[string]*core.Service
	active   int
	lastUsed time.Time
	listener net.Listener
	stopped  chan struct{}
	stopOnce sync.Once
	nextConn int
	// savedCounters is the last value written by saveRefCounters.
	savedCounters     refCounters
	refPersistenceErr error
}

// desktopFingerprint is the session environment the daemon serves. A
// client whose environment differs is talking about another desktop.
func desktopFingerprint(environment []string) string {
	values := map[string]string{}
	for _, item := range environment {
		key, value, _ := strings.Cut(item, "=")
		values[key] = value
	}
	parts := []string{}
	for _, key := range []string{"DISPLAY", "WAYLAND_DISPLAY", "XDG_RUNTIME_DIR"} {
		parts = append(parts, key+"="+values[key])
	}
	return strings.Join(parts, ";")
}

// environmentDisplay is WAYLAND_DISPLAY, or DISPLAY when that is empty.
func environmentDisplay(environment []string) string {
	values := map[string]string{}
	for _, item := range environment {
		key, value, _ := strings.Cut(item, "=")
		values[key] = value
	}
	return core.FirstNonEmpty(values["WAYLAND_DISPLAY"], values["DISPLAY"])
}

func newDaemon(socket string, desktop *core.DesktopState, environment []string, idle time.Duration) *daemon {
	// Replay results live only in this instance. Never substitute a PID or
	// timestamp if random identity generation fails.
	var identity [16]byte
	if _, err := rand.Read(identity[:]); err != nil {
		panic(fmt.Sprintf("cannot generate daemon instance identity: %v", err))
	}
	if idle <= 0 {
		idle = defaultDaemonIdleTimeout
	}
	server := &daemon{
		socket:      socket,
		instanceID:  hex.EncodeToString(identity[:]),
		desktop:     desktop,
		environment: desktopFingerprint(environment),
		display:     environmentDisplay(environment),
		idleTimeout: idle,
		sessions:    map[string]*core.Service{},
		lastUsed:    time.Now(),
		stopped:     make(chan struct{}),
	}
	server.refPersistenceErr = server.loadRefCounters()
	return server
}

// refCounters are the next aN, wN and rN numbers. The daemon keeps them in a
// file beside the socket, so a restarted daemon never issues a number that a
// client of the previous daemon still holds: an old ref then fails as stale
// instead of naming a different element.
type refCounters struct {
	App     uint64 `json:"a"`
	Window  uint64 `json:"w"`
	Element uint64 `json:"r"`
}

func (d *daemon) countersPath() string { return d.socket + ".refs" }

func (d *daemon) loadRefCounters() error {
	data, err := os.ReadFile(d.countersPath())
	if errors.Is(err, os.ErrNotExist) {
		return nil
	}
	if err != nil {
		return fmt.Errorf("read reference counters: %w", err)
	}
	var counters refCounters
	if err := json.Unmarshal(data, &counters); err != nil {
		return fmt.Errorf("decode reference counters: %w", err)
	}
	var fields map[string]json.RawMessage
	_ = json.Unmarshal(data, &fields)
	for _, key := range []string{"a", "w", "r"} {
		if value := fields[key]; len(value) == 0 || string(value) == "null" {
			return fmt.Errorf("reference counters missing %q", key)
		}
	}
	if counters.App > d.desktop.AppRefRegistry.Next {
		d.desktop.AppRefRegistry.Next = counters.App
	}
	if counters.Window > d.desktop.WindowRefRegistry.Next {
		d.desktop.WindowRefRegistry.Next = counters.Window
	}
	if counters.Element > d.desktop.RefRegistry.Next {
		d.desktop.RefRegistry.Next = counters.Element
	}
	d.savedCounters = counters
	return nil
}

// saveRefCounters runs after each call, under the desktop lock.
func (d *daemon) saveRefCounters() error {
	counters := refCounters{
		App:     d.desktop.AppRefRegistry.Next,
		Window:  d.desktop.WindowRefRegistry.Next,
		Element: d.desktop.RefRegistry.Next,
	}
	if counters == d.savedCounters {
		return nil
	}
	data, _ := json.Marshal(counters)
	file, err := os.CreateTemp(filepath.Dir(d.countersPath()), ".refs-*")
	if err != nil {
		return err
	}
	defer os.Remove(file.Name())
	_, writeErr := file.Write(data)
	closeErr := file.Close()
	if writeErr != nil {
		return writeErr
	}
	if closeErr != nil {
		return closeErr
	}
	if err := os.Rename(file.Name(), d.countersPath()); err != nil {
		return err
	}
	d.savedCounters = counters
	return nil
}

// withDesktopCall keeps both transports inside the same desktop boundary.
// Persist refs before releasing the lock or delivering them to a client.
func (d *daemon) withDesktopCall(run func() any) (any, error) {
	d.desktop.Shared.Mu.Lock()
	defer d.desktop.Shared.Mu.Unlock()
	if d.refPersistenceErr != nil {
		return nil, d.refPersistenceErr
	}
	if d.currentEnvironment != nil {
		if now := d.currentEnvironment(); now != d.environment {
			return nil, fmt.Errorf("the desktop environment changed from %q to %q since this daemon started; "+
				"no call was run. Stop this daemon and start a new one", d.environment, now)
		}
	}
	result := run()
	if err := d.saveRefCounters(); err != nil {
		d.refPersistenceErr = fmt.Errorf("reference counters could not be saved: %w; this daemon will refuse further calls. "+
			"Do not reuse returned refs or retry input; the preceding call may already have emitted input", err)
		return result, d.refPersistenceErr
	}
	return result, nil
}

// A failed save must not publish numbers a restarted daemon may reuse.
// Return only input facts, not the tree, refs, or arbitrary application text.
func daemonToolError(result core.ToolCallResult, err error) core.ToolCallResult {
	source := result.StructuredContent
	if source == nil && len(result.Content) > 0 && result.Content[0].Type == "text" {
		_ = json.Unmarshal([]byte(result.Content[0].Text), &source)
	}
	facts := map[string]any{"ok": false, "code": contract.AXCodeRuntimeError}
	for _, key := range []string{"input_emitted", "completed", "requested"} {
		if value, ok := source[key]; ok {
			facts[key] = value
		}
	}
	for key, value := range daemonMutationFacts(source) {
		facts[key] = value
	}
	failure := core.TextResult(contract.CompactJSON(facts), true)
	failure.Content = append(failure.Content, core.ContentItem{Type: "text", Text: err.Error()})
	if result.StructuredContent != nil {
		failure.StructuredContent = facts
	}
	return failure
}

func daemonMutationFacts(source map[string]any) map[string]any {
	facts := map[string]any{}
	for _, key := range []string{"partial_input_possible", "mutation_unknown", "effect_unknown"} {
		if value, ok := source[key].(bool); ok {
			facts[key] = value
		}
	}
	return facts
}

// listen 绑定 socket，并应用平台对应的访问控制。
func (d *daemon) listen() error {
	if d.refPersistenceErr != nil {
		return d.refPersistenceErr
	}
	if err := prepareSocketPath(d.socket); err != nil {
		return err
	}
	listener, err := net.Listen("unix", d.socket)
	if err != nil {
		return err
	}
	if err := secureDaemonSocket(d.socket); err != nil {
		listener.Close()
		return err
	}
	d.listener = listener
	return nil
}

// serve accepts connections until stop, an idle timeout, or a listener
// error. It removes the socket on exit.
func (d *daemon) serve() error {
	defer os.Remove(d.socket)
	go d.watchIdle()
	for {
		conn, err := d.listener.Accept()
		if err != nil {
			select {
			case <-d.stopped:
				return nil
			default:
				return err
			}
		}
		if !peerIsSameUser(conn) {
			conn.Close()
			continue
		}
		d.mu.Lock()
		d.active++
		d.nextConn++
		connID := d.nextConn
		d.mu.Unlock()
		go func() {
			d.handleConn(conn, connID)
			d.mu.Lock()
			d.active--
			d.lastUsed = time.Now()
			d.mu.Unlock()
		}()
	}
}

func (d *daemon) stop() {
	d.stopOnce.Do(func() {
		close(d.stopped)
		if d.listener != nil {
			d.listener.Close()
		}
	})
}

// watchIdle stops the daemon when no client has been connected for the
// idle timeout.
func (d *daemon) watchIdle() {
	tick := d.idleTimeout / 10
	if tick > time.Second {
		tick = time.Second
	}
	if tick < 10*time.Millisecond {
		tick = 10 * time.Millisecond
	}
	ticker := time.NewTicker(tick)
	defer ticker.Stop()
	for {
		select {
		case <-d.stopped:
			return
		case <-ticker.C:
			d.mu.Lock()
			idle := d.active == 0 && time.Since(d.lastUsed) >= d.idleTimeout
			d.mu.Unlock()
			if idle {
				d.stop()
				return
			}
		}
	}
}

// session returns the named session, creating it on first use.
func (d *daemon) session(name string) *core.Service {
	d.mu.Lock()
	defer d.mu.Unlock()
	if svc := d.sessions[name]; svc != nil {
		return svc
	}
	svc := core.NewSessionService(d.desktop)
	d.sessions[name] = svc
	return svc
}

func (d *daemon) handleConn(conn net.Conn, connID int) {
	defer conn.Close()
	reader := bufio.NewReaderSize(conn, 64*1024)
	encoder := json.NewEncoder(conn)
	sessionName := fmt.Sprintf("conn-%d", connID)
	for {
		line, oversized, err := readMCPLine(reader)
		if trimmed := bytes.TrimSpace(line); len(trimmed) > 0 || oversized {
			var response map[string]any
			if oversized {
				response = jsonRPCError(nil, -32600, "message too large")
			} else {
				response = d.handleRequest(trimmed, &sessionName)
			}
			if response != nil {
				if encodeErr := encoder.Encode(response); encodeErr != nil {
					return
				}
			}
			d.mu.Lock()
			d.lastUsed = time.Now()
			d.mu.Unlock()
		}
		if err != nil {
			return
		}
		select {
		case <-d.stopped:
			return
		default:
		}
	}
}

func (d *daemon) handleRequest(line []byte, sessionName *string) map[string]any {
	request, err := decodeMCPRequest(line)
	if err != nil {
		return jsonRPCError(nil, -32700, "Parse error: invalid JSON")
	}
	id := request["id"]
	method, _ := request["method"].(string)
	params, _ := request["params"].(map[string]any)
	if params == nil {
		params = map[string]any{}
	}
	switch method {
	case "hello":
		// A client names its display only when its own environment sets
		// one; the daemon discovers the session for clients that do not.
		if display, _ := params["display"].(string); display != "" && display != d.display {
			return jsonRPCError(id, daemonErrorEnvironment, fmt.Sprintf(
				"this daemon serves display %q, the client runs on %q; use another socket",
				d.display, display))
		}
		if name, _ := params["session"].(string); strings.TrimSpace(name) != "" {
			*sessionName = strings.TrimSpace(name)
		}
		d.session(*sessionName)
		return jsonRPCResult(id, map[string]any{
			"protocol":         daemonProtocolVersion,
			"version":          contract.ServerVersion,
			"contract_version": contract.ContractVersion,
			"session":          *sessionName,
			"desktop":          d.environment,
			"pid":              os.Getpid(),
			"instance_id":      d.instanceID,
		})
	case "schema":
		return jsonRPCResult(id, contract.DescribeContract())
	case "status":
		d.mu.Lock()
		names := make([]string, 0, len(d.sessions))
		for name := range d.sessions {
			names = append(names, name)
		}
		active := d.active
		d.mu.Unlock()
		sort.Strings(names)
		return jsonRPCResult(id, map[string]any{
			"version": contract.ServerVersion, "contract_version": contract.ContractVersion, "pid": os.Getpid(),
			"socket": d.socket, "desktop": d.environment, "sessions": names, "connections": active,
		})
	case "stop":
		go func() {
			time.Sleep(50 * time.Millisecond)
			d.stop()
		}()
		return jsonRPCResult(id, map[string]any{})
	case "call":
		if replay, present := params["replay_only"]; present {
			only, ok := replay.(bool)
			if !ok {
				return jsonRPCError(id, -32602, "replay_only must be a boolean")
			}
			requestID, _ := params["request_id"].(string)
			if only && (params["daemon_instance"] == nil || strings.TrimSpace(requestID) == "") {
				return jsonRPCError(id, -32602, "replay_only requires daemon_instance and a nonempty request_id")
			}
		}
		if binding, present := params["daemon_instance"]; present {
			instance, ok := binding.(string)
			if !ok || instance == "" {
				return jsonRPCError(id, -32602, "daemon_instance must be a nonempty string")
			}
			if instance != d.instanceID {
				return jsonRPCError(id, daemonErrorInstance, "daemon instance changed; input from the previous instance may have run; request was not sent to the desktop")
			}
		}
		result := d.call(d.session(*sessionName), params)
		if err, ok := result.(error); ok && errors.Is(err, contract.ErrReplayUnavailable) {
			return jsonRPCError(id, daemonErrorReplayUnavailable, err.Error())
		}
		return jsonRPCResult(id, result)
	case "mcp":
		message, _ := params["message"].(map[string]any)
		if message == nil {
			return jsonRPCError(id, -32602, "mcp requires message")
		}
		svc := d.session(*sessionName)
		value, callErr := d.withDesktopCall(func() any { return handleMCPRequest(message, svc) })
		response, _ := value.(map[string]any)
		if callErr != nil {
			if result, ok := response["result"].(core.ToolCallResult); ok {
				response["result"] = daemonToolError(result, callErr)
			} else if message["id"] != nil {
				response = jsonRPCError(message["id"], -32603, callErr.Error())
			}
		}
		return jsonRPCResult(id, map[string]any{"message": response})
	default:
		return jsonRPCError(id, -32601, "Method not found: "+method)
	}
}

const daemonErrorEnvironment = -32010
const daemonErrorInstance = -32011
const daemonErrorReplayUnavailable = -32012

// textCallResponse is a text-format call over the socket: the model-facing
// result plus its public error code, which the CLI maps to an exit status.
type textCallResponse struct {
	core.ToolCallResult
	Code string `json:"code,omitempty"`
}

// call runs one public tool call for a session. The desktop lock serializes
// calls from all sessions, so input from two clients never interleaves.
func (d *daemon) call(svc *core.Service, params map[string]any) any {
	tool, _ := params["tool"].(string)
	args, _ := params["args"].(map[string]any)
	if args == nil {
		args = map[string]any{}
	}
	format, _ := params["format"].(string)
	if format == "" {
		format = contract.FormatRecords
	}
	options := core.CallOptions{Format: format}
	options.RequestID, _ = params["request_id"].(string)
	options.Since, _ = params["since"].(string)
	publicName, ok := contract.PublicToolCallName(tool)
	if !ok {
		return core.Record{OK: false, Op: tool, Code: contract.AXCodeInvalidArgument, Path: "tool",
			Message: fmt.Sprintf("unknown tool %q; use AX or GUI", tool), TimingMS: map[string]int64{}}
	}
	run := func() any {
		response, callErr := d.withDesktopCall(func() any {
			if format == contract.FormatText {
				result, code, err := svc.CallForText(publicName, args)
				if err != nil {
					return core.TextResult(err.Error(), true)
				}
				return textCallResponse{ToolCallResult: result, Code: code}
			}
			return svc.CallRecord(publicName, args, options)
		})
		if callErr == nil {
			return response
		}
		if format == contract.FormatText {
			result, _ := response.(textCallResponse)
			result.ToolCallResult = daemonToolError(result.ToolCallResult, callErr)
			result.Code = contract.AXCodeRuntimeError
			return result
		}
		record, ok := response.(core.Record)
		if !ok {
			record = core.Record{Op: core.RecordOp(publicName, args), TimingMS: map[string]int64{}}
		}
		return core.Record{OK: false, Op: record.Op, Code: contract.AXCodeRuntimeError, Message: callErr.Error(),
			InputEmitted: record.InputEmitted, Completed: record.Completed, Requested: record.Requested,
			Evidence: daemonMutationFacts(record.Evidence), TimingMS: record.TimingMS}
	}
	if strings.TrimSpace(options.RequestID) == "" {
		return run()
	}
	key := publicName + "\x00" + contract.CompactJSON(args) + "\x00" + format + "\x00" + options.Since
	replayOnly, _ := params["replay_only"].(bool)
	response, replayed, err := d.desktop.Shared.ReplayOnce(options.RequestID, key, replayOnly, run)
	if err != nil {
		if errors.Is(err, contract.ErrReplayUnavailable) {
			return err
		}
		return core.Record{OK: false, Op: core.RecordOp(publicName, args), RequestID: options.RequestID,
			Code: contract.AXCodeInvalidArgument, Path: "request_id", Message: err.Error(),
			TimingMS: map[string]int64{}}
	}
	if record, isRecord := response.(core.Record); isRecord {
		record.RequestID = options.RequestID
		record.Replayed = replayed
		return record
	}
	return response
}

// runServe implements `computer-use serve`.
func runServe(args []string, stderr io.Writer) error {
	socket := defaultDaemonSocket()
	idle := defaultDaemonIdleTimeout
	for index := 0; index < len(args); index++ {
		switch args[index] {
		case "--socket":
			index++
			if index >= len(args) {
				return usageError("--socket requires a path")
			}
			socket = args[index]
		case "--idle-timeout":
			index++
			if index >= len(args) {
				return usageError("--idle-timeout requires a duration")
			}
			parsed, err := time.ParseDuration(args[index])
			if err != nil || parsed <= 0 {
				return usageError("--idle-timeout must be a positive duration such as 30m")
			}
			idle = parsed
		default:
			return usageError("unknown serve option: " + args[index])
		}
	}
	backend := core.DefaultRuntimeBackend()
	server := newDaemon(socket, core.NewDesktopState(backend), backend.RuntimeEnvironment(os.Environ()), idle)
	server.currentEnvironment = func() string {
		return desktopFingerprint(backend.RuntimeEnvironment(os.Environ()))
	}
	if err := server.listen(); err != nil {
		return err
	}
	fmt.Fprintf(stderr, "computer-use serve: listening on %s\n", socket)
	return server.serve()
}

// usageErr is a command-line usage error; the CLI exits 64 for it.
type usageErr struct{ message string }

func (e *usageErr) Error() string { return e.message }

func usageError(message string) error { return &usageErr{message: message} }
