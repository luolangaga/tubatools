//go:build !windows && windows_sidecar_test

package provider

import "os/exec"

func configureWindowsBackgroundCommand(command *exec.Cmd) {}
