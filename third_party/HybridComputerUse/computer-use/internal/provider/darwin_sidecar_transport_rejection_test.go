//go:build darwin || darwin_sidecar_test

package provider

import (
	"bufio"
	"bytes"
	"encoding/json"
	"errors"
	"io"
	"os"
	"strings"
	"testing"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

func TestDarwinRequestRejectionNativeWireReplay(t *testing.T) {
	path := os.Getenv("COMPUTER_USE_NATIVE_REJECTION_WIRE_FILE")
	if path == "" {
		t.Skip("requires separately bound signed native wire evidence")
	}
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	replayDarwinRequestRejectionWire(t, data)
}

func TestDarwinRequestRejectionPrettyCorpusUsesSingleLineFrames(t *testing.T) {
	request := validDarwinObserveAppsRequest("pretty-corpus")
	response := core.ProviderWireResponse{
		Protocol: core.ProviderProtocolName, SchemaVersion: core.ProviderProtocolVersion,
		RequestID: request.RequestID, Operation: request.Operation,
		RequestRejection: darwinTestRequestRejection(),
	}
	data, err := json.MarshalIndent([]any{
		map[string]any{"request": request, "response": response},
	}, "", "  ")
	if err != nil {
		t.Fatal(err)
	}
	replayDarwinRequestRejectionWire(t, data)
}

func replayDarwinRequestRejectionWire(t *testing.T, data []byte) {
	t.Helper()
	var events []struct {
		Request  json.RawMessage `json:"request"`
		Response json.RawMessage `json:"response"`
	}
	if err := json.Unmarshal(data, &events); err != nil || len(events) == 0 {
		t.Fatalf("native wire corpus missing: %v", err)
	}
	for _, event := range events {
		var request core.ProviderWireRequest
		if err := json.Unmarshal(event.Request, &request); err != nil {
			t.Fatal(err)
		}
		// Corpus formatting is not wire framing: each response is one JSON line.
		var line bytes.Buffer
		if err := json.Compact(&line, event.Response); err != nil {
			t.Fatal(err)
		}
		line.WriteByte('\n')
		connection := &darwinTestConnection{
			writer: io.Discard, reader: &line,
		}
		worker := &darwinProviderWorker{connection: connection, stdout: bufio.NewReader(connection)}
		_, err := worker.call(request, time.Second)
		var rejection *core.RuntimeRequestRejectionError
		if !errors.As(err, &rejection) {
			t.Fatalf("native %s rejection not recognized: %v", request.Operation, err)
		}
	}
}

func TestDarwinRequestRejectionRequiresCorrelation(t *testing.T) {
	for _, mismatch := range []string{"", "request_id", "operation", "invalid_dispatch"} {
		request := validDarwinObserveAppsRequest("bound-request")
		response := core.ProviderWireResponse{
			Protocol: core.ProviderProtocolName, SchemaVersion: core.ProviderProtocolVersion,
			RequestID: request.RequestID, Operation: request.Operation,
			RequestRejection: darwinTestRequestRejection(),
		}
		switch mismatch {
		case "request_id":
			response.RequestID = "other"
		case "operation":
			response.Operation = core.ProviderWireOrderedInput
		case "invalid_dispatch":
			response.RequestRejection.DispatchAttempted = nil
		}
		data, _ := json.Marshal(response)
		connection := &darwinTestConnection{writer: io.Discard, reader: strings.NewReader(string(data) + "\n")}
		worker := &darwinProviderWorker{connection: connection, stdout: bufio.NewReader(connection)}
		_, err := worker.call(request, time.Second)
		var rejected *core.RuntimeRequestRejectionError
		var transport *core.RuntimeTransportError
		if mismatch == "" {
			if !errors.As(err, &rejected) || errors.As(err, &transport) {
				t.Fatalf("correlated rejection misclassified: %v", err)
			}
		} else if !errors.As(err, &transport) || !transport.RequestSent {
			t.Fatalf("%s contradiction reduced to no-input: %v", mismatch, err)
		}
	}
}

func TestDarwinRequestRejectionKeepsWorkerAndDoesNotReplay(t *testing.T) {
	request := validDarwinObserveAppsRequest("reject")
	originalLookup, originalStarter := darwinProviderArtifactLookup, darwinProviderWorkerStarter
	t.Cleanup(func() {
		darwinProviderArtifactLookup, darwinProviderWorkerStarter = originalLookup, originalStarter
	})
	darwinProviderArtifactLookup = func() (darwinProviderArtifact, error) {
		return darwinProviderArtifact{bundlePath: "/test"}, nil
	}
	starts := 0
	darwinProviderWorkerStarter = func(darwinProviderArtifact) (*darwinProviderWorker, error) {
		starts++
		rejection := core.ProviderWireResponse{
			Protocol: core.ProviderProtocolName, SchemaVersion: core.ProviderProtocolVersion,
			RequestID: "reject", Operation: core.ProviderWireObserveApps,
			RequestRejection: darwinTestRequestRejection(),
		}
		success := core.ProviderWireResponse{
			Protocol: core.ProviderProtocolName, SchemaVersion: core.ProviderProtocolVersion,
			RequestID: "next", Operation: core.ProviderWireObserveApps,
			Accessibility: &core.ProviderAccessibilityResult{OK: true},
		}
		a, _ := json.Marshal(rejection)
		b, _ := json.Marshal(success)
		connection := &darwinTestConnection{writer: io.Discard, reader: strings.NewReader(string(a) + "\n" + string(b) + "\n")}
		done, stderrDone := make(chan struct{}), make(chan struct{})
		close(done)
		close(stderrDone)
		return &darwinProviderWorker{connection: connection, stdout: bufio.NewReader(connection),
			done: done, stderrDone: stderrDone}, nil
	}
	manager := &darwinProviderProcessManager{}
	defer manager.stopLocked()
	_, err := manager.run(request)
	var rejection *core.RuntimeRequestRejectionError
	if !errors.As(err, &rejection) || manager.worker == nil || starts != 1 {
		t.Fatalf("rejection restarted/retried worker: %v starts=%d", err, starts)
	}
	response, err := manager.run(validDarwinObserveAppsRequest("next"))
	if err != nil || response.Accessibility == nil || !response.Accessibility.OK || starts != 1 {
		t.Fatalf("worker failed subsequent independent request: %#v %v starts=%d", response, err, starts)
	}
}

func darwinTestRequestRejection() *core.ProviderRequestRejection {
	dispatched := false
	return &core.ProviderRequestRejection{
		Code: "invalid_arguments", Phase: "request_validation",
		DispatchAttempted: &dispatched, Message: "unsupported key",
	}
}
