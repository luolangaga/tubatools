package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestNativeVerifiedClosePreservesLifecycleEvidence(t *testing.T) {
	for _, mode := range []string{"diff", "none", "screenshot"} {
		t.Run(mode, func(t *testing.T) {
			s, backend := windowScriptService(editorApps(
				testWindow{testHandleReport, "Report - Editor", true},
			))
			window, argErr := s.resolveWindowTitle("Report")
			if argErr != nil {
				t.Fatal(argErr)
			}
			backend.windowResponse = &ProviderWindowResult{
				OK: true,
				Snapshot: &ProviderSnapshot{
					WindowAbsent: true, ClosedWindowHandle: testHandleReport,
				},
				Mutation: &ProviderMutationFacts{
					InputEventsEmitted: true, DesktopStateMutated: true,
				},
				ActionEvidence: &ProviderActionEvidence{
					InputEmitted: true, Route: "native_window_command",
				},
			}
			// A redundant read cannot recover an AX subtree for a closed window.
			backend.accessibilityResponses = []ProviderAccessibilityResult{{
				OK: false, Error: "stale_window_ref: source window changed",
				Failure: &ProviderFailure{Code: "stale_window_ref", Phase: "precondition"},
			}}
			result := s.callTool(contract.PublicToolAX, map[string]any{
				"mode": "act", "observe": mode,
				"actions": []any{map[string]any{"type": "close", "ref": window.ref}},
			})
			public := WithAXTextEvidence(PrepareAXEvidence(result))
			fields := public.StructuredContent
			if result.IsError || fields["close_effect_verified"] != true ||
				fields["target_window_absent"] != true || fields["input_emitted"] != true {
				t.Fatalf("close proof or actual emission lost: %#v", fields)
			}
			for _, key := range []string{"effect_unknown", "mutation_unknown"} {
				if fields[key] == true {
					t.Fatalf("verified close became unknown: %#v", fields)
				}
			}
			if fields["observation_failure_code"] != nil || len(backend.observeRequests) != 0 {
				t.Fatalf("closed window was read again: fields=%#v reads=%d", fields, len(backend.observeRequests))
			}
			if fields["completed"] != 1 || fields["requested"] != 1 {
				t.Fatalf("batch progress lost: %#v", fields)
			}
			wantKind := "lifecycle"
			if mode == "none" {
				wantKind = "none"
			}
			if fields["final_observation_kind"] != wantKind ||
				fields["final_observation_returned"] != (mode == "diff") ||
				fields["observation_required"] != (mode == "screenshot") {
				t.Fatalf("observation contract lost: %#v", fields)
			}
			if mode == "screenshot" && fields["screenshot_returned"] != false {
				t.Fatalf("closed window screenshot claimed: %#v", fields)
			}
			if !strings.Contains(ResultText(public), `windows: closed `+window.ref+` "Report - Editor"`) {
				t.Fatalf("closed window not named: %s", ResultText(public))
			}
		})
	}
}

func TestNativeCloseProofRequiresSuccessfulMatchingAbsence(t *testing.T) {
	for _, test := range []struct {
		name   string
		handle string
		absent bool
		ok     bool
	}{
		{"wrong_window", testHandleNotes, true, true},
		{"missing_window", "", true, true},
		{"window_present", testHandleReport, false, true},
		{"failed_close", testHandleReport, true, false},
	} {
		t.Run(test.name, func(t *testing.T) {
			s, backend := windowScriptService(editorApps(
				testWindow{testHandleReport, "Report - Editor", true},
			))
			window, argErr := s.resolveWindowTitle("Report")
			if argErr != nil {
				t.Fatal(argErr)
			}
			backend.windowResponse = &ProviderWindowResult{
				OK: test.ok, Error: "native close failed after input",
				Snapshot:       &ProviderSnapshot{WindowAbsent: test.absent, ClosedWindowHandle: test.handle},
				Mutation:       &ProviderMutationFacts{InputEventsEmitted: true, DesktopStateMutated: true},
				ActionEvidence: &ProviderActionEvidence{InputEmitted: true, Route: "native_window_command"},
			}
			backend.accessibilityResponses = []ProviderAccessibilityResult{{
				OK: false, Error: "stale_window_ref: source window is gone",
				Failure: &ProviderFailure{Code: "stale_window_ref", Phase: "precondition"},
			}}
			result := s.callTool(contract.PublicToolAX, map[string]any{
				"mode": "act", "actions": []any{map[string]any{"type": "close", "ref": window.ref}},
			})
			if result.StructuredContent["close_effect_verified"] == true ||
				result.StructuredContent["target_window_absent"] == true {
				t.Fatalf("unproven close accepted: %#v", result.StructuredContent)
			}
			if !test.ok && !result.IsError {
				t.Fatalf("native action failure cleared: %#v", result.StructuredContent)
			}
		})
	}
}
