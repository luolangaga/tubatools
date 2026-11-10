//go:build linux

package provider

import (
	"bufio"
	"bytes"
	"encoding/json"
	"errors"
	"io"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

func writeFakeLinuxProvider(t *testing.T, source string) string {
	t.Helper()
	path := filepath.Join(t.TempDir(), "fake-provider.py")
	if err := os.WriteFile(path, []byte(source), 0o755); err != nil {
		t.Fatal(err)
	}
	return path
}

func TestLinuxProviderProcessIsReusedAcrossCalls(t *testing.T) {
	shutdownLinuxProviderProcess()
	t.Cleanup(shutdownLinuxProviderProcess)

	tempDir := t.TempDir()
	counterPath := filepath.Join(tempDir, "starts.txt")
	workerPath := writeFakeLinuxProvider(t, `#!/usr/bin/env python3
import json
import os
import sys

with open(os.environ["FAKE_PROVIDER_STARTS"], "a", encoding="utf-8") as out:
    out.write("start\n")

for line in sys.stdin:
    request = json.loads(line)
    response = {
        "protocol": "computer-use-provider",
        "schema_version": 2,
        "request_id": request["request_id"],
        "operation": request["operation"],
        "accessibility": {"ok": True},
    }
    print(json.dumps(response, separators=(",", ":")), flush=True)
`)
	t.Setenv("COMPUTER_USE_PYTHON_BINARY", workerPath)
	t.Setenv("FAKE_PROVIDER_STARTS", counterPath)

	request := core.NewProviderWireRequest("reuse-1", core.ProviderWireObserveApps)
	request.ObserveApps = &core.ProviderObserveAppsRequest{
		ProtocolVersion: core.ProviderProtocolVersion,
	}
	if _, err := runLinuxProvider(request, linuxRuntimeBackend{}); err != nil {
		t.Fatal(err)
	}
	request.RequestID = "reuse-2"
	if _, err := runLinuxProvider(request, linuxRuntimeBackend{}); err != nil {
		t.Fatal(err)
	}

	data, err := os.ReadFile(counterPath)
	if err != nil {
		t.Fatal(err)
	}
	if starts := strings.Count(string(data), "start\n"); starts != 1 {
		t.Fatalf("provider process starts = %d, want 1: %q", starts, data)
	}
}

func TestLinuxProviderProcessRestartsAfterProtocolFailure(t *testing.T) {
	shutdownLinuxProviderProcess()
	t.Cleanup(shutdownLinuxProviderProcess)

	counterPath := filepath.Join(t.TempDir(), "starts.txt")
	workerPath := writeFakeLinuxProvider(t, `#!/usr/bin/env python3
import json
import os
import sys

path = os.environ["FAKE_PROVIDER_STARTS"]
try:
    starts = int(open(path, encoding="utf-8").read())
except (FileNotFoundError, ValueError):
    starts = 0
starts += 1
with open(path, "w", encoding="utf-8") as out:
    out.write(str(starts))

for line in sys.stdin:
    request = json.loads(line)
    if starts == 1:
        print("not-json", flush=True)
        sys.exit(0)
    print(json.dumps({
        "protocol": "computer-use-provider",
        "schema_version": 2,
        "request_id": request["request_id"],
        "operation": request["operation"],
        "accessibility": {"ok": True},
    }, separators=(",", ":")), flush=True)
`)
	t.Setenv("COMPUTER_USE_PYTHON_BINARY", workerPath)
	t.Setenv("FAKE_PROVIDER_STARTS", counterPath)

	request := core.NewProviderWireRequest("restart-1", core.ProviderWireObserveApps)
	request.ObserveApps = &core.ProviderObserveAppsRequest{
		ProtocolVersion: core.ProviderProtocolVersion,
	}
	if _, err := runLinuxProvider(request, linuxRuntimeBackend{}); err == nil {
		t.Fatal("invalid worker response must fail")
	}
	request.RequestID = "restart-2"
	if _, err := runLinuxProvider(request, linuxRuntimeBackend{}); err != nil {
		t.Fatalf("replacement worker failed: %v", err)
	}
	data, err := os.ReadFile(counterPath)
	if err != nil {
		t.Fatal(err)
	}
	if strings.TrimSpace(string(data)) != "2" {
		t.Fatalf("provider process did not restart: %q", data)
	}
}

func TestLinuxProviderProcessRestartsAfterTimeout(t *testing.T) {
	shutdownLinuxProviderProcess()
	t.Cleanup(shutdownLinuxProviderProcess)

	counterPath := filepath.Join(t.TempDir(), "starts.txt")
	workerPath := writeFakeLinuxProvider(t, `#!/usr/bin/env python3
import json
import os
import sys
import time

path = os.environ["FAKE_PROVIDER_STARTS"]
try:
    starts = int(open(path, encoding="utf-8").read())
except (FileNotFoundError, ValueError):
    starts = 0
starts += 1
with open(path, "w", encoding="utf-8") as out:
    out.write(str(starts))

for line in sys.stdin:
    request = json.loads(line)
    if starts == 1:
        time.sleep(2)
    print(json.dumps({
        "protocol": "computer-use-provider",
        "schema_version": 2,
        "request_id": request["request_id"],
        "operation": request["operation"],
        "accessibility": {"ok": True},
    }, separators=(",", ":")), flush=True)
`)
	t.Setenv("COMPUTER_USE_PYTHON_BINARY", workerPath)
	t.Setenv("COMPUTER_USE_RUNTIME_TIMEOUT_SECONDS", "1")
	t.Setenv("FAKE_PROVIDER_STARTS", counterPath)

	request := core.NewProviderWireRequest("timeout-1", core.ProviderWireObserveApps)
	request.ObserveApps = &core.ProviderObserveAppsRequest{
		ProtocolVersion: core.ProviderProtocolVersion,
	}
	if _, err := runLinuxProvider(request, linuxRuntimeBackend{}); err == nil {
		t.Fatal("timed-out worker call must fail")
	}
	request.RequestID = "timeout-2"
	if _, err := runLinuxProvider(request, linuxRuntimeBackend{}); err != nil {
		t.Fatalf("replacement worker failed: %v", err)
	}
	data, err := os.ReadFile(counterPath)
	if err != nil {
		t.Fatal(err)
	}
	if strings.TrimSpace(string(data)) != "2" {
		t.Fatalf("provider process did not restart after timeout: %q", data)
	}
}

func TestLinuxProviderReadOnlyResetRestartsAndRetriesOnce(t *testing.T) {
	shutdownLinuxProviderProcess()
	t.Cleanup(shutdownLinuxProviderProcess)

	counterPath := filepath.Join(t.TempDir(), "starts.txt")
	workerPath := writeFakeLinuxProvider(t, `#!/usr/bin/env python3
import json
import os
import sys

path = os.environ["FAKE_PROVIDER_STARTS"]
try:
    starts = int(open(path, encoding="utf-8").read())
except (FileNotFoundError, ValueError):
    starts = 0
starts += 1
with open(path, "w", encoding="utf-8") as out:
    out.write(str(starts))

for line in sys.stdin:
    request = json.loads(line)
    print(json.dumps({
        "protocol": "computer-use-provider",
        "schema_version": 2,
        "request_id": request["request_id"],
        "operation": request["operation"],
        "provider_reset_required": starts == 1,
        "accessibility": {
            "ok": starts > 1,
            "failure": {
                "code": "observation_incomplete",
                "phase": "observe",
            } if starts == 1 else None,
        },
    }, separators=(",", ":")), flush=True)
`)
	t.Setenv("COMPUTER_USE_PYTHON_BINARY", workerPath)
	t.Setenv("FAKE_PROVIDER_STARTS", counterPath)

	request := core.NewProviderWireRequest("reset-read", core.ProviderWireObserveApps)
	request.ObserveApps = &core.ProviderObserveAppsRequest{
		ProtocolVersion: core.ProviderProtocolVersion,
	}
	response, err := runLinuxProvider(request, linuxRuntimeBackend{})
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
	if strings.TrimSpace(string(data)) != "2" {
		t.Fatalf("provider starts = %q, want 2", data)
	}
}

func TestLinuxProviderMutationResetDoesNotReplay(t *testing.T) {
	shutdownLinuxProviderProcess()
	t.Cleanup(shutdownLinuxProviderProcess)

	counterPath := filepath.Join(t.TempDir(), "starts.txt")
	requestPath := filepath.Join(t.TempDir(), "requests.txt")
	workerPath := writeFakeLinuxProvider(t, `#!/usr/bin/env python3
import json
import os
import sys

with open(os.environ["FAKE_PROVIDER_STARTS"], "a", encoding="utf-8") as out:
    out.write("start\n")
for line in sys.stdin:
    request = json.loads(line)
    with open(os.environ["FAKE_PROVIDER_REQUESTS"], "a", encoding="utf-8") as out:
        out.write(request["request_id"] + "\n")
    print(json.dumps({
        "protocol": "computer-use-provider",
        "schema_version": 2,
        "request_id": request["request_id"],
        "operation": request["operation"],
        "provider_reset_required": True,
        "accessibility": {
            "ok": False,
            "failure": {
                "code": "effect_unknown",
                "phase": "post_action_observation",
            },
            "mutation": {
                "input_events_emitted": True,
                "mutation_unknown": True,
            },
        },
    }, separators=(",", ":")), flush=True)
`)
	t.Setenv("COMPUTER_USE_PYTHON_BINARY", workerPath)
	t.Setenv("FAKE_PROVIDER_STARTS", counterPath)
	t.Setenv("FAKE_PROVIDER_REQUESTS", requestPath)

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
	response, err := runLinuxProvider(request, linuxRuntimeBackend{})
	if err != nil {
		t.Fatal(err)
	}
	if response.Accessibility == nil ||
		response.Accessibility.Mutation == nil ||
		!response.Accessibility.Mutation.InputEventsEmitted {
		t.Fatalf("mutation evidence = %#v", response)
	}
	requests, err := os.ReadFile(requestPath)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Count(string(requests), "reset-mutation\n") != 1 {
		t.Fatalf("mutation request was replayed: %q", requests)
	}
	starts, err := os.ReadFile(counterPath)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Count(string(starts), "start\n") != 1 {
		t.Fatalf("provider starts = %q, want 1", starts)
	}
}

func TestLinuxProviderActionEvidenceResetDoesNotReplay(t *testing.T) {
	shutdownLinuxProviderProcess()
	t.Cleanup(shutdownLinuxProviderProcess)

	counterPath := filepath.Join(t.TempDir(), "starts.txt")
	requestPath := filepath.Join(t.TempDir(), "requests.txt")
	workerPath := writeFakeLinuxProvider(t, `#!/usr/bin/env python3
import json
import os
import sys

with open(os.environ["FAKE_PROVIDER_STARTS"], "a", encoding="utf-8") as out:
    out.write("start\n")
for line in sys.stdin:
    request = json.loads(line)
    with open(os.environ["FAKE_PROVIDER_REQUESTS"], "a", encoding="utf-8") as out:
        out.write(request["request_id"] + "\n")
    print(json.dumps({
        "protocol": "computer-use-provider",
        "schema_version": 2,
        "request_id": request["request_id"],
        "operation": request["operation"],
        "provider_reset_required": True,
        "accessibility": {
            "ok": False,
            "failure": {
                "code": "effect_unknown",
                "phase": "post_action_observation",
            },
            "action_evidence": {
                "input_emitted": True,
            },
        },
    }, separators=(",", ":")), flush=True)
`)
	t.Setenv("COMPUTER_USE_PYTHON_BINARY", workerPath)
	t.Setenv("FAKE_PROVIDER_STARTS", counterPath)
	t.Setenv("FAKE_PROVIDER_REQUESTS", requestPath)

	request := core.NewProviderWireRequest(
		"reset-action-evidence",
		core.ProviderWireResolveAndAct,
	)
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
	response, err := runLinuxProvider(request, linuxRuntimeBackend{})
	if err != nil {
		t.Fatal(err)
	}
	if response.Accessibility == nil ||
		response.Accessibility.ActionEvidence == nil ||
		!response.Accessibility.ActionEvidence.InputEmitted {
		t.Fatalf("action evidence = %#v", response)
	}
	requests, err := os.ReadFile(requestPath)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Count(string(requests), "reset-action-evidence\n") != 1 {
		t.Fatalf("request with emitted input was replayed: %q", requests)
	}
	starts, err := os.ReadFile(counterPath)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Count(string(starts), "start\n") != 1 {
		t.Fatalf("provider starts = %q, want 1", starts)
	}
}

func TestLinuxProviderStderrRemainsOperatorOnly(t *testing.T) {
	shutdownLinuxProviderProcess()
	t.Cleanup(shutdownLinuxProviderProcess)

	tracePath := filepath.Join(t.TempDir(), "operator.jsonl")
	workerPath := writeFakeLinuxProvider(t, `#!/usr/bin/env python3
import json
import sys

for line in sys.stdin:
    request = json.loads(line)
    print(json.dumps({
        "kind": "linux_provider_diagnostic",
        "request_id": request["request_id"],
        "operation": request["operation"],
        "stage": "dispatch",
        "diagnostic": "Traceback: /secret/provider.py",
    }, separators=(",", ":")), file=sys.stderr, flush=True)
    print(json.dumps({
        "protocol": "computer-use-provider",
        "schema_version": 2,
        "request_id": request["request_id"],
        "operation": request["operation"],
        "accessibility": {"ok": True, "text": "safe model response"},
    }, separators=(",", ":")), flush=True)
`)
	t.Setenv("COMPUTER_USE_PYTHON_BINARY", workerPath)
	t.Setenv("COMPUTER_USE_TRACE_FILE", tracePath)

	request := core.NewProviderWireRequest("stderr-1", core.ProviderWireObserveApps)
	request.ObserveApps = &core.ProviderObserveAppsRequest{
		ProtocolVersion: core.ProviderProtocolVersion,
	}
	response, err := runLinuxProvider(request, linuxRuntimeBackend{})
	if err != nil {
		t.Fatal(err)
	}
	encoded, err := json.Marshal(response)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(string(encoded), "Traceback") ||
		strings.Contains(string(encoded), "/secret/provider.py") {
		t.Fatalf("provider diagnostic leaked into response: %s", encoded)
	}

	deadline := time.Now().Add(time.Second)
	for {
		data, readErr := os.ReadFile(tracePath)
		if readErr == nil && strings.Contains(string(data), "/secret/provider.py") {
			break
		}
		if time.Now().After(deadline) {
			t.Fatalf("operator trace did not receive provider diagnostic: %v", readErr)
		}
		time.Sleep(10 * time.Millisecond)
	}
}

func TestLinuxProviderDiagnosticIsBoundedAndRetainsOriginalLength(t *testing.T) {
	tracePath := filepath.Join(t.TempDir(), "operator.jsonl")
	t.Setenv("COMPUTER_USE_TRACE_FILE", tracePath)

	raw := strings.Repeat("diagnostic-", 1<<15)
	writeLinuxProviderDiagnostic(map[string]any{
		"request_id": strings.Repeat("request-", 100),
		"operation":  strings.Repeat("operation-", 100),
		"stage":      strings.Repeat("stage-", 100),
		"diagnostic": raw,
	})

	data, err := os.ReadFile(tracePath)
	if err != nil {
		t.Fatal(err)
	}
	if len(data) > linuxProviderDiagnosticLimit+4096 {
		t.Fatalf("bounded diagnostic record is too large: %d bytes", len(data))
	}
	var record map[string]any
	if err := json.Unmarshal(bytes.TrimSpace(data), &record); err != nil {
		t.Fatal(err)
	}
	if record["diagnostic_truncated"] != true {
		t.Fatalf("diagnostic_truncated = %#v", record["diagnostic_truncated"])
	}
	if int(record["diagnostic_original_bytes"].(float64)) != len(raw) {
		t.Fatalf(
			"diagnostic_original_bytes = %#v, want %d",
			record["diagnostic_original_bytes"],
			len(raw),
		)
	}
	if _, exists := record["diagnostic_sha256"]; exists {
		t.Fatal("diagnostic record contains an unused digest")
	}
	for _, field := range []string{"request_id", "operation", "stage"} {
		if len(record[field].(string)) > linuxProviderDiagnosticLabelLimit {
			t.Fatalf("%s was not bounded: %d", field, len(record[field].(string)))
		}
	}
}

func TestLinuxRuntimeDiagnosticBytesRetainsOriginalLength(t *testing.T) {
	tracePath := filepath.Join(t.TempDir(), "operator.jsonl")
	t.Setenv("COMPUTER_USE_TRACE_FILE", tracePath)
	raw := bytes.Repeat([]byte("error details\n"), linuxProviderDiagnosticLimit)
	request := core.NewProviderWireRequest("malformed-response", core.ProviderWireObserveApps)
	writeLinuxRuntimeDiagnosticBytes(request, "decode_response", raw)

	data, err := os.ReadFile(tracePath)
	if err != nil {
		t.Fatal(err)
	}
	var record map[string]any
	if err := json.Unmarshal(data, &record); err != nil {
		t.Fatal(err)
	}
	if record["diagnostic"] != strings.TrimSpace(string(raw[:linuxProviderDiagnosticLimit])) ||
		record["diagnostic_original_bytes"] != float64(len(raw)) ||
		record["diagnostic_truncated"] != true ||
		record["request_id"] != request.RequestID ||
		record["stage"] != "decode_response" {
		t.Fatal("byte diagnostic lost its bounded sample, length or error context")
	}
	if _, exists := record["diagnostic_sha256"]; exists {
		t.Fatal("byte diagnostic contains an unused digest")
	}
}

func TestLinuxProviderWriteUsesCallDeadline(t *testing.T) {
	shutdownLinuxProviderProcess()
	t.Cleanup(shutdownLinuxProviderProcess)

	workerPath := writeFakeLinuxProvider(t, `#!/usr/bin/env python3
import time
time.sleep(10)
`)
	t.Setenv("COMPUTER_USE_PYTHON_BINARY", workerPath)
	worker, err := startLinuxProviderWorker(os.Environ())
	if err != nil {
		t.Fatal(err)
	}
	defer worker.stop()

	request := core.NewProviderWireRequest("write-timeout", core.ProviderWireObserve)
	request.Observe = &core.ProviderObserveRequest{
		ProtocolVersion: core.ProviderProtocolVersion,
		App:             strings.Repeat("application-", 1<<18),
	}
	started := time.Now()
	_, err = worker.call(request, 100*time.Millisecond)
	var timeoutErr *core.RuntimeTimeoutError
	if !errors.As(err, &timeoutErr) {
		t.Fatalf("blocked request write error = %v, want runtime timeout", err)
	}
	if elapsed := time.Since(started); elapsed > time.Second {
		t.Fatalf("blocked request write ignored call deadline: %s", elapsed)
	}
}

func TestReadLinuxProviderLineRejectsOversizeBeforeNewline(t *testing.T) {
	reader := bufio.NewReaderSize(
		strings.NewReader(strings.Repeat("x", 64)+"\n"),
		8,
	)
	line, err := readLinuxProviderLine(reader, 32)
	if !errors.Is(err, errLinuxProviderResponseTooLarge) {
		t.Fatalf("oversized response error = %v", err)
	}
	if line != nil {
		t.Fatalf("oversized response retained %d bytes", len(line))
	}
	if buffered := reader.Buffered(); buffered > reader.Size() {
		t.Fatalf("reader buffered %d bytes beyond capacity %d", buffered, reader.Size())
	}
}

func TestLinuxProviderStderrDrainsOversizeAndRetainsStreamError(t *testing.T) {
	tracePath := filepath.Join(t.TempDir(), "operator.jsonl")
	t.Setenv("COMPUTER_USE_TRACE_FILE", tracePath)
	reader, writer := io.Pipe()
	defer reader.Close()
	raw := strings.Repeat("x", maxLinuxProviderStderrLineBytes+1024)
	go func() {
		_, _ = io.WriteString(writer, raw+"\nlast diagnostic")
		_ = writer.CloseWithError(errors.New("diagnostic stream interrupted"))
	}()
	worker := &linuxProviderWorker{stderrDone: make(chan struct{})}
	worker.forwardStderr(reader)
	select {
	case <-worker.stderrDone:
	default:
		t.Fatal("stderr completion was not signalled")
	}

	data, err := os.ReadFile(tracePath)
	if err != nil {
		t.Fatal(err)
	}
	lines := bytes.Split(bytes.TrimSpace(data), []byte("\n"))
	if len(lines) != 3 {
		t.Fatalf("got %d diagnostic records, want oversized line, final line and stream error", len(lines))
	}
	for i, line := range lines {
		var record map[string]any
		if err := json.Unmarshal(line, &record); err != nil {
			t.Fatal(err)
		}
		if _, exists := record["diagnostic_sha256"]; exists {
			t.Fatal("stream diagnostic contains an unused digest")
		}
		switch i {
		case 0:
			if record["diagnostic"] != raw[:linuxProviderDiagnosticLimit] ||
				record["diagnostic_original_bytes"] != float64(len(raw)) ||
				record["diagnostic_truncated"] != true {
				t.Fatal("oversized stream diagnostic lost sample, original length or truncation")
			}
		case 1:
			if record["diagnostic"] != "last diagnostic" {
				t.Fatal("oversized line was not fully drained before the final line")
			}
		case 2:
			if record["diagnostic"] != "provider stderr stream failed: diagnostic stream interrupted" {
				t.Fatal("stream error evidence was lost")
			}
		}
	}
}

func TestLinuxProviderStopAllowsGracefulEOFExit(t *testing.T) {
	markerPath := filepath.Join(t.TempDir(), "graceful.txt")
	workerPath := writeFakeLinuxProvider(t, `#!/usr/bin/env python3
import os
import sys

for _line in sys.stdin:
    pass
with open(os.environ["FAKE_PROVIDER_GRACEFUL_EXIT"], "w", encoding="utf-8") as out:
    out.write("closed")
`)
	t.Setenv("COMPUTER_USE_PYTHON_BINARY", workerPath)
	t.Setenv("FAKE_PROVIDER_GRACEFUL_EXIT", markerPath)
	worker, err := startLinuxProviderWorker(os.Environ())
	if err != nil {
		t.Fatal(err)
	}

	worker.stop()
	data, err := os.ReadFile(markerPath)
	if err != nil {
		t.Fatalf("provider did not complete graceful EOF shutdown: %v", err)
	}
	if string(data) != "closed" {
		t.Fatalf("graceful shutdown marker = %q", data)
	}
}

func TestLinuxProviderStopDoesNotWaitForeverAfterKill(t *testing.T) {
	worker := &linuxProviderWorker{
		stdin:      nopWriteCloser{Writer: io.Discard},
		done:       make(chan struct{}),
		stderrDone: make(chan struct{}),
	}
	started := time.Now()
	worker.stop()
	if elapsed := time.Since(started); elapsed > time.Second {
		t.Fatalf("worker stop exceeded bounded kill wait: %s", elapsed)
	}
}

type nopWriteCloser struct {
	io.Writer
}

func (nopWriteCloser) Close() error {
	return nil
}

func processPIDExists(pid int, procRoot string) bool {
	state := processPIDState(pid, procRoot)
	return state != "missing" && state != "zombie" && state != "exited"
}

func TestRuntimeStderrGoesToOperatorTrace(t *testing.T) {
	path := filepath.Join(t.TempDir(), "operator.jsonl")
	t.Setenv("COMPUTER_USE_TRACE_FILE", path)
	request := core.NewProviderWireRequest("req-secret", core.ProviderWireObserve)
	diagnostic := "Traceback (most recent call last):\n  File \"/secret/runtime.py\""
	writeLinuxRuntimeDiagnostic(request, "stderr", diagnostic)

	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	var record map[string]any
	if err := json.Unmarshal(bytes.TrimSpace(data), &record); err != nil {
		t.Fatalf("operator trace must contain valid JSONL: %v: %q", err, data)
	}
	if record["diagnostic"] != diagnostic ||
		record["kind"] != "linux_provider_diagnostic" {
		t.Fatalf("operator trace must retain the full diagnostic: %#v", record)
	}

}
