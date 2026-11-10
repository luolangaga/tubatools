package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestListAppsWithoutWindowListKeepsProviderDisplayText(t *testing.T) {
	backend := &recordingPlatformBackend{
		accessibilityResponse: &ProviderAccessibilityResult{
			OK: true,
			Apps: []AppDescriptor{{
				Name:              "vlc",
				DisplayText:       `vlc [window="master.m3u8 - VLC media player", pid=202]`,
				InputCapable:      true,
				ProviderBusName:   ":1.42",
				ProcessGeneration: "pid:202:start:100",
			}},
		},
	}
	service := NewServiceWithBackend(backend)
	text := ResultText(service.listApps())
	if !strings.Contains(text, `vlc [window="master.m3u8 - VLC media player", pid=202] app=vlc a1`) {
		t.Fatalf("provider display text was not preserved: %q", text)
	}
}

func TestAXObserveRejectsGuessedWholeTreeGeometry(t *testing.T) {
	service := NewService()
	result := service.dispatch(
		contract.PublicToolAX,
		map[string]any{
			"mode":  "observe",
			"app":   "editor",
			"boxes": true,
		},
	)
	if !result.IsError {
		t.Fatalf("guessed boxes argument must fail closed: %#v", result)
	}
	text := ResultText(result)
	if !strings.Contains(text, `mode "observe" does not accept "boxes"`) {
		t.Fatalf("AX observe field rejection is not actionable: %q", text)
	}
	legacy := service.dispatch("get_app_state", map[string]any{"app": "editor"})
	if !legacy.IsError ||
		!strings.Contains(ResultText(legacy), `unsupportedTool("get_app_state")`) {
		t.Fatalf("hidden get_app_state entrypoint was restored: %#v", legacy)
	}
}

// TestChannelCapabilitySwitch checks that a whole channel can be disabled and
// that its tools then disappear from tools/list. A model tries every tool it
// can see, and a refused call wastes a turn; a hidden tool is never tried.
func TestChannelCapabilitySwitch(t *testing.T) {
	names := func() map[string]bool {
		out := map[string]bool{}
		for _, tool := range contract.ToolDefinitions() {
			out[tool.Name] = true
		}
		return out
	}

	t.Setenv("COMPUTER_USE_CHANNELS", "")
	all := names()
	for _, tool := range []string{"AX", "GUI"} {
		if !all[tool] {
			t.Fatalf("all channels should be enabled by default, missing %s", tool)
		}
	}

	t.Setenv("COMPUTER_USE_CHANNELS", "a11y,keyboard")
	without := names()
	if without["GUI"] {
		t.Fatal("GUI must not remain in tools/list after the gui channel is disabled")
	}
	for _, kept := range []string{"AX"} {
		if !without[kept] {
			t.Fatalf("disabling gui must not affect %s", kept)
		}
	}

	// A direct call must be refused as well, and the error must say that the
	// channel is disabled rather than that the tool does not exist.
	service := NewService()
	result := service.callTool(contract.PublicToolGUI, map[string]any{"actions": []any{}})
	if !result.IsError || !strings.Contains(result.Content[0].Text, "channel, which is disabled") {
		t.Fatalf("a direct call to a disabled channel's tool must explain the refusal: %#v", result)
	}
}

func TestAXReadModesKeepTheirEvidenceBoundaries(t *testing.T) {
	if got := toolEvidenceScope(contract.PublicToolAX, map[string]any{"mode": "apps"}); got != "app_inventory" {
		t.Fatalf("apps evidence scope=%q", got)
	}
	for _, args := range []map[string]any{
		{"mode": "observe"},
		{"mode": "find"},
	} {
		if got := toolEvidenceScope(contract.PublicToolAX, args); got != "independent_ui_read" {
			t.Fatalf("%v evidence scope=%q", args, got)
		}
	}
	if got := toolEvidenceScope(contract.PublicToolAX, map[string]any{
		"mode": "act",
	}); got != "delivery_or_local_ui" {
		t.Fatalf("act evidence scope=%q", got)
	}
	service := NewService()
	rejected := service.handleAXRequest(map[string]any{
		"mode":    "find",
		"app":     "Editor",
		"query":   map[string]any{"name_equals": "Save"},
		"actions": []any{},
	})
	if !rejected.IsError ||
		!strings.Contains(ResultText(rejected), `does not accept "actions"`) {
		t.Fatalf("find must reject action fields before reading: %#v", rejected)
	}
}

func TestPreflightOnlyErrorsDoNotCaptureFreshUIContext(t *testing.T) {
	service := NewService()
	result := service.callTool(contract.PublicToolAX, map[string]any{
		"mode":    "find",
		"app":     "Editor",
		"query":   map[string]any{"name": "Save"},
		"actions": []any{},
	})
	if !result.IsError {
		t.Fatalf("invalid mixed-mode request must fail: %#v", result)
	}
	for key, want := range map[string]any{
		"evidence_scope":                 "error_only",
		"error_context_captured":         false,
		"error_context_fresh_ui_sampled": false,
		"preflight_only":                 true,
	} {
		if got := result.StructuredContent[key]; got != want {
			t.Fatalf("%s=%#v want %#v in %#v",
				key, got, want, result.StructuredContent)
		}
	}
	if strings.Contains(ResultText(result), "error context") {
		t.Fatalf("preflight-only failure must not append fresh UI context: %s",
			ResultText(result))
	}
}

func TestCallToolAlwaysAddsEvidenceContract(t *testing.T) {
	service := NewService()
	result := service.callTool("AX", map[string]any{
		"mode": "act",
		"actions": []any{map[string]any{
			"type":         "click",
			"ref":          "r1",
			"click_method": "not-a-method",
		}},
	})
	if !result.IsError || result.StructuredContent == nil {
		t.Fatalf("parameter errors must still carry the evidence contract: %#v", result)
	}
	if result.StructuredContent["evidence_scope"] != "error_only" ||
		result.StructuredContent[contract.AXPublicErrorCodeKey] != contract.AXCodeInvalidArgument {
		t.Fatalf("parameter error evidence contract is incomplete: %#v", result.StructuredContent)
	}
}

func TestErrorContextFreshReadIsStructuredEvidence(t *testing.T) {
	result := withErrorContextEvidence(
		TextResult("failed; external context file written", true),
		true,
	)
	for key, want := range map[string]any{
		"error_context_captured":          true,
		"error_context_fresh_ui_sampled":  true,
		"cause_unattributed":              true,
		"error_context_fresh_state_cause": "unattributed",
		"evidence_scope":                  "error_with_external_fresh_ui_context",
	} {
		if got := result.StructuredContent[key]; got != want {
			t.Fatalf("%s=%#v, want %#v; all=%#v", key, got, want,
				result.StructuredContent)
		}
	}

	unpersisted := withErrorContextEvidence(TextResult("failed", true), false)
	if unpersisted.StructuredContent["error_context_captured"] != false ||
		unpersisted.StructuredContent["error_context_fresh_ui_sampled"] != true ||
		unpersisted.StructuredContent["evidence_scope"] !=
			"error_with_unpersisted_fresh_ui_sample" {
		t.Fatalf("an unpersisted sample must remain visible in structured evidence: %#v",
			unpersisted.StructuredContent)
	}
}

func TestCallToolAppliesAXImagePolicyAtSharedOutputBoundary(t *testing.T) {
	service := NewServiceWithBackend(&recordingPlatformBackend{
		accessibilityResponse: &ProviderAccessibilityResult{
			OK: true,
			Snapshot: &AppSnapshot{
				App:                 AppDescriptor{Name: "Editor", PID: 7},
				WindowTitle:         "Editor",
				WindowHandle:        "window-7",
				TreeLines:           []string{`ax:`, ` - window "Editor"`},
				ScreenshotPNGBase64: "provider-overreturned-image",
				ScreenshotMimeType:  "image/png",
			},
		},
	})

	result := service.callTool("AX", map[string]any{
		"app":     "Editor",
		"mode":    "observe",
		"observe": "diff",
	})
	if result.IsError {
		t.Fatalf("AX observe unexpectedly failed: %#v", result)
	}
	for _, item := range result.Content {
		if item.Type == "image" {
			t.Fatalf("shared AX output boundary leaked provider image: %#v", result.Content)
		}
	}
}

func TestInvalidStructuredAssertionFailsBeforeFreshUIRead(t *testing.T) {
	service := NewService()
	result := service.callTool(contract.PublicToolAX, map[string]any{
		"mode": "act",
		"actions": []any{map[string]any{
			"type":  "wait",
			"query": map[string]any{"role:document": "document"},
		}},
	})
	if !result.IsError ||
		!strings.Contains(ResultText(result), "query does not accept") ||
		result.StructuredContent[contract.AXPublicErrorPathKey] != "actions[0].query.role:document" {
		t.Fatalf("invalid structured assertion must fail clearly: %#v", result)
	}
	for key, want := range map[string]any{
		"evidence_scope":                 "error_only",
		"error_context_captured":         false,
		"error_context_fresh_ui_sampled": false,
		"preflight_only":                 true,
	} {
		if got := result.StructuredContent[key]; got != want {
			t.Fatalf("%s=%#v want %#v in %#v",
				key, got, want, result.StructuredContent)
		}
	}
}

func TestGUISchemaUsesComputerScrollNamesAndExcludesWindowActions(t *testing.T) {
	service := NewService()
	for _, args := range []map[string]any{
		{"actions": []any{map[string]any{
			"type": "scroll", "x": 1, "y": 2, "delta_y": 3,
		}}},
		{"actions": []any{map[string]any{
			"type": "right_click", "x": 1, "y": 2,
		}}},
		{"actions": []any{map[string]any{
			"type": "wait", "seconds": 0.2,
		}}},
		{"actions": []any{map[string]any{
			"type": "activate_window",
		}}},
		{"actions": []any{}, "settle_ms": 100},
		{"actions": []any{}, "expect_change": true},
	} {
		result := service.dispatch(contract.PublicToolGUI, args)
		if !result.IsError {
			t.Fatalf("GUI accepted removed compatibility input: %#v", args)
		}
	}
}

func TestGUIObservationRejectsSettleBeforeRuntime(t *testing.T) {
	result := NewService().dispatch(contract.PublicToolGUI, map[string]any{
		"actions": []any{map[string]any{
			"type": "screenshot",
		}},
		"settle_ms": 100,
	})
	if !result.IsError {
		t.Fatalf("screenshot + settle_ms unexpectedly succeeded: %#v", result)
	}
	text := ResultText(result)
	if !strings.Contains(text, "screenshot observation") ||
		!strings.Contains(text, "does not accept settle_ms") {
		t.Fatalf("wrong preflight error: %q", text)
	}
}

func TestErrorContextIsOnByDefaultAndDisableable(t *testing.T) {
	// Unlike --output-mode file, error context is on by default, for the
	// opposite reason: output mode changes the shape of every successful call,
	// while error context fires only on failure, when nothing else preserves
	// the scene for later diagnosis and the response holds a single error line.
	t.Setenv("COMPUTER_USE_ERROR_CONTEXT", "")
	if !errorContextEnabled() {
		t.Fatal("error context should be written to disk by default")
	}
	t.Setenv("COMPUTER_USE_ERROR_CONTEXT", "off")
	if errorContextEnabled() {
		t.Fatal("an explicit off must disable error context")
	}
	t.Setenv("COMPUTER_USE_ERROR_CONTEXT", "OFF")
	if errorContextEnabled() {
		t.Fatal("switch value must be case-insensitive")
	}
}

func TestErrorContextNeedsAnAppAndNeverMasksTheOriginalError(t *testing.T) {
	service := NewService()
	t.Setenv("COMPUTER_USE_ERROR_CONTEXT", "")
	// Without an app there is nothing to capture. This also keeps app-less
	// operations such as list_apps from taking a pointless snapshot on failure.
	if note, sampled := service.writeErrorContext("", "click", "boom"); note != "" || sampled {
		t.Fatalf("without an app nothing should be sampled or written, got note=%q sampled=%v", note, sampled)
	}
	// If capturing the context fails, the result is an empty string and never a
	// second error, which would only bury the original one.
	if note, sampled := service.writeErrorContext("no-such-app-xyzzy", "click", "boom"); note != "" || sampled {
		t.Fatalf("a failed capture should be dropped silently, got note=%q sampled=%v", note, sampled)
	}
}
