package core

import (
	"bytes"
	"encoding/base64"
	"image"
	"image/jpeg"
	"reflect"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func testJPEGBase64(t *testing.T, width, height int) string {
	t.Helper()
	var encoded bytes.Buffer
	if err := jpeg.Encode(
		&encoded,
		image.NewRGBA(image.Rect(0, 0, width, height)),
		&jpeg.Options{Quality: 80},
	); err != nil {
		t.Fatalf("encode test JPEG: %v", err)
	}
	return base64.StdEncoding.EncodeToString(encoded.Bytes())
}

// guiModelText is the text a model receives for a GUI result.
func guiModelText(result ToolCallResult) string {
	return ResultText(FinalizeForModel(contract.PublicToolGUI, result, false))
}

func TestGUIModelResponseKeepsLayoutProvenancePrivate(t *testing.T) {
	snapshot := &AppSnapshot{
		ScreenshotPNGBase64: "png",
		CoordinateSpace:     "screen_image_px",
		FrameID:             "current-frame",
		DesktopLayout: map[string]any{
			"activeWindow": "0x20",
			"windows":      []any{map[string]any{"id": "0x20"}},
		},
		Width:  1920,
		Height: 1080,
	}
	one, yes, no, percent := 1, true, false, 0.5
	text := contract.CompactJSON(guiMetadata(snapshot, ProviderGUIResult{
		Dispatched:             &one,
		InputEventsEmitted:     &yes,
		InputActionsDispatched: &one,
		EffectUnknown:          &yes,
		Quiescent:              &yes,
		VisualChangeObserved:   &no,
		VisualChangePercent:    &percent,
	}))
	for _, fragment := range []string{
		`"coordinate_space":"screen_image_px"`,
		`"width":1920`,
		`"height":1080`,
		`"screenshot_returned":true`,
		`"dispatched":1`,
		`"input_actions_dispatched":1`,
		`"input_events_emitted":true`,
		`"effect_unknown":true`,
		`"quiescent":true`,
		`"visual_change_observed":false`,
	} {
		if !strings.Contains(text, fragment) {
			t.Fatalf("compact GUI response missing %q: %s", fragment, text)
		}
	}
	for _, forbidden := range []string{
		"frame_id", "content_frame_id", "desktop_layout", "0x20",
		"screenshot_encoding", "screenshot_mime_type",
		"input_actions_emitted", "visual_change_percent",
	} {
		if strings.Contains(text, forbidden) {
			t.Fatalf("model response leaked private GUI provenance %q: %s", forbidden, text)
		}
	}
}

func TestCompactGUIResultPreservesUnknownEffect(t *testing.T) {
	one, yes := 1, true
	compact := compactGUIResult(ProviderGUIResult{
		Dispatched:         &one,
		InputEventsEmitted: &yes,
		EffectUnknown:      &yes,
	})
	if compact["effect_unknown"] != true {
		t.Fatalf("GUI compact evidence hid effect_unknown: %#v", compact)
	}
}

func TestGUIInputEmissionInvalidatesAccessibilitySnapshots(t *testing.T) {
	response := func(emitted bool) ProviderDesktopResult {
		return ProviderDesktopResult{
			OK: true,
			Snapshot: &AppSnapshot{
				ScreenshotPNGBase64: testPNGBase64(t, 100, 80),
				CoordinateSpace:     "screen_image_px",
				FrameID:             "frame",
				Width:               100,
				Height:              80,
			},
			GUIResult: ProviderGUIResult{InputEventsEmitted: &emitted},
		}
	}

	service := NewService()
	service.rememberSnapshot("Editor", expectSnapshot())
	service.guiResponse(response(false))
	if service.currentSnapshot("Editor") == nil {
		t.Fatal("pure GUI observation or pre-emission rejection must preserve accessibility snapshots")
	}

	service.guiResponse(response(true))
	if len(service.snapshots) != 0 || service.currentSnapshot("Editor") != nil {
		t.Fatal("input_events_emitted=true must invalidate every accessibility snapshot and expect baseline")
	}

	service.rememberSnapshot("Editor", expectSnapshot())
	partialFailure := response(false)
	partialFailure.OK = false
	partialFailure.Error = "input failed after partial emission"
	partialFailure.Mutation = &ProviderMutationFacts{InputEventsEmitted: true}
	service.guiResponse(partialFailure)
	if len(service.snapshots) != 0 {
		t.Fatal("top-level input_events_emitted=true on a partial GUI failure must also invalidate snapshots")
	}

	service.rememberSnapshot("Editor", expectSnapshot())
	partialOnly := response(false)
	partialOnly.OK = false
	partialOnly.Mutation = &ProviderMutationFacts{PartialInputPossible: true}
	service.guiResponse(partialOnly)
	if len(service.snapshots) != 0 {
		t.Fatal("partial_input_possible=true must invalidate snapshots even when no complete input action was counted")
	}

	service.rememberSnapshot("Editor", expectSnapshot())
	windowOnly := response(false)
	windowOnly.Mutation = &ProviderMutationFacts{DesktopStateMutated: true}
	service.guiResponse(windowOnly)
	if len(service.snapshots) != 0 {
		t.Fatal("desktop_state_mutated=true must invalidate snapshots independently of input events")
	}

	service.rememberSnapshot("Editor", expectSnapshot())
	unknown := response(false)
	unknown.Mutation = &ProviderMutationFacts{MutationUnknown: true}
	unknownResult := service.guiResponse(unknown)
	if len(service.snapshots) != 0 {
		t.Fatal("mutation_unknown=true must invalidate snapshots after GUI runtime timeout")
	}
	if !strings.Contains(guiModelText(unknownResult), `"mutation_unknown":true`) {
		t.Fatalf("top-level GUI mutation facts were hidden: %s",
			guiModelText(unknownResult))
	}

	field, ok := reflect.TypeOf(ProviderMutationFacts{}).FieldByName("InputEventsEmitted")
	if !ok || field.Tag.Get("json") != "input_events_emitted,omitempty" {
		t.Fatal("ProviderMutationFacts must decode input_events_emitted from partial failures")
	}
	if field, ok := reflect.TypeOf(ProviderMutationFacts{}).FieldByName("MutationUnknown"); !ok ||
		field.Tag.Get("json") != "mutation_unknown,omitempty" {
		t.Fatal("ProviderMutationFacts must decode GUI mutation_unknown")
	}
}

func TestGUIFailureWithoutSnapshotReturnsMutationEvidence(t *testing.T) {
	service := NewService()
	result := service.guiResponse(ProviderDesktopResult{
		OK:    false,
		Error: "post-action screenshot failed",
		Failure: &ProviderFailure{
			Code:  "effect_unknown",
			Phase: "post_action_observation",
		},
		Mutation: &ProviderMutationFacts{
			InputEventsEmitted: true,
			MutationUnknown:    true,
		},
	})
	if result.StructuredContent["failure_code"] != "effect_unknown" {
		t.Fatalf("GUI failure code missing: %#v", result.StructuredContent)
	}
	actions, _ := result.StructuredContent["actions"].(map[string]any)
	if actions["input_events_emitted"] != true ||
		actions["mutation_unknown"] != true {
		t.Fatalf("GUI mutation evidence missing: %#v", result.StructuredContent)
	}
}

func TestGUIResponseAcceptsValidatedJPEGSnapshot(t *testing.T) {
	service := NewService()
	result := service.guiResponse(ProviderDesktopResult{
		OK: true,
		Snapshot: &AppSnapshot{
			FrameID:             "jpeg-frame",
			ScreenshotPNGBase64: testJPEGBase64(t, 100, 80),
			ScreenshotMimeType:  "image/jpeg",
			CoordinateSpace:     "screen_image_px",
			Width:               100,
			Height:              80,
		},
	})
	if result.IsError {
		t.Fatalf("valid JPEG snapshot was rejected: %#v", result)
	}
	if service.guiSnapshot == nil || service.guiSnapshot.FrameID != "jpeg-frame" {
		t.Fatalf("valid JPEG snapshot was not cached: %#v", service.guiSnapshot)
	}
}

func TestGUIResponseCanonicalizesMissingJPEGMIME(t *testing.T) {
	service := NewService()
	result := service.guiResponse(ProviderDesktopResult{
		OK: true,
		Snapshot: &AppSnapshot{
			FrameID:             "jpeg-frame-without-mime",
			ScreenshotPNGBase64: testJPEGBase64(t, 100, 80),
			CoordinateSpace:     "screen_image_px",
			Width:               100,
			Height:              80,
		},
	})
	if result.IsError {
		t.Fatalf("valid JPEG snapshot without MIME was rejected: %#v", result)
	}
	if service.guiSnapshot == nil ||
		service.guiSnapshot.ScreenshotMimeType != "image/jpeg" {
		t.Fatalf("detected JPEG MIME was not cached: %#v", service.guiSnapshot)
	}
	if last := result.Content[len(result.Content)-1]; last.Type != "image" || last.MimeType != "image/jpeg" {
		t.Fatalf("detected JPEG MIME was not rendered: %#v", result.Content)
	}
}

func TestGUIResponseRejectsDeclaredMIMEThatDoesNotMatchImage(t *testing.T) {
	service := NewService()
	previous := &AppSnapshot{FrameID: "previous-frame"}
	service.guiSnapshot = previous
	result := service.guiResponse(ProviderDesktopResult{
		OK: true,
		Snapshot: &AppSnapshot{
			FrameID:             "mismatched-mime",
			ScreenshotPNGBase64: testJPEGBase64(t, 100, 80),
			ScreenshotMimeType:  "image/png",
			CoordinateSpace:     "screen_image_px",
			Width:               100,
			Height:              80,
		},
	})
	if !result.IsError {
		t.Fatalf("mismatched image MIME was accepted: %#v", result)
	}
	if service.guiSnapshot != previous {
		t.Fatal("mismatched image MIME replaced the previous coordinate frame")
	}
}

func TestGUIFrameGuardAppliesOnlyToCoordinateActions(t *testing.T) {
	if guiBatchUsesFrameProvenance([]ProviderDesktopAction{
		{Type: "keypress", Keys: []string{"CTRL", "S"}},
		{Type: "type", Text: "hello"},
	}) {
		t.Fatal("keyboard/text-only GUI batches must not depend on screenshot coordinates")
	}
	for _, action := range []string{"click", "double_click", "move", "drag", "scroll"} {
		if !guiBatchUsesFrameProvenance([]ProviderDesktopAction{{Type: action}}) {
			t.Fatalf("coordinate action must require frame guard: %s", action)
		}
	}
	for _, action := range []string{"activate_window", "close_window"} {
		if guiBatchUsesFrameProvenance([]ProviderDesktopAction{{Type: action}}) {
			t.Fatalf("removed GUI window action must not enter frame provenance: %s", action)
		}
	}
}

func TestGUIValidationErrorsListEveryRejectedAction(t *testing.T) {
	text := guiValidationErrorText(ProviderDesktopResult{
		Error: "gui batch failed static pre-validation",
		ValidationErrors: []map[string]any{
			{"index": float64(0), "type": "scroll", "error": "unsupported field delta_y"},
			{"index": float64(1), "type": "wait", "error": "unsupported field seconds"},
		},
	})
	for _, expected := range []string{
		`actions[0] type="scroll": unsupported field delta_y`,
		`actions[1] type="wait": unsupported field seconds`,
	} {
		if !strings.Contains(text, expected) {
			t.Fatalf("validation summary missing %q: %q", expected, text)
		}
	}
}

func TestGUICoordinateBatchWithoutPriorScreenshotObservesWithoutInput(t *testing.T) {
	internalScreenshot := 1
	backend := &recordingPlatformBackend{desktopResponse: &ProviderDesktopResult{
		OK:        true,
		GUIResult: ProviderGUIResult{Dispatched: &internalScreenshot},
		Snapshot: &AppSnapshot{
			ScreenshotPNGBase64: testPNGBase64(t, 100, 80),
			CoordinateSpace:     "screen_image_px",
			FrameID:             "fresh-frame",
			Width:               100,
			Height:              80,
		},
	}}
	service := NewServiceWithBackend(backend)
	result := service.gui([]any{
		map[string]any{"type": "click", "x": 10.0, "y": 20.0},
	}, 800)
	if len(backend.desktopRequests) != 1 {
		t.Fatalf("want exactly one observation request, got %#v", backend.desktopRequests)
	}
	request := backend.desktopRequests[0]
	// The real backends validate before dispatch; an empty batch used to fail
	// here with "provider desktop actions are required" and no screenshot.
	if err := validateProviderDesktopRequest(request); err != nil {
		t.Fatalf("observation request is not a valid provider batch: %v", err)
	}
	if len(request.Actions) != 1 || request.Actions[0].Type != "screenshot" {
		t.Fatalf("observation request must be one screenshot action: %#v", request.Actions)
	}
	if !result.IsError {
		t.Fatal("coordinate batch without a prior screenshot must not report success")
	}
	text := guiModelText(result)
	if !strings.HasPrefix(text, `{"ok":false,"code":"gui_frame_stale",`) ||
		!strings.Contains(text, "require a prior GUI screenshot") ||
		!strings.Contains(text, "No actions were emitted") {
		t.Fatalf("missing actionable note: %s", text)
	}
	if strings.Contains(text, `"dispatched"`) {
		t.Fatalf("internal observation must not count as a dispatched action: %s", text)
	}
	if len(result.Content) == 0 || result.Content[len(result.Content)-1].Type != "image" {
		t.Fatalf("fresh screenshot was not attached: %#v", result.Content)
	}
	if service.guiSnapshot == nil || service.guiSnapshot.FrameID != "fresh-frame" {
		t.Fatal("the delivered fresh screenshot must become the coordinate frame")
	}
}

// Probe #26: the lenient decode dropped fields that ProviderDesktopAction does
// not carry, so the provider never saw them and {"type":"wait","ms":5} ran as
// a plain wait. Such calls must be rejected before any provider request.
func TestGUIRejectsUnknownActionFieldsBeforeProvider(t *testing.T) {
	for _, action := range []map[string]any{
		{"type": "wait", "ms": 5},
		{"type": "click", "x": 1, "y": 1, "delay": 3},
		{"type": "drag", "path": []any{
			map[string]any{"x": 1, "y": 1}, map[string]any{"x": 2, "y": 2, "z": 0},
		}},
	} {
		backend := &recordingPlatformBackend{}
		result := NewServiceWithBackend(backend).dispatch(contract.PublicToolGUI, map[string]any{
			"actions": []any{action},
		})
		if !result.IsError || !strings.Contains(ResultText(result), "does not accept fields") {
			t.Fatalf("GUI accepted unknown field in %v: %#v", action, result)
		}
		if len(backend.desktopRequests) != 0 {
			t.Fatalf("GUI reached the provider with %v", action)
		}
		// The provider decoder stays strict as well, so a caller that
		// bypasses the schema gate cannot reintroduce silent drops.
		if _, err := decodeProviderDesktopActions([]any{action}); err == nil ||
			!strings.Contains(err.Error(), "unknown field") {
			t.Fatalf("provider decode accepted unknown field in %v: %v", action, err)
		}
	}
}

// Probe #30: the 20-second cumulative wait limit was only stated in the
// description. It is a declared rule checked before any provider request.
func TestGUIWaitBudgetIsRejectedBeforeProvider(t *testing.T) {
	waits := make([]any, contract.GUIMaxTotalWait+1)
	for index := range waits {
		waits[index] = map[string]any{"type": "wait"}
	}
	backend := &recordingPlatformBackend{}
	result := NewServiceWithBackend(backend).callTool(contract.PublicToolGUI, map[string]any{"actions": waits})
	if !result.IsError || !strings.Contains(ResultText(result), "maximum is 20s") {
		t.Fatalf("21 waits were not rejected: %#v", result)
	}
	if result.StructuredContent["argument_error_code"] != contract.ArgumentRuleWaitBudget {
		t.Fatalf("wait budget rejection lacks its rule code: %#v", result.StructuredContent)
	}
	if len(backend.desktopRequests) != 0 {
		t.Fatal("wait budget rejection reached the provider")
	}
}
