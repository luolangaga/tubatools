//go:build !linux && !darwin && !windows

package transport

import (
	"errors"
	"os/exec"
)

// daemonServeCommand is unused here; auto-start is not supported.
var daemonServeCommand = func(string) (*exec.Cmd, error) {
	return nil, errors.New("daemon auto-start is not supported on this platform; run computer-use serve")
}

// startDaemon is unsupported here; run computer-use serve explicitly.
func startDaemon(string) (*daemonClient, error) {
	return nil, errors.New("daemon auto-start is not supported on this platform; run computer-use serve")
}
