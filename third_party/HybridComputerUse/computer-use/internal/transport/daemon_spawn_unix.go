//go:build linux || darwin

package transport

import (
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"time"
)

// detachProcess puts the daemon in its own session, so it outlives the
// shell command that started it and receives no terminal signals.
func detachProcess(command *exec.Cmd) {
	command.SysProcAttr = &syscall.SysProcAttr{Setsid: true}
}

// lockFile takes an exclusive lock on path and returns its release.
func lockFile(path string) (func(), error) {
	file, err := os.OpenFile(path, os.O_CREATE|os.O_RDWR, 0o600)
	if err != nil {
		return nil, err
	}
	if err := syscall.Flock(int(file.Fd()), syscall.LOCK_EX); err != nil {
		file.Close()
		return nil, err
	}
	return func() {
		_ = syscall.Flock(int(file.Fd()), syscall.LOCK_UN)
		file.Close()
	}, nil
}

// daemonServeCommand is the command that runs the daemon: this executable
// with serve. Tests replace it with a helper process.
var daemonServeCommand = func(socket string) (*exec.Cmd, error) {
	executable, err := os.Executable()
	if err != nil {
		return nil, err
	}
	command := exec.Command(executable, "serve", "--socket", socket)
	if idle := strings.TrimSpace(os.Getenv("HCU_IDLE_TIMEOUT")); idle != "" {
		command.Args = append(command.Args, "--idle-timeout", idle)
	}
	return command, nil
}

// startDaemon starts `computer-use serve` in the background and waits for
// its socket. A lock file beside the socket makes concurrent first calls
// start one daemon: the others wait on the lock and then connect.
func startDaemon(socket string) (*daemonClient, error) {
	if err := os.MkdirAll(filepath.Dir(socket), 0o700); err != nil {
		return nil, err
	}
	unlock, err := lockFile(socket + ".lock")
	if err != nil {
		return nil, err
	}
	defer unlock()
	if client, err := dialDaemon(socket); err == nil {
		return client, nil
	}
	command, err := daemonServeCommand(socket)
	if err != nil {
		return nil, err
	}
	logPath := socket + ".log"
	logFile, err := os.OpenFile(logPath, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0o600)
	if err != nil {
		return nil, err
	}
	defer logFile.Close()
	command.Stdout = logFile
	command.Stderr = logFile
	detachProcess(command)
	if err := command.Start(); err != nil {
		return nil, err
	}
	exited := make(chan error, 1)
	go func() { exited <- command.Wait() }()
	deadline := time.Now().Add(15 * time.Second)
	for time.Now().Before(deadline) {
		if client, err := dialDaemon(socket); err == nil {
			return client, nil
		}
		select {
		case err := <-exited:
			return nil, fmt.Errorf("daemon exited during start (%v); see %s", err, logPath)
		case <-time.After(20 * time.Millisecond):
		}
	}
	return nil, fmt.Errorf("daemon did not open %s within 15s; see %s", socket, logPath)
}
