package transport

import (
	"encoding/json"
	"os"
	"regexp"
	"strings"
	"testing"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

// These tests use the real socket transports with the deterministic desktop
// provider. No test sets registry counters to simulate an observation.
func startRefTestDaemon(t *testing.T, socket string) (*daemon, *fakeDesktopBackend, func()) {
	t.Helper()
	backend := newFakeDesktopBackend()
	server := newDaemon(socket, core.NewDesktopState(backend), []string{"DISPLAY=:9"}, time.Minute)
	if err := server.listen(); err != nil {
		t.Fatal(err)
	}
	done := make(chan error, 1)
	go func() { done <- server.serve() }()
	stop := func() {
		server.stop()
		if err := <-done; err != nil {
			t.Fatal(err)
		}
	}
	return server, backend, stop
}

func refTestCall(t *testing.T, client *daemonClient, transport string, args map[string]any) string {
	t.Helper()
	if transport == "call" {
		return contract.CompactJSON(callRecordOver(t, client, "AX", args, nil))
	}
	var response struct {
		Message json.RawMessage `json:"message"`
	}
	message := map[string]any{"jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": map[string]any{
		"name": "AX", "arguments": args,
	}}
	if err := client.request("mcp", map[string]any{"message": message}, &response); err != nil {
		t.Fatal(err)
	}
	return string(response.Message)
}

func responseRef(t *testing.T, response, prefix string) string {
	t.Helper()
	ref := regexp.MustCompile(`\b` + prefix + `[0-9]+\b`).FindString(response)
	if ref == "" {
		t.Fatalf("no %s ref in %s", prefix, response)
	}
	return ref
}

func refTestObserve(t *testing.T, client *daemonClient, transport string) map[string]string {
	t.Helper()
	apps := refTestCall(t, client, transport, map[string]any{"mode": "apps"})
	w := responseRef(t, apps, "w")
	observed := refTestCall(t, client, transport, map[string]any{"mode": "observe", "window_ref": w})
	return map[string]string{"w": w, "r": responseRef(t, observed, "r")}
}

func persistedRefCounters(t *testing.T, socket string) refCounters {
	t.Helper()
	data, err := os.ReadFile(socket + ".refs")
	if err != nil {
		t.Fatal(err)
	}
	var counters refCounters
	if err := json.Unmarshal(data, &counters); err != nil {
		t.Fatal(err)
	}
	return counters
}

func TestDaemonSocketRestartPreservesAllRefCounters(t *testing.T) {
	t.Setenv("COMPUTER_USE_ERROR_CONTEXT", "off")
	for _, before := range []string{"mcp", "call"} {
		for _, after := range []string{"mcp", "call"} {
			t.Run(before+"-to-"+after, func(t *testing.T) {
				socket := shortSocket(t)
				_, _, stopFirst := startRefTestDaemon(t, socket)
				first := testClient(t, socket, "first")
				old := refTestObserve(t, first, before)
				oldCounters := persistedRefCounters(t, socket)
				first.close()
				stopFirst()
				_, backend, stopSecond := startRefTestDaemon(t, socket)
				defer stopSecond()
				second := testClient(t, socket, "second")
				fresh := refTestObserve(t, second, after)
				newCounters := persistedRefCounters(t, socket)
				if oldCounters.App == 0 || oldCounters.Window == 0 || oldCounters.Element == 0 ||
					newCounters.App <= oldCounters.App || newCounters.Window <= oldCounters.Window || newCounters.Element <= oldCounters.Element {
					t.Fatalf("all three counters must advance: old=%+v new=%+v", oldCounters, newCounters)
				}
				for prefix, ref := range fresh {
					if ref == old[prefix] {
						t.Fatalf("%s reused after restart: old=%v fresh=%v", prefix, old, fresh)
					}
				}
				for _, transport := range []string{"mcp", "call"} {
					response := refTestCall(t, second, transport, map[string]any{"mode": "act", "actions": []any{
						map[string]any{"type": "click", "ref": old["r"]},
					}})
					if !strings.Contains(response, "stale_ref") || backend.inputCount() != 0 {
						t.Fatalf("old ref did not fail before input over %s: %s", transport, response)
					}
				}
			})
		}
	}
}

func TestAttachedMCPRejectsEnvironmentChangeBeforeInput(t *testing.T) {
	server, backend, socket := startInProcessDaemon(t, time.Minute)
	client := testClient(t, socket, "mcp")
	refs := refTestObserve(t, client, "mcp")
	server.desktop.Shared.Mu.Lock()
	server.currentEnvironment = func() string { return "different desktop" }
	server.desktop.Shared.Mu.Unlock()
	response := refTestCall(t, client, "mcp", map[string]any{"mode": "act", "actions": []any{
		map[string]any{"type": "click", "ref": refs["r"]},
	}})
	if !strings.Contains(response, "environment changed") || backend.inputCount() != 0 {
		t.Fatalf("changed desktop accepted: %s", response)
	}
}

func TestDaemonCounterSaveFailureWithholdsRefsAndBlocksInput(t *testing.T) {
	for _, transport := range []string{"mcp", "call"} {
		t.Run(transport, func(t *testing.T) {
			server, backend, socket := startInProcessDaemon(t, time.Minute)
			client := testClient(t, socket, transport)
			if err := os.Mkdir(server.countersPath(), 0o700); err != nil {
				t.Fatal(err)
			}
			response := refTestCall(t, client, transport, map[string]any{"mode": "apps"})
			if !strings.Contains(response, "could not be saved") || regexp.MustCompile(`\b[awr][0-9]+\b`).MatchString(response) {
				t.Fatalf("save failure leaked usable refs or hid error: %s", response)
			}
			for _, followup := range []string{"mcp", "call"} {
				response = refTestCall(t, client, followup, map[string]any{"mode": "act", "actions": []any{
					map[string]any{"type": "click", "ref": "r1"},
				}})
				if !strings.Contains(response, "could not be saved") || backend.inputCount() != 0 {
					t.Fatalf("input allowed after save error: %s", response)
				}
			}
		})
	}
}

func TestDaemonRejectsInvalidPersistedCounters(t *testing.T) {
	for _, data := range []string{`{"a":1`, `null`, `{}`, `{"a":1,"w":2,"r":null}`} {
		t.Run(data, func(t *testing.T) {
			socket := shortSocket(t)
			if err := os.WriteFile(socket+".refs", []byte(data), 0o600); err != nil {
				t.Fatal(err)
			}
			server := newDaemon(socket, core.NewDesktopState(newFakeDesktopBackend()), nil, time.Minute)
			if err := server.listen(); err == nil {
				server.stop()
				t.Fatal("invalid counter state started a daemon")
			}
		})
	}
}

// Input causes a new accessible element to appear. Saving the resulting
// observation can fail after the input, and must not erase that fact.
type changingRefBackend struct{ *fakeDesktopBackend }

func (b *changingRefBackend) updateSnapshot(result core.ProviderAccessibilityResult) core.ProviderAccessibilityResult {
	if b.inputCount() > 0 && result.Snapshot != nil {
		result.Snapshot.Elements[3].ProviderObjectPath += "/replacement"
		result.Snapshot.Elements[3].RuntimeID = []int{9000}
		result.Snapshot.Elements[3].Name = "After click"
	}
	return result
}

func (b *changingRefBackend) Observe(request core.ProviderObserveRequest) (core.ProviderAccessibilityResult, error) {
	result, err := b.fakeDesktopBackend.Observe(request)
	return b.updateSnapshot(result), err
}

func (b *changingRefBackend) ResolveAndAct(request core.ProviderActionRequest) (core.ProviderAccessibilityResult, error) {
	result, err := b.fakeDesktopBackend.ResolveAndAct(request)
	return b.updateSnapshot(result), err
}

func TestDaemonCounterSaveFailurePreservesInputEvidence(t *testing.T) {
	t.Setenv("COMPUTER_USE_ERROR_CONTEXT", "off")
	for _, transport := range []string{"mcp", "call", "text"} {
		t.Run(transport, func(t *testing.T) {
			socket := shortSocket(t)
			backend := &changingRefBackend{newFakeDesktopBackend()}
			server := newDaemon(socket, core.NewDesktopState(backend), []string{"DISPLAY=:9"}, time.Minute)
			if err := server.listen(); err != nil {
				t.Fatal(err)
			}
			done := make(chan error, 1)
			go func() { done <- server.serve() }()
			defer func() { server.stop(); <-done }()
			client := testClient(t, socket, transport)
			ref := firstRef(t, observeMain(t, client))
			if err := os.Remove(server.countersPath()); err != nil {
				t.Fatal(err)
			}
			if err := os.Mkdir(server.countersPath(), 0o700); err != nil {
				t.Fatal(err)
			}
			args := map[string]any{"mode": "act", "observe": "full", "actions": []any{map[string]any{"type": "click", "ref": ref}}}
			var response string
			if transport == "text" {
				var result textCallResponse
				if err := client.request("call", map[string]any{"tool": "AX", "args": args, "format": contract.FormatText}, &result); err != nil {
					t.Fatal(err)
				}
				response = contract.CompactJSON(result)
			} else {
				response = refTestCall(t, client, transport, args)
			}
			plain := strings.ReplaceAll(response, `\"`, `"`)
			if !strings.Contains(plain, "could not be saved") || !strings.Contains(plain, `"input_emitted":true`) ||
				!strings.Contains(plain, `"completed":1`) || !strings.Contains(plain, `"requested":1`) || backend.inputCount() != 1 ||
				regexp.MustCompile(`\b[awr][0-9]+\b`).MatchString(response) {
				t.Fatalf("save failure lost input evidence or exposed refs: %s (inputs=%d)", response, backend.inputCount())
			}
		})
	}
}
