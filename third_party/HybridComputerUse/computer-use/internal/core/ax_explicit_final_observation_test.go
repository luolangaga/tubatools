package core

import (
	"fmt"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

type changingFinalObservationBackend struct {
	recordingPlatformBackend
	readNumber int
}

func (b *changingFinalObservationBackend) Observe(request ProviderObserveRequest) (ProviderAccessibilityResult, error) {
	b.observeRequests = append(b.observeRequests, request)
	b.readNumber++
	snapshot := mixedOrderedInputBatchTestSnapshot()
	snapshot.Elements[4].Name = fmt.Sprintf("Read %d", b.readNumber)
	snapshot.TreeLines[4] = fmt.Sprintf("\t\t4 status bar \"Read %d\"", b.readNumber)
	if request.Observation.IncludeScreenshot != nil && *request.Observation.IncludeScreenshot {
		snapshot.ScreenshotPNGBase64 = fmt.Sprintf("image-%d", b.readNumber)
		snapshot.ScreenshotMimeType = "image/png"
	}
	return ProviderAccessibilityResult{OK: true, Snapshot: snapshot}, nil
}

func TestTrailingWaitFullUsesOneFinalReadForTreeAndScreenshot(t *testing.T) {
	s, _, window, _ := orderedInputTestService(t)
	backend := &changingFinalObservationBackend{recordingPlatformBackend: recordingPlatformBackend{
		accessibilityResponse: &ProviderAccessibilityResult{OK: true, Snapshot: mixedOrderedInputBatchTestSnapshot(),
			Mutation: &ProviderMutationFacts{InputEventsEmitted: true}},
	}}
	backend.desktopResponse = testDesktopFrame(t, "final-frame", 64, 36)
	s.Backend = backend
	result := s.axBatch([]any{
		map[string]any{"type": "keypress", "keys": []any{"ENTER"}},
		map[string]any{"type": "wait", "query": map[string]any{"role": "button", "name_contains": "Sav"}, "timeout_ms": 1.0},
	}, observation{mode: contract.ObserveFull}, window)
	if result.IsError || backend.readNumber < 2 {
		t.Fatalf("wait did not complete before final read: reads=%d result=%s", backend.readNumber, ResultText(result))
	}
	if !strings.Contains(ResultText(result), fmt.Sprintf(`status "Read %d"`, backend.readNumber)) {
		t.Fatalf("tree did not use final read: %s", ResultText(result))
	}
	images := 0
	for _, item := range result.Content {
		if item.Type == "image" {
			images++
		}
	}
	// Only the final read captures the desktop, after its tree.
	if images != 1 || len(backend.desktopRequests) != 1 {
		t.Fatalf("full returned %d images from %d captures, want one from the final read",
			images, len(backend.desktopRequests))
	}
}

func TestExplicitFinalObservationAcrossBatchPaths(t *testing.T) {
	for _, path := range []string{"mutation", "wait", "ordered", "mixed_ordered"} {
		for _, mode := range []string{contract.ObserveTree, contract.ObserveFull, contract.ObserveScreenshot, contract.ObserveDiff} {
			t.Run(path+"/"+mode, func(t *testing.T) {
				s, _, window, ref := orderedInputTestService(t)
				snapshot := mixedOrderedInputBatchTestSnapshot()
				snapshot.ScreenshotPNGBase64 = "final-image"
				snapshot.ScreenshotMimeType = "image/png"
				legacy := &recordingPlatformBackend{accessibilityResponse: &ProviderAccessibilityResult{
					OK: true, Snapshot: snapshot, Mutation: &ProviderMutationFacts{InputEventsEmitted: true},
				}}
				legacy.desktopResponse = testDesktopFrame(t, "final-frame", 64, 36)
				finalImage := legacy.desktopResponse.Snapshot.ScreenshotPNGBase64
				s.Backend = legacy
				actions := []any{map[string]any{"type": "keypress", "keys": []any{"ENTER"}}}
				var ordered *orderedInputRecordingBackend
				if path == "wait" {
					actions = append(actions, map[string]any{"type": "wait", "query": map[string]any{"role": "button", "name_contains": "Sav"}, "timeout_ms": 1.0})
				}
				if path == "ordered" || path == "mixed_ordered" {
					ordered = &orderedInputRecordingBackend{recordingPlatformBackend: legacy,
						execute: func(request ProviderOrderedInputRequest) ProviderOrderedInputResult {
							result := testProviderOrderedInputSuccess(request)
							result.Snapshot = mixedOrderedInputBatchTestSnapshot()
							if request.FinalObservation.IncludeScreenshot != nil && *request.FinalObservation.IncludeScreenshot {
								result.Snapshot.ScreenshotPNGBase64 = "final-image"
								result.Snapshot.ScreenshotMimeType = "image/png"
							}
							return result
						},
					}
					s.Backend = ordered
					if path == "mixed_ordered" {
						actions = append([]any{map[string]any{"type": "click", "ref": ref}}, actions...)
					}
				}
				// The second request has a delivered baseline. An explicit tree/full
				// must still deliver controls outside the focused subtree.
				repeats := 2
				if path == "mixed_ordered" {
					repeats = 1 // The fixture replaces the initial semantic ref after click.
				}
				for repeat := 0; repeat < repeats; repeat++ {
					result := s.axBatch(actions, observation{mode: mode}, window)
					if result.IsError {
						t.Fatalf("batch failed: %s", ResultText(result))
					}
					text := ResultText(result)
					if mode == contract.ObserveTree || mode == contract.ObserveFull {
						if !strings.Contains(text, `status "Ready"`) || result.StructuredContent["full_tree_returned"] != true {
							t.Fatalf("explicit %s omitted nonfocused controls on request %d: %s", mode, repeat+1, text)
						}
					}
					image := false
					for _, item := range result.Content {
						image = image || (item.Type == "image" && item.Data == finalImage)
					}
					if (mode == contract.ObserveFull || mode == contract.ObserveScreenshot) && !image {
						t.Fatalf("%s omitted final screenshot: %s", mode, text)
					}
					if mode == contract.ObserveTree && image {
						t.Fatal("tree unexpectedly included screenshot")
					}
					if mode == contract.ObserveScreenshot && strings.Contains(text, `status "Ready"`) {
						t.Fatal("screenshot unexpectedly included tree")
					}
					if mode == contract.ObserveDiff && result.StructuredContent["full_tree_returned"] == true {
						t.Fatalf("default diff expanded to full tree: %s", text)
					}
					wantCaptures := 0
					if mode == contract.ObserveFull || mode == contract.ObserveScreenshot {
						wantCaptures = repeat + 1
					}
					if len(legacy.desktopRequests) != wantCaptures {
						t.Fatalf("%s: %d desktop captures after request %d, want %d",
							mode, len(legacy.desktopRequests), repeat+1, wantCaptures)
					}
				}
			})
		}
	}
}

func TestExplicitFullRecoveryPreservesPartialBatch(t *testing.T) {
	s, app, window, _ := orderedInputTestService(t)
	snapshot := mixedOrderedInputBatchTestSnapshot()
	snapshot.ScreenshotPNGBase64 = "recovery-image"
	snapshot.ScreenshotMimeType = "image/png"
	backend := &recordingPlatformBackend{accessibilityResponse: &ProviderAccessibilityResult{OK: true, Snapshot: snapshot},
		desktopResponse: testDesktopFrame(t, "recovery-frame", 64, 36)}
	s.Backend = backend
	batch := map[string]any{"actions_requested": 3, "actions_completed_without_call_error": 1, "first_failed_action_index": 1}
	failed := ToolCallResult{IsError: true, StructuredContent: map[string]any{
		"failure_code": "effect_unknown", "failure_phase": "action",
		"partial_input_possible": true, "input_events_emitted": true,
		"a11y_batch_result": batch,
	}}
	result := s.recoverAXBatchObservation(&AXBatchContext{
		AppRef: app, AppToken: app, WindowRef: window, WindowHandle: snapshot.WindowHandle,
	}, failed, observation{mode: contract.ObserveFull})
	if !result.IsError || result.StructuredContent["failure_code"] != "effect_unknown" ||
		result.StructuredContent["partial_input_possible"] != true ||
		result.StructuredContent["recovery_observation_returned"] != true {
		t.Fatalf("recovery corrupted failed action evidence: %#v", result)
	}
	got := result.StructuredContent["a11y_batch_result"].(map[string]any)
	if got["actions_completed_without_call_error"] != 1 || got["first_failed_action_index"] != 1 {
		t.Fatalf("recovery lost completed prefix: %#v", got)
	}
	if len(backend.observeRequests) != 1 || len(backend.desktopRequests) != 1 ||
		!strings.Contains(ResultText(result), `status "Ready"`) {
		t.Fatalf("recovery did not request and return full final state: %#v", result)
	}
	foundImage := false
	for _, item := range result.Content {
		foundImage = foundImage || item.Type == "image" &&
			item.Data == backend.desktopResponse.Snapshot.ScreenshotPNGBase64
	}
	if !foundImage {
		t.Fatal("recovery omitted final screenshot")
	}
}

func TestExplicitBatchObservationDeliversReissuedReferences(t *testing.T) {
	for _, mode := range []string{contract.ObserveTree, contract.ObserveFull} {
		s := NewService()
		for _, ref := range []string{"r206", "r207", "r207"} {
			snapshot := deliveredDiffBaselineSnapshot("document", ` - window "Editor":`,
				fmt.Sprintf(`  - button "Save" [ref=%s]`, ref))
			snapshot.comparedToPrevious = true
			snapshot.identicalToPrevious = true
			result := s.renderAXBatchFinalObservation(snapshot, nil, observation{mode: mode})
			if !strings.Contains(ResultText(result), "[ref="+ref+"]") || result.StructuredContent["tree_reference_only"] == true {
				t.Fatalf("explicit %s hid current ref %s: %s", mode, ref, ResultText(result))
			}
		}
	}
}
