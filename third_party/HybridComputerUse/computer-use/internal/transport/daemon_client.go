package transport

import (
	"bufio"
	"encoding/json"
	"errors"
	"fmt"
	"net"
	"os"
	"strings"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// daemonClient is one connection to the daemon. The CLI and an attached MCP
// server use it; the Python SDK speaks the same line protocol.
type daemonClient struct {
	conn   net.Conn
	reader *bufio.Reader
	nextID int
	hello  map[string]any
}

// errDaemonUnavailable marks failures to reach or start the daemon.
var errDaemonUnavailable = errors.New("daemon unavailable")

func dialDaemon(socket string) (*daemonClient, error) {
	conn, err := net.DialTimeout("unix", socket, 2*time.Second)
	if err != nil {
		return nil, err
	}
	return &daemonClient{conn: conn, reader: bufio.NewReaderSize(conn, 64*1024)}, nil
}

// connectDaemon connects to the daemon for this desktop, starting it when
// no daemon answers, and says hello. session "" lets the daemon assign a
// per-connection session.
func connectDaemon(socket, session string, autoStart bool) (*daemonClient, error) {
	client, err := dialDaemon(socket)
	if err != nil && autoStart {
		client, err = startDaemon(socket)
	}
	if err != nil {
		return nil, fmt.Errorf("%w: %v", errDaemonUnavailable, err)
	}
	params := map[string]any{"display": environmentDisplay(os.Environ())}
	if session != "" {
		params["session"] = session
	}
	var hello map[string]any
	if err := client.request("hello", params, &hello); err != nil {
		client.close()
		return nil, err
	}
	if fmt.Sprint(hello["contract_version"]) != contract.ContractVersion {
		client.close()
		return nil, fmt.Errorf("daemon contract %v differs from this client (%s); stop the daemon "+
			"(computer-use daemon stop) and retry", hello["contract_version"], contract.ContractVersion)
	}
	client.hello = hello
	return client, nil
}

func (c *daemonClient) close() {
	if c != nil && c.conn != nil {
		c.conn.Close()
	}
}

// request sends one JSON-RPC request and decodes its result into out, which
// may be a *json.RawMessage to keep the bytes.
func (c *daemonClient) request(method string, params any, out any) error {
	c.nextID++
	message, err := json.Marshal(map[string]any{
		"jsonrpc": "2.0", "id": c.nextID, "method": method, "params": params,
	})
	if err != nil {
		return err
	}
	if _, err := c.conn.Write(append(message, '\n')); err != nil {
		return fmt.Errorf("%w: %v", errDaemonUnavailable, err)
	}
	line, err := c.reader.ReadBytes('\n')
	if err != nil {
		return fmt.Errorf("%w: connection closed: %v", errDaemonUnavailable, err)
	}
	var response struct {
		Result json.RawMessage `json:"result"`
		Error  *struct {
			Code    int    `json:"code"`
			Message string `json:"message"`
		} `json:"error"`
	}
	if err := json.Unmarshal(line, &response); err != nil {
		return err
	}
	if response.Error != nil {
		return &daemonRPCError{code: response.Error.Code, message: response.Error.Message}
	}
	if raw, ok := out.(*json.RawMessage); ok {
		*raw = response.Result
		return nil
	}
	decoder := json.NewDecoder(strings.NewReader(string(response.Result)))
	decoder.UseNumber()
	return decoder.Decode(out)
}

type daemonRPCError struct {
	code    int
	message string
}

func (e *daemonRPCError) Error() string { return e.message }

// daemonLive reports whether a daemon answers on socket.
func daemonLive(socket string) bool {
	conn, err := net.DialTimeout("unix", socket, 200*time.Millisecond)
	if err != nil {
		return false
	}
	conn.Close()
	return true
}
