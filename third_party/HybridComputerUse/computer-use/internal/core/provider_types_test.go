package core

import (
	"encoding/base64"
	"testing"
)

func TestGUIMissingObservationShapesPreserveMutationEvidence(t *testing.T) {
	truncatedPNG := testPNGBase64(t, 100, 80)
	truncatedPNG = truncatedPNG[:len(truncatedPNG)/2]
	cases := []ProviderDesktopResult{
		{
			OK:       true,
			Mutation: &ProviderMutationFacts{MutationUnknown: true},
		},
		{
			OK:       true,
			Mutation: &ProviderMutationFacts{InputEventsEmitted: true},
			Snapshot: &AppSnapshot{
				CoordinateSpace: "screen_image_px",
				Width:           100,
				Height:          80,
			},
		},
		{
			OK:       true,
			Mutation: &ProviderMutationFacts{InputEventsEmitted: true},
			Snapshot: &AppSnapshot{
				ScreenshotPNGBase64: "png",
				CoordinateSpace:     "screen_image_px",
			},
		},
		{
			OK:       true,
			Mutation: &ProviderMutationFacts{InputEventsEmitted: true},
			Snapshot: &AppSnapshot{
				FrameID:             "undelivered-invalid-base64",
				ScreenshotPNGBase64: "not base64!",
				CoordinateSpace:     "screen_image_px",
				Width:               100,
				Height:              80,
			},
		},
		{
			OK:       true,
			Mutation: &ProviderMutationFacts{InputEventsEmitted: true},
			Snapshot: &AppSnapshot{
				FrameID:             "undelivered-invalid-image",
				ScreenshotPNGBase64: base64.StdEncoding.EncodeToString([]byte("not-a-png")),
				CoordinateSpace:     "screen_image_px",
				Width:               100,
				Height:              80,
			},
		},
		{
			OK:       true,
			Mutation: &ProviderMutationFacts{InputEventsEmitted: true},
			Snapshot: &AppSnapshot{
				FrameID:             "undelivered-truncated-image",
				ScreenshotPNGBase64: truncatedPNG,
				ScreenshotMimeType:  "image/png",
				CoordinateSpace:     "screen_image_px",
				Width:               100,
				Height:              80,
			},
		},
		{
			OK:       true,
			Mutation: &ProviderMutationFacts{InputEventsEmitted: true},
			Snapshot: &AppSnapshot{
				FrameID:             "undelivered-size-mismatch",
				ScreenshotPNGBase64: testPNGBase64(t, 1, 1),
				CoordinateSpace:     "screen_image_px",
				Width:               100,
				Height:              80,
			},
		},
	}
	for index, response := range cases {
		service := NewService()
		previous := &AppSnapshot{
			FrameID:             "delivered-frame",
			ScreenshotPNGBase64: "old",
			CoordinateSpace:     "screen_image_px",
			Width:               100,
			Height:              80,
		}
		service.guiSnapshot = previous
		result := service.guiResponse(response)
		if !result.IsError ||
			result.StructuredContent["failure_code"] != "effect_unknown" ||
			result.StructuredContent["failure_phase"] !=
				"post_action_observation" ||
			result.StructuredContent["observation_required"] != true {
			t.Fatalf("case %d lost GUI observation failure evidence: %#v",
				index, result)
		}
		actions, _ := result.StructuredContent["actions"].(map[string]any)
		if actions["mutation_unknown"] != true {
			t.Fatalf("case %d lost GUI mutation uncertainty: %#v",
				index, result.StructuredContent)
		}
		if service.guiSnapshot != previous {
			t.Fatalf("case %d cached an undeliverable GUI snapshot", index)
		}
	}
}

func TestSkippedObservationDoesNotInventMutationUnknown(t *testing.T) {
	result := ProviderAccessibilityResult{
		OK:                 true,
		ObservationSkipped: true,
		ActionEvidence: &ProviderActionEvidence{
			InputEmitted: true,
		},
	}
	result.PrepareForService()
	if !result.InputEventsEmitted {
		t.Fatal("delivery evidence was lost")
	}
	if result.MutationUnknown {
		t.Fatal("intentionally skipped observation was treated as failed observation")
	}
}
