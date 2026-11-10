//go:build darwin || darwin_sidecar_test

package provider

import (
	"bufio"
	"errors"
	"fmt"
	"os"
	"strconv"
	"strings"
	"syscall"
	"time"
)

func readDarwinProviderLine(
	reader *bufio.Reader,
	limit int,
) ([]byte, error) {
	if limit <= 0 {
		return nil, errDarwinProviderResponseTooLarge
	}
	line := make([]byte, 0, min(limit, 64*1024))
	for {
		fragment, err := reader.ReadSlice('\n')
		if len(fragment) > limit-len(line) {
			return nil, errDarwinProviderResponseTooLarge
		}
		line = append(line, fragment...)
		switch {
		case err == nil:
			return line, nil
		case errors.Is(err, bufio.ErrBufferFull):
			continue
		default:
			return line, err
		}
	}
}

func readDarwinProviderPIDUntil(
	path string,
	deadline time.Time,
) (int, error) {
	var lastErr error
	for {
		pid, err := readDarwinProviderPID(path)
		if err == nil {
			return pid, nil
		}
		lastErr = err
		if !time.Now().Before(deadline) {
			return 0, lastErr
		}
		time.Sleep(10 * time.Millisecond)
	}
}

func readDarwinProviderPID(path string) (int, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return 0, err
	}
	pid, err := strconv.Atoi(strings.TrimSpace(string(data)))
	if err != nil || pid <= 0 {
		return 0, fmt.Errorf(
			"invalid pid %q",
			strings.TrimSpace(string(data)),
		)
	}
	return pid, nil
}

func terminateDarwinProvider(pid int) {
	if pid <= 0 {
		return
	}
	process, err := os.FindProcess(pid)
	if err == nil {
		_ = process.Signal(syscall.SIGTERM)
	}
}

func darwinProviderProcessExists(pid int) bool {
	if pid <= 0 {
		return false
	}
	err := syscall.Kill(pid, 0)
	return err == nil || errors.Is(err, syscall.EPERM)
}

func waitForDarwinProviderExit(pid int, timeout time.Duration) bool {
	if pid <= 0 {
		return true
	}
	deadline := time.Now().Add(timeout)
	for darwinProviderProcessExists(pid) {
		if !time.Now().Before(deadline) {
			return false
		}
		time.Sleep(20 * time.Millisecond)
	}
	return true
}

func waitForDarwinProviderCommand(
	done <-chan struct{},
	timeout time.Duration,
) bool {
	if done == nil {
		return true
	}
	timer := time.NewTimer(timeout)
	defer timer.Stop()
	select {
	case <-done:
		return true
	case <-timer.C:
		return false
	}
}
