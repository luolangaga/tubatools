//go:build windows

package provider

import (
	"fmt"
	"os"
	"os/exec"
	"strings"
	"syscall"
	"testing"
)

func TestWindowsBackgroundCommandHasNoConsole(t *testing.T) {
	command := exec.Command(os.Args[0], "-test.run=^TestWindowsBackgroundConsoleProbe$")
	command.Env = append(os.Environ(), "COMPUTER_USE_TEST_CONSOLE_PROBE=1")
	configureWindowsBackgroundCommand(command)
	output, err := command.CombinedOutput()
	if err != nil {
		t.Fatalf("background console probe: %v: %s", err, output)
	}
	if strings.TrimSpace(string(output)) != "0" {
		t.Fatalf("background process acquired a console: %s", output)
	}
}

func TestWindowsBackgroundConsoleProbe(t *testing.T) {
	if os.Getenv("COMPUTER_USE_TEST_CONSOLE_PROBE") != "1" {
		return
	}
	console, _, _ := syscall.NewLazyDLL("kernel32.dll").NewProc("GetConsoleWindow").Call()
	fmt.Println(console)
	os.Exit(0)
}

func TestWindowsBackgroundCommandPreservesProcessAttributes(t *testing.T) {
	command := exec.Command("unused")
	attributes := &syscall.SysProcAttr{HideWindow: true, CreationFlags: 0x00000200}
	command.SysProcAttr = attributes
	configureWindowsBackgroundCommand(command)
	if command.SysProcAttr != attributes || !attributes.HideWindow || attributes.CreationFlags != 0x08000200 {
		t.Fatalf("background command replaced process attributes: %+v", command.SysProcAttr)
	}
}
