package transport

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"sort"
	"strconv"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

// The shorthand commands are a thin adapter: each one builds the arguments
// of one public tool call and sends it to the daemon. No rule lives here;
// the core validates, resolves refs and renders. cliShorthands is the only
// hand-maintained mapping between the CLI and the contract, and
// TestCLIShorthandsProduceSchemaValidCalls checks every entry.

// cliShorthand maps positional arguments and flags to one tool call.
type cliShorthand struct {
	usage string
	build func(positional []string, flags cliFlags) (string, map[string]any, error)
}

// cliFlags are the parsed --name value options of a shorthand command.
type cliFlags map[string]string

func (f cliFlags) has(name string) bool { _, ok := f[name]; return ok }

var cliShorthands = map[string]cliShorthand{
	"apps": {"apps", func(p []string, f cliFlags) (string, map[string]any, error) {
		if len(p) != 0 {
			return "", nil, usageError("apps takes no arguments")
		}
		return contract.PublicToolAX, map[string]any{"mode": "apps"}, nil
	}},
	"observe": {"observe <app|wN> [--root rN|focused] [--scope window|content] [--as tree|diff|full|screenshot]",
		func(p []string, f cliFlags) (string, map[string]any, error) {
			if len(p) != 1 {
				return "", nil, usageError("observe needs one app or wN")
			}
			args := map[string]any{"mode": "observe"}
			setTarget(args, p[0])
			copyFlag(args, f, "root", "root")
			copyFlag(args, f, "scope", "scope")
			copyFlag(args, f, "as", "observe")
			if err := passRemovedUntil(args, f); err != nil {
				return "", nil, err
			}
			return contract.PublicToolAX, args, nil
		}},
	"wait": {"wait [wN] STEP_JSON [--as diff|tree]",
		func(p []string, f cliFlags) (string, map[string]any, error) {
			if len(p) == 0 || len(p) > 2 || (len(p) == 2 && !core.AXWindowRefPattern.MatchString(p[0])) {
				return "", nil, usageError(`wait needs one wait step as JSON, e.g. '{"window":"Save"}', after an optional wN`)
			}
			step, err := waitStepArg(p[len(p)-1])
			if err != nil {
				return "", nil, err
			}
			// A wait reads nothing more by default: a final tree read would
			// keep the desktop lock longer than the wait itself.
			args := map[string]any{"mode": "act", "actions": []any{step}, "observe": "none"}
			if len(p) == 2 {
				args["window_ref"] = p[0]
			}
			copyFlag(args, f, "as", "observe")
			return contract.PublicToolAX, args, nil
		}},
	"find": {"find <app|wN> [--role R] [--name S] [--name-has S] [--text S] [--text-has S] [--state S] [--limit N] | find --cursor fN",
		func(p []string, f cliFlags) (string, map[string]any, error) {
			args := map[string]any{"mode": "find"}
			if f.has("cursor") {
				if len(p) != 0 {
					return "", nil, usageError("find --cursor takes no app or wN")
				}
				args["cursor"] = f["cursor"]
			} else {
				if len(p) != 1 {
					return "", nil, usageError("find needs one app or wN")
				}
				setTarget(args, p[0])
				query := map[string]any{}
				for flag, field := range map[string]string{
					"role": "role", "name": "name_equals", "name-has": "name_contains",
					"text": "text_equals", "text-has": "text_contains", "state": "state",
				} {
					if value, ok := f[flag]; ok {
						query[field] = value
					}
				}
				args["query"] = query
				copyFlag(args, f, "scope", "scope")
			}
			if value, ok := f["limit"]; ok {
				limit, err := strconv.Atoi(value)
				if err != nil {
					return "", nil, usageError("--limit must be an integer")
				}
				args["limit"] = limit
			}
			return contract.PublicToolAX, args, nil
		}},
	"read": {"read rN [rN ...]", func(p []string, f cliFlags) (string, map[string]any, error) {
		if len(p) == 0 {
			return "", nil, usageError("read needs at least one rN")
		}
		refs := make([]any, len(p))
		for index, ref := range p {
			refs[index] = ref
		}
		return contract.PublicToolAX, map[string]any{"mode": contract.RecordsOpRead, "refs": refs}, nil
	}},
	"click":    singleAction("click rN", "click", "ref"),
	"type":     singleAction("type [rN] TEXT", "type", "ref", "text"),
	"key":      singleAction("key [rN] CTRL+S", "keypress", "ref", "keys"),
	"set":      singleAction("set rN VALUE", "set_value", "ref", "value"),
	"scroll":   singleAction("scroll rN up|down|left|right [--pages N]", "scroll", "ref", "direction"),
	"activate": singleAction("activate wN|TITLE", "activate", "window"),
	"close":    singleAction("close wN|TITLE", "close", "window"),
	"act": {"act --json '{\"actions\":[...]}'|-|@FILE [--window wN] [--wait STEP_JSON] [--as none|diff|tree]",
		func(p []string, f cliFlags) (string, map[string]any, error) {
			if len(p) != 0 || !f.has("json") {
				return "", nil, usageError("act needs --json with an object holding actions")
			}
			var body map[string]any
			if err := decodeJSONArg(f["json"], &body); err != nil {
				return "", nil, usageError("--json: " + err.Error())
			}
			args := map[string]any{"mode": "act"}
			for key, value := range body {
				args[key] = value
			}
			copyFlag(args, f, "window", "window_ref")
			copyFlag(args, f, "as", "observe")
			if err := appendWaitFlag(args, f); err != nil {
				return "", nil, err
			}
			return contract.PublicToolAX, args, nil
		}},
	"select": {"select rN [TEXT] [--to rN]",
		func(p []string, f cliFlags) (string, map[string]any, error) {
			if len(p) == 0 || len(p) > 2 || !core.AXElementRefPattern.MatchString(p[0]) {
				return "", nil, usageError("select needs rN, then an optional substring of it")
			}
			action := map[string]any{"type": "select", "ref": p[0]}
			if len(p) == 2 {
				action["text"] = p[1]
			}
			copyFlag(action, f, "to", "to_ref")
			args := map[string]any{"mode": "act", "actions": []any{action}}
			copyFlag(args, f, "window", "window_ref")
			copyFlag(args, f, "as", "observe")
			if err := appendWaitFlag(args, f); err != nil {
				return "", nil, err
			}
			return contract.PublicToolAX, args, nil
		}},
	"gui": {"gui screenshot | gui zoom X0 Y0 X1 Y1 | gui click|double_click|move X Y | gui type TEXT | gui key CTRL+S | gui --json '{\"actions\":[...]}'|-|@FILE",
		func(p []string, f cliFlags) (string, map[string]any, error) {
			if f.has("json") {
				var body map[string]any
				if err := decodeJSONArg(f["json"], &body); err != nil {
					return "", nil, usageError("--json: " + err.Error())
				}
				return contract.PublicToolGUI, body, nil
			}
			if len(p) == 0 {
				return "", nil, usageError("gui needs an action")
			}
			action := map[string]any{"type": p[0]}
			switch {
			case p[0] == "screenshot" && len(p) == 1:
			case p[0] == "zoom" && len(p) == 5:
				region, err := cliNumbers(p[1:])
				if err != nil {
					return "", nil, usageError("gui zoom needs numeric X0 Y0 X1 Y1 in frame pixels")
				}
				action["region"] = region
			case (p[0] == "click" || p[0] == "double_click" || p[0] == "move") && len(p) == 3:
				point, err := cliNumbers(p[1:])
				if err != nil {
					return "", nil, usageError("gui " + p[0] + " needs numeric X Y")
				}
				action["x"], action["y"] = point[0], point[1]
			case p[0] == "type" && len(p) == 2:
				action["text"] = p[1]
			case p[0] == "key" && len(p) == 2:
				action["type"] = "keypress"
				action["keys"] = splitChord(p[1])
			default:
				return "", nil, usageError("unknown gui action; use --json for other actions")
			}
			return contract.PublicToolGUI, map[string]any{"actions": []any{action}}, nil
		}},
}

// singleAction builds an act call with one action. fields name the
// positional arguments in order; a leading rN is optional for type and key,
// which then go to the focused window.
func singleAction(usage, actionType string, fields ...string) cliShorthand {
	return cliShorthand{usage, func(p []string, f cliFlags) (string, map[string]any, error) {
		action := map[string]any{"type": actionType}
		rest := p
		switch fields[0] {
		case "ref":
			optional := actionType == "type" || actionType == "keypress"
			if len(rest) > 0 && core.AXElementRefPattern.MatchString(rest[0]) {
				action["ref"] = rest[0]
				rest = rest[1:]
			} else if !optional {
				return "", nil, usageError(usage + ": the first argument must be rN")
			}
		case "window":
			if len(rest) != 1 {
				return "", nil, usageError(usage)
			}
			if core.AXWindowRefPattern.MatchString(rest[0]) {
				action["ref"] = rest[0]
			} else {
				action["title"] = rest[0]
			}
			rest = nil
		}
		if len(fields) > 1 {
			if len(rest) != 1 {
				return "", nil, usageError(usage)
			}
			switch fields[1] {
			case "keys":
				action["keys"] = splitChord(rest[0])
			default:
				action[fields[1]] = rest[0]
			}
		} else if len(rest) != 0 {
			return "", nil, usageError(usage)
		}
		if value, ok := f["pages"]; ok {
			pages, err := strconv.ParseFloat(value, 64)
			if err != nil {
				return "", nil, usageError("--pages must be a number")
			}
			action["pages"] = pages
		}
		args := map[string]any{"mode": "act", "actions": []any{action}}
		copyFlag(args, f, "window", "window_ref")
		copyFlag(args, f, "as", "observe")
		if err := appendWaitFlag(args, f); err != nil {
			return "", nil, err
		}
		return contract.PublicToolAX, args, nil
	}}
}

// cliNumbers parses positional numbers, such as GUI pixels.
func cliNumbers(words []string) ([]any, error) {
	out := make([]any, len(words))
	for index, word := range words {
		value, err := strconv.ParseFloat(word, 64)
		if err != nil {
			return nil, err
		}
		out[index] = value
	}
	return out, nil
}

func splitChord(chord string) []any {
	parts := strings.Split(chord, "+")
	keys := make([]any, 0, len(parts))
	for _, part := range parts {
		keys = append(keys, strings.ToUpper(strings.TrimSpace(part)))
	}
	return keys
}

func setTarget(args map[string]any, target string) {
	if core.AXWindowRefPattern.MatchString(target) {
		args["window_ref"] = target
	} else {
		args["app"] = target
	}
}

func copyFlag(args map[string]any, f cliFlags, flag, field string) {
	if value, ok := f[flag]; ok {
		args[field] = value
	}
}

// waitStepArg reads one wait step written as JSON. The type is always
// wait, so a step given to wait or --wait never sends input; the core checks
// every other field.
func waitStepArg(raw string) (map[string]any, error) {
	var step map[string]any
	if err := decodeJSONArg(raw, &step); err != nil || step == nil {
		return nil, usageError(`a wait step is a JSON object such as '{"window":"Save"}'`)
	}
	step["type"] = "wait"
	return step, nil
}

// passRemovedUntil passes the removed --until flag through as the until
// field, so the core rejects the call before any input and its hint shows
// the same call with a wait step; the CLI keeps no conversion of its own.
func passRemovedUntil(args map[string]any, f cliFlags) error {
	raw, ok := f["until"]
	if !ok {
		return nil
	}
	var value any
	if err := decodeJSONArg(raw, &value); err != nil {
		return usageError("--until was removed; use --wait STEP_JSON")
	}
	args["until"] = value
	return nil
}

// appendWaitFlag adds the step given with --wait after the actions.
func appendWaitFlag(args map[string]any, f cliFlags) error {
	if err := passRemovedUntil(args, f); err != nil {
		return err
	}
	raw, ok := f["wait"]
	if !ok {
		return nil
	}
	step, err := waitStepArg(raw)
	if err != nil {
		return usageError("--wait: " + err.Error())
	}
	actions, _ := args["actions"].([]any)
	args["actions"] = append(actions, step)
	return nil
}

// cliStdin is where `--json -` reads its body; tests replace it.
var cliStdin io.Reader = os.Stdin

// jsonFlagSource returns the body of an act or gui --json flag: "-" reads
// standard input and "@PATH" reads a file. A JSON value never starts with
// either, so an inline body is returned unchanged. Reading the body keeps
// it out of shell quoting, which matters when code builds the command.
func jsonFlagSource(value string) (string, error) {
	switch {
	case value == "-":
		data, err := io.ReadAll(cliStdin)
		if err != nil {
			return "", usageError("--json -: " + err.Error())
		}
		return string(data), nil
	case strings.HasPrefix(value, "@"):
		data, err := os.ReadFile(value[1:])
		if err != nil {
			return "", usageError("--json " + value + ": " + err.Error())
		}
		return string(data), nil
	}
	return value, nil
}

func decodeJSONArg(raw string, out any) error {
	decoder := json.NewDecoder(strings.NewReader(raw))
	decoder.UseNumber()
	if err := decoder.Decode(out); err != nil {
		return err
	}
	var extra any
	if err := decoder.Decode(&extra); err != io.EOF {
		if err != nil {
			return fmt.Errorf("invalid trailing JSON content: %w", err)
		}
		return errors.New("expected exactly one JSON value")
	}
	return nil
}

// cliBoolFlags take no value.
var cliBoolFlags = map[string]bool{"json": true, "records": true, "text": true, "keep-going": true}

// CLI-only options are checked before connecting to the daemon. The core
// still validates all values and the constructed desktop request.
func allowedCLIFlags(command string) map[string]bool {
	flags := "socket"
	if _, shorthand := cliShorthands[command]; shorthand {
		flags += " session request-id since json records"
	}
	switch command {
	case "observe":
		flags += " root scope as until"
	case "find":
		flags += " role name name-has text text-has state limit cursor scope"
	case "wait":
		flags += " as"
	case "click", "type", "key", "set", "activate", "close", "act":
		flags += " window as wait until"
	case "scroll":
		flags += " window as wait until pages"
	case "select":
		flags += " window as wait until to"
	case "batch":
		flags += " session keep-going text json records"
	}
	allowed := map[string]bool{}
	for _, name := range strings.Fields(flags) {
		allowed[name] = true
	}
	return allowed
}

// parseCLIArgs splits positional arguments from --flags. --json is a value
// flag for act and gui and a switch elsewhere.
func parseCLIArgs(command string, args []string) ([]string, cliFlags, error) {
	positional := []string{}
	flags := cliFlags{}
	jsonTakesValue := command == "act" || command == "gui"
	allowed := allowedCLIFlags(command)
	for index := 0; index < len(args); index++ {
		arg := args[index]
		if !strings.HasPrefix(arg, "--") || arg == "--" {
			positional = append(positional, arg)
			continue
		}
		name := strings.TrimPrefix(arg, "--")
		flag, _, _ := strings.Cut(name, "=")
		if !allowed[flag] {
			return nil, nil, usageError(fmt.Sprintf("unknown option --%s for %s; see computer-use help cli", flag, command))
		}
		if value, inline, found := strings.Cut(name, "="); found {
			flags[value] = inline
			continue
		}
		if cliBoolFlags[name] && !(name == "json" && jsonTakesValue) && !(name == "text" && command == "find") {
			flags[name] = ""
			continue
		}
		index++
		if index >= len(args) {
			return nil, nil, usageError("--" + name + " requires a value")
		}
		flags[name] = args[index]
	}
	return positional, flags, nil
}

// cliOutput selects how a call result is printed: model text (the same
// bytes MCP returns) or the records envelope.
type cliOutput struct {
	records bool
	session string
	socket  string
}

func cliOutputFrom(flags cliFlags, command string) cliOutput {
	output := cliOutput{
		session: core.FirstNonEmpty(flags["session"], os.Getenv("HCU_SESSION"), "default"),
		socket:  core.FirstNonEmpty(flags["socket"], defaultDaemonSocket()),
	}
	// --json selects records. act and gui take --json as their body, so
	// they select records with --records.
	output.records = flags.has("records") || (flags.has("json") && command != "act" && command != "gui")
	return output
}

// runShorthand runs one shorthand command and returns its exit status.
func runShorthand(command string, args []string, stdout, stderr io.Writer) int {
	shorthand := cliShorthands[command]
	positional, flags, err := parseCLIArgs(command, args)
	if err == nil {
		output := cliOutputFrom(flags, command)
		options := core.CallOptions{RequestID: flags["request-id"], Since: flags["since"]}
		for _, name := range []string{"session", "socket", "request-id", "since", "records"} {
			delete(flags, name)
		}
		if command != "act" && command != "gui" {
			delete(flags, "json")
		} else if body, ok := flags["json"]; ok {
			flags["json"], err = jsonFlagSource(body)
		}
		var tool string
		var callArgs map[string]any
		if err == nil {
			tool, callArgs, err = shorthand.build(positional, flags)
		}
		if err == nil {
			return runDaemonCall(output, tool, callArgs, options, stdout, stderr)
		}
	}
	fmt.Fprintf(stderr, "%v\nusage: computer-use %s\n", err, shorthand.usage)
	return exitUsage
}

const exitUsage = 64

// exitCodeFor maps a public error code to the CLI exit status.
func exitCodeFor(code string) int {
	if code == "" {
		return 0
	}
	if entry, ok := contract.PublicErrorCodes[code]; ok {
		return entry.ExitCode
	}
	return contract.PublicErrorCodes[contract.AXCodeRuntimeError].ExitCode
}

// runDaemonCall sends one call to the daemon (starting it if needed), prints
// the result and returns the exit status.
func runDaemonCall(output cliOutput, tool string, args map[string]any, options core.CallOptions, stdout, stderr io.Writer) int {
	client, err := connectDaemon(output.socket, output.session, true)
	if err != nil {
		fmt.Fprintln(stderr, err)
		return exitCodeFor(contract.AXCodeRuntimeError)
	}
	defer client.close()
	code, err := client.callAndPrint(output, tool, args, options, stdout)
	if err != nil {
		fmt.Fprintln(stderr, err)
		return exitCodeFor(contract.AXCodeRuntimeError)
	}
	return exitCodeFor(code)
}

func (c *daemonClient) callAndPrint(output cliOutput, tool string, args map[string]any, options core.CallOptions, stdout io.Writer) (string, error) {
	params := map[string]any{"tool": tool, "args": args}
	if options.RequestID != "" {
		params["request_id"] = options.RequestID
	}
	if options.Since != "" {
		params["since"] = options.Since
	}
	if output.records || isRecordsOnly(tool, args) {
		params["format"] = contract.FormatRecords
		var raw json.RawMessage
		if err := c.request("call", params, &raw); err != nil {
			return "", err
		}
		var record struct {
			Code string `json:"code"`
		}
		_ = json.Unmarshal(raw, &record)
		fmt.Fprintln(stdout, string(raw))
		return record.Code, nil
	}
	params["format"] = contract.FormatText
	var response struct {
		Content []core.ContentItem `json:"content"`
		Code    string             `json:"code"`
		IsError bool               `json:"isError"`
	}
	if err := c.request("call", params, &response); err != nil {
		return "", err
	}
	printTextContent(stdout, response.Content)
	if response.IsError && response.Code == "" {
		return contract.AXCodeRuntimeError, nil
	}
	return response.Code, nil
}

// isRecordsOnly reports calls that have no text form.
func isRecordsOnly(tool string, args map[string]any) bool {
	return tool == contract.PublicToolAX && core.OptionalString(args, "mode") == contract.RecordsOpRead
}

// printTextContent prints the model-facing text items, and an image item as
// its file path.
func printTextContent(stdout io.Writer, content []core.ContentItem) {
	for _, item := range content {
		switch item.Type {
		case "text":
			text := item.Text
			if !strings.HasSuffix(text, "\n") {
				text += "\n"
			}
			fmt.Fprint(stdout, text)
		case "image":
			fmt.Fprintf(stdout, "[image %s]\n", item.Path)
		}
	}
}

// runBatch reads a JSON array of {"tool","args"} calls (from a file or
// stdin) and prints one records line per call. It stops at the first error
// unless --keep-going is given; the exit status is that of the first error.
func runBatch(args []string, stdin io.Reader, stdout, stderr io.Writer) int {
	positional, flags, err := parseCLIArgs("batch", args)
	if err != nil || len(positional) > 1 {
		if err != nil {
			fmt.Fprintln(stderr, err)
		}
		fmt.Fprintln(stderr, "usage: computer-use batch [FILE|-] [--keep-going] [--text]")
		return exitUsage
	}
	source := stdin
	if len(positional) == 1 && positional[0] != "-" {
		file, openErr := os.Open(positional[0])
		if openErr != nil {
			fmt.Fprintln(stderr, openErr)
			return exitUsage
		}
		defer file.Close()
		source = file
	}
	data, err := io.ReadAll(source)
	if err != nil {
		fmt.Fprintln(stderr, err)
		return exitUsage
	}
	calls, err := readCallSequence(string(data), "")
	if err != nil {
		fmt.Fprintln(stderr, err)
		return exitUsage
	}
	output := cliOutputFrom(flags, "batch")
	output.records = !flags.has("text")
	client, err := connectDaemon(output.socket, output.session, true)
	if err != nil {
		fmt.Fprintln(stderr, err)
		return exitCodeFor(contract.AXCodeRuntimeError)
	}
	defer client.close()
	status := 0
	for _, call := range calls {
		tool, ok := contract.PublicToolCallName(call.Tool)
		if !ok {
			fmt.Fprintf(stderr, "unsupported public tool %q; use AX or GUI\n", call.Tool)
			return exitUsage
		}
		code, err := client.callAndPrint(output, tool, call.Args, core.CallOptions{}, stdout)
		if err != nil {
			fmt.Fprintln(stderr, err)
			return exitCodeFor(contract.AXCodeRuntimeError)
		}
		if code != "" && status == 0 {
			status = exitCodeFor(code)
		}
		if code != "" && !flags.has("keep-going") {
			break
		}
	}
	return status
}

// runSchema prints the contract: the published schemas and the error table.
// With a running daemon it prints the daemon's copy, so a client sees what
// it is actually talking to.
func runSchema(args []string, stdout io.Writer) error {
	_, flags, err := parseCLIArgs("schema", args)
	if err != nil {
		return err
	}
	var contract any = contract.DescribeContract()
	socket := core.FirstNonEmpty(flags["socket"], defaultDaemonSocket())
	if daemonLive(socket) {
		if client, dialErr := dialDaemon(socket); dialErr == nil {
			var remote json.RawMessage
			if client.request("schema", map[string]any{}, &remote) == nil {
				contract = remote
			}
			client.close()
		}
	}
	encoded, err := json.MarshalIndent(contract, "", "  ")
	if err != nil {
		return err
	}
	fmt.Fprintln(stdout, string(encoded))
	return nil
}

// runDaemonCommand implements `computer-use daemon start|stop|status`.
func runDaemonCommand(args []string, stdout io.Writer) error {
	positional, flags, err := parseCLIArgs("daemon", args)
	if err != nil {
		return err
	}
	if len(positional) != 1 {
		return usageError("usage: computer-use daemon start|stop|status [--socket PATH]")
	}
	socket := core.FirstNonEmpty(flags["socket"], defaultDaemonSocket())
	switch positional[0] {
	case "start":
		client, err := connectDaemon(socket, "", true)
		if err != nil {
			return err
		}
		defer client.close()
		fmt.Fprintf(stdout, "daemon running on %s (pid %v)\n", socket, client.hello["pid"])
		return nil
	case "status", "stop":
		if !daemonLive(socket) {
			fmt.Fprintf(stdout, "no daemon on %s\n", socket)
			return nil
		}
		client, err := dialDaemon(socket)
		if err != nil {
			return err
		}
		defer client.close()
		var result json.RawMessage
		if err := client.request(positional[0], map[string]any{}, &result); err != nil {
			return err
		}
		if positional[0] == "stop" {
			fmt.Fprintf(stdout, "daemon on %s stopping\n", socket)
			return nil
		}
		fmt.Fprintln(stdout, string(result))
		return nil
	default:
		return usageError("usage: computer-use daemon start|stop|status [--socket PATH]")
	}
}

// ExitError carries an exit status out of runCLI.
type ExitError struct{ status int }

func (e *ExitError) Error() string { return "exit status " + strconv.Itoa(e.status) }

func ExitStatusOf(err error) int {
	var exit *ExitError
	if errors.As(err, &exit) {
		return exit.status
	}
	var usage *usageErr
	if errors.As(err, &usage) {
		return exitUsage
	}
	return 1
}

// cliHelpText documents the daemon commands. The general help text is part
// of the contract golden and stays as it was; it is reached with
// `computer-use help cli`.
func cliHelpText() string {
	names := make([]string, 0, len(cliShorthands))
	for name := range cliShorthands {
		names = append(names, name)
	}
	sort.Strings(names)
	var out strings.Builder
	out.WriteString(`Desktop daemon and command line

One daemon serves this desktop over a Unix socket and starts on first use.
Refs (aN, wN, rN) stay valid across commands and processes; one call runs
at a time, so input from several clients never interleaves.

Commands:
  serve [--socket PATH] [--idle-timeout 30m]   Run the daemon in the foreground.
  daemon start|stop|status                     Manage the daemon.
  schema                                       Print tool schemas and error codes (JSON).
  batch [FILE|-] [--keep-going] [--text]       Run a JSON array of {"tool","args"}; one record per line.
  mcp --attach SOCKET                          MCP over stdio, forwarded to the daemon.
`)
	for _, name := range names {
		fmt.Fprintf(&out, "  %s\n", cliShorthands[name].usage)
	}
	out.WriteString(`
Output: model text by default, identical to the MCP result; --json (or
--records for act and gui) prints one records JSON object. Images are files.
GUI x/y and zoom regions are pixels of the current GUI frame: the latest gui
screenshot or observe --as full|screenshot image. gui zoom only looks; its
result states how a zoom pixel maps back to frame pixels. Other GUI actions
(drag, scroll, wait) and other AX steps take --json; --json - reads the body
from standard input and --json @FILE from a file.
Options: --session NAME (or HCU_SESSION), --socket PATH (or HCU_SOCKET),
--request-id ID (act retries return the first result), --since SNAPSHOT_ID.
Input commands also take --window wN, --as none|diff|tree, and --wait STEP_JSON:
one wait step, e.g. '{"window":"Save"}', '{"ref":"r3","state":"checked"}', or
'{"settle":true}' (any change, then quiet) that runs after the action; it
fails with exit 3 (wait_timeout) when it does not hold.

Exit status:
`)
	codes := core.SortedErrorCodes()
	for _, code := range codes {
		entry := contract.PublicErrorCodes[code]
		fmt.Fprintf(&out, "  %-3d %-18s input emitted: %-5s %s\n", entry.ExitCode, code, entry.InputEmitted, entry.Recovery)
	}
	out.WriteString("  0   success\n  64  command-line usage error\n")
	return out.String()
}
