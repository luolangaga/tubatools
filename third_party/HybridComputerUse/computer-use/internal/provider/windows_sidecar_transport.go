//go:build windows || windows_sidecar_test

package provider

import (
	"bufio"
	"bytes"
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

const maxWindowsProviderResponseBytes = 64 << 20
const maxWindowsProviderStderrBytes = 64 << 10
const maxWindowsProviderStderrLineBytes = 64 << 10
const windowsProviderShutdownGrace = 250 * time.Millisecond
const windowsProviderKillWait = 250 * time.Millisecond
const windowsProviderBaseTimeout = 30 * time.Second
const windowsProviderDesktopActionBudget = 500 * time.Millisecond
const windowsProviderDesktopWaitBudget = time.Second

// Matches the native per-type message-loop bound; the base remains available
// for startup, preparation and the final observation after text emission.
const windowsProviderTextInputBudget = 15 * time.Second

var errWindowsProviderResponseTooLarge = errors.New(
	"Windows provider response exceeded the configured limit",
)

var (
	windowsProviderRequestSequence atomic.Uint64
	windowsProviderProcesses       windowsProviderProcessManager
	windowsProviderCommand         = func(path string) *exec.Cmd {
		return exec.Command(path)
	}
	windowsProviderWorkerStarter              = startWindowsProviderWorker
	windowsProviderDiagnosticWriter io.Writer = os.Stderr
	windowsProviderDiagnostics                = newWindowsProviderDiagnosticQueue()
)

type windowsProviderProcessManager struct {
	mu             sync.Mutex
	worker         *windowsProviderWorker
	retiringWorker *windowsProviderWorker
	environmentKey string
	generation     uint64
	// A non-nil token retains a timed-out startup's worker until the caller
	// can check its exit under mu. Bounded stop alone does not confirm exit.
	startGate chan *windowsProviderWorker
}

type windowsProviderWorker struct {
	generation         uint64
	cmd                *exec.Cmd
	stdin              io.WriteCloser
	stdout             *bufio.Reader
	stderr             *boundedProviderBuffer
	done               chan struct{}
	stderrDone         chan struct{}
	parentDeathBinding io.Closer
	bindingCloseOnce   sync.Once
	stopOnce           sync.Once
	waitErr            error
}

type windowsProviderReadResult struct {
	line []byte
	err  error
}

type windowsProviderWriteResult struct {
	written int
	err     error
}

type windowsProviderStartResult struct {
	worker *windowsProviderWorker
	err    error
}

func nextWindowsProviderRequestID() string {
	return fmt.Sprintf(
		"windows-%d-%d",
		time.Now().UnixNano(),
		windowsProviderRequestSequence.Add(1),
	)
}

func windowsProviderRuntimeTimeout(
	request core.ProviderWireRequest,
) time.Duration {
	timeout := windowsProviderBaseTimeout
	if raw := strings.TrimSpace(
		os.Getenv("COMPUTER_USE_RUNTIME_TIMEOUT_SECONDS"),
	); raw != "" {
		if seconds, err := strconv.Atoi(raw); err == nil && seconds > 0 {
			timeout = time.Duration(seconds) * time.Second
		}
	}
	if request.Operation != core.ProviderWireDesktopBatch ||
		request.Desktop == nil {
		if request.Operation == core.ProviderWireResolveAndAct && request.Action != nil {
			action := request.Action
			if (action.Intent == core.AccessibilityType && action.Type != nil && action.Type.Text != "") ||
				(action.Intent == core.AccessibilitySetValue && action.SetValue != nil &&
					action.SetValue.Value != nil && *action.SetValue.Value != "") {
				timeout += windowsProviderTextInputBudget
			}
		}
		return timeout
	}
	timeout += time.Duration(len(request.Desktop.Actions)) *
		windowsProviderDesktopActionBudget
	for _, action := range request.Desktop.Actions {
		if action.Type == "wait" {
			timeout += windowsProviderDesktopWaitBudget
		}
		if action.Type == "type" && action.Text != "" {
			timeout += windowsProviderTextInputBudget
		}
	}
	if request.Desktop.SettleMS > 0 {
		timeout += time.Duration(request.Desktop.SettleMS) * time.Millisecond
	}
	return timeout
}

func windowsProviderExecutable() (string, error) {
	if configured := strings.TrimSpace(
		os.Getenv("COMPUTER_USE_WINDOWS_PROVIDER"),
	); configured != "" {
		return configured, nil
	}
	executable, err := os.Executable()
	if err != nil {
		return "", err
	}
	path := filepath.Join(
		filepath.Dir(executable),
		"computer-use-windows-provider.exe",
	)
	if _, err := os.Stat(path); err != nil {
		return "", fmt.Errorf(
			"Windows provider artifact is unavailable at %s: %w",
			path,
			err,
		)
	}
	return path, nil
}

func runWindowsProvider(
	request core.ProviderWireRequest,
) (*core.ProviderWireResponse, error) {
	if err := request.Validate(); err != nil {
		return nil, err
	}
	return windowsProviderProcesses.run(request)
}

func (manager *windowsProviderProcessManager) run(
	request core.ProviderWireRequest,
) (response *core.ProviderWireResponse, err error) {
	manager.mu.Lock()
	defer manager.mu.Unlock()
	if err := manager.retirementError(request.Operation); err != nil {
		return nil, err
	}

	timeout := windowsProviderRuntimeTimeout(request)
	deadline := time.Now().Add(timeout)
	trace := newWindowsProviderTrace(request.Operation, deadline, timeout)
	trace.record("request_begin", nil, nil)
	defer func() { trace.record("request_end", response, err) }()
	providerPath, err := windowsProviderExecutable()
	if err != nil {
		return nil, err
	}
	environment := os.Environ()
	environmentKey := providerPath + "\x00" +
		strings.Join(environment, "\x00")
	if manager.worker == nil || manager.environmentKey != environmentKey {
		manager.stopLocked()
		if err := manager.retirementError(request.Operation); err != nil {
			return nil, err
		}
		manager.generation++
		trace = trace.forWorker(manager.generation)
		worker, startErr := manager.startWorkerWithin(
			providerPath,
			environment,
			request.Operation,
			deadline,
			timeout,
			trace,
		)
		if startErr != nil {
			return nil, startErr
		}
		manager.worker = worker
		worker.generation = manager.generation
		manager.environmentKey = environmentKey
	}

	trace = trace.forWorker(manager.worker.generation)
	response, err = manager.worker.call(
		request,
		windowsProviderRemainingTimeout(deadline),
		trace,
	)
	trace.record("native_call_end", response, err)
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
		if windowsProviderRequestCanRetryAfterReset(request, response) {
			if err := manager.retirementError(request.Operation); err != nil {
				return nil, err
			}
			manager.generation++
			trace = trace.forWorker(manager.generation)
			worker, startErr := manager.startWorkerWithin(
				providerPath,
				environment,
				request.Operation,
				deadline,
				timeout,
				trace,
			)
			if startErr != nil {
				return nil, startErr
			}
			manager.worker = worker
			worker.generation = manager.generation
			manager.environmentKey = environmentKey
			response, err = manager.worker.call(
				request,
				windowsProviderRemainingTimeout(deadline),
				trace,
			)
			trace.record("native_call_end", response, err)
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

func windowsProviderRemainingTimeout(deadline time.Time) time.Duration {
	remaining := time.Until(deadline)
	if remaining <= 0 {
		return time.Nanosecond
	}
	return remaining
}

func (manager *windowsProviderProcessManager) startWorkerWithin(
	path string,
	environment []string,
	operation core.ProviderWireOperation,
	deadline time.Time,
	timeout time.Duration,
	trace *windowsProviderTrace,
) (worker *windowsProviderWorker, err error) {
	trace.record("startup_begin", nil, nil)
	defer func() { trace.record("startup_end", nil, err) }()
	if manager.startGate == nil {
		manager.startGate = make(chan *windowsProviderWorker, 1)
		manager.startGate <- nil
	}
	gateTimer := time.NewTimer(windowsProviderRemainingTimeout(deadline))
	defer gateTimer.Stop()
	select {
	case lateWorker := <-manager.startGate:
		if lateWorker != nil {
			manager.retiringWorker = lateWorker
			if err := manager.retirementError(operation); err != nil {
				manager.startGate <- nil
				return nil, err
			}
		}
	case <-gateTimer.C:
		writeWindowsProviderLifecycleDiagnostic(
			"start_provider",
			timeout,
			"startup gate deadline exceeded",
		)
		return nil, &core.RuntimeTimeoutError{
			Duration:    timeout,
			Platform:    "Windows",
			Phase:       "start_provider",
			PreDispatch: true,
		}
	}

	resultChannel := make(chan windowsProviderStartResult)
	cancel := make(chan struct{})
	go func() {
		var abandonedWorker *windowsProviderWorker
		defer func() {
			manager.startGate <- abandonedWorker
		}()
		worker, err := windowsProviderWorkerStarter(
			path,
			environment,
			trace,
		)
		result := windowsProviderStartResult{worker: worker, err: err}
		select {
		case resultChannel <- result:
		case <-cancel:
			if worker != nil {
				worker.stop()
				abandonedWorker = worker
			}
		}
	}()

	startTimer := time.NewTimer(windowsProviderRemainingTimeout(deadline))
	defer startTimer.Stop()
	select {
	case result := <-resultChannel:
		return manager.acceptStartedWorker(result, deadline, timeout)
	case <-startTimer.C:
		close(cancel)
		writeWindowsProviderLifecycleDiagnostic(
			"start_provider",
			timeout,
			"provider startup deadline exceeded",
		)
		return nil, &core.RuntimeTimeoutError{
			Duration:    timeout,
			Platform:    "Windows",
			Phase:       "start_provider",
			PreDispatch: true,
		}
	}
}

func (manager *windowsProviderProcessManager) acceptStartedWorker(
	result windowsProviderStartResult,
	deadline time.Time,
	timeout time.Duration,
) (*windowsProviderWorker, error) {
	if !time.Now().Before(deadline) {
		manager.retireWorker(result.worker)
		writeWindowsProviderLifecycleDiagnostic(
			"start_provider",
			timeout,
			"provider startup completed after its deadline",
		)
		return nil, &core.RuntimeTimeoutError{
			Duration:    timeout,
			Platform:    "Windows",
			Phase:       "start_provider",
			PreDispatch: true,
		}
	}
	return result.worker, result.err
}

func windowsProviderRequestCanRetryAfterReset(
	request core.ProviderWireRequest,
	response *core.ProviderWireResponse,
) bool {
	if response == nil || windowsProviderResponseMayHaveMutated(response) {
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

func windowsProviderResponseMayHaveMutated(
	response *core.ProviderWireResponse,
) bool {
	if response == nil {
		return false
	}
	var mutation *core.ProviderMutationFacts
	switch {
	case response.Accessibility != nil:
		mutation = response.Accessibility.Mutation
	case response.Desktop != nil:
		mutation = response.Desktop.Mutation
	case response.Window != nil:
		mutation = response.Window.Mutation
	}
	return core.ProviderMutationCanInvalidate(mutation)
}

func (manager *windowsProviderProcessManager) recordStderrLocked(
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
	diagnostic := fmt.Sprintf(
		"[computer-use windows-provider request=%s operation=%s "+
			"stderr_bytes=%d stderr_truncated=%t "+
			"transport_error=%q]\n%s\n",
		boundedProviderLabel(request.RequestID),
		boundedProviderLabel(string(request.Operation)),
		len(data),
		truncated,
		callErr.Error(),
		data,
	)
	windowsProviderDiagnostics.write([]byte(diagnostic))
}

// Bounded teardown does not prove termination. Keep the old worker until its
// wait channel confirms exit so a replacement cannot emit input concurrently.
func (manager *windowsProviderProcessManager) retirementError(
	operation core.ProviderWireOperation,
) error {
	if manager.retiringWorker == nil {
		return nil
	}
	select {
	case <-manager.retiringWorker.done:
		manager.retiringWorker = nil
		return nil
	default:
		return &core.RuntimeTransportError{
			Platform:    "Windows",
			Operation:   operation,
			Phase:       "stop_provider",
			RequestSent: false,
			Cause:       errors.New("previous provider exit is unconfirmed; no new request was dispatched; stop automation until it exits"),
		}
	}
}

func (manager *windowsProviderProcessManager) stopLocked() {
	manager.retireWorker(manager.worker)
	manager.worker = nil
	manager.environmentKey = ""
}

func (manager *windowsProviderProcessManager) retireWorker(worker *windowsProviderWorker) {
	if worker != nil {
		worker.stop()
		select {
		case <-worker.done:
		default:
			manager.retiringWorker = worker
		}
	}
}

func shutdownWindowsProviderProcess() {
	windowsProviderProcesses.mu.Lock()
	defer windowsProviderProcesses.mu.Unlock()
	windowsProviderProcesses.stopLocked()
}
