package transport

import (
	"fmt"
	"io"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

// runMCPCommand implements `computer-use mcp [--attach SOCKET]`.
//
// Without --attach the server runs in process, exactly as before. With
// --attach it forwards every request to the daemon on SOCKET, in the
// daemon session "mcp", so MCP, the CLI and the SDK share refs and the
// input lock while the model keeps its own diff baseline.
func runMCPCommand(args []string, stdin io.Reader, stdout io.Writer) error {
	if len(args) == 0 {
		return runMCP(stdin, stdout)
	}
	if len(args) != 2 || args[0] != "--attach" || strings.TrimSpace(args[1]) == "" {
		return usageError("usage: computer-use mcp [--attach SOCKET]")
	}
	client, err := connectDaemon(args[1], "mcp", true)
	if err != nil {
		return err
	}
	defer client.close()
	return serveMCP(stdin, stdout, func(request map[string]any) map[string]any {
		var response struct {
			Message map[string]any `json:"message"`
		}
		if err := client.request("mcp", map[string]any{"message": request}, &response); err != nil {
			return jsonRPCError(request["id"], -32603, "computer-use daemon: "+err.Error())
		}
		return response.Message
	})
}

// daemonInputGuard keeps an in-process MCP server from sending input while
// a daemon serves the same desktop: two input sources would interleave and
// each would hold refs the other cannot see. Reads stay allowed. With no
// daemon on the default socket nothing changes, so existing MCP
// deployments behave as before.
type daemonInputGuard struct {
	socket string
	live   func(string) bool
}

func newDaemonInputGuard(socket string) *daemonInputGuard {
	return &daemonInputGuard{socket: socket, live: daemonLive}
}

// refuse returns an error response for an input call while a daemon is
// live, or nil to let the request run.
func (g *daemonInputGuard) refuse(request map[string]any) map[string]any {
	if g == nil || request["method"] != "tools/call" {
		return nil
	}
	params, _ := request["params"].(map[string]any)
	name, _ := params["name"].(string)
	arguments, _ := params["arguments"].(map[string]any)
	if !callMayEmitInput(name, arguments) || !g.live(g.socket) {
		return nil
	}
	message := fmt.Sprintf("A computer-use daemon serves this desktop (%s), so this in-process "+
		"server sends no input. Start the MCP server with --attach %s to share the daemon's refs "+
		"and input lock. No input was emitted.", g.socket, g.socket)
	result := core.TextResult(message, true)
	if name == contract.PublicToolAX {
		result.StructuredContent = map[string]any{
			contract.AXPublicErrorCodeKey:   contract.AXCodeRuntimeError,
			contract.AXPublicErrorDetailKey: message,
			contract.AXPublicErrorHintKey:   `{"mode":"observe","window_ref":"wN"}`,
			"input_events_emitted":          false,
			"preflight_only":                true,
			"evidence_scope":                "error_only",
		}
	} else {
		core.SetGUIPublicError(&result, contract.AXCodeRuntimeError, "", message, core.GUIScreenshotCall)
	}
	return jsonRPCResult(request["id"], core.FinalizeForModel(name, result, false))
}

// callMayEmitInput reports whether a public call can send desktop input.
func callMayEmitInput(name string, args map[string]any) bool {
	switch name {
	case contract.PublicToolGUI:
		actions, _ := args["actions"].([]any)
		for _, item := range actions {
			action, _ := item.(map[string]any)
			// screenshot and zoom only look at the desktop.
			if kind, _ := action["type"].(string); kind != "screenshot" && kind != "zoom" {
				return true
			}
		}
		return false
	case contract.PublicToolAX:
		if core.OptionalString(args, "mode") != contract.AXModeAct {
			return false
		}
		// An act of only wait steps sends no input; it is the read that
		// observe with until was before wait steps replaced it.
		actions, _ := args["actions"].([]any)
		for _, item := range actions {
			action, _ := item.(map[string]any)
			if kind, _ := action["type"].(string); kind != "wait" {
				return true
			}
		}
		return len(actions) == 0
	default:
		return false
	}
}
