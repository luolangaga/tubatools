//go:build windows || windows_sidecar_test

package provider

import (
	"bufio"
	"bytes"
	"encoding/json"
	"errors"
	"io"
	"reflect"
	"testing"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

func captureWindowsProviderTrace(t *testing.T, capacity int) *windowsProviderDiagnosticQueue {
	t.Helper()
	previous := windowsProviderDiagnostics
	sink := &windowsProviderDiagnosticQueue{queue: make(chan windowsProviderDiagnostic, capacity)}
	windowsProviderDiagnostics = sink
	t.Cleanup(func() { windowsProviderDiagnostics = previous })
	return sink
}

func TestWindowsProviderTraceRequiresOptInAndOmitsPayloads(t *testing.T) {
	sink := captureWindowsProviderTrace(t, 16)
	deadline := time.Now().Add(time.Second)
	for _, value := range []string{"", "0", "true"} {
		t.Setenv("COMPUTER_USE_WINDOWS_PROVIDER_TRACE", value)
		trace := newWindowsProviderTrace(core.ProviderWireObserve, deadline, time.Second)
		if trace != nil {
			t.Fatal("trace enabled without explicit opt-in")
		}
		trace.record("request_begin", nil, nil)
	}
	if len(sink.queue) != 0 {
		t.Fatal("disabled trace emitted a record")
	}
	t.Setenv("COMPUTER_USE_WINDOWS_PROVIDER_TRACE", "1")
	trace := newWindowsProviderTrace(core.ProviderWireObserve, deadline, time.Second)
	const private = "private-task-title-token-and-screenshot"
	response := &core.ProviderWireResponse{RequestID: private, ProviderResetRequired: true,
		Accessibility: &core.ProviderAccessibilityResult{Text: private, Error: private,
			Failure: &core.ProviderFailure{Code: "provider_internal_error", Subject: private}}}
	trace.forWorker(2).record("native_call_end", response, nil)
	trace.record("request_end", nil, errors.New(private))
	for len(sink.queue) > 0 {
		data := (<-sink.queue).data
		if bytes.Contains(data, []byte(private)) || len(data) > 1024 {
			t.Fatalf("trace exposed payload or exceeded bounded schema: %s", data)
		}
	}
}

func TestWindowsProviderTraceKeepsResetResponseAndOriginalDeadline(t *testing.T) {
	t.Setenv("COMPUTER_USE_WINDOWS_PROVIDER_TRACE", "1")
	t.Setenv("COMPUTER_USE_WINDOWS_PROVIDER", currentWindowsTestExecutable(t))
	t.Setenv("GO_WANT_WINDOWS_PROVIDER_HELPER", "reset-read")
	t.Setenv("WINDOWS_PROVIDER_START_COUNTER", t.TempDir()+"/starts")
	useWindowsProviderTestCommand(t)
	sink := captureWindowsProviderTrace(t, 64)
	manager := &windowsProviderProcessManager{}
	t.Cleanup(manager.stopLocked)
	request := validWindowsObserveAppsRequest("private-request-id")
	response, err := manager.run(request)
	if err != nil {
		t.Fatal(err)
	}
	want := &core.ProviderWireResponse{Protocol: core.ProviderProtocolName, SchemaVersion: core.ProviderProtocolVersion,
		RequestID: request.RequestID, Operation: request.Operation, Accessibility: &core.ProviderAccessibilityResult{OK: true}}
	if !reflect.DeepEqual(response, want) {
		t.Fatalf("trace changed final response: %#v", response)
	}
	var deadline string
	var sequence uint64
	var callBudgets []float64
	var resetGeneration uint64
	starts := map[uint64]int{}
	for len(sink.queue) > 0 {
		data := (<-sink.queue).data
		if bytes.Contains(data, []byte(request.RequestID)) {
			t.Fatal("request ID entered trace")
		}
		var event windowsProviderTraceEvent
		if err := json.Unmarshal(data, &event); err != nil {
			t.Fatal(err)
		}
		if event.Event != "windows_provider_transport" {
			continue
		}
		if deadline == "" {
			deadline, sequence = event.DeadlineUTC, event.Sequence
		}
		if event.DeadlineUTC != deadline || event.Sequence != sequence || event.BudgetMS != 30000 {
			t.Fatalf("retry changed the request deadline: %#v", event)
		}
		if event.Stage == "cmd_start_begin" {
			starts[event.Generation]++
		}
		if event.Stage == "request_write_begin" {
			callBudgets = append(callBudgets, event.CallBudgetMS)
		}
		if event.Stage == "native_call_end" && event.ResetRequired {
			resetGeneration = event.Generation
		}
	}
	if len(starts) != 2 || starts[1] != 1 || starts[2] != 1 || resetGeneration != 1 {
		t.Fatalf("lost initial reset or worker generation: starts=%v reset=%d", starts, resetGeneration)
	}
	if len(callBudgets) != 2 || callBudgets[1] >= callBudgets[0] || callBudgets[0] > 30000 {
		t.Fatalf("retry did not retain decreasing original budget: %v", callBudgets)
	}
}

func TestWindowsProviderTraceBackpressurePreservesSuccessAndTimeout(t *testing.T) {
	t.Setenv("COMPUTER_USE_WINDOWS_PROVIDER_TRACE", "1")
	sink := captureWindowsProviderTrace(t, 1)
	request := validWindowsObserveAppsRequest("private-request")
	want := core.ProviderWireResponse{Protocol: core.ProviderProtocolName, SchemaVersion: core.ProviderProtocolVersion,
		RequestID: request.RequestID, Operation: request.Operation, Accessibility: &core.ProviderAccessibilityResult{OK: true}}
	encoded, _ := json.Marshal(want)
	worker := &windowsProviderWorker{stdin: windowsNopWriteCloser{Writer: io.Discard}, stdout: bufio.NewReader(bytes.NewReader(append(encoded, '\n')))}
	trace := newWindowsProviderTrace(request.Operation, time.Now().Add(time.Second), time.Second)
	response, err := worker.call(request, time.Second, trace)
	if err != nil || !reflect.DeepEqual(response, &want) {
		t.Fatalf("blocked trace changed success: %#v %v", response, err)
	}
	reader, writer := io.Pipe()
	defer writer.Close()
	worker.stdout = bufio.NewReader(reader)
	budget := 20 * time.Millisecond
	started := time.Now()
	_, err = worker.call(request, budget, trace)
	var timeout *core.RuntimeTimeoutError
	if !errors.As(err, &timeout) || timeout.Duration != budget || timeout.Phase != "read_response" || timeout.Platform != "Windows" {
		t.Fatalf("trace changed timeout: %#v", err)
	}
	if time.Since(started) > time.Second || sink.dropped.Load() == 0 || len(sink.queue) != 1 {
		t.Fatal("trace backpressure changed the deadline or bypassed the bound")
	}
}

func TestWindowsProviderTracePreservesMutationAndRequestFailures(t *testing.T) {
	t.Setenv("COMPUTER_USE_WINDOWS_PROVIDER_TRACE", "1")
	captureWindowsProviderTrace(t, 64)
	t.Run("mutation is not replayed", TestWindowsProviderManagerNeverReplaysMutationReset)
	t.Run("request rejection preserves worker", TestWindowsProviderRequestRejectionKeepsWorker)
	t.Run("partial request write preserves input evidence", TestWindowsProviderTransportPreservesPartialWriteEvidence)
}
