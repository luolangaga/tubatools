//go:build linux

package provider

import (
	"bufio"
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

const maxLinuxProviderResponseBytes = 64 << 20
const maxLinuxProviderStderrLineBytes = 64 << 10
const linuxProviderShutdownGrace = 250 * time.Millisecond
const linuxProviderKillWait = 250 * time.Millisecond

var errLinuxProviderResponseTooLarge = errors.New(
	"Linux provider response exceeded the configured limit",
)

var (
	linuxProviderRequestSequence atomic.Uint64
	linuxProviderProcesses       linuxProviderProcessManager
)

type linuxProviderProcessManager struct {
	mu             sync.Mutex
	worker         *linuxProviderWorker
	environmentKey string
}

type linuxProviderWorker struct {
	cmd        *exec.Cmd
	stdin      io.WriteCloser
	stdout     *bufio.Reader
	tempDir    string
	done       chan struct{}
	stderrDone chan struct{}
	stopOnce   sync.Once
	waitErr    error
}

type linuxProviderReadResult struct {
	line []byte
	err  error
}

type linuxProviderWriteResult struct {
	written int
	err     error
}

func nextLinuxProviderRequestID() string {
	return fmt.Sprintf(
		"linux-%d-%d",
		time.Now().UnixNano(),
		linuxProviderRequestSequence.Add(1),
	)
}

func linuxProviderRuntimeTimeout() time.Duration {
	timeout := 30 * time.Second
	if raw := os.Getenv("COMPUTER_USE_RUNTIME_TIMEOUT_SECONDS"); raw != "" {
		if seconds, err := strconv.Atoi(raw); err == nil && seconds > 0 {
			timeout = time.Duration(seconds) * time.Second
		}
	}
	return timeout
}

func linuxProviderPythonBinary() string {
	if value := strings.TrimSpace(
		os.Getenv("COMPUTER_USE_PYTHON_BINARY"),
	); value != "" {
		return value
	}
	return "python3"
}

func runLinuxProvider(
	request core.ProviderWireRequest,
	host core.HostBackend,
) (*core.ProviderWireResponse, error) {
	if err := request.Validate(); err != nil {
		return nil, err
	}
	return linuxProviderProcesses.run(request, host)
}

func (manager *linuxProviderProcessManager) run(
	request core.ProviderWireRequest,
	host core.HostBackend,
) (*core.ProviderWireResponse, error) {
	manager.mu.Lock()
	defer manager.mu.Unlock()

	environment := host.RuntimeEnvironment(os.Environ())
	environmentKey := linuxProviderPythonBinary() + "\x00" +
		strings.Join(environment, "\x00")
	if manager.worker == nil || manager.environmentKey != environmentKey {
		manager.stopLocked()
		worker, err := startLinuxProviderWorker(environment)
		if err != nil {
			return nil, err
		}
		manager.worker = worker
		manager.environmentKey = environmentKey
	}

	response, err := manager.worker.call(request, linuxProviderRuntimeTimeout())
	if err != nil {
		manager.stopLocked()
		return nil, err
	}
	if response.ProviderResetRequired {
		manager.stopLocked()
		if linuxProviderRequestCanRetryAfterReset(request, response) {
			worker, startErr := startLinuxProviderWorker(environment)
			if startErr != nil {
				return nil, startErr
			}
			manager.worker = worker
			manager.environmentKey = environmentKey
			response, err = manager.worker.call(
				request,
				linuxProviderRuntimeTimeout(),
			)
			if err != nil {
				manager.stopLocked()
				return nil, err
			}
			if response.ProviderResetRequired {
				manager.stopLocked()
			}
		}
	}
	return response, nil
}

func linuxProviderRequestCanRetryAfterReset(
	request core.ProviderWireRequest,
	response *core.ProviderWireResponse,
) bool {
	if response == nil || providerWireResponseMayHaveMutated(response) {
		return false
	}
	switch request.Operation {
	case core.ProviderWireObserveApps, core.ProviderWireObserve, core.ProviderWireWindowList:
		return true
	case core.ProviderWireResolveAndAct:
		return request.Action != nil &&
			request.Action.Intent == core.AccessibilityResolveTarget
	default:
		return false
	}
}

func providerWireResponseMayHaveMutated(response *core.ProviderWireResponse) bool {
	if response == nil {
		return false
	}
	var mutation *core.ProviderMutationFacts
	var actionEvidence *core.ProviderActionEvidence
	switch {
	case response.Accessibility != nil:
		mutation = response.Accessibility.Mutation
		actionEvidence = response.Accessibility.ActionEvidence
	case response.Desktop != nil:
		mutation = response.Desktop.Mutation
	case response.Window != nil:
		mutation = response.Window.Mutation
		actionEvidence = response.Window.ActionEvidence
	}
	return core.ProviderMutationCanInvalidate(mutation) ||
		(actionEvidence != nil && actionEvidence.InputEmitted)
}

func (manager *linuxProviderProcessManager) stopLocked() {
	if manager.worker != nil {
		manager.worker.stop()
	}
	manager.worker = nil
	manager.environmentKey = ""
}

func shutdownLinuxProviderProcess() {
	linuxProviderProcesses.mu.Lock()
	defer linuxProviderProcesses.mu.Unlock()
	linuxProviderProcesses.stopLocked()
}

func startLinuxProviderWorker(
	environment []string,
) (*linuxProviderWorker, error) {
	tempDir, err := os.MkdirTemp("", "computer-use-linux-*")
	if err != nil {
		return nil, err
	}
	cleanup := true
	defer func() {
		if cleanup {
			_ = os.RemoveAll(tempDir)
		}
	}()

	scriptPath := filepath.Join(tempDir, "runtime.py")
	for relativePath, contents := range linuxRuntimeBundle() {
		path := filepath.Join(tempDir, filepath.FromSlash(relativePath))
		if err := os.MkdirAll(filepath.Dir(path), 0o700); err != nil {
			return nil, err
		}
		if err := os.WriteFile(path, []byte(contents), 0o600); err != nil {
			return nil, err
		}
	}

	cmd := exec.Command(linuxProviderPythonBinary(), scriptPath, "--stdio")
	cmd.Env = environment
	stdin, err := cmd.StdinPipe()
	if err != nil {
		return nil, err
	}
	stdout, err := cmd.StdoutPipe()
	if err != nil {
		return nil, err
	}
	stderr, err := cmd.StderrPipe()
	if err != nil {
		return nil, err
	}
	if err := cmd.Start(); err != nil {
		return nil, err
	}

	worker := &linuxProviderWorker{
		cmd:        cmd,
		stdin:      stdin,
		stdout:     bufio.NewReader(stdout),
		tempDir:    tempDir,
		done:       make(chan struct{}),
		stderrDone: make(chan struct{}),
	}
	cleanup = false
	go worker.forwardStderr(stderr)
	go worker.wait()
	return worker, nil
}

func (worker *linuxProviderWorker) wait() {
	if worker.cmd != nil {
		worker.waitErr = worker.cmd.Wait()
	}
	close(worker.done)
}

func (worker *linuxProviderWorker) forwardStderr(stderr io.Reader) {
	defer close(worker.stderrDone)
	reader := bufio.NewReaderSize(stderr, 4*1024)
	for {
		line, originalBytes, truncated, err :=
			readProviderDiagnosticLine(
				reader,
				maxLinuxProviderStderrLineBytes,
			)
		if len(line) > 0 || originalBytes > 0 {
			if truncated {
				writeLinuxProviderDiagnostic(map[string]any{
					"request_id":                "unknown",
					"operation":                 "unknown",
					"stage":                     "process_stderr",
					"diagnostic":                string(line),
					"diagnostic_original_bytes": originalBytes,
					"diagnostic_truncated":      true,
				})
			} else {
				writeLinuxProviderProcessDiagnostic(string(line))
			}
		}
		if err != nil {
			if !errors.Is(err, io.EOF) {
				writeLinuxProviderProcessDiagnostic(
					fmt.Sprintf("provider stderr stream failed: %v", err),
				)
			}
			return
		}
	}
}

func (worker *linuxProviderWorker) call(
	request core.ProviderWireRequest,
	timeout time.Duration,
) (*core.ProviderWireResponse, error) {
	payload, err := json.Marshal(request)
	if err != nil {
		return nil, err
	}
	payload = append(payload, '\n')

	timer := time.NewTimer(timeout)
	defer timer.Stop()

	writeChannel := make(chan linuxProviderWriteResult, 1)
	go func() {
		written, writeErr := worker.stdin.Write(payload)
		writeChannel <- linuxProviderWriteResult{
			written: written,
			err:     writeErr,
		}
	}()

	select {
	case writeResult := <-writeChannel:
		if writeResult.err != nil || writeResult.written != len(payload) {
			if writeResult.err == nil {
				writeResult.err = io.ErrShortWrite
			}
			writeLinuxRuntimeDiagnostic(
				request,
				"write_request",
				writeResult.err.Error(),
			)
			return nil, &core.RuntimeTransportError{
				Operation:   request.Operation,
				Phase:       "write_request",
				RequestSent: writeResult.written > 0,
			}
		}
	case <-timer.C:
		writeLinuxRuntimeDiagnostic(
			request,
			"timeout",
			"request/response deadline exceeded while writing request",
		)
		return nil, &core.RuntimeTimeoutError{
			Duration: timeout,
			Phase:    "write_request",
		}
	}

	resultChannel := make(chan linuxProviderReadResult, 1)
	go func() {
		line, readErr := readLinuxProviderLine(
			worker.stdout,
			maxLinuxProviderResponseBytes,
		)
		resultChannel <- linuxProviderReadResult{line: line, err: readErr}
	}()

	var result linuxProviderReadResult
	select {
	case result = <-resultChannel:
	case <-timer.C:
		writeLinuxRuntimeDiagnostic(
			request,
			"timeout",
			"request/response deadline exceeded while reading response",
		)
		return nil, &core.RuntimeTimeoutError{
			Duration: timeout,
			Phase:    "read_response",
		}
	}
	if errors.Is(result.err, errLinuxProviderResponseTooLarge) {
		writeLinuxRuntimeDiagnostic(
			request,
			"response_too_large",
			fmt.Sprintf(
				"response exceeded %d bytes",
				maxLinuxProviderResponseBytes,
			),
		)
		return nil, &core.RuntimeTransportError{
			Operation:   request.Operation,
			Phase:       "read_response",
			RequestSent: true,
		}
	}
	if result.err != nil {
		writeLinuxRuntimeDiagnostic(request, "read_response", result.err.Error())
		return nil, &core.RuntimeTransportError{
			Operation:   request.Operation,
			Phase:       "read_response",
			RequestSent: true,
		}
	}

	response, err := core.DecodeProviderWireResponse(result.line)
	if err != nil {
		writeLinuxRuntimeDiagnosticBytes(
			request,
			"decode_response",
			bytes.TrimSpace(result.line),
		)
		return nil, &core.RuntimeTransportError{
			Operation:   request.Operation,
			Phase:       "decode_response",
			RequestSent: true,
		}
	}
	if response.RequestID != request.RequestID {
		writeLinuxRuntimeDiagnostic(
			request,
			"request_id_mismatch",
			fmt.Sprintf(
				"received request_id=%q",
				linuxDiagnosticLabel(response.RequestID),
			),
		)
		return nil, &core.RuntimeTransportError{
			Operation:   request.Operation,
			Phase:       "request_id_mismatch",
			RequestSent: true,
		}
	}
	if response.Operation != request.Operation {
		writeLinuxRuntimeDiagnostic(
			request,
			"operation_mismatch",
			fmt.Sprintf(
				"received operation=%q",
				linuxDiagnosticLabel(response.Operation),
			),
		)
		return nil, &core.RuntimeTransportError{
			Operation:   request.Operation,
			Phase:       "operation_mismatch",
			RequestSent: true,
		}
	}
	return &response, nil
}

func readLinuxProviderLine(
	reader *bufio.Reader,
	limit int,
) ([]byte, error) {
	if limit <= 0 {
		return nil, errLinuxProviderResponseTooLarge
	}
	line := make([]byte, 0, min(limit, 64*1024))
	for {
		fragment, err := reader.ReadSlice('\n')
		if len(fragment) > limit-len(line) {
			return nil, errLinuxProviderResponseTooLarge
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

func (worker *linuxProviderWorker) stop() {
	if worker == nil {
		return
	}
	worker.stopOnce.Do(func() {
		_ = worker.stdin.Close()
		timer := time.NewTimer(linuxProviderShutdownGrace)
		defer timer.Stop()
		stopped := false
		select {
		case <-worker.done:
			stopped = true
		case <-timer.C:
			if worker.cmd != nil && worker.cmd.Process != nil {
				_ = worker.cmd.Process.Kill()
			}
			killTimer := time.NewTimer(linuxProviderKillWait)
			select {
			case <-worker.done:
				stopped = true
			case <-killTimer.C:
				writeLinuxProviderDiagnostic(map[string]any{
					"request_id": "shutdown",
					"operation":  "provider_worker",
					"stage":      "kill_wait_timeout",
					"diagnostic": "provider worker did not exit after forced termination",
				})
			}
			if !killTimer.Stop() {
				select {
				case <-killTimer.C:
				default:
				}
			}
		}
		if stopped {
			select {
			case <-worker.stderrDone:
			case <-time.After(linuxProviderShutdownGrace):
			}
		}
		if stopped && worker.tempDir != "" {
			_ = os.RemoveAll(worker.tempDir)
		}
	})
}
