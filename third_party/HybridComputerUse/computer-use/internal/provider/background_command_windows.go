//go:build windows

package provider

import (
	"os/exec"
	"syscall"
)

func configureWindowsBackgroundCommand(command *exec.Cmd) {
	if command.SysProcAttr == nil {
		command.SysProcAttr = &syscall.SysProcAttr{}
	}
	// Redirected pipes do not prevent console allocation. The MCP launcher
	// may itself have no console, so each background child needs this flag.
	command.SysProcAttr.CreationFlags |= 0x08000000 // CREATE_NO_WINDOW
}
