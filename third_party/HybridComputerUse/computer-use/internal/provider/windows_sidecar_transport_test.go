//go:build windows || windows_sidecar_test

package provider

import (
	"bufio"
	"bytes"
	"encoding/json"
	"errors"
	"io"
	"os"
	"os/exec"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

type blockingWindowsProviderCloser struct {
	started chan struct{}
	release chan struct{}
}

func (closer *blockingWindowsProviderCloser) Write(data []byte) (int, error) {
	return len(data), nil
}

func (closer *blockingWindowsProviderCloser) Close() error {
	close(closer.started)
	<-closer.release
	return nil
}

func TestWindowsProviderRuntimeTimeoutIncludesDesktopExecutionCost(
	t *testing.T,
) {
	t.Setenv("COMPUTER_USE_RUNTIME_TIMEOUT_SECONDS", "")
	observe := validWindowsObserveAppsRequest("observe-timeout")
	if got := windowsProviderRuntimeTimeout(observe); got != 30*time.Second {
		t.Fatalf("observe timeout = %s, want 30s", got)
	}

	actions := make([]core.ProviderDesktopAction, 60)
	for index := range actions {
		actions[index] = core.ProviderDesktopAction{Type: "keypress"}
	}
	desktop := core.NewProviderWireRequest(
		"desktop-timeout",
		core.ProviderWireDesktopBatch,
	)
	desktop.Desktop = &core.ProviderDesktopRequest{
		ProtocolVersion: core.ProviderProtocolVersion,
		Actions:         actions,
		SettleMS:        1200,
	}
	if got := windowsProviderRuntimeTimeout(desktop); got !=
		61*time.Second+200*time.Millisecond {
		t.Fatalf("desktop timeout = %s, want 61.2s", got)
	}

	t.Setenv("COMPUTER_USE_RUNTIME_TIMEOUT_SECONDS", "7")
	if got := windowsProviderRuntimeTimeout(desktop); got !=
		38*time.Second+200*time.Millisecond {
		t.Fatalf("overridden desktop timeout = %s, want 38.2s", got)
	}
}

func TestWindowsProviderRuntimeTimeoutIncludesExplicitWaits(t *testing.T) {
	t.Setenv("COMPUTER_USE_RUNTIME_TIMEOUT_SECONDS", "")
	request := core.NewProviderWireRequest("wait-timeout", core.ProviderWireDesktopBatch)
	request.Desktop = &core.ProviderDesktopRequest{
		ProtocolVersion: core.ProviderProtocolVersion,
		Actions: []core.ProviderDesktopAction{
			{Type: "wait"},
			{Type: "keypress"},
		},
		SettleMS: 3000,
	}
	if got := windowsProviderRuntimeTimeout(request); got != 35*time.Second {
		t.Fatalf("wait timeout = %s, want 35s", got)
	}
}

func TestWindowsProviderRuntimeTimeoutAllowsEachBoundedTextEmission(t *testing.T) {
	t.Setenv("COMPUTER_USE_RUNTIME_TIMEOUT_SECONDS", "7")
	request := core.NewProviderWireRequest("text-batch", core.ProviderWireDesktopBatch)
	request.Desktop = &core.ProviderDesktopRequest{
		ProtocolVersion: core.ProviderProtocolVersion,
		Actions: []core.ProviderDesktopAction{
			{Type: "type", Text: "first"},
			{Type: "type", Text: ""},
			{Type: "type", Text: "second"},
			{Type: "wait"},
		},
		SettleMS: 700,
	}
	if got := windowsProviderRuntimeTimeout(request); got != 40*time.Second+700*time.Millisecond {
		t.Fatalf("two text budgets plus preparation/wait/settle = %s, want 40.7s", got)
	}
	value := "replacement"
	for _, action := range []*core.ProviderActionRequest{
		{Intent: core.AccessibilityType, Type: &core.ProviderTypeInput{Text: "text"}},
		{Intent: core.AccessibilitySetValue, SetValue: &core.ProviderSetValueInput{Value: &value}},
	} {
		request = core.NewProviderWireRequest("ax-text", core.ProviderWireResolveAndAct)
		request.Action = action
		if got := windowsProviderRuntimeTimeout(request); got != 22*time.Second {
			t.Fatalf("AX text budget = %s, want 22s", got)
		}
	}
	request.Action = &core.ProviderActionRequest{Intent: core.AccessibilityType, Type: &core.ProviderTypeInput{}}
	if got := windowsProviderRuntimeTimeout(request); got != 7*time.Second {
		t.Fatalf("empty AX text budget = %s, want unchanged 7s", got)
	}
}

func TestWindowsProviderBindingCloseCannotBlockTeardown(t *testing.T) {
	closer := &blockingWindowsProviderCloser{
		started: make(chan struct{}),
		release: make(chan struct{}),
	}
	worker := &windowsProviderWorker{
		parentDeathBinding: closer,
	}
	started := time.Now()
	if worker.closeParentDeathBindingWithin(20 * time.Millisecond) {
		t.Fatal("blocked binding close unexpectedly completed")
	}
	if elapsed := time.Since(started); elapsed > time.Second {
		t.Fatalf("blocked binding close delayed teardown: %s", elapsed)
	}
	select {
	case <-closer.started:
	default:
		t.Fatal("binding close was not attempted")
	}
	close(closer.release)
}

func TestWindowsProviderInputCloseCannotBlockTeardown(t *testing.T) {
	closer := &blockingWindowsProviderCloser{
		started: make(chan struct{}),
		release: make(chan struct{}),
	}
	done := make(chan struct{})
	close(done)
	worker := &windowsProviderWorker{
		stdin:      closer,
		done:       done,
		stderrDone: done,
	}
	started := time.Now()
	worker.stop()
	if elapsed := time.Since(started); elapsed > time.Second {
		t.Fatalf("blocked stdin close delayed teardown: %s", elapsed)
	}
	select {
	case <-closer.started:
	default:
		t.Fatal("stdin close was not attempted")
	}
	close(closer.release)
}

func TestWindowsProviderStartupDeadlineAndLateCleanup(t *testing.T) {
	originalStarter := windowsProviderWorkerStarter
	defer func() {
		windowsProviderWorkerStarter = originalStarter
	}()

	started := make(chan struct{})
	release := make(chan struct{})
	closed := make(chan struct{})
	var starts atomic.Int32
	windowsProviderWorkerStarter = func(
		string,
		[]string,
		*windowsProviderTrace,
	) (*windowsProviderWorker, error) {
		starts.Add(1)
		close(started)
		<-release
		done := make(chan struct{})
		close(done)
		return &windowsProviderWorker{
			stdin: &notifyingWindowsProviderWriteCloser{
				closed: closed,
			},
			done:       done,
			stderrDone: done,
		}, nil
	}

	manager := &windowsProviderProcessManager{}
	firstStartedAt := time.Now()
	_, err := manager.startWorkerWithin(
		"provider.exe",
		nil,
		core.ProviderWireObserveApps,
		time.Now().Add(20*time.Millisecond),
		20*time.Millisecond,
		nil,
	)
	var timeoutErr *core.RuntimeTimeoutError
	if !errors.As(err, &timeoutErr) ||
		timeoutErr.Phase != "start_provider" || !timeoutErr.PreDispatch {
		t.Fatalf("startup error = %#v, want start_provider timeout", err)
	}
	if elapsed := time.Since(firstStartedAt); elapsed > time.Second {
		t.Fatalf("startup ignored deadline: %s", elapsed)
	}
	select {
	case <-started:
	default:
		t.Fatal("provider startup was not attempted")
	}

	_, err = manager.startWorkerWithin(
		"provider.exe",
		nil,
		core.ProviderWireObserveApps,
		time.Now().Add(20*time.Millisecond),
		20*time.Millisecond,
		nil,
	)
	if !errors.As(err, &timeoutErr) ||
		timeoutErr.Phase != "start_provider" {
		t.Fatalf("startup gate error = %#v, want start_provider timeout", err)
	}
	if starts.Load() != 1 {
		t.Fatalf("provider starts = %d, want 1", starts.Load())
	}

	close(release)
	select {
	case <-closed:
	case <-time.After(time.Second):
		t.Fatal("late provider worker was not closed")
	}
}

func TestWindowsProviderLateStartupBlocksReplacementUntilExit(t *testing.T) {
	path := currentWindowsTestExecutable(t)
	t.Setenv("COMPUTER_USE_WINDOWS_PROVIDER", path)
	t.Setenv("GO_WANT_WINDOWS_PROVIDER_HELPER", "reuse")
	useWindowsProviderTestCommand(t)
	originalStarter := windowsProviderWorkerStarter
	t.Cleanup(func() { windowsProviderWorkerStarter = originalStarter })

	started := make(chan struct{})
	release := make(chan struct{})
	exited := make(chan struct{})
	var releaseOnce, exitOnce sync.Once
	t.Cleanup(func() {
		releaseOnce.Do(func() { close(release) })
		exitOnce.Do(func() { close(exited) })
	})
	late := &windowsProviderWorker{done: exited}
	// Teardown has used its bounded grace period, but exit remains unconfirmed.
	late.stopOnce.Do(func() {})
	var starts atomic.Int32
	windowsProviderWorkerStarter = func(path string, environment []string, trace *windowsProviderTrace) (*windowsProviderWorker, error) {
		if starts.Add(1) == 1 {
			close(started)
			<-release
			return late, nil
		}
		return originalStarter(path, environment, trace)
	}
	manager := &windowsProviderProcessManager{}
	t.Cleanup(manager.stopLocked)
	_, err := manager.startWorkerWithin(path, nil, core.ProviderWireObserveApps, time.Now().Add(100*time.Millisecond), 100*time.Millisecond, nil)
	var timeoutErr *core.RuntimeTimeoutError
	if !errors.As(err, &timeoutErr) || !timeoutErr.PreDispatch {
		t.Fatalf("startup error = %v, want pre-dispatch timeout", err)
	}
	select {
	case <-started:
	default:
		t.Fatal("startup was not attempted")
	}
	releaseOnce.Do(func() { close(release) })
	// Synchronize with late cleanup, rather than waiting an assumed duration.
	select {
	case token := <-manager.startGate:
		manager.startGate <- token
	case <-time.After(time.Second):
		t.Fatal("late startup did not finish bounded cleanup")
	}

	request := validWindowsObserveAppsRequest("late-start-blocked")
	for attempt := 0; attempt < 2; attempt++ {
		_, err = manager.run(request)
		var transport *core.RuntimeTransportError
		if !errors.As(err, &transport) || transport.Phase != "stop_provider" || transport.RequestSent {
			t.Fatalf("unconfirmed late-worker exit = %v, want pre-dispatch retirement error", err)
		}
		if starts.Load() != 1 || manager.worker != nil {
			t.Fatal("replacement started before late worker exited")
		}
	}
	exitOnce.Do(func() { close(exited) })
	request.RequestID = "late-start-exited"
	if _, err := manager.run(request); err != nil {
		t.Fatalf("confirmed late-worker exit did not permit replacement: %v", err)
	}
	if starts.Load() != 2 {
		t.Fatalf("provider starts = %d, want 2", starts.Load())
	}
}

func TestWindowsProviderExpiredStartupResultRetainsUnconfirmedExit(t *testing.T) {
	t.Setenv("COMPUTER_USE_WINDOWS_PROVIDER", currentWindowsTestExecutable(t))
	t.Setenv("GO_WANT_WINDOWS_PROVIDER_HELPER", "reuse")
	useWindowsProviderTestCommand(t)
	exited := make(chan struct{})
	var exitOnce sync.Once
	t.Cleanup(func() { exitOnce.Do(func() { close(exited) }) })
	late := &windowsProviderWorker{done: exited}
	late.stopOnce.Do(func() {})
	manager := &windowsProviderProcessManager{}
	t.Cleanup(manager.stopLocked)

	// A ready result and an expired timer can race in select. Check the
	// result-acceptance path directly, without relying on scheduler timing.
	worker, err := manager.acceptStartedWorker(
		windowsProviderStartResult{worker: late},
		time.Now().Add(-time.Second),
		time.Second,
	)
	var timeoutErr *core.RuntimeTimeoutError
	if worker != nil || !errors.As(err, &timeoutErr) ||
		timeoutErr.Phase != "start_provider" || !timeoutErr.PreDispatch {
		t.Fatalf("expired startup result = %v, %v", worker, err)
	}
	request := validWindowsObserveAppsRequest("expired-start-blocked")
	_, err = manager.run(request)
	var transport *core.RuntimeTransportError
	if !errors.As(err, &transport) || transport.Phase != "stop_provider" ||
		transport.Operation != request.Operation || transport.RequestSent {
		t.Fatalf("expired worker retirement result = %v", err)
	}
	if manager.worker != nil || manager.retiringWorker != late {
		t.Fatal("expired startup worker was discarded before confirmed exit")
	}
	exitOnce.Do(func() { close(exited) })
	request.RequestID = "expired-start-exited"
	if _, err := manager.run(request); err != nil {
		t.Fatalf("confirmed expired-worker exit did not permit replacement: %v", err)
	}
}

func TestWindowsProviderWorkerIsPersistentAndGracefulOnEOF(t *testing.T) {
	path := currentWindowsTestExecutable(t)
	counterPath := t.TempDir() + "/starts"
	t.Setenv("GO_WANT_WINDOWS_PROVIDER_HELPER", "reuse")
	t.Setenv("WINDOWS_PROVIDER_START_COUNTER", counterPath)
	useWindowsProviderTestCommand(t)
	worker, err := startWindowsProviderWorker(path, os.Environ(), nil)
	if err != nil {
		t.Fatal(err)
	}

	request := validWindowsObserveAppsRequest("reuse-1")
	if _, err := worker.call(request, time.Second, nil); err != nil {
		t.Fatal(err)
	}
	request.RequestID = "reuse-2"
	if _, err := worker.call(request, time.Second, nil); err != nil {
		t.Fatal(err)
	}
	worker.stop()

	data, err := os.ReadFile(counterPath)
	if err != nil {
		t.Fatal(err)
	}
	if string(data) != "start\nclosed\n" {
		t.Fatalf("worker lifecycle = %q", data)
	}
}

func TestWindowsProviderManagerRetriesReadOnlyResetOnce(t *testing.T) {
	path := currentWindowsTestExecutable(t)
	counterPath := t.TempDir() + "/starts"
	t.Setenv("COMPUTER_USE_WINDOWS_PROVIDER", path)
	t.Setenv("GO_WANT_WINDOWS_PROVIDER_HELPER", "reset-read")
	t.Setenv("WINDOWS_PROVIDER_START_COUNTER", counterPath)
	useWindowsProviderTestCommand(t)
	shutdownWindowsProviderProcess()
	t.Cleanup(shutdownWindowsProviderProcess)

	response, err := runWindowsProvider(
		validWindowsObserveAppsRequest("reset-read"),
	)
	if err != nil {
		t.Fatal(err)
	}
	if response.Accessibility == nil || !response.Accessibility.OK {
		t.Fatalf("read-only retry response = %#v", response)
	}
	data, err := os.ReadFile(counterPath)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Count(string(data), "start\n") != 2 {
		t.Fatalf("provider starts = %q, want 2", data)
	}
}

func TestWindowsProviderManagerNeverReplaysMutationReset(t *testing.T) {
	path := currentWindowsTestExecutable(t)
	counterPath := t.TempDir() + "/starts"
	requestPath := t.TempDir() + "/requests"
	t.Setenv("COMPUTER_USE_WINDOWS_PROVIDER", path)
	t.Setenv("GO_WANT_WINDOWS_PROVIDER_HELPER", "reset-mutation")
	t.Setenv("WINDOWS_PROVIDER_START_COUNTER", counterPath)
	t.Setenv("WINDOWS_PROVIDER_REQUEST_COUNTER", requestPath)
	useWindowsProviderTestCommand(t)
	shutdownWindowsProviderProcess()
	t.Cleanup(shutdownWindowsProviderProcess)

	request := core.NewProviderWireRequest("reset-mutation", core.ProviderWireResolveAndAct)
	request.Action = &core.ProviderActionRequest{
		ProtocolVersion: core.ProviderProtocolVersion,
		Intent:          core.AccessibilityClick,
		App:             "Editor",
		Target: &core.ProviderSemanticTarget{
			Selector: `button "Save"`,
			Role:     "button",
			Name:     "Save",
			HasName:  true,
		},
		Click: &core.ProviderClickInput{ClickCount: 1},
	}
	response, err := runWindowsProvider(request)
	if err != nil {
		t.Fatal(err)
	}
	if response.Accessibility == nil ||
		response.Accessibility.Mutation == nil ||
		!response.Accessibility.Mutation.MutationUnknown {
		t.Fatalf("mutation reset response = %#v", response)
	}
	data, err := os.ReadFile(requestPath)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Count(string(data), "reset-mutation\n") != 1 {
		t.Fatalf("mutation request was replayed: %q", data)
	}
	starts, err := os.ReadFile(counterPath)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Count(string(starts), "start\n") != 1 {
		t.Fatalf("provider starts = %q, want 1", starts)
	}
}

func TestWindowsProviderTimeoutPreservesSideEffectAndRecoversWithNewWorker(
	t *testing.T,
) {
	path := currentWindowsTestExecutable(t)
	root := t.TempDir()
	counterPath := root + "/starts"
	requestPath := root + "/requests"
	effectPath := root + "/effect"
	t.Setenv("COMPUTER_USE_WINDOWS_PROVIDER", path)
	t.Setenv("COMPUTER_USE_RUNTIME_TIMEOUT_SECONDS", "1")
	t.Setenv("GO_WANT_WINDOWS_PROVIDER_HELPER", "side-effect-timeout")
	t.Setenv("WINDOWS_PROVIDER_START_COUNTER", counterPath)
	t.Setenv("WINDOWS_PROVIDER_REQUEST_COUNTER", requestPath)
	t.Setenv("WINDOWS_PROVIDER_EFFECT_STATE", effectPath)
	useWindowsProviderTestCommand(t)
	shutdownWindowsProviderProcess()
	t.Cleanup(shutdownWindowsProviderProcess)

	request := core.NewProviderWireRequest(
		"side-effect-timeout-mutation",
		core.ProviderWireResolveAndAct,
	)
	request.Action = &core.ProviderActionRequest{
		ProtocolVersion: core.ProviderProtocolVersion,
		Intent:          core.AccessibilityClick,
		App:             "Editor",
		Target: &core.ProviderSemanticTarget{
			Selector: `button "Save"`,
			Role:     "button",
			Name:     "Save",
			HasName:  true,
		},
		Click: &core.ProviderClickInput{ClickCount: 1},
	}
	started := time.Now()
	_, err := runWindowsProvider(request)
	var timeoutErr *core.RuntimeTimeoutError
	if !errors.As(err, &timeoutErr) ||
		timeoutErr.Phase != "read_response" {
		t.Fatalf("mutation error = %#v, want read_response timeout", err)
	}
	if elapsed := time.Since(started); elapsed > 3*time.Second {
		t.Fatalf("mutation timeout was not bounded: %s", elapsed)
	}
	failure := core.NewService().RuntimeFailureResult(
		core.AXServiceRequest{Action: core.AXClickCommand{}},
		nil,
		err,
	)
	if failure.StructuredContent["mutation_unknown"] != true ||
		failure.StructuredContent["retry_same_request"] != false {
		t.Fatalf(
			"timed-out mutation attribution = %#v",
			failure.StructuredContent,
		)
	}
	if windowsProviderProcesses.worker != nil {
		t.Fatal("timed-out worker was retained")
	}

	observation, err := runWindowsProvider(
		validWindowsObserveAppsRequest("side-effect-timeout-observe"),
	)
	if err != nil {
		t.Fatalf("replacement worker observation failed: %v", err)
	}
	if observation.Accessibility == nil ||
		!observation.Accessibility.OK ||
		observation.Accessibility.Text != "effect=applied" {
		t.Fatalf(
			"replacement observation did not see side effect: %#v",
			observation,
		)
	}

	requests, err := os.ReadFile(requestPath)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Count(string(requests), "side-effect-timeout-mutation\n") != 1 {
		t.Fatalf("mutation request was replayed: %q", requests)
	}
	if strings.Count(string(requests), "side-effect-timeout-observe\n") != 1 {
		t.Fatalf("replacement observation count = %q", requests)
	}
	starts, err := os.ReadFile(counterPath)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Count(string(starts), "start\n") != 2 {
		t.Fatalf("provider starts = %q, want 2", starts)
	}
}

func TestWindowsProviderRequestRejectionKeepsWorker(t *testing.T) {
	t.Setenv("COMPUTER_USE_WINDOWS_PROVIDER", currentWindowsTestExecutable(t))
	t.Setenv("GO_WANT_WINDOWS_PROVIDER_HELPER", "reject-request")
	useWindowsProviderTestCommand(t)
	shutdownWindowsProviderProcess()
	t.Cleanup(shutdownWindowsProviderProcess)

	_, err := runWindowsProvider(validWindowsObserveAppsRequest("rejected"))
	var rejection *core.RuntimeRequestRejectionError
	if !errors.As(err, &rejection) || rejection.Rejection.Validate() != nil {
		t.Fatalf("request rejection = %#v", err)
	}
	worker := windowsProviderProcesses.worker
	if worker == nil {
		t.Fatal("pre-dispatch rejection retired the worker")
	}
	_, err = runWindowsProvider(validWindowsObserveAppsRequest("rejected-again"))
	if !errors.As(err, &rejection) || windowsProviderProcesses.worker != worker {
		t.Fatalf("request rejection did not preserve the worker: %v", err)
	}
}

func TestWindowsProviderManagerDestroysWorkerAfterProtocolError(t *testing.T) {
	path := currentWindowsTestExecutable(t)
	counterPath := t.TempDir() + "/starts"
	t.Setenv("COMPUTER_USE_WINDOWS_PROVIDER", path)
	t.Setenv("GO_WANT_WINDOWS_PROVIDER_HELPER", "protocol-error")
	t.Setenv("WINDOWS_PROVIDER_START_COUNTER", counterPath)
	useWindowsProviderTestCommand(t)
	shutdownWindowsProviderProcess()
	t.Cleanup(shutdownWindowsProviderProcess)

	request := validWindowsObserveAppsRequest("protocol-error-1")
	if _, err := runWindowsProvider(request); err == nil {
		t.Fatal("invalid provider response must fail")
	}
	if windowsProviderProcesses.worker != nil {
		t.Fatal("failed worker was retained")
	}
	request.RequestID = "protocol-error-2"
	if _, err := runWindowsProvider(request); err != nil {
		t.Fatalf("replacement worker failed: %v", err)
	}
	data, err := os.ReadFile(counterPath)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Count(string(data), "start\n") != 2 {
		t.Fatalf("provider starts = %q, want 2", data)
	}
}

func TestWindowsProviderCallDeadlineCoversWriteAndRead(t *testing.T) {
	request := validWindowsObserveAppsRequest("deadline")
	request.ObserveApps = &core.ProviderObserveAppsRequest{
		ProtocolVersion: core.ProviderProtocolVersion,
	}

	blockedWrite := &windowsProviderWorker{
		stdin:      blockingWriteCloser{},
		stdout:     bufio.NewReader(strings.NewReader("")),
		done:       make(chan struct{}),
		stderrDone: make(chan struct{}),
	}
	started := time.Now()
	_, err := blockedWrite.call(request, 20*time.Millisecond, nil)
	var timeoutErr *core.RuntimeTimeoutError
	if !errors.As(err, &timeoutErr) {
		t.Fatalf("blocked write error = %v, want runtime timeout", err)
	}
	if timeoutErr.Platform != "Windows" {
		t.Fatalf("blocked write timeout platform = %q", timeoutErr.Platform)
	}
	if timeoutErr.Phase != "write_request" {
		t.Fatalf("blocked write timeout phase = %q", timeoutErr.Phase)
	}
	if !strings.Contains(err.Error(), "Windows runtime timed out") {
		t.Fatalf("blocked write timeout text = %q", err)
	}
	if elapsed := time.Since(started); elapsed > time.Second {
		t.Fatalf("blocked write ignored deadline: %s", elapsed)
	}

	blockedRead := &windowsProviderWorker{
		stdin:      windowsNopWriteCloser{Writer: io.Discard},
		stdout:     bufio.NewReader(blockingReader{}),
		done:       make(chan struct{}),
		stderrDone: make(chan struct{}),
	}
	started = time.Now()
	_, err = blockedRead.call(request, 20*time.Millisecond, nil)
	if !errors.As(err, &timeoutErr) {
		t.Fatalf("blocked read error = %v, want runtime timeout", err)
	}
	if timeoutErr.Platform != "Windows" {
		t.Fatalf("blocked read timeout platform = %q", timeoutErr.Platform)
	}
	if timeoutErr.Phase != "read_response" {
		t.Fatalf("blocked read timeout phase = %q", timeoutErr.Phase)
	}
	if elapsed := time.Since(started); elapsed > time.Second {
		t.Fatalf("blocked read ignored deadline: %s", elapsed)
	}
}

func TestWindowsProviderResponseIsBounded(t *testing.T) {
	reader := bufio.NewReaderSize(
		strings.NewReader(strings.Repeat("x", 64)+"\n"),
		8,
	)
	line, err := readWindowsProviderLine(reader, 32)
	if !errors.Is(err, errWindowsProviderResponseTooLarge) {
		t.Fatalf("oversized response error = %v", err)
	}
	if line != nil {
		t.Fatalf("oversized response retained %d bytes", len(line))
	}
}

func TestWindowsProviderStderrIsForwardedForOperatorDiagnostics(t *testing.T) {
	operator := &notifyingWindowsProviderDiagnosticWriter{wrote: make(chan struct{}, 1)}
	previousWriter := windowsProviderDiagnosticWriter
	windowsProviderDiagnosticWriter = operator
	defer func() {
		windowsProviderDiagnosticWriter = previousWriter
	}()

	worker := &windowsProviderWorker{
		stderr:     &boundedProviderBuffer{limit: 64},
		stderrDone: make(chan struct{}),
	}
	worker.forwardStderr(strings.NewReader(
		"{\"event\":\"semantic-resolution\"}\n",
	))
	select {
	case <-operator.wrote:
	case <-time.After(time.Second):
		t.Fatal("provider diagnostic was not forwarded")
	}
	if !strings.Contains(operator.String(), `"event":"windows_provider_stderr"`) ||
		!strings.Contains(operator.String(), `semantic-resolution`) {
		t.Fatalf("operator diagnostic was not forwarded: %q", operator.String())
	}
	buffered, truncated := worker.stderr.snapshot()
	if truncated ||
		string(buffered) != "{\"event\":\"semantic-resolution\"}\n" {
		t.Fatalf(
			"provider stderr buffer=%q truncated=%v",
			buffered,
			truncated,
		)
	}
}

type notifyingWindowsProviderDiagnosticWriter struct {
	bytes.Buffer
	wrote chan struct{}
}

func (writer *notifyingWindowsProviderDiagnosticWriter) Write(data []byte) (int, error) {
	n, err := writer.Buffer.Write(data)
	select {
	case writer.wrote <- struct{}{}:
	default:
	}
	return n, err
}

func TestWindowsProviderBlockedDiagnosticsDoNotBlockTeardown(t *testing.T) {
	previousWriter := windowsProviderDiagnosticWriter
	previousSink := windowsProviderDiagnostics
	sink := newWindowsProviderDiagnosticQueue()
	blocked := &blockingWindowsDiagnosticWriter{
		started: make(chan struct{}), release: make(chan struct{}),
	}
	windowsProviderDiagnosticWriter = blocked
	windowsProviderDiagnostics = sink
	t.Cleanup(func() {
		windowsProviderDiagnosticWriter = previousWriter
		windowsProviderDiagnostics = previousSink
		close(blocked.release)
		close(sink.queue)
	})
	writeWindowsProviderLifecycleDiagnostic("test", time.Second, "blocked consumer")
	select {
	case <-blocked.started:
	case <-time.After(time.Second):
		t.Fatal("diagnostic consumer did not block")
	}
	done := make(chan struct{})
	close(done)
	manager := &windowsProviderProcessManager{worker: &windowsProviderWorker{
		stdin:      windowsNopWriteCloser{Writer: io.Discard},
		stderr:     &boundedProviderBuffer{limit: 1024},
		done:       done,
		stderrDone: make(chan struct{}),
	}}
	_, _ = manager.worker.stderr.Write([]byte("provider diagnostic"))
	completed := make(chan struct{})
	go func() {
		manager.worker.forwardStderr(strings.NewReader(strings.Repeat("provider diagnostic\n", 100)))
		for index := 0; index < 20; index++ {
			manager.recordStderrLocked(validWindowsObserveAppsRequest("blocked-stderr"), io.EOF)
		}
		manager.stopLocked()
		close(completed)
	}()
	select {
	case <-completed:
	case <-time.After(time.Second):
		t.Fatal("stderr backpressure prevented provider teardown")
	}
	if manager.worker != nil || len(sink.queue) != cap(sink.queue) || sink.dropped.Load() == 0 {
		t.Fatal("diagnostic queue was not bounded while teardown completed")
	}
}

type blockingWindowsDiagnosticWriter struct {
	started chan struct{}
	release chan struct{}
	once    sync.Once
}

func (writer *blockingWindowsDiagnosticWriter) Write(data []byte) (int, error) {
	writer.once.Do(func() { close(writer.started) })
	<-writer.release
	return len(data), nil
}

func TestWindowsProviderDoesNotReplaceWorkerBeforeConfirmedExit(t *testing.T) {
	t.Setenv("COMPUTER_USE_WINDOWS_PROVIDER", currentWindowsTestExecutable(t))
	t.Setenv("GO_WANT_WINDOWS_PROVIDER_HELPER", "reuse")
	useWindowsProviderTestCommand(t)
	retired := &windowsProviderWorker{done: make(chan struct{})}
	// Model a completed bounded teardown whose process has not yet exited.
	retired.stopOnce.Do(func() {})
	manager := &windowsProviderProcessManager{worker: retired}
	t.Cleanup(manager.stopLocked)
	manager.stopLocked()
	if manager.retiringWorker != retired {
		t.Fatal("unconfirmed process exit was discarded")
	}
	_, err := manager.run(validWindowsObserveAppsRequest("blocked-replacement"))
	var transport *core.RuntimeTransportError
	if !errors.As(err, &transport) || transport.Phase != "stop_provider" || transport.RequestSent {
		t.Fatalf("unconfirmed retirement result = %#v", err)
	}
	if manager.worker != nil {
		t.Fatal("replacement started before previous worker exited")
	}
	close(retired.done)
	if _, err := manager.run(validWindowsObserveAppsRequest("after-exit")); err != nil {
		t.Fatalf("confirmed exit did not permit replacement: %v", err)
	}
	if manager.retiringWorker != nil || manager.worker == nil {
		t.Fatal("retirement was not cleared after confirmed exit")
	}
}

func TestWindowsProviderValidatesResponseIdentity(t *testing.T) {
	for _, field := range []string{"request_id", "operation"} {
		t.Run(field, func(t *testing.T) {
			request := validWindowsObserveAppsRequest("identity")
			response := core.ProviderWireResponse{
				Protocol:      core.ProviderProtocolName,
				SchemaVersion: core.ProviderProtocolVersion,
				RequestID:     request.RequestID,
				Operation:     request.Operation,
				Accessibility: &core.ProviderAccessibilityResult{OK: true},
			}
			if field == "request_id" {
				response.RequestID = "other"
			} else {
				response.Operation = core.ProviderWireObserve
			}
			data, err := json.Marshal(response)
			if err != nil {
				t.Fatal(err)
			}
			worker := &windowsProviderWorker{
				stdin:      windowsNopWriteCloser{Writer: io.Discard},
				stdout:     bufio.NewReader(strings.NewReader(string(data) + "\n")),
				done:       make(chan struct{}),
				stderrDone: make(chan struct{}),
			}
			if _, err := worker.call(request, time.Second, nil); err == nil {
				t.Fatalf("%s mismatch must fail", field)
			} else {
				var transport *core.RuntimeTransportError
				if !errors.As(err, &transport) ||
					!transport.RequestSent ||
					transport.Phase == "" {
					t.Fatalf("%s mismatch evidence = %#v", field, err)
				}
			}
		})
	}
}

func TestWindowsProviderTransportPreservesPartialWriteEvidence(t *testing.T) {
	request := validWindowsObserveAppsRequest("partial-write")
	worker := &windowsProviderWorker{
		stdin:      partialWindowsWriteCloser{},
		stdout:     bufio.NewReader(strings.NewReader("")),
		done:       make(chan struct{}),
		stderrDone: make(chan struct{}),
	}
	_, err := worker.call(request, time.Second, nil)
	var transport *core.RuntimeTransportError
	if !errors.As(err, &transport) {
		t.Fatalf("partial write error = %v, want transport error", err)
	}
	if !transport.RequestSent || transport.Phase != "write_request" {
		t.Fatalf(
			"transport evidence requestSent=%v phase=%q",
			transport.RequestSent,
			transport.Phase,
		)
	}
}

func TestWindowsProviderResetRetryClassification(t *testing.T) {
	readOnly := []core.ProviderWireRequest{
		validWindowsObserveAppsRequest("apps"),
		func() core.ProviderWireRequest {
			request := core.NewProviderWireRequest("observe", core.ProviderWireObserve)
			request.Observe = &core.ProviderObserveRequest{
				ProtocolVersion: core.ProviderProtocolVersion,
				App:             "Editor",
			}
			return request
		}(),
		func() core.ProviderWireRequest {
			request := core.NewProviderWireRequest("windows", core.ProviderWireWindowList)
			request.WindowList = &core.ProviderWindowListRequest{
				ProtocolVersion: core.ProviderProtocolVersion,
				App:             "Editor",
			}
			return request
		}(),
		func() core.ProviderWireRequest {
			request := core.NewProviderWireRequest("resolve", core.ProviderWireResolveAndAct)
			request.Action = &core.ProviderActionRequest{
				ProtocolVersion: core.ProviderProtocolVersion,
				Intent:          core.AccessibilityResolveTarget,
				App:             "Editor",
				Target: &core.ProviderSemanticTarget{
					Selector: `button "Save"`,
					Role:     "button",
					Name:     "Save",
					HasName:  true,
				},
			}
			return request
		}(),
	}
	response := &core.ProviderWireResponse{
		Accessibility: &core.ProviderAccessibilityResult{OK: false},
	}
	for _, request := range readOnly {
		if !windowsProviderRequestCanRetryAfterReset(request, response) {
			t.Errorf("read-only operation %q was not retryable", request.Operation)
		}
	}

	mutation := core.NewProviderWireRequest("click", core.ProviderWireResolveAndAct)
	mutation.Action = &core.ProviderActionRequest{
		ProtocolVersion: core.ProviderProtocolVersion,
		Intent:          core.AccessibilityClick,
		App:             "Editor",
		Target: &core.ProviderSemanticTarget{
			Selector: `button "Save"`,
			Role:     "button",
			Name:     "Save",
			HasName:  true,
		},
		Click: &core.ProviderClickInput{ClickCount: 1},
	}
	if windowsProviderRequestCanRetryAfterReset(mutation, response) {
		t.Fatal("mutation request must never be replayed")
	}
	response.Accessibility.Mutation = &core.ProviderMutationFacts{
		MutationUnknown: true,
	}
	if windowsProviderRequestCanRetryAfterReset(readOnly[0], response) {
		t.Fatal("response with possible mutation must never be replayed")
	}
}

func TestWindowsProviderProtocolHelper(t *testing.T) {
	mode := os.Getenv("GO_WANT_WINDOWS_PROVIDER_HELPER")
	if mode == "" {
		return
	}
	counterPath := os.Getenv("WINDOWS_PROVIDER_START_COUNTER")
	if counterPath != "" {
		file, err := os.OpenFile(
			counterPath,
			os.O_CREATE|os.O_APPEND|os.O_WRONLY,
			0o600,
		)
		if err != nil {
			os.Exit(2)
		}
		_, _ = file.WriteString("start\n")
		_ = file.Close()
	}
	scanner := bufio.NewScanner(os.Stdin)
	for scanner.Scan() {
		request, err := providerDecodeProviderWireRequest(scanner.Bytes())
		if err != nil {
			os.Exit(3)
		}
		if requestPath := os.Getenv("WINDOWS_PROVIDER_REQUEST_COUNTER"); requestPath != "" {
			file, err := os.OpenFile(
				requestPath,
				os.O_CREATE|os.O_APPEND|os.O_WRONLY,
				0o600,
			)
			if err != nil {
				os.Exit(6)
			}
			_, _ = file.WriteString(request.RequestID + "\n")
			_ = file.Close()
		}
		starts := providerHelperStartCount(counterPath)
		if mode == "side-effect-timeout" &&
			request.Operation == core.ProviderWireResolveAndAct {
			effectPath := os.Getenv("WINDOWS_PROVIDER_EFFECT_STATE")
			if effectPath == "" {
				os.Exit(7)
			}
			if err := os.WriteFile(effectPath, []byte("applied\n"), 0o600); err != nil {
				os.Exit(8)
			}
			select {}
		}
		if mode == "protocol-error" && starts == 1 {
			_, _ = os.Stdout.WriteString("not-json\n")
			os.Exit(0)
		}
		resetRequired := mode == "reset-mutation" ||
			(mode == "reset-read" && starts == 1)
		response := core.ProviderWireResponse{
			Protocol:              core.ProviderProtocolName,
			SchemaVersion:         core.ProviderProtocolVersion,
			RequestID:             request.RequestID,
			Operation:             request.Operation,
			ProviderResetRequired: resetRequired,
			Accessibility: &core.ProviderAccessibilityResult{
				OK: !resetRequired || mode == "reset-mutation",
			},
		}
		if mode == "side-effect-timeout" {
			effectPath := os.Getenv("WINDOWS_PROVIDER_EFFECT_STATE")
			state, err := os.ReadFile(effectPath)
			if err != nil || string(state) != "applied\n" {
				os.Exit(9)
			}
			response.Accessibility.Text = "effect=applied"
		}
		if mode == "reset-mutation" {
			response.Accessibility.Mutation = &core.ProviderMutationFacts{
				MutationUnknown: true,
			}
		}
		if mode == "reject-request" {
			dispatchAttempted := false
			response.Accessibility = nil
			response.RequestRejection = &core.ProviderRequestRejection{
				Code: "invalid_arguments", Phase: "request_validation",
				DispatchAttempted: &dispatchAttempted, Message: "unsupported test field",
			}
		}
		data, err := json.Marshal(response)
		if err != nil {
			os.Exit(4)
		}
		_, _ = os.Stdout.Write(append(data, '\n'))
	}
	if counterPath != "" {
		file, err := os.OpenFile(
			counterPath,
			os.O_CREATE|os.O_APPEND|os.O_WRONLY,
			0o600,
		)
		if err != nil {
			os.Exit(5)
		}
		_, _ = file.WriteString("closed\n")
		_ = file.Close()
	}
	os.Exit(0)
}

func providerHelperStartCount(path string) int {
	data, err := os.ReadFile(path)
	if err != nil {
		return 0
	}
	return strings.Count(string(data), "start\n")
}

func validWindowsObserveAppsRequest(requestID string) core.ProviderWireRequest {
	request := core.NewProviderWireRequest(requestID, core.ProviderWireObserveApps)
	request.ObserveApps = &core.ProviderObserveAppsRequest{
		ProtocolVersion: core.ProviderProtocolVersion,
	}
	return request
}

func currentWindowsTestExecutable(t *testing.T) string {
	t.Helper()
	path, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	return path
}

func useWindowsProviderTestCommand(t *testing.T) {
	t.Helper()
	original := windowsProviderCommand
	windowsProviderCommand = func(path string) *exec.Cmd {
		return exec.Command(
			path,
			"-test.run=^TestWindowsProviderProtocolHelper$",
		)
	}
	t.Cleanup(func() {
		windowsProviderCommand = original
	})
}

type blockingWriteCloser struct{}

func (blockingWriteCloser) Write([]byte) (int, error) {
	select {}
}

func (blockingWriteCloser) Close() error {
	return nil
}

type blockingReader struct{}

func (blockingReader) Read([]byte) (int, error) {
	select {}
}

type windowsNopWriteCloser struct {
	io.Writer
}

func (windowsNopWriteCloser) Close() error {
	return nil
}

type partialWindowsWriteCloser struct{}

func (partialWindowsWriteCloser) Write(data []byte) (int, error) {
	if len(data) == 0 {
		return 0, io.ErrUnexpectedEOF
	}
	return 1, io.ErrUnexpectedEOF
}

func (partialWindowsWriteCloser) Close() error {
	return nil
}

type notifyingWindowsProviderWriteCloser struct {
	closed chan struct{}
}

func (*notifyingWindowsProviderWriteCloser) Write(data []byte) (int, error) {
	return len(data), nil
}

func (closer *notifyingWindowsProviderWriteCloser) Close() error {
	close(closer.closed)
	return nil
}
