package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

const testHandleScene = "w2:dddddddddddddddddddddddddddddddd"

// sceneApps lists an editor on the accessibility bus and a 3D tool whose
// process exports no accessibility tree: the provider gives its window a
// native handle only.
func sceneApps() []AppDescriptor {
	apps := editorApps(testWindow{testHandleReport, "Quarterly report - Editor", false})
	return append(apps, AppDescriptor{
		Name: "paint3d", BundleIdentifier: "paint3d", PID: 40, InputCapable: true,
		ProcessGeneration: "pid:40:start:1",
		Windows: []ProviderAppWindow{{
			Title: "Scene - Paint3D", Role: "window", Handle: testHandleScene,
			ProcessGeneration: "pid:40:start:1", Active: true, InputTarget: true,
			NoAccessibilityTree: true,
		}},
	})
}

func noTreeWindowRef(t *testing.T, service *Service) string {
	t.Helper()
	text := ResultText(service.listApps())
	for _, line := range strings.Split(text, "\n") {
		if strings.Contains(line, `"Scene - Paint3D"`) {
			if !strings.Contains(line, axNoTreeWindowMark) {
				t.Fatalf("window without a tree lacks its mark: %q", line)
			}
			return strings.Fields(line)[0]
		}
	}
	t.Fatalf("window without a tree is not listed:\n%s", text)
	return ""
}

// A window whose process exports no accessibility tree must still be
// listed with a wN: AX activate title=, close and window waits found
// nothing (window_not_found, wait_timeout) although GUI could see it.
func TestWindowWithoutTreeIsListedAndActivatedByTitle(t *testing.T) {
	service, backend := windowScriptService(sceneApps())
	backend.windowResponse = &ProviderWindowResult{OK: true}
	ref := noTreeWindowRef(t, service)

	result := service.handleAXRequest(map[string]any{
		"mode":    "act",
		"actions": []any{map[string]any{"type": "activate", "title": "Paint3D"}},
	})
	text := ResultText(result)
	if result.IsError || !strings.Contains(text, "activated "+ref) {
		t.Fatalf("activate by title failed:\n%s", text)
	}
	if len(backend.calls) == 0 || backend.calls[len(backend.calls)-1] != "window_activate" {
		t.Fatalf("activate did not reach the provider: %v", backend.calls)
	}
	if strings.Contains(strings.Join(backend.calls, ","), "observe,") {
		t.Fatalf("a window without a tree must not be observed: %v", backend.calls)
	}

	wait := service.handleAXRequest(map[string]any{
		"mode":    "act",
		"actions": []any{map[string]any{"type": "wait", "window": "Paint3D", "timeout_ms": 200}},
	})
	if wait.IsError || !strings.Contains(ResultText(wait), ref) {
		t.Fatalf("window wait did not find the window:\n%s", ResultText(wait))
	}
}

func TestReadsOfWindowWithoutTreePointToGUI(t *testing.T) {
	service, backend := windowScriptService(sceneApps())
	ref := noTreeWindowRef(t, service)
	before := len(backend.calls)

	for _, args := range []map[string]any{
		{"mode": "observe", "window_ref": ref},
		{"mode": "find", "window_ref": ref, "query": map[string]any{"role": "button"}},
		{"mode": "act", "window_ref": ref, "actions": []any{
			map[string]any{"type": "keypress", "keys": []any{"a"}}}},
	} {
		result := service.handleAXRequest(args)
		if !result.IsError ||
			result.StructuredContent[contract.AXPublicErrorCodeKey] != contract.AXCodeAppUnavailable ||
			result.StructuredContent[contract.AXPublicErrorHintKey] != GUIScreenshotCall ||
			!strings.Contains(ResultText(result), "no accessibility tree") {
			t.Fatalf("%v: want app_unavailable pointing to GUI, got %#v\n%s",
				args["mode"], result.StructuredContent, ResultText(result))
		}
		if axEvidenceReportsInputEmission(result.StructuredContent) {
			t.Fatalf("%v emitted input", args["mode"])
		}
	}
	if len(backend.calls) != before {
		t.Fatalf("rejected reads reached the provider: %v", backend.calls[before:])
	}
}
