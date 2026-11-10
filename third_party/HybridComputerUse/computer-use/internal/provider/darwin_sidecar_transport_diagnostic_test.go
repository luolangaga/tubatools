//go:build darwin || darwin_sidecar_test

package provider

import (
	"bufio"
	"bytes"
	"encoding/json"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

func TestDarwinActionDiagnosticsRequireHostOptIn(t *testing.T) {
	for _, enabled := range []bool{false, true} {
		t.Run(map[bool]string{false: "disabled", true: "enabled"}[enabled], func(t *testing.T) {
			t.Setenv("COMPUTER_USE_DIAGNOSTIC_TIMING", "")
			if enabled {
				t.Setenv("COMPUTER_USE_DIAGNOSTIC_TIMING", "1")
			}
			shutdownDarwinProviderProcess()
			lookup, starter := darwinProviderArtifactLookup, darwinProviderWorkerStarter
			t.Cleanup(func() {
				shutdownDarwinProviderProcess()
				darwinProviderArtifactLookup, darwinProviderWorkerStarter = lookup, starter
			})
			lookups := 0
			darwinProviderArtifactLookup = func() (darwinProviderArtifact, error) {
				lookups++
				return darwinProviderArtifact{bundlePath: "/test/ComputerUseProvider.app"}, nil
			}
			request := core.NewProviderWireRequest("diagnostics", core.ProviderWireResolveAndAct)
			request.Action = &core.ProviderActionRequest{
				ProtocolVersion: core.ProviderProtocolVersion, Intent: core.AccessibilityClick, App: "Editor",
				Target:      &core.ProviderSemanticTarget{Selector: `button "Save"`, Role: "button", Name: "Save", HasName: true},
				Click:       &core.ProviderClickInput{ClickCount: 1},
				Diagnostics: &core.ProviderDiagnosticContext{PhaseTiming: true},
			}
			var sent bytes.Buffer
			darwinProviderWorkerStarter = func(darwinProviderArtifact) (*darwinProviderWorker, error) {
				response := core.ProviderWireResponse{
					Protocol: core.ProviderProtocolName, SchemaVersion: core.ProviderProtocolVersion,
					RequestID: request.RequestID, Operation: request.Operation,
					Accessibility: &core.ProviderAccessibilityResult{OK: true},
				}
				data, err := json.Marshal(response)
				if err != nil {
					t.Fatal(err)
				}
				connection := &darwinTestConnection{writer: &sent, reader: strings.NewReader(string(data) + "\n")}
				done := make(chan struct{})
				close(done)
				return &darwinProviderWorker{connection: connection, stdout: bufio.NewReader(connection), done: done}, nil
			}
			if _, err := runDarwinProvider(request); err != nil {
				t.Fatal(err)
			}
			decoded, err := providerDecodeProviderWireRequest(sent.Bytes())
			if err != nil {
				t.Fatal(err)
			}
			if got := decoded.Action.Diagnostics != nil && decoded.Action.Diagnostics.PhaseTiming; got != enabled {
				t.Fatalf("host opt-in=%t, sent %s", enabled, sent.Bytes())
			}
			if !request.Action.Diagnostics.PhaseTiming {
				t.Fatal("sender modified the caller's request")
			}
			sent.Reset()
			request.Action.Intent = "invalid"
			if response, err := runDarwinProvider(request); response != nil || err == nil || lookups != 1 || sent.Len() != 0 {
				t.Fatalf("invalid request crossed transport preflight: response=%v err=%v lookups=%d bytes=%d", response, err, lookups, sent.Len())
			}
		})
	}
}
