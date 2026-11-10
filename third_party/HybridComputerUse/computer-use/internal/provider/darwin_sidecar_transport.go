//go:build darwin || darwin_sidecar_test

package provider

import (
	"bufio"
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

const (
	maxDarwinProviderResponseBytes = 64 << 20
	maxDarwinProviderStderrBytes   = 64 << 10
	darwinProviderShutdownGrace    = 2 * time.Second
	darwinProviderStartupLimit     = 10 * time.Second
	darwinProviderBaseTimeout      = 30 * time.Second
	// Native drag pacing takes 1.458s; this also covers paste consumption,
	// key delivery and click-to-keyboard barriers. Explicit waits are separate.
	darwinProviderDesktopActionBudget = 1500 * time.Millisecond
	darwinProviderLaunchPIDGrace      = 2 * time.Second
	darwinProviderAppName             = "ComputerUseProvider.app"
	darwinProviderExecutableName      = "computer-use-macos-provider"
	darwinProviderLibraryName         = "libComputerUseProviderKit.dylib"
)

var errDarwinProviderResponseTooLarge = errors.New(
	"macOS provider response exceeded the configured limit",
)

var (
	darwinProviderRequestSequence atomic.Uint64
	darwinProviderProcesses       darwinProviderProcessManager
	darwinProviderArtifactLookup  = darwinProviderExecutable
	darwinProviderWorkerStarter   = startDarwinProviderWorker
	darwinProviderRetrySleep      = time.Sleep
	darwinProviderOpenCommand     = func(bundlePath, socketPath, pidPath string) *exec.Cmd {
		return exec.Command(
			"/usr/bin/open",
			"-n",
			bundlePath,
			"--args",
			"--socket",
			socketPath,
			"--pid-file",
			pidPath,
		)
	}
)

type darwinProviderProcessManager struct {
	mu          sync.Mutex
	worker      *darwinProviderWorker
	artifactKey string
}

type darwinProviderWorker struct {
	cmd         *exec.Cmd
	connection  io.ReadWriteCloser
	stdout      *bufio.Reader
	stderr      *boundedProviderBuffer
	done        chan struct{}
	stderrDone  chan struct{}
	providerPID int
	runtimeDir  string
	stopOnce    sync.Once
	waitErr     error
}

type darwinProviderReadResult struct {
	line []byte
	err  error
}

type darwinProviderWriteResult struct {
	written int
	err     error
}

func nextDarwinProviderRequestID() string {
	return fmt.Sprintf(
		"darwin-%d-%d",
		time.Now().UnixNano(),
		darwinProviderRequestSequence.Add(1),
	)
}

func darwinProviderRuntimeTimeout() time.Duration {
	return darwinProviderRequestTimeout(core.ProviderWireRequest{})
}

func darwinProviderRequestTimeout(request core.ProviderWireRequest) time.Duration {
	if raw := strings.TrimSpace(
		os.Getenv("COMPUTER_USE_RUNTIME_TIMEOUT_SECONDS"),
	); raw != "" {
		if seconds, err := strconv.ParseInt(raw, 10, 64); err == nil &&
			seconds > 0 && seconds <= int64((1<<63-1)/time.Second) {
			// An explicit timeout remains the complete request deadline.
			return time.Duration(seconds) * time.Second
		}
	}
	timeout := darwinProviderBaseTimeout
	if request.Operation != core.ProviderWireDesktopBatch || request.Desktop == nil {
		return timeout
	}
	// Bound budgeting even for invalid requests; request validation owns rejection.
	actions := request.Desktop.Actions[:min(len(request.Desktop.Actions), contract.GUIMaxActions)]
	timeout += time.Duration(len(actions)) * darwinProviderDesktopActionBudget
	waits := 0
	for _, action := range actions {
		if action.Type == "wait" {
			waits++
		}
	}
	timeout += time.Duration(min(waits, contract.GUIMaxTotalWait)) * time.Second
	timeout += time.Duration(max(0, min(request.Desktop.SettleMS, contract.GUIMaxSettleMS))) * time.Millisecond
	return timeout
}

func runDarwinProvider(
	request core.ProviderWireRequest,
) (*core.ProviderWireResponse, error) {
	if err := request.Validate(); err != nil {
		return nil, err
	}
	if request.Action != nil {
		action := *request.Action
		action.Diagnostics = core.ProviderDiagnosticContextFromLookup(os.Getenv)
		request.Action = &action
	}
	return darwinProviderProcesses.run(request)
}

func (manager *darwinProviderProcessManager) run(
	request core.ProviderWireRequest,
) (*core.ProviderWireResponse, error) {
	manager.mu.Lock()
	defer manager.mu.Unlock()

	artifact, err := darwinProviderArtifactLookup()
	if err != nil {
		return nil, err
	}
	artifactKey := strings.Join([]string{
		artifact.bundlePath,
		artifact.executablePath,
		artifact.libraryPath,
		artifact.infoPlistPath,
	}, "\x00")
	if manager.worker == nil || manager.artifactKey != artifactKey {
		manager.stopLocked()
		worker, startErr := startDarwinProviderWorkerWithRetry(artifact)
		if startErr != nil {
			return nil, startErr
		}
		manager.worker = worker
		manager.artifactKey = artifactKey
	}

	response, err := manager.worker.call(
		request,
		darwinProviderRequestTimeout(request),
	)
	if err != nil {
		var rejection *core.RuntimeRequestRejectionError
		if errors.As(err, &rejection) {
			return nil, err
		}
		manager.recordStderrLocked(request, err)
		manager.stopLocked()
		return nil, err
	}
	if response.ProviderResetRequired {
		manager.stopLocked()
		if darwinProviderRequestCanRetryAfterReset(request, response) {
			worker, startErr := startDarwinProviderWorkerWithRetry(artifact)
			if startErr != nil {
				return nil, startErr
			}
			manager.worker = worker
			manager.artifactKey = artifactKey
			response, err = manager.worker.call(
				request,
				darwinProviderRequestTimeout(request),
			)
			if err != nil {
				var rejection *core.RuntimeRequestRejectionError
				if errors.As(err, &rejection) {
					return nil, err
				}
				manager.recordStderrLocked(request, err)
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

func darwinProviderRequestCanRetryAfterReset(
	request core.ProviderWireRequest,
	response *core.ProviderWireResponse,
) bool {
	if response == nil || darwinProviderResponseMayHaveMutated(response) {
		return false
	}
	switch request.Operation {
	case core.ProviderWireObserveApps, core.ProviderWireObserve, core.ProviderWireWindowList:
		return true
	case core.ProviderWireResolveAndAct:
		return request.Action != nil &&
			request.Action.Intent == core.AccessibilityResolveTarget
	case core.ProviderWireOrderedInput:
		return response.OrderedInput != nil &&
			response.OrderedInput.RetrySameRequest
	default:
		return false
	}
}

func darwinProviderResponseMayHaveMutated(
	response *core.ProviderWireResponse,
) bool {
	if response == nil {
		return false
	}
	var mutation *core.ProviderMutationFacts
	switch {
	case response.Accessibility != nil:
		mutation = response.Accessibility.Mutation
	case response.OrderedInput != nil:
		value := response.OrderedInput.Mutation
		mutation = &value
	case response.Desktop != nil:
		mutation = response.Desktop.Mutation
	case response.Window != nil:
		mutation = response.Window.Mutation
	}
	return core.ProviderMutationCanInvalidate(mutation)
}

func (manager *darwinProviderProcessManager) recordStderrLocked(
	request core.ProviderWireRequest,
	callErr error,
) {
	if manager.worker == nil || manager.worker.stderr == nil {
		return
	}
	data, truncated := manager.worker.stderr.snapshot()
	data = bytes.TrimSpace(data)
	if len(data) == 0 {
		return
	}
	fmt.Fprintf(
		os.Stderr,
		"[computer-use macos-provider request=%s operation=%s "+
			"stderr_bytes=%d stderr_truncated=%t "+
			"transport_error=%q]\n%s\n",
		boundedProviderLabel(request.RequestID),
		boundedProviderLabel(string(request.Operation)),
		len(data),
		truncated,
		callErr.Error(),
		data,
	)
}

func (manager *darwinProviderProcessManager) stopLocked() {
	if manager.worker != nil {
		manager.worker.stop()
	}
	manager.worker = nil
	manager.artifactKey = ""
}

func shutdownDarwinProviderProcess() {
	darwinProviderProcesses.mu.Lock()
	defer darwinProviderProcesses.mu.Unlock()
	darwinProviderProcesses.stopLocked()
}

func (worker *darwinProviderWorker) forwardStderr(stderr io.Reader) {
	defer close(worker.stderrDone)
	_, _ = io.Copy(worker.stderr, stderr)
}

func (worker *darwinProviderWorker) wait() {
	if worker.cmd != nil {
		worker.waitErr = worker.cmd.Wait()
	}
	close(worker.done)
}

func (worker *darwinProviderWorker) stderrText() string {
	if worker == nil || worker.stderr == nil {
		return ""
	}
	data, _ := worker.stderr.snapshot()
	return string(data)
}

func (worker *darwinProviderWorker) call(
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

	writeChannel := make(chan darwinProviderWriteResult, 1)
	go func() {
		written, writeErr := worker.connection.Write(payload)
		writeChannel <- darwinProviderWriteResult{
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
			return nil, &core.RuntimeTransportError{
				Platform:    "macOS",
				Operation:   request.Operation,
				Phase:       "write_request",
				RequestSent: writeResult.written > 0,
				Cause:       writeResult.err,
			}
		}
	case <-timer.C:
		return nil, &core.RuntimeTimeoutError{
			Duration: timeout,
			Phase:    "write_request",
			Platform: "macOS",
		}
	}

	readChannel := make(chan darwinProviderReadResult, 1)
	go func() {
		line, readErr := readDarwinProviderLine(
			worker.stdout,
			maxDarwinProviderResponseBytes,
		)
		readChannel <- darwinProviderReadResult{line: line, err: readErr}
	}()

	var result darwinProviderReadResult
	select {
	case result = <-readChannel:
	case <-timer.C:
		return nil, &core.RuntimeTimeoutError{
			Duration: timeout,
			Phase:    "read_response",
			Platform: "macOS",
		}
	}
	if result.err != nil {
		return nil, &core.RuntimeTransportError{
			Platform:    "macOS",
			Operation:   request.Operation,
			Phase:       "read_response",
			RequestSent: true,
			Cause:       result.err,
		}
	}
	response, err := core.DecodeProviderWireResponse(result.line)
	if err != nil {
		return nil, &core.RuntimeTransportError{
			Platform:    "macOS",
			Operation:   request.Operation,
			Phase:       "decode_response",
			RequestSent: true,
			Cause:       err,
		}
	}
	if response.RequestID != request.RequestID {
		cause := fmt.Errorf(
			"provider returned request_id %q for a different request",
			boundedProviderLabel(response.RequestID),
		)
		if response.RequestID == "invalid" &&
			response.Accessibility != nil &&
			strings.TrimSpace(response.Accessibility.Error) != "" {
			cause = fmt.Errorf(
				"provider rejected the request envelope: %s",
				boundedProviderLabel(
					response.Accessibility.Error,
				),
			)
		}
		return nil, &core.RuntimeTransportError{
			Platform:    "macOS",
			Operation:   request.Operation,
			Phase:       "request_id_mismatch",
			RequestSent: true,
			Cause:       cause,
		}
	}
	if response.Operation != request.Operation {
		return nil, &core.RuntimeTransportError{
			Platform:    "macOS",
			Operation:   request.Operation,
			Phase:       "operation_mismatch",
			RequestSent: true,
			Cause: fmt.Errorf(
				"provider returned operation %q",
				response.Operation,
			),
		}
	}
	if response.RequestRejection != nil {
		return nil, &core.RuntimeRequestRejectionError{
			Operation: request.Operation,
			Rejection: *response.RequestRejection,
		}
	}
	if request.Operation == core.ProviderWireOrderedInput {
		if request.OrderedInput == nil || response.OrderedInput == nil {
			return nil, &core.RuntimeTransportError{
				Platform:    "macOS",
				Operation:   request.Operation,
				Phase:       "decode_response",
				RequestSent: true,
				Cause:       errors.New("ordered-input request or response payload is missing"),
			}
		}
		if err := response.OrderedInput.ValidateFor(
			request.OrderedInput.ProviderOrderedInputRequest,
		); err != nil {
			return nil, &core.RuntimeTransportError{
				Platform:    "macOS",
				Operation:   request.Operation,
				Phase:       "decode_response",
				RequestSent: true,
				Cause:       err,
			}
		}
	}
	return &response, nil
}

func (worker *darwinProviderWorker) stop() {
	if worker == nil {
		return
	}
	worker.stopOnce.Do(func() {
		if worker.connection != nil {
			_ = worker.connection.Close()
		}
		terminateDarwinProvider(worker.providerPID)
		providerStopped := waitForDarwinProviderExit(
			worker.providerPID,
			darwinProviderShutdownGrace,
		)
		if !providerStopped {
			if worker.providerPID > 0 {
				if process, err := os.FindProcess(worker.providerPID); err == nil {
					_ = process.Kill()
				}
			}
			providerStopped = waitForDarwinProviderExit(
				worker.providerPID,
				darwinProviderShutdownGrace,
			)
		}
		commandStopped := waitForDarwinProviderCommand(
			worker.done,
			darwinProviderShutdownGrace,
		)
		if !commandStopped {
			if worker.cmd != nil && worker.cmd.Process != nil {
				_ = worker.cmd.Process.Kill()
			}
			commandStopped = waitForDarwinProviderCommand(
				worker.done,
				darwinProviderShutdownGrace,
			)
		}
		if commandStopped && worker.stderrDone != nil {
			select {
			case <-worker.stderrDone:
			case <-time.After(250 * time.Millisecond):
			}
		}
		if worker.runtimeDir != "" {
			_ = os.RemoveAll(worker.runtimeDir)
		}
	})
}

func startDarwinProviderWorker(
	artifact darwinProviderArtifact,
) (*darwinProviderWorker, error) {
	if err := verifyDarwinProvider(artifact); err != nil {
		return nil, err
	}
	runtimeDir, err := os.MkdirTemp(
		"/tmp",
		"computer-use-macos-provider-*",
	)
	if err != nil {
		return nil, fmt.Errorf("create macOS provider runtime directory: %w", err)
	}
	cleanup := func() {
		_ = os.RemoveAll(runtimeDir)
	}
	socketPath := filepath.Join(runtimeDir, "provider.sock")
	pidPath := filepath.Join(runtimeDir, "provider.pid")
	listener, err := net.Listen("unix", socketPath)
	if err != nil {
		cleanup()
		return nil, fmt.Errorf("listen for macOS provider: %w", err)
	}
	if err := os.Chmod(socketPath, 0o600); err != nil {
		_ = listener.Close()
		cleanup()
		return nil, fmt.Errorf("protect macOS provider socket: %w", err)
	}

	cmd := darwinProviderOpenCommand(
		artifact.bundlePath,
		socketPath,
		pidPath,
	)
	stderrPipe, err := cmd.StderrPipe()
	if err != nil {
		_ = listener.Close()
		cleanup()
		return nil, fmt.Errorf("open macOS provider stderr: %w", err)
	}
	if err := cmd.Start(); err != nil {
		_ = listener.Close()
		cleanup()
		return nil, fmt.Errorf(
			"launch macOS provider app through LaunchServices: %w",
			err,
		)
	}
	worker := &darwinProviderWorker{
		cmd:        cmd,
		stderr:     &boundedProviderBuffer{limit: maxDarwinProviderStderrBytes},
		done:       make(chan struct{}),
		stderrDone: make(chan struct{}),
		runtimeDir: runtimeDir,
	}
	go worker.forwardStderr(stderrPipe)
	go worker.wait()

	deadline := time.Now().Add(darwinProviderStartupTimeout())
	if unixListener, ok := listener.(*net.UnixListener); ok {
		_ = unixListener.SetDeadline(deadline)
	}
	connection, err := waitForDarwinProviderConnection(
		listener,
		worker,
		pidPath,
		deadline,
		darwinProviderLaunchPIDGrace,
	)
	_ = listener.Close()
	_ = os.Remove(socketPath)
	if err != nil {
		worker.stop()
		return nil, fmt.Errorf(
			"accept macOS provider app connection: %w: %s",
			err,
			strings.TrimSpace(worker.stderrText()),
		)
	}
	providerPID, err := readDarwinProviderPIDUntil(pidPath, deadline)
	if err != nil {
		_ = connection.Close()
		if discoveredPID, pidErr := readDarwinProviderPID(pidPath); pidErr == nil {
			worker.providerPID = discoveredPID
		}
		worker.stop()
		return nil, fmt.Errorf("read macOS provider pid: %w", err)
	}
	worker.connection = connection
	worker.stdout = bufio.NewReader(connection)
	worker.providerPID = providerPID
	return worker, nil
}
