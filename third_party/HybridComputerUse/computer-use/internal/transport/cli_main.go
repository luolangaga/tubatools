package transport

import (
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

func RunCLI(args []string, stdout io.Writer) error {
	if len(args) == 0 {
		fmt.Fprint(stdout, helpText(""))
		return nil
	}

	switch args[0] {
	case "-h", "--help", "help":
		topic := ""
		if len(args) > 1 {
			topic = args[1]
		}
		fmt.Fprint(stdout, helpText(topic))
		return nil
	case "-v", "--version", "version":
		fmt.Fprintln(stdout, contract.ServerVersion)
		return nil
	case "mcp":
		return runMCPCommand(args[1:], os.Stdin, stdout)
	case "serve":
		return runServe(args[1:], os.Stderr)
	case "daemon":
		return runDaemonCommand(args[1:], stdout)
	case "schema":
		return runSchema(args[1:], stdout)
	case "batch":
		if status := runBatch(args[1:], os.Stdin, stdout, os.Stderr); status != 0 {
			return &ExitError{status: status}
		}
		return nil
	case "doctor":
		fmt.Fprintln(stdout, runtimeNotes(core.DefaultRuntimeBackend().HostInfo().Platform))
		return nil
	case "call":
		output, hasError, err := runCallCommand(args[1:], core.NewService())
		if err != nil {
			return err
		}
		encoded, err := json.MarshalIndent(output, "", "  ")
		if err != nil {
			return err
		}
		fmt.Fprintln(stdout, string(encoded))
		if hasError {
			return errors.New("tool call returned isError=true")
		}
		return nil
	default:
		if _, shorthand := cliShorthands[args[0]]; shorthand {
			if status := runShorthand(args[0], args[1:], stdout, os.Stderr); status != 0 {
				return &ExitError{status: status}
			}
			return nil
		}
		return fmt.Errorf("unknown command: %s\n\n%s", args[0], helpText(""))
	}
}

func runCallCommand(args []string, svc *core.Service) (any, bool, error) {
	if len(args) == 0 {
		return nil, false, errors.New("call requires a tool name or --calls/--calls-file")
	}

	guard := newDaemonInputGuard(defaultDaemonSocket())
	dispatch := func(name string, arguments map[string]any) (core.ToolCallResult, error) {
		// Standalone calls own separate refs and an input lock, just like an
		// unattached MCP server. A live daemon must remain the input owner.
		if refusal := guard.refuse(map[string]any{
			"method": "tools/call",
			"params": map[string]any{"name": name, "arguments": arguments},
		}); refusal != nil {
			return refusal["result"].(core.ToolCallResult), nil
		}
		return svc.CallForCommandLine(name, arguments)
	}

	var toolName, argsJSON, argsFile, callsJSON, callsFile string
	for index := 0; index < len(args); index++ {
		arg := args[index]
		switch arg {
		case "--args":
			index++
			if index >= len(args) {
				return nil, false, errors.New("--args requires a value")
			}
			argsJSON = args[index]
		case "--args-file":
			index++
			if index >= len(args) {
				return nil, false, errors.New("--args-file requires a value")
			}
			argsFile = args[index]
		case "--calls":
			index++
			if index >= len(args) {
				return nil, false, errors.New("--calls requires a value")
			}
			callsJSON = args[index]
		case "--calls-file":
			index++
			if index >= len(args) {
				return nil, false, errors.New("--calls-file requires a value")
			}
			callsFile = args[index]
		default:
			if strings.HasPrefix(arg, "-") {
				return nil, false, fmt.Errorf("unknown call option: %s", arg)
			}
			if toolName != "" {
				return nil, false, errors.New("call accepts at most one tool name")
			}
			toolName = arg
		}
	}

	if callsJSON != "" || callsFile != "" {
		if toolName != "" || argsJSON != "" || argsFile != "" {
			return nil, false, errors.New("call sequence does not accept a tool name, --args, or --args-file")
		}
		calls, err := readCallSequence(callsJSON, callsFile)
		if err != nil {
			return nil, false, err
		}
		var outputs []map[string]any
		hasError := false
		for _, call := range calls {
			publicName, ok := contract.PublicToolCallName(call.Tool)
			if !ok {
				return nil, false, fmt.Errorf(
					"unsupported public tool %q; use AX or GUI", call.Tool)
			}
			result, err := dispatch(publicName, call.Args)
			if err != nil {
				return nil, false, err
			}
			outputs = append(outputs, map[string]any{"tool": publicName, "result": result})
			if result.IsError {
				hasError = true
				break
			}
		}
		return outputs, hasError, nil
	}

	if toolName == "" {
		return nil, false, errors.New("call requires a tool name or --calls/--calls-file")
	}
	arguments, err := readArguments(argsJSON, argsFile)
	if err != nil {
		return nil, false, err
	}
	publicName, ok := contract.PublicToolCallName(toolName)
	if !ok {
		return nil, false, fmt.Errorf(
			"unsupported public tool %q; use AX or GUI", toolName)
	}
	result, err := dispatch(publicName, arguments)
	if err != nil {
		return nil, false, err
	}
	return result, result.IsError, nil
}

type callSpec struct {
	Tool string
	Args map[string]any
}

func readArguments(inline, file string) (map[string]any, error) {
	if inline != "" && file != "" {
		return nil, errors.New("Use either inline JSON or a JSON file, not both")
	}
	if inline == "" && file == "" {
		return map[string]any{}, nil
	}
	source, err := readJSONSource(inline, file)
	if err != nil {
		return nil, err
	}
	var args map[string]any
	if err := decodeJSONArg(source, &args); err != nil {
		return nil, fmt.Errorf("Invalid JSON input: %w", err)
	}
	if args == nil {
		return nil, errors.New("--args must be a JSON object")
	}
	return args, nil
}

func readCallSequence(inline, file string) ([]callSpec, error) {
	if inline != "" && file != "" {
		return nil, errors.New("Use either --calls or --calls-file, not both")
	}
	source, err := readJSONSource(inline, file)
	if err != nil {
		return nil, err
	}
	var raw []map[string]any
	if err := decodeJSONArg(source, &raw); err != nil {
		return nil, fmt.Errorf("Invalid JSON input: %w", err)
	}
	calls := make([]callSpec, 0, len(raw))
	for index, item := range raw {
		name, _ := item["tool"].(string)
		if name == "" {
			name, _ = item["name"].(string)
		}
		if name == "" {
			return nil, fmt.Errorf("call sequence item #%d requires a non-empty tool", index+1)
		}
		args, _ := item["args"].(map[string]any)
		if args == nil {
			args, _ = item["arguments"].(map[string]any)
		}
		if args == nil {
			args = map[string]any{}
		}
		calls = append(calls, callSpec{Tool: name, Args: args})
	}
	return calls, nil
}

func readJSONSource(inline, file string) (string, error) {
	if inline != "" {
		return inline, nil
	}
	if file == "" {
		return "", errors.New("JSON input is required")
	}
	data, err := os.ReadFile(file)
	if err != nil {
		return "", err
	}
	return string(data), nil
}

func helpText(command string) string {
	switch command {
	case "mcp":
		return "Usage:\n  computer-use mcp\n\nStart the stdio MCP server.\n"
	case "call":
		return "Usage:\n  computer-use call <AX|GUI> [--args '<json-object>']\n  computer-use call --calls '<json-array>'\n\nOnly the public AX and GUI tools are accepted.\n"
	case "cli":
		return cliHelpText()
	default:
		return `Computer Use

Usage:
  computer-use [command] [options]

Commands:
  mcp                  Start the stdio MCP server.
  doctor               Print platform runtime requirements.
  call <AX|GUI>         Call one public tool, or run a JSON array of calls.
  help [command]       Show general or command-specific help.
  version              Print the CLI version.

Notes:
  Run in the signed-in desktop session. Native providers use Linux AT-SPI2,
  macOS Accessibility, or Windows UI Automation. Run doctor for requirements.
`
	}
}

func runtimeNotes(platform string) string {
	switch platform {
	case "linux":
		return "Linux runtime: AT-SPI2 and GDK run against the signed-in desktop user's accessibility session. When the client starts without XDG_RUNTIME_DIR, DBUS_SESSION_BUS_ADDRESS, or display variables, computer-use tries to discover the same user's session from /run/user/<uid> and desktop processes. On first use the provider switches session accessibility on (org.a11y.Status IsEnabled, and Gtk/Modules on Xfce); applications that were already running may need a restart, and AX apps names them."
	case "darwin":
		return "macOS runtime: install ComputerUseProvider.app beside computer-use and grant that provider Accessibility and Screen Recording permissions. Run inside the signed-in graphical session. Permissions and desktop capture must be checked with a live AX/GUI call; doctor does not verify them."
	case "windows":
		return "Windows runtime: install computer-use-windows-provider.exe beside computer-use.exe. Run in an unlocked interactive user session at the target application's integrity level. Secure desktops and Session 0 cannot provide normal desktop input. Verify access with a live AX/GUI call; doctor only describes requirements."
	default:
		return "No native desktop backend is linked for this platform."
	}
}
