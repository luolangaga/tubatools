//go:build windows || windows_sidecar_test

package provider

import (
	"bufio"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"sync/atomic"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

func startWindowsProviderWorker(
	path string,
	environment []string,
	trace *windowsProviderTrace,
) (*windowsProviderWorker, error) {
	cmd := windowsProviderCommand(path)
	configureWindowsBackgroundCommand(cmd)
	cmd.Env = environment
	stdin, err := cmd.StdinPipe()
	if err != nil {
		return nil, err
	}
	stdout, err := cmd.StdoutPipe()
	if err != nil {
		_ = stdin.Close()
		return nil, err
	}
	stderr, err := cmd.StderrPipe()
	if err != nil {
		_ = stdin.Close()
		return nil, err
	}
	trace.record("cmd_start_begin", nil, nil)
	startErr := cmd.Start()
	trace.record("cmd_start_end", nil, startErr)
	if err := startErr; err != nil {
		_ = stdin.Close()
		return nil, fmt.Errorf("start Windows provider: %w", err)
	}
	parentDeathBinding, err := windowsProviderParentDeathBinder(cmd)
	trace.record("parent_binding_end", nil, err)
	if err != nil {
		_ = stdin.Close()
		_ = cmd.Process.Kill()
		_ = cmd.Wait()
		return nil, fmt.Errorf(
			"bind Windows provider lifetime to MCP process: %w",
			err,
		)
	}
	worker := &windowsProviderWorker{
		cmd:                cmd,
		stdin:              stdin,
		stdout:             bufio.NewReader(stdout),
		stderr:             &boundedProviderBuffer{limit: maxWindowsProviderStderrBytes},
		done:               make(chan struct{}),
		stderrDone:         make(chan struct{}),
		parentDeathBinding: parentDeathBinding,
	}
	go worker.forwardStderr(stderr)
	go worker.wait()
	return worker, nil
}

func (worker *windowsProviderWorker) forwardStderr(stderr io.Reader) {
	defer close(worker.stderrDone)
	reader := bufio.NewReaderSize(io.TeeReader(stderr, worker.stderr), 4*1024)
	for {
		line, originalBytes, truncated, err :=
			readProviderDiagnosticLine(
				reader,
				maxWindowsProviderStderrLineBytes,
			)
		if len(line) > 0 || originalBytes > 0 {
			writeWindowsProviderProcessDiagnostic(
				string(line),
				originalBytes,
				truncated,
			)
		}
		if err != nil {
			if !errors.Is(err, io.EOF) {
				writeWindowsProviderProcessDiagnostic(
					fmt.Sprintf("provider stderr stream failed: %v", err),
					0,
					false,
				)
			}
			return
		}
	}
}

func writeWindowsProviderProcessDiagnostic(
	diagnostic string,
	originalBytes int,
	truncated bool,
) {
	payload, err := json.Marshal(map[string]any{
		"event":                     "windows_provider_stderr",
		"stage":                     "process_stderr",
		"diagnostic":                diagnostic,
		"diagnostic_original_bytes": originalBytes,
		"diagnostic_truncated":      truncated,
	})
	if err != nil {
		return
	}
	windowsProviderDiagnostics.write(append(payload, '\n'))
}

func writeWindowsProviderLifecycleDiagnostic(
	stage string,
	timeout time.Duration,
	diagnostic string,
) {
	payload, err := json.Marshal(map[string]any{
		"event":           "windows_provider_lifecycle",
		"stage":           stage,
		"timeout_ms":      timeout.Milliseconds(),
		"diagnostic":      diagnostic,
		"bounded_failure": true,
	})
	if err != nil {
		return
	}
	windowsProviderDiagnostics.write(append(payload, '\n'))
}

type windowsProviderDiagnostic struct {
	writer io.Writer
	data   []byte
}

type windowsProviderDiagnosticQueue struct {
	queue   chan windowsProviderDiagnostic
	dropped atomic.Uint64
}

func newWindowsProviderDiagnosticQueue() *windowsProviderDiagnosticQueue {
	sink := &windowsProviderDiagnosticQueue{
		queue: make(chan windowsProviderDiagnostic, 8),
	}
	go func() {
		for diagnostic := range sink.queue {
			_, _ = diagnostic.writer.Write(diagnostic.data)
			if dropped := sink.dropped.Swap(0); dropped > 0 {
				_, _ = fmt.Fprintf(diagnostic.writer,
					"{\"event\":\"windows_provider_diagnostics_dropped\",\"count\":%d}\n", dropped)
			}
		}
	}()
	return sink
}

func (sink *windowsProviderDiagnosticQueue) write(data []byte) {
	// A client may stop consuming stderr. Diagnostic backpressure must not
	// prevent request deadlines, provider pipe draining, or process teardown.
	select {
	case sink.queue <- windowsProviderDiagnostic{windowsProviderDiagnosticWriter, data}:
	default:
		sink.dropped.Add(1)
	}
}

func (worker *windowsProviderWorker) wait() {
	if worker.cmd != nil {
		worker.waitErr = worker.cmd.Wait()
	}
	close(worker.done)
	if !worker.closeParentDeathBindingWithin(windowsProviderShutdownGrace) {
		writeWindowsProviderLifecycleDiagnostic(
			"close_parent_binding",
			windowsProviderShutdownGrace,
			"provider exit could not close parent-death binding before deadline",
		)
	}
}

func (worker *windowsProviderWorker) closeParentDeathBinding() {
	worker.bindingCloseOnce.Do(func() {
		if worker.parentDeathBinding != nil {
			_ = worker.parentDeathBinding.Close()
		}
	})
}

func (worker *windowsProviderWorker) closeParentDeathBindingWithin(
	timeout time.Duration,
) bool {
	if worker == nil || worker.parentDeathBinding == nil {
		return true
	}
	done := make(chan struct{})
	go func() {
		worker.closeParentDeathBinding()
		close(done)
	}()
	timer := time.NewTimer(timeout)
	defer timer.Stop()
	select {
	case <-done:
		return true
	case <-timer.C:
		return false
	}
}

func (worker *windowsProviderWorker) closeInputWithin(
	timeout time.Duration,
) bool {
	if worker == nil || worker.stdin == nil {
		return true
	}
	done := make(chan struct{})
	go func() {
		_ = worker.stdin.Close()
		close(done)
	}()
	timer := time.NewTimer(timeout)
	defer timer.Stop()
	select {
	case <-done:
		return true
	case <-timer.C:
		return false
	}
}

func (worker *windowsProviderWorker) killProcessWithin(
	timeout time.Duration,
) bool {
	if worker == nil || worker.cmd == nil || worker.cmd.Process == nil {
		return true
	}
	done := make(chan struct{})
	go func() {
		_ = worker.cmd.Process.Kill()
		close(done)
	}()
	timer := time.NewTimer(timeout)
	defer timer.Stop()
	select {
	case <-done:
		return true
	case <-timer.C:
		return false
	}
}

func (worker *windowsProviderWorker) call(
	request core.ProviderWireRequest,
	timeout time.Duration,
	trace *windowsProviderTrace,
) (*core.ProviderWireResponse, error) {
	payload, err := json.Marshal(request)
	if err != nil {
		return nil, err
	}
	payload = append(payload, '\n')

	timer := time.NewTimer(timeout)
	defer timer.Stop()
	trace = trace.forCall(timeout)
	trace.record("request_write_begin", nil, nil)

	writeChannel := make(chan windowsProviderWriteResult, 1)
	go func() {
		written, writeErr := worker.stdin.Write(payload)
		writeChannel <- windowsProviderWriteResult{
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
			failure := fmt.Errorf("write provider request: %w", writeResult.err)
			if writeResult.written > 0 {
				return nil, &core.RuntimeTransportError{
					Platform:    "Windows",
					Operation:   request.Operation,
					Phase:       "write_request",
					RequestSent: true,
					Cause:       failure,
				}
			}
			return nil, failure
		}
	case <-timer.C:
		return nil, &core.RuntimeTimeoutError{
			Duration: timeout,
			Platform: "Windows",
			Phase:    "write_request",
		}
	}
	trace.record("request_sent", nil, nil)

	readChannel := make(chan windowsProviderReadResult, 1)
	go func() {
		line, readErr := readWindowsProviderLine(
			worker.stdout,
			maxWindowsProviderResponseBytes,
		)
		readChannel <- windowsProviderReadResult{line: line, err: readErr}
	}()

	var result windowsProviderReadResult
	select {
	case result = <-readChannel:
		trace.record("response_received", nil, result.err)
	case <-timer.C:
		return nil, &core.RuntimeTimeoutError{
			Duration: timeout,
			Platform: "Windows",
			Phase:    "read_response",
		}
	}
	if errors.Is(result.err, errWindowsProviderResponseTooLarge) {
		return nil, &core.RuntimeTransportError{
			Platform:    "Windows",
			Operation:   request.Operation,
			Phase:       "read_response",
			RequestSent: true,
			Cause:       result.err,
		}
	}
	if result.err != nil {
		return nil, &core.RuntimeTransportError{
			Platform:    "Windows",
			Operation:   request.Operation,
			Phase:       "read_response",
			RequestSent: true,
			Cause:       result.err,
		}
	}

	response, err := core.DecodeProviderWireResponse(result.line)
	if err != nil {
		return nil, &core.RuntimeTransportError{
			Platform:    "Windows",
			Operation:   request.Operation,
			Phase:       "decode_response",
			RequestSent: true,
			Cause:       err,
		}
	}
	if response.RequestID != request.RequestID {
		return nil, &core.RuntimeTransportError{
			Platform:    "Windows",
			Operation:   request.Operation,
			Phase:       "request_id_mismatch",
			RequestSent: true,
			Cause: errors.New(
				"provider returned a mismatched request_id",
			),
		}
	}
	if response.Operation != request.Operation {
		return nil, &core.RuntimeTransportError{
			Platform:    "Windows",
			Operation:   request.Operation,
			Phase:       "operation_mismatch",
			RequestSent: true,
			Cause: errors.New(
				"provider returned a mismatched operation",
			),
		}
	}
	if response.RequestRejection != nil {
		return nil, &core.RuntimeRequestRejectionError{
			Operation: request.Operation,
			Rejection: *response.RequestRejection,
		}
	}
	return &response, nil
}

func readWindowsProviderLine(
	reader *bufio.Reader,
	limit int,
) ([]byte, error) {
	if limit <= 0 {
		return nil, errWindowsProviderResponseTooLarge
	}
	line := make([]byte, 0, min(limit, 64*1024))
	for {
		fragment, err := reader.ReadSlice('\n')
		if len(fragment) > limit-len(line) {
			return nil, errWindowsProviderResponseTooLarge
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

func (worker *windowsProviderWorker) stop() {
	if worker == nil {
		return
	}
	worker.stopOnce.Do(func() {
		if !worker.closeInputWithin(windowsProviderShutdownGrace) {
			writeWindowsProviderLifecycleDiagnostic(
				"close_stdin",
				windowsProviderShutdownGrace,
				"provider stdin close deadline exceeded",
			)
		}
		timer := time.NewTimer(windowsProviderShutdownGrace)
		defer timer.Stop()
		stopped := false
		select {
		case <-worker.done:
			stopped = true
		case <-timer.C:
			if !worker.killProcessWithin(windowsProviderKillWait) {
				writeWindowsProviderLifecycleDiagnostic(
					"kill_process",
					windowsProviderKillWait,
					"provider process termination deadline exceeded",
				)
			}
			killTimer := time.NewTimer(windowsProviderKillWait)
			select {
			case <-worker.done:
				stopped = true
			case <-killTimer.C:
				if !worker.closeParentDeathBindingWithin(
					windowsProviderShutdownGrace,
				) {
					writeWindowsProviderLifecycleDiagnostic(
						"close_parent_binding",
						windowsProviderShutdownGrace,
						"provider parent-death binding close deadline exceeded",
					)
				}
			}
			if !killTimer.Stop() {
				select {
				case <-killTimer.C:
				default:
				}
			}
		}
		if stopped && worker.stderrDone != nil {
			select {
			case <-worker.stderrDone:
			case <-time.After(windowsProviderShutdownGrace):
			}
		}
	})
}
